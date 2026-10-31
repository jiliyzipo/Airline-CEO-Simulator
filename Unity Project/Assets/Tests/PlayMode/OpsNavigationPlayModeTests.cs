using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    public class OpsNavigationPlayModeTests : IsolatedPlayModeFixture
    {
        [UnityTest]
        public IEnumerator OpsSubpage_NavigateAndBack_WorksWithPrefab()
        {
            var go = new GameObject("OpsNav");
            var router = go.AddComponent<NavigationRouter>();
            var dummy = new GameObject("Dummy_n42");
            var week = ScriptableObject.CreateInstance<UIConfig>();
            week.pageId = "n42_航班周总计划";
            week.title = "航班周总计划";
            week.parentId = "n13_运营部";
            week.prefab = dummy;
            router.SetCatalog(new[] { week });

            Assert.IsTrue(router.Navigate("n42_航班周总计划"));
            Assert.AreEqual("n42_航班周总计划", router.CurrentPageId);
            Assert.IsTrue(router.Back());
            Assert.AreEqual(0, router.StackDepth);

            Object.Destroy(go);
            Object.Destroy(week);
            Object.Destroy(dummy);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MapShell_OpsPage_MapsToLegacyIndexZero()
        {
            Assert.AreEqual(0, MVPBootstrap.MapShellPageToLegacyNav("n13_运营部"));
            Assert.AreEqual(-1, MVPBootstrap.MapShellPageToLegacyNav("n42_航班周总计划"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpsStack_CloseOverlay_ClearsAllLayers()
        {
            var go = new GameObject("OpsOverlay");
            var router = go.AddComponent<NavigationRouter>();
            var d42 = new GameObject("Dummy_n42");
            var d43 = new GameObject("Dummy_n43");
            var week = ScriptableObject.CreateInstance<UIConfig>();
            week.pageId = "n42_航班周总计划";
            week.prefab = d42;
            var detail = ScriptableObject.CreateInstance<UIConfig>();
            detail.pageId = "n43_航班详细计划";
            detail.prefab = d43;
            router.SetCatalog(new[] { week, detail });

            Assert.IsTrue(router.Navigate("n42_航班周总计划"));
            Assert.IsTrue(router.Navigate("n43_航班详细计划"));
            Assert.AreEqual(2, router.StackDepth);
            router.CloseOverlay();
            Assert.AreEqual(0, router.StackDepth);
            Assert.IsNull(router.CurrentPageId);

            Object.Destroy(go);
            Object.Destroy(week);
            Object.Destroy(detail);
            Object.Destroy(d42);
            Object.Destroy(d43);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpsStack_WeekThenDetail_BackOneLevel()
        {
            var go = new GameObject("OpsWeekDetail");
            var router = go.AddComponent<NavigationRouter>();
            var d42 = new GameObject("Dummy_n42");
            var d43 = new GameObject("Dummy_n43");
            var week = ScriptableObject.CreateInstance<UIConfig>();
            week.pageId = "n42_航班周总计划";
            week.prefab = d42;
            var detail = ScriptableObject.CreateInstance<UIConfig>();
            detail.pageId = "n43_航班详细计划";
            detail.prefab = d43;
            router.SetCatalog(new[] { week, detail });

            Assert.IsTrue(router.Navigate("n42_航班周总计划"));
            OpsSubpageContext.SelectedRouteId = "PEK-SHA-demo";
            Assert.IsTrue(router.Navigate("n43_航班详细计划"));
            Assert.AreEqual("n43_航班详细计划", router.CurrentPageId);
            Assert.IsTrue(router.Back());
            Assert.AreEqual("n42_航班周总计划", router.CurrentPageId);

            Object.Destroy(go);
            Object.Destroy(week);
            Object.Destroy(detail);
            Object.Destroy(d42);
            Object.Destroy(d43);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpsRuntimeFactory_BuildsN98AndN36()
        {
            var host = new GameObject("OpsRuntimeHost", typeof(RectTransform));
            var n98 = OpsRuntimeFactory.TryCreate("n98_航线航班环选择器", host.transform);
            Assert.IsNotNull(n98);
            Assert.IsNotNull(n98.GetComponent<OpsSubpagePanel>());

            var n36 = OpsRuntimeFactory.TryCreate("n36_航班环构建器", host.transform);
            Assert.IsNotNull(n36);
            Assert.IsNotNull(n36.GetComponent<OpsSubpagePanel>());

            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpsIrrops_ResolveAppliesCashAndOtp()
        {
            var outcome = OpsIrropsLogic.Resolve(
                OpsIrropsLogic.IrropsKind.Aog,
                OpsIrropsLogic.Choice.A);
            Assert.AreEqual(-20f, outcome.cashDeltaWan, 0.01f);
            Assert.AreEqual(0f, outcome.onTimeDelta, 0.01f);
            Assert.IsFalse(string.IsNullOrEmpty(outcome.label));
            Assert.AreEqual("停飞", OpsIrropsLogic.KindLabel(OpsIrropsLogic.IrropsKind.Aog));
            yield return null;
        }

        /// <summary>
        /// n98 选到「不可排班的飞机」时必须有反馈（CEO 2026-10-13 反馈：点 B-2006/B-2007 下面什么都不显示）。
        /// 旧实现两处一起把点击变成哑巴：机号按钮 interactable = false，且 ResolveSelection 把选择静默切走。
        /// 本用例钉住：① 被过渡业务占用的机号按钮仍可点击 ② 选中它时下方出占用说明卡
        /// ③ 说明卡与正常航线列表互斥（不会出现「B-2006 的标题 + B-2005 的航线」这种错配）。
        /// 文案本身由 LogicSelfTests R23 断言（那边有完整 GameState 且能逐字比对）。
        /// </summary>
        [UnityTest]
        public IEnumerator N98_BusyAircraft_ShowsBlockedCard_AndChipStaysClickable()
        {
            var gsGo = new GameObject("GsN98");
            var gs = gsGo.AddComponent<GameState>();
            gs.fleet = new List<Aircraft>
            {
                new Aircraft { registration = "B-7001", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy },
                new Aircraft { registration = "B-7002", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy, age = 8 },
            };
            gs.activeRoutes = new List<RouteData>();
            gs.transitionContracts = new List<TransitionContract>
            {
                new TransitionContract
                {
                    id = "tc_charter", aircraftReg = "B-7002", kind = TransitionKinds.Charter, client = "某会展包机",
                    rateWanPerDay = 2.6f, totalDays = 5, daysUsed = 1, startUtcTicks = 0,
                    dailyCashWan = 1.56f, recallDaysLeft = -1
                }
            };
            var fmGo = new GameObject("FmN98");
            fmGo.AddComponent<FleetManager>();
            yield return null;   // 等 Awake / Start 落定

            try
            {
                OpsSubpageContext.SelectedRegistration = "B-7002";   // 被包机占用
                var host = new GameObject("OpsN98Host", typeof(RectTransform));
                var panel = OpsRuntimeFactory.TryCreate("n98_航线航班环选择器", host.transform);
                Assert.IsNotNull(panel);
                var ui = panel.GetComponentInChildren<OpsRouteRingSelectorUI>(true);
                Assert.IsNotNull(ui, "n98 必须构建出航线/航班环选择器");
                yield return null;   // 等这一轮 Refresh 里被 Destroy 的旧节点真正出列

                var busyChip = FindChild(ui.transform, "Chip_B-7002");
                Assert.IsNotNull(busyChip, "机号条上应列出在册飞机（含被过渡业务占用的那架）");
                var busyBtn = busyChip.GetComponent<Button>();
                Assert.IsNotNull(busyBtn);
                Assert.IsTrue(busyBtn.interactable,
                    "被过渡业务占用的机号按钮必须仍可点击：点它是为了看「为什么不能排班」，禁用等于毫无反馈");

                Assert.IsNotNull(FindChild(ui.transform, "Blocked"),
                    "选中不可排班的飞机时，下方必须给出占用说明卡（而不是空白或另一架飞机的航线）");

                // 换回一架空闲飞机：说明卡撤掉，列表恢复
                OpsSubpageContext.SelectedRegistration = "B-7001";
                ui.Refresh();
                yield return null;
                Assert.IsNull(FindChild(ui.transform, "Blocked"), "切回可排班的飞机后说明卡必须消失");
                Assert.IsNotNull(FindChild(ui.transform, "Note"), "无航线时应显示「暂无航线」提示，而不是留着上一架的说明卡");

                Object.Destroy(host);
            }
            finally
            {
                Object.Destroy(gsGo);
                Object.Destroy(fmGo);
            }
            yield return null;
        }

        /// <summary>
        /// n36 = **只读**的「运营部编成」看板（2026-10-30 重构）。
        ///
        /// 旧页让 CEO 一段段点航段拼环 —— 玩家报障「既看不懂也无从下手」。
        /// 本用例钉住三件事：① 页面上有六步留痕与甘特时间轴；② 旧的手工拼环层不再启用；
        /// ③ 「请求运营部重编」不会就地改账，而是变成收件箱里的待批方案（批准才落账）。
        /// </summary>
        [UnityTest]
        public IEnumerator N36_RotationBoard_ReadOnly_RequestsRebuildViaInbox()
        {
            var gsGo = new GameObject("GsN36");
            var gs = gsGo.AddComponent<GameState>();
            gs.fleet = new List<Aircraft>
            {
                new Aircraft { registration = "B-7101", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy },
                new Aircraft { registration = "B-7102", type = "737-800", source = "自有", status = AircraftStatuses.Airworthy },
            };
            gs.activeRoutes = new List<RouteData>();
            gs.inbox = new List<InboxMail>();
            yield return null;

            var host = new GameObject("OpsN36Host", typeof(RectTransform));
            try
            {
                var panel = OpsRuntimeFactory.TryCreate("n36_航班环构建器", host.transform);
                Assert.IsNotNull(panel);

                var board = panel.GetComponentInChildren<OpsRotationBoardUI>(true);
                Assert.IsNotNull(board, "n36 必须构建运营部编成看板");

                var legacy = panel.GetComponentInChildren<OpsRingBuilderUI>(true);
                if (legacy != null)
                    Assert.IsFalse(legacy.gameObject.activeInHierarchy, "旧的手工拼环层必须收起");

                var steps = FindChild(board.transform, "Steps");
                Assert.IsNotNull(steps, "六步留痕是玩家看懂「卡在哪一步」的唯一入口");
                Assert.AreEqual(6, steps.childCount, "六步各一格");
                Assert.IsNotNull(FindChild(steps, "Step_5"), "第六步也要有格子");

                Assert.IsNotNull(FindChild(board.transform, "TimelineList"), "甘特时间轴区域必须存在");
                Assert.IsNotNull(FindChild(board.transform, "Axis"), "时间轴要有小时刻度，否则读不出空档");
                Assert.IsNull(FindChild(board.transform, "BtnAuto"), "手工「自动串联」入口必须彻底消失");

                var btn = FindChild(board.transform, "BtnReplan");
                Assert.IsNotNull(btn, "重编请求按钮是 CEO 这一侧唯一的动作");
                btn.GetComponent<Button>().onClick.Invoke();
                yield return null;

                Assert.IsNotNull(InboxManager.FindOpenRotationPlan(gs),
                    "点「请求重编」必须变成收件箱里的待批方案（CEO 批准才落账，页面不得就地改排班）");

                Object.Destroy(host);
            }
            finally
            {
                Object.Destroy(gsGo);
            }
            yield return null;
        }

        /// <summary>按名字找后代（含未激活）；找不到返回 null。</summary>
        static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name) return all[i];
            return null;
        }
    }
}
