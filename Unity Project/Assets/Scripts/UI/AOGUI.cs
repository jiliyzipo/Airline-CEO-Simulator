using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// GDD 7.2 · AOG 危机弹窗：三选项 + 顾问说明后重选 1/2。
    /// </summary>
    public class AOGUI : MonoBehaviour
    {
        private static readonly Color OrangeBtn = new Color(1f, 0.45f, 0.08f, 1f);
        private static readonly Color YellowBtn = new Color(0.92f, 0.75f, 0.12f, 1f);
        private static readonly Color BlueBtn = UITheme.Accent;

        private GameObject _root;
        private GameObject _crisisPanel;
        private GameObject _advisorPanel;
        private Button _btnFerry;
        private Button _btnDelay;
        private Button _btnAdvisor;
        private TextMeshProUGUI _resultHint;

        #region Build

        public static AOGUI Create(Transform canvasParent)
        {
            var host = new GameObject("AOGUI", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<AOGUI>();
            UIFactory.EnsureSortingCanvas(host, UITheme.SortBlockingModal);
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            // 全屏半透明遮罩
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            _crisisPanel = UIFactory.Panel(parent, "CrisisCard",
                new UIFactory.Anchor(new Vector2(0.18f, 0.16f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.06f, 0.08f, 0.14f, 0.98f)).gameObject;
            UIFactory.AddBorder(_crisisPanel, UITheme.Danger, new Vector2(2.5f, -2.5f));

            var title = UIFactory.Label(_crisisPanel.transform, "Title",
                "✈️ 停飞危机：飞机突发故障！", 26, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.Danger, FontStyles.Bold);

            UIFactory.Label(_crisisPanel.transform, "Body",
                "执飞北京-上海的 B-1688 号飞机液压系统故障，无法执行明早的航班。请选择处置方案：",
                16, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.68f), new Vector2(0.94f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            _btnFerry = UIFactory.Button(_crisisPanel.transform, "BtnFerry",
                "【调机备援】备用机顶替（2小时恢复）\n现金 -20 万 · 准点 · 满意度不变",
                new UIFactory.Anchor(new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.66f), Vector2.zero, Vector2.zero),
                OrangeBtn, () => Choose(1), 15);

            _btnDelay = UIFactory.Button(_crisisPanel.transform, "BtnDelay",
                "【延误维修】抢修延误 4 小时\n现金 -15 万 · 准点率↓ · 满意度 -3",
                new UIFactory.Anchor(new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.46f), Vector2.zero, Vector2.zero),
                YellowBtn, () => Choose(2), 15);
            // 黄底深字更清晰
            var delayLabel = _btnDelay.GetComponentInChildren<TextMeshProUGUI>();
            if (delayLabel != null) delayLabel.color = new Color(0.12f, 0.1f, 0.05f, 1f);

            _btnAdvisor = UIFactory.Button(_crisisPanel.transform, "BtnAdvisor",
                "？ 【呼叫机务顾问】新手教学 · 免费\n先听建议，再选择调机或维修",
                new UIFactory.Anchor(new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.26f), Vector2.zero, Vector2.zero),
                BlueBtn, () => Choose(3), 15);

            _resultHint = UIFactory.Label(_crisisPanel.transform, "Hint", "", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.1f, 0.01f), new Vector2(0.9f, 0.07f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            BuildAdvisorPanel(parent);

            // 压制 unused warning
            _ = title;
            _root = gameObject;
        }

        private void BuildAdvisorPanel(Transform parent)
        {
            _advisorPanel = UIFactory.Panel(parent, "AdvisorCard",
                new UIFactory.Anchor(new Vector2(0.22f, 0.28f), new Vector2(0.78f, 0.72f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.12f, 0.22f, 0.98f)).gameObject;
            UIFactory.AddBorder(_advisorPanel, UITheme.Accent, new Vector2(2f, -2f));

            UIFactory.Label(_advisorPanel.transform, "AdvTitle", "机务顾问建议", 22, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            UIFactory.Label(_advisorPanel.transform, "AdvBody",
                "调机最稳妥，能保住准点和服务口碑，但成本高（约 20 万）。\n\n" +
                "维修更省钱（约 15 万），但会延误航班，准点率与董事会满意度都会受损。\n\n" +
                "新手建议：手头宽裕时优先调机；现金紧张时可选维修。\n请关闭本说明后，再选择【调机备援】或【延误维修】。",
                15, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.07f, 0.22f), new Vector2(0.93f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Button(_advisorPanel.transform, "AdvClose", "知道了，返回选择",
                new UIFactory.Anchor(new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.18f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnAdvisorDismissed, 16);

            _advisorPanel.SetActive(false);
        }

        #endregion

        #region Show / Hide

        private void Choose(int choice)
        {
            if (AOGManager.Instance != null)
                AOGManager.Instance.OnAOGChoiceMade(choice);
            else
                Debug.LogError("[AOGUI] AOGManager.Instance 为空，无法提交选择。");
        }

        public void ShowCrisis()
        {
            gameObject.SetActive(true);
            if (_crisisPanel != null) _crisisPanel.SetActive(true);
            if (_advisorPanel != null) _advisorPanel.SetActive(false);
            SetChoiceButtonsVisible(true, true, true);
            if (_btnFerry != null) _btnFerry.interactable = true;
            if (_btnDelay != null) _btnDelay.interactable = true;
            if (_btnAdvisor != null) _btnAdvisor.interactable = true;
            if (_resultHint != null) _resultHint.text = "游戏已暂停，请选择处置方案。";
            transform.SetAsLastSibling();
        }

        /// <summary>选项 3：显示顾问说明，随后只保留 1/2。</summary>
        public void ShowAdvisorThenChoices()
        {
            if (_advisorPanel != null)
                _advisorPanel.SetActive(true);
            if (_resultHint != null)
                _resultHint.text = "已呼叫顾问（不扣费）。看完说明后请选调机或维修。";
        }

        private void OnAdvisorDismissed()
        {
            if (_advisorPanel != null)
                _advisorPanel.SetActive(false);
            // 隐藏顾问按钮，避免重复进入；不重复扣钱
            SetChoiceButtonsVisible(true, true, false);
            if (_resultHint != null)
                _resultHint.text = "请选择【调机备援】或【延误维修】。";
        }

        private void SetChoiceButtonsVisible(bool ferry, bool delay, bool advisor)
        {
            if (_btnFerry != null) _btnFerry.gameObject.SetActive(ferry);
            if (_btnDelay != null) _btnDelay.gameObject.SetActive(delay);
            if (_btnAdvisor != null) _btnAdvisor.gameObject.SetActive(advisor);
        }

        public void Hide()
        {
            if (_advisorPanel != null) _advisorPanel.SetActive(false);
            gameObject.SetActive(false);
        }

        #endregion
    }
}
