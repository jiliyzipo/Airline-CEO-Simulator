using System;
using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>机队单机运行时数据（Phase 10）。</summary>
    [Serializable]
    public class Aircraft
    {
        public string registration;
        public string type;
        public string aircraftName;   // 飞机名称（如「迪士尼号」），可空
        public string source = "自有"; // 自有 / 融资租赁 / 经营租赁 / 二手引进
        /// <summary>退出方式（退役 / 退租）：仅 <c>status == Retired</c> 时有意义，空 = 未处置。租赁机记「退租」。</summary>
        public string disposal;
        public float monthlyLeaseWan;
        public int remainingLifeYears;
        public int age;
        public float condition = 100f;
        public bool isActive = true;
        public string status = AircraftStatuses.Airworthy;
        public int purchaseYear = 1;
        public float purchasePrice;
        public float flightHours;     // 累计飞行小时
        public int flightCycles;      // 累计起降循环

        /// <summary>专用货机载重（吨）。0 = 客机（无专用货运，仅腹舱捎货）。</summary>
        public float cargoTons;

        // —— 舱位配置（n11）——
        public int firstClassSeats;
        public int businessSeats;
        public int economySeats = 180;

        // —— 大修记录（n107）——
        public string lastOverhaulType;   // A/B/C/D 检 · 发动机 · 起落架 · 客舱翻新
        public int lastOverhaulYear;

        /// <summary>
        /// 大修预计出厂时刻（UTC ticks）；0 = 无在排窗口。
        ///
        /// ★2026-10-27 修：此前 `停场 N 天` 只写在日志与文案里，机队里没有任何字段承载这个窗口，
        /// 于是飞机进 `维护中` 之后**永远不会自己出厂**（只有财年末 `RefreshStatus` 会顺带刷回
        /// 适航，且机龄 &gt; 15 时反而被永久钉在维护中）。n107 那句「停场 2 天」是不存在的承诺。
        /// 现在窗口落在本字段上，由 <see cref="Systems.FleetManager.ReleaseCompletedOverhauls"/>
        /// 在每日时钟上推进、到期复飞。
        ///
        /// 用 UTC ticks 而不是「剩余天数」：时钟可以一次跨好几天（`AdvanceGameMinutes` 有追赶上限），
        /// 存绝对值才不会因为跳过某天而漏减。旧存档没有本字段 → 反序列化为 0 → 视为无窗口，
        /// 与旧行为（永不自动出厂）一致，不需要迁移。
        /// </summary>
        public long maintenanceUntilUtcTicks;

        /// <summary>是否仍在排定的停场窗口内（无窗口或已到期返回 false）。</summary>
        public bool InScheduledMaintenance(DateTime nowUtc)
            => maintenanceUntilUtcTicks > 0 && nowUtc.Ticks < maintenanceUntilUtcTicks;

        public int TotalSeats =>
            firstClassSeats + businessSeats + economySeats;

        /// <summary>
        /// 退出方式标签（退役 / 退租）：退出机队后供界面与呈报显示；未处置返回空串。
        /// 老存档里的退役机没有 <c>disposal</c>，一律按「退役」补齐（当时只有自有飞机可退）。
        /// </summary>
        public string ExitLabel
        {
            get
            {
                if (status != AircraftStatuses.Retired) return "";
                return string.IsNullOrEmpty(disposal) ? AircraftStatuses.Retired : disposal;
            }
        }

        /// <summary>是否已退出机队（退役 / 退租）：不再执飞、也不再计月租与月维护。</summary>
        public bool IsDisposed => status == AircraftStatuses.Retired;
    }

    public static class AircraftStatuses
    {
        public const string Airworthy = "适航";
        public const string InFlight = "飞行中";
        public const string Maintenance = "维护中";
        public const string Retired = "退役";
    }

    /// <summary>可采购机型目录条目（内存表，非 ScriptableObject）。</summary>
    [Serializable]
    public class AircraftCatalogEntry
    {
        public string typeId;
        public string oem;
        public string family;
        public string icao;
        /// <summary>对外显示名（如 Airbus A320-200），与 ICAO 代号分开。</summary>
        public string displayName;
        public int maxPassengers;
        public float maxRangeKm;
        public float priceWan;
        /// <summary>是否为专用货机（GDB 14.6 纯货机成本中心）。</summary>
        public bool isFreighter;
        /// <summary>货机载重（吨）；客机为 0。</summary>
        public float cargoTons;
    }

    [Serializable]
    public class FleetListSave
    {
        public Aircraft[] items;
        public int nextRegistrationNumber = 1;
    }
}
