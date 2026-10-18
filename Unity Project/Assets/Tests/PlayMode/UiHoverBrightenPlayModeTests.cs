using System.Collections;
using AirlineCEO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// 悬停提亮的 alpha 边界（2026-10-11 晚）。
    ///
    /// 报障原文：「为什么光标移到下拉菜单外面的区域就会变成灰色？」
    /// 根因不在下拉本身，而在 <see cref="UIHoverBrighten"/>：它由 `UIFactory.Button`
    /// **无条件**挂到每一个按钮上，而其中一批按钮的底色是「透明但可点」的点击区
    /// （点名开档案、航线行、整屏「点外收起」接收层）。旧实现直接 Lerp 到 `Color.white`
    /// （alpha = 1），把它们的 a=0 拉成 0.22 —— 光标一进那块区域就浮出一层 22% 灰雾。
    ///
    /// 本用例钉住两条，缺一不可：
    ///   ① 不透明按钮悬停**仍要提亮**（否则「修好」的方式可能是干脆不提亮，白白丢掉反馈）；
    ///   ② 透明点击区悬停**不得改 alpha**（这才是报障的那件事）。
    /// 只写 ② 是不够的：若拿到手的组件根本没在跑，② 也会绿 ——
    /// 所以 ① 同时充当「这个 handler 真的在跑」的证据，防止本用例退化成一条永远为真的断言。
    /// </summary>
    public class UiHoverBrightenPlayModeTests : IsolatedPlayModeFixture
    {
        [UnityTest]
        public IEnumerator HoverBrighten_LiftsRgbButNeverAlpha()
        {
            var host = new GameObject("HoverHost", typeof(RectTransform));

            // 不透明按钮（底栏 / 页签那一类）
            var solid = UIFactory.Button(host.transform, "Solid", "确定", UIFactory.Fill,
                UITheme.PanelDeep, () => { }, 10);
            // 透明点击区（点名开档案 / 航线行 / 整屏点外收起 那一类）
            var clear = UIFactory.Button(host.transform, "Clear", "", UIFactory.Fill,
                new Color(0f, 0f, 0f, 0f), () => { }, 10);

            // 让 Awake / OnEnable 跑完（它们负责缓存 _image）
            yield return null;

            var solidImg = solid.GetComponent<Image>();
            var clearImg = clear.GetComponent<Image>();
            Assert.IsNotNull(solidImg);
            Assert.IsNotNull(clearImg);

            // 必须按**具体类型**取，不能按 IPointerEnterHandler 取：
            // Selectable（Button 的基类）自己就实现了 IPointerEnterHandler/Exit 做高亮态，
            // 且它在组件表里排在 UIHoverBrighten 之前 —— 按接口取会拿到 Button，
            // 调它的 OnPointerEnter 不打桩到本组件（本用例第一版就是这么假绿的）。
            var solidHover = solid.GetComponent<UIHoverBrighten>();
            var clearHover = clear.GetComponent<UIHoverBrighten>();
            Assert.IsNotNull(solidHover, "UIFactory.Button 必须挂上悬停反馈（本用例的前置）");
            Assert.IsNotNull(clearHover, "UIFactory.Button 必须挂上悬停反馈（本用例的前置）");

            // ① 不透明按钮：RGB 提亮，且 alpha 保持 1
            solidHover.OnPointerEnter(null);
            Assert.Greater(solidImg.color.r, UITheme.PanelDeep.r,
                "不透明按钮悬停仍应提亮 —— 否则本用例的①失去了「handler 在跑」的证据作用");
            Assert.AreEqual(UITheme.PanelDeep.a, solidImg.color.a, 0.0001f,
                "不透明按钮悬停不得改变 alpha");

            // ② 透明点击区：alpha 必须原样为 0（报障点）
            clearHover.OnPointerEnter(null);
            Assert.AreEqual(0f, clearImg.color.a, 0.0001f,
                "透明点击区悬停不得把 a=0 提成半透明灰 —— 那会在光标处凭空盖出一层灰雾");

            // 悬停离开后精确还原（透明区仍全透明、不透明区回到原底色）
            solidHover.OnPointerExit(null);
            clearHover.OnPointerExit(null);
            Assert.AreEqual(UITheme.PanelDeep.r, solidImg.color.r, 0.0001f, "离开后应还原原底色");
            Assert.AreEqual(0f, clearImg.color.a, 0.0001f);

            Object.Destroy(host);
            yield return null;
        }
    }
}
