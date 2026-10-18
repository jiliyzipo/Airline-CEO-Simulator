using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 时间胶囊 + What-If 分支（GDB 26.3 / 第31章 · P2 长线运营 · 存档模式）。
    ///   · 时间胶囊：每财年末自动生成永久快照，供「历史回放」时间轴展示。
    ///   · What-If 分支：从胶囊创建独立分支存档；创建时自动备份主线，分支游玩不影响主线，
    ///     之后可「返回主线」恢复到创建分支前的进度。
    /// MVP 简化：分支内游玩仍写入主存档链（完整差分隔离为后置优化）。
    /// </summary>
    public static class ArchiveManager
    {
        public const string CapsuleListKey = "ACEO_Capsules";
        public const string BranchListKey = "ACEO_Branches";
        public const string CapsuleSlotPrefix = "ACEO_Capsule_";  // + year + "_"
        public const string BranchSlotPrefix = "ACEO_Branch_";    // + id + "_"
        public const string MainlineBackupSlot = "ACEO_Mainline_";
        public const int MaxCapsules = 20;

        // ———— 时间胶囊 ————

        /// <summary>财年末生成时间胶囊快照；返回新胶囊元数据（失败返回 null）。</summary>
        public static TimeCapsuleMeta CaptureTimeCapsule(GameState gs)
        {
            if (gs == null) return null;

            // 确保主存档已落盘（铁人/普通模式均一致），随后克隆为胶囊快照
            if (SaveManager.Instance != null) SaveManager.Instance.Save();

            if (BoardManager.Instance != null) BoardManager.Instance.RefreshTargetProgress();

            int done = 0, total = 0;
            if (gs.currentTargets != null)
            {
                total = gs.currentTargets.Count;
                for (int i = 0; i < gs.currentTargets.Count; i++)
                    if (gs.currentTargets[i] != null && gs.currentTargets[i].completed) done++;
            }

            var meta = new TimeCapsuleMeta
            {
                id = "Capsule_" + gs.year,
                year = gs.year,
                dateLabel = gs.DateLabel,
                cashWan = gs.cash,
                marketCapWan = LegacyScoreCalculator.MarketCap(LegacySnapshotBuilder.FromGameState(gs)),
                boardSatisfaction = gs.boardSatisfaction,
                targetsSummary = $"{done}/{total}",
                safetyGrade = gs.safetyRating.ToString(),
                createdAtUtc = DateTime.UtcNow.Ticks,
                slotPrefix = CapsuleSlotPrefix + gs.year + "_"
            };

            SaveSlotManager.CaptureToSlot(meta.slotPrefix);

            var list = ListCapsules();
            // 同财年覆盖；否则追加并裁剪上限
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == meta.id) { list[i] = meta; return meta; }
            list.Add(meta);
            if (list.Count > MaxCapsules) list.RemoveAt(0);
            SaveCapsuleList(list);

            Debug.Log($"[ArchiveManager] 时间胶囊已生成：第 {meta.year} 年 · 现金 {meta.cashWan:F0} 万 · 市值 {meta.marketCapWan:F0} 万 · 目标 {meta.targetsSummary}");
            return meta;
        }

        public static List<TimeCapsuleMeta> ListCapsules()
        {
            try
            {
                var blob = JsonUtility.FromJson<TimeCapsuleListSave>(PlayerPrefs.GetString(CapsuleListKey, ""));
                return blob?.items ?? new List<TimeCapsuleMeta>();
            }
            catch { return new List<TimeCapsuleMeta>(); }
        }

        static void SaveCapsuleList(List<TimeCapsuleMeta> list)
        {
            PlayerPrefs.SetString(CapsuleListKey, JsonUtility.ToJson(new TimeCapsuleListSave { items = list }));
            PlayerPrefs.Save();
        }

        public static TimeCapsuleMeta FindCapsule(string capsuleId)
        {
            var list = ListCapsules();
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == capsuleId) return list[i];
            return null;
        }

        // ———— What-If 分支 ————

        /// <summary>从胶囊创建分支：备份主线 → 克隆胶囊为独立分支 → 载入分支（在该节点重新决策）。</summary>
        public static BranchSaveMeta CreateBranch(GameState gs, string capsuleId)
        {
            var cap = FindCapsule(capsuleId);
            if (cap == null)
            {
                Debug.LogWarning($"[ArchiveManager] 分支创建失败：未找到胶囊 {capsuleId}");
                return null;
            }

            // 1) 备份当前主线进度（玩家可稍后「返回主线」）；强制主线命名空间再备份
            SaveManager.ActivePrefix = "";
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
            SaveSlotManager.CaptureToSlot(MainlineBackupSlot);

            // 2) 克隆胶囊为独立分支 slot
            string branchId = "B" + DateTime.UtcNow.Ticks;
            string branchSlot = BranchSlotPrefix + branchId + "_";
            RestoreSlotFrom(cap.slotPrefix, branchSlot);

            var meta = new BranchSaveMeta
            {
                id = branchId,
                capsuleId = capsuleId,
                createdAtUtc = DateTime.UtcNow.Ticks,
                dateLabel = cap.dateLabel,
                cashWan = cap.cashWan,
                slotPrefix = branchSlot
            };

            var branches = ListBranches();
            branches.Add(meta);
            SaveBranchList(branches);

            Debug.Log($"[ArchiveManager] What-If 分支已创建：{branchId}（源自 {capsuleId} · {cap.dateLabel}）");

            // 3) 载入分支，进入「在该节点重新决策」
            LoadBranch(branchId);
            return meta;
        }

        /// <summary>从 slot 克隆到另一 slot（不触碰主存档）。</summary>
        static void RestoreSlotFrom(string fromPrefix, string toPrefix)
        {
            var keys = SaveManager.AllSaveKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                string fromKey = fromPrefix + k;
                if (PlayerPrefs.HasKey(fromKey))
                    PlayerPrefs.SetString(toPrefix + k, PlayerPrefs.GetString(fromKey, ""));
            }
            PlayerPrefs.Save();
        }

        public static List<BranchSaveMeta> ListBranches()
        {
            try
            {
                var blob = JsonUtility.FromJson<BranchListSave>(PlayerPrefs.GetString(BranchListKey, ""));
                return blob?.items ?? new List<BranchSaveMeta>();
            }
            catch { return new List<BranchSaveMeta>(); }
        }

        static void SaveBranchList(List<BranchSaveMeta> list)
        {
            PlayerPrefs.SetString(BranchListKey, JsonUtility.ToJson(new BranchListSave { items = list }));
            PlayerPrefs.Save();
        }

        public static BranchSaveMeta FindBranch(string branchId)
        {
            var list = ListBranches();
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == branchId) return list[i];
            return null;
        }

        /// <summary>载入分支：切换到分支命名空间并读档（分支 slot 即其活动存档）。</summary>
        public static bool LoadBranch(string branchId)
        {
            var b = FindBranch(branchId);
            if (b == null) return false;
            SaveManager.ActivePrefix = b.slotPrefix;
            if (SaveManager.Instance != null) SaveManager.Instance.Load();
            return true;
        }

        /// <summary>直接回溯胶囊到主线（不创建分支）。</summary>
        public static bool LoadCapsuleIntoMain(string capsuleId)
        {
            var cap = FindCapsule(capsuleId);
            if (cap == null) return false;
            SaveManager.ActivePrefix = "";
            SaveSlotManager.RestoreFromSlot(cap.slotPrefix);
            if (SaveManager.Instance != null) SaveManager.Instance.Load();
            return true;
        }

        /// <summary>从分支返回主线：恢复创建分支前备份的主线进度。</summary>
        public static bool ReturnToMainline()
        {
            if (!SaveSlotManager.SlotExists(MainlineBackupSlot)) return false;
            SaveManager.ActivePrefix = "";
            SaveSlotManager.RestoreFromSlot(MainlineBackupSlot);
            if (SaveManager.Instance != null) SaveManager.Instance.Load();
            return true;
        }

        // ———— 清档 ————

        public static void ClearAllArchiveData()
        {
            var caps = ListCapsules();
            for (int i = 0; i < caps.Count; i++)
                SaveSlotManager.ClearSlot(caps[i].slotPrefix);
            var branches = ListBranches();
            for (int i = 0; i < branches.Count; i++)
                SaveSlotManager.ClearSlot(branches[i].slotPrefix);
            SaveSlotManager.ClearSlot(MainlineBackupSlot);
            PlayerPrefs.DeleteKey(CapsuleListKey);
            PlayerPrefs.DeleteKey(BranchListKey);
            PlayerPrefs.Save();
        }
    }
}
