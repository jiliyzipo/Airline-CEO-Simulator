using System.Collections.Generic;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 融资贷款的纯计算口径（2026-10-21 · K11）。
    ///
    /// 存在的理由很具体：同一笔「月均还本付息」以前有两份实现 ——
    /// 选司预览里一份（<c>TakeoverWorldBuilder.SumDebtServiceMonth</c>）、
    /// 财务面板里一份（<c>GameState.EffectiveMonthlyBurn</c>，且漏掉了）。
    /// 两份写的必然结局是分叉：预览算 8.0 个月，玩家点进局里看到 9.7 个月。
    ///
    /// 放在 <c>Logic</c> 而不是 <c>Systems</c>：<c>Systems</c> 里的 <c>LoanManager</c> 是 MonoBehaviour，
    /// 平衡工具（<c>tools/balance</c>）编译不进 Unity 引擎，只能引用静态纯函数。
    /// 纯函数 + 单一实现，是让「预览」和「局内」永远同一个数的唯一办法。
    /// </summary>
    public static class LoanEconomics
    {
        /// <summary>权益型贷款不还本，跑道上不应出现偿付压力。</summary>
        public const string EquityType = "equity";

        /// <summary>
        /// 存量贷款的**月均还本付息**（万美元/月）。
        ///
        /// 还款实际发生在季末：一次扣 <c>monthlyPayment × min(3, monthsRemaining)</c>
        /// （见 <c>LoanManager.ProcessQuarterRepayments</c>）。除以 3 得月均，
        /// 与季末一次扣的保费同一套处理方式 —— 跑道衡量的是「现金还能撑多久」，
        /// 强制的现金流出必须计入，无论它哪天记账。
        ///
        /// 折旧到最后一季的贷款按 <c>monthlyPayment × monthsRemaining / 3</c> 摊，
        /// 不能整笔计入，否则还清前最后一个月会凭空翻倍。
        /// </summary>
        public static float MonthlyDebtService(IList<Loan> loans)
        {
            if (loans == null) return 0f;
            float s = 0f;
            for (int i = 0; i < loans.Count; i++)
            {
                var l = loans[i];
                if (!IsAmortizing(l)) continue;
                int months = Mathf.Min(3, l.monthsRemaining);
                s += Mathf.Max(0f, l.monthlyPayment) * months / 3f;
            }
            return s;
        }

        /// <summary>贷款是否仍产生还本付息现金流出（排除空项、权益型、已还清）。</summary>
        public static bool IsAmortizing(Loan loan)
            => loan != null && loan.type != EquityType && loan.monthsRemaining > 0;
    }
}
