using System.Collections.Generic;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// n40 设置/存档：右侧浮层（约屏宽 1/3），点菜单外遮罩收回。
    /// </summary>
    public class SettingsPanelUI : BasePanel
    {
        TextMeshProUGUI _status;
        TextMeshProUGUI _title;
        readonly List<TextMeshProUGUI> _rowTitles = new List<TextMeshProUGUI>();
        TextMeshProUGUI _langValue;
        TextMeshProUGUI _cbValue;
        TextMeshProUGUI _fontValue;
        TextMeshProUGUI _displayValue;
        TextMeshProUGUI _speedValue;
        TextMeshProUGUI _teleValue;
        TextMeshProUGUI _extValue;
        TextMeshProUGUI _modsValue;
        Image _dimmer;
        GameObject _card;

        public static SettingsPanelUI Build()
        {
            var root = UIFactory.Panel(null, "n40_设置_存档", UIFactory.Fill, new Color(0, 0, 0, 0), rounded: false);
            var rootImg = root.GetComponent<Image>();
            if (rootImg != null) rootImg.raycastTarget = false;
            var cg = root.AddComponent<CanvasGroup>();

            var dimmer = UIFactory.Panel(root.transform, "Dimmer", UIFactory.Fill,
                new Color(0.02f, 0.04f, 0.08f, 0.35f), rounded: false);
            var dimmerImg = dimmer.GetComponent<Image>();
            if (dimmerImg != null) dimmerImg.raycastTarget = true;

            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.66f, 0.02f), new Vector2(0.995f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1f, -1f));
            var cardImg = card.GetComponent<Image>();
            if (cardImg != null) cardImg.raycastTarget = true;

            var title = UIFactory.Label(card.transform, "Title", Loc.T("LOC_SETTINGS_TITLE"), 20,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.91f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.86f, 0.91f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);

            var panel = root.AddComponent<SettingsPanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel._title = title;
            panel._dimmer = dimmerImg;
            panel._card = card;

            var dimmerClick = dimmer.AddComponent<SettingsDimmerClick>();
            dimmerClick.Host = panel;

            panel.BuildRows(card.transform);
            panel.BuildBottom(card.transform);

            panel._status = UIFactory.Label(card.transform, "Status", "", 11,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.16f), new Vector2(0.96f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            panel.RefreshAll();
            return panel;
        }

        public override void Show()
        {
            base.Show();
            if (_dimmer != null)
            {
                _dimmer.color = new Color(0.02f, 0.04f, 0.08f, 0.35f);
                _dimmer.raycastTarget = true;
            }
            var rootImg = GetComponent<Image>();
            if (rootImg != null)
            {
                rootImg.color = new Color(0, 0, 0, 0);
                rootImg.raycastTarget = false;
            }
            if (_card != null)
            {
                var cardImg = _card.GetComponent<Image>();
                if (cardImg != null)
                {
                    cardImg.color = UITheme.Panel;
                    cardImg.raycastTarget = true;
                }
            }
        }

        /// <summary>点遮罩只退设置一层，不把底下的主页/部门页一起清掉。</summary>
        public override void CloseOverlay()
        {
            if (NavigationRouter.Instance != null && NavigationRouter.Instance.StackDepth > 0)
                NavigationRouter.Instance.Back();
            else
                Hide();
        }

        void BuildRows(Transform parent)
        {
            MakeRow(parent, 0, "LOC_LANGUAGE", () => { AppSettings.ApplyLanguage(Loc.Current == Language.ZhCN ? Language.EnUS : Language.ZhCN); RefreshAll(); }, out _langValue);

            MakeRow(parent, 1, "LOC_COLORBLIND", () => { AppSettings.ApplyColorblind((ColorblindMode)(((int)Accessibility.Colorblind + 1) % 4)); RefreshAll(); }, out _cbValue);

            MakeRow(parent, 2, "LOC_FONTSCALE", () => { AppSettings.ApplyFontScale((FontScale)(((int)Accessibility.FontScaleLevel + 1) % 3)); RefreshAll(); }, out _fontValue);

            MakeRow(parent, 3, "LOC_DISPLAY", () => { AppSettings.ApplyFullscreen(!AppSettings.Fullscreen); RefreshAll(); }, out _displayValue);

            MakeRow(parent, 4, "LOC_DEFAULT_SPEED", () =>
            {
                int next = AppSettings.DefaultSpeedLevel + 1;
                if (next > GameSpeed.L3) next = GameSpeed.L1;
                AppSettings.ApplyDefaultSpeed(next);
                RefreshAll();
            }, out _speedValue);

            MakeRow(parent, 5, "LOC_TELEMETRY", () => { AppSettings.ApplyTelemetryOptOut(!Telemetry.OptOut); RefreshAll(); }, out _teleValue);

            MakeRow(parent, 6, "LOC_EXT_TITLE", () =>
            {
                Object.FindFirstObjectByType<PrefabNavIntegration>()?.OpenSubPage(ExternalSourcePanelUI.PageKey);
            }, out _extValue);

            MakeRow(parent, 7, "LOC_MODS", null, out _modsValue);
        }

        void MakeRow(Transform parent, int index, string titleKey, UnityAction onClick, out TextMeshProUGUI valueLabel)
        {
            float top = 0.88f - index * 0.072f;
            float bottom = top - 0.062f;

            var titleLabel = UIFactory.Label(parent, "Row" + index + "_Title", Loc.T(titleKey), 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, bottom), new Vector2(0.42f, top), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _rowTitles.Add(titleLabel);

            var valueAnchor = new UIFactory.Anchor(new Vector2(0.44f, bottom + 0.006f), new Vector2(0.96f, top - 0.006f), Vector2.zero, Vector2.zero);

            if (onClick != null)
            {
                var btn = UIFactory.Button(parent, "Row" + index + "_Value", "", valueAnchor, UITheme.PanelDeep, onClick, 13);
                valueLabel = btn.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                valueLabel.alignment = TextAlignmentOptions.MidlineRight;
                valueLabel.color = UITheme.AccentHover;
            }
            else
            {
                valueLabel = UIFactory.Label(parent, "Row" + index + "_Value", "", 13,
                    TextAlignmentOptions.MidlineRight, valueAnchor, UITheme.AccentHover);
            }
        }

        void BuildBottom(Transform parent)
        {
            UIFactory.Button(parent, "Save", Loc.T("LOC_SETTINGS_SAVE"),
                new UIFactory.Anchor(new Vector2(0.03f, 0.03f), new Vector2(0.32f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => { SaveManager.Instance?.Save(); RefreshAll(); }, 11);
            UIFactory.Button(parent, "Load", Loc.T("LOC_SETTINGS_LOAD"),
                new UIFactory.Anchor(new Vector2(0.34f, 0.03f), new Vector2(0.63f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { SaveManager.Instance?.Load(); RefreshAll(); }, 11);
            UIFactory.Button(parent, "Clear", Loc.T("LOC_CLEAR_SAVE"),
                new UIFactory.Anchor(new Vector2(0.65f, 0.03f), new Vector2(0.97f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () =>
                {
                    SaveManager.ClearAllData();
                    _status.text = Loc.T("LOC_CLEAR_SAVE_CONFIRM");
                }, 10);
        }

        void RefreshAll()
        {
            if (_title != null) _title.text = Loc.T("LOC_SETTINGS_TITLE");
            RebindRowTitleKeys();

            if (_langValue != null) _langValue.text = Loc.CurrentLanguageName;
            if (_cbValue != null) _cbValue.text = Accessibility.ColorblindLabel(Accessibility.Colorblind);
            if (_fontValue != null) _fontValue.text = Accessibility.FontScaleLabel(Accessibility.FontScaleLevel);
            if (_displayValue != null) _displayValue.text = Loc.T(AppSettings.Fullscreen ? "LOC_DISPLAY_FULLSCREEN" : "LOC_DISPLAY_WINDOW");
            if (_speedValue != null) _speedValue.text = GameSpeed.Scales[AppSettings.DefaultSpeedLevel] + "×";
            if (_teleValue != null) _teleValue.text = Loc.T(Telemetry.OptOut ? "LOC_TELE_OFF" : "LOC_TELE_ON");
            if (_extValue != null) _extValue.text = ExternalSourcePanelUI.Summary();
            if (_modsValue != null)
                _modsValue.text = ModRegistry.Count == 0
                    ? Loc.T("LOC_MODS_NONE")
                    : Loc.T("LOC_MODS_COUNT", ModRegistry.Count, ModRegistry.Conflicts.Count);

            if (_status != null)
            {
                var sm = SaveManager.Instance;
                _status.text =
                    "F5 存档 · F9 读档\n" +
                    $"存档：{(sm != null && sm.HasSave() ? "有存档" : "无存档")}　{(sm != null ? sm.LastStatus : "")}";
            }
        }

        static readonly string[] _rowKeys =
        {
            "LOC_LANGUAGE", "LOC_COLORBLIND", "LOC_FONTSCALE", "LOC_DISPLAY", "LOC_DEFAULT_SPEED", "LOC_TELEMETRY",
            "LOC_EXT_TITLE", "LOC_MODS"
        };

        void RebindRowTitleKeys()
        {
            for (int i = 0; i < _rowTitles.Count && i < _rowKeys.Length; i++)
                _rowTitles[i].text = Loc.T(_rowKeys[i]);
        }
    }

    /// <summary>点设置浮层外的遮罩即收回菜单。</summary>
    public class SettingsDimmerClick : MonoBehaviour, IPointerClickHandler
    {
        public SettingsPanelUI Host;

        public void OnPointerClick(PointerEventData eventData)
        {
            Host?.CloseOverlay();
        }
    }
}
