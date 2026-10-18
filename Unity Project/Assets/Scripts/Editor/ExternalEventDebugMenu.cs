#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class ExternalEventDebugMenu
    {
        [MenuItem("AirlineCEO/注入演示外部事件（先证映射）", false, 80)]
        public static void IngestDemo()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("外部事件", "请在 Play 模式下使用。", "确定");
                return;
            }
            var gs = GameState.Instance;
            int n = ExternalEventManager.ReceiveMany(gs, ExternalDataFetcher.DemoPayloads());
            ExternalEventManager.Tick(gs, gs.SimUtc, gs.SimUtc);
            EditorUtility.DisplayDialog("外部事件",
                $"已接入 {n} 条演示事件，已映射到游戏日历 {gs.GameLocalDateTimeLabel}。\n"
                + "预告在收件箱；到生效时刻会自动改油价/冻结机场。",
                "确定");
        }
    }
}
#endif
