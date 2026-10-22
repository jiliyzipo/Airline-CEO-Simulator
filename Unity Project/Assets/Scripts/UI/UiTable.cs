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
            bool header = false, UnityAction onClick = null, float rowHeight = 0f)
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
            // 紧凑行（rowHeight 由调用方指定）要相应收紧上下内边距：
            // 否则 34 px 行高扣掉 8+8 只剩 18 px，15 号字连同行高被 Truncate 整行裁掉 ——
            // 表现为「条纹行都画出来了，但没有一个字」（2026-10-19 单班页报障）。
            h.padding = rowHeight > 0f
                ? new RectOffset(10, 10, 3, 3)
                : new RectOffset(10, 10, 8, 8);
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            var le = row.GetComponent<LayoutElement>();
            // rowHeight > 0 时按调用方给的紧凑行高排版：单班页这类「字段 – 数值」密集表
            // 要在一屏内放下 9 行，40 px 的行距在 720p 下会溢出可视区，逼出滚动条。
            float rowH = rowHeight > 0f ? rowHeight : (header ? 42f : 40f);
            le.minHeight = rowH;
            le.preferredHeight = rowH;
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

        /// <summary>
        /// 让表格把可视区填满（不滚动、也不把内容全挤在顶部）。
        ///
        /// 为什么要这一步：表内容是 top 对齐的（pivot 在顶、ContentSizeFitter 按内容收高），
        /// 一旦条目少于一屏，余量就全部堆在**下半屏**变成一块空白 —— 单班页固定 9 行时尤其明显
        /// （2026-10-19 报障「页面下半部分留了太多空白」）。
        ///
        /// 两步：① 把余量摊给数据行（标题/脚注不动），单行高度封顶，免得三五条时被拉成巨条；
        /// ② 封顶后仍多出来的部分上下均分，而不是继续往下堆。
        /// 条目多到超过可视区时余量为负，本方法直接不做事 —— 该滚就滚。
        /// </summary>
        public static void FillVertically(Transform tableContent, float maxRowHeight = 52f)
        {
            if (!(tableContent is RectTransform crt)) return;
            var viewport = crt.parent as RectTransform;
            if (viewport == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
            float viewH = viewport.rect.height;
            float contentH = crt.rect.height;
            if (viewH <= 0f || contentH <= 0f) return;

            var rows = new List<LayoutElement>();
            for (int i = 0; i < crt.childCount; i++)
            {
                var child = crt.GetChild(i);
                // Clear() 用的是延迟销毁：同帧内旧行还在层级里（fake null），必须跳过，
                // 否则会把上一轮的行也当成待分配对象，余量摊错地方。
                if (child == null || child.name != "Row") continue;
                var le = child.GetComponent<LayoutElement>();
                if (le != null) rows.Add(le);
            }

            float gap = viewH - contentH;
            if (gap > 1f && rows.Count > 0)
            {
                float room = 0f;
                for (int i = 0; i < rows.Count; i++)
                    room += Mathf.Max(0f, maxRowHeight - rows[i].preferredHeight);
                float add = Mathf.Min(gap / rows.Count, room > 0f ? room / rows.Count : 0f);
                if (add > 0.5f)
                {
                    for (int i = 0; i < rows.Count; i++)
                        rows[i].preferredHeight += add;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
                    contentH = crt.rect.height;
                }
            }

            float rest = viewH - contentH;
            crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, rest > 1f ? -rest * 0.5f : 0f);
        }

        public static void AddCaption(Transform tableContent, string text)        {
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
