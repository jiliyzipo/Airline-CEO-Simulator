#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 独立菜单：清除 ACEO_* 存档，保留 DeepSeek API Key。
    /// </summary>
    public static class ClearGameSaveMenu
    {
        // priority 10：尽量排在 AirlineCEO 菜单靠前位置
        [MenuItem("AirlineCEO/清档 Clear Save", false, 10)]
        public static void ClearGameSave()
        {
            string[] keys =
            {
                "ACEO_Cash", "ACEO_Year", "ACEO_Quarter", "ACEO_BoardSatisfaction",
                "ACEO_FirstFlight", "ACEO_Difficulty", "ACEO_GameOver", "ACEO_GameOverReason",
                "ACEO_TargetsJson", "ACEO_TargetsYear", "ACEO_YearNet",
                "ACEO_ProbationUsed", "ACEO_FailVotes",
                "ACEO_SafetyRating", "ACEO_SafetyLogJson",
                "ACEO_HasAOGTriggered", "ACEO_AOGChoice", "ACEO_AOGAdvisor", "ACEO_OnTimePercent",
                "ACEO_HasExpansionProposalTriggered", "ACEO_ExpansionDelayCount",
                "ACEO_ExpansionAccepted", "ACEO_ExpansionRejected",
                "ACEO_GreatRouteUnlocked", "ACEO_LastExpansionOfferYear",
                "ACEO_DeptHeads",
                "ACEO_FuelPrice", "ACEO_HedgeContracts", "ACEO_ActiveLoans",
                "ACEO_FuelHistoryJson", "ACEO_LoanDefaults",
                "ACEO_HasSeenHedgeTutorial", "ACEO_Fleet"
            };
            for (int i = 0; i < keys.Length; i++)
                PlayerPrefs.DeleteKey(keys[i]);
            PlayerPrefs.Save();

            Debug.Log("[AirlineCEO] 已清除游戏存档 PlayerPrefs（未删除 API Key）。");
            EditorUtility.DisplayDialog(
                "存档已清除",
                "游戏进度已清空。\n请 Stop 后再 Play，从头开一局。\n（DeepSeek API Key 仍保留）",
                "确定");
        }
    }
}
#endif
