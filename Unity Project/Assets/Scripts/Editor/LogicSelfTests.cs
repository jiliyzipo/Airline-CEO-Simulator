#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 在 Console 打印三个计算器的简易用例，确认编译与数值合理。
    /// 菜单：AirlineCEO → Run Logic Self Tests
    /// </summary>
    public static class LogicSelfTests
    {
        [MenuItem("AirlineCEO/Run Logic Self Tests")]
        public static void Run()
        {
            // 每行单独 Debug.Log，避免多行日志在 Console 列表视图被折叠到首行。
            Debug.Log("=== Logic Self Tests ===");

            // RPM：北京-伦敦约 8150 km（Phase 1.3 校准后日利润个位～十几万）
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 8150f,
                fuelPricePerTon = 850f,
                competitorSupply = 0.65f,
                demandMultiplier = 1.2f,
                brandEquity = 55f,
                seats = 290,
                aircraftType = "B787-9",
                passengerType = "business"
            });
            Debug.Log($"RPM PEK-LHR → score={rpm.score} rating={rpm.rating} profit={rpm.dailyProfit}万/日 rev={rpm.dailyRevenue}");

            // 短途窄体
            var rpm2 = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 1200f,
                fuelPricePerTon = 800f,
                competitorSupply = 0.2f,
                demandMultiplier = 1.5f,
                brandEquity = 70f,
                seats = 180,
                aircraftType = "A320neo",
                passengerType = "leisure"
            });
            Debug.Log($"RPM short → score={rpm2.score} rating={rpm2.rating} profit={rpm2.dailyProfit}万/日");

            // —— 航线周期固定成本：与班次无关，加密即摊薄（「1 班亏 / 3 班赚」的来源）——
            Debug.Log($"周期固定成本 → PEK-SHA 1080km 窄体 {OpsEconomics.RouteFixedDailyWan(1080f, "A320neo"):F2} 万/日"
                      + $"　PEK-LHR 8150km 宽体 {OpsEconomics.RouteFixedDailyWan(8150f, "B787-9"):F2} 万/日"
                      + "（期望 0.12 / 0.42，宽体与长航线更高）");

            var fxRoute = new RouteData
            {
                routeId = "R-FIXED", origin = "PEK", destination = "SHA",
                distanceKm = 1080f, aircraftType = "A320neo", groundStaffAssigned = 3
            };
            float fxFixed = fxRoute.FixedDailyCostWan;
            var m7 = OpsEconomics.Evaluate(1080f, 7, "A320neo", 3, 90f);
            float marginal = 0.8f * fxFixed / m7.profitMultiplier; // 边际毛贡献：1 班必亏、3 班必盈
            Debug.Log($"周期固定成本 摊薄 → 单班毛贡献 {marginal:F2} 万/班时："
                      + $"周7班 {OpsEconomics.RouteDailyProfit(marginal, 7, m7, fxFixed):F2} 万/日"
                      + $"　周21班 {OpsEconomics.RouteDailyProfit(marginal, 21, OpsEconomics.Evaluate(1080f, 21, "A320neo", 3, 90f), fxFixed):F2} 万/日"
                      + $"（期望 1 班亏、3 班盈）"
                      + $"　盈亏平衡 {OpsEconomics.BreakEvenDailyFlights(marginal, m7, fxFixed):F2} 班/日");

            // 回归护栏：负基准（真实亏损）必须原样透传，加密只能放大亏损
            var fxLoss = new RouteData
            {
                routeId = "R-LOSS", origin = "PEK", destination = "SHA",
                distanceKm = 1080f, aircraftType = "A320neo", groundStaffAssigned = 3,
                weeklyFlights = 21, baseDailyProfit = -0.40f, dailyProfit = -0.40f
            };
            float fxResolved = OpsEconomics.ResolvedSingleFlightBaseline(fxLoss);
            OpsEconomics.ApplyToRoute(fxLoss, fxResolved, 90f);
            Debug.Log($"周期固定成本 护栏 → 亏损基准 {fxLoss.baseDailyProfit:F2}（期望 -0.40，未被洗白）"
                      + $"　周21班日净利 {fxLoss.dailyProfit:F2} 万/日（期望 <0：加密只能放大亏损，绝不转盈）"
                      + $"　{(!(OpsEconomics.ResolvedSingleFlightBaseline(fxLoss) > 0f) ? "透传 ✓" : "被改写 ✗")}");

            // 不变量：各班存毛贡献，固定成本只在航线层级扣一次（RecalcDailyProfit 幂等）
            var fxInv = new RouteData
            {
                routeId = "R-INV", origin = "PEK", destination = "SHA",
                distanceKm = 1080f, aircraftType = "A320neo", weeklyFlights = 7, groundStaffAssigned = 3
            };
            OpsEconomics.ApplyToRoute(fxInv, 2f, 90f);
            fxInv.SyncFlightsFromPrimary();
            float fxApplied = fxInv.dailyProfit;
            fxInv.RecalcDailyProfit();
            Debug.Log($"周期固定成本 不变量 → 单班字段 {fxInv.flights[0].dailyProfit:F2} == 毛贡献 {fxInv.GrossDailyProfit:F2}（期望相等）"
                      + $"　Recalc 前后 {fxApplied:F2} → {fxInv.dailyProfit:F2}（期望相等且幂等，不重复计提固定成本）");

            // Cash runway
            float months = CashRunnelCalculator.GetRunwayMonths(300f, 80f);
            Debug.Log($"CashRunway 300/80 → {months:F2} 月 ({CashRunnelCalculator.GetRunwayWarning(months)})");
            Debug.Log($"CashRunway burn<=0 → {CashRunnelCalculator.GetRunwayMonths(100f, 0f)}");

            // Chemistry
            var chem1 = ChemistryMatcher.Match(new List<string> { "A", "B", "H", "K" });
            Debug.Log($"Chem [A,B,H,K] → {string.Join(", ", chem1)}"); // 期望含 B-16, B-01, B-09 等

            var chem2 = ChemistryMatcher.Match(new List<string> { "A", "G" });
            Debug.Log($"Chem [A,G] → {string.Join(", ", chem2)}"); // B-01 不应触发（有 G）

            // —— 终局与遗产结算（GDB 第4章）——
            Debug.Log($"Grade 90→{LegacyScoreCalculator.GradeFor(90f)} " +
                      $"75→{LegacyScoreCalculator.GradeFor(75f)} " +
                      $"60→{LegacyScoreCalculator.GradeFor(60f)} " +
                      $"59→{LegacyScoreCalculator.GradeFor(59f)}"); // 期望 S / A / B / C

            int now = DateTime.Now.Year;
            Debug.Log($"Retire 65岁(第1年)→{YearEndEvaluator.ShouldRetire(now - 65, 1)} " +
                      $"30岁第19年→{YearEndEvaluator.ShouldRetire(now - 30, 19)} " +
                      $"30岁第20年→{YearEndEvaluator.ShouldRetire(now - 30, 20)}"); // 期望 true / false / true

            var goC = new GameObject("__ceo_contract_test__");
            try
            {
                var gsC = goC.AddComponent<GameState>();
                gsC.ceoContractStartYear = 1;
                gsC.ceoContractYears = 4;
                gsC.difficulty = DifficultyMode.Normal;
                gsC.ceoBirthYear = now - 40;
                var miss = new List<BoardTarget> { new BoardTarget { completed = false, title = "未完成" } };
                var hit = new List<BoardTarget> { new BoardTarget { completed = true, title = "完成" } };
                var y2 = YearEndEvaluator.Evaluate(miss, 2, gsC.difficulty, gsC.ceoBirthYear, gsC);
                var y4fail = YearEndEvaluator.Evaluate(miss, 4, gsC.difficulty, gsC.ceoBirthYear, gsC);
                var y4ok = YearEndEvaluator.Evaluate(hit, 4, gsC.difficulty, gsC.ceoBirthYear, gsC);
                Debug.Log($"合同 Y2 未达标 → {y2.outcome}（期望 ProbationContinue）");
                Debug.Log($"合同到期未达标 → {y4fail.outcome} / {y4fail.title}（期望 Fired / 合同到期）");
                Debug.Log($"合同到期达标 → {y4ok.outcome} renewed={y4ok.contractRenewed}（期望 Reappointed true）");
            }
            finally
            {
                GameObject.DestroyImmediate(goC);
            }

            var go = new GameObject("__legacy_test_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.cash = 1000f;
                gs.yearNetProfit = 100f;
                gs.difficulty = DifficultyMode.Normal;
                gs.fleet = new List<Aircraft> { new Aircraft { purchasePrice = 5000f, age = 0 } };
                gs.activeRoutes = new List<RouteData>
                {
                    new RouteData { origin = "PEK", destination = "PVG" },
                    new RouteData { origin = "PEK", destination = "JFK" }
                };
                gs.boardSatisfaction = 80f;
                gs.safetyRating = SafetyRating.B;

                Debug.Log($"MarketCap → {LegacyScoreCalculator.MarketCap(LegacySnapshotBuilder.FromGameState(gs)):F0}（期望 6800 = 1000 现金 + 5000 残值 + 800 净利×8）");

                var legacy = LegacyScoreCalculator.Calc(LegacySnapshotBuilder.FromGameState(gs));
                Debug.Log($"Legacy total={legacy.total:F1} grade={legacy.grade}");
                Debug.Log($"Legacy 市值分={legacy.marketCapScore:F0}（市值 {legacy.marketCapWan:F0}） 安全分={legacy.safetyScore:F0} 覆盖分={legacy.coverageScore:F0} 满意度分={legacy.satisfactionScore:F0}");
                Debug.Log($"Legacy 覆盖明细：{legacy.coveredAirports} 机场 / {legacy.coveredCountries} 国 / {legacy.coveredContinents} 洲 · 累计 {legacy.totalFlightHours:F0} FH");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }

            // —— P1 内容池扩充 ——
            Debug.Log($"事件池数量 → {RandomEventCatalog.All.Count}（期望 ≥120）");
            Debug.Log($"CEO 目标目录 → {TargetCatalog.All.Count} 条（期望 23）");
            Debug.Log($"TargetCatalog T-07 → {TargetCatalog.Get("T-07")?.title} / {TargetCatalog.Get("T-07")?.difficulty}");
            Debug.Log($"TargetCatalog 按组合 B-23 → {TargetCatalog.GetByChemistry("B-23")?.id}");
            Debug.Log($"负面判定 台风→{RandomEventCatalog.Get("EVT_TYPHOON_01")?.IsNegative()}（True） 补贴→{RandomEventCatalog.Get("EVT_SUBSIDY")?.IsNegative()}（False）");
            Debug.Log($"新增人为事故事件 → {RandomEventCatalog.Get("EVT_HUM_BIRD_STRIKE") != null}");
            var chemQ = ChemistryMatcher.Match(new List<string> { "C", "L", "M", "Q" });
            Debug.Log($"Chem [C,L,M,Q] → {string.Join(", ", chemQ)}（期望含 B-23、B-14）");

            // —— P0 遗留：软性失败 / 重大空难 / 三模式 ——
            var go2 = new GameObject("__endgame_test_gs__");
            try
            {
                var gs2 = go2.AddComponent<GameState>();
                gs2.safetyRating = SafetyRating.D;
                gs2.boardSatisfaction = 50f;
                gs2.yearNetProfit = 0f;

                bool d1 = EndgameEvaluator.CheckDefeat(gs2) != null;   // 第 1 个 D 季：不触发
                bool d2 = EndgameEvaluator.CheckDefeat(gs2) != null;   // 第 2 个 D 季：触发
                Debug.Log($"监管接管 连续D(1季)→{d1}（False） 连续D(2季)→{d2}（True）");
            }
            finally
            {
                GameObject.DestroyImmediate(go2);
            }

            var go3 = new GameObject("__hostile_test_gs__");
            try
            {
                var gs3 = go3.AddComponent<GameState>();
                gs3.boardSatisfaction = 15f;
                gs3.yearNetProfit = -200f;
                var hostile = EndgameEvaluator.CheckDefeat(gs3);
                Debug.Log($"敌意收购 满意度15+亏损→{(hostile != null ? hostile.kind.ToString() : "None")}（期望 HostileTakeover）");
            }
            finally
            {
                GameObject.DestroyImmediate(go3);
            }

            var go4 = new GameObject("__hostile2_test_gs__");
            try
            {
                var gs4 = go4.AddComponent<GameState>();
                gs4.boardSatisfaction = 15f;
                gs4.yearNetProfit = 50f;
                Debug.Log($"敌意收购 满意度15+盈利→{(EndgameEvaluator.CheckDefeat(gs4) != null)}（False：需亏损）");
            }
            finally
            {
                GameObject.DestroyImmediate(go4);
            }

            Debug.Log($"模式名称 0→{GameModeInfo.DisplayName(0)} 1→{GameModeInfo.DisplayName(1)} 2→{GameModeInfo.DisplayName(2)} 3→{GameModeInfo.DisplayName(3)}");

            // —— P2 长线运营：货运业务独立核算（GDB 14.6）——
            Debug.Log($"腹舱每吨净 {CargoEconomics.BellyNetPerTonWan:F3}（收入 {CargoEconomics.BellyRevenuePerTonWan:F3} − 装卸 {CargoEconomics.BellyHandlingPerTonWan:F3}）万美元/吨");
            Debug.Log($"腹舱日净 4t×55%×7班 → {CargoEconomics.BellyDailyNetWan(4f, 0.55f, 7):F3} 万美元/日（期望 ~0.306）");
            Debug.Log($"货机日吨公里 100t → {CargoEconomics.FreighterDailyTonKm(100f):F0} tkm（期望 936000）");
            Debug.Log($"货机 100t 日净 → {CargoEconomics.FreighterDailyNetWan(100f, 28000f):F2} 万美元/日（期望 ~14.6，盈利 {CargoEconomics.FreighterProfitable(100f, 28000f)}）");
            Debug.Log($"货机 65t 日净 → {CargoEconomics.FreighterDailyNetWan(65f, 18000f):F2} 万美元/日（期望 ~9.5，盈利 {CargoEconomics.FreighterProfitable(65f, 18000f)}）");

            var go5 = new GameObject("__cargo_test_gs__");
            try
            {
                var gs5 = go5.AddComponent<GameState>();
                gs5.cargo = new CargoState { bellyEnabled = true, bellyUtilization = 0.5f, freighterDailyUtilization = 12f };
                gs5.activeRoutes = new List<RouteData>
                {
                    new RouteData
                    {
                        distanceKm = 1200f,
                        flights = new List<RouteFlight>
                        {
                            new RouteFlight { registration = "B-1001", weeklyFlights = 7 }
                        }
                    }
                };
                gs5.fleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-1001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy },
                    new Aircraft { registration = "B-2001", type = "A330-200F", isActive = true, status = AircraftStatuses.Airworthy, cargoTons = 65f, purchasePrice = 18000f }
                };
                var cm = go5.AddComponent<CargoManager>();
                cm.SettleCargo(gs5);
                Debug.Log($"CargoManager 结算后：货机 {gs5.cargo.freighterCount} 架 · 腹舱月收入 {gs5.cargo.bellyRevenueWanMonthly:F1} 万 · 货机月净 {(gs5.cargo.freighterRevenueWanMonthly - gs5.cargo.freighterCostWanMonthly):F1} 万 · 吨公里 {gs5.cargo.cargoTonKmMonthly:F0} /月");
            }
            finally
            {
                GameObject.DestroyImmediate(go5);
            }

            // —— P2 长线运营：财务细分（GDB 第15章）——
            Debug.Log($"税务 枢纽税率 PEK→{TaxEconomics.CorporateTaxRateForHub("PEK"):P0} DXB→{TaxEconomics.CorporateTaxRateForHub("DXB"):P0} SIN→{TaxEconomics.CorporateTaxRateForHub("SIN"):P0} LHR→{TaxEconomics.CorporateTaxRateForHub("LHR"):P0}");
            Debug.Log($"企业所得税 净利800×25% → {TaxEconomics.CorporateTaxWan(800f, 0.25f):F0} 万（期望 200）；亏损-100 → {TaxEconomics.CorporateTaxWan(-100f, 0.25f):F0} 万（期望 0）");
            Debug.Log($"转移定价风险 激进1.0+合规0 → {TaxEconomics.TransferPricingAuditRisk(1f, 0f):P0}（期望 5%）；合规1.0 → {TaxEconomics.TransferPricingAuditRisk(1f, 1f):P0}（期望 0）");
            Debug.Log($"补贴依赖 净利50-补贴80 → {TaxEconomics.IsSubsidyDependent(50f, 80f)}（True） 净利120-补贴80 → {TaxEconomics.IsSubsidyDependent(120f, 80f)}（False）");

            Debug.Log($"PDP 交付年 窄体→{PdpEconomics.DeliveryYears("A320neo")} 宽体→{PdpEconomics.DeliveryYears("B787-9")} 货机→{PdpEconomics.DeliveryYears("B777F")}（期望 2/3/4）");
            Debug.Log($"PDP 付款 总价10000→定金{PdpEconomics.SigningDepositWan(10000f):F0} +24月{PdpEconomics.Pdp24Wan(10000f):F0} +12月{PdpEconomics.Pdp12Wan(10000f):F0} +尾款{PdpEconomics.DeliveryBalanceWan(10000f):F0} = {PdpEconomics.SigningDepositWan(10000f) + PdpEconomics.Pdp24Wan(10000f) + PdpEconomics.Pdp12Wan(10000f) + PdpEconomics.DeliveryBalanceWan(10000f):F0}（期望 10000）");
            Debug.Log($"PDP 取消违约金 已付15% → {PdpEconomics.CancellationPenaltyRate(0.15f):P0} 已付40% → {PdpEconomics.CancellationPenaltyRate(0.40f):P0}（期望 21.5%/24%）");

            Debug.Log($"保险 年费率 A全险→{InsuranceEconomics.AnnualRate(SafetyRating.A, InsuranceCoverage.Full):P1} B全险→{InsuranceEconomics.AnnualRate(SafetyRating.B, InsuranceCoverage.Full):P1} C基本→{InsuranceEconomics.AnnualRate(SafetyRating.C, InsuranceCoverage.Basic):P1} 自保→{InsuranceEconomics.AnnualRate(SafetyRating.A, InsuranceCoverage.SelfInsured):P1}（期望 1.0%/1.5%/1.2%/0）");
            Debug.Log($"保险 理赔 轻微50%全险→{InsuranceEconomics.ClaimPayoutWan(100f, InsuranceEconomics.LossSeverity.Minor, InsuranceCoverage.Full):F0} 万（期望 50） 重大80%→{InsuranceEconomics.ClaimPayoutWan(100f, InsuranceEconomics.LossSeverity.Major, InsuranceCoverage.Full):F0} 万（期望 80） 自保→{InsuranceEconomics.ClaimPayoutWan(100f, InsuranceEconomics.LossSeverity.Major, InsuranceCoverage.SelfInsured):F0} 万（期望 0）");

            Debug.Log($"燃油预警 低价(850/1000) → {FuelProcurementEconomics.CheckWarnings(850f, 1000f, 30f, 0.2f, false).Count} 条（期望1：低价） 库存2天 → {FuelProcurementEconomics.CheckWarnings(850f, 850f, 2f, 0.2f, false).Count} 条（期望1：紧急）");
            Debug.Log($"燃油紧急溢价 单价850 → {FuelProcurementEconomics.EmergencyPrice(850f):F0}（期望 1147.5）");

            Debug.Log($"碳配额 EEA 判定 LHR→{AirportRegions.IsEea("LHR")} PEK→{AirportRegions.IsEea("PEK")}（期望 True/False）");
            Debug.Log($"碳配额 归类 PEK-LHR→{CarbonEconomics.ClassifyRoute("PEK", "LHR")} LHR-CDG→{CarbonEconomics.ClassifyRoute("LHR", "CDG")} PEK-SHA→{CarbonEconomics.ClassifyRoute("PEK", "SHA")}（期望 Corsia/Ets/None）");
            Debug.Log($"碳配额 免费比例 2024→{CarbonEconomics.EtsFreeQuotaRatio(2024):P0} 2025→{CarbonEconomics.EtsFreeQuotaRatio(2025):P0} 2026→{CarbonEconomics.EtsFreeQuotaRatio(2026):P0}（期望 75%/50%/0%）");
            Debug.Log($"碳配额 CORSIA 强制 2026→{CarbonEconomics.CorsiaMandatory(2026)} 2027→{CarbonEconomics.CorsiaMandatory(2027)}（期望 False/True）");
            Debug.Log($"碳配额 ETS 需购 排放10000吨@2024 → {CarbonEconomics.EtsRequiredTons(10000f, 2024):F0} 吨（期望 2500） @2026 → {CarbonEconomics.EtsRequiredTons(10000f, 2026):F0} 吨（期望 10000）");
            Debug.Log($"碳配额 CORSIA 需购 排放10000吨@2027 → {CarbonEconomics.CorsiaRequiredTons(10000f, 2027):F0} 吨（期望 1500） @2026 → {CarbonEconomics.CorsiaRequiredTons(10000f, 2026):F0} 吨（期望 0）");

            // —— P2 长线运营：非航收入与临空商业（GDB 26.2）——
            Debug.Log($"临空 吞吐解锁档位 400万→{AirportCommerceEconomics.UnlockedTier(400f)} 600万→{AirportCommerceEconomics.UnlockedTier(600f)} 1500万→{AirportCommerceEconomics.UnlockedTier(1500f)} 3500万→{AirportCommerceEconomics.UnlockedTier(3500f)}（期望 0/1/2/3）");
            Debug.Log($"临空 年化ROI 400万→{AirportCommerceEconomics.AnnualRoi(400f):P0} 600万→{AirportCommerceEconomics.AnnualRoi(600f):P0} 1500万→{AirportCommerceEconomics.AnnualRoi(1500f):P0} 3500万→{AirportCommerceEconomics.AnnualRoi(3500f):P0}（期望 6%/8%/10%/12%）");
            Debug.Log($"临空 投资 免税L1/L2/L3→{AirportCommerceEconomics.InvestmentWan(0,1):F0}/{AirportCommerceEconomics.InvestmentWan(0,2):F0}/{AirportCommerceEconomics.InvestmentWan(0,3):F0} 广告L1/L2/L3→{AirportCommerceEconomics.InvestmentWan(1,1):F0}/{AirportCommerceEconomics.InvestmentWan(1,2):F0}/{AirportCommerceEconomics.InvestmentWan(1,3):F0}（期望 300/900/1800 与 150/450/900）");
            Debug.Log($"临空 年收入@吞吐3500万(ROI12%) 免税L3→{AirportCommerceEconomics.FacilityAnnualRevenueWan(0,3,3500f):F0} 广告L3→{AirportCommerceEconomics.FacilityAnnualRevenueWan(1,3,3500f):F0}（期望 216/108，权重 4:2）");
            Debug.Log($"临空 回收期 免税L3@3500万→{AirportCommerceEconomics.PaybackYears(0,3,3500f):F1} 年（期望 8.3）");
            Debug.Log($"临空 吞吐估算 周667班×180座×80% → {AirportCommerceEconomics.HubAnnualThroughputWan(667, 180, 0.8f):F0} 万人次（期望 ~500）");
            Debug.Log($"临空 设施名 {AirportCommerceEconomics.FacilityName(0)}/{AirportCommerceEconomics.FacilityName(1)}/{AirportCommerceEconomics.FacilityName(2)}/{AirportCommerceEconomics.FacilityName(3)}（期望 免税店/广告位/贵宾厅/货运站租赁）");
            Debug.Log($"临空 收入权重 免税→{AirportCommerceEconomics.RevenueWeight(0):P0} 广告→{AirportCommerceEconomics.RevenueWeight(1):P0}（期望 40%/20%）");

            // —— P2 长线运营：铁人模式 / What-If 分支（GDB 第31章）——
            Debug.Log($"铁人 评分系数 → {IronmanEconomics.ScoreMultiplier:P1}（期望 120%） ShouldBoost(true)={IronmanEconomics.ShouldBoost(true)}");
            Debug.Log($"铁人 评分 50→{IronmanEconomics.ApplyIronmanScore(50f):F0} 90→{IronmanEconomics.ApplyIronmanScore(90f):F0} 0→{IronmanEconomics.ApplyIronmanScore(0f):F0}（期望 60/100/0，封顶 100）");

            // —— P3 体验收口：渐进式解锁 + 失误保护（GDB 第28章）——
            Debug.Log($"功能闸门 BypassDeptGate={ProgressionGate.BypassDeptGate}（精简期应为 True → 全部部门解锁）");
            Debug.Log($"功能闸门 解锁财年 OPS={ProgressionGate.UnlockYearForDept("n13_运营部")} MKT={ProgressionGate.UnlockYearForDept("n14_市场销售部")} MNT={ProgressionGate.UnlockYearForDept("n17_机务工程部")}（Bypass 时期望 0/0/0）");
            Debug.Log($"功能闸门 解锁财年 FIN={ProgressionGate.UnlockYearForDept("n15_财务部")} HR={ProgressionGate.UnlockYearForDept("n18_人力资源部")} STG={ProgressionGate.UnlockYearForDept("n16_战略发展部")} SAF={ProgressionGate.UnlockYearForDept("n19_安全质量部")}（Bypass 时期望 0）");
            Debug.Log($"功能闸门 无闸门 主页={ProgressionGate.UnlockYearForDept("n6_主页_Dashboard")} 机队={ProgressionGate.UnlockYearForDept("n9_机队")}（期望 0/0）");
            Debug.Log($"功能闸门 第1财年 FIN 解锁={ProgressionGate.IsDeptUnlocked("n15_财务部", 1)}（Bypass 时期望 True）");
            Debug.Log($"失误保护 净亏损 100→CheckMistake(-100,100,0,60,false)={ProgressionGate.CheckMistake(-100f,100f,0f,60f,false)}（期望 NetLoss）");
            Debug.Log($"失误保护 满意度降 30→{ProgressionGate.CheckMistake(0f,1000f,30f,60f,false)} 下限 20→{ProgressionGate.CheckMistake(0f,1000f,0f,20f,false)} 安全→{ProgressionGate.CheckMistake(0f,1000f,0f,60f,true)}（期望 Drop/Floor/Safety）");
            Debug.Log($"失误保护 无失误→{ProgressionGate.CheckMistake(10f,1000f,5f,60f,false)}（期望 None） 可保护 Y1余3={ProgressionGate.CanProtect(1,3)}（期望 True） Y4余3={ProgressionGate.CanProtect(4,3)}（期望 False）");

            // —— P3 体验收口：见习期软保护（GDB 28.1 特殊保护机制）——
            Debug.Log($"软保护 超支惩罚 Y1={ProgressionGate.OverrunPenalty(1)} Y2={ProgressionGate.OverrunPenalty(2)} Y4={ProgressionGate.OverrunPenalty(4)}（期望 -10/-5/-10）");
            Debug.Log($"软保护 罢工倒计时缩短倍率 Y2={ProgressionGate.StrikeCountdownShrinkScale(2)} Y4={ProgressionGate.StrikeCountdownShrinkScale(4)}（期望 0.5/1）");
            Debug.Log($"软保护 对冲亏损上限 Y2={ProgressionGate.HedgeLossCapRatio(2)} Y4={ProgressionGate.HedgeLossCapRatio(4)}（期望 0.2/0）");
            Debug.Log($"软保护 战略豁免额度 Y1={ProgressionGate.WaiverLimit(1)} Y2={ProgressionGate.WaiverLimit(2)} Y3={ProgressionGate.WaiverLimit(3)} Y4={ProgressionGate.WaiverLimit(4)}（期望 2/2/3/3）");
            Debug.Log($"软保护 局方停飞豁免 Y3={ProgressionGate.SkipRegulatoryTakeover(3)} Y4={ProgressionGate.SkipRegulatoryTakeover(4)}（期望 True/False）");

            // —— P3 体验收口：教程 / 提示 / 消息 / 速度 ——
            Debug.Log($"智能提示 跑道3月/空缺20%/战略40/准点连续3季 → {SmartAlertLogic.EvaluateAll(3f, 0.20f, 40f, 65f, 3).Count} 条（期望 4） 全健康 → {SmartAlertLogic.EvaluateAll(12f, 0.08f, 80f, 90f, 0).Count} 条（期望 0）");
            MessageCenter.Clear();
            MessageCenter.Push(MessagePriority.Info, "n13_运营部", "运营部", "季度结算", "测试", "第1年Q1", 1, 1);
            MessageCenter.Push(MessagePriority.Info, "n13_运营部", "运营部", "季度结算", "测试", "第1年Q1", 1, 1);
            MessageCenter.Push(MessagePriority.Emergency, "n13_运营部", "运营部", "AOG", "停飞", "第1年Q1", 1, 1);
            Debug.Log($"消息中心 聚合后 → {MessageCenter.Count} 条（期望 2：信息×2 聚合为 1） 未读={MessageCenter.UnreadCount} 紧急={MessageCenter.Filter((int)MessagePriority.Emergency, null).Count}");
            MessageCenter.MarkAllRead();
            Debug.Log($"消息中心 已读后未读={MessageCenter.UnreadCount}（期望 0）");
            MessageCenter.Clear();

            // —— P2 无痕：全球航空动态消息流 ——
            Debug.Log($"事件发生器 模板数 → 日报 {EventGenerator.FillerTemplates.Count} 条 + 条件 {EventGenerator.ConditionalTemplates.Count} 条（期望 12 + 12 = 24）");
            var quietCtx = new EventGenerator.Context
            {
                oilPrice = 850f, oilStableQuarters = 0, quietQuarters = 0, profitStreak = 0,
                onTimePercent = 88f, loadFactorPercent = 60f, boardSatisfaction = 60f,
                cash = 5000f, year = 1, quarter = 2, seed = 7
            };
            var quietGen = EventGenerator.Generate(quietCtx, 5, out int quietCond);
            Debug.Log($"事件发生器 平淡季 → 生成 {quietGen.Count} 条 条件命中 {quietCond} 条（期望 1 条日报 + 0 条件）");
            var hotCtx = new EventGenerator.Context
            {
                oilPrice = 850f, oilStableQuarters = 4, quietQuarters = 4, profitStreak = 3,
                onTimePercent = 60f, loadFactorPercent = 45f, boardSatisfaction = 30f,
                cash = -200f, year = 2, quarter = 3, seed = 7
            };
            var hotGen = EventGenerator.Generate(hotCtx, 8, out int hotCond);
            Debug.Log($"事件发生器 压力季 → 生成 {hotGen.Count} 条 条件命中 {hotCond} 条（期望 ≥5：油价/反垄断/流控/纾困/评级等）");
            var merged = MessageMerger.Merge(new List<DynamicFeedItem>(quietGen), new List<DynamicFeedItem>(hotGen), 20);
            Debug.Log($"消息混合器 合并去重后 → {merged.Count} 条（外部 1 + 生成 8，无重复标题应 = 9）");

            // —— P2 后置：文本本地化（GDB 33.7）——
            var zh = Loc.T("LOC_SETTINGS_TITLE");
            Loc.SetLanguage(Language.EnUS);
            var en = Loc.T("LOC_SETTINGS_TITLE");
            Loc.SetLanguage(Language.ZhCN);
            Debug.Log($"本地化 简中「{zh}」/ 英语「{en}」 回退 {Loc.T("LOC_NO_SUCH_KEY")}（期望 设置/存档 · Settings/Save · 原样键名）");
            Debug.Log($"本地化 CSV 注册 {Loc.RegisterCsv("key,zh,en\nTEST_X,测试X,Test X\n")} 条 → T(TEST_X)={Loc.T("TEST_X")}（期望 1 · 测试X）");

            // —— P2 后置：无障碍色盲调色板 / 字体缩放（GDB 29.5）——
            var std = Accessibility.StatusHex();
            Accessibility.SetColorblind(ColorblindMode.Deuteranopia);
            var deut = Accessibility.StatusHex();
            Accessibility.SetColorblind(ColorblindMode.Standard);
            Debug.Log($"无障碍 标准色 {std.danger}/{std.success} ≠ 红绿盲色 {deut.danger}/{deut.success}（期望 危险红→橙 成功绿→蓝）");
            float f1 = Accessibility.FontMultiplier();
            Accessibility.SetFontScale(FontScale.Large);
            float f2 = Accessibility.FontMultiplier();
            Accessibility.SetFontScale(FontScale.XLarge);
            float f3 = Accessibility.FontMultiplier();
            Accessibility.SetFontScale(FontScale.Standard);
            Debug.Log($"无障碍 字体倍率 标准={f1:P0} 大={f2:P0} 特大={f3:P0}（期望 100%/115%/130%）");

            // —— P2 后置：数据遥测（GDB 38）——
            Telemetry.Clear();
            Telemetry.SetOptOut(false);
            Telemetry.Track("game_start", "session", new Dictionary<string, string> { ["mode"] = "0" });
            Telemetry.Track("quarter_advanced", "progress");
            Telemetry.Track("dept_visited", "engagement");
            int beforeOpt = Telemetry.Count;
            Telemetry.SetOptOut(true);
            Telemetry.Track("should_be_ignored", "session");
            bool ignored = Telemetry.Count == beforeOpt;
            Telemetry.SetOptOut(false);
            Debug.Log($"遥测 缓冲 {beforeOpt} 条（期望 3） 退出后静默={ignored}（期望 True） 累计={Telemetry.TotalTracked}（期望 3）");

            // —— P2 后置：Mod 数据覆盖 / 加载顺序 / 冲突（GDB 第36章）——
            ModRegistry.Clear();
            ModRegistry.Register(new ModManifest { id = "com.a.fuel", name = "Cheap Fuel", type = "data", data = new List<ModDataEntry> { new ModDataEntry { key = "fuel_price_base", value = "425" } } });
            ModRegistry.Register(new ModManifest { id = "com.b.dlc", name = "Official Balance", type = "dlc", data = new List<ModDataEntry> { new ModDataEntry { key = "fuel_price_base", value = "500" }, new ModDataEntry { key = "initial_cash_wan", value = "1000" } } });
            ModRegistry.Rebuild();
            Debug.Log($"Mod 加载 {ModRegistry.Count} 个 → fuel_price_base={ModRegistry.GetFloat("fuel_price_base", -1):F0}（期望 425，社区覆盖 DLC） initial_cash_wan={ModRegistry.GetFloat("initial_cash_wan", -1):F0}（期望 1000） 冲突={ModRegistry.Conflicts.Count}（期望 1）");
            Debug.Log($"Mod 缺省回退 missing={ModRegistry.GetFloat("missing_key", 77f):F0}（期望 77）");
            ModRegistry.Clear();

            Debug.Log($"LiveClock 08:00={LiveClockLogic.ParseMinutes("08:00")} 跨夜轮挡={LiveClockLogic.BlockMinutes("22:00", "02:00", 800)}分 每日周三={LiveClockLogic.OperatesOn("每日", new DateTime(2026, 9, 9), 7)} 二四六周三={LiveClockLogic.OperatesOn("二四六", new DateTime(2026, 9, 9), 3)}");
            Debug.Log($"LiveClock 日固定成本 90/30={LiveClockLogic.DailyFixedCostWan(90f, 0f, 0f, 0f, 0f):F0}（期望 3）");

            // —— 外部事件接收与游戏时间映射（现实提供内容，游戏提供时间轴）——
            var recv = new DateTime(2030, 5, 10, 15, 0, 0, DateTimeKind.Utc);
            var ndrc = ExternalDataFetcher.DemoPayloads()[0];
            var mapped = ExternalTimeMapper.Map(ndrc, recv);
            Debug.Log($"外事 调价映射 偏移={mapped.offsetHours:F1}h 生效={ExternalTimeMapper.GameDateTimeLabel(mapped.effective)} 措辞={mapped.relativeDesc}"
                + $"（期望 {ExternalTimeMapper.HoursUntilNextMidnight(recv):F1}h（次日零时）/ 2030年05月11日 00:00 / 明日零时起）");
            Debug.Log($"外事 隐去现实日期 正文={mapped.content}");
            Debug.Log($"外事 隐去现实日期 含2026={mapped.content.Contains("2026")} 含9月12={mapped.content.Contains("9月12")}（期望 False/False）"
                + $" 含相对={mapped.content.Contains(mapped.relativeDesc)} 无叠字={!mapped.content.Contains("零时零时")}（期望 True）");

            var typhoon = ExternalDataFetcher.DemoPayloads()[1];
            var m3 = ExternalTimeMapper.Map(typhoon, recv);
            Debug.Log($"外事 台风映射 偏移={m3.offsetHours:F0}h 措辞={m3.relativeDesc}（期望 72 / 3天后）");

            float nowOff = ExternalTimeMapper.ExtractOffsetHours("本通知即刻生效。", null, null, recv, false);
            float noneOff = ExternalTimeMapper.ExtractOffsetHours("国际油价波动。", null, null, recv, false);
            float expOff = ExternalTimeMapper.ExtractOffsetHours("昨日已上调。", recv, recv.AddHours(-5), recv, true);
            Debug.Log($"外事 即刻={nowOff:F0} 无时间={noneOff:F1} 已过期={expOff:F0}（期望 0 / {ExternalTimeMapper.HoursUntilNextMidnight(recv):F1} / 0）");

            // —— 来文体例（非航司机构：政府公文 / 通讯社消息 / 企业公告 / 机构提示）——
            var evGov = new ExternalEvent
            {
                source = "民航局", category = "监管", title = "民航局发布新机型适航指令，检修工时增加",
                content = "民航局发布适航指令，要求部分机型加装检查项目，机务排班工时相应上升，短期准点率承压。",
                relativeDesc = "明日零时起"
            };
            string bGov = OfficialBulletin.Compose(evGov, "2030年5月10日", false);
            Debug.Log($"来文体例 公文 标题式={bGov.StartsWith("关于")} 文种={OfficialBulletin.DocType("民航局", "监管")}（期望 通知）"
                + $" 逗号不入标题={bGov.StartsWith("关于发布新机型适航指令的通知")} 含特此通知={bGov.Contains("特此通知")}"
                + $" 无贵司/无尊敬的={!bGov.Contains("贵司") && !bGov.Contains("尊敬的")}\n{bGov}");

            var evNews = new ExternalEvent
            {
                source = "路透社", category = "市场", title = "OPEC+ 内部就减产配额现分歧，市场预期供应收紧",
                content = "据路透社报道，OPEC+ 成员国就下阶段减产配额产生分歧，交易商普遍押注供应收紧。",
                relativeDesc = "明日零时起"
            };
            string bNews = OfficialBulletin.Compose(evNews, "2030年5月10日", false);
            Debug.Log($"来文体例 消息 抬头={bNews.StartsWith("【路透社】")} 电头={bNews.Contains("电）")}"
                + $" 体例=消息={OfficialBulletin.StyleOf("路透社") == OfficialBulletin.Style.News} 角色={OfficialBulletin.RoleLabel("路透社", "")}（期望 消息）");

            var evAdv = new ExternalEvent
            {
                source = "标普全球", category = "市场", title = "评级机构下调信用展望",
                content = "标普全球将相关航司信用展望由「稳定」下调为「负面」，融资成本可能抬升。",
                relativeDesc = "明日零时起"
            };
            string bAdv = OfficialBulletin.Compose(evAdv, "2030年5月10日", false);
            Debug.Log($"来文体例 提示 抬头={bAdv.StartsWith("标普全球：")} 含请予关注={bAdv.Contains("请予关注")}"
                + $" 角色={OfficialBulletin.RoleLabel("标普全球", "")}（期望 提示）");

            var evCorp = new ExternalEvent
            {
                source = "波音公司", category = "行业", title = "波音宣布提升 787 产能以满足复苏需求",
                content = "波音公司宣布将 787 梦想客机月产能提升至 5 架，以应对长途航线机队更新潮。",
                relativeDesc = "3天后"
            };
            string bCorp = OfficialBulletin.Compose(evCorp, "2030年5月10日", false);
            Debug.Log($"来文体例 公告 抬头={bCorp.StartsWith("波音公司关于提升 787 产能以满足复苏需求的公告")} 含本公告自3天后起生效={bCorp.Contains("本公告自3天后起生效")}");

            var goX = new GameObject("__external_event_test_gs__");
            try
            {
                var gsX = goX.AddComponent<GameState>();
                gsX.year = 5; gsX.quarter = 2;
                gsX.inbox = new List<InboxMail>();
                gsX.dynamicFeed = new List<DynamicFeedItem>();
                gsX.externalEvents = new List<ExternalEvent>();
                gsX.fuelPricePerTon = 850f;
                gsX.SetSimUtc(recv);
                var ev1 = ExternalEventManager.Receive(gsX, ndrc);
                var ev1b = ExternalEventManager.Receive(gsX, ndrc);
                Debug.Log($"外事 去重 队列={gsX.externalEvents.Count}（期望 1） 预告邮件={gsX.inbox.Exists(m => m != null && m.tag == "预告")}");
                float fuelBefore = gsX.fuelPricePerTon;
                int fired0 = ExternalEventManager.Tick(gsX, recv, recv);
                Debug.Log($"外事 接收瞬间不生效 fired={fired0} 油价={gsX.fuelPricePerTon:F0}（期望 0 / {fuelBefore:F0}）");
                int fired1 = ExternalEventManager.Tick(gsX, recv, ev1.GameEffectiveTime);
                Debug.Log($"外事 次日零时触发 fired={fired1} 已触发={ev1.isTriggered} 油价={gsX.fuelPricePerTon:F0}（期望 1 / True / {Mathf.Max(400f, fuelBefore + 26f):F0}）");
                Debug.Log($"外事 AO简报 空待办时={string.IsNullOrEmpty(ExternalEventManager.GetAdvisorBriefing(gsX)) == false} 跑马灯非空={!string.IsNullOrEmpty(ExternalEventManager.TickerText(gsX))}");

                gsX.SetSimUtc(recv);
                var ev2 = ExternalEventManager.Receive(gsX, typhoon);
                string brief = ExternalEventManager.GetAdvisorBriefing(gsX);
                Debug.Log($"外事 AO预告 含3天={brief.Contains("3")} 含SWA={brief.Contains("SWA")} 简报={brief}");
                ExternalEventManager.Tick(gsX, recv, ev2.GameEffectiveTime);
                Debug.Log($"外事 台风生效 冻结SWA={ExternalEventManager.IsAirportFrozen(gsX, "SWA")} 不冻PEK={ExternalEventManager.IsAirportFrozen(gsX, "PEK")}（期望 True / False）");

                var mN = gsX.inbox.Find(m => m != null && m.id != null && m.id.StartsWith("ext_r_ndrc_fuel"));
                var mT = gsX.inbox.Find(m => m != null && m.id != null && m.id.StartsWith("ext_r_typhoon"));
                Debug.Log($"外事 公文口吻 立项={mN != null} 含尊敬的={mN != null && mN.body.Contains("尊敬的")}（期望 False）"
                    + $" 含特此通知={mN != null && mN.body.Contains("特此通知")} 落款含游戏日期={mN != null && mN.body.Contains("2030年5月10日")}");
                Debug.Log($"外事 预告文种 {mT?.role}（期望 预警通报） 分类={InboxManager.KindLabel(mT?.kind)}（期望 来文）"
                    + $" 含特此通报={mT != null && mT.body.Contains("特此通报")} 标题式={mT != null && mT.body.StartsWith("关于")}");
                var mTb = gsX.inbox.Find(m => m != null && m.id != null && m.id.StartsWith("ext_t_typhoon"));
                Debug.Log($"外事 施行通报 {(mTb != null ? "有" : "无")} 文种={mTb?.role}（期望 施行通报） 含已自施行={mTb != null && mTb.body.Contains("已自")}"
                    + $" 影响行={mTb != null && mTb.body.Contains("起降受限")}");
            }
            finally
            {
                GameObject.DestroyImmediate(goX);
            }

            // —— Phase 19：外部数据源接入（现实抓取 → 内容清洗 → 映射到游戏轴 → 入队）——
            var demoAll = ExternalDataFetcher.DemoPayloads();
            string jsonContract = "{ \"events\": [ {"
                + "\"id\":\"t_fuel\",\"category\":\"市场\",\"title\":\"发改委调整成品油价格\","
                + "\"content\":\"国家发展改革委决定，自2026年9月12日零时起，国内汽、柴油价格每吨分别上调 260 元。\","
                + "\"source\":\"发改委\",\"publishedAt\":\"2026-09-11T15:00:00Z\","
                + "\"effectType\":\"fuelAbs\",\"effectValue\":26 } ] }";
            var pJson = ExternalDataFetcher.ParseText(jsonContract, "https://example.com/events.json");
            var mJson = pJson.Count > 0 ? ExternalTimeMapper.Map(pJson[0], recv) : default;
            Debug.Log($"外源 JSON 解析 条数={pJson.Count}（期望 1） 来源={pJson[0].source}（期望 发改委）"
                + $" 生效类型={pJson[0].effectType} 数值={pJson[0].effectValue:F0}（期望 fuelAbs / 26）");
            Debug.Log($"外源 JSON 映射 偏移={mJson.offsetHours:F1}h（期望 {ExternalTimeMapper.HoursUntilNextMidnight(recv):F1}）"
                + $" 措辞={mJson.relativeDesc}（期望 明日零时起） 隐去现实日期={!mJson.content.Contains("2026") && !mJson.content.Contains("9月12")}");

            string rssSample = "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>\"航空\" - 新闻</title>"
                + "<item><title>9月12日全国航空天气预报 - 天气网</title><link>https://example.com/a1</link>"
                + "<pubDate>Sat, 12 Sep 2026 00:00:00 GMT</pubDate>"
                + "<source url=\"https://www.weather.com.cn\">天气网</source>"
                + "<description>&lt;a href=\"x\"&gt;9月12日全国航空天气预报&lt;/a&gt;&nbsp;&nbsp;&lt;font&gt;天气网&lt;/font&gt;</description></item>"
                + "<item><title>某航司公布季报 - 证券之星</title><link>https://example.com/a2</link>"
                + "<pubDate>Sat, 12 Sep 2026 01:00:00 GMT</pubDate><description>主力资金净卖出 1 亿元，股价承压</description></item>"
                + "</channel></rss>";
            var pRss = ExternalDataFetcher.ParseText(rssSample, "https://news.example.com/rss");
            var mRss = pRss.Count > 0 ? ExternalTimeMapper.Map(pRss[0], recv) : default;
            Debug.Log($"外源 RSS 解析 条数={pRss.Count}（期望 1：股市噪声被过滤） 来源={pRss[0].source}（期望 天气网）"
                + $" 去媒体名={!pRss[0].title.Contains("天气网")} 正文与标题同源时留空={string.IsNullOrEmpty(pRss[0].content)}");
            Debug.Log($"外源 RSS 去现实日期 标题={mRss.title}（期望 今日全国航空天气预报）"
                + $" 未被硬塞即刻={!mRss.title.Contains("即刻")} 无日期残留={!mRss.title.Contains("9月12")}");

            ExternalDataFetcher.Reset();
            ExternalDataFetcher.Inject(new List<ExternalEventPayload> { demoAll[0], demoAll[1] });
            int drained = ExternalDataFetcher.FetchEvents().Count;
            ExternalDataFetcher.Inject(new List<ExternalEventPayload> { demoAll[0], demoAll[1] });
            int drainedAgain = ExternalDataFetcher.FetchEvents().Count;
            Debug.Log($"外源 缓存与去重 首次排空={drained}（期望 1~2） 重复投递再排空={drainedAgain}（期望 0）");

            // —— 相关性过滤：用例取自真实订阅源的实际标题（2026-09-12 抓取）——
            // 旧口径 100 条进 96 条（白名单含「油价」，财经与商品新闻整批放行），此处锁死收紧后的行为。
            // j9/j12/j13 是子串误伤与繁体的回归用例 —— 「中韩定期货运航线」含子串「期货」、「全市值勤」含「市值」。
            string realJunk = "{ \"events\": ["
                + "{\"id\":\"j1\",\"title\":\"国内油价大跌，95号汽油逼近9元时代\",\"source\":\"财经网\"},"
                + "{\"id\":\"j2\",\"title\":\"油价下挫 美股指500涨超12%\",\"source\":\"财经网\"},"
                + "{\"id\":\"j3\",\"title\":\"9月12日隔夜要闻：美股指走弱 油价回落\",\"source\":\"财经网\"},"
                + "{\"id\":\"j4\",\"title\":\"国际油价突破100美元 中国原油进口量升幅收窄\",\"source\":\"财经网\"},"
                + "{\"id\":\"j5\",\"title\":\"某航司公布季报，主力资金净卖出 1 亿元\",\"source\":\"证券之星\"},"
                + "{\"id\":\"j6\",\"title\":\"上海热带低压发展为第25号台风梅花 华南多地航班调整\",\"source\":\"气象台\"},"
                + "{\"id\":\"j7\",\"title\":\"民航局发布新机型适航指令\",\"source\":\"民航局\"},"
                + "{\"id\":\"j8\",\"title\":\"航油价格上调，航空公司成本承压\",\"source\":\"民航资源网\"},"
                + "{\"id\":\"j9\",\"title\":\"青岛机场中韩定期货运航线增至每周10班以上\",\"source\":\"中国民航网\"},"
                + "{\"id\":\"j10\",\"title\":\"原油期货主力合约走高，市场情绪转暖\",\"source\":\"财经网\"},"
                + "{\"id\":\"j11\",\"title\":\"深化防消联勤 守护空港安全\",\"source\":\"新浪财经\"},"
                + "{\"id\":\"j12\",\"title\":\"9.11恐襲近25年後 美機場逐步取消旅行限制\",\"source\":\"大纪元\"},"
                + "{\"id\":\"j13\",\"title\":\"全市值勤民警加强巡查，保障秩序\",\"source\":\"地方网\"},"
                + "{\"id\":\"j14\",\"title\":\"红海局势推高EC 集运红海、中东航线价格待拐点\",\"source\":\"东方财富\"},"
                + "{\"id\":\"j15\",\"title\":\"累计赔付3.35亿元，江西扎实推进台风、强降雨等自然灾害理赔\",\"source\":\"凤凰网\"},"
                + "{\"id\":\"j16\",\"title\":\"深航新开深圳至悉尼直飞航线 加密墨尔本航班\",\"source\":\"新浪财经\"}"
                + "] }";
            var keptReal = ExternalDataFetcher.ParseText(realJunk, "https://news.example.com/rss");
            var keptIds = new List<string>();
            for (int i = 0; i < keptReal.Count; i++) keptIds.Add(keptReal[i].id);
            Debug.Log($"外源 过滤 真实标题 保留={keptReal.Count}（期望 7：j6 台风/j7 适航/j8 航油/j9 定期货运/j11 空港/j12 繁体機場/j16 航线）"
                + $" 保留集=[{string.Join(",", keptIds)}]");
            Debug.Log($"外源 过滤 财经油价全滤掉={!keptIds.Contains("j1") && !keptIds.Contains("j2") && !keptIds.Contains("j3") && !keptIds.Contains("j4")}（期望 True）"
                + $" 股市噪声优先于航司词={!keptIds.Contains("j5")}（期望 True：季报含「证券/主力资金」）");
            Debug.Log($"外源 过滤 子串不误伤 定期货运含「期货」仍在={keptIds.Contains("j9")}（期望 True）"
                + $" 真期货仍被拦={!keptIds.Contains("j10")}（期望 True）"
                + $" 全市值勤不算市值={!keptIds.Contains("j13")}（期望 True）");
            Debug.Log($"外源 过滤 覆盖面 空港={keptIds.Contains("j11")} 繁体機場={keptIds.Contains("j12")}（期望均 True）");
            Debug.Log($"外源 过滤 跨行业假朋友 海运航线被拦={!keptIds.Contains("j14")}（期望 True）"
                + $" 保险理赔被拦={!keptIds.Contains("j15")}（期望 True）"
                + $" 真航线仍留={keptIds.Contains("j16")}（期望 True）");

            // 自带数值影响的载荷不受过滤影响（即便标题像财经新闻）
            string effectJson = "{ \"events\": [ { \"id\":\"e1\", \"title\":\"油价调整\","
                + "\"source\":\"发改委\", \"effectType\":\"fuelAbs\", \"effectValue\":26 } ] }";
            var keptEffect = ExternalDataFetcher.ParseText(effectJson, "https://example.com/events.json");
            Debug.Log($"外源 过滤 带数值影响不被拦={keptEffect.Count == 1}（期望 True，effectType 优先）");
            ExternalDataFetcher.Reset();

            var goSrc = new GameObject("__external_source_test_gs__");
            try
            {
                var gsSrc = goSrc.AddComponent<GameState>();
                gsSrc.year = 5; gsSrc.quarter = 2;
                gsSrc.inbox = new List<InboxMail>();
                gsSrc.dynamicFeed = new List<DynamicFeedItem>();
                gsSrc.externalEvents = new List<ExternalEvent>();
                gsSrc.SetSimUtc(recv);

                ExternalDataFetcher.Inject(new List<ExternalEventPayload> { demoAll[0], demoAll[1], demoAll[2] });
                int queued = ExternalEventManager.DrainSource(gsSrc);
                Debug.Log($"外源 入队 条数={queued} 队列={gsSrc.externalEvents.Count}（期望相等且 ≥1）"
                    + $" 收件箱={gsSrc.inbox.Count}（期望 ≥{queued}） 只读告知类={gsSrc.inbox.TrueForAll(m => m == null || !InboxManager.RequiresDecision(m))}（期望 True）");
                Debug.Log($"外源 入队后可触发 待生效={ExternalEventManager.Pending(gsSrc).Count}（期望 {queued}）");
            }
            finally
            {
                GameObject.DestroyImmediate(goSrc);
                ExternalDataFetcher.Reset();
            }

            // —— 游戏内「外部信息源」面板（n115）：入口在设置内，配置与编辑器菜单同一份 ——
            Debug.Log($"外源 面板 页id={AirlineCEO.UI.Shell.ExternalSourcePanelUI.PageKey}"
                + $" 标题=「{Loc.T("LOC_EXT_TITLE")}」 已本地化={Loc.T("LOC_EXT_TITLE") != "LOC_EXT_TITLE"}"
                + $" 设置行摘要=「{AirlineCEO.UI.Shell.ExternalSourcePanelUI.Summary()}」"
                + $" 摘要非键名={!AirlineCEO.UI.Shell.ExternalSourcePanelUI.Summary().StartsWith("LOC_")}");

            // —— 行情源（n116）：现实报价 → 换算航油 → 每季最多靠拢 ±8% ——
            Debug.Log($"行情 面板 页id={AirlineCEO.UI.Shell.QuoteSourcePanelUI.PageKey}"
                + $" 标题=「{Loc.T("LOC_QUOTE_TITLE")}」 已本地化={Loc.T("LOC_QUOTE_TITLE") != "LOC_QUOTE_TITLE"}"
                + $" 摘要（当前状态）=「{AirlineCEO.UI.Shell.QuoteSourcePanelUI.Summary()}」"
                + $" 开关按钮文案已本地化={Loc.T("LOC_QUOTE_OPEN_ON") != "LOC_QUOTE_OPEN_ON" && Loc.T("LOC_QUOTE_OPEN_OFF") != "LOC_QUOTE_OPEN_OFF"}（期望 True）");

            bool quoteWasEnabled = ExternalDataFetcher.QuoteEnabled;
            float quoteWasCrack = ExternalDataFetcher.CrackSpreadUsdPerTon;
            try
            {
                ExternalDataFetcher.CrackSpreadUsdPerTon = ExternalDataFetcher.DefaultCrackSpreadUsdPerTon;

                // 三种契约都能解析；裂解价差 150 时 80 美元/桶 → 80×7.82+150 = 775.6
                string chartJson = "{\"chart\":{\"result\":[{\"meta\":{\"symbol\":\"BZ=F\","
                    + "\"currency\":\"USD\",\"regularMarketPrice\":80.0}}]}}";
                bool okChart = ExternalDataFetcher.ParseQuote(chartJson, out float qChart, out string lChart, out string nChart);
                bool okFlat = ExternalDataFetcher.ParseQuote("{\"price\":80,\"unit\":\"bbl\",\"symbol\":\"布伦特\"}", out float qFlat, out string lFlat, out _);
                bool okTon = ExternalDataFetcher.ParseQuote("{\"value\":820,\"unit\":\"ton\"}", out float qTon, out _, out _);
                bool okPlain = ExternalDataFetcher.ParseQuote("80.0", out float qPlain, out _, out _);
                Debug.Log($"行情 解析 图表={okChart}/{qChart:F0}（期望 True/776） 扁平={okFlat}/{qFlat:F0}（期望 True/776）"
                    + $" 吨价直取={okTon}/{qTon:F0}（期望 True/820） 纯数字={okPlain}/{qPlain:F0}（期望 True/776）"
                    + $" 标签={lChart}/{lFlat}（期望 BZ=F / 布伦特） 口径=「{nChart}」");
                Debug.Log($"行情 解析 异常输入被拒 空串={!ExternalDataFetcher.ParseQuote("", out _, out _, out _)}"
                    + $" 非JSON={!ExternalDataFetcher.ParseQuote("<html>404</html>", out _, out _, out _)}"
                    + $" 零值={!ExternalDataFetcher.ParseQuote("{\"price\":0}", out _, out _, out _)}（期望全 True）");

                // 收敛：开启 + 注入 776 → 850 每季最多降 8%（850×0.08=68）→ 782
                ExternalDataFetcher.QuoteEnabled = true;
                ExternalDataFetcher.InjectOilQuote(776f, "自测·布伦特80");
                bool hasQuote = ExternalDataFetcher.TryGetOilQuote(out float qCached, out string qLabel);
                Debug.Log($"行情 缓存 可读={hasQuote} 报价={qCached:F0}（期望 True/776） 标签={qLabel}");

                // 共享状态行：各页面（n115 / 财经 / 宏观 / 运营）都用它，必须永远非空且能换算出桶价
                string lineOn = ExternalDataFetcher.QuoteStatusLine();
                string lineCompact = ExternalDataFetcher.QuoteStatusLine(compact: true);
                Debug.Log($"行情 状态行 非空={!string.IsNullOrEmpty(lineOn) && !string.IsNullOrEmpty(lineCompact)}（期望 True）"
                    + $" 含桶价={lineOn.Contains("80.0")}（期望 True，776 反算 ≈80.05）"
                    + $" 非键名={!lineOn.StartsWith("LOC_")}\n　长：{lineOn}\n　短：{lineCompact}");

                var goQ = new GameObject("__quote_test_gs__");
                try
                {
                    var gsQ = goQ.AddComponent<GameState>();
                    gsQ.year = 1; gsQ.quarter = 1; gsQ.inbox = new List<InboxMail>();
                    gsQ.fuelPricePerTon = 850f;
                    string noteQ1 = MacroEconomyManager.ConvergeToRealQuote(gsQ);
                    Debug.Log($"行情 收敛第1季 850→{gsQ.fuelPricePerTon:F0}（期望 782，即单季 −68）"
                        + $" 单季不超 8%={Mathf.Abs(gsQ.fuelPricePerTon - 850f) <= 850f * 0.08f + 0.01f}（期望 True）\n{noteQ1}");

                    ExternalDataFetcher.InjectOilQuote(1600f, "自测·极端高价");
                    string noteQ2 = MacroEconomyManager.ConvergeToRealQuote(gsQ);
                    float step = gsQ.fuelPricePerTon - 782f;
                    Debug.Log($"行情 收敛第2季 782→{gsQ.fuelPricePerTon:F0}（期望 845，即单季 +62.6 上限）"
                        + $" 未跳到现实价={gsQ.fuelPricePerTon < 1600f}（期望 True） 幅度≈8%={Mathf.Abs(step - 782f * 0.08f) < 0.05f}（期望 True）");
                    Debug.Log($"行情 收敛 上限封顶 报价夹到[400,1600]={ExternalDataFetcher.CachedOilPricePerTon <= 1600f}（期望 True）");
                    string forecast = MacroEconomyManager.QuoteConvergeForecast(gsQ);
                    Debug.Log($"行情 本季预告 非空={!string.IsNullOrEmpty(forecast)}（期望 True）"
                        + $" 未改状态={Mathf.Abs(gsQ.fuelPricePerTon - 845f) < 1f}（期望 True，预告只读）"
                        + $"\n　本季收敛后 {forecast}");

                    // 行情收敛发函：进待发队列，去重，无需批复（与「航油价格波动」通报是两封不同的信）
                    gsQ.pendingInbox = new List<PendingInboxMail>();
                    InboxManager.NotifyFuelQuoteConverge(gsQ, noteQ2);
                    InboxManager.NotifyFuelQuoteConverge(gsQ, noteQ2);   // 同季重复调用应被去重
                    var quoteMail = gsQ.pendingInbox.Count > 0 ? gsQ.pendingInbox[0].mail : null;
                    Debug.Log($"行情 发函 队列={gsQ.pendingInbox.Count}（期望 1，同季去重）"
                        + $" 无需批复={quoteMail != null && !InboxManager.RequiresDecision(quoteMail)}（期望 True）"
                        + $" 角色=「{quoteMail?.role}」标签=「{quoteMail?.tag}」"
                        + $" 正文含收敛后价={(quoteMail?.body ?? "").Contains(gsQ.fuelPricePerTon.ToString("F0"))}（期望 True）");
                    InboxManager.NotifyFuelQuoteConverge(gsQ, "");   // 空文案不发
                    Debug.Log($"行情 发函 空文案不发 队列={gsQ.pendingInbox.Count}（期望仍为 1）");

                    // 关掉行情：缓存作废，收敛不再发生（回到纯游戏引擎驱动）
                    ExternalDataFetcher.QuoteEnabled = false;
                    float beforeOff = gsQ.fuelPricePerTon;
                    bool hasAfterOff = ExternalDataFetcher.TryGetOilQuote(out _, out _);
                    string noteOff = MacroEconomyManager.ConvergeToRealQuote(gsQ);
                    Debug.Log($"行情 关闭后 缓存可读={hasAfterOff}（期望 False） 收敛说明为空={string.IsNullOrEmpty(noteOff)}（期望 True）"
                        + $" 油价未动={Mathf.Abs(gsQ.fuelPricePerTon - beforeOff) < 0.01f}（期望 True）");
                    Debug.Log($"行情 关闭后 状态行说未开启={ExternalDataFetcher.QuoteStatusLine().Contains("未开启")}（期望 True）"
                        + $" 本季预告为空={string.IsNullOrEmpty(MacroEconomyManager.QuoteConvergeForecast(gsQ))}（期望 True）"
                        + $"\n　{ExternalDataFetcher.QuoteStatusLine()}");
                }
                finally
                {
                    GameObject.DestroyImmediate(goQ);
                }
            }
            finally
            {
                ExternalDataFetcher.QuoteEnabled = quoteWasEnabled;
                ExternalDataFetcher.CrackSpreadUsdPerTon = quoteWasCrack;
                ExternalDataFetcher.Reset();
            }

            // —— Phase 20：汇率（净美元头寸重估 → 汇兑损益）——
            var goFx = new GameObject("__fx_test_gs__");
            try
            {
                var gsF = goFx.AddComponent<GameState>();
                gsF.year = 1; gsF.quarter = 2;
                gsF.cash = 1000f;
                gsF.monthlyBurnRate = 80f;
                gsF.usdCny = 7.00f;
                gsF.usdCnyHistory = new List<float>();
                gsF.activeLoans = new List<AirlineCEO.Data.Loan>();
                gsF.activeRoutes = new List<AirlineCEO.Data.RouteData>();
                gsF.fleet = new List<AirlineCEO.Data.Aircraft>();
                gsF.pdpPayableWan = 2000f;

                // 美元负债 2000 万 + 无国际航线收入 → 净头寸 −2000（典型国内航司）
                float exp0 = AirlineCEO.Logic.FxEconomics.NetUsdExposureWan(gsF);
                Debug.Log($"汇率 净头寸 无航线={exp0:F0}（期望 -2000） 美元负债={AirlineCEO.Logic.FxEconomics.UsdDebtWan(gsF):F0}（期望 2000）"
                    + $" 美元收入={AirlineCEO.Logic.FxEconomics.UsdIncomeWan(gsF):F0}（期望 0）");

                // 国际航线收入对冲一部分敞口：JFK 属国际，日利润 10 万 → 季度 900 万
                gsF.activeRoutes.Add(new AirlineCEO.Data.RouteData
                {
                    origin = "PEK", destination = "JFK", dailyProfit = 10f
                });
                gsF.activeRoutes.Add(new AirlineCEO.Data.RouteData
                {
                    origin = "PEK", destination = "PVG", dailyProfit = 10f   // 国内，不计入美元收入
                });
                Debug.Log($"汇率 国际航线抵减 美元收入={AirlineCEO.Logic.FxEconomics.UsdIncomeWan(gsF):F0}（期望 900，仅 JFK）"
                    + $" 净头寸={AirlineCEO.Logic.FxEconomics.NetUsdExposureWan(gsF):F0}（期望 -1100）");

                // 人民币贬值 7.00→7.20：净空头 → 汇兑损失
                float cashBefore = gsF.cash;
                string noteDep = AirlineCEO.Logic.FxEconomics.ApplyRate(gsF, 7.20f, 7.00f);
                float pnlDep = gsF.cash - cashBefore;
                float expectDep = -1100f * (0.20f / 7.20f);
                Debug.Log($"汇率 贬值受损 现金变动={pnlDep:F1}（期望 {expectDep:F1}，即 -30.6） 为损失={pnlDep < 0f}（期望 True）"
                    + $" 汇率已写回={gsF.usdCny:F2}（期望 7.20） 累计汇兑={gsF.cumulativeFxPnlWan:F1}（期望同现金变动）"
                    + $"\n{noteDep}");

                // 人民币升值 7.20→7.00：同一头寸转为收益（方向必须相反）
                float cashBefore2 = gsF.cash;
                AirlineCEO.Logic.FxEconomics.ApplyRate(gsF, 7.00f, 7.20f);
                float pnlApp = gsF.cash - cashBefore2;
                Debug.Log($"汇率 升值受益 现金变动={pnlApp:F1}（期望 +{(1100f * (0.20f / 7.00f)):F1}） 为收益={pnlApp > 0f}（期望 True）"
                    + $" 与贬值方向相反={pnlDep * pnlApp < 0f}（期望 True）");

                // 完全对冲：损益归零，但仍要付点数成本
                gsF.fxHedgeRatio = 0.8f;
                float cashBefore3 = gsF.cash;
                AirlineCEO.Logic.FxEconomics.ApplyRate(gsF, 7.20f, 7.00f);
                float pnlHedged = gsF.cash - cashBefore3;
                float costOnly = -1100f * 0.8f * 0.0025f;
                float expectedHedged = -1100f * (0.20f / 7.20f) * 0.2f + costOnly;
                Debug.Log($"汇率 对冲后 现金变动={pnlHedged:F1}（期望 {expectedHedged:F1}）"
                    + $" 损失被压缩={Mathf.Abs(pnlHedged) < Mathf.Abs(pnlDep)}（期望 True）"
                    + $" 仍小于未对冲=|{pnlHedged:F1}|<|{pnlDep:F1}| 含对冲成本={pnlHedged < 0f}（期望 True）");

                // 无对冲时应等于纯敞口损益；有对冲时必须严格更小
                gsF.fxHedgeRatio = 0f;
                float pnlNoHedge = AirlineCEO.Logic.FxEconomics.EstimatePnlWan(gsF, 0.20f / 7.20f);
                Debug.Log($"汇率 预估口径 无对冲预估={pnlNoHedge:F1}（期望 {expectDep:F1}） 与结算一致={Mathf.Abs(pnlNoHedge - expectDep) < 0.05f}（期望 True）");

                // 漂移必须留在 [5.50, 9.50]，且历史被记录
                gsF.economicPhase = "recession";
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 60; i++)
                {
                    gsF.quarter = (i % 4) + 1;
                    AirlineCEO.Logic.FxEconomics.SettleAndDrift(gsF);
                    lo = Mathf.Min(lo, gsF.usdCny);
                    hi = Mathf.Max(hi, gsF.usdCny);
                }
                Debug.Log($"汇率 漂移区间 60 季 min={lo:F2} max={hi:F2}（期望落在 [5.50, 9.50]）"
                    + $" 未越界={lo >= AirlineCEO.Logic.FxEconomics.MinUsdCny - 0.001f && hi <= AirlineCEO.Logic.FxEconomics.MaxUsdCny + 0.001f}（期望 True）"
                    + $" 历史封顶={gsF.usdCnyHistory.Count <= 12}（期望 True，实际 {gsF.usdCnyHistory.Count}）");
                Debug.Log($"汇率 摘要=「{AirlineCEO.Logic.FxEconomics.Summary(gsF)}」 非空={!string.IsNullOrEmpty(AirlineCEO.Logic.FxEconomics.Summary(gsF))}（期望 True）");

                // 汇率越界（含 0）必须被 RateOf 拉回区间，不允许出现 0 汇率
                gsF.usdCny = 0f;
                Debug.Log($"汇率 越界保护 0.00→{AirlineCEO.Logic.FxEconomics.RateOf(gsF):F2}（期望 {AirlineCEO.Logic.FxEconomics.DefaultUsdCny:F2}）"
                    + $" 非零={AirlineCEO.Logic.FxEconomics.RateOf(gsF) > 0f}（期望 True）");
            }
            finally
            {
                GameObject.DestroyImmediate(goFx);
            }

            // —— Phase 18：航线准入（CEO 批准 ≠ 可以飞：许可 / 航权 / 时刻 / 公示）——
            var goP = new GameObject("__permit_test_gs__");
            try
            {
                var gsP = goP.AddComponent<GameState>();
                gsP.year = 1; gsP.quarter = 1;
                gsP.inbox = new List<InboxMail>();
                gsP.activeRoutes = new List<RouteData>();
                gsP.fleet = new List<Aircraft>();
                gsP.hubIata = "PEK";
                gsP.cash = 5000f;
                gsP.safetyRating = SafetyRating.B;
                var stgP = StrategyManager.EnsureState(gsP);
                stgP.governmentRelation = 50f;

                Debug.Log($"准入 判定 国际 PEK-SIN={RoutePermitLogic.IsInternational("PEK", "SIN")} 国内 PEK-SHA={RoutePermitLogic.IsInternational("PEK", "SHA")}（期望 True / False）");
                Debug.Log($"准入 审查季数 国内={RoutePermitLogic.ReviewQuarters(false, 1)} 国际={RoutePermitLogic.ReviewQuarters(true, 1)} 国际重申={RoutePermitLogic.ReviewQuarters(true, 2)}（期望 1 / 2 / 3）");
                Debug.Log($"准入 需时刻槽 国际={RoutePermitLogic.NeedsSlot("PEK", "SIN", true, true)} 国内支线={RoutePermitLogic.NeedsSlot("PVG", "SHA", false, false)}（期望 True / False）");

                int gapSlot = RoutePermitLogic.MissingPrerequisite(gsP, "PEK", "SHA", false, out string gapSlotNote);
                int gapRight = RoutePermitLogic.MissingPrerequisite(gsP, "PEK", "SIN", true, out string gapRightNote);
                Debug.Log($"准入 许可前置 国内（无需时刻，也不卡时刻）={gapSlot}（期望 0） 国际缺航权={gapRight}（期望 1） 提示={gapRightNote}");

                float ch1 = RoutePermitLogic.PassChance(gsP, "PEK", "SHA", false, 1);
                float ch4 = RoutePermitLogic.PassChance(gsP, "PEK", "SHA", false, 4);
                Debug.Log($"准入 通过率 首次={ch1:P0} 重申4次={ch4:P0}（期望 首次 > 重申） 拆解={RoutePermitLogic.ChanceBreakdown(gsP, "PEK", "SHA", false, 1)}");

                try
                {
                    var mailP = new InboxMail { id = "perm_selftest_1", action = "open_route", origin = "PEK", dest = "SHA", subject = "自测：开通 PEK-SHA" };
                    bool acc = InboxManager.AcceptRoutePermit(gsP, mailP, out string pmsg);
                    var inFlight = RoutePermitLogic.InFlightPermits(gsP);
                    Debug.Log($"准入 受理 → accepted={acc} stage={mailP.permitStage}（期望 1 审查中） 节点={mailP.permitNode}/5 预计 第{mailP.permitDueYear}年Q{mailP.permitDueQuarter} 通过率={mailP.permitChance:P0} 前置=[{mailP.permitGate}]");
                    Debug.Log($"准入 回执 {gsP.inbox.Count} 封（期望 ≥1） 在办 {inFlight.Count} 条（期望 1） 进度行={RoutePermitLogic.ProgressLine(mailP)}");
                    Debug.Log($"准入 受理语 = {pmsg}");

                    // 节点排期：受理 Y1Q1 + 国际 2 季 → 批复 Y1Q3
                    var midNodes = RoutePermitLogic.NodesAt(mailP.permitNode, 1, 1, 1, 3, 1, 2);
                    var endNodes = RoutePermitLogic.NodesAt(mailP.permitNode, 1, 1, 1, 3, 1, 3);
                    Debug.Log($"准入 节点排期 国际2季 中间季(第1年Q2) 应报节点={string.Join(",", midNodes)}（期望 2,3,4） 批复季应报节点={endNodes.Count}（期望 0，由批复专函汇报）");
                    RoutePermitLogic.NodeQuarter(RoutePermitLogic.NodeGrant, 2, 1, 1, out int gqy, out int gqq);
                    RoutePermitLogic.NodeQuarter(RoutePermitLogic.NodeAccept, 2, 1, 1, out int aqy, out int aqq);
                    Debug.Log($"准入 节点季度 受理=第{aqy}年Q{aqq}（期望 1/1） 批复=第{gqy}年Q{gqq}（期望 1/3）");

                    string table = RoutePermitLogic.ProgressTable(false, true, 1, 1, 1, 2, mailP.permitNode, mailP.permitStage);
                    Debug.Log($"准入 进度表 含五节点={table.Contains("受理立案") && table.Contains("材料与运力审查") && table.Contains("航权额度与时刻核验") && table.Contains("局方公示") && table.Contains("批复与发证")}（期望 True）\n{table}");
                }
                catch (Exception pe)
                {
                    Debug.LogWarning($"准入 受理用例异常（不阻断后续）：{pe.Message}");
                }

                // —— 节点进度汇报 + 先证后槽（国际航线：只备航权，暂不配时刻）——
                try
                {
                    // 国际运行资质（海外引进渠道运行人员）是国际航线发证硬前置：
                    // 先测「缺资质 → 受阻」，再补一名海外引进机组测「资质齐备 → 正常推进」。
                    stgP.rights.Add(new RouteRight { origin = "PEK", destination = "SIN", freedom = 3, status = "approved" });
                    int gapNoCrew = RoutePermitLogic.MissingPrerequisite(gsP, "PEK", "SIN", true, out string gapNoCrewNote);
                    Debug.Log($"准入 国际运行资质 无海外机组时缺口码={gapNoCrew}（期望 2） 提示含海外引进={gapNoCrewNote.Contains("海外引进")}（期望 True）");

                    gsP.staffPool.Add(new PersonnelData
                    {
                        id = "selftest_overseas_pilot",
                        fullName = "自测·海外机长",
                        jobTitle = "飞行员",
                        source = HrEconomics.SourceOverseas,
                        isOperational = true,
                        onMarket = false,
                        baseSalary = 10.4f,
                        efficiencyBonus = 0.528f
                    });
                    int gapWithCrew = RoutePermitLogic.MissingPrerequisite(gsP, "PEK", "SIN", true, out string gapWithCrewNote);
                    Debug.Log($"准入 国际运行资质 海外机组 {gsP.OverseasCrewCount} 人时缺口码={gapWithCrew}（期望 0） 机组乘数={gsP.CrewQualityMultiplier:F3}");

                    // —— 机组乘数：编制覆盖率插值（接管局口径修复，2026-10-08）——
                    // 旧行为：接管航司已运营的航线被当成「一架机组都没有」，无条件吃满 0.85 折损，
                    // 而叙事层写着「机组人员短缺 X%」——两套口径分叉（见 TakeoverWorldBuilder）。
                    // 现改为 乘数 = 0.85 + (质量乘数 − 0.85) × 覆盖率，无陷阱区（对人数单调不减）。
                    float covNoEst = HrEconomics.CrewQualityMultiplier(1, 0, HrEconomics.CrewAdequacyBaseline);
                    float covHalf = HrEconomics.CrewQualityMultiplier(50, 100, HrEconomics.CrewAdequacyBaseline);
                    float covFull = HrEconomics.CrewQualityMultiplier(100, 100, HrEconomics.CrewAdequacyBaseline);
                    float covTakeover = HrEconomics.CrewQualityMultiplier(68, 80, HrEconomics.CrewAdequacyBaseline);
                    float covOneOfEighty = HrEconomics.CrewQualityMultiplier(1, 80, 0f);
                    Debug.Log($"机组 覆盖率插值 1人(est=0)={covNoEst:F4}（期望 1.0000） 50/100={covHalf:F4}（期望 0.9250）"
                              + $" 100/100={covFull:F4}（期望 1.0000） 68/80(北辰快线难3)={covTakeover:F4}（期望 0.9775）");
                    Debug.Log($"机组 覆盖率不产生陷阱区 1/80(战力触底)={covOneOfEighty:F4} 严格优于未建机组"
                              + $" {HrEconomics.CrewUnstaffedMultiplier:F4}={covOneOfEighty > HrEconomics.CrewUnstaffedMultiplier}（期望 True）"
                              + $" 且单调 ≤ 满编 {covFull:F4}={covOneOfEighty <= covFull}（期望 True）");

                    var mailI = new InboxMail { id = "perm_selftest_2", action = "open_route", origin = "PEK", dest = "SIN", subject = "自测：开通 PEK-SIN" };
                    InboxManager.AcceptRoutePermit(gsP, mailI, out string imsg);
                    gsP.year = mailI.effectYear; gsP.quarter = mailI.effectQuarter;
                    InboxManager.ProcessPendingEffects(gsP);
                    var nodeMail = gsP.inbox.Find(x => x != null && x.action == "permit_progress" && x.tag == "进度");
                    Debug.Log($"准入 节点汇报 国际 推进到 第{gsP.year}年Q{gsP.quarter} → 节点={mailI.permitNode}/5 阶段={mailI.permitStage} 进度邮件={(nodeMail != null ? "有" : "无（可能遇异议/受阻，见下封）")}");
                    if (nodeMail != null) Debug.Log($"准入 节点汇报主题 = {nodeMail.subject}\n{nodeMail.body}");
                    var anyMail = gsP.inbox.Find(x => x != null && x.action == "permit_progress" && x.tag != "受理");
                    if (anyMail != null && nodeMail == null) Debug.Log($"准入 本季回执（非节点汇报）= [{anyMail.tag}] {anyMail.subject}");
                    Debug.Log($"准入 受理语（国际）= {imsg}");

                    // 推到预计批复季：时刻无槽也不该卡发证（先证后槽）
                    gsP.year = mailI.permitDueYear; gsP.quarter = mailI.permitDueQuarter;
                    InboxManager.ProcessPendingEffects(gsP);
                    var slotMail = gsP.inbox.Find(x => x != null && x.subject != null && x.subject.Contains("待协调时刻"));
                    bool opened = gsP.HasRoute("PEK", "SIN");
                    Debug.Log($"准入 先证后槽 批复季 第{gsP.year}年Q{gsP.quarter} → 阶段={mailI.permitStage}（3=已发证） 待开航={mailI.effectPending} 受阻=[{mailI.permitGate}] 已开航={opened}");
                    Debug.Log($"准入 先证后槽 无时刻亦发证={mailI.permitStage == RoutePermitLogic.StageGranted}（期望 True） 时刻等待专函={(slotMail != null ? "有" : "无")} 许可未因缺时刻被否={mailI.permitStage != RoutePermitLogic.StageDenied}（期望 True）");
                    if (slotMail != null) Debug.Log($"准入 时刻等待专函\n{slotMail.body}");

                    // 配到时刻后应自动开航（取消阻碍，若运力/现金不足会发开航受阻专函）
                    stgP.slots.Add(new SlotHolding { airport = "PEK", slotTime = "早", inUse = false });
                    InboxManager.NextQuarter(gsP.year, gsP.quarter, out int ly, out int lq);
                    gsP.year = ly; gsP.quarter = lq;
                    InboxManager.ProcessPendingEffects(gsP);
                    int freeSlots = RoutePermitLogic.AvailableSlots(gsP);
                    Debug.Log($"准入 先证后槽 配到时刻后 第{gsP.year}年Q{gsP.quarter} → 已开航={gsP.HasRoute("PEK", "SIN")} 待开航={mailI.effectPending} 受阻=[{mailI.permitGate}] 空闲槽={freeSlots}");

                    // —— 收件箱按钮可见性：回执类不显示批复按钮 ——
                    var admitMail = gsP.inbox.Find(x => x != null && x.action == "permit_progress");
                    var propMail = gsP.inbox.Find(x => x != null && x.action == "open_route" && InboxManager.IsOpen(x));
                    Debug.Log($"准入 按钮可见性 回执需决策={InboxManager.RequiresDecision(admitMail)}（期望 False）"
                        + $" 已批提案需决策={InboxManager.RequiresDecision(gsP.inbox.Find(x => x != null && x.action == "open_route"))}（期望 False）"
                        + $" 待办提案需决策={InboxManager.RequiresDecision(propMail)}"
                        + $" 空邮件={InboxManager.RequiresDecision(null)}（期望 False）");

                    // —— CEO 决策会议裁决回写：回收件箱不再显示底部决策按钮 ——
                    var meetMail = new InboxMail
                    {
                        id = "mm_selftest", sender = "财务总监", subject = "自测：会议联动函",
                        status = "pending", action = "meet_selftest",
                        receivedYear = gsP.year, receivedQuarter = gsP.quarter, deadlineQuarters = 2
                    };
                    gsP.inbox.Add(meetMail);
                    bool needBefore = InboxManager.RequiresDecision(meetMail);
                    DecisionMeetingManager.ApplyOutcomeToMail(gsP, meetMail.id, "deferred");
                    bool needDeferred = InboxManager.RequiresDecision(meetMail);
                    bool dueDeferred = InboxManager.IsDue(meetMail, gsP.year + 1, gsP.quarter);
                    DecisionMeetingManager.ApplyOutcomeToMail(gsP, meetMail.id, "approved");
                    bool needApproved = InboxManager.RequiresDecision(meetMail);
                    var meetMail2 = new InboxMail
                    {
                        id = "mm_selftest2", sender = "财务总监", subject = "自测：会议拒绝函",
                        status = "pending", action = "meet_selftest",
                        receivedYear = gsP.year, receivedQuarter = gsP.quarter, deadlineQuarters = 2
                    };
                    gsP.inbox.Add(meetMail2);
                    DecisionMeetingManager.ApplyOutcomeToMail(gsP, meetMail2.id, "rejected");
                    bool needRejected = InboxManager.RequiresDecision(meetMail2);
                    Debug.Log($"会议回写 上会前需决策={needBefore}（期望 True） 暂缓后需决策={needDeferred}（期望 False）"
                        + $" 暂缓超期阻断={dueDeferred}（期望 False） 批准后需决策={needApproved}（期望 False）"
                        + $" 拒绝后需决策={needRejected}（期望 False） 暂缓状态={meetMail.status}（approved：批准覆盖暂缓）");

                    // —— 呈报对应航线已不在运营：批准/退回必须结案（决策按钮不得残留）——
                    var staleMail = new InboxMail
                    {
                        id = "cap_stale_selftest", sender = "运营部", subject = "自测：失联航线呈报",
                        status = "pending", action = InboxManager.CapacityDirectiveAction,
                        origin = "ZZZ-XXX", dest = "XXX",
                        receivedYear = gsP.year, receivedQuarter = gsP.quarter, deadlineQuarters = 2
                    };
                    gsP.inbox.Add(staleMail);
                    bool staleOk = InboxManager.ApproveCapacityDirective(gsP, staleMail, out string staleMsg);
                    bool staleOpen = InboxManager.IsOpen(staleMail);
                    var staleMail2 = new InboxMail
                    {
                        id = "sked_stale_selftest", sender = "运营部", subject = "自测：失联航线排班",
                        status = "pending", action = "ops_schedule",
                        origin = "ZZZ-XXX", dest = "XXX",
                        receivedYear = gsP.year, receivedQuarter = gsP.quarter, deadlineQuarters = 2
                    };
                    gsP.inbox.Add(staleMail2);
                    bool staleOk2 = InboxManager.RejectOpsSchedule(gsP, staleMail2, out string staleMsg2);
                    Debug.Log($"失联航线 批准结案={staleOk}（期望 True） 仍待办={staleOpen}（期望 False）"
                        + $" 退回结案={staleOk2}（期望 True） 状态={staleMail.status}/{staleMail2.status}（期望 approved/rejected）");

                    // —— 告知类邮件不该阻塞时间推进（旧档迁移）——
                    var roMail = new InboxMail
                    {
                        id = "ro_selftest_1", sender = "董事会", role = "考核提醒", subject = "自测：告知类邮件",
                        body = "无需批复。", kind = "report", receivedYear = gsP.year, receivedQuarter = gsP.quarter,
                        deadlineQuarters = -1
                    };
                    gsP.inbox.Add(roMail);
                    bool roDueBefore = InboxManager.IsDue(roMail, gsP.year, gsP.quarter);
                    Debug.Log($"告知类 按钮={InboxManager.RequiresDecision(roMail)}（期望 False） 清扫前 到期={roDueBefore}（期望 True）");
                    InboxManager.SweepReadOnlyMails(gsP);
                    bool roDueAfter = InboxManager.IsDue(roMail, gsP.year, gsP.quarter);
                    Debug.Log($"告知类 清扫后 status={roMail.status}（期望 approved） 到期={roDueAfter}（期望 False）");

                    var inq = new InboxMail
                    {
                        id = "ti_selftest", sender = "运营部", subject = "请示：包机询价",
                        status = "pending", action = TransitionBusinessLogic.InquiryAction,
                        payloadCsv = "to_selftest_c", deadlineQuarters = 1,
                        receivedYear = gsP.year, receivedQuarter = gsP.quarter
                    };
                    gsP.inbox.Add(inq);
                    gsP.transitionOffers = gsP.transitionOffers ?? new List<TransitionOffer>();
                    gsP.transitionOffers.Add(new TransitionOffer
                    {
                        id = "to_selftest_c", kind = TransitionKinds.Dry,
                        client = "极地科考队", rateWanPerDay = 0.55f, totalDays = 90
                    });
                    if (gsP.fleet == null) gsP.fleet = new List<Aircraft>();
                    gsP.fleet.Add(new Aircraft
                    {
                        registration = "B-SELF", type = "A320neo", isActive = true,
                        status = AircraftStatuses.Airworthy
                    });
                    InboxManager.SweepReadOnlyMails(gsP);
                    Debug.Log($"询价函 需决策={InboxManager.RequiresDecision(inq)}（期望 True） 清扫后仍待办={InboxManager.IsOpen(inq)}（期望 True）");
                    bool signed = TransitionBusinessLogic.SignAllIdleFromInbox(gsP, inq, out string sInq);
                    bool hasC = false;
                    if (gsP.transitionContracts != null)
                    {
                        for (int ci = 0; ci < gsP.transitionContracts.Count; ci++)
                        {
                            var ct = gsP.transitionContracts[ci];
                            if (ct != null && ct.aircraftReg == "B-SELF") { hasC = true; break; }
                        }
                    }
                    Debug.Log($"询价函 签约闲置机 → ok={signed}（期望 True） status={inq.status}（期望 approved） 合同挂B-SELF={hasC}（期望 True） {sInq}");
                    string term3 = TransitionBusinessLogic.FormatTermRange(gsP, 3, 0, 0);
                    Debug.Log($"过渡合同期限 3 天 → {term3}（含首尾，应含「年」与「—」） 格式={term3.IndexOf("年", StringComparison.Ordinal) > 0 && term3.IndexOf('—') > 0}（期望 True）");

                    // —— 定金 / 尾款 / 预租 三段付款（CEO 定金尾款制）——
                    var tc = gsP.transitionContracts.Find(x => x != null && x.aircraftReg == "B-SELF");
                    float totalSelftest = tc != null ? tc.TotalWan : 0.55f * 90f;
                    TransitionBusinessLogic.SplitTotal(totalSelftest, out float depS, out float balS, out float dayS);
                    Debug.Log($"付款拆分 总额{totalSelftest:F0} → 定金{depS:F1}+逐日{dayS:F1}+尾款{balS:F1}"
                        + $" 合计={depS + dayS + balS:F1}（期望 {totalSelftest:F1}）");

                    bool depOk = tc != null && tc.depositPaid && Mathf.Abs(tc.depositWan - depS) < 0.05f;
                    Debug.Log($"签约即收定金 → 定金{tc?.depositWan:F1} 已收={tc?.depositPaid}（期望 True） 一致={depOk}（期望 True）");

                    // 预租：把合同起始日改到 10 天后，应计入预租而非在租
                    if (tc != null)
                    {
                        tc.startUtcTicks = gsP.SimUtc.Ticks + 10L * TimeSpan.TicksPerDay;
                        int reserved = TransitionBusinessLogic.ReservedCount(gsP);
                        int activeCnt = TransitionBusinessLogic.ActiveContracts(gsP).Count;
                        bool overlapSelf = TransitionBusinessLogic.Overlaps(gsP, "B-SELF",
                            gsP.SimUtc.Ticks + 10L * TimeSpan.TicksPerDay, 90);
                        long nextFree = TransitionBusinessLogic.NextFreeStart(gsP, "B-SELF", 90);
                        Debug.Log($"预租归类 → 预租{reserved}（期望 1） 在租{activeCnt}（期望 0）"
                            + $" 与自身区间重叠={overlapSelf}（期望 True）"
                            + $" 最早空闲={TransitionBusinessLogic.DateFormat(gsP, nextFree)}（应为 10+90=100 天后）");
                        // 预租不执行、不入账
                        float before = gsP.cash;
                        TransitionBusinessLogic.OnDayTick(gsP);
                        float delta = gsP.cash - before;
                        Debug.Log($"预租日推进 → 现金变动{delta:F2}（期望 0，预租未生效不入账）");
                    }
                }
                catch (Exception pe2)
                {
                    Debug.LogWarning($"准入 节点用例异常（不阻断后续）：{pe2.Message}");
                }

                gsP.inbox.Add(new InboxMail
                {
                    id = "pend_slot_n77", action = "buy_slot", origin = "PEK", dest = "1",
                    status = "approved", effectPending = true, effectYear = 1, effectQuarter = 2,
                    permitAttempts = 2
                });
                gsP.inbox.Add(new InboxMail
                {
                    id = "pend_right_n77", action = "apply_right", origin = "PEK", dest = "SIN",
                    status = "approved", effectPending = true, effectYear = 1, effectQuarter = 2
                });
                gsP.inbox.Add(new InboxMail
                {
                    id = "open_slot_n77", action = "buy_slot", origin = "PEK", dest = "2",
                    status = "pending", effectPending = false
                });
                var pendS = InboxManager.PendingSlotSettlements(gsP);
                var pendR = InboxManager.PendingRightSettlements(gsP);
                string sLine = pendS.Count > 0 ? InboxManager.FormatPendingSlot(pendS[0]) : "";
                string rLine = pendR.Count > 0 ? InboxManager.FormatPendingRight(pendR[0]) : "";
                Debug.Log($"n77 待交割 → 槽 {pendS.Count} 条（期望 1，不含未批） {sLine}"
                          + $"\n　航权 {pendR.Count} 条（期望 1） {rLine}"
                          + $"　含待交割={sLine.IndexOf("待交割", StringComparison.Ordinal) >= 0 && rLine.IndexOf("待交割", StringComparison.Ordinal) >= 0}（期望 True）");
            }
            finally
            {
                GameObject.DestroyImmediate(goP);
            }

            // —— 运力调整指令：解析 → 自动编班/组环/联动 → 呈报 → 退回回滚 ——
            var goD = new GameObject("__cap_directive_test_gs__");
            try
            {
                var gsD = goD.AddComponent<GameState>();
                gsD.onTimePercent = 88f;
                gsD.activeRoutes = new List<RouteData>();
                gsD.fleet = new List<Aircraft>();
                gsD.inbox = new List<InboxMail>();
                gsD.flightRings = new List<FlightRing>();

                var ra = new RouteData
                {
                    routeId = "R-PEK-SHA", origin = "PEK", destination = "SHA", distanceKm = 1100f,
                    aircraftType = "A320neo", flightNumber = "NS100", registration = "B-2000",
                    depTime = "08:00", arrTime = "10:20", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 12f, dailyProfit = 12f, groundStaffAssigned = 2
                };
                ra.SyncFlightsFromPrimary();
                var rb = new RouteData
                {
                    routeId = "R-SHA-PEK", origin = "SHA", destination = "PEK", distanceKm = 1100f,
                    aircraftType = "A320neo", flightNumber = "NS101", registration = "B-2001",
                    depTime = "14:00", arrTime = "16:20", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 11f, dailyProfit = 11f, groundStaffAssigned = 2
                };
                rb.SyncFlightsFromPrimary();
                gsD.activeRoutes.Add(ra);
                gsD.activeRoutes.Add(rb);
                gsD.fleet.Add(new Aircraft { registration = "B-2000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 5 });
                gsD.fleet.Add(new Aircraft { registration = "B-2001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 7 });
                gsD.fleet.Add(new Aircraft { registration = "B-2002", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 9 });

                var dir = CapacityDirectiveLogic.Parse("这条线运力偏紧，加密到每天 3 班，早班到红眼都排上，并保证同机连班");
                Debug.Log($"运力指令 解析 → 意图={dir.intent} 班次={dir.waves} 时刻={(dir.deps == null ? "自动" : string.Join("、", dir.deps))} 组环={dir.buildRing} 分析={dir.analysis}");

                int wavesBefore = ra.FlightCount;
                var rep = CapacityDirectiveLogic.Execute(gsD, ra, dir);
                Debug.Log($"运力指令 执行 → ok={rep.ok}（期望 True） 每日 {wavesBefore}→{ra.FlightCount} 班 周{ra.weeklyFlights}班 机号={CapacityDirectiveLogic.RegsOf(ra)} 冻结={ra.schedulePending}（期望 False）");
                string srcBody = CapacityDirectiveLogic.FormatReportBody(gsD, ra, rep);
                Debug.Log($"运力指令 来源章 → 有运力来源={srcBody.IndexOf("运力来源", StringComparison.Ordinal) >= 0}"
                          + $"　复用={srcBody.IndexOf("复用本线", StringComparison.Ordinal) >= 0}"
                          + $"　闲置或串用={srcBody.IndexOf("闲置", StringComparison.Ordinal) >= 0 || srcBody.IndexOf("串用其它航线", StringComparison.Ordinal) >= 0}（期望均 True）");
                Debug.Log($"运力指令 联动 → {rep.linkedSection?.Replace("\n", " / ")}");
                Debug.Log($"运力指令 航班环 → [{rep.ringIdsCsv}] 环数={gsD.flightRings.Count}（期望 1）");
                Debug.Log($"运力指令 回退快照 → hasPreDirective={ra.hasPreDirective}（期望 True） 快照班次={ra.preDirectiveFlights?.Count ?? -1}（期望 1）");

                bool queued = InboxManager.QueueCapacityReport(gsD, ra, rep);
                var capMail = gsD.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction);
                Debug.Log($"运力指令 呈报 → 入箱={queued}（期望 True） 主题={capMail?.subject}");
                Debug.Log($"运力指令 呈报 需决策={InboxManager.RequiresDecision(capMail)}（期望 True） 到期阻断={InboxManager.IsDue(capMail, gsD.year, gsD.quarter)}（期望 False）");
                Debug.Log($"运力指令 呈报正文\n{capMail?.body}");

                // 退回：自动恢复到指令前的排班，并撤销本次航班环
                InboxManager.RejectCapacityDirective(gsD, capMail, out string rmsg);
                Debug.Log($"运力指令 退回 → {rmsg}");
                Debug.Log($"运力指令 退回后 → 每日班次={ra.FlightCount}（期望 {wavesBefore}） 环数={gsD.flightRings.Count}（期望 0） 待批标记={ra.directivePending}（期望 False）");

                var dir2 = CapacityDirectiveLogic.Parse("客座率太低，减到每天 1 班，先不组环");
                Debug.Log($"运力指令 解析2 → 意图={dir2.intent}（期望 reduce） 班次={dir2.waves}（期望 1） 组环={dir2.buildRing}（期望 False）");

                var dir3 = CapacityDirectiveLogic.Parse("保持现状，暂不调整");
                Debug.Log($"运力指令 解析3 → 意图={dir3.intent}（期望 keep）");

                var dirFromTo = CapacityDirectiveLogic.Parse("该航线由每日1段往返，增加到每日4段往返");
                Debug.Log($"运力指令 解析 由1到4 → 意图={dirFromTo.intent}（期望 increase） 班次={dirFromTo.waves}（期望 4）");
                CapacityDirectiveLogic.MergeAiJson(dirFromTo, "{\"intent\":\"increase\",\"waves\":1,\"depsCsv\":\"08:00\",\"rationale\":\"维持1班\"}");
                Debug.Log($"运力指令 解析 由1到4 抗模型误填 → 班次={dirFromTo.waves}（期望仍为 4）");

                // —— 单航线页：班次航班号 / 班期 / 往返时刻（与 AS 的 Flight Schedule 同口径）——
                FlightScheduleFormat.Numbers(ra, 0, out string fnoA, out string fnoB);
                FlightScheduleFormat.Numbers(ra, 2, out string fnoC, out string fnoD);
                Debug.Log($"航班号 → 第1班 {fnoA}/{fnoB}（期望 NS100/NS101）　第3班 {fnoC}/{fnoD}（期望 NS104/NS105）");
                var dDays = FlightScheduleFormat.DaysOf("二四六");
                Debug.Log($"班期解析 → 二四六 = [{string.Join(",", dDays)}]（期望 False,True,False,True,False,True,False）"
                          + $"　每日班次={FlightScheduleFormat.WeeklyOf(ra.flights[0])}（期望 7）");
                var trip0 = FlightScheduleFormat.Trip(ra, ra.flights[0]);
                Debug.Log($"班次周排班 → {fnoA} {ra.origin}→{ra.destination} {trip0.outDepFull}→{trip0.outArrFull}（{FlightScheduleFormat.DurationShort(trip0.blockMinutes)}）");
                Debug.Log($"班次周排班 → 过站 {FlightScheduleFormat.AirportWithIcao(ra.destination)} {FlightScheduleFormat.Duration(trip0.turnMinutes)}");
                Debug.Log($"班次周排班 → {fnoB} {ra.destination}→{ra.origin} {trip0.inDepFull}→{trip0.inArrFull}（{FlightScheduleFormat.DurationShort(trip0.blockMinutes)}）"
                          + $"　一轮往返 {FlightScheduleFormat.Duration(trip0.cycleMinutes)}");

                // —— 周排班：每班次按「去程 + 回程」两段呈现（引擎实际执行口径）——
                LiveClockLogic.RoundTripWindow(new DateTime(2026, 10, 1), "08:00", "10:20", 1100f, "A320neo", 2,
                    out DateTime odRT, out DateTime oaRT, out DateTime rdRT, out DateTime raRT);
                Debug.Log($"周排班 往返 → 去程 {odRT:HH:mm}→{oaRT:HH:mm} 回程 {rdRT:HH:mm}→{raRT:HH:mm}（同日返回={raRT.Date == odRT.Date}，故一机一日 2 段）");

                // —— 一机多段：往返轮转窗口判定 + 同基地串飞选机 + 机队重排（试算）——
                var w8 = OpsRotationLogic.WindowOf(ra, "08:00");
                var w12 = OpsRotationLogic.WindowOf(ra, "12:00");
                var w18 = OpsRotationLogic.WindowOf(ra, "18:00");
                Debug.Log($"轮转 窗口 → 08:00 往返占 {w8.start}~{w8.end} 分（{w8.end - w8.start} 分钟＝去程+过站+回程）");
                Debug.Log($"轮转 冲突判定 → 08:00 vs 12:00 重叠={OpsRotationLogic.Overlaps(w8, w12, OpsRotationLogic.RotationGapMinutes)}（期望 False：1100 km 一段往返 3.4h，午班不撞早班）"
                          + $"；08:00 vs 18:00 重叠={OpsRotationLogic.Overlaps(w8, w18, OpsRotationLogic.RotationGapMinutes)}（期望 False）");

                var planT = new OpsRotationLogic.RotationPlan();
                planT.Assign("B-2000", w8);
                var pickNoon = OpsRotationLogic.PickForSlot(ra, gsD.fleet, planT, "12:00", false, gsD);
                Debug.Log($"轮转 选机 午班串飞 → 12:00 选到 {pickNoon?.registration}（期望 B-2000：早班 08:00 回场后值勤仍够 4 航段）");
                var pickEve = OpsRotationLogic.PickForSlot(ra, gsD.fleet, planT, "18:00", false, gsD);
                Debug.Log($"轮转 选机 晚班换机 → 18:00 选到 {pickEve?.registration}（期望非 B-2000：早+晚并入同一值勤期超 4 航段上限）");

                OpsRotationLogic.ReplanFleet(gsD, gsD.activeRoutes, gsD.fleet, false, out var stRep, out var freedRep);
                Debug.Log($"轮转 机队试算 → 在用 {stRep.aircraftUsed}/{stRep.aircraftTotal} 架，共 {stRep.turnsTotal} 段往返，单机最多 {stRep.maxTurns} 段，可释放 {freedRep.Count} 架");

                // —— 运力缺口：想要的时段排不出时，必须如实报「缺哪几班、为什么」，不再静默少排 ——
                var routesBackup = gsD.activeRoutes;
                var fleetBackup = gsD.fleet;
                gsD.activeRoutes = new List<RouteData>();
                gsD.fleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-9000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 6 }
                };
                var rcThin = new RouteData
                {
                    routeId = "R-SZX-PEK", origin = "SZX", destination = "PEK", distanceKm = 1950f,
                    aircraftType = "A320neo", flightNumber = "SZ100", registration = "B-9000",
                    depTime = "08:00", arrTime = "10:30", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 2
                };
                rcThin.SyncFlightsFromPrimary();
                gsD.activeRoutes.Add(rcThin);
                rcThin.flights.Clear();                 // 视作新航线，尚无可复用排班
                rcThin.registration = "";

                var gaps = OpsScheduleLogic.PrecheckSlots(rcThin, gsD.fleet, gsD.activeRoutes, out var feasibleCsv);
                Debug.Log($"运力缺口 预检 → 单机可排 {feasibleCsv.Count}/{ScheduleSlots.DepTimes.Length} 段（期望 1：R10 真实航司口径，"
                          + $"单机单值勤期只能 1 段往返）"
                          + $"　受限 {gaps.Count} 个（期望 3）　首个：{(gaps.Count > 0 ? gaps[0].slotLabel + " " + gaps[0].dep + " " + gaps[0].kind + "　" + gaps[0].reason : "无")}");
                string preCtx = CapacityDirectiveLogic.Precheck(gsD, rcThin);
                Debug.Log($"运力缺口 单航线页提示 → {preCtx}");
                Debug.Log($"运力缺口 受限归因 → 含短因={preCtx.IndexOf("（缺时刻槽）", StringComparison.Ordinal) >= 0 || preCtx.IndexOf("（缺运力）", StringComparison.Ordinal) >= 0}（期望 True）"
                          + $"　缺槽时附办理说明={preCtx.IndexOf("时刻槽办理", StringComparison.Ordinal) >= 0}（有缺槽需 True）");
                for (int gi = 0; gi < gaps.Count; gi++)
                    Debug.Log($"运力缺口 逐机归因 [{gaps[gi].slotLabel} {gaps[gi].dep}] → {string.Join(" ｜ ", gaps[gi].details.ToArray())}");

                var dirThin = CapacityDirectiveLogic.Parse("这条线运力偏紧，加密到每天 4 段往返，早班到红眼都排上");
                Debug.Log($"运力缺口 解析 → 班次={dirThin.waves}（期望 4） 时刻={(dirThin.deps == null ? "自动" : string.Join("、", dirThin.deps))}（期望 4 个时段）");
                var repThin = CapacityDirectiveLogic.Execute(gsD, rcThin, dirThin);
                Debug.Log($"运力缺口 执行 → ok={repThin.ok}（期望 True） 实排 {rcThin.FlightCount} 段（期望 2）"
                          + $" 缺口 {repThin.shortages?.Count ?? 0} 个（期望 2） 缺口段非空={!string.IsNullOrEmpty(repThin.shortageSection)}（期望 True）");
                Debug.Log($"运力缺口 呈报正文\n{CapacityDirectiveLogic.FormatReportBody(gsD, rcThin, repThin)}");

                // 归因明细（到具体机号）：检修 / 已被其它航线占用 / 本线前后班次冲突，各归各因
                var rcOther = new RouteData
                {
                    routeId = "R-PEK-SZX", origin = "PEK", destination = "SZX", distanceKm = 1950f,
                    aircraftType = "A320neo", flightNumber = "SZ101"
                };
                gsD.activeRoutes.Add(rcOther);
                var diagFleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-9000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 6 },
                    new Aircraft { registration = "B-9001", type = "A320neo", isActive = true, status = AircraftStatuses.Maintenance, age = 8 },
                    new Aircraft { registration = "B-9002", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 7 }
                };
                var planD = new OpsRotationLogic.RotationPlan();
                planD.Assign("B-9002", OpsRotationLogic.WindowOf(rcOther, "12:00"));
                var wd = OpsScheduleLogic.Diagnose(rcThin, diagFleet, planD, "12:00");
                Debug.Log($"运力缺口 归因汇总 → {wd.reason}");
                Debug.Log($"运力缺口 归因明细 → {string.Join(" ｜ ", wd.details.ToArray())}"
                          + $"（期望含「B-9001…检修」「B-9002…被 PEK⇄SZX SZ101 占用」）");
                gsD.activeRoutes.Remove(rcOther);

                gsD.activeRoutes = routesBackup;
                gsD.fleet = fleetBackup;

                // ================= P1：机场分级 / 时刻池 / 宵禁 =================
                Debug.Log($"机场分级 → SZX={AirportCatalog.TierLabelOf("SZX")} PEK={AirportCatalog.TierLabelOf("PEK")}"
                          + $" LHR={AirportCatalog.TierLabelOf("LHR")} NRT={AirportCatalog.TierLabelOf("NRT")}"
                          + $" SHA={AirportCatalog.TierLabelOf("SHA")} SYX={AirportCatalog.TierLabelOf("SYX")}"
                          + $"（枢纽=0 / 干线=1 / 支线=2，由 CityEconomyCatalog 客流/货邮推导）");
                var capHub = AirportCatalog.SlotCapacity("SZX");
                var capTrunk = AirportCatalog.SlotCapacity("SYX");
                Debug.Log($"时刻池 → SZX 早/中/晚/夜 = [{string.Join(",", capHub)}]（期望 12/12/14/8）"
                          + $"　SYX = [{string.Join(",", capTrunk)}]"
                          + $"　SZX 协调机场={AirportCatalog.IsCoordinated("SZX")}（期望 True）"
                          + $"　SYX 协调机场={AirportCatalog.IsCoordinated("SYX")}");

                // 机场分级自检（§7.4）：字段完整 / 容量为正 / 宵禁格式合法 / 等级只取 0–2
                int badApt = 0, noCap = 0, badCurfew = 0, badTier = 0, coordMismatch = 0;
                var aptList = AirportCatalog.Airports;
                for (int ai = 0; ai < aptList.Count; ai++)
                {
                    var a = aptList[ai];
                    if (string.IsNullOrEmpty(a.code) || string.IsNullOrEmpty(a.nameZh) || string.IsNullOrEmpty(a.nameEn)) badApt++;
                    if (a.tier < 0 || a.tier > 2) badTier++;
                    if (a.slotCapacity == null || a.slotCapacity.Length != 4) noCap++;
                    else for (int b = 0; b < 4; b++) if (a.slotCapacity[b] <= 0) noCap++;
                    if (!string.IsNullOrEmpty(a.curfew) && !AirportCatalog.TryCurfew(a.code, out _, out _)) badCurfew++;
                    if (a.coordinated != (a.tier <= 1)) coordMismatch++;
                }
                Debug.Log($"机场分级 自检 → 共 {aptList.Count} 个机场：字段缺失={badApt}（期望 0）　容量异常={noCap}（期望 0）"
                          + $"　宵禁格式错={badCurfew}（期望 0）　等级越界={badTier}（期望 0）　协调标记不符={coordMismatch}（期望 0）");
                int t0 = 0, t1 = 0, t2 = 0;
                for (int ai = 0; ai < aptList.Count; ai++)
                {
                    int t = aptList[ai].tier;
                    if (t == 0) t0++; else if (t == 1) t1++; else t2++;
                }
                Debug.Log($"机场分级 分布 → 枢纽 {t0} / 干线 {t1} / 支线 {t2}（合计 {t0 + t1 + t2}）");

                // 宽体判定自检（§7.4）：A321neo/A321XLR/B757-200 不得误判为宽体；B789/A339/B77W 必须为宽体
                Debug.Log($"宽体判定 自检 → A320neo={AircraftCatalog.IsWidebody("A320neo")}（期望 False）"
                          + $"　A321neo={AircraftCatalog.IsWidebody("A321neo")}（期望 False）"
                          + $"　A321XLR={AircraftCatalog.IsWidebody("A321XLR")}（期望 False）"
                          + $"　B757-200={AircraftCatalog.IsWidebody("B757-200")}（期望 False）"
                          + $"　B787-9={AircraftCatalog.IsWidebody("B787-9")}（期望 True）"
                          + $"　A330-900={AircraftCatalog.IsWidebody("A330-900")}（期望 True）"
                          + $"　B777-300ER={AircraftCatalog.IsWidebody("B777-300ER")}（期望 True）");

                // 增援上限自检（§7.4）：Class 1/2/3 × 3/4 人制 六格全对
                Debug.Log($"增援上限 自检 → C1×3人={AircraftCatalog.AugmentedLimit(1, 3) / 60f:0.##}h（16）"
                          + $"　C1×4人={AircraftCatalog.AugmentedLimit(1, 4) / 60f:0.##}h（17）"
                          + $"　C2×3人={AircraftCatalog.AugmentedLimit(2, 3) / 60f:0.##}h（15）"
                          + $"　C2×4人={AircraftCatalog.AugmentedLimit(2, 4) / 60f:0.##}h（16）"
                          + $"　C3×3人={AircraftCatalog.AugmentedLimit(3, 3) / 60f:0.##}h（14）"
                          + $"　C3×4人={AircraftCatalog.AugmentedLimit(3, 4) / 60f:0.##}h（15）"
                          + $"\n休息舱 判定 → B787-9={AircraftCatalog.RestClassLabel(AircraftCatalog.RestClassOf("B787-9"))}"
                          + $"　B767-300={AircraftCatalog.RestClassLabel(AircraftCatalog.RestClassOf("B767-300"))}"
                          + $"　A320neo={AircraftCatalog.RestClassLabel(AircraftCatalog.RestClassOf("A320neo"))}");
                Debug.Log($"宵禁 → LHR={AirportCatalog.CurfewLabel("LHR")}　NRT={AirportCatalog.CurfewLabel("NRT")}"
                          + $"　PEK={AirportCatalog.CurfewLabel("PEK")}　SZX=（空={string.IsNullOrEmpty(AirportCatalog.CurfewOf("SZX"))}）"
                          + $"\n　中国单列 → PEK={AirportRegions.IsChinaCurfewAirport("PEK")}　PKX={AirportRegions.IsChinaCurfewAirport("PKX")}（期望 True / False）"
                          + $"　PKX 宵禁空={string.IsNullOrEmpty(AirportCatalog.CurfewOf("PKX"))}（期望 True）"
                          + $"\n　判定 → LHR 23:30={AirportCatalog.InCurfew("LHR", 23 * 60 + 30)}（期望 True）"
                          + $"　LHR 05:30={AirportCatalog.InCurfew("LHR", 5 * 60 + 30)}（期望 True）"
                          + $"　LHR 07:00={AirportCatalog.InCurfew("LHR", 7 * 60)}（期望 False）"
                          + $"　PEK 23:30={AirportCatalog.InCurfew("PEK", 23 * 60 + 30)}（期望 True）"
                          + $"　SZX 23:30={AirportCatalog.InCurfew("SZX", 23 * 60 + 30)}（期望 False）"
                          + $"　PKX 23:30={AirportCatalog.InCurfew("PKX", 23 * 60 + 30)}（期望 False）");

                // ================= 需求波窗口（R7：三峰一谷） =================
                Debug.Log($"需求波 → 早 {ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(0))}–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(0))}"
                          + $"　午 {ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(1))}–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(1))}"
                          + $"　晚 {ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(2))}–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(2))}"
                          + $"　夜 {ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(3))}–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(3))}");
                Debug.Log($"需求波 收边 → 07:10→{ScheduleSlots.FormatClock(ScheduleSlots.SnapIntoWindow(0, 7 * 60 + 10))}（期望 07:10）"
                          + $"　10:40→{ScheduleSlots.FormatClock(ScheduleSlots.SnapIntoWindow(0, 10 * 60 + 40))}（期望 09:30）"
                          + $"　05:00→{ScheduleSlots.FormatClock(ScheduleSlots.SnapIntoWindow(0, 5 * 60))}（期望 06:30）");
                Debug.Log($"需求波 归属 → 08:00={ScheduleSlots.LabelForDep("08:00")}（早班）"
                          + $"　12:00={ScheduleSlots.LabelForDep("12:00")}（午班）"
                          + $"　18:00={ScheduleSlots.LabelForDep("18:00")}（晚班）"
                          + $"　22:00={ScheduleSlots.LabelForDep("22:00")}（红眼）"
                          + $"　07:10={ScheduleSlots.LabelForDep("07:10")}（应仍归早班）");

                // ================= 值勤预算（R9 / R10 / R12） =================
                var rcSzx = new RouteData
                {
                    routeId = "R-SZX-PEK-T", origin = "SZX", destination = "PEK", distanceKm = 1950f,
                    aircraftType = "A320neo", flightNumber = "SZ300", groundStaffAssigned = 3
                };
                int blockT = OpsRotationLogic.BlockOf(rcSzx);
                int turnT = OpsRotationLogic.TurnOf(rcSzx);
                Debug.Log($"轮转 物理参数 → SZX–PEK（1950 km）单程 {blockT} 分 / 过站 {turnT} 分 / 一段往返占 {blockT * 2 + turnT} 分");

                var b2 = OpsRotationLogic.BudgetOf("A320neo", 7 * 60, blockT, 2);
                var b4 = OpsRotationLogic.BudgetOf("A320neo", 7 * 60, blockT, 4);
                Debug.Log($"值勤预算 窄体 → 2 航段 {b2.limit} 分（期望 {13 * 60}，{b2.note}）"
                          + $"　4 航段 {b4.limit} 分（期望 {12 * 60}，{b4.note}）"
                          + $"　航段上限 2={b2.sectorCap}／4={b4.sectorCap}（期望 int.MaxValue：窄体不受增援航段约束）");
                var bWide = OpsRotationLogic.BudgetOf("B787-9", 7 * 60, blockT, 4);
                Debug.Log($"值勤预算 宽体增援（R12）→ {bWide.note}　上限={bWide.limit} 分（期望 {16 * 60}）"
                          + $"　航段上限={bWide.sectorCap}（期望 {OpsRotationLogic.AugmentedSectorCap}）　增援={bWide.augmented}（期望 True）");
                var bWideLong = OpsRotationLogic.BudgetOf("B787-9", 7 * 60, 10 * 60, 2);
                Debug.Log($"值勤预算 宽体超长程 → {bWideLong.note}　上限={bWideLong.limit} 分（期望 {17 * 60}：4 人制 + 单段>9h 且 ≤2 段再 +1h）");
                Debug.Log($"增援空中休息（R13）→ C1×16h={AircraftCatalog.MinInflightRestMinutes(1, 16 * 60)} 分（90）"
                          + $"　C2×15h={AircraftCatalog.MinInflightRestMinutes(2, 15 * 60)} 分（150）"
                          + $"　C3×15h={AircraftCatalog.MinInflightRestMinutes(3, 15 * 60)} 分（230）"
                          + $"　B787 10h 段满足={AircraftCatalog.InflightRestSatisfied(1, 17 * 60, 10 * 60)}（期望 True）"
                          + $"　B787 76 分短程满足={AircraftCatalog.InflightRestSatisfied(1, 16 * 60, 76)}（期望 False）");
                var bWideShort = OpsRotationLogic.BudgetOf("B787-9", 7 * 60, 76, 2);
                Debug.Log($"值勤预算 宽体短程休息不足 → 增援={bWideShort.augmented}（期望 False）　上限={bWideShort.limit} 分（期望 {13 * 60}）　{bWideShort.note}");
                Debug.Log($"值勤预算 报到修正 → 04:30={OpsRotationLogic.ReportAdjust(4 * 60 + 30)}（期望 -60）"
                          + $"　05:30={OpsRotationLogic.ReportAdjust(5 * 60 + 30)}（期望 -30）"
                          + $"　09:00={OpsRotationLogic.ReportAdjust(9 * 60)}（期望 0）"
                          + $"　19:00={OpsRotationLogic.ReportAdjust(19 * 60)}（期望 -60）");

                // ================= R10 验收：单机 SZX–PEK 只能 1 段往返；一日 4 段需 4 架 =================
                var oneFleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-3001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 3 }
                };
                var gaps1 = OpsScheduleLogic.PrecheckSlots(rcSzx, oneFleet, new List<RouteData>(), out var feas1);
                Debug.Log($"R10 验收 单机 → 可排 {feas1.Count}/4 段（期望 1）　缺口 {gaps1.Count} 个（期望 3）"
                          + $"\n　可排 [{string.Join("、", feas1.ToArray())}]"
                          + $"\n　缺口 [{string.Join(" ｜ ", gaps1.ConvertAll(g => g.slotLabel + " " + g.dep + " " + g.kind).ToArray())}]"
                          + $"\n　首个原因：{(gaps1.Count > 0 ? gaps1[0].reason : "无")}"
                          + $"\n　逐机归因：{(gaps1.Count > 0 ? string.Join(" ｜ ", gaps1[0].details.ToArray()) : "无")}");

                // R10 决定性验证：同一架机再排一段往返 —— 直接问 CanTake（不经时刻槽，只看值勤预算/宵禁）
                var planR10 = new OpsRotationLogic.RotationPlan();
                planR10.Assign("B-3001", OpsRotationLogic.WindowOf(rcSzx, "06:30"));
                bool secondOk = OpsRotationLogic.CanTake(rcSzx, planR10, oneFleet[0], "12:00", null, false, out string secondWhy);
                int roundTripMin = OpsRotationLogic.BlockOf(rcSzx) * 2 + OpsRotationLogic.TurnOf(rcSzx);
                Debug.Log($"R10 验收 第 2 段 → 06:30 已排一段往返后，12:00 再排={secondOk}（期望 False）"
                          + $"\n　否决原因：{secondWhy}"
                          + $"\n　算术：一段往返占 {roundTripMin} 分（{roundTripMin / 60f:0.#}h），两段需 {roundTripMin * 2} 分（{roundTripMin * 2 / 60f:0.#}h）"
                          + $" > 2 航段上限 {OpsRotationLogic.DutyLimitBySectors(2) / 60f:0.##}h，且 4 航段上限降到 {OpsRotationLogic.DutyLimitBySectors(4) / 60f:0.##}h"
                          + $"\n　→ 结论：R10 真实航司口径下，SZX–PEK 单机每日上限 1 段往返；每日 4 段往返需 4 架");

                // ================= 回归：占位时刻（08:00→10:30＝150 分）把短程往返窗口放大到 6h，
                // 使 PEK–SHA 这类 1100 km 航线一天只能排 1 段往返（B-2000 加不上第二段） =================
                var rcShaDefault = new RouteData
                {
                    routeId = "R-PEK-SHA0", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                    aircraftType = "A320neo", flightNumber = "NS099", registration = "B-2000",
                    baseDailyProfit = 10f, dailyProfit = 10f
                };   // 不写时刻 → 用历史默认 08:00→10:30（占位）
                rcShaDefault.EnsureFlights();
                Debug.Log($"补段回归 占位校正 → PEK–SHA {rcShaDefault.distanceKm:0} km 真实轮挡 "
                          + $"{OpsRotationLogic.LegBlockMinutes(rcShaDefault)} 分（期望 76）"
                          + $"\n　默认占位 08:00→10:30（150 分）→ 校正后 {rcShaDefault.depTime}→{rcShaDefault.arrTime}"
                          + $"\n　旧口径一段往返 = {150 * 2 + OpsRotationLogic.TurnOf(rcShaDefault)} 分"
                          + $"（{(150 * 2 + OpsRotationLogic.TurnOf(rcShaDefault)) / 60f:0.#}h，吃掉 11:30–14:00 整个午班窗口）");

                var rcPekSha = new RouteData
                {
                    routeId = "R-PEK-SHA", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                    aircraftType = "A320neo", flightNumber = "NS100", registration = "B-2000",
                    depTime = "06:30", arrTime = "07:46", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                rcPekSha.SyncFlightsFromPrimary();
                var wSha = OpsRotationLogic.WindowOf(rcPekSha, "06:30");
                var fleetSha = new List<Aircraft>
                {
                    new Aircraft { registration = "B-2000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 5 }
                };
                var gapsSha = OpsScheduleLogic.PrecheckSlots(rcPekSha, fleetSha, new List<RouteData>(), out var feasSha);
                Debug.Log($"补段回归 单机 → PEK–SHA 一段往返 {OpsRotationLogic.FormatMinute(wSha.start)}–"
                          + $"{OpsRotationLogic.FormatMinute(wSha.end)}（{wSha.end - wSha.start} 分 ≈ {(wSha.end - wSha.start) / 60f:0.#}h）"
                          + $"\n　可排 {feasSha.Count}/4 段（期望 2：早班 06:30 + 午班 11:30，即 B-2000 每日 2 段往返）"
                          + $"\n　可排 [{string.Join("、", feasSha.ToArray())}]"
                          + $"\n　缺口 [{string.Join(" ｜ ", gapsSha.ConvertAll(g => g.slotLabel + " " + g.dep + " " + g.kind).ToArray())}]"
                          + $"\n　缺口逐机归因：{(gapsSha.Count > 0 ? string.Join(" ｜ ", gapsSha[0].details.ToArray()) : "无")}"
                          + $"\n　（晚班 16:30 起算 05:30 报到 → 19:52 落地 = 14.4h > 4 航段上限 12h；红眼段去回落在 PEK 23:00–06:00 宵禁内）");

                var planSha = new OpsRotationLogic.RotationPlan();
                planSha.Assign("B-2000", OpsRotationLogic.WindowOf(rcPekSha, "06:30"));
                bool shaSecond = OpsRotationLogic.CanTake(rcPekSha, planSha, fleetSha[0], "11:30", null, false, out string shaWhy);
                Debug.Log($"补段回归 第 2 段 → 06:30 已排一段后，11:30 再排={shaSecond}（期望 True）"
                          + $"\n　结论：{OpsScheduleLogic.DiagnoseBand(rcPekSha, fleetSha, planSha, 2, "", null).reason}");

                var goSlot = new GameObject("__slot_test_gs__");
                try
                {
                    var gsSlot = goSlot.AddComponent<GameState>();
                    string gapMorning = OpsScheduleLogic.SlotGapOf(rcPekSha, 0, gsSlot);
                    string gapNoon = OpsScheduleLogic.SlotGapOf(rcPekSha, 1, gsSlot);
                    Debug.Log($"时刻槽口径 → 已飞早班祖父条款：早班缺口=「{gapMorning}」（期望空）"
                              + $"\n　午班无持仓：缺口非空={!string.IsNullOrEmpty(gapNoon)}（期望 True）　{gapNoon}");
                }
                finally
                {
                    GameObject.DestroyImmediate(goSlot);
                }

                var goRf = new GameObject("__cap_refresh_gs__");
                try
                {
                    var gsRf = goRf.AddComponent<GameState>();
                    gsRf.onTimePercent = 88f;
                    gsRf.inbox = new List<InboxMail>();
                    gsRf.flightRings = new List<FlightRing>();
                    gsRf.fleet = new List<Aircraft>
                    {
                        new Aircraft { registration = "B-2000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 5 },
                        new Aircraft { registration = "B-2100", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 }
                    };
                    var rcRf = new RouteData
                    {
                        routeId = "R-PEK-SHA-RF", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                        aircraftType = "A320neo", flightNumber = "NS198", registration = "B-2000",
                        depTime = "06:30", arrTime = "07:46", daysOfWeek = "每日", weeklyFlights = 7,
                        baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                    };
                    rcRf.SyncFlightsFromPrimary();
                    gsRf.activeRoutes = new List<RouteData> { rcRf };
                    gsRf.inbox.Add(new InboxMail
                    {
                        id = "m_cap_dir_stale",
                        action = InboxManager.CapacityDirectiveAction,
                        origin = rcRf.routeId,
                        dest = rcRf.destination,
                        status = "pending",
                        subject = "旧呈报",
                        body = "实际排定 1 段",
                        directiveNote = "该航线由每日1段往返，增加到每日4段往返",
                        capEngineRev = 0
                    });
                    int refreshed = InboxManager.RefreshOpenCapacityReports(gsRf);
                    var mailRf = gsRf.inbox[0];
                    Debug.Log($"呈报刷新 → 重编 {refreshed} 封（期望 1）　每日 {rcRf.FlightCount} 班（期望 3：B-2000 早+午，第二架晚班；红眼宵禁）"
                              + $"　引擎戳={mailRf.capEngineRev}（期望 {CapacityDirectiveLogic.EngineRev}）"
                              + $"　正文含「{rcRf.FlightCount} 班」={!string.IsNullOrEmpty(mailRf.body)}");
                    int secondPass = InboxManager.RefreshOpenCapacityReports(gsRf);
                    Debug.Log($"呈报刷新 二次 → {secondPass} 封（期望 0：已盖当前引擎戳）");
                }
                finally
                {
                    GameObject.DestroyImmediate(goRf);
                }

                var goP3 = new GameObject("__p3_slot_gs__");
                try
                {
                    var gsP3 = goP3.AddComponent<GameState>();
                    gsP3.cash = 50000f;
                    gsP3.onTimePercent = 88f;
                    gsP3.stg = new StrategyState();
                    gsP3.inbox = new List<InboxMail>();
                    gsP3.flightRings = new List<FlightRing>();
                    gsP3.fleet = new List<Aircraft>
                    {
                        new Aircraft { registration = "B-2000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 5 },
                        new Aircraft { registration = "B-2100", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 }
                    };
                    var rcP3 = new RouteData
                    {
                        routeId = "R-PEK-SHA-P3", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                        aircraftType = "A320neo", flightNumber = "NS188", registration = "B-2000",
                        depTime = "06:30", arrTime = "07:46", daysOfWeek = "每日", weeklyFlights = 7,
                        baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                    };
                    rcP3.SyncFlightsFromPrimary();
                    gsP3.activeRoutes = new List<RouteData> { rcP3 };

                    var dirP3 = CapacityDirectiveLogic.Parse("增加到每日4段往返");
                    var repP3 = CapacityDirectiveLogic.Execute(gsP3, rcP3, dirP3);
                    int noonKind = 0;
                    if (repP3.shortages != null)
                        for (int si = 0; si < repP3.shortages.Count; si++)
                            if (repP3.shortages[si] != null && repP3.shortages[si].kind == "no_slot") noonKind++;
                    Debug.Log($"P3 无槽加密 → ok={repP3.ok} 实排 {rcP3.FlightCount} 班（期望 1：仅祖父早班）"
                              + $"　缺槽波 {noonKind} 个（期望 ≥1）");

                    InboxManager.QueueCapacityReport(gsP3, rcP3, repP3);
                    var applyMail = gsP3.inbox.Find(x => x != null && x.action == InboxManager.ApplySlotAction);
                    Debug.Log($"P3 申请邮件 → {(applyMail != null ? applyMail.subject : "无")}（期望含「申请」） 数量={applyMail?.permitAttempts ?? 0}");
                    string bodyP4 = CapacityDirectiveLogic.FormatReportBody(gsP3, rcP3, repP3);
                    var capMailP4 = gsP3.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction);
                    Debug.Log($"P4 两阶段呈报 → slotFirst={repP3.slotFirst}（期望 True）　日循环={bodyP4.IndexOf("日循环", StringComparison.Ordinal) >= 0}"
                              + $"　时刻槽章节={bodyP4.IndexOf("时刻槽申请", StringComparison.Ordinal) >= 0}"
                              + $"　第一阶段={bodyP4.IndexOf("第一阶段", StringComparison.Ordinal) >= 0}"
                              + $"　角标={capMailP4?.tag}（期望 待槽）");

                    // R14 两端建模：加密一个时段要两端都有持仓，按引擎给出的两端需求逐条购入
                    string buyMsg = "";
                    var buyLog = new System.Text.StringBuilder();
                    for (int bb = 1; bb <= 2; bb++)
                    {
                        var bnBuy = RoutePermitLogic.BandNeeds(rcP3, bb);
                        for (int k = 0; k < bnBuy.Count; k++)
                        {
                            if (!RoutePermitLogic.NeedsDepartureSlot(bnBuy[k].airport)) continue;
                            bool ok = StrategyManager.AcquireSlots(gsP3, bnBuy[k].airport, bnBuy[k].band, 1, out buyMsg);
                            buyLog.Append($"{bnBuy[k].airport}{StrategyManager.SlotBandName(bnBuy[k].band)}={(ok ? "✓" : "✗")} ");
                        }
                    }
                    Debug.Log($"P3 购槽（两端）→ {buyLog}　{buyMsg}　空闲中={RoutePermitLogic.FreeSlots(gsP3, "PEK", 1)}");

                    rcP3.RestoreScheduleFromBackup();
                    var dirP3b = CapacityDirectiveLogic.Parse("增加到每日4段往返");
                    var repP3b = CapacityDirectiveLogic.Execute(gsP3, rcP3, dirP3b);
                    int bound = 0;
                    if (gsP3.stg?.slots != null)
                        for (int si = 0; si < gsP3.stg.slots.Count; si++)
                            if (gsP3.stg.slots[si] != null && gsP3.stg.slots[si].inUse
                                && gsP3.stg.slots[si].usedByRouteId == rcP3.routeId) bound++;
                    Debug.Log($"P3 有槽加密 → 实排 {rcP3.FlightCount} 班（期望 ≥2）　绑定 {bound} 条"
                              + $"　缺口 kind=[{(repP3b.shortages == null ? "" : string.Join(",", repP3b.shortages.ConvertAll(g => g.kind).ToArray()))}]");

                    CapacityDirectiveLogic.Revert(gsP3, rcP3, repP3b.ringIdsCsv, out string revMsg);
                    int stillBound = 0;
                    if (gsP3.stg?.slots != null)
                        for (int si = 0; si < gsP3.stg.slots.Count; si++)
                            if (gsP3.stg.slots[si] != null && gsP3.stg.slots[si].inUse
                                && gsP3.stg.slots[si].usedByRouteId == rcP3.routeId) stillBound++;
                    Debug.Log($"P3 退回退槽 → {revMsg}　仍绑定本线 {stillBound}（期望 0 或仅祖父未购入的跳过）　每日 {rcP3.FlightCount}（期望 1）");
                }
                finally
                {
                    GameObject.DestroyImmediate(goP3);
                }

                // —— R14：时刻槽两端建模（去程离港端 + 回程离港端，缺一不可）——
                var goTwoEnd = new GameObject("__slot_two_end__");
                try
                {
                    var gs2E = goTwoEnd.AddComponent<GameState>();
                    gs2E.cash = 50000f;
                    gs2E.stg = new StrategyState();
                    gs2E.inbox = new List<InboxMail>();
                    gs2E.hubIata = "SZX";
                    var r2E = new RouteData
                    {
                        routeId = "R-SZX-PEK-2E", origin = "SZX", destination = "PEK", distanceKm = 1940f,
                        aircraftType = "A320neo", flightNumber = "SZ100", registration = "B-9001",
                        depTime = "08:00", arrTime = "10:30", daysOfWeek = "每日", weeklyFlights = 7,
                        groundStaffAssigned = 3, baseDailyProfit = 10f, dailyProfit = 10f
                    };
                    r2E.SyncFlightsFromPrimary();
                    gs2E.activeRoutes = new List<RouteData> { r2E };

                    var needs2E = RoutePermitLogic.SlotNeeds(r2E);
                    Debug.Log($"R14 两端槽需求 {needs2E.Count} 条（期望 2：去程 SZX + 回程 PEK）= "
                        + string.Join("、", needs2E.ConvertAll(n =>
                            $"{RoutePermitLogic.LegName(n)} {n.airport}{StrategyManager.SlotBandName(n.band)} {n.flightNo}").ToArray()));

                    bool grandOnly = RoutePermitLogic.CanScheduleBand(gs2E, r2E, 0, true);
                    bool noGrandNoSlot = RoutePermitLogic.CanScheduleBand(gs2E, r2E, 0, false);
                    Debug.Log($"R14 祖父条款 → 祖父可飞={grandOnly}（期望 True） 关掉祖父即缺槽={noGrandNoSlot}（期望 False）");

                    int band2E = 1;
                    var bn2E = RoutePermitLogic.BandNeeds(r2E, band2E);
                    bool onlyOrigin = false, bothEnds = false;
                    if (bn2E.Count == 2)
                    {
                        RoutePermitLogic.ReserveSlot(gs2E, bn2E[0].airport, bn2E[0].band, r2E.routeId, "SZ101");
                        onlyOrigin = RoutePermitLogic.CanScheduleBand(gs2E, r2E, band2E, false);
                        RoutePermitLogic.ReserveSlot(gs2E, bn2E[1].airport, bn2E[1].band, r2E.routeId, "SZ102");
                        bothEnds = RoutePermitLogic.CanScheduleBand(gs2E, r2E, band2E, false);
                    }
                    Debug.Log($"R14 两端缺一不可 → 仅去程端可排={onlyOrigin}（期望 False） 两端齐备={bothEnds}（期望 True）"
                        + $"　两端={string.Join(" / ", bn2E.ConvertAll(n => $"{RoutePermitLogic.LegName(n)} {n.airport}{StrategyManager.SlotBandName(n.band)}").ToArray())}");

                    var gap2E = RoutePermitLogic.BandGapText(gs2E, r2E, 3);
                    Debug.Log($"R14 缺口归因 → 红眼波 {gap2E}");
                }
                finally
                {
                    GameObject.DestroyImmediate(goTwoEnd);
                }

                var fourFleet = new List<Aircraft>();
                for (int k = 1; k <= 4; k++)
                    fourFleet.Add(new Aircraft { registration = "B-301" + k, type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 3 });
                var gaps4 = OpsScheduleLogic.PrecheckSlots(rcSzx, fourFleet, new List<RouteData>(), out var feas4);
                Debug.Log($"R10 验收 4 架 → 可排 {feas4.Count}/4 段　缺口 {gaps4.Count} 个"
                          + $"\n　可排 [{string.Join("、", feas4.ToArray())}]"
                          + $"\n　缺口 [{string.Join(" ｜ ", gaps4.ConvertAll(g => g.slotLabel + " " + g.dep + " " + g.kind).ToArray())}]"
                          + $"\n　（夜间段受 PEK 23:00–06:00 宵禁限制，属预期）");

                var wideFleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-4001", type = "B787-9", isActive = true, status = AircraftStatuses.Airworthy, age = 2 }
                };
                var gapsW = OpsScheduleLogic.PrecheckSlots(rcSzx, wideFleet, new List<RouteData>(), out var feasW);
                Debug.Log($"R12 验收 宽体增援 → B787-9 单机可排 {feasW.Count} 段（期望 1：第 2 段将达 4 航段 > 增援硬上限 "
                          + $"{OpsRotationLogic.AugmentedSectorCap}，仍无法 2 段往返）"
                          + $"\n　缺口首个原因：{(gapsW.Count > 0 ? gapsW[0].reason : "无")}");

                // 长程（单段 > 9h）：外站过夜 —— 去程与回程分属两个值勤期，一段往返仍可排
                var rcLong = new RouteData
                {
                    routeId = "R-PEK-LHR", origin = "PEK", destination = "LHR", distanceKm = 8150f,
                    aircraftType = "B787-9", flightNumber = "SZ900", groundStaffAssigned = 5
                };
                int blockL = OpsRotationLogic.BlockOf(rcLong);
                var longFleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-6001", type = "B787-9", isActive = true, status = AircraftStatuses.Airworthy, age = 2 }
                };
                var gapsL = OpsScheduleLogic.PrecheckSlots(rcLong, longFleet, new List<RouteData>(), out var feasL);
                Debug.Log($"长程 外站过夜 → PEK–LHR 单程 {blockL} 分（>{OpsRotationLogic.LongHaulLegMinutes / 60}h，按外站过夜计）"
                          + $"　可排 {feasL.Count} 段（期望 1）　缺口 {gapsL.Count} 个（期望 3）"
                          + $"\n　可排 [{string.Join("、", feasL.ToArray())}]"
                          + $"\n　首个缺口：{(gapsL.Count > 0 ? gapsL[0].reason : "无")}"
                          + $"\n　（一段往返占 {blockL * 2 + OpsRotationLogic.TurnOf(rcLong)} 分 ≈ {(blockL * 2 + OpsRotationLogic.TurnOf(rcLong)) / 60f:0.#}h，"
                          + $"按往返合并计必然超值勤上限；按外站过夜拆分后去程 1 航段仅 {(blockL + OpsRotationLogic.ReportBeforeMinutes) / 60f:0.#}h）");

                // 长程回程宵禁：宵禁只把航班推迟到窗口外，而非取消（否则会出假宵禁冲突）
                bool longCurfew = OpsRotationLogic.CurfewBlockedLongHaul(rcLong, 6 * 60 + 30, out string longCurfewWhy);
                Debug.Log($"长程 回程宵禁 → PEK–LHR 06:30 出发被拒={longCurfew}（期望 False）"
                          + $"　原因=「{longCurfewWhy}」"
                          + $"\n　缓冲起点（外站休息后）06:30+{blockL}+{OpsRotationLogic.TurnOf(rcLong)}+30 = "
                          + $"{OpsRotationLogic.FormatMinute(6 * 60 + 30 + blockL + OpsRotationLogic.TurnOf(rcLong) + OpsRotationLogic.RotationGapMinutes)}"
                          + $"（落在 LHR 宵禁内）→ 自动推迟到 "
                          + $"{(OpsRotationLogic.TrySnapLeg("LHR", "PEK", 6 * 60 + 30 + blockL + OpsRotationLogic.TurnOf(rcLong) + OpsRotationLogic.RotationGapMinutes, blockL, OpsRotationLogic.LongHaulSnapHorizonMinutes, out int snappedDep, out _) ? OpsRotationLogic.FormatMinute(snappedDep) : "无合法时刻")}"
                          + $" 返场");
                bool longCurfewNight = OpsRotationLogic.CurfewBlockedLongHaul(rcLong, 22 * 60, out string longNightWhy);
                Debug.Log($"长程 去程宵禁 → PEK–LHR 22:00 出发被拒={longCurfewNight}（期望 True，去程按当日严格核）　原因=「{longNightWhy}」");

                // 双口径：一段往返的「值勤日」与「离港日」一致；跨夜班则不同（R4/R14）
                var planDual = new OpsRotationLogic.RotationPlan();
                planDual.Assign("B-3011", OpsRotationLogic.WindowOf(rcSzx, "06:30"));
                planDual.Assign("B-3011", OpsRotationLogic.WindowOf(rcSzx, "22:30"));
                var duties1 = planDual.DutiesOf("B-3011");
                Debug.Log($"值勤切分 双口径（R4）→ 06:30 与 22:30 两段往返被切为 {duties1.Count} 个值勤期（期望 2：间隔 ≥ "
                          + $"{OpsRotationLogic.MinRestMinutes / 60f:0.#}h）"
                          + $"\n　{planDual.DutySummary("B-3011", "A320neo")}");
                var endMin = planDual.DutyEnd("B-3011");
                Debug.Log($"值勤切分 跨夜 → 当日最后落地 {OpsRotationLogic.FormatMinute(endMin)}"
                          + $"（≥1440 表示跨到次日：值勤计入出发日 → 「值勤日」；航班量计入落地日 → 「离港日」）");

                // ================= R4 验收：跨夜值勤按报到日归组，航班量按离港日统计 =================
                int rep1330 = 23 * 60 + 30;               // 前一晚 23:30 报到
                int rep0700 = 7 * 60;                     // 当日 07:00 报到
                int dep0030 = 30;                         // 00:30 离港（钟点早于报到 → 属跨夜）
                int dep0830 = 8 * 60 + 30;                // 08:30 离港
                Debug.Log($"R4 双口径 跨夜 → 报到 {OpsRotationLogic.FormatMinute(rep1330)} / 离港 {OpsRotationLogic.FormatMinute(dep0030)}"
                          + $"　值勤日偏移={OpsRotationLogic.DutyDayOf(rep1330)}　离港日偏移={OpsRotationLogic.DepartureDayOf(rep1330, dep0030)}"
                          + $"　同日={OpsRotationLogic.SameAttributionDay(rep1330, dep0030)}（期望 False）"
                          + $"\n　{OpsRotationLogic.AttributionLabel(rep1330, dep0030)}");
                Debug.Log($"R4 双口径 常规 → 报到 {OpsRotationLogic.FormatMinute(rep0700)} / 离港 {OpsRotationLogic.FormatMinute(dep0830)}"
                          + $"　同日={OpsRotationLogic.SameAttributionDay(rep0700, dep0830)}（期望 True）"
                          + $"　{OpsRotationLogic.AttributionLabel(rep0700, dep0830)}");

                var routeDual = new RouteData
                {
                    routeId = "R-DUAL", origin = "SZX", destination = "PEK", distanceKm = 1950f,
                    aircraftType = "A320neo", flightNumber = "SZ500", weeklyFlights = 7, daysOfWeek = "每日"
                };
                routeDual.flights = new List<RouteFlight>
                {
                    new RouteFlight { registration = "B-5001", depTime = "23:30", arrTime = "01:48", slotLabel = "红眼", weeklyFlights = 7, dutyStart = rep1330 - OpsRotationLogic.ReportBeforeMinutes },
                    new RouteFlight { registration = "B-5001", depTime = "00:30", arrTime = "02:48", slotLabel = "红眼", weeklyFlights = 7, dutyStart = rep1330 - OpsRotationLogic.ReportBeforeMinutes }
                };
                var dutyGroups = routeDual.DutyPeriods();
                Debug.Log($"R4 双口径 归组 → {routeDual.DutyAttributionLine()}"
                          + $"\n　值勤期数={dutyGroups.Count}（期望 1：23:30 与 00:30 两班属同一值勤，不被午夜切开）"
                          + $"　首期班次={dutyGroups[0].Count}（期望 2）"
                          + $"\n　逐班：{OpsRotationLogic.AttributionLabel(RouteData.ReportMinutesOf(routeDual.flights[0]), RouteData.DepartureMinutesOf(routeDual.flights[0]))}"
                          + $"　｜　{OpsRotationLogic.AttributionLabel(RouteData.ReportMinutesOf(routeDual.flights[1]), RouteData.DepartureMinutesOf(routeDual.flights[1]))}");

                var routeR14 = new RouteData
                {
                    routeId = "R-R14", origin = "SZX", destination = "PEK", distanceKm = 1950f,
                    aircraftType = "A320neo", weeklyFlights = 3, daysOfWeek = "一三五"
                };
                routeR14.flights = new List<RouteFlight>
                {
                    new RouteFlight
                    {
                        registration = "B-5101", depTime = "00:30", arrTime = "02:48",
                        daysOfWeek = "一三五", weeklyFlights = 3, dutyStart = 23 * 60 + 30
                    }
                };
                var depDays = FlightScheduleFormat.DaysSummary(routeR14.WeekdayOpsDeparture());
                var dutyDays = FlightScheduleFormat.DaysSummary(routeR14.WeekdayOpsDuty());
                Debug.Log($"R14 双数字 → {routeR14.DualWeeklyLine()}"
                          + $"\n　离港周={routeR14.WeeklyFlightsDeparture()} 班期={depDays}（期望 周一/三/五）"
                          + $"　值勤周={routeR14.WeeklyFlightsDuty()} 班期={dutyDays}（期望 周日/二/四：报到在前一日）");

                // —— 换季运力评审：试算 → 呈报 → 批准执行 ——
                var seasonPlan = SeasonReviewLogic.Build(gsD, CapacityDirectiveLogic.Parse("本季再加密一班"), false);
                Debug.Log($"换季评审 试算 → 建议加密 {seasonPlan.densify.Count} 条 / 减班 {seasonPlan.trim.Count} 条 / 释放 {seasonPlan.freedCount} 架"
                          + $"；加密清单=[{seasonPlan.DensifyCsv}]");
                bool seasonQueued = InboxManager.QueueSeasonReview(gsD, seasonPlan, null, false);
                var seasonMail = gsD.inbox.Find(x => x != null && x.action == InboxManager.SeasonReviewAction);
                Debug.Log($"换季评审 呈报 → 入箱={seasonQueued}（期望 True） 主题={seasonMail?.subject}"
                          + $" 需决策={InboxManager.RequiresDecision(seasonMail)}（期望 True）");
                if (seasonMail != null)
                {
                    int beforeSeason = ra.FlightCount;
                    InboxManager.ApproveSeasonReview(gsD, seasonMail, out string seasonMsg);
                    Debug.Log($"换季评审 执行 → {seasonMsg}");
                    Debug.Log($"换季评审 执行后 → 该航线 {beforeSeason}→{ra.FlightCount} 班（期望 ≥{beforeSeason}）");
                }
            }
            catch (Exception de)
            {
                Debug.LogWarning($"运力指令用例异常（不阻断后续）：{de.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goD);
            }

            // —— 招募岗位口径（n112 职业介绍所「过滤 · 招聘条件」依赖）——
            // UI 里可选的三个岗位必须真的能推导出编制，否则会被 LaunchRecruitment 判成「编制 0」而招募失效；
            // 关键字一旦在 OperationalCategory 里被改名，这里要立刻炸出来。
            try
            {
                int estPilot = HrEconomics.OperationalEstablishment("飞行员", 10, 2, 1800);
                int estCabin = HrEconomics.OperationalEstablishment("乘务员", 10, 2, 1800);
                int estEng = HrEconomics.OperationalEstablishment("机务工程师", 10, 2, 1800);
                int estNone = HrEconomics.OperationalEstablishment("签派员", 10, 2, 1800);
                Debug.Log($"招募岗位 编制(10 架·2 宽体·1800 座) → 飞行员={estPilot}（期望 84 = 10×8 + 2×2）"
                          + $" 乘务员={estCabin}（期望 184 = 1800/50×4 + 10×4） 机务工程师={estEng}（期望 60 = 10×6）"
                          + $" 签派员={estNone}（期望 0：非运行编制岗，走 StaffEstablishment 编制席）");
                Debug.Log($"招募岗位 类别 → {HrEconomics.OperationalCategory("飞行员")}/{HrEconomics.OperationalCategory("乘务员")}"
                          + $"/{HrEconomics.OperationalCategory("机务工程师")}/{HrEconomics.OperationalCategory("签派员")}"
                          + "（期望 Pilot/CabinCrew/Engineer/None）");

                // 条件折叠：渠道候选人必须能进渠道视图，总监候选人必须能进待聘总监视图。
                // 两者共用总监闸门时，渠道页签会永远为空（这正是 2026-10-08 修掉的回归）。
                var unlisted = new PersonnelData { id = "T-1", fullName = "测试候选", rank = "专员", onMarket = true };
                var listedCampus = new PersonnelData { id = "T-2", fullName = "测试校招", rank = "专员", onMarket = true, source = HrEconomics.SourceCampus };
                var listedHead = new PersonnelData { id = "T-3", fullName = "测试猎头", rank = "专员", onMarket = true, source = HrEconomics.SourceHeadhunt };
                var director = new PersonnelData { id = "T-4", fullName = "测试总监", rank = "负责人", onMarket = true, source = HrEconomics.SourceMarket };
                var inService = new PersonnelData { id = "T-5", fullName = "在职者", rank = "负责人", onMarket = false, source = HrEconomics.SourceMarket };
                string[] views = { HrEconomics.SourceMarket, HrEconomics.SourceCampus, HrEconomics.SourceHeadhunt, HrEconomics.SourceInternal };
                string[] names = { "待聘总监", "校招", "猎头", "内部转岗" };
                var sbFold = new System.Text.StringBuilder("条件折叠 判定 → ");
                for (int v = 0; v < views.Length; v++)
                {
                    sbFold.Append($"{names[v]}=[总监{(StaffRecruitment.AcceptedInSourceView(director, views[v]) ? "✓" : "×")}"
                        + $"校招{(StaffRecruitment.AcceptedInSourceView(listedCampus, views[v]) ? "✓" : "×")}"
                        + $"猎头{(StaffRecruitment.AcceptedInSourceView(listedHead, views[v]) ? "✓" : "×")}"
                        + $"在职{(StaffRecruitment.AcceptedInSourceView(inService, views[v]) ? "✓" : "×")}] ");
                }
                Debug.Log(sbFold.ToString());
                Debug.Log("条件折叠 期望 → "
                    + "待聘总监=[总监✓校招×猎头×在职×]；校招=[总监×校招✓猎头×在职×]；"
                    + "猎头=[总监×校招×猎头✓在职×]；内部转岗=[总监×校招×猎头×在职×]"
                    + $"；无来源专员(按市场来源处理)={StaffRecruitment.AcceptedInSourceView(unlisted, HrEconomics.SourceCampus)}（期望 False）"
                    + "。注：内部转岗行由 HrManager.InternalCandidates 另一路推导，不经本判定。");
            }
            catch (Exception he)
            {
                Debug.LogWarning($"招募岗位口径用例异常（不阻断后续）：{he.Message}");
            }

            // —— 候选人筛选：本国 / 外籍判定（职业介绍所「② 候选人筛选」依赖）——
            // 必须按枢纽所在国判，不能硬编码「中国」：枢纽在利雅得/新加坡时，
            // 硬编码会把整个公司判成外籍，筛选器直接反了。
            var goF = new GameObject("__foreign_filter_test_gs__");
            try
            {
                var gsF = goF.AddComponent<GameState>();
                var cnStaff = new PersonnelData { id = "T-CN", nationality = "中国" };
                var usStaff = new PersonnelData { id = "T-US", nationality = "美国" };
                var noNat = new PersonnelData { id = "T-NA", nationality = null };

                gsF.hubIata = "PEK";
                string hubCn = PersonnelGenerator.HomeCountry(gsF);
                bool pekCn = PersonnelGenerator.IsForeign(cnStaff, gsF);
                bool pekUs = PersonnelGenerator.IsForeign(usStaff, gsF);

                gsF.hubIata = "SIN";
                string hubSg = PersonnelGenerator.HomeCountry(gsF);
                bool sinCn = PersonnelGenerator.IsForeign(cnStaff, gsF);
                bool sinNoNat = PersonnelGenerator.IsForeign(noNat, gsF);

                Debug.Log($"本国外籍 判定(枢纽={hubCn}) → 中国籍={pekCn}（期望 False） 美国籍={pekUs}（期望 True）");
                Debug.Log($"本国外籍 判定(枢纽={hubSg}) → 中国籍={sinCn}（期望 True：同一人换枢纽应翻转）"
                          + $" 无国籍={sinNoNat}（期望 False：不判外籍）");
                Debug.Log("本国外籍 结论 → 判定随枢纽国变化，未硬编码「中国」；"
                          + "若两行结论相同即说明硬编码回归，职业介绍所「② 候选人筛选」会整体反相。");
            }
            catch (Exception fe)
            {
                Debug.LogWarning($"本国外籍判定用例异常（不阻断后续）：{fe.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goF);
            }

            // —— 培训体系：等级表 / 入册门槛 / 证照目录（2026-10-08 重构）——
            // 三个必须成立的不变量：① 培训中心 3 级封顶且越界报价不可支付（旧漏洞是 2 级后免费无限升）；
            // ② 入册门槛必须覆盖开局主管档（潜在 0.62），否则「启动高潜管理培训」开局无人生效；
            // ③ 两套「高潜」口径各自成立，不得再出现「培训选人 ≠ 人才档案口径」的静默分叉。
            try
            {
                Debug.Log($"培训 中心等级表 → 最高级={HrEconomics.TrainingCenterMaxLevel}（期望 3）"
                          + $" 工期(天)=[{HrEconomics.TrainingCenterBuildDays(1)},{HrEconomics.TrainingCenterBuildDays(2)},{HrEconomics.TrainingCenterBuildDays(3)}]（期望递增）"
                          + $" 季度常量一致={HrEconomics.DaysPerQuarter == GameState.DaysPerQuarter}（期望 True）");

                var sbLv = new System.Text.StringBuilder("培训 等级明细 → ");
                bool costMonotonic = true, daysMonotonic = true;
                float prevCost = 0f;
                int prevDays = 0;
                for (int lv = 1; lv <= HrEconomics.TrainingCenterMaxLevel; lv++)
                {
                    float cost = HrEconomics.TrainingCenterCostWan(lv);
                    int days = HrEconomics.TrainingCenterBuildDays(lv);
                    if (cost <= prevCost) costMonotonic = false;
                    if (days <= prevDays) daysMonotonic = false;
                    prevCost = cost; prevDays = days;
                    sbLv.Append($"[{lv}级 {HrEconomics.TrainingCenterName(lv)} {cost:F0}万/{days}天"
                                + $" 进度{HrEconomics.TrainingProgressDelta(lv):F2} 折扣×{HrEconomics.TrainingCostFactor(lv):F2}] ");
                }
                Debug.Log(sbLv.ToString());
                Debug.Log($"培训 造价递增={costMonotonic}（期望 True） 工期递增={daysMonotonic}（期望 True）");

                // 旧漏洞回归防线：越界等级与 0 元一律不可支付，且每级都有真实报价
                bool allPayable = true;
                for (int lv = 1; lv <= HrEconomics.TrainingCenterMaxLevel; lv++)
                    if (!HrEconomics.IsUpgradeCostPayable(HrEconomics.TrainingCenterCostWan(lv))) allPayable = false;
                bool overflowRejected = !HrEconomics.IsUpgradeCostPayable(HrEconomics.TrainingCenterCostWan(HrEconomics.TrainingCenterMaxLevel + 1));
                bool zeroRejected = !HrEconomics.IsUpgradeCostPayable(0f);
                Debug.Log($"培训 免费升级防线 → 各级报价可支付={allPayable}（期望 True）"
                          + $" 越界被拒={overflowRejected}（期望 True） 0元被拒={zeroRejected}（期望 True）"
                          + "。旧实现 `level switch { 1=>800f, 2=>1500f, _=>0f }` 在 2 级后收费 0 且无上限。");

                // 入册门槛必须覆盖开局主管档（PersonnelGenerator.Make 给非专员档 0.62）
                bool gateCoversSupervisor = HrEconomics.TrainingEnrollMinPotential <= PersonnelGenerator.SupervisorPotential;
                bool gateBelowHipo = HrEconomics.TrainingEnrollMinPotential < SuccessionLogic.HipoMin;
                Debug.Log($"培训 入册门槛={HrEconomics.TrainingEnrollMinPotential:F2}"
                          + $" 覆盖开局主管档(潜力{PersonnelGenerator.SupervisorPotential:F2})={gateCoversSupervisor}（期望 True）"
                          + $" 低于人才档案高潜线({SuccessionLogic.HipoMin:F2})={gateBelowHipo}（期望 True：否则开局无人可训）");

                // 晋升门槛量纲：必须吃「培训累积增益」，不能吃 efficiencyBonus 本身（后者基准 1.0，条件会恒真）
                bool promotionOk = HrEconomics.CanPromoteFromTraining("专员", 0.91f, HrEconomics.PromotionTrainingGain)
                                   && !HrEconomics.CanPromoteFromTraining("专员", 0.91f, HrEconomics.PromotionTrainingGain - 0.01f)
                                   && !HrEconomics.CanPromoteFromTraining("主管", 0.99f, 9f);
                float defaultEff = new PersonnelData().efficiencyBonus;
                Debug.Log($"培训 晋升门槛量纲 → 判定正确={promotionOk}（期望 True）"
                          + $" 门槛增益={HrEconomics.PromotionTrainingGain:F2}"
                          + $" 而 efficiencyBonus 默认值={defaultEff:F2}（若拿后者直接比门槛会恒真，即旧缺陷）");

                // 证照目录：id 唯一 + 门槛分档 + 工期 ≥1 季
                var ids = new HashSet<string>();
                bool idUnique = true, ladderOk = true, durationOk = true;
                var sbCert = new System.Text.StringBuilder("证照 目录 → ");
                for (int i = 0; i < CertificationCatalog.All.Length; i++)
                {
                    var d = CertificationCatalog.All[i];
                    if (!ids.Add(d.id)) idUnique = false;
                    if (d.minCenterLevel < 1 || d.minCenterLevel > HrEconomics.TrainingCenterMaxLevel) ladderOk = false;
                    if (CertificationLogic.DurationQuarters(d) < 1) durationOk = false;
                    sbCert.Append($"[{d.name} {d.costWan:F0}万 {CertificationLogic.DurationQuarters(d)}季 {(d.validMonths == 0 ? "长期" : d.validMonths + "月")} 需{d.minCenterLevel}级] ");
                }
                Debug.Log(sbCert.ToString());
                Debug.Log($"证照 目录自检 → id唯一={idUnique}（期望 True） 门槛落在 1~3 级={ladderOk}（期望 True）"
                          + $" 工期≥1季={durationOk}（期望 True） 总数={CertificationCatalog.All.Length}");

                // 到期判定：有有效期的证到期后 HasValid 必须翻假
                var holder = new PersonnelData { id = "T-CERT", department = "OPS", jobTitle = "乘务员" };
                var cabin = CertificationCatalog.Find(CertificationCatalog.CabinSafety);
                var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                CertificationLogic.Grant(holder, cabin, t0);
                bool validNow = CertificationLogic.HasValid(holder, CertificationCatalog.CabinSafety, t0.AddMonths(1));
                bool validAfter = CertificationLogic.HasValid(holder, CertificationCatalog.CabinSafety, t0.AddMonths(cabin.validMonths + 1));
                Debug.Log($"证照 到期 → 有效期内={validNow}（期望 True） 过期后={validAfter}（期望 False）"
                          + $" 版本摘要=「{CertificationLogic.Summary(holder, t0.AddMonths(1))}」");

                // —— 全公司总人数：与人力成本同构地随机队派生（2026-10-08 对齐）——
                // 旧口径是固定 450，与机队脱节：20 架机队的「让步」成本被按 450 人低报约 4 倍。
                int hc0 = HrEconomics.EffectiveHeadcount(0, 0f, 0);
                int hc5 = HrEconomics.EffectiveHeadcount(5, 0f, 0);
                int hc20 = HrEconomics.EffectiveHeadcount(20, 0f, 0);
                int hcLegacy = HrEconomics.EffectiveHeadcount(10, 45f, 0);   // 旧档 450 人 / 10 机 → 45 人/机
                int hcAdd = HrEconomics.EffectiveHeadcount(5, 0f, 12);
                Debug.Log($"人数 随机队派生 → 0机={hc0}（期望 0） 5机={hc5}（期望 450·锚点） 20机={hc20}（期望 1800）"
                          + $" 旧档折算(10机×45人/机)={hcLegacy}（期望 450·绝对值延续） +招聘增量12={hcAdd}（期望 462）");
                Debug.Log($"人数 与成本同构 → 每机人数={HrEconomics.HeadcountPerAircraft:F0} 每机费率={HrEconomics.LaborCostPerAircraftWan:F0}万"
                          + $" 人均={HrEconomics.LaborCostPerAircraftWan / HrEconomics.HeadcountPerAircraft:F2}万/人/年"
                          + $" 工会让步 450人={HrEconomics.UnionCostWan(0, 450):F0}万 vs 1800人={HrEconomics.UnionCostWan(0, 1800):F0}万（期望 4×）");

                // —— HR 存档接通（2026-10-08）：HrState 必须先能无损过一遍 JsonUtility ——
                // 此前 gs.hr 从不落盘，在培进度/培训中心等级/工会满意度跨存档全部丢失。
                // JsonUtility 对「嵌套 [Serializable] / List<T> / List<string>」有静默丢字段的风险，
                // 故逐字段核对往返一致性 —— 免得「接上了却写不进去」。
                var hrSrc = new HrState
                {
                    employeeSatisfaction = 63.5f,
                    totalHeadcount = 450,
                    headcountPerAircraft = 90f,
                    headcountAdditive = 12,
                    headcountMigrated = true,
                    vacancyRate = 0.11f,
                    strikeRisk = 27f,
                    trainingRoi = 1.42f,
                    trainingBudgetWan = 620f,
                    trainingCenterLevel = 2,
                    trainingCenterBuildLevel = 3,
                    trainingCenterBuildDoneTicks = 638700000000000000L,
                    laborCostPerAircraftWan = 258f,
                    laborCompAdditiveWan = 45f,
                    laborRateMigrated = true,
                    lastTrainingNote = "结业 3 人",
                    lastCompNote = "年度调薪 +3.5%",
                    talentIndex = new TalentIndex { pilotShortage = 66f, engineerShortage = 51f, cabinShortage = 38f },
                    union = new UnionState { satisfaction = 44f, strikeCountdown = 6, cbaExpiresIn = 9 }
                };
                hrSrc.trainings.Add(new TrainingProgram
                {
                    name = "高潜管理培训", enrolled = 2, completionRate = 0.5f, costWan = 180f,
                    track = "people", graduated = false, certId = "cabin_safety",
                    enrolledTicks = 638600000000000000L, durationQuarters = 3
                });
                hrSrc.trainings[0].memberIds.Add("P-001");
                hrSrc.trainings[0].memberIds.Add("P-002");
                hrSrc.vacancies.Add(new Vacancy
                {
                    position = "飞行员", needed = 12, hired = 5, urgency = 2, channel = 1,
                    startUtcTicks = 638610000000000000L, dueUtcTicks = 638620000000000000L, inPool = 3
                });

                var hrRt = JsonUtility.FromJson<HrSave>(JsonUtility.ToJson(new HrSave { state = hrSrc }))?.state;
                var rtTrain = hrRt != null && hrRt.trainings != null && hrRt.trainings.Count == 1 ? hrRt.trainings[0] : null;
                var rtVac = hrRt != null && hrRt.vacancies != null && hrRt.vacancies.Count == 1 ? hrRt.vacancies[0] : null;
                bool rtOk = hrRt != null && rtTrain != null && rtVac != null
                    && Mathf.Abs(hrRt.employeeSatisfaction - 63.5f) < 0.001f
                    && hrRt.headcountPerAircraft == 90f && hrRt.headcountAdditive == 12 && hrRt.headcountMigrated
                    && hrRt.trainingCenterLevel == 2 && hrRt.trainingCenterBuildLevel == 3
                    && hrRt.trainingCenterBuildDoneTicks == 638700000000000000L
                    && Mathf.Abs(hrRt.laborCostPerAircraftWan - 258f) < 0.001f
                    && Mathf.Abs(hrRt.laborCompAdditiveWan - 45f) < 0.001f && hrRt.laborRateMigrated
                    && hrRt.talentIndex != null && Mathf.Abs(hrRt.talentIndex.pilotShortage - 66f) < 0.001f
                    && hrRt.union != null && Mathf.Abs(hrRt.union.satisfaction - 44f) < 0.001f
                    && hrRt.union.strikeCountdown == 6 && hrRt.union.cbaExpiresIn == 9
                    && hrRt.lastTrainingNote == "结业 3 人" && hrRt.lastCompNote == "年度调薪 +3.5%"
                    && rtTrain.enrolled == 2 && Mathf.Abs(rtTrain.completionRate - 0.5f) < 0.001f
                    && rtTrain.track == "people" && rtTrain.certId == "cabin_safety"
                    && rtTrain.enrolledTicks == 638600000000000000L && rtTrain.durationQuarters == 3
                    && rtTrain.memberIds != null && rtTrain.memberIds.Count == 2 && rtTrain.memberIds[1] == "P-002"
                    && rtVac.needed == 12 && rtVac.hired == 5 && rtVac.inPool == 3
                    && rtVac.channel == 1 && rtVac.urgency == 2 && rtVac.dueUtcTicks == 638620000000000000L;
                Debug.Log($"人力 存档往返 → 全字段无损={rtOk}（期望 True）"
                          + $" 培训=[{rtTrain?.name} 名单{rtTrain?.memberIds?.Count}人 证照{rtTrain?.certId} {rtTrain?.durationQuarters}季]"
                          + $" 空缺=[{rtVac?.position} {rtVac?.hired}/{rtVac?.needed} 池{rtVac?.inPool}]"
                          + $" 中心={hrRt?.trainingCenterLevel}级(在建{hrRt?.trainingCenterBuildLevel})"
                          + $" 工会满意={hrRt?.union?.satisfaction}");
                // 旧档（无本键）：空串必须抛异常，由 SaveManager catch 后落 new HrState，而不是返回半个对象
                bool legacyThrows = false;
                try { JsonUtility.FromJson<HrSave>(""); } catch { legacyThrows = true; }
                Debug.Log($"人力 旧档兜底 → 空串读取抛异常={legacyThrows}（期望 True；SaveManager catch → new HrState → 迁移）");

                // 旧档迁移：certs 为 null 不得抛异常
                var legacyCert = new PersonnelData { id = "T-LEGACY", certs = null };
                CertificationLogic.EnsureCerts(legacyCert);
                Debug.Log($"证照 旧档迁移 → null 归一为非空={legacyCert.certs != null}（期望 True）"
                          + $" 持证判定={CertificationLogic.Has(legacyCert, CertificationCatalog.PilotLicense)}（期望 False）");

                // 入职自带资质：运行序列凭岗位持 1 级基础证；进阶证与国际运行资质永不自动授予
                // （后者会自动打开国际航线准入的第二条路径，等于白送一条法规通道）。
                var hireT0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var stPilot = new PersonnelData { id = "T-HIRE-P", department = "OPS", jobTitle = "飞行员", isOperational = true };
                var stCabin = new PersonnelData { id = "T-HIRE-C", department = "OPS", jobTitle = "乘务员", isOperational = true };
                var stMnt = new PersonnelData { id = "T-HIRE-M", department = "MNT", jobTitle = "机务工程师", isOperational = true };
                var stOffice = new PersonnelData { id = "T-HIRE-O", department = "OPS", jobTitle = "飞行员", isOperational = false };
                int nP = CertificationLogic.GrantStarterCerts(stPilot, hireT0);
                int nC = CertificationLogic.GrantStarterCerts(stCabin, hireT0);
                int nM = CertificationLogic.GrantStarterCerts(stMnt, hireT0);
                int nO = CertificationLogic.GrantStarterCerts(stOffice, hireT0);
                int nAgain = CertificationLogic.GrantStarterCerts(stPilot, hireT0.AddDays(1));
                bool noAdvanced = !CertificationLogic.Has(stPilot, CertificationCatalog.IntlOps)
                                  && !CertificationLogic.Has(stPilot, CertificationCatalog.PilotLicense)
                                  && !CertificationLogic.Has(stMnt, CertificationCatalog.MaintRelease);
                Debug.Log($"证照 入职自带 → 飞行员{nP}本[{CertificationLogic.Summary(stPilot, hireT0)}]"
                          + $" 乘务员{nC}本 机务工程师{nM}本 关键岗{nO}本（期望 0：管理序列不发证）"
                          + $" 重复调用再发{nAgain}本（期望 0：幂等） 进阶/国际证未自动发={noAdvanced}（期望 True）");

                // 国际运行资质第二条路径：无海外机组时，持证运行序列人员应能满足准入
                var goCert = new GameObject("__cert_gate_test_gs__");
                try
                {
                    var gsCert = goCert.AddComponent<GameState>();
                    gsCert.staffPool = new List<PersonnelData>();
                    bool before = RoutePermitLogic.HasOverseasQualification(gsCert);
                    var pilotCert = new PersonnelData
                    {
                        id = "T-CERTPILOT", department = "OPS", jobTitle = "飞行员",
                        isOperational = true, onMarket = false
                    };
                    gsCert.staffPool.Add(pilotCert);
                    CertificationLogic.Grant(pilotCert, CertificationCatalog.Find(CertificationCatalog.IntlOps), gsCert.SimUtc);
                    bool after = RoutePermitLogic.HasOverseasQualification(gsCert);
                    Debug.Log($"证照 国际运行资质第二条路径 → 海外机组={gsCert.OverseasCrewCount} 考取前具备={before}（期望 False）"
                              + $" 自建考取后具备={after}（期望 True） 持证机组={RoutePermitLogic.IntlOpsCertifiedCrewCount(gsCert)}（期望 1）");
                }
                finally
                {
                    GameObject.DestroyImmediate(goCert);
                }

                // —— 开通航线表单默认端点：基地 = 玩家选定的总部基地（2026-10-09）——
                // 此前 Show() 把基地硬编码为 "PEK"，与玩家在开局/接管时选的基地无关：
                // 忘了改会以非枢纽为始发地提交，IsFromHomeBase 返回 false，
                // 基地始发干线**本应占用的时刻槽被跳过**（枢纽成本被静默绕过）。
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints("CAN", out string dpCan, out string ddCan);
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints(" PEK ", out string dpTrim, out string ddTrim);
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints("sin", out string dpLower, out string ddLower);
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints("SHA", out string dpSha, out string ddSha);
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints(null, out string dpNull, out string ddNull);
                AirlineCEO.UI.OpenRouteModalUI.DefaultEndpoints("   ", out string dpBlank, out string ddBlank);
                bool defBaseOk = dpCan == "CAN" && dpTrim == "PEK" && dpLower == "SIN"
                                 && dpSha == "SHA" && dpNull == "PEK" && dpBlank == "PEK";
                bool defDiffOk = ddCan != dpCan && ddTrim != dpTrim && ddLower != dpLower
                                 && ddSha != dpSha && ddNull != dpNull && ddBlank != dpBlank;
                bool defCatalogOk = !string.IsNullOrEmpty(AirportCatalog.FindByCode(dpCan).code)
                                    && !string.IsNullOrEmpty(AirportCatalog.FindByCode(ddCan).code);
                Debug.Log($"开线表单 默认端点 → 基地取自总部[广州=CAN 空白归一={dpTrim} 小写归一={dpLower} 虹桥={dpSha} 空={dpNull} 全空白={dpBlank}]={defBaseOk}（期望 True）"
                          + $" 基地≠目的地={defDiffOk}（期望 True，否则默认值自身落在「基地与目的地不能相同」拒绝分支）"
                          + $" 均收录于机场目录={defCatalogOk}（期望 True）"
                          + $"\n  注：玩家总部={dpCan} 时旧行为会以 PEK 为始发地，IsFromHomeBase 返回 false → 枢纽时刻槽被跳过");
            }
            catch (Exception te)
            {
                Debug.LogWarning($"培训体系用例异常（不阻断后续）：{te.Message}");
            }

            EditorUtility.DisplayDialog("Logic Self Tests", "结果已逐行输出到 Console（Window → General → Console）", "确定");
        }
    }
}
#endif
