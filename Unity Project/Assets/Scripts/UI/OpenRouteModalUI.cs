using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>开通航线弹窗：基地 / 目的地 / 经停，下拉列表 + 可输入中文/英文/代码。</summary>
    public class OpenRouteModalUI : MonoBehaviour
    {
        public struct Selection
        {
            public string originCode;
            public string destCode;
            public string viaCode;
            public string originLabel;
            public string destLabel;
            public string viaLabel;
        }

        Action<Selection> _onConfirm;
        Action _onCancel;
        AirportField _base;
        AirportField _dest;
        AirportField _via;
        TextMeshProUGUI _hint;
        TextMeshProUGUI _title;
        TextMeshProUGUI _resultBody;
        GameObject _rpmButton;
        bool _embedded;
        Transform _card;

        /// <summary>无 GameState（编辑器预览 / 未开局）时的基地兜底，保持改动前的旧行为。</summary>
        public const string FallbackBase = "PEK";

        /// <summary>表单提交按钮文案。Build 与 Show 原先各存一份字面量，改一处必漏另一处。</summary>
        public const string ConfirmLabel = "申请开通";

        /// <summary>总部基地原始值（可能为 null / 空 / 含空白）。</summary>
        static string PlayerHubRaw()
        {
            var gs = AirlineCEO.Systems.GameState.Instance;
            return gs != null ? gs.hubIata : null;
        }

        /// <summary>基地代码规范化：空 / 空白 → 兜底 PEK，否则去空白并转大写。</summary>
        public static string NormalizeBase(string hubIata) =>
            string.IsNullOrWhiteSpace(hubIata) ? FallbackBase : hubIata.Trim().ToUpperInvariant();

        /// <summary>
        /// 由总部基地推导开通航线表单的默认端点。**纯函数**，便于自测锁定不变量。
        ///
        /// 此前 <c>Show()</c> 把基地硬编码为 "PEK"：无论玩家在开局（或接管）时把总部设在北京、广州
        /// 还是新加坡，开通航线都要先手动改一次基地。忘了改的后果不只是「麻烦」——始发地会落在非枢纽，
        /// 于是 <see cref="AirlineCEO.Systems.RouteRightManager.IsFromHomeBase"/> 返回 false，
        /// 基地始发干线**本应占用的时刻槽被跳过**，玩家在不知情下绕过了枢纽的时刻成本。
        ///
        /// 不变量（由自测锁定）：<paramref name="baseCode"/> 非空；<paramref name="destCode"/>
        /// 必与基地不同 —— 否则表单默认值本身就落在 OnConfirm 的「基地与目的地不能相同」拒绝分支上。
        /// </summary>
        public static void DefaultEndpoints(string hubIata, out string baseCode, out string destCode)
        {
            baseCode = NormalizeBase(hubIata);
            // 目的地默认上海虹桥；仅当基地本就是虹桥时改用北京首都。
            destCode = string.Equals(baseCode, "SHA", StringComparison.OrdinalIgnoreCase) ? FallbackBase : "SHA";
        }

        /// <summary>
        /// 表单提示。抽出为单一来源 —— 原先 Build 与 Show 各存一份逐字相同的字面量，改一处必漏另一处。
        /// 不在此处追加「基地默认取总部基地」的说明：提示框高度固定（anchor 0.16~0.28），
        /// 现有两行已接近该宽度上限，再加行会溢出去压到下方确认按钮。
        /// 基地已预填为总部基地，字段本身就说明了这件事。
        /// </summary>
        static string HintText() =>
            "可点「选择」打开全部机场列表，也可直接输入中文名 / 英文名 / IATA 代码。\n" +
            $"经停可留空。运营部约 {AirlineCEO.Logic.InboxCadence.StudyBusinessDays} 个工作日完成会商后，" +
            $"才会把请示发到收件箱（批准后下季度扣启动资金约 {AirlineCEO.Systems.ActionExecutor.OpenRouteStartupCost:F0} 万美元）。";

        public static OpenRouteModalUI MountIn(Transform host)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<OpenRouteModalUI>(true);
            if (existing != null)
            {
                existing._embedded = true;
                existing.FitEmbedCard();
                return existing;
            }
            var root = UIFactory.Panel(host, "OpenRouteEmbed", UIFactory.Fill, new Color(0, 0, 0, 0), rounded: false);
            var img = root.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }
            var ui = root.AddComponent<OpenRouteModalUI>();
            ui._embedded = true;
            ui.Build(root.transform);
            ui.FitEmbedCard();
            return ui;
        }

        void FitEmbedCard()
        {
            if (_card == null) _card = transform.Find("Card");
            var rt = _card as RectTransform;
            if (rt != null)
                UIFactory.ApplyAnchor(rt, UIFactory.Fill);
        }

        public static OpenRouteModalUI Ensure(Transform canvasRoot)
        {
            if (canvasRoot == null) return null;
            var all = canvasRoot.GetComponentsInChildren<OpenRouteModalUI>(true);
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && !all[i]._embedded) return all[i];
            }

            var root = UIFactory.Panel(canvasRoot, "OpenRouteModal", UIFactory.Fill,
                new Color(0.02f, 0.04f, 0.08f, 0.72f), rounded: false);
            root.transform.SetAsLastSibling();
            var ui = root.AddComponent<OpenRouteModalUI>();
            ui.Build(root.transform);
            root.SetActive(false);
            return ui;
        }

        void Build(Transform root)
        {
            _card = UIFactory.Panel(root, "Card",
                new UIFactory.Anchor(new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Panel).transform;
            UIFactory.AddBorder(_card.gameObject, UITheme.Border, new Vector2(1.2f, -1.2f));

            _title = UIFactory.Label(_card, "Title", "开通航线", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.9f), new Vector2(0.7f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _base = AirportField.Build(_card, "出发地",
                new UIFactory.Anchor(new Vector2(0.05f, 0.7f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero),
                allowEmpty: false, defaultCode: null, CloseOtherPickers);
            _dest = AirportField.Build(_card, "目的地",
                new UIFactory.Anchor(new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.68f), Vector2.zero, Vector2.zero),
                allowEmpty: false, defaultCode: null, CloseOtherPickers);
            _via = AirportField.Build(_card, "经停地",
                new UIFactory.Anchor(new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.48f), Vector2.zero, Vector2.zero),
                allowEmpty: true, defaultCode: null, CloseOtherPickers);

            _hint = UIFactory.Label(_card, "Hint", HintText(),
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _hint.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(_card, "Confirm", ConfirmLabel,
                new UIFactory.Anchor(new Vector2(0.52f, 0.04f), new Vector2(0.88f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnConfirm, 15);

            // 与「申请开通」并排：先按当前填好的出发地 / 目的地做一次 RPM 评估，
            // 再决定要不要真的提研究。这正是原先表单左下角的空位（截图中标红的空白块）。
            _rpmButton = UIFactory.Button(_card, "RpmEval", "RPM 评估",
                new UIFactory.Anchor(new Vector2(0.12f, 0.04f), new Vector2(0.48f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, OnOpenRpmEvaluation, 15).gameObject;

            _resultBody = UIFactory.ScrollableLabel(_card, "ResultBody",
                new UIFactory.Anchor(new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 15);
            var resultGo = _resultBody != null ? _resultBody.transform.parent : null;
            if (resultGo != null) resultGo.gameObject.SetActive(false);

            ApplyDefaults();
        }

        /// <summary>
        /// 表单默认值：基地 = 玩家选定的总部基地（见 <see cref="DefaultEndpoints"/>）；
        /// 每次 <see cref="Show"/> 都重新求值，故中途换总部基地后打开即生效。
        /// </summary>
        void ApplyDefaults()
        {
            DefaultEndpoints(PlayerHubRaw(), out string baseCode, out string destCode);
            _base?.ResetToDefault(baseCode);
            _dest?.ResetToDefault(destCode);
            _via?.ResetToDefault(null);
        }

        void CloseOtherPickers(AirportField except)
        {
            if (_base != null && _base != except) _base.ClosePicker();
            if (_dest != null && _dest != except) _dest.ClosePicker();
            if (_via != null && _via != except) _via.ClosePicker();
        }

        public void Show(Action<Selection> onConfirm, Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            gameObject.SetActive(true);
            if (!_embedded)
                transform.SetAsLastSibling();
            ApplyDefaults();
            CloseOtherPickers(null);
            EnsureChrome();
            SetSubmitted(false);
            if (_hint != null)
            {
                _hint.color = UITheme.TextMuted;
                _hint.text = HintText();
            }
            var confirmLbl = _card != null ? _card.Find("Confirm")?.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (confirmLbl != null) confirmLbl.text = ConfirmLabel;
            WireFooter(form: true);
            if (!_embedded) RegisterBack();
        }

        /// <summary>
        /// 把本弹窗挂到底栏 &lt; 的返回目标上，并让底栏浮到蒙层之上。
        ///
        /// 两件事必须一起做，缺一件就等于没做：
        /// ① 不登记 —— &lt; 在主页地图上本就是禁用态（没有浏览历史、路由栈里也没有本弹窗），点了没反应；
        /// ② 不抬高底栏 —— 蒙层是全屏 raycast 目标，点击根本到不了 &lt;，登记了也点不着。
        /// </summary>
        void RegisterBack()
        {
            ModalBackStack.Push(this, TryBack);
            PrefabNavIntegration.RaiseChromeAboveOverlays();
        }

        /// <summary>
        /// 底栏 &lt; 的返回目标：关掉弹窗。
        /// 不在弹窗态时返回 false，让 &lt; 继续按浏览历史 / 路由栈退页。
        /// </summary>
        public bool TryBack()
        {
            if (_embedded || !gameObject.activeSelf) return false;
            Hide();
            return true;
        }

        public void Hide()
        {
            // 先注销再收尾：Hide 可能由返回键触发（ModalBackStack 已摘掉本条），
            // 也可能是页面自己关的（此时还没摘）。两处都调，未登记时是无操作。
            ModalBackStack.Pop(this);
            CloseOtherPickers(null);
            if (_embedded)
            {
                SetSubmitted(false);
                WireFooter(form: true);
                return;
            }
            gameObject.SetActive(false);
            var cb = _onCancel;
            _onCancel = null;
            cb?.Invoke();
        }

        void OnConfirm()
        {
            CloseOtherPickers(null);
            if (!_base.TryGetAirport(out var o))
            {
                Flash("请填写有效的出发地机场（可输入代码/中文/英文，或从列表选择）。");
                return;
            }
            if (!_dest.TryGetAirport(out var d))
            {
                Flash("请填写有效的目的地机场。");
                return;
            }
            if (string.Equals(o.code, d.code, StringComparison.OrdinalIgnoreCase))
            {
                Flash("出发地与目的地不能相同。");
                return;
            }
            float odKm = MapWidget.GreatCircleKm(o.code, d.code);
            string why = RouteGeography.RejectReason(o.code, d.code, odKm);
            if (!string.IsNullOrEmpty(why))
            {
                Flash(why);
                return;
            }

            string viaCode = null;
            string viaLabel = "无";
            if (_via.TryGetAirport(out var v))
            {
                if (string.Equals(v.code, o.code, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.code, d.code, StringComparison.OrdinalIgnoreCase))
                {
                    Flash("经停地不能与出发地或目的地相同。");
                    return;
                }
                viaCode = v.code;
                viaLabel = v.Display;
            }

            var sel = new Selection
            {
                originCode = o.code,
                destCode = d.code,
                viaCode = viaCode,
                originLabel = o.Display,
                destLabel = d.Display,
                viaLabel = viaLabel
            };
            var cb = _onConfirm;
            cb?.Invoke(sel);
            EnsureChrome();
            ShowSubmitted(sel);
        }

        void EnsureChrome()
        {
            if (_card == null) _card = transform.Find("Card");
            if (_title == null && _card != null)
                _title = _card.Find("Title")?.GetComponent<TextMeshProUGUI>();
            if (_hint == null && _card != null)
                _hint = _card.Find("Hint")?.GetComponent<TextMeshProUGUI>();
            if (_resultBody == null && _card != null)
            {
                var c = _card.Find("ResultBody_Viewport/ResultBody_Content");
                if (c != null) _resultBody = c.GetComponent<TextMeshProUGUI>();
            }
            if (_resultBody == null && _card != null)
            {
                _resultBody = UIFactory.ScrollableLabel(_card, "ResultBody",
                    new UIFactory.Anchor(new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, 15);
            }
        }

        public void ShowSubmitted(Selection sel)
        {
            EnsureChrome();
            // 「已提交」态把表单与两个按钮全部隐藏，页内没有别的出口 ——
            // 这里补登记一次，保证即使有人不经 Show() 直接展示结果页，底栏 < 仍然关得掉它。
            if (!_embedded) RegisterBack();
            string path = string.IsNullOrEmpty(sel.viaCode)
                ? $"{sel.originCode} → {sel.destCode}"
                : $"{sel.originCode} → {sel.viaCode} → {sel.destCode}";
            SetSubmitted(true);
            if (_title != null) _title.text = "已交由运营部跟进";
            if (_resultBody != null)
            {
                _resultBody.text =
                    $"运营部已开始召集财务、市场、人力、安全与机务，就开通 {path} 进行会商。\n\n" +
                    $"各部门意见与请示约 {AirlineCEO.Logic.InboxCadence.StudyBusinessDays} 个工作日后送达您的收件箱，不会当场出现。";
            }
            WireFooter(form: false);
        }

        /// <summary>
        /// 「RPM 评估」：用表单里当前的出发地 / 目的地直接打开 n35 评估台。
        ///
        /// 只校验端点，不要求其余字段（经停地）就绪 —— 评估台本来就是一班直飞的单班口径，
        /// 经停在开线时才参与分段航距。校验失败留在本页用 Flash 提示，别把人送进一个空白评估页。
        /// </summary>
        void OnOpenRpmEvaluation()
        {
            CloseOtherPickers(null);
            if (!_base.TryGetAirport(out var o))
            {
                Flash("请先填写有效的出发地机场，再做 RPM 评估。");
                return;
            }
            if (!_dest.TryGetAirport(out var d))
            {
                Flash("请先填写有效的目的地机场，再做 RPM 评估。");
                return;
            }
            if (string.Equals(o.code, d.code, StringComparison.OrdinalIgnoreCase))
            {
                Flash("出发地与目的地不能相同。");
                return;
            }

            RpmPreview.Seed(o.code, d.code);
            if (!_embedded)
            {
                // 这里不是「关闭」，而是把弹窗让给评估台：仍要先注销返回登记，
                // 否则底栏 < 会留下一层点不动的空登记（TryBack 对已隐藏的弹窗返回 false）。
                ModalBackStack.Pop(this);
                gameObject.SetActive(false);
            }
            StrategyPanelUI.OpenStrategyPage("n35_新航线_RPM_评估台");
        }

        void SetSubmitted(bool submitted)
        {
            SetChildActive("Field_出发地", !submitted);
            SetChildActive("Field_目的地", !submitted);
            SetChildActive("Field_经停地", !submitted);
            if (_hint != null) _hint.gameObject.SetActive(!submitted);
            if (_rpmButton == null && _card != null)
            {
                var rpm = _card.Find("RpmEval");
                if (rpm != null) _rpmButton = rpm.gameObject;
            }
            // 已提交 / 地图直通（ShowSubmitted）时表单整体隐藏，评估入口随之隐藏。
            if (_rpmButton != null) _rpmButton.SetActive(!submitted);
            var resultHost = _resultBody != null ? _resultBody.transform.parent : null;
            if (resultHost != null) resultHost.gameObject.SetActive(submitted);
            if (!submitted && _title != null) _title.text = "开通航线";
        }

        void SetChildActive(string name, bool on)
        {
            var t = _card != null ? _card.Find(name) : null;
            if (t != null) t.gameObject.SetActive(on);
        }

        void WireFooter(bool form)
        {
            var confirm = _card != null ? _card.Find("Confirm")?.GetComponent<Button>() : null;
            var rpm = _card != null ? _card.Find("RpmEval") : null;
            if (rpm != null) rpm.gameObject.SetActive(form);
            if (confirm == null) return;
            confirm.gameObject.SetActive(form);
            confirm.onClick.RemoveAllListeners();
            if (form)
                confirm.onClick.AddListener(OnConfirm);
        }

        void Flash(string msg)
        {
            if (_hint == null) return;
            _hint.text = msg;
            _hint.color = UITheme.Danger;
        }

        class AirportField
        {
            public TMP_InputField Input;
            public GameObject PickerRoot;
            public Transform ListContent;
            public bool AllowEmpty;
            string _defaultCode;
            Action<AirportField> _onOpen;
            /// <summary>程序化改写输入框（默认值 / 选中行 / 回车补全）时置位，避免把回填当成「玩家在打字」而自动弹列表。</summary>
            bool _suppress;

            public static AirportField Build(Transform parent, string label, UIFactory.Anchor area,
                bool allowEmpty, string defaultCode, Action<AirportField> onOpen)
            {
                var host = UIFactory.Panel(parent, "Field_" + label, area, new Color(0, 0, 0, 0.01f), rounded: false);
                var himg = host.GetComponent<Image>();
                if (himg != null) himg.raycastTarget = false;

                UIFactory.Label(host.transform, "Label", label, 15, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0f, 0.62f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);

                var field = new AirportField { AllowEmpty = allowEmpty, _defaultCode = defaultCode, _onOpen = onOpen };
                field.Input = UIFactory.Input(host.transform, "Input", "输入中文 / 英文 / 代码…",
                    new UIFactory.Anchor(new Vector2(0f, 0.08f), new Vector2(0.72f, 0.55f), Vector2.zero, Vector2.zero));

                UIFactory.Button(host.transform, "PickBtn", "选择",
                    new UIFactory.Anchor(new Vector2(0.74f, 0.08f), new Vector2(1f, 0.55f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () => field.TogglePicker(), 14);

                // ———— 下拉列表挂在**外层卡片**上，而不是字段内部 ————
                // 字段彼此是兄弟节点，后建的「经停地」会盖住「目的地」自己的子节点 ——
                // 这正是截图中「上面的下拉列表被下面的输入框覆盖」的来路。挂到卡片上才能
                // SetAsLastSibling() 压住所有字段；位置与宽度在展开时按输入框实际矩形现算
                // （见 PlacePicker），所以既对齐输入框、也不会被下面的字段切掉。
                field.PickerRoot = UIFactory.Panel(parent, "Picker_" + label, UIFactory.Fill, UITheme.PanelDeep);
                var scroll = field.PickerRoot.AddComponent<ScrollRect>();
                field.PickerRoot.AddComponent<RectMask2D>();
                var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                content.transform.SetParent(field.PickerRoot.transform, false);
                var crt = content.GetComponent<RectTransform>();
                crt.anchorMin = new Vector2(0, 1);
                crt.anchorMax = new Vector2(1, 1);
                crt.pivot = new Vector2(0.5f, 1);
                crt.anchoredPosition = Vector2.zero;
                crt.sizeDelta = Vector2.zero;
                var vlg = content.GetComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(6, 6, 6, 6);
                vlg.spacing = 4;
                vlg.childControlHeight = false;
                vlg.childControlWidth = true;
                vlg.childForceExpandWidth = true;
                content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.content = crt;
                scroll.viewport = field.PickerRoot.GetComponent<RectTransform>();
                scroll.horizontal = false;
                scroll.vertical = true;
                field.ListContent = content.transform;
                field.PickerRoot.SetActive(false);

                // ———— 边打边筛：输入即展开 ————
                // 旧实现只在点「选择」时展开，输入框打得再准也得先点一下按钮，
                // 于是「输入机场名 → 看不到候选」被当成 bug 报了上来。
                field.Input.onValueChanged.AddListener(text =>
                {
                    if (field._suppress) return;
                    if (string.IsNullOrWhiteSpace(text)) { field.ClosePicker(); return; }
                    field.OpenPicker();
                });
                // 点进另一个字段时收起自己的列表（否则两个列表会叠在一起）。
                field.Input.onSelect.AddListener(_ => field._onOpen?.Invoke(field));
                field.Input.onEndEdit.AddListener(text =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // 必填字段被清空后不能留成空框：那会让界面看起来「没填」，而这里原先直接 return，
                        // 玩家只能靠点「选择」才能恢复（截图中基地一栏就是空占位符的状态）。
                        // 回落到字段默认值 —— 基地即总部基地。
                        if (!allowEmpty) field.ResetToDefault(field._defaultCode);
                        return;
                    }
                    // 只做「补全成标准写法」，不关闭列表也不重排：
                    // 回车/失焦与「点中某一行」在同一帧发生，此时若把列表关掉，
                    // 行按钮会先失活、onClick 就不再触发，选中会静默失败。
                    if (AirportCatalog.TryResolve(text, out var a))
                    {
                        field._suppress = true;
                        field.Input.text = a.Display;
                        field._suppress = false;
                    }
                });

                field.ResetToDefault(defaultCode);
                return field;
            }

            public void TogglePicker()
            {
                if (PickerRoot != null && PickerRoot.activeSelf) ClosePicker();
                else OpenPicker();
            }

            /// <summary>展开（幂等）：刷新候选、按输入框实际矩形定位、置顶。</summary>
            public void OpenPicker()
            {
                if (PickerRoot == null || Input == null) return;
                _onOpen?.Invoke(this);
                RebuildList(filter: Input.text);
                PickerRoot.SetActive(true);
                PickerRoot.transform.SetAsLastSibling();
                PlacePicker();
            }

            public void ClosePicker()
            {
                if (PickerRoot != null) PickerRoot.SetActive(false);
            }

            /// <summary>
            /// 把列表贴到输入框下沿、宽度等于输入框宽度。卡内下方空间不足时改为向上展开
            /// （经停地在表单下半部，向下会被底部的 RPM / 申请按钮挤掉）。
            /// 用实际世界矩形换算，不依赖写死的归一化锚点 —— 后者正是旧版盖住输入框的原因。
            /// </summary>
            void PlacePicker()
            {
                if (PickerRoot == null || Input == null) return;
                var rt = PickerRoot.GetComponent<RectTransform>();
                var cardRt = PickerRoot.transform.parent as RectTransform;
                var inputRt = Input.GetComponent<RectTransform>();
                if (rt == null || cardRt == null || inputRt == null) return;

                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4];
                inputRt.GetWorldCorners(corners);                 // 0=左下 1=左上 2=右上 3=右下
                Vector3 bl = cardRt.InverseTransformPoint(corners[0]);
                Vector3 tr = cardRt.InverseTransformPoint(corners[2]);
                Rect card = cardRt.rect;

                const float rowH = 32f, padH = 12f;
                float want = Mathf.Clamp(_lastRows, 1, 7) * rowH + padH;
                float width = Mathf.Max(220f, tr.x - bl.x);

                float roomBelow = bl.y - card.yMin - 6f;
                float roomAbove = card.yMax - tr.y - 6f;
                bool down = roomBelow >= Mathf.Min(want, 100f) || roomBelow >= roomAbove;
                float h = Mathf.Max(48f, Mathf.Min(want, down ? roomBelow : roomAbove));
                float top = down ? bl.y : tr.y;

                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // 卡片左上角为锚点
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(width, h);
                rt.anchoredPosition = new Vector2(bl.x - card.xMin, top - card.yMax);
            }

            int _lastRows = 1;

            /// <summary>重建候选行，返回行数（供 PlacePicker 算高度）。</summary>
            int RebuildList(string filter = null)
            {
                if (ListContent == null) return 0;

                // Destroy 要到帧末才生效，同一帧内多次重建会让旧行残留、与新行一起排进布局
                //（「打了 NGB，下面却还挂着上一轮的行」）。先失活再销毁，布局立刻干净。
                for (int i = ListContent.childCount - 1; i >= 0; i--)
                {
                    var go = ListContent.GetChild(i).gameObject;
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }

                int rows = 0;
                if (AllowEmpty) { AddPickRow("无经停", null); rows++; }

                var matches = AirportCatalog.Filter(filter);
                for (int i = 0; i < matches.Count; i++)
                {
                    AddPickRow(matches[i].Display, matches[i]);
                    rows++;
                }
                if (rows == 0 && !string.IsNullOrWhiteSpace(filter))
                    AddPickRow($"无匹配机场：{filter.Trim()}", null, pickable: false);
                else if (rows == 0)
                    AddPickRow("输入中文 / 英文 / 代码后筛选…", null, pickable: false);

                _lastRows = Mathf.Max(1, rows);
                return rows;
            }

            void AddPickRow(string label, AirportInfo? airport, bool pickable = true)
            {
                var btn = UIFactory.Button(ListContent, "Opt", label,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    pickable ? UITheme.Panel : UITheme.PanelDeep, () =>
                    {
                        if (!pickable) return;      // 「无匹配机场」只是提示行，点了不该清空输入框
                        _suppress = true;           // 回填不是玩家在打字，不再自动弹出列表
                        if (airport.HasValue)
                            Input.text = airport.Value.Display;
                        else
                            Input.text = "";
                        _suppress = false;
                        ClosePicker();
                    }, 13);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 32;
                le.flexibleWidth = 1;
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.sizeDelta = new Vector2(0, 32);
            }

            public void ResetToDefault(string code)
            {
                _defaultCode = code;
                ClosePicker();
                if (Input == null) return;
                // 回填默认值是程序行为，不能触发「输入即展开」——否则每次 Show() 都会弹出列表。
                _suppress = true;
                if (string.IsNullOrEmpty(code))
                {
                    Input.text = "";
                    _suppress = false;
                    return;
                }
                var a = AirportCatalog.FindByCode(code);
                // 目录未收录该代码时退化为显示原始代码，而不是让输入框留空。
                // 留空有三重坏处：必填字段看起来没填；TryGetAirport 必然失败但玩家看不出原因；
                // onEndEdit 对空值只做回落，玩家若不点「选择」会一直卡在空框。
                Input.text = string.IsNullOrEmpty(a.code) ? code.Trim().ToUpperInvariant() : a.Display;
                _suppress = false;
            }

            public bool TryGetAirport(out AirportInfo airport)
            {
                airport = default;
                string text = Input != null ? Input.text : "";
                if (string.IsNullOrWhiteSpace(text))
                    return AllowEmpty;
                return AirportCatalog.TryResolve(text, out airport);
            }
        }
    }
}
