using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 底栏上方的**一条**：左侧是财务 / 航线 / 机队读数（设计稿 a 栏），
    /// 右侧是 CEO 命令台（c 栏），中间一条竖分隔线。
    ///
    /// 为什么合成一个类：a 与 c 必须**同高、同行、同时显隐**。分两处建，
    /// 迟早出现「状态栏隐藏了、命令条还在」这种只有向导期才看得见的错位。
    /// </summary>
    public class BottomDockView : MonoBehaviour
    {
        /// <summary>整条高度（像素）。状态栏与命令条共用。</summary>
        public const float BarHeight = 42f;
        /// <summary>a 栏（读数）与 c 栏（命令台）的分界：0.40 = 左 40% / 右 60%。</summary>
        public const float SplitX = 0.40f;

        public StatusBarView Stats { get; private set; }
        public CommandBarView Command { get; private set; }

        public static BottomDockView Build(Transform parent)
        {
            var go = UIFactory.Panel(parent, "BottomDock",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(12f, 0f), new Vector2(-12f, BarHeight)),
                UITheme.Glass, rounded: false);
            UIFactory.AddBorder(go, UITheme.Border, new Vector2(1f, -1f));

            var view = go.AddComponent<BottomDockView>();
            view.Stats = StatusBarView.Build(go.transform, 0f, SplitX);

            // 竖直分隔线：只在中间 64% 高度上画，读起来是「分区」而不是「一条压在数字上的框」
            var div = UIFactory.Panel(go.transform, "Split",
                new UIFactory.Anchor(new Vector2(SplitX, 0.18f), new Vector2(SplitX, 0.82f),
                    new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f)),
                UITheme.Border, rounded: false);
            var divImg = div.GetComponent<UnityEngine.UI.Image>();
            if (divImg != null) divImg.raycastTarget = false;

            view.Command = CommandBarView.Build(go.transform, SplitX + 0.004f, 1f);
            return view;
        }
    }
}
