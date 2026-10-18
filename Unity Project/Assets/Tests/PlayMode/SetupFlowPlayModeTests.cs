using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Setup;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    public class SetupFlowPlayModeTests : IsolatedPlayModeFixture
    {
        [UnityTest]
        public IEnumerator ShellRuntime_DashboardAndInbox_CanBuild()
        {
            var dash = DashboardPanelUI.Build();
            Assert.IsNotNull(dash);
            Assert.IsTrue(dash.gameObject.activeSelf);
            Object.Destroy(dash.gameObject);

            var inbox = InboxPanelUI.Build();
            Assert.IsNotNull(inbox);
            Object.Destroy(inbox.gameObject);

            var ao = AoPanelUI.Build();
            Assert.IsNotNull(ao);
            Object.Destroy(ao.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NavigationRouter_PreferShellRuntime_OpensN6()
        {
            var go = new GameObject("ShellNav");
            var router = go.AddComponent<NavigationRouter>();
            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = "n6_主页_Dashboard";
            cfg.title = "主页";
            // 无 prefab → ShellRuntimeFactory
            router.SetCatalog(new[] { cfg });

            Assert.IsTrue(router.Navigate("n6_主页_Dashboard"));
            Assert.AreEqual("n6_主页_Dashboard", router.CurrentPageId);
            Assert.Greater(go.transform.childCount + (router.PanelRoot != null ? router.PanelRoot.childCount : 0), 0);

            Object.Destroy(go);
            Object.Destroy(cfg);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MapShell_N12_DoesNotMapToOps()
        {
            Assert.AreEqual(-1, AirlineCEO.UI.MVPBootstrap.MapShellPageToLegacyNav("n12_航线网络"));
            Assert.AreEqual(0, AirlineCEO.UI.MVPBootstrap.MapShellPageToLegacyNav("n13_运营部"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetupDraft_Defaults_AreSensible()
        {
            var d = new SetupDraft();
            Assert.AreEqual("", d.PlayerName);
            Assert.AreEqual("", d.CompanyNameZh);
            Assert.AreEqual("", d.CompanyNameEn);
            Assert.AreEqual("", d.IataCode);
            Assert.AreEqual("", d.IcaoCode);
            Assert.Greater(d.InitialCashWan, 0f);
            Assert.Greater(d.Age, 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetupCatalogs_LatinAndIcao_Validate()
        {
            Assert.IsTrue(SetupFormWidgets.IsLatinPersonName("Alex"));
            Assert.IsFalse(SetupFormWidgets.IsLatinPersonName("玩家"));
            Assert.IsTrue(SetupFormWidgets.IsIcao("NVA"));
            Assert.IsFalse(SetupFormWidgets.IsIcao("NV"));
            Assert.IsTrue(SetupFormWidgets.IsIata("CA"));
            Assert.IsTrue(SetupFormWidgets.IsIata("U2"));
            Assert.IsTrue(SetupFormWidgets.IsIata("3U"));
            Assert.IsFalse(SetupFormWidgets.IsIata("12"));
            Assert.IsFalse(SetupFormWidgets.IsIata("CAA"));
            Assert.IsFalse(SetupFormWidgets.IsIata("C"));
            Assert.Greater(SetupCatalogs.Countries.Length, 40);
            Assert.AreEqual(4, SetupCatalogs.CashTiersWan.Length);
            yield return null;
        }

        // ── 接管航司列表：点选/展开后重绘不得重置滚动位置（回归：「点任意行列表跳回顶部」）──

        /// <summary>归一化位置换算：不足一屏回顶部（不能沿用 ScrollRect 退化的 0）；一屏以上按像素偏移反算。</summary>
        [UnityTest]
        public IEnumerator AirlineSelectScroll_NormalizedFor_DegenerateCaseGoesTop()
        {
            // 内容比视口矮：必须回顶部（1），而不是把「底部」的 0 当基准
            Assert.AreEqual(1f, AirlineSelectionUI.NormalizedFor(0f, 200f, 600f), 1e-4f);
            Assert.AreEqual(1f, AirlineSelectionUI.NormalizedFor(0f, 600f, 600f), 1e-4f);
            // 顶部 / 中部 / 底部
            Assert.AreEqual(1f, AirlineSelectionUI.NormalizedFor(0f, 2000f, 600f), 1e-4f);
            Assert.AreEqual(0.5f, AirlineSelectionUI.NormalizedFor(700f, 2000f, 600f), 1e-4f);
            Assert.AreEqual(0f, AirlineSelectionUI.NormalizedFor(1400f, 2000f, 600f), 1e-4f);
            // 越界像素偏移被夹到 [0,1]
            Assert.AreEqual(0f, AirlineSelectionUI.NormalizedFor(9999f, 2000f, 600f), 1e-4f);
            Assert.AreEqual(1f, AirlineSelectionUI.NormalizedFor(-50f, 2000f, 600f), 1e-4f);
            yield return null;
        }

        /// <summary>重绘（点选一行 / 展开节点）必须复用同一个滚动区域：不新增 ScrollRect、不堆积旧行、滚动位置原样保留。</summary>
        [UnityTest]
        public IEnumerator AirlineSelect_Redraw_KeepsScrollAreaAndPosition()
        {
            var form = UIFactory.Panel(null, "Form", UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            // 无父级 → rect 由 sizeDelta 决定；显式给尺寸，让视口高度不随屏幕分辨率漂移
            var formRt = form.GetComponent<RectTransform>();
            formRt.sizeDelta = new Vector2(900f, 600f);

            var area = AirlineSelectionUI.CreateArea(form.transform, out var scroll);
            Assert.IsNotNull(area, "应建立滚动内容");
            Assert.IsNotNull(scroll, "滚动区域应带 ScrollRect");
            Assert.IsNotNull(scroll.viewport, "ScrollRect 应绑定 viewport");

            // 展开家数最多的大洲 → 内容必然超出一屏
            AirlineSelectionUI.Draw(area, "", "亚洲", "", "", _ => { }, (_, _, _) => { });
            int rowsAsia = area.childCount;
            float heightAsia = area.rect.height;
            Assert.Greater(rowsAsia, 10, "展开亚洲应产出多行");
            Assert.Greater(heightAsia, scroll.viewport.rect.height, "展开后内容应超出一屏，否则测不到滚动");

            // 模拟用户滚到中部
            scroll.verticalNormalizedPosition = 0.4f;
            yield return null;
            float scrolled = scroll.verticalNormalizedPosition;
            Assert.AreEqual(0.4f, scrolled, 0.02f, "0.4 应落在可滚动区间内");
            float topOffset = area.anchoredPosition.y;
            Assert.Greater(topOffset, 0.5f, "中部应是真的滚动过，而不是停在顶部");

            // 重绘（等价于点选一行：内容高度不变）→ 同一个 ScrollRect，同一像素偏移
            AirlineSelectionUI.Draw(area, "", "亚洲", "", "", _ => { }, (_, _, _) => { });
            Assert.AreEqual(1, form.GetComponentsInChildren<ScrollRect>(true).Length,
                "滚动区域必须复用，不得每轮重绘新建 ScrollRect");
            Assert.AreEqual(rowsAsia, area.childCount, "重绘不得堆积上一轮的行");
            Assert.AreEqual(heightAsia, area.rect.height, 0.01f, "同样展开下内容高度应不变");

            scroll.verticalNormalizedPosition =
                AirlineSelectionUI.NormalizedFor(topOffset, area.rect.height, scroll.viewport.rect.height);
            yield return null;
            Assert.AreEqual(scrolled, scroll.verticalNormalizedPosition, 0.01f,
                "重绘后滚动位置应保留（回归：不得跳回顶部）");

            // 换成另一个大洲：仍只有一个 ScrollRect，且行数等于全新区域的产出（无残留）
            var scratchForm = UIFactory.Panel(null, "Form2", UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            scratchForm.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 600f);
            var scratchArea = AirlineSelectionUI.CreateArea(scratchForm.transform, out _);
            AirlineSelectionUI.Draw(scratchArea, "", "非洲", "", "", _ => { }, (_, _, _) => { });

            AirlineSelectionUI.Draw(area, "", "非洲", "", "", _ => { }, (_, _, _) => { });
            Assert.AreEqual(1, form.GetComponentsInChildren<ScrollRect>(true).Length, "换大洲也不应新建 ScrollRect");
            Assert.AreEqual(scratchArea.childCount, area.childCount, "复用区域的行数应与全新区域一致（无残留）");

            Object.Destroy(form);
            Object.Destroy(scratchForm);
            yield return null;
        }

        // ── 主菜单版面（FM 式两栏）：左主视觉槽 + 右纵向菜单列，utility 降级到次级行 ──

        /// <summary>
        /// 主菜单结构回归：① 左槽存在（后期图像化的替换点）；② 入口是**纵向**整行按钮（旧的横排一行 6 格已废）；
        /// ③ 两栏不重叠；④ <c>Interactable=false</c> 的条目置灰；⑤ 次级入口与页脚在；⑥ 点击只走回调、提示行可回写。
        /// 全部不依赖 GameState / PlayerPrefs（存档卡片内容由调用方给），故可确定性跑。
        /// </summary>
        [UnityTest]
        public IEnumerator MainMenu_Layout_TwoColumnsAndVerticalEntries()
        {
            var overlay = UIFactory.Panel(null, "Overlay", UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            overlay.GetComponent<RectTransform>().sizeDelta = new Vector2(1280f, 720f);

            int clicked = 0;
            var entries = new List<MenuEntry>
            {
                new MenuEntry { Name = "New", Label = "新游戏 · 接管", Color = UITheme.Accent, OnClick = () => clicked++ },
                new MenuEntry { Name = "Scratch", Label = "从零创建" },
                new MenuEntry { Name = "Continue", Label = "继续 / 读档", Interactable = false },
                new MenuEntry { Name = "Archive", Label = "历史回放" }
            };

            var view = MainMenuUI.Build(overlay.transform,
                new SaveCardInfo { HasSave = false, Icon = "新", Title = "尚无存档", Sub = "先开一局" },
                entries, () => { }, () => { }, "初始提示");

            Assert.IsNotNull(view.Root, "应建出版面根节点");
            Assert.AreEqual(MainMenuUI.RootName, view.Root.name);

            // ① 左槽（后期替换为插画的那块）必须在
            var hero = view.Root.transform.Find(MainMenuUI.HeroName)?.GetComponent<RectTransform>();
            Assert.IsNotNull(hero, "应有左侧主视觉槽（图像的替换点）");

            var column = view.Root.transform.Find("Column")?.GetComponent<RectTransform>();
            Assert.IsNotNull(column, "应有右侧菜单列");
            // ③ 两栏不得重叠
            Assert.Less(hero.anchorMax.x, column.anchorMin.x, "主视觉槽与菜单列应为互不重叠的两栏");

            // ② 入口：纵向整行（等宽拉伸 + 依次向下）
            float prevY = float.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                var go = view.Root.transform.Find("Column/" + MainMenuUI.EntryPrefix + entries[i].Name);
                Assert.IsNotNull(go, $"应有入口 {entries[i].Name}");
                var rt = go.GetComponent<RectTransform>();
                Assert.That(rt.anchorMin.x, Is.InRange(0f, 0.05f), $"{entries[i].Name} 左侧仅允许细小留白");
                Assert.That(rt.anchorMax.x, Is.InRange(0.95f, 1f), $"{entries[i].Name} 右侧仅允许细小留白");
                Assert.GreaterOrEqual(rt.anchorMax.x - rt.anchorMin.x, 0.95f, "纵向按钮至少占菜单列 95% 宽度");
                Assert.Less(rt.anchoredPosition.y, prevY, "入口必须依次向下排（纵向），而不是挤成一行横排");
                prevY = rt.anchoredPosition.y;

                // ④ 可用性与条目一致（无存档时「继续 / 读档」置灰）
                var btn = go.GetComponent<Button>();
                Assert.IsNotNull(btn, $"{entries[i].Name} 应是按钮");
                Assert.AreEqual(entries[i].Interactable, btn.interactable,
                    $"{entries[i].Name} 的可用性应与条目一致");
            }

            // ⑤ 次级入口（设置）与页脚（版本 + 退出）都在，且不跟主入口混在同一层
            Assert.IsNotNull(view.Root.transform.Find("Column/" + MainMenuUI.LinkBarName + "/" + MainMenuUI.SettingsName),
                "设置应降级到次级行");
            Assert.IsNotNull(view.Root.transform.Find("Column/" + MainMenuUI.VersionName), "应有版本号");
            Assert.IsNotNull(view.Root.transform.Find("Column/" + MainMenuUI.QuitName), "应有退出");
            Assert.IsNotNull(view.Root.transform.Find("Column/" + MainMenuUI.SaveCardName), "应有「最近保存」卡片");

            // ⑥ 点击只走回调（不碰 GameState）；提示行可被后续点击回写
            view.Root.transform.Find("Column/" + MainMenuUI.EntryPrefix + "New")
                .GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, clicked, "点击入口应触发回调");

            view.SetHint("回写提示");
            Assert.AreEqual("回写提示", view.HintText);

            Object.Destroy(overlay);
            yield return null;
        }

        /// <summary>
        /// 开始界面不得被壳层 chrome 盖住。
        ///
        /// 成因：向导叠加层只在**创建那一刻** `SetAsLastSibling` 过一次，而壳层的
        /// `PrefabNavIntegration.RaiseChrome()` 会在导航过程中把顶栏等 chrome 上浮 —— 于是
        /// 「主菜单上飘着一条顶栏（航司名 / 游戏内时钟 / 速度档）」。向导是全屏闸门，
        /// 醒着的时候必须在最上层，且每进一次向导页都要重新压回去。
        /// </summary>
        [UnityTest]
        public IEnumerator SetupOverlay_StaysAboveChrome()
        {
            bool prevBypass = SetupFlowController.BypassForTests;
            SetupFlowController.BypassForTests = false;

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvasTf = canvasGo.transform;

            // 假 chrome（真游戏里是 MVPBootstrap 建的 TopBar）
            var topBar = new GameObject("TopBar", typeof(RectTransform));
            topBar.transform.SetParent(canvasTf, false);

            var setupGo = new GameObject("SetupFlow");
            setupGo.transform.SetParent(canvasTf, false);
            var setup = setupGo.AddComponent<SetupFlowController>();

            setup.Begin();                       // → ShowMainMenu → EnsureOverlay
            var overlay = canvasTf.Find("SetupOverlay");
            Assert.IsNotNull(overlay, "主菜单应建出全屏叠加层");
            Assert.AreEqual(canvasTf.childCount - 1, overlay.GetSiblingIndex(), "叠加层初始应压在最上层");

            // 前置条件：模拟壳层 RaiseChrome 把顶栏上浮 —— 此时叠加层被压到下面（就是截图里的现象）
            topBar.transform.SetAsLastSibling();
            Assert.AreNotEqual(canvasTf.childCount - 1, overlay.GetSiblingIndex(),
                "前置条件：顶栏上浮后叠加层不应还在最上层");

            // 再次进入向导页 → 叠加层必须自己压回去
            setup.ShowMainMenu();
            Assert.AreEqual(canvasTf.childCount - 1, overlay.GetSiblingIndex(),
                "每次进入向导页都要重新压回最上层，否则壳层 chrome 会浮在开始界面之上");

            Object.Destroy(canvasGo);
            Object.Destroy(setupGo);
            yield return null;
            SetupFlowController.BypassForTests = prevBypass;
        }

        /// <summary>
        /// 开局向导期间壳层 chrome **三条一起**收起（顶栏在列）。
        ///
        /// 底栏与状态栏此前已收，顶栏漏了 —— 而顶栏恰好是最不该出现的那条：航司名兜底成默认的
        /// 「天际航空」、时钟是默认财年、速度档按钮还是活的（在菜单里点会真的改 `timeScale`）。
        /// 这条断言把「三条一起」钉住：以后新增 chrome 忘了加进这个方法，或有人把顶栏从这个
        /// 方法里摘出去，都会红。
        /// </summary>
        [UnityTest]
        public IEnumerator PrefabNav_ChromeIsHiddenUntilGameplayReady()
        {
            var shell = new GameObject("Shell").AddComponent<ShellBarView>();
            var status = new GameObject("Status").AddComponent<StatusBarView>();
            var topBar = new GameObject("TopBar", typeof(RectTransform)).transform;

            PrefabNavIntegration.ApplyChromeVisibility(false, shell, status, topBar);
            Assert.IsFalse(shell.gameObject.activeSelf, "向导期底栏应收起");
            Assert.IsFalse(status.gameObject.activeSelf, "向导期状态栏应收起");
            Assert.IsFalse(topBar.gameObject.activeSelf,
                "向导期顶栏必须收起：此时它显示的航司名/财年时钟都是默认值，速度档按钮却真的能改游戏速度");

            PrefabNavIntegration.ApplyChromeVisibility(true, shell, status, topBar);
            Assert.IsTrue(shell.gameObject.activeSelf, "进入玩法后底栏应回来");
            Assert.IsTrue(status.gameObject.activeSelf, "进入玩法后状态栏应回来");
            Assert.IsTrue(topBar.gameObject.activeSelf, "进入玩法后顶栏应回来");

            // 缺项不得抛异常（例如还没 Attach 完就被调用）
            PrefabNavIntegration.ApplyChromeVisibility(false, null, null, null);

            Object.Destroy(shell.gameObject);
            Object.Destroy(status.gameObject);
            Object.Destroy(topBar.gameObject);
            yield return null;
        }

        /// <summary>
        /// 机场搜索框只在**开局或读档完成后**可用。
        ///
        /// 它是顶栏（`TopBar/SearchBox`）的子节点，所以顶栏一藏它也藏 —— 但回车这条路会
        /// `SelectTab("n6_主页_Dashboard")`，能从开始界面直接捅进主页、跳过整个开局向导。
        /// 因此闸门不能只靠「看不见」：这里同时钉住 `GameplayReady` 这个真值来源与
        /// `OnSearchSubmit` 实际读的那句判断。
        /// </summary>
        [UnityTest]
        public IEnumerator AirportSearch_OnlyAfterGameplayStarts()
        {
            bool prevSuppress = PrefabNavIntegration.SuppressAutoAttachForTests;
            PrefabNavIntegration.SuppressAutoAttachForTests = true;

            var navGo = new GameObject("Nav");
            var nav = navGo.AddComponent<PrefabNavIntegration>();

            Assert.IsFalse(MVPBootstrap.AirportSearchAllowedFor(null), "没有导航组件时不可搜索");
            Assert.IsFalse(MVPBootstrap.AirportSearchAllowedFor(nav),
                "向导期（主菜单 / 选航司 / 公司注册…）不得搜机场：回车会跳过开局闸门直接进主页");

            nav.EnterGameplay();   // 等价于 SetupFlowController.EnterGameplay 走到的 `OpenHomeDashboard`

            Assert.IsTrue(nav.GameplayReady, "开局 / 读档后闸门应打开");
            Assert.IsTrue(MVPBootstrap.AirportSearchAllowedFor(nav), "开局 / 读档后应可搜机场");

            Object.Destroy(navGo);
            yield return null;
            PrefabNavIntegration.SuppressAutoAttachForTests = prevSuppress;
        }

        /// <summary>
        /// 玩法态闸门：`SelectTab` / `OpenSubPage` 在开局 / 读档完成前一律拒绝执行。
        ///
        /// 之前的防护都是「入口不可达」（底部栏、顾问、收件箱在向导期都被隐藏），而机场搜索
        /// 那条路证明了「点不到」不等于「进不去」——导航 API 是 public 的，谁都能调。
        /// 这条用例把闸门本身钉住：向导期调用必须**原地返回**（既不导航，也不能顺手把壳层建起来）。
        /// </summary>
        [UnityTest]
        public IEnumerator Navigation_IsLockedUntilGameplayStarts()
        {
            bool prevSuppress = PrefabNavIntegration.SuppressAutoAttachForTests;
            PrefabNavIntegration.SuppressAutoAttachForTests = true;

            var navGo = new GameObject("Nav");
            var nav = navGo.AddComponent<PrefabNavIntegration>();

            // 向导期：两条入口都必须原地返回。用 LogAssert.Expect 吞掉闸门的告警，
            // 顺带断言「确实走了拒绝分支」而不是悄悄无声通过。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("导航已锁定"));
            nav.SelectTab("n6_主页_Dashboard");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("导航已锁定"));
            nav.OpenSubPage("n40_设置_存档");

            Assert.IsNull(nav.Router, "向导期被拒绝的导航不应顺手把壳层（Router）建起来");

            // 开局 / 读档后放开（本用例只验闸门，不验导航结果，故不带 Canvas）
            nav.EnterGameplay();
            Assert.IsTrue(nav.GameplayReady, "开局 / 读档后闸门应打开");

            Object.Destroy(navGo);
            yield return null;
            PrefabNavIntegration.SuppressAutoAttachForTests = prevSuppress;
        }
    }
}
