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
    /// 一键定位/打开战略部「临空商业 · 非航收入」子页。
    /// 菜单：AirlineCEO → 定位临空商业面板（+ 截图）
    /// </summary>
    public static class OpenCommercePanelMenu
    {
        private const string MainScenePath = "Assets/MVP V1.0.unity";
        private const string CommerceScriptPath = "Assets/Scripts/UI/StrategySubpagePanel.cs";
        private const int StrategyNavIndex = 8;      // MapShellPageToLegacyNav：n16 战略发展部 → 8
        private const string CommercePageId = "n108_临空商业";

        [MenuItem("AirlineCEO/定位临空商业面板", false, 13)]
        public static void OpenCommercePanel()
        {
            if (EditorApplication.isPlaying)
                OpenInPlayMode();
            else
                OpenInEditMode();
        }

        [MenuItem("AirlineCEO/定位临空商业面板 + 截图", false, 14)]
        public static void OpenCommercePanelAndCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("定位临空商业面板 + 截图",
                    "请在 Play 模式下使用此功能（截图需要 Game 视图渲染）。",
                    "确定");
                return;
            }

            OpenInPlayMode();
            CaptureAfterDelay();
        }

        private static void OpenInPlayMode()
        {
            EnsureInGameplay();

            var boot = Object.FindFirstObjectByType<MVPBootstrap>(FindObjectsInactive.Include);
            if (boot == null)
            {
                EditorUtility.DisplayDialog("定位临空商业面板",
                    "未找到 MVPBootstrap。\n请确认已打开主场景「MVP V1.0」并处于 Play 模式。",
                    "确定");
                return;
            }

            // 复现「点战略发展部导航」：隐藏其他部门、激活战略面板、OnOpened
            var showNav = typeof(MVPBootstrap).GetMethod("ShowNavPanel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            showNav?.Invoke(boot, new object[] { StrategyNavIndex });

            // 先清空可能残留的叠层弹窗（如飞机详情 n10），避免 n108 被压在栈底
            var router = NavigationRouter.Instance;
            if (router != null && router.StackDepth > 0)
            {
                int cleared = router.StackDepth;
                router.ClearStack();
                Debug.Log($"[AirlineCEO] 已清空 {cleared} 层叠层（残留弹窗），再打开临空商业子页。");
            }

            // 打开「临空商业 · 非航收入」子页（四设施投资/吞吐/解锁状态）
            StrategyPanelUI.OpenStrategyPage(CommercePageId);

            // 校验：栈顶应为 n108
            if (router != null)
                Debug.Log($"[AirlineCEO] 当前导航栈顶 = {router.CurrentPageId}（期望 n108_临空商业）。");

            // 在 Hierarchy 选中并高亮该子页
            // 注意：n77 航权与时刻槽已内嵌进「航线」模块页签，场景里可能同时存在多个 StrategySubpagePanel，
            // 必须按 PageId 过滤，不能取「第一个」。
            var subs = Object.FindObjectsByType<StrategySubpagePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            StrategySubpagePanel sub = null;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i] != null && subs[i].PageId != null && subs[i].PageId.StartsWith("n108"))
                { sub = subs[i]; break; }
            }
            if (sub != null)
            {
                Selection.activeGameObject = sub.gameObject;
                EditorGUIUtility.PingObject(sub.gameObject);
            }

            Debug.Log("[AirlineCEO] 已定位战略部「临空商业 · 非航收入」子页。");
        }

        /// <summary>
        /// 若仍停在主菜单（SetupOverlay 盖在最上层），关闭它并进入经营态。
        /// 用 fromContinue:true 避免用空 Draft 覆盖公司/枢纽/现金数据。
        /// </summary>
        private static void EnsureInGameplay()
        {
            var setup = Object.FindFirstObjectByType<SetupFlowController>(FindObjectsInactive.Include);
            if (setup == null || setup.IsInGameplay) return;

            var enter = typeof(SetupFlowController).GetMethod("EnterGameplay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (enter == null) return;

            enter.Invoke(setup, new object[] { true, true });
            Debug.Log("[AirlineCEO] 已关闭主菜单并进入经营态（fromContinue）。");
        }

        /// <summary>等淡入动画与子页构建结束后截图，保存到项目根 Screenshots 目录。</summary>
        private static void CaptureAfterDelay()
        {
            float start = Time.realtimeSinceStartup;
            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                if (Time.realtimeSinceStartup - start < 0.8f)
                    return;

                EditorApplication.update -= step;

                string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string dir = System.IO.Path.Combine(projectRoot, "Screenshots");
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                string fullPath = System.IO.Path.Combine(dir, "CommerceDetail.png");

                ScreenCapture.CaptureScreenshot(fullPath);
                Debug.Log("[AirlineCEO] 已保存临空商业面板截图：" + fullPath + "（下一帧写入磁盘）");
            };
            EditorApplication.update += step;
        }

        private static void OpenInEditMode()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(CommerceScriptPath);
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

            EditorUtility.DisplayDialog("定位临空商业面板",
                "临空商业子页在运行时由 StrategyRuntimeFactory 构建，编辑模式下场景内无该对象。\n" +
                "已打开主场景「MVP V1.0」并定位脚本 StrategySubpagePanel.cs。\n" +
                "进入 Play 模式后再次点击本菜单，即可直接切到「临空商业 · 非航收入」子页。",
                "确定");
        }
    }
}
#endif
