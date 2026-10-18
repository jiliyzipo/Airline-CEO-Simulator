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
    /// <summary>n23 部门负责人任命（L4 · 空缺时 3 选 1）。</summary>
    public static class AppointmentContext
    {
        /// <summary>当前待任命的部门代码（如 "OPS"）。</summary>
        public static string DeptCode = "OPS";
    }

    public class AppointmentPanel : BasePanel
    {
        Transform _content;
        TextMeshProUGUI _deptLabel;
        TextMeshProUGUI _toast;
        readonly List<PersonnelData> _candidates = new List<PersonnelData>();
        int _selectedIndex = -1;

        public static AppointmentPanel Build()
        {
            var root = UIFactory.Panel(null, "Appointment", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.10f, 0.86f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(1.5f, -1.5f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "总监任命", 21,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.70f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.18f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var dept = UIFactory.Label(card.transform, "Dept", "", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.81f), new Vector2(0.96f, 0.89f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.80f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var confirmBtn = UIFactory.Button(card.transform, "Confirm", "确认任命",
                new UIFactory.Anchor(new Vector2(0.60f, 0.02f), new Vector2(0.96f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => { }, 15);

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.58f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<AppointmentPanel>();
            panel._content = content.transform;
            panel._deptLabel = dept;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n23_部门负责人任命", "总监任命");

            confirmBtn.onClick.RemoveAllListeners();
            confirmBtn.onClick.AddListener(panel.Confirm);
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(panel.BackOne);
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            ResolveRefs();
        }

        void OnEnable()
        {
            ResolveRefs();
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
            if (_deptLabel == null)
            {
                var d = transform.Find("Card/Dept");
                if (d != null) _deptLabel = d.GetComponent<TextMeshProUGUI>();
            }
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            string code = AppointmentContext.DeptCode;
            if (string.IsNullOrEmpty(code)) code = "OPS";

            var mgr = DepartmentManager.Instance;
            if (mgr == null) return;

            if (_deptLabel != null)
                _deptLabel.text = $"空缺部门：{DepartmentCatalog.DisplayName(code)}";

            _candidates.Clear();
            _selectedIndex = -1;
            var cands = mgr.GetCandidates(code, 3);
            for (int i = 0; i < cands.Count; i++) _candidates.Add(cands[i]);

            if (_content == null) return;
            Clear(_content);

            if (_candidates.Count == 0)
            {
                UIFactory.Label(_content, "Empty", "暂无可用候选人（该部门人选已用尽或已任命）。", 14,
                    TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.8f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                return;
            }

            UIFactory.Label(_content, "Hint", "点击候选人卡片选择，再点「确认任命」。", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            float w = 0.30f;
            float gap = 0.02f;
            for (int i = 0; i < _candidates.Count && i < 3; i++)
            {
                float x0 = 0.02f + i * (w + gap);
                float x1 = x0 + w;
                BuildCandidateCard(i, _candidates[i], x0, x1);
            }
        }

        void BuildCandidateCard(int index, PersonnelData e, float x0, float x1)
        {
            bool sel = index == _selectedIndex;
            var card = UIFactory.Panel(_content, "Cand_" + index,
                new UIFactory.Anchor(new Vector2(x0, 0.02f), new Vector2(x1, 0.90f), Vector2.zero, Vector2.zero),
                sel ? new Color(0.08f, 0.24f, 0.16f, 1f) : UITheme.PanelDeep);
            UIFactory.AddBorder(card, sel ? UITheme.Success : UITheme.Border, new Vector2(1.2f, -1.2f));

            var btn = card.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = colors;
            int idx = index;
            btn.onClick.AddListener(() => { _selectedIndex = idx; RefreshBody(); });

            string traits = string.IsNullOrEmpty(e.traitsCsv) ? "—" : e.traitsCsv.Replace("、", "·");
            UIFactory.Label(card.transform, "Name", e.fullName, 15,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(card.transform, "Meta", $"{e.age} 岁 · {e.nationality}", 12,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(card.transform, "Traits", traits, 12,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.46f), new Vector2(0.94f, 0.62f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(card.transform, "Salary", $"年薪 {e.baseSalary:F0} 万", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.46f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            float effPct = (e.efficiencyBonus - 1f) * 100f;
            UIFactory.Label(card.transform, "Eff",
                $"能力 {(effPct >= 0 ? "+" : "")}{effPct:F0}%", 12,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.28f), Vector2.zero, Vector2.zero),
                effPct >= 0 ? UITheme.Success : UITheme.Warning);
        }

        void Confirm()
        {
            var mgr = DepartmentManager.Instance;
            if (mgr == null) return;
            if (_selectedIndex < 0 || _selectedIndex >= _candidates.Count)
            {
                Toast("请先选择一名候选人。");
                return;
            }
            var e = _candidates[_selectedIndex];
            bool ok = mgr.Appoint(AppointmentContext.DeptCode, e.id);
            Toast(ok ? $"已任命 {e.fullName} 为{DepartmentCatalog.DisplayName(AppointmentContext.DeptCode)}总监。" : "任命失败（可能已被占用）。");
            if (ok)
                Invoke(nameof(BackOneAfterAppoint), 0.4f);
        }

        void BackOneAfterAppoint()
        {
            BackOne();
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
    }
}
