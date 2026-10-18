using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>简易 UI 表格（计划类页面用）。</summary>
    public static class UiTable
    {
        public const int HeaderFontSize = 16;
        public const int CellFontSize = 15;
        public const int CaptionFontSize = 17;
        public const int NoteFontSize = 14;

        public static RectTransform CreateHost(Transform parent, string name, UIFactory.Anchor anchor)
        {
            var host = UIFactory.Panel(parent, name, anchor, new Color(0, 0, 0, 0.01f), rounded: false);
            var img = host.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var scroll = host.AddComponent<ScrollRect>();
            host.AddComponent<RectMask2D>();

            var content = new GameObject("TableContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(host.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = host.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            return crt;
        }

        public static void Clear(Transform tableContent)
        {
            if (tableContent == null) return;
            for (int i = tableContent.childCount - 1; i >= 0; i--)
                Object.Destroy(tableContent.GetChild(i).gameObject);
        }

        public static void AddHeader(Transform tableContent, IList<string> cols, IList<float> weights)
        {
            AddRow(tableContent, cols, weights, header: true, onClick: null);
        }

        public static void AddRow(Transform tableContent, IList<string> cols, IList<float> weights,
            bool header = false, UnityAction onClick = null)
        {
            if (tableContent == null || cols == null || cols.Count == 0) return;

            var row = new GameObject(header ? "Header" : "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(Image));
            row.transform.SetParent(tableContent, false);
            var img = row.GetComponent<Image>();
            img.color = header
                ? new Color(0.12f, 0.22f, 0.38f, 0.95f)
                : (tableContent.childCount % 2 == 0
                    ? new Color(0.08f, 0.12f, 0.2f, 0.7f)
                    : new Color(0.06f, 0.1f, 0.16f, 0.5f));
            img.raycastTarget = true; // 允许悬停高亮（即使不可点）

            if (onClick != null)
            {
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = img;
                var colors = btn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.colors = colors;
                btn.onClick.AddListener(onClick);
            }

            // 光标移入该行微亮，便于读清内容
            var hover = row.AddComponent<UIHoverBrighten>();
            hover.RefreshBase();

            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 10, 8, 8);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            var le = row.GetComponent<LayoutElement>();
            le.minHeight = header ? 42f : 40f;
            le.preferredHeight = header ? 42f : 40f;
            le.flexibleWidth = 1f;

            float sumW = 0f;
            for (int i = 0; i < cols.Count; i++)
                sumW += (weights != null && i < weights.Count) ? Mathf.Max(0.05f, weights[i]) : 1f;

            for (int i = 0; i < cols.Count; i++)
            {
                float w = (weights != null && i < weights.Count) ? Mathf.Max(0.05f, weights[i]) : 1f;
                var cell = new GameObject("C" + i, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
                cell.transform.SetParent(row.transform, false);
                var cle = cell.GetComponent<LayoutElement>();
                cle.flexibleWidth = w / sumW;
                cle.minWidth = 48f;

                var tmp = cell.GetComponent<TextMeshProUGUI>();
                tmp.font = ChineseFontProvider.FontAsset;
                tmp.fontSize = header ? HeaderFontSize : CellFontSize;
                tmp.fontStyle = header ? FontStyles.Bold : FontStyles.Normal;
                tmp.color = UITheme.TextPrimary;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Truncate;
                tmp.raycastTarget = false;
                string cellText = ChineseFontProvider.Sanitize(cols[i] ?? "");
                ChineseFontProvider.EnsureCharacters(cellText);
                tmp.text = cellText;
            }
        }

        public static void AddCaption(Transform tableContent, string text)
        {
            var go = new GameObject("Caption", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(tableContent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 32f;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = ChineseFontProvider.FontAsset;
            tmp.fontSize = CaptionFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = UITheme.TextPrimary;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            string caption = ChineseFontProvider.Sanitize(text);
            ChineseFontProvider.EnsureCharacters(caption);
            tmp.text = caption;
            tmp.raycastTarget = false;
        }

        public static void AddNote(Transform tableContent, string text)
        {
            var go = new GameObject("Note", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(tableContent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 36f;
            le.preferredHeight = 44f;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = ChineseFontProvider.FontAsset;
            tmp.fontSize = NoteFontSize;
            tmp.color = UITheme.TextMuted;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            string note = ChineseFontProvider.Sanitize(text);
            ChineseFontProvider.EnsureCharacters(note);
            tmp.text = note;
            tmp.raycastTarget = false;
        }
    }
}
