using AirlineCEO.UI.Navigation;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>人员 L1 三个并列子标签：管理团队 / 职业介绍所 / 人力职能。</summary>
    public static class HrPeopleTabs
    {
        public const int Team = 0;
        public const int Agency = 1;
        public const int Functions = 2;

        static readonly string[] Labels = { "管理团队", "职业介绍所", "人力职能" };

        static readonly Button[] _btns = new Button[3];
        static int _active = Team;

        public static void Mount(Transform parent, int active,
            Vector2? min = null, Vector2? max = null)
        {
            if (parent == null) return;
            _active = Mathf.Clamp(active, 0, 2);
            var bar = UIFactory.Panel(parent, "PeopleTabs",
                new UIFactory.Anchor(
                    min ?? new Vector2(0.14f, 0.91f),
                    max ?? new Vector2(0.78f, 0.99f),
                    Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            var img = bar.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float x0 = i / 3f;
                float x1 = (i + 1) / 3f;
                _btns[i] = UIFactory.Button(bar.transform, "T" + i, Labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.01f, 0.08f), new Vector2(x1 - 0.01f, 0.92f),
                        Vector2.zero, Vector2.zero),
                    i == _active ? UITheme.NavActive : UITheme.NavIdle, () => Select(idx), 13);
                // 选中/未选中亮色完全由 Paint() 控制；悬停反馈由 UIHoverBrighten 提供，
                // 关掉 Button 自带 ColorTint，避免状态机残留 tint 把亮色冲淡。
                _btns[i].transition = Selectable.Transition.None;
            }
            Paint();
        }

        public static void Select(int tab)
        {
            tab = Mathf.Clamp(tab, 0, 2);
            var hr = Object.FindFirstObjectByType<HrPanelUI>();
            if (tab == Team)
            {
                hr?.ShowTeamHome();
                SetActive(Team);
                return;
            }

            string page = tab == Agency ? "n112_职业介绍所" : "n86_组织规划与招聘";
            hr?.ShowPeopleSubpage(page);
            SetActive(tab);
        }

        /// <summary>外部入口（收件箱深链 / 终端导航等）打开子页后同步页签高亮。</summary>
        public static void SetActive(int tab)
        {
            _active = Mathf.Clamp(tab, 0, 2);
            Paint();
        }

        /// <summary>按页面 id 同步高亮（n112 职业介绍所 / n86 人力职能，其余视为管理团队）。</summary>
        public static void SetActiveByPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            if (pageId.StartsWith("n112")) SetActive(Agency);
            else if (pageId.StartsWith("n86")) SetActive(Functions);
        }

        static void Paint()
        {
            for (int i = 0; i < _btns.Length; i++)
            {
                if (_btns[i] == null) continue;
                var img = _btns[i].GetComponent<Image>();
                if (img != null) img.color = i == _active ? UITheme.NavActive : UITheme.NavIdle;
            }
        }
    }
}
