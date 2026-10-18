using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// SAF 安全质量部系统（Phase 2 Week 10）：SMS/监察/事件调查/局方关系/安全文化/预算。
    /// 全部回写 GameState.saf。
    /// </summary>
    public class SafetyManager : MonoBehaviour
    {
        public static SafetyManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static SafetyState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.saf == null) gs.saf = new SafetyState();
            if (gs.saf.regulators == null) gs.saf.regulators = new List<RegulatorRelation>();
            if (gs.saf.investigations == null) gs.saf.investigations = new List<SafetyInvestigation>();
            if (gs.saf.regulators.Count == 0)
            {
                gs.saf.regulators.Add(new RegulatorRelation { regulator = "CAAC", status = 2 });
                gs.saf.regulators.Add(new RegulatorRelation { regulator = "FAA", status = 1 });
                gs.saf.regulators.Add(new RegulatorRelation { regulator = "EASA", status = 1 });
            }
            return gs.saf;
        }

        #region 安全文化

        /// <summary>切换安全文化：0惩罚型 1鼓励报告型。</summary>
        public bool SetCultureType(int cultureType, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var saf = EnsureState(gs);
            saf.cultureType = cultureType;
            saf.cultureIndex = cultureType == 1 ? Mathf.Max(saf.cultureIndex, 60f) : saf.cultureIndex;
            message = cultureType == 1
                ? "已切换为「鼓励报告型」文化（透明度↑ · 报告率↑ · 隐藏风险↓）。"
                : "已切换为「惩罚型」文化（短期合规↑ · 但隐瞒风险↑）。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string CultureName(int type) => type == 1 ? "鼓励报告型" : "惩罚型";

        #endregion

        #region 局方关系

        public bool ImproveRegulator(string regulator, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var saf = EnsureState(gs);
            var rel = saf.regulators.Find(r => r.regulator == regulator);
            if (rel == null) { message = "监管机构不存在"; return false; }
            if (rel.status >= 3) { message = $"{regulator} 关系已是最佳。"; return false; }
            float cost = SafetyEconomics.RegulatorImproveCostWan(rel.status);
            if (gs.cash < cost) { message = $"改善 {regulator} 关系需 {cost:F0} 万。"; return false; }
            gs.cash -= cost;
            rel.status++;
            message = $"{regulator} 关系提升至 {StatusName(rel.status)}（花费 {cost:F0} 万）。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string StatusName(int status) => status switch { 0 => "红（恶劣）", 1 => "橙（紧张）", 2 => "黄（一般）", _ => "绿（良好）" };

        /// <summary>局方检查三选一：改评级分、局方关系，并同步 GameState.safetyRating。</summary>
        public bool RespondInspection(string regulator, int choice, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var saf = EnsureState(gs);
            SafetyEconomics.InspectionChoice(choice, out float cost, out float dScore, out int dRel, out string label);
            if (gs.cash < cost) { message = $"现金不足，{label} 需 {cost:F0} 万。"; return false; }
            gs.cash -= cost;
            saf.ratingScore = Mathf.Clamp(saf.ratingScore + dScore, 0f, 100f);
            saf.auditPassRate = Mathf.Clamp(saf.auditPassRate + (choice == 0 ? 2f : choice == 1 ? 4f : -3f), 0f, 100f);
            if (choice == 1) saf.rectificationRate = Mathf.Clamp(saf.rectificationRate + 5f, 0f, 100f);
            string reg = string.IsNullOrEmpty(regulator) ? "CAAC" : regulator;
            var rel = saf.regulators.Find(r => r.regulator == reg);
            if (rel == null && saf.regulators.Count > 0) rel = saf.regulators[0];
            if (rel != null)
                rel.status = Mathf.Clamp(rel.status + dRel, 0, 3);
            SyncRating(gs, saf);
            string relTxt = rel != null ? $" · {rel.regulator} {StatusName(rel.status)}" : "";
            message = $"局方检查已按「{label}」应对（{cost:F0} 万）。安全评级 {SafetyEconomics.RatingBand(saf.ratingScore)}（{saf.ratingScore:F0}）{relTxt}。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static void SyncRating(GameState gs, SafetyState saf)
        {
            if (gs == null || saf == null) return;
            char b = SafetyEconomics.RatingBand(saf.ratingScore);
            gs.safetyRating = b == 'A' ? SafetyRating.A : b == 'B' ? SafetyRating.B : b == 'C' ? SafetyRating.C : SafetyRating.D;
            gs.safetyIndex = saf.ratingScore;
        }

        #endregion

        #region 事件调查

        public bool LaunchInvestigation(int severity, int depth, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var saf = EnsureState(gs);
            float cost = SafetyEconomics.InvestigationCostWan(depth, severity);
            if (gs.cash < cost) { message = $"现金不足（调查需 {cost:F0} 万）。"; return false; }
            gs.cash -= cost;
            var inv = new SafetyInvestigation
            {
                id = "INV-" + (saf.investigations.Count + 1).ToString("D2"),
                severity = severity,
                depth = depth,
                status = "调查中"
            };
            saf.investigations.Add(inv);
            message = $"已启动{SafetyEconomics.SeverityName(severity)} · {SafetyEconomics.DepthName(depth)}（{inv.id} · {cost:F0} 万 · 约 {SafetyEconomics.InvestigationDays(depth)} 天）。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool ResolveInvestigation(string id, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var saf = EnsureState(gs);
            var inv = saf.investigations.Find(i => i.id == id);
            if (inv == null) { message = "调查不存在"; return false; }
            if (inv.status == "已结案") { message = "该调查已结案。"; return false; }
            inv.status = "已结案";
            // 结案后小幅提升安全评级（深度调查提升更多）
            float gain = inv.depth switch { 0 => 0.5f, 1 => 1f, _ => 2f };
            saf.ratingScore = Mathf.Clamp(saf.ratingScore + gain, 0f, 100f);
            if (gs != null) gs.safetyIndex = Mathf.Clamp(gs.safetyIndex + gain, 0f, 100f);
            message = $"调查 {inv.id} 已结案，安全评级 +{gain:F1}（当前 {saf.ratingScore:F0} · {SafetyEconomics.RatingBand(saf.ratingScore)} 级）。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 安全预算

        public bool AllocateSafetyBudget(float amountWan, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (amountWan <= 0f) { message = "预算必须 >0"; return false; }
            var saf = EnsureState(gs);
            if (gs.cash < amountWan) { message = $"现金不足（需 {amountWan:F0} 万）。"; return false; }
            gs.cash -= amountWan;
            saf.safetyBudgetWan += amountWan;
            saf.ratingScore = Mathf.Clamp(saf.ratingScore + amountWan / 100f, 0f, 100f);
            HrManager.EnqueueWithRoster(gs, "安全文化培训", 8, amountWan, "safety", "SAF");
            message = $"已追加安全预算 {amountWan:F0} 万（评级 +{amountWan / 100f:F1}）。推进季度后安全岗潜力/效率回写档案。";
            gs.AddDecisionLog("安全：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion
    }
}
