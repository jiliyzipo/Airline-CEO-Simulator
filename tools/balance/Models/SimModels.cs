using AirlineCEO.Logic;

namespace AirlineCEO.Balance.Models;

/// <summary>可飞航线模板（最小航司场景）。</summary>
public sealed class RouteTemplate
{
    public required string RouteId { get; init; }
    public required string Origin { get; init; }
    public required string Destination { get; init; }
    public float DistanceKm { get; init; }
    public float CompetitorSupply { get; init; }
    public string PassengerType { get; init; } = "leisure";
    public int WeeklyFlights { get; init; } = 7;
    public string AircraftType { get; init; } = "A320neo";
    public int Seats { get; init; }
    public float LoadFactor { get; init; } = EconomyBalanceParams.DefaultLoadFactor;
    /// <summary>收益乘数（低成本票价折价等）；默认 1.0 不变。</summary>
    public float YieldMultiplier { get; init; } = 1f;
    /// <summary>地勤人数（OPS 过站/延误模型）。</summary>
    public int GroundStaffAssigned { get; init; } = 3;
}

public sealed class SimAircraft
{
    public required string Registration { get; init; }
    public required string Type { get; init; }
    public float PurchasePriceWan { get; init; }
    public int AgeYears { get; init; }
    public bool IsActive { get; set; } = true;
}

public sealed class MonthlyPnL
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string EconomicPhase { get; init; } = "";
    public float GdpGrowthPct { get; init; }
    public float FuelPriceUsdPerTon { get; init; }
    public float DemandMultiplier { get; init; }

    public float RevenueWan { get; set; }
    public float FuelCostWan { get; set; }
    public float LandingAndCrewWan { get; set; }
    public float FleetMaintWan { get; set; }
    public float FixedOpexWan { get; set; }
    public float LaborCostWan { get; set; }
    public float TotalCostWan { get; set; }
    public float NetProfitWan { get; set; }
    public float CashEndWan { get; set; }
    public float RunwayMonths { get; set; }
    public float AvgLoadProxy { get; set; }
    public int ActiveAircraft { get; set; }
    public int ActiveRoutes { get; set; }

    // —— OPS 调度层 ——
    public float AvgScheduleEfficiency { get; set; }
    public float AvgDelayProbability { get; set; }
    public float OnTimePercent { get; set; }
    public float OpsProfitMult { get; set; }

    // —— FIN 财务层 ——
    public float CarbonCostWan { get; set; }
    public float InterestWan { get; set; }
    /// <summary>航线周期固定成本（万美元/月）：与班次无关的外站/分销/航权维持。</summary>
    public float RouteFixedCostWan { get; set; }

    // —— MKT 市场层 ——
    public float SeasonalDemandMult { get; set; }
    public float AuxRevenueWan { get; set; }
}

public sealed class YearSummary
{
    public float OpeningCash { get; init; }
    public float ClosingCash { get; init; }
    public float TotalRevenue { get; init; }
    public float TotalCost { get; init; }
    public float TotalNetProfit { get; init; }
    public float MinCash { get; init; }
    public float MaxCash { get; init; }
    public float AvgMonthlyProfit { get; init; }
    public float FinalRunwayMonths { get; init; }
    public IReadOnlyList<MonthlyPnL> Months { get; init; } = Array.Empty<MonthlyPnL>();

    // —— OPS 年闭环 ——
    public float AvgScheduleEfficiency { get; init; }
    public float AvgDelayProbability { get; init; }
    public float OpeningOnTimePercent { get; init; }
    public float ClosingOnTimePercent { get; init; }
    public float IrropsCashDeltaWan { get; init; }
    public string IrropsNote { get; init; } = "";

    // —— FIN 年闭环 ——
    public float WaccPercent { get; init; }
    public string CreditRating { get; init; } = "";
    public float BudgetWan { get; init; }
    public int BudgetVotes { get; init; }
    public float CarbonEmissionsTons { get; init; }
    public float CarbonLiabilityWan { get; init; }
    public float CarbonCostWan { get; init; }
    public float LoanPrincipalWan { get; init; }
    public float LoanInterestPaidWan { get; init; }

    // —— FLEET/NET 年闭环 ——
    public int FleetCount { get; init; }
    public float AvgFleetAge { get; init; }
    public float FleetAnnualMaintWan { get; init; }
    public float FleetResidualWan { get; init; }
    public float SeatMixMult { get; init; }
    public int NetworkRouteCount { get; init; }
    public float NetworkDailyProfitWan { get; init; }
    public float AvgRouteDistanceKm { get; init; }
    /// <summary>航线周期固定成本（万美元/年，全网合计）：与班次无关的外站/分销/航权维持。</summary>
    public float RouteFixedCostWan { get; init; }

    // —— MKT 年闭环 ——
    public float MktAuxRevenueWan { get; init; }
    public float AvgRaskUsd { get; init; }
    public float AvgCaskUsd { get; init; }
    public float PeakSeasonRoi { get; init; }
    public string PeakSeasonFestival { get; init; } = "";

    // —— MKT 批次 B 年闭环（竞争/渠道/品牌） ——
    public float PlayerBenchScore { get; init; }
    public float BestRivalScore { get; init; }
    public string SituationalVerdict { get; init; } = "";
    public float DirectRatio { get; init; }
    public float DistChannelRoi { get; init; }
    public float DirectChannelRoi { get; init; }
    public float BrandTrustIndex { get; init; }
    public int BrandCrisisLevel { get; init; }

    // —— STG 年闭环（战略/航权/联盟/投资） ——
    public float GovernmentRelation { get; init; }
    public float StrategicConsistency { get; init; }
    public int GeoRiskIndex { get; init; }
    public int AllianceTier { get; init; }
    public float AllianceRevenueLiftWan { get; init; }
    public int SlotHoldings { get; init; }
    public int ApprovedRights { get; init; }

    // —— MNT 年闭环（维修/航材/定检） ——
    public int MaintenanceStrategy { get; init; }
    public float FleetAvailability { get; init; }
    public float MaintenanceCostMultiplier { get; init; }
    public float PartsStockHealth { get; init; }
    public int PartsShortageCount { get; init; }
    public int GroundedCount { get; init; }
    public int ScheduledChecks { get; init; }

    // —— HR 年闭环（人力/招聘/培训/工会） ——
    public float EmployeeSatisfaction { get; init; }
    public int TotalHeadcount { get; init; }
    public float VacancyRate { get; init; }
    public float LaborCostRatio { get; init; }
    public float TrainingRoi { get; init; }
    public float StrikeRisk { get; init; }

    // —— SAF 年闭环（安全评级/文化/局方/审计） ——
    public float SafetyRatingScore { get; init; }
    public char SafetyBand { get; init; }
    public float SafetyCultureIndex { get; init; }
    public int SafetyCultureType { get; init; }
    public float AuditPassRate { get; init; }
    public float RectificationRate { get; init; }

    // —— BOARD 年闭环（年度目标达成率 / 满意度） ——
    public int BoardTargetDone { get; init; }
    public int BoardTargetTotal { get; init; }
    public float BoardAchievementRate { get; init; }
    public float BoardSatisfactionDelta { get; init; }
    public float BoardSatisfaction { get; init; }

    // —— CROSS 年闭环（跨部门协同指数） ——
    public float CrossConsistency { get; init; }
    public string CrossBand { get; init; } = "";
}
