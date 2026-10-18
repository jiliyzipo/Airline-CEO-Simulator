using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>Phase 11 · AI 航司生成 / 状态机 / 扩张定价 / 破产。</summary>
    public class AIManager : MonoBehaviour
    {
        public static AIManager Instance { get; private set; }

        public event Action OnAiChanged;

        private static readonly string[] PoolNames =
        {
            "云翼航空", "青鸾航空", "星轨航空", "远洋航空", "银燕航空", "北辰货运"
        };

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

        private void Start() => EnsureAiGenerated();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void EnsureAiGenerated()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (gs.aiAirlines == null) gs.aiAirlines = new List<AIAirline>();
            bool seeded = gs.aiAirlines.Count == 0;
            if (seeded)
            {
                gs.aiAirlines.Add(MakeAi("AI-01", "云翼航空", AIAirlineTypes.FullService, 2800f, 12, true,
                    new[] { "北京-上海", "北京-广州", "上海-伦敦" }));
                gs.aiAirlines.Add(MakeAi("AI-02", "青鸾航空", AIAirlineTypes.LowCost, 1600f, 18, true,
                    new[] { "北京-成都", "上海-深圳", "广州-杭州" }));
                gs.aiAirlines.Add(MakeAi("AI-03", "银燕航空", AIAirlineTypes.Regional, 900f, 10, false,
                    new[] { "昆明-丽江", "成都-九寨", "西安-银川" }));
                gs.aiAirlines.Add(MakeAi("AI-04", "北辰货运", AIAirlineTypes.Cargo, 1200f, 8, false,
                    new[] { "上海-法兰克福货", "广州-安克雷奇货" }));
                gs.brandAwareness = 70f;
                gs.nextAiSpawnYear = 0;
            }
            bool named = EnsureNamedCarrier(gs, "AI-SQ", "新加坡航空", AIAirlineTypes.FullService, 5200f, 132,
                new[] { "新加坡-伦敦", "新加坡-东京", "新加坡-悉尼", "新加坡-上海" });
            if (!seeded && !named) return;
            Debug.Log($"[AIManager] 已生成 AI 航司 {gs.aiAirlines.Count} 家。");
            OnAiChanged?.Invoke();
        }

        static bool EnsureNamedCarrier(GameState gs, string id, string name, string type, float cash, int fleet,
            string[] routes)
        {
            for (int i = 0; i < gs.aiAirlines.Count; i++)
            {
                var a = gs.aiAirlines[i];
                if (a == null) continue;
                if (string.Equals(a.id, id, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(a.name, name, StringComparison.Ordinal)) return false;
            }
            gs.aiAirlines.Add(MakeAi(id, name, type, cash, fleet, false, routes));
            return true;
        }

        private static AIAirline MakeAi(string id, string name, string type, float cash, int fleet,
            bool predator, string[] routes)
        {
            return new AIAirline
            {
                id = id,
                name = name,
                type = type,
                state = AIAirlineStates.Stable,
                cash = cash,
                fleetSize = fleet,
                isPredator = predator,
                isAlive = true,
                routes = routes ?? Array.Empty<string>(),
                avgFare = type == AIAirlineTypes.LowCost ? 78f : type == AIAirlineTypes.Cargo ? 120f : 105f,
                reputation = type == AIAirlineTypes.FullService ? 75 : 58,
                debtRatio = UnityEngine.Random.Range(0.25f, 0.45f),
                expansionTimer = UnityEngine.Random.Range(0, 2)
            };
        }

        /// <summary>每季度：状态评估 → 行为 → 定价。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null) return "";
            EnsureAiGenerated();

            var sb = new StringBuilder();

            for (int i = 0; i < gs.aiAirlines.Count; i++)
            {
                var ai = gs.aiAirlines[i];
                if (ai == null || !ai.isAlive) continue;

                // 简易季度现金流动
                float burn = Mathf.Max(40f, ai.fleetSize * 8f);
                float income = ai.routes != null ? ai.routes.Length * 55f : 40f;
                if (ai.state == AIAirlineStates.Dying) income *= 0.5f;
                if (ai.state == AIAirlineStates.Defensive) income *= 0.85f;
                ai.cash += income - burn;
                ai.debtRatio = Mathf.Clamp01(ai.debtRatio + UnityEngine.Random.Range(-0.03f, 0.04f));

                string old = ai.state;
                EvaluateState(ai, burn);
                if (old != ai.state)
                {
                    sb.AppendLine($"[{ai.name}] 状态：{AIAirlineStates.DisplayName(old)} → {AIAirlineStates.DisplayName(ai.state)}");
                    Debug.Log($"[AIManager] {ai.name} 状态切换 {old} → {ai.state}");
                }

                ApplyBehavior(gs, ai, sb);
                UpdatePricing(gs, ai);

                if (ai.state == AIAirlineStates.Dying)
                {
                    ai.dyingQuarters++;
                    if (ai.dyingQuarters >= 1) // ≈ 一季≈90天，MVP 按一季清算
                    {
                        Bankrupt(gs, ai, sb);
                    }
                }
            }

            TryRespawn(gs, sb);
            OnAiChanged?.Invoke();
            return sb.ToString().TrimEnd();
        }

        private static void EvaluateState(AIAirline ai, float monthlyBurn)
        {
            float months = monthlyBurn > 0.01f ? ai.cash / monthlyBurn : 99f;

            if (ai.cash <= 0f)
            {
                ai.state = AIAirlineStates.Dying;
                return;
            }

            if (ai.debtRatio >= 0.70f || months <= 6f)
            {
                ai.state = AIAirlineStates.Defensive;
                ai.dyingQuarters = 0;
                return;
            }

            if (months >= 20f && ai.isPredator)
            {
                ai.state = AIAirlineStates.Expanding;
                ai.dyingQuarters = 0;
                return;
            }

            if (months >= 15f)
            {
                ai.state = AIAirlineStates.Stable;
                ai.dyingQuarters = 0;
            }
            else
            {
                ai.state = AIAirlineStates.Defensive;
                ai.dyingQuarters = 0;
            }
        }

        private void ApplyBehavior(GameState gs, AIAirline ai, StringBuilder sb)
        {
            if (ai.expansionTimer > 0) ai.expansionTimer--;

            var macro = MacroEconomyManager.Instance;
            float expandMod = macro != null ? macro.GetAiExpansionModifier() : 1f;
            float bankruptMod = macro != null ? macro.GetAiBankruptcyModifier() : 1f;

            switch (ai.state)
            {
                case AIAirlineStates.Expanding:
                    if (ai.expansionTimer <= 0 && UnityEngine.Random.value < 0.55f * expandMod)
                        ExpandAgainstPlayer(gs, ai, sb);
                    break;
                case AIAirlineStates.Stable:
                    if (ai.expansionTimer <= 0 && UnityEngine.Random.value < 0.20f * expandMod)
                        ExpandGeneric(ai, sb);
                    break;
                case AIAirlineStates.Defensive:
                    CutRoutes(ai, 0.30f, sb);
                    if (ai.fleetSize > 4 && UnityEngine.Random.value < 0.4f)
                    {
                        ai.fleetSize -= 1;
                        ai.cash += 80f;
                        sb.AppendLine($"[{ai.name}] 防御：抛售飞机，现金 +80 万");
                    }
                    break;
                case AIAirlineStates.Dying:
                    ai.avgFare *= 0.5f;
                    sb.AppendLine($"[{ai.name}] 垂死：票价打 5 折，寻求接盘");
                    // 衰退期破产加速：垂死季数门槛降低
                    ai.dyingQuarters += Mathf.RoundToInt(bankruptMod - 1f);
                    if (CompetitionManager.Instance != null)
                        CompetitionManager.Instance.TryOfferCodeShareFromDying(ai);
                    break;
            }
        }

        private void ExpandAgainstPlayer(GameState gs, AIAirline ai, StringBuilder sb)
        {
            if (gs.activeRoutes == null || gs.activeRoutes.Count == 0)
            {
                ExpandGeneric(ai, sb);
                return;
            }

            // Top 盈利航线
            RouteData best = gs.activeRoutes[0];
            for (int i = 1; i < gs.activeRoutes.Count; i++)
            {
                if (gs.activeRoutes[i].dailyProfit > best.dailyProfit)
                    best = gs.activeRoutes[i];
            }

            string key = $"{best.origin}-{best.destination}";
            AddRoute(ai, key);
            ai.weeklyFreqOnOverlap = Mathf.Max(ai.weeklyFreqOnOverlap, 10f);
            ai.fleetSize += 1;
            ai.cash -= 120f;
            ai.expansionTimer = 1;
            ai.avgFare *= 0.95f;
            sb.AppendLine($"⚠ [{ai.name}] 进攻：向 {key} 投入运力并降价 5%");
            gs.AddDecisionLog($"竞对 {ai.name} 加大对 {key} 的运力投放。");
        }

        private static void ExpandGeneric(AIAirline ai, StringBuilder sb)
        {
            string[] pool =
            {
                "北京-深圳", "上海-成都", "广州-重庆", "杭州-西安", "南京-昆明"
            };
            string pick = pool[UnityEngine.Random.Range(0, pool.Length)];
            AddRoute(ai, pick);
            ai.fleetSize += 1;
            ai.cash -= 90f;
            ai.expansionTimer = 2;
            sb.AppendLine($"[{ai.name}] 扩张：开通 {pick}");
        }

        private static void CutRoutes(AIAirline ai, float ratio, StringBuilder sb)
        {
            if (ai.routes == null || ai.routes.Length == 0) return;
            int cut = Mathf.Max(1, Mathf.RoundToInt(ai.routes.Length * ratio));
            int keep = Mathf.Max(1, ai.routes.Length - cut);
            var list = new List<string>(ai.routes);
            while (list.Count > keep)
                list.RemoveAt(list.Count - 1);
            ai.routes = list.ToArray();
            sb.AppendLine($"[{ai.name}] 防御：停飞部分亏损航线，余 {ai.routes.Length} 条");
        }

        private static void AddRoute(AIAirline ai, string route)
        {
            var list = new List<string>();
            if (ai.routes != null) list.AddRange(ai.routes);
            if (!list.Contains(route)) list.Add(route);
            ai.routes = list.ToArray();
        }

        private static void UpdatePricing(GameState gs, AIAirline ai)
        {
            float playerRef = 100f;
            if (ai.type == AIAirlineTypes.LowCost)
                ai.avgFare = playerRef * UnityEngine.Random.Range(0.85f, 0.92f);
            else if (ai.type == AIAirlineTypes.FullService)
                ai.avgFare = playerRef * 1.05f;
            else if (ai.state == AIAirlineStates.Dying)
                ai.avgFare = playerRef * 0.5f;
        }

        private void Bankrupt(GameState gs, AIAirline ai, StringBuilder sb)
        {
            ai.isAlive = false;
            ai.state = AIAirlineStates.Dying;
            ai.bankruptYear = gs.year;
            ai.routes = Array.Empty<string>();
            ai.askUnits = 0f;
            ai.marketShare = 0f;
            gs.nextAiSpawnYear = Mathf.Max(gs.nextAiSpawnYear, gs.year + 2);
            sb.AppendLine($"💀 [{ai.name}] 破产清算，退出市场。");
            gs.AddDecisionLog($"竞对 {ai.name} 破产退出。");
            Debug.Log($"[AIManager] {ai.name} 破产");
        }

        private void TryRespawn(GameState gs, StringBuilder sb)
        {
            if (gs.nextAiSpawnYear <= 0 || gs.year < gs.nextAiSpawnYear) return;

            // 找一个已死的槽位复活，或新增
            AIAirline slot = null;
            for (int i = 0; i < gs.aiAirlines.Count; i++)
            {
                if (gs.aiAirlines[i] != null && !gs.aiAirlines[i].isAlive)
                {
                    slot = gs.aiAirlines[i];
                    break;
                }
            }

            string name = PoolNames[UnityEngine.Random.Range(0, PoolNames.Length)];
            string type = new[]
            {
                AIAirlineTypes.FullService, AIAirlineTypes.LowCost,
                AIAirlineTypes.Regional, AIAirlineTypes.Cargo
            }[UnityEngine.Random.Range(0, 4)];

            if (slot != null)
            {
                slot.name = name + "（新）";
                slot.type = type;
                slot.isAlive = true;
                slot.cash = 1100f;
                slot.fleetSize = 6;
                slot.state = AIAirlineStates.Stable;
                slot.dyingQuarters = 0;
                slot.debtRatio = 0.3f;
                slot.routes = new[] { "北京-武汉", "上海-青岛" };
                slot.isPredator = type == AIAirlineTypes.LowCost || type == AIAirlineTypes.FullService;
            }
            else if (gs.aiAirlines.Count < 5)
            {
                gs.aiAirlines.Add(MakeAi($"AI-{gs.aiAirlines.Count + 1:00}", name, type, 1100f, 6,
                    type == AIAirlineTypes.LowCost, new[] { "北京-武汉" }));
            }

            gs.nextAiSpawnYear = 0;
            sb.AppendLine($"✈ 新竞对入场：{name}");
            Debug.Log($"[AIManager] 新 AI 入场 {name}");
        }

        #region Debug / Save

        public void DebugForceExpanding(string aiId = null)
        {
            EnsureAiGenerated();
            var ai = FindAlive(aiId);
            if (ai == null) return;
            ai.cash = Mathf.Max(ai.cash, 5000f);
            ai.isPredator = true;
            ai.debtRatio = 0.2f;
            ai.state = AIAirlineStates.Expanding;
            Debug.Log($"[AIManager] Debug 强制进攻：{ai.name}");
            OnAiChanged?.Invoke();
        }

        public void DebugForceDying(string aiId = null)
        {
            EnsureAiGenerated();
            var ai = FindAlive(aiId);
            if (ai == null) return;
            ai.cash = 0f;
            ai.state = AIAirlineStates.Dying;
            ai.dyingQuarters = 0;
            Debug.Log($"[AIManager] Debug 强制垂死：{ai.name}");
            OnAiChanged?.Invoke();
        }

        public void DebugKill(string aiId = null)
        {
            var gs = GameState.Instance;
            var ai = FindAlive(aiId);
            if (gs == null || ai == null) return;
            var sb = new StringBuilder();
            Bankrupt(gs, ai, sb);
            OnAiChanged?.Invoke();
        }

        private AIAirline FindAlive(string aiId)
        {
            var gs = GameState.Instance;
            if (gs?.aiAirlines == null) return null;
            for (int i = 0; i < gs.aiAirlines.Count; i++)
            {
                var a = gs.aiAirlines[i];
                if (a == null || !a.isAlive) continue;
                if (string.IsNullOrEmpty(aiId) || a.id == aiId) return a;
            }
            return null;
        }

        public string ToJson()
        {
            var gs = GameState.Instance;
            var blob = new AIAirlineListSave
            {
                items = gs?.aiAirlines != null ? gs.aiAirlines.ToArray() : Array.Empty<AIAirline>(),
                playerMarketShare = gs != null ? gs.playerMarketShare : 0f,
                brandAwareness = gs != null ? gs.brandAwareness : 70f,
                codeShareLoadBonus = gs != null ? gs.codeShareLoadBonus : 0f,
                codeShareActive = gs != null && gs.codeShareActive,
                codeSharePartnerId = gs?.codeSharePartnerId,
                lastCodeShareYear = gs != null ? gs.lastCodeShareYear : 0,
                pendingCodeShareFromId = gs?.pendingCodeShareFromId,
                lastCompetitionLog = gs?.lastCompetitionLog,
                playerShareHistory = gs?.playerShareHistory != null ? gs.playerShareHistory.ToArray() : Array.Empty<float>(),
                nextAiSpawnYear = gs != null ? gs.nextAiSpawnYear : 0
            };
            return JsonUtility.ToJson(blob);
        }

        public void FromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (string.IsNullOrEmpty(json))
            {
                gs.aiAirlines = new List<AIAirline>();
                EnsureAiGenerated();
                return;
            }

            try
            {
                var blob = JsonUtility.FromJson<AIAirlineListSave>(json);
                gs.aiAirlines = blob?.items != null
                    ? new List<AIAirline>(blob.items)
                    : new List<AIAirline>();
                gs.playerMarketShare = blob != null ? blob.playerMarketShare : 0f;
                gs.brandAwareness = blob != null ? blob.brandAwareness : 70f;
                gs.codeShareLoadBonus = blob != null ? blob.codeShareLoadBonus : 0f;
                gs.codeShareActive = blob != null && blob.codeShareActive;
                gs.codeSharePartnerId = blob?.codeSharePartnerId;
                gs.lastCodeShareYear = blob != null ? blob.lastCodeShareYear : 0;
                gs.pendingCodeShareFromId = blob?.pendingCodeShareFromId;
                gs.lastCompetitionLog = blob?.lastCompetitionLog;
                gs.nextAiSpawnYear = blob != null ? blob.nextAiSpawnYear : 0;
                gs.playerShareHistory = blob?.playerShareHistory != null
                    ? new List<float>(blob.playerShareHistory)
                    : new List<float>();
                if (gs.aiAirlines.Count == 0) EnsureAiGenerated();
            }
            catch
            {
                gs.aiAirlines = new List<AIAirline>();
                EnsureAiGenerated();
            }

            OnAiChanged?.Invoke();
        }

        #endregion
    }
}
