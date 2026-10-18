using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 投机候选池（CEO 裁定 2026-10-11）：职业介绍所里的待聘名单不再只能靠「玩家先呈请 → 等渠道周期」
    /// 产生，人力还会**主动**按渠道把候选人释放进来，玩家在名单里直接挑人。
    ///
    /// 与 <see cref="StaffRecruitment"/> 的分工（两者产物都写进 gs.staffPool，靠 source 区分）：
    ///   · StaffRecruitment 补**编制缺口**：玩家是被动审批者（收件箱呈请）；
    ///   · TalentMarket 造**候选供给**：玩家是主动挑选者（介绍所名单）。
    ///
    /// 两者不重叠的护栏（不要拆）：本类只产出**渠道来源**（校招 / 猎头 / 海外），
    /// 而 StaffRecruitment 的候选人不带 source（= 人才市场）且非负责人，
    /// 被 <see cref="StaffRecruitment.AcceptedInAnySourceView"/> 挡住，不会挤进介绍所名单。
    /// 于是「同一个人既能从收件箱批准、又能从名单录用」这种双入口不会出现。
    ///
    /// 刻意不新增存档字段：
    ///   ① 池量上限由 staffPool 里同来源的 onMarket 人数派生 → 重复执行天然幂等；
    ///   ② 触发条件只看日历（每月 1 / 16 日）→ 读档重放不会漂；
    ///   ③ 于是老档不需要任何迁移：读档时播种一次，之后按日历补齐。
    /// 若将来要加「候选人在池里待久了会流失」，那必须给 PersonnelData 加时间戳字段，届时再评估。
    /// </summary>
    public static class TalentMarket
    {
        // ---- 各渠道在介绍所里的池量上限 ----
        //
        // 取值依据：要「够挑」但不能淹掉名单（一页只 8 行），也不能靠反复退回刷人。
        // 上限按**同来源 onMarket 总量**算，因此玩家自己呈请落池的人也占额度——
        // 呈请一批 12 个校招生之后，本类会安静补到满为止，不会叠成两倍。
        public const int CampusCap = 5;
        public const int HeadhuntCap = 3;
        public const int OverseasCap = 2;

        /// <summary>
        /// 三类运行岗（与 HrEconomics.IsOperationalEstablishment 同集合，由自检断言守卫不漂）。
        /// 它们是编制表之外的**批量岗**：只由机队规模定编制，因此排不进 StaffEstablishment.Roles。
        /// </summary>
        public static readonly string[] CrewPosts = { "飞行员", "乘务员", "机务工程师" };

        /// <summary>
        /// 释放节拍：每月 1 日与 16 日各结算一次。
        /// 选半月一拍而不是「每 30 天」：不看 epoch，读档任意时刻重放都落在同一天，无隐藏状态。
        /// </summary>
        public static bool IsReleaseDay(DateTime utc) => utc.Day == 1 || utc.Day == 16;

        /// <summary>某来源当前在介绍所里的池量（含玩家自己呈请落池的人）。</summary>
        public static int PoolCount(GameState gs, string tag)
        {
            if (gs?.staffPool == null) return 0;
            string want = HrEconomics.NormalizeSource(tag);
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !p.onMarket) continue;
                if (HrEconomics.NormalizeSource(p.source) == want) n++;
            }
            return n;
        }

        /// <summary>
        /// 开局 / 读档播种。只在三个渠道**全空**时补一次 ——
        /// 若按来源逐个补，玩家把某渠道的人全雇光后存档再读就会白得一批（读档刷人）。
        /// 要求「全空」把门槛抬到雇光 10 人，代价（一次重读）远高于收益，不给刷的动机。
        /// </summary>
        public static int EnsureSeeded(GameState gs)
        {
            if (gs == null) return 0;
            if (PoolCount(gs, HrEconomics.SourceCampus)
                + PoolCount(gs, HrEconomics.SourceHeadhunt)
                + PoolCount(gs, HrEconomics.SourceOverseas) > 0) return 0;
            return Fill(gs, HrEconomics.SourceCampus, CampusCap)
                 + Fill(gs, HrEconomics.SourceHeadhunt, HeadhuntCap)
                 + Fill(gs, HrEconomics.SourceOverseas, OverseasCap);
        }

        /// <summary>日级释放（由 <see cref="HrManager.TickDay"/> 调用，与招聘管道共用同一条日级时钟）。</summary>
        public static void TickDay(GameState gs, DateTime toUtc)
        {
            if (gs == null || !gs.staffBootstrapped) return;
            if (!IsReleaseDay(toUtc)) return;

            int made = Fill(gs, HrEconomics.SourceCampus, CampusCap)
                     + Fill(gs, HrEconomics.SourceHeadhunt, HeadhuntCap)
                     + Fill(gs, HrEconomics.SourceOverseas, OverseasCap);
            if (made <= 0) return;

            gs.AddDecisionLog($"人力：按渠道释放 {made} 名候选人（校招 / 猎头挖角 / 海外引进），可在职业介绍所查看。");
            gs.NotifyChanged();
        }

        /// <summary>把某来源补到上限；返回实际释放人数。</summary>
        static int Fill(GameState gs, string tag, int cap)
        {
            int have = PoolCount(gs, tag);
            if (have >= cap) return 0;
            var openings = OpeningsFor(tag);
            if (openings.Count == 0) return 0;

            int made = 0;
            for (int k = have; k < cap; k++)
            {
                var o = openings[UnityEngine.Random.Range(0, openings.Count)];
                var p = PersonnelGenerator.CreateForRole(gs, o.dept, o.rank, o.jobTitle, tag);
                if (p == null) break;
                made++;
            }
            return made;
        }

        struct Opening
        {
            public string dept;
            public string rank;
            public string jobTitle;
        }

        /// <summary>
        /// 渠道 → 可释放的岗位集合。**从编制表现算**而不是写死清单：
        /// 编制表日后增删岗位（如新增「机坪管理」）时，渠道供给会自动跟上，不必两处改。
        ///
        /// 渠道与职级的对应关系是有意设计的——它决定「来源」这一列到底有没有信息量：
        ///   校招   = 专员级：便宜、成型慢（SourceStatMultipliers 打 0.85 潜力 / 0.75 薪资 / 0.8 效率）
        ///   猎头   = 主管 / 副总监：即战力、贵、易被再挖（1.15 潜力 / 1.45 薪资 / 0.9 忠诚）
        ///   海外   = 运行岗 + 稀缺主管：国际航线准入要海外引进的机组（1.30 薪资、有适应期）
        /// </summary>
        static List<Opening> OpeningsFor(string tag)
        {
            var list = new List<Opening>();
            string want = HrEconomics.NormalizeSource(tag);
            bool campus = want == HrEconomics.SourceCampus;
            bool headhunt = want == HrEconomics.SourceHeadhunt;
            bool overseas = want == HrEconomics.SourceOverseas;
            if (!campus && !headhunt && !overseas) return list;

            for (int d = 0; d < StaffEstablishment.DisplayOrder.Length; d++)
            {
                string dept = StaffEstablishment.DisplayOrder[d];
                var roles = StaffEstablishment.Roles(dept);
                for (int r = 0; r < roles.Length; r++)
                {
                    var role = roles[r];
                    // 总监席永不由渠道产生：那是 CEO 直接任命的（StaffRecruitment.IsDirectorRank 同一口径）
                    if (StaffRecruitment.IsDirectorRank(role.rank)) continue;

                    bool take = campus ? role.rank == "专员"
                        : headhunt ? (role.rank == "主管" || role.rank == "副手")
                        : role.rank == "主管";   // overseas
                    if (!take) continue;
                    list.Add(new Opening { dept = dept, rank = role.rank, jobTitle = role.jobTitle });
                }
            }

            // 运行岗（飞行员 / 乘务员 / 机务工程师）：校招补新人、海外补持证者。
            // 猎头不碰运行岗——那些人是批量岗，挖角的经济性应由「呈请招聘」的渠道成本来体现。
            if (campus || overseas)
            {
                for (int i = 0; i < CrewPosts.Length; i++)
                {
                    list.Add(new Opening
                    {
                        dept = HrManager.DeptForPosition(CrewPosts[i]),
                        rank = "专员",
                        jobTitle = CrewPosts[i]
                    });
                }
            }

            return list;
        }
    }
}
