using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// FIN 数值引擎（Phase 2 Week 5 · 纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：WACC / 信用加点、年度预算建议与董事会投票、碳配额、紧急融资条款、破产重组影响。
    /// 单位约定：货币凡未注明均为「万美元」；碳价为「美元/吨 CO2」。
    /// </summary>
    public static class FinanceEconomics
    {
        /// <summary>碳价参考（EU ETS / CORSIA 近似，美元/吨）。</summary>
        public const float CarbonPriceUsdPerTon = 85f;

        /// <summary>喷气燃料每吨 CO2 排放系数（Jet A1 ≈ 3.16 kg CO2 / kg 燃料）。</summary>
        public const float Co2PerTonFuel = 3.16f;

        // —— 预算建议区间（现金比例）——
        public const float BudgetLowFrac = 0.40f;
        public const float BudgetHighFrac = 0.60f;

        /// <summary>董事会席位数（n22：提交投票需 ≥5 票通过）。</summary>
        public const int BoardSeats = 7;
        public const int BoardVotesRequired = 5;

        /// <summary>各部门默认预算份额（合计 1.0）。</summary>
        public static readonly (string id, string name, float share)[] DeptShares =
        {
            ("n13_运营部", "运营部", 0.20f),
            ("n17_机务工程部", "机务工程部", 0.18f),
            ("n14_市场销售部", "市场销售部", 0.16f),
            ("n18_人力资源部", "人力资源部", 0.12f),
            ("n16_战略发展部", "战略发展部", 0.10f),
            ("n19_安全质量部", "安全质量部", 0.08f),
            ("n15_财务部", "财务部", 0.06f),
            ("n20_董事会", "董事会", 0.10f)
        };

        /// <summary>按部门 id 取默认预算份额（用于季度支出分摊），未知部门返回 0。</summary>
        public static float ShareOf(string deptId)
        {
            if (string.IsNullOrEmpty(deptId)) return 0f;
            for (int i = 0; i < DeptShares.Length; i++)
                if (DeptShares[i].id == deptId) return DeptShares[i].share;
            return 0f;
        }

        #region WACC / 信用加点

        /// <summary>按信用评级与宏观阶段的加权平均资本成本（%，5~25 区间）。</summary>
        public static float Wacc(CreditRating rating, string economicPhase)
        {
            float baseWacc = rating switch
            {
                CreditRating.AAA => 6.0f,
                CreditRating.AA => 6.5f,
                CreditRating.A => 7.0f,
                CreditRating.BBB => 8.0f,
                CreditRating.BB => 9.5f,
                CreditRating.B => 12.0f,
                CreditRating.C => 15.0f,
                _ => 20.0f
            };
            float macro = NormalizePhase(economicPhase) switch
            {
                "recession" => 2.0f,
                "boom" => -0.5f,
                _ => 0f
            };
            return Clamp(baseWacc + macro, 5f, 25f);
        }

        /// <summary>贷款利率 = 无风险利率 + 信用加点（%）。</summary>
        public static float CreditSpread(CreditRating rating) => rating switch
        {
            CreditRating.AAA => 0.5f,
            CreditRating.AA => 1.0f,
            CreditRating.A => 1.5f,
            CreditRating.BBB => 2.5f,
            CreditRating.BB => 4.5f,
            CreditRating.B => 7.0f,
            CreditRating.C => 10.0f,
            _ => 15.0f
        };

        public static float BankLoanRate(CreditRating rating, string economicPhase)
        {
            float macro = NormalizePhase(economicPhase) == "recession" ? 1.0f : 0f;
            return Clamp(5f + CreditSpread(rating) + macro, 5f, 16f);
        }

        /// <summary>纯函数信用分（对齐 LoanManager，供离线模拟/测试复用）。</summary>
        public static float CreditScore(float cash, float monthlyBurn, float boardSatisfaction)
        {
            float runway = CashRunnelCalculator.GetRunwayMonths(cash, monthlyBurn);
            float cashFactor = Clamp01(cash / 500f) * 28f;
            float runwayFactor = Clamp(runway / 6f, 0f, 1.5f) * 28f;
            float satFactor = Clamp01(boardSatisfaction / 100f) * 14f;
            return Clamp(35f + cashFactor + runwayFactor + satFactor, 0f, 100f);
        }

        public static CreditRating EstimateCreditRating(float cash, float monthlyBurn, float boardSatisfaction)
        {
            float s = CreditScore(cash, monthlyBurn, boardSatisfaction);
            if (s >= 80f) return CreditRating.AAA;
            if (s >= 70f) return CreditRating.AA;
            if (s >= 60f) return CreditRating.A;
            if (s >= 50f) return CreditRating.BBB;
            if (s >= 40f) return CreditRating.BB;
            if (s >= 30f) return CreditRating.B;
            if (s >= 20f) return CreditRating.C;
            return CreditRating.D;
        }

        static string NormalizePhase(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return "";
            string p = phase.Trim().ToLowerInvariant();
            if (p.Contains("recess") || p.Contains("萧")) return "recession";
            if (p.Contains("boom") || p.Contains("繁")) return "boom";
            if (p.Contains("recover") || p.Contains("复")) return "recovery";
            return "";
        }

        #endregion

        #region 年度预算

        public static float BudgetBandLow(float cash) => Math.Max(0f, cash * BudgetLowFrac);
        public static float BudgetBandHigh(float cash) => Math.Max(0f, cash * BudgetHighFrac);
        public static float RecommendedBudget(float cash) => cash * 0.5f;

        /// <summary>董事会投票预估（0~7 票）。现金占比越合理、满意度越高，票数越多。</summary>
        public static int EstimateBoardVotes(float totalBudgetWan, float cash, float boardSatisfaction)
        {
            if (cash <= 0f) return 0;
            float frac = totalBudgetWan / cash;
            int votes;
            if (frac <= BudgetLowFrac) votes = 6;           // 保守预算
            else if (frac <= BudgetHighFrac) votes = 5;     // 建议区间
            else if (frac <= 0.75f) votes = 4;              // 略超
            else votes = 2;                                  // 激进

            if (boardSatisfaction >= 70f) votes += 1;
            else if (boardSatisfaction < 40f) votes -= 1;

            return Clamp(votes, 0, BoardSeats);
        }

        public static bool BudgetApproved(float totalBudgetWan, float cash, float boardSatisfaction) =>
            EstimateBoardVotes(totalBudgetWan, cash, boardSatisfaction) >= BoardVotesRequired;

        #endregion

        #region 碳配额

        /// <summary>月油耗（吨）近似：基准 + 航线增量（对齐 HedgeManager）。</summary>
        public static float EstimateMonthlyFuelTons(int activeRoutes) => 400f + activeRoutes * 120f;

        /// <summary>年排放（吨 CO2）。</summary>
        public static float EstimateAnnualEmissionsTons(int activeRoutes)
        {
            return EstimateMonthlyFuelTons(activeRoutes) * 12f * Co2PerTonFuel;
        }

        /// <summary>碳负债（万美元）= max(0, 排放 − 已购配额) × 碳价。</summary>
        public static float CarbonLiabilityWan(float emissionsTons, float purchasedTons, float carbonPrice)
        {
            float gap = Math.Max(0f, emissionsTons - purchasedTons);
            return gap * carbonPrice / 10000f;
        }

        /// <summary>单航线年碳成本（万美元）：燃油量 × CO2 系数 × 碳价 × 覆盖系数。</summary>
        public static float CarbonCostPerRouteYearWan(
            float distanceKm, string aircraftType, int weeklyFlights, float carbonPrice)
        {
            float burnPerKm = EconomyBalanceParams.FuelBurnPerThousandKm(aircraftType) / 1000f;
            float fuelPerFlight = distanceKm * burnPerKm;
            float fuelPerYear = fuelPerFlight * weeklyFlights * 52f;
            float co2 = fuelPerYear * Co2PerTonFuel;
            return co2 * carbonPrice / 10000f * 0.5f;
        }

        #endregion

        #region 紧急融资条款

        public struct FinancingInput
        {
            public float cash;
            public float monthlyBurn;
            public CreditRating rating;
            public string economicPhase;
            public float boardSatisfaction;
            public int fleetCount;
        }

        public static bool IsBailoutUnlocked(string economicPhase) =>
            NormalizePhase(economicPhase) == "recession";

        /// <summary>按融资方式生成条款（n103）。银行/过桥/增资/卖资产/政府救助。</summary>
        public static FinancingTerm BuildFinancingTerm(FinancingKind kind, in FinancingInput input)
        {
            switch (kind)
            {
                case FinancingKind.Bank:
                {
                    float amount = Math.Max(80f, input.monthlyBurn * 4.5f);
                    float rate = BankLoanRate(input.rating, input.economicPhase);
                    return new FinancingTerm
                    {
                        kind = (int)kind,
                        label = "🏦 银行贷款",
                        amountWan = amount,
                        annualRate = rate,
                        termMonths = 12,
                        repayment = "等额本息",
                        collateral = "飞机 / 时刻槽 / 航权",
                        condition = input.rating >= CreditRating.BBB ? "信用评级满足 BBB" : "⚠ 信用不足 BBB，银行拒贷",
                        riskNote = "交叉违约条款 · 最低现金比 / 资产负债率上限"
                    };
                }
                case FinancingKind.Bridge:
                {
                    float amount = Math.Max(40f, input.monthlyBurn * 2f);
                    float rate = 20f + Math.Max(0f, 24f - input.cash / Math.Max(1f, input.monthlyBurn) * 3f);
                    return new FinancingTerm
                    {
                        kind = (int)kind,
                        label = "🌉 过桥贷款",
                        amountWan = amount,
                        annualRate = Clamp(rate, 20f, 28f),
                        termMonths = 6,
                        repayment = "到期还本",
                        collateral = "后续融资 / 资产出售",
                        condition = "无信用限制",
                        riskNote = "利率较高 · 偿还来源依赖后续融资"
                    };
                }
                case FinancingKind.Equity:
                {
                    float amount = Math.Max(120f, input.monthlyBurn * 9f);
                    float dilution = 8f + (input.boardSatisfaction < 50f ? 6f : 0f);
                    return new FinancingTerm
                    {
                        kind = (int)kind,
                        label = "💹 股东增资",
                        amountWan = amount,
                        annualRate = 0f,
                        termMonths = 0,
                        dilutionPercent = dilution,
                        repayment = "—",
                        collateral = "新增股份 · 现有股东优先认购权",
                        condition = input.boardSatisfaction >= 40f ? "董事会满意度 ≥ 40" : "⚠ 董事会可能否决（满意度 < 40）",
                        riskNote = $"股权稀释约 {dilution:F0}% · 可能触发董事会改组"
                    };
                }
                case FinancingKind.SellAsset:
                {
                    float amount = Math.Max(60f, input.fleetCount * 40f * 0.8f);
                    return new FinancingTerm
                    {
                        kind = (int)kind,
                        label = "🏭 出售资产",
                        amountWan = amount,
                        annualRate = 0f,
                        termMonths = 0,
                        repayment = "—",
                        collateral = "闲置飞机 / 时刻槽 / 航线权 / 不动产",
                        condition = "急售折价 10–30%",
                        riskNote = "资产剥离后运力下降 · 品牌连贯性受损"
                    };
                }
                case FinancingKind.GovBailout:
                default:
                {
                    float amount = Math.Max(200f, input.monthlyBurn * 6f);
                    return new FinancingTerm
                    {
                        kind = (int)kind,
                        label = "🏛 政府救助",
                        amountWan = amount,
                        annualRate = 5f,
                        termMonths = 24,
                        repayment = "到期还本",
                        collateral = "政府持股 · 董事会政府席位",
                        condition = IsBailoutUnlocked(input.economicPhase) ? "仅萧条期解锁" : "⚠ 当前非萧条期，不可用",
                        riskNote = "附加航线义务 / 定价限制 · 救助后监管期限"
                    };
                }
            }
        }

        /// <summary>拒绝或到期未融资：供应商催款手续费 + 烧钱加成。</summary>
        public static void CrisisInaction(float monthlyBurn, out float feeWan, out float burnBonusWan)
        {
            float burn = monthlyBurn < 0f ? 0f : monthlyBurn;
            feeWan = Math.Max(15f, burn * 0.35f);
            burnBonusWan = Math.Max(4f, burn * 0.08f);
        }

        #endregion

        #region 破产重组影响

        public struct RestructuringImpact
        {
            public float cashRecoveredWan;   // 资产处置回笼
            public float routesCutPercent;   // 亏损航线削减
            public float fleetCutPercent;    // 机队削减
            public float brandLoss;          // 品牌净值损失
            public float staffCutPercent;    // 裁员比例
        }

        public static RestructuringImpact EstimateRestructuring(int fleetCount, int activeRoutes, float cash, float monthlyBurn)
        {
            float recovered = Math.Max(40f, fleetCount * 40f * 0.75f);
            return new RestructuringImpact
            {
                cashRecoveredWan = recovered,
                routesCutPercent = 0.5f,
                fleetCutPercent = 0.15f,
                brandLoss = 25f,
                staffCutPercent = 0.15f
            };
        }

        /// <summary>重组后跑道（月）：(现金 + 回笼) / 月烧钱。</summary>
        public static float RunwayAfterRestructuring(float cash, float cashRecoveredWan, float monthlyBurn)
        {
            if (monthlyBurn <= 0f) return CashRunnelCalculator.InfiniteRunwayMonths;
            return Math.Max(0f, cash + cashRecoveredWan) / monthlyBurn;
        }

        #endregion

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static float Clamp01(float v) => Clamp(v, 0f, 1f);

        static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
