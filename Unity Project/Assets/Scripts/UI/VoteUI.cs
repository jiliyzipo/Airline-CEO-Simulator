using System;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 6：续聘投票结果弹窗（头像区 + 结果文案 + 确认）。
    /// </summary>
    public class VoteUI : MonoBehaviour
    {
        private GameObject _root;
        private Image _portrait;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _bodyText;
        private TextMeshProUGUI _countdownText;
        private Button _confirmBtn;
        private Action _onConfirm;
        private float _countdown;
        private bool _counting;

        public static VoteUI Create(Transform canvasParent)
        {
            var host = new GameObject("VoteUI", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<VoteUI>();
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            // 遮罩
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill, new Color(0.02f, 0.04f, 0.08f, 0.72f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            _root = UIFactory.Panel(parent, "VoteCard",
                new UIFactory.Anchor(new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.82f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.98f)).gameObject;
            UIFactory.AddBorder(_root, UITheme.Accent, new Vector2(2f, -2f));

            // 头像占位（董事会席）
            var portraitFrame = UIFactory.Panel(_root.transform, "Portrait",
                new UIFactory.Anchor(new Vector2(0.38f, 0.68f), new Vector2(0.62f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            _portrait = portraitFrame.GetComponent<Image>();
            UIFactory.Label(portraitFrame.transform, "Initial", "董", 36, TextAlignmentOptions.Center,
                UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            _titleText = UIFactory.Label(_root.transform, "Title", "续聘投票", 26, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.54f), new Vector2(0.94f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _bodyText = UIFactory.Label(_root.transform, "Body", "", 16, TextAlignmentOptions.Top,
                new UIFactory.Anchor(new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.54f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _countdownText = UIFactory.Label(_root.transform, "Countdown", "", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.1f, 0.14f), new Vector2(0.9f, 0.22f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            _confirmBtn = UIFactory.Button(_root.transform, "Confirm", "确认",
                new UIFactory.Anchor(new Vector2(0.32f, 0.05f), new Vector2(0.68f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnConfirmClicked, 18);
        }

        public void Show(YearEndResult yearEnd, Action onConfirm)
        {
            _onConfirm = onConfirm;
            gameObject.SetActive(true);

            if (yearEnd == null)
            {
                _titleText.text = "年度结算";
                _bodyText.text = "无结算数据。";
                return;
            }

            _titleText.text = string.IsNullOrEmpty(yearEnd.voteTitle) ? "续聘投票" : yearEnd.voteTitle;
            _titleText.color = yearEnd.voteOutcome switch
            {
                VoteOutcome.Reappointed => UITheme.Success,
                VoteOutcome.ProbationContinue => UITheme.Warning,
                VoteOutcome.Fired => UITheme.Danger,
                _ => UITheme.TextPrimary
            };

            string rateLine =
                $"完成率 {yearEnd.completionRate:F0}%（{yearEnd.targetsCompleted}/{yearEnd.targetsTotal}）" +
                $" · 续聘线 {yearEnd.reappointmentThreshold:F0}%\n" +
                $"满意度 {(yearEnd.satisfactionDelta >= 0 ? "+" : "")}{yearEnd.satisfactionDelta:F0} → {yearEnd.satisfactionAfter:F0}\n\n";

            _bodyText.text = rateLine + (yearEnd.voteBody ?? yearEnd.message ?? "");

            _portrait.color = yearEnd.voteOutcome switch
            {
                VoteOutcome.Reappointed => new Color(0.1f, 0.45f, 0.28f, 1f),
                VoteOutcome.ProbationContinue => new Color(0.45f, 0.38f, 0.1f, 1f),
                VoteOutcome.Fired => new Color(0.45f, 0.12f, 0.12f, 1f),
                _ => UITheme.PanelDeep
            };

            // 简短倒计时提示（可立即点确认）
            _countdown = 2.5f;
            _counting = true;
            _confirmBtn.interactable = true;
            UpdateCountdownLabel();
        }

        public void Hide()
        {
            _counting = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_counting) return;
            _countdown -= Time.deltaTime;
            UpdateCountdownLabel();
            if (_countdown <= 0f)
                _counting = false;
        }

        private void UpdateCountdownLabel()
        {
            if (_countdownText == null) return;
            if (_countdown > 0f)
                _countdownText.text = $"投票结果确认中… {Mathf.CeilToInt(_countdown)}s（可直接确认）";
            else
                _countdownText.text = "请点击确认继续";
        }

        private void OnConfirmClicked()
        {
            Hide();
            _onConfirm?.Invoke();
        }
    }
}
