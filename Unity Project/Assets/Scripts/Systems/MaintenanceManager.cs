using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// MNT 机务工程部系统（Phase 2 Week 9）：维修策略/OEM关系/航材库存/采购订单/定检排程。
    /// 全部回写 GameState.mnt。
    /// </summary>
    public class MaintenanceManager : MonoBehaviour
    {
        public static MaintenanceManager Instance { get; private set; }

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

        public static MaintenanceState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.mnt == null) gs.mnt = new MaintenanceState();
            if (gs.mnt.parts == null) gs.mnt.parts = new List<PartStock>();
            if (gs.mnt.orders == null) gs.mnt.orders = new List<PurchaseOrder>();
            if (gs.mnt.oems == null) gs.mnt.oems = new List<OemRelation>();
            if (gs.mnt.checks == null) gs.mnt.checks = new List<CheckSchedule>();
            if (gs.mnt.oems.Count == 0)
            {
                gs.mnt.oems.Add(new OemRelation { oem = "波音", stars = 2, discountRate = 0.04f, deliveryPriority = 1 });
                gs.mnt.oems.Add(new OemRelation { oem = "空客", stars = 2, discountRate = 0.04f, deliveryPriority = 1 });
                gs.mnt.oems.Add(new OemRelation { oem = "巴航", stars = 1, discountRate = 0.02f, deliveryPriority = 0 });
            }
            // 停飞架数是**派生量**，不是独立事实：每次取状态时按机队真实情况重算。
            SyncGroundedCount(gs);
            return gs.mnt;
        }

        /// <summary>
        /// 机队真实停飞架数：在册飞机中「停场维修」或「已置为不可用」的架数。
        ///
        /// 已退役 / 已退租的**不算停飞** —— 它们退出机队了，不是趴在地上等修。
        /// 「不可用」这一支是给场景夹具与退租语义留的：<c>isActive=false</c> 而
        /// <c>status</c> 仍是适航的飞机，同样不能飞（<c>FlightGroundLogic</c> 判 Retired）。
        /// </summary>
        public static int GroundedCount(GameState gs)
        {
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null) continue;
                if (a.status == AircraftStatuses.Retired) continue;
                if (!a.isActive || a.status == AircraftStatuses.Maintenance) n++;
            }
            return n;
        }

        /// <summary>
        /// 把 <see cref="MaintenanceState.groundedCount"/> 刷成真实值并返回。
        ///
        /// ★2026-10-27 修：这个字段在所有存档里都恒为默认值 0 —— 全仓搜不到任何一处赋值，
        /// 而维护页 KPI（`停飞飞机：{mnt.groundedCount} 架`）、机务汇报邮件、培训页副标题
        /// 三处都在读它。于是无论停了几架，屏幕上永远是「停飞飞机：0 架」，
        /// 与同一屏「机队可用率」互相矛盾，也与机队页的 `维护中` 标签对不上。
        /// 保留字段本身（存档兼容、旧 UI 不用改），只把写入点收到这里。
        /// </summary>
        public static int SyncGroundedCount(GameState gs)
        {
            if (gs?.mnt == null) return 0;
            gs.mnt.groundedCount = GroundedCount(gs);
            return gs.mnt.groundedCount;
        }

        #region 维修策略

        public bool SetStrategy(int strategy, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mnt = EnsureState(gs);
            mnt.strategy = strategy;
            mnt.fleetAvailability = MaintenanceEconomics.AvailabilityByStrategy(strategy);
            message = $"维修策略已切换为 {StrategyName(strategy)}（可用率 {mnt.fleetAvailability:F1}%）。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string StrategyName(int s) => s switch { 0 => "全面预防性维修", 1 => "最低合规维修", _ => "智能预测维修" };

        #endregion

        #region 航材库存与采购

        public bool CreateOrder(string partNo, string supplier, int quantity, float unitCostUsd, bool urgent, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (quantity <= 0) { message = "数量必须 >0"; return false; }
            var mnt = EnsureState(gs);
            var order = new PurchaseOrder
            {
                orderNo = "PO-" + (mnt.orders.Count + 1).ToString("D3"),
                partNo = partNo,
                supplier = supplier ?? "OEM 原厂",
                quantity = quantity,
                unitCostUsd = unitCostUsd,
                status = "草稿",
                leadDays = urgent ? 3 : 15,
                urgent = urgent
            };
            mnt.orders.Add(order);
            message = $"订单 {order.orderNo} 已创建（{quantity} 件 · {MaintenanceEconomics.OrderTotalWan(quantity, unitCostUsd):F2} 万）。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool ConfirmOrder(string orderNo, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mnt = EnsureState(gs);
            var order = mnt.orders.Find(o => o.orderNo == orderNo);
            if (order == null) { message = "订单不存在"; return false; }
            if (order.status == "已下单") { message = $"订单 {order.orderNo} 已入库。"; return true; }
            if (order.status == "已取消") { message = "订单已取消。"; return false; }
            float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
            if (gs.cash < total) { message = $"现金不足（需 {total:F2} 万）。"; return false; }
            gs.cash -= total;
            order.status = "已下单";
            // 入库
            var part = mnt.parts.Find(p => p.partNo == order.partNo);
            if (part != null) part.quantity += order.quantity;
            else mnt.parts.Add(new PartStock
            {
                partNo = order.partNo,
                name = order.partNo,
                category = "B",
                quantity = order.quantity,
                safetyStock = 10,
                unitCostUsd = order.unitCostUsd
            });
            message = $"订单 {order.orderNo} 已下单并入库（{total:F2} 万）。";
            gs.AddDecisionLog("机务：" + message);
            InboxManager.OnPartsOrderFilled(gs, order.orderNo);
            gs.NotifyChanged();
            return true;
        }

        public bool CancelOrder(string orderNo, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mnt = EnsureState(gs);
            var order = mnt.orders.Find(o => o.orderNo == orderNo);
            if (order == null) { message = "订单不存在"; return false; }
            if (order.status == "已下单") { message = "订单已入库，无法取消。"; return false; }
            order.status = "已取消";
            message = $"订单 {order.orderNo} 已取消，未扣资本、未入库。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region OEM 关系

        public bool UpgradeOem(string oem, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var mnt = EnsureState(gs);
            var rel = mnt.oems.Find(o => o.oem == oem);
            if (rel == null) { message = "OEM 不存在"; return false; }
            if (rel.stars >= 5) { message = $"{oem} 已是启动客户级。"; return false; }
            float cost = 50f * rel.stars;
            if (gs.cash < cost) { message = $"关系升级需 {cost:F0} 万。"; return false; }
            gs.cash -= cost;
            rel.stars++;
            rel.discountRate = Mathf.Min(0.15f, rel.stars * 0.03f);
            rel.deliveryPriority = rel.stars >= 3 ? 2 : 1;
            message = $"{oem} 关系升级至 {rel.stars} 星（折扣 {rel.discountRate * 100:F0}%）。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string OemTier(int stars) => stars switch { 1 => "普通", 2 => "重要", 3 => "战略伙伴", 4 => "战略伙伴", _ => "启动客户" };

        #endregion

        #region 定检排程

        public bool ScheduleCheck(string registration, string checkType, int dueMonth, out string message)
        {
            return ScheduleCheck(GameState.Instance, registration, checkType, dueMonth, out message);
        }

        /// <summary>
        /// 显式传入 <paramref name="gs"/> 的重载：自测与工具不必依赖 <c>GameState.Instance</c> 单例
        /// （EditMode 里 <c>AddComponent</c> 不会触发 <c>Awake</c>，单例恒为 null —— 与
        /// <see cref="FleetManager.TryRetireAircraft(GameState, string, out string)"/> 同一处理）。
        /// </summary>
        public bool ScheduleCheck(GameState gs, string registration, string checkType, int dueMonth, out string message)
        {
            if (gs == null) { message = "无 GameState"; return false; }
            var mnt = EnsureState(gs);
            int fleet = gs.fleet != null ? gs.fleet.Count : 1;
            float cost = MaintenanceEconomics.CheckCostWan(checkType, fleet);
            if (gs.cash < cost) { message = $"现金不足（{checkType} 检需 {cost:F0} 万）。"; return false; }
            gs.cash -= cost;
            mnt.checks.Add(new CheckSchedule
            {
                registration = registration ?? "B-1001",
                checkType = checkType,
                dueMonth = dueMonth,
                costWan = cost,
                conflict = false
            });
            message = $"已排 {registration} {checkType} 检（{cost:F0} 万 · 预计第 {dueMonth} 月）。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 维修培训

        public bool StartTypeRatingBatch(int seats, float costWan, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (seats <= 0) { message = "人数必须 >0"; return false; }
            var mnt = EnsureState(gs);
            if (gs.cash < costWan) { message = $"现金不足（机型改装培训需 {costWan:F0} 万）。"; return false; }
            gs.cash -= costWan;
            mnt.trainingCount += seats;
            mnt.budgetUsedWan += costWan;
            HrManager.EnqueueWithRoster(gs, "机型改装培训", seats, costWan, "maint", "MNT");
            message = $"已启动机型改装培训 {seats} 人（{costWan:F0} 万 · 在培 {mnt.trainingCount} 人）。推进季度结业后持证工程师增加，机务档案效率上升。";
            gs.AddDecisionLog("机务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion
    }
}
