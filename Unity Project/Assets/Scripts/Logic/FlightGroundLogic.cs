using System;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>本班「飞不了」的原因（无执飞机 / 机号不在册 / 退役转售 / 退租 / 停场 / 投入过渡业务 / 时刻冲突）。</summary>
    public enum GroundReason
    {
        /// <summary>具备执飞资格。</summary>
        None = 0,
        /// <summary>未指派执飞机（机号为空）。</summary>
        NoAssignment,
        /// <summary>机号已不在机队（转售 / 老存档残留）。</summary>
        Missing,
        /// <summary>在册但不可用：已退役（自有转售）/ 已退租（租赁交还出租人）/ 未激活。</summary>
        Retired,
        /// <summary>停场维修。</summary>
        Maintenance,
        /// <summary>已投入过渡业务：干租 / 湿租 / 包机 / 培训（含已预约机位）。</summary>
        LeasedOut,
        /// <summary>该机此刻正在执飞其它班次（时刻冲突，通常下一 tick 自愈）。</summary>
        Conflict,
    }

    /// <summary>
    /// 「本班为什么飞不了」的唯一权威判据。供两处共用，避免口径分叉：
    /// <list type="bullet">
    /// <item><see cref="AirlineCEO.Systems.LiveClockSimulator"/>：拒绝起飞并如实呈报（决策日志 + 复航）；</item>
    /// <item>界面（航线列表卡片 / 单航线详情 / 地图航迹）：标注机号为何不可用，并停掉「幽灵航班」动画。</item>
    /// </list>
    ///
    /// 背景（本类存在的理由）：旧实现里 <c>TryTakeoff</c> 在 <c>ac == null</c>（无机号或机号不在册）时
    /// **不拦截**，直接 <c>flight.airborne = true</c>，于是产生「幽灵航班」——没有执飞机，却在
    /// <c>Land()</c> 照常计营收，还不消耗航时 / 机损，等于凭空的收入；地图上也照画一架飞机在飞。
    /// </summary>
    public static class FlightGroundLogic
    {
        /// <summary>在册机队里按注册号取飞机（大小写不敏感）。</summary>
        public static Aircraft FindAircraft(GameState gs, string reg)
        {
            if (gs?.fleet == null || string.IsNullOrEmpty(reg)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && string.Equals(a.registration, reg, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        /// <summary>本班实际使用的机号：班次上的机号优先，缺失时回退到航线平面字段（老存档兼容）。</summary>
        public static string RegistrationOf(RouteData route, RouteFlight flight)
        {
            if (flight != null && !string.IsNullOrEmpty(flight.registration)) return flight.registration;
            return route != null ? route.registration : null;
        }

        /// <summary>本班当前能否执飞；不能则给出原因（判据顺序与 <c>LiveClockSimulator</c> 的落地行为一致）。</summary>
        public static GroundReason Of(GameState gs, RouteData route, RouteFlight flight)
            => OfRegistration(gs, RegistrationOf(route, flight));

        /// <summary>按机号判断执飞资格（班次维度的判据本体）。机队为空（含 <paramref name="gs"/> 为空）时无法核实机号 → <see cref="GroundReason.Missing"/>。</summary>
        public static GroundReason OfRegistration(GameState gs, string registration)
        {
            if (string.IsNullOrEmpty(registration)) return GroundReason.NoAssignment;

            var ac = FindAircraft(gs, registration);
            if (ac == null) return GroundReason.Missing;
            if (!ac.isActive || ac.status == AircraftStatuses.Retired) return GroundReason.Retired;
            if (ac.status == AircraftStatuses.Maintenance) return GroundReason.Maintenance;
            // 已投入过渡业务（干租/湿租/包机/培训）的飞机不执飞定期航班
            if (TransitionBusinessLogic.IsBusy(gs, registration)) return GroundReason.LeasedOut;
            if (ac.status == AircraftStatuses.InFlight) return GroundReason.Conflict;
            return GroundReason.None;
        }

        /// <summary>
        /// 整条航线的执飞资格：只要有任一班无机可派即如实标出。
        /// 只返回**持续性**原因——瞬时冲突（该机正在执飞它班）会在窗口内自愈，
        /// 界面每次重绘都可能命中，若据此标红会闪，故一律忽略。
        /// </summary>
        public static GroundReason RouteOf(GameState gs, RouteData route)
        {
            // 界面在 GameState 尚未就绪时也会构造卡片：此时一律按「可执飞」放过，
            // 绝不能在读档前给所有航线挂一片假红。
            if (gs == null) return GroundReason.None;
            if (route == null) return GroundReason.NoAssignment;
            route.EnsureFlights();
            if (route.flights == null || route.flights.Count == 0)
                return OfRegistration(gs, route.registration);

            var transient = GroundReason.None;
            for (int i = 0; i < route.flights.Count; i++)
            {
                var r = Of(gs, route, route.flights[i]);
                if (r == GroundReason.None) continue;
                if (IsPersistent(r)) return r;
                if (transient == GroundReason.None) transient = r;
            }
            return transient == GroundReason.Conflict ? GroundReason.None : transient;
        }

        /// <summary>是否「持续性」停飞（值得呈报 CEO）。瞬时冲突不算：它会在起飞窗口内重试自愈。</summary>
        public static bool IsPersistent(GroundReason r)
            => r == GroundReason.NoAssignment || r == GroundReason.Missing || r == GroundReason.Retired
               || r == GroundReason.Maintenance || r == GroundReason.LeasedOut;

        /// <summary>停飞原因一句式（呈报正文用）。</summary>
        public static string Label(GroundReason r)
        {
            switch (r)
            {
                case GroundReason.NoAssignment: return "未指派执飞机";
                case GroundReason.Missing: return "机号已不在机队（退役或转售）";
                case GroundReason.Retired: return "飞机已退役或退租（自有转售 / 租赁交还出租人）";
                case GroundReason.Maintenance: return "飞机停场维修";
                case GroundReason.LeasedOut: return "飞机已投入过渡业务";
                case GroundReason.Conflict: return "该机正在执飞其它班次（时刻冲突）";
                default: return "";
            }
        }

        /// <summary>给 CEO 的下一步建议（呈报正文用）。</summary>
        public static string Advice(GroundReason r)
        {
            switch (r)
            {
                case GroundReason.NoAssignment:
                case GroundReason.Missing:
                case GroundReason.Retired:
                    return "请在航线页下达运力指令改派执飞机";
                case GroundReason.Maintenance:
                    return "请等待出厂，或下达运力指令改派执飞机";
                case GroundReason.LeasedOut:
                    return "请召回该机，或下达运力指令改派执飞机";
                case GroundReason.Conflict:
                    return "请调整班次时刻或改派执飞机";
                default:
                    return "";
            }
        }

        /// <summary>界面用短标注（贴在机号后）；可执飞或仅瞬时冲突时返回空串。</summary>
        public static string RegNote(GameState gs, string registration)
        {
            if (gs == null) return "";
            switch (OfRegistration(gs, registration))
            {
                case GroundReason.Missing: return "（已不在机队）";
                case GroundReason.Retired:
                {
                    // 自有飞机是「已退役/转售」，租赁机是「已退租（交还出租人）」—— 机号后面的标注不该一刀切
                    var ac = FindAircraft(gs, registration);
                    return ac != null && ac.ExitLabel == FleetEconomics.DisposalReturned
                        ? "（已退租）"
                        : "（已退役/转售）";
                }
                case GroundReason.Maintenance: return "（停场维修）";
                case GroundReason.LeasedOut: return "（已投入过渡业务）";
                default: return "";
            }
        }
    }
}
