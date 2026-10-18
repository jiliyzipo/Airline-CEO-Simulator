#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
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

            // —— 班次模型：需求 × 枢纽规模 × 机型座级 × 航距 → 周班次 1–28（取代散落各处的 km>=5000 ? 4 : 7）——
            var freqRoute = new RouteData
            {
                routeId = "PEK-SHA-freq", origin = "PEK", destination = "SHA",
                distanceKm = 1080f, aircraftType = "A320neo", registration = "B-1001",
                dailyProfit = -0.40f, baseDailyProfit = -0.40f
            };
            var freqPlan = RouteFrequencyModel.Materialize(freqRoute);
            Debug.Log($"班次模型 PEK-SHA 1080km 窄体 → 周 {freqPlan.weeklyFlights} 班（{freqPlan.daysOfWeek}，{freqPlan.waves} 波）"
                      + $"　需求 {freqPlan.demandPaxPerDay:F1} 人/日，单机上限 {freqPlan.flightsByAircraft:F2} 班/日（期望 ≥7 班）");
            Debug.Log($"班次模型 {RouteFrequencyModel.Explain("PEK", "JFK", 11000f, "B787-9")}（期望 ≤7 班）");

            float freqGross = 0f;
            for (int i = 0; i < freqRoute.flights.Count; i++) freqGross += freqRoute.flights[i].dailyProfit;
            freqRoute.RecalcDailyProfit();
            Debug.Log($"班次模型 不变量 → Σ班次毛贡献 {freqGross:F2} == 航线毛贡献 {freqRoute.GrossDailyProfit:F2}"
                      + $"　Recalc 幂等 {freqRoute.dailyProfit:F2}（期望 -0.40）");

            // 全航司班次分布：确认不再是「所有航司清一色 7 班」
            var freqHist = new Dictionary<int, int>();
            int freqAirlines = 0, freqAllSeven = 0, freqRouteCount = 0;
            foreach (var ap in PresetAirlines.AllSelectable())
            {
                var pv = TakeoverWorldBuilder.BuildPreview(ap != null ? ap.id : null);
                if (pv == null || pv.routes == null || pv.routes.Count == 0) continue;
                freqAirlines++;
                bool everySeven = true;
                for (int i = 0; i < pv.routes.Count; i++)
                {
                    int w = pv.routes[i].weeklyFlights;
                    freqRouteCount++;
                    freqHist[w] = (freqHist.TryGetValue(w, out int c) ? c : 0) + 1;
                    if (w != 7) everySeven = false;
                }
                if (everySeven) freqAllSeven++;
            }
            var freqKeys = new List<int>(freqHist.Keys);
            freqKeys.Sort();
            var freqText = new System.Text.StringBuilder();
            for (int i = 0; i < freqKeys.Count; i++)
                freqText.Append(freqKeys[i]).Append("班×").Append(freqHist[freqKeys[i]]).Append("　");
            Debug.Log($"班次分布（{freqAirlines} 家航司 / {freqRouteCount} 条航线）→ {freqText}");
            Debug.Log($"全 7 班的航司 {freqAllSeven} / {freqAirlines}（期望明显小于全部，说明班次已按需求分化）");

            // —— 目的地池枢纽本地化：海外基地也必须有一张本地短程网 ——
            // 旧池写死「国内 24 场 → 区域 9 场 → 远程 8 场」，伦敦/巴黎基地的整条网络都是洲际远程。
            int bandNear = 0, bandMid = 0, bandFar = 0;
            foreach (var ap in PresetAirlines.AllSelectable())
            {
                var pv = TakeoverWorldBuilder.BuildPreview(ap != null ? ap.id : null);
                if (pv == null || pv.routes == null) continue;
                for (int i = 0; i < pv.routes.Count; i++)
                {
                    float km = pv.routes[i].distanceKm;
                    if (km > 0f && km <= EconomyBalanceParams.FreqBandNearKm) bandNear++;
                    else if (km <= EconomyBalanceParams.FreqBandMidKm) bandMid++;
                    else bandFar++;
                }
            }
            int bandTotal = bandNear + bandMid + bandFar;
            float bandNearShare = bandTotal > 0 ? bandNear * 100f / bandTotal : 0f;
            Debug.Log($"航距分带（{bandTotal} 条）→ 近程(≤{EconomyBalanceParams.FreqBandNearKm:F0}km) {bandNear}"
                      + $"　中程(≤{EconomyBalanceParams.FreqBandMidKm:F0}km) {bandMid}　远程 {bandFar}"
                      + $"　近程占比 {bandNearShare:F0}%（期望 ≥30%：短程高频网是枢纽骨干）");

            // 六大洲各取一个枢纽：任一洲的目录缺口都会在这里暴露为「近程 0 条」。
            foreach (var hub in new[] { "CDG", "ICN", "LHR", "ATL", "GRU", "JNB", "SYD" })
            {
                AirlineProfile hp = null;
                foreach (var ap in PresetAirlines.AllSelectable())
                    if (ap != null && string.Equals(ap.hubAirport, hub, StringComparison.OrdinalIgnoreCase))
                    { hp = TakeoverWorldBuilder.BuildPreview(ap.id); break; }
                if (hp == null || hp.routes == null || hp.routes.Count == 0)
                { Debug.Log($"目的地池 {hub} → 未生成（期望有航线）"); continue; }

                int hubNear = 0;
                float minKm = float.MaxValue, maxKm = 0f;
                for (int i = 0; i < hp.routes.Count; i++)
                {
                    float km = hp.routes[i].distanceKm;
                    if (km > 0f && km <= EconomyBalanceParams.FreqBandNearKm) hubNear++;
                    if (km < minKm) minKm = km;
                    if (km > maxKm) maxKm = km;
                }
                Debug.Log($"目的地池 {hub} → {hp.routes.Count} 条航线，近程 {hubNear} 条，航距 {minKm:F0}–{maxKm:F0} km"
                          + "（期望近程 ≥1，且航距不再清一色落在 5000 km 以上）");
            }

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
                // staffPool 此前没初始化，而下面「国际运行资质 → 补一名海外机组」要往池里加人：
                // 于是整段（资质/机组覆盖率/节点排期/进度表）在 NRE 里被 catch 掉，
                // **一条都不执行**，只留下一行「不阻断后续」的告警 —— 看起来是绿的。
                gsP.staffPool = new List<PersonnelData>();
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
                    // 打全量（含调用栈）而不是只打 Message：这个 catch 曾把整段「国际运行资质 / 机组覆盖率 /
                    // 节点排期 / 进度表」全吞掉，只留一行没有位置信息的告警，谁也定位不到。
                    Debug.LogWarning($"准入 节点用例异常（不阻断后续）：{pe2}");
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

                    // ★2026-10-16 第十八次修订：本段把「哪一端该办槽」的口径**逐波逐端**打出来
                    // （档位 + 池余 + 可否申请），并把航线当前的班次表一并打出 —— 缺槽端为什么开单
                    // （或为什么不开单）必须能在日志里一眼对上，不要再靠「只看到一张单」去猜。
                    var flD = new System.Text.StringBuilder();
                    rcP3.EnsureFlights();
                    for (int fi = 0; fi < rcP3.flights.Count; fi++)
                        flD.Append($"[{fi}:{rcP3.flights[fi]?.registration} {rcP3.flights[fi]?.depTime}→{rcP3.flights[fi]?.arrTime}]");
                    Debug.Log($"P3 诊断 航线班次表 班数={rcP3.flights.Count} FlightCount={rcP3.FlightCount}"
                              + $" 本线 dep={rcP3.depTime} arr={rcP3.arrTime} → {flD}");
                    if (repP3.shortages != null)
                        for (int si = 0; si < repP3.shortages.Count; si++)
                        {
                            var swD = repP3.shortages[si];
                            if (swD == null || swD.kind != "no_slot") continue;
                            var eD = RoutePermitLogic.WaveEndNeeds(rcP3, swD.dep);
                            var sbD = new System.Text.StringBuilder();
                            for (int k = 0; k < eD.Count; k++)
                                sbD.Append($"[{eD[k].station} {eD[k].airport} {eD[k].time}→{eD[k].arrTime}"
                                           + $" 档={RoutePermitLogic.EndStatusKey(gsP3, rcP3, eD[k])}"
                                           + $" 池余={RoutePermitLogic.SlotsAvailable(gsP3, eD[k].airport, eD[k].band)}"
                                           + $" 可申请={RoutePermitLogic.CanApplyEnd(gsP3, rcP3, eD[k])}]");
                            Debug.Log($"P3 诊断 波{swD.dep}（{swD.slotAirport} band={swD.slotBand}）端数={eD.Count} → {sbD}");
                        }

                    InboxManager.QueueCapacityReport(gsP3, rcP3, repP3);
                    var applyMail = gsP3.inbox.Find(x => x != null && x.action == InboxManager.ApplySlotAction);
                    Debug.Log($"P3 申请邮件 → {(applyMail != null ? applyMail.subject : "无")}（期望含「申请」） 数量={applyMail?.permitAttempts ?? 0}");

                    // ★2026-10-16 回归（报障：往返只申请了**起飞机场**的时刻槽，落地机场一次都没被提起）：
                    //    旧版只读 WaveShortage.slotAirport，而那是 TryShortClock 返回的**第一个**缺端，
                    //    本场端排在需求列表下标 0 → 恒定只办本场端一张单。
                    //    断言口径与实现同源：逐端问 CanApplyEnd，该办的端一个都不能漏、也不能多开。
                    bool expectTwoEnds = RoutePermitLogic.NeedsDepartureSlot(rcP3.origin)
                                         && RoutePermitLogic.NeedsDepartureSlot(rcP3.destination);
                    var wantEnds = new List<string>();
                    if (repP3.shortages != null)
                        for (int si = 0; si < repP3.shortages.Count; si++)
                        {
                            var sw = repP3.shortages[si];
                            if (sw == null || sw.kind != "no_slot") continue;
                            var eW = RoutePermitLogic.WaveEndNeeds(rcP3, sw.dep);
                            for (int k = 0; k < eW.Count; k++)
                            {
                                if (!RoutePermitLogic.CanApplyEnd(gsP3, rcP3, eW[k])) continue;
                                string tag = eW[k].airport + " " + eW[k].time;
                                if (!wantEnds.Contains(tag)) wantEnds.Add(tag);
                            }
                        }
                    var gotEnds = new List<string>();
                    for (int ai = 0; ai < gsP3.inbox.Count; ai++)
                    {
                        var mm = gsP3.inbox[ai];
                        if (mm == null || mm.action != InboxManager.ApplySlotAction) continue;
                        string tag = mm.origin + " " + mm.slotClock;
                        if (!gotEnds.Contains(tag)) gotEnds.Add(tag);
                    }
                    bool endsMatch = wantEnds.Count > 0 && gotEnds.Count == wantEnds.Count;
                    for (int ai = 0; endsMatch && ai < wantEnds.Count; ai++)
                        if (!gotEnds.Contains(wantEnds[ai])) endsMatch = false;
                    bool hasDestEnd = false;
                    for (int ai = 0; ai < gotEnds.Count; ai++)
                        if (gotEnds[ai].StartsWith(rcP3.destination + " ", StringComparison.Ordinal)) hasDestEnd = true;
                    Debug.Log($"P3 两端办单 → 该办的端 [{string.Join("、", wantEnds.ToArray())}]"
                              + $"　实开单 [{string.Join("、", gotEnds.ToArray())}]"
                              + $"　逐端齐备={((endsMatch && (!expectTwoEnds || hasDestEnd)) ? "✓" : "✗")}"
                              + $"（期望 ✓；两端都协调={expectTwoEnds}，落地端 {rcP3.destination} 也有单={hasDestEnd}）");

                    string bodyP4 = CapacityDirectiveLogic.FormatReportBody(gsP3, rcP3, repP3);
                    bool p4HasOrigin = bodyP4.IndexOf("本场端", StringComparison.Ordinal) >= 0;
                    bool p4HasDest = bodyP4.IndexOf("对端", StringComparison.Ordinal) >= 0;
                    bool p4HasVia = bodyP4.IndexOf("经停端", StringComparison.Ordinal) >= 0;
                    Debug.Log($"P4 缺口清单逐停站 → 含「本场端」={p4HasOrigin}　含「对端」={p4HasDest}"
                              + $"　含「经停端」={p4HasVia}"
                              + $"（本线有经停={!string.IsNullOrEmpty(RoutePermitLogic.StopoverOf(rcP3))}；"
                              + "两端都协调时期望「本场端/对端」都 True；旧版只列「离港端」一头，等于只说半条）");
                    var capMailP4 = gsP3.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction);
                    Debug.Log($"P4 两阶段呈报 → slotFirst={repP3.slotFirst}（期望 True）　日循环={bodyP4.IndexOf("日循环", StringComparison.Ordinal) >= 0}"
                              + $"　时刻槽章节={bodyP4.IndexOf("时刻槽申请", StringComparison.Ordinal) >= 0}"
                              + $"　第一阶段={bodyP4.IndexOf("第一阶段", StringComparison.Ordinal) >= 0}"
                              + $"　角标={capMailP4?.tag}（期望 待槽）");

                    // R14 两端建模：加密一个时段要两端都有持仓 —— 按**缺口逐端**列出的「机场 × 时刻」逐条购入。
                    // ★2026-10-16 第十八次修订：旧版按 `BandNeeds(route, 1..2)` 买两个波段，
                    //   而缺口的波次是 0/1/2/3 各一波 —— 买的波段与实际缺的端**对不上**，
                    //   于是「买了两端却仍排不出班」。现在直接买「闸门说缺的那几端」，
                    //   买完必须真能排进去，否则说明「审批/购入回来的槽与闸门口径不一致」。
                    string buyMsg = "";
                    var buyLog = new System.Text.StringBuilder();
                    var boughtKeys = new List<string>();
                    var wantBuy = new List<RoutePermitLogic.SlotNeed>();
                    if (repP3.shortages != null)
                        for (int si = 0; si < repP3.shortages.Count; si++)
                        {
                            var swB = repP3.shortages[si];
                            if (swB == null || swB.kind != "no_slot") continue;
                            var eB = RoutePermitLogic.WaveEndNeeds(rcP3, swB.dep);
                            for (int k = 0; k < eB.Count; k++)
                            {
                                if (!RoutePermitLogic.NeedsDepartureSlot(eB[k].airport)) continue;
                                string keyB = eB[k].airport.ToUpperInvariant() + "|" + RoutePermitLogic.TimeOf(eB[k]);
                                if (boughtKeys.Contains(keyB)) continue;
                                boughtKeys.Add(keyB);
                                wantBuy.Add(eB[k]);
                            }
                        }
                    for (int k = 0; k < wantBuy.Count; k++)
                    {
                        // ★按**具体时刻**购入（不是按时段峰点）：槽的身份就是「某刻的一次离港」，
                        //   按时段买回来的是该段峰点（如夜班 22:00），它接不了 20:00 的那一班
                        //   （容差 ±15 分）—— 这正是旧版「两端都买了槽却仍排不进去」的成因。
                        string clkB = RoutePermitLogic.TimeOf(wantBuy[k]);
                        bool ok = StrategyManager.AcquireSlotsAt(gsP3, wantBuy[k].airport, clkB, 1, false, out buyMsg);
                        buyLog.Append($"{wantBuy[k].airport}{clkB}={((ok) ? "✓" : "✗")} ");
                    }
                    Debug.Log($"P3 购槽（逐端）→ 该买 {wantBuy.Count} 条 [{buyLog}]　{buyMsg}");

                    rcP3.RestoreScheduleFromBackup();
                    var dirP3b = CapacityDirectiveLogic.Parse("增加到每日4段往返");
                    var repP3b = CapacityDirectiveLogic.Execute(gsP3, rcP3, dirP3b);
                    int bound = 0;
                    if (gsP3.stg?.slots != null)
                        for (int si = 0; si < gsP3.stg.slots.Count; si++)
                            if (gsP3.stg.slots[si] != null && gsP3.stg.slots[si].inUse
                                && gsP3.stg.slots[si].usedByRouteId == rcP3.routeId) bound++;
                    // ★2026-10-16 第十八次修订：断言口径收窄到**可证伪的那一条** ——
                    //   「按缺口逐端买回来的端，必须真的能排进去」。剩下的缺口只允许是**滑点**
                    //   （例：按缺口买了本场端 12:00，而当天飞机最早 12:45 才能走），现实里那要向
                    //   协调人另提**时刻变更/换槽**（WASG 9.1.3「necessary slot adjustments」），
                    //   与「当初漏办一端」是两件事，不能用它来判「办单口径对不对」。
                    int stillShort = 0, boughtButShort = 0;
                    var shortLog = new System.Text.StringBuilder();
                    if (repP3b.shortages != null)
                        for (int si = 0; si < repP3b.shortages.Count; si++)
                        {
                            var swS = repP3b.shortages[si];
                            if (swS == null) continue;
                            if (swS.kind != "no_slot")
                            {
                                shortLog.Append($"[{swS.dep} {swS.kind}]");
                                continue;
                            }
                            stillShort++;
                            var eS = RoutePermitLogic.WaveEndNeeds(rcP3, swS.dep);
                            for (int k = 0; k < eS.Count; k++)
                            {
                                bool bought = boughtKeys.Contains(
                                    eS[k].airport.ToUpperInvariant() + "|" + RoutePermitLogic.TimeOf(eS[k]));
                                // 只有「这一端自己过不了闸门」才算口径问题；同波另一端缺槽把整波拖住不算。
                                bool endOk = RoutePermitLogic.CanScheduleEnd(gsP3, rcP3, eS[k], true);
                                if (bought && !endOk) boughtButShort++;
                                shortLog.Append($"[{swS.dep} 端={eS[k].airport} {RoutePermitLogic.TimeOf(eS[k])}"
                                                + $" 按此端购过={bought} 本端放行={endOk}]");
                            }
                        }
                    Debug.Log($"P3 有槽加密 → 实排 {rcP3.FlightCount} 班 　绑定 {bound} 条 　仍缺槽波 {stillShort}"
                              + $"　「买过却仍缺」={boughtButShort}　缺口 {shortLog}"
                              + $"　{((rcP3.FlightCount >= 2 && bound >= 1 && boughtButShort == 0) ? "✓" : "✗")}"
                              + "（期望 ✓：凡按缺口买回来的端都必须真能排进去；剩下的缺口若只是飞机滑点，"
                              + "那是要向协调人另提「时刻变更/换槽」，不算本轮口径漏端）");

                    // ★2026-10-16 第四十一次修订：剩下那个缺口（按缺口买了本场端 12:00，飞机当天最早
                    //   12:45 才能走）在现实里**不是**「漏办一端」，而是**持槽后的时刻变更**（改时）：
                    //   WASG 9.1.3 要求航司对已分配的时刻「及时提出必要调整」，8.10.1 明确槽不绑航线/航班号
                    //   且「may be changed」，8.7.1(e) 又把系列界定为「同一或**约同一**时刻」（→ 15 分容差）。
                    //   本段钉三件事：① 管线为它开出一张**时刻变更单**（不是再申请一条新槽）；
                    //   ② 批准 + 交割（协调人按系列余量批）**真的能把这波排进去**；
                    //   ③ 容差内（≤15 分）根本不该开单 —— 那本就算同一条槽。
                    //   ⚠️ 开单发生在 `QueueCapacityReport` 里（线上下达调令走的就是它），
                    //      测试里直调 `Execute` 只得到报告、不会开单，故这里要显式走一次呈报。
                    InboxManager.QueueCapacityReport(gsP3, rcP3, repP3b);
                    var changeMail = gsP3.inbox.Find(x => x != null && x.action == InboxManager.ChangeSlotAction);
                    string chFrom = changeMail == null ? "" : changeMail.slotFromClock;
                    string chTo = changeMail == null ? "" : InboxManager.SlotMailClock(changeMail);
                    int flBefore = rcP3.FlightCount;
                    bool changeOk = changeMail != null
                                    && !string.IsNullOrEmpty(chFrom) && !string.IsNullOrEmpty(chTo)
                                    && ScheduleSlots.ClockDiff(chFrom, chTo) > ScheduleSlots.SlotToleranceMinutes;
                    var holdOk = false;
                    if (changeOk)
                    {
                        InboxManager.Approve(gsP3, changeMail, out string apMsg);
                        // 测试免等一季：把交割季设成本季，直接推一次待办效果
                        changeMail.effectYear = gsP3.year;
                        changeMail.effectQuarter = gsP3.quarter;
                        InboxManager.ProcessPendingEffects(gsP3);
                        for (int si = 0; si < gsP3.stg.slots.Count; si++)
                        {
                            var s = gsP3.stg.slots[si];
                            if (s == null || s.inUse) continue;
                            if (!string.Equals(s.airport, changeMail.origin, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ScheduleSlots.ClockDiff(s.slotTime, chTo) <= ScheduleSlots.SlotToleranceMinutes)
                                holdOk = true;
                        }
                        var dirP3c = CapacityDirectiveLogic.Parse("增加到每日4段往返");
                        CapacityDirectiveLogic.Execute(gsP3, rcP3, dirP3c);
                    }
                    Debug.Log($"P3 改时（持槽后时刻滑点）→ 单={((changeMail != null) ? $"{changeMail.origin} {chFrom} → {chTo}" : "无")}"
                              + $"　协调人批后持仓已改为目标刻={holdOk}　实排 {flBefore} → {rcP3.FlightCount} 班"
                              + $"　{((changeOk && holdOk && rcP3.FlightCount > flBefore) ? "✓" : "✗")}"
                              + "（期望 ✓：已持槽的时刻滑点走「时刻变更申请」，批了就真能排进去）");

                    // ③ 容差内不该开单：12:00 → 12:10（差 10 分 ≤ 15 分容差）本就算同一条槽
                    bool tolDeny = !InboxManager.QueueChangeSlot(gsP3, rcP3.origin, "12:00", "12:10", 1,
                        "m_test_change_tol", out string tolMsg, null, rcP3.routeId, "12:00");
                    Debug.Log($"P3 改时容差 → 12:00 → 12:10 开单={(!tolDeny)}（期望 False：容差内视为同一系列）　{tolMsg}");

                    // ④ 目标系列已满 → 协调人驳：把该站该时段池填满后再提改时，必须被拒（不硬开空单）
                    string aptF = rcP3.origin;
                    int bandF = ScheduleSlots.BandOfDep("22:00");
                    int capF = RoutePermitLogic.SlotPoolCapacity(aptF, bandF);
                    int fillF = 0;
                    while (fillF < 40 && RoutePermitLogic.SlotsAvailable(gsP3, aptF, bandF) > 0)
                    {
                        if (!StrategyManager.AcquireSlotsAt(gsP3, aptF, "22:00", 1, true, out string _)) break;
                        fillF++;
                    }
                    bool fullDeny = !InboxManager.QueueChangeSlot(gsP3, aptF, "20:00", "22:00", 1,
                        "m_test_change_full", out string fullMsg, null, rcP3.routeId, "22:00");
                    Debug.Log($"P3 改时被驳 → 池 {capF} 条填满（补 {fillF}）后提 20:00 → 22:00：开单={(!fullDeny)}（期望 False）"
                              + $"　{fullMsg}");

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

                // R42: isolated assertions are shared with the batch regression entry.
                CapacityReceiptRegression.Run();

                // —— R14：时刻槽**逐停站**建模（一条槽 = 某停站一次停留的一对「到场 + 离场」）——
                //     本场端 1 条、对端 1 条；有经停时经停站一趟往返停两次 = **2 条**（共 4 条）。
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
                    Debug.Log($"R14 逐停站槽需求 {needs2E.Count} 条（期望 2：本场端 SZX + 对端 PEK，各带一对到场/离场时刻）= "
                        + string.Join("、", needs2E.ConvertAll(n =>
                            $"{RoutePermitLogic.LegName(n)} {n.airport}{StrategyManager.SlotBandName(n.band)} {n.flightNo}（{RoutePermitLogic.LegClocks(n)}）").ToArray()));

                    bool grandOnly = RoutePermitLogic.CanScheduleBand(gs2E, r2E, 0, true);
                    bool noGrandNoSlot = RoutePermitLogic.CanScheduleBand(gs2E, r2E, 0, false);
                    Debug.Log($"R14 祖父条款 → 祖父可飞={grandOnly}（期望 True） 关掉祖父即缺槽={noGrandNoSlot}（期望 False）");

                    int band2E = 1;
                    var bn2E = RoutePermitLogic.BandNeeds(r2E, band2E);
                    if (bn2E.Count == 2)
                    {
                        // 本用例要能分辨「两端齐备」，就得**真的先铺出持仓再预约**：
                        // ReserveSlot 只认「已存在且未占用」的持仓，无槽可占时返回 -1 且不吭声，
                        // 于是本用例曾长期退化成「两端都没槽」—— 两个 False 看着像通过，其实什么也没验到。
                        // 故：① 先只给去程端铺槽（验「缺一不可」）；② 再给回程端铺槽（验「齐备可排」）；
                        //     ③ 把两次预约的返回下标打出来，让「空转」不可能再沉默通过。
                        gs2E.stg.slots.Add(new SlotHolding
                        {
                            airport = bn2E[0].airport,
                            slotTime = RoutePermitLogic.TimeOf(bn2E[0])
                        });
                        int r0 = RoutePermitLogic.ReserveSlot(gs2E, bn2E[0].airport, bn2E[0].band,
                            r2E.routeId, "SZ101");
                        bool onlyOrigin = RoutePermitLogic.CanScheduleBand(gs2E, r2E, band2E, false);

                        gs2E.stg.slots.Add(new SlotHolding
                        {
                            airport = bn2E[1].airport,
                            slotTime = RoutePermitLogic.TimeOf(bn2E[1])
                        });
                        int r1 = RoutePermitLogic.ReserveSlot(gs2E, bn2E[1].airport, bn2E[1].band,
                            r2E.routeId, "SZ102");
                        bool bothEnds = RoutePermitLogic.CanScheduleBand(gs2E, r2E, band2E, false);

                        Debug.Log($"R14 两端缺一不可 → 仅去程端可排={onlyOrigin}（期望 False：回程端还没有持仓）"
                            + $" 两端齐备={bothEnds}（期望 True：两端各有一条本航线的持仓）"
                            + $"　预约下标 {r0}/{r1}（期望均 ≥0；-1 = 该端无空闲持仓可占，用例会退化成空转）"
                            + $"　两端={string.Join(" / ", bn2E.ConvertAll(n => $"{RoutePermitLogic.LegName(n)} {n.airport} {RoutePermitLogic.TimeOf(n)}").ToArray())}");
                    }
                    else
                    {
                        Debug.Log($"R14 两端缺一不可 → 该时段需求 {bn2E.Count} 条（期望 2，用例未执行）");
                    }

                    var gap2E = RoutePermitLogic.BandGapText(gs2E, r2E, 3);
                    Debug.Log($"R14 缺口归因 → 红眼波 {gap2E}");

                    // ★2026-10-16 补：**经停航线**的槽需求 —— 经停站既落又起，
                    //   一趟往返在经停站停两次，故要 2 条（去程一次、回程一次），全趟共 4 条。
                    //   这是「经停机场不需要时刻槽」那种读法的正面对照。
                    var r2V = new RouteData
                    {
                        routeId = "R-SZX-PEK-2V", origin = "SZX", destination = "PEK", distanceKm = 1940f,
                        aircraftType = "A320neo", flightNumber = "SZ150", registration = "B-9002",
                        depTime = "08:00", arrTime = "12:00", daysOfWeek = "每日", weeklyFlights = 7,
                        groundStaffAssigned = 3, baseDailyProfit = 10f, dailyProfit = 10f, stopover = "SHA"
                    };
                    r2V.SyncFlightsFromPrimary();
                    var needs2V = RoutePermitLogic.SlotNeeds(r2V);
                    int viaCount = 0;
                    bool viaBothWays = true, viaPairOk = true;
                    for (int k = 0; k < needs2V.Count; k++)
                    {
                        var n = needs2V[k];
                        if (n.station == RoutePermitLogic.StationVia)
                        {
                            viaCount++;
                            if (n.arrTime == null || n.time == null) viaPairOk = false;
                        }
                    }
                    viaBothWays = needs2V.Exists(n => n.station == RoutePermitLogic.StationVia && !n.inbound)
                                  && needs2V.Exists(n => n.station == RoutePermitLogic.StationVia && n.inbound);
                    Debug.Log($"R14 经停航线槽需求 {needs2V.Count} 条（期望 4）= "
                        + string.Join("、", needs2V.ConvertAll(n =>
                            $"{RoutePermitLogic.LegName(n)} {n.airport}（{RoutePermitLogic.LegClocks(n)}）").ToArray())
                        + $"\n　经停站 SHA 共 {viaCount} 条（期望 2）　去程/回程各一次={viaBothWays}（期望 True）"
                        + $"　每对都成对带时刻={viaPairOk}（期望 True）"
                        + $"\n　（WASG 1.6.1：slot 是「一次到场 or 一次离场」的许可；经停站既落又起，"
                        + "两次停留各要一对 —— 故「只申请起飞机场」在经停线上是不可能成立的）");

                    var viaEnds = RoutePermitLogic.WaveEndNeeds(r2V, "08:00");
                    int viaEndCount = 0;
                    for (int k = 0; k < viaEnds.Count; k++)
                        if (viaEnds[k].station == RoutePermitLogic.StationVia) viaEndCount++;
                    Debug.Log($"R14 经停航线开单清单（WaveEndNeeds）{viaEnds.Count} 条（期望 4；经停端 {viaEndCount} 条，期望 2）"
                        + "　—— 开单与呈报共用同一份逐停站清单，经停端不会再被漏掉");
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

                // 长程（单段 > 9h）：外站换组 —— 去程与回程分属两个值勤期，一段往返仍可排
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
                Debug.Log($"长程 外站换组 → PEK–LHR 单程 {blockL} 分（>{OpsRotationLogic.LongHaulLegMinutes / 60}h，按外站换组计）"
                          + $"　可排 {feasL.Count} 段（期望 1）　缺口 {gapsL.Count} 个（期望 3）"
                          + $"\n　可排 [{string.Join("、", feasL.ToArray())}]"
                          + $"\n　首个缺口：{(gapsL.Count > 0 ? gapsL[0].reason : "无")}"
                          + $"\n　（一段往返占 {blockL * 2 + OpsRotationLogic.TurnOf(rcLong)} 分 ≈ {(blockL * 2 + OpsRotationLogic.TurnOf(rcLong)) / 60f:0.#}h，"
                          + $"按往返合并计必然超值勤上限；按外站换组拆分后去程组 1 航段仅 {(blockL + OpsRotationLogic.ReportBeforeMinutes) / 60f:0.#}h）");

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

                // —— R15：双口径周合计恒等（接管局航线被压成单条目后不得少报）——
                // 复现接管界面那条读不懂的「离港25/值勤7班/周」：接管局既有航线经
                // SyncFlightsFromPrimary() 压成 1 个条目（weeklyFlights=25 · 每日）；
                // 旧实现的值勤口径按「每天 +1」计 → 7，与离港 25 量纲不等、并被并写成「X/Y班/周」。
                var routeCollapsed = new RouteData
                {
                    routeId = "R-R15", origin = "DXB", destination = "DOH", distanceKm = 380f,
                    aircraftType = "A320neo", openedDate = RouteData.OpenedBeforeTakeover,
                    weeklyFlights = 25, daysOfWeek = "每日", depTime = "08:00", arrTime = "09:10"
                };
                routeCollapsed.SyncFlightsFromPrimary();
                int cDep = routeCollapsed.WeeklyFlightsDeparture();
                int cDuty = routeCollapsed.WeeklyFlightsDuty();
                var cDist = routeCollapsed.WeekdayOpsDeparture();
                int cSum = 0;
                for (int i = 0; i < cDist.Length; i++) cSum += cDist[i];
                Debug.Log($"R15 压扁单条目（25 班/周 · 每日）→ 离港周={cDep}（期望 25）"
                          + $"　值勤周={cDuty}（期望 25：与离港恒等，两条口径只是归组星期不同）"
                          + $"\n　7 日离港分布={FlightScheduleFormat.DaysSummary(cDist)} 日合计={cSum}（期望 4/4/4/4/3/3/3 = 25）"
                          + $"\n　卡片文案 → {routeCollapsed.CardHeadline()}"
                          + $"（期望「接管前已开航 · 25 班/周」，不再出现「开通：接管前」与「离港25/值勤7」）");

                // —— R16：接管局「机队 ↔ 网络」自洽（每条航线都必须有自己的飞机）——
                // 复现接管界面那两条没有飞机的航线（FRA→ATL / FRA→ORD，界面写成「未分配 A320neo」）：
                // 机队规模与过渡业务占机数是两处独立配置（过渡业务从机队尾部抽干租/湿租/包机/培训），
                // 旧实现各算各的 → 可用机 < 航线数，而选机又强制「一架飞机只配一条航线」，
                // 于是必然漏出无机航线（全量 72 条，且因目的地池按近→中→远生成，全是洲际线）。
                // 另有选机侧的顺序缺陷：贪心按池序配机，唯一的宽体被 1,423 km 的 FRA–MAD 吃掉。
                try
                {
                    var probe = TakeoverWorldBuilder.BuildPreview("hub_FRA");
                    var busyRegs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (probe.transitionBusinesses != null)
                        for (int i = 0; i < probe.transitionBusinesses.Count; i++)
                            if (probe.transitionBusinesses[i] != null)
                                busyRegs.Add(probe.transitionBusinesses[i].aircraftReg);

                    int noPlane = 0, unfit = 0;
                    var unassigned = new List<string>();
                    var tooShort = new List<string>();
                    for (int i = 0; i < probe.routes.Count; i++)
                    {
                        var r = probe.routes[i];
                        if (r == null) continue;
                        if (string.IsNullOrEmpty(r.registration))
                        {
                            noPlane++;
                            unassigned.Add($"{r.origin}→{r.destination}");
                            continue;
                        }
                        var cat = AircraftCatalog.Find(r.aircraftType);
                        if (cat == null || cat.maxRangeKm < r.distanceKm)
                        {
                            unfit++;
                            tooShort.Add($"{r.origin}→{r.destination}({r.distanceKm:F0}km · {r.aircraftType})");
                        }
                    }
                    Debug.Log($"R16 接管局机队自洽（{probe.nameCN} ★{probe.difficulty}）→ 机队 {probe.aircraft.Count} 架"
                              + $"（过渡占机 {busyRegs.Count} → 可用 {probe.aircraft.Count - busyRegs.Count}）· 航线 {probe.routes.Count} 条"
                              + $"\n　无机航线 {noPlane} 条（期望 0）{(noPlane > 0 ? " → " + string.Join("、", unassigned) : "")}"
                              + $"\n　机型飞不到航距 {unfit} 条（期望 0）{(unfit > 0 ? " → " + string.Join("、", tooShort) : "")}"
                              + $"\n　规模底线 → 机队 {probe.aircraft.Count} ≥ 航线 {probe.routes.Count} + 过渡 {busyRegs.Count}"
                              + $"（{(probe.aircraft.Count >= probe.routes.Count + busyRegs.Count ? "成立" : "不成立")}）"
                              + $"\n　洲际线拿宽体 → {LongHaulAircraftCsv(probe)}");
                }
                catch (Exception fe)
                {
                    Debug.LogWarning($"R16 接管局机队自洽用例异常（不阻断后续）：{fe.Message}");
                }

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

            // —— R17：无机航线不得起飞（「幽灵航班」回归护栏）——
            // 旧实现 TryTakeoff 写成 `var ac = FindAircraft(...); if (ac != null) {…}`：
            // ac == null（未指派机号 / 机号已退役转售 / 老存档残留）时整段校验被跳过，
            // 直接 airborne = true —— 没有执飞机，却在 Land() 照常把 flight.dailyProfit 记进
            // cash / liveQuarterIncome，还不消耗航时与机损，等于凭空的收入；
            // 地图上还会照画一架飞机沿航迹飞（DrawPlanes 只看班期，不看机号）。
            // 本用例走完整往返窗口，断言：不起飞、不计营收、如实呈报；补机后再走一次必须复航且营收入账。
            var goG = new GameObject("__ground_test_gs__");
            try
            {
                var gsG = goG.AddComponent<GameState>();
                gsG.activeRoutes = new List<RouteData>();
                gsG.fleet = new List<Aircraft>();

                var rg = new RouteData
                {
                    routeId = "R-R17", origin = "PEK", destination = "SHA", distanceKm = 1100f,
                    aircraftType = "A320neo", flightNumber = "NG100", registration = "",
                    depTime = "08:00", arrTime = "10:30", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 12f, dailyProfit = 12f, groundStaffAssigned = 2
                };
                rg.SyncFlightsFromPrimary();
                gsG.activeRoutes.Add(rg);

                // 本地 07:50 → 13:00 覆盖「去程起飞/落地 + 回程起飞/落地」整段往返窗口。
                // 本地↔UTC 一律走 GameState.LocalUtcOffset，不硬编码时区（用例要跨机器成立）。
                DateTime Loc(int h, int m) => new DateTime(2026, 10, 1, h, m, 0);
                DateTime U(DateTime local) => local - GameState.LocalUtcOffset;

                gsG.SetSimUtc(U(Loc(7, 50)));
                gsG.liveQuarterIncome = 0f;
                LiveClockSimulator.Advance(gsG, U(Loc(7, 50)), U(Loc(13, 0)));

                var gfl = rg.flights[0];
                string groundLine = GroundLogLine(gsG, "【停飞】");
                bool groundedReported = groundLine != null;
                float groundedIncome = gsG.liveQuarterIncome;
                Debug.Log($"R17 无机航线不得起飞 → airborne={gfl.airborne}（期望 False）"
                          + $" 起飞键={(string.IsNullOrEmpty(gfl.lastOpKey) ? "（空）" : gfl.lastOpKey)}（期望空：从未起飞）"
                          + $" 本班营收={groundedIncome:F2} 万（期望 0）"
                          + $" 计划口径={gsG.monthlyRevenue:F2} 万/月（期望 0：季结按 ×3 轧差，停飞线不得照付）"
                          + $" 呈报停飞={groundedReported}（期望 True）"
                          + $"\n　停飞提示：{(groundedReported ? groundLine : "（无）")}"
                          + $"\n　机号标注={FlightGroundLogic.RegNote(gsG, rg.registration)}（期望空：未指派机号由界面走「待复航」文案）"
                          + $" 整线判据={FlightGroundLogic.RouteOf(gsG, rg)}（期望 NoAssignment）");

                // 补上执飞机：同一窗口必须复航并正常计营收（避免「一旦停飞就永远飞不起来」）
                gsG.fleet.Add(new Aircraft
                {
                    registration = "B-9001", type = "A320neo", isActive = true,
                    status = AircraftStatuses.Airworthy, age = 3, condition = 100f
                });
                rg.registration = "B-9001";
                rg.SyncFlightsFromPrimary();
                rg.flights[0].registration = "B-9001";
                gsG.SetSimUtc(U(Loc(7, 50)));
                LiveClockSimulator.Advance(gsG, U(Loc(7, 50)), U(Loc(13, 0)));

                float resumedIncome = gsG.liveQuarterIncome - groundedIncome;
                string resumeLine = GroundLogLine(gsG, "【复航】");
                Debug.Log($"R17 补机后复航 → 本窗营收={resumedIncome:F2} 万（期望 >0）"
                          + $" 计划口径={gsG.monthlyRevenue:F2} 万/月（期望 >0：复航即回到计划收入）"
                          + $" 复航呈报={(resumeLine != null)}（期望 True）"
                          + $" 机号标注={FlightGroundLogic.RegNote(gsG, "B-9001")}（期望空：可执飞不标注）"
                          + $" 整线判据={FlightGroundLogic.RouteOf(gsG, rg)}（期望 None）"
                          + $"\n　复航提示：{(resumeLine != null ? resumeLine : "（无）")}");

                // 退役/转售也要拦住（老存档典型残留：机号还在航线记录里，飞机已不在册）
                var retired = gsG.fleet[0];
                retired.isActive = false;
                retired.status = AircraftStatuses.Retired;
                gsG.liveQuarterIncome = 0f;
                rg.flights[0].airborne = false;
                rg.flights[0].lastOpKey = null;
                gsG.SetSimUtc(U(Loc(7, 50)));
                LiveClockSimulator.Advance(gsG, U(Loc(7, 50)), U(Loc(13, 0)));
                Debug.Log($"R17 退役机不执飞 → airborne={rg.flights[0].airborne}（期望 False）"
                          + $" 本班营收={gsG.liveQuarterIncome:F2} 万（期望 0）"
                          + $" 计划口径={gsG.monthlyRevenue:F2} 万/月（期望 0）"
                          + $" 机号标注={FlightGroundLogic.RegNote(gsG, "B-9001")}（期望「（已退役/转售）」）"
                          + $" 整线判据={FlightGroundLogic.RouteOf(gsG, rg)}（期望 Retired）");
            }
            catch (Exception ge)
            {
                Debug.LogWarning($"无机航线幽灵起飞用例异常（不阻断后续）：{ge.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goG);
            }

            // —— R18：时刻槽使用率考核（IATA WASG §8.6 80/20 · 判航季 · 分系列 · HBD · JNUS）——
            // 这一套是**照搬真实规则**，不是「一年一个率」的简化，所以用例要钉住五件事：
            //   ① 航季数学：key 自增、冬春跨年（Q1 与上一年 Q4 同季）、HBD 落在航季第 2 个季度
            //   ② 系列粒度是「机场 × 时段 × 星期几」：周一掉不牵连周二（WASG 8.7.1f）
            //   ③ 80% 是**闭区间下界**：20/25 = 80% 保、20/26 = 76.9% 掉
            //   ④ HBD 清零计数器，但**不得**清掉历史优先权 —— 否则失格状态在下一个 HBD 复活、
            //      整个机制凭空蒸发（本类最容易写错的一处）
            //   ⑤ 后果落在「下一等价航季开端」：释放持仓 + 祖父条款同时失效；有正当理由者走 JNUS 待裁
            var goS = new GameObject("__slotusage_test_gs__");
            try
            {
                var gsS = goS.AddComponent<GameState>();
                gsS.activeRoutes = new List<RouteData>();
                gsS.fleet = new List<Aircraft>();
                gsS.year = 3;
                gsS.quarter = 2;

                // ① 航季数学
                int kSum3 = SlotUsageLogic.KeyOf(3, 2);
                int kWin3 = SlotUsageLogic.KeyOf(3, 4);
                int kWinQ1 = SlotUsageLogic.KeyOf(4, 1);   // 冬春跨年：第 4 年 Q1 仍属第 3 年起算的冬春
                int kSum4 = SlotUsageLogic.KeyOf(4, 2);
                Debug.Log($"R18 航季数学 → 第3年Q2={kSum3}（期望 6） 第3年Q4={kWin3}（期望 7）"
                          + $" 第4年Q1={kWinQ1}（期望 7：与上一条同季） 第4年Q2={kSum4}（期望 8）"
                          + $"\n　自增性（上一等价航季 = key−1）={(kWin3 == kSum3 + 1 && kSum4 == kWin3 + 1)}（期望 True）"
                          + $" 冬春跨季同号={(kWinQ1 == kWin3)}（期望 True）"
                          + $"\n　标签 {SlotUsageLogic.LabelOf(kSum3)} / {SlotUsageLogic.LabelOf(kWin3)}"
                          + $"　HBD 季度 夏秋=Q{SlotUsageLogic.HbdQuarterOf(kSum3)}（期望 3） 冬春=Q{SlotUsageLogic.HbdQuarterOf(kWin3)}（期望 1）"
                          + $"　开端季度 夏秋=Q{SlotUsageLogic.StartQuarterOf(kSum3)}（期望 2） 冬春=Q{SlotUsageLogic.StartQuarterOf(kWin3)}（期望 4）");
                Debug.Log($"R18 星期口径 → 2026-10-05（周一）={SlotUsageLogic.WeekdayOf(new DateTime(2026, 10, 5))}（期望 0）"
                          + $" 2026-10-11（周日）={SlotUsageLogic.WeekdayOf(new DateTime(2026, 10, 11))}（期望 6）"
                          + "（与 FlightScheduleFormat.DaysOf 的「一…日」字面顺序同口径）");

                // ②③ 系列粒度 + 80% 边界
                SlotUsageLogic.BeginSeason(gsS, kSum4);
                // PEK 早班 周一：26 应飞 / 20 实飞 = 76.9% → 掉
                for (int i = 0; i < 26; i++) SlotUsageLogic.NoteDue(gsS, "PEK", 0, 0, GroundReason.None);
                for (int i = 0; i < 20; i++) SlotUsageLogic.NoteOperated(gsS, "PEK", 0, 0);
                // PEK 早班 周二：25 应飞 / 20 实飞 = 正好 80% → 保（闭区间下界）
                for (int i = 0; i < 25; i++) SlotUsageLogic.NoteDue(gsS, "PEK", 0, 1, GroundReason.None);
                for (int i = 0; i < 20; i++) SlotUsageLogic.NoteOperated(gsS, "PEK", 0, 1);
                // SZX 早班 周一：10 应飞 / 7 实飞 = 70% → 掉（该机场该时段只有这一个系列）
                for (int i = 0; i < 10; i++) SlotUsageLogic.NoteDue(gsS, "SZX", 0, 0, GroundReason.None);
                for (int i = 0; i < 7; i++) SlotUsageLogic.NoteOperated(gsS, "SZX", 0, 0);

                // SZX 早持有一条**已绑定**持仓：用来验证「失格后释放持仓」走绑定兜底分支
                gsS.stg.slots.Add(new SlotHolding
                {
                    airport = "SZX", slotTime = "早", inUse = true,
                    usedByRouteId = "R-R18", usedByFlightNo = "SU100"
                });
                var rs = new RouteData
                {
                    routeId = "R-R18", origin = "PEK", destination = "SZX", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU100", registration = "B-9009",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 12f, dailyProfit = 12f, groundStaffAssigned = 2
                };
                rs.SyncFlightsFromPrimary();
                gsS.activeRoutes.Add(rs);

                var needSz = new RoutePermitLogic.SlotNeed
                { airport = "SZX", band = 0, routeId = "R-R18", flightNo = "SU100", inbound = false };
                var needPek = new RoutePermitLogic.SlotNeed
                { airport = "PEK", band = 0, routeId = "R-R18", flightNo = "SU101", inbound = false };

                string szBefore = RoutePermitLogic.EndStatusKey(gsS, rs, needSz);
                bool canBefore = RoutePermitLogic.CanScheduleEnd(gsS, rs, needSz, true);

                var v18 = SlotUsageLogic.Evaluate(gsS, kSum4);
                string szAfter = RoutePermitLogic.EndStatusKey(gsS, rs, needSz);
                bool canAfter = RoutePermitLogic.CanScheduleEnd(gsS, rs, needSz, true);
                string pekAfter = RoutePermitLogic.EndStatusKey(gsS, rs, needPek);
                // 失格**当下**被关掉的是「再往这个端加新班次」的路，而不是「已绑持仓的现有班次」——
                // 持仓换季才清算，在那之前它仍是本公司的自有资产。两边都要钉住，否则很容易把
                // 「失格」误解成「立刻断供」（真规则里也会给出一整个航季的补救窗口）。
                var needSzNew = new RoutePermitLogic.SlotNeed
                { airport = "SZX", band = 0, routeId = "R-R18", flightNo = "SU900", inbound = false };
                bool canNewEnd = RoutePermitLogic.CanScheduleEnd(gsS, rs, needSzNew, true);

                Debug.Log($"R18 分系列判定 → 达标 {v18.kept}（期望 1：PEK 早 周二 80%）"
                          + $" 失格 {v18.lost}（期望 2：PEK 早 周一 76.9% + SZX 早 周一 70%）"
                          + $" 未评 {v18.unjudged}（期望 0）"
                          + $"\n　同机场同时段两星期各自独立：PEK 早 周一={SlotUsageLogic.HasHistoricPrecedence(gsS, "PEK", 0, 0)}（期望 False）"
                          + $" 周二={SlotUsageLogic.HasHistoricPrecedence(gsS, "PEK", 0, 1)}（期望 True）"
                          + $"\n　祖父条款闸门 → SZX 早 失格前={szBefore}（期望 bound：该端已绑自有持仓）"
                          + $" 失格后={szAfter}（期望 bound：换季清算前仍是自有资产）"
                          + $" 现有班次可排 {canBefore}→{canAfter}（期望 True→True）"
                          + $" 同端加新班次={canNewEnd}（期望 False：祖父已关且无空闲持仓）"
                          + $"\n　「任一星期存活即仍可祖父」→ PEK 早={pekAfter}（期望 grandfather：周二仍保有权）"
                          + $"　文案：{RoutePermitLogic.EndStatusText(gsS, rs, needSz)}");

                // ④ HBD 清零计数器**但保留**历史优先权（回归护栏：清错一处机制就蒸发）
                SlotUsageLogic.BeginSeason(gsS, kSum4 + 2);
                // 取原始列表而非 SlotUsageLogic.Series()：后者按 due > 0 过滤，清零后查不到，
                // 诊断会恒打 -1 —— 看起来像「没清零」，实际是查错了地方（本段护栏自身踩过一次）。
                var serMon = gsS.stg.slotUsage?.series?.Find(x => x.airport == "PEK"
                    && SlotUsageLogic.SeriesTimeOf(x.time) == ScheduleSlots.DepTimes[0] && x.weekday == 0);
                Debug.Log($"R18 HBD 清零保权 → 周一 due={serMon?.due ?? -1}（期望 0：分母清零）"
                          + $" 周一历史优先权={SlotUsageLogic.HasHistoricPrecedence(gsS, "PEK", 0, 0)}（期望 False：不得随计数器一起被清掉）"
                          + $" 周二历史优先权={SlotUsageLogic.HasHistoricPrecedence(gsS, "PEK", 0, 1)}（期望 True）");

                // ⑤ 后果落在下一等价航季开端
                var relNotes = SlotUsageLogic.ApplyDueReleases(gsS, kSum4 + 2, out int relN);
                Debug.Log($"R18 失格释放 → 退回持仓 {relN} 条（期望 1：SZX 早的绑定持仓）"
                          + $" 剩余持仓 {gsS.stg.slots.Count}（期望 0） 释放条目 {relNotes.Count}（期望 2）"
                          + $"\n　{string.Join("\n　", relNotes.ToArray())}");

                // 断供发生在**释放之后**（即下一等价航季开端），这时才转 missing 且不可再排。
                // 与上面「失格当下仍报 bound」合起来才是完整口径：失格 = 先关掉加新班次的路，换季再收回槽。
                string szReleased = RoutePermitLogic.EndStatusKey(gsS, rs, needSz);
                bool canReleased = RoutePermitLogic.CanScheduleEnd(gsS, rs, needSz, true);
                Debug.Log($"R18 释放后断供 → SZX 早={szReleased}（期望 missing） 现有班次可排={canReleased}（期望 False）"
                          + $"　文案：{RoutePermitLogic.EndStatusText(gsS, rs, needSz)}");

                // 尚未到季者不得提前释放
                int relEarly = 0;
                SlotUsageLogic.ApplyDueReleases(gsS, kSum4 + 1, out relEarly);
                Debug.Log($"R18 未到季不释放 → 在 key−1 调用退回 {relEarly} 条（期望 0：释放只认「下一等价航季」）");

                // JNUS：有正当未用事由者不直判失格，改走待裁；批准保权、驳回/到期转失格
                SlotUsageLogic.BeginSeason(gsS, kSum4 + 2);
                for (int i = 0; i < 20; i++) SlotUsageLogic.NoteDue(gsS, "SZX", 2, 0, i < 6 ? GroundReason.NoAssignment : GroundReason.None);
                for (int i = 0; i < 8; i++) SlotUsageLogic.NoteOperated(gsS, "SZX", 2, 0);
                for (int i = 0; i < 20; i++) SlotUsageLogic.NoteDue(gsS, "PEK", 2, 0, i < 9 ? GroundReason.Maintenance : GroundReason.None);
                for (int i = 0; i < 8; i++) SlotUsageLogic.NoteOperated(gsS, "PEK", 2, 0);
                var vJnus = SlotUsageLogic.Evaluate(gsS, kSum4 + 2);
                string jnusCsv = SlotUsageLogic.EncodeSeries(vJnus.jnusSeries);
                var decoded = SlotUsageLogic.DecodeSeries(jnusCsv);
                Debug.Log($"R18 JNUS 分派 → 待裁 {vJnus.pendingJnus}（期望 2：SZX 晚周一 40%/正当无机、PEK 晚周一 40%/正当停场）"
                          + $" 直判失格 {vJnus.lost}（期望 0：有正当理由不直判）"
                          + $"\n　编码往返 {jnusCsv} → 解出 {decoded.Count} 条（期望 2）"
                          + $"　待裁系列：{string.Join("；", SlotUsageLogic.PendingJnusSeries(gsS).ConvertAll(x => SlotUsageLogic.SeriesLabel(x.airport, x.time, x.weekday)))}");

                // 批准 SZX 晚 周一
                bool granted = SlotUsageLogic.GrantJnus(gsS, "SZX", 2, 0);
                // 驳回 PEK 晚 周一
                bool denied = SlotUsageLogic.DenyJnus(gsS, "PEK", 2, 0);
                Debug.Log($"R18 JNUS 裁定 → 批准={granted}（期望 True） 驳回={denied}（期望 True）"
                          + $"　SZX 晚 周一历史优先权={SlotUsageLogic.HasHistoricPrecedence(gsS, "SZX", 2, 0)}（期望 True：豁免保权）"
                          + $" PEK 晚 周一={SlotUsageLogic.HasHistoricPrecedence(gsS, "PEK", 2, 0)}（期望 False：驳回即失格）"
                          + $"\n　待裁队列剩 {gsS.stg.slotUsage.pendingJnus.Count}（期望 0）"
                          + $" 待释放队列 {gsS.stg.slotUsage.pendingReleases.Count}（期望 1：驳回者转入）");

                // 到期未处理视同驳回：SettlePendingJnus 按失格执行
                SlotUsageLogic.BeginSeason(gsS, kSum4 + 4);
                for (int i = 0; i < 20; i++) SlotUsageLogic.NoteDue(gsS, "CAN", 1, 0, GroundReason.Conflict);
                SlotUsageLogic.Evaluate(gsS, kSum4 + 4);
                int jnusPendingBefore = gsS.stg.slotUsage.pendingJnus.Count;
                SlotUsageLogic.SettlePendingJnus(gsS, kSum4 + 6, out int expiredN);
                Debug.Log($"R18 JNUS 到期 → 待裁 {jnusPendingBefore}（期望 1）"
                          + $" 到季未获批而失格，退回持仓 {expiredN} 条（期望 0：CAN 中周一无持仓可退，只终止历史优先权）"
                          + $" 历史优先权 {SlotUsageLogic.HasHistoricPrecedence(gsS, "CAN", 1, 0)}（期望 False）"
                          + $"\n　展示：{SlotUsageLogic.FormatSeasonLine(gsS)}");
            }
            catch (Exception se)
            {
                Debug.LogWarning($"时刻槽使用率考核用例异常（不阻断后续）：{se.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goS);
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

                // 来源视图接纳判定：渠道候选人必须能进对应渠道视图，人才市场总监必须能进人才市场视图。
                // 两者共用总监闸门时，渠道视图会永远为空（这正是 2026-10-08 修掉的回归）。
                var unlisted = new PersonnelData { id = "T-1", fullName = "测试候选", rank = "专员", onMarket = true };
                var listedCampus = new PersonnelData { id = "T-2", fullName = "测试校招", rank = "专员", onMarket = true, source = HrEconomics.SourceCampus };
                var listedHead = new PersonnelData { id = "T-3", fullName = "测试猎头", rank = "专员", onMarket = true, source = HrEconomics.SourceHeadhunt };
                var director = new PersonnelData { id = "T-4", fullName = "测试总监", rank = "负责人", onMarket = true, source = HrEconomics.SourceMarket };
                var inService = new PersonnelData { id = "T-5", fullName = "在职者", rank = "负责人", onMarket = false, source = HrEconomics.SourceMarket };
                string[] views = { HrEconomics.SourceMarket, HrEconomics.SourceCampus, HrEconomics.SourceHeadhunt, HrEconomics.SourceInternal };
                string[] names = { "人才市场", "校招", "猎头", "内部转岗" };
                var sbFold = new System.Text.StringBuilder("来源视图 接纳判定 → ");
                for (int v = 0; v < views.Length; v++)
                {
                    sbFold.Append($"{names[v]}=[总监{(StaffRecruitment.AcceptedInSourceView(director, views[v]) ? "✓" : "×")}"
                        + $"校招{(StaffRecruitment.AcceptedInSourceView(listedCampus, views[v]) ? "✓" : "×")}"
                        + $"猎头{(StaffRecruitment.AcceptedInSourceView(listedHead, views[v]) ? "✓" : "×")}"
                        + $"在职{(StaffRecruitment.AcceptedInSourceView(inService, views[v]) ? "✓" : "×")}] ");
                }
                Debug.Log(sbFold.ToString());
                Debug.Log("来源视图 接纳 期望 → "
                    + "人才市场=[总监✓校招×猎头×在职×]；校招=[总监×校招✓猎头×在职×]；"
                    + "猎头=[总监×校招×猎头✓在职×]；内部转岗=[总监×校招×猎头×在职×]"
                    + $"；无来源专员(按市场来源处理)={StaffRecruitment.AcceptedInSourceView(unlisted, HrEconomics.SourceCampus)}（期望 False）"
                    + "。注：内部转岗行由 HrManager.InternalCandidates 另一路推导，不经本判定。");

                // 「全部」视图 = 原五视图的并集（2026-10-10 起顶栏来源芯片的「全部」档）。
                // 必须保留不对称：人才市场那一支只收负责人级，否则人才市场里的专员会挤进名单。
                Debug.Log($"来源视图 并集(全部) → 人才市场总监={StaffRecruitment.AcceptedInAnySourceView(director)}（期望 True）"
                    +$" 校招专员={StaffRecruitment.AcceptedInAnySourceView(listedCampus)}（期望 True）"
                    +$" 猎头专员={StaffRecruitment.AcceptedInAnySourceView(listedHead)}（期望 True）"
                    +$" 无来源专员={StaffRecruitment.AcceptedInAnySourceView(unlisted)}（期望 False：人才市场非负责人不进并集）"
                    +$" 在职总监={StaffRecruitment.AcceptedInAnySourceView(inService)}（期望 False：不在人才池）");

                // 来源标签 ↔ 渠道编号：UI 已不再用「页签下标 − 1」推渠道号，
                // 换算收敛到 HrEconomics.ChannelOfTag。0..3 必须严格互逆，
                // 且人才市场 / 继承在册这些**非渠道来源**必须返回 -1（否则「全部」会误判成有渠道可呈请）。
                var sbCh = new System.Text.StringBuilder("来源↔渠道 换算 → ");
                for (int c = 0; c <= 3; c++)
                    sbCh.Append($"{c}↔{HrEconomics.SourceTag(c)}→{HrEconomics.ChannelOfTag(HrEconomics.SourceTag(c))} ");
                Debug.Log(sbCh.ToString() + "（期望 0↔campus→0、1↔headhunt→1、2↔overseas→2、3↔internal→3，逐一互逆）");
                Debug.Log($"来源↔渠道 非渠道来源 → 人才市场={HrEconomics.ChannelOfTag(HrEconomics.SourceMarket)}（期望 -1）"
                    +$" 继承在册={HrEconomics.ChannelOfTag(HrEconomics.SourceInherited)}（期望 -1）"
                    +$" 空来源={HrEconomics.ChannelOfTag(null)}（期望 -1：按人才市场处理）");
            }
            catch (Exception he)
            {
                Debug.LogWarning($"招募岗位口径用例异常（不阻断后续）：{he.Message}");
            }

            // —— R19：投机候选池（TalentMarket）—— 名单不再只有总监（CEO 裁定 2026-10-11）——
            //
            // 三条必须成立，缺一条这个特性在 UI 上就是「点不动的按钮」：
            //   ① 渠道 → 岗位的映射不漂（校招=专员档 / 猎头=主管・副总监 / 海外=运行岗+主管）；
            //   ② 释放幂等且封顶（池量 ≤ 上限；重复调用与读档都不会多给人）；
            //   ③ 没有招聘案也能录用（按**编制缺口**放行）—— 投机候选人不属于任何一次呈请，
            //      若沿用旧闸门（必须已有 Vacancy），玩家会在名单里点到一个永远失败的「录用」。
            // 另附一条 id 唯一性回归：CreateForRole 的序号派生自 staffPool.Count，池子变小后会撞号。
            var goTM = new GameObject("__talent_market_test_gs__");
            try
            {
                var gsTM = goTM.AddComponent<GameState>();
                gsTM.staffPool = new List<PersonnelData>();
                gsTM.staffBootstrapped = true;
                gsTM.hubIata = "PEK";

                // ① 运行岗集合不漂：TalentMarket.CrewPosts 每一项都必须被 HrManager 认成运行岗，
                //    且能推导出所属部门 —— 否则「海外引进补机组」会落到编制表外，招募封顶与
                //    国际航线准入（OverseasCrewCount）两条口径同时失效。
                var sbCrew = new System.Text.StringBuilder("R19 运行岗集合 → ");
                bool crewOk = true;
                for (int i = 0; i < TalentMarket.CrewPosts.Length; i++)
                {
                    string post = TalentMarket.CrewPosts[i];
                    bool isCrew = HrManager.IsCrewPost(post);
                    string dept = HrManager.DeptForPosition(post);
                    if (!isCrew || string.IsNullOrEmpty(dept)) crewOk = false;
                    sbCrew.Append($"{post}({dept}/IsCrewPost={isCrew}) ");
                }
                Debug.Log(sbCrew.ToString() + $"→ 全部合格={crewOk}（期望 True：3 项均为运行岗且有部门）");

                // ② 播种：全空时补满三个渠道，再调一次不得多给（幂等）
                int seeded = TalentMarket.EnsureSeeded(gsTM);
                int seededAgain = TalentMarket.EnsureSeeded(gsTM);
                int capCampus = TalentMarket.PoolCount(gsTM, HrEconomics.SourceCampus);
                int capHead = TalentMarket.PoolCount(gsTM, HrEconomics.SourceHeadhunt);
                int capOver = TalentMarket.PoolCount(gsTM, HrEconomics.SourceOverseas);
                Debug.Log($"R19 播种 → 首次释放 {seeded} 人（期望 {TalentMarket.CampusCap + TalentMarket.HeadhuntCap + TalentMarket.OverseasCap}）"
                    + $" 二次释放 {seededAgain} 人（期望 0：池量派生自 staffPool，重复执行幂等）");
                Debug.Log($"R19 封顶 → 校招 {capCampus}/{TalentMarket.CampusCap}"
                    + $" 猎头 {capHead}/{TalentMarket.HeadhuntCap} 海外 {capOver}/{TalentMarket.OverseasCap}"
                    + "（期望逐一等于上限，且不超出）");
                bool capOk = capCampus == TalentMarket.CampusCap && capHead == TalentMarket.HeadhuntCap
                    && capOver == TalentMarket.OverseasCap;
                Debug.Log($"R19 封顶结论 → 全部等于上限={capOk}（期望 True：否则说明 Fill 的 k<cap 循环多放或少放）");

                // ③ 渠道 → 职级分层：每个渠道释放出来的人，其 rank 必须落在该渠道的档位上。
                //    这条是「来源列有信息量」的底线 —— 若三个渠道都产出同一种人，来源就只是换个显示位置。
                int campusSpec = 0, headMgmt = 0, seaBad = 0;
                for (int i = 0; i < gsTM.staffPool.Count; i++)
                {
                    var p = gsTM.staffPool[i];
                    if (p == null) continue;
                    string tag = HrEconomics.NormalizeSource(p.source);
                    if (tag == HrEconomics.SourceCampus && p.rank == "专员") campusSpec++;
                    else if (tag == HrEconomics.SourceHeadhunt
                        && (p.rank == "主管" || p.rank == "副手") && !HrManager.IsCrewPost(p.jobTitle)) headMgmt++;
                    else if (tag == HrEconomics.SourceOverseas
                        && (HrManager.IsCrewPost(p.jobTitle) || p.rank == "主管")) { }
                    else seaBad++;
                }
                Debug.Log($"R19 渠道分层 → 校招专员 {campusSpec}/{capCampus}"
                    + $" 猎头主管副手 {headMgmt}/{capHead} 落档异常 {seaBad}（期望 0）"
                    + "。校招必为专员档、猎头必为主管/副手（且非运行岗）、海外为运行岗或主管。");

                // ④ 释放节拍：非释放日不动，释放日才补
                bool notReleaseDay = TalentMarket.IsReleaseDay(new DateTime(2026, 10, 5));
                bool isDay1 = TalentMarket.IsReleaseDay(new DateTime(2026, 10, 1));
                bool isDay16 = TalentMarket.IsReleaseDay(new DateTime(2026, 10, 16));
                Debug.Log($"R19 节拍判定 → 10-05={notReleaseDay}（期望 False） 10-01={isDay1}（期望 True）"
                    + $" 10-16={isDay16}（期望 True）");
                int beforeOffDay = gsTM.staffPool.Count;
                TalentMarket.TickDay(gsTM, new DateTime(2026, 10, 5));
                int afterOffDay = gsTM.staffPool.Count;
                Debug.Log($"R19 非释放日 → 池量 {beforeOffDay}→{afterOffDay}（期望不变）");

                // 消耗一个校招候选人后再到释放日：应只补回被消耗的那一个，不是重发一整批
                // （上限建立在 PoolCount 上，故只能补到 cap 为止）。
                var victim = null as PersonnelData;
                for (int i = 0; i < gsTM.staffPool.Count; i++)
                {
                    var p = gsTM.staffPool[i];
                    if (p != null && HrEconomics.NormalizeSource(p.source) == HrEconomics.SourceCampus) { victim = p; break; }
                }
                if (victim != null) gsTM.staffPool.Remove(victim);
                TalentMarket.TickDay(gsTM, new DateTime(2026, 10, 16));
                int refilled = TalentMarket.PoolCount(gsTM, HrEconomics.SourceCampus);
                Debug.Log($"R19 消耗后补齐 → 校招 {refilled}/{TalentMarket.CampusCap}"
                    + "（期望回到上限：补被消耗的那一个，其余渠道已满故一个不发）");

                // ⑤ 无招聘案也能录用：编制席走「配额 − 已在位」，不再要求先呈请。
                //    取 FIN「司库」（配额 1，开局无人）：判定 → 直录 → 招满后拒绝。
                gsTM.staffPool.Clear();
                gsTM.hr = HrManager.EnsureState(gsTM);
                gsTM.hr.vacancies = new List<Vacancy>();
                var treasurer = PersonnelGenerator.CreateForRole(gsTM, "FIN", "主管", "司库", HrEconomics.SourceHeadhunt);
                var v1 = HrManager.EvaluateHire(gsTM, treasurer);
                Debug.Log($"R19 闸门判定（无案）→ 招聘案数={gsTM.hr.vacancies.Count}（期望 0：刻意不建案）"
                    + $" ok={v1.ok}/走案={v1.viaVacancy}/部门={v1.dept}/职级={v1.rank}/余量 {v1.roomLeft}"
                    + "（期望 True/False/FIN/主管/1）"
                    + "。旧闸门要求「必须先有呈请案」，而投机候选池的人不属于任何一次呈请 → 名单里的「录用」永远失败。");

                bool hired1 = HrManager.HireCandidate(gsTM, treasurer.id, out string msg1);
                Debug.Log($"R19 无案直录 → 录用={hired1}（期望 True） 提示「{msg1}」");
                Debug.Log($"R19 编制席属性 → onMarket={treasurer.onMarket}（期望 False）"
                    + $" isOperational={treasurer.isOperational}（期望 False：编制席占编制表名额、参与部门关键岗效率）"
                    + $" 岗位={treasurer.jobTitle}/{treasurer.rank} 部门={treasurer.department}"
                    + "。旧实现 ConfirmHire 无条件标 isOperational=true，编制席会「在编却不算在编」。");

                // 招满后再判一次：拒绝依据必须是「缺口为 0」，不能再是「没有进行中的招聘案」。
                var treasurer2 = PersonnelGenerator.CreateForRole(gsTM, "FIN", "主管", "司库", HrEconomics.SourceHeadhunt);
                var v2 = HrManager.EvaluateHire(gsTM, treasurer2);
                bool hired2 = HrManager.HireCandidate(gsTM, treasurer2.id, out string msg2);
                Debug.Log($"R19 席位已满拒绝 → 判定 ok={v2.ok}/走案={v2.viaVacancy}/余量 {v2.roomLeft}（期望 False/False/0）"
                    + $" 录用={hired2}（期望 False） 提示「{msg2}」"
                    + "（期望含「编制已满」：拒绝依据是缺口，不再是「没有进行中的招聘案」）");

                // ⑥ 有招聘案时以案为准：案的 needed 在发起时已被「编制 − 在职 − 在途」裁剪过，
                //    故闸门**不再**叠一层编制缺口判定 —— 否则玩家刚呈请一批、落池的候选反而录不进。
                gsTM.staffPool.Clear();
                gsTM.hr.vacancies = new List<Vacancy>
                {
                    new Vacancy
                    {
                        position = "收益管理主管",
                        needed = 1, hired = 0, inPool = 1, urgency = 0,
                        channel = HrEconomics.ChannelOfTag(HrEconomics.SourceHeadhunt)
                    }
                };
                var rm1 = PersonnelGenerator.CreateForRole(gsTM, "MKT", "主管", "收益管理主管", HrEconomics.SourceHeadhunt);
                var v3 = HrManager.EvaluateHire(gsTM, rm1);
                bool hired3 = HrManager.HireCandidate(gsTM, rm1.id, out string msg3);
                Debug.Log($"R19 有案优先 → 走案={v3.viaVacancy}/余量 {v3.roomLeft}（期望 True/1）"
                    + $" 录用={hired3}（期望 True） 案账本 hired/inPool={gsTM.hr.vacancies[0].hired}/{gsTM.hr.vacancies[0].inPool}（期望 1/0）"
                    + "。账本不同步扣，玩家会看到「已录用 1 人但案内仍显示待处理 1 人」。");

                var rm2 = PersonnelGenerator.CreateForRole(gsTM, "MKT", "主管", "收益管理主管", HrEconomics.SourceHeadhunt);
                var v4 = HrManager.EvaluateHire(gsTM, rm2);
                Debug.Log($"R19 案满拒绝 → ok={v4.ok}/走案={v4.viaVacancy}（期望 False/True）"
                    + $" 提示「{v4.reason}」（期望「…招聘案已招满」：有案时按案拒绝，不再退到编制缺口口径）");

                // ⑦ id 唯一性回归：制造池子「先增后减」再新建，旧实现会撞号。
                //    旧序号 = staffPool.Count + 1，摘掉一个人后 Count 回落 → 新 id 撞上仍在池里的那个人。
                gsTM.staffPool.Clear();
                var a1 = PersonnelGenerator.CreateForRole(gsTM, "FIN", "主管", "司库", HrEconomics.SourceHeadhunt);
                var a2 = PersonnelGenerator.CreateForRole(gsTM, "FIN", "主管", "司库", HrEconomics.SourceHeadhunt);
                string id1 = a1.id, id2 = a2.id;
                gsTM.staffPool.Remove(a1);
                var a3 = PersonnelGenerator.CreateForRole(gsTM, "FIN", "主管", "司库", HrEconomics.SourceHeadhunt);
                bool idUnique = a3.id != id2 && PersonnelGenerator.Find(gsTM, a3.id) == a3;
                Debug.Log($"R19 id 唯一性 → 摘人前 {id1} / {id2}　摘掉 {id1} 后新建 {a3.id}"
                    + $" 与在池者不同={a3.id != id2}（期望 True） 可按 id 取回={PersonnelGenerator.Find(gsTM, a3.id) == a3}（期望 True）"
                    + $"，合计唯一={idUnique}（期望 True）");
            }
            catch (Exception tme)
            {
                Debug.LogWarning($"投机候选池用例异常（不阻断后续）：{tme.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goTM);
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

            // —— R20：运力指令「记忆」的三态（挂起不生效 → 批准落盘 → 驳回抹掉）——
            // 与 slotcheck R20 分工：那边钉「记忆如何参与解析」（纯函数，注入桩）；
            // 这边钉「候选何时才算数」（存储态，走 PlayerPrefs/JsonUtility，离线测不了）。
            try
            {
                const string phrase = "把班次收一收";
                var parsedBefore = CapacityDirectiveLogic.Parse(phrase);

                DirectiveLexicon.Clear();
                // 关键词本来就认不出这句 → 是记忆要解决的场景
                Debug.Log($"记忆 前置 → 「{phrase}」关键词解析 intentKnown={parsedBefore.intentKnown}（期望 False）"
                          + $" 需确认={CapacityDirectiveLogic.NeedsConfirm(parsedBefore)}（期望 True）"
                          + $" 已记条数={DirectiveLexicon.Count}（期望 0）");

                // ① 挂起（模拟：分析服务读懂、CEO 还没批复）
                DirectiveLexicon.Stage(phrase, new CapacityDirectiveLogic.Directive
                {
                    raw = phrase, intent = "reduce", intentKnown = true, waves = 1, buildRing = true
                }, fromAi: true);
                var staged = CapacityDirectiveLogic.Parse(phrase);
                Debug.Log($"记忆 挂起 → 挂起条数={DirectiveLexicon.PendingCount}（期望 1）"
                          + $" 已生效={staged.fromLearned}（期望 False，未批准不得参与解析）"
                          + $" 已记条数={DirectiveLexicon.Count}（期望 0）");
                Debug.Log($"记忆 挂起明细 → 候选={string.Join("、", DirectiveLexicon.PendingPhrases())}");

                // ② 待确认指令确认执行后：需二次确认的路径不能被记忆短路成「直接办」——
                //    记忆命中的是**已批准**的说法，这里还没批准，故仍须过 NeedsConfirm。
                bool stillConfirm = CapacityDirectiveLogic.NeedsConfirm(staged);
                Debug.Log($"记忆 挂起不短路确认闸门 → 需确认={stillConfirm}（期望 True）");

                // ③ 批准 → 落盘，此刻这句话就算关键词了
                bool promoted = DirectiveLexicon.Promote(phrase);
                var promotedParse = CapacityDirectiveLogic.Parse(phrase);
                Debug.Log($"记忆 批准 → 学到={promoted}（期望 True） 已记条数={DirectiveLexicon.Count}（期望 1）"
                          + $" 已生效={promotedParse.fromLearned}（期望 True）"
                          + $" 意图={promotedParse.intent}（期望 reduce） 班次={promotedParse.waves}（期望 1）"
                          + $" 需确认={CapacityDirectiveLogic.NeedsConfirm(promotedParse)}（期望 False：教过一次就不再问）"
                          + $" 通道={CapacityDirectiveLogic.ChannelLabel(promotedParse, false)}（期望含「记忆」）");
                Debug.Log($"记忆 已记原话 → {string.Join("、", DirectiveLexicon.Phrases())}");

                // ④ 关键词本来就认得的说法不该占用记忆槽
                DirectiveLexicon.Stage("加密到每天3班", new CapacityDirectiveLogic.Directive
                { raw = "加密到每天3班", intent = "increase", intentKnown = true, waves = 3 }, fromAi: true);
                bool promotedKnown = DirectiveLexicon.Promote("加密到每天3班");
                Debug.Log($"记忆 不记词表已知说法 → 学到={promotedKnown}（期望 False） 已记条数={DirectiveLexicon.Count}（期望仍为 1）");

                // ⑤ 驳回 → 抹掉（宁可少记，不可记错）
                DirectiveLexicon.Discard(phrase);
                var discarded = CapacityDirectiveLogic.Parse(phrase);
                Debug.Log($"记忆 驳回 → 已记条数={DirectiveLexicon.Count}（期望 0）"
                          + $" 已生效={discarded.fromLearned}（期望 False）"
                          + $" 退回需确认={CapacityDirectiveLogic.NeedsConfirm(discarded)}（期望 True）");

                // ⑥ 跨「重启」持久化：重载注入后仍认（同一次运行内验证存储往返，玩家重启后同理）
                DirectiveLexicon.Stage(phrase, new CapacityDirectiveLogic.Directive
                { raw = phrase, intent = "reduce", intentKnown = true, waves = 2 }, fromAi: true);
                DirectiveLexicon.Promote(phrase);
                int countBeforeReload = DirectiveLexicon.Count;
                DirectiveLexicon.ReloadForTests();
                var reloaded = CapacityDirectiveLogic.Parse(phrase);
                Debug.Log($"记忆 持久化往返 → 重载前条数={countBeforeReload}（期望 1）"
                          + $" 重载后已生效={reloaded.fromLearned}（期望 True）"
                          + $" 意图={reloaded.intent}（期望 reduce） 班次={reloaded.waves}（期望 2，说明存的是量不是默认值）");

                // 收场：别把测试教的东西留在 PlayerPrefs 里影响真实游玩
                DirectiveLexicon.Clear();
                Debug.Log($"记忆 收场 → 已记条数={DirectiveLexicon.Count}（期望 0）"
                          + $" 已生效={CapacityDirectiveLogic.Parse(phrase).fromLearned}（期望 False）");
            }
            catch (Exception me)
            {
                Debug.LogWarning($"运力指令记忆用例异常（不阻断后续）：{me.Message}");
            }

            // —— R21：航班时刻加机呈报（n98 点时段 → 呈报 → CEO 批准才落班）——
            // 与 slotcheck R21 分工：那边钉编解码与时刻口径（纯函数，无需 GameState）；
            // 这边钉三态：呈报期**不落班** → 批准才落班（并按两端占槽）→ 退回即无痕。
            // 「呈报期不落班」是整个设计的地基：若先落班再等批复，退回就得撤销班次，
            // 而撤销在飞班次会连带 schedulePending 那套冻结语义，为一班连夜波及整条航线。
            var goAf = new GameObject("__assign_flight_test_gs__");
            try
            {
                var gsA = goAf.AddComponent<GameState>();
                gsA.activeRoutes = new List<RouteData>();
                gsA.fleet = new List<Aircraft>();
                gsA.inbox = new List<InboxMail>();
                gsA.year = 1;
                gsA.quarter = 1;

                var rA = new RouteData
                {
                    routeId = "R-R21", origin = "PEK", destination = "SZX", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU500", registration = "B-9001",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                rA.SyncFlightsFromPrimary();
                gsA.activeRoutes.Add(rA);
                gsA.fleet.Add(new Aircraft { registration = "B-2006", type = "A320neo" });
                int flights0 = rA.flights.Count;   // 期望 1（首班 mirror 展平）

                // ① 无空闲持仓：不在 n98 上摆一段红字警告，而是运营部按**整张需求波网格**摆出备选时刻对，
                //    让 CEO 点定一刻再去开申请单（★2026-10-15 第十五次问询：
                //    「是否应该在邮件提供几个备选时刻槽，并分析优缺点」——旧版只报「办不成」，没有选择）。
                // 时刻取晚班窗口（16:30–20:00）里**真排得进**的那一刻：本线往返 5.3 h，PEK 宵禁
                // 23:00–06:00，故 18:00 及以后起飞都会被飞机硬门槛当场挡下（先机后槽），
                // 走不到「时刻槽备选」这一段。16:30 / 17:00 / 17:30 才是本波真正可选的三刻。
                string lateClock = "17:30";
                bool queuedNoSlot = InboxManager.QueueAssignFlight(gsA, rA, "B-2006", lateClock, out string msgNoSlot);
                var selMail = FindMailByAction(gsA, InboxManager.SlotSelectAction);
                var opts0 = SlotCandidateLogic.Decode(selMail?.payloadCsv);
                string firstOpt = opts0.Count > 0 ? opts0[0].depClock : "(无备选)";
                Debug.Log($"R21 缺槽 → 加机呈报={queuedNoSlot}（期望 False：槽都没有，报批加机只是把问题推给下一环）"
                          + $" 时刻**备选件**={CountMails(gsA, InboxManager.SlotSelectAction)}（期望 1）"
                          + $" 备选={opts0.Count}（期望 ≥2：整波网格上都拿不到时才改发受阻汇报）"
                          + $" 需 CEO 决策={InboxManager.RequiresDecision(selMail)}（期望 True）"
                          + $"\n　★关键：班次表未动 {flights0} → {rA.flights.Count}（期望仍为 {flights0}）"
                          + $"\n　★关键2：申请的是**系列时刻**、不是排班器的 5 分钟粒度时刻"
                          + $"（首条 {firstOpt}，在 {ScheduleSlots.DepTimes[ScheduleSlots.BandOfDep(lateClock)]} 所属波的 30 分网格上）"
                          + $"\n　★关键3：备选件**结构化**记住为哪条线而提（slotRouteId={selMail?.slotRouteId}，期望 {rA.routeId}）"
                          + $"\n　页面上给玩家的回执（不再是缺槽红字，而是「已呈报备选」）：{msgNoSlot}"
                          + $"\n　备选件主题「{selMail?.subject}」");

                // ①′ 点定一条 = CEO 选定**哪一刻**；运营部这才去为缺槽的那一端开申请单（下季交割）。
                //     点选**不是**落班，也不是「同意买槽」这句空话 —— 它必须精确落在某一刻上。
                string pickClock = opts0.Count > 0 ? opts0[0].depClock : lateClock;
                bool okSel = InboxManager.ApproveSlotSelect(gsA, selMail, pickClock, out string msgSel);
                var applyMail = FindMailByAction(gsA, InboxManager.ApplySlotAction);
                Debug.Log($"R21 点定备选 → 成功={okSel}（期望 True） 备选件状态={selMail?.status}（期望 approved）"
                          + $" 新开时刻槽申请单={CountMails(gsA, InboxManager.ApplySlotAction)}（期望 ≥1）"
                          + $"\n　★关键：选定结果写回备选件本身（slotFlightClock={selMail?.slotFlightClock}，期望 {pickClock}）"
                          + $"\n　★关键2：申请单上的时刻是**缺槽那一端**的系列时刻「{applyMail?.slotClock}」"
                          + $"；并记住为哪一刻的班次而提（slotFlightClock={applyMail?.slotFlightClock}，期望 {pickClock}）"
                          + $"\n　★关键3：班次表仍未动 {rA.flights.Count}（期望 {flights0}）"
                          + $"\n　批复：{msgSel}");
                string msgApprSlot = "（没有可批准的申请单）";
                bool apprSlot = applyMail != null && InboxManager.Approve(gsA, applyMail, out msgApprSlot);
                Debug.Log($"R21 缺槽单批准 → 成功={apprSlot}（期望 True）"
                          + $" 待交割={(applyMail != null && applyMail.effectPending)}（期望 True）"
                          + $" 交割季=第{applyMail?.effectYear}年Q{applyMail?.effectQuarter}（期望第1年Q2）"
                          + $"\n　批复：{msgApprSlot}"
                          + $"\n　★关键：批准「申请槽」不是落班 —— 班次仍为 {rA.flights.Count}（期望 {flights0}）");

                // 清场：后面的用例按 gsA.inbox[0] 取件，这里把 ① 的申请单移出（其行为已断言完）
                for (int i = gsA.inbox.Count - 1; i >= 0; i--)
                    if (gsA.inbox[i] != null && (gsA.inbox[i].action == InboxManager.ApplySlotAction
                        || gsA.inbox[i].action == InboxManager.SlotSelectAction))
                        gsA.inbox.RemoveAt(i);

                // ② 备妥两端**逐时刻**的空闲持仓（★2026-10-15：时刻槽精确到时刻 ——
                //    06:30 / 07:00 / 17:30 各是各的槽，旧写法「一条早班槽通吃整个早班」已不成立）。
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    var grid = ScheduleSlots.SlotGridLabels(b);
                    for (int k = 0; k < grid.Count; k++)
                    {
                        SeedSlotAt(gsA, "PEK", grid[k]);
                        SeedSlotAt(gsA, "SZX", grid[k]);
                    }
                }

                // ③ 呈报：一封邮件，班次表一根汗毛不动
                string flyLate = lateClock;                             // 17:30（晚班波里排得进的那一刻）
                bool queued = InboxManager.QueueAssignFlight(gsA, rA, "B-2006", flyLate, out string msgQ1);
                var mailId1 = gsA.inbox.Count > 0 ? gsA.inbox[0].id : "";
                Debug.Log($"R21 呈报 → 成功={queued}（期望 True） 收件箱={gsA.inbox.Count}（期望 1）"
                          + $" action={gsA.inbox[0].action}（期望 {InboxManager.AssignFlightAction}）"
                          + $" 需 CEO 决策={InboxManager.RequiresDecision(gsA.inbox[0])}（期望 True）"
                          + $"\n　★关键：呈报期班次表未动 {flights0} → {rA.flights.Count}（期望仍为 {flights0}）"
                          + $"\n　说明：{msgQ1}");

                // ④ 同一条航线的新增累加到同一封（不是点几次就几封）
                bool queued2 = InboxManager.QueueAssignFlight(gsA, rA, "B-2006", "06:30", out string msgQ2);
                var pending2 = FlightProposalLogic.Decode(gsA.inbox[0].flightProposalsCsv);
                Debug.Log($"R21 同封累加 → 成功={queued2}（期望 True） 收件箱={gsA.inbox.Count}（期望仍 1）"
                          + $" 待批={pending2.Count}（期望 2） 主题「{gsA.inbox[0].subject}」"
                          + $" 班次仍未动={rA.flights.Count}（期望 {flights0}）");

                // ④′ **同段多波**（2026-10-15 新增）：06:30 与 07:00 同属早班需求波，却是两条不同的槽。
                //    旧口径按「机号 × 时段」去重，第二条会被判成「已在待批」而报不上 —— 这正是 CEO 问的那件事。
                bool queued3 = InboxManager.QueueAssignFlight(gsA, rA, "B-2006", "07:00", out string msgQ3a);
                var pending3 = FlightProposalLogic.Decode(gsA.inbox[0].flightProposalsCsv);
                Debug.Log($"R21 同段多波 → 早班波内 06:30 + 07:00 两条都呈报成功={queued3}（期望 True）"
                          + $" 待批={pending3.Count}（期望 3：17:30 / 06:30 / 07:00）"
                          + $" 说明：{msgQ3a}");

                // ⑤ 去重：同一**时刻**待批里已有 / 该机该时刻已在执飞 —— 都不该再进呈报
                bool dupPending = InboxManager.QueueAssignFlight(gsA, rA, "B-2006", "06:30", out string msgDupP);
                string flyingClock = rA.depTime;                        // 现有首班 08:00
                bool dupFlying = InboxManager.QueueAssignFlight(gsA, rA, "B-9001", flyingClock, out string msgDupF);
                Debug.Log($"R21 去重 → 同一时刻已在待批={dupPending}（期望 False）　该机已执飞该时刻({flyingClock})={dupFlying}（期望 False）"
                          + $" 待批仍 {FlightProposalLogic.Decode(gsA.inbox[0].flightProposalsCsv).Count} 条（期望 3）"
                          + $"\n　{msgDupP}　{msgDupF}");

                // ⑥ 批准才落班，并按去程/回程两端各占 1 条时刻槽。
                //    ★批准那一刻**逐条复验飞机硬门槛**（FlightProposalLogic.Apply）：本批 3 条里
                //    17:30 与 06:30 都排得进，07:00 却排不进 —— 同一条往返的 06:30 已经占着 B-2006
                //    （回场 11:48），07:00 起飞意味着飞机还在空中。这正是复验要挡的：呈报时可以摆出
                //    「同一波里的两条时刻」供 CEO 比较，落班时必须逐条站得住。
                int inUseBefore = CountInUseSlots(gsA);
                bool appr = InboxManager.Approve(gsA, gsA.inbox[0], out string msgAppr);
                int inUseAfter = CountInUseSlots(gsA);
                Debug.Log($"R21 批准 → 成功={appr}（期望 True） 班次 {flights0} → {rA.flights.Count}（期望 {flights0 + 2}：07:00 复验不过）"
                          + $" 邮件状态={gsA.inbox[0].status}（期望 approved）"
                          + $" 仍需决策={InboxManager.RequiresDecision(gsA.inbox[0])}（期望 False）"
                          + $"\n　占用持仓 {inUseBefore} → {inUseAfter}（期望 +4：2 班 × 去程/回程各一条；+0 说明落班根本没占槽）"
                          + $"\n　批复：{msgAppr}"
                          + $"\n　现有班次：{FlightsCsv(rA)}");

                // ⑦ 退回 = 什么都没发生过（提案既然在邮件里，就不需要「撤销」这一步）。
                //    这里换用 B-2007：B-2006 刚落了 06:30 与 17:30 两班，午班 12:00 与 17:30 的往返
                //    只差 12 分钟（< 最短衔接 30 分），拿它去点「午班」会被硬门槛拦下 —— 那是另一条用例的事。
                gsA.fleet.Add(new Aircraft { registration = "B-2007", type = "A320neo" });
                int flightsBeforeReject = rA.flights.Count;
                bool q3 = InboxManager.QueueAssignFlight(gsA, rA, "B-2007", "午班", out string msgQ3);
                bool newBatch = !string.IsNullOrEmpty(mailId1) && gsA.inbox[0].id != mailId1;
                InboxManager.RejectAssignFlight(gsA, gsA.inbox[0], out string msgRej);
                Debug.Log($"R21 退回 → 上一批已结案则开新编号={newBatch}（期望 True） 呈报={q3}（期望 True）"
                          + $"\n　★关键：班次 {flightsBeforeReject} → {rA.flights.Count}（期望不变）"
                          + $" 邮件状态={gsA.inbox[0].status}（期望 rejected）"
                          + $"\n　退回说明：{msgRej}");

                // ⑧ 批准时复验时刻槽：呈报期间槽被别的航线拿走 → 宁可如实拒落，也不落一个占不到槽的班次
                bool q4 = InboxManager.QueueAssignFlight(gsA, rA, "B-2007", "12:00", out string msgQ4);
                for (int i = gsA.stg.slots.Count - 1; i >= 0; i--)
                    if (gsA.stg.slots[i] != null && !gsA.stg.slots[i].inUse) gsA.stg.slots.RemoveAt(i);
                int flightsBeforeBlock = rA.flights.Count;
                InboxManager.Approve(gsA, gsA.inbox[0], out string msgBlock);
                Debug.Log($"R21 批准时丢槽 → 呈报={q4}（期望 True） 空闲持仓已清零"
                          + $"\n　★关键：批准后班次 {flightsBeforeBlock} → {rA.flights.Count}（期望不变：不该落下占不到槽的班次）"
                          + $"\n　批复：{msgBlock}");

                // ⑨ 结案后同一编号可复用同一批（不因「改主意」把历次邮件堆成一片）
                var openAssign = FindMailByAction(gsA, InboxManager.AssignFlightAction);
                Debug.Log($"R21 编号 → 第一封 id={mailId1}　当前待批 id={openAssign?.id}"
                          + $"　同航线同时只一封未结案={CountOpenAssignMails(gsA) <= 1}（期望 True）");

                // ⑩ 池已满（连申请都无门）：不静默、也不开一张「批了也拿不到」的空头提案，更**不给备选**
                //    （整张网格都拿不到时开一张没有选项的选择题，等于把「办不成」包装成需要 CEO 拍板）。
                //    发「受阻」汇报件并列出两端池况与三条下一步；汇报件 action 留空 → 不占 CEO 决策按钮。
                //    用 12:00：本航线在午班没有在飞班次，端上既无空闲持仓也无祖父可依（⑧ 已清空空闲持仓）。
                //    两端各波次的**每个时刻**都填满，免得回程端落在哪个时刻没覆盖到。
                string noonClock = ScheduleSlots.DepOf("午班");          // 12:00
                for (int b = 0; b < ScheduleSlots.Labels.Length; b++)
                {
                    FillPool(gsA, "PEK", b);
                    FillPool(gsA, "SZX", b);
                }
                int slotMailsBefore = CountMails(gsA, InboxManager.ApplySlotAction);
                int selBefore = CountMails(gsA, InboxManager.SlotSelectAction);
                bool q5 = InboxManager.QueueAssignFlight(gsA, rA, "B-2007", noonClock, out string msgQ5);
                int blockedReports = CountMails(gsA, "", "无槽可得");
                var blockedMail = FindMailBySubject(gsA, "无槽可得");
                Debug.Log($"R21 池满 → 加机呈报={q5}（期望 False）"
                          + $" 未新增时刻槽申请单={CountMails(gsA, InboxManager.ApplySlotAction) == slotMailsBefore}（期望 True：申请无门就别开空头提案）"
                          + $" 未新增备选件={CountMails(gsA, InboxManager.SlotSelectAction) == selBefore}（期望 True：一条备选都排不出时不许开没有选项的选择题）"
                          + $" 受阻汇报件={blockedReports}（期望 1：同航线同波同季只留一封，不按端数刷屏）"
                          + $" 汇报件需 CEO 决策={InboxManager.RequiresDecision(blockedMail)}（期望 False）"
                          + $" 回执说明本班未呈报={msgQ5.Contains("本班未呈报")}（期望 True）"
                          + $"\n　★关键：班次表仍未动 {rA.flights.Count}"
                          + $"\n　页面回执：{msgQ5}"
                          + $"\n　汇报件：{blockedMail?.subject}｜{blockedMail?.body}");

                // ⑩′ 重复点同一时刻：同一封汇报件不重复投递（免得连点几次刷出一叠同文）
                bool q6 = InboxManager.QueueAssignFlight(gsA, rA, "B-2007", noonClock, out string msgQ6);
                Debug.Log($"R21 池满重复点 → 加机呈报={q6}（期望 False）"
                          + $" 受阻汇报件仍 {blockedReports} 封={CountMails(gsA, "", "无槽可得") == blockedReports}（期望 True）"
                          + $"\n　页面回执：{msgQ6}");
            }
            catch (Exception ae)
            {
                Debug.LogWarning($"加机呈报用例异常（不阻断后续）：{ae.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goAf);
            }

            // —— R22：退出机队的两条路（自有/二手 → 退役回收残值；租赁 → 退租交还出租人，不回收残值）——
            // CEO 提问（2026-10-13）触发：「经营租赁是从租赁市场租来的飞机，就不该叫退役，应该是退租」。
            // 查证发现旧口径不只是叫错：TryRetireAircraft 对租赁机照发残值 = 目录价 ×（100 − 机龄×3）%，
            // 而经营租赁零首付（月租 2.2%）→ 租进来当天「退役」即可白得整机目录价，是凭空造钱的口子。
            // 本用例钉四件事：① 自有机退役仍回收残值 ② 经营租赁退租 0 现金 ③ 融资租赁提前退租按
            // 剩余租期租金 × 30% 收违约金、且随已付期数递减到 0 ④ 退租后月租/月维护停计、在册数不再含它。
            var goExit = new GameObject("__fleet_exit_test_gs__");
            try
            {
                var gsExit = goExit.AddComponent<GameState>();
                var fmExit = goExit.AddComponent<FleetManager>();
                gsExit.fleet = new List<Aircraft>();
                gsExit.year = 1;
                gsExit.quarter = 1;
                gsExit.cash = 5000f;

                // ① 自有：退役回收残值（旧口径不变，不能因为改租赁口径把这条也改了）
                var ownedX = new Aircraft
                {
                    registration = "B-8001", type = "A320neo", source = FleetEconomics.SourceOwned,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f, age = 5
                };
                gsExit.fleet.Add(ownedX);
                float cashOwned0 = gsExit.cash;
                bool okOwned = fmExit.TryRetireAircraft(gsExit, "B-8001", out string msgOwned);
                float residualExpect = FleetEconomics.ResidualValue(1000f, 5);
                Debug.Log($"R22 自有退役 → 成功={okOwned}（期望 True） 现金 {cashOwned0:F0} → {gsExit.cash:F0}（期望 +{residualExpect:F0} 残值）"
                          + $" 退出方式={ownedX.ExitLabel}（期望 退役） 在册数={FleetManager.InServiceCount(gsExit)}（期望 0）"
                          + $"　留档仍可查={gsExit.fleet.Count == 1}（期望 True：退役机仍留在 fleet 里备查）"
                          + $"\n　{msgOwned}");

                // ② 经营租赁：退租 = 交还出租人 → 0 现金（旧口径在此白送整机目录价）
                var opLease = new Aircraft
                {
                    registration = "B-8002", type = "A320neo", source = FleetEconomics.SourceOperatingLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1090f,
                    monthlyLeaseWan = 24f, age = 0
                };
                gsExit.fleet.Add(opLease);
                float cashOp0 = gsExit.cash;
                bool okOp = fmExit.TryRetireAircraft(gsExit, "B-8002", out string msgOp);
                Debug.Log($"R22 经营租赁退租 → 成功={okOp}（期望 True） 现金变动 {gsExit.cash - cashOp0:F0} 万（期望 0：产权在出租人，无残值可回收）"
                          + $" 退出方式={opLease.ExitLabel}（期望 退租）"
                          + $"　旧口径本会白得 {FleetManager.CalcResidualValue(opLease):F0} 万（= 目录价 × 100%，零首付租进当天退）"
                          + $"\n　{msgOp}");

                // ③ 融资租赁：含买断条款，提前退租属违约 → 剩余租期租金 × 30%（与过渡业务「提前召回」同口径）
                var finLease = new Aircraft
                {
                    registration = "B-8003", type = "B737-800", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 0
                };
                gsExit.fleet.Add(finLease);
                float feeExpect = FleetEconomics.LeaseReturnCostWan(FleetEconomics.SourceFinanceLease, 12f, 0);
                float cashFin0 = gsExit.cash;
                bool okFin = fmExit.TryRetireAircraft(gsExit, "B-8003", out string msgFin);
                Debug.Log($"R22 融资租赁退租 → 成功={okFin}（期望 True） 现金 {cashFin0:F0} → {gsExit.cash:F0}（期望 −{feeExpect:F0}：120 期 × 12 万 × 30%）"
                          + $" 退出方式={finLease.ExitLabel}（期望 退租）"
                          + $"\n　{msgFin}");

                // ④ 违约金随已付期数递减到 0：10 年租期走完就是普通退租，不该再罚
                var finMature = new Aircraft
                {
                    registration = "B-8004", type = "B737-800", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 9
                };
                gsExit.fleet.Add(finMature);
                gsExit.year = 12;   // 引进年第 1 年 → 已付 132 期 > 120 期
                float feeMature = FleetManager.LeaseReturnCost(gsExit, finMature);
                float cashMature0 = gsExit.cash;
                bool okMature = fmExit.TryRetireAircraft(gsExit, "B-8004", out string msgMature);
                gsExit.year = 1;
                Debug.Log($"R22 期满退租 → 违约金 {feeMature:F0} 万（期望 0：120 期已满） 成功={okMature}（期望 True）"
                          + $" 现金变动 {gsExit.cash - cashMature0:F0} 万（期望 0）"
                          + $"　第 5 年退租时违约金 {FleetEconomics.LeaseReturnCostWan(FleetEconomics.SourceFinanceLease, 12f, FleetEconomics.LeaseMonthsPaid(1, 6)):F0} 万（期望 {60 * 12 * 0.3f:F0}）"
                          + $"\n　{msgMature}");

                // ⑤ 护栏：重复处置不得二次入账；现金不够付违约金时宁可退不成，也不许欠着退
                bool dup = fmExit.TryRetireAircraft(gsExit, "B-8001", out string msgDup);
                var finPoor = new Aircraft
                {
                    registration = "B-8005", type = "A320neo", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 0
                };
                gsExit.fleet.Add(finPoor);
                gsExit.cash = 100f;
                bool poor = fmExit.TryRetireAircraft(gsExit, "B-8005", out string msgPoor);
                Debug.Log($"R22 护栏 → 二次处置={dup}（期望 False：「{msgDup}」）"
                          + $"　欠款退租={poor}（期望 False）仍适航={finPoor.status == AircraftStatuses.Airworthy}（期望 True）"
                          + $"\n　{msgPoor}");

                // ⑥ 退租后：月租与月维护同步停计，且不再计入在册数（但记录仍留着）
                float leaseOfPoor = finPoor.monthlyLeaseWan;
                gsExit.cash = 5000f;
                bool poorOk = fmExit.TryRetireAircraft(gsExit, "B-8005", out string msgPoorOk);
                Debug.Log($"R22 停租 → 补足现金后退租={poorOk}（期望 True：违约金 {FleetEconomics.LeaseReturnCostWan(finPoor.source, leaseOfPoor, 0):F0} 万）"
                          + $"\n　月租合计 {gsExit.FleetMonthlyLease:F1} 万（期望 0：在册租赁机已清零）"
                          + $"　已退租机月维护 {FleetManager.MonthlyMaintOf(opLease):F1} 万（期望 0）"
                          + $"　在册数 {FleetManager.InServiceCount(gsExit)}（期望 0）／留档 {gsExit.fleet.Count} 架（期望 5：退出机队后仍在册备查）"
                          + $"\n　{msgPoorOk}");
            }
            catch (Exception xe)
            {
                Debug.LogWarning($"退出方式用例异常（不阻断后续）：{xe.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goExit);
            }

            // —— R23：n98 选到「不可排班的飞机」时必须给出原因与占用明细（CEO 2026-10-13 反馈）——
            // 旧实现把被过渡业务占用的机号按钮直接 interactable = false，点它毫无反馈；
            // 而且 ResolveSelection 会把选择静默切到别的飞机 —— 玩家点 B-2006，看到的却是另一架的航线。
            // 现在判据与文案收口在 TransitionBusinessLogic.ScheduleBlockReason/Detail，界面只排版。
            var goBlk = new GameObject("__n98_block_test_gs__");
            try
            {
                var gsB = goBlk.AddComponent<GameState>();
                gsB.fleet = new List<Aircraft>();
                gsB.transitionContracts = new List<TransitionContract>();
                gsB.year = 1;
                gsB.quarter = 1;

                gsB.fleet.Add(new Aircraft { registration = "B-7001", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy });
                gsB.fleet.Add(new Aircraft { registration = "B-7002", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy, age = 8 });
                gsB.fleet.Add(new Aircraft { registration = "B-7003", type = "A320neo", source = "自有", status = AircraftStatuses.Airworthy });
                var deadX = new Aircraft { registration = "B-7004", type = "A320neo", source = "自有", status = AircraftStatuses.Retired };
                deadX.disposal = FleetEconomics.DisposalRetired;
                deadX.isActive = false;
                gsB.fleet.Add(deadX);

                gsB.transitionContracts.Add(new TransitionContract
                {
                    id = "tc_charter", aircraftReg = "B-7002", kind = TransitionKinds.Charter, client = "某会展包机",
                    rateWanPerDay = 2.6f, totalDays = 5, daysUsed = 1, startUtcTicks = 0,
                    dailyCashWan = 1.56f, recallDaysLeft = -1
                });
                gsB.transitionContracts.Add(new TransitionContract
                {
                    id = "tc_wet", aircraftReg = "B-7003", kind = TransitionKinds.Wet, client = "某支线航",
                    rateWanPerDay = 1.1f, totalDays = 90, daysUsed = 30, startUtcTicks = 0,
                    dailyCashWan = 0.66f, recallDaysLeft = -1
                });

                string freeReason = TransitionBusinessLogic.ScheduleBlockReason(gsB, "B-7001");
                string missingReason = TransitionBusinessLogic.ScheduleBlockReason(gsB, "B-9999");
                string deadReason = TransitionBusinessLogic.ScheduleBlockReason(gsB, "B-7004");
                string charterReason = TransitionBusinessLogic.ScheduleBlockReason(gsB, "B-7002");
                string wetDetail = TransitionBusinessLogic.ScheduleBlockDetail(gsB, "B-7003");
                float wetFeeExpect = UnityEngine.Mathf.Round(60 * 0.66f * 0.3f * 10f) / 10f;   // 剩余 60 天 × 0.66 万/天 × 30%

                Debug.Log($"R23 可排班机 → 原因「{freeReason}」（期望空：不该给能排班的飞机挂提示）"
                          + $"\n　缺机号 → 「{missingReason}」（期望含「不在机队」）"
                          + $"\n　已退役 → 「{deadReason}」（期望含「已退役」：退出方式要跟着来源走）");

                Debug.Log($"R23 被占用机 → 包机「{charterReason}」（期望含「包机」与剩 4 天）"
                          + $"\n　★关键：这些机号按钮必须仍可点击（旧版 interactable=false → 点了毫无反馈）"
                          + $"\n　湿租明细：{wetDetail.Replace("\n", " / ")}"
                          + $"\n　召回违约金预览 {TransitionBusinessLogic.RecallFeeWan(gsB.transitionContracts[1]):F1} 万（期望 {wetFeeExpect:F1}：剩余 60 天 × 0.66 万/天 × 30%）"
                          + $"　与签约时实扣同一函数={TransitionBusinessLogic.RecallFeeWan(gsB.transitionContracts[1]) > 0f}");

                bool okR23 = string.IsNullOrEmpty(freeReason)
                             && missingReason.Contains("不在机队")
                             && deadReason.Contains("已退役")
                             && charterReason.Contains("包机")
                             && wetDetail.Contains("违约金约")
                             && wetDetail.Contains("剩余 60 天")
                             && TransitionBusinessLogic.RecallFeeWan(gsB.transitionContracts[1]) == wetFeeExpect;
                Debug.Log($"R23 结论 → {(okR23 ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception be)
            {
                Debug.LogWarning($"不可排班说明用例异常（不阻断后续）：{be.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goBlk);
            }

            // —— R24：单航线页「安排一架合适的飞机执飞」→ 运营部提名 → CEO 选定一架（2026-10-13 CEO 反馈）——
            // 背景（截图为证）：单航线页**没有**「分配执飞飞机」按钮，CEO 只能在输入框下达指令；
            // 而旧解析只认增/减/维持三种意图，一句「请安排合适的飞机执飞此航班」被判成
            // 「没读懂这条指令想增班还是减班」，拟定方案还写着「加密至 1 段往返」——
            // 可这条线的症结是**没有执飞机**（整线停飞、不计营收），跟班次多少毫无关系。
            // 本用例钉四件事：① 解析认出 assign，且分析服务不得把它改回增/减班；
            // ② 呈报期不动机号也不动班次（= 退回无痕的前提）；③ 硬门槛真在筛（航程/停场会被剔除）；
            // ④ 批准才写机号，退回则维持现状。
            var goNm = new GameObject("__assign_aircraft_test_gs__");
            try
            {
                var gsN = goNm.AddComponent<GameState>();
                gsN.activeRoutes = new List<RouteData>();
                gsN.fleet = new List<Aircraft>();
                gsN.inbox = new List<InboxMail>();
                gsN.year = 1;
                gsN.quarter = 1;

                // 停机待复航的远程线（与截图 FRA—ATL 7,409 km 同量级）：机型字段还留着 A320neo，
                // 但那只是无机航线的兜底值，实际没有机号（HasAircraft=false → 停飞不计营收）。
                var rN = new RouteData
                {
                    routeId = "R-R24", origin = "FRA", destination = "ATL", distanceKm = 7409f,
                    aircraftType = "A320neo", flightNumber = "FR106", registration = "",
                    depTime = "08:00", arrTime = "18:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 8f, dailyProfit = 8f, groundStaffAssigned = 3
                };
                rN.SyncFlightsFromPrimary();
                gsN.activeRoutes.Add(rN);
                int flightsN0 = rN.flights.Count;

                gsN.fleet.Add(new Aircraft { registration = "B-8801", type = "A320neo" });    // 6,300 km → 够不着
                gsN.fleet.Add(new Aircraft { registration = "B-8802", type = "A321LR" });     // 7,400 km → 靠 50 km 余量够
                gsN.fleet.Add(new Aircraft { registration = "B-8803", type = "A330-300" });   // 11,750 km → 够
                gsN.fleet.Add(new Aircraft { registration = "B-8804", type = "B787-9" });     // 14,140 km → 够
                gsN.fleet.Add(new Aircraft { registration = "B-8805", type = "B787-9", status = AircraftStatuses.Maintenance });

                // ① 解析：这句话要办的是**机号**，不是班次；且模型不得把它拉回「加密运力」
                var dAssign = CapacityDirectiveLogic.Parse("请安排合适的飞机执飞此航班");
                bool needConfirmBefore = CapacityDirectiveLogic.NeedsConfirm(dAssign);
                CapacityDirectiveLogic.MergeAiJson(dAssign,
                    "{\"intent\":\"increase\",\"waves\":1,\"depsCsv\":\"08:00\",\"rationale\":\"维持1班\"}");
                Debug.Log($"R24 解析 → 意图={dAssign.intent}（期望 assign） 已读懂={dAssign.intentKnown}（期望 True）"
                          + $" 需确认={needConfirmBefore}（期望 False：这不是要猜的指令）"
                          + $"\n　分析服务想改写成 increase → 意图={dAssign.intent}（期望仍 assign：关键词已明说派机，模型无权改回班次）"
                          + $"\n　规范词表含「派机」={CapacityDirectiveLogic.KeywordHint().Contains("派机")}（期望 True）");

                // ①b 同日第二次实测报障：CEO 换了个说法 →「请给此航段分配可执飞的飞机」。
                //    这句话一个词都不在枚举表上，旧版直接回「没读懂…想增班还是减班」；现在靠结构判据
                //    （飞机身份词 + 指派动词）兜住。同时钉死边界：抱怨句与排班句不得被吞进派机。
                var dCeo2 = CapacityDirectiveLogic.Parse("请给此航段分配可执飞的飞机");
                var dComplaint = CapacityDirectiveLogic.Parse("这架飞机的日利用率太低");
                var dSchedule = CapacityDirectiveLogic.Parse("换到每天12班");
                bool ceoPhraseOk = dCeo2.intent == "assign" && dCeo2.intentKnown
                                   && !CapacityDirectiveLogic.NeedsConfirm(dCeo2)
                                   && dComplaint.intent != "assign"
                                   && dSchedule.intent != "assign"
                                   && !CapacityDirectiveLogic.CanConfirmOnEnter(CapacityDirectiveLogic.Parse("请您斟酌一下"));
                Debug.Log($"R24 报障复现 → 「请给此航段分配可执飞的飞机」意图={dCeo2.intent}（期望 assign）"
                          + $" 需确认={CapacityDirectiveLogic.NeedsConfirm(dCeo2)}（期望 False）"
                          + $"\n　边界（不得误判）：抱怨句「这架飞机的日利用率太低」={dComplaint.intent}（期望非 assign）"
                          + $"　排班句「换到每天12班」={dSchedule.intent}（期望非 assign）"
                          + $"\n　未读懂方向时可否按 Enter 直接执行={CapacityDirectiveLogic.CanConfirmOnEnter(CapacityDirectiveLogic.Parse("请您斟酌一下"))}（期望 False）");

                // ② 执行：提名件进箱；班次表与机号一根汗毛不动（退回无痕的地基）
                var repN = CapacityDirectiveLogic.Execute(gsN, rN, dAssign);
                var nomMail = FindMailByAction(gsN, InboxManager.AssignAircraftAction);
                var nomCands = AircraftNominationLogic.Decode(nomMail?.payloadCsv);
                int capMails = CountMails(gsN, InboxManager.CapacityDirectiveAction);
                Debug.Log($"R24 呈报 → 成功={repN.ok}（期望 True） 标为指派={repN.assignment}（期望 True）"
                          + $" 提名件={CountMails(gsN, InboxManager.AssignAircraftAction)}（期望 1）"
                          + $" 运力呈报={capMails}（期望 0：派机令不该生成一封说班次没变的呈报）"
                          + $"\n　★关键：班次表未动 {flightsN0} → {rN.flights.Count}（期望不变）　机号={CapacityDirectiveLogic.RegsOf(rN)}（期望「未分配」）"
                          + $" 仍停飞={!rN.HasAircraft}（期望 True）"
                          + $"\n　需 CEO 决策={InboxManager.RequiresDecision(nomMail)}（期望 True）"
                          + $"\n　回执：{repN.note}");

                // ③ 硬门槛与排序：航程不够的窄体、停场检修的宽体都不得进候选
                bool candOk = nomCands.Count >= 1 && nomCands.Count <= AircraftNominationLogic.MaxCandidates;
                bool noNarrow = AircraftNominationLogic.IndexOf(nomCands, "B-8801") < 0;
                bool noMaint = AircraftNominationLogic.IndexOf(nomCands, "B-8805") < 0;
                // ★ 余量方向：7,400 km 的 A321LR 必须够得着 7,409 km（余量加在机型侧）。
                // 旧写法（距离 + 50 = 需 7,459）会把它剔掉，而这里打印出来的期望却一直写着「能入候选」——
                // 一条只打印不断言的期望等于没有护栏，故补成硬断言。
                bool a321In = AircraftNominationLogic.IndexOf(nomCands, "B-8802") >= 0;
                bool bodyHasGate = nomMail != null && !string.IsNullOrEmpty(nomMail.body)
                    && nomMail.body.Contains("筛选口径") && nomMail.body.Contains("⑦");
                bool sorted = true;
                for (int i = 1; i < nomCands.Count; i++)
                    if (nomCands[i - 1].profitWan < nomCands[i].profitWan) sorted = false;
                string rejAll = string.Join("　|　", AircraftNominationLogic.Rejections(gsN, rN, 6).ToArray());
                Debug.Log($"R24 候选 → 数量={nomCands.Count}（期望 1~{AircraftNominationLogic.MaxCandidates}）"
                          + $" 投影日利降序={sorted}（期望 True）"
                          + $"\n　航程不足的 B-8801 未入候选={noNarrow}（期望 True）　停场的 B-8805 未入候选={noMaint}（期望 True）"
                          + $"\n　★关键：A321LR（7,400 km）靠 {OpsScheduleLogic.RangeToleranceKm:F0} km 减载余量入候选={a321In}"
                          + $"（期望 True：余量加在机型侧，本线 7,409 km）；6,300 km 的 A320neo 未入候选={noNarrow}"
                          + $"\n　邮件正文含七条硬门槛={bodyHasGate}（期望 True）"
                          + $"\n　候选明细：" + string.Join("、", nomCands.ConvertAll(c => $"{c.reg}/{c.type}/{c.seats}座/{c.profitWan:F2}万").ToArray())
                          + $"\n　逐机落选归因：{rejAll}");

                // ④ 批准 = 写机号、按机型重核收益、复航；且候选首位（收益最优）就是「批准」按钮的口径
                string pickReg = nomCands.Count > 0 ? nomCands[0].reg : "";
                string pickType = nomCands.Count > 0 ? nomCands[0].type : "";
                bool okPick = InboxManager.Approve(gsN, nomMail, out string msgPick);
                bool allCarried = true;
                for (int i = 0; i < rN.flights.Count; i++)
                    if (rN.flights[i] == null
                        || !string.Equals(rN.flights[i].registration, pickReg, StringComparison.OrdinalIgnoreCase))
                        allCarried = false;
                // 回执走 InboxCadence.ReplyReady —— 「运营部答复」是延迟投递的，当场查收件箱必然是 0。
                // 旧版这里打印「期望 1」却从不校验（与航程那条同病），现改为查投递队列并纳入断言。
                int pendingReceipt = CountPending(gsN, "执飞机号已生效");
                // 协调端绑不上槽时必须当场说清（否则留下「有机、缺槽」的半截状态，§15.4 的旧账同源）
                int slotEndsN = RoutePermitLogic.SlotNeeds(rN).Count;
                bool slotGapTold = slotEndsN == 0 || msgPick.Contains("未能绑定");
                Debug.Log($"R24 批准 → 成功={okPick}（期望 True） 选定={pickReg}（期望 {pickType}　＝候选首位/收益最优）"
                          + $"\n　★关键：机号已写入={rN.registration}（期望 {pickReg}） 机型={rN.aircraftType}（期望 {pickType}）"
                          + $" 全部班次同机号={allCarried}（期望 True） 已复航={rN.HasAircraft}（期望 True）"
                          + $"\n　邮件状态={nomMail?.status}（期望 approved）"
                          + $"\n　★关键：批准回执已排入投递队列={pendingReceipt}（期望 1：回执是「延迟投递」，不是当场入箱）"
                          + $"　协调端={slotEndsN} 个、未能绑槽已在回执里说清={slotGapTold}（期望 True）"
                          + $"\n　批复：{msgPick}");

                // ⑤ 改主意：再下一句派机令 → 提名里不再含现机；退回则维持现状（在飞航线毫发无伤）
                int flightsN1 = rN.flights.Count;
                string regN1 = rN.registration;
                var repN2 = CapacityDirectiveLogic.Execute(gsN, rN, CapacityDirectiveLogic.Parse("换一架飞机执飞本线"));
                var mail2 = FindMailByAction(gsN, InboxManager.AssignAircraftAction);
                var cands2 = AircraftNominationLogic.Decode(mail2?.payloadCsv);
                bool excludesCurrent = AircraftNominationLogic.IndexOf(cands2, regN1) < 0;
                // 二次指令另开一封（上一封已结案），且同一航线同时只有一封未结案
                int openNom = 0;
                for (int i = 0; i < gsN.inbox.Count; i++)
                    if (gsN.inbox[i] != null && gsN.inbox[i].action == InboxManager.AssignAircraftAction
                        && InboxManager.IsOpen(gsN.inbox[i])) openNom++;
                InboxManager.RejectAssignAircraft(gsN, mail2, out string msgRej);
                Debug.Log($"R24 改派/退回 → 二次呈报={repN2.ok}（期望 True） 候选={cands2.Count}（期望 ≥1）"
                          + $" 不含现机 {regN1}={excludesCurrent}（期望 True）"
                          + $" 同航线未结案件数={openNom}（期望 1：同一件事不叠两封待批）"
                          + $"\n　★关键：退回后机号仍={rN.registration}（期望 {regN1}） 班次 {flightsN1} → {rN.flights.Count}（期望不变）"
                          + $" 邮件状态={mail2?.status}（期望 rejected）"
                          + $"\n　退回说明：{msgRej}");

                // ⑥ 无机可派：不发一封「挑不出东西」的决策邮件，而是发汇报件（action 空 = 无需批复）
                var rFar = new RouteData
                {
                    routeId = "R-R24F", origin = "FRA", destination = "SCL", distanceKm = 17000f,
                    aircraftType = "B787-9", flightNumber = "FR900", registration = "",
                    depTime = "08:00", arrTime = "18:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 8f, dailyProfit = 8f, groundStaffAssigned = 3
                };
                rFar.SyncFlightsFromPrimary();
                gsN.activeRoutes.Add(rFar);
                var repFar = CapacityDirectiveLogic.Execute(gsN, rFar, CapacityDirectiveLogic.Parse("请安排合适的飞机执飞此航班"));
                var blockedMail = FindMailBySubject(gsN, "机队无合适运力");
                string blockedHead = blockedMail != null && !string.IsNullOrEmpty(blockedMail.body)
                    ? blockedMail.body.Split('\n')[0]
                    : "（无汇报件）";
                Debug.Log($"R24 无机可派 → 执行成功={repFar.ok}（期望 True：已发汇报件）"
                          + $" 汇报件={CountMails(gsN, "", "机队无合适运力")}（期望 1）"
                          + $" 需 CEO 决策={InboxManager.RequiresDecision(blockedMail)}（期望 False：无需批复）"
                          + $" 标为指派={repFar.assignment}（期望 True）"
                          + $"\n　★关键：远线仍无机={!rFar.HasAircraft}（期望 True）"
                          + $"\n　回执：{repFar.note}"
                          + $"\n　汇报件首行：{blockedHead}");

                bool okR24 = dAssign.intent == "assign" && dAssign.intentKnown && !needConfirmBefore
                             && ceoPhraseOk
                             && repN.ok && repN.assignment && capMails == 0
                             && candOk && noNarrow && noMaint && sorted && a321In && bodyHasGate
                             && okPick && allCarried && rN.registration == pickReg
                             && pendingReceipt == 1 && slotGapTold
                             && excludesCurrent && openNom == 1
                             && rN.registration == regN1 && mail2 != null && mail2.status == "rejected"
                             && repFar.ok && blockedMail != null && !InboxManager.RequiresDecision(blockedMail);
                Debug.Log($"R24 结论 → {(okR24 ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception ne)
            {
                Debug.LogWarning($"执飞飞机提名用例异常（不阻断后续）：{ne.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goNm);
            }

            // —— R25：远程线「可指飞」死区（2026-10-13 第二封报障：FRA–ATL 7,409 km）——
            // 报障原文：宽体唯一候选 B-2007 被「值勤超限：07:00 报到 → 次日 02:28 落地 共 19.5h > 上限 15h」
            // 否掉，运营部因而发函「机队里没有能飞本线的飞机」，还劝 CEO「采购/租赁满足航程的机型」。
            // 根因：值勤预算按「当天往返」合并算，而外站换组口径原先只在「单段 > 9h」启用 ——
            // 7,409 km 折算 8.72h 差 0.28h 够不着门槛 → 5,500–7,650 km 整段死区：
            // 宽体航程够、值勤必然超限；窄体航程又不够 → 一台也派不进去。
            // 本用例钉三件事：① 该带现在派得出飞机（判据＝当天往返装不下就走外站换组）；
            // ② 真·超远程（连单段都超值勤）仍如实拒绝；③ 无机可派的汇报件按**真实拦因**归因，
            //    不许再把值勤问题写成航程问题（照那封函办＝白买飞机）。
            var goLh = new GameObject("__longhaul_nomination_test_gs__");
            try
            {
                var gsL = goLh.AddComponent<GameState>();
                gsL.activeRoutes = new List<RouteData>();
                gsL.fleet = new List<Aircraft>();
                gsL.inbox = new List<InboxMail>();
                gsL.year = 1;
                gsL.quarter = 1;

                // 与截图同量级的远程线：08:00 起飞是真班期，到达时刻留空 → 轮挡取物理真值（8.72h）
                var rAtl = new RouteData
                {
                    routeId = "R-R25", origin = "FRA", destination = "ATL", distanceKm = 7409f,
                    aircraftType = "A320neo", flightNumber = "FR106", registration = "",
                    depTime = "08:00", arrTime = "", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 8f, dailyProfit = 8f, groundStaffAssigned = 3
                };
                rAtl.SyncFlightsFromPrimary();
                gsL.activeRoutes.Add(rAtl);

                gsL.fleet.Add(new Aircraft { registration = "B-9001", type = "A320neo" });  // 6,300 km → 航程不够
                gsL.fleet.Add(new Aircraft { registration = "B-9002", type = "A321LR" });   // 7,400 km → 够
                gsL.fleet.Add(new Aircraft { registration = "B-9003", type = "A330-300" }); // 11,750 km → 够
                gsL.fleet.Add(new Aircraft { registration = "B-9004", type = "B787-9" });   // 14,140 km → 够

                int blockMin = OpsRotationLogic.BlockOf(rAtl);
                int turnMin = OpsRotationLogic.TurnOf(rAtl);
                int reportAt = 8 * 60 - OpsRotationLogic.ReportBeforeMinutes;
                var mergedBudget = OpsRotationLogic.BudgetOf("A330-300", reportAt, blockMin, 2);
                int mergedLen = blockMin * 2 + turnMin + OpsRotationLogic.ReportBeforeMinutes;
                bool belowMagic9h = blockMin < OpsRotationLogic.LongHaulLegMinutes;

                var candsLh = AircraftNominationLogic.Nominate(gsL, rAtl);
                bool widebodyIn = AircraftNominationLogic.IndexOf(candsLh, "B-9003") >= 0;
                Debug.Log($"R25 远程线可指飞 → 本线 {rAtl.distanceKm:F0} km　单程 {blockMin / 60f:0.##}h"
                          + $"（< 9h 魔数 {belowMagic9h}（期望 True：旧版正卡在这条线上））"
                          + $"\n　当天往返合并值勤 {(mergedLen) / 60f:0.#}h > 上限 {mergedBudget.limit / 60f:0.##}h={mergedLen > mergedBudget.limit}"
                          + $"（期望 True → 必须走外站换组，不许靠放宽上限过关）"
                          + $"\n　★关键：现在派得出飞机 候选={candsLh.Count}（期望 ≥1）　宽体 B-9003 入围={widebodyIn}（期望 True）"
                          + $"\n　候选：" + string.Join("、", candsLh.ConvertAll(c => $"{c.reg}/{c.type}/{c.profitWan:F2}万").ToArray()));

                // ② 把三架够航程的飞机都用「一段很早的短班」占住（只压值勤、不压时段），
                //    复现截图那封「机队里没有能飞本线的飞机」：此时拦因是值勤，不是航程。
                var rEarly = new RouteData
                {
                    routeId = "R-R25E", origin = "FRA", destination = "MAD", distanceKm = 1500f,
                    aircraftType = "A321LR", flightNumber = "FR105", registration = "B-9002",
                    depTime = "03:00", arrTime = "", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 4f, dailyProfit = 4f, groundStaffAssigned = 3
                };
                rEarly.SyncFlightsFromPrimary();
                rEarly.flights[0].registration = "B-9002";
                rEarly.flights.Add(new RouteFlight { registration = "B-9003", depTime = "03:00",
                                                     daysOfWeek = "每日", weeklyFlights = 7 });
                rEarly.flights.Add(new RouteFlight { registration = "B-9004", depTime = "03:00",
                                                     daysOfWeek = "每日", weeklyFlights = 7 });
                gsL.activeRoutes.Add(rEarly);

                var blockedLh = AircraftNominationLogic.Nominate(gsL, rAtl);
                var detailsLh = AircraftNominationLogic.RejectionDetails(gsL, rAtl, 8);
                string blockedBodyLh = AircraftNominationLogic.FormatBlockedBody(gsL, rAtl, detailsLh);

                // 归因要看的是「本线卡在哪一条」——航程不够的飞机本来就没资格谈别的，
                // 把它们算进结论正是旧版的病（把值勤问题写成航程问题）。
                int rangeCount = 0, capableRej = 0, dutyCount = 0, sectorCapCount = 0;
                bool dutyReasonTold = false;
                for (int i = 0; i < detailsLh.Count; i++)
                {
                    var r = detailsLh[i];
                    if (r.kind == OpsRotationLogic.BlockKind.Range) { rangeCount++; continue; }
                    capableRej++;
                    if (r.kind == OpsRotationLogic.BlockKind.Duty)
                    {
                        dutyCount++;
                        if (r.why.Contains("不能改按外站换组")) dutyReasonTold = true;
                    }
                    if (r.kind == OpsRotationLogic.BlockKind.SectorCap) sectorCapCount++;
                }
                bool noBuyAdvice = !blockedBodyLh.Contains("采购/租赁满足航程的机型");
                bool rangeSlackTold = blockedBodyLh.Contains("航程富余");
                bool verdictNotRange = blockedBodyLh.Contains("真正拦因") && !blockedBodyLh.Contains("真正拦因＝**航程**");
                bool noImpossibleSeats = !blockedBodyLh.Contains("座级 ≥ 26") && blockedBodyLh.Contains("缺班次");
                Debug.Log($"R25 无机可派归因 → 候选={blockedLh.Count}（期望 0）"
                          + $"　航程不够 {rangeCount} 架（期望 1）　够航程却被别的门槛挡下 {capableRej} 架（期望 3）"
                          + $"\n　★关键：机队最长航程够本线，汇报件如实写「航程富余」={rangeSlackTold}（期望 True）"
                          + $"\n　★关键：拦因落到「值勤超限 / 增援航段上限」而不是航程 {verdictNotRange}（期望 True）"
                          + $"　值勤超限 {dutyCount} 架（宽体当日已排班会先撞增援 3 航段上限 {sectorCapCount} 架）"
                          + $"\n　★关键：当日已排班的飞机不得改按外站换组，理由要说清={dutyReasonTold}（期望 True）"
                          + $"\n　★关键：不再劝「采购/租赁满足航程的机型」={noBuyAdvice}（期望 True：照旧版办＝白买飞机）"
                          + $"\n　需求句不再印「座级 ≥ 26088 座」这类不存在的飞机、改为说缺班次={noImpossibleSeats}（期望 True）"
                          + $"\n　汇报件：\n{blockedBodyLh}");

                bool okR25 = belowMagic9h && mergedLen > mergedBudget.limit
                             && candsLh.Count >= 1 && widebodyIn
                             && blockedLh.Count == 0 && rangeCount == 1 && capableRej == 3
                             && dutyCount >= 1 && dutyReasonTold
                             && rangeSlackTold && verdictNotRange && noBuyAdvice && noImpossibleSeats;
                Debug.Log($"R25 结论 → {(okR25 ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception le)
            {
                Debug.LogWarning($"远程线可指飞用例异常（不阻断后续）：{le.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goLh);
            }

            // —— R26：排班咨询答复（2026-10-13 第三次报障：一句「为什么」被当成调令办了）——
            // 报障原文（单航线页指令框）：「为何FR106/107每周三，周五，周日不安排执飞？」
            // 他只是在问，运营部却**真的去提名机号**了 —— 句里有「安排」（指派动词表里就有），
            // 又有「FR106」（字母 2 位 + 数字 3 位，与机型代号 A330 同形）。
            // 与 slotcheck R26（纯函数：判据与话题）分工，本用例钉**带状态**的四件事：
            // ① 问句一律不办（班次 / 机号 / 日利零改动，也不许回一句「没读懂」）；
            // ② 转成一封**汇报件**（action 留空 → 无决策按钮 → 不占待办、不挡季度推进）；
            // ③ 同一问题重复提问只刷新不叠函，换个问题另开一封；
            // ④ 收件箱「提出修改」框里写问句时，原呈报原封不动地留在待批。
            var goAsk = new GameObject("__ops_query_test_gs__");
            try
            {
                var gsQ = goAsk.AddComponent<GameState>();
                gsQ.activeRoutes = new List<RouteData>();
                gsQ.fleet = new List<Aircraft>();
                gsQ.inbox = new List<InboxMail>();
                gsQ.year = 1;
                gsQ.quarter = 1;

                // 与截图同形的航线：FRA–ATL 只排周一/周二 —— 周三、周五、周日正是空着的
                var rQ = new RouteData
                {
                    routeId = "R-R26", origin = "FRA", destination = "ATL", distanceKm = 7409f,
                    aircraftType = "A330-300", flightNumber = "FR106", registration = "B-9003",
                    depTime = "08:00", arrTime = "", daysOfWeek = "周一/周二", weeklyFlights = 2,
                    baseDailyProfit = 8f, dailyProfit = 8f, groundStaffAssigned = 3
                };
                rQ.SyncFlightsFromPrimary();
                gsQ.activeRoutes.Add(rQ);
                gsQ.fleet.Add(new Aircraft { registration = "B-9003", type = "A330-300" });

                string rawQ = "为何FR106/107每周三，周五，周日不安排执飞？";
                int flights0Q = rQ.flights.Count;
                int weekly0Q = rQ.WeeklyFlightsDeparture();
                float profit0Q = rQ.dailyProfit;
                string regs0Q = RegistrationList(rQ);

                // —— ① 提问 → 只发一封答复件，本线台账零改动 ——
                bool q1 = InboxManager.QueueOpsQueryAnswer(gsQ, rQ, rawQ, out string msg1);
                InboxMail ans1 = q1 && gsQ.inbox.Count == 1 ? gsQ.inbox[0] : null;
                bool isAnswer = ans1 != null && ans1.id != null && ans1.id.StartsWith("m_ops_query_")
                                && ans1.role == "排班咨询答复" && ans1.tag == "答复";
                bool noButton = ans1 != null && string.IsNullOrEmpty(ans1.action)
                                && !InboxManager.RequiresDecision(ans1) && ans1.status == "approved";
                bool bodyLedger = ans1 != null && ans1.body.Contains(rawQ)
                                  && ans1.body.Contains("有航班的星期") && ans1.body.Contains("没有航班的星期")
                                  && ans1.body.Contains($"每周 {weekly0Q} 班");
                // 反面：视图上「周三/周五/周日」看着像有航班（跨夜尾段落在次日凌晨），答复必须主动说破
                bool bodyAnswersOffDays = ans1 != null && ans1.body.Contains("周三")
                                          && ans1.body.Contains("周五") && ans1.body.Contains("周日");
                bool untouched = rQ.flights.Count == flights0Q && rQ.WeeklyFlightsDeparture() == weekly0Q
                                 && Math.Abs(rQ.dailyProfit - profit0Q) < 0.001f
                                 && RegistrationList(rQ) == regs0Q;
                Debug.Log($"R26 提问 → 投递答复={q1}（期望 True） 收件箱={gsQ.inbox.Count}（期望 1）"
                          + $"　答复件={isAnswer}（期望 True：role=排班咨询答复 / tag=答复）"
                          + $"\n　★关键：汇报件无决策按钮={noButton}（期望 True：问句不该多出一封待办）"
                          + $"\n　★关键：台账零改动（班次 {flights0Q} / 每周 {weekly0Q} 班 / 日利 {profit0Q:F1} 万 / 机号未变）={untouched}（期望 True）"
                          + $"\n　答复引用了本线台账={bodyLedger}（期望 True）　正面回答「哪几天没有」={bodyAnswersOffDays}（期望 True）"
                          + $"\n　回执：「{msg1}」"
                          + $"\n　答复件：\n{ans1?.body}");

                // —— ② 重复提问只刷新、不叠函；换个问题才另开一封 ——
                bool q2 = InboxManager.QueueOpsQueryAnswer(gsQ, rQ, rawQ, out string msg2);
                bool deduped = q2 && gsQ.inbox.Count == 1;
                // 另一句纯疑问（问法不同 → 另开一封；同一句话才复用）
                bool q4 = InboxManager.QueueOpsQueryAnswer(gsQ, rQ, "这条线每周飞几班？", out string msg4);
                bool separate = q4 && gsQ.inbox.Count == 2;
                // 带方向词的问句**不是**咨询（「还能不能加密？」是在请办事 → 走调令），
                // 判据须在调用方（两处接线都靠它分流），故这里只钉判据本身。
                bool q3 = CapacityDirectiveLogic.LooksLikeQuestion("为什么这条线只有 1 班，还能不能加密？");
                Debug.Log($"R26 重复提问 → 刷新不叠函={deduped}（期望 True：同一航线同一问题只有一封）"
                          + $"　换问一句 → 另开一封={separate}（期望 True：收件箱={gsQ.inbox.Count}）"
                          + $"\n　带方向词的问句不算咨询={!q3}（期望 True：「还能不能加密」走调令）"
                          + $"\n　刷新回执：「{msg2}」　另一问：「{msg4}」");

                // —— ③ 兜底闸门：调用点若只写 Parse → Execute，问句同样必须零改动 ——
                var dQ = CapacityDirectiveLogic.Parse(rawQ);
                var repQ = CapacityDirectiveLogic.Execute(gsQ, rQ, dQ);
                bool execBlocked = dQ.query && !repQ.ok && repQ.note.Contains("询问")
                                   && rQ.flights.Count == flights0Q && RegistrationList(rQ) == regs0Q;
                Debug.Log($"R26 Execute 兜底 → 被拦下={execBlocked}（期望 True）　note=「{repQ.note}」"
                          + $"\n　（改动：班次 {flights0Q} → {rQ.flights.Count}，机号 {regs0Q} → {RegistrationList(rQ)}）");

                // —— ④ 收件箱「提出修改」框里写问句：答复另发，原呈报原封不动 ——
                var capMail = new InboxMail
                {
                    id = "m_test_r26_cap", sender = "运营部", role = "运力呈报", glyph = "运",
                    kind = "decision", action = InboxManager.CapacityDirectiveAction,
                    origin = rQ.routeId, dest = rQ.destination,
                    subject = "关于 FRA—ATL 运力调整与航班环的报告（改稿 1，请批复）",
                    body = "（原始呈报正文：加密到每天 3 班）", tag = "呈报", status = "pending",
                    directiveNote = "加密到每天 3 班", receivedYear = gsQ.year, receivedQuarter = gsQ.quarter,
                    time = gsQ.DateLabel
                };
                InboxManager.Add(gsQ, capMail);
                int inboxBeforeRev = gsQ.inbox.Count;
                string capBody0 = capMail.body;
                bool rev = InboxManager.ReviseCapacityDirective(gsQ, capMail, "这条线为什么周三不安排执飞？",
                                                               null, out string revMsg);
                bool revUntouched = !rev && capMail.status == "pending" && capMail.body == capBody0
                                    && revMsg.Contains("询问") && gsQ.inbox.Count == inboxBeforeRev + 1;
                Debug.Log($"R26 改稿框写问句 → 原呈报未被改掉={revUntouched}（期望 True）"
                          + $"\n　（未受理={!rev}　状态仍={capMail.status}（期望 pending）　正文未变={capMail.body == capBody0}"
                          + $"　另发答复件={gsQ.inbox.Count - inboxBeforeRev}（期望 1））"
                          + $"\n　回执：「{revMsg}」");

                // —— ⑤ 抬头不得把三字码印两遍（2026-10-13 第四次报障）——
                // AirportCatalog.Label 本身已是「城市 三字码」，身后再拼一次裸码，
                // 渲染时 TermNavMap 会把那个多出来的裸码当成新机场词再包一遍链接 →
                // 屏幕上就是「法兰克福 FRA 法兰克福 FRA ⇄ 亚特兰大 ATL 亚特兰大 ATL」。
                string bodyQ = ans1 != null ? ans1.body : "";
                bool headOnce = bodyQ.Contains("法兰克福 FRA ⇄ 亚特兰大 ATL");
                bool noDupRaw = bodyQ.IndexOf("FRA FRA", StringComparison.Ordinal) < 0
                                && bodyQ.IndexOf("ATL ATL", StringComparison.Ordinal) < 0;
                // 深链那一串键要从 **CEO 真正看到的渲染文本** 里取：UI 的显示链路是
                // 「正文 → InboxLetter.FrameIfNeeded（补称谓落款）→ TermNavMap.Decorate（包链接）」，
                // 航线链接其实出自信笺抬头复述的标题「FRA—ATL」。
                string framedQ = InboxLetter.FrameIfNeeded(gsQ, ans1, bodyQ);
                var paintedQ = TermNavMap.Decorate(framedQ);
                // 渲染后逐字看抬头那一行：把富文本标签剥掉，「法兰克福 FRA」应当只出现一次。
                // （不能数整封信——正文别处也会印这两个机场名，那是正常的引用。）
                string[] paintedLinesQ = StripTags(paintedQ).Split('\n');
                string headPaintedQ = "";
                for (int i = 0; i < paintedLinesQ.Length; i++)
                {
                    // 抬头行（装封后不是第一行 —— 信笺称谓与自我介绍在前面）
                    if (paintedLinesQ[i].IndexOf("运营部答复", StringComparison.Ordinal) >= 0)
                    {
                        headPaintedQ = paintedLinesQ[i];
                        break;
                    }
                }
                bool noDupPainted = headPaintedQ.Contains("法兰克福 FRA ⇄ 亚特兰大 ATL")
                                    && CountOf(headPaintedQ, "法兰克福 FRA") == 1
                                    && CountOf(headPaintedQ, "亚特兰大 ATL") == 1;
                Debug.Log($"R26 答复抬头 → 只有一份「城市 三字码」={headOnce}（期望 True）"
                          + $"\n　★关键：原文无重复三字码={noDupRaw}　渲染后抬头每个机场各出现一次={noDupPainted}（期望均 True）"
                          + $"\n　渲染抬头：「{headPaintedQ}」");

                // —— ⑥ 答复件里的航线链接必须指回本线（CEO：「点 FRA—ATL 跳到了 FRA—LHR」）——
                // 正文里的航线链接由 TermNavMap 从文案里的「FRA—ATL」现包，手上只有两个三字码；
                // 而 routeId 是「出发-到达-序号」。旧版只做精确比对、失败就悄悄给第一条 ——
                // 故这里在列表**前面**插一条 FRA—LHR 当诱饵：解析出来必须是本线，不是列表第一条。
                string linkKey = FirstRouteLinkKey(paintedQ);
                var decoyQ = new RouteData
                {
                    routeId = "FRA-LHR-1", origin = "FRA", destination = "LHR", distanceKm = 650f,
                    aircraftType = "A320neo", flightNumber = "FR100", registration = "B-2001",
                    depTime = "08:00", arrTime = "", daysOfWeek = "每日", weeklyFlights = 7
                };
                decoyQ.SyncFlightsFromPrimary();
                var probeQ = new List<RouteData> { decoyQ, rQ };
                var landed = RouteData.FindByKey(probeQ, linkKey);
                bool deepLink = linkKey != null && landed == rQ;
                bool mailCarriesRoute = ans1 != null && ans1.origin == rQ.routeId && ans1.dest == rQ.destination;
                Debug.Log($"R26 答复里的航线链接 → 键=「{linkKey ?? "(未包出航线链接)"}」"
                          + $"\n　★关键：点它落在本线={deepLink}（期望 True：解析到 {landed?.routeId ?? "null"}"
                          + $"，诱饵第一条是 {decoyQ.routeId}——旧版正是错在退而给它）"
                          + $"\n　答复件记住本线（origin=routeId / dest=三字码）={mailCarriesRoute}（期望 True）");

                bool okR26 = q1 && isAnswer && noButton && bodyLedger && bodyAnswersOffDays && untouched
                             && deduped && separate && !q3 && execBlocked && revUntouched
                             && headOnce && noDupRaw && noDupPainted && deepLink && mailCarriesRoute;
                Debug.Log($"R26 结论 → {(okR26 ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception qe)
            {
                Debug.LogWarning($"排班咨询答复用例异常（不阻断后续）：{qe.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goAsk);
            }

            // ———— R28 「照目标下达」的班次调令：不得读成派机令，且办成/办不成都必须回一封 ————
            // 报障原话：「发布增加每日一段往返的指令，收件箱没有收到市场部反馈邮件」，
            // 当时输入框里写的是「把FR106/FR107调整为每日一段往返。」。
            // 旧版把它读成**派机令**：`FR106` 与 A330 同形被当成机型代号，与「调整」里的「调」
            // 凑成「身份词 + 指派动词」→ 运营部去提名机号，班次一点没动，收件箱里也没有那封
            // 带「市场部」联动行的运力调整呈报。
            //
            // ★2026-10-14 第十次问询期间把这条用例修对（它此前一直红着，等于 CEO 要的那封没人钉）：
            //   ① 呈报件与 UI（`RouteDetailPanel`）走**同一条路**：`Execute` 只办班次，
            //      成功后的呈报由调用方 `QueueCapacityReport` 投递（旧版只调 `Execute` 就断言收件箱 +1）；
            //   ② 失败例不能用「加密到每天四段往返」—— 那四条波次里第一条排得出，属**部分办成**
            //      （缺口写进呈报的「运力缺口」段），判据把它当失败就恒红。现在失败例改成
            //      「唯一一架飞机进检修」，另用一段把「部分办成要如实报缺口」钉住；
            //   ③ 失败会把本线回退到**上一个已批准**的基线 → 被回退掉的旧待批呈报必须一并作废
            //      （否则收件箱里留着一封数字对不上台账的待批件）。
            var goCap = new GameObject("__r28_stated_target_test_gs__");
            try
            {
                var gsC = goCap.AddComponent<GameState>();
                gsC.onTimePercent = 88f;
                gsC.activeRoutes = new List<RouteData>();
                gsC.fleet = new List<Aircraft>();
                gsC.inbox = new List<InboxMail>();
                gsC.flightRings = new List<FlightRing>();
                gsC.year = 1;
                gsC.quarter = 1;

                // 与截图同形：FRA–ATL 7,409 km 跨夜长途，每周 4 班（周一/三/五/日），单机 1 段往返/日
                var rT = new RouteData
                {
                    routeId = "FRA-ATL-2", origin = "FRA", destination = "ATL", distanceKm = 7409f,
                    aircraftType = "A330-300", flightNumber = "FR106", registration = "B-2007",
                    depTime = "08:00", arrTime = "02:16", daysOfWeek = "周一/周三/周五/周日", weeklyFlights = 4,
                    baseDailyProfit = 6.12f, dailyProfit = 6.12f, groundStaffAssigned = 3
                };
                rT.SyncFlightsFromPrimary();
                gsC.activeRoutes.Add(rT);
                gsC.fleet.Add(new Aircraft { registration = "B-2007", type = "A330-300", isActive = true, status = AircraftStatuses.Airworthy, age = 3 });

                string rawT = "把FR106/FR107调整为每日一段往返。";
                var dT = CapacityDirectiveLogic.Parse(rawT);
                bool notAssign = dT.intent != "assign" && !dT.intentKnown;
                bool noAircraftWord = !CapacityDirectiveLogic.MentionsAircraft(rawT);   // 航班号不再当机型代号
                bool wavesRead = dT.waves == 1 && dT.days == "每日";                    // 「一段往返」用中文写的量也读得出来
                Debug.Log($"R28 解析「{rawT}」→ 意图={dT.intent}{(dT.intentKnown ? "" : "(未明)")} 班次={dT.waves}（期望 1） 班期={dT.days}（期望 每日）"
                          + $"\n　★关键：不再读成派机令={notAssign}（期望 True，旧版是 assign）"
                          + $"\n　★关键：航班号 FR106 不再算飞机身份词={noAircraftWord}（期望 True）　中文数词读量={wavesRead}（期望 True）");

                // 只说目标、不说增减：方向由现状（1 段/日）与目标（1 段/日）比出，不必请 CEO 重写
                CapacityDirectiveLogic.ResolveStatedTarget(rT, dT);
                bool targetResolved = dT.intentKnown && dT.statedTarget && dT.intent == "increase"
                                      && !CapacityDirectiveLogic.NeedsConfirm(dT);
                Debug.Log($"R28 定方向 → 意图={dT.intent}（期望 increase） 按目标设定={dT.statedTarget}（期望 True）"
                          + $" 需确认={CapacityDirectiveLogic.NeedsConfirm(dT)}（期望 False）"
                          + $"\n　解析：{dT.analysis}");

                int weekly0T = rT.WeeklyFlightsDeparture();
                string daysBaseT = rT.daysOfWeek;                     // 上一个「已批准」的基线（本用例从未批准过任何呈报）
                int inbox0T = gsC.inbox.Count;
                var repT = CapacityDirectiveLogic.Execute(gsC, rT, dT);
                // 呈报件与 UI（`RouteDetailPanel.DirectiveRoutine`）**同一条路径**：`Execute` 只办班次，
                // 成功后的呈报件由调用方投递（失败件例外 —— `Execute` 内部直接投汇报件）。
                // 旧版这条用例只调 `Execute` 就断言「收件箱新增 1 封」，于是它恒红：判据测错了层，
                // 而 CEO 报障要的那封「市场部反馈邮件」其实没人钉住。
                bool reportQueued = repT.ok && !repT.assignment
                                    && InboxManager.QueueCapacityReport(gsC, rT, repT);
                bool executed = repT.ok && !repT.assignment && rT.daysOfWeek == "每日"
                                && rT.WeeklyFlightsDeparture() > weekly0T;
                Debug.Log($"R28 执行 → ok={repT.ok}（期望 True） 班期 {rT.daysOfWeek}（期望 每日）"
                          + $"　每周 {weekly0T} → {rT.WeeklyFlightsDeparture()} 班（期望变大）"
                          + $"\n　呈报已投递={reportQueued}（期望 True）"
                          + $"\n　（班次 {rT.FlightCount} 段/日，机号 {RegistrationList(rT)}）");

                InboxMail repMail = null;
                int nomMail = 0, failedMail = 0;
                for (int i = 0; i < gsC.inbox.Count; i++)
                {
                    var m = gsC.inbox[i];
                    if (m == null) continue;
                    if (m.action == InboxManager.CapacityDirectiveAction) repMail = m;
                    if (m.action == InboxManager.AssignFlightAction
                        || (m.role != null && (m.role.Contains("提名") || m.role.Contains("核对")))) nomMail++;
                    if (m.role == CapacityDirectiveLogic.FailureRole) failedMail++;
                }
                bool gotReport = repMail != null && repMail.status == "pending"
                                 && repMail.origin == rT.routeId
                                 && CapacityDirectiveLogic.Parse(repMail.directiveNote).intent != "assign";
                bool reportHasMarket = repMail != null && repMail.body.Contains("市场部");
                Debug.Log($"R28 收件箱 → 新增 {gsC.inbox.Count - inbox0T} 封（期望 1）"
                          + $"\n　★关键：收到运力调整呈报（待批复）= {gotReport}（期望 True：action={InboxManager.CapacityDirectiveAction}）"
                          + $"\n　★关键：呈报正文含「市场部」联动行={reportHasMarket}（期望 True —— 报障要的正是这封）"
                          + $"\n　误判成派机令的邮件数={nomMail}（期望 0）　未执行汇报件={failedMail}（期望 0）"
                          + $"\n　主题：{repMail?.subject}");

                // —— 办不成时也必须回一封（页面提示承诺的是「结果以邮件呈报」）——
                // 造出**真的办不成**：把唯一一架飞机放进检修 → `ApplyWaves` 的波次与「单班草案」都挑不到机
                // → 硬失败 → 必须投一封汇报件（无决策按钮，不占待办）。
                // ★旧版这里用「加密到每天四段往返」当失败例：四条波次里**第一条排得出**，属**部分办成**
                //   （缺口写进呈报的「运力缺口」段，CEO 要的那封呈报照发）—— 判据把它当失败，于是恒红；
                //   真的失败路径反而没人钉。现在改按上面的造法，并把「部分办成」的如实呈报钉在下面。
                int flights1T = rT.FlightCount;
                gsC.fleet[0].status = AircraftStatuses.Maintenance;
                var dBig = CapacityDirectiveLogic.Parse("把FR106/FR107加密到每天四段往返");
                CapacityDirectiveLogic.ResolveStatedTarget(rT, dBig);
                var repBig = CapacityDirectiveLogic.Execute(gsC, rT, dBig);
                gsC.fleet[0].status = AircraftStatuses.Airworthy;
                InboxMail failMail = null;
                for (int i = 0; i < gsC.inbox.Count; i++)
                {
                    var m = gsC.inbox[i];
                    if (m != null && m.role == CapacityDirectiveLogic.FailureRole) failMail = m;
                }
                bool failQueued = !repBig.ok && repBig.failureQueued && failMail != null
                                  && string.IsNullOrEmpty(failMail.action)
                                  && failMail.status == "approved" && !InboxManager.RequiresDecision(failMail);
                bool failBody = failMail != null && failMail.body.Contains("未执行的原因")
                                && failMail.body.Contains("本线现状（未作任何改动）")
                                && failMail.body.Contains("可以怎么办")
                                && failMail.body.Contains("把FR106/FR107加密到每天四段往返");
                // 办不成 = 一个改动都不落：回退到**上一个已批准**的班次基线。本用例里调令 A（每日 1 段）
                // 还挂着待批、从未批准，而 `BackupScheduleForDirective` 只在「没有快照」时记一次
                //（一封待批函 = 一批改动），故退回的是 A 之前的基线（4 班·周一/三/五/日）。
                bool rolledBackT = rT.FlightCount == flights1T
                                   && rT.WeeklyFlightsDeparture() == weekly0T
                                   && rT.daysOfWeek == daysBaseT;
                // 旧呈报（描述被回退掉的方案）必须一起作废 —— 否则收件箱里留着一封「调整后 7 班/周」
                // 的待批件，台账与回执却是 4 班/周（2026-10-14 顺带修：失败路径也走 SupersedeCapacityReports）。
                int openCapAfterFail = 0;
                for (int i = 0; i < gsC.inbox.Count; i++)
                {
                    var m = gsC.inbox[i];
                    if (m != null && m.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(m))
                        openCapAfterFail++;
                }
                bool oldReportVoided = openCapAfterFail == 0;
                Debug.Log($"R28 机队全在检修时 → 未执行={!repBig.ok}（期望 True） 已投复核报告={repBig.failureQueued}（期望 True）"
                          + $"\n　★关键：汇报件（无决策按钮、不占待办）={failQueued}（期望 True）"
                          + $"　台账回退到已批准基线="
                          + $"{rolledBackT}（期望 True：{rT.WeeklyFlightsDeparture()} 班·{rT.daysOfWeek}，"
                          + $"即回到 {weekly0T} 班·{daysBaseT}）"
                          + $"\n　★关键：被回退掉的旧呈报一并作废（待批件数 {openCapAfterFail}，期望 0）={oldReportVoided}（期望 True）"
                          + $"\n　报告含「未执行的原因 / 可以怎么办」={failBody}（期望 True）"
                          + $"\n　原因：「{repBig.note}」"
                          + $"\n　报告：\n{failMail?.body}");

                // —— 部分办成：四条波次只排得出前面几条 → **不是失败**，缺口必须写进呈报 ——
                // CEO 提「加密到每天四段」而机队只够跑一段时，如实说「哪几个波次排不出、为什么、怎么办」
                // 比一句「办不成」有用得多（那也正是 CEO 要的那封呈报里的实质内容）。
                var repPart = CapacityDirectiveLogic.Execute(gsC, rT, dBig);
                bool partQueued = repPart.ok && !repPart.assignment
                                  && InboxManager.QueueCapacityReport(gsC, rT, repPart);
                InboxMail partMail = null;
                for (int i = gsC.inbox.Count - 1; i >= 0; i--)
                {
                    var m = gsC.inbox[i];
                    if (m != null && m.action == InboxManager.CapacityDirectiveAction) { partMail = m; break; }
                }
                bool partHonest = repPart.ok && repPart.shortages != null && repPart.shortages.Count > 0
                                  && rT.FlightCount * 7 == rT.WeeklyFlightsDeparture()   // 实际排定的每波 7 班 = 每日
                                  && partMail != null && partMail.body.Contains("运力缺口")
                                  && partMail.body.Contains("排不出班");
                Debug.Log($"R28 部分办成（四段只要得到 {rT.FlightCount} 段）→ ok={repPart.ok}（期望 True）"
                          + $" 缺口 {repPart.shortages?.Count ?? 0} 个（期望 >0） 呈报已投递={partQueued}（期望 True）"
                          + $"　实际落定 每周 {rT.WeeklyFlightsDeparture()} 班 · {rT.daysOfWeek}"
                          + $"\n　★关键：缺口写进呈报的「运力缺口」段（含「排不出班」与可选处置）={partHonest}（期望 True）");

                bool okR28 = notAssign && noAircraftWord && wavesRead && targetResolved && executed
                             && gotReport && reportHasMarket && nomMail == 0 && failedMail == 0
                             && failQueued && failBody && rolledBackT && oldReportVoided
                             && partQueued && partHonest;
                Debug.Log($"R28 结论 → {(okR28 ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception ce)
            {
                Debug.LogWarning($"照目标下达用例异常（不阻断后续）：{ce.Message}");
            }
            finally
            {
                GameObject.DestroyImmediate(goCap);
            }

            // ———— R29 邮件正文里的对象链接：裸「<」不得吃掉后文链接（2026-10-13 第五次报障）————
            //
            // 报障原话：「红框中的飞机注册号没有和 B-2000 一样点击跳转飞机详情页的设置」——
            // 同一份「逐机归因」名单里，第一架可点、后面七架不可点。
            //
            // 关键在**用真生成器**（AircraftNominationLogic.FormatBlockedBody）产出正文：
            // 它的落选行写着比较式「减载余量 50 km < 本线 7409 km」，而 TermNavMap.InsideMarkup
            // 旧式把裸的小于号当标签开头（纯文本里一个 '>' 都没有 → lt > gt 恒成立），
            // 于是第一个小于号之后的所有机号都被判成「已在标记里」而不再包链接。
            // 纯函数侧已由 slotcheck R29 用**手写**正文钉住；这里再走一遍**真正文**，
            // 因为「生成器哪天改了措辞、比较式换成别的符号」是纯函数用例看不见的。
            try
            {
                var goLink = new GameObject("__r29_link_swallow_test_gs__");
                var gsL = goLink.AddComponent<GameState>();
                gsL.activeRoutes = new List<RouteData>();
                gsL.fleet = new List<Aircraft>();
                gsL.inbox = new List<InboxMail>();
                gsL.year = 1;
                gsL.quarter = 1;
                // 机队：够不着本线的窄体两架 + 够得着的宽体一架，保证落选名单非空且行内含比较式。
                gsL.fleet.Add(new Aircraft { registration = "B-2000", type = "A320neo" });   // 6,300 km
                gsL.fleet.Add(new Aircraft { registration = "B-2001", type = "737-800" });   // 5,436 km
                gsL.fleet.Add(new Aircraft { registration = "B-2003", type = "A330-300" });  // 11,750 km
                var rL = new RouteData
                {
                    routeId = "R-R29", origin = "FRA", destination = "ATL", distanceKm = 7409f,
                    aircraftType = "A320neo", flightNumber = "FR106", registration = "",
                    depTime = "08:00", arrTime = "", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 6f, dailyProfit = 6f, groundStaffAssigned = 3
                };
                rL.SyncFlightsFromPrimary();
                gsL.activeRoutes.Add(rL);

                var rejectedL = new List<AircraftNominationLogic.Rejection>();
                for (int i = 0; i < gsL.fleet.Count; i++)
                    rejectedL.Add(new AircraftNominationLogic.Rejection
                    {
                        reg = gsL.fleet[i].registration, type = gsL.fleet[i].type,
                        why = "航程不足（5950 km ＋ 减载余量 50 km < 本线 7409 km）",
                        kind = OpsRotationLogic.BlockKind.Range
                    });

                // 走 CEO 真正看到的渲染链路：正文 → 落款装封 → 包链接。
                string rawBodyL = AircraftNominationLogic.FormatBlockedBody(gsL, rL, rejectedL);
                var mailL = new InboxMail { origin = rL.origin, dest = rL.destination, body = rawBodyL };
                string paintedL = TermNavMap.Decorate(InboxLetter.FrameIfNeeded(gsL, mailL, rawBodyL));

                string missedL = "";
                for (int i = 0; i < rejectedL.Count; i++)
                    if (paintedL.IndexOf("<link=aircraft:" + rejectedL[i].reg + ">", StringComparison.Ordinal) < 0)
                        missedL += rejectedL[i].reg + " ";
                bool ltSwallows = missedL.Length == 0;
                bool bodyHasLt = rawBodyL.Contains(" < ");
                // 反例：真标记内的文本不得被重复包装（判据放宽不能宽到「到处都包」）。
                bool noDoubleWrap = CountOf(paintedL, "<link=aircraft:B-2000>") == 1;

                Debug.Log($"R29 逐机归因名单可点性 → 正文含裸小于号={bodyHasLt}（期望 True，这是旧版吃链接的入口）"
                          + $"\n　★关键：名单里每个机号都包成 aircraft 链接={ltSwallows}"
                          + (ltSwallows ? "" : $"（期望 True —— 漏：{missedL.Trim()}，旧版只有第一架可点）")
                          + $"\n　同一机号只包一次={noDoubleWrap}（期望 True）"
                          + $"\n　正文片段：{FirstLineWith(rawBodyL, " < ")}");
            }
            catch (Exception le)
            {
                Debug.LogWarning($"对象链接用例异常（不阻断后续）：{le.Message}");
            }
            finally
            {
                var strayL = GameObject.Find("__r29_link_swallow_test_gs__");
                if (strayL != null) GameObject.DestroyImmediate(strayL);
            }

            // ———— R30 航班号唯一性 + 旧档迁移（2026-10-13 第六次报障）————
            // 报障：「周计划里 FRA-ATL 是 FR106，FRA-ORD 是 FR107。而 FRA-ATL 单航线是 FR106/FR107。」
            // 旧档的基号是连号分配的（100 + i），而回程号 = 去程号 + 1、每波次再 +2 →
            // 第 i 条线要占 8 个号，第 i+1 条线的基号当场落进去。这里在**带状态**下验迁移：
            // 造一个连号旧世界 → 审计必须报撞号 → Normalize 修好 → 再审计必须全绿 → 且幂等。
            try
            {
                var goNum = new GameObject("__r30_flight_no_test_gs__");
                var gsN = goNum.AddComponent<GameState>();
                gsN.activeRoutes = new List<RouteData>();
                gsN.fleet = new List<Aircraft>();
                gsN.inbox = new List<InboxMail>();
                gsN.year = 1; gsN.quarter = 1;
                gsN.companyIata = "FR";
                gsN.fleet.Add(new Aircraft { registration = "B-2007", type = "A330-300" });

                // 旧世界：8 条线、基号连号；前两条多波次（复现「整块重叠」）
                string[] odsN = { "LHR", "CDG", "AMS", "IST", "MUC", "MAD", "ATL", "ORD" };
                for (int i = 0; i < odsN.Length; i++)
                {
                    var rN = new RouteData
                    {
                        routeId = $"FRA-{odsN[i]}-{i + 1}", origin = "FRA", destination = odsN[i],
                        distanceKm = 1000f + i * 900f, aircraftType = "A330-300", flightNumber = $"FR{100 + i}",
                        registration = "B-2007", daysOfWeek = "每日", weeklyFlights = 7,
                        depTime = "08:00", arrTime = "10:30",
                        baseDailyProfit = 5f, dailyProfit = 5f, groundStaffAssigned = 3
                    };
                    rN.SyncFlightsFromPrimary();
                    if (i < 2)   // 多波次：一个波次要两个号，正是「块被撑爆」的现场
                        rN.flights.Add(new RouteFlight
                        {
                            registration = "B-2007", depTime = "14:00", arrTime = "16:30",
                            daysOfWeek = "每日", weeklyFlights = 7
                        });
                    gsN.activeRoutes.Add(rN);
                }

                var beforeN = FlightNumberLedger.Conflicts(gsN.activeRoutes);
                int fixedN = FlightNumberLedger.Normalize(gsN);
                var afterN = FlightNumberLedger.Conflicts(gsN.activeRoutes);
                int againN = FlightNumberLedger.Normalize(gsN);   // 幂等：第二次不该再动
                bool everyBaseN = true;
                for (int i = 0; i < gsN.activeRoutes.Count; i++)
                    if (gsN.activeRoutes[i].flightNumber
                        != $"FR{FlightScheduleFormat.BaseNumberOf(i)}") everyBaseN = false;

                Debug.Log($"R30 航班号迁移 → 迁移前撞号 {beforeN.Count} 处（期望 > 0，这是报障的现场）"
                          + $"\n　★关键：Normalize 重编 {fixedN} 条线（期望 7 —— 第 0 条线的块首本就是 FR100，原地不动）　迁移后撞号 {afterN.Count}（期望 0）"
                          + $"\n　★关键：幂等（第二次重编 {againN} 条，期望 0）　各线块首号正确={everyBaseN}（期望 True）"
                          + $"\n　审计：\n{FlightNumberLedger.Report(gsN.activeRoutes)}");
            }
            catch (Exception ne)
            {
                Debug.LogWarning($"航班号用例异常（不阻断后续）：{ne.Message}");
            }
            finally
            {
                var strayN = GameObject.Find("__r30_flight_no_test_gs__");
                if (strayN != null) GameObject.DestroyImmediate(strayN);
            }

            // ———— R31 周计划与单航线的排班口径（2026-10-14 第七次报障）————
            // 报障：「周计划里 FR148 是周一三五日，单航线 FR148/FR149 是周一二四六。」
            // 根因在周视图拆跨夜窗时把主段（06:30→24:00）丢了、只剩次日尾段；
            // 同一段还牵出时刻列两个口径（周计划印去程落地、单航线印回程落地）。
            // 这里在带状态下验：①真实建线（Materialize）产出的世界对账必须零问题；
            // ②跨夜班的星期圈必须含尾段次日；③时刻列全公司只有一个口径（WindowLabel）。
            try
            {
                var goW = new GameObject("__r31_week_grid_test_gs__");
                var gsW = goW.AddComponent<GameState>();
                gsW.activeRoutes = new List<RouteData>();
                gsW.fleet = new List<Aircraft>();
                gsW.inbox = new List<InboxMail>();
                gsW.year = 1; gsW.quarter = 1;
                gsW.fleet.Add(new Aircraft { registration = "B-2007", type = "A330-300" });

                // 报障同形的线：FRA—ATL 7409 km、06:30 出发、回程次日落地、周一/三/五/日
                var rw = new RouteData
                {
                    routeId = "FRA-ATL-7", origin = "FRA", destination = "ATL",
                    distanceKm = 7409f, aircraftType = "A330-300", flightNumber = "FR148",
                    registration = "B-2007", daysOfWeek = "周一/三/五/日", weeklyFlights = 4,
                    depTime = "06:30", arrTime = "15:13",
                    baseDailyProfit = 5f, dailyProfit = 5f, groundStaffAssigned = 3
                };
                rw.SyncFlightsFromPrimary();
                gsW.activeRoutes.Add(rw);

                var fw = rw.flights[0];
                FlightScheduleFormat.WindowMinutes(rw, fw, out int sw, out int ew);
                var gridW = FlightScheduleFormat.GridRowDays(fw);
                bool departW = gridW[0] && gridW[2] && gridW[4] && gridW[6];
                // ★第九次报障：尾段那天**不得**被预标成有班 —— 否则星期圈七列全亮，读成「每日一班」
                bool tailQuietW = !gridW[1] && !gridW[3] && !gridW[5];
                // 回程跨午夜的部分按时刻落到次日列（06:30 那班只跨 46 分）
                FlightScheduleFormat.WindowTimes(rw, fw, out _, out _, out int retDepW, out int retArrW);
                FlightScheduleFormat.SplitWindow(retDepW, retArrW,
                    out int offW, out int mainSW, out int mainEW, out int tailEW);
                bool spillW = offW == 0 && mainEW == FlightScheduleFormat.MinutesPerDay && tailEW > 0;
                var issuesW = FlightScheduleFormat.AuditSchedule(gsW.activeRoutes);

                Debug.Log($"R31 周计划与单航线口径 → 占窗 {sw}→{ew} 分（期望 end > 1440 = 跨夜）"
                          + $"\n　★关键：星期圈 = 执飞日（一三五日）{departW}（期望 True）　二四六不因尾段点亮 {tailQuietW}（期望 True）"
                          + $"\n　★关键：回程只跨 {tailEW} 分且落到次日列（不是整天重绘）{spillW}（期望 True）"
                          + $"\n　★关键：周计划行与班次行对账零问题={issuesW.Count == 0}（期望 True；有问题：{string.Join("；", issuesW)}）"
                          + $"\n　时刻列口径：{FlightScheduleFormat.WindowLabel(rw, fw)}（周计划/单航线/邮件共用同一串，跨夜标 +1）");
            }
            catch (Exception we)
            {
                Debug.LogWarning($"周排班口径用例异常（不阻断后续）：{we.Message}");
            }
            finally
            {
                var strayW = GameObject.Find("__r31_week_grid_test_gs__");
                if (strayW != null) GameObject.DestroyImmediate(strayW);
            }

            // ———— R32 一趟往返的三段结构与过站时长（2026-10-14 第八次报障）————
            // 报障：「过站 50 分钟，看时刻表上的空余时间，感觉都是在 ATL 过站；而且空余时间感觉也超过了 50 分钟。」
            // 数据没错：06:30 出发 → 15:13 落地 ATL → 过站 50 分 → 16:03 起飞 → 次日 00:46 落地 FRA（往返 18h16m）。
            // 错在旧版把这三段画成**一整条 18 小时的长条**，过站 50 分在图上没有像素；屏幕上唯一看得见的
            // 空档是本场地面（00:46 → 次日 06:30 = 5h44m），看着就像「在对方站过站了 5 小时」。
            // 现画成去程段 / 橙条过站段 / 回程段。这里在带状态下验这套算术与图上三段同源。
            try
            {
                var goT = new GameObject("__r32_turn_test_gs__");
                var gsT = goT.AddComponent<GameState>();
                gsT.activeRoutes = new List<RouteData>();
                gsT.fleet = new List<Aircraft>();
                gsT.inbox = new List<InboxMail>();
                gsT.year = 1; gsT.quarter = 1;
                gsT.fleet.Add(new Aircraft { registration = "B-2007", type = "A330-300" });

                var rt = new RouteData
                {
                    routeId = "FRA-ATL-8", origin = "FRA", destination = "ATL",
                    distanceKm = 7409f, aircraftType = "A330-300", flightNumber = "FR148",
                    registration = "B-2007", daysOfWeek = "周一/三/五/日", weeklyFlights = 4,
                    depTime = "06:30", arrTime = "15:13",
                    baseDailyProfit = 5f, dailyProfit = 5f,
                    groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300")
                };
                rt.SyncFlightsFromPrimary();
                gsT.activeRoutes.Add(rt);

                var ft = rt.flights[0];
                FlightScheduleFormat.WindowTimes(rt, ft, out int d0, out int a0, out int d1, out int a1);
                var blocksT = AsWeekGridUI.Collect(gsT, null, rt, 0);
                var bt = blocksT.Count > 0 ? blocksT[0] : null;
                int turnT = bt != null ? bt.TurnMinutes : 0;
                var tripT = FlightScheduleFormat.Trip(rt, ft);

                Debug.Log($"R32 一趟往返的三段 → 去程 {tripT.outDepFull}→{tripT.outArrFull}"
                          + $"　过站 {turnT} 分（在 {rt.destination}）"
                          + $"　回程 {tripT.inDepFull}→{tripT.inArrFull}"
                          + $"　往返 {FlightScheduleFormat.Duration(a1 - d0)}"
                          + $"\n　★关键：过站 = 回程起飞 − 去程落地 = 引擎口径 50 分"
                          + $"\n　★关键：三段之和 = 占窗（{(a0 - d0)} + {turnT} + {(a1 - d1)} = {a1 - d0} 分）"
                          + $"\n　★关键：图上三段的取数 = 引擎口径（去程段 {(bt != null ? bt.startMin : -1)}→{(bt != null ? bt.outArrMin : -1)}、"
                          + $"过站条 {(bt != null ? bt.outArrMin : -1)}→{(bt != null ? bt.retDepMin : -1)}、"
                          + $"回程段 {(bt != null ? bt.retDepMin : -1)}→{(bt != null ? bt.endMin : -1)}）"
                          + $"\n　（旧版只画一条 {tripT.outDepFull}→次日 {tripT.inArrFull} 的长条，"
                          + $"过站 50 分无处可见；看得见的空档是本场地面 "
                          + $"{ScheduleSlots.FormatClock(a1 % (24 * 60))}→{rt.depTime}"
                          + $" = {FlightScheduleFormat.Duration(24 * 60 + d0 - a1)}）");
            }
            catch (Exception te)
            {
                Debug.LogWarning($"往返三段用例异常（不阻断后续）：{te.Message}");
            }
            finally
            {
                var strayT = GameObject.Find("__r32_turn_test_gs__");
                if (strayT != null) GameObject.DestroyImmediate(strayT);
            }

            // ———— R33 过站不含机组休息 → 外站换组（2026-10-14 第九次报障）————
            // 报障：「FRA-ATL过站时间包括机乘人员的休息时间吗？」
            // 答：不包括 —— 过站是纯地面作业（50 分基准 + 缺勤地勤 ×12 分），在值勤口径里属**值勤时间**。
            // 本线同组连飞＝报到 05:30 → 回场 00:46+1 = 19.3h，而上限只有 14.5h
            // （A330-300 Class 2 / 3 人制 15h，报到 05:30 再 −30 分）—— 旧版判据早已走「外站过夜」
            // 分支放行，**记账**却把它记成一个 19.3h 的值勤期，飞机详情因此印出
            // 「实耗 19.3h（… ≤15h）｜剩余 −4.8h」。现统一为**外站换组**：一趟往返 = 两个值勤期
            // （去程组 05:30→15:13 = 9.7h；驻站回程组 15:03→00:46+1 = 9.7h），两段各自在上限内。
            try
            {
                var goSw = new GameObject("__r33_crew_swap_gs__");
                var gsSw = goSw.AddComponent<GameState>();
                gsSw.activeRoutes = new List<RouteData>();
                gsSw.fleet = new List<Aircraft>();
                gsSw.inbox = new List<InboxMail>();
                gsSw.year = 1; gsSw.quarter = 1;
                gsSw.fleet.Add(new Aircraft { registration = "B-2007", type = "A330-300" });

                var rSw = new RouteData
                {
                    routeId = "FRA-ATL-9", origin = "FRA", destination = "ATL",
                    distanceKm = 7409f, aircraftType = "A330-300", flightNumber = "FR148",
                    registration = "B-2007", daysOfWeek = "周一/三/五/日", weeklyFlights = 4,
                    depTime = "06:30", arrTime = "15:13",
                    baseDailyProfit = 5f, dailyProfit = 5f,
                    groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300")
                };
                rSw.SyncFlightsFromPrimary();
                gsSw.activeRoutes.Add(rSw);

                // 飞机详情那一行（CEO 看到的）：判据与记账必须同源
                var winSw = OpsRotationLogic.WindowOf(rSw, rSw.flights[0].depTime);
                var planSw = new OpsRotationLogic.RotationPlan();
                planSw.Assign("B-2007", winSw);
                string sumSw = planSw.DutySummary("B-2007", "A330-300");
                var shapeSw = OpsRotationLogic.DutyShapeOf("A330-300", winSw, out var oSlice, out var bSlice);
                int limSw = OpsRotationLogic.BudgetOf("A330-300",
                    winSw.start - OpsRotationLogic.ReportBeforeMinutes, winSw.block, 2).limit;
                int lenSw = winSw.end - (winSw.start - OpsRotationLogic.ReportBeforeMinutes);

                bool twoDuties = sumSw.Contains("去程组") && sumSw.Contains("驻站回程组")
                                 && sumSw.Contains("值勤2");
                bool noNegative = !sumSw.Contains("剩余 -") && !sumSw.Contains("剩余-")
                                  && oSlice.Remaining > 0 && bSlice.Remaining > 0;
                bool bothWithin = oSlice.Length <= oSlice.limit && bSlice.Length <= bSlice.limit;
                bool noRestCredit = bSlice.report < oSlice.end
                                    && winSw.turn < OpsRotationLogic.MinRestMinutes;

                Debug.Log($"R33 过站不含休息 → 外站换组 → 飞机详情那一行：\n　{sumSw}"
                          + $"\n　★关键：判为外站换组={shapeSw == OpsRotationLogic.RoundTripShape.CrewSwap}（期望 True）"
                          + $"，同组连飞 {lenSw / 60f:0.#}h > 上限 {limSw / 60f:0.#}h（期望 True）"
                          + $"\n　★关键：印出两个值勤期（去程组 + 驻站回程组）={twoDuties}（期望 True）"
                          + $"，两段各自在上限内={bothWithin}（期望 True：{oSlice.Length / 60f:0.#}h / {bSlice.Length / 60f:0.#}h"
                          + $" ≤ {oSlice.limit / 60f:0.#}h / {bSlice.limit / 60f:0.#}h）"
                          + $"\n　★关键：剩余不为负={noNegative}（期望 True；旧版印「剩余 −4.8h」）"
                          + $"\n　★关键：过站不给机组任何休息余量（回程组报到 {OpsRotationLogic.FormatMinute(bSlice.report)} "
                          + $"早于去程组落地 {OpsRotationLogic.FormatMinute(oSlice.end)}、过站 {winSw.turn} 分 < 最低休息 "
                          + $"{OpsRotationLogic.MinRestMinutes / 60f:0.#}h）={noRestCredit}（期望 True）");

                // 呈报口径：同一份记账派生的换组说明（不再出现 19.3h 的值勤期）
                string swapNote = OpsRotationLogic.CrewSwapNote(shapeSw, rSw.destination, rSw.depTime);
                Debug.Log($"R33 呈报口径 → 换组说明「{swapNote}」（期望非空：{swapNote.Length > 0}）");

                if (shapeSw != OpsRotationLogic.RoundTripShape.CrewSwap || !twoDuties || !noNegative
                    || !bothWithin || !noRestCredit || lenSw <= limSw)
                    Debug.LogWarning("R33 ★有不符期望的项，见上");
                else
                    Debug.Log("R33 结论 → 全部符合期望（过站不含休息 / 拆两个值勤期 / 剩余不为负）");
            }
            catch (Exception te)
            {
                Debug.LogWarning($"外站换组用例异常（不阻断后续）：{te.Message}");
            }
            finally
            {
                var straySw = GameObject.Find("__r33_crew_swap_gs__");
                if (straySw != null) GameObject.DestroyImmediate(straySw);
            }

            // ———— R34 两组机组轮换 → 一架飞机能不能一周七趟（2026-10-14 第十次问询）————
            // 问：「如果用两组机乘人员轮换，那么一架飞机回程 00:46 落地轮挡后，也可以在 05:30 报到。
            // 这样是不是一架飞机也可以安排一周七趟往返？」
            // 三根轴必须分开答，否则「能不能」这个词会把三个不同的上限搅在一起：
            //   ① 机体占用：一趟往返占机 18h16m，回场 00:46 → 次日 06:30 = 本场 5h44m，
            //      正好 24h 一个循环 → **每天一圈排得开**，一周七趟在机体与时刻槽上成立；
            //   ② 机组：**不是**「落地那组 05:30 再报到」（只有 4h44m < 10h 最低休息，判据会拒），
            //      而是**另一组**（去程组）报到；且**两组不够、要三组** —— 一组每 3 天只能飞 2 段
            //      （飞完去程人在 ATL，只能接次日回程；落地 FRA 00:46 后接不上当日早班，再空一天）；
            //   ③ 班次模型：`RouteFrequencyModel` 按「单机日利用率目标 10h ÷ 单圈 18.27h ≈ 0.55 班/日」
            //      给每周 4 班 —— 现排 4 班正好把日利用率摊到 9.96h，贴着目标。
            // ★本轮按 CEO 裁决改了两处口径，本段必须把它们钉住：
            //   调令优先（★ceo_first）：周班次由「班期天数 × 波次数」定，模型退为建议值 ——
            //     旧版无论 CEO 说什么都由模型现算，于是「加密」在一周只给 4 班的长程线上
            //     **抬不动周班次**，CEO 没有一条路能到「每日」（呈报里那句「先改周班次」是死路）。
            //   一圈 2 段（★unify_two_sector）：日利用率一律按「去 + 回两段」计（旧版一处 1 段、一处 2 段，
            //     同一架飞机同一周差一倍：9.96 vs 4.98，效率乘数被误压在 0.80）。
            try
            {
                var goCy = new GameObject("__r34_crew_cycle_gs__");
                var gsCy = goCy.AddComponent<GameState>();
                gsCy.activeRoutes = new List<RouteData>();
                gsCy.fleet = new List<Aircraft>();
                gsCy.inbox = new List<InboxMail>();
                gsCy.year = 1; gsCy.quarter = 1;
                gsCy.fleet.Add(new Aircraft
                {
                    registration = "B-2007", type = "A330-300", isActive = true,
                    status = AircraftStatuses.Airworthy, age = 3
                });

                var rCy = new RouteData
                {
                    routeId = "FRA-ATL-10", origin = "FRA", destination = "ATL",
                    distanceKm = 7409f, aircraftType = "A330-300", flightNumber = "FR148",
                    registration = "B-2007", daysOfWeek = "周一/三/五/日", weeklyFlights = 4,
                    depTime = "06:30", arrTime = "15:13",
                    baseDailyProfit = 5f, dailyProfit = 5f,
                    groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300")
                };
                rCy.SyncFlightsFromPrimary();
                gsCy.activeRoutes.Add(rCy);

                // 时刻槽：两端把整张 30 分钟系列网格铺满（本段考的是机组闸门，不该被槽卡住）。
                SeedFullSeries(gsCy, "FRA");
                SeedFullSeries(gsCy, "ATL");

                // —— 轴①：机体占用 / 24h 循环 ——
                var wCy = OpsRotationLogic.WindowOf(rCy, rCy.depTime);
                int blkCy = OpsRotationLogic.BlockOf(rCy);            // 单段轮挡（后文算周均日利用率用）
                var planCy = new OpsRotationLogic.RotationPlan();
                planCy.Assign("B-2007", wCy);
                int readyCy = planCy.ReadyAt("B-2007");
                int nextDepCy = wCy.start + 1440;                     // 次日同一时刻再出发
                Debug.Log($"R34 轴① 机体 → {rCy.depTime} 出发 · {OpsRotationLogic.FormatMinute(wCy.end)} 回场"
                          + $"（占机 {(wCy.end - wCy.start) / 60f:0.#}h）· 本场可再出发 "
                          + $"{OpsRotationLogic.FormatMinute(readyCy)}（含过站 {wCy.turn} 分 + 轮转裕度 "
                          + $"{OpsRotationLogic.RotationGapMinutes} 分）"
                          + $"\n　　次日同刻再出发余量 {(nextDepCy - readyCy) / 60f:0.#}h（期望 ≥0 → 每天一圈排得开）");

                // —— 轴②：机组休息与组数 ——
                int nextMorningReport = nextDepCy - OpsRotationLogic.ReportBeforeMinutes;
                int backReport = 15 * 60 + 3;                        // 驻站回程组次日 15:03 报到
                Debug.Log($"R34 轴② 机组 → 回程组落地 {OpsRotationLogic.FormatMinute(wCy.end)}"
                          + $" → 次日 05:30 报到只有 {(nextMorningReport - wCy.end) / 60f:0.#}h"
                          + $"（最低 {OpsRotationLogic.MinRestMinutes / 60f:0.#}h → 05:30 报到**必须是另一组**）"
                          + $"\n　　去程组落 ATL {OpsRotationLogic.FormatMinute(wCy.start + wCy.block)}"
                          + $" → 次日回程 {OpsRotationLogic.FormatMinute(backReport)} 报到 = "
                          + $"{((24 * 60 + backReport) - (wCy.start + wCy.block)) / 60f:0.#}h（≥ 最低休息 → 可接）"
                          + $"\n　　回程组落地 FRA {OpsRotationLogic.FormatMinute(wCy.end)} → 再下一日早班报到 = "
                          + $"{((2 * 24 * 60 + 5 * 60 + 30) - wCy.end) / 60f:0.#}h（接不上当日早班 → 再空一天）");
                float perCrewPerDayCy = 2f / 3f;                     // 一组每 3 天只能飞 2 段
                float crewsNeededCy = 2f / perCrewPerDayCy;          // 每天要 2 段
                Debug.Log($"R34 轴② 组数 → 每天 2 段（去程 + 回程）＝每周 14 段；一组每 3 天最多 2 段"
                          + $"\n　　两组 = {2f * perCrewPerDayCy:0.##} 段/天 < 2 段/天（**不够**）；"
                          + $"需要 {crewsNeededCy:0.#} 组（期望 3，且 2 不够）");

                // —— 轴③：班次模型给的建议值 ——
                var freqCy = RouteFrequencyModel.ForRoute(rCy);
                Debug.Log($"R34 轴③ 班次模型 → {RouteFrequencyModel.Explain("FRA", "ATL", 7409f, "A330-300")}"
                          + $"\n　　供给侧 {freqCy.flightsByAircraft:0.###} 班/日（日利用率目标 "
                          + $"{EconomyBalanceParams.TargetDailyUtilizationHours:0.#}h ÷ 单圈 {(2f * RouteFrequencyModel.BlockMinutes(7409f, "A330-300") + EconomyBalanceParams.BaseTurnMinutes) / 60f:0.#}h）"
                          + $"，需求侧 {freqCy.flightsByDemand:0.###} 班/日 → 建议每周 {freqCy.weeklyFlights} 班（期望 4）"
                          + $"\n　　现排 {rCy.FlightCount} 段/日 × 每周 {rCy.WeeklyFlightsDeparture()} 班 → 日利用率 "
                          + $"{rCy.WeeklyFlightsDeparture() * 2f * RouteFrequencyModel.BlockMinutes(7409f, "A330-300") / 60f / 7f:0.##}h/天"
                          + $"（目标 {EconomyBalanceParams.TargetDailyUtilizationHours:0.#}h）");

                // —— 真实调令：加密到每天 1 班（能不能到 7 班/周）——
                var dirCy = CapacityDirectiveLogic.Parse("加密到每天 1 班");
                var repCy = CapacityDirectiveLogic.Execute(gsCy, rCy, dirCy);
                Debug.Log($"R34 调令「加密到每天 1 班」→ ok={repCy.ok}（期望 True）· 每日 {rCy.FlightCount} 段 · "
                          + $"每周 {rCy.WeeklyFlightsDeparture()} 班 {rCy.daysOfWeek}（期望 7 班 · 每日）· "
                          + $"缺口 {repCy.shortages?.Count ?? 0} 个（期望 0：本线早班槽在手）");

                // —— 答复件：CEO 用原话问，运营部到底答什么 ——
                var qaCy = OpsQueryLogic.Build(gsCy, rCy, "如果用两组机乘人员轮换，那么一架飞机回程 00:46 落地后，也可以在 05:30 报到。这样是不是一架飞机也可以安排一周七趟往返？");
                Debug.Log($"R34 答复件 → 话题={OpsQueryLogic.TopicTitles(qaCy.topics)}（期望 班期 / 执飞机 / 值勤 三段都在）\n{qaCy.body}");

                // —— 判据：三根轴的**实际数字**都要对得上，别让这一问退化成「打印一堆数字」 ——
                bool cyOk = true;
                // 轴① 机体：24h 一圈排得开（回场 00:46 → 次日 06:30 再出发仍有余量）
                cyOk &= (wCy.end - wCy.start) == 1096 && nextDepCy > readyCy;
                // 轴② 机组：落地那组接不上 05:30（4h44m < 10h）；去程组在 ATL 的 23h50m 接得上；
                //              一组每 3 天 2 段 → 每日一趟要 3 组（两组不够）
                //              ★第十三次问询：每组人数两岗各算各的（驾驶舱看值勤、客舱按座位 1/50）
                var needCy = OpsRotationLogic.RouteCrewNeed(rCy, 1);
                int setsCy = needCy.sets, pilotsCy = needCy.pilots, cabinCy = needCy.cabin;
                cyOk &= (nextMorningReport - wCy.end) < OpsRotationLogic.MinRestMinutes
                        && ((24 * 60 + backReport) - (wCy.start + wCy.block)) >= OpsRotationLogic.MinRestMinutes
                        && setsCy == 3 && pilotsCy == 3 * OpsRotationLogic.PilotsPerDuty(wCy.block)
                        && cabinCy == 3 * OpsRotationLogic.CabinPerDuty(rCy.aircraftType);
                // 轴③ 班次模型给的建议值仍是 4 班（建议，不是闸门）
                cyOk &= freqCy.weeklyFlights == 4;
                // 轴④ 日利用率口径已统一为「一圈 2 段」：航空经济（周均）与手算逐位相同
                float utilWkCy = rCy.WeeklyFlightsDeparture() * OpsEconomics.RoundTripSectors * blkCy / 60f / 7f;
                var metCy = OpsEconomics.Evaluate(7409f, rCy.WeeklyFlightsDeparture(), "A330-300",
                                                  rCy.groundStaffAssigned, 88f);
                cyOk &= Math.Abs(metCy.dailyUtilizationHours - utilWkCy) < 0.02f;
                // 轴⑤ 调令优先：CEO 说「加密到每天 1 班」就得是 7 班/周 + 每日（旧版恒按模型给 4 班）
                cyOk &= repCy.ok && rCy.WeeklyFlightsDeparture() == 7 && rCy.daysOfWeek == "每日"
                        && (repCy.shortages == null || repCy.shortages.Count == 0);
                // 答复件要把三根轴都答上：班期 + 执飞机 + 值勤（含「要 3 组」这句实话）
                cyOk &= qaCy.topics.Contains(OpsQueryLogic.TopicDays)
                        && qaCy.topics.Contains(OpsQueryLogic.TopicAircraft)
                        && qaCy.topics.Contains(OpsQueryLogic.TopicDuty)
                        && qaCy.body.Contains($"{setsCy} 组");
                Debug.Log($"R34 结论 → 轴① 机体排得开={nextDepCy > readyCy}"
                          + $"　轴② 机组要 {setsCy} 组（{pilotsCy} 名）"
                          + $"　轴③ 模型建议 {freqCy.weeklyFlights} 班"
                          + $"　轴④ 周均日利用率 {metCy.dailyUtilizationHours:0.##}h"
                          + $"　轴⑤ 调令落定 {rCy.WeeklyFlightsDeparture()} 班/周·{rCy.daysOfWeek}"
                          + $"　→ {(cyOk ? "全部符合期望" : "★有不符合期望的项，见上")}");

                GameObject.DestroyImmediate(goCy);
            }
            catch (Exception cy)
            {
                Debug.LogWarning($"一周七趟用例异常（不阻断后续）：{cy.Message}");
            }
            finally
            {
                var strayCy = GameObject.Find("__r34_crew_cycle_gs__");
                if (strayCy != null) GameObject.DestroyImmediate(strayCy);
            }

            // ———— R35 机组产能是排班的第四道硬闸门（2026-10-14 CEO 第十一次问询）————
            // 原话：「当 CEO 输入『增加此航线的航班量』，那么除了要获得更多的时刻槽，也要考虑增加轮换机组。」
            // 本段钉住**带状态**那一半（纯函数半在 slotcheck R35）：
            //   ① 加密时若机组不够，排班器**不再静默照排** —— 超出机组产能的波次记成 kind=crew 缺口；
            //   ② 呈报走**两阶段**（crewFirst）：先补机组 → 再重下加密指令，与缺槽同一套形状；
            //   ③ 反向护栏：机组不够**只拦新增波次**，绝不反把已有班次砍掉；
            //   ④ 补员路径可验证：加机抬编制（编制 = 机队推导）后同一句指令就能排满。
            try
            {
                var goCg = new GameObject("__r35_crew_gate_gs__");
                var gsCg = goCg.AddComponent<GameState>();
                gsCg.activeRoutes = new List<RouteData>();
                gsCg.fleet = new List<Aircraft>();
                gsCg.inbox = new List<InboxMail>();
                gsCg.staffPool = new List<PersonnelData>();   // 未按人建机组 → 走编制口径
                gsCg.year = 1; gsCg.quarter = 1;

                // —— 场景 A：一架宽体 · 编制只够 1 段 → 「加密到每天 2 班」被机组闸门拦住 ——
                // ★舱位必须显式配置（FleetManager 建档时取 `AircraftCatalog.maxPassengers`）：
                //   裸 `new Aircraft` 的 economySeats 默认 180，而客舱**编制**走 `a.TotalSeats`，
                //   不配就会算出 18 名（180/50×4+4）而不是 28 名 —— 那是夹具失真，不是闸门出错。
                gsCg.fleet.Add(new Aircraft
                {
                    registration = "B-3007", type = "A330-300", isActive = true,
                    status = AircraftStatuses.Airworthy, age = 3,
                    economySeats = AircraftCatalog.Find("A330-300") != null
                        ? AircraftCatalog.Find("A330-300").maxPassengers : 300
                });
                var rCg = new RouteData
                {
                    routeId = "FRA-ATL-35", origin = "FRA", destination = "ATL",
                    distanceKm = 7409f, aircraftType = "A330-300", flightNumber = "FR148",
                    registration = "B-3007", daysOfWeek = "周一/三/五/日", weeklyFlights = 4,
                    depTime = "06:30", arrTime = "15:13",
                    baseDailyProfit = 5f, dailyProfit = 5f,
                    groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300")
                };
                rCg.SyncFlightsFromPrimary();
                gsCg.activeRoutes.Add(rCg);
                // 时刻槽：两端把整张 30 分钟系列网格铺满（本段考的是机组闸门，不该被槽卡住）。
                SeedFullSeries(gsCg, "FRA");
                SeedFullSeries(gsCg, "ATL");

                int estCg = gsCg.PilotEstablishment;             // 1 宽体 = 8 + 2 = 10 名
                int onDutyCg = gsCg.PilotFilled;                 // 0（未按人建机组）
                int estCabinCg = gsCg.CabinEstablishment;        // 300 座 → 300/50×4 + 1×4 = 28 名
                int cabinPerDutyCg = OpsRotationLogic.CabinPerDuty(rCg.aircraftType);   // 300 座 → 6 名
                var need2Cg = OpsRotationLogic.RouteCrewNeed(rCg, 2);
                int sets2Cg = need2Cg.sets, pilots2Cg = need2Cg.pilots, cabin2Cg = need2Cg.cabin;
                int capCg = CrewCapacityLogic.MaxCrewedTrips(gsCg, rCg);
                gsCg.GetFleetStaffing(out _, out _, out int seatsCg);   // 供打印：编制按**实际座数**推
                Debug.Log($"R35 场景 A → 驾驶舱编制 {estCg} 名（1 架 A330-300 = 8+2）· 在职 {onDutyCg} 名"
                          + $"　客舱编制 {estCabinCg} 名（在役座数 {seatsCg} → 每 50 座 4 名 + 每架 4 名备份）· 每组客舱 {cabinPerDutyCg} 名"
                          + $"\n　　每日 2 段需 {sets2Cg} 组（驾驶舱 {pilots2Cg} 名 + 客舱 {cabin2Cg} 名）→ 两岗取小 → 机组口径最多 {capCg} 段/日（期望 1：两岗都不够）");

                var repCg = CapacityDirectiveLogic.Execute(gsCg, rCg, CapacityDirectiveLogic.Parse("加密到每天 2 班"));
                int crewGapsCg = CountKind(repCg.shortages, CrewCapacityLogic.KindCrew);
                bool crewFirstCg = repCg.crewFirst;
                Debug.Log($"R35 场景 A 呈报 → ok={repCg.ok}（期望 True）· 目标 2 段 · 实排 {rCg.FlightCount} 段（期望 1：机组到顶）"
                          + $"· crew 缺口 {crewGapsCg} 个（期望 1）· crewFirst={crewFirstCg}（期望 True）");
                Debug.Log($"R35 场景 A 缺口原因 → {FirstLineWith(repCg.shortageSection, "机组")}");
                Debug.Log($"R35 场景 A 飞行机组段 → {FirstLineWith(repCg.crewSection, "需求")}");
                Debug.Log($"R35 场景 A 请批复段 → {FirstLineWith(CapacityDirectiveLogic.FormatReportBody(gsCg, rCg, repCg), "第一阶段")}");

                bool aOk = repCg.ok && rCg.FlightCount == 1 && crewGapsCg == 1 && crewFirstCg
                           && estCg == 10 && capCg == 1
                           && estCabinCg == 28 && cabinPerDutyCg == 6
                           && repCg.shortageSection.Contains("机组缺口")
                           && repCg.crewSection.Contains("需求")
                           && repCg.crewSection.Contains("客舱")
                           && repCg.crewSection.Contains("两阶段");

                // —— 场景 B：加一架宽体（编制 10 → 20，客舱 28 → 56）→ 同一句「加密到每天 2 班」排满 ——
                // （舱位同场景 A 显式配置：客舱编制走实际座数，裸默认 180 会少算）
                gsCg.fleet.Add(new Aircraft
                {
                    registration = "B-3008", type = "A330-300", isActive = true,
                    status = AircraftStatuses.Airworthy, age = 2,
                    economySeats = AircraftCatalog.Find("A330-300") != null
                        ? AircraftCatalog.Find("A330-300").maxPassengers : 300
                });
                int est2Cg = gsCg.PilotEstablishment;            // 8×2 + 2×2 = 20 名
                int estCabin2Cg = gsCg.CabinEstablishment;       // 600/50×4 + 2×4 = 56 名
                int cap2Cg = CrewCapacityLogic.MaxCrewedTrips(gsCg, rCg);   // 驾驶舱 2 段（18 ≤ 20）· 客舱 3 段（54 ≤ 56）→ 2
                var rep2Cg = CapacityDirectiveLogic.Execute(gsCg, rCg, CapacityDirectiveLogic.Parse("加密到每天 2 班"));
                int crewGaps2Cg = CountKind(rep2Cg.shortages, CrewCapacityLogic.KindCrew);
                Debug.Log($"R35 场景 B → 加机后编制 驾驶舱 {est2Cg} 名（期望 20）/ 客舱 {estCabin2Cg} 名（期望 56）"
                          + $"· 机组口径可排 {cap2Cg} 段/日（期望 2 —— 客舱 3 段也够，故**驾驶舱先卡**）"
                          + $"　实排 {rCg.FlightCount} 段（期望 2）· crew 缺口 {crewGaps2Cg} 个（期望 0）"
                          + $"· crewFirst={rep2Cg.crewFirst}（期望 False）");
                Debug.Log($"R35 场景 B 飞行机组段 → {FirstLineWith(rep2Cg.crewSection, "机组")}");

                bool bOk = rep2Cg.ok && est2Cg == 20 && estCabin2Cg == 56 && cap2Cg == 2 && rCg.FlightCount == 2
                           && crewGaps2Cg == 0 && !rep2Cg.crewFirst && rep2Cg.crewSection.Contains("足够");

                // —— 场景 C：反向护栏 —— 在职机组被别的需求挤到只剩 3 名（口径切到「在职」、产能为 0）
                //            此时再要「加密到每天 3 班」：**只拦第 3 波**，已排的 2 段不许被反砍 ——
                //            ★注意：只建驾驶舱的档，客舱口径**不受影响**（仍按编制 56 名）——
                //            两岗退化各自独立，否则「招飞行员」会凭空给乘务也开一道缺口。
                for (int i = 0; i < 3; i++)
                    gsCg.staffPool.Add(new PersonnelData
                    {
                        id = "selftest_r35_p" + i, fullName = "自测·飞行员" + i,
                        department = "OPS", jobTitle = "飞行员", isOperational = true, onMarket = false
                    });
                int onDutyC2 = gsCg.PilotFilled;                 // 3 → 口径切到在职
                int onDutyCabinC2 = gsCg.CabinFilled;            // 0 → 客舱仍走编制口径
                int cap3Cg = CrewCapacityLogic.MaxCrewedTrips(gsCg, rCg);   // 驾驶舱 3 名 → 0 段
                int beforeWavesCg = rCg.FlightCount;
                var rep3Cg = CapacityDirectiveLogic.Execute(gsCg, rCg, CapacityDirectiveLogic.Parse("加密到每天 3 班"));
                int crewGaps3Cg = CountKind(rep3Cg.shortages, CrewCapacityLogic.KindCrew);
                Debug.Log($"R35 场景 C → 在职 驾驶舱 {onDutyC2} 名（期望 3，口径切到在职）/ 客舱 {onDutyCabinC2} 名"
                          + $"（期望 0 → **不受驾驶舱影响**，仍按编制 56 名）· 机组口径可排 {cap3Cg} 段/日（期望 0）"
                          + $"\n　　要 3 段：实排 {rCg.FlightCount} 段（期望 {beforeWavesCg} —— 已有班次**不得被反砍**）"
                          + $"· crew 缺口 {crewGaps3Cg} 个（期望 1 —— 只拦第 3 波）");

                bool cOk = onDutyC2 == 3 && onDutyCabinC2 == 0 && cap3Cg == 0 && rCg.FlightCount == beforeWavesCg
                           && beforeWavesCg == 2 && crewGaps3Cg == 1;

                // —— 场景 D：**乘务先卡**（★2026-10-14 第十三次问询新增：客舱也进闸门）——
                //   给足驾驶舱（60 名）、只给 40 名乘务：驾驶舱 9N 名 → 60 名够 4 段；
                //   客舱 18N 名 → 40 名只够 2 段（36 ≤ 40 &lt; 54）→ **两岗取小，卡在乘务**。
                //   判据：缺口仍是 kind=crew，且原因与「飞行机组」段都要**点名乘务员** ——
                //   只报飞行员就是旧口径（旧口径下 crewAllowed = 4，第 3 波根本不会被拦）。
                for (int i = 0; i < 57; i++)
                    gsCg.staffPool.Add(new PersonnelData
                    {
                        id = "selftest_r35_p2_" + i, fullName = "自测·飞行员B" + i,
                        department = "OPS", jobTitle = "飞行员", isOperational = true, onMarket = false
                    });
                for (int i = 0; i < 40; i++)
                    gsCg.staffPool.Add(new PersonnelData
                    {
                        id = "selftest_r35_c" + i, fullName = "自测·乘务员" + i,
                        department = "OPS", jobTitle = "乘务员", isOperational = true, onMarket = false
                    });
                int pFilledCg = gsCg.PilotFilled;                // 3 + 57 = 60
                int cFilledCg = gsCg.CabinFilled;                // 40
                int pBudgetCg = CrewCapacityLogic.BudgetFor(gsCg, rCg, out _, out _, out bool pByOnDutyCg);
                int cBudgetCg = CrewCapacityLogic.CabinBudgetFor(gsCg, rCg, out _, out _, out bool cByOnDutyCg);
                int cap4Cg = CrewCapacityLogic.MaxCrewedTrips(gsCg, rCg);
                string bindingCg = CrewCapacityLogic.BindingRoles(gsCg, rCg, 3);
                int beforeD = rCg.FlightCount;
                var rep4Cg = CapacityDirectiveLogic.Execute(gsCg, rCg, CapacityDirectiveLogic.Parse("加密到每天 3 班"));
                int crewGaps4Cg = CountKind(rep4Cg.shortages, CrewCapacityLogic.KindCrew);
                Debug.Log($"R35 场景 D → 在职 驾驶舱 {pFilledCg} 名（可用 {pBudgetCg} 名，切到在职 {pByOnDutyCg}）"
                          + $"· 客舱 {cFilledCg} 名（可用 {cBudgetCg} 名，切到在职 {cByOnDutyCg}）"
                          + $"\n　　3 段需 驾驶舱 27 名 ≤ {pBudgetCg} **够** · 客舱 54 名 &gt; {cBudgetCg} **不够**"
                          + $"　→ 两岗取小 {cap4Cg} 段/日（期望 2）· 先卡住的是「{bindingCg}」（期望 {CrewCapacityLogic.RoleCabin}）");
                Debug.Log($"R35 场景 D 缺口原因 → {FirstLineWith(rep4Cg.shortageSection, "机组")}");
                Debug.Log($"R35 场景 D 飞行机组段 → {FirstLineWith(rep4Cg.crewSection, "需求")}");

                bool dOk = pFilledCg == 60 && cFilledCg == 40 && pByOnDutyCg && cByOnDutyCg
                           && pBudgetCg == 60 && cBudgetCg == 40 && cap4Cg == 2
                           && bindingCg == CrewCapacityLogic.RoleCabin
                           && rCg.FlightCount == beforeD && beforeD == 2 && crewGaps4Cg == 1
                           && rep4Cg.shortageSection.Contains(CrewCapacityLogic.RoleCabin)
                           && rep4Cg.crewSection.Contains("客舱");

                bool cgOk = aOk && bOk && cOk && dOk;
                Debug.Log($"R35 结论 → 场景A 机组闸门拦住加密+两阶段呈报（两岗都不够）={aOk}"
                          + $"　场景B 加机抬编制后排满（驾驶舱先卡）={bOk}"
                          + $"　场景C 机组不足不反砍已有班次+两岗退化独立={cOk}"
                          + $"　场景D 客舱进闸门、乘务先卡且被点名={dOk}"
                          + $"　→ {(cgOk ? "全部符合期望" : "★有不符合期望的项，见上")}");

                GameObject.DestroyImmediate(goCg);
            }
            catch (Exception cg)
            {
                Debug.LogWarning($"机组闸门用例异常（不阻断后续）：{cg.Message}");
            }
            finally
            {
                var strayCg = GameObject.Find("__r35_crew_gate_gs__");
                if (strayCg != null) GameObject.DestroyImmediate(strayCg);
            }

            // ———— R38 加班时刻备选 → CEO 点定 → 交割后自动重排（2026-10-15 CEO 第十五/十六次问询）————
            // 三句问询：
            //   ①「当 CEO 输入指令增加某条航线的航班量，运营部基于什么条件计算购买哪个时刻的时刻槽？」
            //   ②「是否应该在邮件提供几个备选时刻槽，并分析优缺点。」
            //   ③「新的时刻槽持仓后，运营部应该安排哪架飞机执飞，并且调整航班环。这条操作是否有模型？」
            // 与 slotcheck R38 分工：那边钉纯函数（整窗口网格 / 往返对 / 排序 / 编解码 / 吸附到系列），
            // 本段钉**带状态**的四件事（每条都对应一个曾经真的漏掉的口子）：
            //   ① 缺槽的出口是「一张有选项的选择题」——备选件成形、点定才开单；
            //   ② ★**飞机硬门槛排在时刻槽闸门之前**：为一条没有合适飞机可飞的班次去申请一条槽是本末倒置；
            //   ③ ★交割后运营部**自己**挑机、重排、呈报加机 —— 槽买回来了还要 CEO 回 n98 补手续，等于只办了一半；
            //   ④ 退回备选 = 无痕（不申请、不花钱、班次一根汗毛不动）。
            var goSc = new GameObject("__r38_slot_candidates_gs__");
            try
            {
                var gsS = goSc.AddComponent<GameState>();
                gsS.activeRoutes = new List<RouteData>();
                gsS.fleet = new List<Aircraft>();
                gsS.inbox = new List<InboxMail>();
                gsS.year = 1; gsS.quarter = 1;
                StrategyManager.EnsureState(gsS);

                // —— 场景 A：缺槽 → 备选件（有选项、有理由）→ 点定一刻才开申请单 ——
                // 时刻取 **17:30**（晚班窗口 16:30–20:00 内）而不是窗口峰点 18:00：本线往返 5.3 h，
                // 18:00 起飞回程落地 23:18 正落在 PEK 23:00–06:00 宵禁里 —— 那会被**飞机硬门槛**
                // 先一步挡住（先机后槽），于是根本走不到「时刻槽备选」这一段。备选件要钉的是缺槽出口，
                // 故夹具必须用一个真排得进的时刻。
                var rSel = new RouteData
                {
                    routeId = "R-R38A", origin = "PEK", destination = "SZX", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU700", registration = "B-9001",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                rSel.SyncFlightsFromPrimary();
                gsS.activeRoutes.Add(rSel);
                gsS.fleet.Add(new Aircraft { registration = "B-2106", type = "A320neo", age = 2 });
                int flightsA = rSel.flights.Count;

                bool qA = InboxManager.QueueAssignFlight(gsS, rSel, "B-2106", "17:30", out string msgA);
                var selA = FindSlotSelectForRoute(gsS, rSel.routeId);
                var optsA = SlotCandidateLogic.Decode(selA?.payloadCsv);
                string firstA = optsA.Count > 0 ? optsA[0].depClock : "(无)";
                bool selNeedsDecision = InboxManager.RequiresDecision(selA);   // 点定之后它会结案，故先取值
                Debug.Log($"R38 场景A 缺槽出口 → 加机呈报={qA}（期望 False）· 备选件={CountMails(gsS, InboxManager.SlotSelectAction)}（期望 1）"
                          + $"· 备选 {optsA.Count} 条（期望 ≥2）· 推荐 {firstA}· 需决策={selNeedsDecision}（期望 True）"
                          + $"\n　★关键：班次表未动 {flightsA} → {rSel.flights.Count}（期望仍为 {flightsA}）"
                          + $"\n　★关键2：备选**全部落在 30 分系列网格上**="
                          + $"{AllSeriesAligned(optsA)}（期望 True —— 排班器的 5 分钟粒度时刻不许当槽的身份）"
                          + $"\n　★关键3：正文逐条给了利弊="
                          + $"{(selA != null && (selA.body ?? "").Contains("有利") && (selA.body ?? "").Contains("代价"))}（期望 True）"
                          + $"\n　回执：{msgA}");

                bool pickA = InboxManager.ApproveSlotSelect(gsS, selA, firstA, out string msgPickA);
                var applyA = FindMailByAction(gsS, InboxManager.ApplySlotAction);   // 点定之后才开出申请单
                Debug.Log($"R38 场景A 点定 → 成功={pickA}（期望 True）· 备选件状态={selA?.status}（期望 approved）"
                          + $"· 新开申请单={(applyA != null ? 1 : 0)}（期望 1）"
                          + $"\n　★关键：申请单记下「为哪一刻的班次而提」={applyA?.slotFlightClock}（期望 {firstA}）"
                          + $"· 申请单本身的时刻（缺槽那一端）={applyA?.slotClock}"
                          + $"\n　★关键2：班次表仍未动 {rSel.flights.Count}（期望 {flightsA}）"
                          + $"\n　批复：{msgPickA}");
                bool aOk = !qA && optsA.Count >= 2 && selNeedsDecision
                           && rSel.flights.Count == flightsA && AllSeriesAligned(optsA)
                           && (selA?.body ?? "").Contains("有利") && (selA?.body ?? "").Contains("代价")
                           && pickA && selA.status == "approved" && applyA != null
                           && applyA.slotFlightClock == firstA && rSel.flights.Count == flightsA;

                // —— 场景 B：★飞机硬门槛排在时刻槽之前（机不行、**槽俱全**也必须挡住）——
                // 这是加机路径过去真正的漏洞：旧版只校验时刻槽不校验飞机，于是一架航程不够的飞机
                // 照样能被排上班次，界面上那行「⚠ 机型可能不匹配」只是一句话，批准时也无复验兜底。
                // 夹具刻意**把两端整张系列网格铺满**（槽俱全）：此时唯一能拦住它的就是飞机闸门。
                var rFar = new RouteData
                {
                    routeId = "R-R38B", origin = "PEK", destination = "CDG", distanceKm = 9000f,
                    aircraftType = "A320neo", flightNumber = "SU800", registration = "",
                    depTime = "07:00", arrTime = "18:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                rFar.SyncFlightsFromPrimary();
                gsS.activeRoutes.Add(rFar);
                SeedFullSeries(gsS, "PEK");
                SeedFullSeries(gsS, "CDG");
                int flightsB = rFar.flights.Count;
                int slotMailsB = CountMails(gsS, InboxManager.ApplySlotAction);
                int selMailsB = CountMails(gsS, InboxManager.SlotSelectAction);
                bool qB = InboxManager.QueueAssignFlight(gsS, rFar, "B-2106", "07:00", out string msgB);
                bool bOk = !qB && msgB.Contains("排不进本线") && msgB.Contains("航程不足")
                           && CountMails(gsS, InboxManager.ApplySlotAction) == slotMailsB   // 没去申请槽
                           && CountMails(gsS, InboxManager.SlotSelectAction) == selMailsB    // 也没摆备选
                           && rFar.flights.Count == flightsB;
                Debug.Log($"R38 场景B 飞机硬门槛 → 加机呈报={qB}（期望 False：A320neo 够不着 9,000 km）"
                          + $"\n　★关键：**没有**去开时刻槽申请单/备选件（槽本身是齐的，去申请毫无意义）="
                          + $"{bOk}（期望 True —— 判序必须是「先机后槽」）"
                          + $"\n　★关键2：班次一条未落 {rFar.flights.Count}（期望 {flightsB}）"
                          + $"\n　回执：{msgB}");

                // —— 场景 C：★交割后自动重排（运营部自己挑机、调环、呈报加机）——
                // 旧口径把这一步留给玩家：交割回执写「请回 n98 重新呈报加机」，要 CEO 记住哪条线哪一刻再点一遍。
                // 夹具：本线两端已各有一条班次跑道，只差 **PEK 12:00 这一条槽**；申请单上带着
                // slotRouteId + slotFlightClock（点定备选时写下的），交割那一刻运营部应自己接着办。
                // 时刻取 **12:00**（午班峰点）：本线往返 5.3 h，18:00 起飞会落进 PEK 宵禁，
                // 自动重排的选机环节当场排不出机（如实汇报但不出加机呈报），测不到「自己接着办」。
                var rAu = new RouteData
                {
                    routeId = "R-R38C", origin = "PEK", destination = "CAN", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU900", registration = "B-9002",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                rAu.SyncFlightsFromPrimary();
                gsS.activeRoutes.Add(rAu);
                gsS.fleet.Add(new Aircraft { registration = "B-2107", type = "A320neo", age = 1 });
                SeedFullSeries(gsS, "CAN");                        // 对端（CAN）槽齐全
                for (int i = gsS.stg.slots.Count - 1; i >= 0; i--)  // 本场只留 12:00 缺着
                    if (gsS.stg.slots[i] != null && gsS.stg.slots[i].airport == "PEK"
                        && gsS.stg.slots[i].slotTime == "12:00") gsS.stg.slots.RemoveAt(i);
                bool openedC = InboxManager.QueueApplySlots(gsS, "PEK", "12:00", 1,
                    "m_apply_slot_PEK_1200_r38", out string msgOpenC,
                    "R-R38C 拟加 12:00 一班", rAu.routeId, "12:00");
                var applyC = FindMailByAction(gsS, InboxManager.ApplySlotAction);
                bool apprC = applyC != null && InboxManager.Approve(gsS, applyC, out string msgApprC);
                int beforeC = rAu.flights.Count;
                gsS.year = applyC != null ? applyC.effectYear : gsS.year;
                gsS.quarter = applyC != null ? applyC.effectQuarter : gsS.quarter;
                int assignBefore = CountMails(gsS, InboxManager.AssignFlightAction);
                InboxManager.ProcessPendingEffects(gsS);
                var assignC = FindAssignMailForRoute(gsS, rAu.routeId);
                bool assignHas1200 = assignC != null
                    && (assignC.flightProposalsCsv ?? "").Contains("12:00");
                bool cOk = openedC && apprC && assignBefore == 0 && assignHas1200
                           && rAu.flights.Count == beforeC;      // 呈报待批，班次**不**先落
                Debug.Log($"R38 场景C 交割后自动重排 → 开单={openedC}（期望 True）· 批准={apprC}（期望 True）"
                          + $"· 交割季=第{gsS.year}年Q{gsS.quarter}"
                          + $"\n　★关键：交割后**自动**出了加机呈报={(assignC != null)}（期望 True —— 不再要 CEO 回 n98 重来一遍）"
                          + $"\n　★关键2：呈报的就是 12:00 那一班={assignHas1200}（期望 True）"
                          + $"\n　★关键3：呈报期班次未落 {beforeC} → {rAu.flights.Count}（期望不变：自动化的边界在「办到该 CEO 拍板那一步」）"
                          + $"\n　开单回执：{msgOpenC}"
                          + $"\n　呈报主题：{assignC?.subject}");

                // —— 场景 D：退回备选 = 无痕 ——
                // 夹具要点：场景 B 在本场铺满了整张系列网格，故本场景必须**先把 12:00 的那条槽拿掉**，
                // 否则这一刻两端齐备、走的是加机呈报而不是备选件（D 要钉的是「退回备选不申请、不花钱」）。
                var rRej = new RouteData
                {
                    routeId = "R-R38D", origin = "PEK", destination = "XIY", distanceKm = 1100f,
                    aircraftType = "A320neo", flightNumber = "SU950", registration = "B-9003",
                    depTime = "08:00", arrTime = "10:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 8f, dailyProfit = 8f, groundStaffAssigned = 3
                };
                rRej.SyncFlightsFromPrimary();
                gsS.activeRoutes.Add(rRej);
                gsS.fleet.Add(new Aircraft { registration = "B-2108", type = "A320neo", age = 1 });
                for (int i = gsS.stg.slots.Count - 1; i >= 0; i--)
                    if (gsS.stg.slots[i] != null && gsS.stg.slots[i].airport == "PEK"
                        && gsS.stg.slots[i].slotTime == "12:00") gsS.stg.slots.RemoveAt(i);
                int flightsD = rRej.flights.Count;
                int applyBeforeD = CountMails(gsS, InboxManager.ApplySlotAction);
                bool qD = InboxManager.QueueAssignFlight(gsS, rRej, "B-2108", "12:00", out string msgD);
                var selD = FindSlotSelectForRoute(gsS, rRej.routeId);
                bool rejD = InboxManager.RejectSlotSelect(gsS, selD, out string msgRejD);
                bool dOk = qD == false && rejD && selD != null && selD.status == "rejected"
                           && !InboxManager.RequiresDecision(selD)
                           && rRej.flights.Count == flightsD
                           && CountMails(gsS, InboxManager.ApplySlotAction) == applyBeforeD;
                Debug.Log($"R38 场景D 退回备选 → 呈报={qD}（期望 False）· 退回={rejD}（期望 True）"
                          + $"\n　★关键：不申请、不花钱、班次不动 {flightsD} → {rRej.flights.Count}（期望不变）="
                          + $"{CountMails(gsS, InboxManager.ApplySlotAction) == applyBeforeD}（期望 True：退回不会顺手开单）"
                          + $"\n　退回说明：{msgRejD}");

                bool scOk = aOk && bOk && cOk && dOk;
                Debug.Log($"R38 结论 → 场景A 缺槽出口是有选项的选择题={aOk}"
                          + $"　场景B 飞机硬门槛先于时刻槽闸门={bOk}"
                          + $"　场景C 交割后自动重排并呈报加机={cOk}"
                          + $"　场景D 退回备选无痕={dOk}"
                          + $"　→ {(scOk ? "全部符合期望" : "★有不符合期望的项，见上")}");
            }
            catch (Exception sc)
            {
                Debug.LogWarning($"时刻备选/自动重排用例异常（不阻断后续）：{sc.Message}");
            }
            finally
            {
                var straySc = GameObject.Find("__r38_slot_candidates_gs__");
                if (straySc != null) GameObject.DestroyImmediate(straySc);
            }

            EditorUtility.DisplayDialog("Logic Self Tests", "结果已逐行输出到 Console（Window → General → Console）", "确定");
        }

        /// <summary>R38：备选表里的时刻是否**全在 30 分系列网格上**（槽的身份必须是系列时刻）。</summary>
        static bool AllSeriesAligned(List<SlotCandidateLogic.SlotOption> opts)
        {
            if (opts == null || opts.Count == 0) return false;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i] == null) return false;
                if (RoutePermitLogic.SlotRequestClock(opts[i].depClock) != opts[i].depClock) return false;
                if (!string.IsNullOrEmpty(opts[i].inClock)
                    && RoutePermitLogic.SlotRequestClock(opts[i].inClock) != opts[i].inClock) return false;
            }
            return true;
        }

        /// <summary>R38：收件箱里属于某条航线的加机呈报（`origin` 存 routeId）。</summary>
        static InboxMail FindAssignMailForRoute(GameState gs, string routeId)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != InboxManager.AssignFlightAction) continue;
                if (string.Equals(m.origin, routeId, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        /// <summary>R38：收件箱里属于某条航线的**加班时刻备选件**（`slotRouteId` 存 routeId）。</summary>
        static InboxMail FindSlotSelectForRoute(GameState gs, string routeId)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != InboxManager.SlotSelectAction) continue;
                if (!string.IsNullOrEmpty(routeId)
                    && !string.Equals(m.slotRouteId, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (InboxManager.IsOpen(m)) return m;
            }
            return null;
        }

        /// <summary>R35：数一数缺口里某一类（kind）有几个（供机组闸门用例判「只拦新增波次」）。</summary>
        static int CountKind(List<OpsScheduleLogic.WaveShortage> shortages, string kind)
        {
            if (shortages == null) return 0;
            int n = 0;
            for (int i = 0; i < shortages.Count; i++)
                if (shortages[i] != null && shortages[i].kind == kind) n++;
            return n;
        }

        /// <summary>R29：取正文里第一行含指定片段的行（打印用，便于对照报障截图）。</summary>
        static string FirstLineWith(string body, string needle)
        {
            if (string.IsNullOrEmpty(body)) return "(空)";
            string[] lines = body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(needle)) return lines[i].Trim();
            return "(未找到含「" + needle + "」的行)";
        }

        // ———————————————————— R21 加机呈报的诊断行 ————————————————————

        /// <summary>R21：已占用（inUse）的时刻持仓条数。落班占槽的净效果靠它看。</summary>
        static int CountInUseSlots(GameState gs)
        {
            if (gs?.stg?.slots == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.stg.slots.Count; i++)
                if (gs.stg.slots[i] != null && gs.stg.slots[i].inUse) n++;
            return n;
        }

        /// <summary>R21：收件箱里未结案的加机呈报封数（同一条航线同时只该有一封）。</summary>
        static int CountOpenAssignMails(GameState gs)
        {
            if (gs?.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != InboxManager.AssignFlightAction) continue;
                if (InboxManager.IsOpen(m)) n++;
            }
            return n;
        }

        /// <summary>R21：航线现有班次的一行式清单。</summary>
        static string FlightsCsv(RouteData route)
        {
            if (route == null) return "（无航线）";
            route.EnsureFlights();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (sb.Length > 0) sb.Append("　|　");
                sb.Append($"{f.registration} {f.slotLabel} {f.depTime}");
            }
            return sb.Length > 0 ? sb.ToString() : "（无班次）";
        }

        /// <summary>R26：本线班次的机号清单（问句不得改机器的一行式证据）。</summary>
        static string RegistrationList(RouteData route)
        {
            if (route == null) return "（无航线）";
            route.EnsureFlights();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (sb.Length > 0) sb.Append('/');
                sb.Append(f.registration);
            }
            return sb.Length > 0 ? sb.ToString() : "（无班次）";
        }

        /// <summary>R26：子串出现次数（渲染后的重复词靠它数，重复一次就是报障）。</summary>
        static int CountOf(string s, string needle)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, at = 0;
            while (at < s.Length)
            {
                int i = s.IndexOf(needle, at, StringComparison.Ordinal);
                if (i < 0) break;
                n++;
                at = i + needle.Length;
            }
            return n;
        }

        /// <summary>R26：剥掉 TMP 富文本标签，只剩 CEO 眼睛看到的字（数重复词得按看到的字算）。</summary>
        static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// R26：从渲染文本里抽出第一条航线链接的键（<c>&lt;link=route:FRA-ATL&gt;</c> → <c>FRA-ATL</c>）。
        /// 深链用例要的是「**CEO 真去点**的那串键」，所以从 TermNavMap 渲染结果里取，而不是自己拼。
        /// </summary>
        static string FirstRouteLinkKey(string painted)
        {
            if (string.IsNullOrEmpty(painted)) return null;
            const string tag = "<link=route:";
            int i = painted.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0) return null;
            int s = i + tag.Length;
            int e = painted.IndexOf('>', s);
            return e > s ? painted.Substring(s, e - s) : null;
        }

        /// <summary>
        /// R21：该**去程时刻**两端里「排不了且池还有余量」的端数（= 期望开出的时刻槽申请单张数）。
        /// ★2026-10-15：按具体时刻取两端需求（`ClockNeeds`），不再按时段折算。
        /// </summary>
        static int ExpectedSlotMails(GameState gs, RouteData route, string depClock)
        {
            var needs = RoutePermitLogic.ClockNeeds(route, FlightProposalLogic.ClockOf(depClock));
            int n = 0;
            for (int i = 0; i < needs.Count; i++)
            {
                if (RoutePermitLogic.CanScheduleEnd(gs, route, needs[i], true)) continue;
                if (PoolHasNoRoom(gs, needs[i])) continue;
                n++;
            }
            return n;
        }

        /// <summary>R21：该去程时刻两端里「排不了且池已满」的端数（= 期望的受阻汇报件封数）。</summary>
        static int ExpectedBlockedReports(GameState gs, RouteData route, string depClock)
        {
            var needs = RoutePermitLogic.ClockNeeds(route, FlightProposalLogic.ClockOf(depClock));
            int n = 0;
            for (int i = 0; i < needs.Count; i++)
            {
                if (RoutePermitLogic.CanScheduleEnd(gs, route, needs[i], true)) continue;
                if (!PoolHasNoRoom(gs, needs[i])) continue;
                n++;
            }
            return n;
        }

        /// <summary>R21：该去程时刻两端的排班可行性一行式（诊断用：看清是哪一端排不了）。</summary>
        static string ShortEndsText(GameState gs, RouteData route, string depClock)
        {
            var needs = RoutePermitLogic.ClockNeeds(route, FlightProposalLogic.ClockOf(depClock));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < needs.Count; i++)
            {
                if (sb.Length > 0) sb.Append("；");
                sb.Append(needs[i].airport).Append(' ').Append(RoutePermitLogic.TimeOf(needs[i]))
                  .Append(RoutePermitLogic.CanScheduleEnd(gs, route, needs[i], true) ? "可排" : "排不了");
            }
            return sb.Length > 0 ? sb.ToString() : "两端皆非协调";
        }

        /// <summary>R21：该端连申请都无门（池已满且该时刻无空闲持仓）。</summary>
        static bool PoolHasNoRoom(GameState gs, RoutePermitLogic.SlotNeed need)
            => RoutePermitLogic.SlotPoolFull(gs, need.airport, need.band)
               && RoutePermitLogic.FreeSlotsAt(gs, need.airport, RoutePermitLogic.TimeOf(need)) <= 0;

        /// <summary>R21：在某机场某**时刻**补一条空闲持仓（缺则补，够则不重复加）。</summary>
        static void SeedSlotAt(GameState gs, string airport, string clock)
        {
            if (gs?.stg == null) return;
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
            if (RoutePermitLogic.FreeSlotsAt(gs, airport, clock) > 0) return;
            gs.stg.slots.Add(new SlotHolding { airport = airport, slotTime = clock, inUse = false });
        }

        /// <summary>
        /// R21：把某机场某波次的**每个时刻**都填满（模拟池已无余量、连申请都无门）。
        /// ★2026-10-15：由「填一条时段名持仓」改为「填满该波 30 分钟网格上的每个时刻」——
        /// 池满的判据是**时刻**缺槽（`FreeSlotsAt`），只填一条代表时刻已经拦不住别的时刻了。
        /// </summary>
        static void FillPool(GameState gs, string airport, int band)
        {
            if (gs?.stg == null) return;
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
            var grid = ScheduleSlots.SlotGridLabels(band);
            for (int guard = 0, k = 0; guard < 128 && RoutePermitLogic.SlotsAvailable(gs, airport, band) > 0; guard++, k++)
                gs.stg.slots.Add(new SlotHolding { airport = airport, slotTime = grid[k % grid.Count], inUse = true });
        }

        /// <summary>
        /// 两端各填**整张 30 分钟系列网格**的空闲持仓（模拟真实存档里「这条线两端各时刻的槽都在手」）。
        /// ★2026-10-15：由「每时段 2 条代表时刻」改为「网格全铺」—— 时刻槽精确到时刻之后，
        /// 一条 18:00 的持仓接不了 17:30 的回程班（差 30 分 > 容差 15 分），
        /// 夹具若还按旧时段口径只铺峰点，会把「槽不够」误当成「机组/运力不够」。
        /// </summary>
        static void SeedFullSeries(GameState gs, string airport)
        {
            if (gs?.stg == null) return;
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                var grid = ScheduleSlots.SlotGridLabels(b);
                for (int k = 0; k < grid.Count; k++)
                {
                    if (RoutePermitLogic.FreeSlotsAt(gs, airport, grid[k]) > 0) continue;
                    gs.stg.slots.Add(new SlotHolding { airport = airport, slotTime = grid[k], inUse = false });
                }
            }
        }

        /// <summary>R21：收件箱里符合条件（action 相同 / 主题含关键词）的邮件封数。</summary>
        static int CountMails(GameState gs, string action, string subjectContains = null)
        {
            if (gs?.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null) continue;
                if (!string.IsNullOrEmpty(action) && m.action != action) continue;
                if (!string.IsNullOrEmpty(subjectContains)
                    && (m.subject == null || m.subject.IndexOf(subjectContains, StringComparison.Ordinal) < 0)) continue;
                n++;
            }
            return n;
        }

        /// <summary>R42：从投递队列里取**最后一封** id 以指定前缀开头的邮件。
        /// 回执走 `InboxCadence.ReplyReady`（延迟 4 小时投递），当场查收件箱必然是空的；
        /// 又因为同一航线会先后发好几封回执，取「最后一封」才是本次批复的那封。</summary>
        static InboxMail LastPendingMail(GameState gs, string idPrefix)
        {
            if (gs?.pendingInbox == null) return null;
            for (int i = gs.pendingInbox.Count - 1; i >= 0; i--)
            {
                var m = gs.pendingInbox[i] != null ? gs.pendingInbox[i].mail : null;
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (m.id.StartsWith(idPrefix, StringComparison.Ordinal)) return m;
            }
            return null;
        }

        /// <summary>R24：已排入投递队列（`pendingInbox`）、尚未落进收件箱的邮件封数（按主题关键词）。</summary>
        static int CountPending(GameState gs, string subjectContains)
        {
            if (gs?.pendingInbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var m = gs.pendingInbox[i] != null ? gs.pendingInbox[i].mail : null;
                if (m == null) continue;
                if (!string.IsNullOrEmpty(subjectContains)
                    && (m.subject == null || m.subject.IndexOf(subjectContains, StringComparison.Ordinal) < 0)) continue;
                n++;
            }
            return n;
        }

        /// <summary>R21：按 action 取第一封符合条件的邮件。</summary>
        static InboxMail FindMailByAction(GameState gs, string action)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].action == action) return gs.inbox[i];
            return null;
        }

        /// <summary>R21：按主题关键词取第一封符合条件的邮件。</summary>
        static InboxMail FindMailBySubject(GameState gs, string keyword)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m?.subject != null && m.subject.IndexOf(keyword, StringComparison.Ordinal) >= 0) return m;
            }
            return null;
        }

        /// <summary>R17 诊断行：取决策日志里含指定关键词的最后一条（如「【停飞】」）。无则返回 null。</summary>
        static string GroundLogLine(GameState gs, string keyword)
        {
            if (gs?.decisionLog == null || string.IsNullOrEmpty(keyword)) return null;
            for (int i = gs.decisionLog.Count - 1; i >= 0; i--)
            {
                string line = gs.decisionLog[i];
                if (!string.IsNullOrEmpty(line) && line.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                    return line;
            }
            return null;
        }

        /// <summary>R16 诊断行：列出该航司的远程线（&gt; <see cref="EconomyBalanceParams.FreqBandMidKm"/>）配了什么机型。</summary>
        static string LongHaulAircraftCsv(AirlineProfile p)
        {
            if (p == null || p.routes == null) return "（无航线）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.routes.Count; i++)
            {
                var r = p.routes[i];
                if (r == null || r.distanceKm <= EconomyBalanceParams.FreqBandMidKm) continue;
                if (sb.Length > 0) sb.Append("　");
                sb.Append($"{r.origin}→{r.destination}({r.distanceKm:F0}km→{r.aircraftType})");
            }
            return sb.Length > 0 ? sb.ToString() : "（无远程线）";
        }
    }
}
#endif
