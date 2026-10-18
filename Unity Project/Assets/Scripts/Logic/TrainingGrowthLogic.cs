using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>P14：培训投入 → 高潜成长 → 晋升 → 部门效率。</summary>
    public static class TrainingGrowthLogic
    {
        /// <summary>
        /// 每季推进所有在办项目。完成率来源有二：
        /// 有 <see cref="TrainingProgram.durationQuarters"/> 的（证照考取）按计划季度均分，
        /// 其余的沿用培训中心等级的固定速度。
        /// </summary>
        public static string AdvanceAll(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            if (hr?.trainings == null || hr.trainings.Count == 0) return "";
            PersonnelGenerator.EnsurePool(gs);

            var sb = new StringBuilder();
            for (int i = 0; i < hr.trainings.Count; i++)
            {
                var t = hr.trainings[i];
                if (t == null || t.graduated) continue;
                float delta = t.durationQuarters > 0
                    ? 1f / t.durationQuarters
                    : HrEconomics.TrainingProgressDelta(hr.trainingCenterLevel);
                t.completionRate += delta;
                if (t.completionRate < 0.999f) continue;
                t.completionRate = 1f;
                t.graduated = true;
                string note = ApplyGraduation(gs, hr, t);
                if (!string.IsNullOrEmpty(note))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(note);
                }
            }

            string result = sb.ToString();
            if (!string.IsNullOrEmpty(result))
                hr.lastTrainingNote = result;
            return result;
        }

        static string ApplyGraduation(GameState gs, HrState hr, TrainingProgram t)
        {
            string track = string.IsNullOrEmpty(t.track) ? "people" : t.track;

            // 证照考取走独立结算：不发效率/潜力，只发证。
            if (track == "cert") return GraduateCert(gs, t);

            var targets = ResolveTargets(gs, t);

            int grown = 0;
            int promoted = 0;
            float maxEffGain = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                float dEff = HrEconomics.TrainingEfficiencyGain(p.potential, hr.trainingCenterLevel);
                float dPot = HrEconomics.TrainingPotentialGain(p.potential);
                p.efficiencyBonus += dEff;
                if (p.efficiencyBonus > 1.35f) p.efficiencyBonus = 1.35f;
                p.potential += dPot;
                if (p.potential > 1f) p.potential = 1f;
                grown++;
                if (maxEffGain < dEff) maxEffGain = dEff;
                // 门槛吃的是「培训累积增益」（efficiencyBonus 以 1.0 为基准），不是 efficiencyBonus 本身。
                if (HrEconomics.CanPromoteFromTraining(p.rank, p.potential, p.efficiencyBonus - 1f))
                {
                    p.rank = "主管";
                    p.baseSalary += 4f;
                    promoted++;
                }
            }

            // 机务/安全的分部结算与「个人是否成长」无关（对价是编制与文化指数，不是个人属性），
            // 因此不得因为名单为空而提前 return —— 否则在培人数永远转不成工程师。
            if (track == "maint")
            {
                var mnt = MaintenanceManager.EnsureState(gs);
                if (mnt != null)
                {
                    int seats = t.enrolled > 0 ? t.enrolled : grown;
                    int fromTrain = mnt.trainingCount < seats ? mnt.trainingCount : seats;
                    mnt.trainingCount -= fromTrain;
                    mnt.engineerCount += fromTrain;
                    if (mnt.fleetAvailability < 99f)
                        mnt.fleetAvailability += 0.4f;
                }
            }
            else if (track == "safety")
            {
                var saf = SafetyManager.EnsureState(gs);
                if (saf != null)
                    saf.cultureIndex = saf.cultureIndex > 98f ? 100f : saf.cultureIndex + 2f;
            }

            BoostAppointedHeads(gs, track, maxEffGain * 0.45f);
            string tail = promoted > 0 ? $" · {promoted} 人晋升主管" : "";
            if (grown == 0) return $"「{t.name}」结业：在培人员已全部离岗（个人成长 0 人）{tail}。";
            return $"「{t.name}」结业：{grown} 名关键岗成长（效率最高 +{maxEffGain:F2}）{tail}。";
        }

        /// <summary>证照结业：给每位在册人员发证（已持有则顺延有效期）。</summary>
        static string GraduateCert(GameState gs, TrainingProgram t)
        {
            var def = CertificationCatalog.Find(t.certId);
            if (def == null) return $"「{t.name}」结业，但证照定义已失效（无收益）。";

            var targets = ResolveTargets(gs, t);
            int got = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                CertificationLogic.Grant(targets[i], def, gs.SimUtc);
                got++;
            }
            if (got == 0) return $"「{t.name}」结业，但在培人员已全部离岗（未发证）。";
            return $"「{t.name}」结业：{got} 人取得{def.name}。";
        }

        /// <summary>
        /// 解析在册人员。
        /// 有名单（新档）→ 按 id 取人，跳过已离职/待聘者；
        /// 无名单（旧档）→ 回退到旧的「按潜力降序补足」逻辑，保证旧档行为不变。
        /// </summary>
        static List<PersonnelData> ResolveTargets(GameState gs, TrainingProgram t)
        {
            if (t != null && t.memberIds != null && t.memberIds.Count > 0)
            {
                var list = new List<PersonnelData>(t.memberIds.Count);
                for (int i = 0; i < t.memberIds.Count; i++)
                {
                    var p = PersonnelGenerator.Find(gs, t.memberIds[i]);
                    if (p == null || p.onMarket) continue;
                    list.Add(p);
                }
                return list;
            }
            return PickTargets(gs, string.IsNullOrEmpty(t.track) ? "people" : t.track, t.enrolled);
        }

        /// <summary>
        /// 入册选人：在职 + 非运行序列 + 潜力达标（TrainingEnrollMinPotential），按潜力降序取前 N。
        /// 由 <see cref="HrManager.AddTraining"/> 在**启动时**调用并把结果记入 memberIds——
        /// 此前这一步发生在结业那一刻，玩家无从预知也无法指定个人。
        /// </summary>
        public static List<PersonnelData> SelectMembers(GameState gs, int take, string requiredDept = null)
        {
            var list = new List<PersonnelData>();
            if (gs?.staffPool == null) return list;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket) continue;
                if (p.isOperational) continue;   // 运行序列（飞行员/机务等渠道批量岗）不参与关键岗培训与晋升
                if (p.potential < HrEconomics.TrainingEnrollMinPotential) continue;
                if (!string.IsNullOrEmpty(requiredDept)
                    && !string.Equals(p.department, requiredDept, System.StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(p);
            }
            list.Sort((a, b) => b.potential.CompareTo(a.potential));
            int cap = take > 0 ? take : 8;
            if (cap > list.Count) cap = list.Count;
            if (cap < list.Count) list.RemoveRange(cap, list.Count - cap);
            return list;
        }

        /// <summary>旧档回退：按轨道选人（无名单的培训记录结业时用）。</summary>
        static List<PersonnelData> PickTargets(GameState gs, string track, int enrolled)
        {
            string dept = track == "maint" ? "MNT" : track == "safety" ? "SAF" : null;
            return SelectMembers(gs, enrolled, dept);
        }

        static void BoostAppointedHeads(GameState gs, string track, float gain)
        {
            if (gain <= 0.001f || DepartmentManager.Instance == null) return;
            for (int d = 0; d < DepartmentCatalog.Codes.Length; d++)
            {
                string code = DepartmentCatalog.Codes[d];
                if (track == "maint" && code != "MNT") continue;
                if (track == "safety" && code != "SAF") continue;
                var head = DepartmentManager.Instance.GetHead(code);
                if (head == null) continue;
                head.efficiencyBonus += gain;
                if (head.efficiencyBonus > 1.35f) head.efficiencyBonus = 1.35f;
            }
        }

        /// <summary>某项目当前在册且仍在岗的人数（进度列表 / 结业预告用）。</summary>
        public static int ActiveRosterCount(GameState gs, TrainingProgram t) =>
            t == null ? 0 : ResolveTargets(gs, t).Count;

        /// <summary>在职关键岗平均效率 → 部门 KPI 附加（0~0.08）。交接期内的人按打折效率计入。</summary>
        public static float DeptStaffLift(GameState gs, string deptCode)
        {
            if (gs?.staffPool == null || string.IsNullOrEmpty(deptCode)) return 0f;
            long now = gs.SimUtc.Ticks;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket) continue;
                if (p.isOperational) continue;   // 运行/职能批量岗不计入关键岗效率均值
                if (!string.Equals(p.department, deptCode, System.StringComparison.OrdinalIgnoreCase)) continue;
                sum += p.EfficiencyAt(now);
                n++;
            }
            if (n == 0) return 0f;
            float avg = sum / n;
            float lift = (avg - 0.50f) * 0.12f;
            if (lift < 0f) return 0f;
            if (lift > 0.08f) return 0.08f;
            return lift;
        }
    }
}
