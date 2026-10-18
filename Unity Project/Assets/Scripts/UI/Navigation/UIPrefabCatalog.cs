using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>Phase 1 Prefab / Resources 路径约定。</summary>
    public static class UIPrefabCatalog
    {
        public const string ResourcesConfigFolder = "UIConfig";
        public const string PrefabRoot = "Assets/Prefabs/UI";
        public const string PanelsFolder = PrefabRoot + "/Panels";
        public const string ModalsFolder = PrefabRoot + "/Modals";
        public const string WidgetsFolder = PrefabRoot + "/Widgets";
        public const string TemplatesFolder = PrefabRoot + "/Templates";

        public const string PanelBase = "Panel_Base";
        public const string PanelScrollList = "Panel_ScrollList";
        public const string ModalDecision = "Modal_Decision";
        public const string WidgetStatRow = "Widget_StatRow";
        public const string WidgetProgressBar = "Widget_ProgressBar";
        public const string WidgetBadge = "Widget_Badge";
        public const string WidgetTabGroup = "Widget_TabGroup";
        public const string TemplateListItem = "Template_ListItem";

        public const string ShellN0 = "Panel_Shell_n0";
        public const string PanelL1Ops = "Panel_L1_n13_运营部";

        /// <summary>七大部门 L1（以 OPS 为模板批量复制）。</summary>
        public static readonly (string pageId, string fileName, string title, string tabLabel, string code)[] DepartmentL1 =
        {
            ("n13_运营部", "Panel_L1_n13_运营部", "运营部", "运营", "OPS"),
            ("n14_市场销售部", "Panel_L1_n14_市场销售部", "市场销售部", "市场", "MKT"),
            ("n15_财务部", "Panel_L1_n15_财务部", "财务部", "财务", "FIN"),
            ("n16_战略发展部", "Panel_L1_n16_战略发展部", "战略发展部", "战略", "STG"),
            ("n17_机务工程部", "Panel_L1_n17_机务工程部", "机务工程部", "机务", "MNT"),
            ("n18_人力资源部", "Panel_L1_n18_人力资源部", "人力资源部", "人力", "HR"),
            ("n19_安全质量部", "Panel_L1_n19_安全质量部", "安全质量部", "安全", "SAF"),
        };

        /// <summary>CEO 视角底栏：主页 + 收件箱（核心）+ 7 块。运营/市场等部门页降级，不再常驻底栏。</summary>
        public readonly struct ShellTabDef
        {
            public readonly string pageId;
            public readonly string label;
            public readonly float weight;
            public readonly bool core;

            public ShellTabDef(string pageId, string label, float weight = 1f, bool core = false)
            {
                this.pageId = pageId;
                this.label = label;
                this.weight = weight;
                this.core = core;
            }
        }

        public static readonly ShellTabDef[] ShellTabs =
        {
            new ShellTabDef("n6_主页_Dashboard", "主页"),
            new ShellTabDef("n7_收件箱", "收件箱"),
            new ShellTabDef("n12_航线网络", "航线"),
            new ShellTabDef("n9_机队", "机队"),
            new ShellTabDef("n18_人力资源部", "人员"),
            new ShellTabDef("n17_机务工程部", "维护"),
            new ShellTabDef("n15_财务部", "财务"),
            new ShellTabDef("n87_培训与发展", "培训"),
            new ShellTabDef("n16_战略发展部", "愿景"),
        };
    }
}
