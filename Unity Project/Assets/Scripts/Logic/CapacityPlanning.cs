using System;

namespace AirlineCEO.Logic
{
    /// <summary>运力缺口：航线数相对适航架数（与运营部购机建议同一口径）。</summary>
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
