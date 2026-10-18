using System;
using AirlineCEO.Logic;
using AirlineCEO.Testing;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 0.4：单次「月度 Tick」纯函数管线（离线/测试用）。
    /// 顺序对齐任务书 TickExecutionPipeline 的财务相关子集。
    /// </summary>
    public static class TickExecutionPipeline
    {
        public struct TickInput
        {
            public MockGameState State;
            /// <summary>本月油价冲击（乘法，1=不变）。</summary>
            public float FuelShockMultiplier;
            /// <summary>是否结算一个月的航线净利与固定开销。</summary>
            public bool SettleMonth;
        }

        public struct TickResult
        {
            public float CashBefore;
            public float CashAfter;
            public float MonthlyRevenue;
            public float MonthlyBurn;
            public float NetDelta;
            public float RunwayMonths;
            public float FuelPriceAfter;
            public string RunwayWarning;
        }

        public static TickResult Execute(TickInput input)
        {
            if (input.State == null)
                throw new ArgumentNullException(nameof(input.State));

            var gs = input.State;
            float before = gs.cash;
            float shock = input.FuelShockMultiplier <= 0f ? 1f : input.FuelShockMultiplier;
            gs.fuelPricePerTon = Math.Max(100f, gs.fuelPricePerTon * shock);

            float rev = gs.MonthlyRevenue;
            float burn = gs.EffectiveMonthlyBurn;
            float net = 0f;

            if (input.SettleMonth)
            {
                // EffectiveMonthlyBurn 已是 max(开销-收入,0)；月结应使用真实净额
                float opex = gs.monthlyBurnRate + gs.FleetMonthlyMaintenance + Math.Max(0f, gs.financeCrisisBurnBonus);
                net = rev - opex;
                gs.cash += net;
                burn = net < 0f ? -net : 0f;
            }

            float runway = CashRunnelCalculator.GetRunwayMonths(Math.Max(0f, gs.cash), burn);
            return new TickResult
            {
                CashBefore = before,
                CashAfter = gs.cash,
                MonthlyRevenue = rev,
                MonthlyBurn = burn,
                NetDelta = net,
                RunwayMonths = runway,
                FuelPriceAfter = gs.fuelPricePerTon,
                RunwayWarning = CashRunnelCalculator.GetRunwayWarning(runway)
            };
        }
    }
}
