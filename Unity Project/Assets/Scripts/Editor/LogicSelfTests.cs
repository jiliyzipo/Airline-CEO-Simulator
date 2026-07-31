#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Logic;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 在 Console 打印三个计算器的简易用例，确认编译与数值合理。
    /// 菜单：AirlineCEO → Run Logic Self Tests
    /// </summary>
    public static class LogicSelfTests
    {
        [MenuItem("AirlineCEO/Run Logic Self Tests")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Logic Self Tests ===");

            // RPM：北京-伦敦约 8150 km
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 8150f,
                fuelPricePerTon = 850f,
                competitorSupply = 0.65f,
                demandMultiplier = 1.2f,
                brandEquity = 55f
            });
            sb.AppendLine($"RPM PEK-LHR → score={rpm.score} rating={rpm.rating} profit={rpm.dailyProfit}万/日");

            // 短途低竞争
            var rpm2 = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 1200f,
                fuelPricePerTon = 800f,
                competitorSupply = 0.2f,
                demandMultiplier = 1.5f,
                brandEquity = 70f
            });
            sb.AppendLine($"RPM short → score={rpm2.score} rating={rpm2.rating} profit={rpm2.dailyProfit}万/日");

            // Cash runway
            float months = CashRunnelCalculator.GetRunwayMonths(300f, 80f);
            sb.AppendLine($"CashRunway 300/80 → {months:F2} 月 ({CashRunnelCalculator.GetRunwayWarning(months)})");
            sb.AppendLine($"CashRunway burn<=0 → {CashRunnelCalculator.GetRunwayMonths(100f, 0f)}");

            // Chemistry
            var chem1 = ChemistryMatcher.Match(new List<string> { "A", "B", "H", "K" });
            sb.AppendLine($"Chem [A,B,H,K] → {string.Join(", ", chem1)}"); // 期望含 B-16, B-01, B-09 等

            var chem2 = ChemistryMatcher.Match(new List<string> { "A", "G" });
            sb.AppendLine($"Chem [A,G] → {string.Join(", ", chem2)}"); // B-01 不应触发（有 G）

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Logic Self Tests", "结果已输出到 Console（Window → General → Console）", "确定");
        }
    }
}
#endif
