#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 「购入时刻槽」与「协调池申请」两条渠道的隔离回归（2026-11-01）。
    ///
    /// ★由来：CEO 问「航权与时刻槽这三颗按钮到底做什么」，一查才发现——
    /// 页面上只有**付费**一条路（<c>buy_slot</c>），而提案正文却写着
    /// 「若不愿付费：改走协调池申请（收件箱『申请时刻槽』，不收费、按序分配）」。
    /// 免费渠道 <see cref="InboxManager.QueueApplySlots"/> 确实存在，但**这页没有入口**，
    /// 只能等运营部在缺槽时主动递单进来 —— CEO 想省钱时无处可点。
    ///
    /// 补按钮容易，补错更容易：两颗按钮共用一个 ◀▶ 选中目标，一旦渠道搞混，
    /// 玩家点了「协调池申请」却被扣了几百万，比没有按钮更糟。故这里钉四件事：
    /// <list type="number">
    /// <item><b>两封信各自落在自己的 action 上</b>（apply_slot / buy_slot），且文案把渠道讲明白；</item>
    /// <item><b>提案阶段一分钱都不扣</b> —— 扣款只发生在交割季（<c>effectPending</c>），
    ///   在提案里顺手扣一次、交割再扣一次，是这类改动的经典漏法；</item>
    /// <item><b>两条渠道互不阻塞</b>：免费单在办不该挡住「我愿意花钱插队」——
    ///   挡住就等于逼 CEO 只能在排队与静默之间二选一；</item>
    /// <item><b>付费渠道按「机场 + 时刻」判重</b>（★2026-11-02）：旧判据是全局
    ///   <c>HasOpenAction(gs,"buy_slot")</c>，ORD 的单在箱里 CGK 的单就递不进来 ——
    ///   拿「有没有任何一张单在办」冒充「这一笔是否已提过」，与航权那边修过的是同一类错误；</item>
    /// <item><b>交割时的计费分野与池量上限</b>：免费渠道现金不动、付费按价扣款、
    ///   现金不够时失败且**不扣款**、池满时免费渠道同样拒绝（免费 ≠ 无限）。</item>
    /// </list>
    ///
    /// 隔离手段与其它回归相同：内存 GameState + 静态函数，不起壳层、不碰 UI。
    /// </summary>
    public static class SlotChannelRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__slot_channel__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 5000f;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                StrategyManager.EnsureState(gs);

                if (!FindTarget(gs, out string apt, out string clock))
                {
                    Debug.LogError("R45 夹具失败：找不到池内还有余量的协调机场时刻，无法做渠道对照。"
                                   + "（先确认 RoutePermitLogic.SlotPoolCapacity / 机场目录没变，别把夹具失效当成通过。）");
                    return false;
                }
                int band = ScheduleSlots.BandOfClock(clock);
                float price = StrategyEconomics.SlotValueWanAt(apt, clock);   // 按具体时刻定价（2026-11-05）

                // ———— ① 免费渠道：落在 apply_slot 上，信里写明不收费 ————
                float cash0 = gs.cash;
                bool freeQueued = InboxManager.QueueApplySlots(gs, apt, clock, 1, null, out string freeMsg);
                var freeMail = FindMail(gs, InboxManager.ApplySlotAction);
                bool freeOk = freeQueued && freeMail != null
                              && (freeMail.body ?? "").IndexOf("不收费", System.StringComparison.Ordinal) >= 0
                              && (freeMail.body ?? "").IndexOf("协调", System.StringComparison.Ordinal) >= 0;
                if (!freeOk)
                    Debug.LogError($"R45 ① 协调池申请未落成 apply_slot 提案：{freeMsg}"
                                   + $"（信={(freeMail == null ? "无" : freeMail.subject)}）");

                // ———— ② 免费单在办时，付费渠道仍须可提（两路并存，不是二选一） ————
                bool paidQueued = InboxManager.QueueSlotDecision(gs, apt, clock, null, out string paidMsg);
                var paidMail = FindMail(gs, "buy_slot");
                bool paidOk = paidQueued && paidMail != null
                              && (paidMail.body ?? "").IndexOf("需付款", System.StringComparison.Ordinal) >= 0
                              && (paidMail.body ?? "").IndexOf("二级市场", System.StringComparison.Ordinal) >= 0;
                if (!paidOk)
                    Debug.LogError($"R45 ② 免费单在办时付费渠道被挡住了（或提案没落成）：{paidMsg}");

                // ———— ②b 付费渠道按「机场 + 时刻」判重，不跨机场阻塞（★2026-11-02） ————
                // 旧判据是全局 HasOpenAction(gs,"buy_slot")：ORD 的单在箱里，CGK 的单就递不进来。
                // 现实里这是两笔互不相干的交易。这里既钉「同刻重复被挡」，也钉「别的机场照常能提」。
                bool sameBlocked = !InboxManager.QueueSlotDecision(gs, apt, clock, null, out string sameMsg);
                bool otherFound = FindOtherTarget(gs, apt, out string apt2, out string clock2);
                string otherMsg = "";
                bool otherQueued = otherFound
                                   && InboxManager.QueueSlotDecision(gs, apt2, clock2, null, out otherMsg);
                if (!sameBlocked)
                    Debug.LogError($"R45 ②b 同一机场同一时刻的重复购入单没有被挡住（会重复扣款）。");
                if (!otherFound)
                    Debug.LogError("R45 ②b 夹具失败：找不到第二个有余量的机场时刻，无法做跨机场对照。");
                else if (!otherQueued)
                    Debug.LogError($"R45 ②b 付费渠道仍被别的机场的单挡住（{apt} 在箱，{apt2} {clock2} 递不进）：{otherMsg}");

                // ———— ③ 提案阶段不得动现金（钱在交割季才走） ————
                bool noChargeAtProposal = Mathf.Abs(gs.cash - cash0) < 0.01f;
                if (!noChargeAtProposal)
                    Debug.LogError($"R45 ③ 提案阶段就扣了钱：{cash0:F0} → {gs.cash:F0} 万（应在交割季才扣）。");

                // ———— ④ 交割计费分野：免费不动现金，付费按价扣款 ————
                //
                // ★2026-11-06：不能再假设「同一刻能连放三条」。容量改由小时容量派生后，
                //   每个 15 分钟窗口最多 2 条（容量 1 的刻只放 1 条），而本用例要连续交割
                //   免费 → 付费 → 免费三次。旧写法三次都打同一个刻，第三条必然被「该刻已被占满」
                //   驳回，误报成「免费渠道被现金挡住」（见下文 ⑤）。现在每次交割前现找一刻。
                int slots0 = gs.stg.slots.Count;
                float beforeFree = gs.cash;
                bool freeFound = FindFreeClock(gs, apt, null, null, out string clockFree);
                string freeSettleMsg = "";
                bool freeSettle = freeFound
                                  && StrategyManager.AcquireSlotsAt(gs, apt, clockFree, 1, true, out freeSettleMsg);
                bool freeFree = freeSettle && Mathf.Abs(gs.cash - beforeFree) < 0.01f
                                && gs.stg.slots.Count == slots0 + 1;
                if (!freeFree)
                    Debug.LogError($"R45 ④ 协调池交割应不收费：成功={freeSettle} 现金 {beforeFree:F0}→{gs.cash:F0} "
                                   + $"槽 {slots0}→{gs.stg.slots.Count}"
                                   + $"（{(freeFound ? clockFree + "：" : "夹具找不到可交割时刻：")}"
                                   + $"{(freeFound ? freeSettleMsg : "池全程无余量")}）");

                // 付费那一笔优先落在**同一刻**（同刻对照才说明「渠道分野」而不是「时刻不同」）；
                // 该刻只有 1 条容量时只能换刻，如实记进日志。
                bool sameClockPaid = freeFound && RoutePermitLogic.SlotsAvailableAt(gs, apt, clockFree) > 0;
                string clockPaid = sameClockPaid ? clockFree : null;
                bool paidFound = sameClockPaid || FindFreeClock(gs, apt, clockFree, null, out clockPaid);
                price = paidFound ? StrategyEconomics.SlotValueWanAt(apt, clockPaid) : price;

                float beforePaid = gs.cash;
                int slotsBeforePaid = gs.stg.slots.Count;
                string paidSettleMsg = "";
                bool paidSettle = paidFound
                                  && StrategyManager.AcquireSlotsAt(gs, apt, clockPaid, 1, false, out paidSettleMsg);
                bool paidCharged = paidSettle
                                   && Mathf.Abs((beforePaid - gs.cash) - price) < 0.51f
                                   && gs.stg.slots.Count == slotsBeforePaid + 1;
                if (!paidCharged)
                    Debug.LogError($"R45 ④ 二级市场交割应按价扣 {price:F0} 万：成功={paidSettle} "
                                   + $"现金 {beforePaid:F0}→{gs.cash:F0} 槽 {slotsBeforePaid}→{gs.stg.slots.Count}"
                                   + $"（{(paidFound ? clockPaid + "：" : "夹具找不到可交割时刻：")}"
                                   + $"{(paidFound ? paidSettleMsg : "池全程无余量")}）");

                // ———— ⑤ 反例：现金不足时付费渠道必须失败且不扣款 ————
                float saved = gs.cash;
                gs.cash = 1f;
                string brokeMsg = "";
                bool brokePaid = paidFound
                                 && StrategyManager.AcquireSlotsAt(gs, apt, clockPaid, 1, false, out brokeMsg);
                bool brokeOk = !brokePaid && Mathf.Abs(gs.cash - 1f) < 0.01f;
                // 免费渠道在同样条件下不受现金影响（这正是它存在的意义）
                int slotsBroke = gs.stg.slots.Count;
                string clockBroke = null;
                bool brokeFound = FindFreeClock(gs, apt, clockFree, clockPaid, out clockBroke);
                bool brokeStillFree = brokeFound
                                      && StrategyManager.AcquireSlotsAt(gs, apt, clockBroke, 1, true, out _)
                                      && gs.stg.slots.Count == slotsBroke + 1
                                      && Mathf.Abs(gs.cash - 1f) < 0.01f;
                gs.cash = saved;
                if (!brokeOk)
                    Debug.LogError($"R45 ⑤ 现金不足时付费渠道竟成功了，或失败却扣了款（现金={gs.cash:F0}，{brokeMsg}）");
                if (!brokeStillFree)
                    Debug.LogError($"R45 ⑤ 协调池渠道被现金挡住了（或夹具找不到第三个空刻）："
                                   + $"现金={gs.cash:F0} 找到空刻={brokeFound} 槽 {slotsBroke}→{gs.stg.slots.Count}。"
                                   + "免费渠道不该看现金。");

                // ———— ⑥ 反例：池满时免费渠道同样要拒（免费 ≠ 无限） ————
                //
                // ★2026-11-06：旧写法 `while (余量 > 0) AcquireSlotsAt(…, clock, …)` 是把同一刻
                //   反复买 —— 该刻一满就返回 false，循环再也不会推进，**要么空转要么卡死**。
                //   现在每轮现找一刻还有余量的刻，并加次数上限兜底。
                for (int guard = 0; guard < 64 && RoutePermitLogic.SlotsAvailable(gs, apt, band) > 0; guard++)
                {
                    var fillGrid = ScheduleSlots.BandGridLabels(band);
                    string fillClock = null;
                    for (int g = 0; g < fillGrid.Count; g++)
                        if (RoutePermitLogic.SlotsAvailableAt(gs, apt, fillGrid[g]) > 0) { fillClock = fillGrid[g]; break; }
                    if (fillClock == null) break;   // 池有余量但逐刻无空位 → 无法再填，交给断言暴露
                    if (!StrategyManager.AcquireSlotsAt(gs, apt, fillClock, 1, true, out _)) break;
                }
                bool poolFull = RoutePermitLogic.SlotPoolFull(gs, apt, band);
                bool freeBlockedOnFull = !InboxManager.QueueApplySlots(gs, apt, clock, 1, null, out string fullMsg)
                                         && !string.IsNullOrEmpty(fullMsg);
                if (!poolFull || !freeBlockedOnFull)
                    Debug.LogError($"R45 ⑥ 池满时免费渠道未拒绝：池满={poolFull} 拒绝={freeBlockedOnFull}（{fullMsg}）");

                bool all = freeOk && paidOk && sameBlocked && otherFound && otherQueued
                           && noChargeAtProposal && freeFree && paidCharged
                           && brokeOk && brokeStillFree && poolFull && freeBlockedOnFull;

                Debug.Log($"R45 时刻槽双渠道（{apt} {clock}·池价 {price:F0} 万）→ "
                          + $"免费单落 apply_slot={freeOk}　付费单落 buy_slot={paidOk}"
                          + $"\n　交割实位：免费 {clockFree}　付费 {clockPaid}（同刻={sameClockPaid}）　免费③ {clockBroke}"
                          + $"\n　按件事判重：同刻重复被挡={sameBlocked}　跨机场不被挡={otherQueued}（{apt2} {clock2}）"
                          + $"　提案不扣款={noChargeAtProposal}"
                          + $"\n　交割：免费不动现金={freeFree}　付费按价扣款={paidCharged}"
                          + $"\n　反例：现金不足拒绝且不扣={brokeOk}　免费不看现金={brokeStillFree}"
                          + $"　池满拒绝免费={freeBlockedOnFull}"
                          + $"\n　{all.ToString().Replace("True", "✓").Replace("False", "✗")}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 找一个「协调机场 + 池内余量 ≥3 条」的机场时刻，作为两条渠道的对照对象。
        /// 余量要 ≥1，且**该刻容量要 ≥2**：本回归先后要交割免费、付费两次，
        /// 若挑中的刻每刻只有 1 条容量，免费那条一落位，同刻就再无位置可给付费渠道 ——
        /// 那不是「付费渠道失效」，是夹具把自己堵死了（2026-11-06 曾因此误报 R45 ④/⑤）。
        /// </summary>
        static bool FindTarget(GameState gs, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            var codes = new List<string>();
            if (!string.IsNullOrEmpty(gs.hubIata)) codes.Add(gs.hubIata);
            string[] fallback = { "PEK", "PVG", "CAN", "SZX", "CDG", "FRA", "LHR", "SIN" };
            for (int i = 0; i < fallback.Length; i++)
                if (!codes.Contains(fallback[i])) codes.Add(fallback[i]);

            for (int c = 0; c < codes.Count; c++)
            {
                if (!RoutePermitLogic.NeedsDepartureSlot(codes[c])) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    // ★2026-11-06：门槛从 ≥3 降到 ≥1。用户裁决把他航占用率抬到 85%~92% 后，
                    // 任何波段都只剩 1~2 条免费槽（`MustLeaveOpen` 硬留 1），≥3 会让夹具恒失效；
                    // 而本用例实际只需要「能申请到一条、并能把池填满」这两件事，≥1 就够。
                    if (RoutePermitLogic.SlotsAvailable(gs, codes[c], b) < 1) continue;
                    // ★2026-11-06：只在该机场**确实公布了容量**的刻里挑（网格第 0 个是 00:00，没有槽）。
                    var grid = ScheduleSlots.BandGridClaimableLabels(codes[c], b);
                    if (grid == null || grid.Count == 0) continue;

                    // 逐刻找「容量 ≥2 且此刻还没被我司占」的刻：免费 + 付费两次交割要落在同一刻，
                    // 容量 1 的刻放不下第二笔。找不到就退化为首刻（至少池满场景仍能测）。
                    string pick = grid[0];
                    for (int g = 0; g < grid.Count; g++)
                    {
                        int m = ScheduleSlots.ParseClock(grid[g]);
                        if (m < 0) continue;
                        if (AirlineCEO.Data.AirportCatalog.QuarterCapacity(codes[c], m) < 2) continue;
                        if (RoutePermitLogic.SlotsAvailableAt(gs, codes[c], grid[g]) < 1) continue;
                        pick = grid[g];
                        break;
                    }
                    airport = codes[c];
                    clock = pick;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 现找一刻「该刻还有余量」的公布时刻（可排除若干已用掉的刻）。
        ///
        /// ★2026-11-06 新增：本回归要连续交割 3 次，而每 15 分钟窗口最多 2 条容量，
        /// 三次不可能打同一刻。旧写法靠「某波段余量 ≥3」间接保证，`IndustryHeld` 抬到
        /// 85%~92% 后那个前提不再成立，于是误报成渠道判据错了。
        /// 改为**每次交割前现找**，与池子有多紧无关。
        /// </summary>
        static bool FindFreeClock(GameState gs, string airport, string exclude1, string exclude2,
            out string clock)
        {
            clock = null;
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                if (RoutePermitLogic.SlotsAvailable(gs, airport, b) <= 0) continue;
                var grid = ScheduleSlots.BandGridLabels(b);
                if (grid == null || grid.Count == 0) continue;
                for (int g = 0; g < grid.Count; g++)
                {
                    string c = grid[g];
                    if (c == exclude1 || c == exclude2) continue;
                    if (RoutePermitLogic.SlotsAvailableAt(gs, airport, c) <= 0) continue;
                    clock = c;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 找一个「与 <paramref name="exclude"/> 不同、且有可用时刻的机场」，用于跨机场判重对照。
        /// 与主夹具一样只认协调机场；找不到就返回 false（由调用方报「夹具失败」，不要静默通过）。
        /// </summary>
        static bool FindOtherTarget(GameState gs, string exclude, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            string[] candidates = { "PEK", "PVG", "CAN", "SZX", "CDG", "FRA", "LHR", "SIN", "JFK", "ORD", "CGK", "SYD" };
            for (int c = 0; c < candidates.Length; c++)
            {
                string a = candidates[c];
                if (string.Equals(a, exclude, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!RoutePermitLogic.NeedsDepartureSlot(a)) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, a, b) < 1) continue;
                    var grid = ScheduleSlots.BandGridClaimableLabels(a, b);
                    if (grid == null || grid.Count == 0) continue;
                    airport = a;
                    clock = grid[0];
                    return true;
                }
            }
            return false;
        }

        static InboxMail FindMail(GameState gs, string action)
        {
            if (gs?.inbox == null) return null;
            for (int i = gs.inbox.Count - 1; i >= 0; i--)
            {
                var m = gs.inbox[i];
                if (m != null && m.action == action) return m;
            }
            return null;
        }
    }
}
#endif
