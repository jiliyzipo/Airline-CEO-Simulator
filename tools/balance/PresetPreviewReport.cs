using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Balance
{
    /// <summary>
    /// 四个具名预设在接管瞬间的净烧钱 / 跑道读数。
    ///
    /// 为什么单独做一档：<c>--routeinventory</c> 的星级表是「该星级全部航司的平均」，
    /// 每个星级还混进了枢纽派生的航司，和 <c>PresetAirlines</c> 里那四个真正会被
    /// PlayMode 用例接管的预设并不等价。按星级平均去回填初始现金会偏档
    /// （★5 平均 2061.9 vs 预设实测 1985.1，直接差出 0.1 个月跑道）。
    ///
    /// 口径与 <c>TakeoverWorldBuilder.BuildPreview</c> 完全一致：
    /// 净烧钱 = 总部 + 机队/人力 + 还本付息 + 全险保费 − 航线收入(按班期折算) − 过渡收入(按剩余档期封顶)，
    /// 跑道 = 现金 ÷ 净烧钱。改任何一侧的成本模型后，跑一次本档回填 <c>PresetAirlines</c>。
    /// </summary>
    internal static class PresetPreviewReport
    {
        // 标签只写「档位 + 角色」，枢纽一律从被测 profile 读（见 Run 里的 label 拼装）。
        // 曾经这里把枢纽也硬编码进标签，于是 Base 被换成别的枢纽时，报表会一边测 CAN 一边印「CTU」——
        // 报表自己把自己骗过去，比没有报表更糟。现在印出来的枢纽一定是量到的那一个。
        static readonly (string id, string tag, float target)[] Presets =
        {
            // 2026-10-22 D2 第三步：与 RunwayDebtRegression 同源，一档一座、按客流升序。
            ("preset_startup", "★2 创业", 8f),
            ("preset_regional", "★3 区域", 6f),
            ("preset_legacy", "★4 老牌", 4f),
            ("preset_distressed", "★5 困境", 3f),
        };

        public static int Run()
        {
            Console.WriteLine("=== 具名预设接管读数（预设口径，非星级平均）===");
            Console.WriteLine("预设            现金   收入    经营烧钱  还本付息    保费   净烧钱   跑道  目标  建议现金");

            bool ok = true;
            foreach (var (id, tag, target) in Presets)
            {
                var p = Systems.TakeoverWorldBuilder.BuildPreview(id);
                if (p == null)
                {
                    Console.WriteLine($"{tag,-8}  {id,-20}  无法构建预览");
                    ok = false;
                    continue;
                }

                // 枢纽从被测对象读：报表印的必须是它真正量到的那座城市。
                string label = tag + " " + (string.IsNullOrEmpty(p.hubAirport) ? "??" : p.hubAirport.ToUpperInvariant());

                float operating = p.monthlyCost + p.monthlyFixedCost - p.monthlyRevenue;
                float net = operating + p.monthlyDebtService + p.monthlyInsurance;
                float runway = net > 0f ? p.cash / net : float.PositiveInfinity;
                float suggested = target * net;
                float drift = runway / target - 1f;

                Console.WriteLine($"{label,-14}  {p.cash,6:F0}  {p.monthlyRevenue,6:F1}  {operating,9:F1}  {p.monthlyDebtService,8:F1}"
                                  + $"  {p.monthlyInsurance,6:F1}  {net,7:F1}  {runway,5:F2}  {target,4:F1}"
                                  + $"  {suggested,8:F0}  (偏离 {drift * 100f:+0.0;-0.0}%)");

                if (Math.Abs(drift) > 0.03f) ok = false;
            }

            Console.WriteLine();
            Console.WriteLine(ok ? "全部落在目标 ±3% 内 ✓" : "存在偏离 > 3% 的预设，请回填「建议现金」后重跑");
            return ok ? 0 : 1;
        }
    }
}
