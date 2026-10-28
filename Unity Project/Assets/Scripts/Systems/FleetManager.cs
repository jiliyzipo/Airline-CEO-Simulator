using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>Phase 10 · 机队：购买 / 退役 / 机龄老化 / 维护成本。</summary>
    public class FleetManager : MonoBehaviour
    {
        public static FleetManager Instance { get; private set; }

        public const float BaseMaintPerAircraft = EconomyBalanceParams.FleetMonthlyMaintWan; // 万美元/月 · 适航或维护中

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
            var cat = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(type) : null;
            var ac = new Aircraft
            {
                registration = $"B-{1000 + n}",
                type = type,
                source = "自有",
                age = 0,
                condition = 100f,
                isActive = true,
                status = AircraftStatuses.Airworthy,
                purchaseYear = gs.year,
                purchasePrice = price,
                economySeats = cat != null ? cat.maxPassengers : EconomyBalanceParams.DefaultSeatsFor(type)
            };
            gs.fleet.Add(ac);
            gs.nextRegistrationNumber = n + 1;
        }

        /// <summary>
        /// 在册飞机数（不含已退役 / 已退租）。退出机队的飞机仍留在 <c>fleet</c> 里备查（履历、
        /// 航线记录里还引用着机号），但不计入机队规模 —— 否则「总数」会随退役逐年虚增。
        /// </summary>
        public int CountTotal(GameState gs = null) => InServiceCount(gs ?? GameState.Instance);

        /// <summary>在册飞机数（静态版）：底部状态栏等没有 <see cref="Instance"/> 的调用点用它，口径与机队页「总数」同源。</summary>
        public static int InServiceCount(GameState gs)
        {
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && a.status != AircraftStatuses.Retired) n++;
            }
            return n;
        }

        public int CountAirworthy(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && a.isActive
                    && (a.status == AircraftStatuses.Airworthy || a.status == AircraftStatuses.InFlight))
                    n++;
            }
            return n;
        }

        /// <summary>可用于开通航线的飞机数（适航且未被过渡业务占用）。</summary>
        public int CountAvailableForRoutes(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !a.isActive || a.status != AircraftStatuses.Airworthy) continue;
                if (AirlineCEO.Logic.TransitionBusinessLogic.IsBusy(gs, a.registration)) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// 该机是否要在机队列表上挂「年结停场」徽标。
        ///
        /// ★2026-10-27：用户截图发现 `B-2005`（20 年）、`B-2006`（24 年）在列表里仍是
        /// 一片绿色的 `适航` —— 延寿规则只在**点进详情页**时才出现，列表上毫无预警。
        /// 玩家要一架架点开才知道下一个财年末会停哪几架。
        ///
        /// 判据**只写这一处**，列表徽标与详情页提示必须同源：若列表按当前机况判、
        /// 详情页按年结口径（先扣年度磨损）判，同一架飞机会出现「列表无提示、点进去说要停场」。
        ///
        /// 排除项：
        /// - 已退役 / 已退租 —— 本来就不飞了，提示只是噪音；
        /// - 停场窗口内（<c>maintenanceUntilUtcTicks != 0</c>）—— 已有明确的 `维护中` 状态与出厂日期，
        ///   再叠一条「年结会停场」会让玩家以为这次定检白做了。
        /// </summary>
        public static bool NeedsLifeExtensionWarning(Aircraft a)
        {
            if (a == null) return false;
            // 已退役 / 已退租：ExitLabel 只在 status == Retired 时非空，故 IsDisposed 已覆盖两者
            if (a.IsDisposed) return false;
            if (a.maintenanceUntilUtcTicks != 0) return false;
            if (a.age <= FleetEconomics.LifeExtensionAgeLimit) return false;
            return !FleetEconomics.WillStayAirworthyAtYearEnd(a.age, a.condition);
        }

        /// <summary>
        /// 年结会因机龄-机况停场的飞机数（列表徽标与顶栏汇总共用）。
        ///
        /// 刻意声明为 **static**：它只依赖传入的 <paramref name="gs"/>，与 <c>Instance</c> 无关。
        /// 这样隔离回归不必先造一个 <c>FleetManager</c> 组件就能验证判据 ——
        /// 判据一旦只能经 MonoBehaviour 调用，测试就会绕开它自己另写一份，两边迟早分叉。
        /// </summary>
        public static int CountLifeExtensionRisk(GameState gs = null)
        {
            gs ??= GameState.Instance;
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
                if (NeedsLifeExtensionWarning(gs.fleet[i])) n++;
            return n;
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
                purchasePrice = catalog.priceWan,
                economySeats = catalog.maxPassengers,
                cargoTons = catalog.cargoTons
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

        /// <summary>n97 购机决策会议批准：批量引进。买断（自有资金 / 二手引进）付全款；融资租赁 10% 首付、经营租赁零首付。</summary>
        public bool TryAcquireFleet(string typeId, int count, string source, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                message = "游戏状态未就绪。";
                return false;
            }
            EnsureStarterFleet();
            var catalog = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(typeId) : null;
            if (catalog == null)
            {
                message = $"未知机型：{typeId}";
                return false;
            }
            if (count <= 0)
            {
                message = "数量至少 1 架。";
                return false;
            }

            // 只有两种租赁方式可以「低首付 / 零首付」；自有资金买断与二手引进都是买断，必须付全款。
            // （旧写法把「非买断」一律按 10% 首付，于是「二手引进」只用付 10% 却记成自有机 →
            //  退租口径上线后，退一架自有机要回收整机残值，这个 10% 缺口与「租进再退」是同一种凭空造钱。）
            bool leased = FleetEconomics.IsLeasedSource(source);
            bool finance = FleetEconomics.IsFinanceLease(source);
            float total = catalog.priceWan * count;
            float down = leased ? (finance ? total * 0.10f : 0f) : total;
            float monthlyEach = 0f;
            if (finance) monthlyEach = catalog.priceWan * 0.012f;
            else if (string.Equals(source, FleetEconomics.SourceOperatingLease, System.StringComparison.Ordinal)) monthlyEach = catalog.priceWan * 0.022f;

            if (gs.cash + 0.01f < down)
            {
                message = leased ? $"首付款不足：需 {down:F0} 万，当前 {gs.cash:F0} 万。"
                                 : $"现金不足：需 {down:F0} 万，当前 {gs.cash:F0} 万。";
                return false;
            }

            gs.cash -= down;
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();
            for (int i = 0; i < count; i++)
            {
                int n = Mathf.Max(1, gs.nextRegistrationNumber);
                var ac = new Aircraft
                {
                    registration = $"B-{1000 + n}",
                    type = catalog.typeId,
                    source = source,
                    age = 0,
                    condition = 100f,
                    isActive = true,
                    status = AircraftStatuses.Airworthy,
                    purchaseYear = gs.year,
                    purchasePrice = catalog.priceWan,
                    economySeats = catalog.maxPassengers,
                    cargoTons = catalog.cargoTons,
                    monthlyLeaseWan = monthlyEach
                };
                gs.fleet.Add(ac);
                gs.nextRegistrationNumber = n + 1;
            }
            string costNote = leased ? $"首付 {down:F0} 万（{source}）" : $"全款 {total:F0} 万（{source}）";
            gs.AddDecisionLog($"购机会议批准：引进 {count} 架 {typeId}（{source}）−{costNote}");
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"已批准引进 {count} 架 {typeId}（{source}），现金 −{costNote} → {gs.cash:F0} 万。";
            return true;
        }

        public bool TryBuyUsed(string listingId, out string message)
        {
            var gs = GameState.Instance;
            UsedAircraftMarket.Ensure(gs);
            var lot = UsedAircraftMarket.Find(gs, listingId);
            if (gs == null || lot == null || lot.sold)
            {
                message = "该二手机已下架。";
                return false;
            }
            var catalog = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(lot.typeId) : null;
            if (catalog == null)
            {
                message = "未知机型。";
                return false;
            }
            if (gs.cash + 0.01f < lot.askPriceWan)
            {
                message = $"现金不足：需 {lot.askPriceWan:F0} 万，当前 {gs.cash:F0} 万。";
                return false;
            }

            gs.cash -= lot.askPriceWan;
            lot.sold = true;
            AddAirframe(gs, catalog, "二手引进", lot.age, lot.askPriceWan, 0f, lot.remainingLife);
            gs.AddDecisionLog($"二手引进 {lot.typeId} 机龄{lot.age}年 −{lot.askPriceWan:F0} 万" + (lot.trap ? "（高龄陷阱）" : ""));
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"已购入二手 {lot.typeId}（机龄 {lot.age} 年），现金 −{lot.askPriceWan:F0} 万。";
            return true;
        }

        public bool TryLeaseNew(string typeId, string source, out string message)
        {
            var gs = GameState.Instance;
            var catalog = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(typeId) : null;
            if (gs == null || catalog == null)
            {
                message = "机型无效。";
                return false;
            }
            bool finance = source == "融资租赁";
            float down = finance ? catalog.priceWan * 0.10f : 0f;
            float monthly = finance ? catalog.priceWan * 0.012f : catalog.priceWan * 0.022f;
            if (gs.cash + 0.01f < down)
            {
                message = $"首付不足：需 {down:F0} 万，当前 {gs.cash:F0} 万。";
                return false;
            }
            gs.cash -= down;
            AddAirframe(gs, catalog, source, 0, catalog.priceWan, monthly, 25);
            string note = finance ? $"首付 {down:F0} 万 · 月租 {monthly:F1} 万" : $"无首付 · 月租 {monthly:F1} 万";
            gs.AddDecisionLog($"租赁 {typeId}（{source}）{note}");
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"已引入 {typeId}（{source}），{note}。现金 {gs.cash:F0} 万。";
            return true;
        }

        void AddAirframe(GameState gs, AircraftCatalogEntry catalog, string source, int age,
            float bookPrice, float monthlyLease, int remainingLife)
        {
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();
            int n = Mathf.Max(1, gs.nextRegistrationNumber);
            gs.fleet.Add(new Aircraft
            {
                registration = $"B-{1000 + n}",
                type = catalog.typeId,
                source = source,
                age = age,
                condition = Mathf.Clamp(100f - age * 3f, 40f, 100f),
                isActive = true,
                status = AircraftStatuses.Airworthy,
                purchaseYear = gs.year,
                purchasePrice = bookPrice,
                economySeats = catalog.maxPassengers,
                cargoTons = catalog.cargoTons,
                monthlyLeaseWan = monthlyLease,
                remainingLifeYears = remainingLife
            });
            gs.nextRegistrationNumber = n + 1;
        }

        /// <summary>
        /// 飞机退出机队。按来源分两条完全不同的路，术语与账目都不得混用：
        /// <list type="bullet">
        /// <item>自有 / 二手引进 → <b>退役</b>：飞机是本公司的，转售/拆解并回收残值入账；</item>
        /// <item>融资租赁 / 经营租赁 → <b>退租</b>：产权在出租人手里，公司无权「退役」别人的资产，
        /// 只能交还出租人；不回收残值，融资租赁还要付提前解约违约金。</item>
        /// </list>
        /// 命名保留（既有调用点、文档与自测都按此名索引），退租的账目口径见
        /// <see cref="LeaseReturnCost"/> 与 <see cref="FleetEconomics.LeaseReturnCostWan"/>。
        /// </summary>
        public bool TryRetireAircraft(string registration, out string message)
            => TryRetireAircraft(GameState.Instance, registration, out message);

        /// <summary>
        /// 显式传入 <paramref name="gs"/> 的重载：自测与工具不必依赖 <c>GameState.Instance</c> 单例。
        /// </summary>
        public bool TryRetireAircraft(GameState gs, string registration, out string message)
        {
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
                message = $"{registration} 已{ac.ExitLabel}。";
                return false;
            }

            // 租赁机：退租（交还出租人），不是退役
            if (FleetEconomics.IsLeasedSource(ac.source))
            {
                float fee = LeaseReturnCost(gs, ac);
                if (fee > 0.01f && gs.cash + 0.01f < fee)
                {
                    message = $"{ac.source} 提前退租须付违约金 {fee:F0} 万，当前现金 {gs.cash:F0} 万，不足。";
                    return false;
                }

                gs.cash -= fee;
                ac.isActive = false;
                ac.status = AircraftStatuses.Retired;
                ac.disposal = FleetEconomics.DisposalReturned;
                string feeNote = fee > 0.01f ? $"，提前解约违约金 −{fee:F0} 万" : "，无违约金";
                gs.AddDecisionLog($"退租 {ac.registration}（{ac.source}）{feeNote}");
                gs.NotifyChanged();
                OnFleetChanged?.Invoke();
                message = $"{ac.registration} 已退租（{ac.source}，交还出租人）{feeNote}" +
                          $" → 现金 {gs.cash:F0} 万；月租同步停止。";
                return true;
            }

            float residual = CalcResidualValue(ac);
            ac.isActive = false;
            ac.status = AircraftStatuses.Retired;
            ac.disposal = FleetEconomics.DisposalRetired;
            gs.cash += residual;
            gs.AddDecisionLog($"退役 {ac.registration}，回收残值 +{residual:F0} 万");
            gs.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"{ac.registration} 已退役，回收残值 {residual:F0} 万 → 现金 {gs.cash:F0} 万。";
            return true;
        }

        /// <summary>是否租赁机（产权在出租人）：UI 据此把「退役」改称「退租」。</summary>
        public static bool IsLeased(Aircraft ac) =>
            ac != null && FleetEconomics.IsLeasedSource(ac.source);

        /// <summary>
        /// 退租代价（万美元），UI 与处置逻辑共用同一口径，免得按钮上的数字与入账数字分叉：
        /// 经营租赁 0；融资租赁 = 剩余租期租金 × 30%（详见 <see cref="FleetEconomics.LeaseReturnCostWan"/>）。
        /// </summary>
        public static float LeaseReturnCost(GameState gs, Aircraft ac)
        {
            if (ac == null) return 0f;
            int currentYear = gs != null ? gs.year : ac.purchaseYear;
            int paid = FleetEconomics.LeaseMonthsPaid(ac.purchaseYear, currentYear);
            return FleetEconomics.LeaseReturnCostWan(ac.source, ac.monthlyLeaseWan, paid);
        }


        public Aircraft GetAircraftByRegistration(string registration)
        {
            var gs = GameState.Instance;
            if (gs?.fleet == null || string.IsNullOrEmpty(registration)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                if (gs.fleet[i] != null &&
                    string.Equals(gs.fleet[i].registration, registration, StringComparison.OrdinalIgnoreCase))
                    return gs.fleet[i];
            }
            return null;
        }

        /// <summary>n11 载客量配置：写回头等/商务/经济舱座位数。</summary>
        public bool ApplySeatConfig(string registration, int first, int business, int economy, out string message)
        {
            var ac = GetAircraftByRegistration(registration);
            if (ac == null)
            {
                message = $"未找到飞机 {registration}";
                return false;
            }
            if (first < 0 || business < 0 || economy <= 0)
            {
                message = "座位数不合法：经济舱至少 1 座，各舱 ≥ 0。";
                return false;
            }

            ac.firstClassSeats = first;
            ac.businessSeats = business;
            ac.economySeats = economy;
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"调整 {registration} 舱位配置 → 头等 {first} / 商务 {business} / 经济 {economy}");
            gs?.NotifyChanged();
            OnFleetChanged?.Invoke();
            message = $"已保存 {registration} 舱位配置（总 {ac.TotalSeats} 座）。";
            return true;
        }

        /// <summary>
        /// n107 大修预算确认：扣款（预估 + 不可预见费）、置为维护中、写入停场窗口。
        ///
        /// ★2026-10-27 修三件事：
        /// ① 扣款改为 <see cref="FleetEconomics.OverhaulCashCost"/> —— 预算页列的
        ///   「不可预见费(+12%)」此前只显示不扣，实际扣款与屏幕数字不一致；
        /// ② 写入 <see cref="Aircraft.maintenanceUntilUtcTicks"/>，「停场 N 天」第一次真的生效，
        ///   到期由 <see cref="ReleaseCompletedOverhauls"/> 复飞；
        /// ③ 返回值语义不变（是否排程成功），但 0 天 / 已退役的情形现在如实拒绝。
        /// </summary>
        public bool ScheduleOverhaul(string registration, string overhaulType, out string message)
            => ScheduleOverhaul(GameState.Instance, GetAircraftByRegistration(registration), overhaulType, out message);

        /// <summary>
        /// 与上面同一条链路，但显式收 <paramref name="gs"/> 与 <paramref name="ac"/>。
        ///
        /// 分出来是为了让 EditMode 回归能驱动真实扣款/排程路径：<see cref="GameState"/> 没有
        /// <c>[ExecuteInEditMode]</c>，`AddComponent` 不跑 `Awake`，`GameState.Instance` 恒为 null，
        /// 单例版在回归里根本走不到被测分支（同 `TermNavMap.Decorate(body, host)` 的取舍）。
        /// 游戏内调用方一个字不用改。
        /// </summary>
        public static bool ScheduleOverhaul(GameState gs, Aircraft ac, string overhaulType, out string message)
        {
            if (ac == null)
            {
                message = "未找到飞机";
                return false;
            }
            string registration = ac.registration;
            if (ac.status == AircraftStatuses.Retired)
            {
                message = $"{registration} 已退出机队，不能安排大修。";
                return false;
            }
            if (ac.status == AircraftStatuses.Maintenance)
            {
                message = $"{registration} 已在维护中"
                          + (ac.maintenanceUntilUtcTicks > 0
                              ? $"（排定出厂 {ReleaseDateLabel(ac)}），不能重复排程。"
                              : "，不能重复排程。");
                return false;
            }

            bool wide = EconomyBalanceParams.IsWidebody(ac.type ?? "");
            float cost = FleetEconomics.OverhaulCost(overhaulType, wide);
            float cashCost = FleetEconomics.OverhaulCashCost(overhaulType, wide);
            int days = FleetEconomics.OverhaulDays(overhaulType);
            DateTime now = gs != null ? gs.SimUtc : DateTime.UtcNow;

            if (gs != null && gs.cash + 0.01f < cashCost)
            {
                message = $"现金不足：{overhaulType} 预估 {cost:F0} 万 + 不可预见费 "
                          + $"{cashCost - cost:F0} 万 = {cashCost:F0} 万，当前 {gs.cash:F0} 万。";
                return false;
            }

            if (gs != null) gs.cash -= cashCost;
            ac.lastOverhaulType = overhaulType;
            ac.lastOverhaulYear = gs != null ? gs.year : ac.lastOverhaulYear;
            ac.condition = Mathf.Clamp(ac.condition + FleetEconomics.OverhaulConditionGain, 0f, 100f);
            ac.status = AircraftStatuses.Maintenance;
            ac.isActive = true;
            ac.maintenanceUntilUtcTicks = now.AddDays(days).Ticks;

            gs?.AddDecisionLog($"安排大修 {registration}：{overhaulType} · 停场 {days} 天 · −{cashCost:F0} 万"
                               + $"（含不可预见费 {cashCost - cost:F0} 万）");
            gs?.NotifyChanged();
            Instance?.OnFleetChanged?.Invoke();
            message = $"{registration} 已排程 {overhaulType}（停场 {days} 天，扣款 {cashCost:F0} 万"
                      + $"，其中不可预见费 {cashCost - cost:F0} 万，状态→维护中，预计 {ReleaseDateLabel(ac)} 出厂）。";
            return true;
        }

        /// <summary>排定出厂日（本地日期），无窗口返回「未排定」。仅用于界面与日志文案。</summary>
        public static string ReleaseDateLabel(Aircraft ac)
        {
            if (ac == null || ac.maintenanceUntilUtcTicks <= 0) return "未排定";
            var dt = new DateTime(ac.maintenanceUntilUtcTicks, DateTimeKind.Utc).ToLocalTime();
            return $"{dt.Month} 月 {dt.Day} 日";
        }

        /// <summary>
        /// 每日时钟：把停场窗口已到期的飞机放回适航。
        ///
        /// 做成 <c>static</c> 而不是实例方法：EditMode 回归里没有 <see cref="Instance"/>，
        /// 而验收必须能直接驱动这条路径（同 <see cref="RefreshStatus"/> 的取舍）。
        /// 返回本次复飞架数，便于断言与「机务报告」取数。
        ///
        /// 刻意**不**在这里加「机况奖励」或改机龄：大修的效果（+25 机况）在排程那一刻已结算，
        /// 复飞只负责把状态与窗口还原，避免同一次大修被结算两遍。
        /// </summary>
        public static int ReleaseCompletedOverhauls(GameState gs, DateTime? nowUtc = null)
        {
            if (gs?.fleet == null) return 0;
            DateTime now = nowUtc ?? (gs.SimUtc);
            int released = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || a.maintenanceUntilUtcTicks <= 0) continue;
                if (now.Ticks < a.maintenanceUntilUtcTicks) continue;
                // 窗口到了：只在「仍处于维护中」时改写状态；若中途被退役/退租，只清窗口不动状态。
                if (a.status == AircraftStatuses.Maintenance)
                {
                    a.status = AircraftStatuses.Airworthy;
                    released++;
                    gs.AddDecisionLog($"{a.registration} 定检出厂：状态 维护中 → 适航（{a.lastOverhaulType} 完成）");
                }
                a.maintenanceUntilUtcTicks = 0;
            }
            if (released > 0)
            {
                gs.NotifyChanged();
                Debug.Log($"[Fleet] 大修到期出厂 {released} 架。");
            }
            return released;
        }


        /// <summary>
        /// 单机大修预估费用（万美元）—— **不含不可预见费**，不能拿它当扣款额。
        /// 实际扣款一律走 <see cref="FleetEconomics.OverhaulCashCost"/>（n107 屏幕上的「合计扣款」）。
        /// </summary>
        public static float CalcOverhaulCost(Aircraft ac, string overhaulType)
        {
            bool wide = ac != null && EconomyBalanceParams.IsWidebody(ac.type ?? "");
            return FleetEconomics.OverhaulCost(overhaulType, wide);
        }

        public static float CalcResidualValue(Aircraft ac)
        {
            if (ac == null) return 0f;
            return FleetEconomics.ResidualValue(ac.purchasePrice, ac.age);
        }

        /// <summary>机队总价值（万美元）：全体非退役机残值之和（航空保险计价基准）。</summary>
        public static float FleetTotalValue(GameState gs)
        {
            if (gs?.fleet == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || a.status == AircraftStatuses.Retired || !a.isActive) continue;
                sum += FleetEconomics.ResidualValue(a.purchasePrice, a.age);
            }
            return sum;
        }

        /// <summary>PDP 交付：直接入列飞机（不重复扣现金，尾款由 FinanceManager 结算）。</summary>
        public static void DeliverAircraft(GameState gs, string typeId, int count, string source, float unitPriceWan)
        {
            if (gs == null || count <= 0) return;
            var catalog = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(typeId) : null;
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();

            for (int i = 0; i < count; i++)
            {
                int n = Mathf.Max(1, gs.nextRegistrationNumber);
                var ac = new Aircraft
                {
                    registration = $"B-{1000 + n}",
                    type = typeId,
                    source = source,
                    age = 0,
                    condition = 100f,
                    isActive = true,
                    status = AircraftStatuses.Airworthy,
                    purchaseYear = gs.year,
                    purchasePrice = unitPriceWan,
                    economySeats = catalog != null ? catalog.maxPassengers : EconomyBalanceParams.DefaultSeatsFor(typeId),
                    cargoTons = catalog != null ? catalog.cargoTons : 0f
                };
                gs.fleet.Add(ac);
                gs.nextRegistrationNumber = n + 1;
            }
            if (Instance != null) Instance.OnFleetChanged?.Invoke();
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
                a.condition = Mathf.Clamp(a.condition - FleetEconomics.AnnualConditionDecay, FleetEconomics.AnnualConditionFloor, 100f);
                RefreshStatus(a);
            }

            OnFleetChanged?.Invoke();
            Debug.Log("[Fleet] 财年末机龄 +1 完成。");
        }

        /// <param name="nowUtc">判定「是否仍在停场窗口内」用的时刻；null = 取当前游戏时刻。回归可显式传入。</param>
        public static void RefreshStatus(Aircraft a, DateTime? nowUtc = null)
        {
            if (a == null || a.status == AircraftStatuses.Retired) return;
            // 停场窗口内的车不许被财年末这一刷提前放出来：否则「停场 N 天」在最坏情况下
            // 会被年结缩短成 1 天，屏上那个 N 又变成假数字。到期由 ReleaseCompletedOverhauls 负责。
            DateTime now = nowUtc ?? (GameState.Instance != null ? GameState.Instance.SimUtc : DateTime.UtcNow);
            if (a.InScheduledMaintenance(now))
            {
                a.status = AircraftStatuses.Maintenance;
                a.isActive = true;
                return;
            }
            // ★2026-10-27 修：机龄超限不再是无条件死刑。原实现 `age > 15 → 维护中`，
            // 且没有任何恢复路径（LiveClockSimulator 只处理 InFlight → 适航，落地时飞机已不在飞），
            // 于是 16 年的飞机做完大修也救不回来。现在改由 FleetEconomics.CanExtendLife 判定：
            // 机龄超限但机况达标 → 允许继续适航；机况不够 → 停场，且玩家可以去定检抬机况。
            if (FleetEconomics.IsGroundedByAge(a.age, a.condition))
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
                sum += MonthlyMaintOf(gs.fleet[i]);
            return sum;
        }

        public static float MonthlyMaintOf(Aircraft a)
        {
            // 唯一口径在 FleetEconomics：本类不再自带「5%/年」的第二套公式，
            // 否则同一架飞机会在 FleetUI（本类）与 FleetSubpagePanel（Logic）上印出两个数。
            return AirlineCEO.Logic.FleetEconomics.MonthlyMaintenance(a);
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
            sb.Append($"机队 {CountTotal(gs)} 架 · 在役 {CountAirworthy(gs)} · 均龄 {AverageAge(gs):F1} 年");
            sb.Append($" · 月维护约 {CalcMonthlyMaintenance(gs):F1} 万");
            return sb.ToString();
        }

        #endregion
    }
}
