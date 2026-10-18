using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 7：每季度 10% 概率触发轻度/中度安全事件，影响 safetyRating。
    /// </summary>
    public class SafetyEventManager : MonoBehaviour
    {
        public static SafetyEventManager Instance { get; private set; }

        [Range(0f, 1f)] public float eventChancePerQuarter = 0.10f;

        /// <summary>重大空难每季概率（GDB 4.2 硬性失败：评级归零 → Game Over）。</summary>
        [Range(0f, 1f)] public float fatalCrashChancePerQuarter = 0.002f;

        private static readonly string[] MildTitles =
        {
            "发动机轻微故障",
            "客舱烟雾报警误报",
            "滑行道偏离事件",
            "机务发现液压渗漏"
        };

        private static readonly string[] ModerateTitles =
        {
            "空中释压紧急下降",
            "跑道偏出事件",
            "严重鸟击致发动机损伤",
            "局方突击检查发现问题"
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

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>尝试触发本季安全事件。未触发返回空串。</summary>
        public string TryRollQuarterEvent(GameState gs)
        {
            if (gs == null || gs.gameOver) return "";

            // 重大空难：极低概率，评级归零 → 立即 Game Over（硬性失败）
            if (UnityEngine.Random.value < fatalCrashChancePerQuarter)
                return TriggerFatalCrash(gs);

            if (UnityEngine.Random.value > eventChancePerQuarter) return "";

            bool moderate = UnityEngine.Random.value < 0.45f;
            var severity = moderate ? SafetyEventSeverity.Moderate : SafetyEventSeverity.Mild;
            return ApplyEvent(gs, severity);
        }

        /// <summary>重大空难：安全评级归零，立即触发 Game Over。</summary>
        public string TriggerFatalCrash(GameState gs)
        {
            if (gs == null) return "";
            SafetyRating before = gs.safetyRating;

            gs.safetyRating = SafetyRating.D;
            gs.safetyIndex = 0f;
            gs.majorCrashOccurred = true;

            var log = new SafetyEventLog
            {
                id = $"SE-CRASH-{gs.year}Q{gs.quarter}",
                dateLabel = gs.DateLabel,
                severity = SafetyEventSeverity.Moderate,
                title = "重大空难",
                detail = $"发生重大空难，安全评级归零（{Letter(before)} → 0）。执照吊销。",
                ratingBefore = before,
                ratingAfter = SafetyRating.D
            };
            gs.AddSafetyEventLog(log);
            gs.AddDecisionLog("安全：重大空难，安全评级归零！");

            gs.gameOver = true;
            gs.gameOverVictory = false;
            gs.gameOverReason = "重大空难：安全评级归零，运营执照被吊销。游戏结束。";
            gs.NotifyChanged();

            return "🚨 重大空难！安全评级归零，运营执照被吊销，游戏结束。";
        }

        public string ApplyEvent(GameState gs, SafetyEventSeverity severity)
        {
            if (gs == null) return "";

            SafetyRating before = gs.safetyRating;
            SafetyRating after = Downgrade(before, severity == SafetyEventSeverity.Moderate ? 1 : 1);

            // 中度：若已是 C，可再降到 D；轻度最多降一级
            if (severity == SafetyEventSeverity.Moderate && before == SafetyRating.B && UnityEngine.Random.value < 0.25f)
                after = SafetyRating.D;

            string title = PickTitle(severity);
            string detail = severity == SafetyEventSeverity.Mild
                ? $"轻度事件「{title}」。安全评级 {Letter(before)} → {Letter(after)}。"
                : $"中度事件「{title}」。安全评级 {Letter(before)} → {Letter(after)}，局方加强关注。";

            gs.safetyRating = after;
            gs.SyncSafetyIndexFromRating();

            if (severity == SafetyEventSeverity.Moderate)
                gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 3f, 0f, 100f);

            var log = new SafetyEventLog
            {
                id = $"SE-{gs.year}Q{gs.quarter}-{gs.safetyEventLog.Count + 1}",
                dateLabel = gs.DateLabel,
                severity = severity,
                title = title,
                detail = detail,
                ratingBefore = before,
                ratingAfter = after
            };
            gs.AddSafetyEventLog(log);
            gs.AddDecisionLog("安全：" + detail);
            gs.NotifyChanged();

            string warn = after >= SafetyRating.C
                ? $" ⚠ 当前安全评级 {Letter(after)}，请打开左侧「安全」查看。"
                : "";

            return "安全事件：" + detail + warn;
        }

        public static SafetyRating Downgrade(SafetyRating current, int steps)
        {
            int v = (int)current + Mathf.Max(1, steps);
            if (v > (int)SafetyRating.D) v = (int)SafetyRating.D;
            return (SafetyRating)v;
        }

        public static string Letter(SafetyRating r) => r.ToString();

        public static string Describe(SafetyRating r)
        {
            return r switch
            {
                SafetyRating.A => "优秀：局方信任度高，保险费率优惠。",
                SafetyRating.B => "良好：符合主流航司标准（初始等级）。",
                SafetyRating.C => "关注：局方加强抽查，部分航权谈判承压。",
                SafetyRating.D => "危险：面临停飞/监管接管风险。",
                _ => ""
            };
        }

        public static Color ColorFor(SafetyRating r)
        {
            return r switch
            {
                SafetyRating.A => new Color(0.25f, 0.85f, 0.55f),
                SafetyRating.B => new Color(0.35f, 0.65f, 1f),
                SafetyRating.C => new Color(0.95f, 0.75f, 0.2f),
                SafetyRating.D => new Color(0.95f, 0.3f, 0.3f),
                _ => Color.white
            };
        }

        private static string PickTitle(SafetyEventSeverity severity)
        {
            var pool = severity == SafetyEventSeverity.Moderate ? ModerateTitles : MildTitles;
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        /// <summary>编辑器/测试：强制触发一次。</summary>
        public string ForceEvent(SafetyEventSeverity severity)
        {
            return ApplyEvent(GameState.Instance, severity);
        }
    }
}
