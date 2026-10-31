using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// T03.8「CEO 走查清单」中**机器可判定**的部分 + T05.7「1× 实时节奏」的实测证据。
    ///
    /// 分工说明（写在这里是为了不再把两者混谈）：
    /// · 本文件覆盖：关键词落点页、返回后仍停在同一封信、批准后按钮消失且不串信、回执只在 4 游戏小时后送达、
    ///   一趟往返恰好两段循环、列表页/单航线页/公式三处数字一致、地图线宽的档位函数、1× 与现实的换算。
    /// · 仍然只能人眼的：字号观感与截断、线宽肉眼能否分辨、真人手速下点得到点不到。
    ///   这些在 -nographics 下没有渲染结果，任何断言都只是自欺。
    /// </summary>
    public class T038UiAcceptancePlayModeTests : IsolatedPlayModeFixture
    {
        readonly List<GameObject> _owned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            TimeManager.SimulationEnabled = false;
            FleetNetContext.OpenRightsTab = false;
            FleetNetContext.SelectedRouteId = null;
            OpsSubpageContext.SelectedRouteId = null;
            for (int i = 0; i < _owned.Count; i++)
                if (_owned[i] != null) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            MapWidget.SetRoutesVisible(false);   // 图层开关是 static，别把状态泄给下一条用例
            MapSelection.Reset();                // 选中状态同理
        }

        void RequireBaseline()
        {
            if (Application.companyName != "AirlineCEORegression")
                Assert.Ignore("Run in the isolated baseline project only.");
        }

        GameObject Own(GameObject go) { _owned.Add(go); return go; }

        GameState NewState(string name)
        {
            RequireBaseline();
            var gs = Own(new GameObject(name)).AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1; gs.cash = 100000f;
            gs.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
            gs.simDay = 1;
            gs.inbox.Clear(); gs.pendingInbox.Clear();
            gs.activeRoutes.Clear(); gs.fleet.Clear();
            gs.stg = new StrategyState();
            return gs;
        }

        static RouteData MakeRoute(string routeId, string registration, string dep = "08:00", string arr = "10:30")
        {
            var r = new RouteData
            {
                routeId = routeId, origin = "PEK", destination = "SHA", distanceKm = 1100f,
                aircraftType = "A320neo", flightNumber = "TQ380", registration = registration,
                depTime = dep, arrTime = arr, daysOfWeek = "每日", weeklyFlights = 7,
                baseDailyProfit = 12.34f, dailyProfit = 12.34f, groundStaffAssigned = 3
            };
            r.SyncFlightsFromPrimary();
            return r;
        }

        static Aircraft MakeAircraft(string registration) => new Aircraft
        {
            registration = registration, type = "A320neo", isActive = true,
            status = AircraftStatuses.Airworthy, age = 3, condition = 100f
        };

        static IEnumerable<TextMeshProUGUI> Labels(GameObject root)
            => root.GetComponentsInChildren<TextMeshProUGUI>(true);

        static TextMeshProUGUI LabelContaining(GameObject root, params string[] tokens)
            => Labels(root).FirstOrDefault(t => t != null && !string.IsNullOrEmpty(t.text)
                && tokens.All(t.text.Contains));

        static TextMeshProUGUI LabelNamed(GameObject root, string name)
            => Labels(root).FirstOrDefault(t => t != null && t.name == name);

        /// <summary>取「token 后面的那个数字」，跳过全角空格。F1/F2 在 zh-CN 下仍是 '.' 小数点。</summary>
        static float NumberAfter(string text, string token)
        {
            int at = text.IndexOf(token, StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, $"文案里找不到「{token}」：{text}");
            int i = at + token.Length;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\u3000')) i++;
            int start = i;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == '-' || text[i] == '+')) i++;
            string raw = text.Substring(start, i - start);
            Assert.IsTrue(float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value),
                $"「{token}」后面不是数字：'{raw}'（原文 {text}）");
            return value;
        }

        PrefabNavIntegration BuildShell(string canvasName, List<InboxMail> mails)
        {
            var gs = GameState.Instance;
            gs.inbox.Clear();
            foreach (var m in mails) gs.inbox.Add(m);
            var canvas = Own(new GameObject(canvasName, typeof(Canvas)));
            var nav = canvas.AddComponent<PrefabNavIntegration>();
            nav.Attach();
            nav.EnterGameplay();
            nav.SelectTab("n7_收件箱");
            return nav;
        }

        static Button Row(InboxPanelUI inbox, string mailId)
            => inbox.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Row_" + mailId);

        static Button ApproveButton(InboxPanelUI inbox)
            => inbox.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Approve");

        // —————————————————————————————————————————————————————————————
        // ① 关键词落点页：邮件正文里点了词，应当去哪一页
        // —————————————————————————————————————————————————————————————

        [TestCase("page:n43_航班详细计划", "n43_航班详细计划")]
        [TestCase("page:n35_新航线_RPM_评估台", "n35_新航线_RPM_评估台")]
        [TestCase("page:n20_董事会", "n20_董事会")]
        [TestCase("page:n77_航权与时刻槽", "n77_航权与时刻槽")]
        [TestCase("city:PEK", "n12_航线网络")]
        [TestCase("aircraft:B-3100", "n10_飞机详情_履历")]
        [TestCase("ac:B-3100", "n10_飞机详情_履历")]
        [TestCase("airline:3U", "n61_竞对详情")]
        public void KeywordLink_ResolvesToDocumentedPage(string linkId, string pageId)
        {
            Assert.IsTrue(TermNavMap.TryResolve(linkId, out var hit), linkId + " 解不出落点页");
            Assert.AreEqual(pageId, hit.pageId);
        }

        [Test]
        public void KeywordLink_RouteAndFlightCarryTheirTargetKeys()
        {
            Assert.IsTrue(TermNavMap.TryResolve("route:PEK-SHA", out var route));
            Assert.AreEqual("n41_单航线", route.pageId);
            Assert.AreEqual("PEK-SHA", route.routeKey);
            Assert.AreEqual(-1, route.flightIndex, "航线链接不带班次下标，落地后停在该线总览");

            Assert.IsTrue(TermNavMap.TryResolve("flight:TQ380#2", out var flight));
            Assert.AreEqual("n43_航班详细计划", flight.pageId);
            Assert.AreEqual("TQ380", flight.routeKey);
            Assert.AreEqual(2, flight.flightIndex, "航班号链接必须带波次下标，否则会落到第 1 班");
        }

        [Test]
        public void KeywordBody_TurnsDocumentedTermsIntoClicks()
        {
            string body = TermNavMap.Decorate("请到航权与时刻槽确认，再看航班详细计划与 RPM 评估。");
            StringAssert.Contains("<link=page:n77_航权与时刻槽>", body);
            StringAssert.Contains("<link=page:n43_航班详细计划>", body);
            StringAssert.Contains("<link=page:n35_新航线_RPM_评估台>", body);
        }

        // —————————————————————————————————————————————————————————————
        // ② 真实收件箱面板：n77 深链改投航线页签
        //
        // 2026-10-25 回归抓到的缺陷：n12 首次建页时 NetworkPanelUI.OnEnable 先消费掉
        // OpenRightsTab 并切到页签 1，紧接着 Build() 里的 BuildContent 末尾 SetTab(0)
        // 又把它抹回「航线总览」；而 JumpTo 的补救分支以 `&& OpenRightsTab` 为条件，
        // 标志此时已被消费 → 本次深链整段丢失。真机表现是「第一次从收件箱点时刻槽，
        // 落地停在航线总览」，n12 之前访问过才正常。
        // —————————————————————————————————————————————————————————————

        [UnityTest]
        public IEnumerator SlotKeyword_OpensNetworkRightsTab_NotAStandalonePage()
        {
            NewState("T038SlotNav");
            var mail = new InboxMail
            {
                id = "t038-slot", sender = "战略发展部", role = "时刻槽", glyph = "战",
                subject = "时刻槽待批", body = "请到航权与时刻槽页面确认。",
                action = "notice", kind = "report", status = "pending",
                receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 2
            };
            var nav = BuildShell("T038SlotCanvas", new List<InboxMail> { mail });
            yield return null;

            var inbox = UnityEngine.Object.FindFirstObjectByType<InboxPanelUI>();
            Assert.IsNotNull(inbox, "收件箱面板没建起来");

            var row = Row(inbox, mail.id);
            Assert.IsNotNull(row, "邮件行没出现");
            row.onClick.Invoke();
            yield return null;

            var body = LabelContaining(inbox.gameObject, "航权与时刻槽");
            Assert.IsNotNull(body, "正文没有把「航权与时刻槽」渲染出来");
            StringAssert.Contains("link=page:n77", body.text, "关键词必须是可点的 link");

            var click = inbox.GetComponentInChildren<InboxLinkClick>(true);
            Assert.IsNotNull(click, "正文没有挂点击回调");
            click.onLink("page:n77_航权与时刻槽");

            Assert.AreEqual("n12_航线网络", nav.Router.CurrentPageId,
                "n77 已并入航线模块，深链必须改投 n12，不能落到一个不存在的页");
            Assert.IsFalse(FleetNetContext.OpenRightsTab,
                "这个标志是一次性消费：面板读到它就该清掉，留着会让下次进 n12 又跳页签");
            var network = UnityEngine.Object.FindFirstObjectByType<NetworkPanelUI>();
            Assert.IsNotNull(network, "n12 页面没有建起来");
            var tabField = typeof(NetworkPanelUI).GetField("_tab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(tabField, "NetworkPanelUI._tab 改名了，请同步本用例");
            Assert.AreEqual(1, (int)tabField.GetValue(network),
                "落地后应停在「航权与时刻槽」页签（下标 1）。若为 0，说明深链被 Build 里的 SetTab(0) 抹掉了");
            yield return null;
        }

        // —————————————————————————————————————————————————————————————
        // ③ 返回：回到收件箱，且还停在同一封信上
        // —————————————————————————————————————————————————————————————

        [UnityTest]
        public IEnumerator BackFromRoutePage_ReturnsToInbox_WithTheSameMailStillOpen()
        {
            var gs = NewState("T038Back");
            var route = MakeRoute("T038-BACK", "B-3801");
            gs.activeRoutes.Add(route);
            gs.fleet.Add(MakeAircraft("B-3801"));

            var mail = new InboxMail
            {
                id = "t038-back", sender = "运营部", role = "航线", glyph = "运",
                subject = "T038 返回测试", body = "T038BODY 请查看 PEK-SHA 的运力。",
                action = "notice", kind = "report", status = "pending",
                receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 2
            };
            var nav = BuildShell("T038BackCanvas", new List<InboxMail> { mail });
            yield return null;

            var inbox = UnityEngine.Object.FindFirstObjectByType<InboxPanelUI>();
            Assert.IsNotNull(inbox);
            Row(inbox, mail.id).onClick.Invoke();
            yield return null;

            const string bodyText = "T038BODY";
            Assert.IsNotNull(LabelContaining(inbox.gameObject, bodyText),
                "点开邮件后正文应显示出来（列表行不带正文，故此断言只在详情页成立）");

            var click = inbox.GetComponentInChildren<InboxLinkClick>(true);
            Assert.IsNotNull(click);
            click.onLink("route:T038-BACK");
            Assert.AreEqual("n41_单航线", nav.Router.CurrentPageId, "航线链接应落到单航线页");
            Assert.AreEqual("T038-BACK", OpsSubpageContext.SelectedRouteId);

            Assert.IsTrue(nav.Router.Back(), "单航线页应当能返回");
            Assert.AreEqual("n7_收件箱", nav.Router.CurrentPageId, "返回必须回到收件箱");
            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<InboxPanelUI>(),
                "返回后收件箱面板不应被销毁");
            Assert.IsNotNull(LabelContaining(inbox.gameObject, bodyText),
                "返回后应仍停在那封信的正文上，而不是退回列表");
            yield return null;
        }

        // —————————————————————————————————————————————————————————————
        // ④ 批准：按钮消失、不重复扣钱；换一封信不得留有上一封的提示
        // —————————————————————————————————————————————————————————————

        [UnityTest]
        public IEnumerator Approve_HidesButton_AndSwitchingMailLeavesNoStaleHint()
        {
            var gs = NewState("T038Approve");
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "PEK", "18:00", 1, "t038-a", out string noteA), noteA);
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "SHA", "12:00", 1, "t038-b", out string noteB), noteB);
            var mailA = gs.inbox.First(m => m.id == "t038-a");

            BuildShell("T038ApproveCanvas", gs.inbox.ToList());
            yield return null;

            var inbox = UnityEngine.Object.FindFirstObjectByType<InboxPanelUI>();
            Assert.IsNotNull(inbox);
            Row(inbox, "t038-a").onClick.Invoke();
            yield return null;

            var hint = LabelNamed(inbox.gameObject, "Hint");
            Assert.IsNotNull(hint, "详情页缺少提示行");
            Assert.IsEmpty(hint.text, "刚打开一封信时提示行应为空");

            float cashBefore = gs.cash;
            var approve = ApproveButton(inbox);
            Assert.IsNotNull(approve, "待批件必须有批准按钮");
            Assert.IsTrue(approve.gameObject.activeInHierarchy);
            approve.onClick.Invoke();
            yield return null;

            Assert.AreEqual("approved", mailA.status);
            Assert.IsTrue(mailA.effectPending, "批准只是受理，生效要等下季结算");
            Assert.AreEqual(cashBefore, gs.cash, 0.001f, "协调池时刻槽免费，批准本身不得动现金");
            Assert.IsFalse(ApproveButton(inbox).gameObject.activeInHierarchy,
                "已批过的单子不能再给一次批准按钮");
            Assert.IsNotEmpty(LabelNamed(inbox.gameObject, "Hint").text,
                "批准后应给出回执性提示，而不是静默");

            // 换到另一封信：上一封的处理提示必须清掉（旧弊端：串信）
            var rowB = Row(inbox, "t038-b");
            Assert.IsNotNull(rowB, "另一封待批件应仍在列表里");
            rowB.onClick.Invoke();
            yield return null;
            Assert.IsEmpty(LabelNamed(inbox.gameObject, "Hint").text,
                "切到另一封信后，不应还挂着上一封的批准回执");

            // 已批准的那封不会被重复计一次
            Assert.AreEqual(1, gs.inbox.Count(m => m.id == "t038-a"), "批准不得复制出第二封同一张单子");
            yield return null;
        }

        // —————————————————————————————————————————————————————————————
        // ⑤ 回执节奏：批准 ≠ 立刻收到回执，4 游戏小时后才落箱
        // —————————————————————————————————————————————————————————————

        [Test]
        public void ExecutionReceipt_LandsOnlyAfterFourGameHours()
        {
            var gs = NewState("T038Receipt");
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "PEK", "18:00", 1, "t038-rcpt", out string note), note);
            var mail = gs.inbox.First(m => m.id == "t038-rcpt");
            Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

            gs.quarter = 2;
            InboxManager.ProcessPendingEffects(gs);

            var pending = gs.pendingInbox.FirstOrDefault(p => p?.mail != null && p.mail.id.StartsWith("fx_t038-rcpt"));
            Assert.IsNotNull(pending, "回执必须先排进待发队列，而不是当场入箱");
            Assert.IsFalse(gs.inbox.Any(m => m.id == pending.mail.id),
                "刚点完批准就收到回执＝部门不用干活（2026-10-19 报障）");

            var ready = new DateTime(pending.deliverUtcTicks, DateTimeKind.Utc);
            Assert.AreEqual(gs.SimUtc.AddHours(InboxCadence.ReplyHours), ready,
                $"回执应在受理后 {InboxCadence.ReplyHours} 游戏小时落笔");

            InboxManager.DeliverDue(gs, ready.AddMinutes(-1));
            Assert.IsFalse(gs.inbox.Any(m => m.id == pending.mail.id), "差一分钟不得提前送达");
            Assert.IsTrue(gs.pendingInbox.Any(p => p?.mail != null && p.mail.id == pending.mail.id),
                "提前取件不得把待发件吃掉");

            InboxManager.DeliverDue(gs, ready);
            Assert.IsTrue(gs.inbox.Any(m => m.id == pending.mail.id), "到点必须送达");
            Assert.IsFalse(gs.pendingInbox.Any(p => p?.mail != null && p.mail.id == pending.mail.id),
                "送达后不得留在待发队列里重复投递");
        }

        // —————————————————————————————————————————————————————————————
        // ⑥ 执行回执里的机队读数：一趟往返 = 两段 = 两次起落循环
        // —————————————————————————————————————————————————————————————

        [UnityTest]
        public IEnumerator OneRoundTrip_WritesExactlyTwoFlightCycles()
        {
            var gs = NewState("T038Cycles");
            var route = MakeRoute("T038-CYCLE", "B-3802");
            gs.activeRoutes.Add(route);
            var ac = MakeAircraft("B-3802");
            gs.fleet.Add(ac);

            gs.SetSimUtc(Utc(7, 50));
            LiveClockSimulator.Advance(gs, Utc(7, 50), Utc(20, 0));

            Assert.AreEqual(2, ac.flightCycles,
                "一趟往返＝去程＋回程两段，每段落地记 1 次循环；写 1 会让定检周期算长一倍");
            int block = LiveClockLogic.BlockMinutes(route.depTime, route.arrTime, route.distanceKm);
            Assert.AreEqual(2f * block / 60f, ac.flightHours, 0.02f,
                "航时同样按两段累加，必须与循环数同口径");
            Assert.Greater(gs.liveQuarterIncome, 0f, "有执飞机的往返必须真的入账");
            Assert.Less(ac.condition, 100f, "落地一定要有机损，否则定检永远不触发");
            yield return null;
        }

        // —————————————————————————————————————————————————————————————
        // ⑦ 三处数字一致：列表卡片 / 单航线页 / 公式
        // —————————————————————————————————————————————————————————————

        [Test]
        public void RouteCardAndDetailPage_PrintTheSameDailyAndWeeklyProfit()
        {
            var gs = NewState("T038Numbers");
            var route = MakeRoute("T038-NUM", "B-3803");
            gs.activeRoutes.Add(route);
            gs.fleet.Add(MakeAircraft("B-3803"));
            OpsSubpageContext.SelectedRouteId = route.routeId;
            FleetNetContext.SelectedRouteId = null;

            // 列表页卡片
            var holder = Own(new GameObject("T038Card", typeof(RectTransform)));
            var card = RouteCard.BuildRuntime(holder.transform);
            card.Bind(route);
            var profit = Labels(card.gameObject).FirstOrDefault(t => t.name == "Profit");
            Assert.IsNotNull(profit, "卡片缺少日利文本");
            Assert.AreEqual(route.dailyProfit, NumberAfter(profit.text, "日利"), 0.051f,
                "卡片日利必须等于航线日利（F1 一位小数，容差半个刻度）");

            // 单航线详情页
            var detail = RouteDetailPanel.Build("单航线");
            Own(detail.gameObject);
            var line = LabelContaining(detail.gameObject, "日利", "周利");
            Assert.IsNotNull(line, "单航线页没有同时写出日利与周利");
            Assert.AreEqual(route.dailyProfit, NumberAfter(line.text, "日利"), 0.006f,
                "详情页日利（F2）与航线日利必须同源");
            Assert.AreEqual(route.dailyProfit * 7f, NumberAfter(line.text, "周利"), 0.051f,
                "周利＝日利×7，不能另起一套口径");
            StringAssert.DoesNotContain("停飞", line.text, "机号有效、停机原因为空时不得标成停飞");
        }

        // —————————————————————————————————————————————————————————————
        // ⑦a 四口径：邮件里的数字必须与列表、详情同源（T03.8 剩下的那一半）
        //
        // 此前只有「卡片 ↔ 详情」两条腿（§⑦）。任务书要的是「列表、详情、地图、邮件」
        // 四处一致，而**邮件**是最容易分叉的一处：它由 DeptReportLogic/OpsQueryLogic 拼接，
        // 历史事故正是「把航线级日利当单班级写进信里」。地图层不承担数字（只画线与城市名，
        // 已由 §⑦ 之外的地图用例钉住「地图层不写金额」），所以这里钉「邮件 ↔ 详情 ↔ 卡片」三处
        // 读的是同一个 route.dailyProfit，且「单班日利」确实是它除以班次。
        // —————————————————————————————————————————————————————————————

        [Test]
        public void MailAnswer_CarriesTheSameRouteAndNumbersAsListAndDetail()
        {
            var gs = NewState("T038MailFourWays");
            var route = MakeRoute("T038-MAIL", "B-3805");
            gs.activeRoutes.Add(route);
            gs.fleet.Add(MakeAircraft("B-3805"));
            OpsSubpageContext.SelectedRouteId = route.routeId;
            FleetNetContext.SelectedRouteId = null;

            // ① 邮件：走真链路（命令台问句 → 运营部按本线台账答复 → 收件箱汇报件）
            Assert.IsTrue(DeptReportLogic.RunRouteQuery(gs, route.routeId, "这条线现在赚多少", out string receipt),
                "运营部未能就本线作答：" + receipt);
            Assert.IsNotNull(gs.inbox, "收件箱未初始化");
            InboxMail mail = gs.inbox.Count > 0 ? gs.inbox[gs.inbox.Count - 1] : null;
            Assert.IsNotNull(mail, "答复件没有进收件箱");
            string body = mail.body ?? "";
            // 正文里两端的写法可能是「PEK — SHA」也可能是城市名，故按三字码分别断言，
            // 不钉分隔符（钉分隔符等于测排版，改一次排版就红）。
            StringAssert.Contains(route.origin, body, "邮件正文里没有出发地三字码");
            StringAssert.Contains(route.destination, body, "邮件正文里没有目的地三字码");

            int flights = Math.Max(1, route.FlightCount);
            // 单班日利 = 航线日利 ÷ 班次 —— 把航线级当单班级写进信里是反复出现过的错法
            Assert.AreEqual(route.dailyProfit / flights, NumberAfter(body, "单班日利"), 0.006f,
                "邮件「单班日利」必须等于航线日利 ÷ 班次");
            Assert.AreEqual(route.dailyProfit * 7f, NumberAfter(body, "周利 ≈"), 0.06f,
                "邮件「周利」必须等于日利 × 7（加密航线不得再乘一遍周班次）");

            // ② 详情页（F2）
            var detail = RouteDetailPanel.Build("单航线");
            Own(detail.gameObject);
            var line = LabelContaining(detail.gameObject, "日利", "周利");
            Assert.IsNotNull(line, "单航线页没有同时写出日利与周利");
            Assert.AreEqual(route.dailyProfit, NumberAfter(line.text, "日利"), 0.006f,
                "详情页日利与邮件必须读同一个字段");

            // ③ 列表卡（F1）
            var holder = Own(new GameObject("T038MailCard", typeof(RectTransform)));
            var card = RouteCard.BuildRuntime(holder.transform);
            card.Bind(route);
            var profit = Labels(card.gameObject).FirstOrDefault(t => t != null && t.name == "Profit");
            Assert.IsNotNull(profit, "卡片缺少日利文本");
            Assert.AreEqual(route.dailyProfit, NumberAfter(profit.text, "日利"), 0.051f,
                "卡片日利（F1 一位小数）必须与详情页同源");
        }

        // —————————————————————————————————————————————————————————————
        // ⑦b 「执飞行」的串环一格：没飞机说没飞机，有飞机说利用率并给入口
        //
        // 2026-10-29 CEO 问「单航线的『未串环』是什么意思」，暴露旧文案只写「未串环（本线往返）」：
        // 既不说明后果，也不说明去哪办。更糟的是它**在没飞机时也这么说** —— 旧版把 "未分配"
        // 当机号去查环，必然查不到，于是停飞航线也印「未串环」，把「没飞机」读成「有飞机、
        // 只是没串环」，同一行还跟着印一个「0 座」。
        // —————————————————————————————————————————————————————————————

        [UnityTest]
        public IEnumerator FlightDetailRingCell_SaysNoAircraft_WhenRegistrationEmpty()
        {
            var gs = NewState("T038NoPlane");
            var route = MakeRoute("T038-NOPLANE", "");
            gs.activeRoutes.Add(route);
            FleetNetContext.SelectedRouteId = route.routeId;
            OpsSubpageContext.SelectedRouteId = null;

            var detail = RouteDetailPanel.Build("单航线");
            Own(detail.gameObject);
            yield return null;

            var hit = detail.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Hit");
            Assert.IsNotNull(hit, "班次行缺少点击热区，进不了单班页");
            hit.onClick.Invoke();
            yield return null;

            var ac = LabelNamed(detail.gameObject, "Ac");
            Assert.IsNotNull(ac, "单班页缺少「执飞行」");
            StringAssert.Contains("未配执飞机", ac.text, "没有执飞机就必须说没有执飞机");
            StringAssert.DoesNotContain("未串环", ac.text,
                "「没飞机」不得读成「有飞机、只是没串环」（旧版把「未分配」当机号查环，必然查不到）");
            StringAssert.DoesNotContain("0 座", ac.text, "载客量查不到时不得印「0 座」，那是空值的伪装");
        }

        [UnityTest]
        public IEnumerator FlightDetailRingCell_ShowsUtilizationOver10h_WhenAircraftAssigned()
        {
            var gs = NewState("T038Util");
            var route = MakeRoute("T038-UTIL", "B-3811");   // 每日 1 班、1100 km、A320neo
            gs.activeRoutes.Add(route);
            gs.fleet.Add(MakeAircraft("B-3811"));
            FleetNetContext.SelectedRouteId = route.routeId;
            OpsSubpageContext.SelectedRouteId = null;

            var detail = RouteDetailPanel.Build("单航线");
            Own(detail.gameObject);
            yield return null;

            var hit = detail.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Hit");
            Assert.IsNotNull(hit, "班次行缺少点击热区");
            hit.onClick.Invoke();
            yield return null;

            var ac = LabelNamed(detail.gameObject, "Ac");
            Assert.IsNotNull(ac, "单班页缺少「执飞行」");
            StringAssert.Contains("未串环", ac.text, "没有航班环记录时必须如实说未串环");
            StringAssert.Contains("点此串环", ac.text, "必须给出串环入口，否则玩家不知道去哪办");

            // 口径：一圈 = 去 + 回两段（不含过站），与 OpsEconomics.dailyUtilizationHours 同源。
            // 1100 km / 850 kmh = 1.29h 单程 → 每日 1 班 = 2.59h/日；目标 10h 由常量给出。
            float expect = OpsEconomics.BlockHours(route.distanceKm) * OpsEconomics.RoundTripSectors;
            Assert.AreEqual(expect, NumberAfter(ac.text, "本机日利用"), 0.051f,
                "本机日利用必须按「一圈 2 段」累计（F1 一位小数，容差半个刻度）");
            StringAssert.Contains($"/{EconomyBalanceParams.TargetDailyUtilizationHours:0}h", ac.text,
                "利用率必须与目标时长并列，否则「2.6h 算高还是低」无从判断");
        }

        // —————————————————————————————————————————————————————————————
        // ⑧ 地图线宽：档位函数（肉眼能否分辨另说）
        // —————————————————————————————————————————————————————————————

        [TestCase(0, 1.8f)]
        [TestCase(7, 1.8f)]
        [TestCase(8, 2.8f)]
        [TestCase(20, 2.8f)]
        [TestCase(21, 4.0f)]
        [TestCase(28, 4.0f)]
        public void MapRouteThickness_FollowsWeeklyDepartureBands(int weeklyFlights, float expected)
        {
            var route = new RouteData { routeId = "T038-W", origin = "PEK", destination = "SHA", weeklyFlights = weeklyFlights };
            Assert.AreEqual(expected, MapWidget.RouteThickness(route), 1e-4f,
                $"{weeklyFlights} 班/周 应画 {expected}px");
        }

        /// <summary>
        /// 三档之间的差必须 ≥1 px。
        ///
        /// 首版是 2.0 / 2.4 / 3.0，相邻只差 0.4 / 0.6 px —— 比一个像素还小，
        /// 上机目测「线条粗细一样」（2026-10-27 用户实测）。档位函数当时是全绿的：
        /// 断言只证明「取到了 2.0 和 3.0」，证明不了「这两个数看得出来」。
        /// 所以补这条守卫，把「差值够不够看」变成机器可判定的下限，
        /// 免得以后有人为了「更细腻」把档位又调回 0.x 的间隔。
        /// </summary>
        [Test]
        public void MapRouteThickness_BandsAreSeparatedByAtLeastOnePixel()
        {
            var thin = new RouteData { routeId = "T038-S1", origin = "PEK", destination = "SHA", weeklyFlights = 7 };
            var mid = new RouteData { routeId = "T038-S2", origin = "PEK", destination = "SHA", weeklyFlights = 14 };
            var thick = new RouteData { routeId = "T038-S3", origin = "PEK", destination = "SHA", weeklyFlights = 21 };

            float a = MapWidget.RouteThickness(thin);
            float b = MapWidget.RouteThickness(mid);
            float c = MapWidget.RouteThickness(thick);

            Assert.GreaterOrEqual(b - a, 1.0f, $"细档→基准档只差 {b - a:F2}px，人眼分辨不出；至少 1px");
            Assert.GreaterOrEqual(c - b, 1.0f, $"基准档→粗档只差 {c - b:F2}px，人眼分辨不出；至少 1px");
            Assert.Greater(c - a, 1.5f, $"细档→粗档只差 {c - a:F2}px，运力分布仍然读不出来");
        }

        [Test]
        public void MapRouteThickness_ReadsDepartureNotDutyTotals()
        {
            var route = new RouteData { routeId = "T038-W2", origin = "PEK", destination = "SHA" };
            route.SyncFlightsFromPrimary();          // 每日 7 班
            Assert.AreEqual(1.8f, MapWidget.RouteThickness(route), 1e-4f);
            route.flights[0].weeklyFlights = 21;
            Assert.AreEqual(4.0f, MapWidget.RouteThickness(route), 1e-4f,
                "线宽取离港周合计；改班次后必须跟着变，不能沿用首次绘制的档位");
        }

        // —————————————————————————————————————————————————————————————
        // ⑧b 地图渲染链路：档位函数对了，不等于画出来的线就是那个宽度
        //
        // 这一段补的是 ⑧ 覆盖不到的缝。⑧ 只验 RouteThickness(route) 的返回值；
        // 真实的绘制要经过 DrawRoutes → DrawLine(写进 sizeDelta.y) → MapTrackZoom.RegisterLine，
        // 任何一环把 thickness 丢掉或改写，⑧ 都照样全绿。
        // 所以这里走真实绘制路径，断言三层同时一致：绘制条数 / 注册厚度 / 实际几何。
        // —————————————————————————————————————————————————————————————

        static GameObject MapCanvas(string name)
        {
            // 不建 MapViewport：视口缩放会把 sizeDelta.y 改成 thickness / viewScale，
            // 那样断言的是「缩放到多少」而不是「登记了多少」。本段只验无缩放时的登记契约。
            var go = new GameObject(name, typeof(RectTransform));
            return go;
        }

        [Test]
        public void MapRouteRender_PaintsBandsThroughRealDrawPath()
        {
            var gs = NewState("T038MapRender");
            var thin = MakeRoute("T038-M1", "B-3801");                    // 每日 7 班 → ≤7 档
            var thick = MakeRoute("T038-M2", "B-3802");
            thick.destination = "PVG";                                    // 换目的地，避免两线重叠
            thick.SyncFlightsFromPrimary();
            thick.flights[0].weeklyFlights = 21;                          // >20 档
            gs.activeRoutes.Add(thin);
            gs.activeRoutes.Add(thick);

            var map = Own(MapCanvas("T038MapRoot"));
            int drawn = MapWidget.DrawRoutes(map.transform);

            Assert.Greater(drawn, 0, "两条有效航线必须画出线段；0 表示绘制入口被跳过");
            var zoom = map.GetComponent<MapTrackZoom>();
            Assert.IsNotNull(zoom, "DrawRoutes 必须挂上 MapTrackZoom（否则缩放补偿整条链路失效）");
            Assert.AreEqual(drawn, zoom.LineCount, "每条绘制成功的线段都必须登记，不能只画不登记");

            var th = zoom.LineThicknesses;
            CollectionAssert.Contains(th.ToArray(), 1.8f, "7 班/周 的航线必须是 1.8px 档");
            CollectionAssert.Contains(th.ToArray(), 4.0f, "21 班/周 的航线必须是 4.0px 档");
            foreach (var t in th)
                Assert.IsTrue(Mathf.Approximately(t, 1.8f) || Mathf.Approximately(t, 2.8f) || Mathf.Approximately(t, 4.0f),
                    $"绘制出了档位外的线宽 {t}；地图只允许 1.8 / 2.8 / 4.0 三档");
        }

        [Test]
        public void MapRouteRender_WritesRegisteredThicknessIntoRealGeometry()
        {
            var gs = NewState("T038MapGeom");
            var thin = MakeRoute("T038-G1", "B-3803");
            var thick = MakeRoute("T038-G2", "B-3804");
            thick.destination = "PVG";
            thick.SyncFlightsFromPrimary();
            thick.flights[0].weeklyFlights = 21;
            gs.activeRoutes.Add(thin);
            gs.activeRoutes.Add(thick);

            var map = Own(MapCanvas("T038MapRoot2"));
            MapWidget.DrawRoutes(map.transform);
            var zoom = map.GetComponent<MapTrackZoom>();
            Assert.IsNotNull(zoom);
            Assert.Greater(zoom.LineCount, 0);

            for (int i = 0; i < zoom.LineCount; i++)
            {
                var rt = zoom.LineAt(i);
                Assert.IsNotNull(rt, $"第 {i} 条线段登记了却取不到 RectTransform");
                Assert.AreEqual(zoom.LineThicknesses[i], rt.sizeDelta.y, 1e-3f,
                    $"第 {i} 条线段登记的厚度与实际 sizeDelta.y 不一致 —— 绘制函数把档位丢了");
                Assert.Greater(rt.sizeDelta.x, 0f, $"第 {i} 条线段长度为 0，画了等于没画");
            }
        }

        [Test]
        public void MapRouteRender_DrawLineHonoursThicknessForEveryBand()
        {
            var map = Own(MapCanvas("T038MapBands")).transform;
            foreach (var band in new[] { 1.8f, 2.8f, 4.0f })
            {
                var rt = MapWidget.DrawLine(map, "band_" + band, Vector2.zero, new Vector2(100f, 0f),
                    Color.white, band);
                Assert.IsNotNull(rt, $"{band}px 档必须画得出来");
                Assert.AreEqual(band, rt.sizeDelta.y, 1e-3f, $"DrawLine 未把 {band}px 写进 sizeDelta.y");
                Assert.AreEqual(100f, rt.sizeDelta.x, 1e-3f, "线段长度必须等于两端点距离");
            }
        }

        [Test]
        public void MapRouteRender_AddsNoAmountsToTheMapLayer()
        {
            var gs = NewState("T038MapNoMoney");
            gs.activeRoutes.Add(MakeRoute("T038-N1", "B-3805"));
            var map = Own(MapCanvas("T038MapRoot3"));
            MapWidget.DrawRoutes(map.transform);

            // T03.8 的「地图口径」前提：航线绘制不往地图上写任何金额。
            // 城市气泡里的 GDP / 吞吐量 / 买基地价属城市经济学，不是航线盈亏，不在此断言范围。
            var money = Labels(map).FirstOrDefault(t => t != null && !string.IsNullOrEmpty(t.text)
                && (t.text.Contains("万") || t.text.Contains("日利") || t.text.Contains("周利")));
            Assert.IsNull(money, $"航线绘制不应产生金额文本，但出现了「{(money != null ? money.text : "")}」");
        }

        // —————————————————————————————————————————————————————————————
        // ⑩ 地图图层：航线默认不画（点按钮才画）+ 飞机按执飞机型画
        //
        // ★2026-10-30 CEO 两条要求：「白色三角形换成执飞的机型图标」「默认只有飞机在动，点了航线按钮才显示航线」。
        // 这一段验的是**接线**（开关 → 绘制入口、机型 → 图标），不是好看不好看。
        // —————————————————————————————————————————————————————————————

        [Test]
        public void MapRouteLayer_StaysHiddenUntilTheRoutesButtonIsOn()
        {
            var gs = NewState("T038RouteLayer");
            gs.activeRoutes.Add(MakeRoute("T038-L1", "B-3901"));
            var map = Own(MapCanvas("T038RouteLayerRoot"));

            try
            {
                MapWidget.SetRoutesVisible(false);
                Assert.AreEqual(0, MapWidget.DrawRouteLayer(map.transform),
                    "没点过「航线」按钮时，地图上不得出现航线");
                Assert.IsNull(map.transform.Find("Route_0_0"), "航线图层关着却画出了线段");

                MapWidget.SetRoutesVisible(true);
                Assert.Greater(MapWidget.DrawRouteLayer(map.transform), 0,
                    "点了「航线」按钮必须把航线画出来");
                Assert.IsNotNull(map.transform.Find("Route_0_0"), "图层开着却没有线段");
            }
            finally
            {
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void MapRouteLayer_DrawsTheSelectedRouteEvenWhenTheLayerIsOff()
        {
            var gs = NewState("T038SelectedRoute");
            var a = MakeRoute("T038-SR1", "B-3911");
            var b = MakeRoute("T038-SR2", "B-3912");
            b.destination = "PVG";
            b.SyncFlightsFromPrimary();
            gs.activeRoutes.Add(a);
            gs.activeRoutes.Add(b);
            var map = Own(MapCanvas("T038SelectedRouteRoot"));

            try
            {
                MapWidget.SetRoutesVisible(false);
                MapSelection.Select("T038-SR1", 0);

                int drawn = MapWidget.DrawRouteLayer(map.transform);
                Assert.Greater(drawn, 0, "选中飞机后必须画出它的航线 —— 哪怕航线图层是关的");
                Assert.IsNull(map.transform.Find("Route_1_0"),
                    "未选中的另一条航线不得跟着出现（否则等于把图层打开了）");

                // ★2026-10-30 CEO：「点的航班的航线线条的粗细度要和航线网络的线条一样」。
                // 09:00 时这班在空 —— 已飞段是实线、待飞段是虚线；两种线都必须与航线网络同色同宽。
                var segs = map.GetComponentsInChildren<Image>(true)
                    .Where(i => i != null && i.name != null && i.name.StartsWith("Route_0", StringComparison.Ordinal))
                    .ToList();
                Assert.IsNotEmpty(segs, "选中的航线必须至少画出一段");

                float expectedT = MapWidget.RouteThickness(a);
                foreach (var seg in segs)
                {
                    Assert.AreEqual(expectedT, seg.rectTransform.sizeDelta.y, 1e-3f,
                        $"选中航线的线宽（{seg.name}）必须等于档位值 {expectedT}，不得加粗");
                    Assert.Less(Mathf.Abs(seg.color.r - seg.color.g), 0.35f,
                        $"选中航线不该是黄色/橙色（实测 {seg.color}）");
                    Assert.Greater(seg.color.b, seg.color.r,
                        $"选中航线应保持普通航线的紫蓝色（实测 {seg.color}）");
                }
            }
            finally
            {
                MapSelection.Reset();
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void MapSelectedRoute_KeepsScreenWideThicknessAtAnyZoom()
        {
            // ★2026-10-30 CEO 截图：点开飞机后航线粗成一条宽带，而航线网络上的同一条线是细线；
            // 「不论地图放大缩小，线条的粗细始终不变」。
            //
            // 根因：`DrawSelectedRouteOnly` 没有先 Clear()，`MapTrackZoom.Reapply()` 又因
            // `_last` 已等于当前缩放而提前返回 —— 新画的线以「世界单位 = 屏幕像素」的数值留在场上，
            // 市区级放大（viewScale≈10）时就是十几像素的宽带。这里用真实视口缩放把它钉死。
            var gs = NewState("T038ZoomWidth");
            gs.fleet.Add(MakeAircraft("B-3951"));
            var r = MakeRoute("T038-ZOOM", "B-3951");
            gs.activeRoutes.Add(r);

            var vpGo = Own(new GameObject("T038ZoomViewport", typeof(RectTransform)));
            var vp = vpGo.AddComponent<MapViewport>();
            var world = new GameObject("World", typeof(RectTransform)).GetComponent<RectTransform>();
            world.SetParent(vpGo.transform, false);
            vp.world = world;
            var map = Own(new GameObject("T038ZoomContent", typeof(RectTransform)));
            map.transform.SetParent(world, false);

            void Wipe()
            {
                while (map.transform.childCount > 0)
                    UnityEngine.Object.DestroyImmediate(map.transform.GetChild(0).gameObject);
                // ★每轮都要连 MapTrackZoom 一起重建：真实流程里每次重建动态层都会新建一个 Content，
                //   新组件的 OnEnable 会把「当前缩放」记进 `_last`，于是随后 Reapply() 因「缩放未变」
                //   提前返回 —— 这正是那条宽带事故的发生条件。复用同一个组件的话，每轮缩放都在变，
                //   Reapply 反而会补偿，测试就永远绿（本用例的第一版就是这样漏掉的）。
                var z = map.GetComponent<MapTrackZoom>();
                if (z != null) UnityEngine.Object.DestroyImmediate(z);
            }

            List<Image> RouteSegs() => map.GetComponentsInChildren<Image>(true)
                .Where(i => i != null && i.name != null && i.name.StartsWith("Route_0", StringComparison.Ordinal))
                .ToList();

            try
            {
                float screenT = MapWidget.RouteThickness(r);   // 档位值 = 屏幕像素

                // ① 选中态在真实缩放下：世界厚度必须等于 屏幕值 ÷ 缩放
                MapWidget.SetRoutesVisible(false);
                MapSelection.Select("T038-ZOOM", 0);
                foreach (float k in new[] { 1f, 4f, 12f })
                {
                    world.localScale = new Vector3(k, k, 1f);
                    Wipe();
                    Assert.Greater(MapWidget.DrawRouteLayer(map.transform), 0, $"{k}× 时选中航线必须有线");
                    var segs = RouteSegs();
                    Assert.IsNotEmpty(segs, $"{k}× 时选中航线必须有线段");
                    foreach (var seg in segs)
                        Assert.AreEqual(screenT / k, seg.rectTransform.sizeDelta.y, 1e-2f,
                            $"{k}× 时「{seg.name}」的世界厚度必须是屏幕值 {screenT} ÷ {k}；" +
                            $"实测 {seg.rectTransform.sizeDelta.y:F3}（偏大 = 没做缩放补偿，就是那条宽带）");
                }

                // ② 同一缩放下，「点开的航线」与「网络里的同一条航线」厚度必须一模一样
                world.localScale = new Vector3(4f, 4f, 1f);
                float selectedThick;
                Wipe();
                MapWidget.SetRoutesVisible(false);
                MapSelection.Select("T038-ZOOM", 0);
                MapWidget.DrawRouteLayer(map.transform);
                selectedThick = RouteSegs()[0].rectTransform.sizeDelta.y;

                Wipe();
                MapWidget.SetRoutesVisible(true);
                MapWidget.DrawRoutes(map.transform);
                float networkThick = RouteSegs()[0].rectTransform.sizeDelta.y;

                Assert.AreEqual(networkThick, selectedThick, 1e-3f,
                    $"选中航线的厚度（{selectedThick:F3}）必须与航线网络的同一条线（{networkThick:F3}）相同");
                Assert.AreEqual(screenT / 4f, networkThick, 1e-2f, "航线网络的线宽也必须是屏幕恒定值");
            }
            finally
            {
                MapSelection.Reset();
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void MapRouteDashes_AreFineDots_LikeTheTerminator()
        {
            // ★2026-10-30 CEO：「图一中的虚线中的点太宽了，参照地图昼夜变更线的虚线修改」。
            // 晨昏线在 shader 里是「每 8px 一个点、点径约 3.4px」（_DashPx=8、_EdgePx=1.7）。
            // 这里断言选中的待飞段也是同一量级：单段 ≤ 6px（旧版是 7px 起、长航线上更长），
            // 且段数足够密 —— 段数太少说明它还在按「长杠」画，而不是「一串点」。
            var gs = NewState("T038DashMetrics");
            gs.fleet.Add(MakeAircraft("B-3961"));
            var r = MakeRoute("T038-DASH", "B-3961", "08:00", "16:30");
            r.destination = "LHR";
            r.distanceKm = 8140f;
            r.SyncFlightsFromPrimary();
            gs.activeRoutes.Add(r);

            var map = Own(MapCanvas("T038DashMetricsRoot"));
            try
            {
                MapWidget.SetRoutesVisible(false);
                MapSelection.Select("T038-DASH", 0);
                gs.SetSimUtc(new DateTime(2026, 1, 5, 9, 0, 0) - GameState.LocalUtcOffset);
                MapWidget.DrawRouteLayer(map.transform);

                var dashes = map.GetComponentsInChildren<Image>(true)
                    .Where(i => i != null && i.name != null
                                && i.name.StartsWith("Route_0_dash_", StringComparison.Ordinal))
                    .ToList();
                Assert.Greater(dashes.Count, 20,
                    $"待飞段必须是一串细密的点，实际只有 {dashes.Count} 段 —— 段太长就会变成「一条长杠」");

                float maxLen = dashes.Max(d => d.rectTransform.sizeDelta.x);
                float avgLen = dashes.Average(d => d.rectTransform.sizeDelta.x);
                Assert.Less(maxLen, 6.5f, $"单段虚线（屏幕 px）必须 ≤ 6.5px 才对得上晨昏线，实测最长 {maxLen:F2}px");
                Assert.Greater(avgLen, 1.5f, $"段长不能退化成几乎为零，实测平均 {avgLen:F2}px");

                // ★2026-10-30 CEO 第三次反馈：「虚线不连贯」。
                // 根因是旧实现会把跨过折线拐点的一段虚线拆成两个各自旋转的 Image，接缝处错位。
                // 现在一段虚线只生成一个 Image，段长与间隔必须**按构造相等**。这里逐段量：
                // ① 段长偏差；② 相邻段之间的空隙偏差（末段可能被航线终点截短，排除）。

                // 与晨昏线同周期（8px）：段长 + 间距应落在 6~10px，且实/空大致 1:1
                float thickness = MapWidget.RouteThickness(r);
                Assert.AreEqual(thickness, dashes[0].rectTransform.sizeDelta.y, 1e-3f,
                    "虚线的粗细仍必须与航线网络的档位一致");

                var ordered = dashes
                    .OrderBy(d => int.Parse(d.name.Substring("Route_0_dash_".Length)))
                    .ToList();

                Vector2 End(Image d)
                {
                    var rt = d.rectTransform;
                    float rad = rt.localRotation.eulerAngles.z * Mathf.Deg2Rad;
                    return rt.anchoredPosition + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * rt.sizeDelta.x;
                }

                // 段长：除末段外必须一致（±0.6 世界 px）。旧实现会在这里出现「半截段」。
                var lens = ordered.Take(ordered.Count - 1).Select(d => d.rectTransform.sizeDelta.x).ToList();
                Assert.Greater(lens.Count, 5, "段数太少，量不出均匀性");
                float nominalDash = lens.Average();
                for (int i = 0; i < lens.Count; i++)
                    Assert.AreEqual(nominalDash, lens[i], 0.6f,
                        $"第 {i} 段虚线长 {lens[i]:F2}，与基准 {nominalDash:F2} 偏差过大 —— " +
                        "说明它又被折线拐点切成了半截（这就是「不连贯」）");

                // 空隙：相邻两段首尾之间的距离必须一致（±0.6 世界 px）。
                var gaps = new List<float>();
                for (int i = 0; i + 1 < ordered.Count - 1; i++)
                    gaps.Add(Vector2.Distance(End(ordered[i]), ordered[i + 1].rectTransform.anchoredPosition));
                Assert.Greater(gaps.Count, 4, "空隙数太少，量不出均匀性");
                float nominalGap = gaps.Average();
                for (int i = 0; i < gaps.Count; i++)
                    Assert.AreEqual(nominalGap, gaps[i], 0.6f,
                        $"第 {i} 个空隙 {gaps[i]:F2}，与基准 {nominalGap:F2} 偏差过大 —— 线上会出现「大洞」");

                // 实/空 1:1（对齐晨昏线）：空隙和段长应是同一个量级
                Assert.AreEqual(nominalDash, nominalGap, nominalDash * 0.45f,
                    $"实{nominalDash:F2}/空{nominalGap:F2} 应大致 1:1，与晨昏线同观感");
            }
            finally
            {
                MapSelection.Reset();
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void MapRouteSplit_UsesSolidForFlown_AndDashedForAhead()
        {
            // ★2026-10-30 CEO：「参照 AS 的航线，点击飞机后，飞机已经行驶的线路用实线，
            // 前方待飞行的线路用虚线」。
            var gs = NewState("T038Split");
            gs.fleet.Add(MakeAircraft("B-3941"));
            var r = MakeRoute("T038-SPLIT", "B-3941", "08:00", "10:30");
            // 长航线：24 段与 40 段采样的偏差在这里才够大（短航线两种采样几乎重合，
            // 断言会「看不出」机头与分界点错位 —— 那等于这条断言白写）。
            r.destination = "LHR";
            r.distanceKm = 8140f;
            r.SyncFlightsFromPrimary();
            gs.activeRoutes.Add(r);
            var map = Own(MapCanvas("T038SplitRoot"));

            int CountByPrefix(string prefix) => map.GetComponentsInChildren<Image>(true)
                .Count(i => i != null && i.name != null && i.name.StartsWith(prefix, StringComparison.Ordinal));

            void Redraw(DateTime utc)
            {
                gs.SetSimUtc(utc - GameState.LocalUtcOffset);
                MapSelection.Select("T038-SPLIT", 0);
                // 注意：不能在 foreach 里 DestroyImmediate —— 边遍历边销毁会跳过节点，
                // 上一轮的 PlaneMark 会留在场上，断言就可能读到"上一秒的飞机"。
                while (map.transform.childCount > 0)
                    UnityEngine.Object.DestroyImmediate(map.transform.GetChild(0).gameObject);
                MapWidget.DrawRouteLayer(map.transform);
                MapWidget.DrawPlaneMarks(map.transform);
            }

            try
            {
                MapWidget.SetRoutesVisible(false);

                // ① 起飞前：整条都是虚线（一段都没飞）
                Redraw(new DateTime(2026, 1, 5, 6, 0, 0));
                Assert.AreEqual(0, CountByPrefix("Route_0_") - CountByPrefix("Route_0_dash_"),
                    "起飞前不该有实线段");
                Assert.Greater(CountByPrefix("Route_0_dash_"), 0, "起飞前整条航线都应是虚线");

                // ② 去程在空：既有实线（已飞）也有虚线（待飞）
                Redraw(new DateTime(2026, 1, 5, 9, 0, 0));
                int solid = CountByPrefix("Route_0_") - CountByPrefix("Route_0_dash_");
                int dash = CountByPrefix("Route_0_dash_");
                Assert.Greater(solid, 0, "去程在空时，已飞部分必须画成实线");
                Assert.Greater(dash, 0, "去程在空时，前方待飞部分必须画成虚线");

                // ③ 分界点必须压中机头：实线最后一段的终点 = 飞机图钉的位置。
                //    两边都按 ArcSegments 采样、都用同一个 frac，差几像素就说明口径又分叉了。
                var plane = map.transform.Find("PlaneMark") as RectTransform;
                Assert.IsNotNull(plane, "地图上必须有飞机图钉");
                var solidSegs = map.GetComponentsInChildren<Image>(true)
                    .Where(i => i.name != null && i.name.StartsWith("Route_0_", StringComparison.Ordinal)
                                && !i.name.StartsWith("Route_0_dash_", StringComparison.Ordinal))
                    .OrderBy(i => int.Parse(i.name.Substring("Route_0_".Length)))
                    .ToList();
                var lastSolid = solidSegs[solidSegs.Count - 1];
                var rt = lastSolid.rectTransform;
                float rad = rt.localRotation.eulerAngles.z * Mathf.Deg2Rad;
                Vector2 end = rt.anchoredPosition + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * rt.sizeDelta.x;
                var snap = MapFlightState.Snapshot(new DateTime(2026, 1, 5, 9, 0, 0), r.daysOfWeek,
                    r.weeklyFlights, r.depTime, r.arrTime, r.distanceKm, r.aircraftType, r.groundStaffAssigned);
                var planes = map.GetComponentsInChildren<RectTransform>(true).Count(t => t.name == "PlaneMark");
                float near = float.MaxValue;
                foreach (var s in solidSegs)
                {
                    var r2 = s.rectTransform;
                    float a2 = r2.localRotation.eulerAngles.z * Mathf.Deg2Rad;
                    var e2 = r2.anchoredPosition + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * r2.sizeDelta.x;
                    near = Mathf.Min(near, Vector2.Distance(e2, plane.anchoredPosition));
                    near = Mathf.Min(near, Vector2.Distance(r2.anchoredPosition, plane.anchoredPosition));
                }
                float gap = Vector2.Distance(end, plane.anchoredPosition);
                Assert.Less(gap, 2f,
                    $"实线/虚线分界点（{end}）必须落在飞机位置（{plane.anchoredPosition}），实测偏差 {gap:F2}px；" +
                    $"solidSeg={solidSegs.Count} last={lastSolid.name} planes={planes} nearest={near:F2} " +
                    $"phase={snap.phase} frac={snap.mapFrac:F3} dash={dash}");
            }
            finally
            {
                MapSelection.Reset();
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void MapPlaneMarks_HaveNoGlowHalo()
        {
            var gs = NewState("T038NoGlow");
            gs.fleet.Add(MakeAircraft("B-3925"));
            gs.activeRoutes.Add(MakeRoute("T038-NG", "B-3925"));
            var map = Own(MapCanvas("T038NoGlowRoot"));

            MapWidget.DrawPlaneMarks(map.transform);

            // ★2026-10-30 CEO：「飞机图标周围是不是有光晕？？？有的话删除这个光晕」。
            // 光晕是 PlaneMark 下的 PlaneGlow 节点（径向渐变软圆斑）。它必须彻底不存在 ——
            // 不是调小、不是调透明，而是不建。
            Assert.IsNull(map.transform.Find("PlaneMark/PlaneGlow"), "飞机不得再带光晕节点");
            Assert.AreEqual(0, map.GetComponentsInChildren<Transform>(true)
                    .Count(t => t != null && t.name == "PlaneGlow"),
                "整张地图上不得存在任何 PlaneGlow 节点");
        }

        [Test]
        public void MapPlaneMarks_MarkTheSelection_AndMakeItLarger()
        {
            var gs = NewState("T038PlaneSelect");
            gs.fleet.Add(MakeAircraft("B-3921"));
            var r = MakeRoute("T038-SEL", "B-3921");
            gs.activeRoutes.Add(r);
            var map = Own(MapCanvas("T038PlaneSelectRoot"));

            MapWidget.DrawPlaneMarks(map.transform);
            var before = map.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Plane");
            Assert.IsNotNull(before, "有执飞机的航线必须画出飞机");
            float sizeBefore = before.rectTransform.sizeDelta.x;
            Assert.AreNotEqual(MapWidget.PlaneSelectedCol, before.color, "未选中时不该已经是选中色");

            MapSelection.Select("T038-SEL", 0);
            foreach (Transform child in map.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
            MapWidget.DrawPlaneMarks(map.transform);

            var after = map.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Plane");
            Assert.IsNotNull(after);
            Assert.AreEqual(MapWidget.PlaneSelectedCol, after.color, "选中的飞机必须换成选中色（橙色）");
            Assert.Greater(after.rectTransform.sizeDelta.x, sizeBefore, "选中的飞机必须比常态更大，否则看不出选了谁");
            // ★2026-10-30 CEO：「点击飞机图片后，不应该变成白色，应该是橙色」——白机身会从浅底图上消失。
            Assert.Less(after.color.b, 0.25f, $"选中态必须是橙色（蓝通道要低），实测 {after.color}");
            Assert.Greater(after.color.r - after.color.g, 0.3f, $"选中态必须偏橙（红明显高于绿），实测 {after.color}");
            // 选中态也要有暗边
            Assert.IsNotNull(map.transform.Find("PlaneMark/PlaneEdge"), "选中态同样必须有深色描边");
        }

        [Test]
        public void MapPlaneColors_UnselectedIsMediumYellow_SelectedIsOrange()
        {
            // 常态 = 中黄（色相偏黄，绿通道高）；选中 = 橙（色相偏红，红明显高于绿）。
            Color air = MapWidget.PlaneAirborneCol;
            Color sel = MapWidget.PlaneSelectedCol;

            Assert.Greater(air.g, 0.70f, $"常态必须是中黄（绿通道要高），实测 {air}");
            Assert.Greater(air.g - air.b, 0.60f, $"常态中黄不能发白，实测 {air}");
            Assert.Less(sel.g, 0.62f, $"选中态必须是橙，不是黄，实测 {sel}");
            Assert.Less(sel.b, 0.25f, $"选中态必须是橙，实测 {sel}");
            Assert.Greater(sel.r - sel.g, 0.3f, $"选中态红明显高于绿，实测 {sel}");

            // 两者必须能同时出现在一屏里而不混淆：色相差 ≥ 10°，且「中黄更亮、橙更沉」。
            // 明度（HSV 的 V）在两者上几乎相同（1.00 / 0.96），真正拉开的是**亮度**（加权 luma）——
            // 所以这里比 luma，不比 V。
            Color.RGBToHSV(air, out float ha, out _, out _);
            Color.RGBToHSV(sel, out float hs, out _, out _);
            float hueGap = Mathf.Abs(ha - hs) * 360f;
            Assert.Greater(hueGap, 10f, $"常态中黄与选中橙的色相差只有 {hueGap:F1}°，太接近");

            float lumaAir = 0.299f * air.r + 0.587f * air.g + 0.114f * air.b;
            float lumaSel = 0.299f * sel.r + 0.587f * sel.g + 0.114f * sel.b;
            Assert.Greater(lumaAir - lumaSel, 0.10f,
                $"中黄（luma {lumaAir:F2}）必须明显亮于橙（luma {lumaSel:F2}），否则小图标上看不出选中");
        }

        [Test]
        public void MapPlaneIcons_UseConspicuousColors_NotWhiteOnWhite()
        {
            // 白机身压在浅色矢量底图上等于隐形（2026-10-30 之前的实情）。
            // 在空=深琥珀、停场=青蓝，两者都必须有足够饱和度，且不能是近白色。
            Color air = MapWidget.PlaneAirborneCol;
            Color park = MapWidget.PlaneParkedCol;
            foreach (var c in new[] { air, park })
            {
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                Assert.Greater(max - min, 0.45f, $"飞机色 {c} 太灰，在底图上不够醒目");
                Assert.Less(min, 0.35f, $"飞机色 {c} 太接近白色，浅色底图上会糊掉");
            }
            Assert.AreNotEqual(air, park, "在空与停场必须是两个颜色，否则看不出飞机在不在飞");

            // ★2026-10-30 CEO 第三次定稿：「未点击的飞机图标颜色应该是中黄色」——
            // 上一版按「再深一点」做成了深橙（0.95,0.50,0），这次明确要回中黄。
            // 所以这条断言的**方向反过来了**：要黄、要够饱和，但不能发白。
            float airLuma = 0.299f * air.r + 0.587f * air.g + 0.114f * air.b;
            Assert.Greater(air.g, 0.70f, $"在空色 {air} 的绿分量 {air.g:F2} 偏低 → 不是中黄");
            Assert.Greater(air.g - air.b, 0.60f, $"在空色的黄蓝差 {air.g - air.b:F2} 太小，黄色不纯");
            Assert.Less(air.b, 0.30f, $"在空色 {air} 的蓝分量 {air.b:F2} 偏高，中黄会发白");
            Assert.Greater(airLuma, 0.60f, $"在空色 {air} 明度 {airLuma:F2} 偏低 → 那是橙不是中黄");
        }

        [Test]
        public void HomeLayerBar_RoutesButton_MatchesNeighbourBrightness()
        {
            RequireBaseline();
            var dash = Own(DashboardPanelUI.Build().gameObject);
            var buttons = dash.GetComponentsInChildren<Button>(true);
            var btn = buttons.FirstOrDefault(b => b != null && b.name == "Routes");
            var globe = buttons.FirstOrDefault(b => b != null && b.name == "Globe");
            Assert.IsNotNull(btn, "主页图层栏必须有「航线」按钮");
            Assert.IsNotNull(globe, "主页图层栏必须有地球按钮（同栏基准）");

            var icon = btn.transform.Find("Icon")?.GetComponent<Image>();
            var bg = btn.GetComponent<Image>();
            Assert.IsNotNull(icon, "航线按钮必须有一个 Icon 子节点（图标在这里上色）");

            try
            {
                // ★2026-10-30 CEO：「整块亮度不够，要和旁边的地图按钮一样的亮度」。
                // 实测亮度差来自「底色被刷成青蓝 + 图标是细淡线」。判据：
                // ① 底色必须与邻居完全相同（同一栏不能有两种底）；② 开态图标必须接近亮白。
                Assert.AreEqual(globe.GetComponent<Image>().color, bg.color,
                    "航线按钮底色必须与旁边的地图按钮一致 —— 同栏两种底色看着就是一块更暗");

                MapWidget.SetRoutesVisible(false);
                btn.onClick.Invoke();              // 关 → 开
                Assert.IsTrue(MapWidget.RoutesVisible);
                Color onIcon = icon.color;

                btn.onClick.Invoke();              // 开 → 关
                Assert.IsFalse(MapWidget.RoutesVisible);
                Color offIcon = icon.color;

                Assert.Greater(onIcon.r + onIcon.g + onIcon.b, offIcon.r + offIcon.g + offIcon.b,
                    "打开时图标必须更亮");
                Assert.Greater(onIcon.r, 0.85f, $"打开时图标必须接近亮白（实测 {onIcon}）");
                Assert.Greater(onIcon.a, 0.99f, "打开时图标必须完全不透明（关闭态是半透明灰）");
            }
            finally
            {
                MapWidget.SetRoutesVisible(false);
            }
        }

        [Test]
        public void InboxHeader_HasNoCountOrInflightCaption()
        {
            // ★2026-10-30 CEO：「删除红框中的『邮件列表多少封，在途多少封』，不需要这样的提示，多此一举」。
            //
            // 这一行原来在抬头第二行，且因为塞在 162×34 的窄盒里会折行压住「收件箱」。
            // 现在整行删除：抬头只有「收件箱」+ 两个筛选筹码，任何「统计说明」文字都不该出现。
            // 本用例锁两件事：① 那个节点不存在；② 抬头里没有任何含「邮件列表 / 在途」的文本。
            var gs = NewState("T038InboxHeader");

            // 造一封在途邮件：以前正是它让「在途 4 封（…）」这句出现
            gs.pendingInbox.Add(new PendingInboxMail
            {
                mail = new InboxMail { id = "T038-PENDING", subject = "在途件", body = "x" },
                deliverUtcTicks = gs.SimUtc.AddHours(6).Ticks,
            });

            var canvasGo = Own(new GameObject("T038InboxCanvas", typeof(RectTransform), typeof(Canvas)));
            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(1920f, 1080f);

            var panel = Own(InboxPanelUI.Build().gameObject);
            var prt = panel.GetComponent<RectTransform>();
            prt.SetParent(canvasRt, false);
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = prt.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);

            Assert.IsNotNull(prt.Find("Title"), "抬头必须有「收件箱」标题");
            Assert.IsNull(prt.Find("Count"), "抬头不得再有统计行（节点已删除）");

            // 抬头区里不得出现任何统计文案。只查**抬头自己这一层的直接文本子节点**
            // （Title / ChipOpen / ChipUnread 的文字）：列表里的日期分组行本来就写着
            // 「2026年11月01日 星期日 (3封)」，那是列表内容，不该被这条断言误伤。
            var captions = prt.Cast<Transform>()
                .Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                .Where(t => t != null && !string.IsNullOrEmpty(t.text))
                .Select(t => t.text)
                .ToList();
            Assert.IsNotEmpty(captions, "抬头本身必须有文字（标题/筹码）");
            foreach (var txt in captions)
            {
                Assert.IsFalse(txt.Contains("邮件列表"),
                    $"抬头不得出现「邮件列表（N）」这类统计提示，实测：{txt}");
                Assert.IsFalse(txt.Contains("在途"),
                    $"抬头不得出现「在途 N 封…」这类送达提示，实测：{txt}");
                Assert.IsFalse(txt.Contains("筛选："),
                    $"抬头也不该再用文字说明当前筛选（筹码高亮已表达），实测：{txt}");
            }
        }

        [Test]
        public void MapFlightCard_ShowsSelectedFlight_AndHidesWhenDeselected()
        {
            var gs = NewState("T038FlightCard");
            gs.fleet.Add(MakeAircraft("B-3931"));
            var r = MakeRoute("T038-CARD", "B-3931", "08:00", "10:30");
            r.flightNumber = "TQ3931";
            gs.activeRoutes.Add(r);
            gs.SetSimUtc(new DateTime(2026, 1, 5, 9, 0, 0) - GameState.LocalUtcOffset);

            var host = Own(MapCanvas("T038FlightCardRoot"));
            var card = MapFlightCard.Build(host.transform);

            card.Refresh();
            Assert.IsFalse(card.gameObject.activeSelf, "没选中任何飞机时，航班卡必须隐藏");

            MapSelection.Select("T038-CARD", 0);
            card.Refresh();
            Assert.IsTrue(card.gameObject.activeSelf, "选中后航班卡必须出现");

            var texts = card.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Where(t => t != null && !string.IsNullOrEmpty(t.text)).Select(t => t.text).ToList();
            Assert.IsTrue(texts.Any(t => t.Contains("TQ3931")), $"卡片必须显示航班号，实际：{string.Join(" | ", texts)}");
            Assert.IsTrue(texts.Any(t => t.Contains("PEK")), $"卡片必须显示出发机场三字码，实际：{string.Join(" | ", texts)}");
            Assert.IsTrue(texts.Any(t => t.Contains("SHA")), $"卡片必须显示到达机场三字码，实际：{string.Join(" | ", texts)}");
            Assert.IsTrue(texts.Any(t => t.Contains("B-3931")), $"卡片必须显示机号，实际：{string.Join(" | ", texts)}");

            MapSelection.Clear();
            card.Refresh();
            Assert.IsFalse(card.gameObject.activeSelf, "取消选中后航班卡必须收起");
        }

        [Test]
        public void MapSelection_IgnoresRepeatClicks_ButNotifiesOnChange()
        {
            MapSelection.Reset();
            int fired = 0;
            Action onChanged = () => fired++;
            MapSelection.Changed += onChanged;
            try
            {
                MapSelection.Select("R-A", 0);
                Assert.AreEqual(1, fired, "第一次选中必须通知（地图要重绘高亮）");
                MapSelection.Select("R-A", 0);
                Assert.AreEqual(1, fired, "重复点同一架不得重复通知 —— 每通知一次整张地图重建一次");

                MapSelection.Select("R-B", 2);
                Assert.AreEqual(2, fired, "换一架必须通知");
                Assert.AreEqual("R-B", MapSelection.RouteId);
                Assert.AreEqual(2, MapSelection.FlightIndex);

                MapSelection.Clear();
                Assert.AreEqual(3, fired, "取消选中必须通知（卡片要收起）");
                Assert.IsFalse(MapSelection.Has);
            }
            finally
            {
                MapSelection.Changed -= onChanged;
                MapSelection.Reset();
            }
        }

        [Test]
        public void MapPlaneMarks_UseTheAircraftThatActuallyFlies()
        {
            var gs = NewState("T038PlaneIcon");
            var ac = MakeAircraft("B-3902");
            ac.type = "B787-9";                       // 机队里的真身是宽体
            gs.fleet.Add(ac);

            var r = MakeRoute("T038-P1", "B-3902");
            r.aircraftType = "A321";                  // 航线字段还是旧的窄体（换机后没同步）
            r.SyncFlightsFromPrimary();
            gs.activeRoutes.Add(r);
            // 08:00–10:30 的班，本地时间放在 09:00（本班在空）
            gs.SetSimUtc(new DateTime(2026, 1, 5, 9, 0, 0) - GameState.LocalUtcOffset);

            var map = Own(MapCanvas("T038PlaneIconRoot"));
            MapWidget.DrawPlaneMarks(map.transform);

            var icons = map.GetComponentsInChildren<Image>(true)
                .Where(i => i != null && i.name == "Plane").ToList();
            Assert.IsNotEmpty(icons, "有执飞机的航线必须在地图上画出一架飞机");
            Assert.AreSame(MapAircraftIcons.SpriteFor("B787-9"), icons[0].sprite,
                "图标必须按机队里这架机的机型画（B787-9 宽体），不能沿用航线上的旧字段（A321 窄体）");
        }

        [Test]
        public void MapPlaneIcons_DifferPerAircraftFamily()
        {
            var narrow = MapAircraftIcons.SpriteFor("A321");
            var wide = MapAircraftIcons.SpriteFor("B787-9");
            var quad = MapAircraftIcons.SpriteFor("A380-800");
            var prop = MapAircraftIcons.SpriteFor("ATR72-600");
            var regional = MapAircraftIcons.SpriteFor("CRJ900");

            Assert.IsNotNull(narrow);
            Assert.AreNotSame(narrow, wide, "窄体与宽体不能共用同一张图 —— 那就是旧的「全图一个白三角」");
            Assert.AreNotSame(wide, quad, "宽体与四发必须是两张图（发动机台数不同）");
            Assert.AreNotSame(quad, prop, "四发与涡桨必须是两张图（后掠翼 vs 平直翼 + 桨盘）");
            Assert.AreNotSame(prop, regional, "涡桨与尾吊支线必须是两张图（机翼 vs 尾吊发动机）");

            // 尺寸按翼展拉开：A380 的图标必须明显大于 CRJ
            Assert.Greater(MapAircraftIcons.SizeFor("A380-800"), MapAircraftIcons.SizeFor("A321"));
            Assert.Greater(MapAircraftIcons.SizeFor("A321"), MapAircraftIcons.SizeFor("CRJ900"));
        }

        [Test]
        public void HomeLayerBar_HasRoutesButton_ThatDrivesTheMapLayer()
        {
            RequireBaseline();
            var dash = Own(DashboardPanelUI.Build().gameObject);
            var btn = dash.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b != null && b.name == "Routes");
            Assert.IsNotNull(btn, "主页图层栏必须有「航线」按钮（否则玩家永远打不开航线）");

            try
            {
                MapWidget.SetRoutesVisible(false);
                btn.onClick.Invoke();
                Assert.IsTrue(MapWidget.RoutesVisible, "点一下「航线」按钮必须打开航线图层");
                btn.onClick.Invoke();
                Assert.IsFalse(MapWidget.RoutesVisible, "再点一下必须关掉");
            }
            finally
            {
                MapWidget.SetRoutesVisible(false);
            }
        }

        // —————————————————————————————————————————————————————————————
        // ⑨ 1× 实时：换算表 + 真的按现实秒走表（61 秒实测）
        // —————————————————————————————————————————————————————————————

        [Test]
        public void SpeedLevels_MapToTheDocumentedRealTimeRatios()
        {
            Assert.AreEqual(1f / 60f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L1], 1e-6f,
                "1×：现实 1 分钟 = 游戏 1 分钟");
            Assert.AreEqual(1f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L2], 1e-6f,
                "2×：现实 1 分钟 = 游戏 1 小时");
            Assert.AreEqual(3f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L3], 1e-6f,
                "3×：现实 1 分钟 = 游戏 3 小时");
        }

        /// <summary>
        /// T05.7 的核心承诺是「1× 下 4 游戏小时就是 4 现实小时」。这条用例不看常量，
        /// 而是真的让时钟跑 61 秒现实时间，量游戏时钟走了多少秒，并确认 4 小时的回执没有提前到。
        /// </summary>
        [UnityTest]
        public IEnumerator AtOneX_SimClockTracksRealSeconds_AndFourHourReplyDoesNotArriveInAMinute()
        {
            var gs = NewState("T038Pace");
            Own(new GameObject("T038PaceClock")).AddComponent<TimeManager>();
            gs.SetSimUtc(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc));
            gs.inbox.Clear(); gs.pendingInbox.Clear();
            GameSpeed.Set(GameSpeed.L1);

            InboxManager.Schedule(gs, new InboxMail
            {
                id = "t038-pace", sender = "运营部", role = "回执", glyph = "运",
                subject = "节奏探针", body = "", action = "notice", kind = "report",
                status = "approved", receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 2
            }, InboxCadence.ReplyReady(gs.SimUtc));
            Assert.IsFalse(gs.inbox.Any(m => m.id == "t038-pace"));

            var before = gs.SimUtc;
            float t0 = Time.realtimeSinceStartup;
            TimeManager.SimulationEnabled = true;
            while (Time.realtimeSinceStartup - t0 < 61f) yield return null;
            TimeManager.SimulationEnabled = false;
            double realSeconds = Time.realtimeSinceStartup - t0;
            double simSeconds = (gs.SimUtc - before).TotalSeconds;

            Assert.GreaterOrEqual(simSeconds, 55.0,
                $"1× 下 {realSeconds:F1} 秒现实应走约同样多的游戏秒，实得 {simSeconds:F1} —— 表在被快进");
            Assert.LessOrEqual(simSeconds, realSeconds + 5.0,
                $"1× 不是慢放：{realSeconds:F1} 秒现实走了 {simSeconds:F1} 游戏秒");

            Assert.IsFalse(gs.inbox.Any(m => m.id == "t038-pace"),
                "4 游戏小时的回执不得在 1 分钟现实时间内出现");
            Assert.IsTrue(gs.pendingInbox.Any(p => p?.mail != null && p.mail.id == "t038-pace"),
                "探针应还在待发队列里");
            yield return null;
        }

        static DateTime Utc(int hour, int minute)
            => new DateTime(2026, 10, 1, hour, minute, 0) - GameState.LocalUtcOffset;
    }
}
