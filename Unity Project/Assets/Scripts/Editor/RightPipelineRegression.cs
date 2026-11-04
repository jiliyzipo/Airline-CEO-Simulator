#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 航权办理链路回归（2026-11-01）。
    ///
    /// ★由来：CEO 问「删掉『申请航权』『提交航权竞标』会有什么影响」，一查才发现——
    /// 撤按钮本身无害（那两颗是硬编码 PEK—SIN 的重复 stub），但它同时掀开了两个既有窟窿：
    /// <list type="number">
    /// <item><b>航权申请只递一次</b>：<c>AdvanceRoutePermit</c> 的递单条件是
    ///   <c>rightGate &amp;&amp; first</c>，而 <c>first = m.permitGate != gateLabel</c> 只在
    ///   首次置位时为真。那张单被拒、或交割时点数不足 → permitGate 仍是「航权」→
    ///   之后每季都走不到递单分支；<c>ApprovePermitRetry</c> 重递许可也不复位 permitGate。
    ///   于是这条国际线**永久卡死在「待航权」**，而航权又是国际线的硬前置。</item>
    /// <item><b>政府关系只减不增</b>：全仓只有扣点（航权 25、异议公关 10），初值 50，
    ///   没有任何回补 —— 一局最多办 2 条航权，页面上那个「/100」形同虚设。</item>
    /// </list>
    ///
    /// 本回归钉五件事：
    /// <list type="number">
    /// <item>点数成本同源：<c>RightCost(5)</c> = 25，实际扣的也是 25（信里写的、闸门判的、真扣的同一个数）；</item>
    /// <item>政府关系每季回升、封顶 100、旧档负值回正；</item>
    /// <item><b>死锁复现与修复</b>：许可卡在缺航权 → 自动递单 → 单被拒 → 两季后必须**再递一张**；</item>
    /// <item>反例：点数不足以支付时不递空单（递了也交割不了，白批一次）；</item>
    /// <item>判重按机场对，不跨线阻塞：A—B 的单在办不该挡住 C—D 的请示。</item>
    /// </list>
    ///
    /// 隔离手段与其它回归相同：内存 GameState + 静态入口，不起壳层、不碰 UI。
    /// 注意：<c>GameState.Awake</c> 会销毁重复实例，故全场只能用**一个** GameState，
    /// 各段之间靠 <see cref="Reset"/> 复位，不能各造一份（那是多个 gs 互相打架的经典坑）。
    /// </summary>
    public static class RightPipelineRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__right_pipeline__");
            try
            {
                var gs = go.AddComponent<GameState>();
                Reset(gs);
                var stg = StrategyManager.EnsureState(gs);

                // ———— ① 成本同源：RightCost(5) == 实际扣点 == 25 ————
                float cost = StrategyManager.RightCost(5);
                bool costOk = Mathf.Abs(cost - 25f) < 0.01f;
                if (!costOk) Debug.LogError($"R46 ① 第五航权成本应为 25 点，RightCost(5)={cost:F1}");

                stg.governmentRelation = 60f;
                bool applied = StrategyManager.ApplyRouteRightOn(gs, "PEK", "FRA", 5, out string applyMsg);
                bool deducted = applied
                                && Mathf.Abs(stg.governmentRelation - (60f - cost)) < 0.01f
                                && StrategyManager.HasRouteRight(gs, "PEK", "FRA");
                if (!deducted)
                    Debug.LogError($"R46 ① 实际扣点与 RightCost 不一致：60 → {stg.governmentRelation:F1}"
                                   + $"（应扣 {cost:F1}；rights={stg.rights.Count}；{applyMsg}）");

                // 反例：点数不足时必须失败，且不扣点、不入账
                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                stg.governmentRelation = cost - 1f;
                bool lowRejected = !StrategyManager.ApplyRouteRightOn(gs, "PEK", "SIN", 5, out _)
                                   && Mathf.Abs(stg.governmentRelation - (cost - 1f)) < 0.01f
                                   && stg.rights.Count == 0;
                if (!lowRejected)
                    Debug.LogError("R46 ① 点数不足时航权申请竟成功，或失败却扣了点/入了账。");

                // ———— ② 政府关系每季回升：+2、封顶 100、负值回正 ————
                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                float per = StrategyManager.GovernmentRelationPerQuarter;
                stg.governmentRelation = 10f;
                StrategyManager.TickQuarter(gs);
                float afterUp = stg.governmentRelation;
                bool up = Mathf.Abs(afterUp - (10f + per)) < 0.01f;
                stg.governmentRelation = StrategyManager.GovernmentRelationCap - 1f;
                StrategyManager.TickQuarter(gs);
                float afterCap = stg.governmentRelation;
                bool capped = Mathf.Abs(afterCap - StrategyManager.GovernmentRelationCap) < 0.01f;
                stg.governmentRelation = -5f;   // 旧档脏值：只减不增的时代可能留下负数
                StrategyManager.TickQuarter(gs);
                float afterFix = stg.governmentRelation;
                bool fixedNegative = Mathf.Abs(afterFix - per) < 0.01f;
                if (!up) Debug.LogError($"R46 ② 政府关系未每季回升：10 → {afterUp:F1}（应 +{per:F0}）");
                if (!capped) Debug.LogError($"R46 ② 政府关系未封顶 {StrategyManager.GovernmentRelationCap:F0}：{afterCap:F1}");
                if (!fixedNegative) Debug.LogError($"R46 ② 负值未回正：-5 → {afterFix:F1}（应 {per:F0}）");

                // ———— ③ 死锁复现与修复：卡在缺航权 → 递单 → 被拒 → 两季后必须再递 ————
                // 用 PEK—FRA 而不用 PEK—SIN：ProcessPendingEffects 末尾会跑 MaybeOfferRightsAndSlots，
                // 那条硬编码的 PEK—SIN 提案（id m_right_pek_sin）会混进收件箱，
                // 让「有没有重新递单」这个判据永远为真 —— 夹具被生产代码的兜底提案骗过去，
                // 是最容易写出「假绿」的一类错误。
                const string O = "PEK";
                const string D = "FRA";
                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                stg.governmentRelation = 50f;
                var permit = BuildPermitMail(gs, O, D);
                InboxManager.Add(gs, permit);
                InboxManager.ProcessPendingEffects(gs);

                var firstRight = FindRightMail(gs, O, D);
                bool queuedOnce = firstRight != null;
                if (!queuedOnce)
                    Debug.LogError("R46 ③ 许可卡在「待航权」却没有自动递出航权申请 —— 整条国际线就此无解。");

                // 模拟 CEO 拒绝（或交割失败）：结案那张单，再推进两个季度
                if (firstRight != null) firstRight.status = "rejected";
                bool requeued = false;
                string secondId = null;
                for (int i = 0; i < 2 && queuedOnce; i++)
                {
                    AdvanceQuarter(gs);
                    permit.effectPending = true;
                    permit.effectYear = gs.year;
                    permit.effectQuarter = gs.quarter;
                    InboxManager.ProcessPendingEffects(gs);
                    var again = FindOpenRightMail(gs, O, D);
                    if (again != null && (firstRight == null || again.id != firstRight.id))
                    {
                        requeued = true;
                        secondId = again.id;
                        break;
                    }
                }
                if (!requeued)
                    Debug.LogError("R46 ③ 航权单被拒后不再递单（旧死锁复现）：permitGate 仍是「航权」时，"
                                   + "旧实现走不到递单分支，这条线永久卡在待航权。");

                // ———— ④ 反例：点数不足以支付时不递空单 ————
                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                stg.governmentRelation = 0f;    // 递了也交割不了
                InboxManager.Add(gs, BuildPermitMail(gs, O, D));
                InboxManager.ProcessPendingEffects(gs);
                bool noEmptyAsk = FindRightMail(gs, O, D) == null;
                if (!noEmptyAsk)
                    Debug.LogError("R46 ④ 点数不足时仍递了空单 —— 那次批准注定交割失败，白耗 CEO 一次决策。");

                // ———— ⑤ 判重按机场对：A—B 在办不挡 C—D ————
                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                stg.governmentRelation = 90f;
                bool q1 = InboxManager.QueueRightDecision(gs, "PEK", "SIN", null, out _);
                bool q2 = InboxManager.QueueRightDecision(gs, "SZX", "LAX", null, out string q2Msg);
                bool dupBlocked = !InboxManager.QueueRightDecision(gs, "PEK", "SIN", null, out _);
                if (!q1 || !q2)
                    Debug.LogError($"R46 ⑤ 按机场对判重失效：PEK—SIN={q1} SZX—LAX={q2}（{q2Msg}）");
                if (!dupBlocked)
                    Debug.LogError("R46 ⑤ 同一机场对的重复单没有被挡住（会重复扣点）。");

                // ———— ⑥ 航权档位按「承运人本国」判定，不再一律第五航权 ————
                // 报障原话：「基地在巴黎，申请 PEK—SIN 第五航权是对的，CEO 又申请开通 CDG—SIN，
                // 为什么会收到战略部发的申请 CDG—SIN 第五航权？而且从基地巴黎到新加坡也不是第五航权吧？」
                // 旧口径 = 国际线一律「第五航权 / 25 点」，于是法国承运人回自家基地 CDG 飞 SIN
                // （第三/第四航权）也被当成要三国点头的第五航权，还按最贵的一档收钱。
                Reset(gs);
                gs.hubIata = "CDG";
                int fLocal = RoutePermitLogic.FreedomFor(gs, "CDG", "SIN");    // 本国 ↔ 他国 → 3
                int fBeyond = RoutePermitLogic.FreedomFor(gs, "PEK", "SIN");   // 他国 ↔ 他国 → 5
                int fCabotage = RoutePermitLogic.FreedomFor(gs, "PEK", "PVG"); // 他国境内两点 → -1
                gs.hubIata = "PEK";
                int fDomestic = RoutePermitLogic.FreedomFor(gs, "PEK", "SHA"); // 国内 → 0
                gs.hubIata = "CDG";   // 后面的提案检查仍在巴黎基地下做

                Reset(gs);
                stg = StrategyManager.EnsureState(gs);
                gs.hubIata = "CDG";
                stg.governmentRelation = 60f;
                bool localQueued = InboxManager.QueueRightDecision(gs, "CDG", "SIN", "m_right_cdg_sin", out string localMsg);
                var localMail = FindRightMail(gs, "CDG", "SIN");
                bool localLabel = localMail != null
                                  && localMail.subject.Contains("第三/第四航权")
                                  && localMail.subject.IndexOf("第五航权", System.StringComparison.Ordinal) < 0
                                  && localMail.body.Contains("第三/第四航权");
                bool cabotageRefused = !InboxManager.QueueRightDecision(gs, "PEK", "PVG", null, out string cabMsg);
                // 批准时按同一档扣点：60 − RightCost(3) = 39，而不是按第五航权扣到 35
                bool localApplied = StrategyManager.ApplyRouteRightOn(gs, "CDG", "SIN", 3, out _);
                float localCost = StrategyManager.RightCost(3);
                bool localCharged = localApplied && Mathf.Abs(stg.governmentRelation - (60f - localCost)) < 0.01f;
                bool freedomOk = fLocal == 3 && fBeyond == 5 && fCabotage == -1 && fDomestic == 0
                                 && localQueued && localLabel && cabotageRefused && localCharged;
                if (!freedomOk)
                    Debug.LogError($"R46 ⑥ 航权档位判定不符：CDG—SIN={fLocal}（应 3）　PEK—SIN={fBeyond}（应 5）"
                                   + $"　PEK—PVG={fCabotage}（应 -1）　PEK—SHA={fDomestic}（应 0）"
                                   + $"　递单={localQueued}（{localMsg}）　标题写对档位={localLabel}"
                                   + $"　国内载权被拒={cabotageRefused}（{cabMsg}）　按 3 档扣点={localCharged}"
                                   + $"（余 {stg.governmentRelation:F1}，应为 {60f - localCost:F1}）");
                gs.hubIata = "PEK";

                bool all = costOk && deducted && lowRejected && up && capped && fixedNegative
                           && queuedOnce && requeued && noEmptyAsk && q1 && q2 && dupBlocked && freedomOk;

                Debug.Log($"R46 航权办理链路 → 成本 {cost:F0} 点同源={costOk && deducted}　点数不足拒绝={lowRejected}"
                          + $"\n　政府关系：每季+{per:F0}={up}　封顶={capped}　负值回正={fixedNegative}"
                          + $"\n　死锁：首次递单={queuedOnce}　被拒后再递={requeued}（{secondId ?? "无"}）　点数不足不递空单={noEmptyAsk}"
                          + $"\n　按对判重：同对重复被挡={dupBlocked}"
                          + $"\n　档位（巴黎基地）：CDG—SIN={fLocal}（应 3）　PEK—SIN={fBeyond}（应 5）"
                          + $"　PEK—PVG={fCabotage}（应 -1）　PEK—SHA={fDomestic}（应 0）"
                          + $"\n　{all.ToString().Replace("True", "✓").Replace("False", "✗")}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>各段之间复位：清空收件箱/航权持仓、点数归位，避免上一段的状态污染下一段。</summary>
        static void Reset(GameState gs)
        {
            gs.year = 1;
            gs.quarter = 1;
            gs.cash = 5000f;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            var stg = StrategyManager.EnsureState(gs);
            stg.rights.Clear();
            stg.governmentRelation = 50f;
        }

        /// <summary>造一张「已受理的国际线许可、卡在缺航权」的办理单。</summary>
        static InboxMail BuildPermitMail(GameState gs, string o, string d)
        {
            return new InboxMail
            {
                id = "m_open_" + o + "_" + d,
                sender = "运营部",
                role = "准入办理",
                glyph = "运",
                kind = "regulatory",
                action = "open_route",
                origin = o,
                dest = d,
                subject = $"准入受理：{o}-{d} 国际/地区航线经营许可",
                body = "",
                status = "approved",
                tag = "受理",
                permitInternational = true,
                permitStage = RoutePermitLogic.StageReview,
                permitNode = RoutePermitLogic.NodeAccept,
                permitAttempts = 1,
                permitAcceptYear = gs.year,
                permitAcceptQuarter = gs.quarter,
                permitDueYear = gs.year + 1,
                permitDueQuarter = gs.quarter,
                effectPending = true,
                effectYear = gs.year,
                effectQuarter = gs.quarter,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel
            };
        }

        static void AdvanceQuarter(GameState gs)
        {
            InboxManager.NextQuarter(gs.year, gs.quarter, out int y, out int q);
            gs.year = y;
            gs.quarter = q;
            // ★2026-11-02：审批改为按天到期（dueUtcTicks），只改 year/quarter 不够 ——
            // 游戏时钟也必须往前走，否则 ProcessPendingEffects 永远认为「还没到期」。
            gs.SetSimUtc(gs.SimUtc.AddDays(GameState.DaysPerQuarter));
            StrategyManager.TickQuarter(gs);
        }

        static InboxMail FindRightMail(GameState gs, string o, string d)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null) continue;
                if (!string.Equals(m.action, "apply_right", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(m.origin, o, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.dest, d, System.StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        static InboxMail FindOpenRightMail(GameState gs, string o, string d)
        {
            var m = FindRightMail(gs, o, d);
            return m != null && InboxManager.IsOpen(m) ? m : null;
        }
    }
}
#endif

