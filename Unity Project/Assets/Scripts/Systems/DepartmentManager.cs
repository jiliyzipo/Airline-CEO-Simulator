using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 8 · 部门任命 / 效率 / 忠诚度 / 管理化学反应（GDD 第 6 章）。
    /// </summary>
    public class DepartmentManager : MonoBehaviour
    {
        public static DepartmentManager Instance { get; private set; }

        public const float MinEfficiency = 0.7f;
        public const float MaxEfficiency = 1.3f;
        public const float ResignLoyaltyThreshold = 30f;
        public const float KpiQuarterBonus = 0.05f;

        private readonly Dictionary<string, DepartmentRuntime> _depts =
            new Dictionary<string, DepartmentRuntime>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _activeChemistry = new List<string>();

        public event Action OnDepartmentsChanged;
        public event Action<string, string> OnResignation; // dept, executiveName

        public IReadOnlyList<string> ActiveChemistry => _activeChemistry;

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureDepartments();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void EnsureDepartments()
        {
            for (int i = 0; i < DepartmentCatalog.Codes.Length; i++)
            {
                string code = DepartmentCatalog.Codes[i];
                if (!_depts.ContainsKey(code))
                    _depts[code] = new DepartmentRuntime { deptCode = code };
            }
        }

        #endregion

        #region Query

        public DepartmentRuntime GetDept(string code)
        {
            EnsureDepartments();
            _depts.TryGetValue(code, out var d);
            return d;
        }

        public ExecutiveData GetHead(string deptCode)
        {
            var d = GetDept(deptCode);
            if (d == null || string.IsNullOrEmpty(d.headId) || DataBase.Instance == null)
                return null;
            return DataBase.Instance.GetExecutive(d.headId);
        }

        public bool IsAppointed(string deptCode)
        {
            var d = GetDept(deptCode);
            return d != null && !string.IsNullOrEmpty(d.headId);
        }

        public List<ExecutiveData> GetCandidates(string deptCode, int count = 2)
        {
            var pool = new List<ExecutiveData>();
            if (DataBase.Instance == null) return pool;
            var all = DataBase.Instance.Executives;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null) continue;
                if (!string.Equals(e.department, deptCode, StringComparison.OrdinalIgnoreCase)) continue;
                if (_removedIds.Contains(e.id)) continue;
                // 已被其他部门任命的不出现
                if (IsHeadSomewhere(e.id)) continue;
                pool.Add(e);
            }

            // 打乱取前 count
            for (int i = 0; i < pool.Count; i++)
            {
                int j = UnityEngine.Random.Range(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            if (pool.Count > count)
                pool.RemoveRange(count, pool.Count - count);
            return pool;
        }

        private bool IsHeadSomewhere(string execId)
        {
            foreach (var kv in _depts)
            {
                if (string.Equals(kv.Value.headId, execId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        #region Appoint / Replace

        public bool Appoint(string deptCode, string executiveId)
        {
            var gs = GameState.Instance;
            var exec = DataBase.Instance != null ? DataBase.Instance.GetExecutive(executiveId) : null;
            if (gs == null || exec == null) return false;
            if (!string.Equals(exec.department, deptCode, StringComparison.OrdinalIgnoreCase)) return false;
            if (_removedIds.Contains(executiveId)) return false;

            var dept = GetDept(deptCode);
            if (dept == null) return false;
            if (!string.IsNullOrEmpty(dept.headId)) return false;

            dept.headId = executiveId;
            dept.loyalty = exec.loyalty;
            dept.salary = exec.baseSalary;
            dept.kpiBonus = 0f;
            dept.kpiMetLastQuarter = false;
            RecalcEfficiency(dept);
            RefreshChemistry();

            gs.AddDecisionLog($"人事：任命 {exec.fullName} 为{DepartmentCatalog.DisplayName(deptCode)}负责人（年薪 {dept.salary:F0} 万）。");
            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
            return true;
        }

        /// <summary>更换：先遣散再空缺（由 UI 随后打开任命）。</summary>
        public float Dismiss(string deptCode)
        {
            var gs = GameState.Instance;
            var dept = GetDept(deptCode);
            if (gs == null || dept == null || string.IsNullOrEmpty(dept.headId)) return 0f;

            var exec = DataBase.Instance?.GetExecutive(dept.headId);
            string name = exec != null ? exec.fullName : dept.headId;
            float severance = CalcSeverance(dept.salary, gs.quarter);
            gs.cash -= severance;
            gs.yearNetProfit -= severance;

            _removedIds.Add(dept.headId);
            dept.headId = "";
            dept.loyalty = 0f;
            dept.efficiency = 0f;
            dept.kpiBonus = 0f;

            RefreshChemistry();
            gs.AddDecisionLog(
                $"人事：遣散 {name}（{DepartmentCatalog.DisplayName(deptCode)}），遣散费 -{severance:F0} 万。部门空缺。");
            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
            return severance;
        }

        public static float CalcSeverance(float annualSalary, int currentQuarter)
        {
            int remainingQ = Mathf.Clamp(5 - currentQuarter, 1, 4); // 含本季
            return annualSalary * (remainingQ / 4f) * 0.5f;
        }

        #endregion

        #region Efficiency / Culture

        public void RecalcAllEfficiencies()
        {
            EnsureDepartments();
            foreach (var kv in _depts)
                RecalcEfficiency(kv.Value);
            RefreshChemistry();
            OnDepartmentsChanged?.Invoke();
        }

        public void RecalcEfficiency(DepartmentRuntime dept)
        {
            if (dept == null || string.IsNullOrEmpty(dept.headId))
            {
                if (dept != null) dept.efficiency = 0f;
                return;
            }

            var exec = DataBase.Instance?.GetExecutive(dept.headId);
            if (exec == null)
            {
                dept.efficiency = MinEfficiency;
                return;
            }

            float cultureFit = GetCultureFit(exec.nationality);
            float core = exec.efficiencyBonus * 0.5f
                         + (dept.loyalty / 100f) * 0.3f
                         + cultureFit * 0.2f;
            float eff = 0.7f + 0.3f * core;
            eff += dept.kpiBonus;
            // 化学反应加成
            eff *= GetChemistryMultiplier(dept.deptCode);
            dept.efficiency = Mathf.Clamp(eff, MinEfficiency, MaxEfficiency);
        }

        /// <summary>CEO 文化圈适配 0.8~1.2。</summary>
        public static float GetCultureFit(string execNationality)
        {
            string ceo = GameState.Instance != null ? GameState.Instance.ceoNationality : "中国";
            string a = CultureCircle(ceo);
            string b = CultureCircle(execNationality);
            if (string.Equals(ceo, execNationality, StringComparison.OrdinalIgnoreCase))
                return 1.2f;
            if (a == b && a != "Other") return 1.05f; // 同文化圈约 +5%
            return 0.8f;
        }

        private static string CultureCircle(string nationality)
        {
            if (string.IsNullOrEmpty(nationality)) return "Other";
            if (ContainsAny(nationality, "中国", "马来", "斯里", "孟加", "巴基斯坦")) return "Asia";
            if (ContainsAny(nationality, "瑞典", "丹麦", "芬兰", "冰岛", "爱沙", "比利时")) return "NordicEU";
            if (ContainsAny(nationality, "约旦", "黎巴嫩", "卡塔尔", "突尼斯", "阿尔及", "阿曼")) return "MENA";
            if (ContainsAny(nationality, "加纳", "塞内", "毛里求斯")) return "Africa";
            if (ContainsAny(nationality, "乌拉圭")) return "LatAm";
            if (ContainsAny(nationality, "阿尔巴尼亚")) return "Balkan";
            return "Other";
        }

        private static bool ContainsAny(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (s.Contains(keys[i])) return true;
            return false;
        }

        #endregion

        #region Chemistry M-01 / M-02 / M-05

        public void RefreshChemistry()
        {
            _activeChemistry.Clear();
            bool ops = IsAppointed("OPS");
            bool mnt = IsAppointed("MNT");
            bool saf = IsAppointed("SAF");
            bool mkt = IsAppointed("MKT");
            bool fin = IsAppointed("FIN");

            if (ops && mnt && saf) _activeChemistry.Add("M-01");
            if (mkt && ops && fin) _activeChemistry.Add("M-02");
            if (ops && mnt && fin) _activeChemistry.Add("M-05");

            // 效率含化学乘区，需重算
            foreach (var kv in _depts)
            {
                if (!string.IsNullOrEmpty(kv.Value.headId))
                    RecalcEfficiencyRaw(kv.Value);
            }
        }

        private void RecalcEfficiencyRaw(DepartmentRuntime dept)
        {
            var exec = DataBase.Instance?.GetExecutive(dept.headId);
            if (exec == null) { dept.efficiency = MinEfficiency; return; }
            float cultureFit = GetCultureFit(exec.nationality);
            float core = exec.efficiencyBonus * 0.5f + (dept.loyalty / 100f) * 0.3f + cultureFit * 0.2f;
            float eff = 0.7f + 0.3f * core + dept.kpiBonus;
            eff *= GetChemistryMultiplier(dept.deptCode);
            dept.efficiency = Mathf.Clamp(eff, MinEfficiency, MaxEfficiency);
        }

        private float GetChemistryMultiplier(string deptCode)
        {
            float m = 1f;
            for (int i = 0; i < _activeChemistry.Count; i++)
            {
                switch (_activeChemistry[i])
                {
                    case "M-01": // 安全运行效率 +15%
                        if (deptCode == "OPS" || deptCode == "MNT" || deptCode == "SAF")
                            m *= 1.15f;
                        break;
                    case "M-02": // 收入实现 +12%
                        if (deptCode == "MKT" || deptCode == "OPS" || deptCode == "FIN")
                            m *= 1.12f;
                        break;
                    case "M-05": // 成本削减 +10%
                        if (deptCode == "OPS" || deptCode == "MNT" || deptCode == "FIN")
                            m *= 1.10f;
                        break;
                }
            }
            return m;
        }

        public static string ChemistryLabel(string id)
        {
            return id switch
            {
                "M-01" => "M-01 安全铁三角：安全运行效率 +15%",
                "M-02" => "M-02 商业引擎：收入实现效率 +12%",
                "M-05" => "M-05 成本杀手：成本削减效率 +10%",
                _ => id
            };
        }

        #endregion

        #region KPI / Loyalty / Year

        /// <summary>季末：评估各部门 KPI，达标则下季效率 +5%。</summary>
        public void OnQuarterEndEvaluateKpi()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            foreach (var kv in _depts)
            {
                var d = kv.Value;
                if (string.IsNullOrEmpty(d.headId)) continue;
                bool met = EvaluateKpi(d.deptCode, gs);
                d.kpiMetLastQuarter = met;
                d.kpiBonus = met ? KpiQuarterBonus : 0f;
                RecalcEfficiencyRaw(d);
            }

            OnDepartmentsChanged?.Invoke();
        }

        private static bool EvaluateKpi(string code, GameState gs)
        {
            return code switch
            {
                "OPS" => gs.onTimePercent >= 85f,
                "MNT" => gs.cash >= 80f && (!gs.hasAOGTriggered || gs.aogChoice == 1),
                "MKT" => gs.loadFactorPercent >= 75f || gs.monthlyRevenue > 0f,
                "FIN" => gs.cash >= 0f && CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn) >= 3f,
                "HR" => gs.boardSatisfaction >= 40f,
                "SAF" => gs.safetyRating <= SafetyRating.B,
                "STG" => gs.activeRoutes != null && gs.activeRoutes.Count >= 1,
                _ => true
            };
        }

        /// <summary>财年末：按 KPI 与薪酬调整忠诚度，可能触发离职。</summary>
        public void OnYearEndLoyalty()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            var resignList = new List<(string dept, string name)>();

            foreach (var kv in _depts)
            {
                var d = kv.Value;
                if (string.IsNullOrEmpty(d.headId)) continue;
                var exec = DataBase.Instance?.GetExecutive(d.headId);
                if (exec == null) continue;

                if (d.kpiMetLastQuarter) d.loyalty += 5f;
                else d.loyalty -= 10f;

                // 薪酬相对要求：MVP 以 baseSalary 为要求，当前 salary 对比
                float req = exec.baseSalary;
                if (d.salary >= req * 1.10f) d.loyalty += 5f;
                else if (d.salary <= req * 0.90f) d.loyalty -= 10f;

                d.loyalty = Mathf.Clamp(d.loyalty, 0f, 100f);
                RecalcEfficiencyRaw(d);

                if (d.loyalty < ResignLoyaltyThreshold)
                    resignList.Add((d.deptCode, exec.fullName));
            }

            for (int i = 0; i < resignList.Count; i++)
                ForceResign(resignList[i].dept, resignList[i].name, fireEvent: false);

            if (resignList.Count > 0)
            {
                RefreshChemistry();
                // 合并弹窗：用第一条触发 UI，正文由 UI 展示部门名
                OnResignation?.Invoke(resignList[0].dept, resignList[0].name);
            }

            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
        }

        private void ForceResign(string deptCode, string name, bool fireEvent = true)
        {
            var d = GetDept(deptCode);
            if (d == null || string.IsNullOrEmpty(d.headId)) return;
            string id = d.headId;
            _removedIds.Add(id);
            d.headId = "";
            d.loyalty = 0f;
            d.efficiency = 0f;
            d.kpiBonus = 0f;
            if (fireEvent) RefreshChemistry();

            var gs = GameState.Instance;
            gs?.AddDecisionLog($"人事：{name}（{DepartmentCatalog.DisplayName(deptCode)}）因忠诚度过低主动辞职。");
            if (fireEvent)
                OnResignation?.Invoke(deptCode, name);
        }

#if UNITY_EDITOR
        /// <summary>调试：将指定部门负责人忠诚度设为低值并立刻结算离职。</summary>
        public void DebugForceResign(string deptCode)
        {
            var d = GetDept(deptCode);
            if (d == null || string.IsNullOrEmpty(d.headId)) return;
            d.loyalty = 20f;
            var exec = DataBase.Instance?.GetExecutive(d.headId);
            string name = exec != null ? exec.fullName : d.headId;
            ForceResign(deptCode, name, fireEvent: true);
            GameState.Instance?.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
        }
#endif

        #endregion

        #region Save / Load

        public string ToJson()
        {
            EnsureDepartments();
            var items = new DepartmentHeadSave[DepartmentCatalog.Codes.Length];
            for (int i = 0; i < DepartmentCatalog.Codes.Length; i++)
            {
                var d = GetDept(DepartmentCatalog.Codes[i]);
                items[i] = new DepartmentHeadSave
                {
                    deptCode = d.deptCode,
                    headId = d.headId ?? "",
                    loyalty = d.loyalty,
                    salary = d.salary,
                    efficiency = d.efficiency,
                    kpiMetLastQuarter = d.kpiMetLastQuarter,
                    kpiBonus = d.kpiBonus
                };
            }

            var blob = new DepartmentHeadsBlob
            {
                items = items,
                removedIds = new string[_removedIds.Count],
                activeChemistry = _activeChemistry.ToArray()
            };
            int ri = 0;
            foreach (var id in _removedIds)
                blob.removedIds[ri++] = id;
            return JsonUtility.ToJson(blob);
        }

        public void FromJson(string json)
        {
            EnsureDepartments();
            _removedIds.Clear();
            foreach (var kv in _depts)
            {
                kv.Value.headId = "";
                kv.Value.loyalty = 0f;
                kv.Value.efficiency = 0f;
                kv.Value.kpiBonus = 0f;
            }

            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<DepartmentHeadsBlob>(json);
                if (blob?.removedIds != null)
                {
                    for (int i = 0; i < blob.removedIds.Length; i++)
                        if (!string.IsNullOrEmpty(blob.removedIds[i]))
                            _removedIds.Add(blob.removedIds[i]);
                }

                if (blob?.items != null)
                {
                    for (int i = 0; i < blob.items.Length; i++)
                    {
                        var s = blob.items[i];
                        if (s == null || string.IsNullOrEmpty(s.deptCode)) continue;
                        var d = GetDept(s.deptCode);
                        if (d == null) continue;
                        d.headId = s.headId ?? "";
                        d.loyalty = s.loyalty;
                        d.salary = s.salary;
                        d.efficiency = s.efficiency;
                        d.kpiMetLastQuarter = s.kpiMetLastQuarter;
                        d.kpiBonus = s.kpiBonus;
                    }
                }

                RefreshChemistry();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DepartmentManager] 读档失败: " + e.Message);
            }

            OnDepartmentsChanged?.Invoke();
        }

        #endregion
    }

    [Serializable]
    public class DepartmentRuntime
    {
        public string deptCode;
        public string headId = "";
        public float loyalty;
        public float salary;
        public float efficiency;
        public bool kpiMetLastQuarter;
        public float kpiBonus;
    }
}
