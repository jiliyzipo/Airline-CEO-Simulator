using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>Phase 6：财年末完成率与续聘投票结果。</summary>
    public class YearEndEvaluation
    {
        public int finishedYear;
        public int targetsTotal;
        public int targetsCompleted;
        public float completionRate;
        public float reappointmentThreshold;
        public bool metThreshold;
        public VoteOutcome outcome;
        public string title;
        public string body;
        public DifficultyMode difficulty;
        /// <summary>光荣退休时的 CEO 年龄（其余结果为 0）。</summary>
        public int ceoAge;
        /// <summary>光荣退休时的遗产评分（其余结果为 null）。</summary>
        public LegacyScoreResult legacy;
        /// <summary>本合同期满且董事会同意续约。</summary>
        public bool contractRenewed;
    }

    /// <summary>
    /// 计算目标完成率并判定续聘 / 见习保护 / 解雇（GDD 4.2 + Phase 6），
    /// 以及光荣退休（GDB 第4章：65 岁或 20 财年触发）。
    /// </summary>
    public static class YearEndEvaluator
    {
        public const int RetirementAge = 65;     // 光荣退休年龄
        public const int MaxFiscalYears = 20;    // 光荣退休财年上限

        /// <summary>当前日历年下，第 finishedYear 财年结束时 CEO 年龄。</summary>
        public static int CeoAge(int birthYear, int finishedYear)
        {
            int startAge = Math.Max(18, DateTime.Now.Year - birthYear);
            return startAge + (finishedYear - 1);
        }

        /// <summary>是否触发光荣退休（65 岁或 20 财年）。</summary>
        public static bool ShouldRetire(int birthYear, int finishedYear)
        {
            if (finishedYear >= MaxFiscalYears) return true;
            if (birthYear <= 0) return false;
            return CeoAge(birthYear, finishedYear) >= RetirementAge;
        }

        public static YearEndEvaluation Evaluate(
            IReadOnlyList<BoardTarget> targets,
            int finishedYear,
            DifficultyMode difficulty)
        {
            int total = targets != null ? targets.Count : 0;
            int done = 0;
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] != null && targets[i].completed)
                        done++;
                }
            }

            float rate = total > 0 ? done * 100f / total : 0f;
            float threshold = BoardEconomics.ReappointmentThreshold(difficulty);
            bool met = rate + 0.001f >= threshold;

            var eval = new YearEndEvaluation
            {
                finishedYear = finishedYear,
                targetsTotal = total,
                targetsCompleted = done,
                completionRate = rate,
                reappointmentThreshold = threshold,
                metThreshold = met,
                difficulty = difficulty
            };

            if (met)
            {
                eval.outcome = VoteOutcome.Reappointed;
                eval.title = "续聘成功";
                eval.body =
                    $"董事会投票通过，续聘你继续执掌第 {finishedYear + 1} 财年。";
            }
            else if (finishedYear <= BoardEconomics.ProbationMaxYear)
            {
                eval.outcome = VoteOutcome.ProbationContinue;
                eval.title = "见习保护";
                eval.body = "董事会决定再给一年机会（见习保护）。";
            }
            else
            {
                eval.outcome = VoteOutcome.Fired;
                eval.title = "董事会解雇";
                eval.body =
                    $"董事会投票罢免 CEO。游戏结束。";
            }

            return eval;
        }

        /// <summary>
        /// 带光荣退休判定的结算：先判退休，否则回退到常规续聘/见习/解雇逻辑。
        /// </summary>
        public static YearEndEvaluation Evaluate(
            IReadOnlyList<BoardTarget> targets,
            int finishedYear,
            DifficultyMode difficulty,
            int ceoBirthYear,
            GameState gs)
        {
            if (ShouldRetire(ceoBirthYear, finishedYear))
            {
                int age = CeoAge(ceoBirthYear, finishedYear);
                var legacy = LegacyScoreCalculator.Calc(LegacySnapshotBuilder.FromGameState(gs));
                return new YearEndEvaluation
                {
                    finishedYear = finishedYear,
                    targetsTotal = targets != null ? targets.Count : 0,
                    targetsCompleted = targets != null ? CountCompleted(targets) : 0,
                    completionRate = targets != null && targets.Count > 0
                        ? CountCompleted(targets) * 100f / targets.Count
                        : 0f,
                    reappointmentThreshold = BoardEconomics.ReappointmentThreshold(difficulty),
                    metThreshold = true,
                    outcome = VoteOutcome.Retired,
                    title = "光荣退休",
                    body = $"你以 {age} 岁光荣退休，执掌航空 {finishedYear} 个财年。" +
                           $"遗产评级 {legacy.grade}（总分 {legacy.total:F0}）。",
                    difficulty = difficulty,
                    ceoAge = age,
                    legacy = legacy
                };
            }

            var eval = Evaluate(targets, finishedYear, difficulty);
            return ApplyCeoContract(eval, gs, finishedYear);
        }

        /// <summary>合同期满未续约 → 解雇；达标期满 → 续约。见习保护不能越过合同终点。</summary>
        public static YearEndEvaluation ApplyCeoContract(YearEndEvaluation eval, GameState gs, int finishedYear)
        {
            if (eval == null || gs == null) return eval;
            if (eval.outcome == VoteOutcome.Retired) return eval;
            gs.EnsureCeoContractDefaults();
            bool expired = finishedYear >= gs.CeoContractEndYear;

            if (eval.outcome == VoteOutcome.Reappointed)
            {
                if (expired)
                {
                    int nextStart = finishedYear + 1;
                    int nextEnd = nextStart + Math.Max(1, gs.ceoContractYears) - 1;
                    eval.contractRenewed = true;
                    eval.title = "合同续约";
                    eval.body =
                        $"董事会批准续约。新合同自第 {nextStart} 财年起，共 {gs.ceoContractYears} 年（至第 {nextEnd} 财年）。";
                }
                return eval;
            }

            if (expired)
            {
                eval.outcome = VoteOutcome.Fired;
                eval.title = "合同到期 · 不予续约";
                eval.body =
                    $"第 {finishedYear} 财年合同期满，年度目标未达标（完成率 {eval.completionRate:F0}%，续聘线 {eval.reappointmentThreshold:F0}%）。" +
                    "董事会拒绝续约，CEO 被解雇。游戏结束。";
                return eval;
            }

            if (eval.outcome == VoteOutcome.Fired)
            {
                eval.title = "董事会解雇";
                eval.body =
                    $"合同尚未到期（至第 {gs.CeoContractEndYear} 财年），但董事会对任期内业绩不满（完成率 {eval.completionRate:F0}%）。" +
                    "提前解除合同，游戏结束。";
            }
            return eval;
        }

        private static int CountCompleted(IReadOnlyList<BoardTarget> targets)
        {
            int done = 0;
            if (targets == null) return 0;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null && targets[i].completed) done++;
            return done;
        }
    }
}
