using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 OPS · n24 IRROPS 处置（纯 C#，供 UI 与离线年闭环共用）。
    /// </summary>
    public static class OpsIrropsLogic
    {
        public enum IrropsKind
        {
            Weather,
            Aog,
            Atc,
            CrewShortage
        }

        public enum Choice
        {
            A = 1,
            B = 2,
            C = 3
        }

        public struct Outcome
        {
            public float cashDeltaWan;
            public float onTimeDelta;
            public float boardDelta;
            public string label;
            public string summary;
        }

        public static string KindLabel(IrropsKind kind) => kind switch
        {
            IrropsKind.Weather => "台风暴雨",
            IrropsKind.Aog => "停飞",
            IrropsKind.Atc => "空管流控",
            IrropsKind.CrewShortage => "机组短缺",
            _ => "运行扰动"
        };

        public static string ScenarioBody(IrropsKind kind) => kind switch
        {
            IrropsKind.Weather =>
                "枢纽遭遇强对流，多班次可能延误或取消。请在倒计时内选择处置方案。",
            IrropsKind.Aog =>
                "执飞飞机突发故障停飞，无法按计划起飞。请选择调机、抢修或降舱方案。",
            IrropsKind.Atc =>
                "空管发布流量控制，今日起飞窗口压缩。请选择等待、改航或合并班次。",
            IrropsKind.CrewShortage =>
                "签派发现机组疲劳/缺员，无法覆盖原排班。请选择换班、延后或减班。",
            _ => "运行出现非正常扰动，请选择处置。"
        };

        public static Outcome Resolve(IrropsKind kind, Choice choice)
        {
            return (kind, choice) switch
            {
                (IrropsKind.Weather, Choice.A) => Ok(-25f, -1f, 0f, "全网延误缓冲",
                    "加长过站与备用窗，现金 −25 万；准点略降，旅客投诉可控。"),
                (IrropsKind.Weather, Choice.B) => Ok(-12f, -4f, -1f, "取消低收益班",
                    "取消部分休闲航线，现金 −12 万；准点与满意度承压。"),
                (IrropsKind.Weather, Choice.C) => Ok(-40f, 1f, 1f, "包机/酒店安抚",
                    "高成本保服务，现金 −40 万；准点与董事会口碑略升。"),

                (IrropsKind.Aog, Choice.A) => Ok(-20f, 0f, 0f, "调机备援",
                    "备用机顶替，现金 −20 万；准点与满意度基本不变。"),
                (IrropsKind.Aog, Choice.B) => Ok(-15f, -5f, -2f, "现场抢修",
                    "抢修延误约 4 小时，现金 −15 万；准点与满意度下降。"),
                (IrropsKind.Aog, Choice.C) => Ok(-8f, -8f, -3f, "降舱合并",
                    "合并旅客至后续班，现金 −8 万；准点与口碑明显受损。"),

                (IrropsKind.Atc, Choice.A) => Ok(-10f, -2f, 0f, "地面等待",
                    "按流控窗口排队，现金 −10 万（额外过站/餐食）；准点小幅下降。"),
                (IrropsKind.Atc, Choice.B) => Ok(-18f, -1f, 0f, "改航绕飞",
                    "绕飞加油与时刻损失，现金 −18 万；保住大部分准点。"),
                (IrropsKind.Atc, Choice.C) => Ok(-6f, -6f, -2f, "合并班次",
                    "合并低客座航班，现金 −6 万；准点与满意度下降。"),

                (IrropsKind.CrewShortage, Choice.A) => Ok(-14f, 0f, 0f, "紧急换班",
                    "调用备份机组，现金 −14 万；排班可维持。"),
                (IrropsKind.CrewShortage, Choice.B) => Ok(-5f, -4f, -1f, "整体延后",
                    "全网推后 2 小时，现金 −5 万；准点承压。"),
                (IrropsKind.CrewShortage, Choice.C) => Ok(-3f, -7f, -2f, "减班保安全",
                    "取消部分班次，现金 −3 万；准点与满意度下降。"),

                _ => Ok(-10f, -2f, 0f, "默认处置", "采用保守默认方案。")
            };
        }

        public static string ChoiceLabel(IrropsKind kind, Choice choice)
        {
            var o = Resolve(kind, choice);
            return $"【{choice}】{o.label}\n现金 {o.cashDeltaWan:F0} 万 · 准点 {Signed(o.onTimeDelta)} · 董事会 {Signed(o.boardDelta)}";
        }

        static Outcome Ok(float cash, float otp, float board, string label, string summary) => new Outcome
        {
            cashDeltaWan = cash,
            onTimeDelta = otp,
            boardDelta = board,
            label = label,
            summary = summary
        };

        static string Signed(float v) => v >= 0f ? $"+{v:F0}" : $"{v:F0}";
    }
}
