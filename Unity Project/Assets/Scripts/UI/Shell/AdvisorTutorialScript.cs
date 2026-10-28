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

        // ———— 首航结算：文案与入账**必须同一对常量** ————
        //
        // 这两条不是「演示数字」，它们就是 ApplyFirstFlightRewards 真正加进账里的量。
        // 写成常量而不是各写一遍字面量，是因为以前的文案会漂：信里说 +6.2、代码里改 +5，
        // 玩家会发现「账没对上」，而这种漂移只能靠人肉比对发现。
        public const float FirstFlightNetProfit = 6.2f;
        public const float FirstFlightBoardGain = 5f;

        /// <summary>开航启动资金。取 <see cref="ActionExecutor.OpenRouteStartupCost"/>，绝不另写一个数。</summary>
        public static string StartupCostText => $"{ActionExecutor.OpenRouteStartupCost:F0} 万美元";

        // ———— S6「下一程」：教命令台，而不是再点一遍页面 ————
        //
        // 旧版这是第二条航线的推销（成都—上海 12.8 万 / 79 分 / 2 家航司），
        // 三个数字都无处可查；而玩家真正需要的下一课是**怎么不再逐页点**。
        // 文案里点名的五种说法，正是 <see cref="AirlineCEO.Logic.CeoCommandRouter"/> 认得的五类。
        public const string NextHookBody =
            "航空顾问：\n\n" +
            "“恭喜，[玩家姓名]。从发现机会、开会、配机到排班，一个完整闭环您已经走完了。”\n\n" +
            "“接下来不必每一步都点页面 —— 看屏幕最下方那一条，那是命令台。您直接说一句话，我按话里的事派给对应部门：”\n\n" +
            "“· 问情况：财务还能撑几个月 → 财务部回函\n" +
            "· 开航线：开通成都—上海 → 运营部把开航研究会纪要送进收件箱\n" +
            "· 增运力：买 2 架 A320neo → 机队发展部发购机请示\n" +
            "· 补人手：招聘 10 名飞行员 → 人力资源部发招聘请示\n" +
            "· 找钱：申请银行贷款 → 财务部发融资请示”\n\n" +
            "“要动钱、动编制、动机队的事，命令台只发请示 —— 批不批，仍然是您说了算。”\n\n" +
            "“说过的每一句都留在「历史」里，可以翻回去重看当时的回执；常说的句子可以收藏。”";

        public const string NextHookExtra = "引导结束 · 顾问转入待命；底栏命令台随时可用";

        /// <summary>估算日利润 = 基准 × 频率收益 × 时段需求。</summary>
        public static float EstimateDailyProfit(float slotDemand, float freqRevenue)
        {
            return RouteDailyBaseline * freqRevenue * slotDemand;
        }
    }
}
