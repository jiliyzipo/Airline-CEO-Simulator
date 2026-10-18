using AirlineCEO.Systems;

namespace AirlineCEO.UI.Shell
{
    /// <summary>开局引导步骤（对齐《航空顾问开局引导线》v1.1）。</summary>
    public enum AdvisorTutorialStep
    {
        S0_Opportunity = 0,
        S1_RpmCard = 1,
        S2_Meeting = 2,
        S3_FleetGuide = 3,
        S4_Schedule = 4,
        S5_FlightResult = 5,
        S6_NextHook = 6,
        Done = 7
    }

    /// <summary>引导脚本与动态姓名替换。</summary>
    public static class AdvisorTutorialScript
    {
        public const string AdvisorTitle = "航空顾问";
        public const string RouteLabel = "深圳宝安（SZX）→ 北京首都（PEK）";
        public const string Origin = "SZX";
        public const string Dest = "PEK";
        public const string TutorialMailId = "m_tutorial_szx_pek";

        /// <summary>GameState.advisorTutorialAwaitPage</summary>
        public const int AwaitNone = 0;
        public const int AwaitInbox = 1;
        public const int AwaitFleet = 2;

        public static string PlayerName
        {
            get
            {
                var gs = GameState.Instance;
                if (gs == null || string.IsNullOrWhiteSpace(gs.ceoDisplayName)) return "CEO";
                return gs.ceoDisplayName.Trim();
            }
        }

        public static string Fmt(string template) =>
            (template ?? "").Replace("[玩家姓名]", PlayerName);

        public static string RecommendedAircraftLabel()
        {
            var gs = GameState.Instance;
            if (gs?.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a != null && a.isActive && a.status == AirlineCEO.Data.AircraftStatuses.Airworthy)
                        return $"{a.type}（注册号 {a.registration}）";
                }
            }
            return "A320neo（注册号 B-1001）";
        }

        public static string RecommendedRegistration()
        {
            var gs = GameState.Instance;
            if (gs?.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a != null && a.isActive && a.status == AirlineCEO.Data.AircraftStatuses.Airworthy)
                        return a.registration ?? "";
                }
            }
            return "B-1001";
        }

        public static bool IsAwaitingInbox()
        {
            var gs = GameState.Instance;
            return gs != null && !gs.advisorTutorialCompleted && gs.advisorTutorialAwaitPage == AwaitInbox;
        }

        public static bool IsAwaitingFleet()
        {
            var gs = GameState.Instance;
            return gs != null && !gs.advisorTutorialCompleted && gs.advisorTutorialAwaitPage == AwaitFleet;
        }

        // ── 排班调整（S4）预设 ──

        public readonly struct FreqPreset
        {
            public readonly string Label;   // 每日
            public readonly int Weekly;     // 7
            public readonly string Days;    // 每日
            public readonly float Revenue;  // 收益倍数（相对每日）
            public FreqPreset(string label, int weekly, string days, float revenue)
            {
                Label = label; Weekly = weekly; Days = days; Revenue = revenue;
            }
        }

        public readonly struct SlotPreset
        {
            public readonly string Label;   // 早班
            public readonly string Dep;     // 08:00
            public readonly string Arr;     // 10:30
            public readonly float Demand;   // 时段需求系数
            public SlotPreset(string label, string dep, string arr, float demand)
            {
                Label = label; Dep = dep; Arr = arr; Demand = demand;
            }
        }

        public static readonly FreqPreset[] FreqPresets =
        {
            new FreqPreset("每日", 7, "每日", 1.00f),
            new FreqPreset("每周5班", 5, "周一至周五", 0.85f),
            new FreqPreset("每周3班", 3, "周一/三/五", 0.62f)
        };

        /// <summary>深圳—北京单程约 2.5h（2070km）。</summary>
        public static readonly SlotPreset[] SlotPresets =
        {
            new SlotPreset("早班", "08:00", "10:30", 1.00f),
            new SlotPreset("午班", "12:00", "14:30", 0.90f),
            new SlotPreset("晚班", "18:00", "20:30", 1.10f),
            new SlotPreset("红眼", "22:00", "00:30+1", 0.80f)
        };

        /// <summary>RPM 卡给出的日基准利润（每日 1 班口径）。</summary>
        public const float RouteDailyBaseline = 16.2f;

        /// <summary>估算日利润 = 基准 × 频率收益 × 时段需求。</summary>
        public static float EstimateDailyProfit(float slotDemand, float freqRevenue)
        {
            return RouteDailyBaseline * freqRevenue * slotDemand;
        }
    }
}
