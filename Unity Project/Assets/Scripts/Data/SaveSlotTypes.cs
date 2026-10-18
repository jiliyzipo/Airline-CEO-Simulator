using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 存档与 What-If 分支数据（GDB 第31章 / 26.3 · P2 长线运营）。
    /// 定义「时间胶囊」与「分支存档」的元数据，用于历史回放时间轴展示与分支创建。
    /// </summary>

    /// <summary>时间胶囊元数据（每财年末自动生成，永久保留）。</summary>
    [Serializable]
    public class TimeCapsuleMeta
    {
        /// <summary>胶囊唯一 id（如 Capsule_3，财年编号）。</summary>
        public string id = "";
        /// <summary>生成时的财年（已结算完成的年份）。</summary>
        public int year;
        /// <summary>生成时日期标签（如「第 3 年 Q4」）。</summary>
        public string dateLabel = "";
        /// <summary>生成时现金（万美元）。</summary>
        public float cashWan;
        /// <summary>生成时简化市值（万美元，用于时间轴快照对比）。</summary>
        public float marketCapWan;
        /// <summary>生成时董事会满意度（0~100）。</summary>
        public float boardSatisfaction;
        /// <summary>该财年目标完成数 / 总数（如「3/5」）。</summary>
        public string targetsSummary = "";
        /// <summary>生成时安全评级字母。</summary>
        public string safetyGrade = "";
        /// <summary>生成时间戳（真实 UTC，仅排序/展示用）。</summary>
        public long createdAtUtc;
        /// <summary>底层快照存储的 slot 前缀（用于恢复完整状态）。</summary>
        public string slotPrefix = "";
    }

    /// <summary>What-If 分支存档元数据（从时间胶囊派生，独立于主线）。</summary>
    [Serializable]
    public class BranchSaveMeta
    {
        /// <summary>分支唯一 id。</summary>
        public string id = "";
        /// <summary>来源时间胶囊 id。</summary>
        public string capsuleId = "";
        /// <summary>分支创建时间戳（真实 UTC）。</summary>
        public long createdAtUtc;
        /// <summary>分支起始日期标签。</summary>
        public string dateLabel = "";
        /// <summary>分支起始现金（万美元）。</summary>
        public float cashWan;
        /// <summary>底层快照存储的 slot 前缀。</summary>
        public string slotPrefix = "";
    }

    /// <summary>时间胶囊列表包装（JsonUtility 序列化用）。</summary>
    [Serializable]
    public class TimeCapsuleListSave
    {
        public List<TimeCapsuleMeta> items = new List<TimeCapsuleMeta>();
    }

    /// <summary>分支存档列表包装（JsonUtility 序列化用）。</summary>
    [Serializable]
    public class BranchListSave
    {
        public List<BranchSaveMeta> items = new List<BranchSaveMeta>();
    }
}
