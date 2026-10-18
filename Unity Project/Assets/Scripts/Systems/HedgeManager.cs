using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>燃油对冲：固定价 / 区间价 / 看涨期权 + 季度结算（GDD 7.4 · Phase 9）。</summary>
    public class HedgeManager : MonoBehaviour
    {
        public static HedgeManager Instance { get; private set; }

        public static float EstimateMonthlyFuelTons(GameState gs)
        {
            int routes = gs != null && gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            return 400f + routes * 120f;
        }

        public static float PremiumForTerm(int termMonths)
        {
            return termMonths switch
            {
                3 => 0.03f,
                6 => 0.05f,
                12 => 0.08f,
                _ => 0.05f
            };
        }

        public static float CalcLockPrice(float spot, int termMonths)
        {
            return spot * (1f + PremiumForTerm(termMonths));
        }

        /// <summary>看涨期权权利金（美元/吨）= 市价 × 0.5% × 月数。</summary>
        public static float CallPremiumPerTon(float spot, int termMonths)
        {
            return spot * 0.005f * termMonths;
        }

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

        #region Open Hedge

        /// <param name="lockType">fixed / collar / call</param>
        /// <param name="floorOffset">collar：下限偏离，如 0.05 表示 −5%</param>
        /// <param name="capOffset">collar：上限偏离，如 0.05 表示 +5%</param>
        public bool TryOpenHedge(
            float ratio, int termMonths, string lockType,
            float floorOffset, float capOffset, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                message = "游戏状态未就绪。";
                return false;
            }

            if (ratio <= 0.001f)
            {
                message = "对冲比例为 0%，无需建仓。";
                return false;
            }

            if (ratio > 0.8f + 0.001f)
            {
                message = "对冲比例上限为 80%。";
                return false;
            }

            if (termMonths != 3 && termMonths != 6 && termMonths != 12)
            {
                message = "期限仅支持 3 / 6 / 12 个月。";
                return false;
            }

            string type = NormalizeLockType(lockType);
            if (gs.hedgeContracts == null)
                gs.hedgeContracts = new List<HedgeContract>();
            if (gs.hedgeContracts.Count >= 3)
            {
                message = "对冲合约已满（最多 3 笔），请等待到期。";
                return false;
            }

            float spot = gs.fuelPricePerTon;
            float monthlyTons = EstimateMonthlyFuelTons(gs);
            float qty = monthlyTons * ratio * termMonths;
            float lockPx = CalcLockPrice(spot, termMonths);
            float termPrem = PremiumForTerm(termMonths);

            var c = new HedgeContract
            {
                lockType = type,
                lockPrice = lockPx,
                quantity = qty,
                hedgeRatio = ratio,
                termMonths = termMonths,
                monthsRemaining = termMonths,
                createdAt = gs.DateLabel,
                premiumRate = termPrem,
                floorPrice = 0f,
                capPrice = 0f,
                premiumPaid = 0f,
                floorOffset = floorOffset,
                capOffset = capOffset
            };

            if (type == HedgeLockTypes.Collar)
            {
                float fOff = Mathf.Clamp(floorOffset, 0.05f, 0.10f);
                float cOff = Mathf.Clamp(capOffset, 0.05f, 0.10f);
                c.floorOffset = fOff;
                c.capOffset = cOff;
                c.floorPrice = spot * (1f - fOff);
                c.capPrice = spot * (1f + cOff);
                c.lockPrice = spot; // 中枢参考
                c.premiumRate = 0f;
            }
            else if (type == HedgeLockTypes.Call)
            {
                float perTon = CallPremiumPerTon(spot, termMonths);
                float premiumWan = perTon * qty / 10000f;
                if (gs.cash + 0.01f < premiumWan)
                {
                    message = $"现金不足以支付权利金（需 {premiumWan:F1} 万）。";
                    return false;
                }

                c.premiumPaid = premiumWan;
                gs.cash -= premiumWan;
                gs.yearNetProfit -= premiumWan;
            }

            gs.hedgeContracts.Add(c);
            message = BuildOpenMessage(c, spot);
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>兼容旧调用：默认固定价。</summary>
        public bool TryOpenHedge(float ratio, int termMonths, out string message)
        {
            return TryOpenHedge(ratio, termMonths, HedgeLockTypes.Fixed, 0.05f, 0.05f, out message);
        }

        private static string NormalizeLockType(string t)
        {
            if (string.IsNullOrEmpty(t)) return HedgeLockTypes.Fixed;
            t = t.Trim().ToLowerInvariant();
            if (t == HedgeLockTypes.Collar || t == "区间" || t == "区间价") return HedgeLockTypes.Collar;
            if (t == HedgeLockTypes.Call || t == "期权" || t == "看涨") return HedgeLockTypes.Call;
            return HedgeLockTypes.Fixed;
        }

        private static string BuildOpenMessage(HedgeContract c, float spot)
        {
            string head =
                $"已锁定燃油对冲【{HedgeLockTypes.DisplayName(c.lockType)}】：" +
                $"比例 {c.hedgeRatio * 100:F0}% · {c.termMonths} 个月 · 总量 {c.quantity:F0} 吨。";
            return c.lockType switch
            {
                HedgeLockTypes.Collar =>
                    head + $" 区间 [${c.floorPrice:F1}, ${c.capPrice:F1}]（零额外溢价）。",
                HedgeLockTypes.Call =>
                    head + $" 行权价 ${c.lockPrice:F1}/吨 · 权利金已扣 {c.premiumPaid:F2} 万。",
                _ =>
                    head + $" 锁定价 ${c.lockPrice:F1}/吨（溢价 {c.premiumRate * 100:F0}%）。"
            };
        }

        #endregion

        #region Quarter Settle

        /// <summary>
        /// 航司买方逻辑：损益（美元）≈ 量 × (市价 − 有效结算价)。
        /// 市价高于保护价 → 现金盈利（抵消高油价成本）。
        /// </summary>
        public string SettleQuarter(float quarterAvgPrice)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.hedgeContracts == null || gs.hedgeContracts.Count == 0)
                return "";

            float totalPnlWan = 0f;
            var sb = new StringBuilder();
            var keep = new List<HedgeContract>();

            for (int i = 0; i < gs.hedgeContracts.Count; i++)
            {
                var c = gs.hedgeContracts[i];
                if (c == null || c.monthsRemaining <= 0) continue;

                float quarters = Mathf.Max(1f, c.termMonths / 3f);
                float qQty = c.quantity / quarters;
                float pnlUsd = SettleContractUsd(c, quarterAvgPrice, qQty);
                float pnlWan = pnlUsd / 10000f;

                // 见习期软保护（第 2 财年）：燃油对冲亏损上限锁定 20%（占单季对冲名义量价值）
                float capRatio = ProgressionGate.HedgeLossCapRatio(gs.year);
                if (capRatio > 0f && pnlWan < 0f)
                {
                    float notionalWan = qQty * c.lockPrice / 10000f;
                    float maxLossWan = notionalWan * capRatio;
                    if (-pnlWan > maxLossWan)
                    {
                        pnlWan = -maxLossWan;
                    }
                }

                totalPnlWan += pnlWan;
                c.monthsRemaining -= 3;

                sb.AppendLine(FormatSettleLine(c, quarterAvgPrice, qQty, pnlWan));

                if (c.monthsRemaining > 0)
                    keep.Add(c);
                else
                    sb.AppendLine("对冲合约已到期并移除。");
            }

            gs.hedgeContracts = keep;
            gs.cash += totalPnlWan;
            if (gs.cash < 0f)
            {
                sb.AppendLine($"⚠ 对冲结算后现金为负数（{gs.cash:F1} 万），已归零。");
                gs.cash = 0f;
            }
            gs.yearNetProfit += totalPnlWan;

            if (Mathf.Abs(totalPnlWan) > 0.001f)
            {
                string line = $"燃油对冲本季合计 {(totalPnlWan >= 0 ? "+" : "")}{totalPnlWan:F1} 万。";
                gs.AddDecisionLog("财务：" + line);
                return sb.ToString().TrimEnd() + "\n" + line;
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "";
        }

        /// <summary>单季结算损益（美元）。</summary>
        public static float SettleContractUsd(HedgeContract c, float market, float quarterQty)
        {
            if (c == null || quarterQty <= 0f) return 0f;
            string type = string.IsNullOrEmpty(c.lockType) ? HedgeLockTypes.Fixed : c.lockType;

            switch (type)
            {
                case HedgeLockTypes.Collar:
                    if (market > c.capPrice + 0.0001f)
                        return quarterQty * (market - c.capPrice); // 涨破上限：按上限保护，盈利削顶
                    if (market < c.floorPrice - 0.0001f)
                        return quarterQty * (market - c.floorPrice); // 跌破下限：按下限结算，亏损保底
                    return 0f;

                case HedgeLockTypes.Call:
                    // 市价高于行权价才行权；权利金已在开仓扣除
                    if (market > c.lockPrice)
                        return quarterQty * (market - c.lockPrice);
                    return 0f;

                default: // fixed
                    return quarterQty * (market - c.lockPrice);
            }
        }

        private static string FormatSettleLine(HedgeContract c, float market, float qQty, float pnlWan)
        {
            string type = string.IsNullOrEmpty(c.lockType) ? HedgeLockTypes.Fixed : c.lockType;
            string detail = type switch
            {
                HedgeLockTypes.Collar when market > c.capPrice =>
                    $"市价 ${market:F0} > 上限 ${c.capPrice:F0}（削顶）",
                HedgeLockTypes.Collar when market < c.floorPrice =>
                    $"市价 ${market:F0} < 下限 ${c.floorPrice:F0}（保底）",
                HedgeLockTypes.Collar =>
                    $"市价 ${market:F0} 在区间内（无损益）",
                HedgeLockTypes.Call when market > c.lockPrice =>
                    $"期权行权：市价 ${market:F0} > 行权价 ${c.lockPrice:F0}",
                HedgeLockTypes.Call =>
                    $"期权不行权：市价 ${market:F0} ≤ ${c.lockPrice:F0}",
                _ =>
                    $"均价 ${market:F0} vs 锁定 ${c.lockPrice:F0}"
            };
            return $"对冲[{HedgeLockTypes.DisplayName(type)}] 量 {qQty:F0} 吨 · {detail} → " +
                   $"{(pnlWan >= 0 ? "+" : "")}{pnlWan:F2} 万";
        }

        #endregion

        #region Price History / Save

        public void PushPriceHistory(float price)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (gs.fuelPriceHistory == null)
                gs.fuelPriceHistory = new List<float>();
            gs.fuelPriceHistory.Add(price);
            while (gs.fuelPriceHistory.Count > 12)
                gs.fuelPriceHistory.RemoveAt(0);
        }

        public string ContractsToJson()
        {
            var gs = GameState.Instance;
            var blob = new HedgeListSave
            {
                items = gs?.hedgeContracts != null
                    ? gs.hedgeContracts.ToArray()
                    : System.Array.Empty<HedgeContract>()
            };
            return JsonUtility.ToJson(blob);
        }

        public void ContractsFromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.hedgeContracts = new List<HedgeContract>();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<HedgeListSave>(json);
                if (blob?.items != null)
                {
                    gs.hedgeContracts = new List<HedgeContract>(blob.items);
                    // 旧档缺 lockType → fixed
                    for (int i = 0; i < gs.hedgeContracts.Count; i++)
                    {
                        if (gs.hedgeContracts[i] != null &&
                            string.IsNullOrEmpty(gs.hedgeContracts[i].lockType))
                            gs.hedgeContracts[i].lockType = HedgeLockTypes.Fixed;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[HedgeManager] 读档失败: " + e.Message);
            }
        }

        #endregion
    }
}
