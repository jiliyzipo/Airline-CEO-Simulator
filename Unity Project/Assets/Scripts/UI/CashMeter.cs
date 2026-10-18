using System.Collections;
using AirlineCEO.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 横向「油量表」现金跑道：填充条 + 指针，随现金/烧钱变化移动。
    /// </summary>
    public class CashMeter : MonoBehaviour
    {
        [SerializeField] private Image trackImage;
        [SerializeField] private Image fillImage;
        [SerializeField] private RectTransform needle;
        [SerializeField] private TextMeshProUGUI cashText;
        [SerializeField] public TextMeshProUGUI runwayText;
        [SerializeField] private TextMeshProUGUI titleText;

        private float _displayMonths;
        private float _targetMonths;
        private float _displayCash;
        private float _targetCash;
        private float _revenueHint;
        private Coroutine _anim;
        private const float MaxMonthsOnGauge = 6f;

        public TextMeshProUGUI CashLabel => cashText;
        public float DisplayCash => _displayCash;

        public static CashMeter BuildRuntime(Transform parent, UIFactory.Anchor anchor)
        {
            var root = UIFactory.Panel(parent, "CashMeter", anchor, new Color(0, 0, 0, 0), rounded: false);
            var meter = root.AddComponent<CashMeter>();

            meter.titleText = UIFactory.Label(root.transform, "Title", "现金 / 跑道", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0, 0.72f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            meter.cashText = UIFactory.Label(root.transform, "Cash", "500 万美元", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0, 0.42f), new Vector2(0.55f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Success, FontStyles.Bold);

            meter.runwayText = UIFactory.Label(root.transform, "RunwayLabel", "撑 — 个月", 13,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.55f, 0.42f), new Vector2(1, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            meter.runwayText.textWrappingMode = TextWrappingModes.NoWrap;
            meter.runwayText.overflowMode = TextOverflowModes.Truncate;

            meter.cashText.textWrappingMode = TextWrappingModes.NoWrap;
            meter.cashText.overflowMode = TextOverflowModes.Truncate;

            var track = UIFactory.Panel(root.transform, "Track",
                new UIFactory.Anchor(new Vector2(0, 0.08f), new Vector2(1, 0.36f), Vector2.zero, Vector2.zero),
                new Color(0.06f, 0.1f, 0.16f, 1f));
            meter.trackImage = track.GetComponent<Image>();

            var fillGo = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, UITheme.Success);
            meter.fillImage = fillGo.GetComponent<Image>();
            meter.fillImage.sprite = UIFactory.RoundedSprite;
            meter.fillImage.type = Image.Type.Filled;
            meter.fillImage.fillMethod = Image.FillMethod.Horizontal;
            meter.fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            meter.fillImage.fillAmount = 0.5f;

            var needleGo = new GameObject("Needle", typeof(RectTransform), typeof(Image));
            needleGo.transform.SetParent(track.transform, false);
            var nrt = needleGo.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.5f, 0f);
            nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.sizeDelta = new Vector2(4f, 8f);
            nrt.anchoredPosition = Vector2.zero;
            var nImg = needleGo.GetComponent<Image>();
            nImg.sprite = UIFactory.RoundedSprite;
            nImg.type = Image.Type.Sliced;
            nImg.color = UITheme.TextPrimary;
            nImg.raycastTarget = false;
            meter.needle = nrt;

            var cap = new GameObject("Cap", typeof(RectTransform), typeof(Image));
            cap.transform.SetParent(needleGo.transform, false);
            var crt = cap.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(10f, 8f);
            crt.anchoredPosition = new Vector2(0f, 2f);
            var cImg = cap.GetComponent<Image>();
            cImg.sprite = UIFactory.CircleSprite;
            cImg.color = UITheme.Accent;
            cImg.raycastTarget = false;

            return meter;
        }

        public void SetImmediate(float cash, float months, float monthlyRevenue = 0f)
        {
            _targetCash = cash;
            _displayCash = cash;
            _targetMonths = months;
            _displayMonths = months;
            _revenueHint = monthlyRevenue;
            ApplyVisual(_displayCash, _displayMonths);
        }

        public void AnimateTo(float cash, float months, float monthlyRevenue, float duration = 0.7f)
        {
            _targetCash = cash;
            _targetMonths = months;
            _revenueHint = monthlyRevenue;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimateRoutine(duration));
        }

        private IEnumerator AnimateRoutine(float duration)
        {
            float fromCash = _displayCash;
            float fromMonths = _displayMonths;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                p = 1f - (1f - p) * (1f - p);
                _displayCash = Mathf.Lerp(fromCash, _targetCash, p);
                _displayMonths = Mathf.Lerp(fromMonths, _targetMonths, p);
                ApplyVisual(_displayCash, _displayMonths);
                yield return null;
            }

            _displayCash = _targetCash;
            _displayMonths = _targetMonths;
            ApplyVisual(_displayCash, _displayMonths);
            _anim = null;
        }

        private void ApplyVisual(float cash, float months)
        {
            float amount = Mathf.Clamp01(months / MaxMonthsOnGauge);
            if (fillImage != null)
            {
                fillImage.fillAmount = amount;
                fillImage.color = UITheme.RunwayColor(months);
            }

            if (needle != null && needle.parent is RectTransform track)
            {
                float w = track.rect.width;
                if (w <= 1f) w = ((RectTransform)transform).rect.width;
                float x = (amount - 0.5f) * w;
                needle.anchoredPosition = new Vector2(x, 0f);
            }

            if (cashText != null)
            {
                string hint = _revenueHint > 0.5f ? $"  ·月入{_revenueHint:F0}万美元" : "";
                cashText.text = $"{cash:F0} 万美元{hint}";
                cashText.color = cash < 0f ? UITheme.Danger : UITheme.Success;
            }

            if (runwayText != null)
            {
                if (months >= CashRunnelCalculator.InfiniteRunwayMonths - 0.1f)
                    runwayText.text = "流动性宽裕";
                else
                    runwayText.text = $"撑 {months:F1} 月 · {CashRunnelCalculator.FormatWarningPlain(months)}";
            }
        }

        private void LateUpdate()
        {
            if (needle != null && fillImage != null)
                ApplyNeedleOnly(fillImage.fillAmount);
        }

        private void ApplyNeedleOnly(float amount)
        {
            if (needle == null || !(needle.parent is RectTransform track)) return;
            float w = track.rect.width;
            if (w <= 1f) return;
            needle.anchoredPosition = new Vector2((amount - 0.5f) * w, 0f);
        }
    }
}
