using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// K12 · 季末一次性项。
    ///
    /// 问题：K10 第六轮的第 14 项残差（★2 −1.1% 到 ★4 −11.2%）**全部**来自季末一次性项，
    /// 其中最大一项是汇兑损益（★4 人民币单季升值 3.1% → +271.6 万）。这些钱是真的进了现金，
    /// 但它不按月发生、方向也不确定，**不能塞进「月均烧钱」再折成跑道** ——
    /// 那会把跑道从 4.0 月虚报成 4.33 月，玩家按 4.33 月排资金就会踩空。
    ///
    /// 所以口径拆成两条互不混算的线：
    /// ① 月烧钱 / 跑道：只含**可重复**的月度收支（K9/K10/K11 已收口）；
    /// ② 季末一次性项：单独一行，先给**预计区间**，季末再给**实际分项**。
    ///
    /// 预计区间的算法是「敞口 × 模型本身能产生的单季最大汇率变动」，不是拍脑袋的经验值：
    /// 单季汇率变动来自两处，二者可以叠加 ——
    /// · <see cref="FxEconomics.RateConvergeBand"/>（现实汇率靠拢，±3%/季）；
    /// · 游戏自身的季度漂移：财年首季 <c>|drift| + noise</c> = 1.2% + 1.5% = 2.7%，
    ///   其余季度 ±1.0%。
    /// 首季合计 5.7%、其余 4.0%。区间取 ±(敞口 × 该比例)，**覆盖**而不是**等于**实测值：
    /// K10 实测 ★4 271.6 万落在 ±500 万内、★5 405.2 万落在 ±745 万内，
    /// 这就是「预计 ±X 万」该有的语义 —— 它是上界，不是预测点。
    /// </summary>
    public static partial class QuarterlyOneOffLogic
    {
        /// <summary>
        /// 现实汇率单季靠拢上限（3%），与 <c>FxEconomics.RateConvergeBand</c> 同值。
        ///
        /// 为什么复写一份：这个文件要被 `tools/balance` 链接做离线回归，而 `FxEconomics`
        /// 依赖 <c>GameState</c>（MonoBehaviour）无法离线编译。**两处 3% 的一致性由断言保证**
        /// —— `QuarterlyOneOffRegression` 首条就比对二者相等；改了一处而漏另一处，回归会红。
        /// 这比「靠注释提醒后来者」可靠，也比让两者各自漂移强。
        /// </summary>
        public const float ConvergeBand = 0.03f;

        /// <summary>财年首季的方向性漂移上限（衰退 1.2% / 繁荣 1.0%，取大者）。</summary>
        public const float QuarterOpenDrift = 0.012f;
        /// <summary>财年首季的噪声上限。</summary>
        public const float QuarterOpenNoise = 0.015f;
        /// <summary>非首季的噪声上限。</summary>
        public const float QuarterNoise = 0.010f;

        /// <summary>该季度汇率单季最大变动（不含现实汇率靠拢那一段）。</summary>
        public static float MaxDriftRatio(int quarter)
            => quarter <= 1 ? QuarterOpenDrift + QuarterOpenNoise : QuarterNoise;

        /// <summary>单季最大汇率变动比例：漂移 + 现实靠拢，两者可叠加。</summary>
        public static float BandRatio(int quarter)
            => ConvergeBand + MaxDriftRatio(quarter);

        /// <summary>对冲后的敞口绝对值 × 单季最大变动 = 预计区间半宽（万美元）。</summary>
        public static float EstimateBandWan(float netExposureWan, float hedgeRatio, int quarter)
        {
            float hedge = Mathf.Clamp(hedgeRatio, 0f, 0.8f);
            return Mathf.Abs(netExposureWan) * BandRatio(quarter) * (1f - hedge);
        }

        /// <summary>面板用的一行：预计区间 + 明确写出「不计入月烧钱」。</summary>
        public static string EstimateLine(float netExposureWan, float hedgeRatio, int quarter)
        {
            float band = EstimateBandWan(netExposureWan, hedgeRatio, quarter);
            float hedge = Mathf.Clamp(hedgeRatio, 0f, 0.8f);
            string hedgeNote = hedge > 0.001f ? $"已对冲 {hedge * 100f:F0}% 后" : "未对冲";
            string direction = netExposureWan <= -0.5f ? "净美元负债 → 人民币升值多为收益"
                             : netExposureWan >= 0.5f ? "净美元资产 → 人民币贬值多为收益"
                             : "敞口极小";
            return $"季末一次性项　预计 ±{band:F0} 万"
                 + $"（{direction}；{hedgeNote}；以汇兑为主，货运/险损/随机事件不可预判；不计入月烧钱）";
        }

        /// <summary>
        /// 季报用的一行：先给「预计 ±X 万」，再给实际与分项，并判定汇兑项是否落在预计区间内。
        /// 判定只比**汇兑项**，因为区间是按汇率波动算的；货运/险损/随机事件本就不在区间内。
        /// </summary>
        public static string RetrospectiveLine(OneOffTally t, float exposureBefore, float hedgeBefore, int quarter)
        {
            if (t == null) return "";
            float band = EstimateBandWan(exposureBefore, hedgeBefore, quarter);
            string F(float v) => (v >= 0f ? "+" : "") + v.ToString("F1");
            bool covered = Mathf.Abs(t.fx) <= band + 0.05f;
            string verdict = covered ? "落在预计区间内" : "超出预计区间";
            var sb = new System.Text.StringBuilder();
            sb.Append($"季末一次性项　预计 ±{band:F0} 万 → 实际合计 {F(t.Total)} 万（汇兑 {F(t.fx)} 万，{verdict}）");
            sb.Append($"\n　分项：汇兑 {F(t.fx)} · 货运 {F(t.cargo)} · 油价 {F(t.fuel)}");
            sb.Append($" · 险损 {F(t.safety)} · 随机事件 {F(t.random)}");
            if (Mathf.Abs(t.other) >= 0.05f) sb.Append($" · 其他 {F(t.other)}");
            sb.Append("\n　（季末一次性项不计入月烧钱；跑道分母只含可重复的月度收支）");
            return sb.ToString();
        }

        /// <summary>
        /// 季末实际分项（K12.2）。只有真的发生过的类别才印出来，避免用一排 0 让人觉得「都算过」。
        /// </summary>
        public static string Breakdown(OneOffTally t)
        {
            if (t == null) return "";
            string F(float v) => (v >= 0f ? "+" : "") + v.ToString("F1");
            var sb = new System.Text.StringBuilder();
            sb.Append("季末一次性项（本季实际，不计入月烧钱）：");
            sb.Append($"汇兑 {F(t.fx)} 万 · 货运 {F(t.cargo)} 万 · 油价 {F(t.fuel)} 万");
            sb.Append($" · 险损 {F(t.safety)} 万 · 随机事件 {F(t.random)} 万");
            if (Mathf.Abs(t.other) >= 0.05f) sb.Append($" · 其他 {F(t.other)} 万");
            sb.Append($" = 合计 {F(t.Total)} 万");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 一季季末一次性项的现金影响（万美元，正 = 增现金）。
    /// 由 <c>TimeManager.AdvanceQuarterCore</c> 在每一步结算前后逐段取现金差得到 ——
    /// 是**实测**，不是把公式再算一遍。这样即使某个子系统改了记账方式，这里也不会说谎。
    /// </summary>
    [System.Serializable]
    public sealed class OneOffTally
    {
        public float fx;
        public float cargo;
        public float fuel;
        public float safety;
        public float random;
        public float other;

        public float Total => fx + cargo + fuel + safety + random + other;

        public bool IsZero => Mathf.Abs(fx) < 0.05f && Mathf.Abs(cargo) < 0.05f
                           && Mathf.Abs(fuel) < 0.05f && Mathf.Abs(safety) < 0.05f
                           && Mathf.Abs(random) < 0.05f && Mathf.Abs(other) < 0.05f;

        public void Reset()
        {
            fx = cargo = fuel = safety = random = other = 0f;
        }

        public void AddFx(float d) => fx += d;
        public void AddCargo(float d) => cargo += d;
        public void AddFuel(float d) => fuel += d;
        public void AddSafety(float d) => safety += d;
        public void AddRandom(float d) => random += d;
        public void AddOther(float d) => other += d;
    }
}
