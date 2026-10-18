using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 非总监岗位由人力 + 职能部门招聘，耗时接近真实周期，面试通过后报 CEO 审批。
    /// 总监岗位不走本管线，由玩家直接录用/解聘。
    /// </summary>
    public static class StaffRecruitment
    {
        public const string HireAction = "hire_offer";
        const int MaxActive = 3;

        public static bool IsDirectorRank(string rank) => rank == "负责人";

        public static bool CanCeoHire(PersonnelData p) => p != null && IsDirectorRank(p.rank);

        public static bool CanCeoFire(PersonnelData p) => p != null && IsDirectorRank(p.rank);

        /// <summary>
        /// 职业介绍所「条件折叠」的唯一判定口径：某个来源视图是否接纳该候选人。
        ///
        /// 待聘总监视图（<paramref name="wantTag"/> == market）只收总监（负责人）级——这些人由 CEO 直接任命；
        /// 渠道视图收该渠道落池的任何职级。两者必须区分：渠道候选人由 HrManager.RankForPosition
        /// 一律产出为「专员」，若两种视图共用同一个总监闸门，校招/猎头/海外三个页签将永远为空，
        /// 「渠道实体化」整条链路在 UI 上等于没接上（2026-10-08 修复）。
        ///
        /// 只判来源与职级；「是否已被占岗（DepartmentManager.IsOccupied）」由 UI 补齐——
        /// 那是运行期字典，不属于纯逻辑。
        /// </summary>
        public static bool AcceptedInSourceView(PersonnelData p, string wantTag)
        {
            if (p == null || !p.onMarket) return false;
            string tag = HrEconomics.NormalizeSource(p.source);
            if (tag != wantTag) return false;
            return tag != HrEconomics.SourceMarket || IsDirectorRank(p.rank);
        }

        /// <summary>
        /// 「全部来源」视图是否接纳该候选人 —— 即原先五个来源视图的**并集**。
        ///
        /// 并集必须保留不对称：人才市场视图只收负责人级（那些人是总监席的候选人，由 CEO 直接任命），
        /// 四个渠道视图则收该渠道落池的任何职级。若并集写成「所有 onMarket 的人」，
        /// 人才市场里的非负责人会一并挤进名单，「一次只处理一类候选人」的 CEO 注意力保护即失效。
        /// 内部转岗不在本判定内：那是在编员工的实时推导（`HrManager.InternalCandidates`），
        /// 与人才池（`onMarket`）互斥，由 UI 单独并入。
        /// </summary>
        public static bool AcceptedInAnySourceView(PersonnelData p)
        {
            if (p == null || !p.onMarket) return false;
            return HrEconomics.NormalizeSource(p.source) != HrEconomics.SourceMarket || IsDirectorRank(p.rank);
        }

        public static void Reset(GameState gs)
        {
            if (gs == null) return;
            gs.staffRecruits = new List<StaffRecruitCase>();
        }

        public static void EnsureVacancies(GameState gs)
        {
            if (gs == null) return;
            if (gs.staffRecruits == null) gs.staffRecruits = new List<StaffRecruitCase>();
            var mgr = DepartmentManager.Instance;
            var now = gs.SimUtc;
            int stagger = 0;
            var planned = ListNeededSlots(gs, mgr);
            for (int i = 0; i < planned.Count; i++)
            {
                var slot = planned[i];
                if (HasOpenCase(gs, slot.dept, slot.jobTitle)) continue;
                var c = NewCase(slot.dept, slot.rank, slot.jobTitle, now.AddDays(stagger * 3));
                gs.staffRecruits.Add(c);
                stagger++;
            }
        }

        public static void Tick(GameState gs, DateTime toUtc)
        {
            if (gs == null || !gs.staffBootstrapped) return;
            if (gs.staffRecruits == null) gs.staffRecruits = new List<StaffRecruitCase>();
            EnsureVacancies(gs);

            int active = CountActive(gs);
            for (int i = 0; i < gs.staffRecruits.Count; i++)
            {
                var c = gs.staffRecruits[i];
                if (c == null) continue;
                if (c.stage == "done" || c.stage == "cancelled") continue;

                if (c.stage == "queued")
                {
                    if (toUtc.Ticks < c.startUtcTicks) continue;
                    if (active >= MaxActive) continue;
                    Enter(c, "sourcing", toUtc);
                    active++;
                    gs.AddDecisionLog($"招聘：{DepartmentCatalog.DisplayName(c.dept)}开始寻访「{Display(c)}」。");
                    continue;
                }

                if (c.stage == "sourcing" && DaysSince(c.stageUtcTicks, toUtc) >= c.sourceDays)
                {
                    AttachCandidate(gs, c);
                    Enter(c, "interview", toUtc);
                    gs.AddDecisionLog($"招聘：{DepartmentCatalog.DisplayName(c.dept)}「{Display(c)}」进入面试。");
                    continue;
                }

                if (c.stage == "interview" && DaysSince(c.stageUtcTicks, toUtc) >= c.interviewDays)
                {
                    AttachCandidate(gs, c);
                    Enter(c, "pending", toUtc);
                    SendOfferMail(gs, c);
                    continue;
                }

                if (c.stage == "onboard" && DaysSince(c.stageUtcTicks, toUtc) >= c.onboardDays)
                    CompleteHire(gs, c);
            }
        }

        public static string StageLabel(StaffRecruitCase c)
        {
            if (c == null) return "";
            return c.stage switch
            {
                "queued" => "待启动",
                "sourcing" => "寻访简历",
                "interview" => "面试评估",
                "pending" => "待您审批",
                "onboard" => "入职通知期",
                _ => ""
            };
        }

        public static StaffRecruitCase FindOpen(GameState gs, string dept, string jobTitle)
        {
            if (gs?.staffRecruits == null) return null;
            for (int i = 0; i < gs.staffRecruits.Count; i++)
            {
                var c = gs.staffRecruits[i];
                if (c == null) continue;
                if (c.stage == "done" || c.stage == "cancelled") continue;
                if (!string.Equals(c.dept, dept, StringComparison.OrdinalIgnoreCase)) continue;
                if (c.jobTitle != jobTitle) continue;
                return c;
            }
            return null;
        }

        public static bool ApproveOffer(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var c = FindByMail(gs, mail);
            if (c == null)
            {
                message = "招聘案已失效。";
                InboxManager.Mark(gs, mail.id, "approved");
                return false;
            }
            AttachCandidate(gs, c);
            Enter(c, "onboard", gs.SimUtc);
            InboxManager.Mark(gs, mail.id, "approved");
            var p = PersonnelGenerator.Find(gs, c.personId);
            string name = p != null ? p.fullName : "候选人";
            message = $"已批准录用 {name} 为{DepartmentCatalog.DisplayName(c.dept)}「{Display(c)}」。通知期 {c.onboardDays} 天后到岗。";
            gs.AddDecisionLog("人事审批：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static bool RejectOffer(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var c = FindByMail(gs, mail);
            InboxManager.Mark(gs, mail.id, "rejected");
            if (c == null)
            {
                message = "已退回该录用请示。";
                return true;
            }

            // 退回 = 这个人被换掉，必须**同时**把他从 staffPool 摘掉。
            // 只清 personId 会让候选人永远留在池里：他 source=市场、职级非负责人，
            // 介绍所名单看不见他（被 AcceptedInAnySourceView 挡住），因此玩家永远处置不掉，
            // 而 AttachCandidate 下一轮又会新建一个人 —— 每退一次池子与实际人力需求就脱钩一分。
            string dropped = c.personId;
            if (!string.IsNullOrEmpty(dropped) && gs.staffPool != null)
            {
                var gone = PersonnelGenerator.Find(gs, dropped);
                if (gone != null)
                {
                    gs.staffPool.Remove(gone);
                    gs.InvalidateCrewCache();
                }
            }

            c.personId = "";
            c.mailId = "";
            Enter(c, "sourcing", gs.SimUtc);
            message = $"已退回「{Display(c)}」录用请示。人力将重新寻访候选人。";
            gs.AddDecisionLog("人事审批：" + message);
            gs.NotifyChanged();
            return true;
        }

        static void CompleteHire(GameState gs, StaffRecruitCase c)
        {
            var mgr = DepartmentManager.Instance;
            var p = PersonnelGenerator.Find(gs, c.personId);
            if (p == null) AttachCandidate(gs, c);
            p = PersonnelGenerator.Find(gs, c.personId);
            if (p == null)
            {
                c.stage = "cancelled";
                return;
            }

            p.onMarket = false;
            p.department = c.dept;
            p.rank = c.rank;
            p.jobTitle = c.jobTitle;

            if (c.rank == "副手" && mgr != null)
            {
                var d = mgr.GetDept(c.dept);
                if (d != null && string.IsNullOrEmpty(d.deputyId))
                    mgr.AppointDeputy(c.dept, p.id);
            }

            c.stage = "done";
            gs.AddDecisionLog($"人事：{p.fullName} 已到岗，任{DepartmentCatalog.DisplayName(c.dept)}「{Display(c)}」。");
            gs.NotifyChanged();
            mgr?.NotifyRosterChanged();
        }

        static void AttachCandidate(GameState gs, StaffRecruitCase c)
        {
            if (!string.IsNullOrEmpty(c.personId) && PersonnelGenerator.Find(gs, c.personId) != null)
                return;
            var p = PersonnelGenerator.CreateForRole(gs, c.dept, c.rank, c.jobTitle);
            c.personId = p != null ? p.id : "";
        }

        static void SendOfferMail(GameState gs, StaffRecruitCase c)
        {
            var p = PersonnelGenerator.Find(gs, c.personId);
            if (p == null) return;
            string mailId = "hire_" + c.id;
            c.mailId = mailId;
            string title = StaffEstablishment.DisplayTitle(c.jobTitle, c.rank);
            string hr = DepartmentCatalog.DisplayName("HR");
            string deptName = DepartmentCatalog.DisplayName(c.dept);
            // 人力为本部招人时，联署会变成「人力资源部 / 人力资源部」，故去重
            string sender = deptName == hr ? hr : hr + " / " + deptName;
            string scouted = deptName == hr ? hr : deptName + "与" + hr;
            InboxManager.Schedule(gs, new InboxMail
            {
                id = mailId,
                sender = sender,
                role = "招聘请示",
                glyph = "人",
                subject = $"请审批录用：{p.fullName} · {title}",
                body =
                    $"{scouted}完成寻访与面试，提请您批准录用：\n\n" +
                    $"姓名　{p.fullName}\n国籍　{p.nationality}\n岗位　{title}\n年薪　{p.baseSalary:F0} 万\n" +
                    $"特质　{(string.IsNullOrEmpty(p.traitsCsv) ? "—" : p.traitsCsv)}\n\n" +
                    $"本轮已完成简历筛选与两轮面试。批准后进入 {c.onboardDays} 天入职通知期，到期到岗。\n" +
                    "拒绝则由人力重新寻访，不占用总监编制。",
                status = "pending",
                kind = "report",
                action = HireAction,
                origin = c.id,
                dest = p.id,
                tag = "提案",
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.AddDecisionLog($"招聘：{DepartmentCatalog.DisplayName(c.dept)}呈请录用 {p.fullName}（{title}）。");
            gs.NotifyChanged();
        }

        static StaffRecruitCase FindByMail(GameState gs, InboxMail mail)
        {
            if (gs?.staffRecruits == null || mail == null) return null;
            for (int i = 0; i < gs.staffRecruits.Count; i++)
            {
                var c = gs.staffRecruits[i];
                if (c == null) continue;
                if (c.mailId == mail.id || c.id == mail.origin) return c;
            }
            return null;
        }

        static bool HasOpenCase(GameState gs, string dept, string jobTitle)
        {
            return FindOpen(gs, dept, jobTitle) != null;
        }

        static int CountActive(GameState gs)
        {
            int n = 0;
            for (int i = 0; i < gs.staffRecruits.Count; i++)
            {
                var s = gs.staffRecruits[i]?.stage;
                if (s == "sourcing" || s == "interview" || s == "pending" || s == "onboard") n++;
            }
            return n;
        }

        static void Enter(StaffRecruitCase c, string stage, DateTime utc)
        {
            c.stage = stage;
            c.stageUtcTicks = utc.Ticks;
        }

        static double DaysSince(long ticks, DateTime toUtc)
        {
            if (ticks <= 0) return 0;
            return (toUtc - new DateTime(ticks, DateTimeKind.Utc)).TotalDays;
        }

        static string Display(StaffRecruitCase c)
            => StaffEstablishment.DisplayTitle(c.jobTitle, c.rank);

        static StaffRecruitCase NewCase(string dept, string rank, string jobTitle, DateTime start)
        {
            Durations(rank, out int src, out int iv, out int on);
            return new StaffRecruitCase
            {
                id = $"RC-{dept}-{jobTitle}-{start.Ticks % 100000}",
                dept = dept,
                rank = rank,
                jobTitle = jobTitle,
                stage = "queued",
                startUtcTicks = start.Ticks,
                stageUtcTicks = start.Ticks,
                sourceDays = src,
                interviewDays = iv,
                onboardDays = on
            };
        }

        static void Durations(string rank, out int source, out int interview, out int onboard)
        {
            if (rank == "副手") { source = 21; interview = 14; onboard = 7; }
            else if (rank == "主管") { source = 14; interview = 10; onboard = 5; }
            else { source = 10; interview = 7; onboard = 3; }
        }

        struct Slot
        {
            public string dept, rank, jobTitle;
            public int priority;
        }

        static List<Slot> ListNeededSlots(GameState gs, DepartmentManager mgr)
        {
            var list = new List<Slot>();
            for (int d = 0; d < StaffEstablishment.DisplayOrder.Length; d++)
            {
                string code = StaffEstablishment.DisplayOrder[d];
                var roles = StaffEstablishment.Roles(code);
                for (int r = 0; r < roles.Length; r++)
                {
                    var role = roles[r];
                    if (IsDirectorRank(role.rank)) continue;
                    int filled = StaffEstablishment.CountFilled(code, role, gs, mgr);
                    int open = role.quota - filled;
                    if (open <= 0) continue;
                    if (HasOpenCase(gs, code, role.jobTitle)) continue;
                    int pri = 50;
                    if (code == "HR" && role.jobTitle.Contains("招聘")) pri = 1;
                    else if (code == "HR") pri = 2;
                    else if (role.rank == "主管") pri = 10;
                    else if (role.rank == "专员") pri = 20;
                    else if (role.rank == "副手") pri = 30;
                    list.Add(new Slot { dept = code, rank = role.rank, jobTitle = role.jobTitle, priority = pri });
                }
            }
            list.Sort((a, b) => a.priority.CompareTo(b.priority));
            return list;
        }
    }
}
