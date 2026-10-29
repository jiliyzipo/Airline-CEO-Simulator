using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// 开线弹窗的「必须留一个出口」契约（2026-10-28 报障）。
    ///
    /// 报障原文：「地图里点两个城市开通航线后弹窗『已提交运营部研究』，点左下角的返回键没有效果。」
    /// 根因是两处叠加，本用例把两处分别钉住：
    ///   ① 弹窗不进 NavigationRouter 也不写浏览历史 → 底栏 &lt; 在主页上恒为禁用；
    ///   ② 「已提交」态把表单 / 确认 / RPM 全隐藏 → 页内没有第二个出口。
    /// 只要 ① 的登记缺失，玩家提交后就再也关不掉这个弹窗（蒙层还是全屏 raycast 目标）。
    /// </summary>
    public class OpenRouteModalBackPlayModeTests : IsolatedPlayModeFixture
    {
        static OpenRouteModalUI BuildModal(out GameObject host)
        {
            host = new GameObject("ModalHost", typeof(RectTransform), typeof(Canvas));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var modal = OpenRouteModalUI.Ensure(host.transform);
            Assert.IsNotNull(modal, "Ensure 应建出弹窗");
            return modal;
        }

        [Test]
        public void SubmittedModal_IsDismissibleByBottomBarBack()
        {
            OpenRouteModalUI modal = BuildModal(out var host);
            try
            {
                modal.Show(_ => { }, null);
                modal.ShowSubmitted(new OpenRouteModalUI.Selection
                {
                    originCode = "SIN",
                    destCode = "LHR",
                    viaLabel = "直飞",
                });

                Assert.IsTrue(ModalBackStack.HasOpen, "弹窗打开后必须登记为返回目标，否则底栏 < 无从得知");
                Assert.IsTrue(modal.TryBack(), "底栏 < 应消费这次返回并关掉弹窗");
                Assert.IsFalse(modal.gameObject.activeSelf, "关掉后弹窗必须真的隐藏（蒙层也要撤掉）");
                Assert.IsFalse(ModalBackStack.HasOpen, "关闭后必须注销，否则 < 会一直停在可用态");
                Assert.IsFalse(modal.TryBack(), "已关闭的弹窗不应再消费返回，返回键要能继续退页面");
            }
            finally
            {
                ModalBackStack.Clear();
                if (host != null) Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 端到端那条：真正的底栏 &lt;（`NavBack` 按钮）在弹窗打开时必须是**可点的**，
        /// 点下去弹窗要真的关掉。
        ///
        /// 上面那条用例直接调 `TryBack()`，绕过了「按钮当时是不是禁用」和「点击有没有接到 `GoBack`」
        /// —— 而报障说的正是这两件事。本用例走 `<see cref="PrefabNavIntegration"/>` 建出整条壳层，
        /// 断言的是玩家真的会碰到的那颗按钮。
        /// </summary>
        [Test]
        public void BottomBarBackButton_IsEnabledWhileModalOpen_AndClosesIt()
        {
            var canvas = new GameObject("ShellCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var nav = canvas.AddComponent<PrefabNavIntegration>();
            try
            {
                nav.Attach();
                nav.EnterGameplay();

                var shell = canvas.transform.GetComponentInChildren<ShellBarView>(true);
                Assert.IsNotNull(shell, "壳层没建出底栏，本用例的前置不成立");
                var back = shell.transform.Find("NavBack")?.GetComponent<Button>();
                Assert.IsNotNull(back, "底栏缺少 NavBack 按钮");

                // 前置：主页上没有任何可返回的东西时，< 本就该是禁用的。
                // 这一条同时证明下面那次「变可用」不是按钮天生就可用。
                Assert.IsFalse(back.interactable, "主页空闲态下 < 不应可用（否则本用例失去对照）");

                var modal = OpenRouteModalUI.Ensure(canvas.transform);
                modal.Show(_ => { }, null);
                Assert.IsTrue(back.interactable,
                    "弹窗打开后 < 必须变为可点 —— 否则玩家点它没有任何反应，这正是报障的现象");

                back.onClick.Invoke();

                Assert.IsFalse(modal.gameObject.activeSelf, "点 < 之后弹窗必须关闭");
                Assert.IsFalse(ModalBackStack.HasOpen, "关闭后必须注销返回登记");
                Assert.IsFalse(back.interactable, "< 应回到「无层可退」的禁用态，不能一直亮着");
            }
            finally
            {
                ModalBackStack.Clear();
                Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void SubmittedTitle_SaysFollowUpNotResearch()
        {
            OpenRouteModalUI modal = BuildModal(out var host);
            try
            {
                modal.Show(_ => { }, null);
                modal.ShowSubmitted(new OpenRouteModalUI.Selection
                {
                    originCode = "SIN",
                    destCode = "LHR",
                    viaLabel = "直飞",
                });

                var title = modal.transform.Find("Card/Title")?.GetComponent<TextMeshProUGUI>();
                Assert.IsNotNull(title, "结果页标题节点缺失");
                Assert.AreEqual("已交由运营部跟进", title.text,
                    "提交结果页的标题是玩家判断「这件事归谁办」的唯一线索，措辞须与设计一致");
            }
            finally
            {
                ModalBackStack.Clear();
                if (host != null) Object.DestroyImmediate(host);
            }
        }
    }
}
