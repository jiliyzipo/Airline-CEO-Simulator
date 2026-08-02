using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    public enum DifficultyMode
    {
        Easy = 0,   // 续聘线 50%
        Normal = 1, // 60%
        Hard = 2    // 75%
    }

    public enum VoteOutcome
    {
        Reappointed,       // 续聘成功
        ProbationContinue, // 见习保护：再给一年
        Fired              // 解雇 → Game Over
    }

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
    }

    /// <summary>
    /// 计算目标完成率并判定续聘 / 见习保护 / 解雇（GDD 4.2 + Phase 6）。
    /// </summary>
    public static class YearEndEvaluator
    {
        public const int ProbationMaxYear = 3; // 第 1～3 财年见习保护

        public static float GetReappointmentThreshold(DifficultyMode difficulty)
        {
            return difficulty switch
            {
                DifficultyMode.Easy => 50f,
                DifficultyMode.Hard => 75f,
                _ => 60f
            };
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
            float threshold = GetReappointmentThreshold(difficulty);
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
                    $"完成率 {rate:F0}%（{done}/{total}），达到续聘线 {threshold:F0}%。\n" +
                    $"董事会投票通过，续聘你继续执掌第 {finishedYear + 1} 财年。";
            }
            else if (finishedYear <= ProbationMaxYear)
            {
                eval.outcome = VoteOutcome.ProbationContinue;
                eval.title = "见习保护";
                eval.body =
                    $"完成率 {rate:F0}%（{done}/{total}），未达续聘线 {threshold:F0}%。\n" +
                    $"董事会决定再给一年机会（见习保护）。\n" +
                    $"（见习期覆盖第 1～{ProbationMaxYear} 财年）";
            }
            else
            {
                eval.outcome = VoteOutcome.Fired;
                eval.title = "董事会解雇";
                eval.body =
                    $"完成率 {rate:F0}%（{done}/{total}），连续未达续聘线 {threshold:F0}%。\n" +
                    $"董事会投票罢免 CEO。游戏结束。";
            }

            return eval;
        }

        public static float SatisfactionDelta(YearEndEvaluation eval)
        {
            if (eval == null) return 0f;
            return eval.outcome switch
            {
                VoteOutcome.Reappointed => eval.completionRate >= 80f ? 10f : 8f,
                VoteOutcome.ProbationContinue => -4f,
                VoteOutcome.Fired => -15f,
                _ => 0f
            };
        }
    }
}
