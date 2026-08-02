using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 5：管理 12 名董事满意度，并在财年初始生成年度目标。
    /// </summary>
    public class BoardManager : MonoBehaviour
    {
        public static BoardManager Instance { get; private set; }

        /// <summary>董事 ID → 满意度（0～100）。</summary>
        public readonly Dictionary<string, float> memberSatisfaction = new Dictionary<string, float>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureMemberSatisfaction();
        }

        private void Start()
        {
            // 第 1 年 Q1 开局生成目标
            EnsureAnnualTargets();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void EnsureMemberSatisfaction()
        {
            var db = DataBase.Instance;
            if (db == null || db.BoardMembers == null) return;

            for (int i = 0; i < db.BoardMembers.Count; i++)
            {
                var m = db.BoardMembers[i];
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (!memberSatisfaction.ContainsKey(m.id))
                    memberSatisfaction[m.id] = 50f;
            }
        }

        /// <summary>仅在尚无任何目标时生成；年份切换只由 OnNewFiscalYearStarted 负责。</summary>
        public void EnsureAnnualTargets()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            if (gs.currentTargets != null && gs.currentTargets.Count > 0)
            {
                RefreshTargetProgress();
                return;
            }

            GenerateTargetsForYear(gs.year);
        }

        public void GenerateTargetsForYear(int fiscalYear)
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            // 防止同一财年被连续生成两次
            if (gs.currentTargets != null
                && gs.currentTargets.Count > 0
                && gs.targetsFiscalYear == fiscalYear)
            {
                RefreshTargetProgress();
                return;
            }

            EnsureMemberSatisfaction();
            var ids = DataBase.Instance != null
                ? DataBase.Instance.GetBoardMemberIds()
                : new List<string> { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" };

            gs.currentTargets = TargetGenerator.Generate(ids, fiscalYear);
            gs.targetsFiscalYear = fiscalYear;
            gs.yearNetProfit = 0f;
            gs.yearStartCash = gs.cash;
            gs.boardTargetsAnnounced = false;

            UpdateSimulatedKpis(gs);
            TargetGenerator.RefreshProgress(gs.currentTargets, gs);
            gs.AddDecisionLog($"董事会下达第 {fiscalYear} 年考核目标共 {gs.currentTargets.Count} 项。");

            Debug.Log($"[BoardManager] 已生成第 {fiscalYear} 年目标 {gs.currentTargets.Count} 项。");
        }

        public void RefreshTargetProgress()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.currentTargets == null) return;
            UpdateSimulatedKpis(gs);
            TargetGenerator.RefreshProgress(gs.currentTargets, gs);
        }

        /// <summary>MVP：客座率随航线/营收变化；开通盈利航线后应能达到 ≥75% 考核线。</summary>
        public static void UpdateSimulatedKpis(GameState gs)
        {
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;

            // 旧公式：55+10+5+4=74，永远差 1 个点 → 改为可达 75+
            float lf = 60f;
            if (routes >= 1) lf += 12f;
            if (routes >= 2) lf += 6f;
            if (gs.firstFlightCompleted) lf += 4f;
            if (gs.monthlyRevenue > 0f) lf += 6f;
            if (gs.yearNetProfit > 100f) lf += 2f;
            // 1 条赚钱航线：60+12+4+6(+2) ≈ 82~84
            gs.loadFactorPercent = Mathf.Clamp(lf, 40f, 95f);

            // Phase 7：安全指数由 safetyRating 驱动，不再被现金公式覆盖
            gs.SyncSafetyIndexFromRating();
        }

        public void ApplySatisfactionDeltaToMembers(float delta)
        {
            var keys = new List<string>(memberSatisfaction.Keys);
            for (int i = 0; i < keys.Count; i++)
                memberSatisfaction[keys[i]] = Mathf.Clamp(memberSatisfaction[keys[i]] + delta, 0f, 100f);
        }

        /// <summary>进入新年时重新生成目标（续聘成功或见习保护后调用）。</summary>
        public void OnNewFiscalYearStarted(int newYear)
        {
            GenerateTargetsForYear(newYear);
        }
    }
}
