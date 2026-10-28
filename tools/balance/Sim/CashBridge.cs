namespace AirlineCEO.Balance;

/// <summary>
/// 年度现金桥：把「期初 → 期末」的现金变动逐项摊开。
///
/// 这条等式是为 T04.8 的结构性缺陷 ② 建的。在此之前，
/// `YearSummary.TotalNetProfit` 只累加 12 个月的 `MonthlyPnL.NetProfitWan`，
/// 而 IRROPS 一次性冲击、银行贷款放款、还本付息都是**直接改 cash**、不进损益。
/// 于是「净利」和「期末 − 期初」永远对不上，且差额随手一算就是几十万：
///
///   全服务/繁荣：期初 625、净利 1248、期末 1853
///   期末 − 期初 = 1228，比 1248 少 20 —— 恰好是 −25 万 IRROPS × 0.80
///
/// 差额本身不是错（现金流 ≠ 损益），错在**没有地方把它说清**。任何拿这张矩阵
/// 当现金对账 oracle 的人都会得出「净利 ≠ 现金变化」这个看似矛盾的结论。
///
/// 等式（单位：万美元）：
///
///   期末现金 = 期初现金
///            + 净利（不含利息，见下）
///            + IRROPS 一次性现金冲击
///            − 已付利息
///            + 期末未偿贷款本金
///
/// 为什么利息要单独减：`SimulateMonth` 的 `net = revenue − totalCost` 里**没有利息**，
/// 利息是在月度循环里直接 `cash -= loanMonthlyPay` 扣掉的。等额本息月供里本金那部分
/// 已经在「未偿本金」项里自然抵消，所以只把利息单列出来。
///
/// 为什么未偿本金要加回：放款时 `cash += 本金` 是一次性全额，但还款只还了其中一部分，
/// 没还的那部分仍躺在账上 —— 它是现金，不是利润。
///
/// 残差必须为 0。非零意味着现金流里有条路径没登记进这个等式，那才是真缺陷。
/// </summary>
public static class CashBridge
{
    /// <summary>残差容差（万美元）。逐月 <c>Round2</c> 的累计误差不应超过此值。</summary>
    public const float ToleranceWan = 0.5f;

    /// <summary>按现金桥等式算出的期末现金。用于与真实 <c>ClosingCash</c> 对照。</summary>
    public static float ExpectedClosingCash(
        float openingCash,
        float netProfit,
        float irropsCash,
        float interestPaid,
        float loanOutstanding)
        => openingCash + netProfit + irropsCash - interestPaid + loanOutstanding;

    /// <summary>实际期末现金与等式预测之差。接近 0 表示账目可逐项解释。</summary>
    public static float Residual(
        float openingCash,
        float closingCash,
        float netProfit,
        float irropsCash,
        float interestPaid,
        float loanOutstanding)
        => closingCash - ExpectedClosingCash(openingCash, netProfit, irropsCash, interestPaid, loanOutstanding);

    public static bool IsBalanced(float residual) => System.Math.Abs(residual) <= ToleranceWan;

    /// <summary>给报告用的一行文字说明，避免读者自己心算差额。</summary>
    public static string Describe(
        float openingCash,
        float closingCash,
        float netProfit,
        float irropsCash,
        float interestPaid,
        float loanOutstanding)
    {
        float expected = ExpectedClosingCash(openingCash, netProfit, irropsCash, interestPaid, loanOutstanding);
        float residual = closingCash - expected;
        string verdict = IsBalanced(residual) ? "对账通过" : "对账失败（现金流有未登记路径）";
        return $"现金桥 {openingCash:F1} 期初 + {netProfit:F1} 净利 " +
               $"{(irropsCash >= 0 ? "+" : "")}{irropsCash:F1} IRROPS " +
               $"−{interestPaid:F1} 利息 + {loanOutstanding:F1} 未偿本金 = {expected:F1}，" +
               $"实际 {closingCash:F1}，残差 {residual:F2} → {verdict}";
    }
}
