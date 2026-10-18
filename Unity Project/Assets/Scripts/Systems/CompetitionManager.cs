using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>Phase 11 · ASK 市场份额 / 重叠航线压力 / 代码共享。</summary>
    public class CompetitionManager : MonoBehaviour
    {
        public static CompetitionManager Instance { get; private set; }

        public event Action OnCompetitionUpdated;
        public event Action<CodeShareProposal> OnCodeShareProposed;

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

        /// <summary>季度：重算份额 + 竞争压力 + 偶发代码共享。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null) return "";
            if (AIManager.Instance != null) AIManager.Instance.EnsureAiGenerated();

            RecalculateMarketShares(gs);
            string pressure = ApplyCompetitionPressure(gs);
            string coop = MaybeProposeCodeShare(gs);

            if (gs.playerShareHistory == null) gs.playerShareHistory = new List<float>();
            gs.playerShareHistory.Add(gs.playerMarketShare);
            while (gs.playerShareHistory.Count > 16)
                gs.playerShareHistory.RemoveAt(0);

            OnCompetitionUpdated?.Invoke();

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(pressure)) sb.AppendLine(pressure);
            if (!string.IsNullOrEmpty(coop)) sb.AppendLine(coop);
            gs.lastCompetitionLog = sb.ToString().TrimEnd();
            return gs.lastCompetitionLog;
        }

        public void RecalculateMarketShares(GameState gs)
        {
            float playerAsk = EstimatePlayerAsk(gs);
            float total = playerAsk;

            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || !ai.isAlive)
                    {
                        if (ai != null) { ai.askUnits = 0f; ai.marketShare = 0f; }
                        continue;
                    }
                    ai.askUnits = EstimateAiAsk(ai);
                    total += ai.askUnits;
                }
            }

            if (total < 1f) total = 1f;
            gs.playerMarketShare = playerAsk / total * 100f;

            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || !ai.isAlive) continue;
                    ai.marketShare = ai.askUnits / total * 100f;
                }
            }
        }

        private static float EstimatePlayerAsk(GameState gs)
        {
            int seats = 0;
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null || !a.isActive) continue;
                    if (a.status == AircraftStatuses.Retired) continue;
                    int pax = 180;
                    if (DataBase.Instance != null)
                    {
                        var cat = DataBase.Instance.GetAircraftType(a.type);
                        if (cat != null) pax = Mathf.Max(80, cat.maxPassengers);
                    }
                    seats += pax;
                }
            }
            if (seats <= 0) seats = 180;
            int routes = gs.activeRoutes != null ? Mathf.Max(1, gs.activeRoutes.Count) : 1;
            // ASK ≈ 座位数 × 平均航距 × 周班 × 季周数（简化）
            return seats * 4500f * 7f * 13f * (routes / 3f);
        }

        private static float EstimateAiAsk(AIAirline ai)
        {
            int routes = ai.routes != null ? Mathf.Max(1, ai.routes.Length) : 1;
            int seats = Mathf.Max(80, ai.fleetSize * 150);
            float freq = Mathf.Clamp(ai.weeklyFreqOnOverlap, 3f, 14f);
            return seats * 4200f * freq * 13f * (routes / 4f);
        }

        /// <summary>重叠航线：玩家 load / brand 下降。</summary>
        private string ApplyCompetitionPressure(GameState gs)
        {
            if (gs.activeRoutes == null || gs.activeRoutes.Count == 0) return "";

            int overlaps = 0;
            float fareCutPressure = 0f;

            if (gs.aiAirlines != null)
            {
                for (int a = 0; a < gs.aiAirlines.Count; a++)
                {
                    var ai = gs.aiAirlines[a];
                    if (ai == null || !ai.isAlive || ai.routes == null) continue;

                    for (int r = 0; r < gs.activeRoutes.Count; r++)
                    {
                        var route = gs.activeRoutes[r];
                        string key = $"{route.origin}-{route.destination}";
                        string rev = $"{route.destination}-{route.origin}";
                        bool hit = false;
                        for (int k = 0; k < ai.routes.Length; k++)
                        {
                            if (ai.routes[k] == key || ai.routes[k] == rev)
                            {
                                hit = true;
                                break;
                            }
                        }
                        if (!hit) continue;
                        overlaps++;
                        if (ai.avgFare < 95f) fareCutPressure += (95f - ai.avgFare) * 0.002f;
                        if (ai.weeklyFreqOnOverlap > 7f)
                            fareCutPressure += 0.01f;
                    }
                }
            }

            if (overlaps <= 0) return "";

            // 传奇模式：AI 竞争压力 +50%（GDB 第4章）
            float legendMult = (gs.activeMode == (int)GameMode.Legend || gs.legendModeActive) ? 1.5f : 1f;

            float loadHit = Mathf.Clamp((0.015f * overlaps + fareCutPressure) * legendMult, 0.01f, 0.12f);
            float brandHit = Mathf.Clamp(1.5f * overlaps * legendMult, 1f, 9f);

            gs.loadFactorPercent = Mathf.Max(35f, gs.loadFactorPercent - loadHit * 100f);
            gs.brandAwareness = Mathf.Clamp(gs.brandAwareness - brandHit, 20f, 100f);

            // 代码共享加成可部分抵消
            if (gs.codeShareActive && gs.codeShareLoadBonus > 0f)
                gs.loadFactorPercent = Mathf.Min(92f, gs.loadFactorPercent + gs.codeShareLoadBonus);

            return $"竞争压力：重叠航线 {overlaps} 条，客座率 −{loadHit * 100f:F1}%，品牌认知 −{brandHit:F0}";
        }

        private string MaybeProposeCodeShare(GameState gs)
        {
            if (gs.codeShareActive) return "";
            if (!string.IsNullOrEmpty(gs.pendingCodeShareFromId)) return "";
            if (gs.lastCodeShareYear > 0 && gs.year - gs.lastCodeShareYear < 2) return "";
            if (UnityEngine.Random.value > 0.18f) return ""; // 约每两年一次期望

            AIAirline partner = null;
            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || !ai.isAlive || ai.hatesPlayer) continue;
                    if (ai.state == AIAirlineStates.Dying || ai.state == AIAirlineStates.Stable)
                    {
                        partner = ai;
                        break;
                    }
                }
            }
            if (partner == null) return "";

            gs.pendingCodeShareFromId = partner.id;
            var proposal = new CodeShareProposal
            {
                fromAiId = partner.id,
                fromAiName = partner.name,
                message =
                    $"{partner.name} 提议代码共享：接受后客座率 +3%，对方可获得你 1～2 条航线的联运权。拒绝或事后背叛将结怨。"
            };
            OnCodeShareProposed?.Invoke(proposal);
            return $"代码共享提案：来自 {partner.name}";
        }

        public void TryOfferCodeShareFromDying(AIAirline dying)
        {
            var gs = GameState.Instance;
            if (gs == null || dying == null || !dying.isAlive) return;
            if (gs.codeShareActive || !string.IsNullOrEmpty(gs.pendingCodeShareFromId)) return;
            gs.pendingCodeShareFromId = dying.id;
            OnCodeShareProposed?.Invoke(new CodeShareProposal
            {
                fromAiId = dying.id,
                fromAiName = dying.name,
                message = $"{dying.name} 垂死求生，愿以代码共享换取喘息。接受可获客座率加成；拒绝则其加速出局。"
            });
        }

        public void AcceptCodeShare()
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(gs.pendingCodeShareFromId)) return;

            gs.codeShareActive = true;
            gs.codeSharePartnerId = gs.pendingCodeShareFromId;
            gs.codeShareLoadBonus = 3f;
            gs.lastCodeShareYear = gs.year;
            gs.pendingCodeShareFromId = null;
            gs.loadFactorPercent = Mathf.Min(92f, gs.loadFactorPercent + 3f);
            gs.AddDecisionLog("接受代码共享协议。");
            Debug.Log("[Competition] 接受代码共享");
            OnCompetitionUpdated?.Invoke();
        }

        public void RejectCodeShare()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            string id = gs.pendingCodeShareFromId;
            gs.pendingCodeShareFromId = null;
            gs.lastCodeShareYear = gs.year;
            gs.AddDecisionLog("拒绝代码共享提案。");
            Debug.Log($"[Competition] 拒绝代码共享 from={id}");
            OnCompetitionUpdated?.Invoke();
        }

        /// <summary>背叛：撕毁协议，对方仇恨并可能降价报复。</summary>
        public void BetrayCodeShare()
        {
            var gs = GameState.Instance;
            if (gs == null || !gs.codeShareActive) return;

            string pid = gs.codeSharePartnerId;
            gs.codeShareActive = false;
            gs.codeShareLoadBonus = 0f;
            gs.codeSharePartnerId = null;
            gs.loadFactorPercent = Mathf.Max(35f, gs.loadFactorPercent - 4f);

            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || ai.id != pid) continue;
                    ai.hatesPlayer = true;
                    ai.avgFare *= 0.9f;
                    ai.state = AIAirlineStates.Expanding;
                    break;
                }
            }

            gs.AddDecisionLog("撕毁代码共享——对方结怨并降价报复。");
            Debug.Log("[Competition] 背叛代码共享");
            OnCompetitionUpdated?.Invoke();
        }

        public string FormatShareSummary()
        {
            var gs = GameState.Instance;
            if (gs == null) return "无数据";
            RecalculateMarketShares(gs);
            var sb = new StringBuilder();
            sb.AppendLine($"你：{gs.playerMarketShare:F1}%");
            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null) continue;
                    if (!ai.isAlive)
                    {
                        sb.AppendLine($"{ai.name}：已破产");
                        continue;
                    }
                    sb.AppendLine($"{ai.name}：{ai.marketShare:F1}% · {AIAirlineTypes.DisplayName(ai.type)} · {AIAirlineStates.DisplayName(ai.state)}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
