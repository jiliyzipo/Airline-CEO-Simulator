using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 时刻二级市场（2026-11-02）：航司之间的时刻**转让**。
    ///
    /// 与协调池的分野（这是本模块存在的全部理由）：
    /// - 协调池 = WASG 协调人**分配**。不收费，受机场时刻池容量硬约束，排队按序。
    /// - 二级市场 = 航司之间**买卖**。收费，**不占协调池容量** —— 卖家的槽转到你名下，机场总量没变。
    ///   所以成交判据里**没有** `SlotPoolFull`：池满了照样能买，只是价格被稀缺度顶上去。
    ///   现实里 LHR 一对时刻能拍到 7500 万美元，正是这个道理（容量封死，价高者得）。
    ///
    /// 旧版的错误正是把两条渠道合并成一条：`AcquireSlotsAt(free:false)` 既扣钱又查池容量，
    /// 于是「花钱插队」在唯一真正需要它的场景（池满）里失效 —— 玩家付钱也买不到，
    /// 只能等协调池腾位置。本模块把付费渠道单独拎出来，与池容量解耦。
    ///
    /// 关于「超额持有」：买进来之后我司在该时段的持仓可能超过池容量。这是**允许**的
    /// （槽本来就是别人让出来的，机场没有多出一条），代价已经在价格里付过。
    /// `RoutePermitLogic.SlotsAvailable` 下限为 0，不会因此算出负数。
    /// </summary>
    public static class SlotMarketLogic
    {
        /// <summary>他航挂牌每季条数（下限/上限）。</summary>
        public const int ListingsMin = 5;
        public const int ListingsMax = 9;

        /// <summary>我司同时可挂牌的上限。</summary>
        public const int MaxOwnListings = 6;

        /// <summary>每季最多收到几份购买意向（防止收件箱被同一件事堆满）。</summary>
        public const int MaxOffersPerQuarter = 3;

        /// <summary>
        /// 运营部每季代挂几条闲置槽（2026-11-05）。
        /// 挂牌按钮随「挑机场×时刻不是 CEO 的事」一并删除后，「我司挂牌」若无来源，
        /// 整条卖出闭环（他航出价 → 收件箱批过户）与只读页右栏就全是死内容。
        /// 挂牌由运营部按需代劳，卖不卖仍由 CEO 在出价邮件里批。
        /// </summary>
        public const int AutoListPerQuarter = 1;

        /// <summary>我司挂牌的定价基准：市价上浮一成半（留出让价空间，也解释「为什么没人理」）。</summary>
        public const float AskPremium = 1.15f;

        /// <summary>挂牌在货架上最多挂几个季度，逾期自动下架（避免陈旧叫价长期占用名额）。</summary>
        public const int ListingShelfQuarters = 3;

        static readonly string[] SellerPool =
        {
            "中国国际航空", "中国东方航空", "中国南方航空", "汉莎航空", "法国航空",
            "荷兰皇家航空", "新加坡航空", "阿联酋航空", "卡塔尔航空", "达美航空",
            "美国联合航空", "全日空", "国泰航空", "土耳其航空"
        };

        /// <summary>
        /// 货架候选机场：本航网（基地 + 航线两端 + 经停）+ 一张常备大机场名单。
        ///
        /// 为什么要有常备名单：只列「我已在飞的机场」，玩家就永远买不到**想新开**的机场的时刻 ——
        /// 而二级市场最典型的用法恰恰是「我要开 LHR，池子排不上，去买一对」。
        /// </summary>
        static readonly string[] AlwaysListedAirports =
        {
            "PEK", "PVG", "CAN", "SZX", "CTU", "HKG", "LHR", "CDG", "FRA", "AMS",
            "JFK", "LAX", "ORD", "SIN", "NRT", "DXB", "IST", "SYD"
        };

        public static SlotMarketState Ensure(GameState gs, bool refresh = false)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) return null;
            var mkt = stg.slotMarket;
            if (mkt == null) { mkt = stg.slotMarket = new SlotMarketState(); }

            bool stale = !mkt.rolled
                         || mkt.year != gs.year
                         || mkt.quarter != gs.quarter;
            if (refresh || stale) Roll(gs);
            PruneExpired(gs);
            return stg.slotMarket;
        }

        /// <summary>本季货架换新（他航挂牌整批替换；我司挂牌与意向不受影响）。</summary>
        public static void Roll(GameState gs)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) return;
            var mkt = stg.slotMarket;
            if (mkt == null) { mkt = stg.slotMarket = new SlotMarketState(); }

            mkt.listings = new List<SlotMarketListing>();
            mkt.year = gs.year;
            mkt.quarter = gs.quarter;
            mkt.rolled = true;

            var airports = CandidateAirports(gs);
            if (airports.Count == 0) return;

            // 种子只由年月决定：同一季度反复打开页面看到的是同一张货架，
            // 否则每点一次刷新就换一批，玩家会当成老虎机而不是市场。
            var rng = new System.Random(gs.year * 17 + gs.quarter * 31 + 4649);
            int want = ListingsMin + rng.Next(0, ListingsMax - ListingsMin + 1);
            for (int i = 0; i < want; i++)
            {
                string apt = airports[rng.Next(airports.Count)];
                int band = rng.Next(0, ScheduleSlots.SlotBands.Length);
                var grid = ScheduleSlots.SlotGridLabels(band);
                if (grid == null || grid.Count == 0) continue;
                string clock = RoutePermitLogic.SlotRequestClock(grid[rng.Next(grid.Count)]);

                if (FindOnShelf(mkt, apt, clock) != null) continue;

                float fair = StrategyEconomics.SlotValueWan(apt, band);
                // 稀缺度：该机场该时段我司持仓越满，说明这一档越紧，叫价越高。
                float mult = ScarcityMultiplier(gs, apt, band);
                float wobble = 0.85f + (float)rng.NextDouble() * 0.75f;   // 0.85 ~ 1.60
                float ask = fair * mult * wobble;

                mkt.seq++;
                mkt.listings.Add(new SlotMarketListing
                {
                    id = $"MK-{gs.year}Q{gs.quarter}-{mkt.seq}",
                    airport = apt,
                    clock = clock,
                    seller = SellerPool[rng.Next(SellerPool.Length)],
                    askWan = RoundAsk(ask),
                    fairWan = RoundAsk(fair * mult),
                    mine = false,
                    listedYear = gs.year,
                    listedQuarter = gs.quarter
                });
            }
        }

        static List<string> CandidateAirports(GameState gs)
        {
            var list = new List<string>();
            var seen = new HashSet<string>();
            void TryAdd(string code)
            {
                if (string.IsNullOrEmpty(code)) return;
                string a = code.Trim().ToUpperInvariant();
                if (!seen.Add(a)) return;
                if (!RoutePermitLogic.NeedsDepartureSlot(a)) return;   // 非协调机场没有时刻可交易
                list.Add(a);
            }

            if (gs != null)
            {
                TryAdd(gs.hubIata);
                if (gs.activeRoutes != null)
                {
                    for (int i = 0; i < gs.activeRoutes.Count; i++)
                    {
                        var r = gs.activeRoutes[i];
                        if (r == null) continue;
                        TryAdd(r.origin);
                        TryAdd(r.destination);
                        TryAdd(RoutePermitLogic.StopoverOf(r));
                    }
                }
            }
            for (int i = 0; i < AlwaysListedAirports.Length; i++) TryAdd(AlwaysListedAirports[i]);
            return list;
        }

        /// <summary>稀缺乘数：我司在该段的持仓占池比例越高，二级市场叫价越高（池满 ≈ 市价的 2 倍以上）。</summary>
        public static float ScarcityMultiplier(GameState gs, string airport, int band)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            if (cap <= 0) return 2.2f;                    // 容量为 0 的极端情形按最紧处理
            int held = RoutePermitLogic.SlotsHeld(gs, airport, band);
            float fill = held / (float)cap;
            if (fill > 1f) fill = 1f;
            if (fill < 0f) fill = 0f;
            float mult = 1f + 1.3f * fill;                // 1.0 ~ 2.3
            if (RoutePermitLogic.SlotPoolFull(gs, airport, band)) mult += 0.25f;   // 池满再加一口价
            return mult;
        }

        public static float FairValueWan(string airport, string clock)
        {
            int m = ScheduleSlots.ParseClock(clock);
            int band = m < 0 ? 0 : ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(m));
            return StrategyEconomics.SlotValueWan(airport, band);
        }

        static float RoundAsk(float wan)
        {
            if (wan < 10f) wan = 10f;
            return (float)System.Math.Round(wan / 10f) * 10f;
        }

        public static SlotMarketListing Find(GameState gs, string id)
        {
            var mkt = StrategyManager.EnsureState(gs)?.slotMarket;
            if (mkt == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < mkt.listings.Count; i++)
                if (mkt.listings[i] != null && mkt.listings[i].id == id) return mkt.listings[i];
            for (int i = 0; i < mkt.mine.Count; i++)
                if (mkt.mine[i] != null && mkt.mine[i].id == id) return mkt.mine[i];
            return null;
        }

        /// <summary>
        /// 按「机场 + 时刻」在**他航待售**里找一条（命令台「购买 SIN 12:00 时刻槽」用）。
        /// 时刻按 <see cref="ScheduleSlots.SlotToleranceMinutes"/> 容差比对，与页面点选同一口径。
        /// </summary>
        public static bool TryFindOnShelf(GameState gs, string airport, string clock, out SlotMarketListing listing)
        {
            listing = FindOnShelf(StrategyManager.EnsureState(gs)?.slotMarket, airport, clock);
            return listing != null;
        }

        static SlotMarketListing FindOnShelf(SlotMarketState mkt, string airport, string clock)
        {
            if (mkt?.listings == null) return null;
            for (int i = 0; i < mkt.listings.Count; i++)
            {
                var l = mkt.listings[i];
                if (l == null) continue;
                if (!string.Equals(l.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(l.clock, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                return l;
            }
            return null;
        }

        /// <summary>
        /// 买入一条他航挂牌：**当场成交**（不走进收件箱、不等下季交割）。
        ///
        /// 为什么当场成交而不是照抄协调池的「批准→下季交割」：二级市场是**现券交易**，
        /// 卖家此刻就持有那条槽，签字即过户；再压一个季度只是把「池满买不到」换成「下季又满了」。
        ///
        /// ★成交判据里**没有池容量检查** —— 见类注释。
        /// </summary>
        public static bool Buy(GameState gs, string listingId, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            var mkt = Ensure(gs);
            SlotMarketListing hit = null;
            for (int i = 0; i < mkt.listings.Count; i++)
            {
                if (mkt.listings[i] != null && mkt.listings[i].id == listingId) { hit = mkt.listings[i]; break; }
            }
            if (hit == null) { message = "该挂牌已下架（货架每季换新）。"; return false; }

            if (gs.cash < hit.askWan)
            {
                message = $"现金不足：{hit.airport} {hit.clock} 叫价 {hit.askWan:F0} 万，"
                          + $"现有 {gs.cash:F0} 万（差 {hit.askWan - gs.cash:F0} 万）。";
                return false;
            }

            var stg = StrategyManager.EnsureState(gs);
            float cashBefore = gs.cash;
            gs.cash -= hit.askWan;
            int turn = RoutePermitLogic.Turn(gs.year, gs.quarter);
            stg.slots.Add(new SlotHolding
            {
                airport = hit.airport,
                slotTime = hit.clock,
                inUse = false,
                acquisitionCostWan = hit.askWan,
                expiresQuarter = gs.quarter + 2,
                expiresTurn = turn + 2
            });
            mkt.listings.Remove(hit);

            // 提示里直接给出「现金 A → B」：玩家不必自己拿旧余额去减，
            // 也不会再去底栏找现金（那里是季度运营口径，本来就不随买卖变动）。
            message = $"已成交：{hit.airport} {hit.clock} 时刻槽（{hit.seller} 出让），"
                      + $"付 {hit.askWan:F0} 万（当场入账）· 现金 {cashBefore:F0} → {gs.cash:F0} 万";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>建议挂牌价：市价 × 上浮（<see cref="AskPremium"/>），取整到十万。</summary>
        public static float SuggestedAskWan(string airport, string clock)
        {
            float fair = FairValueWan(airport, clock);
            return RoundAsk(fair * AskPremium);
        }

        /// <summary>
        /// 把一条**闲置**持仓挂牌。使用中的槽不能卖 —— 那是正在飞的班次占着的实体，
        /// 卖出去等于当天就把航班取消，游戏里没有「先卖后停」这种操作。
        /// </summary>
        public static bool ListOwn(GameState gs, int holdingIndex, out string message)
        {
            message = "";
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null || stg.slots == null) { message = "游戏未就绪"; return false; }
            if (holdingIndex < 0 || holdingIndex >= stg.slots.Count) { message = "该时刻槽不存在。"; return false; }

            var mkt = Ensure(gs);
            var h = stg.slots[holdingIndex];
            if (h == null) { message = "该时刻槽不存在。"; return false; }
            if (h.inUse)
            {
                message = $"{h.airport} {ScheduleSlots.BandNameToClock(h.slotTime)} 正在被班次占用，不能挂牌；"
                          + "请先在「周计划及航班环」把该班次调开。";
                return false;
            }
            if (IsListed(mkt, h.airport, h.slotTime))
            {
                message = "这条槽已经在挂牌中。";
                return false;
            }
            if (mkt.mine.Count >= MaxOwnListings)
            {
                message = $"同时最多挂牌 {MaxOwnListings} 条，请先撤下一条。";
                return false;
            }

            mkt.seq++;
            float ask = SuggestedAskWan(h.airport, h.slotTime);
            mkt.mine.Add(new SlotMarketListing
            {
                id = $"MY-{gs.year}Q{gs.quarter}-{mkt.seq}",
                airport = h.airport,
                clock = RoutePermitLogic.SlotRequestClock(h.slotTime),
                seller = "我司",
                askWan = ask,
                fairWan = RoundAsk(FairValueWan(h.airport, h.slotTime)),
                mine = true,
                listedYear = gs.year,
                listedQuarter = gs.quarter
            });
            message = $"已挂牌：{h.airport} {ScheduleSlots.BandNameToClock(h.slotTime)}，叫价 {ask:F0} 万。"
                      + "有航司提意向会送收件箱待批。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static bool Withdraw(GameState gs, string listingId, out string message)
        {
            message = "";
            var mkt = Ensure(gs);
            for (int i = 0; i < mkt.mine.Count; i++)
            {
                if (mkt.mine[i] == null || mkt.mine[i].id != listingId) continue;
                var l = mkt.mine[i];
                mkt.mine.RemoveAt(i);
                // 撤回即作废未处理意向：挂牌都没了，再留意向就是给 CEO 一封点不出结果的邮件。
                for (int k = 0; k < mkt.offers.Count; k++)
                {
                    var o = mkt.offers[k];
                    if (o != null && o.open && o.listingId == listingId) o.open = false;
                }
                message = $"已撤回 {l.airport} {l.clock} 的挂牌。";
                gs.AddDecisionLog("战略：" + message);
                gs.NotifyChanged();
                return true;
            }
            message = "该挂牌已不在架上。";
            return false;
        }

        static bool IsListed(SlotMarketState mkt, string airport, string clock)
        {
            if (mkt?.mine == null) return false;
            for (int i = 0; i < mkt.mine.Count; i++)
            {
                var l = mkt.mine[i];
                if (l == null) continue;
                if (!string.Equals(l.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(l.clock, clock) <= ScheduleSlots.SlotToleranceMinutes) return true;
            }
            return false;
        }

        /// <summary>挂牌对应的持仓下标（−1 = 找不到，说明槽已被考核收回或卖了）。</summary>
        public static int HoldingIndexOf(GameState gs, SlotMarketListing listing)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null || listing == null) return -1;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var h = stg.slots[i];
                if (h == null) continue;
                if (!string.Equals(h.airport, listing.airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(h.slotTime, listing.clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                return i;
            }
            return -1;
        }

        static void PruneExpired(GameState gs)
        {
            var mkt = StrategyManager.EnsureState(gs)?.slotMarket;
            if (mkt == null) return;
            for (int i = mkt.mine.Count - 1; i >= 0; i--)
            {
                var l = mkt.mine[i];
                if (l == null) { mkt.mine.RemoveAt(i); continue; }
                bool alive = HoldingIndexOf(gs, l) >= 0;
                int age = (gs.year - l.listedYear) * 4 + (gs.quarter - l.listedQuarter);
                if (!alive || age >= ListingShelfQuarters)
                {
                    for (int k = 0; k < mkt.offers.Count; k++)
                    {
                        var o = mkt.offers[k];
                        if (o != null && o.open && o.listingId == l.id) o.open = false;
                    }
                    mkt.mine.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 季度推进：换货架 + 为他航挂牌摇出购买意向。
        ///
        /// 出价概率只由「叫价 / 市价」决定 —— 挂得越贵越没人问，这条关系必须让玩家**学会**，
        /// 所以文案里直接把倍数写出来（见收件箱邮件）。
        /// </summary>
        public static void TickQuarter(GameState gs)
        {
            if (gs == null) return;
            var mkt = Ensure(gs, refresh: true);
            if (mkt == null) return;

            // ★2026-11-05：挂牌按钮随「挑机场×时刻不是 CEO 的事」删除后，「我司挂牌」
            //   若无来源，卖出闭环（他航出价 → 收件箱批过户）与只读页右栏全是死内容。
            //   改由运营部每季代挂闲置槽；卖不卖仍由 CEO 在出价邮件里批。
            AutoListIdleSlots(gs, mkt);

            var rng = new System.Random(gs.year * 29 + gs.quarter * 41 + 7717);
            int made = 0;
            for (int i = 0; i < mkt.mine.Count && made < MaxOffersPerQuarter; i++)
            {
                var l = mkt.mine[i];
                if (l == null) continue;
                if (HasOpenOffer(mkt, l.id)) continue;

                float fair = FairValueWan(l.airport, l.clock);
                if (fair <= 0f) fair = 1f;
                float ratio = l.askWan / fair;
                float p = ratio <= 1.0f ? 0.80f
                        : ratio <= 1.2f ? 0.60f
                        : ratio <= 1.5f ? 0.35f
                        : ratio <= 2.0f ? 0.15f
                        : 0.05f;
                if ((float)rng.NextDouble() > p) continue;

                string buyer = SellerPool[rng.Next(SellerPool.Length)];
                float price = RoundAsk(l.askWan * (0.85f + (float)rng.NextDouble() * 0.15f));
                mkt.seq++;
                var offer = new SlotBuyerOffer
                {
                    id = $"OFFER-{gs.year}Q{gs.quarter}-{mkt.seq}",
                    listingId = l.id,
                    airport = l.airport,
                    clock = l.clock,
                    buyer = buyer,
                    priceWan = price,
                    year = gs.year,
                    quarter = gs.quarter,
                    open = true
                };
                mkt.offers.Add(offer);
                InboxManager.QueueSlotSaleOffer(gs, offer);
                made++;
            }
        }

        static bool HasOpenOffer(SlotMarketState mkt, string listingId)
        {
            if (mkt?.offers == null) return false;
            for (int i = 0; i < mkt.offers.Count; i++)
            {
                var o = mkt.offers[i];
                if (o != null && o.open && o.listingId == listingId) return true;
            }
            return false;
        }

        /// <summary>
        /// 运营部每季代挂闲置槽：按持仓顺序挑**闲置且未挂牌**的，按建议价挂出，
        /// 每季至多 <see cref="AutoListPerQuarter"/> 条。只挂不卖 ——
        /// 成交仍走 <see cref="AcceptOffer"/>（CEO 在出价邮件里批）。
        /// </summary>
        static void AutoListIdleSlots(GameState gs, SlotMarketState mkt)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null || mkt == null) return;
            int made = 0;
            for (int i = 0; i < stg.slots.Count && made < AutoListPerQuarter; i++)
            {
                var h = stg.slots[i];
                if (h == null || h.inUse) continue;
                if (IsListed(mkt, h.airport, h.slotTime)) continue;
                if (ListOwn(gs, i, out _)) made++;
            }
        }

        public static SlotBuyerOffer FindOffer(GameState gs, string offerId)
        {
            var mkt = StrategyManager.EnsureState(gs)?.slotMarket;
            if (mkt?.offers == null || string.IsNullOrEmpty(offerId)) return null;
            for (int i = 0; i < mkt.offers.Count; i++)
                if (mkt.offers[i] != null && mkt.offers[i].id == offerId) return mkt.offers[i];
            return null;
        }

        /// <summary>
        /// 接受出价：槽过户给出价方、现金入账、挂牌下架。
        ///
        /// 与「买入」同口径地绕开池容量判据：卖出是**把槽还回市场**，机场容量一分没多，
        /// 不需要也不可能去校验池子。
        /// </summary>
        public static bool AcceptOffer(GameState gs, string offerId, out string message)
        {
            message = "";
            var mkt = Ensure(gs);
            var offer = FindOffer(gs, offerId);
            if (offer == null || !offer.open) { message = "该购买意向已失效。"; return false; }

            SlotMarketListing listing = null;
            for (int i = 0; i < mkt.mine.Count; i++)
                if (mkt.mine[i] != null && mkt.mine[i].id == offer.listingId) { listing = mkt.mine[i]; break; }
            if (listing == null) { message = "对应挂牌已下架，无法成交。"; offer.open = false; return false; }

            int idx = HoldingIndexOf(gs, listing);
            if (idx < 0) { message = "对应时刻槽已不在持仓里，无法成交。"; offer.open = false; return false; }

            var stg = StrategyManager.EnsureState(gs);
            var h = stg.slots[idx];
            if (h.inUse)
            {
                message = $"{offer.airport} {offer.clock} 的槽正在被班次占用，无法过户；请先调开该班次。";
                return false;
            }

            stg.slots.RemoveAt(idx);
            float cashBeforeSale = gs.cash;
            gs.cash += offer.priceWan;
            mkt.mine.Remove(listing);
            offer.open = false;

            message = $"已出售：{offer.airport} {offer.clock} 时刻槽给 {offer.buyer}，"
                      + $"入账 {offer.priceWan:F0} 万（原挂牌 {listing.askWan:F0} 万）"
                      + $" · 现金 {cashBeforeSale:F0} → {gs.cash:F0} 万";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static bool DeclineOffer(GameState gs, string offerId, out string message)
        {
            message = "";
            var offer = FindOffer(gs, offerId);
            if (offer == null || !offer.open) { message = "该购买意向已失效。"; return false; }
            offer.open = false;
            message = $"已婉拒 {offer.buyer} 对 {offer.airport} {offer.clock} 的出价（{offer.priceWan:F0} 万），"
                      + "挂牌继续有效。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }
    }
}
