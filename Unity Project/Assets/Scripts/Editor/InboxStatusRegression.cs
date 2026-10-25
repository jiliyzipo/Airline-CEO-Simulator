#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// T05.2「延迟与状态提示」的隔离回归（2026-10-24）。
    ///
    /// 旧版三个缺口（见 `docs/handoff/残余七项审计_2026-10-24.md` §4）：
    /// ① **在途送达无任何 UI**：`gs.pendingInbox` 全文只有 Systems/Editor 读取，
    ///    UI 层零命中。部门说「约 2 小时后送达」，收件箱一动不动，看起来像没生效。
    /// ② **「已批准待交割」与「已办结」不可区分**：`StatusLabel("approved")` 一律返回「已批准」，
    ///    收件箱列表与详情标签完全相同。
    /// ③ **没有统一状态行**：六阶段的「在等什么 / 何时推进 / 是否需 CEO」散落在命令条回执、
    ///    详情页底注与公文正文里，玩家必须读完整封信才知道这封是在等自己还是等别人。
    ///
    /// 本回归钉住 ①②③ 的数据口径。UI 是否真的把这些字画到屏幕上，仍属界面手测范围
    /// （本文档不宣称已完成，见任务书 T05.2 备注）。
    /// </summary>
    public static class InboxStatusRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__inbox_status__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.year = 1;
                gs.quarter = 1;
                gs.SetSimUtc(new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc));

                bool all = true;

                // ① 标签：已批准 + 尚未交割 → 「已批准·待交割」
                var pending = new InboxMail
                {
                    id = "T052-A", sender = "运营部", role = "时刻槽提案",
                    subject = "候补", body = "", status = "approved",
                    action = "slot_select", effectPending = true, effectYear = 1, effectQuarter = 2
                };
                string tagFuture = InboxManager.StatusLabel(gs, pending);
                bool tagFutureOk = tagFuture == "已批准·待交割";
                all &= tagFutureOk;
                if (!tagFutureOk) Debug.LogError($"T05.2-① 待交割标签不符：得到「{tagFuture}」期望「已批准·待交割」");

                // ①b 交割季已到 → 「已批准·交割中」（不能再叫「待交割」）
                gs.quarter = 2;
                string tagNow = InboxManager.StatusLabel(gs, pending);
                bool tagNowOk = tagNow == "已批准·交割中";
                all &= tagNowOk;
                if (!tagNowOk) Debug.LogError($"T05.2-①b 交割中标签不符：得到「{tagNow}」期望「已批准·交割中」");
                gs.quarter = 1;

                // ①c 已办结（effectPending=false）→ 仍是「已批准」，不得带「待交割」
                var done = new InboxMail
                {
                    id = "T052-B", sender = "运营部", role = "执行回执",
                    subject = "已办结", body = "", status = "approved", action = "slot_select",
                    effectPending = false
                };
                string tagDone = InboxManager.StatusLabel(gs, done);
                bool tagDoneOk = tagDone == "已批准";
                all &= tagDoneOk;
                if (!tagDoneOk) Debug.LogError($"T05.2-①c 已办结标签不符：得到「{tagDone}」期望「已批准」");

                // ② 统一状态行：三件事齐备
                var open = new InboxMail
                {
                    id = "T052-C", sender = "运营部", role = "呈报",
                    subject = "待批", body = "", status = "pending", action = "slot_select",
                    receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 2
                };
                string lineOpen = InboxManager.StatusLine(gs, open);
                bool lineOpenOk = lineOpen.Contains("在等")
                                  && lineOpen.Contains("CEO 批复")
                                  && lineOpen.Contains("何时推进")
                                  && lineOpen.Contains("需 CEO　是");
                all &= lineOpenOk;
                if (!lineOpenOk) Debug.LogError($"T05.2-② 待批状态行不符：「{lineOpen}」");

                string linePending = InboxManager.StatusLine(gs, pending);
                bool linePendingOk = linePending.Contains("交割")
                                     && linePending.Contains("第1年 Q2")
                                     && linePending.Contains("需 CEO　否");
                all &= linePendingOk;
                if (!linePendingOk) Debug.LogError($"T05.2-②b 待交割状态行不符：「{linePending}」");

                string lineDone = InboxManager.StatusLine(gs, done);
                bool lineDoneOk = lineDone.Contains("已办结") && lineDone.Contains("需 CEO　否");
                all &= lineDoneOk;
                if (!lineDoneOk) Debug.LogError($"T05.2-②c 已办结状态行不符：「{lineDone}」");

                var news = new InboxMail
                {
                    id = "T052-D", sender = "中央气象台", role = "预警通报",
                    subject = "台风", body = "", status = "approved", action = "news",
                    kind = "official", tag = "预警", effectPending = false
                };
                string lineNews = InboxManager.StatusLine(gs, news);
                bool lineNewsOk = lineNews.Contains("外部通报") && lineNews.Contains("需 CEO　否");
                all &= lineNewsOk;
                if (!lineNewsOk) Debug.LogError($"T05.2-②d 外部通报状态行不符：「{lineNews}」");

                // ③ 在途送达：空列表不显示；有 2 封时说明封数与最近一封的预计到达
                bool emptyBefore = string.IsNullOrEmpty(InboxManager.InflightLine(gs));
                all &= emptyBefore;
                if (!emptyBefore) Debug.LogError($"T05.2-③ 空列表不应有在途提示：「{InboxManager.InflightLine(gs)}」");
                gs.pendingInbox.Add(new PendingInboxMail
                {
                    deliverUtcTicks = gs.SimUtc.AddHours(2).Ticks,
                    mail = new InboxMail { id = "P1", sender = "运营部", subject = "呈报" }
                });
                gs.pendingInbox.Add(new PendingInboxMail
                {
                    deliverUtcTicks = gs.SimUtc.AddHours(6).Ticks,
                    mail = new InboxMail { id = "P2", sender = "战略发展部", subject = "复核" }
                });
                string inflight = InboxManager.InflightLine(gs);
                bool inflightOk = !string.IsNullOrEmpty(inflight)
                                  && inflight.Contains("在途 2 封")
                                  && inflight.Contains("小时")
                                  && inflight.Contains("送达");
                all &= inflightOk;
                if (!inflightOk) Debug.LogError($"T05.2-③ 在途提示不符：空列表时为空={emptyBefore} 文案「{inflight}」");

                // ③b 已到期未投递 → 「正在送达」，不能显示「约 0 小时后送达」
                gs.pendingInbox[0].deliverUtcTicks = gs.SimUtc.AddMinutes(-5).Ticks;
                string overdue = InboxManager.InflightLine(gs);
                bool overdueOk = overdue.Contains("正在送达");
                all &= overdueOk;
                if (!overdueOk) Debug.LogError($"T05.2-③b 逾期在途不符：「{overdue}」");

                Debug.Log($"T05.2 状态提示 → 待交割标签={tagFutureOk}（{tagFuture}）　交割中={tagNowOk}（{tagNow}）"
                          + $"　已办结={tagDoneOk}（{tagDone}）\n"
                          + $"　待批状态行={lineOpenOk}：「{lineOpen}」\n"
                          + $"　待交割状态行={linePendingOk}：「{linePending}」\n"
                          + $"　在途提示={inflightOk}：「{inflight}」（逾期→「{overdue}」）");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
