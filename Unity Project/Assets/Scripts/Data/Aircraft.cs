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
        public int age;
        public float condition = 100f;
        public bool isActive = true;
        public string status = AircraftStatuses.Airworthy;
        public int purchaseYear = 1;
        public float purchasePrice;
    }

    public static class AircraftStatuses
    {
        public const string Airworthy = "适航";
        public const string Maintenance = "维护中";
        public const string Retired = "退役";
    }

    /// <summary>可采购机型目录条目（内存表，非 ScriptableObject）。</summary>
    [Serializable]
    public class AircraftCatalogEntry
    {
        public string typeId;
        public int maxPassengers;
        public float maxRangeKm;
        public float priceWan;
    }

    [Serializable]
    public class FleetListSave
    {
        public Aircraft[] items;
        public int nextRegistrationNumber = 1;
    }
}
