using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>单个数据覆盖项：key → value（value 为字符串，读取时按需转 float/int）。</summary>
    [Serializable]
    public class ModDataEntry
    {
        public string key;
        public string value;
    }

    /// <summary>Mod 清单（manifest.json）。Unity 侧用 JsonUtility 反序列化。</summary>
    [Serializable]
    public class ModManifest
    {
        public string id;
        public string name;
        public string version;
        /// <summary>type：core（核心）/ dlc（官方）/ data·content·rule·full（社区，按字母序覆盖）。</summary>
        public string type = "data";
        public string description;
        public List<string> dependencies = new List<string>();
        public List<ModDataEntry> data = new List<ModDataEntry>();
    }

    /// <summary>一处数据冲突：同 key 被两个 Mod 声明，后者覆盖前者。</summary>
    [Serializable]
    public class ModConflict
    {
        public string key;
        public string firstMod;
        public string secondMod;
    }

    /// <summary>
    /// Mod 注册表（纯 C#，GDB 第36章 Lv.1 数据 Mod）。
    /// 加载顺序：核心(0) → 官方DLC(1) → 社区(2，按 id 字母序)，后者覆盖前者；
    /// 冲突（同 key 多 Mod 声明）按覆盖顺序记录并提示，不中断。
    /// </summary>
    public static class ModRegistry
    {
        static readonly List<ModManifest> _mods = new List<ModManifest>();
        static readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> _keyOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly List<ModConflict> _conflicts = new List<ModConflict>();

        public static IReadOnlyList<ModManifest> LoadedMods => _mods;
        public static IReadOnlyList<ModConflict> Conflicts => _conflicts;
        public static int Count => _mods.Count;

        public static void Clear()
        {
            _mods.Clear();
            _overrides.Clear();
            _keyOwner.Clear();
            _conflicts.Clear();
        }

        static int Weight(string type)
        {
            switch (type)
            {
                case "core": return 0;
                case "dlc": return 1;
                default: return 2;
            }
        }

        public static void Register(ModManifest m)
        {
            if (m == null || string.IsNullOrEmpty(m.id)) return;
            for (int i = 0; i < _mods.Count; i++)
                if (string.Equals(_mods[i].id, m.id, StringComparison.Ordinal)) return; // 去重
            _mods.Add(m);
        }

        /// <summary>按加载顺序重算覆盖表与冲突清单。ModManager 在全部 Register 后调用一次。</summary>
        public static void Rebuild()
        {
            _overrides.Clear();
            _keyOwner.Clear();
            _conflicts.Clear();

            var sorted = _mods
                .OrderBy(m => Weight(m.type))
                .ThenBy(m => m.id, StringComparer.Ordinal)
                .ToList();

            foreach (var m in sorted)
            {
                if (m.data == null) continue;
                foreach (var d in m.data)
                {
                    if (d == null || string.IsNullOrEmpty(d.key)) continue;
                    if (_keyOwner.TryGetValue(d.key, out var owner))
                        _conflicts.Add(new ModConflict { key = d.key, firstMod = owner, secondMod = m.id });
                    _overrides[d.key] = d.value;
                    _keyOwner[d.key] = m.id;
                }
            }
        }

        public static bool TryGetParam(string key, out string value) => _overrides.TryGetValue(key, out value);

        public static string GetParam(string key, string fallback) =>
            _overrides.TryGetValue(key, out var v) ? v : fallback;

        public static float GetFloat(string key, float fallback)
        {
            if (!_overrides.TryGetValue(key, out var v)) return fallback;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
        }

        public static int GetInt(string key, int fallback)
        {
            if (!_overrides.TryGetValue(key, out var v)) return fallback;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
        }
    }
}
