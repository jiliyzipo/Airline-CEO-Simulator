using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 命令台「购买 / 申请 &lt;机场&gt; &lt;时刻&gt;时刻槽」的回归。
    ///
    /// 历史报障（2026-11-03）：这句话曾被派成部门汇报，槽没买、账没动，
    /// 却回了一封「8 条航线 · 周 112 班」的网络总账 —— 比「没听懂」更糟。
    ///
    /// 2026-11-05 设计裁决：挑具体机场的具体时刻是**运营部**的工作，不是 CEO 指令。
    /// CEO 只在收件箱批「买不买」。故命令台对这类句子的新口径是：
    /// <list type="number">
    /// <item>仍必须**认出来**是时刻槽事务（不能退回部门汇报的假受理）；</item>
    /// <item>但 <c>ok=false</c>，命令条如实说明「转运营部提案、你只批收件箱」；</item>
    /// <item><b>零副作用</b>：不扣款、不入持仓、不下架挂牌、不递提案、不发任何邮件。</item>
    /// </list>
    /// 成交与挂牌管线本身由 <see cref="SlotMarketRegression"/>（收件箱审批流）覆盖。
    /// </summary>
    public class SlotDealCommandEditModeTests
    {
        GameObject _go;
        GameState _gs;
        SlotMarketState _mkt;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SlotDealCmdGS");
            _gs = _go.AddComponent<GameState>();
            _gs.EnsureDefaults();
            _gs.inbox = new System.Collections.Generic.List<InboxMail>();
            _gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            _gs.cash = 5000f;
            _mkt = SlotMarketLogic.Ensure(_gs);
            Assert.IsNotNull(_mkt, "二级市场状态没建起来");
            _mkt.listings.Clear();
            _mkt.mine.Clear();
            _mkt.offers.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _gs = null;
        }

        static CeoCommandRouter.CeoAsk Route(string raw)
            => CeoCommandRouter.Route("n77_航权与时刻槽", raw);

        /// <summary>认出来、但不执行：ok=false + 引导文案 + 零副作用，三样一次钉死。</summary>
        void AssertRefusedWithNoSideEffects(CeoCommandRouter.CeoAsk ask, string raw)
        {
            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic,
                "「" + raw + "」被派成了别的事务 —— 当年就是这样混进部门汇报的");
            Assert.IsFalse(ask.ok, "挑具体时刻是运营部的活，命令台不该放行执行");
            StringAssert.Contains("运营部", ask.note ?? "");
            StringAssert.Contains("收件箱", ask.note ?? "");

            float cashBefore = _gs.cash;
            int inboxBefore = _gs.inbox.Count;
            int pendingBefore = _gs.pendingInbox.Count;
            int heldBefore = _gs.stg.slots.Count;
            int listingsBefore = _mkt.listings.Count;

            var ans = DeptReportLogic.Build(_gs, ask, raw);
            Assert.IsFalse(ans.ok, "命令台拒办的指令不该有可执行答复");
            DeptReportLogic.RunPending(_gs, ans, raw, out _);

            Assert.AreEqual(cashBefore, _gs.cash, 0.01f, "没执行却动了钱");
            Assert.AreEqual(heldBefore, _gs.stg.slots.Count, "没执行却入了持仓");
            Assert.AreEqual(listingsBefore, _mkt.listings.Count, "没执行却动了挂牌");
            Assert.AreEqual(inboxBefore, _gs.inbox.Count, "不该往收件箱塞邮件");
            Assert.AreEqual(pendingBefore, _gs.pendingInbox.Count, "不该排任何邮件");
        }

        [Test]
        public void BuySlotSentence_IsRecognized_ButRefusedAsOpsWork()
        {
            var ask = Route("购买SIN 12:00时刻槽");

            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic,
                "「购买 SIN 12:00 时刻槽」被派成了部门汇报 —— 槽没买、账没动，却会回一封网络总账");
            Assert.IsFalse(ask.ok, "买具体槽不该再由命令台放行");
            Assert.AreEqual("SIN", ask.origin, "机场没解析出来（引导文案要点名你说的机场）");
            Assert.AreEqual("12:00", ask.clock, "时刻没解析出来");
        }

        [Test]
        public void ApplySlotSentence_IsAlsoRefusedAsOpsWork()
        {
            // 协调池申请与二级市场购入一样是运营部的活：认出来、不执行
            var ask = Route("申请SIN 12:00时刻槽");
            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic);
            Assert.IsFalse(ask.ok, "协调池申请也不该由命令台直办");
        }

        [Test]
        public void SlotQuestion_StillGoesToDepartment()
        {
            // 问句归运营部答，不能被当成买卖指令（否则一封「为什么缺时刻槽」会去改台账）
            var ask = Route("为什么 SIN 12:00 没有时刻槽");
            Assert.AreNotEqual(CeoCommandRouter.TopicSlotDeal, ask.topic);
        }

        [Test]
        public void BuySlotWithoutAirport_GuidesInsteadOfSendingMail()
        {
            var ask = Route("购买时刻槽");
            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic);
            Assert.IsFalse(ask.ok, "缺机场/时刻时不该执行");
            StringAssert.Contains("运营部", ask.note ?? "", $"引导文案没说清转给谁：{ask.note}");
            Assert.AreEqual(0, _gs.inbox.Count, "说不清就说不清，不许往收件箱塞一封无关汇报");
        }

        [Test]
        public void BuySlot_NoListing_RefusesWithZeroSideEffects()
        {
            const string raw = "购买SIN 12:00时刻槽";
            AssertRefusedWithNoSideEffects(Route(raw), raw);
        }

        [Test]
        public void BuySlot_WithListing_StillRefusesCommand_AndLeavesListingAlone()
        {
            // 货架上有现成的槽也一样：命令台不成交。CEO 批的是收件箱里的购入提案，
            // 不是在命令条上点名买某一格 —— 挂牌原样留着，等运营部走提案管线。
            const string raw = "购买SIN 12:00时刻槽";
            _mkt.listings.Add(new SlotMarketListing
            {
                id = "MK-TEST-1",
                airport = "SIN",
                clock = "12:00",
                seller = "星洲联航",
                askWan = 300f,
                fairWan = 280f,
                listedYear = _gs.year,
                listedQuarter = _gs.quarter
            });

            AssertRefusedWithNoSideEffects(Route(raw), raw);
        }

        [Test]
        public void ApplySlot_DeliversNothing_AndChargesNothing()
        {
            const string raw = "申请SIN 12:00时刻槽";
            AssertRefusedWithNoSideEffects(Route(raw), raw);
            for (int i = 0; i < _gs.inbox.Count; i++)
                Assert.AreNotEqual(InboxManager.ApplySlotAction, _gs.inbox[i]?.action,
                    "命令台直办的申请提案不该再出现 —— 申请由运营部按缺口发起");
        }
    }
}
