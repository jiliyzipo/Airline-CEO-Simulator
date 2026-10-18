using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Shell;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AirlineCEO.UI.Setup
{
    /// <summary>
    /// Phase 1.4：开局闸门 n1→n5，完成后进入壳层玩法。
    /// 对照白板：新游戏走资料流；继续/读档进主页；设置/学院可开。
    /// </summary>
    public class SetupFlowController : MonoBehaviour
    {
        public static SetupFlowController Instance { get; private set; }

        /// <summary>PlayMode / 自动化测试跳过主菜单。</summary>
        public static bool BypassForTests;

        public SetupDraft Draft { get; private set; } = new SetupDraft();
        public bool IsInGameplay { get; private set; }

        GameObject _overlay;
        GameObject _pageRoot;
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        Transform _formRoot;
        MVPBootstrap _bootstrap;
        PrefabNavIntegration _nav;
        System.Collections.Generic.List<BoardMemberData> _boardBatch;
        System.Collections.Generic.HashSet<string> _selectedIds = new System.Collections.Generic.HashSet<string>();
        string _selectedCapsuleId = "";
        int _overviewTab;
        AirlineProfile _takeoverPreview;
        /// <summary>
        /// 接管航司页的展开焦点（大洲 / 国家 / 城市）。初始一律为空 = 全部收起：
        /// 六个大洲各占一行正好铺满一屏，先让玩家看到「有哪些洲、各多少家」，
        /// 而不是一进来就把亚洲 42 家摊开、把其余五个洲挤到屏幕外。
        /// </summary>
        string _alContinent = "";
        string _alCountry = "";
        string _alCity = "";
        /// <summary>接管航司页的滚动内容：跨重绘复用，避免 ScrollRect 重建导致滚动位置回到顶部。</summary>
        RectTransform _alContent;
        ScrollRect _alScroll;
        /// <summary>主菜单版面（FM 式两栏）：只在主菜单存活，切页时随 ClearForm 销毁。</summary>
        MainMenuView _mmView;

        /// <summary>董事会共 9 席（含玩家 CEO 1 席），玩家从候选池选出 8 位董事。</summary>
        const int BoardSeatCount = 8;

        void Awake()
        {
            Instance = this;
            _bootstrap = GetComponent<MVPBootstrap>() ?? FindFirstObjectByType<MVPBootstrap>();
            _nav = GetComponent<PrefabNavIntegration>() ?? FindFirstObjectByType<PrefabNavIntegration>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Begin()
        {
            if (BypassForTests)
            {
                EnterGameplay(fromContinue: false, skipAoIntro: true);
                return;
            }

            EnsureOverlay();
            ShowMainMenu();
        }

        public void ForceEnterGameplayForTests()
        {
            BypassForTests = true;
            EnterGameplay(fromContinue: false, skipAoIntro: true);
        }

        void EnsureOverlay()
        {
            if (_overlay != null)
            {
                // 已建：每次进向导页都要重新压回最上层。叠加层此前只在创建那一刻 SetAsLastSibling 过一次，
                // 而壳层的 RaiseChrome()（PrefabNavIntegration）会在导航过程中把顶栏等 chrome 上浮 ——
                // 于是「开始界面顶栏浮在主菜单之上」。向导是全屏闸门，醒着的时候必须在最上面。
                _overlay.transform.SetAsLastSibling();
                return;
            }

            var canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Setup] 无 Canvas");
                return;
            }

            _overlay = UIFactory.Panel(canvas.transform, "SetupOverlay", UIFactory.Fill,
                new Color(0.04f, 0.08f, 0.14f, 0.98f), rounded: false);
            _overlay.transform.SetAsLastSibling();

            var card = UIFactory.Panel(_overlay.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1f, -1f));

            _title = UIFactory.Label(card.transform, "Title", "主菜单", 26,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _body = UIFactory.Label(card.transform, "Body", "", 15,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.72f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var formGo = UIFactory.Panel(card.transform, "Form",
                new UIFactory.Anchor(new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.70f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.12f));
            _formRoot = formGo.transform;
            _pageRoot = card;
        }

        void SetHint(string text)
        {
            if (_body != null) _body.text = text ?? "";
        }

        void ClearForm()
        {
            // 主菜单版面（FM 式两栏，挂在 overlay 上而非表单卡内）随页面切换一并销毁，
            // 并把通用表单卡恢复显示 —— 主菜单是唯一不用那张卡的页面。
            if (_mmView != null)
            {
                if (_mmView.Root != null) Destroy(_mmView.Root);
                _mmView = null;
            }
            if (_pageRoot != null) _pageRoot.SetActive(true);

            // 表单被清空 → 接管航司页的滚动区域随之失效，下次进入时重建。
            _alContent = null;
            _alScroll = null;
            if (_formRoot == null) return;
            for (int i = _formRoot.childCount - 1; i >= 0; i--)
                Destroy(_formRoot.GetChild(i).gameObject);
        }

        void ClearActions(Transform card)
        {
            var old = card.Find("Actions");
            if (old != null) Destroy(old.gameObject);
        }

        Transform EnsureActions()
        {
            ClearActions(_pageRoot.transform);
            var actions = UIFactory.Panel(_pageRoot.transform, "Actions",
                new UIFactory.Anchor(new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.16f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            actions.GetComponent<Image>().raycastTarget = false;
            return actions.transform;
        }

        void AddAction(Transform actions, string name, string label, float x0, float x1, Color color, UnityEngine.Events.UnityAction onClick)
        {
            UIFactory.Button(actions, name, label,
                new UIFactory.Anchor(new Vector2(x0, 0.15f), new Vector2(x1, 0.85f), Vector2.zero, Vector2.zero),
                color, onClick, 16);
        }

        public void ShowMainMenu()
        {
            EnsureOverlay();
            if (_overlay == null) return;             // 无 Canvas：EnsureOverlay 已打错误日志
            _overlay.SetActive(true);
            ClearForm();                              // 顺带销毁上一轮主菜单版面
            // 主菜单不走「标题 + 说明 + 一排横按钮」的通用表单卡，改用 FM 式两栏版面（见 MainMenuUI）。
            if (_pageRoot != null)
            {
                ClearActions(_pageRoot.transform);    // 清掉上一页遗留的底部按钮行
                _pageRoot.SetActive(false);
            }

            bool hasSave = SaveManager.TryPeekSummary(out string companyZh, out int year, out int quarter,
                out float cashWan, out float satisfaction, out bool gameOver);

            var save = new SaveCardInfo
            {
                HasSave = hasSave,
                Icon = hasSave ? "存" : "新",
                Title = hasSave
                    ? (string.IsNullOrEmpty(companyZh) ? "已存档航司" : companyZh)
                    : "尚无存档",
                Sub = hasSave
                    ? $"财年 {year} · Q{quarter} · 现金 {cashWan:F0} 万 · 董事会 {satisfaction:F0}"
                      + (gameOver ? " · 本局已结束" : "")
                    : "「新游戏 · 接管」开一局；局内 F5 存档 / F9 读档"
            };

            var entries = new System.Collections.Generic.List<MenuEntry>
            {
                new MenuEntry
                {
                    Name = "New", Label = "新游戏 · 接管", Color = UITheme.Accent,
                    OnClick = () => { Draft.FromScratch = false; ShowPlayerProfile(); }
                },
                new MenuEntry
                {
                    Name = "Scratch", Label = "从零创建",
                    OnClick = () => { Draft.FromScratch = true; ShowPlayerProfile(); }
                },
                new MenuEntry
                {
                    // 无存档时置灰（FM 同款处理）：不再让玩家点进一个必然失败的入口
                    Name = "Continue", Label = "继续 / 读档",
                    Color = hasSave ? UITheme.Success : UITheme.PanelDeep,
                    Interactable = hasSave,
                    OnClick = () =>
                    {
                        if (SaveManager.Instance == null)
                        {
                            _mmView?.SetHint("存档组件未就绪，无法读档。");
                            return;
                        }
                        SaveManager.Instance.Load();
                        EnterGameplay(fromContinue: true, skipAoIntro: true);
                    }
                },
                new MenuEntry { Name = "Archive", Label = "历史回放", OnClick = ShowArchivePanel }
            };

            _mmView = MainMenuUI.Build(_overlay.transform, save, entries,
                onSettings: () => OpenShellPage("n40_设置_存档"),
                onQuit: QuitGame,
                hint: hasSave
                    ? "「历史回放」可回溯各财年末的时间胶囊，或创建 What-If 分支（不影响主线）。"
                    : "尚无存档：请先「新游戏 · 接管」；「从零创建」为白手起家方案。");
        }

        /// <summary>主菜单「历史回放」：时间胶囊时间轴 + What-If 分支 + 返回主线。</summary>
        void ShowArchivePanel()
        {
            EnsureOverlay();
            _overlay.SetActive(true);
            ClearForm();
            CloseSetupPicker();
            _title.text = "历史回放 · 时间胶囊";
            _selectedCapsuleId = "";

            var capsules = ArchiveManager.ListCapsules();
            bool hasMainlineBackup = SaveSlotManager.SlotExists(ArchiveManager.MainlineBackupSlot);

            var sb = new StringBuilder();
            sb.AppendLine("每财年末自动生成时间胶囊，可回溯复盘或创建 What-If 分支（独立于主线，不影响当前进度）。");
            sb.AppendLine(capsules.Count == 0
                ? "尚无时间胶囊（推进到第一个财年末自动生成）。"
                : $"共 {capsules.Count} 个胶囊，最新在前。");
            _body.text = sb.ToString();

            int show = Mathf.Min(capsules.Count, 6);
            for (int i = 0; i < show; i++)
            {
                var c = capsules[capsules.Count - 1 - i]; // 倒序（新→旧）
                float rowH = 1f / 6f;
                float y1 = 1f - i * rowH;
                float y0 = y1 - rowH * 0.88f;
                bool selected = _selectedCapsuleId == c.id;
                string capId = c.id;
                string label = $"{c.dateLabel} · 现金 {c.cashWan:F0} 万 · 市值 {c.marketCapWan:F0} 万 · 目标 {c.targetsSummary} · 安全 {c.safetyGrade}";
                UIFactory.Button(_formRoot, "Cap_" + i, label,
                    new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero),
                    selected ? UITheme.Accent : UITheme.PanelDeep,
                    () => { _selectedCapsuleId = capId; ShowArchivePanel(); }, 12);
            }

            bool canAct = !string.IsNullOrEmpty(_selectedCapsuleId);
            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回主菜单", 0.00f, 0.19f, UITheme.PanelDeep, ShowMainMenu);
            AddAction(actions, "Branch", "创建 What-If 分支", 0.20f, 0.43f,
                canAct ? UITheme.Accent : UITheme.PanelDeep,
                () =>
                {
                    if (!canAct) { SetHint("请先点击选择一个时间胶囊。"); return; }
                    var branch = ArchiveManager.CreateBranch(GameState.Instance, _selectedCapsuleId);
                    if (branch != null) EnterGameplay(fromContinue: true, skipAoIntro: true);
                });
            AddAction(actions, "Replay", "回溯到此节点", 0.44f, 0.62f,
                canAct ? UITheme.Success : UITheme.PanelDeep,
                () =>
                {
                    if (!canAct) { SetHint("请先点击选择一个时间胶囊。"); return; }
                    ArchiveManager.LoadCapsuleIntoMain(_selectedCapsuleId);
                    EnterGameplay(fromContinue: true, skipAoIntro: true);
                });
            AddAction(actions, "Mainline", "返回主线", 0.63f, 0.81f,
                hasMainlineBackup ? UITheme.Success : UITheme.PanelDeep,
                () =>
                {
                    if (!ArchiveManager.ReturnToMainline()) { SetHint("无主线备份可返回（仅创建分支后可用）。"); return; }
                    EnterGameplay(fromContinue: true, skipAoIntro: true);
                });
            AddAction(actions, "Quit", "退出", 0.82f, 1.00f, UITheme.Danger, QuitGame);
        }

        void ShowPlayerProfile()
        {
            ClearForm();
            CloseSetupPicker();
            _title.text = "玩家资料创建";
            SetHint("填写 CEO 档案。接管开局下一步选择航司；从零创建则进入公司注册。");

            var name = SetupFormWidgets.AddLabeledInput(_formRoot, "姓名",
                string.IsNullOrEmpty(Draft.PlayerName) ? "请输入…" : Draft.PlayerName, 0, 7,
                TMP_InputField.ContentType.Standard, latinOnly: false);
            if (name != null) name.characterLimit = 24;

            int genderIdx = System.Array.IndexOf(SetupCatalogs.Genders, Draft.Gender);
            if (genderIdx < 0) genderIdx = 0;
            var gender = SetupFormWidgets.AddCycleSelect(_formRoot, "性别", SetupCatalogs.Genders, genderIdx, 1, 7);

            var years = SetupCatalogs.BuildBirthYears();
            var yearLabels = new string[years.Length];
            int yearIdx = 0;
            for (int i = 0; i < years.Length; i++)
            {
                yearLabels[i] = $"{years[i]} 年（{SetupCatalogs.AgeFromBirthYear(years[i])} 岁）";
                if (years[i] == Draft.BirthYear) yearIdx = i;
            }
            var birth = SetupFormWidgets.AddListSelect(_formRoot, "出生年份", yearLabels, yearIdx, 2, 7, SetHint);

            int nationIdx = System.Array.IndexOf(SetupCatalogs.Countries, Draft.Nationality);
            if (nationIdx < 0) nationIdx = 0;
            var nation = SetupFormWidgets.AddListSelect(_formRoot, "国籍", SetupCatalogs.Countries, nationIdx, 3, 7, SetHint);

            int eduIdx = System.Array.IndexOf(SetupCatalogs.Educations, Draft.Education);
            if (eduIdx < 0) eduIdx = 0;
            var edu = SetupFormWidgets.AddCycleSelect(_formRoot, "教育", SetupCatalogs.Educations, eduIdx, 4, 7);

            int carIdx = System.Array.IndexOf(SetupCatalogs.Careers, Draft.Career);
            if (carIdx < 0) carIdx = 0;
            var career = SetupFormWidgets.AddCycleSelect(_formRoot, "经历", SetupCatalogs.Careers, carIdx, 5, 7);

            int trIdx = System.Array.IndexOf(SetupCatalogs.Traits, Draft.Trait);
            if (trIdx < 0) trIdx = 0;
            var trait = SetupFormWidgets.AddCycleSelect(_formRoot, "特质", SetupCatalogs.Traits, trIdx, 6, 7);

            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回", 0.00f, 0.28f, UITheme.PanelDeep, ShowMainMenu);
            AddAction(actions, "Next", "下一步 →", 0.72f, 1.00f, UITheme.Accent, () =>
            {
                string n = name != null ? name.text.Trim() : "";
                if (string.IsNullOrEmpty(n))
                {
                    SetHint("请输入姓名。");
                    return;
                }
                Draft.PlayerName = n;
                Draft.Gender = gender.Current;
                if (birth.Index >= 0 && birth.Index < years.Length)
                    Draft.BirthYear = years[birth.Index];
                Draft.Nationality = nation.Current;
                Draft.Education = edu.Current;
                Draft.Career = career.Current;
                Draft.Trait = trait.Current;
                if (Draft.FromScratch) ShowCompanyRegister();
                else ShowAirlineSelect();
            });
        }

        void ShowAirlineSelect()
        {
            // 本页在「点选航司 / 展开节点」后会整表重绘，因此滚动区域只在首次进入时建立，
            // 之后沿用同一个 ScrollRect：若每轮重绘都连它一起销毁重建，
            // 归一化位置会回到 1，表现为「点击任意行后列表跳回顶部」。
            if (_alContent == null)
            {
                ClearForm();
                _alContent = AirlineSelectionUI.CreateArea(_formRoot, out _alScroll);
            }
            // 记住「距顶部像素偏移」而不是 verticalNormalizedPosition：列表不足一屏时
            // ScrollRect 的归一化值会退化成 0（语义上等于「底部」），直接存它会在
            // 「展开第一个大洲、列表刚变长」的一瞬间把视图甩到列表末尾。
            float keepTop = _alContent != null ? _alContent.anchoredPosition.y : 0f;

            CloseSetupPicker();
            _title.text = "选择航司";
            SetHint("按大洲 → 国家 → 城市展开，点选航司后查看机队、航线、财务与遗留问题。");
            AirlineSelectionUI.Draw(_alContent, Draft.TakeoverAirlineId,
                _alContinent, _alCountry, _alCity,
                id =>
                {
                    Draft.TakeoverAirlineId = id;
                    ShowAirlineSelect();
                },
                (continent, country, city) =>
                {
                    _alContinent = continent;
                    _alCountry = country;
                    _alCity = city;
                    ShowAirlineSelect();
                });
            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回", 0.00f, 0.28f, UITheme.PanelDeep, ShowPlayerProfile);
            AddAction(actions, "Next", "查看现状 →", 0.62f, 1.00f, UITheme.Accent, () =>
            {
                if (string.IsNullOrEmpty(Draft.TakeoverAirlineId))
                    Draft.TakeoverAirlineId = PresetAirlines.Regional;
                _overviewTab = 6;
                ShowAirlineOverview();
            });

            // 放在最后：列表与按钮都建完之后再复位，避免后续新建控件触发画布重建时插一脚。
            RestoreAirlineScroll(keepTop);
        }

        /// <summary>
        /// 按「距顶部像素偏移」还原接管航司列表的滚动位置；列表不足一屏则停在顶部。
        /// 只应在列表重绘之后调用（此时内容高度已由 SetScrollContentHeight 更新）。
        /// </summary>
        void RestoreAirlineScroll(float topOffset)
        {
            if (_alScroll == null || _alContent == null) return;
            float viewH = _alScroll.viewport != null ? _alScroll.viewport.rect.height : 0f;
            _alScroll.verticalNormalizedPosition =
                AirlineSelectionUI.NormalizedFor(topOffset, _alContent.rect.height, viewH);
        }

        void ShowAirlineOverview()
        {
            ClearForm();
            CloseSetupPicker();
            _takeoverPreview = TakeoverWorldBuilder.BuildPreview(Draft.TakeoverAirlineId);
            var p = _takeoverPreview;
            _title.text = "航司现状 · 接管协议";
            SetHint(AirlineOverviewUI.Header(p) + "\n" + AirlineOverviewUI.Metrics(p));

            float tabW = 1f / AirlineOverviewUI.Tabs.Length;
            for (int i = 0; i < AirlineOverviewUI.Tabs.Length; i++)
            {
                int idx = i;
                float x0 = i * tabW;
                UIFactory.Button(_formRoot, "T" + i, AirlineOverviewUI.Tabs[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.005f, 0.86f), new Vector2(x0 + tabW - 0.005f, 0.98f), Vector2.zero, Vector2.zero),
                    idx == _overviewTab ? UITheme.Accent : UITheme.PanelDeep,
                    () => { _overviewTab = idx; ShowAirlineOverview(); }, 11);
            }

            var body = UIFactory.Label(_formRoot, "OvBody", AirlineOverviewUI.Body(p, _overviewTab), 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.84f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Truncate;

            var actions = EnsureActions();
            AddAction(actions, "Back", "← 重选", 0.00f, 0.28f, UITheme.PanelDeep, ShowAirlineSelect);
            AddAction(actions, "Go", "确认接管", 0.55f, 1.00f, UITheme.Success, ConfirmTakeover);
        }

        void ConfirmTakeover()
        {
            var p = _takeoverPreview ?? TakeoverWorldBuilder.BuildPreview(Draft.TakeoverAirlineId);
            Draft.CompanyNameZh = p.nameCN;
            Draft.CompanyNameEn = p.nameEN;
            Draft.IataCode = p.iataCode;
            Draft.IcaoCode = p.icaoCode;
            Draft.HubIata = p.hubAirport;
            Draft.InitialCashWan = p.cash;
            _selectedIds.Clear();
            if (p.boardMemberIds != null)
            {
                for (int i = 0; i < p.boardMemberIds.Count; i++)
                    _selectedIds.Add(p.boardMemberIds[i]);
            }
            EnterGameplay(fromContinue: false, skipAoIntro: false);
        }

        void ShowCompanyRegister()
        {
            ClearForm();
            CloseSetupPicker();
            _title.text = "公司注册";
            SetHint("公司中文名仅可输入汉字；IATA 为两位字母或字母与数字组合；ICAO代码为三位字母。");

            const int rows = 6;
            var zh = SetupFormWidgets.AddLabeledInput(_formRoot, "公司中文名",
                string.IsNullOrEmpty(Draft.CompanyNameZh) ? "请输入…" : Draft.CompanyNameZh, 0, rows,
                TMP_InputField.ContentType.Standard, latinOnly: false, chineseOnly: true);
            var en = SetupFormWidgets.AddLabeledInput(_formRoot, "公司英文名",
                string.IsNullOrEmpty(Draft.CompanyNameEn) ? "请输入…" : Draft.CompanyNameEn, 1, rows,
                TMP_InputField.ContentType.Standard, latinOnly: true);

            var iata = SetupFormWidgets.AddLabeledInput(_formRoot, "IATA代码",
                string.IsNullOrEmpty(Draft.IataCode) ? "请输入…" : Draft.IataCode, 2, rows);
            iata.characterLimit = 2;
            iata.onValueChanged.AddListener(v =>
            {
                string u = SanitizeAirlineCode(v, 2);
                if (u != v) iata.SetTextWithoutNotify(u);
            });

            var icao = SetupFormWidgets.AddLabeledInput(_formRoot, "ICAO代码",
                string.IsNullOrEmpty(Draft.IcaoCode) ? "请输入…" : Draft.IcaoCode, 3, rows);
            icao.characterLimit = 3;
            icao.onValueChanged.AddListener(v =>
            {
                // ICAO 仅字母
                string u = (v ?? "").Replace(" ", "").ToUpperInvariant();
                var sb = new StringBuilder(3);
                for (int i = 0; i < u.Length && sb.Length < 3; i++)
                {
                    char c = u[i];
                    if (c >= 'A' && c <= 'Z') sb.Append(c);
                }
                string cleaned = sb.ToString();
                if (cleaned != v) icao.SetTextWithoutNotify(cleaned);
            });

            var hub = SetupFormWidgets.AddAirportSelect(_formRoot, "总部基地", Draft.HubIata, 4, rows, SetHint);

            int cashIdx = 1;
            for (int i = 0; i < SetupCatalogs.CashTiersWan.Length; i++)
                if (Mathf.Approximately(SetupCatalogs.CashTiersWan[i], Draft.InitialCashWan)) cashIdx = i;
            var cash = SetupFormWidgets.AddCycleSelect(_formRoot, "启动资金", SetupCatalogs.CashTierLabels, cashIdx, 5, rows);

            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回", 0.00f, 0.28f, UITheme.PanelDeep, ShowPlayerProfile);
            AddAction(actions, "Next", "确认注册 →", 0.72f, 1.00f, UITheme.Accent, () =>
            {
                string zhName = zh != null ? zh.text.Trim() : "";
                if (string.IsNullOrWhiteSpace(zhName))
                {
                    SetHint("请填写公司中文名。");
                    return;
                }
                if (!SetupFormWidgets.IsChineseCompanyName(zhName))
                {
                    SetHint("公司中文名须为汉字（至少 2 个字，可含 ·）。");
                    return;
                }
                string enName = en != null ? en.text.Trim() : "";
                if (string.IsNullOrEmpty(enName))
                {
                    SetHint("请填写公司英文名。");
                    return;
                }
                if (!SetupFormWidgets.IsLatinCompanyName(enName))
                {
                    SetHint("公司英文名须为字母开头（可含数字 / 空格 / -）。");
                    return;
                }
                string iataCode = iata != null ? iata.text.Trim().ToUpperInvariant() : "";
                if (!SetupFormWidgets.IsIata(iataCode))
                {
                    SetHint("IATA代码须为两位字母，或字母与数字组合（如 CA、U2、3U）。");
                    return;
                }
                string code = icao != null ? icao.text.Trim().ToUpperInvariant() : "";
                if (!SetupFormWidgets.IsIcao(code))
                {
                    SetHint("ICAO代码限定三个字母。");
                    return;
                }
                Draft.CompanyNameZh = zhName;
                Draft.CompanyNameEn = enName;
                Draft.IataCode = iataCode;
                Draft.IcaoCode = code;
                Draft.HubIata = string.IsNullOrEmpty(hub.Code) ? Draft.HubIata : hub.Code.ToUpperInvariant();
                Draft.InitialCashWan = SetupCatalogs.CashTiersWan[Mathf.Clamp(cash.Index, 0, SetupCatalogs.CashTiersWan.Length - 1)];
                ShowBoardDraft();
            });
        }

        static string SanitizeAirlineCode(string raw, int maxLen)
        {
            string u = (raw ?? "").Replace(" ", "").ToUpperInvariant();
            var sb = new StringBuilder(maxLen);
            for (int i = 0; i < u.Length && sb.Length < maxLen; i++)
            {
                char c = u[i];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        void ShowBoardDraft()
        {
            ClearForm();
            CloseSetupPicker();
            _title.text = "董事会组建（抽卡选座）";
            SetHint($"点击候选卡选中 / 取消。董事会共 9 席（含 CEO 1 席），需选出 {BoardSeatCount} 位董事。");
            _selectedIds.Clear();
            RefreshBoardBatchUi();

            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回", 0.00f, 0.22f, UITheme.PanelDeep, ShowCompanyRegister);
            AddAction(actions, "Reroll", "换一批候选人", 0.24f, 0.62f, UITheme.PanelDeep, () =>
            {
                Draft.BoardBatchSeed++;
                _selectedIds.Clear();
                RefreshBoardBatchUi();
                SetHint("已换一批候选人。点击卡片选中，选满 8 位董事。");
            });
            AddAction(actions, "Next", "确认本届董事会 →", 0.64f, 1.00f, UITheme.Accent, () =>
            {
                if (_selectedIds.Count < BoardSeatCount)
                {
                    SetHint($"还需选出 {BoardSeatCount - _selectedIds.Count} 位董事（已选 {_selectedIds.Count}/{BoardSeatCount}）。");
                    return;
                }
                ShowAnnualGoals();
            });
        }

        void RefreshBoardBatchUi()
        {
            ClearForm();
            var db = DataBase.Instance;
            _boardBatch = SetupCatalogs.PickBoardBatch(db?.BoardMembers, Draft.BoardBatchSeed, 12);

            // 右侧详情
            var detail = UIFactory.Label(_formRoot, "AgendaDetail", "", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.48f, 0.04f), new Vector2(0.98f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            detail.textWrappingMode = TextWrappingModes.Normal;

            // 左侧候选卡（12 人，可点选中）
            int n = _boardBatch.Count;
            for (int i = 0; i < n; i++)
            {
                var m = _boardBatch[i];
                if (m == null) continue;
                bool selected = _selectedIds.Contains(m.id);
                float rowH = 1f / Mathf.Max(1, n);
                float y1 = 1f - i * rowH;
                float y0 = y1 - rowH * 0.92f;

                var card = UIFactory.Button(_formRoot, "Dir_" + m.id,
                    $"{m.fullName}\n{SetupCatalogs.ProfileFor(m).age} 岁 · {SetupCatalogs.ProfileFor(m).nationality}",
                    new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.46f, y1), Vector2.zero, Vector2.zero),
                    selected ? new Color(0.16f, 0.30f, 0.52f, 1f) : UITheme.PanelDeep, () =>
                    {
                        ToggleSeat(m);
                    }, 11);
                if (selected)
                    UIFactory.AddBorder(card.gameObject, UITheme.Accent, new Vector2(1.4f, -1.4f));

                // 左侧头像色块（姓名首字）
                var avatar = UIFactory.Panel(card.transform, "Avatar",
                    new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(6f, -14f), new Vector2(34f, 14f)),
                    AvatarColor(m.id));
                var avImg = avatar.GetComponent<UnityEngine.UI.Image>();
                avImg.sprite = UIFactory.CircleSprite;
                avImg.type = UnityEngine.UI.Image.Type.Simple;
                avImg.raycastTarget = false;
                UIFactory.Label(avatar.transform, "Glyph",
                    string.IsNullOrEmpty(m.fullName) ? "?" : m.fullName.Substring(0, 1), 14,
                    TMPro.TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary,
                    TMPro.FontStyles.Bold);
            }

            if (n > 0)
            {
                detail.text = SetupCatalogs.DetailFor(_boardBatch[0]);
                SetHint($"点击候选卡选中 / 取消（已选 {_selectedIds.Count}/{BoardSeatCount}）。派系与化学组合标签已隐藏。");
            }
            else
            {
                detail.text = "董事库未加载。将使用默认董事会进入下一页。";
                SetHint("");
            }
        }

        void ToggleSeat(BoardMemberData m)
        {
            if (m == null) return;
            if (_selectedIds.Contains(m.id))
            {
                _selectedIds.Remove(m.id);
            }
            else if (_selectedIds.Count >= BoardSeatCount)
            {
                SetHint($"已选满 {BoardSeatCount} 席，请先取消某位董事再选择。");
                return;
            }
            else
            {
                _selectedIds.Add(m.id);
            }
            RefreshBoardBatchUi();
            SetHint($"已选 {_selectedIds.Count}/{BoardSeatCount} 位董事。点击候选卡选中 / 取消。");
        }

        static UnityEngine.Color AvatarColor(string id)
        {
            // 按 id 稳定取色（头像色块）
            int h = 0;
            if (!string.IsNullOrEmpty(id)) h = id[0] * 47 + id.Length * 13;
            float hue = (h % 12) / 12f;
            return UnityEngine.Color.HSVToRGB(hue, 0.55f, 0.85f);
        }

        void CloseSetupPicker()
        {
            if (_pageRoot == null) return;
            var old = _pageRoot.transform.Find("SetupPicker");
            if (old != null) Destroy(old.gameObject);
        }

        void ShowAnnualGoals()
        {
            ClearForm();
            _title.text = "年度目标确认";

            if (BoardManager.Instance != null)
                BoardManager.Instance.EnsureAnnualTargets();

            var gs = GameState.Instance;
            var sb = new StringBuilder();
            sb.AppendLine("董事会下达本财年考核。确认后进入主页，可通过顶部「顾问」获得引导。\n");
            if (gs?.currentTargets != null)
            {
                for (int i = 0; i < gs.currentTargets.Count && i < 8; i++)
                {
                    var t = gs.currentTargets[i];
                    if (t == null) continue;
                    sb.AppendLine($"· {t.title}（目标 {BoardTarget.FormatValue(t.threshold)}{t.unit}）");
                }
            }
            _body.text = sb.ToString();

            var actions = EnsureActions();
            AddAction(actions, "Back", "← 返回", 0.00f, 0.28f, UITheme.PanelDeep, ShowBoardDraft);
            AddAction(actions, "Start", "开始经营", 0.55f, 1.00f, UITheme.Success,
                () =>
                {
                    BoardManager.Instance?.ConfirmAnnualGoals();
                    EnterGameplay(fromContinue: false, skipAoIntro: false);
                });
        }

        // legacy AddField removed — use SetupFormWidgets

        static string ReadField(TMP_InputField field, string fallback)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.text)) return fallback;
            return field.text.Trim();
        }

        void EnterGameplay(bool fromContinue, bool skipAoIntro)
        {
            // skipAoIntro 保留兼容旧调用；顾问弹窗改为玩家手动打开
            _ = skipAoIntro;
            IsInGameplay = true;
            if (_overlay != null) _overlay.SetActive(false);

            var gs = GameState.Instance;
            if (gs != null && !fromContinue)
            {
                gs.companyNameZh = Draft.CompanyNameZh;
                gs.companyNameEn = Draft.CompanyNameEn;
                gs.ceoDisplayName = Draft.PlayerName;
                gs.companyIata = Draft.IataCode;
                gs.companyIcao = Draft.IcaoCode;
                gs.hubIata = Draft.HubIata;
                gs.ceoNationality = Draft.Nationality;
                gs.ceoGender = Draft.Gender;
                gs.ceoContractStartYear = gs.year > 0 ? gs.year : 1;
                gs.ceoContractYears = 4;
                gs.EnsureCeoContractDefaults();
                gs.cash = Draft.InitialCashWan;
                gs.difficulty = Draft.Difficulty;
                gs.ceoBirthYear = Draft.BirthYear;
                gs.ceoEducation = Draft.Education;
                gs.ceoCareer = Draft.Career;
                gs.ceoTrait = Draft.Trait;
                gs.activeMode = Draft.Mode;
                gs.legendModeActive = Draft.Mode == (int)GameMode.Legend;
                gs.fastModeActive = Draft.Mode == (int)GameMode.Fast;
                gs.ironmanModeActive = Draft.Ironman;

                // 模式效果（GDB 第4章 / 第31章）
                if (Draft.Mode == (int)GameMode.Legend)
                {
                    gs.difficulty = DifficultyMode.Hard;
                }
                else if (Draft.Mode == (int)GameMode.NewGamePlus)
                {
                    gs.brandAwareness = Mathf.Clamp(gs.brandAwareness + 10f, 0f, 100f);
                    gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + 10f, 0f, 100f);
                }

                gs.advisorTutorialStep = 0;
                gs.advisorTutorialDeferred = false;
                gs.advisorTutorialCompleted = false;
                gs.advisorTutorialAwaitPage = 0;
                gs.boardMemberIds = new System.Collections.Generic.List<string>(_selectedIds);
                gs.speedLevel = GameSpeed.L1;
                gs.simDay = 1;
                gs.AlignClockToRealWorld();
                gs.AddDecisionLog($"开局：{Draft.CompanyNameZh}（{Draft.IataCode}/{Draft.IcaoCode}）· CEO {Draft.PlayerName} · 总部基地 {Draft.HubIata} · 董事会 {_selectedIds.Count} 席");
                gs.staffPool = new System.Collections.Generic.List<AirlineCEO.Data.PersonnelData>();
                gs.staffRecruits = new System.Collections.Generic.List<AirlineCEO.Data.StaffRecruitCase>();
                gs.staffBootstrapped = false;
                gs.inbox = new System.Collections.Generic.List<AirlineCEO.Data.InboxMail>();
                gs.pendingInbox = new System.Collections.Generic.List<AirlineCEO.Data.PendingInboxMail>();
                if (DepartmentManager.Instance == null)
                {
                    var go = new GameObject("DepartmentManager");
                    go.AddComponent<DepartmentManager>();
                }
                DepartmentManager.Instance.BootstrapOpeningTeam();

                if (!Draft.FromScratch && !string.IsNullOrEmpty(Draft.TakeoverAirlineId))
                {
                    var profile = _takeoverPreview ?? TakeoverWorldBuilder.BuildPreview(Draft.TakeoverAirlineId);
                    TakeoverWorldBuilder.Apply(gs, profile);
                    gs.ceoDisplayName = Draft.PlayerName;
                    gs.ceoNationality = Draft.Nationality;
                    gs.ceoGender = Draft.Gender;
                    gs.ceoEducation = Draft.Education;
                    gs.ceoCareer = Draft.Career;
                    gs.ceoTrait = Draft.Trait;
                    TransitionBusinessManager.OnTakeoverReady(gs);
                    if (gs.boardMemberIds != null)
                    {
                        _selectedIds.Clear();
                        for (int i = 0; i < gs.boardMemberIds.Count; i++)
                            _selectedIds.Add(gs.boardMemberIds[i]);
                    }
                }
                else
                {
                    AirlineCEO.Logic.TransitionBusinessLogic.Reset(gs);
                }
            }

            if (BoardManager.Instance != null)
            {
                BoardManager.Instance.EnsureAnnualTargets();
                if (!fromContinue)
                    BoardManager.Instance.ConfirmAnnualGoals();
            }

            SaveManager.Instance?.MarkSetupCompleted();
            _bootstrap?.OnSetupCompleted();
            _bootstrap?.ApplyStartingModeSpeed();
            if (_nav == null)
                _nav = FindFirstObjectByType<PrefabNavIntegration>();
            _nav?.OpenHomeDashboard();
            // 新开局：进入主页约 2 秒后自动弹出顾问引导；读档续玩不强制弹出（可点顶栏「顾问」继续）
            if (!fromContinue)
            {
                TelemetryManager.OnGameStarted(gs);
                _bootstrap?.TryStartAdvisorTutorial(0.5f);
            }
        }

        void OpenShellPage(string pageId)
        {
            // 主菜单上的设置页：用临时叠层打开（玩法未开始时无壳）
            if (!IsInGameplay)
            {
                EnsureOverlay();
                ClearForm();
                _title.text = "设置 / 存档";
                _body.text = "局内可用 F5 存档 / F9 读档。\n显示模式与语言将在完整设置页完善。\n\n返回主菜单继续开局。";
                var actions = EnsureActions();
                AddAction(actions, "Back", "← 返回主菜单", 0.30f, 0.70f, UITheme.Accent, ShowMainMenu);
                return;
            }

            _nav?.OpenSubPage(pageId);
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
