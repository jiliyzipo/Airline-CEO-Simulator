using System.Collections.Generic;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Setup;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>Ensure an assertion failure cannot leak application singletons into later tests.</summary>
    public abstract class IsolatedPlayModeFixture
    {
        [SetUp] public void ResetBeforeCase() { ResetApplicationObjects(); }
        [TearDown] public void ResetAfterCase() { ResetApplicationObjects(); }

        static void ResetApplicationObjects()
        {
            TimeManager.SimulationEnabled = false;
            var roots = new HashSet<GameObject>();
            foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (component != null && component.GetType().Namespace != null
                    && component.GetType().Namespace.StartsWith("AirlineCEO."))
                    roots.Add(component.transform.root.gameObject);
            foreach (var root in roots) if (root != null) Object.DestroyImmediate(root);
            SetupFlowController.BypassForTests = false;
            MessageCenter.Clear();
            GameSpeed.Set(GameSpeed.L1);
        }
    }
}
