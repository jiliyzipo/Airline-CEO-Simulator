using System;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 6：续聘投票结果弹窗（头像区 + 可滚动结果文案 + 确认）。
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
            UIFactory.EnsureSortingCanvas(host, UITheme.SortBlockingModal);
            ui.Build(host.transform);
            host.SetActive(false);
            return ui;
        }

        private void Build(Transform parent)
        {
            var dim = UIFactory.Panel(parent, "Dim", UIFactory.Fill, new Color(0.02f, 0.04f, 0.08f, 0.72f), rounded: false);
            dim.GetComponent<Image>().raycastTarget = true;

            _root = UIFactory.Panel(parent, "VoteCard",
                new UIFactory.Anchor(new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.98f)).gameObject;
            UIFactory.AddBorder(_root, UITheme.Accent, new Vector2(2f, -2f));

            var portraitFrame = UIFactory.Panel(_root.transform, "Portrait",
                new UIFactory.Anchor(new Vector2(0.40f, 0.82f), new Vector2(0.60f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            _portrait = portraitFrame.GetComponent<Image>();
            UIFactory.Label(portraitFrame.transform, "Initial", "董", 28, TextAlignmentOptions.Center,
                UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            _titleText = UIFactory.Label(_root.transform, "Title", "续聘投票", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.72f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            // 正文可滚动，底部留给按钮，互不遮挡
            _bodyText = UIFactory.ScrollableLabel(_root.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.70f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 14, TextAlignmentOptions.TopLeft);

            _countdownText = UIFactory.Label(_root.transform, "Countdown", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.17f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            _confirmBtn = UIFactory.Button(_root.transform, "Confirm", "确认",
                new UIFactory.Anchor(new Vector2(0.30f, 0.03f), new Vector2(0.70f, 0.11f), Vector2.zero, Vector2.zero),
                UIFactory.ModalButton, OnConfirmClicked, 18);
        }

        public void Show(YearEndResult yearEnd, Action onConfirm)
        {
            _onConfirm = onConfirm;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            // 年结会 Pause（timeScale=0）：必须用 unscaled，否则淡入永远停在透明并挡住点击
            if (_root != null)
            {
                var cg = _root.GetComponent<CanvasGroup>();
                if (cg == null) cg = _root.AddComponent<CanvasGroup>();
                var cardRt = _root.GetComponent<RectTransform>();
                if (Time.timeScale < 0.01f)
                {
                    cg.alpha = 1f;
                }
                else
                {
                    cg.alpha = 0f;
                    var originalPos = cardRt.anchoredPosition;
                    cardRt.anchoredPosition = originalPos + new Vector2(0, -30f);
                    StartCoroutine(AnimatePopupIn(cg, cardRt, originalPos));
                }
            }

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
                VoteOutcome.Retired => new Color(0.95f, 0.78f, 0.2f, 1f),
                _ => UITheme.TextPrimary
            };

            float satBefore = yearEnd.satisfactionAfter - yearEnd.satisfactionDelta;
            string satDeltaSign = yearEnd.satisfactionDelta >= 0 ? "+" : "";
            string satDeltaStr = $"{satDeltaSign}{yearEnd.satisfactionDelta:F0}";

            var sb = new StringBuilder();

            // ── 考核目标 ──
            sb.AppendLine("■ 考核目标（达成 ≥ 60% 即续聘）");
            if (yearEnd.targetSnapshot != null && yearEnd.targetSnapshot.Count > 0)
            {
                for (int i = 0; i < yearEnd.targetSnapshot.Count; i++)
                {
                    var t = yearEnd.targetSnapshot[i];
                    sb.AppendLine($"  {i + 1}. {t.title}");
                }
            }
            else
            {
                sb.AppendLine("  （无目标数据）");
            }

            sb.AppendLine();

            // ── 本财年达成 ──
            sb.AppendLine("■ 本财年达成");
            if (yearEnd.targetSnapshot != null && yearEnd.targetSnapshot.Count > 0)
            {
                for (int i = 0; i < yearEnd.targetSnapshot.Count; i++)
                {
                    var t = yearEnd.targetSnapshot[i];
                    string mark = t.completed ? "✓" : "✗";
                    string val = $"（实际：{BoardTarget.FormatValue(t.currentValue)}{t.unit}）";
                    sb.AppendLine($"  {mark} {t.title} {val}");
                }
            }
            else
            {
                sb.AppendLine("  （无目标数据）");
            }

            sb.AppendLine();
            string metLabel = yearEnd.completionRate + 0.001f >= yearEnd.reappointmentThreshold
                ? "已达标 ✓"
                : "未达标";
            sb.AppendLine($"  目标完成率：{yearEnd.completionRate:F0}%（{yearEnd.targetsCompleted}/{yearEnd.targetsTotal} 项）· {metLabel}");

            // ── 董事会满意度 ──
            sb.AppendLine();
            sb.AppendLine("■ 董事会满意度");
            sb.AppendLine($"  全体满意度：{satBefore:F0} → {yearEnd.satisfactionAfter:F0}（{satDeltaStr} 点）");

            // ── 董事会判定 ──
            sb.AppendLine();
            sb.AppendLine("■ 董事会判定");
            string body = yearEnd.voteBody ?? yearEnd.message ?? "";
            sb.Append(body);

            // ── 分隔 + 财务摘要（由 ShowVoteResult 追加的 quarterTip）──
            // quarterTip 已在 voteBody 后以 "\n\n" 拼接，这里不重复处理
            // 但需要在判定正文与财务摘要之间加分隔线。由 ShowVoteResult 负责。

            _bodyText.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            var scroll = _bodyText.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;

            _portrait.color = yearEnd.voteOutcome switch
            {
                VoteOutcome.Reappointed => new Color(0.1f, 0.45f, 0.28f, 1f),
                VoteOutcome.ProbationContinue => new Color(0.45f, 0.38f, 0.1f, 1f),
                VoteOutcome.Fired => new Color(0.45f, 0.12f, 0.12f, 1f),
                VoteOutcome.Retired => new Color(0.9f, 0.7f, 0.15f, 1f),
                _ => UITheme.PanelDeep
            };

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
            _countdown -= Time.unscaledDeltaTime;
            if (_countdown <= 0f)
            {
                _counting = false;
                if (_countdownText != null) _countdownText.text = "";
                return;
            }
            UpdateCountdownLabel();
        }

        private void UpdateCountdownLabel()
        {
            if (_countdownText == null) return;
            _countdownText.text = _counting ? $"可随时确认（{Mathf.CeilToInt(_countdown)}）" : "";
        }

        private void OnConfirmClicked()
        {
            var cb = _onConfirm;
            Hide();
            cb?.Invoke();
        }

        // Phase 14：弹窗淡入 + 上滑动效
        private System.Collections.IEnumerator AnimatePopupIn(CanvasGroup cg, RectTransform rt, Vector2 targetPos)
        {
            float duration = 0.4f;
            float elapsed = 0f;
            Vector2 startPos = rt.anchoredPosition;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                t = 1f - (1f - t) * (1f - t); // ease-out quad
                cg.alpha = Mathf.Lerp(0f, 1f, t);
                rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                yield return null;
            }
            cg.alpha = 1f;
            rt.anchoredPosition = targetPos;
        }
    }
}
