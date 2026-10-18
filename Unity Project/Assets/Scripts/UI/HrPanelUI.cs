using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 人员 L1：各部门管理团队编制总览（Football Manager 职员页：职位 + 入职/编制）。
    /// </summary>
    public class HrPanelUI : MonoBehaviour
    {
        GameObject _homeRoot;
        Transform _cardsRoot;

        public static HrPanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_HR2", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<HrPanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public static void OpenHrPage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) { nav.OpenSubPage(pageId); return; }
            var router = NavigationRouter.Instance;
            if (router != null) router.Navigate(pageId, replaceCurrent: false);
            else Debug.LogWarning("[HR] NavigationRouter 未就绪：" + pageId);
        }

        /// <summary>管理团队总览：收起子页宿主，只显示七部门卡片。</summary>
        public void ShowTeamHome()
        {
            if (_homeRoot != null) _homeRoot.SetActive(true);
            ModuleFrame.EnsureSubHost(transform);
            ModuleFrame.ExitSubpage();
            NavigationRouter.Instance?.PopOverlaysKeepBase();
            HrPeopleTabs.SetActive(HrPeopleTabs.Team);
            Refresh();
        }

        /// <summary>职业介绍所 / 人力职能：固定挂在本页 ModuleSubHost，避免挂到航线等其它模块框上。</summary>
        public void ShowPeopleSubpage(string pageId)
        {
            if (_homeRoot != null) _homeRoot.SetActive(false);
            ModuleFrame.EnsureSubHost(transform);
            ModuleFrame.EnterSubpage();
            HrPeopleTabs.SetActiveByPage(pageId);
            var router = NavigationRouter.Instance;
            if (router != null && PageIdMatch.Is(router.CurrentPageId, pageId))
            {
                var existing = router.GetSpawned(router.CurrentPageId);
                AttachToHost(existing);
                return;
            }

            router?.PopOverlaysKeepBase();
            ModuleFrame.EnsureSubHost(transform);
            ModuleFrame.EnterSubpage();
            OpenHrPage(pageId);
            AttachToHost(router != null ? router.GetSpawned(pageId) : null);
            ModuleFrame.EnterSubpage();
        }

        static void AttachToHost(GameObject go)
        {
            if (go == null || ModuleFrame.Body == null) return;
            if (go.transform.parent != ModuleFrame.Body)
                go.transform.SetParent(ModuleFrame.Body, false);
            ModuleFrame.Flatten(go);
            go.SetActive(true);
            go.transform.SetAsLastSibling();
        }

        public void OnOpened()
        {
            if (_homeRoot != null) _homeRoot.SetActive(true);
            SyncTabsFromRouter();
            Refresh();
        }

        void OnEnable()
        {
            var r = NavigationRouter.Instance;
            if (r != null)
            {
                r.Navigated -= OnRouterNavigated;
                r.Navigated += OnRouterNavigated;
                r.Closed -= OnRouterNavigated;
                r.Closed += OnRouterNavigated;
            }
        }

        void OnDisable()
        {
            var r = NavigationRouter.Instance;
            if (r != null)
            {
                r.Navigated -= OnRouterNavigated;
                r.Closed -= OnRouterNavigated;
            }
        }

        void OnRouterNavigated(string _) => SyncTabsFromRouter();

        /// <summary>页签高亮跟随导航栈顶：深链打开 n112/n86 或返回 n18 时同步亮色。</summary>
        void SyncTabsFromRouter()
        {
            var r = NavigationRouter.Instance;
            if (r == null) return;
            string cur = r.CurrentPageId;
            if (PageIdMatch.Is(cur, "n112_职业介绍所")) HrPeopleTabs.SetActive(HrPeopleTabs.Agency);
            else if (PageIdMatch.Is(cur, "n86_组织规划与招聘")) HrPeopleTabs.SetActive(HrPeopleTabs.Functions);
            else if (PageIdMatch.Is(cur, "n18_人力资源部")) HrPeopleTabs.SetActive(HrPeopleTabs.Team);
        }

        public void Refresh()
        {
            PersonnelGenerator.EnsurePool(GameState.Instance);
            DepartmentManager.Instance?.EnsureKeyAppointments();
            RebuildCards();
        }

        void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "人员", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.13f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            HrPeopleTabs.Mount(panel, HrPeopleTabs.Team,
                new Vector2(0.14f, 0.935f), new Vector2(0.72f, 0.995f));

            _homeRoot = UIFactory.Panel(panel, "HrHome",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _homeRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            _cardsRoot = _homeRoot.transform;
        }

        void RebuildCards()
        {
            if (_cardsRoot == null) return;
            for (int i = _cardsRoot.childCount - 1; i >= 0; i--)
                Destroy(_cardsRoot.GetChild(i).gameObject);

            var gs = GameState.Instance;
            var mgr = DepartmentManager.Instance;
            var order = StaffEstablishment.DisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;
                float x0 = 0.01f + col * 0.2475f;
                float y1 = row == 0 ? 0.99f : 0.48f;
                float y0 = row == 0 ? 0.51f : 0.01f;
                BuildDeptCard(_cardsRoot, order[i], x0, y0, x0 + 0.237f, y1, gs, mgr);
            }
        }

        void BuildDeptCard(Transform parent, string code, float x0, float y0, float x1, float y1,
            GameState gs, DepartmentManager mgr)
        {
            var card = UIFactory.Panel(parent, "Card_" + code,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1f, -1f));

            int team = StaffEstablishment.TeamHeadcount(code, gs, mgr);
            string c0 = code;
            var nameBtn = UIFactory.Button(card.transform, "Name", DepartmentCatalog.DisplayName(code) + "  >",
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.01f), () =>
                {
                    HrContext.SelectedDept = c0;
                    OpenHrPage("n113_部门信息");
                }, 15);
            var nameLbl = nameBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (nameLbl != null)
            {
                nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
                nameLbl.fontStyle = FontStyles.Bold;
            }
            UIFactory.Label(card.transform, "Sum", team + " 名管理团队成员", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var roles = StaffEstablishment.Roles(code);
            int slots = 0;
            int filledSlots = 0;
            for (int r = 0; r < roles.Length; r++)
            {
                slots += roles[r].quota;
                filledSlots += Mathf.Min(StaffEstablishment.CountFilled(code, roles[r], gs, mgr), roles[r].quota);
            }
            int iconN = Mathf.Clamp(slots, 1, 10);
            float iconY = 0.70f;
            for (int k = 0; k < iconN; k++)
            {
                float t = (k + 0.5f) / iconN;
                var dot = UIFactory.Panel(card.transform, "I" + k,
                    new UIFactory.Anchor(new Vector2(t - 0.04f, iconY), new Vector2(t + 0.04f, iconY + 0.08f),
                        Vector2.zero, Vector2.zero),
                    k < filledSlots ? UITheme.Accent : new Color(0.25f, 0.35f, 0.48f, 0.55f));
                var im = dot.GetComponent<Image>();
                im.sprite = UIFactory.CircleSprite;
                im.type = Image.Type.Simple;
                im.raycastTarget = false;
            }

            int lines = roles.Length;
            for (int r = 0; r < lines; r++)
            {
                var role = roles[r];
                int filled = StaffEstablishment.CountFilled(code, role, gs, mgr);
                float ly1 = 0.66f - r * (0.54f / Mathf.Max(1, lines));
                float ly0 = ly1 - (0.50f / Mathf.Max(1, lines));
                Color c = filled > role.quota ? UITheme.Warning
                    : filled == 0 && role.quota > 0 ? UITheme.TextMuted
                    : UITheme.TextPrimary;
                string extra = "";
                var pipe = StaffRecruitment.FindOpen(gs, code, role.jobTitle);
                if (pipe != null && filled < role.quota)
                {
                    extra = " · " + StaffRecruitment.StageLabel(pipe);
                    if (pipe.stage == "pending" || pipe.stage == "interview") c = UITheme.Accent;
                }
                UIFactory.Label(card.transform, "L" + r, $"{filled}/{role.quota}  {StaffEstablishment.DisplayTitle(role.jobTitle, role.rank)}{extra}", 12,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.08f, ly0), new Vector2(0.94f, ly1), Vector2.zero, Vector2.zero),
                    c);
            }
        }
    }
}
