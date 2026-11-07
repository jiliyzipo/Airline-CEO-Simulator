#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 命令台「购买 / 申请 &lt;机场&gt; &lt;时刻&gt;时刻槽」的隔离回归（2026-11-03 建，
    /// **2026-11-04 改口径，2026-11-06 再改**）。
    ///
    /// 报障原话：「图二命令台输入：购买 SIN 12:00 时刻槽，收到完全无关的邮件」。
    /// 病根：这句话里唯一像部门词的只有「时刻槽」，命中 <see cref="CeoCommandRouter.OpsWords"/>
    /// 后被当成「汇报运营情况」，回了一封「8 条航线 · 周 112 班 · 准点 80%」的网络总账 ——
    /// 槽没买、现金没动、台账没动，却像受理了一样。
    ///
    /// ★2026-11-04 用户裁定：「买卖具体哪个机场的哪个时刻，不是 CEO 的工作，是运营部的工作。」
    /// 命令台**不再执行**这类写指令。但「认得这句话」这条防线必须留着 —— 认不出它就会重蹈
    /// 2026-11-03 的报障：落回部门词表、回一封无关总账。
    ///
    /// ★2026-11-06 用户改判（推翻 11-04 的「一律拒办」）：
    /// 「取消浮窗里的按钮，所有申请时刻命令，都在命令台下达。」
    /// 机场时刻页那颗「只申请到港时刻」按钮已删，于是**申请**必须由命令台真执行 ——
    /// 递出的仍是 `apply_slot` 提案（收件箱待批），与运营部自己递单同一条管线。
    /// **买入**仍拒办：一句话直接扣款与「CEO 只批不挑」的现金纪律冲突。
    ///
    /// 本回归钉五件事：
    /// <list type="number">
    /// <item>「购买…」被认成时刻槽事务、**且拒办**（不动账、不递件）；</item>
    /// <item>「申请…」被认成时刻槽事务、**且真执行**（递 apply_slot 提案、不动现金）；</item>
    /// <item>问句（「为什么…没有时刻槽」）仍归运营部答，不被当买卖指令；</item>
    /// <item>「到港 / 离港」两端的解析：说了到港才走到港池，没说默认离港；</item>
    /// <item>货架上有货也不许命令台直接成交 —— 手动买槽仍然只走运营部提案。</item>
    /// </list>
    /// </summary>
    public static class SlotDealCommandRegression
    {
        public static bool Run()
        {
            bool ok = true;

            // ———— ① 购买：认得准，但一律拒办（纯函数，无需台账）————
            var buy = CeoCommandRouter.Route("n77_航权与时刻槽", "购买SIN 12:00时刻槽");
            bool buyOk = buy.topic == CeoCommandRouter.TopicSlotDeal
                         && buy.kind == CeoCommandRouter.CeoAskKind.Act
                         && buy.slotBuy && buy.origin == "SIN" && buy.clock == "12:00"
                         && !buy.ok && !string.IsNullOrEmpty(buy.note);
            if (!buyOk)
            {
                Debug.LogError($"R48 ①「购买SIN 12:00时刻槽」没被认成时刻槽事务或未被拒办"
                               + $"（topic={buy.topic} ok={buy.ok} apt={buy.origin} clock={buy.clock} buy={buy.slotBuy}）"
                               + " —— 认不出就会派给运营部，回一封与买槽无关的网络总账（2026-11-03 报障）。");
                ok = false;
            }

            // ———— ①b 申请：认得出，而且**真的执行**（★2026-11-06 用户改判）————
            var apply = CeoCommandRouter.Route("n77_航权与时刻槽", "申请SIN 12:00时刻槽");
            bool applyOk = apply.topic == CeoCommandRouter.TopicSlotDeal && !apply.slotBuy
                           && apply.ok && apply.origin == "SIN" && apply.clock == "12:00"
                           && apply.count == 1
                           && apply.slotKind == SlotHolding.SlotKindDeparture;
            if (!applyOk)
            {
                Debug.LogError($"R48 ①b「申请SIN 12:00时刻槽」应被认成协调池申请且可执行，"
                               + $"实际 topic={apply.topic} buy={apply.slotBuy} ok={apply.ok}"
                               + $" apt={apply.origin} clock={apply.clock} kind={apply.slotKind} n={apply.count}");
                ok = false;
            }

            // 两端解析：「到港」才走到港池，没说默认离港（浮窗按钮删掉后，这是唯一的到港入口）。
            var applyArr = CeoCommandRouter.Route("n77_航权与时刻槽", "申请ATL 08:00到港时刻槽");
            bool arrOk = applyArr.topic == CeoCommandRouter.TopicSlotDeal && applyArr.ok
                         && applyArr.origin == "ATL" && applyArr.clock == "08:00"
                         && applyArr.slotKind == SlotHolding.SlotKindArrival;
            if (!arrOk)
            {
                Debug.LogError($"R48 ①c「申请ATL 08:00到港时刻槽」应解析为到港池，"
                               + $"实际 ok={applyArr.ok} apt={applyArr.origin} clock={applyArr.clock}"
                               + $" kind={applyArr.slotKind}");
                ok = false;
            }

            // 问句归运营部答：不能被当成买卖指令去改台账
            var ask = CeoCommandRouter.Route("n77_航权与时刻槽", "为什么 SIN 12:00 没有时刻槽");
            if (ask.topic == CeoCommandRouter.TopicSlotDeal)
            {
                Debug.LogError("R48 ②「为什么…没有时刻槽」是问句，不该被当买卖指令。");
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

                // ———— ③ 空货架：如实拒办，且不许往收件箱塞任何邮件 ————
                const string raw = "购买SIN 12:00时刻槽";
                float cashBefore = gs.cash;
                int inboxBefore = gs.inbox.Count;
                int pendingBefore = gs.pendingInbox.Count;
                int heldBefore = gs.stg.slots.Count;

                var ansNo = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", raw), raw);
                bool refuseOk = !ansNo.ok && !ansNo.needsRun
                                && Mathf.Abs(gs.cash - cashBefore) < 0.01f
                                && gs.stg.slots.Count == heldBefore
                                && gs.inbox.Count == inboxBefore
                                && gs.pendingInbox.Count == pendingBefore;
                if (!refuseOk)
                {
                    Debug.LogError($"R48 ③ 命令台买槽应被拒办且不动任何台账：ok={ansNo.ok} needsRun={ansNo.needsRun}"
                                   + $" cash={cashBefore}→{gs.cash} 持仓 {heldBefore}→{gs.stg.slots.Count}"
                                   + $" 收件箱 {inboxBefore}→{gs.inbox.Count} 待投递 {pendingBefore}→{gs.pendingInbox.Count}");
                    ok = false;
                }

                // ———— ④ 货架上有同机场同时刻的槽：也**不许**成交（手动买槽已废止）————
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
                bool noDeal = !ran
                              && Mathf.Abs(gs.cash - cashBefore) < 0.01f
                              && gs.stg.slots.Count == heldBefore
                              && mkt.listings.Count == 1
                              && gs.inbox.Count == inboxBefore
                              && gs.pendingInbox.Count == pendingBefore;
                if (!noDeal)
                {
                    Debug.LogError($"R48 ④ 货架有货时命令台竟成交了：ran={ran} receipt={receipt}"
                                   + $" 现金 {cashBefore}→{gs.cash}（应不动）持仓 {heldBefore}→{gs.stg.slots.Count}"
                                   + $" 货架余 {mkt.listings.Count}（应仍 1）—— 手动买槽必须只走运营部提案。");
                    ok = false;
                }

                // ———— ⑤ 协调池申请：**真递单**，但不扣款、不直接过户（★2026-11-06 改判）————
                // 浮窗里的「只申请到港时刻」按钮已删，命令台是唯一的申请入口，所以这里必须办成：
                // 收件箱多一张 apply_slot 提案（仍需 CEO 批），现金与持仓**不动**。
                float cashBeforeApply = gs.cash;
                int inboxBeforeApply = gs.inbox.Count;
                int heldBeforeApply = gs.stg.slots.Count;
                const string rawApply = "申请SIN 12:00时刻槽";
                var ansApply = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", rawApply), rawApply);
                string applyMsg = "";
                bool applyRan = ansApply.ok && ansApply.needsRun;
                if (applyRan) applyRan = DeptReportLogic.RunPending(gs, ansApply, rawApply, out applyMsg);
                int proposals = 0;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) proposals++;
                bool applyDone = applyRan && proposals == 1
                                 && Mathf.Abs(gs.cash - cashBeforeApply) < 0.01f
                                 && gs.stg.slots.Count == heldBeforeApply
                                 && gs.inbox.Count == inboxBeforeApply + 1;
                if (!applyDone)
                {
                    Debug.LogError($"R48 ⑤ 命令台协调池申请应递出一张 apply_slot 提案且不动账：ran={applyRan} "
                                   + $"apply_slot 件={proposals}（期望 1） 现金 {cashBeforeApply}→{gs.cash}"
                                   + $" 持仓 {heldBeforeApply}→{gs.stg.slots.Count} 收件箱 {inboxBeforeApply}→{gs.inbox.Count}"
                                   + $"｜{applyMsg}");
                    ok = false;
                }

                // ⑤b 到港端同样能递单：提案上的 kind 必须是 ARR（浮窗按钮删掉后唯一的到港入口）。
                const string rawArr = "申请SIN 13:00到港时刻槽";
                var ansArr = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n77_航权与时刻槽", rawArr), rawArr);
                bool arrRan = ansArr.ok && ansArr.needsRun;
                string arrMsg = "";
                if (arrRan) arrRan = DeptReportLogic.RunPending(gs, ansArr, rawArr, out arrMsg);
                InboxMail arrMail = null;
                if (arrRan)
                    for (int i = 0; i < gs.inbox.Count; i++)
                    {
                        var m = gs.inbox[i];
                        if (m != null && m.action == InboxManager.ApplySlotAction
                            && m.slotKind == SlotHolding.SlotKindArrival) { arrMail = m; break; }
                    }
                bool arrApplyDone = arrRan && arrMail != null;
                if (!arrApplyDone)
                {
                    Debug.LogError($"R48 ⑤b 命令台「申请…到港时刻槽」应递出 kind=ARR 的提案：ran={arrRan} "
                                   + $"arrMail={arrMail?.action ?? "(无)"}｜{arrMsg}");
                    ok = false;
                }

                Debug.Log($"R48 时刻槽命令（2026-11-06 命令台可申请）→ ①购买认得且拒办 {buyOk}"
                          + $" ①b申请可执行 {applyOk} ①c到港解析 {arrOk}"
                          + $" ②问句不归买卖 {ask.topic != CeoCommandRouter.TopicSlotDeal}"
                          + $" ③空货架拒办不动账 {refuseOk} ④有货也不成交 {noDeal}"
                          + $" ⑤申请递单不动账 {applyDone} ⑤b到港递单 {arrApplyDone}");
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
