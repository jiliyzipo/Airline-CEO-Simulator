using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// SAF 安全质量部 L1（n19）：安全总览仪表 + 安全评级徽章 + 五大功能入口 + 安全文化。
    /// </summary>
    public class SafetyPanelUI : MonoBehaviour
    {
        private GameObject _homeRoot;
        private TextMeshProUGUI _kpiText;
        private TextMeshProUGUI _ratingText;
        private TextMeshProUGUI _cultureText;

        public static SafetyPanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_SAF2", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<SafetyPanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public static void OpenSafetyPage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) { nav.OpenSubPage(pageId); return; }
            var router = NavigationRouter.Instance;
            if (router != null) router.Navigate(pageId, replaceCurrent: false);
            else Debug.LogWarning("[SAF] NavigationRouter 未就绪：" + pageId);
        }

        public void OnOpened()
        {
            if (_homeRoot != null) _homeRoot.SetActive(true);
            Refresh();
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            char band = SafetyEconomics.RatingBand(saf.ratingScore);
            if (_kpiText != null)
                _kpiText.text = $"零重大事故天数：{saf.zeroAccidentDays} 天    审计通过率：{saf.auditPassRate:F0}%\n隐患整改率：{saf.rectificationRate:F0}%    安全预算使用率：{(saf.safetyBudgetWan > 0 ? saf.safetyBudgetUsedWan / saf.safetyBudgetWan * 100f : 0f):F0}%";
            if (_ratingText != null)
            {
                bool takeover = SafetyEconomics.RegulatorTakeoverRisk(saf.ratingScore);
                _ratingText.text = $"安全评级：{band} 级（{saf.ratingScore:F0}/100）· {(takeover ? "D级触发局方接管风险" : "评级影响航权与董事会考核")}";
                _ratingText.color = band == 'A' ? UITheme.Success : band == 'B' ? UITheme.Accent : band == 'C' ? UITheme.Warning : UITheme.Danger;
            }
            if (_cultureText != null)
            {
                _cultureText.text = $"安全文化：{SafetyManager.CultureName(saf.cultureType)} · 文化指数 {saf.cultureIndex:F0} · 隐藏风险倍率 {SafetyEconomics.CultureHiddenRisk(saf.cultureType):F1}";
            }
        }

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "安全质量部 · 安全运营的守门人与免疫系统", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.94f), new Vector2(0.52f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _homeRoot = UIFactory.Panel(panel, "SafHome",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _homeRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            _kpiText = UIFactory.Label(_homeRoot.transform, "Kpi", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.80f), new Vector2(0.52f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _kpiText.textWrappingMode = TextWrappingModes.Normal;

            _ratingText = UIFactory.Label(_homeRoot.transform, "Rating", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.54f, 0.86f), new Vector2(0.99f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _cultureText = UIFactory.Label(_homeRoot.transform, "Culture", "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.54f, 0.74f), new Vector2(0.99f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            // 五大功能入口
            UIFactory.Label(_homeRoot.transform, "EntryHdr", "五大功能入口", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.68f), new Vector2(0.30f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            AddEntry(0, "📊 SMS与风险管理", "n92_SMS与风险管理");
            AddEntry(1, "🔍 安全监察与审核", "n93_安全监察与审核");
            AddEntry(2, "🔬 事件调查与分析", "n94_事件调查与分析");
            AddEntry(3, "🏛 局方与法规事务", "n95_局方与法规事务");
            AddEntry(4, "💡 安全文化与培训", "n96_安全文化与培训");

            // 跨部门协同
            UIFactory.Label(_homeRoot.transform, "Synergy", "跨部门SAF联动：运营部（延误/事故）· 机务部（维修审计/适航）· 人力部（疲劳管理）· 市场部（品牌/媒体）· 战略部（地缘风险）· 财务部（碳配额/安全预算）",
                12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.24f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        private void AddEntry(int idx, string label, string pageId)
        {
            int row = idx / 3;
            int col = idx % 3;
            float x0 = 0.02f + col * 0.32f;
            float y0 = 0.48f - row * 0.15f;
            UIFactory.Button(_homeRoot.transform, "Entry" + idx, label,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x0 + 0.30f, y0 + 0.12f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenSafetyPage(pageId), 12);
        }
    }
}
