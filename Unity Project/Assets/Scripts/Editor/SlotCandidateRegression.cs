#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// T05.6「备选时刻必须可按真实约束检验」的隔离回归（2026-10-31）。
    ///
    /// 任务书原文：*备选时刻写明机场、时刻、逐停站需求、优缺点、费用／免费渠道及预计办理时点；
    /// 所列选项必须可按真实约束检验。*
    ///
    /// 前半句是**文案要素**，后半句才是要害：运营部在信里说「这一刻两端都有持仓、不用花钱」，
    /// 玩家没有任何办法验证；如果这句话是错的，玩家会按「立刻能排」的预期点下去，然后在交割季
    /// 发现槽没到、班次没加。此前 <see cref="SlotCandidateLogic"/> 只在 <c>LogicSelfTests.Run()</c>
    /// 的手动总集合里出现（协作规则禁止拿全量自测替代隔离回归），于是隔离跑批里这条路径
    /// **长期不在执行范围内** —— 与 <c>CeoCommandRegression</c> 当年同样的问题。
    ///
    /// 本回归钉三件事：
    /// ① **文案六要素**逐条备选都在：机场 / 时刻 / 逐停站需求 / 有利与代价 / 费用与免费渠道 / 办理时点；
    /// ② **可检验**：把信里那条备选的每一项声明（两端档位、经停端条数、价格、「即刻可排」）
    ///    拿**当前台账**重算一遍，必须逐项相等；口径与生成侧同源，但取数发生在断言时刻，
    ///    所以「信箱里躺着的旧信」不会因为台账改了而自动变成对的；
    /// ③ **检不出错就说明这套检验是空的**：故意篡改一条备选（把「需申请」写成「已有持仓」、
    ///    价格清零、标成「即刻可排」），复检必须判伪 —— 只钉正面的话，一个「永远 return true」
    ///    的复检函数也能过。
    /// </summary>
    public static class SlotCandidateRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__slot_candidate__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 5000f;

                // 与 LogicSelfTests 的 R21 同一夹具：PEK—SZX 1900 km、08:00 首班、晚班窗口要 17:30。
                // 晚班窗口里本线往返 5.3 h，PEK 宵禁 23:00–06:00，故 18:00 之后会先被「派不出机」挡下，
                // 走不到时刻槽这一段 —— 16:30 / 17:00 / 17:30 才是本波真正可选的三刻。
                var route = new RouteData
                {
                    routeId = "R-T056", origin = "PEK", destination = "SZX", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU500", registration = "B-9001",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                route.SyncFlightsFromPrimary();
                gs.activeRoutes.Add(route);
                gs.fleet.Add(new Aircraft { registration = "B-2006", type = "A320neo", isActive = true });

                const string pegClock = "17:30";
                InboxManager.QueueAssignFlight(gs, route, "B-2006", pegClock, out _);

                var mail = FindMail(gs, InboxManager.SlotSelectAction);
                if (mail == null)
                {
                    Debug.LogError("T05.6 夹具失败：缺槽时应当生成 slot_select 备选件，实际没有。"
                                   + "（先确认 QueueAssignFlight 的前置还成立，别把夹具失效当成通过。）");
                    return false;
                }

                // 信里存的编码串 → 解码后的「对外声明」；同一次生成用完整对象核优缺点文案。
                var claimed = SlotCandidateLogic.Decode(mail.payloadCsv);
                var full = SlotCandidateLogic.Options(gs, route, pegClock);
                string body = mail.body ?? "";

                bool countOk = claimed.Count >= 2 && claimed.Count <= SlotCandidateLogic.MaxCandidates
                               && full.Count == claimed.Count;
                bool payloadOk = SlotCandidateLogic.Encode(full) == (mail.payloadCsv ?? "");
                if (!countOk)
                    Debug.LogError($"T05.6 ① 备选条数不符：信里 {claimed.Count} 条、现算 {full.Count} 条，"
                                   + $"期望 2–{SlotCandidateLogic.MaxCandidates} 条。");
                if (!payloadOk)
                    Debug.LogError($"T05.6 ①b 信里存的备选与现算不一致：\n　信={mail.payloadCsv}\n　算={SlotCandidateLogic.Encode(full)}");

                // —— ② 逐条：文案六要素 + 按当前台账复检 ——
                bool textOk = true, recheckOk = true, orderOk = true;
                var detail = new StringBuilder();
                for (int i = 0; i < claimed.Count; i++)
                {
                    var c = claimed[i];
                    var o = i < full.Count ? full[i] : null;
                    // 每条备选只看**自己那一段**：整封信里别的候选写了「取槽成本」，不能算这一条写了。
                    string block = BlockOf(body, i, claimed.Count);

                    // ②a 时刻：备选身份是「去程离港时刻」，必须写进「候选 N　HH:mm 起飞」
                    bool clockOk = c.depClock.Length == 5
                                   && block.IndexOf($"候选 {i + 1}　{c.depClock} 起飞", StringComparison.Ordinal) >= 0;
                    // ②b 机场：两端都要在这一段里出现（对端名在「各停站」行）
                    bool airportOk = block.IndexOf(o != null ? o.outAirport : route.origin, StringComparison.Ordinal) >= 0
                                     && block.IndexOf(o != null ? o.inAirport : route.destination, StringComparison.Ordinal) >= 0
                                     && block.IndexOf("各停站", StringComparison.Ordinal) >= 0;
                    // ②c 逐停站需求：本场端 / 对端各一行，经停端每站额外一行
                    bool eachEndOk = block.IndexOf("本场端", StringComparison.Ordinal) >= 0
                                     && block.IndexOf("对端", StringComparison.Ordinal) >= 0
                                     && (o == null || o.viaTotal == 0
                                         || block.IndexOf("经停端", StringComparison.Ordinal) >= 0);
                    // ②d 优缺点两面：有利必写；有代价时「代价」段必须在（无代价不该硬凑一段）
                    bool prosOk = o != null && !string.IsNullOrEmpty(o.pros);
                    bool consOk = o == null || (string.IsNullOrEmpty(o.cons)
                                                 ? block.IndexOf("　代价　", StringComparison.Ordinal) < 0
                                                 : block.IndexOf("　代价　", StringComparison.Ordinal) >= 0);
                    // ②e 费用与免费渠道：要花钱的备选必须同时写「二级市场约 N 万」与「协调池申请 0」
                    bool feeOk = o == null || (o.priceWan > 0.01f
                        ? block.IndexOf("取槽成本", StringComparison.Ordinal) >= 0
                          && block.IndexOf("协调池申请 0", StringComparison.Ordinal) >= 0
                          && block.IndexOf("二级市场", StringComparison.Ordinal) >= 0
                        : block.IndexOf("取槽成本", StringComparison.Ordinal) < 0);
                    // ②f 预计办理时点：独立一行，且与 EtaText 同一口径（不是「下季」这种相对说法）
                    string eta = SlotCandidateLogic.EtaText(gs, c);
                    bool etaOk = !string.IsNullOrEmpty(eta)
                                 && block.IndexOf("办理时点", StringComparison.Ordinal) >= 0
                                 && block.IndexOf(eta, StringComparison.Ordinal) >= 0;

                    bool oneText = clockOk && airportOk && eachEndOk && prosOk && consOk && feeOk && etaOk;
                    textOk &= oneText;
                    if (!oneText)
                        Debug.LogError($"T05.6 ② 候选 {i + 1}（{c.depClock}）文案要素缺失：时刻={clockOk} 机场={airportOk} "
                                       + $"逐停站={eachEndOk} 有利={prosOk} 代价={consOk} 费用={feeOk} 办理时点={etaOk}（期望「{eta}」）");

                    // ②g 可按真实约束检验：把声明拿当前台账重算
                    bool reOk = RecheckAgainstLedger(gs, route, c, out string why);
                    recheckOk &= reOk;
                    if (!reOk)
                        Debug.LogError($"T05.6 ② 候选 {i + 1}（{c.depClock}）声明与台账不符：{why}");

                    detail.Append($"\n　候选 {i + 1}　{c.depClock}　本场端={SlotCandidateLogic.EndStateName(c.outState)}"
                                  + $"　对端={SlotCandidateLogic.EndStateName(c.inState)}"
                                  + $"　价={c.priceWan:F0}万　即刻可排={c.readyNow}　可派机={c.aircraftOk}"
                                  + $"　经停 {(c.viaTotal == 0 ? "—" : $"{c.viaApplyCount}/{c.viaTotal} 需办")}"
                                  + $"　复检={reOk}");
                }

                // —— ③ 检不出错就说明检验是空的：篡改一条再复检 ——
                //    取第一条「需申请」的备选（本夹具里必然存在：无任何持仓），把它写成「已有持仓 + 即刻可排 + 免费」。
                bool tamperCaught = false;
                string tamperWhy = "（本夹具没有需申请的备选，无法做反例）";
                for (int i = 0; i < claimed.Count; i++)
                {
                    if (claimed[i].outState != SlotCandidateLogic.EndApply
                        && claimed[i].inState != SlotCandidateLogic.EndApply) continue;
                    var forged = Clone(claimed[i]);
                    forged.outState = SlotCandidateLogic.EndReady;
                    forged.inState = SlotCandidateLogic.EndReady;
                    forged.priceWan = 0f;
                    forged.readyNow = true;
                    tamperCaught = !RecheckAgainstLedger(gs, route, forged, out tamperWhy);
                    break;
                }
                if (!tamperCaught)
                    Debug.LogError($"T05.6 ③ 反例未被抓住：把「需申请」改成「已有持仓」后复检仍判真（{tamperWhy}）。"
                                   + "说明这套复检是空的，② 的通过不算证据。");

                // —— ④ 同一台账生成两次必须逐字相同（否则「排序」里混进了非确定性，玩家每次看到的推荐会变）——
                orderOk = SlotCandidateLogic.Encode(SlotCandidateLogic.Options(gs, route, pegClock))
                          == SlotCandidateLogic.Encode(SlotCandidateLogic.Options(gs, route, pegClock));
                if (!orderOk)
                    Debug.LogError("T05.6 ④ 同一台账两次生成的备选顺序不一致（排序含非确定性）。");

                // —— ⑤ 台账变了，重算必须跟着变：给本场端补一条 17:30 持仓，该条不得再声称「需申请」——
                bool reactsOk = true;
                string reactsWhy = "";
                if (claimed.Count > 0)
                {
                    string probe = claimed[0].depClock;
                    if (gs.stg == null) gs.stg = new StrategyState();
                    if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
                    gs.stg.slots.Add(new SlotHolding { airport = route.origin, slotTime = probe });
                    var after = SlotCandidateLogic.Options(gs, route, probe);
                    var probed = after.Find(x => x.depClock == probe);
                    reactsOk = probed != null && probed.outState == SlotCandidateLogic.EndReady
                               && probed.priceWan < claimed[0].priceWan + 0.01f;
                    var stale = SlotCandidateLogic.Options(gs, route, probe).Find(x => x.depClock == probe);
                    bool staleCaught = stale == null || RecheckAgainstLedger(gs, route, claimed[0], out _) == false;
                    reactsWhy = probed == null
                        ? "补了持仓后该时刻不再出现在备选里"
                        : $"补 {route.origin} {probe} 持仓后：本场端={SlotCandidateLogic.EndStateName(probed.outState)}"
                          + $"（期望 ready）　价 {claimed[0].priceWan:F0}→{probed.priceWan:F0} 万"
                          + $"　旧声明复检={staleCaught}（期望 True：旧声明已过期，必须被检出）";
                    reactsOk = reactsOk && staleCaught;
                    if (!reactsOk) Debug.LogError("T05.6 ⑤ 台账变更后复检未跟上：" + reactsWhy);
                }

                bool all = countOk && payloadOk && textOk && recheckOk && tamperCaught && orderOk && reactsOk;
                Debug.Log($"T05.6 备选时刻可检验 → 条数={countOk}（{claimed.Count}）　编码一致={payloadOk}"
                          + $"　文案六要素={textOk}　按台账复检={recheckOk}"
                          + $"\n　篡改可检出={tamperCaught}（{tamperWhy}）　两次生成一致={orderOk}"
                          + $"\n　台账变更后跟上={reactsOk}（{reactsWhy}）"
                          + detail
                          + $"\n　主题「{mail.subject}」　{all.ToString().Replace("True", "✓").Replace("False", "✗")}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 把一条备选的**声明**拿当前台账重算：两端档位、经停端条数、价格、「即刻可排」。
        ///
        /// 与 <see cref="SlotCandidateLogic.Build"/> 同源取数（<see cref="RoutePermitLogic.ClockNeeds"/> /
        /// <see cref="SlotCandidateLogic.EndStateOf"/> / <see cref="StrategyEconomics.SlotValueWan"/>），
        /// 但**在断言时刻重新调用**，所以它检验的是「信里那句话对不对」，不是「生成函数跑过没有」。
        /// </summary>
        static bool RecheckAgainstLedger(GameState gs, RouteData route, SlotCandidateLogic.SlotOption o,
            out string why)
        {
            why = "";
            if (o == null) { why = "备选为空"; return false; }

            var needs = RoutePermitLogic.ClockNeeds(route, o.depClock);
            string outState = SlotCandidateLogic.EndNone, inState = SlotCandidateLogic.EndNone;
            int viaTotal = 0, viaApply = 0, viaChange = 0;
            float price = 0f;
            bool viable = true, readyNow = true;

            for (int i = 0; i < needs.Count; i++)
            {
                var need = needs[i];
                string st = SlotCandidateLogic.EndStateOf(gs, route, need);
                if (need.station == RoutePermitLogic.StationOrigin) outState = st;
                else if (need.station == RoutePermitLogic.StationDest) inState = st;
                else
                {
                    viaTotal++;
                    if (st == SlotCandidateLogic.EndApply) viaApply++;
                    if (st == SlotCandidateLogic.EndChange) viaChange++;
                }
                if (st == SlotCandidateLogic.EndBlocked) viable = false;
                if (st == SlotCandidateLogic.EndReady || st == SlotCandidateLogic.EndNone) continue;
                readyNow = false;
                price += StrategyEconomics.SlotValueWan(need.airport, need.band);
            }
            readyNow = readyNow && viable;

            var diffs = new List<string>();
            if (o.outState != outState)
                diffs.Add($"本场端 声明={SlotCandidateLogic.EndStateName(o.outState)} 台账={SlotCandidateLogic.EndStateName(outState)}");
            if (o.inState != inState)
                diffs.Add($"对端 声明={SlotCandidateLogic.EndStateName(o.inState)} 台账={SlotCandidateLogic.EndStateName(inState)}");
            if (o.viaTotal != viaTotal) diffs.Add($"经停端条数 声明={o.viaTotal} 台账={viaTotal}");
            if (o.viaApplyCount != viaApply) diffs.Add($"经停端需申请 声明={o.viaApplyCount} 台账={viaApply}");
            if (o.viaChangeCount != viaChange) diffs.Add($"经停端需改时 声明={o.viaChangeCount} 台账={viaChange}");
            if (Mathf.Abs(o.priceWan - price) > 0.51f) diffs.Add($"价格 声明={o.priceWan:F1} 台账={price:F1}");
            if (o.readyNow != readyNow) diffs.Add($"即刻可排 声明={o.readyNow} 台账={readyNow}");

            if (diffs.Count > 0) { why = string.Join("；", diffs.ToArray()); return false; }
            return true;
        }

        /// <summary>
        /// 取出第 <paramref name="index"/> 条备选在正文里的那一段（从「候选 N　」到下一个「候选 N+1　」
        /// 或结尾的「口径：」）。逐条文案判定必须按段做 —— 用整封信判定时，别的候选写了
        /// 「取槽成本」，这一条也会被算成写了。
        /// </summary>
        static string BlockOf(string body, int index, int count)
        {
            if (string.IsNullOrEmpty(body)) return "";
            string head = $"候选 {index + 1}　";
            int from = body.IndexOf(head, StringComparison.Ordinal);
            if (from < 0) return "";
            int to = index + 1 < count
                ? body.IndexOf($"候选 {index + 2}　", from + head.Length, StringComparison.Ordinal)
                : body.IndexOf("\n口径：", from, StringComparison.Ordinal);
            if (to < 0) to = body.Length;
            return body.Substring(from, to - from);
        }

        static SlotCandidateLogic.SlotOption Clone(SlotCandidateLogic.SlotOption o)
            => new SlotCandidateLogic.SlotOption
            {
                depClock = o.depClock, pegClock = o.pegClock, band = o.band,
                outAirport = o.outAirport, inAirport = o.inAirport, inClock = o.inClock,
                outState = o.outState, inState = o.inState,
                viaTotal = o.viaTotal, viaApplyCount = o.viaApplyCount, viaChangeCount = o.viaChangeCount,
                priceWan = o.priceWan, demand = o.demand,
                aircraftOk = o.aircraftOk, aircraftNote = o.aircraftNote,
                curfewOk = o.curfewOk, curfewNote = o.curfewNote, sameDay = o.sameDay,
                windowMin = o.windowMin, viable = o.viable, readyNow = o.readyNow,
                pros = o.pros, cons = o.cons
            };

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
