using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 智能提示系统（GDB 28.4）——季度末由私人幕僚主动提醒，而非生硬报错。
    /// 判定逻辑在纯 C# SmartAlertLogic；本类负责取数 + 推送消息中心 + 返回展示文案。
    /// </summary>
    public static class SmartAlertManager
    {
        /// <summary>季度末检查四类关键指标，触发则推送消息中心（Warning）并返回文案。</summary>
        public static string OnQuarterEnd(GameState gs)
        {
            if (gs == null) return "";

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            float vacancy = gs.hr != null ? gs.hr.vacancyRate : 0f;
            float consistency = gs.stg != null ? gs.stg.strategicConsistency : 100f;
            float onTime = gs.onTimePercent;

            // 准点率连续低季度计数
            if (onTime < SmartAlertLogic.OnTimeFloor)
                gs.onTimeLowQuarters++;
            else
                gs.onTimeLowQuarters = 0;

            var alerts = SmartAlertLogic.EvaluateAll(runway, vacancy, consistency, onTime, gs.onTimeLowQuarters);
            if (alerts == null || alerts.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            foreach (var a in alerts)
            {
                MessageCenter.Push(MessagePriority.Warning, a.deptId, a.deptName,
                    a.title, $"[{a.advisor}] {a.body}", gs.DateLabel, gs.year, gs.quarter);
                sb.AppendLine($"💡 {a.advisor}：{a.body}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
