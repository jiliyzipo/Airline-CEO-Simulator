using System;
using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>机型模板（目录用）。与运行时 Aircraft / AircraftData 互补。</summary>
    [CreateAssetMenu(fileName = "AircraftTemplate", menuName = "AirlineCEO/Data/Aircraft Template", order = 10)]
    public class AircraftTemplate : ScriptableObject
    {
        public string typeId;
        public string displayName;
        public int maxPassengers = 180;
        public float maxRangeKm = 6000f;
        public float cruiseSpeedKmh = 850f;
        public float fuelBurnTonPerHour = 2.4f;
        public float purchasePriceWan = 11000f;
        public float monthlyMaintWan = 5f;
        public string category = "narrowbody"; // narrowbody / widebody / regional
    }

    /// <summary>航线模板（可开通目录）。运行时开通记录仍用 RouteData。</summary>
    [CreateAssetMenu(fileName = "RouteTemplate", menuName = "AirlineCEO/Data/Route Template", order = 11)]
    public class RouteTemplate : ScriptableObject
    {
        public string routeId;
        public string originIata;
        public string destinationIata;
        public float distanceKm;
        public string passengerType = "leisure"; // leisure / business
        public float baseDemand = 1f;
        public float competitorSupply = 0.4f;
        public int suggestedWeeklyFlights = 7;
        public float minAircraftRangeKm = 2000f;
    }

    /// <summary>员工/岗位画像（招聘与培训用）。</summary>
    [Serializable]
    public class StaffProfile
    {
        public string staffId;
        public string displayName;
        public string role;           // pilot / cabin / mechanic / ground / office
        public string departmentCode; // OPS/MNT/...
        public int seniorityYears;
        public float skill01 = 0.5f;
        public float fatigue01;
        public float salaryWanPerMonth = 1.2f;
        public string nationality;
        public bool isKeyTalent;
    }

    /// <summary>局方/内部审计定义。</summary>
    [Serializable]
    public class AuditDef
    {
        public string auditId;
        public string title;
        public string scope;          // fleet / ops / finance / safety
        public int durationDays = 5;
        public float severity01 = 0.5f;
        public string[] checklistKeys;
        public float ratingImpactIfFail = -5f;
    }

    /// <summary>
    /// 术语定义资产（可配置表）。
    /// </summary>
    [CreateAssetMenu(fileName = "TermDef", menuName = "AirlineCEO/Data/Term Def", order = 12)]
    public class TermDef : ScriptableObject
    {
        public string key;
        public string display;
        [TextArea(2, 6)] public string explain;
        public string category; // finance / ops / safety / market
    }
}
