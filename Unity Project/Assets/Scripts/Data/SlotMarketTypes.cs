using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>
    /// 时刻二级市场的一条挂牌（2026-11-02）。
    ///
    /// 为什么要与「协调池申请」分家：现实里这是两套完全不同的机制。
    /// - 协调池 = WASG 协调人**分配**，不收费，池容量是硬约束；
    /// - 二级市场 = 航司之间**转让**，收费，**不占协调池容量** —— 卖家的槽转给你，总量一分没变。
    ///
    /// ★这就是本类型存在的理由（修 305 号缺陷）：旧版把「购入时刻槽」写成 `AcquireSlotsAt(free:false)`，
    /// 交割时照样过 `SlotPoolFull` 判据，于是**池一满，付钱也买不到** —— 「花钱插队」这个卖点
    /// 在真正需要它的时候（池满）恰好失效。二级市场的成交必须绕开池容量判据。
    /// </summary>
    [Serializable]
    public class SlotMarketListing
    {
        public string id;            // 挂牌号（MK-2Q1-3）
        public string airport;       // 机场 IATA
        public string clock;         // 具体时刻 "HH:mm"（离港；kind=ARR 时为到港）
        /// <summary>
        /// 这一对的**到港时刻**（★2026-11-06「一槽一对」）。
        /// 空 = 卖家没登记（旧档 / 只到港挂牌）；买入后原样入账，于是「几点落地」跟着资产走。
        /// </summary>
        public string arrClock = "";
        /// <summary>挂牌类型：`DEP` = 离港对（默认）/ `ARR` = 只到港（only-landing）。</summary>
        public string kind = "";
        public string seller;        // 出让方（他航名；我司挂牌时为「我司」）
        public float askWan;         // 叫价（万美元）
        public bool mine;            // true = 我司挂牌（false = 他航待售）
        public int listedYear;
        public int listedQuarter;

        /// <summary>该挂牌的「市价参考」（万美元）。他航挂牌为成交时价，我司挂牌为定价基准。</summary>
        public float fairWan;
    }

    /// <summary>
    /// 他航对我司挂牌提出的购买意向。批准即成交（槽出手、现金入账），拒绝则该次意向作废。
    /// 一份挂牌每季最多收到一份意向，避免同一件事在收件箱里堆成一叠。
    /// </summary>
    [Serializable]
    public class SlotBuyerOffer
    {
        public string id;            // OFFER-2Q1-1
        public string listingId;     // 对应我司挂牌
        public string airport;
        public string clock;
        public string buyer;         // 出价航司
        public float priceWan;       // 出价（万美元）
        public int year;
        public int quarter;
        /// <summary>false = 已处理（成交或婉拒）。保留记录以便回执可查「谁问过价」。</summary>
        public bool open = true;
    }

    /// <summary>时刻二级市场状态（随战术状态一起存档）。</summary>
    [Serializable]
    public class SlotMarketState
    {
        /// <summary>他航待售货架：每季度整批换新（与二手飞机货架同一口径）。</summary>
        public List<SlotMarketListing> listings = new List<SlotMarketListing>();
        /// <summary>我司挂牌：跨季度保留，除非撤回或成交。</summary>
        public List<SlotMarketListing> mine = new List<SlotMarketListing>();
        /// <summary>收到过的购买意向（open=false 的是已处理的历史）。</summary>
        public List<SlotBuyerOffer> offers = new List<SlotBuyerOffer>();

        /// <summary>货架所属季度（与 gs.year/quarter 不一致即视为过期，下次打开时重摇）。</summary>
        public int year;
        public int quarter;
        /// <summary>挂牌号自增序号，保证同一季度内 id 不撞。</summary>
        public int seq;

        /// <summary>
        /// 本季货架是否已摇过。**不能用 listings.Count == 0 代替** ——
        /// 玩家把货架买空之后，空列表会被误判成「还没摇」，下一帧又整批补货，
        /// 等于同一季度无限进货。这个标记才是「本季换新过」的真正判据。
        /// </summary>
        public bool rolled;
    }
}
