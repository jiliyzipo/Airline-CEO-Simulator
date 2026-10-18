using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AirlineCEO.Systems;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 财务顾问引导：首次进入财务面板时的燃油对冲新手弹窗。
    /// </summary>
    public class HedgeTutorialUI : MonoBehaviour
    {
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _bodyText;
        private Action _onTrySafe;
        private Action _onDiy;

        public static HedgeTutorialUI Create(Transform canvasParent)
        {
            var host = new GameObject("HedgeTutorialUI", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<HedgeTutorialUI>();
            UIFactory.EnsureSortingCanvas(host, UITheme.SortBlockingModal);
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            var card = UIFactory.Panel(parent, "AdvisorCard",
                new UIFactory.Anchor(new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.98f)).gameObject;
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(2f, -2f));

            _titleText = UIFactory.Label(card.transform, "Title",
                "📊 燃油价格波动提醒", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.Warning, FontStyles.Bold);

            _bodyText = UIFactory.Label(card.transform, "Body", "", 15, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.07f, 0.36f), new Vector2(0.93f, 0.80f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.overflowMode = TextOverflowModes.Truncate;

            UIFactory.Button(card.transform, "BtnSafe",
                "👍 试试保本稳妥",
                new UIFactory.Anchor(new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.30f), Vector2.zero, Vector2.zero),
                UITheme.Success, OnTrySafeClicked, 16);

            UIFactory.Button(card.transform, "BtnDiy",
                "👀 我自己来",
                new UIFactory.Anchor(new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnDiyClicked, 15);
        }

        /// <summary>显示引导；不在此处标记已读（点按钮后再标记）。</summary>
        public void Show(Action onTrySafe, Action onDiy)
        {
            _onTrySafe = onTrySafe;
            _onDiy = onDiy;

            if (_bodyText != null)
            {
                _bodyText.text =
                    "老板，您注意到燃油价格波动了吗？上季度油价涨了 8%，我们的燃油成本多了 $12 万。\n\n" +
                    "建议您试试 燃油对冲 ——锁定未来油价，避免暴涨风险。新手推荐 保本稳妥 方案，点击下方按钮即可生效。";
            }

            if (_titleText != null)
                _titleText.text = "燃油价格波动提醒";

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _onTrySafe = null;
            _onDiy = null;
        }

        private static void MarkTutorialSeen()
        {
            var gs = GameState.Instance;
            if (gs != null)
                gs.hasSeenHedgeTutorial = true;
        }

        private void OnTrySafeClicked()
        {
            MarkTutorialSeen();
            var cb = _onTrySafe;
            Hide();
            cb?.Invoke();
        }

        private void OnDiyClicked()
        {
            MarkTutorialSeen();
            var cb = _onDiy;
            Hide();
            cb?.Invoke();
        }
    }
}
