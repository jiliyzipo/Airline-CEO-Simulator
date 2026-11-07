using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 流程节点图回归（2026-11-02 CEO：「汇报邮件要写明事项流程及其所有节点、已完成哪个、后续还有哪些」）。
    ///
    /// 报障形态：一封「时刻槽已交割」的回执只写了一句话，
    /// CEO 读完不知道这件事一共几关、走到哪一关、下一关归谁。
    ///
    /// 本用例钉四件事：
    /// <list type="number">
    /// <item>图里**列出全部节点**，顺序与编号不乱，五种状态各有标记；</item>
    /// <item>许可受理回执给出六关全貌（含与许可并行的第 6 关开航）；</item>
    /// <item>时刻交割回执给出五关，且**本次交割的那一关**是「✓ 已完成」而不是「待办」；</item>
    /// <item>节点进度件里，**已办结的那一关是 ✓、下一关是 ▶**，不能两关都写成「进行中」。</item>
    /// </list>
    /// </summary>
    public class ProcessFlowEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__flow__");
            var gs = host.AddComponent<GameState>();
            gs.year = 1;
            gs.quarter = 1;
            gs.simDay = 1;
            gs.cash = 90000f;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            StrategyManager.EnsureState(gs);
            return gs;
        }

        static InboxMail NewPermitMail(string o, string d)
            => new InboxMail
            {
                id = "m_open_" + o + "_" + d,
                sender = "运营部",
                role = "准入办理",
                glyph = "运",
                kind = "regulatory",
                action = "open_route",
                origin = o,
                dest = d,
                subject = $"开通 {o}-{d}",
                body = "",
                status = "pending",
                receivedYear = 1,
                receivedQuarter = 1,
                permitAttempts = 1,
                time = "09:00"
            };

        static InboxMail FindByTag(GameState gs, string tag, string subjectPart = null)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.tag != tag) continue;
                if (subjectPart == null) return m;
                if (!string.IsNullOrEmpty(m.subject)
                    && m.subject.IndexOf(subjectPart, System.StringComparison.Ordinal) >= 0) return m;
            }
            return null;
        }

        [Test]
        public void Render_ListsEveryNodeInOrder_WithStateMarks()
        {
            var nodes = new List<FlowNode>
            {
                new FlowNode("受理立案", FlowState.Done, "11月03日"),
                new FlowNode("材料与运力审查", FlowState.Done, "11月08日"),
                new FlowNode("航权额度与时刻核验", FlowState.Active, "11月14日（还剩 6 天）"),
                new FlowNode("局方公示", FlowState.Pending, "预计 11月20日"),
                new FlowNode("时刻与开航", FlowState.Skipped, "非协调机场，无需时刻")
            };
            string text = ProcessFlow.Render("办理流程", nodes);

            int last = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                int at = text.IndexOf(nodes[i].name, System.StringComparison.Ordinal);
                Assert.Greater(at, last, $"节点「{nodes[i].name}」缺失或顺序不对：\n{text}");
                last = at;
            }

            StringAssert.Contains("① ", text);
            StringAssert.Contains("⑤ ", text);
            StringAssert.Contains("✓ 已完成", text);
            StringAssert.Contains("▶ 进行中", text);
            StringAssert.Contains("○ 待办", text);
            StringAssert.Contains("— 无需办理", text);
            StringAssert.Contains("共 5 个节点：已完成 2 · 进行中 1 · 待办 1 · 无需办理 1", text);
            StringAssert.Contains("还剩 6 天", text);
        }

        [Test]
        public void PermitAdmitMail_ShowsAllNineNodes()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "SIN");
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);
                InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));

                var admit = FindByTag(gs, "受理");
                Assert.IsNotNull(admit, "受理回执未生成");
                string body = admit.body ?? "";

                StringAssert.Contains("办理流程（准入 → 开航 → 运营）", body);
                // ★2026-11-04 用户要求补全「从零到航班运行」：六关扩到九关（第 7 开航执行 /
                //   第 8 航班计划编排 / 第 9 投入运营）。这条断言必须跟着数据源走，
                //   写死 6 会在扩关后误报「流程缺关」——它测的是**图上列全没列全**，不是某个数字。
                StringAssert.Contains($"共 {RoutePermitLogic.NodeLast} 个节点", body);
                Assert.AreEqual(9, RoutePermitLogic.NodeLast, "节点数应为 9：五道许可关 + 时刻 + 开航执行 + 编班 + 运营");
                for (int node = RoutePermitLogic.NodeAccept; node <= RoutePermitLogic.NodeLast; node++)
                    StringAssert.Contains(RoutePermitLogic.NodeName(node), body);
                StringAssert.Contains(RoutePermitLogic.NodeName(RoutePermitLogic.NodeSlot), body);

                // 受理当天：第一关已办（①），第二关进行中，其余待办
                FlowLineHas(body, "①", RoutePermitLogic.NodeName(RoutePermitLogic.NodeAccept));
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeAccept), "✓ 已完成");
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeReview), "▶ 进行中");
                StringAssert.Contains("○ 待办", body);

                // 发证后三关此刻必须是**待办**：许可还没批，不能提前显示「已开航/已运营」
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeLaunch), "○ 待办");
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeSchedule), "○ 待办");
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeOperate), "○ 待办");
                Assert.IsFalse(FlowLineOf(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeOperate))
                                   .Contains("已完成"),
                    "许可尚未发证，第 9 关（投入运营）就显示已完成了");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NodeProgressMail_MarksDoneNodeDone_AndNextNodeActive()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "CTU");    // 国内协调：21 天，第 2 关在第 ~3 天
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);

                int reviewDay = PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeReview, permit.permitTotalDays);
                gs.SetSimUtc(gs.SimUtc.AddDays(reviewDay + 1));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));

                var node = FindByTag(gs, "进度", "准入进度");
                Assert.IsNotNull(node, "节点进度件未生成");
                string body = node.body ?? "";

                StringAssert.Contains("本次办结", body);
                int doneAt = body.IndexOf("✓ 已完成", System.StringComparison.Ordinal);
                int activeAt = body.IndexOf("▶ 进行中", System.StringComparison.Ordinal);
                Assert.Greater(doneAt, 0, $"图里没有「✓ 已完成」：\n{body}");
                Assert.Greater(activeAt, 0, $"图里没有「▶ 进行中」：\n{body}");

                // 已办结的那一关必须挂在 ✓ 上，不能还写成进行中
                int reviewAt = body.IndexOf(RoutePermitLogic.NodeName(RoutePermitLogic.NodeReview), System.StringComparison.Ordinal);
                Assert.Greater(reviewAt, 0);
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeReview), "✓ 已完成");
                FlowLineHas(body, RoutePermitLogic.NodeName(RoutePermitLogic.NodeRights), "▶ 进行中");
                Assert.Less(doneAt, activeAt, "「已完成」应出现在「进行中」之前（链自上而下）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SlotSettleReceipt_ShowsFiveNodes_WithDeliveryDone()
        {
            var gs = NewGame(out var host);
            try
            {
                Assert.IsTrue(FindFreeSlot(gs, out string apt, out string clock),
                    "夹具失败：找不到有余量的协调机场时刻（别把夹具失效当成通过）");

                Assert.IsTrue(InboxManager.QueueApplySlots(gs, apt, clock, 1, null, out string qm), qm);
                InboxMail order = null;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) order = gs.inbox[i];
                Assert.IsNotNull(order);
                Assert.IsTrue(InboxManager.Approve(gs, order, out string am), am);

                gs.SetSimUtc(gs.SimUtc.AddDays(PermitTimeline.SlotPoolOffWindowDays + 1));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));

                var receipt = FindByTag(gs, "回执", "时刻槽已交割");
                Assert.IsNotNull(receipt, "交割回执未生成");
                string body = receipt.body ?? "";

                StringAssert.Contains("办理流程（时刻槽购置）", body);
                StringAssert.Contains("共 5 个节点", body);
                StringAssert.Contains("发现缺口与提案", body);
                StringAssert.Contains("CEO 批准购置", body);
                StringAssert.Contains("交割入册", body);
                StringAssert.Contains("可用于排班", body);
                StringAssert.Contains("协调人分配", body);       // 免费渠道的正确措辞

                FlowLineHas(body, "交割入册", "✓ 已完成");
                Assert.IsFalse(FlowLineOf(body, "交割入册").Contains("○ 待办"), "「交割入册」这一关不该是待办");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 同一季度内走过两个节点 → 两封进度件都要送达。
        ///
        /// 旧 id 是「年+季」，<c>Schedule</c> 按 id 判重时会把第二封直接丢掉；
        /// 节点改为按天推进后，一季走 2–3 关是常态，这个「静默吞信」会变成常态性缺陷。
        /// </summary>
        [Test]
        public void TwoNodeReceiptsInOneQuarter_BothDelivered()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "CTU");   // 国内协调 21 天，两个节点都在 Q1 内
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);

                int day2 = PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeReview, permit.permitTotalDays);
                gs.SetSimUtc(gs.SimUtc.AddDays(day2 + 1));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));

                int day3 = PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeRights, permit.permitTotalDays);
                gs.SetSimUtc(gs.SimUtc.AddDays(Mathf.Max(1, day3 - day2)));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));

                int count = 0;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].tag == "进度") count++;
                Assert.GreaterOrEqual(count, 2,
                    $"一季内走过两关，进度件却只收到 {count} 封（第二封多半被 id 判重吞掉了）");
                Assert.AreEqual(1, gs.year, "本用例要求两关都落在同一年内");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 取**流程图里**包含 <paramref name="keyword"/> 的那一整行（判断某一关的状态标记用）。
        /// 只在「▎办理流程」之后找：节点名同时出现在自我介绍、正文要点里，
        /// 直接全文找会命中第一处不相干的行（自测里已经踩过一次）。
        /// </summary>
        static string FlowLineOf(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int begin = text.IndexOf("▎办理流程", System.StringComparison.Ordinal);
            string scope = begin < 0 ? text : text.Substring(begin);
            int at = scope.IndexOf(keyword, System.StringComparison.Ordinal);
            if (at < 0) return "";
            int start = scope.LastIndexOf('\n', at);
            start = start < 0 ? 0 : start + 1;
            int end = scope.IndexOf('\n', at);
            if (end < 0) end = scope.Length;
            return scope.Substring(start, end - start);
        }

        static void FlowLineHas(string text, string keyword, string mustContain)
            => StringAssert.Contains(mustContain, FlowLineOf(text, keyword));

        /// <summary>找一个协调机场 + 有余量的时刻（免费渠道不看现金，池内有余量即可）。</summary>
        static bool FindFreeSlot(GameState gs, out string airport, out string clock)
        {
            airport = null;
            clock = null;
            string[] candidates = { "PEK", "PVG", "CAN", "SZX", "CTU", "SIN", "CDG", "FRA", "LHR", "JFK" };
            for (int c = 0; c < candidates.Length; c++)
            {
                if (!RoutePermitLogic.NeedsDepartureSlot(candidates[c])) continue;
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    if (RoutePermitLogic.SlotsAvailable(gs, candidates[c], b) < 1) continue;
                    // ★2026-11-06：不能再拿网格第 0 个 —— 全天网格是 00:00 起的，
                    // 而 00:00–05:45 在这张容量表上没有槽，递单能过、交割会取不到。
                    // ★2026-11-07：也不能只拿「第一个公布刻」—— 他航逐刻占用计入容量后，
                    // 段内第一个刻经常正是被他航占满的那一个（占用从段首铺开），
                    // 于是夹具挑到一个「段有余量、刻级为 0」的时刻，交割必然失败。
                    var grid = ScheduleSlots.BandGridClaimableLabels(candidates[c], b);
                    if (grid == null || grid.Count == 0) continue;
                    for (int i = 0; i < grid.Count; i++)
                    {
                        if (RoutePermitLogic.SlotsAvailableAt(gs, candidates[c], grid[i]) < 1) continue;
                        airport = candidates[c];
                        clock = grid[i];
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
