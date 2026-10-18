using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>各部门管理团队编制（职位 / 编制人数）。入职人数由任命与 staffPool 统计。</summary>
    public static class StaffEstablishment
    {
        public readonly struct Role
        {
            public readonly string jobTitle;
            public readonly string rank;
            public readonly int quota;
            /// <summary>历史字段；关键岗现由 PersonnelGenerator 按 quota 填满。</summary>
            public readonly int openingFilled;

            public Role(string jobTitle, string rank, int quota, int openingFilled)
            {
                this.jobTitle = jobTitle;
                this.rank = rank;
                this.quota = quota;
                this.openingFilled = openingFilled;
            }
        }

        public static readonly string[] DisplayOrder =
        {
            "OPS", "MKT", "MNT", "SAF", "STG", "HR", "FIN"
        };

        /// <summary>部门正职的对外职务名（存档里仍是「负责人」键，仅展示层改名）。</summary>
        public const string HeadTitle = "总监";
        /// <summary>部门副职的对外职务名。</summary>
        public const string DeputyTitle = "副总监";

        /// <summary>把存档里的职务名规范成对外展示名：负责人 → 总监，副手 → 副总监。</summary>
        public static string DisplayTitle(string jobTitle, string rank)
        {
            if (rank == "负责人" || jobTitle == "部门负责人" || jobTitle == HeadTitle) return HeadTitle;
            if (rank == "副手" || jobTitle == "副手" || jobTitle == DeputyTitle) return DeputyTitle;
            return string.IsNullOrEmpty(jobTitle) ? (rank ?? "") : jobTitle;
        }

        /// <summary>部门代码 → 对外职务名（正职）。</summary>
        public static string HeadTitleFor(string dept)
        {
            var roles = Roles(dept);
            for (int i = 0; i < roles.Length; i++)
                if (roles[i].rank == "负责人") return DisplayTitle(roles[i].jobTitle, roles[i].rank);
            return HeadTitle;
        }

        public static Role[] Roles(string dept) => dept switch
        {
            "OPS" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("飞行运行主管", "主管", 2, 0),
                new Role("签派经理", "主管", 2, 0),
                new Role("客舱经理", "主管", 2, 0),
                new Role("地面服务主管", "专员", 3, 0),
            },
            "MKT" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("收益管理主管", "主管", 2, 0),
                new Role("销售经理", "主管", 2, 0),
                new Role("品牌公关", "专员", 2, 0),
                new Role("大客户经理", "专员", 2, 0),
            },
            "MNT" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("工程经理", "主管", 2, 0),
                new Role("航材主管", "主管", 2, 0),
                new Role("定检计划", "专员", 2, 0),
                new Role("质量控制", "专员", 2, 0),
            },
            "SAF" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("SMS经理", "主管", 1, 0),
                new Role("安全监察", "主管", 2, 0),
                new Role("应急主管", "专员", 1, 0),
            },
            "STG" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("网络规划", "主管", 2, 0),
                new Role("航权经理", "主管", 1, 0),
                new Role("投资分析", "专员", 2, 0),
            },
            "HR" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("招聘主管", "主管", 2, 0),
                new Role("薪酬绩效", "主管", 1, 0),
                new Role("培训主管", "专员", 2, 0),
            },
            "FIN" => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
                new Role("会计主管", "主管", 2, 0),
                new Role("司库", "主管", 1, 0),
                new Role("预算经理", "专员", 2, 0),
            },
            _ => new[]
            {
                new Role(HeadTitle, "负责人", 1, 0),
                new Role(DeputyTitle, "副手", 1, 0),
            }
        };

        public static int CountFilled(string dept, Role role, GameState gs, DepartmentManager mgr)
        {
            if (role.rank == "负责人")
                return mgr != null && mgr.IsAppointed(dept) ? 1 : 0;
            if (role.rank == "副手")
            {
                var d = mgr?.GetDept(dept);
                return d != null && !string.IsNullOrEmpty(d.deputyId) ? 1 : 0;
            }

            if (gs?.staffPool == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket) continue;
                if (!string.Equals(p.department, dept, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (mgr != null && mgr.IsOccupied(p.id) && p.rank == "副手") continue;
                if (!string.IsNullOrEmpty(p.jobTitle))
                {
                    if (p.jobTitle == role.jobTitle) n++;
                }
                else if (p.rank == role.rank)
                    n++;
            }

            return n;
        }

        /// <summary>总监、副总监、主管为开局必配关键岗；专员可空缺后招。</summary>
        public static bool IsKeyOpeningRole(Role role)
        {
            return role.rank == "负责人" || role.rank == "副手" || role.rank == "主管";
        }

        public static int TeamHeadcount(string dept, GameState gs, DepartmentManager mgr)
        {
            int n = 0;
            var roles = Roles(dept);
            for (int i = 0; i < roles.Length; i++)
                n += CountFilled(dept, roles[i], gs, mgr);
            return n;
        }

        public static int QuotaTotal(string dept)
        {
            int n = 0;
            var roles = Roles(dept);
            for (int i = 0; i < roles.Length; i++) n += roles[i].quota;
            return n;
        }

        /// <summary>在职人员（含负责人），按编制表顺序。</summary>
        public static System.Collections.Generic.List<PersonnelData> ListEmployed(
            string dept, GameState gs, DepartmentManager mgr)
        {
            var list = new System.Collections.Generic.List<PersonnelData>();
            var roles = Roles(dept);
            var used = new System.Collections.Generic.HashSet<string>();
            for (int r = 0; r < roles.Length; r++)
            {
                var role = roles[r];
                if (role.rank == "负责人")
                {
                    var head = mgr?.GetHead(dept);
                    if (head != null)
                    {
                        head.jobTitle = DisplayTitle(head.jobTitle, role.rank);
                        list.Add(head);
                        if (!string.IsNullOrEmpty(head.id)) used.Add(head.id);
                    }
                    continue;
                }
                if (role.rank == "副手")
                {
                    var dep = mgr?.GetDeputy(dept);
                    if (dep != null)
                    {
                        dep.jobTitle = DisplayTitle(dep.jobTitle, role.rank);
                        list.Add(dep);
                        used.Add(dep.id);
                    }
                    continue;
                }
                if (gs?.staffPool == null) continue;
                int added = 0;
                for (int i = 0; i < gs.staffPool.Count && added < role.quota + 8; i++)
                {
                    var p = gs.staffPool[i];
                    if (p == null || p.onMarket || used.Contains(p.id)) continue;
                    if (!string.Equals(p.department, dept, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.jobTitle != role.jobTitle) continue;
                    list.Add(p);
                    used.Add(p.id);
                    added++;
                }
            }
            return list;
        }

        public static bool IsEmployedId(string personId, GameState gs, DepartmentManager mgr)
        {
            if (string.IsNullOrEmpty(personId)) return false;
            if (mgr != null && mgr.IsOccupied(personId)) return true;
            for (int d = 0; d < DepartmentCatalog.Codes.Length; d++)
            {
                var people = ListEmployed(DepartmentCatalog.Codes[d], gs, mgr);
                for (int i = 0; i < people.Count; i++)
                    if (people[i] != null && people[i].id == personId) return true;
            }
            return false;
        }

        // 编制席职务名集合（缓存）：Roles(dept) 每次调用都新建数组，
        // 若在逐人遍历里反复调用会产生大量 GC，故一次性建表。
        static System.Collections.Generic.HashSet<string> _establishmentTitles;

        static System.Collections.Generic.HashSet<string> EstablishmentTitles()
        {
            if (_establishmentTitles != null) return _establishmentTitles;
            var set = new System.Collections.Generic.HashSet<string>();
            for (int d = 0; d < DepartmentCatalog.Codes.Length; d++)
            {
                var roles = Roles(DepartmentCatalog.Codes[d]);
                for (int i = 0; i < roles.Length; i++)
                {
                    if (!string.IsNullOrEmpty(roles[i].jobTitle))
                        set.Add(roles[i].jobTitle);
                    // 存档里的原始键（负责人 / 副手）也要认，否则旧档漏判
                    if (!string.IsNullOrEmpty(roles[i].rank))
                        set.Add(roles[i].rank);
                }
            }
            // 对外展示名
            set.Add(HeadTitle);
            set.Add(DeputyTitle);
            set.Add("部门负责人");
            _establishmentTitles = set;
            return set;
        }

        /// <summary>
        /// 是否编制席职务名（StaffEstablishment 的岗位，薪酬已在聚合口径内、
        /// 战力已由 DeptStaffLift 消费）。
        ///
        /// 必须用它把编制席从「运行岗」判定里剔除：编制席名称含同样的关键字
        /// （飞行运行主管含「飞行」、工程经理含「工程」），若不剔除会
        /// ① 虚增飞行员在职数导致招募被误封；② 把同一个人的战力重复计入两处。
        /// </summary>
        public static bool IsEstablishmentJobTitle(string jobTitle) =>
            !string.IsNullOrEmpty(jobTitle) && EstablishmentTitles().Contains(jobTitle);
    }
}
