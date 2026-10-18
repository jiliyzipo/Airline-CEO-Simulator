using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Models;
using AirlineCEO.Balance.Scenarios;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace AirlineCEO.Balance.Sim;

/// <summary>
/// 最小航司一年模拟：RPM 日基准 × 班次 × <see cref="OpsEconomics"/> 调度效率/延误，并投影准点率；
/// Q2 注入一次 IRROPS 处置，形成 OPS 年闭环。
/// </summary>
public sealed class AirlineYearSimulator
{
    private readonly MacroEconomyEngine _macro;
    private readonly IReadOnlyList<SimAircraft> _fleet;
    private readonly IReadOnlyList<RouteTemplate> _routes;
    private readonly float _fixedOpex;
    private readonly float _brandEquity;
    private readonly float _openingCash;
    private readonly bool _applyOpsLayer;
    private readonly bool _injectIrrops;
    private readonly bool _applyFinanceLayer;
    private readonly bool _applyMktLayer;
    private readonly AncillarySettings _ancillary;
    private readonly PeakSeasonPackage _peakSeason;
    private readonly float _irropsSeverity;
    /// <summary>存量人力成本（万美元/月）：未逐人建模的全公司员工。</summary>
    private readonly float _laborMonthly;

    public AirlineYearSimulator(
        MacroEconomyEngine? macro = null,
        IReadOnlyList<SimAircraft>? fleet = null,
        IReadOnlyList<RouteTemplate>? routes = null,
        float openingCash = MinimalAirline.OpeningCashWan,
        float? fixedMonthlyOpex = null,
        float brandEquity = MinimalAirline.BrandEquity,
        bool applyOpsLayer = true,
        bool injectIrrops = true,
        bool applyFinanceLayer = true,
        bool applyMktLayer = true,
        AncillarySettings? ancillary = null,
        PeakSeasonPackage? peakSeason = null,
        float irropsSeverity = 1f,
        float? annualLaborCostWan = null)
    {
        _macro = macro ?? new MacroEconomyEngine();
        _fleet = fleet ?? MinimalAirline.CreateFleet();
        _routes = routes ?? MinimalAirline.CreateRoutes();
        _openingCash = openingCash;
        _fixedOpex = fixedMonthlyOpex ?? EconomyBalanceParams.FixedMonthlyOpexFor(_fleet.Count(a => a.IsActive));
        _brandEquity = brandEquity;
        _applyOpsLayer = applyOpsLayer;
        _injectIrrops = injectIrrops;
        _applyFinanceLayer = applyFinanceLayer;
        _applyMktLayer = applyMktLayer;
        _ancillary = ancillary ?? new AncillarySettings();
        _peakSeason = peakSeason ?? new PeakSeasonPackage
        {
            festival = "暑运",
            capacityChoice = 1,
            pricingChoice = 2,
            serviceChoice = 1,
            extendShoulder = true,
            extendDays = 14
        };
        _irropsSeverity = irropsSeverity;
        // 人力随规模走：未显式给定时按在役机队推导（见 HrEconomics.EstablishmentLaborCostWan）。
        _laborMonthly = HrEconomics.MonthlyLaborCostWan(
            annualLaborCostWan
            ?? HrEconomics.EstablishmentLaborCostWan(_fleet.Count(a => a.IsActive)));
    }

    public YearSummary RunFiscalYear(int year = 1)
    {
        Console.WriteLine(_macro.TickYear(year));

        var months = new List<MonthlyPnL>(12);
        float cash = _openingCash;
        float minCash = cash;
        float maxCash = cash;
        float totalRev = 0f;
        float totalCost = 0f;
        float totalProfit = 0f;
        float otp = 88f;
        float sumEff = 0f;
        float sumDelay = 0f;
        float irropsCash = 0f;
        string irropsNote = "";

        // —— FIN 年闭环状态 ——
        float carbonTotal = 0f;
        float interestTotal = 0f;
        float loanPrincipal = 0f;
        float loanRemaining = 0f;
        float loanMonthlyPay = 0f;
        float loanRate = 0f;
        int financingEvents = 0;

        int activeAc = _fleet.Count(a => a.IsActive);
        float fleetMaint = MinimalAirline.CalcFleetMonthlyMaint(_fleet);

        // 财年初：年度预算与董事会投票（建议值，不直接扣现金）
        float budgetWan = FinanceEconomics.RecommendedBudget(_openingCash);
        int budgetVotes = FinanceEconomics.EstimateBoardVotes(budgetWan, _openingCash, 50f);

        for (int month = 1; month <= 12; month++)
        {
            if (month is 4 or 7 or 10)
                _macro.ApplyFuelDrift();

            if (_injectIrrops && month == 6)
            {
                // 年中一次台风暴雨 IRROPS：选 A（全网延误缓冲）
                var outcome = OpsIrropsLogic.Resolve(
                    OpsIrropsLogic.IrropsKind.Weather,
                    OpsIrropsLogic.Choice.A);
                float irropsDelta = outcome.cashDeltaWan * _irropsSeverity;
                cash += irropsDelta;
                otp = Clamp(otp + outcome.onTimeDelta, 40f, 99f);
                irropsCash += irropsDelta;
                irropsNote = $"M6 {OpsIrropsLogic.KindLabel(OpsIrropsLogic.IrropsKind.Weather)}/{outcome.label}";
                Console.WriteLine($"[OPS] IRROPS {irropsNote}: cash {outcome.cashDeltaWan:F1}, otp→{otp:F1}");
            }

            var pnl = SimulateMonth(year, month, cash, activeAc, fleetMaint, ref otp);
            cash = pnl.CashEndWan;
            carbonTotal += pnl.CarbonCostWan;

            if (_applyFinanceLayer)
            {
                // 跑道告急（<3 月）且尚未融资时，触发一次银行贷款
                if (financingEvents == 0)
                {
                    float burnNow = Math.Max(0f, -pnl.NetProfitWan);
                    float runwayNow = CashRunnelCalculator.GetRunwayMonths(cash, burnNow);
                    if (runwayNow < 3f)
                    {
                        loanPrincipal = Math.Max(80f, burnNow * 4.5f);
                        loanRemaining = loanPrincipal;
                        loanRate = FinanceEconomics.BankLoanRate(
                            FinanceEconomics.EstimateCreditRating(cash, burnNow, 50f),
                            _macro.EconomicPhase) / 100f;
                        loanMonthlyPay = loanPrincipal / 12f + loanPrincipal * (loanRate / 12f);
                        cash += loanPrincipal;
                        financingEvents++;
                        Console.WriteLine($"[FIN] 跑道告急→银行贷款 {loanPrincipal:F0} 万 (年利率 {loanRate * 100:F1}%)");
                    }
                }

                // 等额本息月供（利息 + 本金）
                float interest = 0f;
                if (loanRemaining > 0.01f)
                {
                    interest = loanRemaining * (loanRate / 12f);
                    float principalPart = Math.Max(0f, loanMonthlyPay - interest);
                    loanRemaining = Math.Max(0f, loanRemaining - principalPart);
                    cash -= loanMonthlyPay;
                    interestTotal += interest;
                }
                pnl.InterestWan = Round2(interest);
                pnl.CashEndWan = Round2(cash);
            }

            minCash = Math.Min(minCash, cash);
            maxCash = Math.Max(maxCash, cash);
            totalRev += pnl.RevenueWan;
            totalCost += pnl.TotalCostWan;
            totalProfit += pnl.NetProfitWan;
            sumEff += pnl.AvgScheduleEfficiency;
            sumDelay += pnl.AvgDelayProbability;
            months.Add(pnl);
        }

        // 财年末：信用评级 / WACC / 碳负债
        float closingBurn = Math.Max(0f, -months[^1].NetProfitWan);
        var rating = FinanceEconomics.EstimateCreditRating(cash, closingBurn, 50f);
        float wacc = FinanceEconomics.Wacc(rating, _macro.EconomicPhase);
        float carbonEmit = FinanceEconomics.EstimateAnnualEmissionsTons(_routes.Count);
        float carbonLiab = FinanceEconomics.CarbonLiabilityWan(
            carbonEmit, 0f, FinanceEconomics.CarbonPriceUsdPerTon);

        // —— FLEET/NET 年闭环 ——
        var activeFleet = _fleet.Where(a => a.IsActive).ToList();
        int fleetCount = activeFleet.Count;
        float avgFleetAge = fleetCount > 0 ? (float)activeFleet.Average(a => a.AgeYears) : 0f;
        float fleetAnnualMaint = fleetMaint * 12f;
        float fleetResidual = activeFleet.Sum(a => FleetEconomics.ResidualValue(a.PurchasePriceWan, a.AgeYears));
        float seatMixSum = 0f;
        foreach (var a in activeFleet)
        {
            int seats = EconomyBalanceParams.DefaultSeatsFor(a.Type);
            int bus = (int)Math.Round(seats * EconomyBalanceParams.DefaultBusinessSeatShare);
            seatMixSum += FleetEconomics.SeatMixRevenueMultiplier(0, bus, seats - bus);
        }
        float seatMixMult = fleetCount > 0 ? seatMixSum / fleetCount : 1f;

        float routeContribution = 0f;
        float routeFixedCostYear = 0f;
        foreach (var m in months)
        {
            routeContribution += m.RevenueWan - m.FuelCostWan - m.LandingAndCrewWan - m.CarbonCostWan - m.RouteFixedCostWan;
            routeFixedCostYear += m.RouteFixedCostWan;
        }
        float networkDailyProfit = routeContribution / 365f;
        float avgRouteDistance = _routes.Count > 0 ? _routes.Average(r => r.DistanceKm) : 0f;

        // —— MKT 年闭环 ——
        float mktAuxRevenue = 0f;
        foreach (var m in months)
            mktAuxRevenue += m.AuxRevenueWan;
        float annualAsk = 0f;
        foreach (var route in _routes)
            annualAsk += MktEconomics.Ask(route.Seats, route.DistanceKm, route.WeeklyFlights) * 52f;
        float avgRask = annualAsk > 0f ? totalRev * 10000f / annualAsk : 0f;
        float avgCask = annualAsk > 0f ? totalCost * 10000f / annualAsk : 0f;
        float peakRoi = _applyMktLayer
            ? MktEconomics.PeakSeasonRoi(totalRev, totalCost, _peakSeason)
            : 0f;

        // —— MKT 批次 B 年闭环（竞争/渠道/品牌） ——
        var playerBench = new MktCompetitionEconomics.BenchInput
        {
            routes = _routes.Count,
            marketShare = 22f,
            avgFare = 900f,
            reputation = 70f,
            debtRatio = 0.3f,
            cash = cash,
            fleetSize = fleetCount,
            ancillaryScore = 50f,
            macroScore = 60f
        };
        float playerBenchScore = MktCompetitionEconomics.OverallScore(MktCompetitionEconomics.DimensionScores(playerBench));
        var rivalBench = new MktCompetitionEconomics.BenchInput
        {
            routes = _routes.Count + 2,
            marketShare = 25f,
            avgFare = 860f,
            reputation = 75f,
            debtRatio = 0.25f,
            cash = cash * 1.2f,
            fleetSize = fleetCount + 3,
            ancillaryScore = 55f,
            macroScore = 62f
        };
        float rivalBenchScore = MktCompetitionEconomics.OverallScore(MktCompetitionEconomics.DimensionScores(rivalBench));
        string verdict = MktCompetitionEconomics.SituationalVerdict(playerBenchScore, rivalBenchScore);
        float directRatio = 0.35f;
        float distRoi = MktCompetitionEconomics.ChannelRoi(totalRev * (1f - directRatio), Math.Max(1f, totalRev * (1f - directRatio) * 0.12f));
        float directRoi = MktCompetitionEconomics.ChannelRoi(totalRev * directRatio, Math.Max(1f, totalRev * directRatio * 0.06f));
        float brandTrust = MktCompetitionEconomics.TrustIndex(20f, 0.15f);
        int brandLevel = MktCompetitionEconomics.CrisisLevel(0.15f, 80);

        // —— STG 年闭环 ——
        float govRelation = 50f;
        float strategicConsistency = StrategyEconomics.StrategicConsistency(new[] { 1f, 0.9f, 0.8f, 0.7f, 0.6f }, 70f);
        int geoRisk = 35;
        int allianceTier = 2;
        float allianceLift = StrategyEconomics.AllianceRevenueLiftWan(allianceTier, _routes.Count, 3);
        int slotHoldings = 4;
        int approvedRights = _routes.Count;

        // —— MNT 年闭环 ——
        int maintStrategy = 0;
        float fleetAvailability = MaintenanceEconomics.AvailabilityByStrategy(maintStrategy);
        float maintCostMult = MaintenanceEconomics.CostMultiplierByStrategy(maintStrategy);
        int totalPartsQty = 120, totalSafety = 90, shortage = 1;
        float partsHealth = MaintenanceEconomics.StockHealth(totalPartsQty, totalSafety, shortage);
        int grounded = 0;
        int scheduledChecks = fleetCount * 2;

        // —— HR 年闭环 ——
        float empSatisfaction = 72f;
        // 人数与人力成本同源：都按在役机队派生（此前此处硬编码 450，与 laborCostWan 的机队缩放脱节）
        int headcount = HrEconomics.EffectiveHeadcount(fleetCount, 0f, 0);
        float vacancyRate = 0.08f;
        float laborCostWan = _laborMonthly * 12f;
        float laborRatio = HrEconomics.LaborCostRatio(laborCostWan, totalRev);
        float trainingRoi = HrEconomics.TrainingRoi(500f, headcount);
        float strikeRisk = 15f;

        // —— SAF 年闭环 ——
        float safetyScore = 82f;
        char safetyBand = SafetyEconomics.RatingBand(safetyScore);
        float cultureIndex = 68f;
        int cultureType = 1;
        float auditPassRate = 95f;
        float rectRate = 88f;

        // —— BOARD 年闭环 ——
        // 年度目标：净利 ≥ 50 / 客座率 ≥ 75 / 安全 ≥ 80（3 项保底），用 BoardEconomics 单一源
        float loadFactorYear = months.Count > 0 ? (float)months.Average(m => m.AvgLoadProxy) : 0f;
        int boardDone = 0;
        int boardTotal = 3;
        if (totalProfit >= BoardEconomics.GuaranteeNetProfit) boardDone++;
        if (loadFactorYear >= BoardEconomics.GuaranteeLoadFactor) boardDone++;
        if (safetyScore >= BoardEconomics.DefaultSafety) boardDone++;
        float boardRate = BoardEconomics.AchievementRate(boardTotal, boardDone);
        float boardThreshold = BoardEconomics.ReappointmentThreshold(DifficultyMode.Normal);
        VoteOutcome boardOutcome = boardRate + 0.001f >= boardThreshold
            ? VoteOutcome.Reappointed
            : (year <= BoardEconomics.ProbationMaxYear ? VoteOutcome.ProbationContinue : VoteOutcome.Fired);
        float boardDelta = BoardEconomics.SatisfactionDelta(boardOutcome, boardRate);
        float boardSat = Math.Clamp(BoardEconomics.InitialSatisfaction + boardDelta, 0f, 100f);

        // —— CROSS 年闭环 ——
        var crossHealth = new CrossDeptHealth
        {
            opsOtp = otp,
            finRunwayScore = CrossEconomics.RunwayScore(months[^1].RunwayMonths),
            mktBrand = brandTrust,
            hrSatisfaction = empSatisfaction,
            safRatingScore = safetyScore,
            mntAvailability = fleetAvailability
        };
        float crossScore = CrossEconomics.CrossConsistency(crossHealth);
        string crossBand = CrossEconomics.ConsistencyBand(crossScore);

        return new YearSummary
        {
            OpeningCash = _openingCash,
            ClosingCash = cash,
            TotalRevenue = Round2(totalRev),
            TotalCost = Round2(totalCost),
            TotalNetProfit = Round2(totalProfit),
            MinCash = Round2(minCash),
            MaxCash = Round2(maxCash),
            AvgMonthlyProfit = Round2(totalProfit / 12f),
            FinalRunwayMonths = months[^1].RunwayMonths,
            Months = months,
            AvgScheduleEfficiency = Round2(sumEff / 12f),
            AvgDelayProbability = Round2(sumDelay / 12f),
            OpeningOnTimePercent = 88f,
            ClosingOnTimePercent = Round2(otp),
            IrropsCashDeltaWan = Round2(irropsCash),
            IrropsNote = irropsNote,

            // FIN 年闭环
            WaccPercent = Round2(wacc),
            CreditRating = rating.ToString(),
            BudgetWan = Round2(budgetWan),
            BudgetVotes = budgetVotes,
            CarbonEmissionsTons = Round2(carbonEmit),
            CarbonLiabilityWan = Round2(carbonLiab),
            CarbonCostWan = Round2(carbonTotal),
            LoanPrincipalWan = Round2(loanPrincipal),
            LoanInterestPaidWan = Round2(interestTotal),

            // FLEET/NET 年闭环
            FleetCount = fleetCount,
            AvgFleetAge = Round2(avgFleetAge),
            FleetAnnualMaintWan = Round2(fleetAnnualMaint),
            FleetResidualWan = Round2(fleetResidual),
            SeatMixMult = Round2(seatMixMult),
            NetworkRouteCount = _routes.Count,
            NetworkDailyProfitWan = Round2(networkDailyProfit),
            AvgRouteDistanceKm = Round2(avgRouteDistance),
            RouteFixedCostWan = Round2(routeFixedCostYear),

            // MKT 年闭环
            MktAuxRevenueWan = Round2(mktAuxRevenue),
            AvgRaskUsd = Round2(avgRask),
            AvgCaskUsd = Round2(avgCask),
            PeakSeasonRoi = Round2(peakRoi),
            PeakSeasonFestival = _peakSeason.festival,

            // MKT 批次 B 年闭环
            PlayerBenchScore = Round2(playerBenchScore),
            BestRivalScore = Round2(rivalBenchScore),
            SituationalVerdict = verdict,
            DirectRatio = Round2(directRatio),
            DistChannelRoi = Round2(distRoi),
            DirectChannelRoi = Round2(directRoi),
            BrandTrustIndex = Round2(brandTrust),
            BrandCrisisLevel = brandLevel,

            // STG 年闭环
            GovernmentRelation = Round2(govRelation),
            StrategicConsistency = Round2(strategicConsistency),
            GeoRiskIndex = geoRisk,
            AllianceTier = allianceTier,
            AllianceRevenueLiftWan = Round2(allianceLift),
            SlotHoldings = slotHoldings,
            ApprovedRights = approvedRights,

            // MNT 年闭环
            MaintenanceStrategy = maintStrategy,
            FleetAvailability = Round2(fleetAvailability),
            MaintenanceCostMultiplier = Round2(maintCostMult),
            PartsStockHealth = Round2(partsHealth),
            PartsShortageCount = shortage,
            GroundedCount = grounded,
            ScheduledChecks = scheduledChecks,

            // HR 年闭环
            EmployeeSatisfaction = Round2(empSatisfaction),
            TotalHeadcount = headcount,
            VacancyRate = Round2(vacancyRate),
            LaborCostRatio = Round2(laborRatio),
            TrainingRoi = Round2(trainingRoi),
            StrikeRisk = Round2(strikeRisk),

            // SAF 年闭环
            SafetyRatingScore = Round2(safetyScore),
            SafetyBand = safetyBand,
            SafetyCultureIndex = Round2(cultureIndex),
            SafetyCultureType = cultureType,
            AuditPassRate = Round2(auditPassRate),
            RectificationRate = Round2(rectRate),

            // BOARD 年闭环
            BoardTargetDone = boardDone,
            BoardTargetTotal = boardTotal,
            BoardAchievementRate = Round2(boardRate),
            BoardSatisfactionDelta = Round2(boardDelta),
            BoardSatisfaction = Round2(boardSat),

            // CROSS 年闭环
            CrossConsistency = Round2(crossScore),
            CrossBand = crossBand
        };
    }

    private MonthlyPnL SimulateMonth(
        int year, int month, float cashStart, int activeAc, float fleetMaint, ref float otp)
    {
        float revenue = 0f;
        float fuelCost = 0f;
        float landingCrew = 0f;
        float carbonCost = 0f;
        float routeFixedCost = 0f;
        float demandSum = 0f;
        float loadSum = 0f;
        float effSum = 0f;
        float delaySum = 0f;
        float multSum = 0f;

        float seasonalMult = _applyMktLayer ? MktEconomics.MonthSeasonalCoefficient(month) : 1f;
        float auxRevenue = 0f;

        foreach (var route in _routes)
        {
            float demand = _macro.GetDemandMultiplier(route.PassengerType) * seasonalMult;
            demandSum += demand;
            loadSum += route.LoadFactor;

            float dailyFlightFactor = route.WeeklyFlights / 7f;

            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = route.DistanceKm,
                fuelPricePerTon = _macro.FuelPriceUsdPerTon,
                competitorSupply = route.CompetitorSupply,
                demandMultiplier = demand,
                brandEquity = _brandEquity,
                seats = route.Seats,
                loadFactor = route.LoadFactor,
                aircraftType = route.AircraftType,
                passengerType = route.PassengerType,
                fatigue01 = 0.15f,
                yieldMultiplier = route.YieldMultiplier
            });

            float opsMult = 1f;
            float eff = 1f;
            float delay = 0.1f;
            if (_applyOpsLayer)
            {
                var m = OpsEconomics.Evaluate(
                    route.DistanceKm,
                    route.WeeklyFlights,
                    route.AircraftType,
                    route.GroundStaffAssigned,
                    otp,
                    fatigue01: 0.15f);
                opsMult = m.profitMultiplier;
                eff = m.scheduleEfficiency;
                delay = m.delayProbability;
            }

            float monthFactor = dailyFlightFactor * 30f * opsMult;
            revenue += rpm.dailyRevenue * monthFactor;
            fuelCost += rpm.fuelCostWan * monthFactor;
            landingCrew += (rpm.airportCostWan + rpm.crewCostWan + rpm.maintVarWan) * monthFactor;
            // 航线周期固定成本（与班次无关）：外站场站/本地销售/航线管理与航权时刻维持。
            // 与 RPMCalculator 的按班成本互补、不重复；Unity 侧同口径见 OpsEconomics.RouteFixedDailyWan。
            routeFixedCost += OpsEconomics.RouteFixedDailyWan(route.DistanceKm, route.AircraftType) * 30f;
            if (_applyMktLayer)
            {
                float paxPerMonth = route.Seats * route.LoadFactor * dailyFlightFactor * 30f;
                float auxWan = paxPerMonth * MktEconomics.AuxRevenuePerPaxUsd(_ancillary) / 10000f;
                revenue += auxWan;
                auxRevenue += auxWan;
            }
            if (_applyFinanceLayer)
                carbonCost += FinanceEconomics.CarbonCostPerRouteYearWan(
                    route.DistanceKm, route.AircraftType, route.WeeklyFlights,
                    FinanceEconomics.CarbonPriceUsdPerTon) / 12f;
            effSum += eff;
            delaySum += delay;
            multSum += opsMult;
        }

        int n = Math.Max(1, _routes.Count);
        float avgDelay = delaySum / n;
        if (_applyOpsLayer)
            otp = OpsEconomics.ProjectOnTimePercent(otp, avgDelay);

        float totalCost = fuelCost + landingCrew + fleetMaint + _fixedOpex + carbonCost + _laborMonthly + routeFixedCost;
        float net = revenue - totalCost;
        float cashEnd = cashStart + net;
        float burn = net < 0f ? -net : 0f;
        float runway = CashRunnelCalculator.GetRunwayMonths(Math.Max(0f, cashEnd), burn);

        return new MonthlyPnL
        {
            Year = year,
            Month = month,
            EconomicPhase = _macro.EconomicPhase,
            GdpGrowthPct = Round2(_macro.GdpGrowthRate),
            FuelPriceUsdPerTon = Round2(_macro.FuelPriceUsdPerTon),
            DemandMultiplier = Round2(demandSum / n),
            RevenueWan = Round2(revenue),
            FuelCostWan = Round2(fuelCost),
            LandingAndCrewWan = Round2(landingCrew),
            FleetMaintWan = Round2(fleetMaint),
            FixedOpexWan = Round2(_fixedOpex),
            LaborCostWan = Round2(_laborMonthly),
            TotalCostWan = Round2(totalCost),
            NetProfitWan = Round2(net),
            CashEndWan = Round2(cashEnd),
            RunwayMonths = Round2(runway),
            AvgLoadProxy = Round2(loadSum / n),
            ActiveAircraft = activeAc,
            ActiveRoutes = _routes.Count,
            AvgScheduleEfficiency = Round2(effSum / n),
            AvgDelayProbability = Round2(avgDelay),
            OnTimePercent = Round2(otp),
            OpsProfitMult = Round2(multSum / n),
            CarbonCostWan = Round2(carbonCost),
            SeasonalDemandMult = Round2(seasonalMult),
            AuxRevenueWan = Round2(auxRevenue),
            RouteFixedCostWan = Round2(routeFixedCost)
        };
    }

    private static float Round2(float v) => (float)Math.Round(v, 2);

    private static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}
