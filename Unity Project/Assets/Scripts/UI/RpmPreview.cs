using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.UI
{
    /// <summary>
    /// RPM 入参装配的**唯一**来源（2026-10-19）。
    ///
    /// 背景：n35「新航线 RPM 评估台」此前是空壳 —— 分数由 `班次 × 品牌` 拼出来，
    /// 和「航线 → 开通新航线」里 `RPMCalculator` 算的那套完全没有关系。同一条航线，
    /// 两个页面给出两个数。这里把「距离 / 机型 / 座位 / 需求系数 / 油价 / 品牌 / 竞对」
    /// 的取数口径收在一处，两个入口都调它，改一处两边同时生效。
    ///
    /// 纯装配，不读写任何 UI：便于在 Editor 隔离回归里直接钉住。
    /// </summary>
    public static class RpmPreview
    {
        public struct Plan
        {
            public string originCode;
            public string destCode;
            public float distanceKm;
            public float demandMultiplier;
            public string aircraftType;
            public int seats;
            public RPMCalculator.RPMOutput output;
        }

        /// <summary>按航程自动选机型：≥5000 km 用宽体，否则窄体（口径在 <see cref="RouteFeasibility"/>）。</summary>
        public static string AutoAircraft(float distanceKm) => RouteFeasibility.AutoAircraft(distanceKm);

        /// <param name="aircraftType">空 = 按航程自动（<see cref="AutoAircraft"/>）。</param>
        /// <param name="distanceKm">≤0 = 取大圆距离；经停航线由调用方传入分段之和。</param>
        public static Plan Build(GameState gs, string originCode, string destCode,
            string aircraftType = null, float competitorSupply = 0.4f, float distanceKm = 0f)
        {
            // 算式与「CEO 命令台」的部门答复同源：都走 RouteFeasibility。
            var f = RouteFeasibility.Build(gs, originCode, destCode, aircraftType, competitorSupply, distanceKm);
            return new Plan
            {
                originCode = f.originCode,
                destCode = f.destCode,
                distanceKm = f.distanceKm,
                demandMultiplier = f.demandMultiplier,
                aircraftType = f.aircraftType,
                seats = f.seats,
                output = f.output
            };
        }

        /// <summary>
        /// 周折算：`dailyProfit` 是「1 班/日」基准，所以 N 班/周 = 日利 × N / 7。
        /// n35 页面上的「周利润」必须走这个式子，不能直接乘班次。
        /// </summary>
        public static float WeeklyFromDaily(float dailyWan, float weeklyFlights) =>
            dailyWan * weeklyFlights / 7f;

        // —— 入口预填（航线 → 开通新航线 → RPM 评估）——
        //
        // 「开通航线」页已经把出发地 / 目的地填好了，再点「RPM 评估」时如果还要重输一遍，
        // 等于让人把刚填的东西抄一次。这里存一份**待消费的种子**：写入方只放值，
        // 消费方（n35 的 RenderRpm）在下次渲染时取走并清空。
        //
        // 用「消费」而不是「常驻」的原因：种子只在一次跳转中有效。常驻的话，
        // 以后从收件箱或顾问直接进 n35，会被上一次开线页的机场莫名其妙地覆盖。
        static string _seedOrigin;
        static string _seedDest;

        /// <summary>由开线页调用：把当前出发地 / 目的地交给下一次 RPM 渲染。</summary>
        public static void Seed(string originCode, string destCode)
        {
            _seedOrigin = string.IsNullOrWhiteSpace(originCode) ? null : originCode.Trim().ToUpperInvariant();
            _seedDest = string.IsNullOrWhiteSpace(destCode) ? null : destCode.Trim().ToUpperInvariant();
        }

        /// <summary>取出并清空待用种子；没有种子返回 false。</summary>
        public static bool TryConsumeSeed(out string originCode, out string destCode)
        {
            originCode = _seedOrigin;
            destCode = _seedDest;
            _seedOrigin = null;
            _seedDest = null;
            return !string.IsNullOrEmpty(originCode) && !string.IsNullOrEmpty(destCode);
        }

        /// <summary>丢弃种子（编辑器自测用，避免跨用例残留）。</summary>
        public static void ClearSeed()
        {
            _seedOrigin = null;
            _seedDest = null;
        }
    }
}
