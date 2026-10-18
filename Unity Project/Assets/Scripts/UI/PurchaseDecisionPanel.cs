using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n97 购机决策会议（L4）：机型/数量/采购方式 + 五部门意见卡 + 批准/暂缓/拒绝。
    /// 运行时构建（无 Prefab），导航栈驱动。
    /// </summary>
    public class PurchaseDecisionPanel : BasePanel
    {
        Transform _content;
        TextMeshProUGUI _toast;

        string _typeId;
        int _count = 1;
        string _source = "自有资金买断";

        static readonly string[] Sources =
        {
            "自有资金买断", "融资租赁", "经营租赁", "二手引进"
        };

        public static PurchaseDecisionPanel Build(string title)
        {
            var root = UIFactory.Panel(null, "PurchaseDecision", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(card.transform, "Title", title, 21,
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
                new UIFactory.Anchor(new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.22f, 0.005f), new Vector2(0.50f, 0.13f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<PurchaseDecisionPanel>();
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            UIFactory.Button(card.transform, "Approve", "呈请 CEO 发函",
                new UIFactory.Anchor(new Vector2(0.22f, 0.02f), new Vector2(0.42f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Success, panel.Approve, 13);
            UIFactory.Button(card.transform, "Defer", "暂缓",
                new UIFactory.Anchor(new Vector2(0.44f, 0.02f), new Vector2(0.58f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Warning, panel.Defer, 13);
            UIFactory.Button(card.transform, "Reject", "拒绝",
                new UIFactory.Anchor(new Vector2(0.60f, 0.02f), new Vector2(0.74f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Danger, panel.Reject, 13);
            UIFactory.Button(card.transform, "Draft", "保存草案",
                new UIFactory.Anchor(new Vector2(0.76f, 0.02f), new Vector2(0.96f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, panel.SaveDraft, 12);
            panel.BindPage("n97_购机决策会议", title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
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
            if (string.IsNullOrEmpty(_typeId))
            {
                _typeId = !string.IsNullOrEmpty(FleetNetContext.PurchaseTypeId)
                    ? FleetNetContext.PurchaseTypeId
                    : "A320neo";
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);

            var gs = GameState.Instance;
            var types = DataBase.Instance != null ? DataBase.Instance.AircraftTypes : null;
            var cat = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(_typeId) : null;
            if (cat == null && types != null && types.Count > 0)
            {
                cat = types[0];
                _typeId = cat.typeId;
            }
            if (cat == null)
            {
                L("H", "购机决策会议", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                L("E", "机型目录未加载。", 0.0f, 0.78f, 1.0f, 0.86f, 14, UITheme.Warning);
                return;
            }

            bool isBuyout = _source == "自有资金买断";
            float total = cat.priceWan * _count;
            float down = isBuyout ? total : total * 0.10f;
            bool affordable = (gs?.cash ?? 0f) + 0.01f >= down;
            int routes = gs?.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);

            L("H", $"议题：引进 {_count} 架 {cat.typeId} · {_source}",
                0.0f, 0.92f, 1.0f, 1.0f, 15, UITheme.Accent, true);

            L("TypeH", "机型（滚动选择 · 目录价）", 0.0f, 0.86f, 0.48f, 0.91f, 13, UITheme.TextMuted, true);
            var typeList = UIFactory.VerticalScrollArea(_content, "TypeScroll",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.48f, 0.85f), Vector2.zero, Vector2.zero));
            float rowY = 6f;
            const float rowH = 36f;
            if (types != null)
            {
                for (int i = 0; i < types.Count; i++)
                {
                    var t = types[i];
                    string id = t.typeId;
                    var c = id == _typeId ? UITheme.NavActive : UITheme.PanelDeep;
                    var btn = UIFactory.Button(typeList, "T" + i, $"{id}  ·  {t.priceWan:F0}万",
                        UIFactory.Fill, c, () => { _typeId = id; RefreshBody(); }, 12);
                    UIFactory.PinFromTop(btn.gameObject, rowY, rowH, 0.02f, 0.98f);
                    rowY += rowH + 6f;
                }
            }
            UIFactory.SetScrollContentHeight(typeList, rowY + 8f);

            L("SrcH", "采购方式", 0.52f, 0.86f, 1.0f, 0.91f, 13, UITheme.TextMuted, true);
            for (int i = 0; i < Sources.Length; i++)
            {
                string s = Sources[i];
                float y1 = 0.85f - i * 0.09f;
                var c = s == _source ? UITheme.NavActive : UITheme.PanelDeep;
                B("S" + i, s, 0.52f, y1 - 0.08f, 1.0f, y1, c, () => { _source = s; RefreshBody(); }, 13);
            }

            L("Spec", $"载客 {cat.maxPassengers}  ·  航程 {cat.maxRangeKm:F0} km  ·  单价 {cat.priceWan:F0} 万",
                0.52f, 0.46f, 1.0f, 0.52f, 12, UITheme.TextPrimary);

            L("CntH", "数量", 0.52f, 0.40f, 0.64f, 0.46f, 13, UITheme.TextMuted, true);
            B("Cnt-", "−", 0.66f, 0.39f, 0.74f, 0.47f, UITheme.PanelDeep,
                () => { _count = Mathf.Max(1, _count - 1); RefreshBody(); }, 16);
            L("CntV", _count.ToString(), 0.75f, 0.39f, 0.86f, 0.47f, 18, UITheme.TextPrimary, true, TextAlignmentOptions.Center);
            B("Cnt+", "+", 0.87f, 0.39f, 0.96f, 0.47f, UITheme.PanelDeep,
                () => { _count++; RefreshBody(); }, 16);

            L("Fin", $"总价 {total:F0} 万  ·  本季支付 {down:F0} 万  ·  现金 {gs?.cash ?? 0:F0} 万",
                0.52f, 0.32f, 1.0f, 0.38f, 12, UITheme.TextMuted);
            L("Ok", affordable ? "资金充足，可发 LOI" : "资金不足，需改租赁/减数量或先融资",
                0.52f, 0.26f, 1.0f, 0.32f, 13, affordable ? UITheme.Success : UITheme.Danger, true);

            L("Cards", "部门意见", 0.52f, 0.20f, 1.0f, 0.26f, 13, UITheme.Accent, true);
            L("O0", gap > 0 ? $"运营 · 缺口 {gap} 架，建议引进" : "运营 · 运力已覆盖航网，可作备份",
                0.52f, 0.16f, 1.0f, 0.20f, 11, UITheme.TextMuted);
            L("O1", affordable ? "财务 · 现金可覆盖本季支付" : "财务 · 现金不足，建议租赁或减单",
                0.52f, 0.12f, 1.0f, 0.16f, 11, UITheme.TextMuted);
            L("O2", "市场 · 该机型目标航线收益稳定", 0.52f, 0.08f, 1.0f, 0.12f, 11, UITheme.TextMuted);
            L("O3", "机务 · OEM 关系与交付窗口可接受", 0.52f, 0.04f, 1.0f, 0.08f, 11, UITheme.TextMuted);
            L("O4", "战略 · 符合机队规划，大单建议分期", 0.52f, 0.00f, 1.0f, 0.04f, 11, UITheme.TextMuted);
        }

        void Approve()
        {
            bool ok = InboxManager.OfferFleetBuy(GameState.Instance, _typeId, _count, _source, out string msg);
            Toast(msg);
            if (ok)
            {
                FleetNetContext.PurchaseTypeId = _typeId;
                FleetNetContext.PurchaseCount = _count;
                BackOne();
            }
        }

        void Defer()
        {
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"购机会议暂缓：{_typeId} ×{_count}（{_source}）已标记为待定。");
            gs?.NotifyChanged();
            Toast("已暂缓，议题标记为待定。");
            BackOne();
        }

        void Reject()
        {
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"购机会议拒绝：{_typeId} ×{_count}（{_source}）已搁置。");
            gs?.NotifyChanged();
            Toast("已拒绝并标记搁置。");
            BackOne();
        }

        void SaveDraft()
        {
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"购机会议草案已保存：{_typeId} ×{_count}（{_source}），可在收件箱「会议草案」找回。");
            gs?.NotifyChanged();
            Toast("草案已保存到收件箱。");
        }

        void Toast(string msg)
        {
            if (_toast != null) _toast.text = msg ?? "";
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

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }
    }
}
