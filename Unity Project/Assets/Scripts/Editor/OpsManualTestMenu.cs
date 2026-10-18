#if UNITY_EDITOR
using System.Reflection;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// OPS 手测（第 418 行待办）：一键进入经营态并定位/触发
    /// ① 航班异常 IRROPS 弹窗　② 航线分配 n98。
    /// 菜单：AirlineCEO → Phase 2 → OPS 手测 · …
    /// </summary>
    public static class OpsManualTestMenu
    {
        private const string MainScenePath = "Assets/MVP V1.0.unity";
        private const string OpsScriptPath = "Assets/Scripts/UI/OpsPanelUI.cs";
        private const int OpsNavIndex = 0; // MVPBootstrap.MapShellPageToLegacyNav("n13_运营部") == 0

        [MenuItem("AirlineCEO/Phase 2/OPS 手测 · 航班异常 IRROPS", false, 15)]
        public static void OpenIrrops()
        {
            if (EditorApplication.isPlaying) OpenIrropsInPlayMode(false);
            else OpenInEditMode("航班异常 IRROPS 弹窗在运行时由 OpsIrropsModalUI 构建，编辑模式下场景内无该对象。\n进入 Play 模式后再次点击本菜单即可弹出。");
        }

        [MenuItem("AirlineCEO/Phase 2/OPS 手测 · 航线分配 n98", false, 16)]
        public static void OpenAssign()
        {
            if (EditorApplication.isPlaying) OpenAssignInPlayMode(false);
            else OpenInEditMode("航线分配页 n98 在运行时由 OpsRuntimeFactory 构建，编辑模式下场景内无该对象。\n进入 Play 模式后再次点击本菜单即可切到「航线/航班环选择器」。");
        }

        [MenuItem("AirlineCEO/Phase 2/OPS 手测 · IRROPS + 截图", false, 17)]
        public static void OpenIrropsAndCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("OPS 手测 · IRROPS + 截图", "请在 Play 模式下使用此功能（截图需要 Game 视图渲染）。", "确定");
                return;
            }
            OpenIrropsInPlayMode(true);
        }

        [MenuItem("AirlineCEO/Phase 2/OPS 手测 · 航线分配 + 截图", false, 18)]
        public static void OpenAssignAndCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("OPS 手测 · 航线分配 + 截图", "请在 Play 模式下使用此功能（截图需要 Game 视图渲染）。", "确定");
                return;
            }
            OpenAssignInPlayMode(true);
        }

        static void OpenIrropsInPlayMode(bool capture)
        {
            EnsureInGameplay();
            ShowOpsPanel();

            // 弹 IRROPS（轮换 台风/停飞/流控/机组 四类场景，便于逐类手测）
            OpsPanelUI.OpenIrrops();

            var modal = Object.FindFirstObjectByType<OpsIrropsModalUI>(FindObjectsInactive.Include);
            if (modal != null)
            {
                Selection.activeGameObject = modal.gameObject;
                EditorGUIUtility.PingObject(modal.gameObject);
                Debug.Log("[AirlineCEO] 已弹出航班异常 IRROPS 弹窗，可点 A/B/C 验证处置回写现金/准点/董事会。");
            }

            if (capture) CaptureAfterDelay("OpsIrrops.png");
        }

        static void OpenAssignInPlayMode(bool capture)
        {
            EnsureInGameplay();
            ShowOpsPanel();

            OpsPanelUI.OpenOpsPage("n98_航线航班环选择器");

            var router = NavigationRouter.Instance;
            if (router != null)
                Debug.Log($"[AirlineCEO] 已打开航线分配页，导航栈顶 = {router.CurrentPageId}（期望 n98_航线航班环选择器）。");

            var sub = Object.FindFirstObjectByType<OpsSubpagePanel>(FindObjectsInactive.Include);
            if (sub != null)
            {
                Selection.activeGameObject = sub.gameObject;
                EditorGUIUtility.PingObject(sub.gameObject);
            }

            if (capture) CaptureAfterDelay("OpsAssign.png");
        }

        /// <summary>复现「点运营部导航」：隐藏其他部门、激活运营面板。</summary>
        static void ShowOpsPanel()
        {
            var boot = Object.FindFirstObjectByType<MVPBootstrap>(FindObjectsInactive.Include);
            if (boot == null) return;
            var showNav = typeof(MVPBootstrap).GetMethod("ShowNavPanel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            showNav?.Invoke(boot, new object[] { OpsNavIndex });
        }

        static void EnsureInGameplay()
        {
            var setup = Object.FindFirstObjectByType<SetupFlowController>(FindObjectsInactive.Include);
            if (setup == null || setup.IsInGameplay) return;
            var enter = typeof(SetupFlowController).GetMethod("EnterGameplay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            enter?.Invoke(setup, new object[] { true, true });
            Debug.Log("[AirlineCEO] 已关闭主菜单并进入经营态（fromContinue）。");
        }

        static void CaptureAfterDelay(string filename)
        {
            float start = Time.realtimeSinceStartup;
            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                if (Time.realtimeSinceStartup - start < 0.8f) return;
                EditorApplication.update -= step;

                string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string dir = System.IO.Path.Combine(projectRoot, "Screenshots");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                string fullPath = System.IO.Path.Combine(dir, filename);
                ScreenCapture.CaptureScreenshot(fullPath);
                Debug.Log("[AirlineCEO] 已保存 OPS 手测截图：" + fullPath);
            };
            EditorApplication.update += step;
        }

        static void OpenInEditMode(string message)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(OpsScriptPath);
            if (script != null)
            {
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
            }
            var current = EditorSceneManager.GetActiveScene();
            if (!current.IsValid() || current.path != MainScenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(MainScenePath);
            }
            EditorUtility.DisplayDialog("OPS 手测", message, "确定");
        }
    }
}
#endif
