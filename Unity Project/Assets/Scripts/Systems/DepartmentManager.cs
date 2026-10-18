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

        public const float DeputyEfficiencyMult = 1.075f;

        public PersonnelData GetHead(string deptCode)
        {
            var d = GetDept(deptCode);
            if (d == null || string.IsNullOrEmpty(d.headId)) return null;
            PersonnelGenerator.EnsurePool(GameState.Instance);
            return PersonnelGenerator.Find(GameState.Instance, d.headId);
        }

        public bool IsAppointed(string deptCode)
        {
            var d = GetDept(deptCode);
            return d != null && !string.IsNullOrEmpty(d.headId);
        }

        public List<PersonnelData> GetCandidates(string deptCode, int count = 2)
        {
            var pool = GetAvailableHeads(deptCode);
            for (int i = 0; i < pool.Count; i++)
            {
                int j = UnityEngine.Random.Range(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            if (pool.Count > count)
                pool.RemoveRange(count, pool.Count - count);
            return pool;
        }

        public bool IsOccupied(string personId)
        {
            if (string.IsNullOrEmpty(personId)) return false;
            foreach (var kv in _depts)
            {
                if (string.Equals(kv.Value.headId, personId, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(kv.Value.deputyId, personId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool IsHeadSomewhere(string execId) => IsOccupied(execId);

        public string DisplayName(string personId)
        {
            var p = PersonnelGenerator.Find(GameState.Instance, personId);
            return p != null ? p.fullName : personId;
        }

        public PersonnelData GetDeputy(string deptCode)
        {
            var d = GetDept(deptCode);
            if (d == null || string.IsNullOrEmpty(d.deputyId)) return null;
            return PersonnelGenerator.Find(GameState.Instance, d.deputyId);
        }

        public List<PersonnelData> GetDeputyCandidates(string deptCode)
        {
            var list = new List<PersonnelData>();
            var gs = GameState.Instance;
            PersonnelGenerator.EnsurePool(gs);
            if (gs?.staffPool == null) return list;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null) continue;
                if (!string.Equals(p.department, deptCode, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.rank != "副手" && p.rank != "主管") continue;
                if (p.onMarket) continue;
                if (IsOccupied(p.id)) continue;
                list.Add(p);
            }
            return list;
        }

        public bool AppointDeputy(string deptCode, string personnelId)
        {
            var gs = GameState.Instance;
            var dept = GetDept(deptCode);
            var p = PersonnelGenerator.Find(gs, personnelId);
            if (gs == null || dept == null || p == null) return false;
            if (!string.Equals(p.department, deptCode, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(dept.deputyId)) return false;
            if (IsOccupied(personnelId)) return false;

            dept.deputyId = personnelId;
            RecalcEfficiency(dept);
            gs.AddDecisionLog($"人事：任命 {p.fullName} 为{DepartmentCatalog.DisplayName(deptCode)}副总监（年薪 {p.baseSalary:F0} 万，效率 +7.5%）。");
            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
            return true;
        }

        public float DismissDeputy(string deptCode, float severanceMult = 1f)
        {
            var gs = GameState.Instance;
            var dept = GetDept(deptCode);
            if (gs == null || dept == null || string.IsNullOrEmpty(dept.deputyId)) return 0f;
            var p = PersonnelGenerator.Find(gs, dept.deputyId);
            string name = p != null ? p.fullName : dept.deputyId;
            float severance = (p != null ? p.baseSalary * 0.15f : 2f) * Mathf.Max(0f, severanceMult);
            if (gs.cash < severance) { severance = gs.cash; gs.cash = 0f; }
            else gs.cash -= severance;
            gs.yearNetProfit -= severance;
            dept.deputyId = "";
            RecalcEfficiency(dept);
            if (p != null) p.onMarket = true;
            gs.AddDecisionLog($"人事：解聘副总监 {name}（{DepartmentCatalog.DisplayName(deptCode)}），遣散费 {severance:F1} 万。");
            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
            return severance;
        }

        public bool HireFromMarket(string personId)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(personId)) return false;
            PersonnelGenerator.EnsurePool(gs);
            var p = PersonnelGenerator.Find(gs, personId);
            if (p == null || !StaffRecruitment.CanCeoHire(p)) return false;
            return Appoint(p.department, personId);
        }

        public float TerminateContract(string personId, bool mutual = false)
        {
            if (string.IsNullOrEmpty(personId)) return 0f;
            float scale = mutual ? 0.5f : 1f;
            foreach (var kv in _depts)
            {
                if (string.Equals(kv.Value.headId, personId, StringComparison.OrdinalIgnoreCase))
                    return Dismiss(kv.Key, scale);
            }
            return 0f;
        }

        /// <summary>新开局：只自动任命各部门总监；副总监及以下由人力招聘后报 CEO 审批。</summary>
        public void ResetForNewGame()
        {
            EnsureDepartments();
            _removedIds.Clear();
            _activeChemistry.Clear();
            foreach (var kv in _depts)
            {
                kv.Value.headId = "";
                kv.Value.deputyId = "";
                kv.Value.loyalty = 0f;
                kv.Value.salary = 0f;
                kv.Value.efficiency = 0f;
                kv.Value.kpiBonus = 0f;
                kv.Value.kpiMetLastQuarter = false;
            }
        }

        public List<PersonnelData> GetAvailableHeads(string deptCode) => GetAvailableExecutives(deptCode);

        public List<PersonnelData> GetAvailableExecutives(string deptCode)
        {
            var pool = new List<PersonnelData>();
            var gs = GameState.Instance;
            PersonnelGenerator.EnsurePool(gs);
            if (gs?.staffPool == null) return pool;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.rank != "负责人") continue;
                if (!string.Equals(p.department, deptCode, StringComparison.OrdinalIgnoreCase)) continue;
                if (_removedIds.Contains(p.id) || IsOccupied(p.id)) continue;
                pool.Add(p);
            }
            return pool;
        }

        public void BootstrapOpeningTeam()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            ResetForNewGame();
            gs.staffPool = new List<PersonnelData>();
            gs.staffBootstrapped = false;
            PersonnelGenerator.EnsurePool(gs);

            EnsureKeyAppointments();

            gs.staffBootstrapped = true;
            StaffRecruitment.Reset(gs);
            StaffRecruitment.EnsureVacancies(gs);
            PersonnelGenerator.SyncOccupiedFlags(gs);
            // 投机候选池开局即播种一次：否则新局要等到下一个「1 日 / 16 日」才有人，
            // 开局第一周的职业介绍所会只有总监候选人（2026-10-11）。
            TalentMarket.EnsureSeeded(gs);
            RefreshChemistry();
            OnDepartmentsChanged?.Invoke();
        }

        /// <summary>各部门总监、副总监必须有人；缺人则从编制池任命或生成。</summary>
        public void EnsureKeyAppointments()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            PersonnelGenerator.EnsurePool(gs);
            PersonnelGenerator.FillMissingKeyStaff(gs);

            for (int i = 0; i < DepartmentCatalog.Codes.Length; i++)
            {
                string code = DepartmentCatalog.Codes[i];
                if (!IsAppointed(code))
                {
                    var available = GetAvailableHeads(code);
                    if (available.Count == 0)
                    {
                        var created = PersonnelGenerator.CreateEmployedHead(gs, code);
                        if (created != null) available.Add(created);
                    }
                    if (available.Count > 0)
                        Appoint(code, available[0].id);
                }

                var dept = GetDept(code);
                if (dept == null || !string.IsNullOrEmpty(dept.deputyId)) continue;
                PersonnelData deputy = null;
                if (gs.staffPool != null)
                {
                    for (int s = 0; s < gs.staffPool.Count; s++)
                    {
                        var p = gs.staffPool[s];
                        if (p == null || p.onMarket || IsOccupied(p.id)) continue;
                        if (!string.Equals(p.department, code, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.rank != "副手") continue;
                        deputy = p;
                        break;
                    }
                }
                if (deputy != null)
                    AppointDeputy(code, deputy.id);
            }
        }

        public void NotifyRosterChanged() => OnDepartmentsChanged?.Invoke();

        #endregion

        #region Appoint / Replace

        public bool Appoint(string deptCode, string personId)
        {
            var gs = GameState.Instance;
            PersonnelGenerator.EnsurePool(gs);
            var person = PersonnelGenerator.Find(gs, personId);
            if (gs == null || person == null) return false;
            if (!string.Equals(person.department, deptCode, StringComparison.OrdinalIgnoreCase)) return false;
            if (_removedIds.Contains(personId)) return false;
            if (person.rank != "负责人") return false;

            var dept = GetDept(deptCode);
            if (dept == null) return false;
            if (!string.IsNullOrEmpty(dept.headId)) return false;

            person.onMarket = false;
            person.rank = "负责人";
            person.jobTitle = StaffEstablishment.HeadTitle;
            dept.headId = personId;
            dept.loyalty = person.loyalty;
            dept.salary = person.baseSalary;
            dept.kpiBonus = 0f;
            dept.kpiMetLastQuarter = false;
            RecalcEfficiency(dept);
            RefreshChemistry();

            gs.AddDecisionLog($"人事：任命 {person.fullName} 为{DepartmentCatalog.DisplayName(deptCode)}{StaffEstablishment.HeadTitle}（年薪 {dept.salary:F0} 万）。");
            gs.NotifyChanged();
            OnDepartmentsChanged?.Invoke();
            return true;
        }

        /// <summary>更换：先遣散再空缺（由 UI 随后打开任命）。</summary>
        public float Dismiss(string deptCode, float severanceMult = 1f)
        {
            var gs = GameState.Instance;
            var dept = GetDept(deptCode);
            if (gs == null || dept == null || string.IsNullOrEmpty(dept.headId)) return 0f;

            var person = PersonnelGenerator.Find(gs, dept.headId);
            string name = person != null ? person.fullName : dept.headId;
            float severance = CalcSeverance(dept.salary, gs.quarter) * Mathf.Max(0f, severanceMult);
            // Phase 15：现金不足时遣散费按现金归零处理
            if (gs.cash < severance)
            {
                severance = gs.cash;
                gs.cash = 0f;
            }
            else
            {
                gs.cash -= severance;
            }
            gs.yearNetProfit -= severance;

            if (person != null) person.onMarket = true;
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

            var person = HeadPerson(dept);
            if (person == null)
            {
                dept.efficiency = MinEfficiency;
                return;
            }

            float cultureFit = GetCultureFit(person.nationality);
            float core = person.efficiencyBonus * 0.5f
                         + (dept.loyalty / 100f) * 0.3f
                         + cultureFit * 0.2f;
            float staffLift = TrainingGrowthLogic.DeptStaffLift(GameState.Instance, dept.deptCode);
            float eff = 0.7f + 0.3f * core;
            eff += dept.kpiBonus;
            eff += staffLift;
            // 化学反应加成
            eff *= GetChemistryMultiplier(dept.deptCode);
            if (!string.IsNullOrEmpty(dept.deputyId))
                eff *= DeputyEfficiencyMult;
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

        PersonnelData HeadPerson(DepartmentRuntime dept)
        {
            if (dept == null || string.IsNullOrEmpty(dept.headId)) return null;
            return PersonnelGenerator.Find(GameState.Instance, dept.headId);
        }

        private void RecalcEfficiencyRaw(DepartmentRuntime dept)
        {
            var person = HeadPerson(dept);
            if (person == null) { dept.efficiency = MinEfficiency; return; }
            float cultureFit = GetCultureFit(person.nationality);
            float core = person.efficiencyBonus * 0.5f + (dept.loyalty / 100f) * 0.3f + cultureFit * 0.2f;
            float staffLift = TrainingGrowthLogic.DeptStaffLift(GameState.Instance, dept.deptCode);
            float eff = 0.7f + 0.3f * core + dept.kpiBonus + staffLift;
            eff *= GetChemistryMultiplier(dept.deptCode);
            if (!string.IsNullOrEmpty(dept.deputyId))
                eff *= DeputyEfficiencyMult;
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
                var person = HeadPerson(d);
                if (person == null) continue;

                if (d.kpiMetLastQuarter) d.loyalty += 5f;
                else d.loyalty -= 10f;

                float req = person.baseSalary;
                if (d.salary >= req * 1.10f) d.loyalty += 5f;
                else if (d.salary <= req * 0.90f) d.loyalty -= 10f;

                d.loyalty = Mathf.Clamp(d.loyalty, 0f, 100f);
                person.loyalty = d.loyalty;
                RecalcEfficiencyRaw(d);

                if (d.loyalty < ResignLoyaltyThreshold)
                    resignList.Add((d.deptCode, person.fullName));
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
            var person = PersonnelGenerator.Find(GameState.Instance, id);
            if (person != null) person.onMarket = true;
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
            var person = HeadPerson(d);
            string name = person != null ? person.fullName : d.headId;
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
                    deputyId = d.deputyId ?? "",
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
                kv.Value.deputyId = "";
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
                        d.deputyId = s.deputyId ?? "";
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
        public string deputyId = "";
        public float loyalty;
        public float salary;
        public float efficiency;
        public bool kpiMetLastQuarter;
        public float kpiBonus;
    }
}
