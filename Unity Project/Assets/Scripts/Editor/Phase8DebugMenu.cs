#if UNITY_EDITOR
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 8 人事系统 Play 调试。</summary>
    public static class Phase8DebugMenu
    {
        [MenuItem("Debug/HR Force Resign OPS", false, 220)]
        public static void ForceResignOps()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("提示", "请先进入 Play 模式。", "确定");
                return;
            }

            if (DepartmentManager.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "DepartmentManager 未就绪。", "确定");
                return;
            }

            if (!DepartmentManager.Instance.IsAppointed("OPS"))
            {
                EditorUtility.DisplayDialog("提示", "请先在人事面板任命运营部负责人。", "确定");
                return;
            }

            DepartmentManager.Instance.DebugForceResign("OPS");
            Debug.Log("[Phase8] 已强制 OPS 负责人离职。");
        }

        [MenuItem("Debug/HR Run Year-End Loyalty", false, 221)]
        public static void RunYearEndLoyalty()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("提示", "请先进入 Play 模式。", "确定");
                return;
            }

            DepartmentManager.Instance?.OnYearEndLoyalty();
            Debug.Log("[Phase8] 已执行财年末忠诚度结算。");
        }
    }
}
#endif
