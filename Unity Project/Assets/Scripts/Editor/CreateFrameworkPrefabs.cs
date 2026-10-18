#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using AirlineCEO.Data;
using AirlineCEO.UI;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Shell;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// Phase 1.1 / 1.2：生成 Framework Prefab + Shell + 七大部门 L1。
    /// 菜单：AirlineCEO → Phase 1 → Create Framework &amp; L1 Prefabs
    /// </summary>
    public static class CreateFrameworkPrefabs
    {
        [MenuItem("AirlineCEO/Phase 1/Create Framework & L1 Prefabs")]
        public static void CreateAll()
        {
            ChineseFontProvider.WarmupSampleChinese();
            EnsureDirs();

            CreatePanelBase();
            CreatePanelScrollList();
            CreateModalDecision();
            CreateWidgetStatRow();
            CreateWidgetProgressBar();
            CreateWidgetBadge();
            CreateWidgetTabGroup();
            CreateTemplateListItem();
            CreateShellN0();

            foreach (var d in UIPrefabCatalog.DepartmentL1)
                CreateDeptL1(d.pageId, d.fileName, d.title, d.code);

            // 额外 L1 占位（主页/收件箱/机队/航线/董事会）
            CreateGenericL1("n6_主页_Dashboard", "Panel_L1_n6_主页", "主页 Dashboard", "HOME");
            CreateGenericL1("n7_收件箱", "Panel_L1_n7_收件箱", "收件箱", "INBOX");
            CreateGenericL1("n9_机队", "Panel_L1_n9_机队", "机队", "FLEET");
            CreateGenericL1("n12_航线网络", "Panel_L1_n12_航线网络", "航线网络", "ROUTE");
            CreateGenericL1("n20_董事会", "Panel_L1_n20_董事会", "董事会", "BOARD");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            CreateUIConfigAssets.CreatePhase1CatalogAndBindPrefabs();

            EditorUtility.DisplayDialog("Phase 1 Prefabs",
                "已生成 Framework / Shell / L1 Prefab，并写入 Resources/UIConfig。\n" +
                "路径：" + UIPrefabCatalog.PrefabRoot,
                "确定");
        }

        static void EnsureDirs()
        {
            Ensure("Assets", "Prefabs");
            Ensure("Assets/Prefabs", "UI");
            Ensure(UIPrefabCatalog.PrefabRoot, "Panels");
            Ensure(UIPrefabCatalog.PrefabRoot, "Modals");
            Ensure(UIPrefabCatalog.PrefabRoot, "Widgets");
            Ensure(UIPrefabCatalog.PrefabRoot, "Templates");
            Ensure("Assets", "Resources");
            Ensure("Assets/Resources", "UIConfig");
        }

        static void Ensure(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        static void CreatePanelBase()
        {
            var root = NewUiRoot(UIPrefabCatalog.PanelBase);
            var panelGo = UIFactory.Panel(root.transform, "Panel", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panelGo, UITheme.Border, new Vector2(1f, -1f));
            var cg = panelGo.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(panelGo.transform, "Title", "面板标题", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.75f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var close = UIFactory.Button(panelGo.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);

            var bp = panelGo.AddComponent<BasePanel>();
            bp.AssignRefs(panelGo.GetComponent<RectTransform>(), title, close, cg);
            bp.BindPage("panel_base", "面板标题");

            Save(panelGo, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.PanelBase + ".prefab", root);
        }

        static void CreatePanelScrollList()
        {
            var root = NewUiRoot(UIPrefabCatalog.PanelScrollList);
            var panelGo = UIFactory.Panel(root.transform, "Panel", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panelGo, UITheme.Border, new Vector2(1f, -1f));
            var cg = panelGo.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(panelGo.transform, "Title", "列表面板", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.75f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(panelGo.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);

            var viewport = UIFactory.Panel(panelGo.transform, "Viewport",
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.84f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.15f));
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = new Vector2(0, 0);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;

            var panel = panelGo.AddComponent<ScrollListPanel>();
            panel.AssignRefs(panelGo.GetComponent<RectTransform>(), title, close, cg);
            panel.AssignScrollRefs(scroll, crt);
            panel.BindPage("panel_scroll", "列表面板");

            Save(panelGo, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.PanelScrollList + ".prefab", root);
        }

        static void CreateModalDecision()
        {
            var root = NewUiRoot(UIPrefabCatalog.ModalDecision);
            var dimmerGo = UIFactory.Panel(root.transform, "Dimmer", UIFactory.Fill,
                new Color(0, 0, 0, 0.55f), rounded: false);
            var dimmer = dimmerGo.GetComponent<Image>();

            var card = UIFactory.Panel(dimmerGo.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.2f, 0.25f), new Vector2(0.8f, 0.75f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "决策确认", 22,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var body = UIFactory.Label(card.transform, "Body", "请确认此项决策。", 16,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            body.textWrappingMode = TextWrappingModes.Normal;

            var primary = UIFactory.Button(card.transform, "Primary", "确认",
                new UIFactory.Anchor(new Vector2(0.52f, 0.08f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { }, 18);
            var secondary = UIFactory.Button(card.transform, "Secondary", "取消",
                new UIFactory.Anchor(new Vector2(0.08f, 0.08f), new Vector2(0.48f, 0.22f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 18);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.88f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);

            var modal = card.AddComponent<DecisionModal>();
            modal.AssignRefs(card.GetComponent<RectTransform>(), title, close, cg);
            modal.AssignModalRefs(primary, secondary, body, dimmer);
            modal.BindPage("modal_decision", "决策确认");

            // 保存整棵含 Dim罩的树
            dimmerGo.name = UIPrefabCatalog.ModalDecision;
            Save(dimmerGo, UIPrefabCatalog.ModalsFolder + "/" + UIPrefabCatalog.ModalDecision + ".prefab", root);
        }

        static void CreateWidgetStatRow()
        {
            var root = NewUiRoot(UIPrefabCatalog.WidgetStatRow);
            var row = UIFactory.Panel(root.transform, "Row",
                new UIFactory.Anchor(new Vector2(0, 0.3f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 36;
            le.flexibleWidth = 1;
            var label = UIFactory.Label(row.transform, "Label", "指标", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            var value = UIFactory.Label(row.transform, "Value", "—", 16,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.55f, 0), new Vector2(0.96f, 1), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var view = row.AddComponent<StatRowView>();
            var so = new SerializedObject(view);
            so.FindProperty("labelText").objectReferenceValue = label;
            so.FindProperty("valueText").objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(row, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetStatRow + ".prefab", root);
        }

        static void CreateWidgetProgressBar()
        {
            var root = NewUiRoot(UIPrefabCatalog.WidgetProgressBar);
            var box = UIFactory.Panel(root.transform, "Progress",
                new UIFactory.Anchor(new Vector2(0, 0.25f), new Vector2(1, 0.75f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var le = box.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            var label = UIFactory.Label(box.transform, "Label", "进度", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.55f), new Vector2(0.7f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            var pct = UIFactory.Label(box.transform, "Percent", "0%", 14,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.7f, 0.55f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            var track = UIFactory.Panel(box.transform, "Track",
                new UIFactory.Anchor(new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.45f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.35f));
            var fillGo = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, UITheme.Accent);
            var fill = fillGo.GetComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0.4f;
            var view = box.AddComponent<ProgressBarView>();
            var so = new SerializedObject(view);
            so.FindProperty("fill").objectReferenceValue = fill;
            so.FindProperty("labelText").objectReferenceValue = label;
            so.FindProperty("percentText").objectReferenceValue = pct;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(box, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetProgressBar + ".prefab", root);
        }

        static void CreateWidgetBadge()
        {
            var root = NewUiRoot(UIPrefabCatalog.WidgetBadge);
            var badge = UIFactory.Panel(root.transform, "Badge",
                new UIFactory.Anchor(new Vector2(0.35f, 0.25f), new Vector2(0.65f, 0.75f), Vector2.zero, Vector2.zero),
                UITheme.Accent);
            var le = badge.AddComponent<LayoutElement>();
            le.preferredWidth = 72;
            le.preferredHeight = 28;
            le.minWidth = 48;
            le.minHeight = 24;
            var label = UIFactory.Label(badge.transform, "Label", "NEW", 13,
                TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
            label.raycastTarget = false;
            var view = badge.AddComponent<BadgeIconView>();
            view.AssignRefs(badge.GetComponent<Image>(), label);
            Save(badge, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetBadge + ".prefab", root);
        }

        static void CreateWidgetTabGroup()
        {
            var root = NewUiRoot(UIPrefabCatalog.WidgetTabGroup);
            var box = UIFactory.Panel(root.transform, "TabGroup", UIFactory.Fill, UITheme.Panel);
            var tabA = UIFactory.Button(box.transform, "TabA", "页签 A",
                new UIFactory.Anchor(new Vector2(0.05f, 0.7f), new Vector2(0.48f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { }, 14);
            var tabB = UIFactory.Button(box.transform, "TabB", "页签 B",
                new UIFactory.Anchor(new Vector2(0.52f, 0.7f), new Vector2(0.95f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);
            var pageA = UIFactory.Panel(box.transform, "PageA",
                new UIFactory.Anchor(new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.62f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var pageB = UIFactory.Panel(box.transform, "PageB",
                new UIFactory.Anchor(new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.62f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            pageB.SetActive(false);
            UIFactory.Label(pageA.transform, "Hint", "内容 A", 16, TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary);
            UIFactory.Label(pageB.transform, "Hint", "内容 B", 16, TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary);

            var view = box.AddComponent<TabGroupView>();
            var so = new SerializedObject(view);
            var tabsProp = so.FindProperty("tabs");
            tabsProp.arraySize = 2;
            SetTabEntry(tabsProp.GetArrayElementAtIndex(0), "a", tabA, tabA.GetComponentInChildren<TextMeshProUGUI>(), pageA);
            SetTabEntry(tabsProp.GetArrayElementAtIndex(1), "b", tabB, tabB.GetComponentInChildren<TextMeshProUGUI>(), pageB);
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(box, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetTabGroup + ".prefab", root);
        }

        static void SetTabEntry(SerializedProperty p, string id, Button btn, TextMeshProUGUI label, GameObject page)
        {
            p.FindPropertyRelative("tabId").stringValue = id;
            p.FindPropertyRelative("button").objectReferenceValue = btn;
            p.FindPropertyRelative("label").objectReferenceValue = label;
            p.FindPropertyRelative("pageRoot").objectReferenceValue = page;
        }

        static void CreateTemplateListItem()
        {
            var root = NewUiRoot(UIPrefabCatalog.TemplateListItem);
            var item = UIFactory.Panel(root.transform, "Item",
                new UIFactory.Anchor(new Vector2(0, 0.2f), new Vector2(1, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var le = item.AddComponent<LayoutElement>();
            le.preferredHeight = 56;
            le.flexibleWidth = 1;
            var title = UIFactory.Label(item.transform, "Title", "列表项", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.45f), new Vector2(0.9f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var sub = UIFactory.Label(item.transform, "Subtitle", "副标题", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.05f), new Vector2(0.9f, 0.5f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            var btn = item.AddComponent<Button>();
            btn.targetGraphic = item.GetComponent<Image>();
            var view = item.AddComponent<ClickableListItemView>();
            var so = new SerializedObject(view);
            so.FindProperty("button").objectReferenceValue = btn;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("subtitleText").objectReferenceValue = sub;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(item, UIPrefabCatalog.TemplatesFolder + "/" + UIPrefabCatalog.TemplateListItem + ".prefab", root);
        }

        static void CreateShellN0()
        {
            var root = NewUiRoot(UIPrefabCatalog.ShellN0);
            var bar = UIFactory.Panel(root.transform, "ShellBar",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(0, 0), new Vector2(0, 72)),
                UITheme.Glass);
            UIFactory.AddBorder(bar, UITheme.Border, new Vector2(1f, -1f));
            var view = bar.AddComponent<ShellBarView>();
            var list = new List<ShellBarView.TabButton>();
            var defs = UIPrefabCatalog.ShellTabs;
            float total = 0f;
            for (int i = 0; i < defs.Length; i++)
                total += Mathf.Max(0.2f, defs[i].weight);
            if (total <= 0f) total = defs.Length;
            float cursor = 0f;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float w = Mathf.Max(0.2f, def.weight);
                float x0 = cursor / total;
                cursor += w;
                float x1 = cursor / total;
                Color idle = def.core ? UITheme.NavCoreIdle : UITheme.NavIdle;
                var btn = UIFactory.Button(bar.transform, "Tab_" + def.label, def.label,
                    new UIFactory.Anchor(new Vector2(x0 + 0.001f, 0.12f), new Vector2(x1 - 0.001f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    idle, () => { }, def.core ? 15 : 12);
                list.Add(new ShellBarView.TabButton
                {
                    pageId = def.pageId,
                    button = btn,
                    background = btn.GetComponent<Image>(),
                    label = btn.GetComponentInChildren<TextMeshProUGUI>(),
                    core = def.core
                });
            }

            var so = new SerializedObject(view);
            var tabsProp = so.FindProperty("tabs");
            tabsProp.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var e = tabsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("pageId").stringValue = list[i].pageId;
                e.FindPropertyRelative("button").objectReferenceValue = list[i].button;
                e.FindPropertyRelative("label").objectReferenceValue = list[i].label;
                e.FindPropertyRelative("background").objectReferenceValue = list[i].background;
                e.FindPropertyRelative("core").boolValue = list[i].core;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            bar.name = UIPrefabCatalog.ShellN0;
            Save(bar, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.ShellN0 + ".prefab", root);
        }

        static void CreateDeptL1(string pageId, string fileName, string title, string code)
        {
            CreateGenericL1(pageId, fileName, title, code);
        }

        static void CreateGenericL1(string pageId, string fileName, string title, string code)
        {
            var root = NewUiRoot(fileName);
            var panelGo = UIFactory.Panel(root.transform, "Panel", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panelGo, UITheme.Border, new Vector2(1f, -1f));
            var cg = panelGo.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(panelGo.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var codeLabel = UIFactory.Label(panelGo.transform, "Code", code, 14,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.7f, 0.9f), new Vector2(0.88f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Accent);
            var close = UIFactory.Button(panelGo.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.9f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var meta = UIFactory.Label(panelGo.transform, "Meta", "Phase 1 Prefab 骨架", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.84f), new Vector2(0.96f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var viewport = UIFactory.Panel(panelGo.transform, "Viewport",
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.82f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.12f));
            viewport.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 8;
            vlg.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var body = UIFactory.Label(content.transform, "Body",
                $"【{title}】\n\nPhase 1 Prefab 骨架（pageId={pageId}）。\nPhase 2 将按白板补齐完整内容。",
                15, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            body.textWrappingMode = TextWrappingModes.Normal;
            var bodyLe = body.gameObject.AddComponent<LayoutElement>();
            bodyLe.minHeight = 120;
            bodyLe.preferredHeight = 220;

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;

            var view = panelGo.AddComponent<DeptL1PanelView>();
            view.AssignRefs(panelGo.GetComponent<RectTransform>(), titleLabel, close, cg);
            view.AssignScrollRefs(scroll, crt);
            view.AssignExtraRefs(body, meta, codeLabel);
            view.BindPage(pageId, title);

            Save(panelGo, UIPrefabCatalog.PanelsFolder + "/" + fileName + ".prefab", root);
        }

        static GameObject NewUiRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1280, 720);
            return go;
        }

        static void Save(GameObject content, string path, GameObject tempRoot)
        {
            content.transform.SetParent(null, false);
            if (File.Exists(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(content, path);
            Object.DestroyImmediate(content);
            if (tempRoot != null) Object.DestroyImmediate(tempRoot);
        }
    }
}
#endif
