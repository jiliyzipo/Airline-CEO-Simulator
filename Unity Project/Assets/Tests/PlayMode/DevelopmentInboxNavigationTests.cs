using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirlineCEO.Tests.PlayMode
{
    public class DevelopmentInboxNavigationTests : IsolatedPlayModeFixture
    {
        [UnityTest]
        public IEnumerator ActualInboxApprovalButton_SchedulesSettlementWithoutAddingAFlight()
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            var gs = new GameObject("InboxApprovalGS").AddComponent<GameState>();
            gs.inbox.Clear(); gs.pendingInbox.Clear(); gs.activeRoutes.Clear();
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "PEK", "18:00", 1, "ui-slot", out _));
            var mail = gs.inbox.Find(x => x.id == "ui-slot");
            var panel = InboxPanelUI.Build();
            yield return null;
            var row = System.Linq.Enumerable.FirstOrDefault(panel.GetComponentsInChildren<Button>(true), b => b.name == "Row_ui-slot");
            Assert.IsNotNull(row); row.onClick.Invoke();
            var button = System.Linq.Enumerable.FirstOrDefault(panel.GetComponentsInChildren<Button>(true), b => b.name == "Approve");
            Assert.IsNotNull(button); Assert.IsTrue(button.interactable); Assert.IsTrue(button.gameObject.activeInHierarchy);
            button.onClick.Invoke();
            Assert.AreEqual("approved", mail.status); Assert.IsTrue(mail.effectPending);
            Assert.AreEqual(0, gs.activeRoutes.Count, "UI approval does not skip settlement or aircraft assignment.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator InboxKeyword_OpensExactRoute_AndBackReturnsToInbox()
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            var gs = new GameObject("InboxNavigationGS").AddComponent<GameState>();
            gs.activeRoutes = new List<RouteData> { new RouteData { routeId = "NAV-ROUTE",
                origin = "PEK", destination = "SHA", depTime = "08:00", arrTime = "09:30", weeklyFlights = 7 } };
            gs.inbox.Clear();
            gs.inbox.Add(new InboxMail { id = "nav-mail", subject = "航线执行回执", body = "PEK-SHA",
                action = "news", status = "approved", origin = "NAV-ROUTE" });
            var canvas = new GameObject("InboxNavigationCanvas", typeof(Canvas));
            var nav = canvas.AddComponent<PrefabNavIntegration>();
            nav.Attach(); nav.EnterGameplay(); nav.SelectTab("n7_收件箱");
            yield return null;
            var inbox = Object.FindFirstObjectByType<InboxPanelUI>();
            Assert.IsNotNull(inbox);
            var link = inbox.GetComponentInChildren<InboxLinkClick>(true);
            Assert.IsNotNull(link); Assert.IsNotNull(link.onLink);
            link.onLink("route:NAV-ROUTE");
            Assert.AreEqual("NAV-ROUTE", OpsSubpageContext.SelectedRouteId);
            Assert.AreEqual("n41_单航线", nav.Router.CurrentPageId);
            Assert.IsTrue(nav.Router.Back());
            Assert.AreEqual("n7_收件箱", nav.Router.CurrentPageId);
            yield return null;
        }
    }
}
