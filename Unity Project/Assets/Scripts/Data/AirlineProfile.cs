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
        /// <summary>机队维护 + 月租 + 存量人力（不含 <see cref="monthlyCost"/> 的总部/地面开销）。</summary>
        public float monthlyFixedCost;
        public float monthlyRevenue;
        /// <summary>
        /// 存量贷款的**月均还本付息**（万美元/月）= Σ <c>monthlyPayment</c>。
        /// 单独成字段而不是并进 <see cref="monthlyFixedCost"/>，因为两者的记账节拍不同：
        /// 维护/租金/人力逐日计提，贷款是季末一次扣 3 个月。并进去会让 `gs.monthlyBurnRate`
        /// 重复计一次钱（LoanManager 已经在扣）。它只进 <see cref="monthlyNetBurn"/>：
        /// 跑道衡量的是「现金能撑多久」，而偿付是**强制性**现金流出，标准的 runway 定义含它。
        /// </summary>
        public float monthlyDebtService;
        /// <summary>
        /// 月均航空保险保费（万美元/月）= 季度保费 / 3。全险按季一次性扣现金
        /// （<c>FinanceManager.ChargeQuarterlyInsurance</c>），故它与还本付息一样是
        /// **真实且强制**的现金流出，必须进跑道。旧口径完全漏掉它：★2/★3/★4/★5 分别
        /// 约 16 / 69 / 183 / 294 万/月，占预览烧钱的 10%~20%（2026-10-21 K10 复验发现）。
        /// </summary>
        public float monthlyInsurance;
        /// <summary>
        /// 接管前的每月净烧钱。= 总部/地面 + 机队维护/租金/人力 + 月均还本付息 + 月均保费 − 收入。
        /// ≤ 0 表示不烧钱，跑道无上限。
        /// </summary>
        public float monthlyNetBurn;
        public float debtRatio;
        /// <summary>
        /// 现金跑道（月）。仅当 <see cref="cashRunwayUnbounded"/> 为 false 时有意义 ——
        /// 净烧钱 ≤ 0 时「跑道」在数学上没有尽头，旧实现把分母夹成 1，于是跑道退化成现金原值
        /// （★3 报 1200.0 月、★4 报 2000.0 月）。展示请用 <see cref="Systems.TakeoverWorldBuilder.RunwayText"/>。
        /// </summary>
        public float cashRunwayMonths;
        /// <summary>净烧钱 ≤ 0：跑道无上限，<see cref="cashRunwayMonths"/> 不可当数字读。</summary>
        public bool cashRunwayUnbounded;
        /// <summary>
        /// K12：净美元头寸（万美元，负 = 净美元负债）。与 <c>FxEconomics.NetUsdExposureWan</c> 同口径，
        /// 只是用接管档案的数据算：国际航线季净收入 −（存量贷款余本 + 一年租赁承诺）。
        /// 只用于「季末一次性项预计 ±X 万」那一行，**不参与**月烧钱与跑道。
        /// </summary>
        public float netUsdExposureWan;
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
