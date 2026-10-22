#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K11「局内跑道并入还本付息」的隔离回归（2026-10-21）。
    ///
    /// 事故现场：选司界面写「跑道 8.0 个月」，玩家点进局里，财务面板写 9.7 个月。
    /// 差的那 1.7 个月就是 <c>monthlyDebtService</c> —— 贷款在季末一次扣 3 个月，
    /// 逐日计提里没有它，而 <c>GameState.EffectiveMonthlyBurn</c> 只统计逐日计提。
    /// 于是「接管越难、跑道越短」在局内彻底反过来：★2 显示 9.7、★5 显示 3.6。
    ///
    /// 这组断言钉四件事：
    /// · 月供函数本身：权益型不算、已还清不算、最后一季按剩余月份摊；
    /// · 预览与局内共用同一个实现（两处算出来的必须逐分相等，不允许再各写一份）；
    /// · <c>EffectiveMonthlyBurn</c> 真的把月供计入了（差值相等，不是「看起来像」）；
    /// · 四个具名预设的跑道仍然落在设计目标 8 / 6 / 4 / 3 个月。
    ///
    /// 与 <see cref="RpmPreviewRegression"/> 一样是隔离回归：只碰静态函数和内存 GameState。
    /// </summary>
    public static class RunwayDebtRegression
    {
        static readonly (string id, string label, float target)[] Presets =
        {
            // 2026-10-22 D2 第三步：四个具名预设改为「一档一座、按枢纽客流升序」，
            // 修复了 广州★2 / 成都★5 的档位颠倒（详见 PresetAirlines.FromHub 注释与任务书 §18.4）。
            ("preset_startup", "★2 创业 CTU", 8f),
            ("preset_regional", "★3 区域 PEK", 6f),
            ("preset_legacy", "★4 老牌 CAN", 4f),
            ("preset_distressed", "★5 困境 PVG", 3f),
        };

        public static void Run()
        {
            var go = new GameObject("__runway_debt_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.monthlyBurnRate = 500f;   // 给一个明显 > 0 的底，避免 max(burn,0) 把差值夹没

                // ① 月供函数：权益型不还本
                float equity = LoanEconomics.MonthlyDebtService(new List<Loan>
                {
                    new Loan { type = "equity", monthsRemaining = 12, monthlyPayment = 200f }
                });
                bool equityIgnored = Mathf.Abs(equity) < 0.001f;

                // ② 已还清（monthsRemaining = 0）不得再计
                float repaid = LoanEconomics.MonthlyDebtService(new List<Loan>
                {
                    new Loan { type = "bank", monthsRemaining = 0, monthlyPayment = 200f }
                });
                bool repaidIgnored = Mathf.Abs(repaid) < 0.001f;

                // ③ 最后一季按剩余月份摊：只剩 2 个月、月供 90 → 本季只扣 180，月均 60
                float tail = LoanEconomics.MonthlyDebtService(new List<Loan>
                {
                    new Loan { type = "bank", monthsRemaining = 2, monthlyPayment = 90f }
                });
                bool tailAmortized = Mathf.Abs(tail - 60f) < 0.01f;

                // ④ 混装：常规 12 期 100 + 权益 300 + 已还清 50 → 只算 100
                float mixed = LoanEconomics.MonthlyDebtService(new List<Loan>
                {
                    new Loan { type = "bank", monthsRemaining = 12, monthlyPayment = 100f },
                    new Loan { type = "equity", monthsRemaining = 12, monthlyPayment = 300f },
                    new Loan { type = "bank", monthsRemaining = 0, monthlyPayment = 50f },
                });
                bool mixedOk = Mathf.Abs(mixed - 100f) < 0.01f;

                // ⑤ 局内烧钱必须含月供（这是本轮的修复点）
                float baseBurn = gs.EffectiveMonthlyBurn;
                gs.activeLoans = new List<Loan>
                {
                    new Loan { type = "bank", monthsRemaining = 12, monthlyPayment = 100f }
                };
                float withDebt = gs.EffectiveMonthlyBurn;
                bool burnIncludesDebt = Mathf.Abs(withDebt - baseBurn - 100f) < 0.5f;

                // ⑥ 权益型不得抬高烧钱（否则「增资」会被当成负债）
                gs.activeLoans = new List<Loan>
                {
                    new Loan { type = "equity", monthsRemaining = 12, monthlyPayment = 100f }
                };
                bool burnIgnoresEquity = Mathf.Abs(gs.EffectiveMonthlyBurn - baseBurn) < 0.01f;
                gs.activeLoans = new List<Loan>();

                // ⑦ 预览与局内共用实现 + 跑道仍落在设计目标
                var sb = new StringBuilder();
                sb.Append("跑道/月供口径 → 权益排除=").Append(equityIgnored)
                  .Append("　已还清排除=").Append(repaidIgnored)
                  .Append("　末季摊销=").Append(tailAmortized)
                  .Append("　混装=").Append(mixedOk)
                  .Append("　局内含月供=").Append(burnIncludesDebt)
                  .Append("　权益不加烧钱=").Append(burnIgnoresEquity);

                bool presetsOk = true;
                for (int i = 0; i < Presets.Length; i++)
                {
                    var (id, label, target) = Presets[i];
                    var p = TakeoverWorldBuilder.BuildPreview(id);
                    if (p == null)
                    {
                        sb.Append($"　[{label} 预览构建失败]");
                        presetsOk = false;
                        continue;
                    }

                    float debt = LoanEconomics.MonthlyDebtService(p.openingLoans);
                    bool sameSource = Mathf.Abs(debt - p.monthlyDebtService) < 0.01f;
                    float net = p.monthlyCost + p.monthlyFixedCost + p.monthlyDebtService
                                + p.monthlyInsurance - p.monthlyRevenue;
                    float runway = net > 0f ? p.cash / net : float.PositiveInfinity;
                    bool inTarget = Mathf.Abs(runway - target) <= target * 0.03f;
                    if (!sameSource || !inTarget) presetsOk = false;

                    sb.Append($"　{label} 月供 {p.monthlyDebtService:F1}(同源={sameSource}) 跑道 {runway:F2}/{target:F1}");
                }

                bool all = equityIgnored && repaidIgnored && tailAmortized && mixedOk
                           && burnIncludesDebt && burnIgnoresEquity && presetsOk;

                Debug.Log(sb.Append($"　{(all ? "✓" : "✗")}").ToString());
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }
    }
}
#endif
