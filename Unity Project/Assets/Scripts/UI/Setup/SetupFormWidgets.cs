using System;
using System.Text;
using System.Text.RegularExpressions;
using AirlineCEO.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Setup
{
    /// <summary>开局表单控件：拉丁文输入、循环选择、机场/列表选择器。</summary>
    public static class SetupFormWidgets
    {
        static readonly Regex LatinName = new Regex(@"^[A-Za-z][A-Za-z '\-]{0,47}$", RegexOptions.Compiled);
        static readonly Regex LatinCompany = new Regex(@"^[A-Za-z][A-Za-z0-9 '\-]{0,47}$", RegexOptions.Compiled);
        /// <summary>IATA：两位字母，或一位字母+一位数字（顺序不限）。</summary>
        static readonly Regex IataCode = new Regex(@"^([A-Za-z]{2}|[A-Za-z][0-9]|[0-9][A-Za-z])$", RegexOptions.Compiled);
        static readonly Regex IcaoCode = new Regex(@"^[A-Za-z]{3}$", RegexOptions.Compiled);

        public static bool IsLatinPersonName(string s) =>
            !string.IsNullOrWhiteSpace(s) && LatinName.IsMatch(s.Trim());

        public static bool IsLatinCompanyName(string s) =>
            !string.IsNullOrWhiteSpace(s) && LatinCompany.IsMatch(s.Trim());

        public static bool IsIata(string s) =>
            !string.IsNullOrWhiteSpace(s) && IataCode.IsMatch(s.Trim());

        public static bool IsIcao(string s) =>
            !string.IsNullOrWhiteSpace(s) && IcaoCode.IsMatch(s.Trim());

        /// <summary>至少 2 个汉字（可含间隔号 ·）。</summary>
        public static bool IsChineseCompanyName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            int han = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (IsChineseChar(c)) han++;
                else if (c != '·' && c != '•' && c != '—' && c != ' ')
                    return false;
            }
            return han >= 2;
        }

        public static TMP_InputField AddLabeledInput(Transform form, string label, string value, int row, int totalRows,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard,
            bool latinOnly = false,
            bool chineseOnly = false)
        {
            RowRange(row, totalRows, out float y0, out float y1);
            UIFactory.Label(form, "L_" + label, label, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.26f, y1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            var input = UIFactory.Input(form, "I_" + label, value,
                new UIFactory.Anchor(new Vector2(0.28f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero));
            input.contentType = contentType;
            if (latinOnly)
            {
                input.onValueChanged.AddListener(v =>
                {
                    string cleaned = SanitizeLatin(v);
                    if (cleaned != v) input.SetTextWithoutNotify(cleaned);
                });
            }
            else if (chineseOnly)
            {
                input.onValueChanged.AddListener(v =>
                {
                    string cleaned = SanitizeChinese(v);
                    if (cleaned != v) input.SetTextWithoutNotify(cleaned);
                });
            }
            return input;
        }

        public static CycleSelect AddCycleSelect(Transform form, string label, string[] options, int initialIndex,
            int row, int totalRows)
        {
            RowRange(row, totalRows, out float y0, out float y1);
            UIFactory.Label(form, "L_" + label, label, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.26f, y1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var host = UIFactory.Panel(form, "Cyc_" + label,
                new UIFactory.Anchor(new Vector2(0.28f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);

            var sel = new CycleSelect { Options = options ?? Array.Empty<string>(), Index = Mathf.Clamp(initialIndex, 0, Math.Max(0, (options?.Length ?? 1) - 1)) };
            sel.ValueLabel = UIFactory.Label(host.transform, "Value", sel.Current, 15, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.18f, 0.1f), new Vector2(0.82f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Button(host.transform, "Prev", "◀",
                new UIFactory.Anchor(new Vector2(0.02f, 0.12f), new Vector2(0.16f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Panel, () => { sel.Step(-1); }, 14);
            UIFactory.Button(host.transform, "Next", "▶",
                new UIFactory.Anchor(new Vector2(0.84f, 0.12f), new Vector2(0.98f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Panel, () => { sel.Step(1); }, 14);
            return sel;
        }

        public static AirportSelect AddAirportSelect(Transform form, string label, string initialCode,
            int row, int totalRows, Action<string> onHint)
        {
            RowRange(row, totalRows, out float y0, out float y1);
            UIFactory.Label(form, "L_" + label, label, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.26f, y1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var host = UIFactory.Panel(form, "Apt_" + label,
                new UIFactory.Anchor(new Vector2(0.28f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);

            var sel = new AirportSelect();
            if (!AirportCatalog.TryResolve(initialCode, out var ap))
                ap = AirportCatalog.Airports.Count > 0 ? AirportCatalog.Airports[0] : default;
            sel.Code = ap.code;
            sel.ValueLabel = UIFactory.Label(host.transform, "Value", ap.Display, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.1f), new Vector2(0.72f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Button(host.transform, "Pick", "选择",
                new UIFactory.Anchor(new Vector2(0.74f, 0.12f), new Vector2(0.98f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenAirportPicker(form.parent != null ? form.parent : form, sel, onHint), 13);
            return sel;
        }

        public static ListSelect AddListSelect(Transform form, string label, string[] options, int initialIndex,
            int row, int totalRows, Action<string> onHint)
        {
            RowRange(row, totalRows, out float y0, out float y1);
            UIFactory.Label(form, "L_" + label, label, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, y0), new Vector2(0.26f, y1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var host = UIFactory.Panel(form, "Lst_" + label,
                new UIFactory.Anchor(new Vector2(0.28f, y0), new Vector2(0.98f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);

            var sel = new ListSelect
            {
                Options = options ?? Array.Empty<string>(),
                Index = Mathf.Clamp(initialIndex, 0, Math.Max(0, (options?.Length ?? 1) - 1))
            };
            sel.ValueLabel = UIFactory.Label(host.transform, "Value", sel.Current, 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.1f), new Vector2(0.72f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Button(host.transform, "Pick", "选择",
                new UIFactory.Anchor(new Vector2(0.74f, 0.12f), new Vector2(0.98f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenStringPicker(form.parent != null ? form.parent : form, sel, label, onHint), 13);
            return sel;
        }

        static void OpenAirportPicker(Transform overlayParent, AirportSelect sel, Action<string> onHint)
        {
            CloseExistingPicker(overlayParent);
            var root = UIFactory.Panel(overlayParent, "SetupPicker",
                new UIFactory.Anchor(new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(root, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(root.transform, "Title", "选择总部基地（全球主要机场）", 18,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.8f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var viewport = UIFactory.Panel(root.transform, "Viewport",
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.2f));
            viewport.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0, 0);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            var fit = content.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;

            var airports = AirportCatalog.Airports;
            for (int i = 0; i < airports.Count; i++)
            {
                var a = airports[i];
                var btn = UIFactory.Button(content.transform, "A_" + a.code, a.Display,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { }, 14);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40;
                le.flexibleWidth = 1;
                string code = a.code;
                string display = a.Display;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    sel.Code = code;
                    if (sel.ValueLabel != null) sel.ValueLabel.text = display;
                    onHint?.Invoke($"已选总部基地：{display}");
                    UnityEngine.Object.Destroy(root);
                });
            }
        }

        static void OpenStringPicker(Transform overlayParent, ListSelect sel, string title, Action<string> onHint)
        {
            CloseExistingPicker(overlayParent);
            var root = UIFactory.Panel(overlayParent, "SetupPicker",
                new UIFactory.Anchor(new Vector2(0.15f, 0.1f), new Vector2(0.85f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(root, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(root.transform, "Title", "选择" + title, 18,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.8f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var viewport = UIFactory.Panel(root.transform, "Viewport",
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.2f));
            viewport.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;

            for (int i = 0; i < sel.Options.Length; i++)
            {
                int idx = i;
                string opt = sel.Options[i];
                var btn = UIFactory.Button(content.transform, "O_" + i, opt,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { }, 14);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 36;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    sel.Index = idx;
                    if (sel.ValueLabel != null) sel.ValueLabel.text = opt;
                    onHint?.Invoke($"已选{title}：{opt}");
                    UnityEngine.Object.Destroy(root);
                });
            }
        }

        static void CloseExistingPicker(Transform parent)
        {
            if (parent == null) return;
            var old = parent.Find("SetupPicker");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
        }

        static void RowRange(int row, int totalRows, out float y0, out float y1)
        {
            float h = 1f / Mathf.Max(1, totalRows);
            y1 = 1f - row * h;
            y0 = y1 - h * 0.88f;
        }

        static string SanitizeLatin(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == ' ' || c == '-' || c == '\'')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsChineseChar(char c) =>
            (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);

        static string SanitizeChinese(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (IsChineseChar(c) || c == '·' || c == '•' || c == '—')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public sealed class CycleSelect
        {
            public string[] Options;
            public int Index;
            public TextMeshProUGUI ValueLabel;
            public string Current => Options != null && Options.Length > 0 ? Options[Mathf.Clamp(Index, 0, Options.Length - 1)] : "";
            public void Step(int delta)
            {
                if (Options == null || Options.Length == 0) return;
                Index = (Index + delta + Options.Length * 8) % Options.Length;
                if (ValueLabel != null) ValueLabel.text = Current;
            }
        }

        public sealed class ListSelect
        {
            public string[] Options;
            public int Index;
            public TextMeshProUGUI ValueLabel;
            public string Current => Options != null && Options.Length > 0 ? Options[Mathf.Clamp(Index, 0, Options.Length - 1)] : "";
        }

        public sealed class AirportSelect
        {
            public string Code;
            public TextMeshProUGUI ValueLabel;
        }
    }
}
