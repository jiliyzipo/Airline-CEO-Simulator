using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 5：管理董事满意度，并在财年初始生成年度目标。
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
            var db = DataBase.Instance;
            // 优先用 n4 抽卡选座选出的董事；未选（空/读档缺字段）则回退全库
            var ids = new List<string>();
            if (gs.boardMemberIds != null && gs.boardMemberIds.Count > 0)
            {
                ids.AddRange(gs.boardMemberIds);
            }
            else if (db != null)
            {
                ids = db.GetBoardMemberIds();
            }
            if (ids.Count == 0)
                ids = new List<string> { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q" };

            gs.currentTargets = TargetGenerator.Generate(ids, fiscalYear);
            gs.targetsFiscalYear = fiscalYear;
            gs.yearNetProfit = 0f;
            gs.yearStartCash = gs.cash;
            gs.boardTargetsAnnounced = false;
            // 新年目标需重新确认；不清零 confirmedYear 以免误把旧年当成已确认

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

        public bool ConfirmAnnualGoals()
        {
            var gs = GameState.Instance;
            if (gs == null) return false;
            EnsureAnnualTargets();
            if (gs.currentTargets == null || gs.currentTargets.Count == 0) return false;
            gs.annualGoalsConfirmedYear = gs.year;
            gs.AddDecisionLog($"愿景：确认第 {gs.year} 财年年度目标。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>进入新年时重新生成目标（续聘成功或见习保护后调用）。</summary>
        public void OnNewFiscalYearStarted(int newYear)
        {
            GenerateTargetsForYear(newYear);
            InboxManager.MaybeOfferAnnualGoals(GameState.Instance);
        }
    }
}
