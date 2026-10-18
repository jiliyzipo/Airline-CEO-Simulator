using System;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 7：长航线航权审批。通过率 = 70% − 竞对占比 × 30%。
    /// </summary>
    public class RouteRightManager : MonoBehaviour
    {
        public static RouteRightManager Instance { get; private set; }

        public const string DeniedMessage = "航权被拒，暂无法开通";
        public const string DeferredMessage = "竞争激烈，航权暂缓核准";

        /// <summary>竞对运力占比 ≥ 此值视为「竞争激烈」，非基地航线在此类市场暂缓核准。</summary>
        public const float CompetitiveShareThreshold = 0.5f;

        /// <summary>主基地出发航线的航权通过率加成（优先获得航权）。</summary>
        public const float BaseRouteBonus = 0.30f;

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
            if (Instance == this)
                Instance = null;
        }

        /// <summary>北京-伦敦、深圳-洛杉矶等长航线需要航权；短途示意航线免审。</summary>
        public bool RequiresApproval(string origin, string destination)
        {
            string o = (origin ?? "").Trim();
            string d = (destination ?? "").Trim();

            if (IsPair(o, d, "北京", "伦敦")) return true;
            if (IsPair(o, d, "Beijing", "London")) return true;
            if (IsPair(o, d, "PEK", "LHR")) return true;
            if (IsPair(o, d, "PKX", "LHR")) return true;
            if (IsPair(o, d, "深圳", "洛杉矶")) return true;
            if (IsPair(o, d, "深圳", "洛杉")) return true;
            if (IsPair(o, d, "SZX", "LAX")) return true;

            // 兜底：含「伦敦/洛杉矶/纽约/巴黎」等国际目的地（中文或 IATA）视为需航权
            string joined = o + d;
            string ju = joined.ToUpperInvariant();
            return joined.Contains("伦敦") || joined.Contains("洛杉") || joined.Contains("纽约")
                   || joined.Contains("巴黎") || joined.Contains("London") || joined.Contains("Los")
                   || ju.Contains("LHR") || ju.Contains("LAX") || ju.Contains("JFK") || ju.Contains("CDG");
        }

        public RouteRightResult TryApprove(string origin, string destination, float competitorShare = -1f)
        {
            if (!RequiresApproval(origin, destination))
            {
                return new RouteRightResult
                {
                    approved = true,
                    skipped = true,
                    passRate = 1f,
                    competitorShare = 0f,
                    message = "本航线无需航权审批（短途/免审）。"
                };
            }

            float share = competitorShare;
            if (share < 0f)
                share = EstimateCompetitorShare(origin, destination);

            bool fromBase = IsFromHomeBase(origin, destination);

            // 非基地航线 + 竞争激烈：暂缓核准（不直接拒绝，可从基地开通或稍后再申请）
            if (!fromBase && share >= CompetitiveShareThreshold)
            {
                return new RouteRightResult
                {
                    approved = false,
                    skipped = false,
                    deferred = true,
                    passRate = 0f,
                    competitorShare = share,
                    message = DeferredMessage
                };
            }

            // 通过率 = 70% − 竞对占比 × 30%（北京-伦敦约 50.5%）
            float passRate = Mathf.Clamp01(0.70f - share * 0.30f);

            // 安全评级 C/D：航权谈判额外承压
            var gs = GameState.Instance;
            if (gs != null && gs.safetyRating >= SafetyRating.C)
                passRate = Mathf.Clamp01(passRate - 0.10f);

            // 主基地出发：优先获得航权（额外加成）
            if (fromBase)
                passRate = Mathf.Clamp01(passRate + BaseRouteBonus);

            bool approved = UnityEngine.Random.value < passRate;

            if (approved)
            {
                string priorityNote = fromBase ? "（主基地出发，优先核准）" : "";
                return new RouteRightResult
                {
                    approved = true,
                    skipped = false,
                    passRate = passRate,
                    competitorShare = share,
                    message =
                        $"航权审批通过{priorityNote}（通过率约 {passRate * 100f:F0}%，竞对运力占比 {share * 100f:F0}%）。"
                };
            }

            return new RouteRightResult
            {
                approved = false,
                skipped = false,
                passRate = passRate,
                competitorShare = share,
                message = DeniedMessage
            };
        }

        /// <summary>航线是否从主基地出发（任一端点是主基地即视为基地航线，航线双向等价）。</summary>
        public static bool IsFromHomeBase(string origin, string destination)
        {
            var gs = GameState.Instance;
            string hub = gs != null ? (gs.hubIata ?? "").Trim() : "";
            if (string.IsNullOrEmpty(hub)) return false;
            return IsHubEndpoint(origin, hub) || IsHubEndpoint(destination, hub);
        }

        static bool IsHubEndpoint(string endpoint, string hub)
        {
            string e = (endpoint ?? "").Trim();
            if (string.IsNullOrEmpty(e)) return false;

            // 直接 IATA 代码相等
            if (string.Equals(e, hub, StringComparison.OrdinalIgnoreCase)) return true;

            // 解析成 IATA 再比对（支持「北京」「Beijing Capital」等别名）
            if (AirportCatalog.TryResolve(e, out var info))
                if (string.Equals(info.code, hub, StringComparison.OrdinalIgnoreCase))
                    return true;

            // 兜底：端点是主基地的中文/英文名
            if (AirportCatalog.TryResolve(hub, out var hubInfo))
            {
                if (string.Equals(e, hubInfo.nameZh, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(e, hubInfo.nameEn, StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains(hubInfo.nameZh) || e.Contains(hubInfo.nameEn)) return true;
            }

            return false;
        }

        public static float EstimateCompetitorShare(string origin, string destination)
        {
            string o = (origin ?? "").Trim();
            string d = (destination ?? "").Trim();

            // 与 RPM 快照一致：北京-伦敦默认 65%
            if (IsPair(o, d, "北京", "伦敦") || IsPair(o, d, "Beijing", "London")
                || IsPair(o, d, "PEK", "LHR") || IsPair(o, d, "PKX", "LHR"))
                return 0.65f;

            if (IsPair(o, d, "深圳", "洛杉矶") || (o.Contains("深圳") && d.Contains("洛杉"))
                || IsPair(o, d, "SZX", "LAX"))
                return 0.55f;

            return 0.40f;
        }

        private static bool IsPair(string o, string d, string a, string b)
        {
            return (o.Contains(a) && d.Contains(b)) || (o.Contains(b) && d.Contains(a));
        }
    }

    public struct RouteRightResult
    {
        public bool approved;
        public bool skipped;
        /// <summary>非基地航线在竞争激烈市场「暂缓核准」（非拒绝，可稍后再申请或改从基地开通）。</summary>
        public bool deferred;
        public float passRate;
        public float competitorShare;
        public string message;
    }
}
