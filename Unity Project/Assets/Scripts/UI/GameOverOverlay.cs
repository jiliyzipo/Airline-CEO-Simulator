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
                new UIFactory.Anchor(new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _body.textWrappingMode = TextWrappingModes.Normal;
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
                    "\n\n本局已结束。请重新开局，或用读档回到解雇之前。";
            }
            ui.gameObject.SetActive(true);
            ui.transform.SetAsLastSibling();
        }
    }
}
