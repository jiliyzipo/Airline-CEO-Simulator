using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>Phase 10 · 机队列表面板。</summary>
    public class FleetUI : MonoBehaviour
    {
        private TextMeshProUGUI _summaryText;
        private TextMeshProUGUI _toastText;
        private Transform _listRoot;
        private FleetPurchaseUI _purchaseUi;
        private readonly List<GameObject> _rows = new List<GameObject>();

        // 与数据行共用的列锚点（相对行/表头容器宽度）
        private const float ColReg0 = 0.05f, ColReg1 = 0.18f;
        private const float ColType0 = 0.19f, ColType1 = 0.38f;
        private const float ColAge0 = 0.39f, ColAge1 = 0.50f;
        private const float ColStatus0 = 0.51f, ColStatus1 = 0.68f;
        private const float ColAction0 = 0.70f, ColAction1 = 0.98f;

        public static FleetUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Fleet", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<FleetUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public void BindPurchase(FleetPurchaseUI purchase) => _purchaseUi = purchase;

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "✈️ 机队管理", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.92f), new Vector2(0.55f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _summaryText = UIFactory.Label(panel, "Summary", "", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.85f), new Vector2(0.97f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            // 表头与列表同宽，列锚点与 CreateRow 一致
            var headerBar = UIFactory.Panel(panel, "HeaderBar",
                new UIFactory.Anchor(new Vector2(0.02f, 0.79f), new Vector2(0.98f, 0.845f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false).gameObject;
            headerBar.GetComponent<Image>().raycastTarget = false;
            AddColumnLabel(headerBar.transform, "HReg", "注册号", ColReg0, ColReg1);
            AddColumnLabel(headerBar.transform, "HType", "机型", ColType0, ColType1);
            AddColumnLabel(headerBar.transform, "HAge", "机龄", ColAge0, ColAge1);
            AddColumnLabel(headerBar.transform, "HStatus", "状态", ColStatus0, ColStatus1);
            AddColumnLabel(headerBar.transform, "HAction", "操作", ColAction0, ColAction1);

            var listHost = UIFactory.Panel(panel, "ListHost",
                new UIFactory.Anchor(new Vector2(0.02f, 0.14f), new Vector2(0.98f, 0.79f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            _listRoot = listHost.transform;

            UIFactory.Button(panel, "Buy", "购买飞机",
                new UIFactory.Anchor(new Vector2(0.28f, 0.03f), new Vector2(0.72f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OpenPurchase, 16);

            _toastText = UIFactory.Label(panel, "Toast", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.0f), new Vector2(0.95f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
        }

        private static void AddColumnLabel(Transform parent, string name, string text, float x0, float x1)
        {
            UIFactory.Label(parent, name, text, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
        }

        public void Refresh()
        {
            if (FleetManager.Instance != null)
                FleetManager.Instance.EnsureStarterFleet();

            var gs = GameState.Instance;
            var fm = FleetManager.Instance;
            if (_summaryText != null && fm != null)
            {
                _summaryText.text =
                    $"总数 {fm.CountTotal(gs)} ｜ 适航 {fm.CountAirworthy(gs)} ｜ 平均机龄 {fm.AverageAge(gs):F1} 年" +
                    $" ｜ 月维护约 {fm.CalcMonthlyMaintenance(gs):F1} 万 ｜ 现金 {gs?.cash:F0} 万";
            }

            ClearRows();
            if (gs?.fleet == null) return;

            // 列表：自上而下，每行约 36px → 归一化高度按可见区域分
            const float rowH = 0.095f;
            float y = 1f;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var ac = gs.fleet[i];
                if (ac == null) continue;
                y -= rowH;
                if (y < 0f) break;
                CreateRow(ac, y, rowH * 0.88f);
            }
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i]);
            }
            _rows.Clear();
        }

        private void CreateRow(Aircraft ac, float yMin, float height)
        {
            var row = UIFactory.Panel(_listRoot, "Row_" + ac.registration,
                new UIFactory.Anchor(new Vector2(0f, yMin), new Vector2(1f, yMin + height), Vector2.zero, Vector2.zero),
                new Color(0.08f, 0.14f, 0.24f, 0.85f));
            _rows.Add(row);

            Color statusColor = ac.status switch
            {
                AircraftStatuses.Airworthy => UITheme.Success,
                AircraftStatuses.Maintenance => UITheme.Warning,
                _ => UITheme.TextMuted
            };

            var dot = UIFactory.Panel(row.transform, "Dot",
                new UIFactory.Anchor(new Vector2(0.01f, 0.28f), new Vector2(0.035f, 0.72f), Vector2.zero, Vector2.zero),
                statusColor, rounded: true);
            dot.GetComponent<Image>().raycastTarget = false;

            UIFactory.Label(row.transform, "Reg", ac.registration ?? "—", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColReg0, 0.1f), new Vector2(ColReg1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(row.transform, "Type", ac.type ?? "—", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColType0, 0.1f), new Vector2(ColType1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(row.transform, "Age", $"{ac.age} 年", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColAge0, 0.1f), new Vector2(ColAge1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(row.transform, "Status", ac.status ?? "—", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColStatus0, 0.1f), new Vector2(ColStatus1, 0.9f), Vector2.zero, Vector2.zero),
                statusColor);

            bool canRetire = ac.status != AircraftStatuses.Retired;
            if (canRetire)
            {
                float residual = FleetManager.CalcResidualValue(ac);
                string reg = ac.registration;
                UIFactory.Button(row.transform, "Retire", $"退役(+{residual:F0}万)",
                    new UIFactory.Anchor(new Vector2(ColAction0, 0.12f), new Vector2(ColAction1, 0.88f), Vector2.zero, Vector2.zero),
                    UITheme.Danger, () => OnRetire(reg), 11);
            }
            else
            {
                UIFactory.Label(row.transform, "Done", "已退役", 12, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(ColAction0, 0.1f), new Vector2(ColAction1, 0.9f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
            }
        }

        private void OnRetire(string registration)
        {
            if (FleetManager.Instance == null) return;
            FleetManager.Instance.TryRetireAircraft(registration, out string msg);
            Toast(msg);
            Refresh();
        }

        private void OpenPurchase()
        {
            if (_purchaseUi == null)
                _purchaseUi = FindFirstObjectByType<FleetPurchaseUI>(FindObjectsInactive.Include);
            if (_purchaseUi == null)
            {
                Toast("购买界面未就绪");
                return;
            }
            _purchaseUi.Show(Refresh);
        }

        private void Toast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
            Debug.Log("[FleetUI] " + msg);
        }
    }
}
