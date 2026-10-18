using System.Collections.Generic;
using System.IO;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Mod 加载器（Unity 侧，GDB 第36章 Lv.1 数据 Mod）。
    /// 扫描 Mods 目录（persistentDataPath 优先，editor 下附带 dataPath/Mods 开发目录），
    /// 读取 manifest.json（UnityEngine.JsonUtility）→ 注册 ModRegistry → 按加载顺序重算覆盖与冲突。
    /// </summary>
    public static class ModManager
    {
        public const string ManifestFile = "manifest.json";

        public static IReadOnlyList<ModManifest> LoadedMods => ModRegistry.LoadedMods;
        public static IReadOnlyList<ModConflict> Conflicts => ModRegistry.Conflicts;

        static List<string> ScanFolders()
        {
            var dirs = new List<string>();
            var roots = new List<string>
            {
                Path.Combine(Application.persistentDataPath, "Mods"),
                Path.Combine(Application.dataPath, "Mods")
            };

            var seen = new HashSet<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var subs = Directory.GetDirectories(root);
                foreach (var sub in subs)
                    if (seen.Add(Path.GetFullPath(sub)))
                        dirs.Add(sub);
            }
            return dirs;
        }

        public static int LoadAll()
        {
            ModRegistry.Clear();
            var dirs = ScanFolders();
            int loaded = 0;

            foreach (var dir in dirs)
            {
                string manifestPath = Path.Combine(dir, ManifestFile);
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonUtility.FromJson<ModManifest>(json);
                    if (manifest == null || string.IsNullOrEmpty(manifest.id)) continue;

                    ModRegistry.Register(manifest);
                    loaded++;
                    Telemetry.Track("mod_loaded", "mod", new Dictionary<string, string>
                    {
                        ["id"] = manifest.id,
                        ["version"] = manifest.version ?? "",
                        ["type"] = manifest.type ?? "data"
                    });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[ModManager] 解析失败 {manifestPath}：{e.Message}");
                }
            }

            ModRegistry.Rebuild();
            return loaded;
        }

        /// <summary>供数据 Mod 覆盖数值的统一入口（未覆盖返回默认值）。</summary>
        public static float GetFloat(string key, float fallback) => ModRegistry.GetFloat(key, fallback);
        public static int GetInt(string key, int fallback) => ModRegistry.GetInt(key, fallback);
        public static string GetParam(string key, string fallback) => ModRegistry.GetParam(key, fallback);
    }
}
