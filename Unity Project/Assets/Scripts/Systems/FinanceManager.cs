using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// FIN 财务部系统（Phase 2 Week 5）：年度预算 / 碳配额 / 卖资产与政府救助 / 破产重组。
    /// 银行·过桥·增资复用 LoanManager；燃油对冲复用 HedgeManager。
    /// </summary>
    public class FinanceManager : MonoBehaviour
    {
        public static FinanceManager Instance { get; private set; }

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region 预算

        /// <summary>若尚无预算，按默认部门份额 × 建议预算生成草案。</summary>
        public void EnsureDefaultBudget(GameState gs)
        {
            if (gs == null) return;
            if (gs.budgetItems == null)
                gs.budgetItems = new List<BudgetItem>();
            if (gs.budgetItems.Count > 0) return;

            float total = FinanceEconomics.RecommendedBudget(gs.cash);
            for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
            {
                var d = FinanceEconomics.DeptShares[i];
                gs.budgetItems.Add(new BudgetItem
                {
                    deptId = d.id,
                    deptName = d.name,
                    amountWan = total * d.share,
                    spentWan = 0f
                });
            }
        }

        public static float BudgetTotal(GameState gs)
        {
            if (gs?.budgetItems == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < gs.budgetItems.Count; i++)
                sum += gs.budgetItems[i].amountWan;
            return sum;
        }

        public static int EstimateVotes(GameState gs) =>
            FinanceEconomics.EstimateBoardVotes(BudgetTotal(gs), gs.cash, gs.boardSatisfaction);

        /// <summary>提交预算 → 董事会投票 → 通过则按额度执行。</summary>
        public bool SubmitBudget(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            EnsureDefaultBudget(gs);

            float total = BudgetTotal(gs);
            int votes = EstimateVotes(gs);
            bool ok = votes >= FinanceEconomics.BoardVotesRequired;

            gs.budgetSubmitted = true;
            gs.budgetFiscalYear = gs.year;
            gs.budgetApproved = ok;

            message = ok
                ? $"年度预算 {total:F0} 万经董事会 {votes}/{FinanceEconomics.BoardSeats} 票通过，各部门按额度执行。"
                : $"年度预算 {total:F0} 万仅得 {votes}/{FinanceEconomics.BoardSeats} 票，未通过。请调低总额或提高董事会满意度。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return ok;
        }

        public void SetBudgetAmount(GameState gs, string deptId, float amountWan)
        {
            if (gs?.budgetItems == null) return;
            for (int i = 0; i < gs.budgetItems.Count; i++)
            {
                if (gs.budgetItems[i].deptId == deptId)
                {
                    gs.budgetItems[i].amountWan = Mathf.Max(0f, amountWan);
                    return;
                }
            }
        }

        #endregion

        #region 预算 · 超支问责（GDB 28.1 见习期软保护）

        /// <summary>
        /// 季度结算时把季度支出按部门默认份额分摊累计到各预算项 spentWan，
        /// 作为财年末「超支问责」的判定依据。
        /// </summary>
        public static void AccrueBudgetSpend(GameState gs, float quarterExpenseWan)
        {
            if (gs?.budgetItems == null || quarterExpenseWan <= 0f) return;
            for (int i = 0; i < gs.budgetItems.Count; i++)
            {
                var b = gs.budgetItems[i];
                float share = FinanceEconomics.ShareOf(b.deptId);
                if (share <= 0f) continue;
                b.spentWan += quarterExpenseWan * share;
            }
        }

        /// <summary>
        /// 财年末结算：判定各部门是否超支（spentWan > 额度 amountWan），
        /// 超支从现金池调拨并触发董事会问责（满意度惩罚，第 2 财年减半）。
        /// </summary>
        public static string SettleBudgetOverrun(GameState gs)
        {
            if (gs?.budgetItems == null || gs.budgetItems.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            int overrunCount = 0;
            for (int i = 0; i < gs.budgetItems.Count; i++)
            {
                var b = gs.budgetItems[i];
                if (b.spentWan > b.amountWan)
                {
                    overrunCount++;
                    sb.AppendLine($"  · {b.deptName}：超支 {(b.spentWan - b.amountWan):F0} 万");
                }
                b.spentWan = 0f; // 财年末决算：清空当年执行数，新年重新累计
            }

            if (overrunCount == 0)
            {
                // 预算执行达标，无问责
                return "";
            }

            float penalty = ProgressionGate.OverrunPenalty(gs.year);
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + penalty, 0f, 100f);
            string tip = gs.year == 2
                ? $"董事会问责满意度 {penalty:F0}（见习期软保护：超支惩罚减半）"
                : $"董事会问责满意度 {penalty:F0}";
            string summary = $"本财年 {overrunCount} 个部门预算超支，{tip}。";
            sb.Insert(0, summary + "\n");
            gs.AddDecisionLog("财务：" + summary);
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region 碳配额

        public float RefreshEmissions(GameState gs)
        {
            if (gs == null) return 0f;
            gs.carbonEmissionsTons = FinanceEconomics.EstimateAnnualEmissionsTons(gs.activeRoutes?.Count ?? 0);
            return gs.carbonEmissionsTons;
        }

        public static float CarbonLiability(GameState gs)
        {
            if (gs == null) return 0f;
            return FinanceEconomics.CarbonLiabilityWan(
                gs.carbonEmissionsTons, gs.carbonPurchasedQuotaTons, gs.carbonPricePerTon);
        }

        public bool PurchaseCarbon(float tons, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (tons <= 0f) { message = "购买量需大于 0。"; return false; }

            float costWan = tons * gs.carbonPricePerTon / 10000f;
            if (gs.cash + 0.01f < costWan)
            {
                message = $"现金不足以购买配额（需 {costWan:F1} 万）。";
                return false;
            }

            gs.cash -= costWan;
            gs.carbonPurchasedQuotaTons += tons;
            gs.yearNetProfit -= costWan;
            message = $"已购买 {tons:F0} 吨碳配额，支出 {costWan:F1} 万。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 卖资产 / 政府救助

        public bool ExecuteSellAsset(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }

            var input = BuildInput(gs);
            var term = FinanceEconomics.BuildFinancingTerm(FinancingKind.SellAsset, input);
            gs.cash += term.amountWan;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 5f, 0f, 100f);
            gs.brandAwareness = Mathf.Clamp(gs.brandAwareness - 4f, 0f, 100f);
            LoanManager.ClearCrisisBurnIfRecovered(gs);

            message = $"出售资产回笼 {term.amountWan:F0} 万（急售折价）。品牌认知 −4 → {gs.brandAwareness:F0}，董事会满意度 −5。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool ExecuteGovBailout(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }

            var input = BuildInput(gs);
            if (!FinanceEconomics.IsBailoutUnlocked(input.economicPhase))
            {
                message = "政府救助仅在经济萧条期可用。";
                return false;
            }

            var term = FinanceEconomics.BuildFinancingTerm(FinancingKind.GovBailout, input);
            gs.cash += term.amountWan;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 3f, 0f, 100f);
            LoanManager.ClearCrisisBurnIfRecovered(gs);

            message = $"政府救助 {term.amountWan:F0} 万到账（政府持股 + 航线义务）。董事会满意度 −3。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 破产重组

        public bool FileRestructuring(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (gs.restructuring == null)
                gs.restructuring = new RestructuringState();

            var impact = FinanceEconomics.EstimateRestructuring(
                gs.fleet?.Count ?? 0, gs.activeRoutes?.Count ?? 0, gs.cash, gs.EffectiveMonthlyBurn);

            gs.cash += impact.cashRecoveredWan;
            gs.brandAwareness = Mathf.Clamp(gs.brandAwareness - impact.brandLoss, 0f, 100f);
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 10f, 0f, 100f);

            gs.restructuring.filed = true;
            gs.restructuring.inProgress = true;
            gs.restructuring.protectionMonthsLeft = 4;
            gs.restructuring.cashRecoveredWan = impact.cashRecoveredWan;
            gs.restructuring.routesCutPercent = impact.routesCutPercent;
            gs.restructuring.fleetCutPercent = impact.fleetCutPercent;
            gs.restructuring.brandLoss = impact.brandLoss;
            LoanManager.ClearCrisisBurnIfRecovered(gs);

            float runway = FinanceEconomics.RunwayAfterRestructuring(gs.cash, 0f, gs.EffectiveMonthlyBurn);
            message =
                $"已进入 Chapter 11 破产保护：回笼 {impact.cashRecoveredWan:F0} 万，品牌 −{impact.brandLoss:F0}，董事会满意度 −10。" +
                $"现金保护期 4 个月，重组后跑道约 {runway:F1} 月。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 财务细分 · 季度心跳（GDB 第15章 · P2 长线运营）

        /// <summary>每季财务细分结算：补贴入账 + 燃油预警 + PDP 里程碑 + 保险保费。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver) return "";
            return ConcatLines(
                ApplyQuarterlySubsidies(gs),
                ConcatLines(
                    FuelQuarterTick(gs),
                    ConcatLines(
                        ProcessPdpMilestones(gs),
                        ChargeQuarterlyInsurance(gs))));
        }

        /// <summary>财年末财务细分结算：企业所得税 + 碳配额（EU ETS/CORSIA）+ 补贴年限递减。</summary>
        public string SettleAnnualFinance(GameState gs, int finishedYear, float yearProfitWan)
        {
            if (gs == null) return "";
            string tax = SettleAnnualTax(gs, yearProfitWan);
            string carbon = SettleAnnualCarbon(gs, finishedYear);
            string subsidy = DecrementSubsidyYears(gs);
            return ConcatLines(tax, ConcatLines(carbon, subsidy));
        }

        #region 税务与补贴（GDB 15.4 / 15.10）

        /// <summary>年结企业所得税：按枢纽国税率对税前净利征税，亏损不征。</summary>
        public string SettleAnnualTax(GameState gs, float yearProfitWan)
        {
            if (gs == null) return "";
            float rate = TaxEconomics.CorporateTaxRateForHub(gs.hubIata);
            float tax = TaxEconomics.CorporateTaxWan(yearProfitWan, rate);
            gs.cash -= tax;
            gs.yearTaxPaidWan = tax;
            gs.cumulativeTaxPaidWan += tax;
            if (tax <= 0f && yearProfitWan <= 0f)
            {
                gs.AddDecisionLog($"财务：本财年税前亏损 {yearProfitWan:F0} 万，无需缴纳企业所得税。");
                return $"本财年亏损 {yearProfitWan:F0} 万，免征企业所得税。";
            }
            string msg = $"本财年企业所得税 {tax:F0} 万（{rate * 100f:F0}% · 枢纽 {gs.hubIata}）。";
            gs.AddDecisionLog("财务：" + msg);
            return msg;
        }

        /// <summary>新增航线补贴记录（GDB 15.4：战略部申请后由财务部入账）。</summary>
        public bool AddRouteSubsidy(string routeId, float annualWan, int years, string note, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (string.IsNullOrEmpty(routeId)) { message = "缺少航线 ID。"; return false; }
            gs.routeSubsidies.Add(new RouteSubsidy
            {
                routeId = routeId,
                annualAmountWan = Mathf.Max(0f, annualWan),
                yearsRemaining = Mathf.Max(1, years),
                note = note
            });
            message = $"已为航线 {routeId} 登记补贴 {annualWan:F0} 万/年 × {years} 年。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>季度补贴入账（annual/4）。</summary>
        public string ApplyQuarterlySubsidies(GameState gs)
        {
            if (gs == null || gs.routeSubsidies == null || gs.routeSubsidies.Count == 0) return "";
            float total = 0f;
            for (int i = 0; i < gs.routeSubsidies.Count; i++)
            {
                var s = gs.routeSubsidies[i];
                if (s == null || s.yearsRemaining <= 0) continue;
                total += s.annualAmountWan / 4f;
            }
            if (total <= 0f) return "";
            gs.cash += total;
            gs.yearNetProfit += total;
            return $"航线补贴入账 +{total:F0} 万（本季）。";
        }

        /// <summary>补贴年限递减（年结）。</summary>
        string DecrementSubsidyYears(GameState gs)
        {
            if (gs?.routeSubsidies == null) return "";
            int expired = 0;
            for (int i = 0; i < gs.routeSubsidies.Count; i++)
            {
                var s = gs.routeSubsidies[i];
                if (s == null) continue;
                if (s.yearsRemaining > 0) s.yearsRemaining--;
                if (s.yearsRemaining <= 0) expired++;
            }
            if (expired > 0)
                gs.routeSubsidies.RemoveAll(s => s == null || s.yearsRemaining <= 0);
            return expired > 0 ? $"{expired} 条航线补贴到期。" : "";
        }

        /// <summary>补贴依赖报告（离开补贴即亏损的航线）。</summary>
        public string SubsidyDependencyReport(GameState gs)
        {
            if (gs?.routeSubsidies == null || gs.routeSubsidies.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gs.routeSubsidies.Count; i++)
            {
                var s = gs.routeSubsidies[i];
                if (s == null) continue;
                float routeProfit = RouteProfitWan(gs, s.routeId);
                if (TaxEconomics.IsSubsidyDependent(routeProfit, s.annualAmountWan))
                    sb.AppendLine($"⚠ 补贴依赖：航线 {s.routeId} 剔除补贴后亏损（净利 {routeProfit:F0} 万 < 补贴 {s.annualAmountWan:F0} 万）。");
            }
            return sb.ToString().TrimEnd();
        }

        float RouteProfitWan(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null) return 0f;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && string.Equals(r.routeId, routeId, System.StringComparison.OrdinalIgnoreCase))
                    return r.dailyProfit * 365f;
            }
            return 0f;
        }

        #endregion

        #region PDP 订单簿（GDB 15.6）

        /// <summary>下达飞机订单：扣定金 15%，生成 2~5 年交付时间轴。</summary>
        public bool PlaceAircraftOrder(string typeId, int count, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var catalog = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(typeId) : null;
            if (catalog == null) { message = $"未知机型：{typeId}"; return false; }
            if (count <= 0) { message = "数量至少 1 架。"; return false; }

            float total = catalog.priceWan * count;
            float deposit = PdpEconomics.SigningDepositWan(total);
            if (gs.cash + 0.01f < deposit)
            {
                message = $"定金不足：需 {deposit:F0} 万（15%），当前 {gs.cash:F0} 万。";
                return false;
            }

            gs.cash -= deposit;
            int quarters = PdpEconomics.DeliveryQuarters(typeId);
            int orderIndex = (gs.year - 1) * 4 + (gs.quarter - 1) + quarters;
            var order = new AircraftOrder
            {
                orderId = $"PO-{gs.year}-{gs.aircraftOrders.Count + 1}",
                typeId = typeId,
                count = count,
                unitPriceWan = catalog.priceWan,
                totalWan = total,
                orderYear = gs.year,
                orderQuarter = gs.quarter,
                deliveryYear = orderIndex / 4 + 1,
                deliveryQuarter = orderIndex % 4 + 1,
                deliveryQuartersTotal = quarters,
                depositPaidWan = deposit
            };
            gs.aircraftOrders.Add(order);
            RefreshPdpPayable(gs);

            message = $"订单 {order.orderId} 已下：{typeId} ×{count}，总价 {total:F0} 万，" +
                      $"定金 {deposit:F0} 万（15%）。预计第 {order.deliveryYear} 年 Q{order.deliveryQuarter} 交付。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>季度 PDP 里程碑：交付前 24/12 月预付款 + 到期交付。</summary>
        public string ProcessPdpMilestones(GameState gs)
        {
            if (gs?.aircraftOrders == null || gs.aircraftOrders.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            var delivered = new List<AircraftOrder>();
            for (int i = 0; i < gs.aircraftOrders.Count; i++)
            {
                var o = gs.aircraftOrders[i];
                if (o == null || o.delivered || o.cancelled) continue;
                int q = QuartersUntilDelivery(o, gs.year, gs.quarter);
                string msg = "";
                if (q <= 0)
                {
                    msg = DeliverOrder(gs, o);
                    delivered.Add(o);
                }
                else if (q <= 8 && q < o.deliveryQuartersTotal && o.pdp24PaidWan <= 0.001f)
                {
                    float amt = PdpEconomics.Pdp24Wan(o.totalWan);
                    gs.cash -= amt;
                    o.pdp24PaidWan = amt;
                    msg = $"PDP 预付款：{o.orderId}（{o.typeId}）交付前 24 月，支付 {amt:F0} 万（10%）。";
                }
                else if (q <= 4 && o.pdp12PaidWan <= 0.001f)
                {
                    float amt = PdpEconomics.Pdp12Wan(o.totalWan);
                    gs.cash -= amt;
                    o.pdp12PaidWan = amt;
                    msg = $"PDP 预付款：{o.orderId}（{o.typeId}）交付前 12 月，支付 {amt:F0} 万（15%）。";
                }
                if (!string.IsNullOrEmpty(msg))
                {
                    gs.AddDecisionLog("财务：" + msg);
                    sb.AppendLine(msg);
                }
            }
            for (int i = 0; i < delivered.Count; i++)
                gs.aircraftOrders.Remove(delivered[i]);
            if (delivered.Count > 0) RefreshPdpPayable(gs);
            return sb.ToString().TrimEnd();
        }

        string DeliverOrder(GameState gs, AircraftOrder o)
        {
            float balance = PdpEconomics.DeliveryBalanceWan(o.totalWan);
            gs.cash -= balance;
            o.balancePaidWan = balance;
            o.delivered = true;
            FleetManager.DeliverAircraft(gs, o.typeId, o.count, "自有（PDP 交付）", o.unitPriceWan);
            return $"订单 {o.orderId}（{o.typeId} ×{o.count}）交付，尾款 {balance:F0} 万（60%），已入列。";
        }

        /// <summary>取消订单：支付 20%~30% 违约金（已付部分不退），OEM 关系降级。</summary>
        public bool CancelOrder(string orderId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            AircraftOrder o = null;
            for (int i = 0; i < gs.aircraftOrders.Count; i++)
            {
                if (gs.aircraftOrders[i] != null && gs.aircraftOrders[i].orderId == orderId)
                {
                    o = gs.aircraftOrders[i];
                    break;
                }
            }
            if (o == null || o.delivered) { message = $"未找到可取消订单：{orderId}"; return false; }

            float paid = o.depositPaidWan + o.pdp24PaidWan + o.pdp12PaidWan + o.balancePaidWan;
            float paidRatio = o.totalWan > 0f ? paid / o.totalWan : 0f;
            float penalty = PdpEconomics.CancellationPenaltyWan(o.totalWan, paidRatio);
            float extra = Mathf.Max(0f, penalty - paid);
            gs.cash -= extra;
            o.cancelled = true;
            DecrementOemRelation(gs, o.typeId);

            message = $"订单 {o.orderId} 已取消：违约金 {penalty:F0} 万（{PdpEconomics.CancellationPenaltyRate(paidRatio) * 100f:F0}%），" +
                      $"已付 {paid:F0} 万不退，额外扣 {extra:F0} 万。OEM 关系降级。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        void DecrementOemRelation(GameState gs, string typeId)
        {
            if (gs?.mnt?.oems == null) return;
            string oem = OemNameFor(typeId);
            for (int i = 0; i < gs.mnt.oems.Count; i++)
            {
                var r = gs.mnt.oems[i];
                if (r != null && string.Equals(r.oem, oem, System.StringComparison.OrdinalIgnoreCase))
                    r.stars = Mathf.Max(1, r.stars - 1);
            }
        }

        string OemNameFor(string typeId)
        {
            string t = (typeId ?? "").ToUpperInvariant();
            if (t.StartsWith("B")) return "波音";
            if (t.StartsWith("A")) return "空客";
            return "巴航";
        }

        int QuartersUntilDelivery(AircraftOrder o, int year, int quarter)
        {
            int now = (year - 1) * 4 + (quarter - 1);
            int delivery = (o.deliveryYear - 1) * 4 + (o.deliveryQuarter - 1);
            return delivery - now;
        }

        void RefreshPdpPayable(GameState gs)
        {
            float pending = 0f;
            for (int i = 0; i < gs.aircraftOrders.Count; i++)
            {
                var o = gs.aircraftOrders[i];
                if (o == null || o.delivered || o.cancelled) continue;
                float paid = o.depositPaidWan + o.pdp24PaidWan + o.pdp12PaidWan + o.balancePaidWan;
                pending += Mathf.Max(0f, o.totalWan - paid);
            }
            gs.pdpPayableWan = pending;
        }

        #endregion

        #region 航空保险（GDB 15.11）

        /// <summary>季度保费 = 机队总价值 × 年费率 / 4。</summary>
        public string ChargeQuarterlyInsurance(GameState gs)
        {
            if (gs == null) return "";
            if (gs.insuranceCoverage == InsuranceCoverage.SelfInsured) return "";
            float fleetValue = FleetManager.FleetTotalValue(gs);
            float premium = InsuranceEconomics.QuarterlyPremiumWan(fleetValue, gs.safetyRating, gs.insuranceCoverage);
            if (premium <= 0f) return "";
            gs.cash -= premium;
            gs.cumulativeInsurancePremiumWan += premium;
            gs.yearNetProfit -= premium;
            return $"航空保险保费 {premium:F0} 万（本季 · {CoverageLabel(gs.insuranceCoverage)}）。";
        }

        /// <summary>覆盖档位中文名。</summary>
        public static string CoverageLabel(InsuranceCoverage coverage) => coverage switch
        {
            InsuranceCoverage.Full => "全险",
            InsuranceCoverage.Basic => "基本险",
            _ => "自保"
        };

        /// <summary>理赔：按覆盖档位与严重度赔付，入现金。</summary>
        public float ProcessInsuranceClaim(float lossWan, InsuranceEconomics.LossSeverity severity)
        {
            var gs = GameState.Instance;
            if (gs == null) return 0f;
            float payout = InsuranceEconomics.ClaimPayoutWan(lossWan, severity, gs.insuranceCoverage);
            gs.cash += payout;
            gs.cumulativeInsuranceClaimWan += payout;
            return payout;
        }

        #endregion

        #region 燃油自动采购（GDB 15.12）

        /// <summary>季度：库存递减 + 预警检查，返回预警文案。</summary>
        public string FuelQuarterTick(GameState gs)
        {
            if (gs?.fuelProcurement == null) return "";
            var fp = gs.fuelProcurement;
            // 季度消耗约 30 天安全缓冲
            fp.inventoryDays = Mathf.Max(0f, fp.inventoryDays - 30f);

            float avg = FuelProcurementEconomics.RecentAveragePrice(gs.fuelPriceHistory);
            fp.lastAvgPrice = avg;

            var warnings = FuelProcurementEconomics.CheckWarnings(
                gs.fuelPricePerTon, avg, fp.inventoryDays, GeopoliticalRisk(gs), IsSeasonPeak(gs));
            if (warnings.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < warnings.Count; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(warnings[i].text);
            }
            return "燃油预警：" + sb;
        }

        /// <summary>手动采购燃油至目标库存天数。</summary>
        public bool ProcureFuel(float targetDays, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (targetDays <= 0f) { message = "目标库存天数需大于 0。"; return false; }

            var fp = gs.fuelProcurement;
            float addDays = Mathf.Max(0f, targetDays - fp.inventoryDays);
            float dailyTons = FinanceEconomics.EstimateMonthlyFuelTons(gs.activeRoutes?.Count ?? 0) / 30f;
            float tons = addDays * dailyTons;
            float costWan = tons * gs.fuelPricePerTon / 10000f;

            bool emergency = fp.inventoryDays <= FuelProcurementEconomics.EmergencyInventoryDays;
            float unitPrice = emergency ? FuelProcurementEconomics.EmergencyPrice(gs.fuelPricePerTon) : gs.fuelPricePerTon;
            costWan = tons * unitPrice / 10000f;

            if (gs.cash + 0.01f < costWan)
            {
                message = $"现金不足以采购（需 {costWan:F0} 万）。";
                return false;
            }

            gs.cash -= costWan;
            fp.inventoryDays = targetDays;
            gs.yearNetProfit -= costWan;
            message = $"已采购燃油至 {targetDays:F0} 天库存，支出 {costWan:F0} 万" +
                      (emergency ? "（紧急溢价采购）" : "") + "。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        float GeopoliticalRisk(GameState gs)
        {
            // 简化：以宏观阶段 + 随机事件近似；衰退/萧条期地缘风险略升。
            return string.Equals(gs.economicPhase, "recession", System.StringComparison.OrdinalIgnoreCase) ? 0.5f : 0.2f;
        }

        bool IsSeasonPeak(GameState gs)
        {
            // 简化：Q3（暑运）与 Q1（春节）视为旺季。
            return gs.quarter == 3 || gs.quarter == 1;
        }

        #endregion

        #region 碳配额 EU ETS + CORSIA（GDB 15.13）

        /// <summary>年结碳合规：按航线归类 EU ETS / CORSIA，核算配额成本并扣除未覆盖负债。</summary>
        public string SettleAnnualCarbon(GameState gs, int finishedYear)
        {
            if (gs == null) return "";
            int calYear = CarbonEconomics.CalendarYear(finishedYear);
            var cc = gs.carbonCompliance ?? new CarbonComplianceState();
            gs.carbonCompliance = cc;

            float etsEmissions = 0f, corsiaEmissions = 0f;
            if (gs.activeRoutes != null)
            {
                for (int r = 0; r < gs.activeRoutes.Count; r++)
                {
                    var route = gs.activeRoutes[r];
                    if (route == null) continue;
                    var regime = CarbonEconomics.ClassifyRoute(route.origin, route.destination);
                    if (regime == CarbonEconomics.CarbonRegime.None) continue;
                    float em = RouteEmissions(gs, route);
                    if (regime == CarbonEconomics.CarbonRegime.Ets) etsEmissions += em;
                    else corsiaEmissions += em;
                }
            }

            cc.etsEmissionsTons = etsEmissions;
            cc.corsiaEmissionsTons = corsiaEmissions;
            cc.etsRequiredTons = CarbonEconomics.EtsRequiredTons(etsEmissions, calYear);
            cc.corsiaRequiredTons = CarbonEconomics.CorsiaRequiredTons(corsiaEmissions, calYear);
            cc.etsCostWan = CarbonEconomics.QuotaCostWan(cc.etsRequiredTons, gs.carbonPricePerTon);
            cc.corsiaCostWan = CarbonEconomics.QuotaCostWan(cc.corsiaRequiredTons, gs.carbonPricePerTon);

            float required = cc.etsRequiredTons + cc.corsiaRequiredTons;
            float covered = Mathf.Min(gs.carbonPurchasedQuotaTons, required);
            float uncovered = Mathf.Max(0f, required - covered);
            float costWan = CarbonEconomics.QuotaCostWan(uncovered, gs.carbonPricePerTon);
            gs.cash -= costWan;
            gs.carbonPurchasedQuotaTons = Mathf.Max(0f, gs.carbonPurchasedQuotaTons - covered);

            string freeNote = calYear <= 2024 ? "（免费 75%）" : calYear == 2025 ? "（免费 50%）" : "（免费 0%）";
            string corsiaNote = CarbonEconomics.CorsiaMandatory(calYear) ? "强制期" : "自愿期";
            string msg =
                $"碳合规（{calYear}）：EU ETS {cc.etsRequiredTons:F0} 吨{freeNote} · CORSIA {cc.corsiaRequiredTons:F0} 吨（{corsiaNote}）。" +
                $"未覆盖配额 {uncovered:F0} 吨，补缴 {costWan:F0} 万。";
            gs.AddDecisionLog("财务：" + msg);
            return msg;
        }

        float RouteEmissions(GameState gs, RouteData route)
        {
            if (route == null) return 0f;
            route.EnsureFlights();
            float total = 0f;
            for (int f = 0; f < route.flights.Count; f++)
            {
                var flight = route.flights[f];
                if (flight == null) continue;
                string acType = AircraftTypeFor(gs, flight.registration, route.aircraftType);
                total += CarbonEconomics.RouteAnnualEmissionsTons(route.distanceKm, acType, flight.weeklyFlights);
            }
            if (route.flights.Count == 0)
                total += CarbonEconomics.RouteAnnualEmissionsTons(route.distanceKm, route.aircraftType, route.weeklyFlights);
            return total;
        }

        string AircraftTypeFor(GameState gs, string registration, string fallback)
        {
            if (gs?.fleet != null && !string.IsNullOrEmpty(registration))
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a != null && string.Equals(a.registration, registration, System.StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrEmpty(a.type) ? fallback : a.type;
                }
            }
            return fallback;
        }

        #endregion

        #endregion

        #region Helpers

        static string ConcatLines(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + "\n" + b;
        }

        static FinanceEconomics.FinancingInput BuildInput(GameState gs)
        {
            return new FinanceEconomics.FinancingInput
            {
                cash = gs.cash,
                monthlyBurn = gs.EffectiveMonthlyBurn,
                rating = LoanManager.GetCreditRating(gs),
                economicPhase = gs.economicPhase,
                boardSatisfaction = gs.boardSatisfaction,
                fleetCount = gs.fleet?.Count ?? 0
            };
        }

        #endregion

        #region Save helpers

        public string BudgetToJson()
        {
            var gs = GameState.Instance;
            var blob = new BudgetListSave
            {
                items = gs?.budgetItems != null ? gs.budgetItems.ToArray() : System.Array.Empty<BudgetItem>()
            };
            return JsonUtility.ToJson(blob);
        }

        public void BudgetFromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.budgetItems = new List<BudgetItem>();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<BudgetListSave>(json);
                if (blob?.items != null)
                    gs.budgetItems = new List<BudgetItem>(blob.items);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FinanceManager] 预算读档失败: " + e.Message);
            }
        }

        public string CarbonToJson()
        {
            var gs = GameState.Instance;
            var blob = new CarbonSave
            {
                emissionsTons = gs?.carbonEmissionsTons ?? 0f,
                purchasedQuotaTons = gs?.carbonPurchasedQuotaTons ?? 0f,
                carbonPricePerTon = gs?.carbonPricePerTon ?? FinanceEconomics.CarbonPriceUsdPerTon
            };
            return JsonUtility.ToJson(blob);
        }

        public void CarbonFromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<CarbonSave>(json);
                if (blob != null)
                {
                    gs.carbonEmissionsTons = blob.emissionsTons;
                    gs.carbonPurchasedQuotaTons = blob.purchasedQuotaTons;
                    gs.carbonPricePerTon = blob.carbonPricePerTon > 0f
                        ? blob.carbonPricePerTon : FinanceEconomics.CarbonPriceUsdPerTon;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FinanceManager] 碳配额读档失败: " + e.Message);
            }
        }

        public string RestructuringToJson()
        {
            var gs = GameState.Instance;
            var r = gs?.restructuring ?? new RestructuringState();
            var blob = new RestructuringSave
            {
                filed = r.filed,
                inProgress = r.inProgress,
                protectionMonthsLeft = r.protectionMonthsLeft,
                confirmedConditions = r.confirmedConditions,
                cashRecoveredWan = r.cashRecoveredWan,
                routesCutPercent = r.routesCutPercent,
                fleetCutPercent = r.fleetCutPercent,
                brandLoss = r.brandLoss
            };
            return JsonUtility.ToJson(blob);
        }

        public void RestructuringFromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.restructuring = new RestructuringState();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<RestructuringSave>(json);
                if (blob != null)
                {
                    gs.restructuring.filed = blob.filed;
                    gs.restructuring.inProgress = blob.inProgress;
                    gs.restructuring.protectionMonthsLeft = blob.protectionMonthsLeft;
                    gs.restructuring.confirmedConditions = blob.confirmedConditions;
                    gs.restructuring.cashRecoveredWan = blob.cashRecoveredWan;
                    gs.restructuring.routesCutPercent = blob.routesCutPercent;
                    gs.restructuring.fleetCutPercent = blob.fleetCutPercent;
                    gs.restructuring.brandLoss = blob.brandLoss;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FinanceManager] 重组读档失败: " + e.Message);
            }
        }

        #endregion
    }
}
