using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// n103 条款确认 / n106 重组条件页的签署入口（2026-10-29 报障）。
    ///
    /// 报障原文：「邮件里点击『批准』弹窗『条款确认』，页面里却显示在收件箱批示。
    /// 需要确认在邮件里确认还是在条款确认的弹窗里确认。」
    ///
    /// 查下来不是措辞之争，是**融资永远签不成**：
    ///   · 收件箱「批准」对 finance_crisis 只记一个 PendingFinanceMailId 就 return，不调审批；
    ///   · n103 页写着「签署须在收件箱批复，本页不可生效」；
    ///   · 于是回收件箱再点「批准」又回到本页 —— 两页互相指路，没有一处真正 Approve。
    ///
    /// 本用例钉住三件事，缺一条这个洞就会重开：
    ///   ① 有未结危机函时，n103 上有可点的「确认」，点了邮件状态变 approved 且贷款真的落地；
    ///   ② 「取消」只退页，邮件保持 pending（不能顺手把人签了）；
    ///   ③ 没有危机函时「确认」是禁用的（不能凭空造出一笔融资）。
    /// </summary>
    public class FinanceTermSignPlayModeTests : IsolatedPlayModeFixture
    {
        GameObject _host;
        GameObject _gsGo;
        GameState _gs;

        [SetUp]
        public void SetUp()
        {
            FinanceContext.PendingFinanceMailId = null;
            FinanceContext.FinancingKind = -1;
        }

        [TearDown]
        public void TearDown()
        {
            FinanceContext.PendingFinanceMailId = null;
            FinanceContext.FinancingKind = -1;
            if (_host != null) Object.DestroyImmediate(_host);
            if (_gsGo != null) Object.DestroyImmediate(_gsGo);
            _host = null;
            _gsGo = null;
        }

        /// <summary>
        /// 造一个「现金跑道过短」的局面。跑道 = 现金 / 月烧钱，必须 &lt; 3 个月
        /// （<see cref="LoanManager.IsFinanceUnlocked"/> 的闸门），否则危机函根本不会发出。
        /// 融资落地还要 LoanManager / FinanceManager 单例在场，这里一并挂上。
        /// </summary>
        GameState BuildCrisisGame(float cash = 150f, float monthlyBurn = 80f, float boardSatisfaction = 60f)
        {
            _gsGo = new GameObject("SignGS");
            _gs = _gsGo.AddComponent<GameState>();
            _gs.inbox = new List<InboxMail>();
            _gs.cash = cash;
            _gs.monthlyBurnRate = monthlyBurn;
            _gs.ceoWeeklyPayWan = 0f;
            _gs.financeCrisisBurnBonus = 0f;
            _gs.boardSatisfaction = boardSatisfaction;
            _gs.year = 1;
            _gs.quarter = 2;

            if (LoanManager.Instance == null)
                new GameObject("SignLoanMgr").AddComponent<LoanManager>();
            if (FinanceManager.Instance == null)
                new GameObject("SignFinMgr").AddComponent<FinanceManager>();
            return _gs;
        }

        FinanceSubpagePanel BuildPage(string pageId)
        {
            _host = new GameObject("SignHost", typeof(RectTransform));
            var go = FinanceRuntimeFactory.TryCreate(pageId, _host.transform);
            Assert.IsNotNull(go, "应能构建 " + pageId);
            var panel = go.GetComponent<FinanceSubpagePanel>();
            Assert.IsNotNull(panel, pageId + " 应挂 FinanceSubpagePanel");
            panel.RefreshBody();
            return panel;
        }

        static Button FindButton(FinanceSubpagePanel panel, string name)
            => panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        [UnityTest]
        public IEnumerator TermsPage_SignButton_ApprovesThePendingMail()
        {
            var gs = BuildCrisisGame();
            Assert.Less(CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn), 3f,
                "前置不成立：跑道 ≥ 3 月不会触发危机函");
            InboxManager.MaybeOfferFinanceCrisis(gs);
            var mail = gs.inbox.FirstOrDefault(m => m.action == "finance_crisis");
            Assert.IsNotNull(mail, "前置不成立：危机函没发出来");
            Assert.AreEqual("pending", mail.status);

            double cashBefore = gs.cash;
            var panel = BuildPage("n103_条款确认");

            var sign = FindButton(panel, "Sign");
            Assert.IsNotNull(sign, "条款确认页必须有「确认」——这正是报障里缺的那颗按钮");
            Assert.IsTrue(sign.interactable, "有未结危机函时「确认」必须可点");

            sign.onClick.Invoke();
            yield return null;

            Assert.AreEqual("approved", mail.status,
                "点「确认」必须真的审批这封函；否则玩家在收件箱与本页之间永远走不出去");
            Assert.Greater(gs.cash, cashBefore, "签的是贷款，现金应增加");
        }

        [UnityTest]
        public IEnumerator TermsPage_Cancel_LeavesMailPending()
        {
            var gs = BuildCrisisGame();
            InboxManager.MaybeOfferFinanceCrisis(gs);
            var mail = gs.inbox.FirstOrDefault(m => m.action == "finance_crisis");
            Assert.IsNotNull(mail);

            var panel = BuildPage("n103_条款确认");
            var cancel = FindButton(panel, "Cancel");
            Assert.IsNotNull(cancel, "条款确认页必须有「取消」");
            Assert.IsTrue(cancel.interactable);

            cancel.onClick.Invoke();
            yield return null;

            Assert.AreEqual("pending", mail.status, "「取消」只退页，不得顺手把函件签掉");
        }

        [UnityTest]
        public IEnumerator TermsPage_WithoutCrisisMail_DisablesSign()
        {
            BuildCrisisGame(cash: 5000f, monthlyBurn: 10f);  // 跑道宽裕：不会发危机函
            var gs = _gs;
            Assert.GreaterOrEqual(CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn), 3f);
            InboxManager.MaybeOfferFinanceCrisis(gs);
            Assert.AreEqual(0, gs.inbox.Count, "前置不成立：这个局面不该有危机函");

            var panel = BuildPage("n103_条款确认");
            var sign = FindButton(panel, "Sign");
            Assert.IsNotNull(sign, "没有待签函件时按钮也要在（禁用），不然玩家会以为这页坏了");
            Assert.IsFalse(sign.interactable, "没有危机函却能点「确认」，等于凭空造一笔融资");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TermsPage_SignTwice_DoesNotDoubleLend()
        {
            var gs = BuildCrisisGame();
            InboxManager.MaybeOfferFinanceCrisis(gs);
            var mail = gs.inbox.FirstOrDefault(m => m.action == "finance_crisis");
            Assert.IsNotNull(mail);

            var panel = BuildPage("n103_条款确认");
            var sign = FindButton(panel, "Sign");
            Assert.IsNotNull(sign);

            // 连点两下：按钮闭包握着同一封函的引用，第二下绝不能再放一笔款。
            sign.onClick.Invoke();
            sign.onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, gs.activeLoans == null ? 0 : gs.activeLoans.Count,
                "重复签署放了两笔款 —— 这是「融资永远签不成」修好之后最容易开出来的新洞");
        }

        [UnityTest]
        public IEnumerator RestructuringPage_CanAlsoSign()
        {
            var gs = BuildCrisisGame(cash: 40f, boardSatisfaction: 30f);
            Assert.Less(CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn), 3f);
            InboxManager.MaybeOfferFinanceCrisis(gs);
            var mail = gs.inbox.FirstOrDefault(m => m.action == "finance_crisis");
            Assert.IsNotNull(mail);
            Assert.IsTrue(InboxManager.IsFinanceRestructure(mail), "这个局面应推荐重组");

            var panel = BuildPage("n106_重组条件勾选");
            var sign = FindButton(panel, "Sign");
            Assert.IsNotNull(sign, "n106 与 n103 同一条缺陷路径，也必须能签");
            Assert.IsTrue(sign.interactable);

            sign.onClick.Invoke();
            yield return null;

            Assert.AreEqual("approved", mail.status);
            Assert.IsNotNull(gs.restructuring, "重组应已立卷");
            Assert.IsTrue(gs.restructuring.filed, "点「确认重组」应真的申请破产保护");
        }
    }
}
