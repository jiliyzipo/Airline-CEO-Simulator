using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>n113 部门信息：编制摘要 + 职位空缺 + 在职名单（对标 FM 职员概况）。</summary>
    public class DepartmentInfoPanel : BasePanel
    {
        Transform _content;

        public static DepartmentInfoPanel Build()
        {
            var root = UIFactory.Panel(null, "n113_部门信息", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "部门信息", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.91f), new Vector2(0.70f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.91f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.18f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.03f, 0.11f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var panel = root.AddComponent<DepartmentInfoPanel>();
            panel._content = content.transform;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n113_部门信息", "部门信息");
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            WireBackButton();
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        void OnEnable() => Refresh();

        void Refresh()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            string dept = HrContext.SelectedDept;
            if (string.IsNullOrEmpty(dept)) dept = "OPS";
            var gs = GameState.Instance;
            var mgr = DepartmentManager.Instance;
            PersonnelGenerator.EnsurePool(gs);

            string dname = DepartmentCatalog.DisplayName(dept);
            int team = StaffEstablishment.TeamHeadcount(dept, gs, mgr);
            int quota = StaffEstablishment.QuotaTotal(dept);
            if (titleLabel != null) titleLabel.text = dname;

            L("H", dname, 0.00f, 0.90f, 0.70f, 1.00f, 20, UITheme.TextPrimary, true);
            L("N", team + " 名管理团队成员", 0.00f, 0.84f, 0.70f, 0.90f, 14, UITheme.TextMuted);

            string board;
            if (team > quota) board = "董事会认为管理团队人数偏多，建议控制编制。";
                else if (team < quota) board = $"尚有 {quota - team} 个编制空缺。总监由您任命；其余岗位由人力与职能部门招聘，面试后报您审批。";
            else board = "董事会认为当前编制与入职人数匹配。";
            L("B", board, 0.00f, 0.78f, 1.00f, 0.84f, 13, UITheme.TextMuted);

            var roles = StaffEstablishment.Roles(dept);
            int filledSlots = 0;
            for (int r = 0; r < roles.Length; r++)
                filledSlots += Mathf.Min(StaffEstablishment.CountFilled(dept, roles[r], gs, mgr), roles[r].quota);
            int iconN = Mathf.Clamp(quota, 1, 12);
            for (int k = 0; k < iconN; k++)
            {
                float t = k / (float)Mathf.Max(1, iconN);
                var dot = UIFactory.Panel(_content, "I" + k,
                    new UIFactory.Anchor(new Vector2(t * 0.55f, 0.72f), new Vector2(t * 0.55f + 0.04f, 0.77f),
                        Vector2.zero, Vector2.zero),
                    k < filledSlots ? UITheme.Accent : new Color(0.25f, 0.35f, 0.48f, 0.55f));
                var im = dot.GetComponent<Image>();
                im.sprite = UIFactory.CircleSprite;
                im.raycastTarget = false;
            }

            L("QH", "职位编制", 0.00f, 0.65f, 0.40f, 0.71f, 14, UITheme.Accent, true);
            float quotaBottom = 0.65f;
            for (int r = 0; r < roles.Length; r++)
            {
                var role = roles[r];
                int filled = StaffEstablishment.CountFilled(dept, role, gs, mgr);
                float y = 0.63f - r * 0.038f;
                quotaBottom = y;
                Color c = filled > role.quota ? new Color(0.45f, 0.85f, 1f, 1f)
                    : filled == 0 && role.quota > 0 ? UITheme.TextMuted
                    : filled == role.quota ? UITheme.Warning
                    : UITheme.TextPrimary;
                L("Q" + r, $"{filled}/{role.quota}  {StaffEstablishment.DisplayTitle(role.jobTitle, role.rank)}", 0.02f, y, 0.48f, y + 0.038f, 13, c);
            }

            float tableTop = Mathf.Max(0.08f, quotaBottom - 0.04f);
            L("TH", "在职名单", 0.00f, tableTop - 0.06f, 1.00f, tableTop, 14, UITheme.Accent, true);
            float colY1 = tableTop - 0.06f;
            float colY0 = colY1 - 0.045f;
            Col("C0", "职位", 0.00f, colY0, 0.22f, colY1);
            Col("C1", "姓名", 0.22f, colY0, 0.46f, colY1);
            Col("C2", "国籍", 0.46f, colY0, 0.62f, colY1);
            Col("C3", "类型", 0.62f, colY0, 0.76f, colY1);
            Col("C4", "年薪", 0.76f, colY0, 1.00f, colY1);

            var people = StaffEstablishment.ListEmployed(dept, gs, mgr);
            int row = 0;
            int maxRows = 6;
            for (int i = 0; i < people.Count && row < maxRows; i++)
            {
                var p = people[i];
                float y1 = colY0 - row * 0.040f;
                float y0 = y1 - 0.038f;
                string wage = p.baseSalary > 0 ? $"{p.baseSalary:F0} 万" : "—";
                L("J" + row, StaffEstablishment.DisplayTitle(p.jobTitle, p.rank), 0.00f, y0, 0.22f, y1, 12, UITheme.TextPrimary);
                string pid = p.id;
                var nameBtn = UIFactory.Button(_content, "N" + row, p.fullName ?? "",
                    new UIFactory.Anchor(new Vector2(0.22f, y0), new Vector2(0.46f, y1), Vector2.zero, Vector2.zero),
                    new Color(0, 0, 0, 0.01f), () =>
                    {
                        HrContext.SelectedStaffId = pid;
                        HrContext.SelectedStaffIsExec = false;
                        HrPanelUI.OpenHrPage("n114_员工信息");
                    }, 12);
                var nl = nameBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (nl != null)
                {
                    nl.alignment = TextAlignmentOptions.MidlineLeft;
                    nl.color = UITheme.Accent;
                }
                L("Nat" + row, p.nationality ?? "—", 0.46f, y0, 0.62f, y1, 12, UITheme.TextPrimary);
                L("T" + row, "全职", 0.62f, y0, 0.76f, y1, 12, UITheme.TextMuted);
                L("W" + row, wage, 0.76f, y0, 1.00f, y1, 12, UITheme.TextPrimary);
                row++;
            }
            if (people.Count == 0)
                L("Empty", "暂无在职管理人员。", 0.00f, colY0 - 0.08f, 0.80f, colY0, 13, UITheme.Warning);
            else if (people.Count > maxRows)
                L("More", $"其余 {people.Count - maxRows} 人未列出。", 0.00f, 0.00f, 0.80f, 0.04f, 11, UITheme.TextMuted);
        }

        void Col(string name, string text, float x0, float y0, float x1, float y1)
        {
            L(name, text, x0, y0, x1, y1, 11, UITheme.TextMuted);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color color, bool bold = false)
        {
            return UIFactory.Label(_content, name, text, size, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color, bold ? FontStyles.Bold : FontStyles.Normal);
        }
    }
}
