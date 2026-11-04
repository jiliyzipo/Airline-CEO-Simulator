using System.Collections;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// STG 战略 + MNT 机务 PlayMode（Phase 2 Week 9）。
    /// 覆盖：导航工厂、战略/维修数值、Manager 回写 GameState。
    /// </summary>
    public class StgMntPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 STG + MNT 全部子页 ——

        [UnityTest]
        public IEnumerator RuntimeFactories_BuildAllSubpages()
        {
            var host = new GameObject("StgMntRuntimeHost", typeof(RectTransform));

            string[] stgPages = { "n76_机队与运力规划", "n77_航权与时刻槽", "n111_时刻二级市场", "n78_联盟与战略合作", "n79_战略投资与并购", "n80_战略研究与预案", "n35_新航线_RPM_评估台", "n102_谈判弹窗", "n108_临空商业" };
            foreach (var page in stgPages)
            {
                var go = StrategyRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<StrategySubpagePanel>(), page + " 应挂 StrategySubpagePanel");
            }

            string[] mntPages = { "n81_航线与定检维修", "n82_工程技术与计划", "n83_航材与供应链", "n84_安全质量与适航", "n85_维修培训与资质", "n105_订单确认" };
            foreach (var page in mntPages)
            {
                var go = MaintenanceRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<MaintenanceSubpagePanel>(), page + " 应挂 MaintenanceSubpagePanel");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— n77：航权不再由本页发起，时刻槽两条渠道保留 ——

        /// <summary>
        /// 「航权与时刻槽」页（2026-11-01 用户裁定）。
        ///
        /// 撤掉「申请航权」「提交航权竞标」两颗按钮：它们是同一个硬编码 PEK—SIN 的重复 stub，
        /// 而航权真正的来源是准入流水线（递件 → 许可审查发现缺额度 → 运营部自动请示收件箱）。
        /// 本用例钉住「撤干净了、也没误伤时刻槽渠道」——
        /// 撤按钮最容易的翻车方式是把旁边那两颗也一起删掉。
        /// </summary>
        [UnityTest]
        public IEnumerator RightsPage_HasNoRightButtons_ButKeepsSlotChannels()
        {
            var host = new GameObject("RightsPageHost", typeof(RectTransform));
            // 页面渲染直接读 GameState.Instance；夹具默认不起 GameState，
            // 不起就等于「页面什么都没画」——那样「按钮不存在」的断言会假绿。
            var gsHost = new GameObject("RightsPageGs");
            var gs = gsHost.AddComponent<GameState>();
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            gs.fleet = new System.Collections.Generic.List<Aircraft>();
            StrategyManager.EnsureState(gs);

            var go = StrategyRuntimeFactory.TryCreate("n77_航权与时刻槽", host.transform);
            Assert.IsNotNull(go, "应能构建 n77 航权与时刻槽");
            yield return null;

            // 先确认这一页真的画出来了（否则下面「按钮消失」的断言毫无意义）
            Assert.IsNotNull(FindChild(go.transform, "SlotFree"),
                "「协调池申请」必须保留 —— 若它都没有，说明页面根本没渲染，本用例不成立");
            Assert.IsNull(FindChild(go.transform, "Slot"),
                "「购入时刻槽」应从本页移除：付费插队是二级市场的事（受池容量约束的免费渠道才留在这里）");

            Assert.IsNull(FindChild(go.transform, "Apply"),
                "「申请航权」按钮必须消失：航权由准入流水线自动请示，页面上不再手动发起");
            Assert.IsNull(FindChild(go.transform, "Bid"),
                "「提交航权竞标」按钮必须消失：它原本与「申请航权」是同一个硬编码 stub");

            // ★2026-11-02：目标选择改成两层（机场 / 时刻各自一对 ◀▶）。
            // 钉住这两层都存在 —— 只留时钟那一对就成了「在哪个机场都看不出来」的老问题。
            Assert.IsNotNull(FindChild(go.transform, "SlotAptPrev"), "缺机场选择层：找不到「上一个机场」");
            Assert.IsNotNull(FindChild(go.transform, "SlotAptNext"), "缺机场选择层：找不到「下一个机场」");
            Assert.IsNotNull(FindChild(go.transform, "SlotAirport"), "缺机场读数：看不出当前选中哪个机场");
            Assert.IsNotNull(FindChild(go.transform, "SlotTarget"), "缺时刻读数：看不出当前选中哪一刻");
            Assert.IsNotNull(FindChild(go.transform, "SlotPool"), "缺池况读数：看不出该时段还剩几条");

            Object.Destroy(host);
            Object.Destroy(gsHost);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SlotMarketPage_HasBothSidesAndActions()
        {
            var host = new GameObject("SlotMarketHost", typeof(RectTransform));
            var gsHost = new GameObject("SlotMarketGs");
            var gs = gsHost.AddComponent<GameState>();
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            gs.fleet = new System.Collections.Generic.List<Aircraft>();
            gs.cash = 3000f;
            StrategyManager.EnsureState(gs);

            var go = StrategyRuntimeFactory.TryCreate("n111_时刻二级市场", host.transform);
            Assert.IsNotNull(go, "应能构建 n111 时刻二级市场");
            yield return null;

            // 两侧列表 + 三个动作都在 —— 少一侧就成了「只能买不能卖」或反之，与需求不符。
            Assert.IsNotNull(FindChild(go.transform, "MkShelf"), "缺左侧「他航待售」列表");
            Assert.IsNotNull(FindChild(go.transform, "MkMine"), "缺右侧「我司待售」列表");
            Assert.IsNotNull(FindChild(go.transform, "MkBuy"), "缺「购买时刻槽」");
            Assert.IsNotNull(FindChild(go.transform, "MkList"), "缺「挂牌时刻槽」");
            Assert.IsNotNull(FindChild(go.transform, "MkWithdraw"), "缺「撤回挂牌」");
            Assert.IsNotNull(FindChild(go.transform, "Cash"), "缺现金读数 —— 买不起时玩家无从判断");

            // 二级市场页不能反过来长出 n77 的协调池按钮：两条渠道必须分页。
            Assert.IsNull(FindChild(go.transform, "SlotFree"), "协调池申请不该出现在二级市场页");
            // 2026-11-02 用户裁定：同排那颗「去协调池申请（免费）」也已删除，本页只做买卖。
            Assert.IsNull(FindChild(go.transform, "MkGoPool"),
                "「去协调池申请」按钮已按用户要求删除 —— 取得时刻槽的入口留在「持仓与在办」页签");

            Object.Destroy(host);
            Object.Destroy(gsHost);
            yield return null;
        }

        /// <summary>
        /// 2026-11-02 用户裁定：
        /// ① 删掉「先证后槽」那段文字窗（「不需要文字提示，多此一举」）；
        /// ② 二级市场从模块级页签收进 n77 的**二级标签**。
        /// 两件事一起钉住：两个标签下都不得再有 Hint / HintBox / MkHintBox 这类说明窗，
        /// 而标签条 RightsTab0 / RightsTab1 必须存在（否则玩家没有入口切到市场）。
        /// </summary>
        [UnityTest]
        public IEnumerator RightsPage_HasNoHintWindow_AndCarriesTwoSubTabs()
        {
            var gsHost = new GameObject("RightsSubTabGs");
            var gs = gsHost.AddComponent<GameState>();
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            gs.fleet = new System.Collections.Generic.List<Aircraft>();
            gs.cash = 5000f;
            StrategyManager.EnsureState(gs);

            string[] pages = { "n77_航权与时刻槽", "n111_时刻二级市场" };
            for (int p = 0; p < pages.Length; p++)
            {
                var host = new GameObject("SubTabHost" + p, typeof(RectTransform));
                host.GetComponent<RectTransform>().sizeDelta = new Vector2(1280f, 720f);
                var go = StrategyRuntimeFactory.TryCreate(pages[p], host.transform);
                Assert.IsNotNull(go, "应能构建 " + pages[p]);
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;

                string tag = "[" + pages[p] + "]";
                Assert.IsNull(FindChild(go.transform, "Hint"), tag + " 说明文字仍在 —— 用户已要求删除");
                Assert.IsNull(FindChild(go.transform, "HintBox"), tag + " 说明窗仍在 —— 用户已要求删除");
                Assert.IsNull(FindChild(go.transform, "MkHintBox"), tag + " 市场说明窗仍在 —— 同一类多余提示");

                Assert.IsNotNull(FindChild(go.transform, "RightsTab0"), tag + " 缺二级标签「持仓与在办」");
                Assert.IsNotNull(FindChild(go.transform, "RightsTab1"), tag + " 缺二级标签「时刻二级市场」");

                Object.Destroy(host);
                yield return null;
            }

            Object.Destroy(gsHost);
            yield return null;
        }

        /// <summary>
        /// 二级标签必须真的能切换：默认停在「持仓与在办」（有协调池申请条、无市场货架），
        /// 点「时刻二级市场」后换成货架 + 买卖按钮，且协调池按钮同时消失 ——
        /// 两条渠道仍在同一面板里分屏，不能同时出现（否则「池满也能买」与「池满不能申请」互相拆台）。
        /// </summary>
        [UnityTest]
        public IEnumerator RightsPage_SubTabSwitchesPoolAndMarket()
        {
            var gsHost = new GameObject("SubTabSwitchGs");
            var gs = gsHost.AddComponent<GameState>();
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            gs.fleet = new System.Collections.Generic.List<Aircraft>();
            gs.cash = 5000f;
            StrategyManager.EnsureState(gs);

            var host = new GameObject("SubTabSwitchHost", typeof(RectTransform));
            host.GetComponent<RectTransform>().sizeDelta = new Vector2(1280f, 720f);
            var go = StrategyRuntimeFactory.TryCreate("n77_航权与时刻槽", host.transform);
            Assert.IsNotNull(go, "应能构建 n77 航权与时刻槽");
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.IsNotNull(FindChild(go.transform, "SlotFree"), "默认标签应停在「持仓与在办」：缺协调池申请条");
            Assert.IsNull(FindChild(go.transform, "MkShelf"), "默认标签不该画出市场货架");

            ClickChild(go.transform, "RightsTab1");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsNotNull(FindChild(go.transform, "MkShelf"), "点「时刻二级市场」后应出现他航待售货架");
            Assert.IsNotNull(FindChild(go.transform, "MkBuy"), "点「时刻二级市场」后应出现购买按钮");
            Assert.IsNull(FindChild(go.transform, "SlotFree"), "市场标签下不该同时出现协调池申请 —— 两条渠道分屏");

            ClickChild(go.transform, "RightsTab0");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsNotNull(FindChild(go.transform, "SlotFree"), "切回「持仓与在办」后协调池申请条应回来");
            Assert.IsNull(FindChild(go.transform, "MkShelf"), "切回后市场货架应撤掉");

            Object.Destroy(host);
            Object.Destroy(gsHost);
            yield return null;
        }

        /// <summary>
        /// 2026-11-02 用户裁定：「将航权与时刻槽框架放大到最大的红框」。
        /// 旧版内嵌时套了两层内边距（模块宿 1% + 面板自己的卡片 6%/8%），
        /// 等于把模块页签已有的边框又画一遍，四周白白空掉约 15%。
        /// 断言口径：embedded 面板的内容区必须铺到宿主 95% 以上，且不再有嵌套 Card 边框。
        /// </summary>
        [UnityTest]
        public IEnumerator EmbeddedRightsPanel_FillsHostArea()
        {
            var gsHost = new GameObject("EmbedFillGs");
            var gs = gsHost.AddComponent<GameState>();
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            gs.fleet = new System.Collections.Generic.List<Aircraft>();
            StrategyManager.EnsureState(gs);

            var host = new GameObject("EmbedFillHost", typeof(RectTransform));
            var hostRt = host.GetComponent<RectTransform>();
            hostRt.sizeDelta = new Vector2(1280f, 720f);

            var panel = StrategySubpagePanel.Build("n77", "航权与时刻槽", embedded: true);
            panel.transform.SetParent(host.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.IsNull(FindChild(panel.transform, "Card"),
                "内嵌模式不该再有嵌套卡片 —— 那正是「框架没吃到最大红框」的原因");
            Assert.IsNull(FindChild(panel.transform, "Back"), "内嵌模式不该再画页内「← 返回」");

            var content = FindChild(panel.transform, "Content") as RectTransform;
            Assert.IsNotNull(content, "缺内容区");
            var r = RectOf(content);
            Assert.Greater(r.width, 1280f * 0.95f, $"内容区宽 {r.width:F0}px 未铺满 1280px 宿主（应 ≥ 95%）");
            Assert.Greater(r.height, 720f * 0.95f, $"内容区高 {r.height:F0}px 未铺满 720px 宿主（应 ≥ 95%）");

            // 顶部二级标签条与底部协调池条不得互相侵占。
            var tabs = FindChild(panel.transform, "RightsTabs") as RectTransform;
            var slotBar = FindChild(panel.transform, "SlotBar") as RectTransform;
            Assert.IsNotNull(tabs, "缺二级标签条");
            Assert.IsNotNull(slotBar, "缺底部协调池条");
            Assert.IsFalse(RectOf(tabs).Overlaps(RectOf(slotBar)),
                "二级标签条与协调池条重叠");

            Object.Destroy(host);
            Object.Destroy(gsHost);
            yield return null;
        }

        static void ClickChild(Transform root, string name)
        {
            var t = FindChild(root, name);
            Assert.IsNotNull(t, "找不到可点击控件 " + name);
            var btn = t.GetComponent<UnityEngine.UI.Button>();
            Assert.IsNotNull(btn, name + " 上没有 Button 组件");
            btn.onClick.Invoke();
        }

        static Rect RectOf(RectTransform rt)
        {
            if (rt == null) return new Rect();
            var world = new Vector3[4];
            rt.GetWorldCorners(world);
            float xMin = Mathf.Min(world[0].x, world[2].x);
            float xMax = Mathf.Max(world[0].x, world[2].x);
            float yMin = Mathf.Min(world[0].y, world[2].y);
            float yMax = Mathf.Max(world[0].y, world[2].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static Transform FindChild(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
                var deep = FindChild(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        // —— 数值：航权竞标 / 时刻槽价值 / 投资 IRR / 定检成本 / 库存健康 ——

        [UnityTest]
        public IEnumerator StrategyMaintenanceEconomics_Scoring()
        {
            // 航权竞标：经济性/投资承诺/政府关系/战略契合更高 → 分数更高
            float strongBid = StrategyEconomics.RouteRightBidScore(90f, 85f, 80f, 90f);
            float weakBid = StrategyEconomics.RouteRightBidScore(30f, 20f, 20f, 30f);
            Assert.Greater(strongBid, weakBid);

            // 时刻槽价值：枢纽 > 干线 > 支线；早班 > 夜班
            Assert.Greater(StrategyEconomics.SlotValueWan(0, 0), StrategyEconomics.SlotValueWan(2, 3));

            // 联盟收益随等级/航线增加
            Assert.Greater(StrategyEconomics.AllianceRevenueLiftWan(3, 10, 5),
                StrategyEconomics.AllianceRevenueLiftWan(1, 2, 0));

            // 投资 IRR 为正（NPV>0）
            Assert.Greater(StrategyEconomics.InvestmentIrr(1200f, 2000f, 5), 0f);

            // 地缘风险加成
            Assert.AreEqual(0.20f, StrategyEconomics.GeoRiskSurcharge(80), 0.001f);
            Assert.AreEqual(0f, StrategyEconomics.GeoRiskSurcharge(20), 0.001f);

            // 定检成本：D > C > B > A
            Assert.Greater(MaintenanceEconomics.CheckCostWan("D", 10), MaintenanceEconomics.CheckCostWan("C", 10));
            Assert.Greater(MaintenanceEconomics.CheckCostWan("C", 10), MaintenanceEconomics.CheckCostWan("B", 10));

            // 维修策略可用率：全面预防 > 智能预测 > 最低合规
            Assert.Greater(MaintenanceEconomics.AvailabilityByStrategy(0), MaintenanceEconomics.AvailabilityByStrategy(2));
            Assert.Greater(MaintenanceEconomics.AvailabilityByStrategy(2), MaintenanceEconomics.AvailabilityByStrategy(1));

            // 库存健康度：缺料越多越低
            Assert.Greater(MaintenanceEconomics.StockHealth(120, 90, 0),
                MaintenanceEconomics.StockHealth(120, 90, 3));

            yield return null;
        }

        // —— Manager 回写：航权/联盟/投资/预案 + 维修/航材/定检 ——

        [UnityTest]
        public IEnumerator StrategyManager_WritesState()
        {
            var gsGo = new GameObject("StgGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("StgManager");
            var mgr = mgrGo.AddComponent<StrategyManager>();

            // 时刻槽购买回写
            Assert.IsTrue(mgr.AcquireSlot("PEK", 1, out _));
            Assert.AreEqual(1, gs.stg.slots.Count);

            // 航权申请回写（消耗政府关系）
            float govBefore = gs.stg.governmentRelation;
            Assert.IsTrue(mgr.ApplyRouteRight("PEK", "SIN", 5, out _));
            Assert.Less(gs.stg.governmentRelation, govBefore);
            Assert.AreEqual(1, gs.stg.rights.Count);

            // 联盟升级回写（需机队 ≥20，先注入机队）
            for (int i = 0; i < 20; i++)
                gs.fleet.Add(new Aircraft { registration = "B-" + (1001 + i) });
            Assert.IsTrue(mgr.UpgradeAlliance(out _));
            Assert.Greater(gs.stg.alliance.tier, 0);

            // 投资立项：只入待批，不立刻扣款；执行后才扣资本
            Assert.IsTrue(mgr.ProposeInvestment(StrategyManager.CatalogHub(), out _));
            Assert.AreEqual(1, gs.stg.investments.Count);
            Assert.AreEqual("待批", gs.stg.investments[0].stage);
            Assert.IsFalse(gs.stg.investments[0].approved);
            float cashBefore = gs.cash;
            Assert.IsTrue(mgr.ExecuteInvestment(gs.stg.investments[0].id, out _));
            Assert.AreEqual("执行", gs.stg.investments[0].stage);
            Assert.IsTrue(gs.stg.investments[0].approved);
            Assert.Less(gs.cash, cashBefore);

            // 预案回写
            Assert.IsTrue(mgr.SaveContingency(new ContingencyPlan { trigger = "油价暴涨", autoMeasure = "锁定套保", lossReduction = 0.35f }, out _));
            Assert.AreEqual(1, gs.stg.contingencyPlans.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AirportCommerce_InvestAndRefresh()
        {
            var gsGo = new GameObject("CommerceGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 100000f;

            // EnsureState 初始化四类设施占位
            var state = AirportCommerceManager.EnsureState(gs);
            Assert.AreEqual(4, state.facilities.Count, "应初始化四类商业设施");
            Assert.AreEqual(0, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level);

            // 注入高吞吐（模拟已解锁高端档），绕开 Refresh 重算
            state.unlockedTier = 3;
            state.hubAnnualThroughputWan = 3500f;

            // 投资免税店 L1（300 万）成功回写
            float cashBefore = gs.cash;
            Assert.IsTrue(AirportCommerceManager.InvestFacility(gs, AirportCommerceEconomics.DutyFree, 1, out _));
            Assert.AreEqual(1, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level);
            Assert.Less(gs.cash, cashBefore, "投资应扣减现金");

            // 降档后无法继续升级（解锁限制）
            state.unlockedTier = 0;
            Assert.IsFalse(AirportCommerceManager.InvestFacility(gs, AirportCommerceEconomics.DutyFree, 2, out _));
            Assert.AreEqual(1, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level, "未解锁时不应升级");

            // 吞吐估算公式：1 条 PEK 航线 周7班 × 180座 × 55% 客座
            gs.hubIata = "PEK";
            gs.fleet = new System.Collections.Generic.List<Aircraft>
            {
                new Aircraft { registration = "B-1001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy }
            };
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>
            {
                new RouteData { origin = "PEK", destination = "PVG", weeklyFlights = 7 }
            };
            gs.loadFactorPercent = 55f;
            AirportCommerceManager.Refresh(gs);
            float expected = AirportCommerceEconomics.HubAnnualThroughputWan(7, 180, 0.55f);
            Assert.AreEqual(expected, state.hubAnnualThroughputWan, 0.01f, "吞吐应与公式一致");

            Object.Destroy(gsGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MaintenanceManager_WritesState()
        {
            var gsGo = new GameObject("MntGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("MntManager");
            var mgr = mgrGo.AddComponent<MaintenanceManager>();

            // 维修策略回写
            Assert.IsTrue(mgr.SetStrategy(0, out _));
            Assert.AreEqual(0, gs.mnt.strategy);
            Assert.AreEqual(MaintenanceEconomics.AvailabilityByStrategy(0), gs.mnt.fleetAvailability, 0.01f);

            // 采购订单创建 + 确认（入库）
            Assert.IsTrue(mgr.CreateOrder("PN-ENG-001", "OEM 原厂", 5, 120000f, false, out _));
            Assert.AreEqual(1, gs.mnt.orders.Count);
            Assert.IsTrue(mgr.ConfirmOrder(gs.mnt.orders[0].orderNo, out _));
            Assert.AreEqual("已下单", gs.mnt.orders[0].status);
            Assert.AreEqual(1, gs.mnt.parts.Count);
            Assert.AreEqual(5, gs.mnt.parts[0].quantity);

            Assert.IsTrue(mgr.CancelOrder("PO-MISSING", out _) == false);
            Assert.IsTrue(mgr.CreateOrder("PN-HYD-002", "OEM 原厂", 2, 80000f, true, out _));
            string cancelNo = gs.mnt.orders[gs.mnt.orders.Count - 1].orderNo;
            Assert.IsTrue(mgr.CancelOrder(cancelNo, out _));
            Assert.AreEqual("已取消", gs.mnt.orders.Find(o => o.orderNo == cancelNo).status);

            // OEM 关系升级回写
            int starsBefore = gs.mnt.oems[0].stars;
            Assert.IsTrue(mgr.UpgradeOem("波音", out _));
            Assert.Greater(gs.mnt.oems[0].stars, starsBefore);

            // 定检排程回写
            Assert.IsTrue(mgr.ScheduleCheck("B-1001", "C", 3, out _));
            Assert.AreEqual(1, gs.mnt.checks.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }
    }
}
