using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 白板一页一资产（Phase 0.2）。pageId 与交互白板 CATALOG.id 对齐。
    /// </summary>
    [CreateAssetMenu(fileName = "UIConfig", menuName = "AirlineCEO/UI/UI Config", order = 100)]
    public class UIConfig : ScriptableObject
    {
        [Tooltip("白板完整 id，如 n0_主界面框架_常驻")]
        public string pageId;

        public string title;
        public string level;          // L0 / L1 / L2
        public string parentId;       // 上级 pageId，可空
        public string tabGroup;       // 底栏 Tab 分组，可空
        public string badgeSource;    // 角标数据源键，可空
        public string badge;
        [TextArea(2, 4)] public string meta;

        [Tooltip("可选：对应 Prefab（Phase 1 起填充）")]
        public GameObject prefab;
    }
}
