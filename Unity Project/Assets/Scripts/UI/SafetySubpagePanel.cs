using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>SAF 子页共享上下文。</summary>
    public static class SafetyContext
    {
        public static int InvestigationSeverity = 1;  // 0征候 1一般 2重大 3特大
        public static int InvestigationDepth = 1;     // 0快速 1标准 2深度
        public static string PendingInspectMailId;
    }

    /// <summary>
    /// SAF 子页：n92 SMS与风险管理 / n93 安全监察与审核 / n94 事件调查与分析 /
    /// n95 局方与法规事务 / n96 安全文化与培训 / n100 检查应对弹窗 / n101 策略切换确认弹窗。
    /// </summary>
    public class SafetySubpagePanel : BasePanel
    {
        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        public static SafetySubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "SafSub_" + pageKind, UIFactory.Fill,
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

            var panel = root.AddComponent<SafetySubpagePanel>();
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

            switch (_pageKind)
            {
                case "n92": RenderSms(); break;
                case "n93": RenderAudit(); break;
                case "n94": RenderInvestigation(); break;
                case "n95": RenderRegulator(); break;
                case "n96": RenderCulture(); break;
                case "n100": RenderInspection(); break;
                case "n101": RenderCultureSwitch(); break;
                default: RenderFallback(); break;
            }
        }

        static string NormalizeKind(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return "";
            if (pageId.StartsWith("n92")) return "n92";
            if (pageId.StartsWith("n93")) return "n93";
            if (pageId.StartsWith("n94")) return "n94";
            if (pageId.StartsWith("n95")) return "n95";
            if (pageId.StartsWith("n96")) return "n96";
            if (pageId.StartsWith("n100")) return "n100";
            if (pageId.StartsWith("n101")) return "n101";
            return pageId;
        }

        void Toast(string msg) { if (_toast != null) _toast.text = msg ?? ""; }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            return UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }

        void RenderFallback()
        {
            L("F", "该子页规格尚未展开。", 0.0f, 0.5f, 1.0f, 0.7f, 16, UITheme.TextMuted);
        }

        // ================= n92 SMS与风险管理 =================
        void RenderSms()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "SMS与风险管理 · 安全管理体系中枢", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Pillars", "四大支柱：安全政策与目标 · 安全风险管理 · 安全保证 · 安全促进（运行状态 绿/绿/黄/绿）",
                0.0f, 0.82f, 1.0f, 0.90f, 12, UITheme.Success, true);

            L("Risk", $"四色风险分布：红 {saf.riskRed} · 橙 {saf.riskOrange} · 黄 {saf.riskYellow} · 蓝 {saf.riskBlue}",
                0.0f, 0.72f, 0.5f, 0.82f, 13);
            L("Rect", $"隐患排查整改率：{saf.rectificationRate:F0}% · 闭环率 91% · 逾期未改 {((saf.riskRed + saf.riskOrange) > 0 ? "红色标记" : "无")}",
                0.0f, 0.62f, 0.5f, 0.72f, 12);
            L("Spi", "安全绩效指标（SPI）：事故率 · 事故征候率 · 严重差错率 · 行业基准线对比 · 红线触发预警",
                0.52f, 0.62f, 1.0f, 0.82f, 12, UITheme.TextMuted);

            B("Mitigate", "制定缓解措施", 0.02f, 0.40f, 0.34f, 0.50f, UITheme.Accent,
                () => { Toast("已发送风险缓解任务至责任部门。"); }, 12);
            B("Rectify", "发起整改", 0.38f, 0.40f, 0.70f, 0.50f, UITheme.PanelDeep,
                () => { Toast("整改通知已发送至收件箱。"); }, 12);
            B("Spi", "SPI红线 → 调查", 0.74f, 0.40f, 0.98f, 0.50f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n94_事件调查与分析"), 12);
        }

        // ================= n93 安全监察与审核 =================
        void RenderAudit()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "安全监察与审核 · 独立客观的监督力量", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"年度审计计划覆盖率 {92}% · 审计通过率 {saf.auditPassRate:F0}% · 不符合项关闭率 {saf.rectificationRate:F0}%",
                0.0f, 0.82f, 1.0f, 0.90f, 13, UITheme.Success, true);
            L("S1", "飞行运行监察：航线监察（驾驶舱观察）· 起降监察 · 机组CRM评估 · SOP遵守率", 0.0f, 0.66f, 1.0f, 0.76f, 12, UITheme.TextMuted);
            L("S2", "机务维修审核：维修记录 · 航材验收 · 工具校验 · 适航指令合规", 0.0f, 0.58f, 1.0f, 0.66f, 12, UITheme.TextMuted);
            L("S3", "客舱安全监察：安全检查单执行率 · 应急设备 · 危险品运输合规", 0.0f, 0.50f, 1.0f, 0.58f, 12, UITheme.TextMuted);
            L("S4", "地面运行审计：站坪车辆 · 除冰/防冰 · 加油安全 · 货物装载", 0.0f, 0.42f, 1.0f, 0.50f, 12, UITheme.TextMuted);

            B("Plan", "发起审计", 0.02f, 0.28f, 0.34f, 0.38f, UITheme.Accent,
                () => { Toast("审计通知已发送至责任部门。"); }, 12);
            B("Track", "整改追踪", 0.38f, 0.28f, 0.70f, 0.38f, UITheme.PanelDeep,
                () => { Toast("不符合项整改追踪已发送。"); }, 12);
            B("Report", "安全举报 → 调查", 0.74f, 0.28f, 0.98f, 0.38f, UITheme.PanelDeep,
                () => SafetyPanelUI.OpenSafetyPage("n94_事件调查与分析"), 12);
        }

        // ================= n94 事件调查与分析 =================
        void RenderInvestigation()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "事件调查与分析 · 原因追查与改进", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            var sb = new System.Text.StringBuilder("调查事件：\n");
            if (saf.investigations.Count == 0) sb.Append("  （暂无调查）");
            foreach (var inv in saf.investigations)
                sb.Append($"  {inv.id} · {SafetyEconomics.SeverityName(inv.severity)} · {SafetyEconomics.DepthName(inv.depth)} · {inv.status}\n");
            L("List", sb.ToString().TrimEnd('\n'), 0.0f, 0.58f, 0.6f, 0.88f, 12);

            L("Depth", "调查深度三档：快速（≤3天）· 标准（≤7天）· 深度（≤14天·多部门+外部专家）",
                0.62f, 0.66f, 1.0f, 0.88f, 12, UITheme.TextMuted);
            L("Sev", $"当前严重度：{SafetyEconomics.SeverityName(SafetyContext.InvestigationSeverity)} · 深度：{SafetyEconomics.DepthName(SafetyContext.InvestigationDepth)}",
                0.62f, 0.56f, 1.0f, 0.66f, 12);

            B("Sev+", "严重度 +1", 0.62f, 0.46f, 0.82f, 0.56f, UITheme.PanelDeep,
                () => { SafetyContext.InvestigationSeverity = Mathf.Min(3, SafetyContext.InvestigationSeverity + 1); RefreshBody(); }, 12);
            B("Depth+", "深度 +1", 0.84f, 0.46f, 1.0f, 0.56f, UITheme.PanelDeep,
                () => { SafetyContext.InvestigationDepth = Mathf.Min(2, SafetyContext.InvestigationDepth + 1); RefreshBody(); }, 12);

            B("Start", "启动调查", 0.02f, 0.30f, 0.34f, 0.42f, UITheme.Accent,
                () =>
                {
                    string msg = "启动失败";
                    if (SafetyManager.Instance != null) SafetyManager.Instance.LaunchInvestigation(SafetyContext.InvestigationSeverity, SafetyContext.InvestigationDepth, out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Resolve", "结案（最近一条）", 0.38f, 0.30f, 0.70f, 0.42f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "结案失败";
                    if (SafetyManager.Instance != null && saf.investigations.Count > 0)
                        SafetyManager.Instance.ResolveInvestigation(saf.investigations[saf.investigations.Count - 1].id, out msg);
                    else msg = "暂无进行中的调查。";
                    Toast(msg); RefreshBody();
                }, 12);
            B("Capa", "CAPA 整改", 0.74f, 0.30f, 0.98f, 0.42f, UITheme.PanelDeep,
                () => { Toast("纠正/预防措施已发送至责任部门。"); }, 12);
        }

        // ================= n95 局方与法规事务 =================
        void RenderRegulator()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "局方与法规事务 · 监管合规与政府关系", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            var sb = new System.Text.StringBuilder("监管机构关系：\n");
            foreach (var r in saf.regulators)
                sb.Append($"  {r.regulator} · {SafetyManager.StatusName(r.status)}\n");
            L("List", sb.ToString().TrimEnd('\n'), 0.0f, 0.58f, 0.5f, 0.88f, 12);

            L("S1", "局方检查日历：定期检查 · 突击检查标记 · 检查类型（运行安全/适航/安保/危险品）", 0.52f, 0.66f, 1.0f, 0.88f, 12, UITheme.TextMuted);
            L("S2", "运行许可：AOC有效期 · ETOPS/RVSM/PBN/ADS-B · 到期提醒 · 续期申请", 0.52f, 0.56f, 1.0f, 0.66f, 12, UITheme.TextMuted);

            B("Improve", "改善 CAAC 关系", 0.02f, 0.30f, 0.34f, 0.42f, UITheme.Accent,
                () =>
                {
                    string msg = "改善失败";
                    if (SafetyManager.Instance != null) SafetyManager.Instance.ImproveRegulator("CAAC", out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Inspect", "检查应对 → 收件箱/弹窗", 0.38f, 0.30f, 0.70f, 0.42f, UITheme.PanelDeep,
                () =>
                {
                    InboxManager.MaybeOfferInspection(gs, true);
                    SafetyPanelUI.OpenSafetyPage("n100_检查应对弹窗");
                }, 12);
            B("Carbon", "碳合规 → 配额管理", 0.74f, 0.30f, 0.98f, 0.42f, UITheme.PanelDeep,
                () => FinancePanelUI.OpenFinancePage("n32_碳配额管理"), 12);
        }

        // ================= n96 安全文化与培训 =================
        void RenderCulture()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "安全文化与培训 · 安全意识的土壤", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Culture", $"当前文化：{SafetyManager.CultureName(saf.cultureType)} · 安全文化指数 {saf.cultureIndex:F0} · 报告率倍率 {SafetyEconomics.CultureReportRate(saf.cultureType):F1}",
                0.0f, 0.80f, 1.0f, 0.90f, 13, UITheme.Success, true);
            L("Compare", "惩罚型=短期合规↑但隐瞒风险↑ · 鼓励报告型=透明度↑真实风险↓",
                0.0f, 0.70f, 1.0f, 0.80f, 12, UITheme.TextMuted);
            L("S1", "安全培训：SMS基础 / 应急响应 / 危险品 / CRM / 人为因素 / 安全文化 · 覆盖率 94%", 0.0f, 0.60f, 1.0f, 0.70f, 12, UITheme.TextMuted);
            L("S2", "安全文化成熟度（SCMM）：病态→反应式→计算式→主动式→生成式 · 当前「计算式」", 0.0f, 0.52f, 1.0f, 0.60f, 12, UITheme.TextMuted);
            L("S3", "自愿报告系统（VRS）：匿名报告率 12/千员工 · 处理时效 · 免罚政策覆盖", 0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextMuted);

            B("Switch", "切换惩罚/鼓励", 0.02f, 0.28f, 0.34f, 0.38f, UITheme.Accent,
                () => SafetyPanelUI.OpenSafetyPage("n101_策略切换确认弹窗"), 12);
            B("Budget", "安全培训预算", 0.38f, 0.28f, 0.70f, 0.38f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "追加失败";
                    if (SafetyManager.Instance != null) SafetyManager.Instance.AllocateSafetyBudget(100f, out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Vrs", "安全奖惩制度", 0.74f, 0.28f, 0.98f, 0.38f, UITheme.PanelDeep,
                () => { Toast("安全奖励/处罚记录 · 奖惩平衡分析已生成。"); }, 12);
        }

        // ================= n100 检查应对弹窗 =================
        void RenderInspection()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            L("H", "检查应对弹窗 · 局方检查决策", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Summary", $"监管机构：CAAC · 检查类型：综合 · 当前评级 {SafetyEconomics.RatingBand(saf.ratingScore)} 级（{saf.ratingScore:F0}）· 预检自查完成率 88%",
                0.0f, 0.80f, 1.0f, 0.90f, 12);

            L("A", "ⓐ 全力配合：成本$ · 1-3天 · 局方关系 +10~15 · 风险=暴露所有问题", 0.0f, 0.66f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("B", "ⓑ 重点整改后迎检：成本$$ · 延迟1-2周 · 局方关系 −5~0 · 发现项减少", 0.0f, 0.56f, 1.0f, 0.66f, 12, UITheme.TextMuted);
            L("C", "ⓒ 申请延期：成本$ · 延迟1-3月 · 局方关系 −10~−20 · 可能触发更严格检查", 0.0f, 0.46f, 1.0f, 0.56f, 12, UITheme.TextMuted);

            B("Full", "确认：全力配合", 0.02f, 0.28f, 0.34f, 0.40f, UITheme.Success,
                () => ApplyInspectChoice(0), 12);
            B("Fix", "确认：重点整改", 0.38f, 0.28f, 0.70f, 0.40f, UITheme.PanelDeep,
                () => ApplyInspectChoice(1), 12);
            B("Delay", "确认：申请延期", 0.74f, 0.28f, 0.98f, 0.40f, UITheme.PanelDeep,
                () => ApplyInspectChoice(2), 12);
        }

        void ApplyInspectChoice(int choice)
        {
            string msg = "应对失败";
            var gs = GameState.Instance;
            if (!string.IsNullOrEmpty(SafetyContext.PendingInspectMailId) && gs?.inbox != null)
            {
                InboxMail mail = null;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].id == SafetyContext.PendingInspectMailId)
                    { mail = gs.inbox[i]; break; }
                if (mail != null && InboxManager.IsOpen(mail))
                {
                    InboxManager.ApplyInspection(gs, mail, choice, out msg);
                    SafetyContext.PendingInspectMailId = null;
                    Toast(msg);
                    RefreshBody();
                    return;
                }
            }
            if (SafetyManager.Instance != null)
                SafetyManager.Instance.RespondInspection("CAAC", choice, out msg);
            Toast(msg);
            RefreshBody();
        }

        // ================= n101 策略切换确认弹窗 =================
        void RenderCultureSwitch()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var saf = SafetyManager.EnsureState(gs);
            int target = saf.cultureType == 0 ? 1 : 0;
            L("H", "策略切换确认弹窗 · 安全文化", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("From", $"当前文化：{SafetyManager.CultureName(saf.cultureType)} → 目标文化：{SafetyManager.CultureName(target)}",
                0.0f, 0.78f, 1.0f, 0.90f, 13, UITheme.Warning, true);
            L("Impact", $"影响预估：短期报告量 ×{SafetyEconomics.CultureReportRate(target):F1} · 隐藏风险 ×{SafetyEconomics.CultureHiddenRisk(target):F1} · 品牌/监管观感变化",
                0.0f, 0.64f, 1.0f, 0.76f, 12, UITheme.TextMuted);

            B("Confirm", "确认切换", 0.02f, 0.34f, 0.34f, 0.46f, UITheme.Success,
                () =>
                {
                    string msg = "切换失败";
                    if (SafetyManager.Instance != null) SafetyManager.Instance.SetCultureType(target, out msg);
                    Toast(msg);
                }, 13);
            B("Cancel", "取消", 0.38f, 0.34f, 0.70f, 0.46f, UITheme.PanelDeep,
                () => BackOne(), 13);
        }
    }
}
