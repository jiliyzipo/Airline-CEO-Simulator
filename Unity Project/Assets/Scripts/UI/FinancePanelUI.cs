using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>财务面板：燃油对冲 + 紧急融资 + 新手预设（GDD 7.4 · Phase 9）。</summary>
    public class FinancePanelUI : MonoBehaviour
    {
        private struct HedgePreset
        {
            public string id;
            public string title;
            public string blurb;
            public float ratio;
            public int termMonths;
            public string lockType;
            public float displaySaveWan;
        }

        private static readonly HedgePreset[] Presets =
        {
            new HedgePreset
            {
                id = "safe",
                title = "🛡️ 保本稳妥",
                blurb = "30% · 6月 · 固定价\n油价涨跌都平稳，最适合新手",
                ratio = 0.30f,
                termMonths = 6,
                lockType = HedgeLockTypes.Fixed,
                displaySaveWan = 2.3f
            },
            new HedgePreset
            {
                id = "balanced",
                title = "⚖️ 稳健平衡",
                blurb = "30% · 6月 · 区间价\n震荡时免费保护，适合有经验玩家",
                ratio = 0.30f,
                termMonths = 6,
                lockType = HedgeLockTypes.Collar,
                displaySaveWan = 1.8f
            },
            new HedgePreset
            {
                id = "aggressive",
                title = "🚀 进取博弈",
                blurb = "50% · 12月 · 看涨期权\n判断大涨时用，高风险高回报",
                ratio = 0.50f,
                termMonths = 12,
                lockType = HedgeLockTypes.Call,
                displaySaveWan = 4.5f
            }
        };

        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _priceText;
        private TextMeshProUGUI _historyText;
        private TextMeshProUGUI _lockPreviewText;
        private TextMeshProUGUI _hedgeListText;
        private TextMeshProUGUI _financeHintText;
        private TextMeshProUGUI _loanListText;
        private TextMeshProUGUI _toastText;
        private TextMeshProUGUI _collarHintText;
        private TextMeshProUGUI _estimateText;
        private TextMeshProUGUI _selectedPresetLabel;

        private float _ratio = 0.30f;
        private int _termMonths = 6;
        private string _lockType = HedgeLockTypes.Fixed;
        private float _floorOffset = 0.05f;
        private float _capOffset = 0.05f;
        private string _activePresetId = "safe";
        private float? _forcedEstimateWan = 2.3f;

        private GameObject _financeButtonsRoot;
        private GameObject _collarRow;
        private HedgeTutorialUI _tutorialUi;
        private readonly List<Button> _ratioBtns = new List<Button>();
        private readonly List<Button> _termBtns = new List<Button>();
        private readonly List<Button> _lockTypeBtns = new List<Button>();
        private readonly List<Button> _floorBtns = new List<Button>();
        private readonly List<Button> _capBtns = new List<Button>();
        private readonly List<Button> _presetBtns = new List<Button>();

        #region Build

        public static FinancePanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Finance", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<FinancePanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public void BindTutorial(HedgeTutorialUI tutorial) => _tutorialUi = tutorial;

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "财务中心 · 对冲与融资", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.94f), new Vector2(0.48f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(panel, "Tip",
                "💡 燃油对冲：锁定未来油价，避免暴涨风险。不明白？",
                12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.885f), new Vector2(0.62f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Button(panel, "AskAdvisor", "问问财务顾问 👉",
                new UIFactory.Anchor(new Vector2(0.63f, 0.885f), new Vector2(0.97f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Accent, AskAdvisorAgain, 12);

            _statusText = UIFactory.Label(panel, "Status", "", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.83f), new Vector2(0.97f, 0.885f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var left = UIFactory.Panel(panel, "HedgeCol",
                new UIFactory.Anchor(new Vector2(0.02f, 0.04f), new Vector2(0.49f, 0.825f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(left, UITheme.Border, new Vector2(1f, -1f));
            BuildHedgeColumn(left.transform);

            var right = UIFactory.Panel(panel, "LoanCol",
                new UIFactory.Anchor(new Vector2(0.51f, 0.04f), new Vector2(0.98f, 0.825f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(right, UITheme.Border, new Vector2(1f, -1f));
            BuildLoanColumn(right.transform);

            _toastText = UIFactory.Label(panel, "Toast", "", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.15f, 0.0f), new Vector2(0.85f, 0.04f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
        }

        private void BuildHedgeColumn(Transform col)
        {
            UIFactory.Label(col, "HTitle", "燃油对冲", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            _priceText = UIFactory.Label(col, "Price", "当前油价：$850 / 吨", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.945f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            _historyText = UIFactory.Label(col, "Hist", "走势：—", 10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(col, "PresetL", "推荐方案", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.815f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            for (int i = 0; i < Presets.Length; i++)
            {
                int idx = i;
                float x0 = 0.03f + i * 0.32f;
                float x1 = x0 + 0.30f;
                var p = Presets[i];
                var btn = UIFactory.Button(col, "Preset_" + p.id,
                    p.title + "\n" + p.blurb.Split('\n')[0],
                    new UIFactory.Anchor(new Vector2(x0, 0.68f), new Vector2(x1, 0.815f), Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => ApplyPreset(Presets[idx]), 10);
                _presetBtns.Add(btn);
            }

            _selectedPresetLabel = UIFactory.Label(col, "SelPreset", "已选：保本稳妥", 11,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.635f), new Vector2(0.55f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _estimateText = UIFactory.Label(col, "Estimate", "预计每月节省 $2.3 万", 13,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.40f, 0.635f), new Vector2(0.96f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.Success, FontStyles.Bold);

            UIFactory.Label(col, "CustomL", "自定义设置", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.59f), new Vector2(0.96f, 0.635f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(col, "RatioL", "比例", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.535f), new Vector2(0.2f, 0.59f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _ratioBtns.Add(MakeChoice(col, "R0", "0%", 0.22f, 0.535f, 0.24f, () => SetRatio(0f)));
            _ratioBtns.Add(MakeChoice(col, "R30", "30%", 0.48f, 0.535f, 0.24f, () => SetRatio(0.30f)));
            _ratioBtns.Add(MakeChoice(col, "R50", "50%", 0.74f, 0.535f, 0.22f, () => SetRatio(0.5f)));

            UIFactory.Label(col, "TermL", "期限", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.475f), new Vector2(0.2f, 0.53f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _termBtns.Add(MakeChoice(col, "T3", "3月", 0.22f, 0.475f, 0.24f, () => SetTerm(3)));
            _termBtns.Add(MakeChoice(col, "T6", "6月", 0.48f, 0.475f, 0.24f, () => SetTerm(6)));
            _termBtns.Add(MakeChoice(col, "T12", "12月", 0.74f, 0.475f, 0.22f, () => SetTerm(12)));

            UIFactory.Label(col, "LockL", "锁价方式", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.42f), new Vector2(0.96f, 0.47f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _lockTypeBtns.Add(MakeChoice(col, "LT_F", "固定价", 0.04f, 0.36f, 0.30f,
                () => SetLockType(HedgeLockTypes.Fixed)));
            _lockTypeBtns.Add(MakeChoice(col, "LT_C", "区间价", 0.36f, 0.36f, 0.30f,
                () => SetLockType(HedgeLockTypes.Collar)));
            _lockTypeBtns.Add(MakeChoice(col, "LT_O", "看涨期权", 0.68f, 0.36f, 0.28f,
                () => SetLockType(HedgeLockTypes.Call)));

            // 区间价档位区：单独拉高，避免按钮按父级比例算成细线
            _collarRow = UIFactory.Panel(col, "CollarRow",
                new UIFactory.Anchor(new Vector2(0.02f, 0.195f), new Vector2(0.98f, 0.355f), Vector2.zero, Vector2.zero),
                new Color(0.08f, 0.14f, 0.24f, 0.55f), rounded: true).gameObject;
            UIFactory.Label(_collarRow.transform, "FloorL", "下限", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.52f), new Vector2(0.18f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _floorBtns.Add(MakeChoice(_collarRow.transform, "F5", "-5%", 0.18f, 0.52f, 0.26f, () => SetFloor(0.05f), 0.40f));
            _floorBtns.Add(MakeChoice(_collarRow.transform, "F8", "-8%", 0.46f, 0.52f, 0.26f, () => SetFloor(0.08f), 0.40f));
            _floorBtns.Add(MakeChoice(_collarRow.transform, "F10", "-10%", 0.74f, 0.52f, 0.26f, () => SetFloor(0.10f), 0.40f));
            UIFactory.Label(_collarRow.transform, "CapL", "上限", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.05f), new Vector2(0.18f, 0.48f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _capBtns.Add(MakeChoice(_collarRow.transform, "C5", "+5%", 0.18f, 0.05f, 0.26f, () => SetCap(0.05f), 0.40f));
            _capBtns.Add(MakeChoice(_collarRow.transform, "C8", "+8%", 0.46f, 0.05f, 0.26f, () => SetCap(0.08f), 0.40f));
            _capBtns.Add(MakeChoice(_collarRow.transform, "C10", "+10%", 0.74f, 0.05f, 0.26f, () => SetCap(0.10f), 0.40f));
            _collarHintText = UIFactory.Label(col, "CollarHint", "", 10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.155f), new Vector2(0.96f, 0.195f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _lockPreviewText = UIFactory.Label(col, "LockPrev", "", 11, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.115f), new Vector2(0.96f, 0.155f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            UIFactory.Button(col, "ConfirmHedge", "确认对冲",
                new UIFactory.Anchor(new Vector2(0.18f, 0.055f), new Vector2(0.82f, 0.115f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnConfirmHedge, 14);

            _hedgeListText = UIFactory.Label(col, "HList", "合约：无", 10, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.005f), new Vector2(0.96f, 0.055f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        private void BuildLoanColumn(Transform col)
        {
            UIFactory.Label(col, "LTitle", "紧急融资", 18, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Warning, FontStyles.Bold);

            _financeHintText = UIFactory.Label(col, "Hint", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.72f), new Vector2(0.96f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _financeButtonsRoot = UIFactory.Panel(col, "FinBtns",
                new UIFactory.Anchor(new Vector2(0.04f, 0.28f), new Vector2(0.96f, 0.72f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false).gameObject;
            _financeButtonsRoot.GetComponent<Image>().raycastTarget = false;

            UIFactory.Button(_financeButtonsRoot.transform, "Bank", "银行贷款\n3～6月现金 · 利率6～10%\n需信用≥BBB",
                new UIFactory.Anchor(new Vector2(0f, 0.68f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => DoLoan(1), 13);

            UIFactory.Button(_financeButtonsRoot.transform, "Bridge", "高息过桥\n1～3月现金 · 利率20%+\n无信用限制",
                new UIFactory.Anchor(new Vector2(0f, 0.34f), new Vector2(1f, 0.64f), Vector2.zero, Vector2.zero),
                new Color(1f, 0.45f, 0.1f, 1f), () => DoLoan(2), 13);

            UIFactory.Button(_financeButtonsRoot.transform, "Equity", "股东增资\n6～12月现金 · 股权稀释\n需董事会满意度≥40",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.3f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => DoLoan(3), 13);

            _loanListText = UIFactory.Label(col, "LoanList", "贷款：无", 12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        private Button MakeChoice(Transform parent, string name, string label, float xMin, float yMin,
            float width, UnityEngine.Events.UnityAction action, float height = 0.048f)
        {
            return UIFactory.Button(parent, name, label,
                new UIFactory.Anchor(new Vector2(xMin, yMin), new Vector2(xMin + width, yMin + height),
                    Vector2.zero, Vector2.zero),
                UITheme.NavIdle, action, 12);
        }

        /// <summary>设置选中色并同步悬停基准，避免鼠标移开后颜色被还原。</summary>
        private static void SetChoiceVisual(Button btn, bool selected)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = selected ? UITheme.NavActive : UITheme.NavIdle;
            var hover = btn.GetComponent<UIHoverBrighten>();
            if (hover != null) hover.RefreshBase();
        }

        #endregion

        #region Open / Tutorial

        /// <summary>导航切入财务时调用：刷新 + 首次引导。</summary>
        public void OnOpened()
        {
            Refresh();
            var gs = GameState.Instance;
            if (gs == null) return;
            if (!gs.hasSeenHedgeTutorial)
                ShowTutorial();
        }

        public void AskAdvisorAgain()
        {
            ShowTutorial();
        }

        private void ShowTutorial()
        {
            if (_tutorialUi == null)
                _tutorialUi = FindFirstObjectByType<HedgeTutorialUI>(FindObjectsInactive.Include);
            if (_tutorialUi == null)
            {
                Debug.LogWarning("[Finance] HedgeTutorialUI 未找到");
                var gs = GameState.Instance;
                if (gs != null) gs.hasSeenHedgeTutorial = true;
                return;
            }

            _tutorialUi.Show(OnTutorialTrySafe, OnTutorialDiy);
        }

        private void OnTutorialTrySafe()
        {
            ApplyPreset(Presets[0]);
            Toast("已填充「保本稳妥」方案，确认对冲即可生效。");
        }

        private void OnTutorialDiy()
        {
            Toast("可自由选择推荐方案或自定义设置。");
        }

        #endregion

        #region Refresh

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var credit = LoanManager.GetCreditRating(gs);
            if (_statusText != null)
            {
                _statusText.text =
                    $"{CashRunnelCalculator.FormatPlayerLabel(runway)} · {CashRunnelCalculator.FormatWarningPlain(runway)}" +
                    $" ｜ 信用 {LoanManager.CreditLabel(credit)} ｜ 现金 {gs.cash:F0} 万";
            }

            if (_priceText != null)
                _priceText.text = $"当前油价：${gs.fuelPricePerTon:F0} / 吨";

            if (_historyText != null)
                _historyText.text = "走势：" + FormatHistory(gs.fuelPriceHistory);

            UpdateEstimate();
            UpdateLockPreview(gs);
            HighlightChoices();
            RefreshHedgeList(gs);
            RefreshFinanceSide(gs, runway);
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }

        private static string FormatHistory(List<float> hist)
        {
            if (hist == null || hist.Count == 0) return "—";
            var sb = new StringBuilder();
            int start = Mathf.Max(0, hist.Count - 8);
            for (int i = start; i < hist.Count; i++)
            {
                if (i > start) sb.Append(" → ");
                sb.Append($"${hist[i]:F0}");
            }
            return sb.ToString();
        }

        private void UpdateEstimate()
        {
            if (_estimateText == null) return;
            float wan;
            if (_forcedEstimateWan.HasValue)
                wan = _forcedEstimateWan.Value;
            else if (_ratio <= 0.001f)
                wan = 0f;
            else
            {
                // 自定义：按「油价再涨 8%」粗算月节省
                var gs = GameState.Instance;
                float spot = gs != null ? gs.fuelPricePerTon : 850f;
                float tons = HedgeManager.EstimateMonthlyFuelTons(gs) * _ratio;
                wan = tons * spot * 0.08f / 10000f;
                if (_lockType == HedgeLockTypes.Collar) wan *= 0.78f;
                if (_lockType == HedgeLockTypes.Call) wan *= 1.35f;
            }

            _estimateText.text = wan <= 0.01f
                ? "预计每月节省 —"
                : $"预计每月节省 ${wan:F1} 万";
        }

        private void UpdateLockPreview(GameState gs)
        {
            if (_lockPreviewText == null) return;
            bool isCollar = _lockType == HedgeLockTypes.Collar;
            if (_collarRow != null) _collarRow.SetActive(isCollar);
            if (_collarHintText != null)
                _collarHintText.gameObject.SetActive(isCollar);

            if (_ratio <= 0.001f)
            {
                _lockPreviewText.text = "比例 0%：不建仓。";
                return;
            }

            float spot = gs.fuelPricePerTon;
            float tons = HedgeManager.EstimateMonthlyFuelTons(gs) * _ratio * _termMonths;

            if (_lockType == HedgeLockTypes.Collar)
            {
                float floor = spot * (1f - _floorOffset);
                float cap = spot * (1f + _capOffset);
                if (_collarHintText != null)
                    _collarHintText.text = $"区间：[${floor:F1}, ${cap:F1}] · 零额外溢价";
                _lockPreviewText.text =
                    $"预览【区间震荡保护】总量约 {tons:F0} 吨\n区间内无损益；破上限削顶盈利，破下限保底亏损";
                return;
            }

            if (_lockType == HedgeLockTypes.Call)
            {
                float lockPx = HedgeManager.CalcLockPrice(spot, _termMonths);
                float perTon = HedgeManager.CallPremiumPerTon(spot, _termMonths);
                float premWan = perTon * tons / 10000f;
                _lockPreviewText.text =
                    $"预览【期权保险】行权价 ${lockPx:F1}/吨\n权利金 ${perTon:F1}/吨 × {tons:F0} 吨 ≈ {premWan:F2} 万（确认时立即扣除）";
                return;
            }

            float lockFixed = HedgeManager.CalcLockPrice(spot, _termMonths);
            float prem = HedgeManager.PremiumForTerm(_termMonths);
            _lockPreviewText.text =
                $"预览【固定价锁死】锁定价 ${lockFixed:F1}/吨（溢价 {prem * 100:F0}%）· 总量约 {tons:F0} 吨";
        }

        private void HighlightChoices()
        {
            if (_ratioBtns.Count >= 3)
            {
                SetChoiceVisual(_ratioBtns[0], Mathf.Abs(_ratio) < 0.01f);
                SetChoiceVisual(_ratioBtns[1], Mathf.Abs(_ratio - 0.30f) < 0.01f);
                SetChoiceVisual(_ratioBtns[2], Mathf.Abs(_ratio - 0.5f) < 0.01f);
            }

            if (_termBtns.Count >= 3)
            {
                SetChoiceVisual(_termBtns[0], _termMonths == 3);
                SetChoiceVisual(_termBtns[1], _termMonths == 6);
                SetChoiceVisual(_termBtns[2], _termMonths == 12);
            }

            if (_lockTypeBtns.Count >= 3)
            {
                SetChoiceVisual(_lockTypeBtns[0], _lockType == HedgeLockTypes.Fixed);
                SetChoiceVisual(_lockTypeBtns[1], _lockType == HedgeLockTypes.Collar);
                SetChoiceVisual(_lockTypeBtns[2], _lockType == HedgeLockTypes.Call);
            }

            for (int i = 0; i < _presetBtns.Count && i < Presets.Length; i++)
                SetChoiceVisual(_presetBtns[i], _activePresetId == Presets[i].id);

            HighlightOffsetBtns(_floorBtns, _floorOffset);
            HighlightOffsetBtns(_capBtns, _capOffset);
        }

        private static void HighlightOffsetBtns(List<Button> btns, float selected)
        {
            if (btns == null || btns.Count < 3) return;
            float[] opts = { 0.05f, 0.08f, 0.10f };
            for (int i = 0; i < 3; i++)
                SetChoiceVisual(btns[i], Mathf.Abs(selected - opts[i]) < 0.001f);
        }

        private void RefreshHedgeList(GameState gs)
        {
            if (_hedgeListText == null) return;
            if (gs.hedgeContracts == null || gs.hedgeContracts.Count == 0)
            {
                _hedgeListText.text = "合约：无";
                return;
            }

            var sb = new StringBuilder("合约：\n");
            for (int i = 0; i < gs.hedgeContracts.Count; i++)
            {
                var c = gs.hedgeContracts[i];
                string t = string.IsNullOrEmpty(c.lockType) ? HedgeLockTypes.Fixed : c.lockType;
                string extra = t switch
                {
                    HedgeLockTypes.Collar => $"[{c.floorPrice:F0}~{c.capPrice:F0}]",
                    HedgeLockTypes.Call => $"权金{c.premiumPaid:F1}万",
                    _ => $"锁${c.lockPrice:F0}"
                };
                sb.AppendLine(
                    $"· {HedgeLockTypes.DisplayName(t)} {c.hedgeRatio * 100:F0}%/{c.termMonths}月 {extra} 余{c.monthsRemaining}月");
            }
            _hedgeListText.text = sb.ToString().TrimEnd();
        }

        private void RefreshFinanceSide(GameState gs, float runway)
        {
            bool unlock = LoanManager.IsFinanceUnlocked(gs);
            if (_financeButtonsRoot != null)
                _financeButtonsRoot.SetActive(unlock);

            if (_financeHintText != null)
            {
                if (!unlock)
                {
                    _financeHintText.text =
                        "暂无融资需求。\n当现金跑道 < 3 个月时，将解锁银行贷款 / 过桥 / 股东增资。\n" +
                        $"当前信用评级：{LoanManager.CreditLabel(LoanManager.GetCreditRating(gs))}。";
                }
                else
                {
                    _financeHintText.text =
                        $"⚠ 现金告急（跑道 {runway:F1} 月）· 信用 {LoanManager.CreditLabel(LoanManager.GetCreditRating(gs))}\n" +
                        "请选择融资方式：";
                }
            }

            if (_loanListText == null) return;
            if (gs.activeLoans == null || gs.activeLoans.Count == 0)
            {
                _loanListText.text = "贷款：无";
                return;
            }

            var sb = new StringBuilder("贷款：\n");
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L == null) continue;
                if (L.type == "equity")
                    sb.AppendLine($"· 增资 {L.principal:F0} 万（{L.createdAt}）");
                else
                    sb.AppendLine(
                        $"· {L.id} 余本 {L.remainingPrincipal:F0} · 月供 {L.monthlyPayment:F1} · 余 {L.monthsRemaining}月");
            }
            _loanListText.text = sb.ToString().TrimEnd();
        }

        #endregion

        #region Actions

        private void ApplyPreset(HedgePreset preset)
        {
            _activePresetId = preset.id;
            _ratio = preset.ratio;
            _termMonths = preset.termMonths;
            _lockType = preset.lockType;
            _floorOffset = 0.05f;
            _capOffset = 0.05f;
            _forcedEstimateWan = preset.displaySaveWan;
            if (_selectedPresetLabel != null)
                _selectedPresetLabel.text = "已选：" + preset.title.Replace("🛡️ ", "").Replace("⚖️ ", "").Replace("🚀 ", "");
            Refresh();
        }

        private void SetRatio(float r)
        {
            _ratio = r;
            ClearForcedPreset();
            Refresh();
        }

        private void SetTerm(int m)
        {
            _termMonths = m;
            ClearForcedPreset();
            Refresh();
        }

        private void SetLockType(string t)
        {
            _lockType = t;
            ClearForcedPreset();
            Refresh();
        }

        private void SetFloor(float off)
        {
            _floorOffset = off;
            ClearForcedPreset();
            Refresh();
        }

        private void SetCap(float off)
        {
            _capOffset = off;
            ClearForcedPreset();
            Refresh();
        }

        private void ClearForcedPreset()
        {
            _forcedEstimateWan = null;
            _activePresetId = null;
            if (_selectedPresetLabel != null)
                _selectedPresetLabel.text = "已选：自定义";
        }

        private void OnConfirmHedge()
        {
            if (HedgeManager.Instance == null)
            {
                Toast("HedgeManager 未就绪");
                return;
            }

            HedgeManager.Instance.TryOpenHedge(
                _ratio, _termMonths, _lockType, _floorOffset, _capOffset, out string msg);
            Toast(msg);
            Refresh();
        }

        private void DoLoan(int kind)
        {
            if (LoanManager.Instance == null)
            {
                Toast("LoanManager 未就绪");
                return;
            }

            string message;
            bool ok = kind switch
            {
                1 => LoanManager.Instance.TryBankLoan(out message),
                2 => LoanManager.Instance.TryBridgeLoan(out message),
                3 => LoanManager.Instance.TryEquityRaise(out message),
                _ => FailUnknown(out message)
            };
            Toast(message);
            Refresh();
            _ = ok;
        }

        private static bool FailUnknown(out string message)
        {
            message = "未知融资类型";
            return false;
        }

        private void Toast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
            Debug.Log("[Finance] " + msg);
        }

        #endregion
    }
}
