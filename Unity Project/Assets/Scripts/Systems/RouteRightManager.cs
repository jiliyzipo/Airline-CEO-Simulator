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
            if (IsPair(o, d, "深圳", "洛杉矶")) return true;
            if (IsPair(o, d, "深圳", "洛杉")) return true;

            // 兜底：含「伦敦/洛杉矶/纽约/巴黎」等国际目的地视为需航权
            string joined = o + d;
            return joined.Contains("伦敦") || joined.Contains("洛杉") || joined.Contains("纽约")
                   || joined.Contains("巴黎") || joined.Contains("London") || joined.Contains("Los");
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

            // 通过率 = 70% − 竞对占比 × 30%（北京-伦敦约 50.5%）
            float passRate = Mathf.Clamp01(0.70f - share * 0.30f);

            // 安全评级 C/D：航权谈判额外承压
            var gs = GameState.Instance;
            if (gs != null && gs.safetyRating >= SafetyRating.C)
                passRate = Mathf.Clamp01(passRate - 0.10f);

            bool approved = UnityEngine.Random.value < passRate;

            if (approved)
            {
                return new RouteRightResult
                {
                    approved = true,
                    skipped = false,
                    passRate = passRate,
                    competitorShare = share,
                    message =
                        $"航权审批通过（通过率约 {passRate * 100f:F0}%，竞对运力占比 {share * 100f:F0}%）。"
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

        public static float EstimateCompetitorShare(string origin, string destination)
        {
            string o = (origin ?? "").Trim();
            string d = (destination ?? "").Trim();

            // 与 RPM 快照一致：北京-伦敦默认 65%
            if (IsPair(o, d, "北京", "伦敦") || IsPair(o, d, "Beijing", "London"))
                return 0.65f;

            if (IsPair(o, d, "深圳", "洛杉矶") || (o.Contains("深圳") && d.Contains("洛杉")))
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
        public float passRate;
        public float competitorShare;
        public string message;
    }
}
