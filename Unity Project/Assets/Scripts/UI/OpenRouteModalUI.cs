using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
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
        bool _embedded;
        Transform _card;

        /// <summary>无 GameState（编辑器预览 / 未开局）时的基地兜底，保持改动前的旧行为。</summary>
        public const string FallbackBase = "PEK";

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

            _base = AirportField.Build(_card, "基地名称",
                new UIFactory.Anchor(new Vector2(0.05f, 0.7f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero),
                allowEmpty: false, defaultCode: null, CloseOtherPickers);
            _dest = AirportField.Build(_card, "目的地名称",
                new UIFactory.Anchor(new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.68f), Vector2.zero, Vector2.zero),
                allowEmpty: false, defaultCode: null, CloseOtherPickers);
            _via = AirportField.Build(_card, "经停地名称",
                new UIFactory.Anchor(new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.48f), Vector2.zero, Vector2.zero),
                allowEmpty: true, defaultCode: null, CloseOtherPickers);

            _hint = UIFactory.Label(_card, "Hint", HintText(),
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _hint.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(_card, "Confirm", "由运营部召集研究",
                new UIFactory.Anchor(new Vector2(0.52f, 0.04f), new Vector2(0.88f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnConfirm, 15);

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
            if (confirmLbl != null) confirmLbl.text = "由运营部召集研究";
            WireFooter(form: true);
        }

        public void Hide()
        {
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
                Flash("请填写有效的基地机场（可输入代码/中文/英文，或从列表选择）。");
                return;
            }
            if (!_dest.TryGetAirport(out var d))
            {
                Flash("请填写有效的目的地机场。");
                return;
            }
            if (string.Equals(o.code, d.code, StringComparison.OrdinalIgnoreCase))
            {
                Flash("基地与目的地不能相同。");
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
                    Flash("经停地不能与基地或目的地相同。");
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
            string path = string.IsNullOrEmpty(sel.viaCode)
                ? $"{sel.originCode} → {sel.destCode}"
                : $"{sel.originCode} → {sel.viaCode} → {sel.destCode}";
            SetSubmitted(true);
            if (_title != null) _title.text = "已提交运营部研究";
            if (_resultBody != null)
            {
                _resultBody.text =
                    $"运营部已开始召集财务、市场、人力、安全与机务，就开通 {path} 进行会商。\n\n" +
                    $"各部门意见与请示约 {AirlineCEO.Logic.InboxCadence.StudyBusinessDays} 个工作日后送达您的收件箱，不会当场出现。";
            }
            WireFooter(form: false);
        }

        void SetSubmitted(bool submitted)
        {
            SetChildActive("Field_基地名称", !submitted);
            SetChildActive("Field_目的地名称", !submitted);
            SetChildActive("Field_经停地名称", !submitted);
            if (_hint != null) _hint.gameObject.SetActive(!submitted);
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

                // 下拉列表（叠在字段下方）
                field.PickerRoot = UIFactory.Panel(host.transform, "Picker",
                    new UIFactory.Anchor(new Vector2(0f, -1.35f), new Vector2(1f, 0.05f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep);
                field.PickerRoot.transform.SetAsLastSibling();
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

                field.RebuildList();
                field.Input.onValueChanged.AddListener(_ =>
                {
                    if (field.PickerRoot != null && field.PickerRoot.activeSelf)
                        field.RebuildList(filter: field.Input.text);
                });
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
                    if (AirportCatalog.TryResolve(text, out var a))
                        field.Input.text = a.Display;
                });

                field.ResetToDefault(defaultCode);
                return field;
            }

            public void TogglePicker()
            {
                bool open = PickerRoot != null && !PickerRoot.activeSelf;
                if (open)
                {
                    _onOpen?.Invoke(this);
                    RebuildList(filter: Input != null ? Input.text : "");
                    PickerRoot.SetActive(true);
                    PickerRoot.transform.SetAsLastSibling();
                }
                else ClosePicker();
            }

            public void ClosePicker()
            {
                if (PickerRoot != null) PickerRoot.SetActive(false);
            }

            void RebuildList(string filter = null)
            {
                if (ListContent == null) return;
                for (int i = ListContent.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(ListContent.GetChild(i).gameObject);

                if (AllowEmpty)
                    AddPickRow("无经停", null);

                string f = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
                var airports = AirportCatalog.Airports;
                for (int i = 0; i < airports.Count; i++)
                {
                    var a = airports[i];
                    if (f != null)
                    {
                        if (a.code.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (a.nameZh == null || a.nameZh.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) &&
                            (a.nameEn == null || a.nameEn.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) &&
                            a.Display.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                    AddPickRow(a.Display, a);
                }
            }

            void AddPickRow(string label, AirportInfo? airport)
            {
                var btn = UIFactory.Button(ListContent, "Opt", label,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    UITheme.Panel, () =>
                    {
                        if (airport.HasValue)
                            Input.text = airport.Value.Display;
                        else
                            Input.text = "";
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
                if (string.IsNullOrEmpty(code))
                {
                    Input.text = "";
                    return;
                }
                var a = AirportCatalog.FindByCode(code);
                // 目录未收录该代码时退化为显示原始代码，而不是让输入框留空。
                // 留空有三重坏处：必填字段看起来没填；TryGetAirport 必然失败但玩家看不出原因；
                // onEndEdit 对空值只做回落，玩家若不点「选择」会一直卡在空框。
                Input.text = string.IsNullOrEmpty(a.code) ? code.Trim().ToUpperInvariant() : a.Display;
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
