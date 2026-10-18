using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>FIN 子页共享上下文（跨页面传参）。</summary>
    public static class FinanceContext
    {
        public static int FinancingKind = -1;   // n103 确认的融资方式
        public static string PendingFinanceMailId;
    }

    /// <summary>
    /// FIN 子页：n22 年度预算 / n31 对冲工作台 / n32 碳配额 / n25 紧急融资 / n103 条款确认 / n106 重组。
    /// 运行时构建（无 Prefab），导航栈驱动。
    /// </summary>
    public class FinanceSubpagePanel : BasePanel
    {
        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        public static FinanceSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "FinSub_" + pageKind, UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(card.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<FinanceSubpagePanel>();
            panel.pageKind = pageKind;
            panel._pageKind = pageKind;
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageKind, title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
            _pageKind = NormalizeKind(pageId);
            pageKind = _pageKind;
            if (titleLabel != null && !string.IsNullOrEmpty(title))
                titleLabel.text = title;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            WireBackButton();
        }

        void OnEnable()
        {
            ResolveRefs();
            WireBackButton();
            RefreshBody();
        }

        public override void Show()
        {
            base.Show();
            ResolveRefs();
            RefreshBody();
        }

        void ResolveRefs()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(!string.IsNullOrEmpty(pageKind) ? pageKind : PageId);
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);
            var gs = GameState.Instance;
            InboxManager.MaybeOfferFuelHedge(gs);
            InboxManager.MaybeOfferAnnualBudget(gs);
            InboxManager.MaybeOfferCarbonBuy(gs);
            InboxManager.MaybeOfferFinanceCrisis(gs);

            switch (_pageKind)
            {
                case "n22": RenderBudget(); break;
                case "n31": RenderHedge(); break;
                case "n32": RenderCarbon(); break;
                case "n25": RenderFinancing(); break;
                case "n103": RenderTerms(); break;
                case "n106": RenderRestructuring(); break;
                default: RenderFallback(); break;
            }
            HideCloseXButtons(transform);
        }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var l = UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
            return l;
        }

        // —— n22 年度预算 ——
        void RenderBudget()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            FinanceManager.Instance?.EnsureDefaultBudget(gs);

            L("H", "各部门预算（万美元）", 0.0f, 0.90f, 1.0f, 0.97f, 14, UITheme.Accent, true);
            L("Hint", "草案由财务总监发函，您在收件箱批复后提交董事会。本页只列示额度。", 0.0f, 0.845f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            float y = 0.74f;
            float step = 0.082f;
            if (gs.budgetItems != null)
            {
                for (int i = 0; i < gs.budgetItems.Count; i++)
                {
                    var item = gs.budgetItems[i];
                    L("D" + i, item.deptName, 0.0f, y, 0.55f, y + 0.06f, 14);
                    L("A" + i, item.amountWan.ToString("F0") + " 万", 0.56f, y, 1.0f, y + 0.06f, 14, UITheme.Success, true);
                    y -= step;
                }
            }

            float total = FinanceManager.BudgetTotal(gs);
            float low = FinanceEconomics.BudgetBandLow(gs.cash);
            float high = FinanceEconomics.BudgetBandHigh(gs.cash);
            int votes = FinanceManager.EstimateVotes(gs);
            L("Tot", $"合计 {total:F0} 万 · 建议区间 {low:F0}~{high:F0} 万", 0.0f, 0.10f, 1.0f, 0.17f, 14, UITheme.Warning, true);
            L("Votes", $"董事会投票预估 {votes}/{FinanceEconomics.BoardSeats} 票 · {(gs.budgetApproved ? "已批准" : gs.budgetSubmitted ? "已提交董事会" : "请示已发收件箱")}",
                0.0f, 0.04f, 1.0f, 0.10f, 12, UITheme.TextMuted);
        }

        // —— n31 对冲工作台 ——

        /// <summary>现货油价下方那行：现实报价基准与本季收敛预告（未接行情时说明状态，不留空行）。</summary>
        static string FuelQuoteLine(GameState gs)
        {
            string line = ExternalDataFetcher.QuoteStatusLine(compact: true);
            string forecast = MacroEconomyManager.QuoteConvergeForecast(gs);
            if (!string.IsNullOrEmpty(forecast)) line += $"　·　本季收敛后 {forecast}";
            return line;
        }

        void RenderHedge()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            L("H", "燃油对冲", 0.0f, 0.90f, 0.48f, 0.97f, 15, UITheme.Accent, true);
            L("FuelSpot", $"现货油价 ${gs.fuelPricePerTon:F0}/吨", 0.0f, 0.815f, 0.48f, 0.885f, 13);
            L("FuelQuote", FuelQuoteLine(gs), 0.0f, 0.735f, 0.48f, 0.815f, 11, UITheme.TextMuted);
            L("Hint", "开仓、改仓均由财务总监发函，您在收件箱批复。", 0.0f, 0.665f, 0.48f, 0.735f, 11, UITheme.TextMuted);

            L("H2", "汇率敞口", 0.52f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("FxSel", $"现行对冲比例 {gs.fxHedgeRatio * 100:F0}%", 0.52f, 0.80f, 1.0f, 0.88f, 13);
            L("FxLive", AirlineCEO.Logic.FxEconomics.Summary(gs), 0.52f, 0.66f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("Contract", ContractSummary(gs), 0.0f, 0.06f, 1.0f, 0.64f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
        }

        static string ContractSummary(GameState gs)
        {
            int n = gs.hedgeContracts?.Count ?? 0;
            return n == 0 ? "燃油合约：无" : $"燃油合约：{n} 份（见财务部对冲页）";
        }

        // —— n32 碳配额 ——
        void RenderCarbon()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            FinanceManager.Instance?.RefreshEmissions(gs);

            float liability = FinanceManager.CarbonLiability(gs);
            L("H", "碳配额管理", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("E", $"累计排放 {gs.carbonEmissionsTons:F0} 吨 CO2", 0.0f, 0.80f, 0.55f, 0.87f, 14);
            L("Q", $"已购配额 {gs.carbonPurchasedQuotaTons:F0} 吨", 0.0f, 0.71f, 0.55f, 0.78f, 14);
            L("P", $"碳价参考 ${gs.carbonPricePerTon:F0}/吨", 0.0f, 0.62f, 0.55f, 0.69f, 14);
            L("Liab", $"碳负债 {liability:F1} 万", 0.0f, 0.53f, 0.55f, 0.60f, 14, liability > 0.01f ? UITheme.Danger : UITheme.Success, true);
            L("Mail", "采购请示由财务总监发到收件箱，本页不直接下单。", 0.0f, 0.40f, 0.55f, 0.50f, 12, UITheme.TextMuted);

            L("RouteHint", "航线碳成本（EU ETS / CORSIA）", 0.58f, 0.80f, 1.0f, 0.87f, 13, UITheme.TextMuted, true);
            float y = 0.74f;
            var routes = gs.activeRoutes;
            int shown = 0;
            if (routes != null)
            {
                for (int i = 0; i < routes.Count && shown < 6; i++)
                {
                    var r = routes[i];
                    if (r == null) continue;
                    float cost = FinanceEconomics.CarbonCostPerRouteYearWan(
                        r.distanceKm, r.aircraftType, r.weeklyFlights, gs.carbonPricePerTon);
                    L("RC" + shown, $"{r.origin}-{r.destination}  {cost:F2} 万/年", 0.58f, y, 1.0f, y + 0.055f, 12, UITheme.TextMuted);
                    y -= 0.062f;
                    shown++;
                }
            }
            if (shown == 0)
                L("RC0", "暂无航线", 0.58f, 0.74f, 1.0f, 0.80f, 12, UITheme.TextMuted);
        }

        // —— n25 紧急融资 ——
        void RenderFinancing()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var credit = LoanManager.GetCreditRating(gs);
            float wacc = FinanceEconomics.Wacc(credit, gs.economicPhase);

            L("H", "紧急融资 / 破产保护", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Warning, true);
            L("Status", $"{CashRunnelCalculator.FormatPlayerLabel(runway)} · 信用 {LoanManager.CreditLabel(credit)} · WACC {wacc:F1}%",
                0.0f, 0.82f, 1.0f, 0.89f, 13, UITheme.TextMuted);
            L("Hint", "银行贷款、过桥、增资或重组均由财务总监发函，请在收件箱批复。本页只列示流动性。",
                0.0f, 0.68f, 1.0f, 0.80f, 13, UITheme.TextMuted);
            L("Ch11", "跑道过短时将另行发函请示 Chapter 11 重组。", 0.0f, 0.56f, 1.0f, 0.66f, 12, UITheme.TextMuted);
        }

        // —— n103 条款说明 ——
        void RenderTerms()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            int kind = FinanceContext.FinancingKind;
            if (kind < 0) kind = (int)FinancingKind.Bank;

            var input = new FinanceEconomics.FinancingInput
            {
                cash = gs.cash,
                monthlyBurn = gs.EffectiveMonthlyBurn,
                rating = LoanManager.GetCreditRating(gs),
                economicPhase = gs.economicPhase,
                boardSatisfaction = gs.boardSatisfaction,
                fleetCount = gs.fleet?.Count ?? 0
            };
            var term = FinanceEconomics.BuildFinancingTerm((FinancingKind)kind, input);
            FinanceContext.FinancingKind = kind;

            L("H", term.label + " · 条款摘要", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("A", $"金额 {term.amountWan:F0} 万", 0.0f, 0.80f, 0.48f, 0.87f, 14);
            L("R", term.annualRate > 0.01f ? $"年利率 {term.annualRate:F1}%" : "无利息", 0.0f, 0.71f, 0.48f, 0.78f, 14);
            L("T", term.termMonths > 0 ? $"期限 {term.termMonths} 月" : "—", 0.0f, 0.62f, 0.48f, 0.69f, 14);
            L("Rep", $"还款方式 {term.repayment}", 0.0f, 0.53f, 0.48f, 0.60f, 14);
            if (term.dilutionPercent > 0.01f)
                L("Dil", $"股权稀释 {term.dilutionPercent:F0}%", 0.0f, 0.44f, 0.48f, 0.51f, 14, UITheme.Warning);

            L("C", $"抵押/标的：{term.collateral}", 0.52f, 0.80f, 1.0f, 0.87f, 13, UITheme.TextMuted);
            L("Cond", $"条件：{term.condition}", 0.52f, 0.71f, 1.0f, 0.78f, 13, UITheme.TextMuted);
            L("Risk", $"风险提示：{term.riskNote}", 0.52f, 0.62f, 1.0f, 0.69f, 13, UITheme.Danger);
            L("Irrev", "签署须在收件箱批复紧急融资函，本页不可生效。", 0.0f, 0.10f, 1.0f, 0.20f, 13, UITheme.TextMuted, false, TextAlignmentOptions.Center);
        }

        // —— n106 重组条件说明 ——
        void RenderRestructuring()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var impact = FinanceEconomics.EstimateRestructuring(
                gs.fleet?.Count ?? 0, gs.activeRoutes?.Count ?? 0, gs.cash, gs.EffectiveMonthlyBurn);
            float runwayAfter = FinanceEconomics.RunwayAfterRestructuring(gs.cash, impact.cashRecoveredWan, gs.EffectiveMonthlyBurn);

            L("H", "🏛 破产保护 · 重组条件", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Danger, true);

            string[] rows =
            {
                $"📉 停飞并出售 ≥50% 亏损航线（削减 {impact.routesCutPercent * 100:F0}%）",
                $"🏭 出售非核心资产 → 回笼约 {impact.cashRecoveredWan:F0} 万",
                $"✈️ 退租非必需租赁机（机队 −{impact.fleetCutPercent * 100:F0}%）",
                $"👥 裁员 ≥15% · 高管薪酬削减 20–50%",
                $"🏛 接受债权人委员会监督 · 季度重组进展报告",
                $"💰 强制现金留存 · 新资本开支冻结 · 优先清偿顺序确认"
            };
            float y = 0.80f;
            for (int i = 0; i < rows.Length; i++)
            {
                L("R" + i, rows[i], 0.0f, y, 1.0f, y + 0.055f, 13, UITheme.TextPrimary);
                y -= 0.075f;
            }

            L("Brand", $"品牌净值影响 −{impact.brandLoss:F0} · 重组后跑道约 {runwayAfter:F1} 月", 0.0f, y - 0.01f, 1.0f, y + 0.04f, 13, UITheme.Warning, true);
            L("Mail", "是否进入重组，请在收件箱批复财务总监来函。", 0.0f, 0.08f, 1.0f, 0.16f, 13, UITheme.TextMuted);
        }

        void RenderFallback()
        {
            L("H", "FIN 子页", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted);
            L("B", "未知页面：" + _pageKind, 0.0f, 0.80f, 1.0f, 0.88f, 14, UITheme.TextMuted);
        }

        static string NormalizeKind(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("n22")) return "n22";
            if (id.StartsWith("n31")) return "n31";
            if (id.StartsWith("n32")) return "n32";
            if (id.StartsWith("n25")) return "n25";
            if (id.StartsWith("n103")) return "n103";
            if (id.StartsWith("n106")) return "n106";
            if (id.Contains("预算")) return "n22";
            if (id.Contains("对冲")) return "n31";
            if (id.Contains("碳")) return "n32";
            if (id.Contains("破产") || id.Contains("融资")) return "n25";
            if (id.Contains("条款")) return "n103";
            if (id.Contains("重组")) return "n106";
            return id;
        }
    }
}
