using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>按部门编制表生成关键岗（副手候选人 + 各职位在职）。</summary>
    public static class PersonnelGenerator
    {
        static readonly string[] WorldNations =
        {
            "韩国", "日本", "新加坡", "马来西亚", "美国", "英国", "法国", "德国",
            "泰国", "印度", "澳大利亚", "阿联酋"
        };

        static readonly string[] EnNames =
        {
            "詹姆斯·威尔逊", "艾米丽·克拉克", "迈克尔·布朗", "莎拉·米勒", "大卫·安德森",
            "奥利弗·史密斯", "艾玛·泰勒", "丹尼尔·哈里斯"
        };
        static readonly string[] FrNames =
        {
            "皮埃尔·杜邦", "卡米尔·洛朗", "朱利安·马丁", "索菲·贝尔纳"
        };
        static readonly string[] DeNames =
        {
            "卢卡斯·穆勒", "安娜·施密特", "费利克斯·韦伯", "劳拉·贝克尔"
        };
        static readonly string[] ThNames =
        {
            "索拉蓬·差亚蓬", "阿查拉·苏万", "纳塔蓬·西里", "玛妮·差伦"
        };
        static readonly string[] InNames =
        {
            "阿米尔·夏尔马", "普里娅·帕特尔", "拉胡尔·辛格", "安贾利·梅赫塔"
        };
        static readonly string[] ArNames =
        {
            "穆罕默德·哈桑", "法蒂玛·阿里", "哈立德·拉希德", "阿伊莎·优素福"
        };

        static readonly string[] CnSurnames =
        {
            "林", "陈", "赵", "周", "吴", "郑", "冯", "曹", "邓", "许", "沈", "韩", "唐", "蒋",
            "王", "李", "张", "刘", "黄", "徐", "孙", "马", "朱", "胡", "郭", "何", "高", "罗",
            "梁", "宋", "谢", "苏", "卢", "叶", "方", "白", "崔", "潘", "戴", "任", "姚", "廖"
        };
        static readonly string[] CnGiven =
        {
            "舟", "衡", "宁", "岚", "启", "越", "川", "默", "晴", "远", "峰", "明",
            "涛", "琳", "悦", "宸", "睿", "轩", "妍", "曦", "逸", "泽", "琪", "瑶",
            "铭", "毅", "昊", "晨", "彦", "哲", "楚", "澜",
            "晓萱", "志强", "雨桐", "嘉怡", "浩然", "思远", "婉清", "俊杰", "雅琴", "博文",
            "子墨", "欣怡", "梓涵", "宇轩", "语嫣", "佳琪", "天佑", "梦洁", "振宇", "静怡",
            "文博", "若曦", "嘉豪", "诗涵", "泽宇", "雨欣", "亦辰", "一诺", "思琪", "俊熙"
        };

        static readonly string[] KrSurnames = { "金", "朴", "李", "崔", "郑", "姜", "尹", "赵", "韩", "申", "权", "文", "吴", "具" };
        static readonly string[] KrGiven =
        {
            "敏俊", "智恩", "成浩", "秀彬", "在勋", "惠真", "东赫", "秀妍", "俊昊", "多恩",
            "相佑", "恩彩", "正焕", "瑞妍", "泰亨", "知允", "道贤", "书妍", "宇彬", "艺珍"
        };

        static readonly string[] JpSurnames = { "佐藤", "鈴木", "高橋", "田中", "伊藤", "渡辺", "山本", "中村", "小林", "加藤", "吉田", "松本", "井上", "木村" };
        static readonly string[] JpGiven = { "健", "結衣", "悠真", "翔太", "美咲", "大輝", "陽菜", "蓮", "咲良", "蒼", "颯太", "芽衣", "陸", "凛", "樹", "葵", "颯", "千尋" };

        static readonly string[] SgNames =
        {
            "陈伟明", "林慧玲", "黄志强", "李美芬", "张国辉",
            "阿末·拉赫曼", "法蒂玛·萨勒", "哈菲兹·优素福",
            "拉维·纳伊尔", "普里娅·拉詹"
        };

        static readonly string[] MyNames =
        {
            "阿兹兰·哈桑", "努尔·阿伊莎", "法伊兹·阿卜杜拉", "西蒂·玛丽安",
            "李金福", "张美莲", "陈顺发",
            "穆鲁根·拉詹", "拉克希米·纳达尔", "哈桑·宾·奥斯曼"
        };

        public static void EnsurePool(GameState gs)
        {
            if (gs == null) return;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            if (gs.staffPool.Count == 0)
                GenerateAll(gs);
            else
                EnsureJobTitles(gs);
            FillMissingKeyStaff(gs);
            RepairGeneratedIdentities(gs);
            SeedCatalogHeads(gs);
            SyncOccupiedFlags(gs);
        }

        /// <summary>P15：把 DataBase 负责人目录写入 staffPool（已有同 id 不覆盖，旧档可迁移）。</summary>
        public static void SeedCatalogHeads(GameState gs)
        {
            if (gs == null || DataBase.Instance == null) return;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            var all = DataBase.Instance.Executives;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (Find(gs, e.id) != null) continue;
                gs.staffPool.Add(PersonnelData.FromExecutive(e));
            }
        }

        /// <summary>任命占岗的人离开介绍所；空缺负责人回到待聘。</summary>
        public static void SyncOccupiedFlags(GameState gs)
        {
            var mgr = DepartmentManager.Instance;
            if (gs?.staffPool == null) return;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null) continue;
                bool occupied = mgr != null && mgr.IsOccupied(p.id);
                if (occupied) p.onMarket = false;
                else if (p.rank == "负责人") p.onMarket = true;
            }
        }

        static void GenerateAll(GameState gs)
        {
            FillMissingKeyStaff(gs);
        }

        /// <summary>补齐各部门总监以外的关键岗（副总监、主管）；专员保持空缺。</summary>
        public static void FillMissingKeyStaff(GameState gs)
        {
            if (gs == null) return;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            var used = new HashSet<string>();
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null) continue;
                n++;
                if (!string.IsNullOrEmpty(p.fullName)) used.Add(p.fullName);
            }
            var mgr = DepartmentManager.Instance;
            for (int d = 0; d < StaffEstablishment.DisplayOrder.Length; d++)
            {
                string code = StaffEstablishment.DisplayOrder[d];
                var roles = StaffEstablishment.Roles(code);
                for (int r = 0; r < roles.Length; r++)
                {
                    var role = roles[r];
                    if (role.rank == "负责人" || role.rank == "专员") continue;
                    if (!StaffEstablishment.IsKeyOpeningRole(role)) continue;
                    int have = CountEmployedForRole(gs, mgr, code, role);
                    int need = role.quota - have;
                    for (int k = 0; k < need; k++)
                    {
                        var p = Make(gs, code, role.rank, role.jobTitle, 32 + k + r, used, ref n, employed: true);
                        gs.staffPool.Add(p);
                    }
                }
            }
        }

        static int CountEmployedForRole(GameState gs, DepartmentManager mgr, string dept, StaffEstablishment.Role role)
        {
            if (role.rank == "副手")
            {
                int n = 0;
                if (gs?.staffPool == null) return 0;
                for (int i = 0; i < gs.staffPool.Count; i++)
                {
                    var p = gs.staffPool[i];
                    if (p == null || p.onMarket) continue;
                    if (!string.Equals(p.department, dept, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.rank != "副手") continue;
                    n++;
                }
                if (n == 0 && mgr != null)
                    return StaffEstablishment.CountFilled(dept, role, gs, mgr);
                return n;
            }
            return StaffEstablishment.CountFilled(dept, role, gs, mgr);
        }

        /// <summary>旧档：按总部基地国 80% 本国 / 20% 外籍重分配，并去掉数字姓名。</summary>
        public static void RepairGeneratedIdentities(GameState gs)
        {
            if (gs?.staffPool == null) return;
            string home = HomeCountry(gs);
            string[] foreign = ForeignCountries(home);
            var used = new HashSet<string>();
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || string.IsNullOrEmpty(p.fullName)) continue;
                if (p.id != null && p.id.StartsWith("STF-")) continue;
                used.Add(p.fullName);
            }
            int slot = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.id == null || !p.id.StartsWith("STF-")) continue;
                slot++;
                int idx = ParseStaffIndex(p.id, slot);
                string nation = NationForSlot(idx, home, foreign);
                bool nationChanged = p.nationality != nation;
                p.nationality = nation;
                if (!nationChanged && !NeedsNameRepair(p))
                {
                    used.Add(p.fullName);
                    continue;
                }
                p.fullName = ComposeName(nation, StableHash(p.id), used);
            }
        }

        public static string HomeCountry(GameState gs)
        {
            string hub = gs != null ? gs.hubIata : null;
            var city = CityEconomyCatalog.Find(hub);
            if (city != null && !string.IsNullOrEmpty(city.countryZh))
                return city.countryZh;
            if (AirportRegions.IsChina(hub)) return "中国";
            if (AirportRegions.IsSingapore(hub)) return "新加坡";
            if (AirportRegions.IsUs(hub)) return "美国";
            if (AirportRegions.IsUae(hub)) return "阿联酋";
            return "中国";
        }

        /// <summary>
        /// 是否外籍（口径：国籍与枢纽所在国不同）。职业介绍所「本国 / 外籍」筛选的唯一判定处——
        /// 不要就地写 `nationality == "中国"`，枢纽不在中国时那个硬编码会全判成外籍。
        /// </summary>
        public static bool IsForeign(PersonnelData p, GameState gs)
        {
            if (p == null) return false;
            string nat = p.nationality ?? "";
            if (nat.Length == 0) return false;
            return nat != HomeCountry(gs);
        }

        static string[] ForeignCountries(string home)
        {
            var list = new List<string>();
            for (int i = 0; i < WorldNations.Length; i++)
            {
                if (WorldNations[i] == home) continue;
                list.Add(WorldNations[i]);
            }
            if (home != "中国") list.Insert(0, "中国");
            return list.Count > 0 ? list.ToArray() : new[] { "美国", "英国", "日本" };
        }

        /// <summary>每 5 人里 4 人本国、1 人外籍（约 80/20）。</summary>
        static string NationForSlot(int index1, string home, string[] foreign)
        {
            if (index1 <= 0) index1 = 1;
            if (index1 % 5 != 0 || foreign == null || foreign.Length == 0)
                return home;
            return foreign[(index1 / 5 - 1) % foreign.Length];
        }

        static int ParseStaffIndex(string id, int fallback)
        {
            if (string.IsNullOrEmpty(id)) return fallback;
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash + 1 >= id.Length) return fallback;
            int n;
            return int.TryParse(id.Substring(dash + 1), out n) && n > 0 ? n : fallback;
        }

        static bool NeedsNameRepair(PersonnelData p)
        {
            if (p == null || string.IsNullOrEmpty(p.fullName)) return true;
            if (HasDigit(p.fullName)) return true;
            string nation = p.nationality ?? "";
            if (nation == "中国" || nation == "") return false;
            return LooksGenericChineseStaffName(p.fullName);
        }

        static bool HasDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '0' && s[i] <= '9') return true;
            return false;
        }

        static bool LooksGenericChineseStaffName(string name)
        {
            if (name.IndexOf('·') >= 0 || name.IndexOf(' ') >= 0) return false;
            if (name.Length < 2 || name.Length > 3) return false;
            for (int i = 0; i < KrSurnames.Length; i++)
                if (name.StartsWith(KrSurnames[i]) && name.Length == 3) return false;
            for (int i = 0; i < JpSurnames.Length; i++)
                if (name.StartsWith(JpSurnames[i])) return false;
            return true;
        }

        /// <summary>旧档无 jobTitle 时，按职级补职位名，便于编制表统计。</summary>
        public static void EnsureJobTitles(GameState gs)
        {
            if (gs?.staffPool == null) return;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !string.IsNullOrEmpty(p.jobTitle)) continue;
                var roles = StaffEstablishment.Roles(p.department);
                for (int r = 0; r < roles.Length; r++)
                {
                    if (roles[r].rank == p.rank)
                    {
                        p.jobTitle = roles[r].jobTitle;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(p.jobTitle))
                    p.jobTitle = p.rank;
            }
        }

        static PersonnelData Make(GameState gs, string dept, string rank, string jobTitle, int age, HashSet<string> used, ref int n, bool employed = false)
        {
            n++;
            string home = HomeCountry(gs);
            string nation = NationForSlot(n, home, ForeignCountries(home));
            string name = ComposeName(nation, n * 31 + StableHash(dept), used);
            float eff = rank == "负责人" ? 1f : rank == "副手" ? 0.85f : rank == "主管" ? 0.64f : 0.48f;
            float pay = rank == "负责人" ? 48f : rank == "副手" ? 28f : rank == "主管" ? 16f : 8f;
            return new PersonnelData
            {
                id = $"STF-{dept}-{n:D3}",
                fullName = name,
                age = age,
                nationality = nation,
                traitsCsv = rank == "副手" || rank == "负责人" ? "稳妥、执行力" : "成长型",
                baseSalary = pay,
                loyalty = 68f + (n % 12),
                efficiencyBonus = eff,
                department = dept,
                rank = rank,
                jobTitle = jobTitle,
                potential = rank == "专员" ? HrEconomics.BaseSpecialistPotential : SupervisorPotential,
                onMarket = !employed
            };
        }

        /// <summary>招聘管线生成一名待聘候选人（尚未到岗）。</summary>
        public static PersonnelData CreateForRole(GameState gs, string dept, string rank, string jobTitle, string source = null)
        {
            if (gs == null) return null;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            var used = new HashSet<string>();
            for (int i = 0; i < gs.staffPool.Count; i++)
                if (gs.staffPool[i] != null && !string.IsNullOrEmpty(gs.staffPool[i].fullName))
                    used.Add(gs.staffPool[i].fullName);

            // id 由序号派生，而序号取自 staffPool.Count —— 但池子会**变小**（录用后离池、
            // 退回时 ReleaseCandidate 直接 Remove），于是新序号会撞上既有 id。
            // 撞号的后果不是重复显示那么简单：Find 只返回首个匹配，后来者永远打不开档案，
            // 且退回他时按引用移除会误删前一个人。故此处必须循环到取到空闲 id 为止
            // （Make 内部会自增 n，重试自然换号）。
            int n = gs.staffPool.Count + 1;
            PersonnelData p = null;
            for (int guard = 0; guard < 4096; guard++)
            {
                p = Make(gs, dept, rank, jobTitle, 26 + (n % 16), used, ref n, employed: false);
                if (Find(gs, p.id) == null) break;
                p = null;
            }
            if (p == null)
            {
                Debug.LogWarning($"[PersonnelGenerator] 「{dept}/{jobTitle}」连续 4096 个序号都被占用，放弃生成。");
                return null;
            }
            ApplySourceProfile(p, source);
            gs.staffPool.Add(p);
            return p;
        }

        /// <summary>
        /// 非专员档（副总监/主管）基准潜力。
        /// 培训入册门槛（HrEconomics.TrainingEnrollMinPotential）必须 ≤ 本值，
        /// 否则「启动高潜管理培训」在开局会因无人达标而永远失败（已由自检断言）。
        /// </summary>
        public const float SupervisorPotential = 0.62f;

        /// <summary>
        /// 按来源渠道给候选人分层。这是「职业介绍所按来源筛选」具备玩法意义的前提：
        /// 若各来源属性同分布，合并后只是换了个显示位置，零决策价值。
        /// 倍率权威值在 HrEconomics.SourceStatMultipliers（纯 C#，游戏与 tools/balance 共用）。
        /// </summary>
        public static void ApplySourceProfile(PersonnelData p, string source)
        {
            if (p == null) return;
            p.source = HrEconomics.NormalizeSource(source);
            // 基准来源不改属性：市场 / 内部转岗 / 继承在册都不是「渠道分层」的对象
            //（继承在册尤其重要——若落到 else 分支，会被贴上「国际经验、适应期」的特性标签）
            if (p.source == HrEconomics.SourceMarket || p.source == HrEconomics.SourceInternal
                || p.source == HrEconomics.SourceInherited)
                return;

            HrEconomics.ApplySourceStats(p.source,
                p.potential, p.baseSalary, p.loyalty, p.efficiencyBonus,
                out p.potential, out p.baseSalary, out p.loyalty, out p.efficiencyBonus);

            p.traitsCsv = p.source == HrEconomics.SourceCampus ? "成长型、待培养"
                : p.source == HrEconomics.SourceHeadhunt ? "即战力、履历亮眼"
                : "国际经验、适应期";
        }

        /// <summary>
        /// 接管局：按继承的编制把在册运行机组真正写进人员池（此前接管航司的机组完全不入模，
        /// 8 条已运营航线被当成「一架机组都没有」，无条件吃满未建机组折损）。
        ///
        /// 编制 = OperationalEstablishment(岗位, 在役机队, 宽体, 总座位)，在册数 = 编制 × (1 − 缺员率)。
        /// 于是 profile.crewVacancyPercent（「机组人员短缺 X%」）从装饰性文案变成真实缺口：
        /// 职业介绍所里会看到对应的人数缺口，机组乘数按覆盖率插值。
        ///
        /// 两处刻意不做的事：
        ///   ① source 标为 SourceInherited → 薪酬留在「存量人力成本」聚合口径内（不双计）；
        ///   ② 不标成 SourceOverseas → 不计入 OverseasCrewCount，
        ///      国际航线准入仍必须靠海外引进或考取 intl_ops（否则接管局白送一条法规通道）。
        /// </summary>
        /// <returns>实际生成的在册机组人数。</returns>
        public static int SeedInheritedCrew(GameState gs, int vacancyPercent)
        {
            if (gs == null) return 0;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            gs.GetFleetStaffing(out int activeFleet, out int widebody, out int seats);
            if (activeFleet <= 0) return 0;

            int vac = vacancyPercent < 0 ? 0 : (vacancyPercent > 60 ? 60 : vacancyPercent);
            // 与 HrManager.IsCrewPost / OperationalEstablishment 同口径的三类运行岗。
            string[] posts = { "飞行员", "乘务员", "机务工程师" };
            int made = 0;
            for (int i = 0; i < posts.Length; i++)
            {
                string post = posts[i];
                int est = HrEconomics.OperationalEstablishment(post, activeFleet, widebody, seats);
                if (est <= 0) continue;
                int need = (int)System.Math.Round(est * (100 - vac) / 100.0);
                string dept = post == "机务工程师" ? "MNT" : "OPS";
                for (int k = 0; k < need; k++)
                {
                    var p = CreateForRole(gs, dept, "专员", post, HrEconomics.SourceInherited);
                    if (p == null) break;
                    p.onMarket = false;      // 在册在职，不进介绍所
                    p.isOperational = true;  // 运行序列（机组乘数 / 编制封顶 / 运行岗薪酬均认此位）
                    p.age = 28 + ((made * 7) % 22);
                    made++;
                }
            }
            gs.InvalidateCrewCache();
            return made;
        }

        /// <summary>开局缺总监时生成一名在职负责人（不进介绍所）。</summary>
        public static PersonnelData CreateEmployedHead(GameState gs, string dept)
        {
            if (gs == null) return null;
            if (gs.staffPool == null) gs.staffPool = new List<PersonnelData>();
            var used = new HashSet<string>();
            for (int i = 0; i < gs.staffPool.Count; i++)
                if (gs.staffPool[i] != null && !string.IsNullOrEmpty(gs.staffPool[i].fullName))
                    used.Add(gs.staffPool[i].fullName);
            int n = gs.staffPool.Count + 1;
            var p = Make(gs, dept, "负责人", StaffEstablishment.HeadTitle, 44, used, ref n, employed: true);
            gs.staffPool.Add(p);
            return p;
        }

        static string ComposeName(string nation, int seed, HashSet<string> used)
        {
            if (used == null) used = new HashSet<string>();
            seed = seed < 0 ? -seed : seed;
            for (int t = 0; t < 96; t++)
            {
                int s = seed + t * 17;
                string name = DraftName(nation, s);
                if (used.Add(name)) return name;
            }
            string fallback = DraftName(nation, seed + 41);
            used.Add(fallback);
            return fallback;
        }

        static string DraftName(string nation, int s)
        {
            switch (nation)
            {
                case "韩国": return Pick(KrSurnames, s) + Pick(KrGiven, s * 5 + 1);
                case "日本": return Pick(JpSurnames, s) + Pick(JpGiven, s * 7 + 2);
                case "新加坡": return Pick(SgNames, s * 3 + 1);
                case "马来西亚": return Pick(MyNames, s * 5 + 2);
                case "美国":
                case "英国":
                case "澳大利亚":
                case "加拿大": return Pick(EnNames, s * 7 + 3);
                case "法国": return Pick(FrNames, s * 5 + 1);
                case "德国": return Pick(DeNames, s * 5 + 3);
                case "泰国": return Pick(ThNames, s * 7 + 1);
                case "印度": return Pick(InNames, s * 5 + 2);
                case "阿联酋":
                case "卡塔尔": return Pick(ArNames, s * 7 + 4);
                default: return Pick(CnSurnames, s) + Pick(CnGiven, s * 13 + 5);
            }
        }

        static string Pick(string[] arr, int seed)
        {
            int i = seed % arr.Length;
            if (i < 0) i += arr.Length;
            return arr[i];
        }

        static int StableHash(string s)
        {
            int h = 17;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++)
                h = h * 31 + s[i];
            return h < 0 ? -h : h;
        }

        public static PersonnelData Find(GameState gs, string id)
        {
            if (gs?.staffPool == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                if (gs.staffPool[i] != null && gs.staffPool[i].id == id)
                    return gs.staffPool[i];
            }
            return null;
        }
    }
}
