using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>Phase 12 · 随机事件抽取 / 效果 / 连锁。</summary>
    public class RandomEventManager : MonoBehaviour
    {
        public static RandomEventManager Instance { get; private set; }

        public event Action OnEventLogChanged;

        [Header("抽取")]
        [Range(0f, 1f)] public float triggerChance = 0.55f;

        /// <summary>本季是否已占用大油价事件槽（与 TryOilEvent 互斥）。</summary>
        public bool ConsumedOilSlotThisQuarter { get; private set; }

        public string LastTriggeredSummary { get; private set; } = "";

        private string _forceNextId;
        private bool _forceAsChainRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void BeginQuarter()
        {
            ConsumedOilSlotThisQuarter = false;
        }

        /// <summary>Debug：下一季或立即强制某事件。</summary>
        public void QueueForceEvent(string eventId, bool asChainRoot = false)
        {
            _forceNextId = eventId;
            _forceAsChainRoot = asChainRoot;
        }

        /// <summary>立即强制触发（Play 调试用）。</summary>
        public string ForceTriggerNow(string eventId, bool startChain = false)
        {
            var gs = GameState.Instance;
            if (gs == null) return "无 GameState";
            BeginQuarter();
            var def = RandomEventCatalog.Get(eventId);
            if (def == null) return $"未知事件：{eventId}";
            string msg = ApplyEvent(gs, def, fromChain: false, dampenNegatives: ShouldDampen(gs));
            if (startChain && !string.IsNullOrEmpty(def.chainEventId))
                ScheduleChain(gs, def);
            OnEventLogChanged?.Invoke();
            return msg;
        }

        public string ForceChainNow(string rootEventId)
        {
            return ForceTriggerNow(rootEventId, startChain: true);
        }

        /// <summary>每季由 TimeManager 调用。返回玩家可读文案；无事件则空串。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null) return "";
            EnsureState(gs);
            BeginQuarter();

            bool dampen = ShouldDampen(gs);
            string result = "";

            // 1) 到期连锁优先
            if (!string.IsNullOrEmpty(gs.pendingChainEventId))
            {
                if (gs.pendingChainQuarters > 0)
                    gs.pendingChainQuarters--;

                if (gs.pendingChainQuarters <= 0)
                {
                    var chainDef = RandomEventCatalog.Get(gs.pendingChainEventId);
                    gs.pendingChainEventId = null;
                    gs.pendingChainQuarters = 0;
                    if (chainDef != null)
                    {
                        result = ApplyEvent(gs, chainDef, fromChain: true, dampenNegatives: dampen);
                        if (!string.IsNullOrEmpty(chainDef.chainEventId))
                            ScheduleChain(gs, chainDef);
                        OnEventLogChanged?.Invoke();
                        return result;
                    }
                }
            }

            // 2) Debug 强制
            if (!string.IsNullOrEmpty(_forceNextId))
            {
                string id = _forceNextId;
                bool asRoot = _forceAsChainRoot;
                _forceNextId = null;
                _forceAsChainRoot = false;
                var forced = RandomEventCatalog.Get(id);
                if (forced != null)
                {
                    result = ApplyEvent(gs, forced, fromChain: false, dampenNegatives: dampen);
                    if (asRoot || !string.IsNullOrEmpty(forced.chainEventId))
                        ScheduleChain(gs, forced);
                    OnEventLogChanged?.Invoke();
                    return result;
                }
            }

            // 3) 随机 0～1（坏运保护 Pity Timer 优先）
            bool pity = gs.quartersSinceRedCrisis >= PityQuartersThreshold;
            float effectiveChance = triggerChance * (gs.fastModeActive ? 0.5f : 1f);
            if (!pity && UnityEngine.Random.value > effectiveChance)
            {
                gs.quartersSinceRedCrisis++;
                return "";
            }

            var pick = WeightedPick(gs, pity);
            if (pick == null)
            {
                if (!pity) gs.quartersSinceRedCrisis++;
                return "";
            }

            result = ApplyEvent(gs, pick, fromChain: false, dampenNegatives: dampen);
            if (!string.IsNullOrEmpty(pick.chainEventId))
                ScheduleChain(gs, pick);
            OnEventLogChanged?.Invoke();
            return result;
        }

        private static bool ShouldDampen(GameState gs)
        {
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            return runway < 3f;
        }

        private const int MaxPerCategoryPerYear = 2;
        private const int PityQuartersThreshold = 2;

        private RandomEventDef WeightedPick(GameState gs, bool forceNegative)
        {
            float total = 0f;
            var candidates = new List<RandomEventDef>();
            var all = RandomEventCatalog.All;
            var macro = MacroEconomyManager.Instance;
            float positiveMod = macro != null ? macro.GetPositiveEventWeightModifier() : 1f;
            float negativeMod = macro != null ? macro.GetNegativeEventWeightModifier() : 1f;

            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || e.weight <= 0.05f) continue;
                if (e.isOneShot && IsOneShotFired(gs, e.id)) continue;
                if (IsCategoryLimited(gs, e.category)) continue;
                if (forceNegative && !e.IsNegative()) continue;

                float adjustedWeight = e.weight;
                bool isNegative = e.IsNegative();
                adjustedWeight *= isNegative ? negativeMod : positiveMod;

                candidates.Add(e);
                total += adjustedWeight;
            }
            if (candidates.Count == 0 || total <= 0f) return null;

            float r = UnityEngine.Random.value * total;
            float acc = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var e = candidates[i];
                bool isNeg = e.IsNegative();
                acc += e.weight * (isNeg ? negativeMod : positiveMod);
                if (r <= acc) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private int GetCategoryCount(GameState gs, string category)
        {
            if (gs == null || string.IsNullOrEmpty(category)) return 0;
            if (gs.eventCategoryCounters == null) gs.eventCategoryCounters = new List<EventCategoryCounter>();
            for (int i = 0; i < gs.eventCategoryCounters.Count; i++)
            {
                var c = gs.eventCategoryCounters[i];
                if (c != null && c.category == category && c.fiscalYear == gs.year) return c.count;
            }
            return 0;
        }

        private bool IsCategoryLimited(GameState gs, string category)
        {
            return GetCategoryCount(gs, category) >= MaxPerCategoryPerYear;
        }

        private void IncrementCategory(GameState gs, string category)
        {
            if (gs == null || string.IsNullOrEmpty(category)) return;
            if (gs.eventCategoryCounters == null) gs.eventCategoryCounters = new List<EventCategoryCounter>();
            for (int i = 0; i < gs.eventCategoryCounters.Count; i++)
            {
                var c = gs.eventCategoryCounters[i];
                if (c != null && c.category == category && c.fiscalYear == gs.year)
                {
                    c.count++;
                    return;
                }
            }
            gs.eventCategoryCounters.Add(new EventCategoryCounter { category = category, fiscalYear = gs.year, count = 1 });
        }

        private void UpdateEventCounters(GameState gs, RandomEventDef def)
        {
            if (gs == null || def == null) return;
            if (def.IsNegative())
                gs.quartersSinceRedCrisis = 0;
            else
                gs.quartersSinceRedCrisis++;
            IncrementCategory(gs, def.category);
        }

        private void ScheduleChain(GameState gs, RandomEventDef from)
        {
            if (from == null || string.IsNullOrEmpty(from.chainEventId)) return;
            if (RandomEventCatalog.Get(from.chainEventId) == null) return;
            gs.pendingChainEventId = from.chainEventId;
            gs.pendingChainQuarters = Mathf.Max(1, from.chainDelayQuarters);
            Debug.Log($"[RandomEvent] 预约连锁 {from.chainEventId}，{gs.pendingChainQuarters} 季后");
        }

        private string ApplyEvent(GameState gs, RandomEventDef def, bool fromChain, bool dampenNegatives)
        {
            if (def.consumesOilSlot)
                ConsumedOilSlotThisQuarter = true;

            if (def.isOneShot)
                MarkOneShot(gs, def.id);

            var effectSb = new StringBuilder();
            if (def.effects != null)
            {
                for (int i = 0; i < def.effects.Length; i++)
                    ApplyOneEffect(gs, def.effects[i], dampenNegatives, effectSb);
            }

            string softNote = dampenNegatives ? "（现金偏紧，负面影响已略作克制）" : "";
            string chainTag = fromChain ? "【连锁】" : "【随机事件】";
            string summary =
                $"{chainTag}{def.title}\n{def.description}{softNote}\n{effectSb.ToString().TrimEnd()}";

            var entry = new RandomEventLogEntry
            {
                year = gs.year,
                quarter = gs.quarter,
                eventId = def.id,
                title = def.title,
                category = def.category,
                summary = summary,
                effectSummary = effectSb.ToString().TrimEnd()
            };
            if (gs.randomEventLog == null) gs.randomEventLog = new List<RandomEventLogEntry>();
            gs.randomEventLog.Add(entry);
            while (gs.randomEventLog.Count > 20)
                gs.randomEventLog.RemoveAt(0);

            gs.lastRandomEventSummary = summary;
            LastTriggeredSummary = summary;
            gs.AddDecisionLog($"{chainTag}{def.title}");
            Debug.Log($"[RandomEvent] {def.id} {def.title}");
            UpdateEventCounters(gs, def);
            gs.NotifyChanged();
            return summary;
        }

        private static void ApplyOneEffect(GameState gs, EventEffect fx, bool dampen, StringBuilder sb)
        {
            if (fx == null || string.IsNullOrEmpty(fx.target)) return;
            float v = fx.value;
            if (dampen && v < 0f)
                v *= 0.6f;

            string label;
            switch (fx.target)
            {
                case "cash":
                    gs.cash += v;
                    if (gs.cash < 0f)
                    {
                        gs.AddDecisionLog($"⚠ 随机事件致现金为负（{gs.cash:F1} 万），已强制归零。");
                        gs.cash = 0f;
                    }
                    label = $"现金 {(v >= 0 ? "+" : "")}{v:F0} 万";
                    break;
                case "brand":
                    gs.brandAwareness = Mathf.Clamp(gs.brandAwareness + v, 0f, 100f);
                    label = $"品牌认知 {(v >= 0 ? "+" : "")}{v:F0}";
                    break;
                case "satisfaction":
                    gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + v, 0f, 100f);
                    label = $"董事会满意度 {(v >= 0 ? "+" : "")}{v:F0}";
                    break;
                case "fuelPrice":
                    if (fx.isPercent)
                    {
                        float before = gs.fuelPricePerTon;
                        gs.fuelPricePerTon = Mathf.Max(400f, before * (1f + v / 100f));
                        label = $"航油 {(v >= 0 ? "+" : "")}{v:F0}%（{before:F0}→{gs.fuelPricePerTon:F0}）";
                    }
                    else
                    {
                        gs.fuelPricePerTon = Mathf.Max(400f, gs.fuelPricePerTon + v);
                        label = $"航油价 {(v >= 0 ? "+" : "")}{v:F0}";
                    }
                    break;
                case "loadFactor":
                    gs.loadFactorPercent = Mathf.Clamp(gs.loadFactorPercent + v, 25f, 95f);
                    label = $"客座率 {(v >= 0 ? "+" : "")}{v:F0}%";
                    break;
                case "onTime":
                    gs.onTimePercent = Mathf.Clamp(gs.onTimePercent + v, 40f, 99f);
                    label = $"准点率 {(v >= 0 ? "+" : "")}{v:F0}%";
                    break;
                case "safetyIndex":
                    gs.safetyIndex = Mathf.Clamp(gs.safetyIndex + v, 0f, 100f);
                    label = $"安全指数 {(v >= 0 ? "+" : "")}{v:F0}";
                    break;
                case "monthlyBurn":
                    gs.financeCrisisBurnBonus = Mathf.Max(0f, gs.financeCrisisBurnBonus + v);
                    label = v >= 0
                        ? $"月度开销临时 +{v:F0} 万"
                        : $"月度开销临时 {v:F0} 万";
                    break;
                default:
                    label = $"{fx.target} {v:F1}";
                    break;
            }
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(label);
        }

        private static void EnsureState(GameState gs)
        {
            if (gs.randomEventLog == null) gs.randomEventLog = new List<RandomEventLogEntry>();
            if (gs.randomEventOneShots == null) gs.randomEventOneShots = new List<string>();
            if (gs.eventCategoryCounters == null) gs.eventCategoryCounters = new List<EventCategoryCounter>();
        }

        private static bool IsOneShotFired(GameState gs, string id)
        {
            if (gs.randomEventOneShots == null) return false;
            return gs.randomEventOneShots.Contains(id);
        }

        private static void MarkOneShot(GameState gs, string id)
        {
            if (gs.randomEventOneShots == null) gs.randomEventOneShots = new List<string>();
            if (!gs.randomEventOneShots.Contains(id))
                gs.randomEventOneShots.Add(id);
        }

        public string ToJson()
        {
            var gs = GameState.Instance;
            if (gs == null) return "{}";
            EnsureState(gs);
            var blob = new RandomEventsSave
            {
                oneShotFiredIds = gs.randomEventOneShots != null ? gs.randomEventOneShots.ToArray() : Array.Empty<string>(),
                log = gs.randomEventLog != null ? gs.randomEventLog.ToArray() : Array.Empty<RandomEventLogEntry>(),
                pendingChainEventId = gs.pendingChainEventId,
                pendingChainQuarters = gs.pendingChainQuarters,
                lastTriggeredId = gs.randomEventLog != null && gs.randomEventLog.Count > 0
                    ? gs.randomEventLog[gs.randomEventLog.Count - 1].eventId
                    : "",
                lastTriggeredSummary = gs.lastRandomEventSummary ?? "",
                quartersSinceRedCrisis = gs.quartersSinceRedCrisis,
                categoryCounters = gs.eventCategoryCounters != null ? gs.eventCategoryCounters.ToArray() : Array.Empty<EventCategoryCounter>()
            };
            return JsonUtility.ToJson(blob);
        }

        public void FromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            EnsureState(gs);
            if (string.IsNullOrEmpty(json))
            {
                gs.randomEventLog = new List<RandomEventLogEntry>();
                gs.randomEventOneShots = new List<string>();
                gs.pendingChainEventId = null;
                gs.pendingChainQuarters = 0;
                gs.lastRandomEventSummary = "";
                gs.quartersSinceRedCrisis = 0;
                gs.eventCategoryCounters = new List<EventCategoryCounter>();
                OnEventLogChanged?.Invoke();
                return;
            }

            try
            {
                var blob = JsonUtility.FromJson<RandomEventsSave>(json);
                gs.randomEventOneShots = blob?.oneShotFiredIds != null
                    ? new List<string>(blob.oneShotFiredIds)
                    : new List<string>();
                gs.randomEventLog = blob?.log != null
                    ? new List<RandomEventLogEntry>(blob.log)
                    : new List<RandomEventLogEntry>();
                gs.pendingChainEventId = blob?.pendingChainEventId;
                gs.pendingChainQuarters = blob != null ? blob.pendingChainQuarters : 0;
                gs.lastRandomEventSummary = blob?.lastTriggeredSummary ?? "";
                gs.quartersSinceRedCrisis = blob != null ? blob.quartersSinceRedCrisis : 0;
                gs.eventCategoryCounters = blob?.categoryCounters != null
                    ? new List<EventCategoryCounter>(blob.categoryCounters)
                    : new List<EventCategoryCounter>();
                LastTriggeredSummary = gs.lastRandomEventSummary;
            }
            catch
            {
                gs.randomEventLog = new List<RandomEventLogEntry>();
                gs.randomEventOneShots = new List<string>();
                gs.quartersSinceRedCrisis = 0;
                gs.eventCategoryCounters = new List<EventCategoryCounter>();
            }
            OnEventLogChanged?.Invoke();
        }
    }
}
