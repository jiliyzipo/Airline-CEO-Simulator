using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>n24 航班异常弹窗：3 选项处置，回写现金与准点。</summary>
    public class OpsIrropsModalUI : BasePanel
    {
        TextMeshProUGUI _body;
        TextMeshProUGUI _hint;
        OpsIrropsLogic.IrropsKind _kind = OpsIrropsLogic.IrropsKind.Weather;
        /// <summary>收件箱警报对应的邮件 id，处置后标记已批准。</summary>
        public static string PendingMailId;
        Button _btnA;
        Button _btnB;
        Button _btnC;
        bool _choosing;

        public static OpsIrropsModalUI Build(OpsIrropsLogic.IrropsKind kind = OpsIrropsLogic.IrropsKind.Weather)
        {
            var root = UIFactory.Panel(null, "OpsIrrops", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false);
            // 全屏遮罩吃掉背后点击
            var dimImg = root.GetComponent<Image>();
            if (dimImg != null) dimImg.raycastTarget = true;

            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.16f, 0.14f), new Vector2(0.84f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.06f, 0.08f, 0.14f, 0.98f));
            UIFactory.AddBorder(card, UITheme.Danger, new Vector2(2f, -2f));
            var cg = card.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.alpha = 1f;

            var title = UIFactory.Label(card.transform, "Title",
                "航班异常", 24, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.88f), new Vector2(0.8f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Danger, FontStyles.Bold);
            DisableRaycast(title);

            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);

            var body = UIFactory.Label(card.transform, "Body", "", 15, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            body.textWrappingMode = TextWrappingModes.Normal;
            DisableRaycast(body);

            var btnA = UIFactory.Button(card.transform, "ChoiceA", "A",
                new UIFactory.Anchor(new Vector2(0.06f, 0.42f), new Vector2(0.94f, 0.58f), Vector2.zero, Vector2.zero),
                new Color(1f, 0.45f, 0.08f, 1f), () => { }, 14);
            var btnB = UIFactory.Button(card.transform, "ChoiceB", "B",
                new UIFactory.Anchor(new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.4f), Vector2.zero, Vector2.zero),
                new Color(0.92f, 0.75f, 0.12f, 1f), () => { }, 14);
            var btnC = UIFactory.Button(card.transform, "ChoiceC", "C",
                new UIFactory.Anchor(new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.22f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { }, 14);
            DisableChildLabelRaycasts(btnA);
            DisableChildLabelRaycasts(btnB);
            DisableChildLabelRaycasts(btnC);
            DisableChildLabelRaycasts(close);

            var hint = UIFactory.Label(card.transform, "Hint", "请选择 A / B / C；× 关闭。", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.01f), new Vector2(0.92f, 0.06f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            DisableRaycast(hint);

            // AddComponent 会立刻 Awake：字段尚未赋值，故 Build 末尾再 Wire
            var panel = root.AddComponent<OpsIrropsModalUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel._body = body;
            panel._hint = hint;
            panel._btnA = btnA;
            panel._btnB = btnB;
            panel._btnC = btnC;
            panel._kind = kind;
            panel.BindPage("n24_危机_IRROPS_弹窗", "航班异常");
            panel.ApplyKind(kind);
            panel.Wire();
            return panel;
        }

        public static OpsIrropsModalUI EnsureOverlay(Transform canvasRoot, OpsIrropsLogic.IrropsKind kind)
        {
            if (canvasRoot == null) return null;
            var existing = canvasRoot.GetComponentInChildren<OpsIrropsModalUI>(true);
            if (existing != null)
            {
                existing._choosing = false;
                existing.ApplyKind(kind);
                existing.gameObject.SetActive(true);
                existing.Wire();
                existing.transform.SetAsLastSibling();
                existing.Show();
                return existing;
            }

            var panel = Build(kind);
            panel.transform.SetParent(canvasRoot, false);
            var rt = panel.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            panel.transform.SetAsLastSibling();
            panel.Show();
            panel.Wire();
            return panel;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            Wire();
        }

        void OnEnable()
        {
            ResolveRefs();
            Wire();
        }

        public void ApplyKind(OpsIrropsLogic.IrropsKind kind)
        {
            ResolveRefs();
            _kind = kind;
            _choosing = false;
            if (titleLabel != null)
                titleLabel.text = $"航班异常 · {OpsIrropsLogic.KindLabel(kind)}";
            if (_body != null)
                _body.text = OpsIrropsLogic.ScenarioBody(kind);
            if (_btnA != null) SetBtn(_btnA, OpsIrropsLogic.Choice.A);
            if (_btnB != null) SetBtn(_btnB, OpsIrropsLogic.Choice.B);
            if (_btnC != null) SetBtn(_btnC, OpsIrropsLogic.Choice.C);
            SetChoicesInteractable(true);
            if (_hint != null) _hint.text = "请选择 A / B / C；点 × 关闭。";
        }

        public override void CloseOverlay()
        {
            CancelInvoke(nameof(CloseOverlay));
            // 本弹窗常挂在 Canvas 上（非导航栈），必须自身 Hide；若在栈上则一并清栈
            if (NavigationRouter.Instance != null && NavigationRouter.Instance.StackDepth > 0)
                NavigationRouter.Instance.CloseOverlay();
            Hide();
        }

        void ResolveRefs()
        {
            var card = transform.Find("Card");
            if (card == null) return;

            if (titleLabel == null)
            {
                var t = card.Find("Title");
                if (t != null) titleLabel = t.GetComponent<TextMeshProUGUI>();
            }
            if (closeButton == null)
            {
                var c = card.Find("Close");
                if (c != null) closeButton = c.GetComponent<Button>();
            }
            if (_body == null)
            {
                var b = card.Find("Body");
                if (b != null) _body = b.GetComponent<TextMeshProUGUI>();
            }
            if (_hint == null)
            {
                var h = card.Find("Hint");
                if (h != null) _hint = h.GetComponent<TextMeshProUGUI>();
            }
            if (_btnA == null)
            {
                var a = card.Find("ChoiceA");
                if (a != null) _btnA = a.GetComponent<Button>();
            }
            if (_btnB == null)
            {
                var b = card.Find("ChoiceB");
                if (b != null) _btnB = b.GetComponent<Button>();
            }
            if (_btnC == null)
            {
                var c = card.Find("ChoiceC");
                if (c != null) _btnC = c.GetComponent<Button>();
            }
            if (canvasGroup == null)
                canvasGroup = card.GetComponent<CanvasGroup>();
        }

        void Wire()
        {
            ResolveRefs();
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseOverlay);
                closeButton.interactable = true;
            }
            BindChoice(_btnA, OpsIrropsLogic.Choice.A);
            BindChoice(_btnB, OpsIrropsLogic.Choice.B);
            BindChoice(_btnC, OpsIrropsLogic.Choice.C);
        }

        void BindChoice(Button btn, OpsIrropsLogic.Choice choice)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => Choose(choice));
            btn.interactable = !_choosing;
        }

        void SetBtn(Button btn, OpsIrropsLogic.Choice choice)
        {
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = OpsIrropsLogic.ChoiceLabel(_kind, choice);
        }

        void SetChoicesInteractable(bool on)
        {
            if (_btnA != null) _btnA.interactable = on;
            if (_btnB != null) _btnB.interactable = on;
            if (_btnC != null) _btnC.interactable = on;
        }

        void Choose(OpsIrropsLogic.Choice choice)
        {
            if (_choosing) return;
            _choosing = true;
            SetChoicesInteractable(false);

            var outcome = OpsIrropsLogic.Resolve(_kind, choice);
            InboxManager.ApplyIrropsOutcome(GameState.Instance, _kind, outcome);
            InboxManager.CompleteIrropsMail(PendingMailId, outcome);
            PendingMailId = null;

            if (_hint != null)
                _hint.text = outcome.summary;
            if (_body != null)
                _body.text = $"{OpsIrropsLogic.ScenarioBody(_kind)}\n\n已执行：{outcome.label}\n{outcome.summary}";

            CancelInvoke(nameof(CloseOverlay));
            Invoke(nameof(CloseOverlay), 1.0f);
        }

        static void DisableRaycast(TextMeshProUGUI tmp)
        {
            if (tmp != null) tmp.raycastTarget = false;
        }

        static void DisableChildLabelRaycasts(Button btn)
        {
            if (btn == null) return;
            var labels = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++)
                labels[i].raycastTarget = false;
        }
    }
}
