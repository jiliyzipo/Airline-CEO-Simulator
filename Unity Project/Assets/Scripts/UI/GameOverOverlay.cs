using AirlineCEO.Systems;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>CEO 被解雇后的终局遮罩：挡操作，读档/重开才能继续。</summary>
    public class GameOverOverlay : MonoBehaviour
    {
        static GameOverOverlay _instance;
        TextMeshProUGUI _body;

        public static GameOverOverlay Ensure(Canvas canvas = null)
        {
            if (_instance != null) return _instance;
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            var go = UIFactory.Panel(canvas.transform, "GameOverOverlay", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.82f), rounded: false);
            go.transform.SetAsLastSibling();
            UIFactory.EnsureSortingCanvas(go, UITheme.SortBlockingModal);
            var ui = go.AddComponent<GameOverOverlay>();
            ui.Build(go.transform);
            go.SetActive(false);
            _instance = ui;
            return ui;
        }

        void Build(Transform root)
        {
            var card = UIFactory.Panel(root, "Card",
                new UIFactory.Anchor(new Vector2(0.18f, 0.28f), new Vector2(0.82f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Danger, new Vector2(1.5f, -1.5f));

            UIFactory.Label(card.transform, "Title", "游戏结束", 24,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.Danger, FontStyles.Bold);

            _body = UIFactory.Label(card.transform, "Body", "", 16,
                TextAlignmentOptions.Top,
                new UIFactory.Anchor(new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _body.textWrappingMode = TextWrappingModes.Normal;

            // 终局遮罩会挡住底栏，齿轮里的「退出到开始界面」此时点不到 —— 所以这里必须自带一个，
            // 否则玩家唯一出路是重启进程（这正是加「退出到开始界面」之前的状况）。
            UIFactory.Button(card.transform, "ExitToStart", "退出到开始界面",
                new UIFactory.Anchor(new Vector2(0.30f, 0.05f), new Vector2(0.70f, 0.16f), Vector2.zero, Vector2.zero),
                UITheme.Accent, ExitToStartScreen, 14);
        }

        static void ExitToStartScreen()
        {
            var setup = AirlineCEO.UI.Setup.SetupFlowController.Instance;
            if (setup != null) setup.ReturnToStartScreen();
        }

        public static void ShowIfNeeded()
        {
            var gs = GameState.Instance;
            if (gs == null || !gs.gameOver || gs.gameOverVictory) return;
            var ui = Ensure();
            if (ui == null) return;
            if (ui._body != null)
            {
                ui._body.text = (gs.gameOverReason ?? "董事会已解雇 CEO。") +
                    "\n\n本局已结束。\n「退出到开始界面」可另开一局；F9 读档（或设置里的「读档」）可回到解雇之前。";
            }
            ui.gameObject.SetActive(true);
            ui.transform.SetAsLastSibling();
        }

        /// <summary>收起终局遮罩：退出到开始界面时用（下一局若又失败会由 <see cref="ShowIfNeeded"/> 重新弹出）。</summary>
        public static void Hide()
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }
    }
}
