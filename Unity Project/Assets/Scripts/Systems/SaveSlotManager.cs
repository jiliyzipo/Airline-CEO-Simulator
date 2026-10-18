using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 多槽位快照（GDB 第31章 · P2 长线运营 · 存档模式）。
    /// 原理：主存档由 SaveManager 逐字段写入 PlayerPrefs；本类把「主存档」整体克隆到
    /// 带前缀的命名空间（slot），从而实现时间胶囊 / 分支存档 / 滚动自动存档，而无需
    /// 重复维护几百行的逐字段序列化代码。
    /// </summary>
    public static class SaveSlotManager
    {
        /// <summary>滚动自动存档节点前缀（季度，保留最近 12 个 = 3 财年）。</summary>
        public const string AutoSlotPrefix = "ACEO_Auto_";
        public const int AutoSlotCount = 12;
        private const string AutoIndexKey = "ACEO_Auto_Index";

        /// <summary>季度初快照槽（失误保护·时间回溯用，每次季度推进前覆盖）。</summary>
        public const string QuarterStartSlot = "ACEO_QuarterStart_";

        public static string SlotPrefixFor(int index) => AutoSlotPrefix + index + "_";

        /// <summary>把当前活动存档整体克隆到 slot 前缀（不覆盖活动存档）。</summary>
        public static void CaptureToSlot(string slotPrefix)
        {
            var keys = SaveManager.AllSaveKeys;
            string active = SaveManager.ActivePrefix;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                string activeKey = active + k;
                if (PlayerPrefs.HasKey(activeKey))
                    PlayerPrefs.SetString(slotPrefix + k, PlayerPrefs.GetString(activeKey, ""));
            }
            PlayerPrefs.Save();
        }

        /// <summary>从 slot 前缀恢复到当前活动存档（调用方随后 SaveManager.Load() 生效）。</summary>
        public static void RestoreFromSlot(string slotPrefix)
        {
            var keys = SaveManager.AllSaveKeys;
            string active = SaveManager.ActivePrefix;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                string slotKey = slotPrefix + k;
                if (PlayerPrefs.HasKey(slotKey))
                    PlayerPrefs.SetString(active + k, PlayerPrefs.GetString(slotKey, ""));
            }
            PlayerPrefs.Save();
        }

        /// <summary>slot 是否存在（用首个键探测）。</summary>
        public static bool SlotExists(string slotPrefix)
        {
            if (SaveManager.AllSaveKeys.Length == 0) return false;
            return PlayerPrefs.HasKey(slotPrefix + SaveManager.AllSaveKeys[0]);
        }

        /// <summary>删除某个 slot 下的全部键。</summary>
        public static void ClearSlot(string slotPrefix)
        {
            var keys = SaveManager.AllSaveKeys;
            for (int i = 0; i < keys.Length; i++)
                PlayerPrefs.DeleteKey(slotPrefix + keys[i]);
            PlayerPrefs.Save();
        }

        /// <summary>写滚动自动存档节点（环形覆盖），返回本次写入的索引 0~11。</summary>
        public static int WriteRollingAutoSlot()
        {
            int idx = PlayerPrefs.GetInt(AutoIndexKey, 0) % AutoSlotCount;
            CaptureToSlot(SlotPrefixFor(idx));
            PlayerPrefs.SetInt(AutoIndexKey, (idx + 1) % AutoSlotCount);
            PlayerPrefs.Save();
            return idx;
        }

        /// <summary>清档：删除所有滚动节点 + 索引 + 时间胶囊 + 分支 + 季度初快照。</summary>
        public static void ClearAllSlots()
        {
            for (int i = 0; i < AutoSlotCount; i++)
                ClearSlot(SlotPrefixFor(i));
            PlayerPrefs.DeleteKey(AutoIndexKey);
            ClearSlot(QuarterStartSlot);
            ArchiveManager.ClearAllArchiveData();
            PlayerPrefs.Save();
        }
    }
}
