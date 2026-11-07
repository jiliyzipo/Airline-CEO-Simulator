#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 开航研究会纪要的**单一出处**回归（2026-10-25 · P22 / S12-AC5）。
    ///
    /// 钉三件事，都不是排版：
    /// <list type="number">
    /// <item><b>先否决、后算账</b>。不成立的机场对（同城、航距过短、非口岸跨境）必须在
    ///   算引力模型与日利**之前**就被挡掉。顺序反了会产出一封看起来很专业的信，
    ///   却在推销一条不该存在的航线 —— 而且信里的日利数字会让 CEO 相信它。</item>
    /// <item><b>同一输入 → 逐字节相同的纪要</b>。页面（<see cref="OpenRouteDecisionFlow"/>）
    ///   与命令台（<see cref="DeptReportLogic"/>）都调 <see cref="OpenRouteStudyLogic.Build"/>，
    ///   所以两者对同一机场对不可能给出两种说法。这条断言的是「可以由同一处产出一致结果」，
    ///   不是「两处碰巧写得一样」。</item>
    /// <item><b>UI 侧不得再长出第二个正文作者</b>。抽出去之后，<c>BuildStudyMailBody</c> /
    ///   <c>FormatPath</c> 应当已从流程类上消失；若有人为了「顺手改一句」又加回来，
    ///   纪要就重新有了两个作者，而分叉通常只在某次改口径时漏一处，事后极难发现。</item>
    /// <item><b>选时刻的判据不许退回「固定 12:00」</b>（2026-11-03）。旧口径一受理就按
    ///   `DepTimes[1]` 提买单，把「又旺又便宜」的 18:00 一直漏掉；同时也不许用纯
    ///   「需求 ÷ 价格」把最便宜的夜航顶上来 —— 夜航省下的钱，小于它少赚的客。</item>
    /// <item><b>航权 / 时刻 / 全链条现金要写在签字页上</b>，不能只躺在「批准后要办的事」里。</item>
    /// <item><b>「最优」必须是买得起的</b>：现金不够时仍给同一时段，但正文要说明差多少。</item>
    /// </list>
    /// </summary>
    public static class OpenRouteStudyRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__open_route_study_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.cash = 900f;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();

                // ① 同城机场对：PEK（首都）与 PKX（大兴）之间现实中没有定期客运。
                //    必须在算日利之前否决 —— 所以 body 为空、rpm 未填。
                var bad = OpenRouteStudyLogic.Build(gs, "PEK", "PKX", null);
                bool rejectOk = !bad.ok
                                && !string.IsNullOrEmpty(bad.reject)
                                && bad.reject.Contains("同城")
                                && string.IsNullOrEmpty(bad.body)
                                && bad.rpm.score == 0;

                // ② 否决件不投递：命令台说同一句话，只回一句理由，收件箱一封不加。
                string badRaw = "开通北京首都—北京大兴航线";
                var badAsk = CeoCommandRouter.Route("n12_航线网络", badRaw);
                var badAns = DeptReportLogic.Build(gs, badAsk, badRaw);
                int before = gs.inbox.Count + gs.pendingInbox.Count;
                bool badRun = DeptReportLogic.RunPending(gs, badAns, badRaw, out string badReceipt);
                bool rejectNoMail = badAsk.ok && badAns.ok && badAns.needsRun
                                    && !badRun
                                    && gs.inbox.Count + gs.pendingInbox.Count == before
                                    && !string.IsNullOrEmpty(badReceipt) && badReceipt.Contains("同城");

                // ③ 同一输入两次 → 逐字节相同的纪要（页面与命令台共用这一个函数）
                var first = OpenRouteStudyLogic.Build(gs, "PEK", "JFK", null);
                var second = OpenRouteStudyLogic.Build(gs, "PEK", "JFK", null);
                bool sameMemo = first.ok && second.ok
                                && first.subject == second.subject
                                && first.body == second.body
                                && first.path == second.path
                                && first.distanceKm > 0f
                                && first.path.Contains("PEK") && first.path.Contains("JFK")
                                && first.subject.Contains(first.path)
                                && first.body.Contains("各部门讨论意见")
                                && first.body.Contains("请示事项")
                                && first.rpm.score > 0;

                // ④ 经停口径：直飞 = 大圆距离；经停 = 两段之和（不许两套距离算法）
                //    经停点必须**真的是中间点**（SVO 在 PEK—JFK 中间），否则「两段之和」
                //    会退化成一段 —— 那种夹具看起来很严谨，其实什么都没测。
                float direct = OpenRouteStudyLogic.DistanceKm("PEK", "JFK", null);
                float viaKm = OpenRouteStudyLogic.DistanceKm("PEK", "JFK", "SVO");
                bool distOk = direct > 0f && viaKm > direct
                              && viaKm > 1.15f * direct
                              && Mathf.Abs(direct - AirportGeoCatalog.GreatCircleKm("PEK", "JFK")) < 0.01f;

                // ⑤ UI 侧没有第二个正文作者
                const BindingFlags any =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                bool noSecondAuthor =
                    typeof(OpenRouteDecisionFlow).GetMethod("BuildStudyMailBody", any) == null
                    && typeof(OpenRouteDecisionFlow).GetMethod("FormatPath", any) == null;
                bool stillHasEntry = typeof(OpenRouteDecisionFlow).GetMethod("SubmitStudy", any) != null;

                // ⑥ 最优时刻：时段必须挑「又旺又便宜」的晚高峰（需求 1.10），而不是旧口径钉死的 12:00；
                //    也不能被最便宜的夜航 22:00 抢走 —— 夜航需求 0.80 最淡，用「省钱」换来一条
                //    不赚钱的班次不是最优。
                //
                //    ★2026-11-07 修订：不能再钉死「推荐时刻 == 18:00」。他航逐刻占用计入容量后，
                //    晚高峰峰点 18:00 在 PEK 常年被他航占满（此时「可申请 0 条」），
                //    部门若仍报 18:00，CEO 批准后交割必然失败 —— 那正是用户报障的那类错。
                //    现在部门会**换刻**到本段最近的空刻，并在理由里写明为什么换。
                //    故这里改为钉三条更硬的约束：
                //      ① 推荐时刻此刻**真的能申请**（SlotsAvailableAt ≥ 1）——旧口径不查这一条；
                //      ② 仍落在晚高峰（最旺、且不是最便宜的夜航）；
                //      ③ 若离开峰点，理由必须把「峰点已满、改报某刻」写出来，不许悄悄换。
                var pick = RoutePermitLogic.TryBestSlotClock(gs, "PEK", 5000f);
                bool pickClaimable = pick.ok
                                     && RoutePermitLogic.SlotsAvailableAt(gs, "PEK", pick.clock) >= 1;
                bool pickMovedExplained = string.Equals(pick.clock, "18:00", System.StringComparison.Ordinal)
                                          || (pick.reason.Contains("已被他航占满")
                                              && pick.reason.Contains(pick.clock));
                bool pickOk = pickClaimable && pick.best && pick.band == 2
                              && pick.demand > 1f && pick.price > 0f
                              && pick.reason.Contains("需求系数")
                              && pickMovedExplained;

                // ⑦ 「最优」必须是**买得起的**最优：现金 1 万时仍推荐同一时段，
                //    但正文必须把「钱不够」写出来，不许把买不起的方案当可行方案递上去。
                var poor = RoutePermitLogic.TryBestSlotClock(gs, "PEK", 1f);
                bool poorOk = poor.ok && poor.price > 1f && poor.reason.Contains("不足以买入");

                // ⑧ 前置条件必须写在签字页上：航权 / 时刻 / 全链条现金，
                //    而不是藏在「批准后要办的事」里让 CEO 签完才发现还缺东西。
                bool frontLoaded = first.body.Contains("开航前置条件")
                                   && first.body.Contains("航权")
                                   && first.body.Contains("时刻")
                                   && first.body.Contains("全链条现金");

                // ⑨ 前置条件要落在**签字前的最后一节**：排在「批准后的准入办理」之后、「请示事项」之前。
                //    2026-11-03 二次报障：「『开航前置条件』不在邮件底部」——清单夹在部门意见与办理流程
                //    中间时，CEO 一路往下读会以为后面还有正文，落笔前反而没看到该看的清单。
                int iGate = first.body.IndexOf("开航前置条件", System.StringComparison.Ordinal);
                int iPermit = first.body.IndexOf("批准后的准入办理", System.StringComparison.Ordinal);
                int iAsk = first.body.IndexOf("请示事项", System.StringComparison.Ordinal);
                bool gateAtBottom = iPermit >= 0 && iGate > iPermit && iAsk > iGate;

                // ⑩ 到件时重算（2026-11-03 用户问询：「邮件时间是提案时间吗？」）：
                //    研究会要开 2 个工作日，正文里的「当前现金」却写在下达指令那一刻。
                //    到件时必须按当天的账重写 —— 否则 CEO 签的是一封「两天前余额」的信。
                bool refreshOk;
                string refreshNote;
                var go2 = new GameObject("__open_route_refresh_gs__");
                try
                {
                    var gsR = go2.AddComponent<GameState>();
                    gsR.year = 1; gsR.quarter = 1;
                    gsR.cash = 5000f;
                    gsR.inbox = new List<InboxMail>();
                    gsR.pendingInbox = new List<PendingInboxMail>();
                    gsR.activeRoutes = new List<RouteData>();
                    gsR.fleet = new List<Aircraft>();

                    var draft = OpenRouteStudyLogic.Build(gsR, "PEK", "JFK", null);
                    bool queued = InboxManager.QueueOpenRouteStudy(gsR, "PEK", "JFK", null,
                        draft.subject, draft.body);
                    bool saidRich = draft.body.Contains("当前 5000 万");

                    gsR.cash = 120f;                                   // 两天里现金被别的事花掉了
                    InboxManager.DeliverDue(gsR, gsR.SimUtc.AddDays(30));

                    var landed = gsR.inbox.Count == 1 ? gsR.inbox[0] : null;
                    bool saidPoor = landed != null && landed.body.Contains("当前 120 万")
                                    && landed.body.Contains("差");
                    refreshOk = queued && saidRich && saidPoor;
                    refreshNote = queued
                        ? $"下达时 5000 万 → 到件时 120 万，正文改口={saidPoor}"
                        : "没能排进待发队列";
                }
                finally { GameObject.DestroyImmediate(go2); }

                bool all = rejectOk && rejectNoMail && sameMemo && distOk && noSecondAuthor && stillHasEntry
                           && pickOk && poorOk && frontLoaded && gateAtBottom && refreshOk;

                Debug.Log($"开航纪要单一出处 → 先否决后算账={rejectOk}（{Trim(bad.reject, 40)}）"
                          + $"　否决不投递={rejectNoMail}"
                          + $"　同输入同纪要={sameMemo}（{first.path} · {first.distanceKm:F0} km · {first.rpm.score} 分）"
                          + $"　距离口径一致={distOk}（直飞 {direct:F0} / 经停 {viaKm:F0}）"
                          + $"　UI 无第二作者={noSecondAuthor}　页面入口仍在={stillHasEntry}"
                          + $"　最优时刻={pickOk}（{pick.clock} 需求 {pick.demand:F2} / {pick.price:F0} 万）"
                          + $"　买不起要说清={poorOk}"
                          + $"　前置条件前置={frontLoaded}"
                          + $"　清单在签字前={gateAtBottom}"
                          + $"　到件重算={refreshOk}（{refreshNote}）"
                          + $"　{(all ? "✓" : "✗")}");
                return all;
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        static string Trim(string s, int max)
        {
            return RegressionTriggerBridge.Trim(s, max);
        }
    }
}
#endif
