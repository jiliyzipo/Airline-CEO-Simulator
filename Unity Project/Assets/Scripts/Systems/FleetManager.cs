using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>Phase 10 · 机队：购买 / 退役 / 机龄老化 / 维护成本。</summary>
    public class FleetManager : MonoBehaviour
    {
        public static FleetManager Instance { get; private set; }

        public const float BaseMaintPerAircraft = 5f; // 万美元/月 · 适航或维护中

        public event Action OnFleetChanged;

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
        }

        private void Start()
        {
            EnsureStarterFleet();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Init / Query

        public void EnsureStarterFleet()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();
            if (gs.fleet.Count > 0) return;

            SpawnStarter(gs, "A320neo", 11000f);
            SpawnStarter(gs, "A320neo", 11000f);
            SpawnStarter(gs, "B737-800", 9600f);
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            Debug.Log("[Fleet] 已生成初始机队 3 架。");
        }

        private static void SpawnStarter(GameState gs, string type, float price)
        {
            int n = Mathf.Max(1, gs.nextRegistrationNumber);
            var ac = new Aircraft
            {
                registration = $"B-{1000 + n}",
                type = type,
                age = 0,
                condition = 100f,
                isActive = true,
                status = AircraftStatuses.Airworthy,
                purchaseYear = gs.year,
                purchasePrice = price
            };
            gs.fleet.Add(ac);
            gs.nextRegistrationNumber = n + 1;
        }

        public int CountTotal(GameState gs = null)
        {
            gs ??= GameState.Instance;
            return gs?.fleet?.Count ?? 0;
        }

        public int CountAirworthy(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && a.isActive && a.status == AircraftStatuses.Airworthy)
                    n++;
            }
            return n;
        }

        /// <summary>可用于开通航线的飞机数（适航）。</summary>
        public int CountAvailableForRoutes(GameState gs = null)
        {
            return CountAirworthy(gs);
        }

        public float AverageAge(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null || gs.fleet.Count == 0) return 0f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || a.status == AircraftStatuses.Retired) continue;
                sum += a.age;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        public bool HasSpareAircraftForNewRoute(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs == null) return false;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            return CountAvailableForRoutes(gs) > routes;
        }

        #endregion

        #region Buy / Retire / Age

        public bool TryBuyAircraft(string typeId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                message = "游戏状态未就绪。";
                return false;
            }

            EnsureStarterFleet();
            var catalog = DataBase.Instance != null
                ? DataBase.Instance.GetAircraftType(typeId)
                : null;
            if (catalog == null)
            {
                message = $"未知机型：{typeId}";
                return false;
            }

            if (gs.cash + 0.01f < catalog.priceWan)
            {
                message =
                    $"现金不足：{catalog.typeId} 需 {catalog.priceWan:F0} 万，当前 {gs.cash:F0} 万。\n" +
                    "提示：可退役旧机回收残值，或 Debug → Grant Fleet Cash。";
                return false;
            }

            gs.cash -= catalog.priceWan;
            int n = Mathf.Max(1, gs.nextRegistrationNumber);
            var ac = new Aircraft
            {
                registration = $"B-{1000 + n}",
                type = catalog.typeId,
                age = 0,
                condition = 100f,
                isActive = true,
                status = AircraftStatuses.Airworthy,
                purchaseYear = gs.year,
                purchasePrice = catalog.priceWan
            };
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();
            gs.fleet.Add(ac);
            gs.nextRegistrationNumber = n + 1;
            gs.AddDecisionLog($"采购飞机 {ac.registration}（{ac.type}）−{catalog.priceWan:F0} 万");
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"已购入 {ac.registration}（{ac.type}），现金 −{catalog.priceWan:F0} 万 → {gs.cash:F0} 万。";
            return true;
        }

        public bool TryRetireAircraft(string registration, out string message)
        {
            var gs = GameState.Instance;
            if (gs?.fleet == null)
            {
                message = "机队为空。";
                return false;
            }

            Aircraft ac = null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                if (gs.fleet[i] != null && gs.fleet[i].registration == registration)
                {
                    ac = gs.fleet[i];
                    break;
                }
            }

            if (ac == null)
            {
                message = $"未找到飞机 {registration}";
                return false;
            }

            if (ac.status == AircraftStatuses.Retired || !ac.isActive)
            {
                message = $"{registration} 已退役。";
                return false;
            }

            float residual = CalcResidualValue(ac);
            ac.isActive = false;
            ac.status = AircraftStatuses.Retired;
            gs.cash += residual;
            gs.AddDecisionLog($"退役 {ac.registration}，回收残值 +{residual:F0} 万");
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"{ac.registration} 已退役，回收残值 {residual:F0} 万 → 现金 {gs.cash:F0} 万。";
            return true;
        }

        public static float CalcResidualValue(Aircraft ac)
        {
            if (ac == null) return 0f;
            float pct = (100f - ac.age * 3f) / 100f;
            pct = Mathf.Max(0.10f, pct);
            return ac.purchasePrice * pct;
        }

        /// <summary>财年末：全体非退役机龄 +1，并刷新状态。</summary>
        public void OnYearEndAgeFleet()
        {
            var gs = GameState.Instance;
            if (gs?.fleet == null) return;

            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || a.status == AircraftStatuses.Retired) continue;
                a.age = Mathf.Max(0, a.age + 1);
                a.condition = Mathf.Clamp(a.condition - 2f, 20f, 100f);
                RefreshStatus(a);
            }

            OnFleetChanged?.Invoke();
            Debug.Log("[Fleet] 财年末机龄 +1 完成。");
        }

        public static void RefreshStatus(Aircraft a)
        {
            if (a == null || a.status == AircraftStatuses.Retired) return;
            if (a.age > 15)
            {
                a.status = AircraftStatuses.Maintenance;
                a.isActive = true; // 仍在册，但非适航开通
            }
            else
            {
                a.status = AircraftStatuses.Airworthy;
                a.isActive = true;
            }
        }

        /// <summary>月维护费合计（万美元）。退役不计；机龄系数；&gt;20 年翻倍。</summary>
        public float CalcMonthlyMaintenance(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || a.status == AircraftStatuses.Retired) continue;
                float factor = 1f + a.age * 0.05f;
                float cost = BaseMaintPerAircraft * factor;
                if (a.age > 20) cost *= 2f;
                sum += cost;
            }
            return sum;
        }

        #endregion

        #region Save

        public string ToJson()
        {
            var gs = GameState.Instance;
            var blob = new FleetListSave
            {
                items = gs?.fleet != null ? gs.fleet.ToArray() : Array.Empty<Aircraft>(),
                nextRegistrationNumber = gs != null ? gs.nextRegistrationNumber : 1
            };
            return JsonUtility.ToJson(blob);
        }

        public void FromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (string.IsNullOrEmpty(json))
            {
                gs.fleet = new List<Aircraft>();
                gs.nextRegistrationNumber = 1;
                EnsureStarterFleet();
                return;
            }

            try
            {
                var blob = JsonUtility.FromJson<FleetListSave>(json);
                gs.fleet = blob?.items != null
                    ? new List<Aircraft>(blob.items)
                    : new List<Aircraft>();
                gs.nextRegistrationNumber = blob != null && blob.nextRegistrationNumber > 0
                    ? blob.nextRegistrationNumber
                    : 1;
                if (gs.fleet.Count == 0)
                    EnsureStarterFleet();
            }
            catch
            {
                gs.fleet = new List<Aircraft>();
                gs.nextRegistrationNumber = 1;
                EnsureStarterFleet();
            }

            OnFleetChanged?.Invoke();
        }

        public string FormatSummary()
        {
            var gs = GameState.Instance;
            var sb = new StringBuilder();
            sb.Append($"机队 {CountTotal(gs)} 架 · 适航 {CountAirworthy(gs)} · 均龄 {AverageAge(gs):F1} 年");
            sb.Append($" · 月维护约 {CalcMonthlyMaintenance(gs):F1} 万");
            return sb.ToString();
        }

        #endregion
    }
}
