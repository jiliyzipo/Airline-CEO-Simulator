using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>人事面板：部门任命 / 效率 / 忠诚度 / 化学反应（GDD 第 6 章 · Phase 8）。</summary>
    public class HRUI : MonoBehaviour
    {
        private Transform _cardsRoot;
        private readonly List<GameObject> _cardRows = new List<GameObject>();

        private GameObject _candidateRoot;
        private TextMeshProUGUI _candidateTitle;
        private Transform _candidateSlotA;
        private Transform _candidateSlotB;
        private string _pendingDept;
        private readonly List<ExecutiveData> _pendingCandidates = new List<ExecutiveData>();

        private GameObject _confirmRoot;
        private TextMeshProUGUI _confirmText;
        private System.Action _confirmAction;

        private GameObject _resignRoot;
        private TextMeshProUGUI _resignText;

        #region Build

        public static HRUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_HR", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<HRUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "人事中心 · 部门任命", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.55f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            // 化学反应加成在后台生效，不向玩家展示配方（自行摸索）
            UIFactory.Label(panel, "Hint", "任命各部门负责人，关注效率与忠诚度变化。", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            // 列表底色更深，行间空隙才看得清
            var listHost = UIFactory.Panel(panel, "DeptList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.07f, 0.12f, 1f));

            // 滚动视口：7 行固定高度不会再被压扁
            var viewport = UIFactory.Panel(listHost.transform, "Viewport", UIFactory.Fill,
                new Color(0.04f, 0.07f, 0.12f, 1f), rounded: false);
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperCenter;
            // 与董事会/安全列表一致：不控制高度，靠 LayoutElement + sizeDelta
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = listHost.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            _cardsRoot = content.transform;

            BuildCandidateModal(panel);
            BuildConfirmModal(panel);
            BuildResignModal(panel);
        }

        private void BuildCandidateModal(Transform parent)
        {
            _candidateRoot = UIFactory.Panel(parent, "CandidateModal", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.82f), rounded: false).gameObject;
            _candidateRoot.GetComponent<Image>().raycastTarget = true;

            var card = UIFactory.Panel(_candidateRoot.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.12f, 0.18f), new Vector2(0.88f, 0.86f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.09f, 0.16f, 0.98f)).gameObject;
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(2f, -2f));

            _candidateTitle = UIFactory.Label(card.transform, "Title", "选择候选人", 22,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _candidateSlotA = UIFactory.Panel(card.transform, "SlotA",
                new UIFactory.Anchor(new Vector2(0.04f, 0.18f), new Vector2(0.48f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep).transform;
            _candidateSlotB = UIFactory.Panel(card.transform, "SlotB",
                new UIFactory.Anchor(new Vector2(0.52f, 0.18f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep).transform;

            UIFactory.Button(card.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.35f, 0.04f), new Vector2(0.65f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => _candidateRoot.SetActive(false), 16);

            _candidateRoot.SetActive(false);
        }

        private void BuildConfirmModal(Transform parent)
        {
            _confirmRoot = UIFactory.Panel(parent, "ConfirmModal", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false).gameObject;
            var card = UIFactory.Panel(_confirmRoot.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.28f, 0.35f), new Vector2(0.72f, 0.68f), Vector2.zero, Vector2.zero),
                new Color(0.06f, 0.08f, 0.14f, 0.98f)).gameObject;
            UIFactory.AddBorder(card, UITheme.Warning, new Vector2(2f, -2f));
            _confirmText = UIFactory.Label(card.transform, "Text", "", 16, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Button(card.transform, "Ok", "确认",
                new UIFactory.Anchor(new Vector2(0.12f, 0.08f), new Vector2(0.45f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () =>
                {
                    _confirmRoot.SetActive(false);
                    _confirmAction?.Invoke();
                }, 16);
            UIFactory.Button(card.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.55f, 0.08f), new Vector2(0.88f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => _confirmRoot.SetActive(false), 16);
            _confirmRoot.SetActive(false);
        }

        private void BuildResignModal(Transform parent)
        {
            _resignRoot = UIFactory.Panel(parent, "ResignModal", UIFactory.Fill,
                new Color(0.02f, 0.03f, 0.06f, 0.78f), rounded: false).gameObject;
            var card = UIFactory.Panel(_resignRoot.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.68f), Vector2.zero, Vector2.zero),
                new Color(0.12f, 0.06f, 0.08f, 0.98f)).gameObject;
            UIFactory.AddBorder(card, UITheme.Danger, new Vector2(2f, -2f));
            _resignText = UIFactory.Label(card.transform, "Text", "", 17, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Button(card.transform, "Ok", "知道了",
                new UIFactory.Anchor(new Vector2(0.35f, 0.08f), new Vector2(0.65f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () =>
                {
                    _resignRoot.SetActive(false);
                    Refresh();
                }, 16);
            _resignRoot.SetActive(false);
        }

        #endregion

        #region Public

        public void Refresh()
        {
            RebuildCards(DepartmentManager.Instance);
        }

        public void ShowResignation(string deptCode, string name)
        {
            if (_resignText != null)
            {
                _resignText.text =
                    $"离职事件\n\n{name}（{DepartmentCatalog.DisplayName(deptCode)}）\n因忠诚度过低主动辞职。\n该部门现已空缺，可重新任命。";
            }
            if (_resignRoot != null)
            {
                _resignRoot.SetActive(true);
                _resignRoot.transform.SetAsLastSibling();
            }
            Refresh();
        }

        #endregion

        #region Cards

        private void RebuildCards(DepartmentManager dm)
        {
            for (int i = 0; i < _cardRows.Count; i++)
            {
                if (_cardRows[i] != null) Destroy(_cardRows[i]);
            }
            _cardRows.Clear();
            if (_cardsRoot == null || dm == null) return;

            for (int i = 0; i < DepartmentCatalog.Codes.Length; i++)
            {
                string code = DepartmentCatalog.Codes[i];
                _cardRows.Add(BuildDeptCard(code, dm.GetDept(code), dm.GetHead(code)));
            }

            if (_cardsRoot is RectTransform contentRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        }

        private GameObject BuildDeptCard(string code, DepartmentRuntime dept, ExecutiveData head)
        {
            // 槽位高度含少量底部空隙，行距适中、七行更易一屏看完
            const float SlotH = 92f;
            const float CardBottomPad = 0.06f;

            var slot = new GameObject($"Slot_{code}", typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(_cardsRoot, false);
            var slotRt = slot.GetComponent<RectTransform>();
            slotRt.anchorMin = new Vector2(0, 1);
            slotRt.anchorMax = new Vector2(1, 1);
            slotRt.pivot = new Vector2(0.5f, 1f);
            slotRt.sizeDelta = new Vector2(0, SlotH);
            var slotLe = slot.GetComponent<LayoutElement>();
            slotLe.minHeight = SlotH;
            slotLe.preferredHeight = SlotH;
            slotLe.flexibleWidth = 1;
            slotLe.flexibleHeight = 0;

            var row = UIFactory.Panel(slot.transform, $"Card_{code}",
                new UIFactory.Anchor(new Vector2(0f, CardBottomPad), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                new Color(0.10f, 0.18f, 0.30f, 1f));
            UIFactory.AddBorder(row, UITheme.Border, new Vector2(1.2f, -1.2f));

            bool vacant = head == null;
            string headLine = vacant ? "负责人：空缺" : $"负责人：{head.fullName}";
            float eff = dept != null ? dept.efficiency : 0f;
            float loy = dept != null ? dept.loyalty : 0f;

            UIFactory.Label(row.transform, "Name", DepartmentCatalog.DisplayName(code), 17,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.52f), new Vector2(0.26f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(row.transform, "Head", headLine, 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.12f), new Vector2(0.26f, 0.48f), Vector2.zero, Vector2.zero),
                vacant ? UITheme.Warning : UITheme.TextMuted);

            BuildBar(row.transform, "EffBar", "效率", vacant ? 0f : (eff - 0.7f) / 0.6f,
                vacant ? "—" : $"{eff:F2}",
                new UIFactory.Anchor(new Vector2(0.28f, 0.52f), new Vector2(0.68f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.Accent);

            BuildBar(row.transform, "LoyBar", "忠诚", vacant ? 0f : loy / 100f,
                vacant ? "—" : $"{loy:F0}",
                new UIFactory.Anchor(new Vector2(0.28f, 0.1f), new Vector2(0.68f, 0.48f), Vector2.zero, Vector2.zero),
                loy < 30f ? UITheme.Danger : UITheme.Success);

            if (vacant)
            {
                UIFactory.Button(row.transform, "Appoint", "任命",
                    new UIFactory.Anchor(new Vector2(0.72f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () => OpenCandidates(code), 18);
            }
            else
            {
                UIFactory.Button(row.transform, "Replace", "更换",
                    new UIFactory.Anchor(new Vector2(0.72f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Warning, () => ConfirmReplace(code, head, dept), 16);
            }

            return slot;
        }

        private static void BuildBar(Transform parent, string name, string label, float fill01, string value,
            UIFactory.Anchor anchor, Color fillColor)
        {
            var host = UIFactory.Panel(parent, name, anchor, new Color(0, 0, 0, 0), rounded: false);
            host.GetComponent<Image>().raycastTarget = false;

            UIFactory.Label(host.transform, "L", label, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.14f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var track = UIFactory.Panel(host.transform, "Track",
                new UIFactory.Anchor(new Vector2(0.16f, 0.25f), new Vector2(0.78f, 0.75f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.08f, 0.12f));
            var fill = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, fillColor).GetComponent<Image>();
            fill.sprite = UIFactory.RoundedSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = Mathf.Clamp01(fill01);

            UIFactory.Label(host.transform, "V", value, 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.8f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
        }

        #endregion

        #region Appoint / Replace

        private void OpenCandidates(string deptCode)
        {
            var dm = DepartmentManager.Instance;
            if (dm == null) return;

            _pendingDept = deptCode;
            _pendingCandidates.Clear();
            _pendingCandidates.AddRange(dm.GetCandidates(deptCode, 2));

            if (_candidateTitle != null)
                _candidateTitle.text = $"任命 · {DepartmentCatalog.DisplayName(deptCode)}（选 1 人）";

            FillCandidateSlot(_candidateSlotA, _pendingCandidates.Count > 0 ? _pendingCandidates[0] : null);
            FillCandidateSlot(_candidateSlotB, _pendingCandidates.Count > 1 ? _pendingCandidates[1] : null);

            if (_pendingCandidates.Count == 0)
            {
                if (_candidateTitle != null)
                    _candidateTitle.text = $"{DepartmentCatalog.DisplayName(deptCode)}：暂无可用候选人";
            }

            _candidateRoot.SetActive(true);
            _candidateRoot.transform.SetAsLastSibling();
        }

        private void FillCandidateSlot(Transform slot, ExecutiveData exec)
        {
            for (int i = slot.childCount - 1; i >= 0; i--)
                Destroy(slot.GetChild(i).gameObject);

            if (exec == null)
            {
                UIFactory.Label(slot, "Empty", "无候选人", 16, TextAlignmentOptions.Center, UIFactory.Fill,
                    UITheme.TextMuted);
                return;
            }

            var avatar = UIFactory.Panel(slot, "Avatar",
                new UIFactory.Anchor(new Vector2(0.3f, 0.72f), new Vector2(0.7f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.Label(avatar.transform, "Initial",
                string.IsNullOrEmpty(exec.fullName) ? "?" : exec.fullName.Substring(0, 1),
                22, TextAlignmentOptions.Center, UIFactory.Fill, UITheme.Accent, FontStyles.Bold);

            UIFactory.Label(slot, "Name", exec.fullName, 16, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(slot, "Meta", $"{exec.age} 岁 · {exec.nationality}", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.58f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            string traits = exec.traits != null && exec.traits.Length > 0
                ? string.Join(" / ", exec.traits)
                : "—";
            UIFactory.Label(slot, "Traits", $"能力：{traits}\n核心加成 {exec.efficiencyBonus:F2}", 13,
                TextAlignmentOptions.Top,
                new UIFactory.Anchor(new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.48f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(slot, "Salary", $"薪酬要求：年薪 {exec.baseSalary:F0} 万", 14,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            string id = exec.id;
            UIFactory.Button(slot, "Pick", "确认任命",
                new UIFactory.Anchor(new Vector2(0.15f, 0.03f), new Vector2(0.85f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => ConfirmAppoint(id), 14);
        }

        private void ConfirmAppoint(string executiveId)
        {
            var dm = DepartmentManager.Instance;
            if (dm == null || string.IsNullOrEmpty(_pendingDept)) return;
            if (dm.Appoint(_pendingDept, executiveId))
            {
                _candidateRoot.SetActive(false);
                Refresh();
            }
        }

        private void ConfirmReplace(string code, ExecutiveData head, DepartmentRuntime dept)
        {
            var gs = GameState.Instance;
            float salary = dept != null ? dept.salary : (head != null ? head.baseSalary : 0f);
            float sev = DepartmentManager.CalcSeverance(salary, gs != null ? gs.quarter : 1);
            string name = head != null ? head.fullName : "负责人";
            _confirmText.text =
                $"更换负责人？\n\n将遣散 {name}\n遣散费约 {sev:F0} 万美元\n（年薪×剩余季度/4×0.5）\n部门将变为空缺。";
            _confirmAction = () =>
            {
                DepartmentManager.Instance?.Dismiss(code);
                Refresh();
                OpenCandidates(code);
            };
            _confirmRoot.SetActive(true);
            _confirmRoot.transform.SetAsLastSibling();
        }

        #endregion
    }
}
