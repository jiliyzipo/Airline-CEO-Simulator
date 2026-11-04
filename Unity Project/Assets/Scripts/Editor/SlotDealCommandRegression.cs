#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 命令台「购买 / 申请 &lt;机场&gt; &lt;时刻&gt;时刻槽」的隔离回归（2026-11-03）。
    ///
    /// 报障原话：「图二命令台输入：购买 SIN 12:00 时刻槽，收到完全无关的邮件」。
    ///
    /// 病根：这句话里唯一像部门词的只有「时刻槽」，命中 <see cref="CeoCommandRouter.OpsWords"/>
    /// 后被当成「汇报运营情况」，回了一封「8 条航线 · 周 112 班 · 准点 80%」的网络总账 ——
    /// 槽没买、现金没动、台账没动，却像受理了一样。
    ///
    /// 本回归钉四件事：
    /// <list type="number">
    /// <item>这句话必须被认成时刻槽事务，机场与时刻都要解析出来；</item>
    /// <item>货架没有这条槽 → **如实说不成**，且不发、不排任何邮件；</item>
    /// <item>货架上有 → 当场成交：扣款、入持仓、下架，同样不发无关邮件；</item>
    /// <item>「申请」走协调池（免费），不扣款。</item>
    /// </list>
    /// </summary>
    public static class SlotDealCommandRegression
    {
        public static bool Run()
        {
            bool ok = true;

            // ———— ① 认得准（纯函数，无需台账）————
            var buy = CeoCommandRouter.Route("n77_航权与时刻槽", "购买SIN 12:00时刻槽");
            bool buyOk = buy.ok && buy.topic == CeoCommandRouter.TopicSlotDeal
                         && buy.kind == CeoCommandRouter.CeoAskKind.Act
                         && buy.slotBuy && buy.origin == "SIN" && buy.clock == "12:00";
            if (!buyOk)
            {
                Debug.LogError($"R48 ①「购买SIN 12:00时刻槽」没被认成时刻槽事务"
                               + $"（topic={buy.topic} ok={buy.ok} apt={buy.origin} clock={buy.clock} buy={buy.slotBuy}）"
                               + " —— 这会派给运营部，回一封与买槽无关的网络总账。");
                ok = false;
            }

            var apply = CeoCommandRouter.Route("n77_航权与时刻槽", "申请SIN 12:00时刻槽");
            bool applyOk = apply.ok && apply.topic == CeoCommandRouter.TopicSlotDeal && !apply.slotBuy;
            if (!applyOk)
            {
                Debug.LogError($"R48 ①「申请SIN 12:00时刻槽」应走协调池（免费），实际 topic={apply.topic} buy={apply.slotBuy}");
                ok = false;
            }

            // 问句归运营部答：不能被当成买卖指令去改台账
            var ask = CeoCommandRouter.Route("n77_航权与时刻槽", "为什么 SIN 12:00 没有时刻槽");
            if (ask.topic == CeoCommandRouter.TopicSlotDeal)
            {
                Debug.LogError("R48 ①「为什么…没有时刻槽」是问句，不该被当买卖指令。");
                ok = false;
            }

            var go = new GameObject("__slot_deal_test_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.EnsureDefaults();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.cash = 5000f;
                var mkt = SlotMarketLogic.Ensure(gs);
                mkt.listings.Clear();
                mkt.mine.Clear();
                mkt.offers.Clear();

                // ———— ② 货架上没有：如实拒绝，且不许往收件箱塞无关邮件 ————
                const string raw = "购买SIN 12:00时刻槽";
                float cashBefore = gs.cash;
                int inboxBefore = gs.inbox.Count;
                int pendingBefore = gs.pendingInbox.Count;
                int heldBefore = gs.stg.slots.Count;

                var ansNo = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", raw), raw);
                bool refuseOk = !ansNo.ok
                                && (ansNo.note ?? "").Contains("未成交")
                                && Mathf.Abs(gs.cash - cashBefore) < 0.01f
                                && gs.stg.slots.Count == heldBefore
                                && gs.inbox.Count == inboxBefore
                                && gs.pendingInbox.Count == pendingBefore;
                if (!refuseOk)
                {
                    Debug.LogError($"R48 ② 货架没货时应「未成交」并且不发信：ok={ansNo.ok} note={ansNo.note}"
                                   + $" cash={cashBefore}→{gs.cash} 持仓 {heldBefore}→{gs.stg.slots.Count}"
                                   + $" 收件箱 {inboxBefore}→{gs.inbox.Count} 待投递 {pendingBefore}→{gs.pendingInbox.Count}");
                    ok = false;
                }

                // ———— ③ 货架上有：当场成交，扣款 + 入持仓 + 下架，不发信 ————
                mkt.listings.Add(new SlotMarketListing
                {
                    id = "MK-R48-1", airport = "SIN", clock = "12:00", seller = "星洲联航",
                    askWan = 300f, fairWan = 280f, listedYear = gs.year, listedQuarter = gs.quarter
                });
                cashBefore = gs.cash;
                inboxBefore = gs.inbox.Count;
                pendingBefore = gs.pendingInbox.Count;
                heldBefore = gs.stg.slots.Count;

                var ansBuy = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", raw), raw);
                string receipt = "";
                bool ran = ansBuy.ok && ansBuy.needsRun;
                if (ran) ran = DeptReportLogic.RunPending(gs, ansBuy, raw, out receipt);
                bool buyDone = ran
                               && Mathf.Abs(gs.cash - (cashBefore - 300f)) < 0.01f
                               && gs.stg.slots.Count == heldBefore + 1
                               && mkt.listings.Count == 0
                               && gs.inbox.Count == inboxBefore
                               && gs.pendingInbox.Count == pendingBefore;
                if (!buyDone)
                {
                    Debug.LogError($"R48 ③ 有挂牌却没成交：ran={ran} receipt={receipt}"
                                   + $" 现金 {cashBefore}→{gs.cash}（应 −300）持仓 {heldBefore}→{gs.stg.slots.Count}"
                                   + $" 货架余 {mkt.listings.Count} 收件箱 {inboxBefore}→{gs.inbox.Count}");
                    ok = false;
                }

                // ———— ④ 协调池申请：递提案、不扣款 ————
                float cashBeforeApply = gs.cash;
                var ansApply = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", "申请SIN 12:00时刻槽"), "申请SIN 12:00时刻槽");
                string applyMsg = "";
                bool applyRan = ansApply.ok && ansApply.needsRun;
                if (applyRan) applyRan = DeptReportLogic.RunPending(gs, ansApply, "申请SIN 12:00时刻槽", out applyMsg);
                bool applyCashOk = Mathf.Abs(gs.cash - cashBeforeApply) < 0.01f;
                if (!applyCashOk)
                {
                    Debug.LogError($"R48 ④ 协调池申请是免费的，却动了现金：{cashBeforeApply}→{gs.cash}（{applyMsg}）");
                    ok = false;
                }
                if (!ansApply.ok || !ansApply.needsRun)
                {
                    Debug.LogError($"R48 ④「申请SIN 12:00时刻槽」没被受理：{ansApply.note} —— "
                                   + "协调池是免费的正常渠道，不该在这一步被挡。");
                    ok = false;
                }
                else if (applyRan)
                {
                    int proposals = 0;
                    for (int i = 0; i < gs.inbox.Count; i++)
                        if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) proposals++;
                    if (proposals == 0)
                    {
                        Debug.LogError("R48 ④ 协调池申请报成功却没递进收件箱。");
                        ok = false;
                    }
                }
                else if (string.IsNullOrEmpty(applyMsg))
                {
                    Debug.LogError("R48 ④ 协调池申请被拒绝，却没说明原因 —— CEO 无从判断是被谁挡下的。");
                    ok = false;
                }

                Debug.Log($"R48 时刻槽命令 → ①认事 {buyOk}/{applyOk} ②没货如实拒绝 {refuseOk}"
                          + $" ③当场成交 {buyDone} ④协调池免费 {applyCashOk}");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return ok;
        }
    }
}
#endif
