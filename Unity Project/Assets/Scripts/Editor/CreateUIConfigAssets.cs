#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using AirlineCEO.Data;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// Phase 0.2：生成首批 10 个 UIConfig 资产（对齐交互白板）。
    /// 菜单：AirlineCEO → UI → Create Starter UIConfig Assets
    /// </summary>
    public static class CreateUIConfigAssets
    {
        private const string Folder = "Assets/Resources/UIConfig";

        private sealed class PageDef
        {
            public string pageId, title, level, parentId, tabGroup, badgeSource, badge, meta;
        }

        [MenuItem("AirlineCEO/UI/Create Starter UIConfig Assets")]
        public static void CreateStarter()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "UIConfig");

            var pages = new[]
            {
                new PageDef
                {
                    pageId = "n0_主界面框架_常驻", title = "主界面框架（常驻）", level = "L0",
                    parentId = "", tabGroup = "shell", badgeSource = "", badge = "框架",
                    meta = "线框图核心：地图为舞台，UI 为覆层"
                },
                new PageDef
                {
                    pageId = "n2_玩家资料创建", title = "玩家资料创建", level = "L0",
                    parentId = "n1_主菜单", tabGroup = "setup", badgeSource = "", badge = "NEW",
                    meta = "GDD 3.1 / 开局"
                },
                new PageDef
                {
                    pageId = "n3_公司注册", title = "公司注册", level = "L0",
                    parentId = "n2_玩家资料创建", tabGroup = "setup", badgeSource = "", badge = "NEW",
                    meta = "开局强制"
                },
                new PageDef
                {
                    pageId = "n4_董事会组建_抽卡", title = "董事会组建（抽卡）", level = "L0",
                    parentId = "n3_公司注册", tabGroup = "setup", badgeSource = "", badge = "NEW",
                    meta = "GDD 第5–6章"
                },
                new PageDef
                {
                    pageId = "n5_年度目标确认", title = "年度目标确认", level = "L0",
                    parentId = "n4_董事会组建_抽卡", tabGroup = "setup", badgeSource = "board.targets", badge = "NEW",
                    meta = "财年开局"
                },
                new PageDef
                {
                    pageId = "n7_收件箱", title = "收件箱", level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = "home", badgeSource = "inbox.unread", badge = "补齐",
                    meta = "左列表 + 右详情"
                },
                new PageDef
                {
                    pageId = "n12_航线网络", title = "航线网络", level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = "network", badgeSource = "", badge = "补齐",
                    meta = "地图 + 列表"
                },
                new PageDef
                {
                    pageId = "n13_运营部", title = "运营部", level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = "ops", badgeSource = "dept.ops", badge = "补齐",
                    meta = "OPS · 逐季解锁"
                },
                new PageDef
                {
                    pageId = "n14_市场销售部", title = "市场销售部", level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = "mkt", badgeSource = "dept.mkt", badge = "补齐",
                    meta = "MKT · 含航线收益总览入口"
                },
                new PageDef
                {
                    pageId = "n15_财务部", title = "财务部", level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = "fin", badgeSource = "dept.fin", badge = "补齐",
                    meta = "FIN · 现金命脉"
                },
            };

            int n = 0;
            foreach (var p in pages)
            {
                string safe = SanitizeFileName(p.pageId);
                string path = $"{Folder}/{safe}.asset";
                var asset = LoadOrCreate<UIConfig>(path);
                asset.pageId = p.pageId;
                asset.title = p.title;
                asset.level = p.level;
                asset.parentId = p.parentId;
                asset.tabGroup = p.tabGroup;
                asset.badgeSource = p.badgeSource;
                asset.badge = p.badge;
                asset.meta = p.meta;
                EditorUtility.SetDirty(asset);
                n++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("UIConfig", $"已创建/更新 {n} 个资产\n{Folder}", "确定");
        }

        /// <summary>
        /// Phase 1：把 Shell/L1 UIConfig 写到 Resources/UIConfig，并绑定 Prefab 引用。
        /// 由 CreateFrameworkPrefabs 调用；也可单独菜单触发。
        /// </summary>
        [MenuItem("AirlineCEO/Phase 1/Bind UIConfig Prefabs (Resources)")]
        public static void CreatePhase1CatalogAndBindPrefabs()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "UIConfig");

            var pages = new List<PageDef>
            {
                new PageDef
                {
                    pageId = "n0_主界面框架_常驻", title = "主界面框架（常驻）", level = "L0",
                    parentId = "", tabGroup = "shell", badgeSource = "", badge = "框架",
                    meta = "底栏 Tab + 内容宿主"
                },
            };

            foreach (var d in AirlineCEO.UI.Navigation.UIPrefabCatalog.DepartmentL1)
            {
                pages.Add(new PageDef
                {
                    pageId = d.pageId, title = d.title, level = "L1",
                    parentId = "n0_主界面框架_常驻", tabGroup = d.code.ToLowerInvariant(),
                    badgeSource = "dept." + d.code.ToLowerInvariant(), badge = "Phase1",
                    meta = d.code + " · Prefab L1"
                });
            }

            pages.Add(new PageDef
            {
                pageId = "n6_主页_Dashboard", title = "主页 Dashboard", level = "L1",
                parentId = "n0_主界面框架_常驻", tabGroup = "home", badgeSource = "", badge = "Phase1",
                meta = "主页占位"
            });
            pages.Add(new PageDef
            {
                pageId = "n7_收件箱", title = "收件箱", level = "L1",
                parentId = "n0_主界面框架_常驻", tabGroup = "inbox", badgeSource = "inbox.unread", badge = "Phase1",
                meta = "收件箱占位"
            });
            pages.Add(new PageDef
            {
                pageId = "n9_机队", title = "机队", level = "L1",
                parentId = "n0_主界面框架_常驻", tabGroup = "fleet", badgeSource = "", badge = "Phase1",
                meta = "机队占位"
            });
            pages.Add(new PageDef
            {
                pageId = "n12_航线网络", title = "航线网络", level = "L1",
                parentId = "n0_主界面框架_常驻", tabGroup = "network", badgeSource = "", badge = "Phase1",
                meta = "航线占位"
            });
            pages.Add(new PageDef
            {
                pageId = "n20_董事会", title = "董事会", level = "L1",
                parentId = "n0_主界面框架_常驻", tabGroup = "board", badgeSource = "", badge = "Phase1",
                meta = "董事会占位"
            });

            int n = 0;
            foreach (var p in pages)
            {
                string safe = SanitizeFileName(p.pageId);
                // Resources 供运行时 LoadAll
                string resPath = $"Assets/Resources/UIConfig/{safe}.asset";
                var asset = LoadOrCreate<UIConfig>(resPath);
                asset.pageId = p.pageId;
                asset.title = p.title;
                asset.level = p.level;
                asset.parentId = p.parentId;
                asset.tabGroup = p.tabGroup;
                asset.badgeSource = p.badgeSource;
                asset.badge = p.badge;
                asset.meta = p.meta;
                asset.prefab = FindPrefabForPage(p.pageId);
                EditorUtility.SetDirty(asset);
                n++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Phase1] UIConfig bound: {n} → Resources/UIConfig");
        }

        static GameObject FindPrefabForPage(string pageId)
        {
            if (pageId == "n0_主界面框架_常驻")
                return AssetDatabase.LoadAssetAtPath<GameObject>(
                    AirlineCEO.UI.Navigation.UIPrefabCatalog.PanelsFolder + "/" + AirlineCEO.UI.Navigation.UIPrefabCatalog.ShellN0 + ".prefab");

            foreach (var d in AirlineCEO.UI.Navigation.UIPrefabCatalog.DepartmentL1)
            {
                if (d.pageId == pageId)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(
                        AirlineCEO.UI.Navigation.UIPrefabCatalog.PanelsFolder + "/" + d.fileName + ".prefab");
            }

            string[] extras =
            {
                "n6_主页_Dashboard|Panel_L1_n6_主页",
                "n7_收件箱|Panel_L1_n7_收件箱",
                "n9_机队|Panel_L1_n9_机队",
                "n12_航线网络|Panel_L1_n12_航线网络",
                "n20_董事会|Panel_L1_n20_董事会",
            };
            for (int i = 0; i < extras.Length; i++)
            {
                var parts = extras[i].Split('|');
                if (parts[0] == pageId)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(
                        AirlineCEO.UI.Navigation.UIPrefabCatalog.PanelsFolder + "/" + parts[1] + ".prefab");
            }

            return null;
        }

        private static string SanitizeFileName(string pageId)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                pageId = pageId.Replace(c, '_');
            return pageId;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
#endif
