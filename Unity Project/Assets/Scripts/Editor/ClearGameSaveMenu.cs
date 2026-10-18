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
        /// <summary>
        /// 已废弃字段的遗留 PlayerPrefs 键 —— **一次性清理**。
        ///
        /// 这些键已无任何代码读写，只会作为孤儿数据永久留在玩家机器上（既不参与存档，清档也不会碰它）。
        /// 待全部存档都清过一轮后，本数组与 <see cref="CleanLegacyKeys"/> 可整体删除。
        /// </summary>
        static readonly string[] LegacyKeys =
        {
            // 2026-10-08 部门强制教程整体移除（取消 GDB 28.2 的强制引导），
            // 该键保存「已读部门页 id 列表」，随之作废。
            "ACEO_DeptTutorialSeen"
        };

        /// <summary>
        /// 一次性清理遗留键。可单独执行，**不会**动任何现存存档字段，
        /// 因此不必为了清它而牺牲进度；也可从本菜单的「清档」一并触发。
        /// </summary>
        [MenuItem("AirlineCEO/清理遗留键 Clean Legacy Keys", false, 11)]
        public static void CleanLegacyKeys()
        {
            int removed = DeleteLegacyKeys();
            PlayerPrefs.Save();

            Debug.Log($"[AirlineCEO] 遗留键清理完成：删除 {removed} / {LegacyKeys.Length} 个（{string.Join(", ", LegacyKeys)}）");
            EditorUtility.DisplayDialog(
                "遗留键清理",
                removed > 0
                    ? $"已清理 {removed} 个废弃键：\n{string.Join("\n", LegacyKeys)}\n\n（未触碰任何现存存档字段）"
                    : "未发现遗留键，无需清理。",
                "确定");
        }

        /// <summary>删除遗留键，返回实际删除数量（存在才算删到）。</summary>
        static int DeleteLegacyKeys()
        {
            int removed = 0;
            for (int i = 0; i < LegacyKeys.Length; i++)
            {
                if (PlayerPrefs.HasKey(LegacyKeys[i])) removed++;
                PlayerPrefs.DeleteKey(LegacyKeys[i]);
            }
            return removed;
        }

        // priority 10：尽量排在 AirlineCEO 菜单靠前位置
        [MenuItem("AirlineCEO/清档 Clear Save", false, 10)]
        public static void ClearGameSave()
        {
            // 直接消费 SaveManager 的权威键表（AllSaveKeys），不再在本文件另维护一份副本。
            // 硬编码副本此前已经漂移：它漏掉了 19 个键（ACEO_StaffPool / ACEO_StaffRecruits /
            // ACEO_UsedMarket / ACEO_CityEconomy / ACEO_UsdCny / ACEO_SpeedLevel / ACEO_FlightRings …），
            // 导致「清档」后人事池、汇率、本季度收支与二手市场仍带着上一局的数据重新开局。
            // 同时它也漏了本次新增的 ACEO_Hr。改读权威表后，新增存档字段只需在 SaveManager 登记一次。
            string[] keys = AirlineCEO.Systems.SaveManager.AllSaveKeys;
            for (int i = 0; i < keys.Length; i++)
                PlayerPrefs.DeleteKey(keys[i]);

            // 顺带清掉已废弃字段的遗留键（本类顶部 LegacyKeys，一次性清理）
            int legacyRemoved = DeleteLegacyKeys();

            // 多槽位快照：滚动自动存档 / 时间胶囊 / What-If 分支 / 主线备份
            AirlineCEO.Systems.SaveSlotManager.ClearAllSlots();
            PlayerPrefs.Save();

            Debug.Log($"[AirlineCEO] 已清除游戏存档 PlayerPrefs（未删除 API Key）；另清理遗留键 {legacyRemoved} 个。");
            EditorUtility.DisplayDialog(
                "存档已清除",
                "游戏进度已清空。\n请 Stop 后再 Play，从头开一局。\n（DeepSeek API Key 仍保留）",
                "确定");
        }
    }
}
#endif
