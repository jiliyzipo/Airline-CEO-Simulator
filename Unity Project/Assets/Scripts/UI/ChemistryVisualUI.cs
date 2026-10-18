using System.Collections.Generic;
using System.Text;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 14 · 董事会化学反应可视化——绿色/灰色徽章列表。
    /// </summary>
    public class ChemistryVisualUI : MonoBehaviour
    {
        private Transform _badgeRoot;
        private TextMeshProUGUI _titleText;
        private static readonly Dictionary<string, string> ComboDescriptions = new Dictionary<string, string>
        {
            ["B-01"] = "北极航线 +15% 满意度",
            ["B-02"] = "三人合力 +10% 效率",
            ["B-03"] = "四方协力 +8% 收入",
            ["B-04"] = "四方联动 +8% 安全",
            ["B-05"] = "独狼 +5% 利润",
            ["B-06"] = "铁三角 +10% 满意度",
            ["B-07"] = "三角稳固 +8% 效率",
            ["B-08"] = "三杰 +8% 安全",
            ["B-09"] = "双雄 +6% 效率",
            ["B-10"] = "双星 +6% 满意度",
            ["B-11"] = "双璧 +6% 利润",
            ["B-12"] = "三巨头 +10% 收入",
            ["B-13"] = "四重奏 +8% 品牌",
            ["B-14"] = "四方呼应 +8% 利润",
            ["B-15"] = "五虎 +10% 综合",
            ["B-16"] = "⚠ 冲突：效率 −15%",
            ["B-17"] = "⚠ 冲突：安全 −15%",
            ["B-18"] = "⚠ 派系对立 −10% 满意度",
            ["B-19"] = "⚠ 冲突：利润 −10%",
            ["B-20"] = "⚠ 冲突：品牌 −10%",
            ["B-21"] = "隐藏：五人共鸣",
            ["B-22"] = "隐藏：四人合璧",
            ["B-23"] = "隐藏：四方秘契",
        };

        public static ChemistryVisualUI Build(Transform parent, UIFactory.Anchor anchor)
        {
            var panel = UIFactory.Panel(parent, "Panel_Chemistry", anchor, UITheme.PanelDeep);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<ChemistryVisualUI>();

            ui._titleText = UIFactory.Label(panel.transform, "Title", "🧪 化学反应", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.85f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            var scroll = UIFactory.Panel(panel.transform, "BadgeScroll",
                new UIFactory.Anchor(new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.83f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var vlg = scroll.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 4, 4);
            vlg.spacing = 6;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            ui._badgeRoot = scroll.transform;

            return ui;
        }

        public void Refresh()
        {
            // 清空旧徽章
            for (int i = _badgeRoot.childCount - 1; i >= 0; i--)
                Destroy(_badgeRoot.GetChild(i).gameObject);

            var gs = GameState.Instance;
            if (gs == null) return;

            // 收集当前董事会成员 ID
            var boardIds = new List<string>();
            if (BoardManager.Instance != null)
            {
                foreach (var kv in BoardManager.Instance.memberSatisfaction)
                    boardIds.Add(kv.Key);
            }

            if (boardIds.Count == 0)
            {
                var empty = UIFactory.Label(_badgeRoot, "EmptyHint", "(尚未任命部门经理，无法计算化学反应)", 12,
                    TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
                empty.rectTransform.sizeDelta = new Vector2(0, 28);
                return;
            }

            // 计算化学反应
            var combos = ChemistryMatcher.Match(boardIds);
            if (combos == null || combos.Count == 0)
            {
                var none = UIFactory.Label(_badgeRoot, "NoCombo", "当前无特殊化学反应", 12,
                    TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
                none.rectTransform.sizeDelta = new Vector2(0, 28);
                return;
            }

            // 所有可能的组合（含未触发的）
            var allComboIds = new HashSet<string>
            {
                "B-01","B-02","B-03","B-04","B-05","B-06","B-07","B-08","B-09","B-10","B-11",
                "B-12","B-13","B-14","B-15","B-16","B-17","B-18","B-19","B-20","B-21","B-22","B-23"
            };
            var activeSet = new HashSet<string>(combos);

            foreach (var id in allComboIds)
            {
                bool active = activeSet.Contains(id);
                string desc = ComboDescriptions.TryGetValue(id, out var d) ? d : id;
                Color bg = active ? UITheme.Success : new Color(0.18f, 0.22f, 0.30f, 0.8f);
                Color textColor = active ? Color.white : UITheme.TextMuted;

                var badge = UIFactory.Button(_badgeRoot, $"Badge_{id}",
                    $"{id}：{desc}", UIFactory.Fill, bg, null, 11);
                badge.interactable = false;
                var label = badge.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.color = textColor;
                var img = badge.GetComponent<Image>();
                if (img != null)
                {
                    img.color = bg;
                    var h = img.GetComponent<UIHoverBrighten>();
                    if (h != null) Destroy(h);
                }
                badge.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 32);
            }
        }
    }
}
