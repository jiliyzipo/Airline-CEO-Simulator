#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 「一槽一对：离港 + 到港」的隔离回归（2026-11-06）。
    ///
    /// 用户要求（原话）：
    /// <c>机场要有到港时刻，一旦运营部AI申请了机场离港时刻，也要同时申请到达机场的到港时刻，
    /// 也可以单独申请或购买。</c>
    ///
    /// 这件事在旧模型里**根本无法表达**：`SlotHolding` 只存 `slotTime`（离港），
    /// 到港时刻由「离港 + 轮挡」每次现算，既不落盘也不校验 ——
    /// 于是「本场这条槽对应几点落地」在数据里不存在，界面也无从显示。
    ///
    /// 本回归钉住四条不变量：
    /// <list type="number">
    /// <item><b>成对入账</b>：带 <c>arrClock</c> 取得离港槽，到港时刻必须写进持仓
    ///   （而不是留在调用栈里丢掉）。</item>
    /// <item><b>只到港可单独取得</b>：`kind=ARR` 的持仓 <c>arrTime</c> 为空、`slotTime` 表示到港，
    ///   且**不能**被当成离港槽用来排班 —— 否则「只有落地权」会变成「白送一条起飞权」。</item>
    /// <item><b>池子不翻倍</b>：一对到港+离港只占池里一条。这是拆端之后最容易犯的错：
    ///   容量按类型各算一份，机场容量凭空变两倍，稀缺性立刻消失。</item>
    /// <item><b>绑班补记</b>：老档持仓的 `arrTime` 是空的，绑上班次后必须补记成
    ///   「这一对的落地时刻」，界面才读得到（见 <c>RoutePermitLogic.MyArrivalAt</c>）。</item>
    /// </list>
    /// </summary>
    public static class ArrivalPairRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__arrival_pair__");
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
                var stg = StrategyManager.EnsureState(gs);
                bool ok = true;

                if (!FindTarget(gs, out string apt, out string depClock, out string arrClock))
                {
                    Debug.LogError("R46 夹具失败：找不到池内还有余量的协调机场时刻。"
                                   + "（先确认 SlotPoolCapacity / 机场目录没变，别把夹具失效当成通过。）"
                                   + "\n" + FindTargetDiag(gs));
                    return false;
                }

                // ———— ① 成对入账：离港 + 到港一起落到持仓上 ————
                string msg;
                var assigned = new List<string>();
                int before = RoutePermitLogic.SlotsAvailable(gs, apt, ScheduleSlots.BandOfClock(depClock));
                bool got = StrategyManager.AcquireSlotsAt(gs, apt, depClock, 1, false,
                    out msg, out assigned, SlotHolding.SlotKindDeparture, arrClock);
                SlotHolding dep = got ? FindHolding(gs, apt, assigned.Count > 0 ? assigned[0] : depClock,
                    SlotHolding.SlotKindDeparture) : null;
                bool pairOk = got && dep != null
                              && dep.arrTime == arrClock
                              && !dep.IsArrivalOnly
                              && RoutePermitLogic.MyArrivalAt(gs, apt, dep.slotTime) == arrClock;
                if (!pairOk)
                {
                    ok = false;
                    Debug.LogError($"R46 ① 成对入账失败：{msg}"
                                   + $"（持仓={(dep == null ? "无" : $"{dep.airport} {dep.slotTime} arr={dep.arrTime} kind={dep.kind}")}，"
                                   + $"期望到港 {arrClock}）");
                }

                // ———— ② 池子不翻倍：一条配对只占一条容量 ————
                int after = RoutePermitLogic.SlotsAvailable(gs, apt, ScheduleSlots.BandOfClock(depClock));
                if (pairOk && !(after == before - 1 || (before == 1 && after == 0)))
                {
                    ok = false;
                    Debug.LogError($"R46 ② 池容量被重复扣减：取得一条配对前余 {before}、后余 {after}，"
                                   + "应只少 1（到港与离港共用同一个起降容量池，不能各算一份）。");
                }

                // ———— ③ 只到港可以单独取得，且不冒充离港槽 ————
                // ★2026-11-07 夹具修正：容量放大后「离港有空」不再顺带保证「同刻到港也空」
                // （两池他航占用由独立哈希推导）。这一步要测的是「只到港持仓不能被当成起飞权」，
                // 因此必须挑一条**离港已满、到港还有余量**的刻 —— 否则离港本来就空着，
                // 事后「离港空闲必须为 0」的断言等于没测。
                if (!FindArrivalOnlyClock(gs, out string apt2, out string dep2))
                {
                    Debug.LogError("R46 ③ 夹具失败：找不到「离港已满、到港仍有余量」的机场时刻。"
                                   + "（夹具要的正是这个组合；找不到说明到港池已没有任何空刻，属数据异常。）");
                    return false;
                }
                var arrOnly = new List<string>();
                bool gotArr = StrategyManager.AcquireSlotsAt(gs, apt2, dep2, 1, false,
                    out msg, out arrOnly, SlotHolding.SlotKindArrival, null);
                SlotHolding hold = gotArr
                    ? FindHolding(gs, apt2, arrOnly.Count > 0 ? arrOnly[0] : dep2,
                        SlotHolding.SlotKindArrival)
                    : null;
                bool arrOk = gotArr && hold != null && hold.IsArrivalOnly && string.IsNullOrEmpty(hold.arrTime);
                if (!arrOk)
                {
                    ok = false;
                    Debug.LogError($"R46 ③ 只到港时刻取得失败：{msg}"
                                   + $"（持仓={(hold == null ? "无" : $"{hold.airport} {hold.slotTime} kind={hold.kind}")}）");
                }
                else
                {
                    // 关键：这条「只到港」持仓不能被离港需求拿去用。
                    int depFree = RoutePermitLogic.FreeSlotsAt(gs, apt2, hold.slotTime,
                        SlotHolding.SlotKindDeparture, null);
                    int arrFree = RoutePermitLogic.FreeSlotsAt(gs, apt2, hold.slotTime,
                        SlotHolding.SlotKindArrival, null);
                    if (depFree != 0 || arrFree < 1)
                    {
                        ok = false;
                        Debug.LogError($"R46 ③ 只到港持仓串池了：同一刻离港空闲 {depFree} 条、只到港空闲 {arrFree} 条，"
                                       + "应为 0 / ≥1（只有落地权的时刻不能当起飞权用）。");
                    }
                    if (!RoutePermitLogic.HasArrivalOnlyAt(gs, apt2, hold.slotTime))
                    {
                        ok = false;
                        Debug.LogError("R46 ③ HasArrivalOnlyAt 没认出刚取得的只到港持仓。");
                    }
                }

                // ———— ④ 绑班补记：老档空到港 → 绑班后写入 ————
                // 必须另找一处「此刻还没有任何持仓」的时刻，否则会挑到 ① 刚建的那条（它已有到港），
                // 补记这一步就被跳过了，测了个寂寞。
                if (!FindTarget(gs, out string apt4, out string dep4, out string arr4))
                {
                    Debug.LogError("R46 ④ 夹具失败：找不到第三处可用机场时刻。");
                    return false;
                }
                stg.slots.Add(new SlotHolding
                {
                    airport = apt4,
                    slotTime = dep4,
                    arrTime = "",                       // 模拟旧档
                    inUse = false,
                    expiresTurn = 99
                });
                var need = new RoutePermitLogic.SlotNeed
                {
                    airport = apt4,
                    time = dep4,
                    arrTime = arr4,
                    band = ScheduleSlots.BandOfClock(dep4),
                    routeId = "R46-TEST",
                    flightNo = "CE1",
                    station = RoutePermitLogic.StationOrigin,
                    inbound = false,
                    kind = SlotHolding.SlotKindDeparture
                };
                int idx = RoutePermitLogic.ReserveSlot(gs, need.airport, RoutePermitLogic.TimeOf(need),
                    need.routeId, need.flightNo, need.arrTime, need.kind);
                bool bindOk = idx >= 0 && stg.slots[idx].arrTime == arr4;
                if (!bindOk)
                {
                    ok = false;
                    Debug.LogError($"R46 ④ 绑班没补记到港时刻（idx={idx}，"
                                   + $"arr={(idx < 0 ? "—" : stg.slots[idx].arrTime)}，期望 {arr4}）。");
                }
                else if (!string.IsNullOrEmpty(RoutePermitLogic.PairMismatchNote(gs, need)))
                {
                    ok = false;
                    Debug.LogError("R46 ④ 刚补记完就到港失配：PairMismatchNote 应为空。");
                }

                // ———— ⑤ 二级市场：配对挂牌带走到港，只到港挂牌落成 ARR 持仓 ————
                var market = stg.slotMarket ?? (stg.slotMarket = new SlotMarketState());
                // 必须先标成「本季已换新」：Ensure 见到 rolled=false 会立刻 Roll，
                // 把刚刚塞进去的夹具挂牌整批冲掉（这一步漏了就会报「挂牌已下架」）。
                market.rolled = true;
                market.year = gs.year;
                market.quarter = gs.quarter;
                market.listings = new List<SlotMarketListing>();
                MarketBuyCase(gs, apt, depClock, arrClock, false, ref ok);
                MarketBuyCase(gs, apt, depClock, arrClock, true, ref ok);

                // ———— ⑥ 到港端硬约束（G2，资料 M11 的场景）————
                // 旧版只判离港那一刻，于是「离港有、到港满」的单子照样批 —— 这正是资料里最核心的例子。
                // 这里把某一刻的到港池人为打满，再要求离港端仍有空间的时刻去成对取得：
                // 必须**整单失败**，且一条持仓都不许落账（原子）。
                ArrivalHardBlock(gs, ref ok, out string blockedApt, out string blockedArr);

                // ———— ⑦ 只到港走收件箱这条真实路径（UI「只申请到港时刻」按钮的后端）————
                ArrivalOnlyViaInbox(gs, blockedApt, blockedArr, ref ok);

                return ok;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>二级市场买入：配对挂牌与只到港挂牌各买一次，确认到港属性跟着资产走。</summary>
        static void MarketBuyCase(GameState gs, string apt, string clock, string arrClock,
            bool arrivalOnly, ref bool ok)
        {
            var mkt = StrategyManager.EnsureState(gs)?.slotMarket;
            if (mkt == null)
            {
                ok = false;
                Debug.LogError("R46 ⑤ 二级市场状态为空，无法构造买入夹具。");
                return;
            }
            mkt.seq++;
            var listing = new SlotMarketListing
            {
                id = "R46-" + (arrivalOnly ? "ARR" : "DEP"),
                airport = apt,
                clock = clock,
                arrClock = arrivalOnly ? "" : arrClock,
                kind = arrivalOnly ? SlotHolding.SlotKindArrival : "",
                seller = "他航",
                askWan = 10f,
                fairWan = 10f,
                listedYear = gs.year,
                listedQuarter = gs.quarter
            };
            mkt.listings.Add(listing);
            string msg;
            if (!SlotMarketLogic.Buy(gs, listing.id, out msg))
            {
                ok = false;
                Debug.LogError($"R46 ⑤ 二级市场买入失败（{(arrivalOnly ? "只到港" : "配对")}）：{msg}");
                return;
            }
            SlotHolding bought = FindHolding(gs, apt, clock,
                arrivalOnly ? SlotHolding.SlotKindArrival : SlotHolding.SlotKindDeparture);
            bool good = bought != null
                        && (arrivalOnly
                            ? bought.IsArrivalOnly && string.IsNullOrEmpty(bought.arrTime)
                            : !bought.IsArrivalOnly && bought.arrTime == arrClock);
            if (!good)
            {
                ok = false;
                Debug.LogError($"R46 ⑤ 买入后到港属性丢失（{(arrivalOnly ? "只到港" : "配对")}）："
                               + $"持仓={(bought == null ? "无" : $"{bought.slotTime} arr={bought.arrTime} kind={bought.kind}")}，"
                               + $"期望 arr={(arrivalOnly ? "空" : arrClock)}");
            }
        }

        /// <summary>
        /// 夹具扫描诊断：把「到底卡在哪一道关卡」打出来。
        /// 他航逐刻占用计入容量后，夹具可能整场都找不到空刻 —— 那时错误文案若只说
        /// 「找不到时刻」，读的人无法区分「功能坏了」和「这一天真的没槽了」，
        /// 于是会去改功能（把容量判据放松）来让夹具变绿，那才是真正的退化。
        /// </summary>
        static string FindTargetDiag(GameState gs)
        {
            int airports = 0, bandOpen = 0, depOpen = 0, bothOpen = 0;
            string firstDep = "—";
            var airportsAll = AirportCatalog.Airports;
            for (int a = 0; a < airportsAll.Count; a++)
            {
                string code = airportsAll[a].code;
                if (!RoutePermitLogic.NeedsDepartureSlot(code)) continue;
                airports++;
                for (int b = 0; b < 4; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, code, b) < 1) continue;
                    bandOpen++;
                    var grid = ScheduleSlots.BandGridLabels(b);
                    for (int i = 0; i < grid.Count; i++)
                    {
                        string clk = RoutePermitLogic.SlotRequestClock(grid[i]);
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk) < 1) continue;
                        if (RoutePermitLogic.SlotsHeldAt(gs, code, clk) > 0) continue;
                        depOpen++;
                        if (firstDep == "—") firstDep = $"{code} {clk}";
                        int m = ScheduleSlots.ParseClock(clk);
                        if (m < 0) continue;
                        string arr = ScheduleSlots.FormatClock((m + 90) % 1440);
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, arr,
                                SlotHolding.SlotKindArrival) < 1) continue;
                        if (RoutePermitLogic.SlotsAvailable(gs, code, ScheduleSlots.BandOfClock(arr),
                                SlotHolding.SlotKindArrival) < 1) continue;
                        bothOpen++;
                    }
                }
            }
            return $"夹具扫描：协调机场 {airports} 个 · 段级有余量 {bandOpen} 段 · "
                   + $"刻级离港有余量 {depOpen} 个（首个 {firstDep}）· 两端同时有余量 {bothOpen} 个。"
                   + "若 depOpen 为 0，说明这一天确实没有可申请的离港刻（不是判据坏了）；"
                   + "若 depOpen > 0 而 bothOpen 为 0，缺的是到港端。"
                   + "\n到港端抽样：" + ArrProbe(gs);
        }

        /// <summary>到港端抽样：把「离港还空着的刻」对应的到港刻逐项拆开看。</summary>
        static string ArrProbe(GameState gs)
        {
            var airportsAll = AirportCatalog.Airports;
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            for (int a = 0; a < airportsAll.Count && shown < 5; a++)
            {
                string code = airportsAll[a].code;
                if (!RoutePermitLogic.NeedsDepartureSlot(code)) continue;
                for (int b = 0; b < 4 && shown < 5; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, code, b) < 1) continue;
                    var grid = ScheduleSlots.BandGridLabels(b);
                    for (int i = 0; i < grid.Count && shown < 5; i++)
                    {
                        string clk = RoutePermitLogic.SlotRequestClock(grid[i]);
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk) < 1) continue;
                        if (RoutePermitLogic.SlotsHeldAt(gs, code, clk) > 0) continue;
                        int m = ScheduleSlots.ParseClock(clk);
                        if (m < 0) continue;
                        string arr = ScheduleSlots.FormatClock((m + 90) % 1440);
                        int arrBand = ScheduleSlots.BandOfClock(arr);
                        sb.Append($"\n　{code} 离港 {clk}（余 {RoutePermitLogic.SlotsAvailableAt(gs, code, clk)}）"
                                  + $" → 到港 {arr}：刻余 "
                                  + $"{RoutePermitLogic.SlotsAvailableAt(gs, code, arr, SlotHolding.SlotKindArrival)}"
                                  + $" / 段余 {RoutePermitLogic.SlotsAvailable(gs, code, arrBand, SlotHolding.SlotKindArrival)}"
                                  + $" / 他航占 {IndustryAirlineLogic.IndustryAtClock(gs, code, arr, SlotHolding.SlotKindArrival)}"
                                  + $" / 容量 {RoutePermitLogic.QuarterCapacityAt(code, arr)}");
                        shown++;
                    }
                }
            }
            return shown == 0 ? "（没有可抽样的离港空刻）" : sb.ToString();
        }

        /// <summary>
        /// 在同一机场里找一个「还空着的落地刻」，与离港刻相隔 45 分钟到 6 小时。
        /// 优先取最接近 90 分钟的（贴近一次短程往返的真实轮挡），找不到就往后放宽。
        /// </summary>
        static string FindFreeArrival(GameState gs, string code, int depMin)
        {
            string best = null;
            int bestGap = int.MaxValue;
            for (int b = 0; b < 4; b++)
            {
                if (RoutePermitLogic.SlotsAvailable(gs, code, b, SlotHolding.SlotKindArrival) < 1) continue;
                var grid = ScheduleSlots.BandGridLabels(b);
                for (int i = 0; i < grid.Count; i++)
                {
                    string cand = grid[i];
                    int cm = ScheduleSlots.ParseClock(cand);
                    if (cm < 0) continue;
                    int gap = ((cm - depMin) % 1440 + 1440) % 1440;
                    if (gap < 45 || gap > 360) continue;
                    if (RoutePermitLogic.SlotsAvailableAt(gs, code, cand,
                            SlotHolding.SlotKindArrival) < 1) continue;
                    if (gap < bestGap) { bestGap = gap; best = cand; }
                }
            }
            return best;
        }

        /// <summary>找一条「某协调机场某刻还有余量」的夹具：机场 × 离港刻 × 一个合理到港刻。</summary>
        static bool FindTarget(GameState gs, out string airport, out string depClock, out string arrClock)
        {
            airport = null;
            depClock = null;
            arrClock = null;
            var airports = AirportCatalog.Airports;
            for (int a = 0; a < airports.Count; a++)
            {
                string code = airports[a].code;
                if (!RoutePermitLogic.NeedsDepartureSlot(code)) continue;
                // ★2026-11-07：bands 改为全天四段 —— 他航逐刻占用计入容量后，某一段可能一个
                // 空刻都没有（尤其是午/晚高峰），只扫 1~2 段会让夹具误报「找不到时刻」，
                // 看起来像功能坏了。夹具要的是「存在一对可申请的离港/到港刻」，与哪一段无关。
                for (int b = 0; b < 4; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, code, b) < 1) continue;
                    var grid = ScheduleSlots.BandGridLabels(b);
                    for (int i = 0; i < grid.Count; i++)
                    {
                        string clk = RoutePermitLogic.SlotRequestClock(grid[i]);
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk) < 1) continue;
                        if (RoutePermitLogic.SlotsHeldAt(gs, code, clk) > 0) continue;
                        int m = ScheduleSlots.ParseClock(clk);
                        if (m < 0) continue;
                        // ★2026-11-07：到港刻不再钉死「离港 + 90 分」，而是在同一机场里
                        // **搜一个还空着的落地刻**（45 分钟到 6 小时之间）。
                        // 钉死 +90 的写法在他航逐刻占用计入容量后变成了死结：
                        // 85–92% 的占用把「恰好 +90 分那一刻」几乎全部占满，
                        // 而现实里一对槽的落地时刻由轮挡与过站决定，本来就不是固定 90 分。
                        string arr = FindFreeArrival(gs, code, m);
                        if (arr == null) continue;
                        airport = code;
                        depClock = clk;
                        arrClock = arr;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// ⑥ 到港端硬约束（★2026-11-06，G2）。
        ///
        /// 资料 M11：<c>PEK 08:00 离港可用，但 SZX 11:15 的进场配额已满</c> —— 这一单必须被驳回，
        /// 并说清是哪一端、哪一刻，给出改点与二级市场两条出路。
        ///
        /// 旧实现把 <c>arrClock</c> 一路只写进持仓、从未参与可行性判定，所以这种单子会照批。
        /// 本用例把某一刻的到港池人为打满后要求成对取得：必须**整单失败**且一条都不落账。
        /// </summary>
        static void ArrivalHardBlock(GameState gs, ref bool ok,
            out string blockedApt, out string blockedArr)
        {
            blockedApt = null;
            blockedArr = null;
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return;

            if (!FindTarget(gs, out string apt, out string dep, out string arr))
            {
                ok = false;
                Debug.LogError("R46 ⑥ 夹具失败：找不到两端都有余量的机场时刻。");
                return;
            }
            // 到港端按「就近系列」判（AcquireSlotsAt / QueueApplySlots 都先 snap 再查），
            // 这里不先 snap 会让 ⑦b 落在另一个刻上，反例就测了个寂寞。
            arr = ScheduleSlots.NearestSeries(arr);

            // 把到港池在该刻打满：加「离港在别处、落地都在 arr」的持仓。
            // 刻意用不同的离港钟点，确保被打满的只是到港池，离港端仍然可用。
            int roomNow = RoutePermitLogic.SlotsAvailableAt(gs, apt, arr, SlotHolding.SlotKindArrival);
            // 要补的条数 = 该刻**当前余量**（不是容量减余量）——
            // 补完之后 used 正好等于 QuarterCapacity，余量为 0。
            int need = roomNow;
            for (int i = 0; i < need; i++)
            {
                int devMin = ScheduleSlots.ParseClock(dep) + 30 + i * 15; // 与 dep 错开，别把离港端也堵死
                stg.slots.Add(new SlotHolding
                {
                    airport = apt,
                    slotTime = ScheduleSlots.FormatClock(((devMin % 1440) + 1440) % 1440),
                    arrTime = arr,
                    inUse = false,
                    expiresTurn = 99
                });
            }

            if (RoutePermitLogic.SlotsAvailableAt(gs, apt, arr, SlotHolding.SlotKindArrival) > 0)
            {
                ok = false;
                Debug.LogError($"R46 ⑥ 夹具没把到港池打满（{apt} {arr} 该刻到港余量仍 > 0），后续断言无意义。");
                return;
            }
            blockedApt = apt;
            blockedArr = arr;
            if (RoutePermitLogic.SlotsAvailableAt(gs, apt, dep, SlotHolding.SlotKindDeparture) < 1)
            {
                // ★2026-11-07：容量放大后，打满到港时补的持仓（离港钟点在 dep+30/+45…）
                // 也吃掉了同波离港池，`dep` 本身可能被挤满。断言要的是「同一机场 · 到港满
                // 而离港不满」这一组合，不必绑定某一刻，故在同机场另找一个还空着的离港刻。
                string alt = null;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length && alt == null; b++)
                {
                    var g2 = ScheduleSlots.BandGridLabels(b);
                    if (g2 == null) continue;
                    for (int i = 0; i < g2.Count; i++)
                    {
                        string c = g2[i];
                        if (ScheduleSlots.ClockDiff(c, arr) <= ScheduleSlots.SlotToleranceMinutes) continue;
                        if (RoutePermitLogic.SlotsAvailableAt(gs, apt, c, SlotHolding.SlotKindDeparture) < 1)
                            continue;
                        alt = c;
                        break;
                    }
                }
                if (alt == null)
                {
                    ok = false;
                    Debug.LogError($"R46 ⑥ 夹具把离港端也堵死了（{apt} {dep}），且该机场没有别的空离港刻"
                                   + " —— 无法验证「一端有、一端无」。");
                    return;
                }
                dep = alt;
            }

            // 判据层：CanApplyStop 必须指向到港端，且缺口文案要给出两条出路。
            var needStruct = new RoutePermitLogic.SlotNeed
            {
                airport = apt, time = dep, arrTime = arr,
                band = ScheduleSlots.BandOfClock(dep),
                routeId = "R46-BLOCK", flightNo = "CE9",
                station = RoutePermitLogic.StationDest, inbound = true,
                kind = SlotHolding.SlotKindDeparture
            };
            bool canApply = RoutePermitLogic.CanApplyStop(gs, null, needStruct, out string end, out string clk);
            if (canApply || end != "到港端" || ScheduleSlots.ClockDiff(clk, arr) > 0)
            {
                ok = false;
                Debug.LogError($"R46 ⑥ CanApplyStop 没认出到港端缺口：canApply={canApply} end={end} clock={clk}，"
                               + $"期望 false / 到港端 / {arr}。");
            }
            else
            {
                string gap = RoutePermitLogic.StopGapText(gs, null, needStruct);
                if (string.IsNullOrEmpty(gap) || !gap.Contains("到港端缺口") || !gap.Contains("二级市场"))
                {
                    ok = false;
                    Debug.LogError("R46 ⑥ 缺口文案不合格：必须写明「到港端缺口」并给出「二级市场」这条路。\n" + gap);
                }
            }

            // 落账层：AcquireSlotsAt 必须整单失败，且持仓条数一条不增（原子）。
            int beforeCount = stg.slots.Count;
            bool got = StrategyManager.AcquireSlotsAt(gs, apt, dep, 1, false,
                out string msg, out var assigned, SlotHolding.SlotKindDeparture, arr);
            if (got || assigned.Count > 0 || stg.slots.Count != beforeCount)
            {
                ok = false;
                Debug.LogError($"R46 ⑥ 到港端已满仍然批了单：got={got} 落账 {assigned.Count} 条、"
                               + $"持仓 {beforeCount}→{stg.slots.Count}（应为 false / 0 / 不变）。msg={msg}");
            }
        }

        /// <summary>
        /// ⑦ 只到港走收件箱这条**真实路径**（★2026-11-06）。
        ///
        /// 机场时刻页那颗「只申请到港时刻（协调池 · 免费）」按钮调的就是
        /// <c>InboxManager.QueueApplySlots(..., kind: ARR)</c>，这条路上有三个关口会把 kind 丢掉：
        /// ① 申请时没写进信（<c>mail.slotKind</c>）；② 交割时没把 kind 交给 <c>AcquireSlotsAt</c>；
        /// ③ 拿到手却按离港槽入账。任何一处漏掉，玩家等 7~18 天换来的就是一条**起飞权**，
        /// 而到港端一分钱没补上 —— 界面上看着「已持有」，实际排班时仍然落不下来。
        ///
        /// ⑦b 反例：离港单带着一个已满的到港刻。申请阶段就该驳回并说清是哪一端，
        /// 别让玩家把 7~18 天押在一张注定批不下来的单子上。
        /// </summary>
        static void ArrivalOnlyViaInbox(GameState gs, string blockedApt, string blockedArr, ref bool ok)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return;

            if (!FindArrivalTarget(gs, out string apt, out string arr))
            {
                ok = false;
                Debug.LogError("R46 ⑦ 夹具失败：找不到到港池还有余量、且我司未占的机场时刻。");
                return;
            }

            int depFreeBefore = RoutePermitLogic.SlotsAvailableAt(gs, apt, arr, SlotHolding.SlotKindDeparture);
            const string mailId = "R46-ARR-ONLY";
            bool queued = InboxManager.QueueApplySlots(gs, apt, arr, 1, mailId, out string msg,
                "R46 到港端单独立单：只为落地权，不追加起飞权", null, null, 0, false, null,
                SlotHolding.SlotKindArrival);

            InboxMail mail = null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == mailId) mail = gs.inbox[i];

            bool mailOk = queued && mail != null
                          && mail.action == InboxManager.ApplySlotAction
                          && string.Equals(mail.slotKind, SlotHolding.SlotKindArrival,
                              System.StringComparison.OrdinalIgnoreCase)
                          && string.IsNullOrEmpty(mail.slotArrClock)
                          && (mail.body ?? "").IndexOf("到港", System.StringComparison.Ordinal) >= 0;
            if (!mailOk)
            {
                ok = false;
                Debug.LogError($"R46 ⑦ 只到港申请没落成一封「到港」提案：queued={queued} msg={msg}"
                               + $"（信={(mail == null ? "无" : $"{mail.subject} kind={mail.slotKind} arr={mail.slotArrClock}")}）");
                return;
            }

            // 交割：批准 → 到工期 → 结算。走的就是玩家在收件箱里点「批准」的那条路。
            bool approved = InboxManager.Approve(gs, mail, out string am);
            gs.SetSimUtc(gs.SimUtc.AddDays(PermitTimeline.SlotPoolOffWindowDays + 1));
            InboxManager.SettleDueEffects(gs, gs.SimUtc);

            SlotHolding got = FindHolding(gs, apt, arr, SlotHolding.SlotKindArrival);
            bool settled = approved && got != null && got.IsArrivalOnly
                           && string.IsNullOrEmpty(got.arrTime);
            if (!settled)
            {
                ok = false;
                Debug.LogError($"R46 ⑦ 只到港交割没落成 ARR 持仓：approved={approved}（{am}）"
                               + $"持仓={(got == null ? "无" : $"{got.slotTime} arr={got.arrTime} kind={got.kind}")}");
            }

            // 拿了落地权不能顺便白送一条起飞权：该刻的离港余量必须一条不变。
            int depFreeAfter = RoutePermitLogic.SlotsAvailableAt(gs, apt, arr, SlotHolding.SlotKindDeparture);
            if (depFreeAfter != depFreeBefore)
            {
                ok = false;
                Debug.LogError($"R46 ⑦ 只到港持仓把离港池也扣了：{apt} {arr} 离港余量 "
                               + $"{depFreeBefore}→{depFreeAfter}（应不变）。");
            }

            // ⑦b 反例：离港单 + 已满的到港刻 → 申请阶段驳回。
            if (string.IsNullOrEmpty(blockedApt) || string.IsNullOrEmpty(blockedArr))
            {
                ok = false;
                Debug.LogError("R46 ⑦b 缺少 ⑥ 打满的到港刻，无法做申请阶段反例。");
                return;
            }
            string depClock = null;
            for (int b = 0; b < ScheduleSlots.SlotBands.Length && depClock == null; b++)
            {
                var grid = ScheduleSlots.BandGridLabels(b);
                if (grid == null) continue;
                for (int i = 0; i < grid.Count; i++)
                {
                    string c = grid[i];
                    if (ScheduleSlots.ClockDiff(c, blockedArr) <= ScheduleSlots.SlotToleranceMinutes) continue;
                    if (RoutePermitLogic.SlotsAvailableAt(gs, blockedApt, c, SlotHolding.SlotKindDeparture) < 1)
                        continue;
                    depClock = c;
                    break;
                }
            }
            if (depClock == null)
            {
                ok = false;
                Debug.LogError($"R46 ⑦b 夹具失败：{blockedApt} 找不到此刻还空着的离港刻。");
                return;
            }
            bool queued2 = InboxManager.QueueApplySlots(gs, blockedApt, depClock, 1, "R46-DEP-BLOCKED",
                out string why, "R46 到港端已满的反例", null, null, 0, false, blockedArr);
            bool rejected = !queued2 && !string.IsNullOrEmpty(why)
                            && why.IndexOf("到港端缺口", System.StringComparison.Ordinal) >= 0;
            if (!rejected)
            {
                ok = false;
                Debug.LogError($"R46 ⑦b 到港刻已满（{blockedApt} {blockedArr}）离港端仍开了申请单："
                               + $"queued={queued2}　说明={why}（应驳回并写明「到港端缺口」）。");
            }
        }

        /// <summary>
        /// 找一条「**离港已满、到港仍有余量**、且我司在该刻没有任何持仓」的时刻（★2026-11-07）。
        ///
        /// 只到港取得的反例只能建在这种刻上：事后要断言「这条只到港持仓没有把离港池让出来」，
        /// 若离港本来就空着，断言恒为真。容量放大后到港/离港两池的他航占用由独立哈希推导，
        /// 「离港满」不再自动意味着「到港也满」，所以必须显式搜这个组合。
        /// </summary>
        static bool FindArrivalOnlyClock(GameState gs, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            var airports = AirportCatalog.Airports;
            for (int a = 0; a < airports.Count; a++)
            {
                string code = airports[a].code;
                if (!RoutePermitLogic.NeedsDepartureSlot(code)) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    var grid = ScheduleSlots.BandGridLabels(b);
                    if (grid == null) continue;
                    for (int i = 0; i < grid.Count; i++)
                    {
                        string clk = grid[i];
                        if (RoutePermitLogic.SlotsHeldAt(gs, code, clk) > 0) continue;
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk, SlotHolding.SlotKindDeparture) > 0)
                            continue;   // 离港还有空 → 这条刻测不出「不串池」
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk, SlotHolding.SlotKindArrival) < 1)
                            continue;
                        airport = code;
                        clock = clk;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>找一条「到港池还有余量、且我司在该刻没有任何持仓」的时刻（只判到港端）。</summary>
        static bool FindArrivalTarget(GameState gs, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            var airports = AirportCatalog.Airports;
            for (int a = 0; a < airports.Count; a++)
            {
                string code = airports[a].code;
                if (!RoutePermitLogic.NeedsDepartureSlot(code)) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, code, b, SlotHolding.SlotKindArrival) < 1) continue;
                    var grid = ScheduleSlots.BandGridLabels(b);
                    if (grid == null) continue;
                    for (int i = 0; i < grid.Count; i++)
                    {
                        string clk = grid[i];
                        if (RoutePermitLogic.SlotsHeldAt(gs, code, clk) > 0) continue;
                        if (RoutePermitLogic.SlotsAvailableAt(gs, code, clk, SlotHolding.SlotKindArrival) < 1)
                            continue;
                        airport = code;
                        clock = clk;
                        return true;
                    }
                }
            }
            return false;
        }

        static SlotHolding FindHolding(GameState gs, string airport, string clock, string kind)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return null;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(SlotHolding.KindOf(s), kind, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(s.slotTime, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                return s;
            }
            return null;
        }
    }
}
#endif
