using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 时刻二级市场「一线到底」的账实核对（2026-11-02）。
    ///
    /// 与 <see cref="SlotMarketEditModeTests"/> 的分工：那一支钉的是**规则**
    /// （池满仍可成交、使用中的槽不能卖、货架不补货……），直接调 Logic。
    /// 本用例钉的是**玩家实际走的那条路**：
    /// 在页面上买一条 → 挂一条 → 推季度 → 收件箱里点「批准」/「拒绝」，
    /// 而且每一颗按钮都走收件箱那一层的入口（<see cref="InboxManager.Approve"/> /
    /// <see cref="InboxManager.RejectSlotSale"/>），不是绕过它直接调 Logic。
    ///
    /// 为什么必须走收件箱这一层：Logic 对了不代表按钮接对了。历史上
    /// `RejectSlotSale` 写好了却没人调用，拒绝后意向仍是 open，挂牌从此再也摇不到新出价 ——
    /// 这类「函数是对的、没人接」的缺陷，只有从按钮入口跑才看得见。
    ///
    /// 账的口径（分三笔，混在一起就查不出哪笔错了）：
    /// <list type="number">
    /// <item>买入：现金 −叫价、持仓 +1（当场过户）。</item>
    /// <item>挂牌：**现金不变** —— 挂牌只是挂出报价，钱要等买方出价被批准才到账。</item>
    /// <item>批准：现金 +出价、持仓 −1、挂牌下架；拒绝：现金不变、挂牌仍在。</item>
    /// </list>
    /// </summary>
    public class SlotMarketInboxFlowEditModeTests
    {
        static GameState NewGame(out GameObject host, float cash = 50000f)
        {
            host = new GameObject("__slot_flow__");
            var gs = host.AddComponent<GameState>();
            gs.year = 1;
            gs.quarter = 1;
            gs.cash = cash;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            StrategyManager.EnsureState(gs);
            return gs;
        }

        static List<InboxMail> SellMails(GameState gs)
        {
            var list = new List<InboxMail>();
            if (gs?.inbox == null) return list;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.action == InboxManager.SellSlotAction) list.Add(m);
            }
            return list;
        }

        /// <summary>推进季度直到出现一封「〈航司〉愿以 X 万收购 …」的待办邮件（上限 12 季）。</summary>
        static InboxMail AdvanceUntilOffer(GameState gs, SlotMarketListing listing, int maxQuarters = 12)
        {
            for (int i = 0; i < maxQuarters; i++)
            {
                // 挂牌保鲜：真实游戏里 CEO 看到没人问价会自己撤下重挂，
                // 这里只关心出价链路，故每季刷新上架季，避免被 3 季陈旧下架规则提前清掉。
                if (listing != null)
                {
                    listing.listedYear = gs.year;
                    listing.listedQuarter = gs.quarter;
                }
                gs.quarter++;
                if (gs.quarter > 4) { gs.quarter = 1; gs.year++; }
                SlotMarketLogic.TickQuarter(gs);

                var mails = SellMails(gs);
                for (int k = 0; k < mails.Count; k++)
                    if (InboxManager.IsOpen(mails[k])) return mails[k];
            }
            return null;
        }

        [Test]
        public void BuyThenListThenApprove_And_Reject_MoveCashExactlyOnce()
        {
            var gs = NewGame(out GameObject host);
            try
            {
                var mkt = SlotMarketLogic.Ensure(gs, refresh: true);
                Assert.IsNotNull(mkt, "时刻二级市场状态建不起来");
                Assert.Greater(mkt.listings.Count, 0, "货架是空的，本用例无从做起");

                // ———— ① 买一条：现金 −叫价、持仓 +1 ————
                var shelf = mkt.listings[0];
                float ask = shelf.askWan;
                float cashBeforeBuy = gs.cash;
                int heldBeforeBuy = gs.stg.slots.Count;

                Assert.IsTrue(SlotMarketLogic.Buy(gs, shelf.id, out string buyMsg), "买入失败：" + buyMsg);
                Assert.AreEqual(cashBeforeBuy - ask, gs.cash, 0.01f,
                    $"买入后现金应为 {cashBeforeBuy:F0} − 叫价 {ask:F0}，实际 {gs.cash:F0}");
                Assert.AreEqual(heldBeforeBuy + 1, gs.stg.slots.Count, "买入后持仓没 +1");
                Debug.Log($"[FLOW①买入] {shelf.airport} {shelf.clock}（{shelf.seller}）"
                          + $" 叫价 {ask:F0} 万 → 现金 {cashBeforeBuy:F0} → {gs.cash:F0}"
                          + $"（差额 {cashBeforeBuy - gs.cash:F0}），持仓 {heldBeforeBuy} → {gs.stg.slots.Count}");

                // ———— ② 挂牌：现金必须**不动** ————
                int idx = gs.stg.slots.Count - 1;
                float cashBeforeList = gs.cash;
                Assert.IsTrue(SlotMarketLogic.ListOwn(gs, idx, out string listMsg), "挂牌失败：" + listMsg);
                Assert.AreEqual(cashBeforeList, gs.cash, 0.01f,
                    "挂牌当场动了现金 —— 挂牌只是挂出报价，钱要等买方出价被批准才到账");
                Assert.AreEqual(1, mkt.mine.Count, "挂牌后我司挂牌数应为 1");

                var listing = mkt.mine[0];
                Debug.Log($"[FLOW②挂牌] {listing.airport} {listing.clock} 叫价 {listing.askWan:F0} 万"
                          + $"（市价 {listing.fairWan:F0}），持仓仍 {gs.stg.slots.Count} 条");

                // ———— ③ 推季度 → 收件箱出现收购意向 ————
                var mail = AdvanceUntilOffer(gs, listing);
                Assert.IsNotNull(mail, "推进 12 个季度都没收到「愿以 X 万收购」的邮件 —— 出价链路断了");
                Assert.IsTrue(mail.subject.Contains("收购"), "邮件标题不是收购意向：" + mail.subject);
                var offer = SlotMarketLogic.FindOffer(gs, mail.payloadCsv);
                Assert.IsNotNull(offer, "邮件里指向的购买意向查不到（payload 与意向对不上）");
                Assert.IsTrue(offer.open, "邮件还在待办，意向却被标成已关闭");

                // ———— ④ 批准：现金 +出价、持仓 −1、挂牌下架 ————
                float cashBeforeApprove = gs.cash;
                int heldBeforeApprove = gs.stg.slots.Count;
                float price = offer.priceWan;

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string approveMsg),
                    "收件箱「批准」失败：" + approveMsg);
                Assert.AreEqual("approved", mail.status, "批准后邮件状态不是 approved");
                Assert.AreEqual(cashBeforeApprove + price, gs.cash, 0.01f,
                    $"批准后现金应为 {cashBeforeApprove:F0} + 出价 {price:F0}，实际 {gs.cash:F0}（没入账或多记了）");
                Assert.AreEqual(heldBeforeApprove - 1, gs.stg.slots.Count, "批准后该槽没从持仓移除");
                Assert.IsNull(SlotMarketLogic.Find(gs, listing.id), "批准后挂牌还在架上（会挂空、再次成交）");
                Assert.IsFalse(offer.open, "批准后意向仍是 open（下一季会重复摇到它）");
                Debug.Log($"[FLOW③批准] {mail.subject}"
                          + $"\n　现金 {cashBeforeApprove:F0} → {gs.cash:F0}（+{gs.cash - cashBeforeApprove:F0}，出价 {price:F0}）"
                          + $"\n　持仓 {heldBeforeApprove} → {gs.stg.slots.Count}，挂牌仍在架={SlotMarketLogic.Find(gs, listing.id) != null}"
                          + $" 邮件状态={mail.status} 意向 open={offer.open}");

                // 卖光了就不该再有我司挂牌：卖掉一条后持仓少一条、挂牌少一条
                Assert.AreEqual(0, mkt.mine.Count, "成交后我司挂牌数应回到 0");

                // ———— ⑤ 再挂一条 → 拒绝：现金不变、挂牌仍在 ————
                // 上一步把唯一一条槽卖掉了，从货架上再买一条回来（买进来的槽天然闲置）。
                if (mkt.listings.Count == 0) SlotMarketLogic.Ensure(gs, refresh: true);
                Assert.Greater(mkt.listings.Count, 0, "货架空了，买不到第二条槽来测「拒绝」分支");
                Assert.IsTrue(SlotMarketLogic.Buy(gs, mkt.listings[0].id, out string buyMsg2),
                    "为「拒绝」分支补买一条槽失败：" + buyMsg2);

                int idx2 = -1;
                for (int i = 0; i < gs.stg.slots.Count; i++)
                    if (gs.stg.slots[i] != null && !gs.stg.slots[i].inUse) { idx2 = i; break; }
                Assert.Greater(idx2, -1, "没有闲置持仓可挂牌，本用例后半段无法进行");
                Assert.IsTrue(SlotMarketLogic.ListOwn(gs, idx2, out string listMsg2), "第二次挂牌失败：" + listMsg2);
                var listing2 = mkt.mine[0];

                var mail2 = AdvanceUntilOffer(gs, listing2);
                Assert.IsNotNull(mail2, "第二次挂牌没收到任何收购意向");
                var offer2 = SlotMarketLogic.FindOffer(gs, mail2.payloadCsv);
                Assert.IsNotNull(offer2, "第二封邮件指向的意向查不到");

                float cashBeforeReject = gs.cash;
                int heldBeforeReject = gs.stg.slots.Count;

                Assert.IsTrue(InboxManager.RejectSlotSale(gs, mail2, out string rejectMsg),
                    "收件箱「拒绝」失败：" + rejectMsg);
                Assert.AreEqual("rejected", mail2.status, "拒绝后邮件状态不是 rejected");
                Assert.AreEqual(cashBeforeReject, gs.cash, 0.01f, "拒绝出价却动了现金");
                Assert.AreEqual(heldBeforeReject, gs.stg.slots.Count, "拒绝出价却动了持仓");
                Assert.IsFalse(offer2.open, "拒绝后意向仍是 open —— 这封邮件会一直挂在待办里，"
                                           + "且该挂牌下一季再也摇不到新出价");
                Assert.IsNotNull(SlotMarketLogic.Find(gs, listing2.id), "拒绝出价后挂牌应继续有效，实际被撤了");
                Debug.Log($"[FLOW④拒绝] {mail2.subject}"
                          + $"\n　现金 {cashBeforeReject:F0} → {gs.cash:F0}（+{gs.cash - cashBeforeReject:F0}，应 0）"
                          + $"\n　持仓 {heldBeforeReject} → {gs.stg.slots.Count}（应不变）"
                          + $" 挂牌仍在架={SlotMarketLogic.Find(gs, listing2.id) != null}"
                          + $" 邮件状态={mail2.status} 意向 open={offer2.open}");

                // ———— ⑥ 拒绝之后仍能等来下一次问价（只有意向被关掉才可能） ————
                var mail3 = AdvanceUntilOffer(gs, listing2);
                Assert.IsNotNull(mail3, "婉拒一次之后就再也没人问价 —— 说明拒绝没有把旧意向关掉");
                Assert.AreNotEqual(mail2.id, mail3.id, "同一个季度重复摇到了同一封意向邮件");

                // ———— ⑦ 前置不满足时「批准」必须真的失败，且不许被标成已批准 ————
                // 场景：槽正被班次占用（现实中卖之前要先把班次调开）。
                // 这里钉两件事：① 交易不成立时不得动账；② 此类邮件被认作「过户类」，
                // 收件箱的批准兜底不会把它谎报成「已批准」。
                var offer3 = SlotMarketLogic.FindOffer(gs, mail3.payloadCsv);
                Assert.IsNotNull(offer3, "第三封邮件指向的意向查不到");
                int listedIdx = SlotMarketLogic.HoldingIndexOf(gs, SlotMarketLogic.Find(gs, offer3.listingId));
                Assert.Greater(listedIdx, -1, "挂牌对应的持仓找不到");
                gs.stg.slots[listedIdx].inUse = true;
                gs.stg.slots[listedIdx].usedByFlightNo = "CA9801";

                float cashBeforeFail = gs.cash;
                int heldBeforeFail = gs.stg.slots.Count;
                bool failed = InboxManager.Approve(gs, mail3, out string failMsg);

                gs.stg.slots[listedIdx].inUse = false;
                gs.stg.slots[listedIdx].usedByFlightNo = "";

                Assert.IsFalse(failed, "槽正被班次占用，批准居然成功了 —— 那会把在飞的航班卖掉");
                Assert.AreEqual(cashBeforeFail, gs.cash, 0.01f, "交易没成却动了现金");
                Assert.AreEqual(heldBeforeFail, gs.stg.slots.Count, "交易没成却动了持仓");
                Assert.IsTrue(InboxManager.IsOpen(mail3),
                    "交易没成，邮件却被结案 —— 玩家会看到「已批准」但现金没变、槽还在");
                Assert.IsTrue(InboxManager.IsTransactionalAction(InboxManager.SellSlotAction),
                    "收购意向必须被认作过户类邮件，否则收件箱的批准兜底会谎报「已批准」");
                Assert.IsFalse(string.IsNullOrEmpty(failMsg), "过户失败必须有可读原因");
                Debug.Log($"[FLOW⑤占用中学拒] 原因「{failMsg}」→ 现金 {cashBeforeFail:F0} 未动，"
                          + $"持仓 {heldBeforeFail} 未动，邮件仍待办={InboxManager.IsOpen(mail3)}");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
