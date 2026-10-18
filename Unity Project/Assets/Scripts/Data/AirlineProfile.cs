using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>接管开局用的航司档案（预览 + 写入 GameState 的种子）。</summary>
    [Serializable]
    public class AirlineProfile
    {
        public string id;
        public string nameCN;
        public string nameEN;
        public string iataCode;
        public string icaoCode;
        public string hubAirport;
        public string continentZh;
        public string countryZh;
        public string cityZh;
        public int difficulty;
        public string blurb;
        public int fleetCount;
        public float avgFleetAge;
        public float cash;
        public float monthlyCost;
        public float monthlyRevenue;
        public float debtRatio;
        public float cashRunwayMonths;
        public int routeCount;
        public int crewVacancyPercent;
        public int dryLeaseCount;
        public int wetLeaseCount;
        public int charterCount;
        public int trainingCount;

        public List<Aircraft> aircraft = new List<Aircraft>();
        public List<RouteData> routes = new List<RouteData>();
        public List<DepartmentHeadSnapshot> departmentHeads = new List<DepartmentHeadSnapshot>();
        public List<string> boardMemberIds = new List<string>();
        public List<string> boardMemberNames = new List<string>();
        public List<AIAirline> competitors = new List<AIAirline>();
        public List<TransitionContract> transitionBusinesses = new List<TransitionContract>();
        public List<LegacyIssue> legacyIssues = new List<LegacyIssue>();
        public List<Loan> openingLoans = new List<Loan>();
    }
}
