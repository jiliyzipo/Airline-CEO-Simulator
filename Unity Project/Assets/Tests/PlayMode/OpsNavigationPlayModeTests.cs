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
        public IEnumerator OpsRuntimeFactory_BuildsN42_AndRemovedPagesStayGone()
        {
            var host = new GameObject("OpsRuntimeHost", typeof(RectTransform));
            var week = OpsRuntimeFactory.TryCreate("n42_航班周总计划", host.transform);
            Assert.IsNotNull(week, "n42 周计划页仍在服役，运行时工厂必须能建出来");
            Assert.IsNotNull(week.GetComponent<OpsSubpagePanel>());

            // ★2026-11-01 用户裁定：n98「航线分配」整页删除（逐机航线计划与飞机详情重复）；
            //   n36「航班环构建器」也整页删除（看板搬进「机队 › 周计划及航班环」）。
            //   两条负向断言钉住它们不会「因为有人又接上一条 pageId 就自己长回来」。
            var n98 = OpsRuntimeFactory.TryCreate("n98_航线航班环选择器", host.transform);
            Assert.IsNull(n98, "n98「航线分配」已删除，运行时工厂不得再为它建出页面");

            var n36 = OpsRuntimeFactory.TryCreate("n36_航班环构建器", host.transform);
            Assert.IsNull(n36, "n36「航班环构建器」已删除，运行时工厂不得再为它建出页面");

            Object.Destroy(host);
            yield return null;
        }

        /// <summary>
        /// 「飞机详情」属于机队，不属于航线（2026-11-01 CEO 裁定：移入机队模块）。
        ///
        /// 症状：从「单航线」页点机号，详情确实打开了，但底栏还亮着「航线」、
        /// 顶栏也是航线那排页签 —— 页面被塞进了**当时开着的**那个部门框里。
        /// 本用例从一个非机队模块出发开页，钉住三件事：① 底栏切到「机队」；
        /// ② 详情仍是当前页；③ 详情实例确实挂在 <see cref="FleetUI"/> 的子框里。
        ///
        /// ★机队模块由用例自己用 <see cref="FleetUI.Build"/> 建出来（正式局里由
        /// <c>MVPBootstrap</c> 建在 center 面板上）。不建的话画布上根本没有 FleetUI，
        /// 断言「找得到机队」只会测到「本用例没搭场景」，测不到归属逻辑。
        /// </summary>
        [UnityTest]
        public IEnumerator AircraftDetail_OpensInsideFleetModule()
        {
            var gsGo = new GameObject("GsFleetAffinity");
            var gs = gsGo.AddComponent<GameState>();
            gs.fleet = new List<Aircraft>
            {
                new Aircraft { registration = "B-7201", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy },
            };
            gs.activeRoutes = new List<RouteData>();
            gs.inbox = new List<InboxMail>();
            yield return null;

            var canvas = new GameObject("FleetAffinityCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var nav = canvas.AddComponent<PrefabNavIntegration>();
            FleetUI fleet = null;
            try
            {
                nav.Attach();
                nav.EnterGameplay();

                var fleetHost = new GameObject("FleetCenter", typeof(RectTransform));
                fleetHost.transform.SetParent(canvas.transform, false);
                fleet = FleetUI.Build(fleetHost.transform);
                Assert.IsNotNull(fleet, "机队模块必须建得起来");

                nav.SelectTab("n7_收件箱");
                yield return null;
                Assert.AreEqual("n7_收件箱", nav.Shell.ActivePageId, "前置：本用例要从一个非机队模块出发");

                nav.OpenSubPage("n10_飞机详情_履历");
                yield return null;

                Assert.AreEqual("n9_机队", nav.Shell.ActivePageId,
                    "飞机详情必须把底栏切到「机队」，否则详情页会被塞进当时开着的那个部门框里");
                Assert.AreEqual("n10_飞机详情_履历", nav.Router.CurrentPageId, "详情页仍应是当前页");

                var spawned = nav.Router.GetSpawned("n10_飞机详情_履历");
                Assert.IsNotNull(spawned, "详情页实例缺失");
                Assert.IsTrue(spawned.transform.IsChildOf(fleet.transform),
                    "详情页必须挂在机队模块的子框里（挂在别的模块就等于没搬）");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
                Object.Destroy(gsGo);
            }
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
        /// 被过渡业务占用的机号，「为什么不能排班」的原因与明细**必须仍然可查**
        /// （CEO 2026-10-13 反馈：点 B-2006/B-2007 下面什么都不显示）。
        ///
        /// 旧用例断言的是 n98 页面上那张占用说明卡。★2026-11-01 该页整页删除，
        /// 界面那一层没了，但这条判据仍在服役 —— 命令台下达加班时，运营部要靠它说明
        /// 「这一刻为什么派不出机」。故本用例改为钉住**判据本身**：
        /// 被包机占用的机号必须给出含「包机」的原因，明细里要带得出还剩几天。
        /// </summary>
        [UnityTest]
        public IEnumerator BusyAircraft_BlockReason_StillExplainsWhy()
        {
            var gsGo = new GameObject("GsBusy");
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
            var fmGo = new GameObject("FmBusy");
            fmGo.AddComponent<FleetManager>();
            yield return null;   // 等 Awake / Start 落定

            try
            {
                Assert.IsTrue(string.IsNullOrEmpty(TransitionBusinessLogic.ScheduleBlockReason(gs, "B-7001")),
                    "空闲飞机不该被挂上「不能排班」的原因");

                string busyReason = TransitionBusinessLogic.ScheduleBlockReason(gs, "B-7002");
                Assert.IsFalse(string.IsNullOrEmpty(busyReason), "被包机占用的飞机必须给出原因");
                StringAssert.Contains("包机", busyReason, "原因里要点明占用类型，否则 CEO 分不清是包机还是湿租");

                string busyDetail = TransitionBusinessLogic.ScheduleBlockDetail(gs, "B-7002");
                Assert.IsFalse(string.IsNullOrEmpty(busyDetail), "被占用的飞机必须给得出占用明细");
                StringAssert.Contains("天", busyDetail, "明细要写出还剩多少天，否则看不出什么时候能排");
            }
            finally
            {
                Object.Destroy(gsGo);
                Object.Destroy(fmGo);
            }
            yield return null;
        }

        /// <summary>
        /// 航班环看板 = **只读**的「运营部编成」视图（2026-10-30 重构）。
        ///
        /// 旧页让 CEO 一段段点航段拼环 —— 玩家报障「既看不懂也无从下手」。
        /// ★2026-11-01：n36 页整页删除，同一块看板改挂在「机队 › 周计划及航班环」页签里
        /// （<c>FleetUI.RefreshPlanRing</c> 调 <see cref="OpsRotationBoardUI.Ensure"/>）。
        /// 本用例钉住三件事：① 有六步留痕与甘特时间轴；② 旧的手工拼环入口不存在；
        /// ③ 看板上**一个可点动作都没有** —— 重编令改由命令台下达，
        ///    命令台那条路是否真的能落到收件箱由 <c>WriteCommandRegression</c> 单独钉住。
        /// </summary>
        [UnityTest]
        public IEnumerator RotationBoard_IsReadOnly_WithNoClickableAction()
        {
            var gsGo = new GameObject("GsRotationBoard");
            var gs = gsGo.AddComponent<GameState>();
            gs.fleet = new List<Aircraft>
            {
                new Aircraft { registration = "B-7101", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy },
                new Aircraft { registration = "B-7102", type = "737-800", source = "自有", status = AircraftStatuses.Airworthy },
            };
            gs.activeRoutes = new List<RouteData>();
            gs.inbox = new List<InboxMail>();
            yield return null;

            // 与「机队 › 周计划及航班环」页签同一个调用：把看板 Ensure 到宿主卡上。
            var host = new GameObject("FleetPlanRingCard", typeof(RectTransform));
            try
            {
                var board = OpsRotationBoardUI.Ensure(host.transform);
                Assert.IsNotNull(board, "「周计划及航班环」页签必须构建出运营部编成看板");

                var steps = FindChild(board.transform, "Steps");
                Assert.IsNotNull(steps, "六步留痕是玩家看懂「卡在哪一步」的唯一入口");
                Assert.AreEqual(6, steps.childCount, "六步各一格");
                Assert.IsNotNull(FindChild(steps, "Step_5"), "第六步也要有格子");

                Assert.IsNotNull(FindChild(board.transform, "TimelineList"), "甘特时间轴区域必须存在");
                Assert.IsNotNull(FindChild(board.transform, "Axis"), "时间轴要有小时刻度，否则读不出空档");
                Assert.IsNull(FindChild(board.transform, "BtnAuto"), "手工「自动串联」入口必须彻底消失");

                // ★2026-11-01 用户裁定：「请求运营部重编」按钮撤掉 —— 该指令由 CEO 在命令台输入。
                //   看板上不该再有任何可点动作，否则等于留了第二个指挥入口。
                Assert.IsNull(FindChild(board.transform, "BtnReplan"), "看板不得再留「请求运营部重编」按钮");
                var strayButtons = board.GetComponentsInChildren<Button>(true);
                Assert.AreEqual(0, strayButtons.Length, "看板是只读视图，不应出现任何按钮");

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
