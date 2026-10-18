#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 生产后置（GDB 36/38）编辑器调试：重载 Mod、遥测落盘/清空。
    /// Mod 开发目录：项目 Assets/Mods/&lt;mod_id&gt;/manifest.json。
    /// </summary>
    public static class PostProductionDebugMenu
    {
        [MenuItem("AirlineCEO/Post-Production/Reload Mods")]
        public static void ReloadMods()
        {
            int count = ModManager.LoadAll();
            string msg = $"已加载 {count} 个 Mod。\n冲突 {ModRegistry.Conflicts.Count} 处。";
            foreach (var m in ModRegistry.LoadedMods)
                Debug.Log($"[Mod] {m.id} v{m.version} type={m.type} — {m.name}");
            foreach (var c in ModRegistry.Conflicts)
                Debug.LogWarning($"[Mod] 冲突 key={c.key}：{c.firstMod} 被 {c.secondMod} 覆盖");
            EditorUtility.DisplayDialog("Post-Production / Mods", msg, "确定");
        }

        [MenuItem("AirlineCEO/Post-Production/Flush Telemetry to File")]
        public static void FlushTelemetry()
        {
            string path = TelemetryManager.FlushToFile();
            string msg = string.IsNullOrEmpty(path)
                ? "遥测写盘失败（详见 Console）。"
                : $"已写入 {path}\n共 {Telemetry.Count} 条事件。";
            EditorUtility.DisplayDialog("Post-Production / Telemetry", msg, "确定");
        }

        [MenuItem("AirlineCEO/Post-Production/Clear Telemetry Buffer")]
        public static void ClearTelemetry()
        {
            Telemetry.Clear();
            EditorUtility.DisplayDialog("Post-Production / Telemetry", "遥测缓冲已清空。", "确定");
        }
    }
}
#endif
