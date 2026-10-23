using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// <see cref="QuarterlyOneOffLogic"/> 的「依赖 GameState」那一半。
    ///
    /// 单独拆文件是为了让 <c>tools/balance</c> 能只链接纯算式那一半：
    /// 离线工具拿不到 <see cref="GameState"/>（MonoBehaviour），但 K12.3 的回归
    /// 需要验证「敞口 × 波动带」是否覆盖 K10 实测值 —— 那条断言必须能在离线工具里跑，
    /// 否则每次改一个数字都要开 Unity。
    /// </summary>
    public static partial class QuarterlyOneOffLogic
    {
        public static float EstimateBandWan(GameState gs)
            => gs == null ? 0f : EstimateBandWan(FxEconomics.NetUsdExposureWan(gs), gs.fxHedgeRatio, gs.quarter);

        /// <summary>面板用的一行：预计区间。敞口与季度直接取局内状态。</summary>
        public static string EstimateLine(GameState gs)
        {
            if (gs == null) return "";
            return EstimateLine(FxEconomics.NetUsdExposureWan(gs), gs.fxHedgeRatio, gs.quarter);
        }

        /// <summary>季报用的一行：预计 vs 实际。季度与敞口在结算前取，故由调用方传入。</summary>
        public static string RetrospectiveLine(OneOffTally t, GameState gs)
        {
            if (gs == null) return "";
            return RetrospectiveLine(t, FxEconomics.NetUsdExposureWan(gs), gs.fxHedgeRatio, gs.quarter);
        }
    }
}
