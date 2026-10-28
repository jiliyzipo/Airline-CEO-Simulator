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
