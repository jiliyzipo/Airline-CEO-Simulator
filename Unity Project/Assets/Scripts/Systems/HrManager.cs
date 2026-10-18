using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// HR 人力资源部系统（Phase 2 Week 10）：招聘管道/培训/绩效薪酬/员工关系/工会谈判/合规。
    /// 全部回写 GameState.hr。
    /// </summary>
    public class HrManager : MonoBehaviour
    {
        public static HrManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static HrState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.hr == null) gs.hr = new HrState();
            if (gs.hr.talentIndex == null) gs.hr.talentIndex = new TalentIndex();
            if (gs.hr.vacancies == null) gs.hr.vacancies = new List<Vacancy>();
            if (gs.hr.trainings == null) gs.hr.trainings = new List<TrainingProgram>();
            if (gs.hr.union == null) gs.hr.union = new UnionState();
            MigrateLaborRate(gs);
            MigrateHeadcount(gs);
            return gs.hr;
        }

        /// <summary>
        /// 旧档迁移：把遗留的「存量人力成本锚点」折算为「每机费率」。
        ///
        /// 接通后年度人力成本 = 每机费率 × 在役机队（见 <see cref="HrEconomics.EffectiveAnnualLaborCostWan"/>），
        /// 故旧档里那个与规模脱节的固定值必须折算，否则要么被当成 0（免费人力），
        /// 要么在机队扩充后不再增长。按当时在役机队折算可延续原有绝对值，之后随规模自动缩放。
        /// </summary>
        static void MigrateLaborRate(GameState gs)
        {
            var hr = gs.hr;
            if (hr == null || hr.laborRateMigrated) return;
            hr.laborRateMigrated = true;
            if (hr.laborCostPerAircraftWan > 0f) return;   // 新档（字段初值 240）无需折算
            gs.GetFleetStaffing(out int n, out _, out _);
            if (n > 0)
            {
                hr.laborCostPerAircraftWan = Mathf.Max(1f, hr.laborCostWan / n);
            }
            else
            {
                // 无机队可折算：保底费率 + 把旧总额转成一次性增量，避免旧档人力瞬间归零。
                hr.laborCostPerAircraftWan = HrEconomics.LaborCostPerAircraftWan;
                hr.laborCompAdditiveWan += Mathf.Max(0f, hr.laborCostWan);
            }
        }

        /// <summary>
        /// 旧档迁移：把遗留的「固定总人数」折算为「每机人数」。与 <see cref="MigrateLaborRate"/> 完全同构。
        ///
        /// 新口径总人数 = 每机人数 × 在役机队（见 <see cref="HrEconomics.EffectiveHeadcount"/>），
        /// 故旧档里那个与规模脱节的固定值必须折算，否则机队扩张后人数不再增长 ——
        /// 培训 ROI（按人均预算）与工会谈判成本（按人头计费）会随规模持续失真。
        /// 按当时在役机队折算可延续原有绝对值，之后随规模自动缩放。
        /// </summary>
        static void MigrateHeadcount(GameState gs)
        {
            var hr = gs.hr;
            if (hr == null || hr.headcountMigrated) return;
            hr.headcountMigrated = true;
            if (hr.headcountPerAircraft > 0f) return;   // 新档（字段初值 90）无需折算
            int legacy = Mathf.Max(0, hr.totalHeadcount);
            gs.GetFleetStaffing(out int n, out _, out _);
            if (n > 0)
            {
                hr.headcountPerAircraft = Mathf.Max(1f, legacy / (float)n);
            }
            else
            {
                // 无机队可折算：保底每机人数 + 把旧总数转成一次性增量，避免旧档人数瞬间归零。
                hr.headcountPerAircraft = HrEconomics.HeadcountPerAircraft;
                hr.headcountAdditive += legacy;
            }
        }

        #region 招聘管道
        /// <summary>渠道岗位 → 归属部门（运行岗归运营部，机务岗归机务部…）。</summary>
        public static string DeptForPosition(string position)
        {
            if (string.IsNullOrEmpty(position)) return "OPS";
            if (position.Contains("飞行") || position.Contains("机长") || position.Contains("乘务")
                || position.Contains("签派") || position.Contains("运控") || position.Contains("客舱")) return "OPS";
            if (position.Contains("机务") || position.Contains("维修") || position.Contains("工程")
                || position.Contains("航材") || position.Contains("定检")) return "MNT";
            if (position.Contains("安全") || position.Contains("监察") || position.Contains("应急")) return "SAF";
            if (position.Contains("销售") || position.Contains("市场") || position.Contains("品牌")
                || position.Contains("收益") || position.Contains("客户")) return "MKT";
            if (position.Contains("财务") || position.Contains("会计") || position.Contains("预算")
                || position.Contains("司库")) return "FIN";
            if (position.Contains("战略") || position.Contains("规划") || position.Contains("航权")
                || position.Contains("投资")) return "STG";
            if (position.Contains("招聘") || position.Contains("薪酬") || position.Contains("培训")) return "HR";
            return "OPS";
        }

        /// <summary>渠道岗位 → 职级。渠道招聘补充的是运行/职能专员级，总监席仍由 CEO 任命。</summary>
        public static string RankForPosition(string position) => "专员";

        /// <summary>
        /// 发起招聘：创建关键岗位空缺 + 招聘管道，扣除招聘成本。
        /// 招聘成本按人才紧缺度浮动；候选人按渠道周期（RecruitLeadWeeks）在 TickDay 落池。
        /// </summary>
        public bool LaunchRecruitment(string position, int needed, int urgency, int channel, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (string.IsNullOrEmpty(position)) { message = "岗位名称不能为空"; return false; }
            if (needed <= 0) { message = "招聘人数必须 >0"; return false; }
            var hr = EnsureState(gs);

            // 编制封顶：运行岗（飞行员/机务/乘务）编制由在役机队规模推导，
            // needed 不得超过「编制 − 已在职 − 在途（已发起未到岗）」。
            // 此前飞行员无任何编制上限，needed 可由玩家无限累加；而到岗人数会累积进
            // headcountAdditive（→ GameState.TotalHeadcount），后者进 UnionCostWan 与 TrainingRoi，
            // 等于无限扩招能凭空推高工会成本与培训 ROI。
            if (RecruitRoomLeft(gs, position, out int establishment, out int alreadyOn, out int inFlight, out int roomLeft)
                && roomLeft <= 0)
            {
                message = $"「{position}」已按机队规模配满（编制 {establishment} 人，在职 {alreadyOn}，在途 {inFlight}）。" +
                          "扩充机队后编制会自动上调。";
                return false;
            }
            if (needed > roomLeft && roomLeft > 0) needed = roomLeft;

            float shortage = ShortageFor(position, hr);
            float costPer = HrEconomics.RecruitCostPerHire(channel, shortage);
            float costWan = needed * costPer / 10000f;
            if (gs.cash < costWan) { message = $"现金不足（招聘成本约 {costWan:F2} 万）。"; return false; }
            gs.cash -= costWan;

            var vac = hr.vacancies.Find(v => v.position == position);
            if (vac != null) { vac.needed += needed; vac.channel = channel; }
            else
            {
                vac = new Vacancy { position = position, needed = needed, hired = 0, urgency = urgency, channel = channel };
                hr.vacancies.Add(vac);
            }
            // 本期招募窗口：候选人按渠道周期到位（周期由此真正参与推进，而不再只是按钮文案）
            vac.startUtcTicks = gs.SimUtc.Ticks;
            vac.dueUtcTicks = gs.SimUtc.AddDays(HrEconomics.RecruitLeadWeeks(channel) * 7.0).Ticks;

            hr.vacancyRate = CalcVacancyRate(hr);
            float weeks = HrEconomics.RecruitLeadWeeks(channel);
            string eta = channel == 3 ? "内部转岗可即时办理" : $"约 {weeks:F0} 周后候选人到位";
            message = $"已发起「{position}」招聘（{needed} 人 · {HrEconomics.ChannelName(channel)} · 成本 {costWan:F2} 万 · 紧缺系数 ×{HrEconomics.ShortageCostFactor(shortage):F2} · {eta}）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 日级推进：渠道周期到期后把候选人落池，使其出现在职业介绍所。
        /// 与 StaffRecruitment.Tick 同一套时间语义；由 TimeManager 驱动。
        /// </summary>
        public static void TickDay(GameState gs, System.DateTime toUtc)
        {
            var hr = EnsureState(gs);
            if (hr?.vacancies == null) return;
            bool changed = false;
            for (int i = 0; i < hr.vacancies.Count; i++)
            {
                var v = hr.vacancies[i];
                if (v == null || v.channel == 3) continue;          // 内部转岗不走外部落池
                if (v.hired >= v.needed) { v.inPool = 0; continue; }
                if (toUtc.Ticks < v.dueUtcTicks) continue;          // 渠道周期未到
                int room = v.needed - v.hired;
                if (v.inPool >= room) continue;                     // 池内已够，等 CEO 处置

                int want = room - v.inPool;
                string tag = HrEconomics.SourceTag(v.channel);
                int made = 0;
                for (int k = 0; k < want; k++)
                {
                    var p = PersonnelGenerator.CreateForRole(gs, DeptForPosition(v.position), RankForPosition(v.position), v.position, tag);
                    if (p == null) break;
                    made++;
                }
                v.inPool += made;
                // 下一批候选人再等一个渠道周期（持续输送，但池量始终不超过剩余需求）
                v.dueUtcTicks = toUtc.AddDays(HrEconomics.RecruitLeadWeeks(v.channel) * 7.0).Ticks;
                if (made > 0)
                {
                    changed = true;
                    gs.AddDecisionLog($"招聘：{HrEconomics.ChannelName(v.channel)}候选人 {made} 人已到位（{v.position}），可在职业介绍所查看。");
                }
            }
            // 培训中心工期与招聘管道共用同一条日级时钟，确保「投资 → 等待 → 启用」有真实节奏。
            if (!string.IsNullOrEmpty(TickTrainingCenter(gs, toUtc))) changed = true;
            if (EnsurePoolCerts(gs) > 0) changed = true;

            // 投机候选池：按渠道把候选人释放进介绍所（与上面两条共用同一条日级时钟）。
            // 放在最后：它只补池、不改招聘案账本，顺序不影响前两者的结果。
            TalentMarket.TickDay(gs, toUtc);

            if (changed) gs.NotifyChanged();
        }

        /// <summary>
        /// 旧档迁移 + 入职发证（由 TickDay 每日调用，幂等）：
        /// ① staffPool 里缺失的 certs 列表补空表（Unity 反序列化缺失字段会留 null）；
        /// ② 运行序列新到岗者补发 1 级基础资质（详见 CertificationLogic.GrantStarterCerts）。
        /// 返回补发的证书数量，供 TickDay 决定要不要 NotifyChanged。
        /// </summary>
        public static int EnsurePoolCerts(GameState gs)
        {
            if (gs?.staffPool == null) return 0;
            int granted = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                CertificationLogic.EnsureCerts(p);
                granted += CertificationLogic.GrantStarterCerts(p, gs.SimUtc);
            }
            return granted;
        }

        /// <summary>招聘到岗：按成功率确认池内候选人；内部转岗渠道则自动调岗补员。</summary>
        public bool RecruitNow(string position, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            var vac = hr.vacancies.Find(v => v.position == position);
            if (vac == null) { message = "该岗位无空缺招聘记录。"; return false; }
            if (vac.hired >= vac.needed) { message = "该岗位已招满。"; return false; }

            if (vac.channel == 3)
                return FillByInternalTransfer(gs, hr, vac, out message);

            TickDay(gs, gs.SimUtc);   // 先按当前时间把到期候选人落池
            var pool = PoolCandidates(gs, HrEconomics.SourceTag(vac.channel), position);
            if (pool.Count == 0)
            {
                double days = System.Math.Max(0.0, (new System.DateTime(vac.dueUtcTicks, System.DateTimeKind.Utc) - gs.SimUtc).TotalDays);
                message = $"候选人尚未到位（{HrEconomics.ChannelName(vac.channel)}周期约 {HrEconomics.RecruitLeadWeeks(vac.channel):F0} 周，约 {days:F0} 天后到位）。";
                return false;
            }

            float shortage = ShortageFor(position, hr);
            float rate = HrEconomics.RecruitSuccessRate(vac.channel, shortage);
            int room = vac.needed - vac.hired;
            int cap = System.Math.Min(room, pool.Count);
            int hiredNow = Mathf.Clamp(Mathf.CeilToInt(pool.Count * rate), 1, cap);

            for (int i = 0; i < pool.Count; i++)
            {
                if (i < hiredNow) ConfirmHire(gs, pool[i], position);
                else ReleaseCandidate(gs, pool[i]);   // 落选者退出介绍所
                vac.inPool = Mathf.Max(0, vac.inPool - 1);
            }

            vac.hired += hiredNow;
            hr.headcountAdditive += hiredNow;   // 增量口径：基数由在役机队派生（GameState.TotalHeadcount）
            hr.vacancyRate = CalcVacancyRate(hr);
            message = $"「{position}」到岗 {hiredNow} 人（成功率 {rate * 100:F0}% · 本批候选 {pool.Count} 人）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>职业介绍所：录用某位渠道候选人（CEO 确认人选，由人力办理入职）。</summary>
        public bool HireCandidate(string personId, out string message)
            => HireCandidate(GameState.Instance, personId, out message);

        /// <summary>
        /// 录用闸门的判定结果（纯函数产出）。把「能不能录」与「录了之后做什么」拆开，
        /// 是为了让自检能直接断言判定表 —— 否则整条链路都锁在 <see cref="GameState.Instance"/>
        /// 单例上，编辑器自检里跑不动（AddComponent 不触发 Awake，单例取不到）。
        /// </summary>
        public readonly struct HireVerdict
        {
            public readonly bool ok;
            /// <summary>拒绝原因（ok = false 时非空）。</summary>
            public readonly string reason;
            /// <summary>true = 走招聘案账本；false = 走编制缺口（无案直录）。</summary>
            public readonly bool viaVacancy;
            /// <summary>落座部门（编制表可能修正候选人自带的部门）。</summary>
            public readonly string dept;
            /// <summary>落座职级。</summary>
            public readonly string rank;
            /// <summary>可占名额：有招聘案时为案内剩余名额，无案时为编制余量。</summary>
            public readonly int roomLeft;

            public HireVerdict(bool ok, string reason, bool viaVacancy, string dept, string rank, int roomLeft)
            {
                this.ok = ok;
                this.reason = reason;
                this.viaVacancy = viaVacancy;
                this.dept = dept;
                this.rank = rank;
                this.roomLeft = roomLeft;
            }
        }

        /// <summary>
        /// 录用闸门（CEO 裁定 2026-10-11：不再要求「先呈请、后有案」）。
        ///
        /// 两级判定：
        ///   ① 有招聘案 → 以案为准（呈请链路仍是权威账本）。案的 needed 在发起时已被
        ///      「编制 − 在职 − 在途」裁剪过，故此处**不再**叠一层编制缺口判定 ——
        ///      否则玩家刚呈请 12 名飞行员（在途 12 已占满编制余量），落池的候选反而录不进。
        ///   ② 没有招聘案 → 退回编制缺口判定（<see cref="OpeningFor"/>）。这是投机候选池
        ///      （TalentMarket）能落地的关键：那些人不属于任何一次呈请，若沿用旧闸门，
        ///      玩家会在名单里点到一个永远失败的「录用」。
        /// </summary>
        public static HireVerdict EvaluateHire(GameState gs, PersonnelData p)
        {
            if (gs == null || p == null) return new HireVerdict(false, "候选人不存在。", false, null, null, 0);
            string tag = HrEconomics.NormalizeSource(p.source);

            var vac = VacancyFor(gs, p.jobTitle, tag) ?? VacancyForPosition(gs, p.jobTitle);
            string seatDept = p.department, seatRank = p.rank;
            if (vac != null)
            {
                int room = vac.needed - vac.hired;
                if (room <= 0)
                    return new HireVerdict(false, $"「{p.jobTitle}」的招聘案已招满。", true, seatDept, seatRank, 0);
                return new HireVerdict(true, null, true, seatDept, seatRank, room);
            }

            if (!OpeningFor(gs, p, out seatDept, out seatRank, out int roomLeft) || roomLeft <= 0)
                return new HireVerdict(false,
                    $"「{p.jobTitle}」编制已满，无法再录用（可在〔人力〕页复查编制，或先让在职者调岗/离职）。",
                    false, seatDept, seatRank, 0);
            return new HireVerdict(true, null, false, seatDept, seatRank, roomLeft);
        }

        /// <summary>
        /// 对指定 <see cref="GameState"/> 录用候选人。
        /// 与单例版分开是为了可测：自检要能对着自己造的那份状态跑完整条入职链路。
        /// </summary>
        public static bool HireCandidate(GameState gs, string personId, out string message)
        {
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null) { message = "候选人不存在。"; return false; }
            if (!p.onMarket) { message = "该候选人已不在介绍所。"; return false; }
            string tag = HrEconomics.NormalizeSource(p.source);
            if (tag == HrEconomics.SourceMarket)
            {
                message = "该候选人不属于招聘渠道，请走总监任命流程。";
                return false;
            }

            var verdict = EvaluateHire(gs, p);
            if (!verdict.ok) { message = verdict.reason; return false; }

            bool crew = IsCrewPost(p.jobTitle);
            string seatDept = verdict.dept, seatRank = crew ? p.rank : verdict.rank;
            if (!string.IsNullOrEmpty(seatDept)) p.department = seatDept;
            if (!string.IsNullOrEmpty(seatRank)) p.rank = seatRank;

            if (verdict.viaVacancy)
            {
                var vac = VacancyFor(gs, p.jobTitle, tag) ?? VacancyForPosition(gs, p.jobTitle);
                if (vac != null)
                {
                    vac.hired++;
                    vac.inPool = Mathf.Max(0, vac.inPool - 1);
                }
            }
            ConfirmHire(gs, p, p.jobTitle, operational: crew);

            // 副总监席的"在任"记在 DepartmentManager.deputyId 上，不数 staffPool；
            // 不代办这一步，玩家会看到一个「履新了但副总监席位仍然空着」的部门。
            if (!crew && p.rank == "副手" && DepartmentManager.Instance != null && !string.IsNullOrEmpty(p.department))
            {
                var d = DepartmentManager.Instance.GetDept(p.department);
                if (d != null && string.IsNullOrEmpty(d.deputyId))
                    DepartmentManager.Instance.AppointDeputy(p.department, p.id);
            }

            hr.headcountAdditive++;
            hr.vacancyRate = CalcVacancyRate(hr);
            message = $"已录用 {p.fullName}（{p.jobTitle} · {HrEconomics.SourceName(tag)}），由人力办理入职。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>职业介绍所：退回某位渠道候选人（离开介绍所，不再显示）。</summary>
        public bool RejectCandidate(string personId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            EnsureState(gs);
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null) { message = "候选人不存在。"; return false; }

            var vac = VacancyFor(gs, p.jobTitle, HrEconomics.NormalizeSource(p.source))
                ?? VacancyForPosition(gs, p.jobTitle);
            if (vac != null) vac.inPool = Mathf.Max(0, vac.inPool - 1);
            string name = p.fullName;
            ReleaseCandidate(gs, p);
            message = $"已退回候选人 {name}。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 内部转岗：把在编员工调到目标部门/职位。
        /// 代价设计（CEO 已裁定）：原部门编制缺口由 CountFilled 自动体现并由人力重新招聘 +
        /// 交接期内战力打折；因此不收取外部招聘费（RecruitCostPerHire 对内部转岗本就最低）。
        /// </summary>
        public bool TransferStaff(string personId, string toDept, string toJobTitle, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            EnsureState(gs);
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null) { message = "员工不存在。"; return false; }
            if (p.onMarket) { message = "只有本公司在编员工可内部转岗。"; return false; }
            if (p.rank == "负责人") { message = "总监不适用内部转岗，请走任命/解聘流程。"; return false; }
            if (string.IsNullOrEmpty(toDept) || string.IsNullOrEmpty(toJobTitle)) { message = "暂无匹配的空缺岗位。"; return false; }

            var mgr = DepartmentManager.Instance;
            if (mgr != null && mgr.IsOccupied(p.id)) { message = "该员工当前占管理岗，请先免职再转岗。"; return false; }
            if (string.Equals(p.department, toDept, System.StringComparison.OrdinalIgnoreCase)
                && p.jobTitle == toJobTitle) { message = "该员工已在目标岗位。"; return false; }

            string fromDept = p.department;
            p.department = toDept;
            p.jobTitle = toJobTitle;
            p.isOperational = false;   // 转入编制岗位，重新纳入关键岗统计
            p.baseSalary *= 1.20f;   // 调岗薪资上调（表格「期望年薪」列显示增量）
            p.handoverUntilTicks = gs.SimUtc.AddDays(PersonnelData.HandoverDays).Ticks;

            // 原部门编制缺口：CountFilled 立即下降，人力会自动重建该岗招聘案
            StaffRecruitment.EnsureVacancies(gs);
            gs.InvalidateCrewCache();   // 离开运行序列，机组战力当天即更新

            message = $"已批准内部转岗：{p.fullName} 由{DepartmentCatalog.DisplayName(fromDept)}调入"
                + $"{DepartmentCatalog.DisplayName(toDept)}任「{toJobTitle}」，交接期 {PersonnelData.HandoverDays} 天（期间战力 ×{PersonnelData.HandoverEfficiencyScale:F2}），原岗位已由人力重新招聘。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>内部转岗候选：在编、非总监、未占管理岗，且存在同职级空缺的员工。</summary>
        public static List<PersonnelData> InternalCandidates(GameState gs)
        {
            var list = new List<PersonnelData>();
            if (gs?.staffPool == null) return list;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (CanTransfer(gs, p)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// 逐人版「可否内部转岗」，与 <see cref="InternalCandidates"/> 同源。
        /// 人员页（n114）用它决定是否显示「调岗」按钮 —— 名单推导与按钮闸门若各写一份，
        /// 必然出现「名单里有这个人，但他的档案页没有调岗按钮」这类断链。
        /// </summary>
        public static bool CanTransfer(GameState gs, PersonnelData p)
        {
            if (gs == null || p == null) return false;
            if (p.onMarket) return false;                     // 只有本公司在编员工可内部转岗
            if (p.isOperational) return false;                // 运行序列（飞行员/机务等）不参与管理岗转岗
            if (p.rank == "负责人") return false;              // 总监走任命/解聘流程
            var mgr = DepartmentManager.Instance;
            if (mgr != null && mgr.IsOccupied(p.id)) return false;
            return TryFindTransferTarget(gs, p, out _, out _);
        }

        /// <summary>为某位在编员工找目标空缺：同职级、编制未满、且不是他现在的岗位。</summary>
        public static bool TryFindTransferTarget(GameState gs, PersonnelData p, out string dept, out string jobTitle)
        {
            dept = null; jobTitle = null;
            if (gs == null || p == null) return false;
            var mgr = DepartmentManager.Instance;
            int bestGap = 0;
            for (int d = 0; d < StaffEstablishment.DisplayOrder.Length; d++)
            {
                string code = StaffEstablishment.DisplayOrder[d];
                var roles = StaffEstablishment.Roles(code);
                for (int r = 0; r < roles.Length; r++)
                {
                    var role = roles[r];
                    if (role.rank != p.rank) continue;
                    if (string.Equals(code, p.department, System.StringComparison.OrdinalIgnoreCase)
                        && role.jobTitle == p.jobTitle) continue;
                    int gap = role.quota - StaffEstablishment.CountFilled(code, role, gs, mgr);
                    if (gap > bestGap)
                    {
                        bestGap = gap;
                        dept = code;
                        jobTitle = role.jobTitle;
                    }
                }
            }
            return dept != null;
        }

        /// <summary>按来源标签批量取待决候选人。</summary>
        static List<PersonnelData> PoolCandidates(GameState gs, string tag, string position)
        {
            var list = new List<PersonnelData>();
            if (gs?.staffPool == null) return list;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !p.onMarket) continue;
                if (HrEconomics.NormalizeSource(p.source) != tag) continue;
                if (!string.IsNullOrEmpty(position) && p.jobTitle != position) continue;
                list.Add(p);
            }
            return list;
        }

        static Vacancy VacancyFor(GameState gs, string position, string tag)
        {
            var hr = EnsureState(gs);
            if (hr?.vacancies == null || string.IsNullOrEmpty(position)) return null;
            for (int i = 0; i < hr.vacancies.Count; i++)
            {
                var v = hr.vacancies[i];
                if (v == null || v.position != position) continue;
                if (HrEconomics.SourceTag(v.channel) != tag) continue;
                return v;
            }
            return null;
        }

        /// <summary>
        /// 仅按岗位匹配招聘案（忽略渠道）。用于换渠道后落池候选人的兜底，
        /// 使其仍能占用配额被录用，而不是卡在池里无法处置。
        /// 优先返回仍有余量的招聘案，其次返回任意同岗位招聘案。
        /// </summary>
        static Vacancy VacancyForPosition(GameState gs, string position)
        {
            var hr = EnsureState(gs);
            if (hr?.vacancies == null || string.IsNullOrEmpty(position)) return null;
            Vacancy fallback = null;
            for (int i = 0; i < hr.vacancies.Count; i++)
            {
                var v = hr.vacancies[i];
                if (v == null || v.position != position) continue;
                if (v.hired < v.needed) return v;
                if (fallback == null) fallback = v;
            }
            return fallback;
        }

        // ============================================================
        // 运行岗编制封顶（e4）
        //
        // 飞行员/机务/乘务的编制由在役机队规模推导，不再由玩家口径无限累加。
        // needed 上限 = 编制 − 在职 − 在途（已发起未到岗）。
        // ============================================================

        /// <summary>
        /// 岗位名是否运行岗（飞行员/机务/乘务）且**不是**编制席。
        /// 编制席名称含相同关键字（飞行运行主管含「飞行」、工程经理含「工程」），必须剔除，
        /// 否则会把编制席纳入运行编制封顶、并虚增在职数导致招募被误封。
        ///
        /// public：TalentMarket 也按这同一口径决定「运行岗该由哪个渠道供给」，两处口径不得各写一份。
        /// </summary>
        public static bool IsCrewPost(string jobTitle) =>
            HrEconomics.IsOperationalEstablishment(jobTitle)
            && !StaffEstablishment.IsEstablishmentJobTitle(jobTitle);

        /// <summary>该岗位的运行编制（0 表示非运行编制岗，不受封顶约束）。</summary>
        static int OperationalEstablishmentFor(GameState gs, string position)
        {
            if (gs == null || !IsCrewPost(position)) return 0;
            gs.GetFleetStaffing(out int active, out int wide, out int seats);
            return HrEconomics.OperationalEstablishment(position, active, wide, seats);
        }

        /// <summary>
        /// 该运行岗类别的在职人数（已到岗，不含介绍所候选人，不含编制席）。
        /// 判定口径与 GameState.IsPilot 完全一致（isOperational + 类别 + 非编制席职务名），
        /// 保证「编制封顶用的在职数」与「机组乘数用的人数」不会各算各的。
        /// </summary>
        static int OperationalOnDuty(GameState gs, string position)
        {
            if (gs?.staffPool == null) return 0;
            var cat = HrEconomics.OperationalCategory(position);
            if (cat == HrEconomics.OpsCategory.None) return 0;
            int c = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket || !p.isOperational) continue;
                if (HrEconomics.OperationalCategory(p.jobTitle) != cat) continue;
                if (StaffEstablishment.IsEstablishmentJobTitle(p.jobTitle)) continue;
                c++;
            }
            return c;
        }

        /// <summary>该岗位已发起、尚未到岗的在途配额（needed − hired 之和）。</summary>
        static int InFlightQuota(HrState hr, string position)
        {
            if (hr?.vacancies == null || string.IsNullOrEmpty(position)) return 0;
            int c = 0;
            for (int i = 0; i < hr.vacancies.Count; i++)
            {
                var v = hr.vacancies[i];
                if (v == null || v.position != position) continue;
                int outstanding = v.needed - v.hired;
                if (outstanding > 0) c += outstanding;
            }
            return c;
        }

        /// <summary>
        /// 运行岗招募余量 = 编制 − 在职 − 在途（已发起未到岗）。
        ///
        /// 返回 false 表示该岗位**不受编制封顶约束**（非运行岗，或机队为空使编制为 0），
        /// 此时 roomLeft 为 0 —— 调用方不得据此裁剪 needed（否则会把非运行岗招募误砍成 0）。
        /// 返回 true 时 roomLeft 可能为负（历史超额雇佣），语义是「还能招几个」，&lt;=0 即已配满。
        /// </summary>
        public static bool RecruitRoomLeft(GameState gs, string position,
            out int establishment, out int onDuty, out int inFlight, out int roomLeft)
        {
            establishment = 0; onDuty = 0; inFlight = 0; roomLeft = 0;
            if (gs == null || string.IsNullOrEmpty(position)) return false;
            establishment = OperationalEstablishmentFor(gs, position);
            if (establishment <= 0) return false;
            onDuty = OperationalOnDuty(gs, position);
            inFlight = InFlightQuota(EnsureState(gs), position);
            roomLeft = establishment - onDuty - inFlight;
            return true;
        }

        static void ConfirmHire(GameState gs, PersonnelData p, string position)
            => ConfirmHire(gs, p, position, operational: true);

        /// <summary>
        /// 办理入职。<paramref name="operational"/> = 是否运行/职能**批量岗**（飞行员/乘务/机务等渠道批量岗）。
        ///
        /// 该位决定这个人走哪条薪酬与战力口径，必须与岗位一致：
        ///   true  → 不占编制表名额、薪酬进 OperationalPayrollMonthlyWan、不参与 DeptStaffLift（运行序列）；
        ///   false → 编制席（会计主管/收益管理主管…）：占编制表名额、进存量人力聚合、参与关键岗效率。
        /// 此前只有一个 true 的分支，因为录用入口只有运行岗；编制席经 TalentMarket 落池后，
        /// 若仍标 true，玩家会雇到一个「在编却不算在编」的主管（关键岗统计与部门效率都看不到他）。
        /// </summary>
        static void ConfirmHire(GameState gs, PersonnelData p, string position, bool operational)
        {
            if (p == null) return;
            p.onMarket = false;
            p.isOperational = operational;
            if (string.IsNullOrEmpty(p.jobTitle)) p.jobTitle = position;
            // 入职当天即发基础资质：玩家可能马上点开档案，证书不该等到次日日切才出现。
            if (gs != null) CertificationLogic.GrantStarterCerts(p, gs.SimUtc);
            gs?.InvalidateCrewCache();   // 新机组当天即影响航线利润，不必等日切
            gs?.AddDecisionLog($"人力：{p.fullName} 已入职（{position}）。");
        }

        /// <summary>
        /// 候选人可占的编制缺口（CEO 裁定 2026-10-11：录用不再无条件要求「先呈请招聘」）。
        ///
        /// 两类岗位缺口来源不同，必须分开算：
        ///   ① 运行岗（飞行员/乘务/机务）→ 编制由在役机队规模派生：编制 − 在职 − 在途；
        ///   ② 编制席（会计主管/收益管理主管…）→ StaffEstablishment 配额 − 已在位人数。
        /// 返回 false 表示岗位名对不上编制表（例如旧档里的自由文本职务），调用方不得据此放行。
        /// </summary>
        public static bool OpeningFor(GameState gs, PersonnelData p,
            out string dept, out string rank, out int roomLeft)
        {
            dept = p?.department;
            rank = p?.rank;
            roomLeft = 0;
            if (gs == null || p == null || string.IsNullOrEmpty(p.jobTitle)) return false;

            if (IsCrewPost(p.jobTitle))
            {
                if (!RecruitRoomLeft(gs, p.jobTitle, out _, out _, out _, out int crewRoom)) return false;
                rank = "专员";
                roomLeft = crewRoom;
                return true;
            }

            // 编制席：先认候选人自带的部门，再退化为全公司搜同名职务。
            // 渠道候选人的 department 由 CreateForRole 按编制表写入，正常情况下第一步即命中；
            // 退化分支是给「玩家用调岗把在编员工换过部门」之后留下的在池候选兜底。
            var mgr = DepartmentManager.Instance;
            string[] order = StaffEstablishment.DisplayOrder;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int d = 0; d < order.Length; d++)
                {
                    string code = order[d];
                    if (pass == 0 && !string.Equals(code, dept, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var roles = StaffEstablishment.Roles(code);
                    for (int r = 0; r < roles.Length; r++)
                    {
                        var role = roles[r];
                        if (role.jobTitle != p.jobTitle) continue;
                        int room = role.quota - StaffEstablishment.CountFilled(code, role, gs, mgr);
                        // 同名职务取余量最大的部门；全满则保持 0（调用方据此拒绝）
                        if (room > roomLeft) { roomLeft = room; dept = code; rank = role.rank; }
                    }
                }
            }
            return true;
        }

        /// <summary>落选/退回：直接移出人事池，避免在 staffPool 里留下永不消解的幽灵记录。</summary>
        static void ReleaseCandidate(GameState gs, PersonnelData p)
        {
            if (gs?.staffPool == null || p == null) return;
            gs.staffPool.Remove(p);
            gs.InvalidateCrewCache();
        }

        /// <summary>内部转岗渠道的到岗：自动挑选在编员工补员（受交接期与编制缺口约束）。</summary>
        static bool FillByInternalTransfer(GameState gs, HrState hr, Vacancy vac, out string message)
        {
            var elig = InternalCandidates(gs);
            if (elig.Count == 0) { message = "暂无可调岗员工（需在编、非总监、且存在同职级空缺）。"; return false; }

            int room = vac.needed - vac.hired;
            string dept = DeptForPosition(vac.position);
            // 目标是否为运行岗（飞行员/机务/乘务）：转入后必须标 isOperational，
            // 与 TransferStaff（运行岗 → 编制席，置 false）互为对称。
            // 不标会导致这个人既不计入编制在职数（招募封顶失效）、薪酬也不入账、
            // 战力也不进机组乘数 —— 内部转岗白拿编制。
            bool toCrewPost = IsCrewPost(vac.position);
            int moved = 0;
            for (int i = 0; i < elig.Count && moved < room; i++)
            {
                var p = elig[i];
                p.department = dept;
                p.jobTitle = vac.position;
                p.isOperational = toCrewPost;
                // 转入运行序列者，补齐对应岗位的基础资质（转出编制席位不影响既有证书）。
                if (toCrewPost) CertificationLogic.GrantStarterCerts(p, gs.SimUtc);
                // 标记来源为「内部转岗」：这些人是**从既有编制席搬过来的**，
                // 其薪酬本来就已包含在 HrState 的存量人力成本聚合口径内（GameState.LaborCostAnnualWan）。
                // 若不打这个标记，OperationalPayrollMonthlyWan 会把他们再算一遍，
                // 与**聚合口径**重叠。（注意：这与 RPMCalculator 的按小时可变机组成本无关——
                // 那是「飞才有」的另一只桶，与本 rollup 的「固定底薪」互补，不构成双计。）
                p.source = HrEconomics.SourceInternal;
                p.baseSalary *= 1.20f;
                p.handoverUntilTicks = gs.SimUtc.AddDays(PersonnelData.HandoverDays).Ticks;
                moved++;
            }
            if (moved == 0) { message = "暂无可调岗员工。"; return false; }

            vac.hired += moved;
            hr.vacancyRate = CalcVacancyRate(hr);
            StaffRecruitment.EnsureVacancies(gs);
            gs.InvalidateCrewCache();
            message = $"「{vac.position}」内部转岗到岗 {moved} 人（交接期 {PersonnelData.HandoverDays} 天，原岗位已重新招聘）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>招聘管道单行状态：空缺口 / 已招满 / 内部转岗可即时办理 / 候选人到位倒计时。在职业介绍所 ④ 在途行展示。</summary>
        public static string VacancyStatusLine(Vacancy v, System.DateTime nowUtc)
        {
            if (v == null) return "";
            if (v.hired >= v.needed) return "已招满";
            if (v.channel == 3) return $"内部转岗可即时办理（在编可调 {InternalCandidates(GameState.Instance).Count} 人）";
            if (v.inPool > 0) return $"候选人已到位 {v.inPool} 人（在左侧名单逐人处置）";
            double days = System.Math.Max(0.0, (new System.DateTime(v.dueUtcTicks, System.DateTimeKind.Utc) - nowUtc).TotalDays);
            return $"候选人约 {days:F0} 天后到位（{HrEconomics.ChannelName(v.channel)}周期 {HrEconomics.RecruitLeadWeeks(v.channel):F0} 周）";
        }

        static float ShortageFor(string position, HrState hr)
        {
            var t = hr.talentIndex;
            if (position.Contains("飞行") || position.Contains("机长")) return t.pilotShortage;
            if (position.Contains("机务") || position.Contains("维修")) return t.engineerShortage;
            if (position.Contains("乘务")) return t.cabinShortage;
            return 30f;
        }

        static float CalcVacancyRate(HrState hr)
        {
            if (hr.vacancies == null || hr.vacancies.Count == 0) return 0f;
            float needed = 0f, hired = 0f;
            for (int i = 0; i < hr.vacancies.Count; i++)
            {
                needed += hr.vacancies[i].needed;
                hired += hr.vacancies[i].hired;
            }
            return needed <= 0f ? 0f : 1f - hired / needed;
        }

        #endregion

        #region 培训

        public bool AddTraining(string name, int enrolled, float costWan, out string message,
            string track = "people", bool chargeCash = true)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (string.IsNullOrEmpty(name)) { message = "培训项目名不能为空"; return false; }
            var hr = EnsureState(gs);
            track = string.IsNullOrEmpty(track) ? "people" : track;

            if (!HrEconomics.TrainingTrackUnlocked(track, hr.trainingCenterLevel))
            {
                message = $"「{track}」培训需培训中心达到 2 级（{HrEconomics.TrainingCenterName(2)}），"
                        + $"当前 {HrEconomics.TrainingCenterName(hr.trainingCenterLevel)}。";
                return false;
            }

            // 入册选人：此前这一步发生在结业那一刻，玩家既看不到名单也无法指定个人。
            string requiredDept = track == "maint" ? "MNT" : track == "safety" ? "SAF" : null;
            var members = TrainingGrowthLogic.SelectMembers(gs, enrolled, requiredDept);
            if (members.Count == 0)
            {
                message = track == "safety"
                    ? "安全部暂无潜力达标（≥0.60）的在职关键岗可入册。"
                    : $"暂无可入册人员：需在职、非运行序列、潜力 ≥{HrEconomics.TrainingEnrollMinPotential:F2}。";
                return false;
            }

            float billed = costWan * HrEconomics.TrainingCostFactor(hr.trainingCenterLevel);
            if (chargeCash)
            {
                if (gs.cash < billed) { message = $"现金不足（培训需 {billed:F0} 万）。"; return false; }
                gs.cash -= billed;
            }

            var prog = EnqueueProgram(gs, name, members.Count, billed, track);
            if (prog != null)
            {
                prog.enrolledTicks = gs.SimUtc.Ticks;
                for (int i = 0; i < members.Count; i++)
                    prog.memberIds.Add(members[i].id);
            }
            hr.trainingRoi = HrEconomics.TrainingRoi(hr.trainingBudgetWan, gs.TotalHeadcount);
            string shorten = members.Count < enrolled ? $"（报名 {enrolled} 人，实际达标入册 {members.Count} 人）" : "";
            message = $"已启动「{name}」培训（{members.Count} 人 · {billed:F0} 万 · 中心折扣 ×{HrEconomics.TrainingCostFactor(hr.trainingCenterLevel):F2}）{shorten}。" +
                      "推进季度后结业，在培名单见「培训 → 人员培训」。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 把个人加入某个在办培训项目（个人页「安排培训」）。已在册则不重复加入。
        /// </summary>
        public bool EnrollInProgram(string programName, string personId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            var prog = hr.trainings.Find(t => t != null && !t.graduated && t.name == programName);
            if (prog == null) { message = $"没有在办的「{programName}」项目。"; return false; }
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null) { message = "找不到该员工。"; return false; }
            if (p.onMarket) { message = "待聘人员不能入册。"; return false; }
            if (p.potential < HrEconomics.TrainingEnrollMinPotential)
            {
                message = $"{p.fullName} 潜力 {(p.potential * 100f):F0}%，低于入册门槛 " +
                          $"{(HrEconomics.TrainingEnrollMinPotential * 100f):F0}%。";
                return false;
            }
            if (prog.memberIds != null && prog.memberIds.Contains(p.id))
            { message = $"{p.fullName} 已在该项目名单中。"; return false; }
            if (prog.memberIds == null) prog.memberIds = new List<string>();
            prog.memberIds.Add(p.id);
            prog.enrolled = prog.memberIds.Count;
            message = $"已把 {p.fullName} 加入「{prog.name}」（当前 {prog.enrolled} 人）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 安排单人为考证项目（个人页「考取 XX 证」）。占用一个 TrainingProgram 通道，
        /// 好处是在培进度能与其他培训共用一套结算与展示。
        /// </summary>
        public bool StartCertification(string personId, string certId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            var def = CertificationCatalog.Find(certId);
            if (def == null) { message = "未知证照。"; return false; }
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null) { message = "找不到该员工。"; return false; }
            if (p.onMarket) { message = "待聘人员不能安排考证。"; return false; }
            if (hr.trainingCenterLevel < def.minCenterLevel)
            {
                message = $"「{def.name}」需培训中心达到 {def.minCenterLevel} 级（{HrEconomics.TrainingCenterName(def.minCenterLevel)}），当前 {HrEconomics.TrainingCenterName(hr.trainingCenterLevel)}。";
                return false;
            }
            if (!CertificationCatalog.AppliesTo(p, def))
            { message = $"{p.fullName}（{p.jobTitle}）不适用「{def.name}」。"; return false; }

            string existing = FindActiveCertProgram(hr, p.id, certId);
            if (existing != null) { message = $"{p.fullName} 已在「{existing}」考证中。"; return false; }

            float billed = def.costWan * HrEconomics.TrainingCostFactor(hr.trainingCenterLevel);
            if (gs.cash < billed) { message = $"现金不足（{def.name} 需 {billed:F0} 万）。"; return false; }
            gs.cash -= billed;

            var prog = EnqueueProgram(gs, def.name + " · 考前培训", 1, billed, "cert");
            if (prog == null) { message = "入册失败。"; return false; }
            prog.certId = def.id;
            prog.durationQuarters = CertificationLogic.DurationQuarters(def);
            prog.enrolledTicks = gs.SimUtc.Ticks;
            prog.memberIds.Add(p.id);

            hr.trainingRoi = HrEconomics.TrainingRoi(hr.trainingBudgetWan, gs.TotalHeadcount);
            message = $"已为 {p.fullName} 报名「{def.name}」（{billed:F0} 万 · 约 {prog.durationQuarters} 季结业发证）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        static string FindActiveCertProgram(HrState hr, string personId, string certId)
        {
            if (hr?.trainings == null) return null;
            for (int i = 0; i < hr.trainings.Count; i++)
            {
                var t = hr.trainings[i];
                if (t == null || t.graduated || t.track != "cert" || t.certId != certId) continue;
                if (t.memberIds != null && t.memberIds.Contains(personId)) return t.name;
            }
            return null;
        }

        /// <summary>
        /// 建项目并登记可见名单（供机务/安全等非 HR 页入口复用）。
        /// 注意：<paramref name="enrolled"/> 保持调用方传入值（机务的「在培人数」是 MNT 状态里的独立计数，
        /// 不因人名单而变），memberIds 只用于「人员培训」页展示与结业时定位到人。
        /// </summary>
        public static TrainingProgram EnqueueWithRoster(GameState gs, string name, int enrolled,
            float costWan, string track, string requiredDept = null)
        {
            var members = TrainingGrowthLogic.SelectMembers(gs, enrolled, requiredDept);
            var prog = EnqueueProgram(gs, name, enrolled, costWan, track);
            if (prog == null) return null;
            for (int i = 0; i < members.Count; i++)
                prog.memberIds.Add(members[i].id);
            return prog;
        }

        /// <summary>入册并返回记录（不再扣现金——扣款由调用方在算完折扣后处理）。</summary>
        public static TrainingProgram EnqueueProgram(GameState gs, string name, int enrolled, float costWan, string track)
        {
            var hr = EnsureState(gs);
            if (hr == null) return null;
            var prog = new TrainingProgram
            {
                name = name,
                enrolled = enrolled,
                completionRate = 0f,
                costWan = costWan,
                track = string.IsNullOrEmpty(track) ? "people" : track,
                graduated = false,
                memberIds = new List<string>(),
                enrolledTicks = gs != null ? gs.SimUtc.Ticks : 0L
            };
            hr.trainings.Add(prog);
            hr.trainingBudgetWan += costWan;
            return prog;
        }

        /// <summary>
        /// 申请升级培训中心：扣款并开工，工期见 HrEconomics.TrainingCenterBuildDays，
        /// 完工由 <see cref="TickDay"/> 写入 trainingCenterLevel。
        ///
        /// 两道闸门同时拦住此前的「免费无限升级」漏洞：目标等级必须正好是「当前等级 + 1」
        /// 且不得超过 3 级；越界时 TrainingCenterCostWan 返回 +∞，报价判定直接拒绝。
        /// </summary>
        public bool StartTrainingCenterUpgrade(int targetLevel, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);

            if (hr.trainingCenterBuildLevel > 0)
            {
                double days = System.Math.Max(0.0,
                    (new System.DateTime(hr.trainingCenterBuildDoneTicks, System.DateTimeKind.Utc) - gs.SimUtc).TotalDays);
                message = $"「{HrEconomics.TrainingCenterName(hr.trainingCenterBuildLevel)}」正在施工，约 {days:F0} 天后完工。";
                return false;
            }
            if (targetLevel <= hr.trainingCenterLevel) { message = "该等级已建成或更低。"; return false; }
            if (targetLevel > HrEconomics.TrainingCenterMaxLevel)
            {
                message = $"培训中心已达最高等级「{HrEconomics.TrainingCenterName(HrEconomics.TrainingCenterMaxLevel)}」。";
                return false;
            }
            if (targetLevel != hr.trainingCenterLevel + 1) { message = "只能逐级升级培训中心。"; return false; }

            float cost = HrEconomics.TrainingCenterCostWan(targetLevel);
            if (!HrEconomics.IsUpgradeCostPayable(cost)) { message = "该等级的报价无效，升级被拒绝。"; return false; }
            if (gs.cash < cost) { message = $"升级至「{HrEconomics.TrainingCenterName(targetLevel)}」需 {cost:F0} 万。"; return false; }

            gs.cash -= cost;
            int days2 = HrEconomics.TrainingCenterBuildDays(targetLevel);
            hr.trainingCenterBuildLevel = targetLevel;
            hr.trainingCenterBuildDoneTicks = gs.SimUtc.AddDays(days2).Ticks;

            message = $"已开工：{HrEconomics.TrainingCenterName(targetLevel)}（{cost:F0} 万 · 工期 {days2} 天 · " +
                      $"预计 {gs.SimUtc.AddDays(days2):yyyy-MM-dd} 启用）。施工期内沿用现有中心等级。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>培训中心升级完工检查（由 TickDay 调用，时钟口径与招聘管道一致）。</summary>
        public static string TickTrainingCenter(GameState gs, System.DateTime toUtc)
        {
            var hr = EnsureState(gs);
            if (hr == null || hr.trainingCenterBuildLevel <= 0) return "";
            if (toUtc.Ticks < hr.trainingCenterBuildDoneTicks) return "";
            int lv = hr.trainingCenterBuildLevel;
            hr.trainingCenterLevel = lv;
            hr.trainingCenterBuildLevel = 0;
            hr.trainingCenterBuildDoneTicks = 0L;
            // 建校是 CAPEX，不计入「年度培训预算」（后者是经营性支出，直接决定 ROI 与每客培训投入）。
            string msg = $"培训中心已建成「{HrEconomics.TrainingCenterName(lv)}」（后续培训成本 ×{HrEconomics.TrainingCostFactor(lv):F2}）。";
            gs.AddDecisionLog("人力：" + msg);
            gs.NotifyChanged();
            return msg;
        }

        /// <summary>中心等级名（转发 HrEconomics 的权威表，避免两处各写一份）。</summary>
        public static string CenterName(int level) => HrEconomics.TrainingCenterName(level);

        #endregion

        #region 工会谈判

        /// <summary>工会谈判：0让步 1强硬 2拖延。</summary>
        public bool NegotiateUnion(int choice, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            float cost = HrEconomics.UnionCostWan(choice, gs.TotalHeadcount);
            if (gs.cash < cost) { message = $"现金不足（谈判成本 {cost:F0} 万）。"; return false; }
            gs.cash -= cost;
            hr.union.satisfaction = Mathf.Clamp(hr.union.satisfaction + HrEconomics.UnionSatisfactionDelta(choice), 0f, 100f);
            hr.strikeRisk = Mathf.Clamp(hr.strikeRisk + HrEconomics.UnionStrikeRiskDelta(choice), 0f, 100f);
            // 见习期软保护（第 2 财年）：罢工倒计时缩短幅度减半 → 缓冲延长 50%
            int countdownShrink = (int)(-3f * ProgressionGate.StrikeCountdownShrinkScale(gs.year));
            hr.union.strikeCountdown += choice == 0 ? 6 : countdownShrink;
            if (choice == 0)
            {
                hr.union.cbaExpiresIn = 12;
                if (hr.union.strikeCountdown < 9) hr.union.strikeCountdown = 9;
            }
            else if (choice == 2)
                hr.union.cbaExpiresIn = Mathf.Max(hr.union.cbaExpiresIn, 3);
            string choiceName = choice switch { 0 => "让步", 1 => "强硬", _ => "拖延" };
            message = $"已采取「{choiceName}」策略（成本 {cost:F0} 万 · 工会满意度 {hr.union.satisfaction:F0} · 罢工风险 {hr.strikeRisk:F0}）。";
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 绩效薪酬

        /// <summary>kind: raise / bonus / exec</summary>
        public bool ApplyCompensation(string kind, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            kind = NormalizeKind(kind);
            float cost = CompCostWan(gs, kind);
            if (gs.cash < cost) { message = $"现金不足，本方案需 {cost:F0} 万。"; return false; }
            gs.cash -= cost;
            float sat0 = hr.employeeSatisfaction;
            if (kind == "raise")
            {
                // 调薪放大「每机费率」而非锁定总额：机队扩充后新飞机也按加薪后的费率计提
                hr.laborCostPerAircraftWan *= 1.035f;
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction + 7f, 0f, 100f);
                if (hr.union != null) hr.union.satisfaction = Mathf.Clamp(hr.union.satisfaction + 3f, 0f, 100f);
                hr.strikeRisk = Mathf.Clamp(hr.strikeRisk - 4f, 0f, 100f);
                message = $"已批准年度调薪（{cost:F0} 万）。每机人力费率现 {hr.laborCostPerAircraftWan:F0} 万/机/年，年度人力成本约 {gs.LaborCostAnnualWan:F0} 万，员工满意度 {sat0:F0}→{hr.employeeSatisfaction:F0}。";
            }
            else if (kind == "exec")
            {
                hr.laborCompAdditiveWan += 50f;
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction + 2f, 0f, 100f);
                message = $"已批准高管长期激励（{cost:F0} 万）。年度人力成本 +50 万（现 {gs.LaborCostAnnualWan:F0} 万），员工满意度 {hr.employeeSatisfaction:F0}。";
            }
            else
            {
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction + 8f, 0f, 100f);
                if (hr.union != null) hr.union.satisfaction = Mathf.Clamp(hr.union.satisfaction + 2f, 0f, 100f);
                hr.strikeRisk = Mathf.Clamp(hr.strikeRisk - 3f, 0f, 100f);
                message = $"已批准年度奖金池（{cost:F0} 万）。薪酬池不变，员工满意度 {sat0:F0}→{hr.employeeSatisfaction:F0}。";
            }
            hr.lastCompNote = message;
            gs.AddDecisionLog("人力：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool RejectCompensation(string kind, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var hr = EnsureState(gs);
            kind = NormalizeKind(kind);
            float sat0 = hr.employeeSatisfaction;
            float pool = gs.LaborCostAnnualWan;
            if (kind == "raise")
            {
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction - 6f, 0f, 100f);
                hr.strikeRisk = Mathf.Clamp(hr.strikeRisk + 5f, 0f, 100f);
                if (hr.union != null) hr.union.satisfaction = Mathf.Clamp(hr.union.satisfaction - 3f, 0f, 100f);
                message = "已驳回年度调薪。薪酬池维持原方案，员工满意度下降、罢工风险上升。";
            }
            else if (kind == "exec")
            {
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction - 1f, 0f, 100f);
                message = "已驳回高管长期激励。薪酬池不变。";
            }
            else
            {
                hr.employeeSatisfaction = Mathf.Clamp(hr.employeeSatisfaction - 4f, 0f, 100f);
                hr.strikeRisk = Mathf.Clamp(hr.strikeRisk + 3f, 0f, 100f);
                message = "已驳回奖金方案。薪酬池维持原方案，满意度下降。";
            }
            hr.lastCompNote = message + $"（满意度 {sat0:F0}→{hr.employeeSatisfaction:F0}，薪酬池 {pool:F0} 万）";
            gs.AddDecisionLog("人力：" + hr.lastCompNote);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 薪酬方案的公司层面现金成本（万美元）：以**派生**年度人力成本为基数
        /// （每机费率 × 在役机队 + 增量），保证方案随规模联动。
        /// </summary>
        public static float CompCostWan(GameState gs, string kind)
        {
            kind = NormalizeKind(kind);
            float labor = gs != null ? gs.LaborCostAnnualWan : HrEconomics.DefaultAnnualLaborCostWan;
            if (kind == "raise") return HrEconomics.RaisePackageWan(labor);
            if (kind == "exec") return HrEconomics.ExecLtiWan();
            return HrEconomics.BonusPoolWan(labor);
        }

        public static string CompTitle(string kind)
        {
            kind = NormalizeKind(kind);
            if (kind == "raise") return "年度调薪";
            if (kind == "exec") return "高管长期激励";
            return "年度奖金池";
        }

        public static string NormalizeKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "bonus";
            if (kind == "raise" || kind == "exec" || kind == "bonus") return kind;
            return "bonus";
        }

        #endregion

        /// <summary>每季推进 CBA / 罢工倒计时，并结算在办培训结业。</summary>
        public static void TickQuarter(GameState gs)
        {
            var hr = EnsureState(gs);
            if (hr?.union != null)
            {
                hr.union.cbaExpiresIn = Mathf.Max(0, hr.union.cbaExpiresIn - 3);
                hr.union.strikeCountdown -= 3;
                if (hr.union.cbaExpiresIn <= 0)
                {
                    hr.strikeRisk = Mathf.Min(100f, hr.strikeRisk + 8f);
                    hr.union.satisfaction = Mathf.Max(0f, hr.union.satisfaction - 4f);
                }
                if (hr.union.strikeCountdown <= 0)
                    hr.strikeRisk = Mathf.Min(100f, hr.strikeRisk + 12f);
            }

            string growth = TrainingGrowthLogic.AdvanceAll(gs);
            if (!string.IsNullOrEmpty(growth))
            {
                gs.AddDecisionLog("培训：" + growth.Replace("\n", "；"));
                PostTrainingReport(gs, growth);
                if (DepartmentManager.Instance != null)
                    DepartmentManager.Instance.RecalcAllEfficiencies();
            }
        }

        static void PostTrainingReport(GameState gs, string body)
        {
            if (gs == null) return;
            InboxManager.Add(gs, new InboxMail
            {
                id = "fx_train_" + gs.year + "q" + gs.quarter + "_" + (gs.inbox != null ? gs.inbox.Count : 0),
                sender = "人力资源部",
                role = "培训结业",
                glyph = "培",
                kind = "report",
                subject = "一期培训已结业，关键岗成长已回写档案",
                body = body + "\n\n请到人员页查看部门效率，或打开员工档案查看潜力/效率变化。",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1,
                read = false
            });
        }
    }
}
