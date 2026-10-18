using System.Collections;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Testing;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    public class NavigationAndTickPlayModeTests : IsolatedPlayModeFixture
    {
        [UnityTest]
        public IEnumerator NavigationRouter_NavigateAndBack_UpdatesStack()
        {
            var go = new GameObject("TestNavRouter");
            var router = go.AddComponent<NavigationRouter>();

            var dummyA = new GameObject("Dummy_n13");
            var dummyB = new GameObject("Dummy_n15");
            var cfgA = ScriptableObject.CreateInstance<UIConfig>();
            cfgA.pageId = "n13_运营部";
            cfgA.title = "运营部";
            cfgA.prefab = dummyA;
            var cfgB = ScriptableObject.CreateInstance<UIConfig>();
            cfgB.pageId = "n15_财务部";
            cfgB.title = "财务部";
            cfgB.prefab = dummyB;

            router.SetCatalog(new[] { cfgA, cfgB });

            Assert.IsTrue(router.Navigate("n13_运营部"));
            Assert.AreEqual("n13_运营部", router.CurrentPageId);
            Assert.AreEqual(1, router.StackDepth);

            Assert.IsTrue(router.Navigate("n15_财务部"));
            Assert.AreEqual("n15_财务部", router.CurrentPageId);
            Assert.AreEqual(2, router.StackDepth);

            Assert.IsTrue(router.Back());
            Assert.AreEqual("n13_运营部", router.CurrentPageId);
            Assert.AreEqual(1, router.StackDepth);

            Object.Destroy(go);
            Object.Destroy(cfgA);
            Object.Destroy(cfgB);
            Object.Destroy(dummyA);
            Object.Destroy(dummyB);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NavigationRouter_ReplaceCurrent_KeepsStackDepthOne()
        {
            var go = new GameObject("TestNavReplace");
            var router = go.AddComponent<NavigationRouter>();
            var dummyA = new GameObject("Dummy_n13r");
            var dummyB = new GameObject("Dummy_n14");
            var a = ScriptableObject.CreateInstance<UIConfig>();
            a.pageId = "n13_运营部";
            a.prefab = dummyA;
            var b = ScriptableObject.CreateInstance<UIConfig>();
            b.pageId = "n14_市场销售部";
            b.prefab = dummyB;
            router.SetCatalog(new[] { a, b });

            Assert.IsTrue(router.Navigate("n13_运营部", replaceCurrent: true));
            Assert.IsTrue(router.Navigate("n14_市场销售部", replaceCurrent: true));
            Assert.AreEqual("n14_市场销售部", router.CurrentPageId);
            Assert.AreEqual(1, router.StackDepth);

            Object.Destroy(go);
            Object.Destroy(a);
            Object.Destroy(b);
            Object.Destroy(dummyA);
            Object.Destroy(dummyB);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TickPipeline_WithFuelShock_ChangesFuelPrice()
        {
            var gs = MockGameState.CreateMinimalAirline();
            float before = gs.fuelPricePerTon;

            var result = TickExecutionPipeline.Execute(new TickExecutionPipeline.TickInput
            {
                State = gs,
                FuelShockMultiplier = 1.10f,
                SettleMonth = true
            });

            Assert.AreEqual(before * 1.10f, result.FuelPriceAfter, 0.01f);
            Assert.Greater(result.CashAfter, 0f);
            yield return null;
        }
    }
}
