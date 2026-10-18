using System.Collections.Generic;
using System.IO;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 遥测接线（Unity 侧，GDB 第38章）：把游戏事件翻译成匿名 Telemetry 事件 + 落盘。
    /// 遵循匿名化原则，仅记录事件名/分类/数值键值，不含玩家身份信息。
    /// </summary>
    public static class TelemetryManager
    {
        [System.Serializable]
        class TelemetryDump
        {
            public List<TelemetryEvent> events;
        }

        public static void OnGameStarted(GameState gs)
        {
            if (gs == null) return;
            Telemetry.Track("game_start", "session", new Dictionary<string, string>
            {
                ["mode"] = ((int)gs.activeMode).ToString(),
                ["year"] = gs.year.ToString(),
                ["hub"] = gs.hubIata ?? "",
                ["ironman"] = gs.ironmanModeActive ? "1" : "0"
            });
        }

        public static void OnQuarterAdvanced(GameState gs)
        {
            if (gs == null) return;
            Telemetry.Track("quarter_advanced", "progress", new Dictionary<string, string>
            {
                ["year"] = gs.year.ToString(),
                ["quarter"] = gs.quarter.ToString(),
                ["cash"] = gs.cash.ToString("F0"),
                ["satisfaction"] = gs.boardSatisfaction.ToString("F0"),
                ["safety"] = gs.safetyRating.ToString()
            });
        }

        public static void OnDeptVisited(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            Telemetry.Track("dept_visited", "engagement", new Dictionary<string, string>
            {
                ["page"] = pageId
            });
        }

        /// <summary>把缓冲的遥测事件写入 JSON 文件，返回文件路径（失败返回空串）。</summary>
        public static string FlushToFile(string path = null)
        {
            var snap = Telemetry.Snapshot;
            var dump = new TelemetryDump { events = new List<TelemetryEvent>(snap) };

            string json;
            try { json = JsonUtility.ToJson(dump, true); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TelemetryManager] 序列化失败：{e.Message}");
                return "";
            }

            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Application.persistentDataPath, "telemetry.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TelemetryManager] 写盘失败：{e.Message}");
                return "";
            }
        }
    }
}
