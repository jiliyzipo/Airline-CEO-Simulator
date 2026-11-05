#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// R47 时刻二级市场（2026-11-02）。
    ///
    /// 钉的是两件事，错一件这个市场就没有存在意义：
    /// <item><b>池满仍可成交</b>：二级市场是航司间**转让**，不占协调池容量。
    ///   旧版把付费渠道塞进 `AcquireSlotsAt(free:false)`，成交判据里带着 `SlotPoolFull` ——
    ///   池一满，付钱也买不到，而池满恰恰是玩家唯一想花钱的时候。这里的反例是真实路径：
    ///   先把池填满，再买，必须成交。</item>
    /// <item><b>卖出闭环</b>：挂牌 → 他航出价（进收件箱）→ 批准过户并收款；
    ///   拒绝只作废本次出价，挂牌继续有效；撤回则把未处理意向一并作废
    ///   （否则 CEO 会收到一封点不出结果的邮件）。</item>
    ///
    /// 另钉：使用中的槽不能挂牌/过户（卖出去等于当天取消航班）、现金不足拒绝且不扣款、
    /// 池越满叫价越高、买来的槽同样受 use-it-or-lose-it（两个季度不投运退回）。
    /// </summary>
    public static class SlotMarketRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__slot_market__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 9000f;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                StrategyManager.EnsureState(gs);

                if (!FindTarget(gs, out string apt, out string clock))
                {
                    Debug.LogError("R47 夹具失败：找不到有余量的协调机场时刻（先确认机场目录 / 池容量没变，"
                                   + "别把夹具失效当成通过）。");
                    return false;
                }
                int band = ScheduleSlots.BandOfClock(clock);
                float fair = StrategyEconomics.SlotValueWan(apt, band);

                // ———— ① 货架：每季换新，条数在区间内，条目可读 ————
                var mkt = SlotMarketLogic.Ensure(gs, refresh: true);
                bool rollOk = mkt != null
                              && mkt.listings.Count >= SlotMarketLogic.ListingsMin
                              && mkt.listings.Count <= SlotMarketLogic.ListingsMax;
                if (!rollOk)
                    Debug.LogError($"R47 ① 货架条数异常：{mkt?.listings.Count} 条"
                                   + $"（期望 {SlotMarketLogic.ListingsMin}–{SlotMarketLogic.ListingsMax}）。");
                bool listingShapeOk = true;
                for (int i = 0; i < mkt.listings.Count; i++)
                {
                    var l = mkt.listings[i];
                    if (l == null || string.IsNullOrEmpty(l.airport)
                        || ScheduleSlots.ParseClock(l.clock) < 0 || l.askWan < 10f)
                    { listingShapeOk = false; break; }
                }
                if (!listingShapeOk) Debug.LogError("R47 ① 货架里有条目不完整（机场/时刻/叫价）。");

                // ———— ② 池满仍可成交（本回归的核心） ————
                // 先把该机场该时段的池买到满 —— 用免费渠道（不推进季度，直接 Acquire）。
                int room = RoutePermitLogic.SlotsAvailable(gs, apt, band);
                if (room > 0) StrategyManager.AcquireSlotsAt(gs, apt, clock, room, true, out _);
                bool poolFull = RoutePermitLogic.SlotPoolFull(gs, apt, band);

                float ask = 800f;
                mkt.listings.Add(new SlotMarketListing
                {
                    id = "MK-TEST-FULL",
                    airport = apt,
                    clock = clock,
                    seller = "汉莎航空",
                    askWan = ask,
                    fairWan = fair,
                    mine = false,
                    listedYear = gs.year,
                    listedQuarter = gs.quarter
                });
                float cashBefore = gs.cash;
                int heldBefore = gs.stg.slots.Count;
                bool bought = SlotMarketLogic.Buy(gs, "MK-TEST-FULL", out string buyMsg);
                bool cashCharged = Mathf.Abs(gs.cash - (cashBefore - ask)) < 0.01f;
                bool heldAdded = gs.stg.slots.Count == heldBefore + 1;
                int availAfter = RoutePermitLogic.SlotsAvailable(gs, apt, band);
                if (!poolFull)
                    Debug.LogError("R47 ② 夹具失败：池没被填满，池满场景没测到。");
                if (!bought)
                    Debug.LogError($"R47 ② 池满时买不到（这就是 305 号缺陷复现）：{buyMsg}");
                if (!cashCharged) Debug.LogError($"R47 ② 成交后现金不对：{cashBefore:F0} → {gs.cash:F0}（应 −{ask:F0}）。");
                if (!heldAdded) Debug.LogError("R47 ② 成交后持仓没增加。");
                if (availAfter < 0) Debug.LogError($"R47 ② 池余量被算成负数（{availAfter}），超额持有应有下限 0。");
                var boughtHolding = gs.stg.slots[gs.stg.slots.Count - 1];
                bool expiresOk = boughtHolding != null && boughtHolding.expiresTurn > 0;
                if (!expiresOk) Debug.LogError("R47 ② 买来的槽没有 use-it-or-lose-it 时点（闲置两季应收回归池）。");

                // ———— ③ 现金不足：拒绝且一分不动 ————
                mkt.listings.Add(new SlotMarketListing
                {
                    id = "MK-TEST-BROKE", airport = apt, clock = clock,
                    seller = "法国航空", askWan = gs.cash + 500f, fairWan = fair,
                    listedYear = gs.year, listedQuarter = gs.quarter
                });
                float cashBeforeBroke = gs.cash;
                int heldBeforeBroke = gs.stg.slots.Count;
                bool brokeBought = SlotMarketLogic.Buy(gs, "MK-TEST-BROKE", out string brokeMsg);
                bool brokeMessaged = (brokeMsg ?? "").IndexOf("现金不足", System.StringComparison.Ordinal) >= 0;
                bool brokeIntact = Mathf.Abs(gs.cash - cashBeforeBroke) < 0.01f
                                   && gs.stg.slots.Count == heldBeforeBroke;
                if (brokeBought || !brokeMessaged || !brokeIntact)
                    Debug.LogError($"R47 ③ 现金不足处置不对（成交={brokeBought}，有提示={brokeMessaged}，"
                                   + $"账未动={brokeIntact}）：{brokeMsg}");

                // ———— ④ 挂牌：闲置可挂、重复被挡、使用中被挡 ————
                var idleIdx = FindHolding(gs, inUse: false);
                if (idleIdx < 0) { Debug.LogError("R47 ④ 夹具失败：找不到闲置持仓。"); return false; }
                var idle = gs.stg.slots[idleIdx];
                float suggest = SlotMarketLogic.SuggestedAskWan(idle.airport, idle.slotTime);
                bool listed = SlotMarketLogic.ListOwn(gs, idleIdx, out string listMsg);
                bool askMatches = false;
                if (mkt.mine.Count > 0)
                    askMatches = Mathf.Abs(mkt.mine[mkt.mine.Count - 1].askWan - suggest) < 0.01f;
                bool listedOk = listed && mkt.mine.Count > 0 && askMatches;
                if (!listedOk)
                    Debug.LogError($"R47 ④ 闲置槽没能按建议价挂牌（{listMsg}；建议 {suggest:F0} 万）。");

                bool dupBlocked = !SlotMarketLogic.ListOwn(gs, idleIdx, out _);
                if (!dupBlocked) Debug.LogError("R47 ④ 同一条槽被挂了两次（会导致一次成交、一次挂空）。");

                gs.stg.slots[idleIdx].inUse = true;
                gs.stg.slots[idleIdx].usedByFlightNo = "CA9801";
                bool inUseBlocked = !SlotMarketLogic.ListOwn(gs, FindHolding(gs, inUse: true), out _);
                gs.stg.slots[idleIdx].inUse = false;
                gs.stg.slots[idleIdx].usedByFlightNo = "";
                if (!inUseBlocked)
                    Debug.LogError("R47 ④ 使用中的槽也能挂牌 —— 卖出去等于当天取消航班。");

                // ———— ⑤ 季度推进：出价进收件箱；拒价只作废本次意向 ————
                mkt.mine.Clear();
                mkt.offers.Clear();
                gs.year = 2;
                gs.quarter = 1;
                var listing = new SlotMarketListing
                {
                    id = "MY-TEST-1",
                    airport = idle.airport,
                    clock = RoutePermitLogic.SlotRequestClock(idle.slotTime),
                    seller = "我司",
                    askWan = SlotMarketLogic.FairValueWan(idle.airport, idle.slotTime),   // 平价挂牌 → 有人问价
                    fairWan = SlotMarketLogic.FairValueWan(idle.airport, idle.slotTime),
                    mine = true,
                    listedYear = gs.year,
                    listedQuarter = gs.quarter
                };
                mkt.mine.Add(listing);

                bool anyOffer = false;
                for (int q = 0; q < 8 && !anyOffer; q++)
                {
                    // 挂牌跨季保留：每季把「上架季」刷新一次，免得被 3 季的陈旧下架规则提前清掉
                    // （测试关心的是出价模型，不是货架保鲜期）。
                    listing.listedYear = gs.year;
                    listing.listedQuarter = gs.quarter;
                    gs.quarter = q + 1;
                    SlotMarketLogic.TickQuarter(gs);
                    for (int i = 0; i < mkt.offers.Count; i++)
                        if (mkt.offers[i] != null && mkt.offers[i].open) anyOffer = true;
                }
                int mailCount = 0;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.SellSlotAction) mailCount++;
                if (!anyOffer)
                    Debug.LogError("R47 ⑤ 平价挂牌连续 8 个季度无人问价（出价概率模型失效）。");
                if (mailCount == 0)
                    Debug.LogError("R47 ⑤ 出了意向却没进收件箱（CEO 无从批复）。");

                SlotBuyerOffer offer = null;
                for (int i = 0; i < mkt.offers.Count; i++)
                    if (mkt.offers[i] != null && mkt.offers[i].open) { offer = mkt.offers[i]; break; }

                bool declined = offer != null && SlotMarketLogic.DeclineOffer(gs, offer.id, out _);
                bool stillListed = offer != null && SlotMarketLogic.Find(gs, offer.listingId) != null;
                if (!declined || !stillListed)
                    Debug.LogError("R47 ⑤ 婉拒出价后挂牌应继续有效，实际被一并撤了。");

                // ———— ⑥ 接受出价：过户 + 收款 + 下架；挂牌槽不在持仓时拒绝 ————
                float cashBeforeSale = gs.cash;
                int heldBeforeSale = gs.stg.slots.Count;
                var offer2 = new SlotBuyerOffer
                {
                    id = "OFFER-TEST-2",
                    listingId = listing.id,
                    airport = listing.airport,
                    clock = listing.clock,
                    buyer = "新加坡航空",
                    priceWan = listing.askWan,
                    year = gs.year,
                    quarter = gs.quarter,
                    open = true
                };
                mkt.offers.Add(offer2);
                bool sold = SlotMarketLogic.AcceptOffer(gs, offer2.id, out string sellMsg);
                bool paidIn = Mathf.Abs(gs.cash - (cashBeforeSale + listing.askWan)) < 0.01f;
                bool holdingGone = gs.stg.slots.Count == heldBeforeSale - 1;
                bool delisted = SlotMarketLogic.Find(gs, listing.id) == null;
                if (!sold || !paidIn || !holdingGone || !delisted)
                    Debug.LogError($"R47 ⑥ 成交不完整（成交={sold} 收款={paidIn} 持仓减少={holdingGone} "
                                   + $"下架={delisted}）：{sellMsg}");

                var ghost = new SlotBuyerOffer
                {
                    id = "OFFER-TEST-GHOST", listingId = "MY-GONE", airport = apt, clock = clock,
                    buyer = "阿联酋航空", priceWan = 100f, year = gs.year, quarter = gs.quarter, open = true
                };
                mkt.offers.Add(ghost);
                bool ghostBlocked = !SlotMarketLogic.AcceptOffer(gs, ghost.id, out _);
                if (!ghostBlocked) Debug.LogError("R47 ⑥ 挂牌已消失仍能成交（会凭空多出一笔钱）。");

                // ———— ⑦ 撤回：未处理意向一并作废 ————
                var list2 = new SlotMarketListing
                {
                    id = "MY-TEST-2", airport = idle.airport,
                    clock = RoutePermitLogic.SlotRequestClock(idle.slotTime),
                    seller = "我司", askWan = 500f, fairWan = fair, mine = true,
                    listedYear = gs.year, listedQuarter = gs.quarter
                };
                mkt.mine.Add(list2);
                var offer3 = new SlotBuyerOffer
                {
                    id = "OFFER-TEST-3", listingId = list2.id, airport = list2.airport,
                    clock = list2.clock, buyer = "达美航空", priceWan = 480f,
                    year = gs.year, quarter = gs.quarter, open = true
                };
                mkt.offers.Add(offer3);
                bool withdrawn = SlotMarketLogic.Withdraw(gs, list2.id, out _);
                bool offerClosed = !offer3.open;
                if (!withdrawn || !offerClosed)
                    Debug.LogError("R47 ⑦ 撤回挂牌后仍有未处理意向（CEO 会点出一封无结果的邮件）。");

                // ———— ⑦b 运营部代挂牌（2026-11-05）：挂牌按钮删除后「我司挂牌」的唯一来源 ————
                // 挂牌按钮随「挑机场×时刻不是 CEO 的事」一并删除；若没人挂牌，
                // 卖出闭环（他航出价 → 收件箱批过户）与只读页右栏全是死内容。
                // 钉三条：闲置槽按季被代挂（至多 AutoListPerQuarter 条、按建议价）、
                // 使用中的槽绝不代挂、已在架上的不重复挂。
                mkt.mine.Clear();
                mkt.offers.Clear();
                int idleIdx2 = FindHolding(gs, inUse: false);
                int usedIdx2 = FindHolding(gs, inUse: true);
                if (idleIdx2 < 0)
                {
                    // 夹具兜底：把一条持仓置闲，保证有可挂对象
                    idleIdx2 = 0;
                    gs.stg.slots[0].inUse = false;
                    gs.stg.slots[0].usedByFlightNo = "";
                }
                if (usedIdx2 >= 0 && usedIdx2 == idleIdx2) usedIdx2 = -1;
                string usedApt = usedIdx2 >= 0 ? gs.stg.slots[usedIdx2].airport : null;
                string usedClk = usedIdx2 >= 0
                    ? RoutePermitLogic.SlotRequestClock(gs.stg.slots[usedIdx2].slotTime) : null;

                SlotMarketLogic.TickQuarter(gs);

                bool autoListed = mkt.mine.Count > 0 && mkt.mine.Count <= SlotMarketLogic.AutoListPerQuarter;
                bool askRight = true;
                bool usedNeverListed = true;
                for (int i = 0; i < mkt.mine.Count; i++)
                {
                    var al = mkt.mine[i];
                    if (al == null) continue;
                    if (Mathf.Abs(al.askWan - SlotMarketLogic.SuggestedAskWan(al.airport, al.clock)) > 0.01f)
                        askRight = false;
                    if (usedApt != null && string.Equals(al.airport, usedApt, System.StringComparison.OrdinalIgnoreCase)
                        && string.Equals(al.clock, usedClk, System.StringComparison.Ordinal))
                        usedNeverListed = false;
                }
                if (!autoListed || !askRight || !usedNeverListed)
                    Debug.LogError($"R47 ⑦b 运营部代挂牌不对：挂出 {mkt.mine.Count} 条（应 1..{SlotMarketLogic.AutoListPerQuarter}）"
                                   + $" 建议价={askRight} 使用中未挂={usedNeverListed}"
                                   + " —— 挂牌按钮删除后这是「我司挂牌」的唯一来源，断了卖出闭环就死。");

                // 重复挂挡：再跑一季，已在架上的闲置槽不得二次挂出
                int mineBefore = mkt.mine.Count;
                SlotMarketLogic.TickQuarter(gs);
                bool noDup = mkt.mine.Count <= mineBefore + SlotMarketLogic.AutoListPerQuarter;
                for (int i = 0; i < mkt.mine.Count; i++)
                    for (int k = i + 1; k < mkt.mine.Count; k++)
                        if (mkt.mine[i] != null && mkt.mine[k] != null
                            && string.Equals(mkt.mine[i].airport, mkt.mine[k].airport, System.StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mkt.mine[i].clock, mkt.mine[k].clock, System.StringComparison.Ordinal))
                            noDup = false;
                if (!noDup)
                    Debug.LogError("R47 ⑦b 同一条闲置槽被代挂两次（会一次成交、一次挂空）。");

                // ———— ⑧ 稀缺度：池越满叫价越高 ————
                float multFull = SlotMarketLogic.ScarcityMultiplier(gs, apt, band);
                var gs2 = new GameObject("__slot_market_empty__").AddComponent<GameState>();
                try
                {
                    gs2.year = 1; gs2.quarter = 1; gs2.cash = 9000f;
                    gs2.inbox = new List<InboxMail>();
                    gs2.pendingInbox = new List<PendingInboxMail>();
                    gs2.activeRoutes = new List<RouteData>();
                    StrategyManager.EnsureState(gs2);
                    float multEmpty = SlotMarketLogic.ScarcityMultiplier(gs2, apt, band);
                    if (multFull <= multEmpty)
                        Debug.LogError($"R47 ⑧ 池满没有抬价（满 {multFull:F2} ≤ 空 {multEmpty:F2}）——"
                                       + "「池满也能买」就变成了隐藏折扣，稀缺性没有代价。");
                }
                finally
                {
                    Object.DestroyImmediate(gs2.gameObject);
                }

                // ———— ⑨ 本季货架买空后不得自动补货 ————
                // 旧判据「listings.Count == 0 就重摇」会让玩家在同一季度无限进货：
                // 买空 → 页面刷新 → 又整批补上。这里买空后显式再 Ensure 一次，必须仍为空。
                gs.cash = 99999f;
                while (mkt.listings.Count > 0)
                {
                    var first = mkt.listings[0];
                    if (!SlotMarketLogic.Buy(gs, first.id, out string restockMsg))
                    {
                        Debug.LogError($"R47 ⑨ 清空货架时买入失败：{restockMsg}");
                        break;
                    }
                }
                SlotMarketLogic.Ensure(gs);
                bool noRefill = mkt.listings.Count == 0;
                if (!noRefill)
                    Debug.LogError($"R47 ⑨ 同一季度买空后又补了 {mkt.listings.Count} 条 —— 货架被当成无限货源。");

                bool all = rollOk && listingShapeOk && poolFull && bought && cashCharged && heldAdded
                           && availAfter >= 0 && expiresOk
                           && !brokeBought && brokeMessaged && brokeIntact
                           && listedOk && dupBlocked && inUseBlocked
                           && anyOffer && mailCount > 0 && declined && stillListed
                           && sold && paidIn && holdingGone && delisted && ghostBlocked
                           && withdrawn && offerClosed && noRefill;

                Debug.Log($"R47 时刻二级市场（{apt} {clock}·市价 {fair:F0} 万）→ "
                          + $"货架 {mkt.listings.Count} 条 roll={rollOk}"
                          + $"\n　核心：池满={poolFull} 仍成交={bought} 扣款={cashCharged} 入账={heldAdded}"
                          + $" 余量不为负={availAfter >= 0} 收回时点={expiresOk}"
                          + $"\n　反例：现金不足拒绝={!brokeBought} 提示={brokeMessaged} 账未动={brokeIntact}"
                          + $"\n　挂牌：按建议价={listedOk} 重复被挡={dupBlocked} 使用中被挡={inUseBlocked}"
                          + $"\n　卖侧：有人问价={anyOffer} 进收件箱={mailCount > 0} 婉拒保留={declined && stillListed}"
                          + $" 成交完整={sold && paidIn && holdingGone && delisted} 幽灵拒={ghostBlocked}"
                          + $" 撤回作废={withdrawn && offerClosed} 货架不补货={noRefill}"
                          + $"\n　{all.ToString().Replace("True", "✓").Replace("False", "✗")}");
                return all;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static int FindHolding(GameState gs, bool inUse)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return -1;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var h = stg.slots[i];
                if (h == null) continue;
                if (h.inUse == inUse) return i;
            }
            return -1;
        }

        /// <summary>找一个池内还有余量的协调机场时刻。</summary>
        static bool FindTarget(GameState gs, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            string[] candidates = { "PEK", "PVG", "CAN", "SZX", "CDG", "FRA", "LHR", "SIN", "JFK", "ORD", "CGK", "SYD" };
            for (int c = 0; c < candidates.Length; c++)
            {
                string a = candidates[c];
                if (!RoutePermitLogic.NeedsDepartureSlot(a)) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, a, b) < 2) continue;
                    var grid = ScheduleSlots.SlotGridLabels(b);
                    if (grid == null || grid.Count == 0) continue;
                    airport = a;
                    clock = grid[0];
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
