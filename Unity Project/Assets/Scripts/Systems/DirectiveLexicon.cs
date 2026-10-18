using System;
using System.Collections.Generic;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 运力指令「记忆」：把 CEO 说过的原话记成关键词。
    ///
    /// 要解决的问题（见设计文档 §14）：
    ///   关键词表是**封闭**的（写死在 <see cref="CapacityDirectiveLogic"/> 里的几个 string[]），
    ///   而分析服务会因未配 Key / 请求过频 / 超时 / 返回非法 JSON 而**静默缺席**。
    ///   于是「把班次收一收」这种说法，AI 在线时读得对，AI 一掉线就认不出。
    ///
    /// 机制（三态，**只有「已确认」才参与解析**）：
    ///   1. <see cref="Stage"/>   —— 执行时把「原话 + 分析服务读出的含义」挂起（仅内存，退出即弃）
    ///   2. <see cref="Promote"/> —— CEO 在收件箱**批准**该呈报 → 落盘，此后这句话就算关键词了
    ///   3. <see cref="Discard"/> —— CEO **驳回**该呈报 → 丢弃（读错了，不能让它变成"记忆"）
    ///
    /// 为什么必须经过批准：记忆会让一句话**压过关键词猜测**（<see cref="CapacityDirectiveLogic.Parse"/>
    /// 里记忆优先）。若从「执行过」就学，一次误读会被固化成永久词汇，比不学更糟。
    /// 批准是玩家给出的、唯一明确的「这个理解是对的」信号。
    ///
    /// 只记句子、不做泛化：学到的是「把班次收一收」这七个字，换个说法不认 ——
    /// 泛化是语义层（分析服务）的活，记忆只负责别再忘。
    /// </summary>
    public static class DirectiveLexicon
    {
        const string Key = "aceo.directive_lexicon.v1";

        /// <summary>上限：一句话一条，超过就丢最早的。防止长期游玩后 PlayerPrefs 无限膨胀。</summary>
        const int MaxEntries = 200;

        /// <summary>太短的原话不值得记（「加密」「+1」这类本来就命中关键词，学了也没用）。</summary>
        const int MinRawLength = 3;

        [Serializable]
        public class Entry
        {
            public string raw = "";        // 已归一化（CapacityDirectiveLogic.NormalizeRaw）
            public string intent = "increase";
            public int waves;
            public string depsCsv = "";
            public string days = "";
            public bool buildRing = true;
            public bool maximize;
        }

        [Serializable]
        public class Blob
        {
            public List<Entry> items = new List<Entry>();
        }

        static List<Entry> _entries;
        static bool _registered;

        // 挂起区：只活在本次运行里。退出游戏没批准 → 忘掉，不落盘（宁可不学，不可学错）。
        static readonly Dictionary<string, CapacityDirectiveLogic.LearnedDirective> _pending =
            new Dictionary<string, CapacityDirectiveLogic.LearnedDirective>(StringComparer.Ordinal);

        /// <summary>
        /// 注册记忆查询。用 [RuntimeInitializeOnLoadMethod] 而不是改启动流程 ——
        /// 这个机制是解析链路的旁挂件，不该要求任何人记得在 bootstrap 里调它。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // 编辑器中禁用域重载时静态字段会跨次运行残留，重载一遍免得读到上一局的记忆。
            _entries = null;
            _pending.Clear();
            _registered = false;
            Init();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器域加载时也注册一次。
        ///
        /// 必要性（由 R20 自测当场暴露）：<see cref="RuntimeInitializeOnLoadMethod"/> 只在**进入播放模式**
        /// 或构建产物里触发。而编辑器里有大量不进入播放模式的解析入口 —— 编辑器菜单驱动的流程、
        /// 以及 `LogicSelfTests`。没有这一条，那些路径下记忆**静默失效**：`Promote` 照样返回 true、
        /// 条数照样 +1，只有 `Parse` 永远查不到（因为查询根本没挂上），
        /// 表现为「明明学会了却还是认不出」，且不报任何错。
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void BootEditor() => Boot();
#endif

        /// <summary>
        /// 幂等初始化并注册记忆查询。除上述两个自动钩子外，指令入口也会显式调一次 ——
        /// 两条钩子覆盖了「播放模式/构建」与「编辑器域加载」，显式调用是第三重保险。
        /// </summary>
        public static void Init() => EnsureRegistered();

        static void EnsureRegistered()
        {
            EnsureLoaded();
            if (_registered) return;
            CapacityDirectiveLogic.SetLearnedLookup(Lookup);
            _registered = true;
        }

        static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new List<Entry>();
            try
            {
                var blob = JsonUtility.FromJson<Blob>(PlayerPrefs.GetString(Key, ""));
                if (blob?.items != null) _entries = blob.items;
            }
            catch
            {
                // 存档脏数据不抛异常：记忆坏了顶多退回关键词兜底，不该连累解析链路。
                _entries = new List<Entry>();
            }
        }

        static bool Lookup(string normalizedRaw, out CapacityDirectiveLogic.LearnedDirective learned)
        {
            learned = null;
            if (string.IsNullOrEmpty(normalizedRaw)) return false;
            EnsureLoaded();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e == null || !string.Equals(e.raw, normalizedRaw, StringComparison.Ordinal)) continue;
                learned = new CapacityDirectiveLogic.LearnedDirective
                {
                    intent = e.intent,
                    waves = e.waves,
                    deps = SplitDeps(e.depsCsv),
                    days = e.days,
                    buildRing = e.buildRing,
                    maximize = e.maximize
                };
                return true;
            }
            return false;
        }

        /// <summary>
        /// 挂起一条候选（执行时调用）。只有**分析服务读过**的指令才值得挂起 ——
        /// 关键词兜底读出来的结果本来就在词表里，学了也不会让认知变宽。
        /// </summary>
        public static void Stage(string raw, CapacityDirectiveLogic.Directive d, bool fromAi)
        {
            Init();
            if (!fromAi || d == null || !d.intentKnown) return;
            string norm = CapacityDirectiveLogic.NormalizeRaw(raw);
            if (norm.Length < MinRawLength) return;
            _pending[norm] = new CapacityDirectiveLogic.LearnedDirective
            {
                intent = d.intent,
                waves = d.waves,
                deps = d.deps,
                days = d.days ?? "",
                buildRing = d.buildRing,
                maximize = d.maximize
            };
        }

        /// <summary>CEO 批准呈报 → 把挂起的候选变成正式词汇。返回是否真的学到了新东西。</summary>
        public static bool Promote(string raw)
        {
            Init();
            string norm = CapacityDirectiveLogic.NormalizeRaw(raw);
            if (norm.Length < MinRawLength) return false;
            if (!_pending.TryGetValue(norm, out var learned) || learned == null) return false;
            _pending.Remove(norm);

            EnsureLoaded();
            // 关键词表本来就认得的说法不必占用记忆槽（词表改了反而该跟着变）。
            // 注意排除 fromLearned：否则「同一句再确认一次」会被当成「词表已认得」而拒绝更新。
            var probe = CapacityDirectiveLogic.Parse(norm);
            if (probe.intentKnown && !probe.fromLearned) return false;

            var entry = FromLearned(norm, learned);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && _entries[i].raw == norm) { _entries[i] = entry; return true; }
            }
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Save();
            return true;
        }

        /// <summary>CEO 驳回呈报 → 丢弃候选；若这句话此前已被记住，一并抹掉（那次学错了）。</summary>
        public static void Discard(string raw)
        {
            Init();
            string norm = CapacityDirectiveLogic.NormalizeRaw(raw);
            if (norm.Length == 0) return;
            _pending.Remove(norm);
            EnsureLoaded();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i] != null && _entries[i].raw == norm) { _entries.RemoveAt(i); Save(); return; }
            }
        }

        static Entry FromLearned(string norm, CapacityDirectiveLogic.LearnedDirective learned)
        {
            return new Entry
            {
                raw = norm,
                intent = string.IsNullOrEmpty(learned.intent) ? "increase" : learned.intent,
                waves = learned.waves,
                depsCsv = learned.deps != null ? string.Join(",", learned.deps) : "",
                days = learned.days ?? "",
                buildRing = learned.buildRing,
                maximize = learned.maximize
            };
        }

        static void Save()
        {
            try
            {
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Blob { items = _entries }));
                PlayerPrefs.Save();
            }
            catch { /* 落盘失败不影响本次运行 */ }
        }

        static string[] SplitDeps(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            var parts = csv.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length >= 4 && p.IndexOf(':') > 0) list.Add(p);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        // ———— 调试/自测用 ————

        /// <summary>已记住的原话条数。</summary>
        public static int Count { get { EnsureLoaded(); return _entries.Count; } }

        /// <summary>挂起中的候选条数。</summary>
        public static int PendingCount => _pending.Count;

        /// <summary>清空记忆（含挂起）。自测与「我想让它重新学」用。</summary>
        public static void Clear()
        {
            Init();
            _pending.Clear();
            EnsureLoaded();
            _entries.Clear();
            Save();
        }

        /// <summary>当前记住的原话（诊断用）。</summary>
        public static List<string> Phrases()
        {
            EnsureLoaded();
            var list = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i] != null) list.Add(_entries[i].raw);
            return list;
        }

        /// <summary>挂起中的原话（诊断用）。</summary>
        public static List<string> PendingPhrases()
            => new List<string>(_pending.Keys);

        /// <summary>
        /// 强制从 PlayerPrefs 重新读一遍并重挂查询（**仅供自测**）。
        /// 用来验证「存进去的东西取得回来」——否则静态字段一直驻留内存，
        /// 落盘那一步写错了也看不出来（写对了/没写 在本次运行里表现完全一样）。
        /// </summary>
        public static void ReloadForTests()
        {
            _entries = null;
            _registered = false;
            Init();
        }
    }
}
