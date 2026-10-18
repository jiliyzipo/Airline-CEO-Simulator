#if UNITY_EDITOR
using System.Reflection;
using AirlineCEO.UI;
using AirlineCEO.UI.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 一键定位/打开财务部面板。
    /// 菜单：AirlineCEO → 定位财务部面板
    /// </summary>
    public static class OpenFinancePanelMenu
    {
        private const string MainScenePath = "Assets/MVP V1.0.unity";
        private const string FinanceScriptPath = "Assets/Scripts/UI/FinancePanelUI.cs";
        private const int FinanceNavIndex = 5;

        [MenuItem("AirlineCEO/定位财务部面板", false, 11)]
        public static void OpenFinancePanel()
        {
            if (EditorApplication.isPlaying)
                OpenInPlayMode();
            else
                OpenInEditMode();
        }

        [MenuItem("AirlineCEO/定位财务部面板 + 截图", false, 12)]
        public static void OpenFinancePanelAndCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("定位财务部面板 + 截图",
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

            // 财务面板初始被 HideAllLegacyCenterPanels 隐藏，必须包含未激活对象查找
            var fin = Object.FindFirstObjectByType<FinancePanelUI>(FindObjectsInactive.Include);
            if (fin == null)
            {
                EditorUtility.DisplayDialog("定位财务部面板",
                    "未找到 FinancePanelUI 实例。\n请确认已打开主场景「MVP V1.0」并处于 Play 模式。",
                    "确定");
                return;
            }

            // 复现「点财务部导航」：隐藏其他部门、激活财务面板、OnOpened
            var boot = Object.FindFirstObjectByType<MVPBootstrap>(FindObjectsInactive.Include);
            if (boot != null)
            {
                var showNav = typeof(MVPBootstrap).GetMethod("ShowNavPanel",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                showNav?.Invoke(boot, new object[] { FinanceNavIndex });
            }
            else
            {
                fin.gameObject.SetActive(true);
                fin.OnOpened();
            }

            // 切到「财务细分」标签（税务/保险/PDP/碳合规 四宫格）
            var showDetail = typeof(FinancePanelUI).GetMethod("ShowDetail",
                BindingFlags.Instance | BindingFlags.NonPublic);
            showDetail?.Invoke(fin, null);

            // 在 Hierarchy 选中并高亮该面板
            Selection.activeGameObject = fin.gameObject;
            EditorGUIUtility.PingObject(fin.gameObject);

            Debug.Log("[AirlineCEO] 已定位财务部面板并切到「财务细分」标签。");
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

        /// <summary>等淡入动画结束后截图，保存到项目根 Screenshots 目录。</summary>
        private static void CaptureAfterDelay()
        {
            float start = Time.realtimeSinceStartup;
            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                if (Time.realtimeSinceStartup - start < 0.6f)
                    return;

                EditorApplication.update -= step;

                // 项目根 = Assets 的上一级；绝对路径避免工作目录差异
                string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string dir = System.IO.Path.Combine(projectRoot, "Screenshots");
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                string fullPath = System.IO.Path.Combine(dir, "FinanceDetail.png");

                ScreenCapture.CaptureScreenshot(fullPath);
                Debug.Log("[AirlineCEO] 已保存财务部面板截图：" + fullPath + "（下一帧写入磁盘）");
            };
            EditorApplication.update += step;
        }

        private static void OpenInEditMode()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(FinanceScriptPath);
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

            EditorUtility.DisplayDialog("定位财务部面板",
                "财务部面板在运行时由 MVPBootstrap 构建，编辑模式下场景内无该对象。\n" +
                "已打开主场景「MVP V1.0」并定位脚本 FinancePanelUI.cs。\n" +
                "进入 Play 模式后再次点击本菜单，即可直接切到「财务细分」标签。",
                "确定");
        }
    }
}
#endif
