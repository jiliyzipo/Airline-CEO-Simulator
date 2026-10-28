using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 运力缺口：航线数相对**在役**架数（与运营部购机建议同一口径）。
    ///
    /// 入参通常来自 FleetManager.CountAirworthy —— 那个名字容易误读：它数的是
    /// 「isActive 且状态适航」的飞机，即**已分配航线**的飞机；未分配的在册飞机不计入，
    /// 即便它们在机队列表里每行都写着「适航」。界面文案统一写「在役」，别写「适航」。
    /// </summary>
    public static class CapacityPlanning
    {
        public const float WarnRatio = 0.20f;

        public static int Gap(int routes, int airworthy) =>
            Math.Max(0, routes - Math.Max(0, airworthy));

        public static float GapRatio(int routes, int airworthy)
        {
            if (routes <= 0) return 0f;
            return Gap(routes, airworthy) / (float)routes;
        }

        public static bool NeedsDecision(int routes, int airworthy) =>
            Gap(routes, airworthy) > 0;
    }
}
