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
    /// 底栏「维护」：适航总览（机务）+ 安全仪表盘（原安全部并入）。
    /// </summary>
    public class MaintenancePanelUI : MonoBehaviour
    {
        GameObject _airRoot;
        GameObject _safRoot;
        TextMeshProUGUI _kpiText;
        TextMeshProUGUI _strategyText;
        TextMeshProUGUI _oemText;
        TextMeshProUGUI _safKpi;
        TextMeshProUGUI _safRating;
        TextMeshProUGUI _safCulture;
        readonly Button[] _tabs = new Button[2];
        int _tab;

        // 适航页右侧入口网格：AddAirEntry 与「维修培训」入口共用同一套坐标，
        // 避免手写锚点与网格重合（历史上两者矩形完全相同，把「适航质量」整个盖住了）。
        const float GridX0 = 0.35f, GridColStep = 0.215f, GridW = 0.20f;
        const float GridY0 = 0.48f, GridRowStep = 0.16f, GridH = 0.13f;

        public static MaintenancePanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Maintenance", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<MaintenancePanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public static void OpenMaintenancePage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) { nav.OpenSubPage(pageId); return; }
            var router = NavigationRouter.Instance;
            if (router != null) router.Navigate(pageId, replaceCurrent: false);
            else Debug.LogWarning("[MNT] NavigationRouter 未就绪：" + pageId);
        }

        public void OnOpened()
        {
            SetTab(0);
        }

        public void ShowSafetyTab() => SetTab(1);

        public void Refresh()
        {
            RefreshAir();
            RefreshSaf();
        }

        void SetTab(int idx)
        {
            _tab = Mathf.Clamp(idx, 0, 1);
            if (_airRoot != null) _airRoot.SetActive(_tab == 0);
            if (_safRoot != null) _safRoot.SetActive(_tab == 1);
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                var img = _tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _tab ? UITheme.NavActive : UITheme.NavIdle;
            }
            Refresh();
        }

        void RefreshAir()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            if (_kpiText != null)
                _kpiText.text = $"维修策略目标可用率：{mnt.fleetAvailability:F1}%    维修差错率：{mnt.errorRate:F1}%\n停飞飞机：{mnt.groundedCount} 架    持证工程师：{mnt.engineerCount} 人    在培：{mnt.trainingCount} 人";
            if (_strategyText != null)
            {
                string s = MaintenanceManager.StrategyName(mnt.strategy);
                float exec = mnt.budgetWan > 0 ? mnt.budgetUsedWan / mnt.budgetWan * 100f : 0f;
                _strategyText.text = $"维修预算策略：{s} · 预算执行率 {exec:F0}%";
            }
            if (_oemText != null)
            {
                var sb = new System.Text.StringBuilder("OEM 关系：");
                if (mnt.oems != null)
                    foreach (var o in mnt.oems)
                        sb.Append($"{o.oem} {o.stars}★（{MaintenanceManager.OemTier(o.stars)}）  ");
                _oemText.text = sb.ToString();
            }
        }

        void RefreshSaf()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            char band = SafetyEconomics.RatingBand(saf.ratingScore);
            if (_safKpi != null)
                _safKpi.text = $"零重大事故天数：{saf.zeroAccidentDays} 天    审计通过率：{saf.auditPassRate:F0}%\n隐患整改率：{saf.rectificationRate:F0}%    安全预算使用率：{(saf.safetyBudgetWan > 0 ? saf.safetyBudgetUsedWan / saf.safetyBudgetWan * 100f : 0f):F0}%";
            if (_safRating != null)
            {
                bool takeover = SafetyEconomics.RegulatorTakeoverRisk(saf.ratingScore);
                _safRating.text = $"安全评级：{band} 级（{saf.ratingScore:F0}/100）· {(takeover ? "D级触发局方接管风险" : "评级影响航权与董事会考核")}";
                _safRating.color = band == 'A' ? UITheme.Success : band == 'B' ? UITheme.Accent : band == 'C' ? UITheme.Warning : UITheme.Danger;
            }
            if (_safCulture != null)
                _safCulture.text = $"安全文化：{SafetyManager.CultureName(saf.cultureType)} · 文化指数 {saf.cultureIndex:F0} · 隐藏风险倍率 {SafetyEconomics.CultureHiddenRisk(saf.cultureType):F1}";
        }

        void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "维护", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.14f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var bar = UIFactory.Panel(panel, "MntTabs",
                new UIFactory.Anchor(new Vector2(0.15f, 0.935f), new Vector2(0.62f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;
            string[] labels = { "适航总览", "安全仪表盘" };
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                float x0 = i / 2f;
                _tabs[i] = UIFactory.Button(bar.transform, "T" + i, labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.02f, 0.12f), new Vector2(x0 + 0.48f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => SetTab(idx), 13);
            }

            BuildAirworthiness(panel);
            BuildSafetyDash(panel);
            SetTab(0);
        }

        void BuildAirworthiness(Transform panel)
        {
            _airRoot = UIFactory.Panel(panel, "MntHome",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _airRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            _kpiText = UIFactory.Label(_airRoot.transform, "Kpi", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.80f), new Vector2(0.50f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _kpiText.textWrappingMode = TextWrappingModes.Normal;

            _strategyText = UIFactory.Label(_airRoot.transform, "Strategy", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.52f, 0.88f), new Vector2(0.99f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _oemText = UIFactory.Label(_airRoot.transform, "Oem", "", 12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.52f, 0.80f), new Vector2(0.99f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(_airRoot.transform, "StrategyHdr", "维修预算策略", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.70f), new Vector2(0.30f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            UIFactory.Button(_airRoot.transform, "Strat0", "全面预防（高成本/高可用）",
                new UIFactory.Anchor(new Vector2(0.02f, 0.56f), new Vector2(0.32f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => SetStrategy(0), 11);
            UIFactory.Button(_airRoot.transform, "Strat1", "最低合规（低成本/中风险）",
                new UIFactory.Anchor(new Vector2(0.02f, 0.44f), new Vector2(0.32f, 0.56f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => SetStrategy(1), 11);
            UIFactory.Button(_airRoot.transform, "Strat2", "智能预测（AI 优化）",
                new UIFactory.Anchor(new Vector2(0.02f, 0.32f), new Vector2(0.32f, 0.44f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => SetStrategy(2), 11);

            UIFactory.Label(_airRoot.transform, "EntryHdr", "适航与工程", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.35f, 0.70f), new Vector2(0.60f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            AddAirEntry(0, "航线与定检", "n81_航线与定检维修");
            AddAirEntry(1, "工程技术", "n82_工程技术与计划");
            AddAirEntry(2, "航材与供应链", "n83_航材与供应链");
            AddAirEntry(3, "适航质量", "n84_安全质量与适航");
            // 「维修培训 → 培训」排在第 2 行第 2 列；第 2 行第 1 列留给 AddAirEntry(3)「适航质量」。
            float tx0 = GridX0 + GridColStep;
            float ty0 = GridY0 - GridRowStep;
            UIFactory.Button(_airRoot.transform, "ToTrain", "维修培训 → 培训",
                new UIFactory.Anchor(new Vector2(tx0, ty0), new Vector2(tx0 + GridW, ty0 + GridH), Vector2.zero, Vector2.zero),
                UITheme.Accent, () =>
                {
                    var nav = FindFirstObjectByType<PrefabNavIntegration>();
                    if (nav != null) nav.SelectTab("n87_培训与发展");
                    else OpenMaintenancePage("n85_维修培训与资质");
                }, 11);

            UIFactory.Label(_airRoot.transform, "Synergy", "适航看板：健康度 / 可用率 / 在修。维修培训已并入底栏「培训」。",
                12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.24f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        void BuildSafetyDash(Transform panel)
        {
            _safRoot = UIFactory.Panel(panel, "SafDash",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _safRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            _safKpi = UIFactory.Label(_safRoot.transform, "Kpi", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.80f), new Vector2(0.52f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _safKpi.textWrappingMode = TextWrappingModes.Normal;

            _safRating = UIFactory.Label(_safRoot.transform, "Rating", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.54f, 0.86f), new Vector2(0.99f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _safCulture = UIFactory.Label(_safRoot.transform, "Culture", "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.54f, 0.74f), new Vector2(0.99f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(_safRoot.transform, "EntryHdr", "安全详情（邮件/决策入口）", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.64f), new Vector2(0.50f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            AddSafEntry(0, "SMS与风险管理", "n92_SMS与风险管理");
            AddSafEntry(1, "安全监察与审核", "n93_安全监察与审核");
            AddSafEntry(2, "事件调查与分析", "n94_事件调查与分析");
            AddSafEntry(3, "局方与法规事务", "n95_局方与法规事务");
            AddSafEntry(4, "检查应对", "n100_检查应对弹窗");
            UIFactory.Button(_safRoot.transform, "ToCult", "安全文化 → 培训",
                new UIFactory.Anchor(new Vector2(0.66f, 0.28f), new Vector2(0.96f, 0.40f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () =>
                {
                    var nav = FindFirstObjectByType<PrefabNavIntegration>();
                    if (nav != null) nav.SelectTab("n87_培训与发展");
                    else SafetyPanelUI.OpenSafetyPage("n96_安全文化与培训");
                }, 12);

            UIFactory.Label(_safRoot.transform, "Hint", "安全已并入维护看板。文化培训在底栏「培训」；局方检查走决策弹窗。",
                12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.22f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        void SetStrategy(int s)
        {
            if (MaintenanceManager.Instance != null)
                MaintenanceManager.Instance.SetStrategy(s, out _);
            Refresh();
        }

        void AddAirEntry(int idx, string label, string pageId)
        {
            float x0 = GridX0 + (idx % 3) * GridColStep;
            float y0 = GridY0 - (idx / 3) * GridRowStep;
            UIFactory.Button(_airRoot.transform, "Air" + idx, label,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x0 + GridW, y0 + GridH), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMaintenancePage(pageId), 11);
        }

        void AddSafEntry(int idx, string label, string pageId)
        {
            int row = idx / 3;
            int col = idx % 3;
            float x0 = 0.02f + col * 0.32f;
            float y0 = 0.48f - row * 0.16f;
            UIFactory.Button(_safRoot.transform, "Saf" + idx, label,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x0 + 0.30f, y0 + 0.12f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => SafetyPanelUI.OpenSafetyPage(pageId), 12);
        }
    }
}
