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

                bool all = rejectOk && rejectNoMail && sameMemo && distOk && noSecondAuthor && stillHasEntry;

                Debug.Log($"开航纪要单一出处 → 先否决后算账={rejectOk}（{Trim(bad.reject, 40)}）"
                          + $"　否决不投递={rejectNoMail}"
                          + $"　同输入同纪要={sameMemo}（{first.path} · {first.distanceKm:F0} km · {first.rpm.score} 分）"
                          + $"　距离口径一致={distOk}（直飞 {direct:F0} / 经停 {viaKm:F0}）"
                          + $"　UI 无第二作者={noSecondAuthor}　页面入口仍在={stillHasEntry}"
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
