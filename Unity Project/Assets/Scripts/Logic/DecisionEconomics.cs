using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// CEO 决策会议五部门意见引擎（Phase 3 Week 11）。
    /// 纯 C#，Unity 与 tools/balance 共用，保证数值口径一致。
    /// </summary>
    public static class DecisionEconomics
    {
        public const string DeptFin = "FIN";
        public const string DeptOps = "OPS";
        public const string DeptMkt = "MKT";
        public const string DeptHr = "HR";
        public const string DeptSaf = "SAF";

        public static readonly string[] Departments = { DeptFin, DeptOps, DeptMkt, DeptHr, DeptSaf };

        /// <summary>计算五个部门的意见（红黄绿 + 量化意见 + 评分）。</summary>
        public static DeptOpinion[] EvaluateOpinions(DecisionProposal p, DecisionSnapshot s)
        {
            if (p == null) p = new DecisionProposal { topic = "综合议题", category = DecisionCategories.Generic };
            if (s == null) s = new DecisionSnapshot();

            var list = new List<DeptOpinion>(5)
            {
                EvaluateFinance(p, s),
                EvaluateOps(p, s),
                EvaluateMarketing(p, s),
                EvaluateHr(p, s),
                EvaluateSafety(p, s)
            };
            return list.ToArray();
        }

        private static DeptOpinion EvaluateFinance(DecisionProposal p, DecisionSnapshot s)
        {
            bool affordable = s.cash + 0.001f >= p.costWan;
            bool comfortable = s.cash - p.costWan >= s.monthlyBurn * 3f;
            float runway = s.runwayMonths;
            float score = Math.Clamp(40f + (affordable ? 40f : 0f) + (comfortable ? 15f : 0f) + Math.Clamp(runway * 2f, 0f, 10f), 0f, 100f);

            if (!affordable && p.costWan > 0f)
            {
                return new DeptOpinion
                {
                    deptCode = DeptFin,
                    color = DeptOpinionColor.Red,
                    headline = "现金不足以支付",
                    detail = $"需 {p.costWan:F0} 万，当前现金 {s.cash:F0} 万，缺口 {p.costWan - s.cash:F0} 万。",
                    score = score
                };
            }
            if (!comfortable || runway < 4f)
            {
                return new DeptOpinion
                {
                    deptCode = DeptFin,
                    color = DeptOpinionColor.Yellow,
                    headline = "资金面偏紧",
                    detail = $"支付后现金 {s.cash - p.costWan:F0} 万，现金跑道约 {runway:F1} 个月，建议补充融资方案。",
                    score = score
                };
            }
            return new DeptOpinion
            {
                deptCode = DeptFin,
                color = DeptOpinionColor.Green,
                headline = "资金面可控",
                detail = $"支付后现金 {s.cash - p.costWan:F0} 万，跑道 {runway:F1} 个月，可承受。",
                score = score
            };
        }

        private static DeptOpinion EvaluateOps(DecisionProposal p, DecisionSnapshot s)
        {
            bool route = p.category == DecisionCategories.Route;
            float score = Math.Clamp(s.onTime * 0.6f + (route ? 20f : 30f), 0f, 100f);

            if (s.onTime < 75f)
                return Opinion(DeptOps, DeptOpinionColor.Red, "运营保障存在短板",
                    $"准点率仅 {s.onTime:F0}%，低于 85% 目标，先提升运行质量再上量。", score);
            if (s.onTime < 85f)
                return Opinion(DeptOps, DeptOpinionColor.Yellow, "运营压力偏高",
                    $"准点率 {s.onTime:F0}%，接近警戒线，建议补充机组/时刻方案。", score);
            return Opinion(DeptOps, DeptOpinionColor.Green, "运营衔接良好",
                $"准点率 {s.onTime:F0}%，{(route ? "航线时刻与运力可衔接。" : "现有运力可支撑新增需求。")}", score);
        }

        private static DeptOpinion EvaluateMarketing(DecisionProposal p, DecisionSnapshot s)
        {
            float score = Math.Clamp(s.loadFactor * 0.8f + (s.activeRoutes > 0 ? 10f : 0f) + 10f, 0f, 100f);

            if (p.category == DecisionCategories.Route && p.expectedDailyProfit <= 0f)
                return Opinion(DeptMkt, DeptOpinionColor.Red, "收益预测为负",
                    $"预估日利润 {p.expectedDailyProfit:F1} 万，RPM 评估不达标，建议先做收益评估。", 35f);
            if (s.loadFactor < 70f)
                return Opinion(DeptMkt, DeptOpinionColor.Yellow, "需求偏弱",
                    $"整体客座率 {s.loadFactor:F0}%，低于 75% 目标，需更细的定价/旺季策略。", score);
            return Opinion(DeptMkt, DeptOpinionColor.Green, "需求侧向好",
                $"客座率 {s.loadFactor:F0}%，月收入 {s.monthlyRevenue:F0} 万，需求可支撑。", score);
        }

        private static DeptOpinion EvaluateHr(DecisionProposal p, DecisionSnapshot s)
        {
            bool recruit = p.category == DecisionCategories.Recruit;
            float score = Math.Clamp(50f + s.boardSatisfaction * 0.5f, 0f, 100f);

            if (s.boardSatisfaction < 40f)
                return Opinion(DeptHr, DeptOpinionColor.Red, "组织稳定风险",
                    $"董事会/员工满意度仅 {s.boardSatisfaction:F0}，存在离职与罢工风险，先稳军心。", score);
            if (recruit && s.boardSatisfaction < 60f)
                return Opinion(DeptHr, DeptOpinionColor.Yellow, "招聘吸引力一般",
                    $"满意度 {s.boardSatisfaction:F0}，关键岗位招聘成功率可能偏低，建议补充薪酬方案。", score);
            return Opinion(DeptHr, DeptOpinionColor.Green, "人力可支撑",
                $"满意度 {s.boardSatisfaction:F0}，{(recruit ? "招聘渠道与排期已就绪。" : "现有编制可覆盖。")}", score);
        }

        private static DeptOpinion EvaluateSafety(DecisionProposal p, DecisionSnapshot s)
        {
            float score = Math.Clamp(s.safetyIndex * 0.7f + 30f, 0f, 100f);

            if (s.safetyIndex < 60f)
                return Opinion(DeptSaf, DeptOpinionColor.Red, "安全底线告警",
                    $"安全指数 {s.safetyIndex:F0}，低于 60，任何扩张都应暂停，先补安全投入。", score);
            if (s.safetyIndex < 80f)
                return Opinion(DeptSaf, DeptOpinionColor.Yellow, "安全余量不足",
                    $"安全指数 {s.safetyIndex:F0}，建议同步补充安全预算与审计整改。", score);
            return Opinion(DeptSaf, DeptOpinionColor.Green, "安全状态良好",
                $"安全指数 {s.safetyIndex:F0}，无重大隐患。", score);
        }

        private static DeptOpinion Opinion(string dept, DeptOpinionColor c, string headline, string detail, float score)
        {
            return new DeptOpinion { deptCode = dept, color = c, headline = headline, detail = detail, score = score };
        }

        /// <summary>汇总裁决建议：批准 / 补条件 / 拒绝。</summary>
        public static string Verdict(IReadOnlyList<DeptOpinion> opinions)
        {
            int red = 0, yellow = 0, green = 0;
            if (opinions != null)
            {
                for (int i = 0; i < opinions.Count; i++)
                {
                    switch (opinions[i].color)
                    {
                        case DeptOpinionColor.Red: red++; break;
                        case DeptOpinionColor.Yellow: yellow++; break;
                        default: green++; break;
                    }
                }
            }
            if (red > 0) return "reject";
            if (yellow > 0) return "defer";
            return "approve";
        }

        public static string VerdictLabel(string verdict)
        {
            return verdict switch
            {
                "approve" => "建议批准",
                "defer" => "建议补条件后重议",
                "reject" => "建议拒绝",
                _ => "—"
            };
        }

        /// <summary>意见汇总字符串，如「绿3 黄2 红0」。</summary>
        public static string OpinionSummary(IReadOnlyList<DeptOpinion> opinions)
        {
            int red = 0, yellow = 0, green = 0;
            if (opinions != null)
            {
                for (int i = 0; i < opinions.Count; i++)
                {
                    switch (opinions[i].color)
                    {
                        case DeptOpinionColor.Red: red++; break;
                        case DeptOpinionColor.Yellow: yellow++; break;
                        default: green++; break;
                    }
                }
            }
            return $"绿{green} 黄{yellow} 红{red}";
        }

        // ──────── n104 补条件模板 ────────

        public static int PriorityFromString(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (s.Contains("强制")) return 2;
            if (s.Contains("必要")) return 1;
            return 0;
        }

        public static string PriorityLabel(int p)
        {
            return p switch
            {
                2 => "🔴 强制",
                1 => "🟡 必要",
                _ => "🟢 建议"
            };
        }

        /// <summary>根据议题类别生成智能条件模板（预填）。</summary>
        public static List<SupplementCondition> ConditionTemplates(string category)
        {
            var list = new List<SupplementCondition>();
            switch (category)
            {
                case DecisionCategories.Purchase:
                    Add(list, "财务补充数据：NPV 重算 + 融资方案比选", "FIN", 1, "Q+1", "NPV>0 且融资成本最低");
                    Add(list, "运营保障方案：机组/机位/交付排期", "OPS", 2, "Q+1", "交付后可立即投入运行");
                    Add(list, "OEM 交付确认函（交付窗口）", "MNT", 2, "Q+1", "收到 OEM 书面确认");
                    Add(list, "市场预测更新（目标航线收益）", "MKT", 1, "Q+1", "RPM 评分 ≥ 70");
                    Add(list, "维修成本细化（前三年）", "MNT", 0, "Q+2", "单位维修成本测算完成");
                    break;
                case DecisionCategories.Recruit:
                    Add(list, "薪酬市场数据补充", "HR", 1, "Q+1", "对比同岗市场分位");
                    Add(list, "用人部门确认签核", "OPS", 2, "Q+1", "部门负责人书面签核");
                    Add(list, "培训排期确认", "HR", 0, "Q+2", "入职培训排期落定");
                    Add(list, "招聘周期重新评估", "HR", 1, "Q+1", "更新 lead time 估算");
                    Add(list, "外包替代方案调研", "HR", 0, "Q+2", "外包成本/质量对比");
                    break;
                case DecisionCategories.Route:
                    Add(list, "时刻槽确认", "STG", 2, "Q+1", "取得目标机场时刻批复");
                    Add(list, "航权批复进度", "STG", 2, "Q+1", "航权批复或明确时限");
                    Add(list, "竞争态势更新", "MKT", 1, "Q+1", "竞对运力与票价最新数据");
                    Add(list, "收益预测修正", "MKT", 1, "Q+1", "RPM 评估台重算日利");
                    break;
                default:
                    Add(list, "财务补充数据", "FIN", 1, "Q+1", "补充现金流与成本测算");
                    Add(list, "运营可行性方案", "OPS", 1, "Q+1", "提交落地排期");
                    Add(list, "风险评估与预案", "STG", 0, "Q+2", "列出主要风险与应对");
                    break;
            }
            return list;
        }

        private static void Add(List<SupplementCondition> list, string desc, string owner, int priority, string deadline, string criteria)
        {
            list.Add(new SupplementCondition
            {
                id = "C" + (list.Count + 1),
                description = desc,
                ownerDept = owner,
                priority = priority,
                deadline = deadline,
                criteria = criteria,
                done = false
            });
        }

        /// <summary>补条件期间的延期损失/机会成本估算（返回人类可读文案）。</summary>
        public static string DeferLossText(DecisionProposal p)
        {
            if (p == null) return "暂无量化损失估算。";
            if (p.category == DecisionCategories.Purchase && p.count > 0 && !string.IsNullOrEmpty(p.aircraftType))
                return $"若延误一个季度：预计运力缺口约 {p.count} 架 {p.aircraftType}，可能错过旺季，机会成本约 {Math.Max(1, p.count) * 60:F0} 万。";
            if (p.category == DecisionCategories.Route && p.expectedDailyProfit > 0f)
                return $"若延误一个季度：按日利 {p.expectedDailyProfit:F1} 万计，机会成本约 {p.expectedDailyProfit * 90:F0} 万。";
            return $"延期一个季度可能产生机会成本与连锁影响（上游运力/市场份额受限）。";
        }
    }
}
