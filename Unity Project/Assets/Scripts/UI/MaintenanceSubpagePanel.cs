using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>MNT 子页共享上下文。</summary>
    public static class MaintenanceContext
    {
        public static string ConfirmOrderNo = "";
    }

    /// <summary>
    /// MNT 子页：n81 航线与定检维修 / n82 工程技术与计划 / n83 航材与供应链 /
    /// n84 安全质量与适航 / n85 维修培训与资质 / n105 订单确认。
    /// </summary>
    public class MaintenanceSubpagePanel : BasePanel
    {
        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        public static MaintenanceSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "MntSub_" + pageKind, UIFactory.Fill,
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

            var panel = root.AddComponent<MaintenanceSubpagePanel>();
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
                case "n81": RenderChecks(); break;
                case "n82": RenderEngineering(); break;
                case "n83": RenderParts(); break;
                case "n84": RenderSafetyQuality(); break;
                case "n85": RenderTraining(); break;
                case "n105": RenderOrderConfirm(); break;
                default: RenderFallback(); break;
            }
        }

        static string NormalizeKind(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return "";
            if (pageId.StartsWith("n81")) return "n81";
            if (pageId.StartsWith("n82")) return "n82";
            if (pageId.StartsWith("n83")) return "n83";
            if (pageId.StartsWith("n84")) return "n84";
            if (pageId.StartsWith("n85")) return "n85";
            if (pageId.StartsWith("n105")) return "n105";
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

        // ================= n81 航线与定检维修 =================
        void RenderChecks()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            L("H", "航线与定检维修 · 一线即时诊疗与深度体检", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"机队可用率 {mnt.fleetAvailability:F1}% · 停飞 {mnt.groundedCount} 架", 0.0f, 0.82f, 0.6f, 0.90f, 13, UITheme.Success, true);
            L("Levels", "定检等级：A检（~2月）· B检（~7月）· C检（~21月·停场1-2周）· D检（~7年·停场1-2月）", 0.0f, 0.72f, 1.0f, 0.80f, 12, UITheme.TextMuted);

            var sb = new StringBuilder("定检排程（未来 6 个月）：\n");
            if (mnt.checks.Count == 0) sb.Append("  （暂无）");
            foreach (var c in mnt.checks) sb.Append($"  {c.registration} · {c.checkType}检 · 第{c.dueMonth}月 · {c.costWan:F0}万\n");
            L("Checks", sb.ToString().TrimEnd('\n'), 0.0f, 0.40f, 0.6f, 0.70f, 12);

            B("C", "排 C 检", 0.62f, 0.54f, 0.98f, 0.64f, UITheme.Accent,
                () => { DoCheck("C", 3); }, 12);
            B("A", "排 A 检", 0.62f, 0.42f, 0.98f, 0.52f, UITheme.PanelDeep,
                () => { DoCheck("A", 1); }, 12);
            B("D", "排 D 检", 0.62f, 0.30f, 0.98f, 0.40f, UITheme.PanelDeep,
                () => { DoCheck("D", 12); }, 12);

            L("Hint", "淡季（Q1 末 / Q3 末）是执行 C/D 检的黄金窗口，停场机会成本最低。", 0.0f, 0.24f, 1.0f, 0.32f, 11, UITheme.TextMuted);
            B("MEL", "MEL 放行审批", 0.02f, 0.12f, 0.30f, 0.21f, UITheme.PanelDeep,
                () => { Toast("MEL 技术放行决策已触发。"); }, 12);
        }

        void DoCheck(string type, int month)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            string reg = "B-1001";
            if (gs.fleet != null && gs.fleet.Count > 0 && !string.IsNullOrEmpty(gs.fleet[0].registration))
                reg = gs.fleet[0].registration;
            string msg = "排检失败";
            if (MaintenanceManager.Instance != null)
                MaintenanceManager.Instance.ScheduleCheck(reg, type, month, out msg);
            Toast(msg);
            RefreshBody();
        }

        // ================= n82 工程技术与计划 =================
        void RenderEngineering()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            L("H", "工程技术与计划控制 · 后台专家会诊", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Hint", "维修方案（MPD/MSG-3）· 技术支援工作台 · 12个月维修计划 · 二手飞机评估 · 退租检 · 发动机管理", 0.0f, 0.82f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            var sb = new StringBuilder("OEM 供应商深度关系：\n");
            foreach (var o in mnt.oems)
                sb.Append($"  {o.oem} · {o.stars}★（{MaintenanceManager.OemTier(o.stars)}）· 折扣 {o.discountRate * 100:F0}% · 交付优先级 {o.deliveryPriority}\n");
            L("Oems", sb.ToString().TrimEnd('\n'), 0.0f, 0.52f, 0.6f, 0.78f, 12);

            B("OEM0", "升级波音关系", 0.62f, 0.60f, 0.98f, 0.70f, UITheme.Accent,
                () =>
                {
                    string msg = "升级失败";
                    if (MaintenanceManager.Instance != null) MaintenanceManager.Instance.UpgradeOem("波音", out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("OEM1", "升级空客关系", 0.62f, 0.48f, 0.98f, 0.58f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "升级失败";
                    if (MaintenanceManager.Instance != null) MaintenanceManager.Instance.UpgradeOem("空客", out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Used", "二手飞机 → 发起购买", 0.02f, 0.30f, 0.32f, 0.40f, UITheme.PanelDeep,
                () => { Toast("二手飞机采购已提交 CEO 决策会议。"); }, 12);
            B("Redelivery", "退租检 → 财务", 0.36f, 0.30f, 0.68f, 0.40f, UITheme.PanelDeep,
                () => FinancePanelUI.OpenFinancePage("n22_年度预算编制_审批"), 12);
        }

        // ================= n83 航材与供应链 =================
        void RenderParts()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            int qty = 0, safety = 0, shortage = 0;
            foreach (var p in mnt.parts) { qty += p.quantity; safety += p.safetyStock; if (p.quantity < p.safetyStock) shortage++; }
            float health = MaintenanceEconomics.StockHealth(qty, safety, shortage);
            L("H", "航材与供应链 · 零部件全生命周期管理", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"库存健康度 {health:F0} · 缺料预警 {shortage} 项 · ABC 分类管理", 0.0f, 0.82f, 0.7f, 0.90f, 13,
                health >= 70 ? UITheme.Success : health >= 40 ? UITheme.Warning : UITheme.Danger, true);

            var sb = new StringBuilder("实时库存：\n");
            if (mnt.parts.Count == 0) sb.Append("  （暂无库存）");
            foreach (var p in mnt.parts) sb.Append($"  {p.partNo} {p.name} · {p.category}类 · 在库 {p.quantity} · 安全线 {p.safetyStock}\n");
            L("Parts", sb.ToString().TrimEnd('\n'), 0.0f, 0.40f, 0.5f, 0.78f, 12);

            var ob = new StringBuilder("采购订单：\n");
            if (mnt.orders.Count == 0) ob.Append("  （暂无）");
            foreach (var o in mnt.orders) ob.Append($"  {o.orderNo} · {o.partNo} ×{o.quantity} · {o.status}\n");
            L("Orders", ob.ToString().TrimEnd('\n'), 0.52f, 0.40f, 1.0f, 0.78f, 12);

            B("Create", "创建采购订单", 0.02f, 0.28f, 0.32f, 0.38f, UITheme.Accent,
                () =>
                {
                    string msg = "创建失败";
                    if (MaintenanceManager.Instance != null
                        && MaintenanceManager.Instance.CreateOrder("PN-ENG-001", "OEM 原厂", 5, 120000f, false, out msg))
                    {
                        var last = mnt.orders.Count > 0 ? mnt.orders[mnt.orders.Count - 1] : null;
                        if (last != null) InboxManager.QueuePartsMail(gs, last);
                        msg += " 已送收件箱请 CEO 批准。";
                    }
                    Toast(msg); RefreshBody();
                }, 12);
            B("Urgent", "停飞紧急采购", 0.36f, 0.28f, 0.68f, 0.38f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "创建失败";
                    if (MaintenanceManager.Instance != null
                        && MaintenanceManager.Instance.CreateOrder("PN-HYD-002", "OEM 原厂", 2, 80000f, true, out msg))
                    {
                        var last = mnt.orders.Count > 0 ? mnt.orders[mnt.orders.Count - 1] : null;
                        if (last != null) InboxManager.QueuePartsMail(gs, last);
                        msg += " 紧急单已送收件箱，批准后本季入库。";
                    }
                    Toast(msg); RefreshBody();
                }, 12);
            B("Shared", "共享池借用", 0.72f, 0.28f, 0.98f, 0.38f, UITheme.PanelDeep,
                () => { Toast("共享池借用申请已提交。"); }, 12);
        }

        // ================= n84 安全质量与适航 =================
        void RenderSafetyQuality()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            L("H", "安全质量与适航 · 维修质量守护者", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"维修差错率 {mnt.errorRate:F1}%（目标 0）· 适航状态：{(gs.safetyRating == SafetyRating.A || gs.safetyRating == SafetyRating.B ? "绿色·零重大事件" : "黄色·需关注")}",
                0.0f, 0.80f, 1.0f, 0.90f, 13, UITheme.Success, true);
            L("Items", "适航指令（AD）合规率 100% · 服务通告（SB）执行率 92% · 内部审计发现项 3 · 局方检查结果 B", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("Sms", "SMS 安全管理体系：危险源识别 / 安全绩效指标（SPI）/ 自愿报告 / 安全文化评估", 0.0f, 0.58f, 1.0f, 0.66f, 12, UITheme.TextMuted);
            L("Camp", "持续适航管理方案（CAMP）：维修方案符合性 / 可靠性方案 / 腐蚀预防（CPCP）", 0.0f, 0.48f, 1.0f, 0.56f, 12, UITheme.TextMuted);

            B("Audit", "内部审计整改", 0.02f, 0.32f, 0.30f, 0.42f, UITheme.Accent,
                () => { Toast("审计整改通知已发送。"); }, 12);
            B("Caac", "局方检查预检自查", 0.34f, 0.32f, 0.66f, 0.42f, UITheme.PanelDeep,
                () =>
                {
                    InboxManager.MaybeOfferInspection(gs, true);
                    Toast("局方检查已进入收件箱，请选择应对方案。");
                }, 12);
            B("Event", "安全事件调查", 0.70f, 0.32f, 0.98f, 0.42f, UITheme.PanelDeep,
                () => { Toast("重大安全事件调查已发起。"); }, 12);
        }

        // ================= n85 维修培训与资质 =================
        void RenderTraining()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            L("H", "维修培训与资质 · 机务人才培养", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"持证工程师 {mnt.engineerCount} 人 · 培训中 {mnt.trainingCount} 人 · 资质到期预警 2", 0.0f, 0.80f, 1.0f, 0.90f, 13, UITheme.Success, true);
            L("Methods", "机型改装培训方式：自建培训中心 / 外包 / OEM 培训 / 压缩时长 · 每批人数 · 成本/人 · 周期", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("Courses", "课程体系：基础培训 · 机型培训（理论+实操）· 专项技能（发动机试车/NDT/复合材料/高空）· 安全管理", 0.0f, 0.58f, 1.0f, 0.66f, 12, UITheme.TextMuted);
            L("Path", "职业路径：机械师 → 工程师 → 放行工程师 → 技术经理 → 部门总监", 0.0f, 0.48f, 1.0f, 0.56f, 12, UITheme.TextMuted);

            B("Train", "发起培训计划", 0.02f, 0.32f, 0.32f, 0.42f, UITheme.Accent,
                () =>
                {
                    string msg = "启动失败";
                    if (MaintenanceManager.Instance != null)
                        MaintenanceManager.Instance.StartTypeRatingBatch(12, 80f, out msg);
                    Toast(msg);
                    RefreshBody();
                }, 12);
            B("Center", "自建培训中心投资", 0.36f, 0.32f, 0.68f, 0.42f, UITheme.PanelDeep,
                () => { Toast("培训中心建设已提交 CEO 决策会议。"); }, 12);
            B("Hire", "招聘联动 → 人力", 0.72f, 0.32f, 0.98f, 0.42f, UITheme.PanelDeep,
                () => { Toast("机务招聘需求已同步人力部。"); }, 12);
        }

        // ================= n105 订单确认 =================
        void RenderOrderConfirm()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            string orderNo = string.IsNullOrEmpty(MaintenanceContext.ConfirmOrderNo)
                ? (mnt.orders.Count > 0 ? mnt.orders[mnt.orders.Count - 1].orderNo : "")
                : MaintenanceContext.ConfirmOrderNo;
            var order = mnt.orders.Find(o => o.orderNo == orderNo);

            L("H", "订单确认 · 航材采购", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            if (order == null)
            {
                L("None", "（无待确认订单）", 0.0f, 0.5f, 1.0f, 0.7f, 16, UITheme.TextMuted);
                return;
            }
            L("Order", $"订单编号 {order.orderNo} · 类型 {(order.urgent ? "紧急采购" : "常规补货")} · 供应商 {order.supplier}",
                0.0f, 0.80f, 1.0f, 0.90f, 13);
            L("Detail", $"件号 {order.partNo} · 数量 {order.quantity} · 单价 {order.unitCostUsd:F0} 美元 · 交期 {order.leadDays} 天",
                0.0f, 0.70f, 1.0f, 0.80f, 13);
            L("Total", $"订单总金额：{MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd):F2} 万美元",
                0.0f, 0.60f, 0.6f, 0.70f, 14, UITheme.Success, true);
            L("Risk", "供应链风险：独家供应商（中）· 地缘风险（低）· 运输中断（低）· 全球短缺指数 18", 0.0f, 0.48f, 1.0f, 0.58f, 12, UITheme.TextMuted);

            B("Confirm", "确认下单（生成 PO）", 0.02f, 0.30f, 0.34f, 0.40f, UITheme.Accent,
                () =>
                {
                    string msg = "下单失败";
                    if (MaintenanceManager.Instance != null)
                        MaintenanceManager.Instance.ConfirmOrder(order.orderNo, out msg);
                    Toast(msg); RefreshBody();
                }, 13);
            B("Modify", "退回修改", 0.38f, 0.30f, 0.66f, 0.40f, UITheme.PanelDeep,
                () => BackOne(), 12);
            L("Hint", "订单确认仅核对；真正扣款入库需收件箱批准（紧急单本季入库）。", 0.0f, 0.22f, 1.0f, 0.28f, 11, UITheme.TextMuted);
        }
    }
}
