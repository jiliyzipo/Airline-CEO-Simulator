#if UNITY_EDITOR
using System.Text;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// Phase 4 遗留（第 431 行待办）：存档体积手测 + 编辑器内性能 Profile（年闭环计时）。
    /// 菜单：AirlineCEO → Phase 4 → …
    /// </summary>
    public static class Phase4ProfileMenu
    {
        [MenuItem("AirlineCEO/Phase 4/存档体积检测", false, 20)]
        public static void MeasureSaveSize()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("存档体积检测",
                    "请在 Play 模式下使用（需 GameState/SaveManager 运行时实例）。", "确定");
                return;
            }

            var sm = SaveManager.Instance;
            if (sm == null)
            {
                EditorUtility.DisplayDialog("存档体积检测", "未找到 SaveManager 实例。", "确定");
                return;
            }

            var sw = Stopwatch.StartNew();
            sm.Save();
            sw.Stop();

            long totalBytes = 0;
            int stringKeys = 0;
            int scalarKeys = 0;
            long largestBytes = 0;
            string largestKey = "";
            var detail = new StringBuilder();

            foreach (var key in SaveManager.AllSaveKeys)
            {
                string s = PlayerPrefs.GetString(key, "");
                if (!string.IsNullOrEmpty(s))
                {
                    int b = Encoding.UTF8.GetByteCount(s);
                    totalBytes += b;
                    stringKeys++;
                    if (b > largestBytes) { largestBytes = b; largestKey = key; }
                    if (b >= 512) detail.AppendLine($"  {key}: {b} B");
                }
                else if (PlayerPrefs.HasKey(key))
                {
                    totalBytes += 4; // int/float 标量
                    scalarKeys++;
                }
            }

            string summary =
                $"存档体积：{totalBytes} B = {totalBytes / 1024.0f:F2} KB\n" +
                $"字符串键 {stringKeys} 个 · 标量键 {scalarKeys} 个 · 共 {stringKeys + scalarKeys} 键\n" +
                $"Save() 单次耗时 {sw.ElapsedMilliseconds} ms\n" +
                $"最大单键 {largestKey} = {largestBytes} B";
            if (detail.Length > 0)
                summary += "\n\n≥512B 的大键：\n" + detail.ToString();

            Debug.Log("[Phase4] " + summary.Replace("\n", "\n[Phase4] "));
            EditorUtility.DisplayDialog("存档体积检测", summary, "确定");
        }

        [MenuItem("AirlineCEO/Phase 4/性能 Profile · 年闭环计时", false, 21)]
        public static void ProfileYearLoop()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("性能 Profile",
                    "请在 Play 模式下使用（需 TimeManager/GameState 运行时实例）。", "确定");
                return;
            }

            var tm = TimeManager.Instance;
            var gs = GameState.Instance;
            if (tm == null || gs == null)
            {
                EditorUtility.DisplayDialog("性能 Profile", "未找到 TimeManager/GameState 实例。", "确定");
                return;
            }

            // 不实际推进；正式计时从下方循环开始
            const int quarters = 4; // 1 个财年
            long totalMs = 0;
            long maxMs = 0;
            var perQuarter = new StringBuilder();

            for (int i = 0; i < quarters; i++)
            {
                if (gs.gameOver) break;

                var qSw = Stopwatch.StartNew();
                var result = tm.AdvanceQuarter();
                qSw.Stop();

                long ms = qSw.ElapsedMilliseconds;
                totalMs += ms;
                if (ms > maxMs) maxMs = ms;
                perQuarter.AppendLine(
                    $"  Q{i + 1}（{result.fromYear}-{result.fromQuarter}→{result.toYear}-{result.toQuarter}）：{ms} ms{(result.success ? "" : "（失败）")}");
            }

            double avg = quarters > 0 ? totalMs / (double)quarters : 0;
            string summary =
                $"年闭环（{quarters} 季度）总耗时 {totalMs} ms · 平均 {avg:F1} ms/季 · 峰值 {maxMs} ms\n" +
                perQuarter.ToString();

            Debug.Log("[Phase4] 性能 Profile（含季度结算/消息/遥测/自动存档）：\n" + summary);
            EditorUtility.DisplayDialog("性能 Profile · 年闭环计时", summary, "确定");
        }
    }
}
#endif
