using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 证照定义（纯数据）。此前全项目没有「按人的资质」这一层：
    /// 唯一的资质概念是二元的 <see cref="RoutePermitLogic.HasOverseasQualification"/>，
    /// 以「海外引进渠道到岗的运行人员数 &gt; 0」表示，无法表达「张三持有机务执照、李四没有」。
    /// </summary>
    public sealed class CertDef
    {
        public string id;
        public string name;
        /// <summary>适用部门代码（OPS / MNT）。</summary>
        public string dept;
        /// <summary>岗位关键字（匹配 jobTitle）；留空表示该部门全员适用。</summary>
        public string titleKeyword;
        /// <summary>发证所需培训天数（决定占用几个季度，见 CertificationLogic.DurationQuarters）。</summary>
        public int studyDays;
        /// <summary>人均费用（万美元）。</summary>
        public float costWan;
        /// <summary>有效期（月）；0 = 长期有效。</summary>
        public int validMonths;
        /// <summary>允许办理的最低培训中心等级（1自建 2综合基地 3航司大学）。</summary>
        public int minCenterLevel;
        /// <summary>办理该证的玩法意义，显示在个人页。</summary>
        public string note;
    }

    /// <summary>证照目录：民航从业常见证照。权威值，游戏与平衡工具共用。</summary>
    public static class CertificationCatalog
    {
        public const string PilotLicense = "pilot_atpl";
        public const string PilotMedical = "pilot_medical";
        public const string TypeRating = "type_rating";
        public const string CabinSafety = "cabin_safety";
        public const string MaintLicense = "mnt_license";
        public const string MaintRelease = "mnt_release";
        public const string IntlOps = "intl_ops";

        /// <summary>
        /// 全部证照。门槛分三档，使培训中心的每一级都有明确解锁物：
        /// 1 级办基础证、2 级办进阶证、3 级才办国际运行资质（航线准入的第二条路径）。
        /// </summary>
        public static readonly CertDef[] All =
        {
            new CertDef
            {
                id = PilotMedical, name = "体检合格证", dept = "OPS", titleKeyword = "飞行",
                studyDays = 14, costWan = 5f, validMonths = 12, minCenterLevel = 1,
                note = "飞行人员适航必备，12 个月到期需复检"
            },
            new CertDef
            {
                id = MaintLicense, name = "机务执照", dept = "MNT", titleKeyword = "",
                studyDays = 56, costWan = 50f, validMonths = 0, minCenterLevel = 1,
                note = "机务从业基础资质，长期有效"
            },
            new CertDef
            {
                id = CabinSafety, name = "乘务安全培训记录", dept = "OPS", titleKeyword = "乘务",
                studyDays = 21, costWan = 8f, validMonths = 24, minCenterLevel = 1,
                note = "客舱安全资质，24 个月到期"
            },
            new CertDef
            {
                id = PilotLicense, name = "飞行员执照", dept = "OPS", titleKeyword = "飞行",
                studyDays = 120, costWan = 60f, validMonths = 0, minCenterLevel = 2,
                note = "商用驾驶员执照，长期有效"
            },
            new CertDef
            {
                id = TypeRating, name = "机型授权", dept = "OPS", titleKeyword = "飞行",
                studyDays = 100, costWan = 80f, validMonths = 0, minCenterLevel = 2,
                note = "机型改装放行资格，长期有效"
            },
            new CertDef
            {
                id = MaintRelease, name = "放行授权", dept = "MNT", titleKeyword = "",
                studyDays = 100, costWan = 70f, validMonths = 24, minCenterLevel = 2,
                note = "定检放行签署权，24 个月到期"
            },
            new CertDef
            {
                id = IntlOps, name = "国际运行资质", dept = "OPS", titleKeyword = "飞行",
                studyDays = 200, costWan = 120f, validMonths = 0, minCenterLevel = 3,
                note = "国际/地区航线准入资质，可替代「海外引进」渠道满足民航局要求"
            },
        };

        public static CertDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && All[i].id == id) return All[i];
            return null;
        }

        /// <summary>该证照是否适用于此人（部门 + 岗位关键字）。</summary>
        public static bool AppliesTo(PersonnelData p, CertDef d)
        {
            if (p == null || d == null) return false;
            if (!string.Equals(p.department, d.dept, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrEmpty(d.titleKeyword)) return true;
            // 兼容 jobTitle 为空的历史记录：此时只要部门对上就放行，避免旧档无人可办证。
            if (string.IsNullOrEmpty(p.jobTitle)) return true;
            return p.jobTitle.Contains(d.titleKeyword);
        }

        /// <summary>该人可办理的全部证照（不过滤中心等级与持证情况）。</summary>
        public static List<CertDef> ForPerson(PersonnelData p)
        {
            var list = new List<CertDef>();
            if (p == null) return list;
            for (int i = 0; i < All.Length; i++)
                if (AppliesTo(p, All[i])) list.Add(All[i]);
            return list;
        }
    }

    /// <summary>持证与到期判定。所有对外方法都对 null 的 certs 列表做了归一，旧档可直接调。</summary>
    public static class CertificationLogic
    {
        /// <summary>旧档迁移：certs 为 null 时补空表（Unity 反序列化缺失字段会留 null）。</summary>
        public static void EnsureCerts(PersonnelData p)
        {
            if (p == null) return;
            if (p.certs == null) p.certs = new List<CertHeld>();
        }

        public static CertHeld Held(PersonnelData p, string certId)
        {
            if (p?.certs == null || string.IsNullOrEmpty(certId)) return null;
            for (int i = 0; i < p.certs.Count; i++)
            {
                var h = p.certs[i];
                if (h != null && h.id == certId) return h;
            }
            return null;
        }

        /// <summary>是否持有（不管到期）。</summary>
        public static bool Has(PersonnelData p, string certId) => Held(p, certId) != null;

        /// <summary>是否持有且未过期。<paramref name="nowUtc"/> 为模拟时钟。</summary>
        public static bool HasValid(PersonnelData p, string certId, DateTime nowUtc)
        {
            var h = Held(p, certId);
            if (h == null) return false;
            return !IsExpired(h, nowUtc);
        }

        public static bool IsExpired(CertHeld h, DateTime nowUtc) =>
            h != null && h.expiresTicks > 0 && nowUtc.Ticks > h.expiresTicks;

        /// <summary>距到期天数；长期有效返回 -1。</summary>
        public static int DaysToExpiry(CertHeld h, DateTime nowUtc)
        {
            if (h == null || h.expiresTicks <= 0) return -1;
            return (int)Math.Ceiling((new DateTime(h.expiresTicks, DateTimeKind.Utc) - nowUtc).TotalDays);
        }

        /// <summary>发证：已持有则只顺延有效期，不重复叠加。</summary>
        public static void Grant(PersonnelData p, CertDef d, DateTime nowUtc)
        {
            if (p == null || d == null) return;
            EnsureCerts(p);
            long expires = d.validMonths > 0
                ? nowUtc.AddMonths(d.validMonths).Ticks
                : 0L;
            var h = Held(p, d.id);
            if (h == null)
            {
                p.certs.Add(new CertHeld { id = d.id, issuedTicks = nowUtc.Ticks, expiresTicks = expires });
            }
            else
            {
                h.issuedTicks = nowUtc.Ticks;
                h.expiresTicks = expires;
            }
        }

        /// <summary>办证占用的季度数（向上取整，至少 1 季）。</summary>
        public static int DurationQuarters(CertDef d)
        {
            if (d == null || d.studyDays <= 0) return 1;
            int q = (d.studyDays + HrEconomics.DaysPerQuarter - 1) / HrEconomics.DaysPerQuarter;
            return q < 1 ? 1 : q;
        }

        /// <summary>
        /// 「入职自带」的证照门槛上限：只有 1 级门槛的基础资质随岗自带
        /// （体检合格证 / 机务执照 / 乘务安全培训记录）。
        /// 进阶证（2 级）与国际运行资质（3 级）**永不自动授予** —— 它们必须由玩家
        /// 建培训中心 + 花钱考取，否则 <see cref="RoutePermitLogic.HasOverseasQualification"/>
        /// 的第二条路径会被免费打开。
        /// </summary>
        public const int StarterCertMaxCenterLevel = 1;

        /// <summary>
        /// 入职自带资质：运行序列（飞行员 / 乘务员 / 机务工程师）凭岗位持有对应的 1 级基础证。
        /// 目的是让「个人页 → 持证情况」不是永远空白，并让到期复检成为活的机制；
        /// 关键岗（管理序列）不自动发证，仍须走「考取 XX 证」。
        /// 幂等：已持有（含已过期）则跳过，可每日重复调用。
        /// </summary>
        public static int GrantStarterCerts(PersonnelData p, DateTime nowUtc)
        {
            if (p == null || !p.isOperational) return 0;
            EnsureCerts(p);
            int n = 0;
            for (int i = 0; i < CertificationCatalog.All.Length; i++)
            {
                var d = CertificationCatalog.All[i];
                if (d == null || d.minCenterLevel > StarterCertMaxCenterLevel) continue;
                if (!CertificationCatalog.AppliesTo(p, d)) continue;
                if (Held(p, d.id) != null) continue;
                Grant(p, d, nowUtc);
                n++;
            }
            return n;
        }

        /// <summary>
        /// 满足条件（部门/岗位匹配 + 中心等级够 + 未持有有效证）的可办证照。
        /// 用于个人页「考取 XX 证」菜单。
        /// </summary>
        public static List<CertDef> AvailableFor(PersonnelData p, int centerLevel, DateTime nowUtc)
        {
            var list = new List<CertDef>();
            if (p == null || p.onMarket) return list;
            var all = CertificationCatalog.ForPerson(p);
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (centerLevel < d.minCenterLevel) continue;
                if (HasValid(p, d.id, nowUtc)) continue;
                list.Add(d);
            }
            return list;
        }

        /// <summary>某证照的持证人数（只算在职且未过期）。</summary>
        public static int ValidHolderCount(IList<PersonnelData> pool, string certId, DateTime nowUtc)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (p == null || p.onMarket) continue;
                if (HasValid(p, certId, nowUtc)) n++;
            }
            return n;
        }

        /// <summary>全公司在职持证总数（含过期未续的除外）。用于 n90 合规页的到期预警出口。</summary>
        public static int ExpiringCount(IList<PersonnelData> pool, DateTime nowUtc, int withinDays)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (p == null || p.onMarket || p.certs == null) continue;
                for (int k = 0; k < p.certs.Count; k++)
                {
                    int d = DaysToExpiry(p.certs[k], nowUtc);
                    if (d >= 0 && d <= withinDays) n++;
                }
            }
            return n;
        }

        /// <summary>一人持证摘要，供个人页一行显示。</summary>
        public static string Summary(PersonnelData p, DateTime nowUtc)
        {
            EnsureCerts(p);
            if (p.certs.Count == 0) return "暂无持证记录";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.certs.Count; i++)
            {
                var h = p.certs[i];
                var d = CertificationCatalog.Find(h.id);
                if (d == null) continue;
                if (sb.Length > 0) sb.Append("、");
                sb.Append(d.name);
                int days = DaysToExpiry(h, nowUtc);
                if (days < 0) continue;
                sb.Append(days <= 0 ? "（已过期）" : $"（{days} 天到期）");
            }
            return sb.Length == 0 ? "暂无持证记录" : sb.ToString();
        }
    }
}
