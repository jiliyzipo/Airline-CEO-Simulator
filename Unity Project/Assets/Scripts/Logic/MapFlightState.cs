using System;

namespace AirlineCEO.Logic
{
    /// <summary>地图上一班飞机此刻处在往返循环的哪一段。</summary>
    public enum MapFlightPhase
    {
        /// <summary>今天还没到起飞时刻（或今天不飞）：停在出发场。</summary>
        Scheduled = 0,
        /// <summary>去程在空。</summary>
        Outbound = 1,
        /// <summary>去程已落地、回程未起飞：停在到达场过站。</summary>
        Turnaround = 2,
        /// <summary>回程在空。</summary>
        Inbound = 3,
        /// <summary>当天两段都飞完：回到出发场。</summary>
        Landed = 4,
    }

    /// <summary>
    /// 一班飞机此刻的快照。<see cref="MapFrac"/> 用于在地图上定位（0=出发场，1=到达场），
    /// <see cref="LegProgress01"/> 用于信息卡上的进度条（**当前航段**的进度，回程时从 0 重新数）。
    ///
    /// 两个进度必须分开：地图位置沿「去→回」是一条折返线（回程时数值递减），
    /// 而玩家看进度条时想的是「这一班飞了多少」——回程也从 0 开始。
    /// 混用会出现在回程上进度条倒着走。
    /// </summary>
    public struct MapFlightSnapshot
    {
        public MapFlightPhase phase;
        public bool inAir;
        /// <summary>0（出发场）→ 1（到达场）的地图位置。</summary>
        public float mapFrac;
        /// <summary>当前航段进度 0→1（待起飞/过站/已落地为 0 或 1，见实现）。</summary>
        public float legProgress01;
        public DateTime legDep;
        public DateTime legArr;
        public int legMinutes;

        public string PhaseLabel => phase switch
        {
            MapFlightPhase.Outbound => "去程在空",
            MapFlightPhase.Turnaround => "过站中",
            MapFlightPhase.Inbound => "回程在空",
            MapFlightPhase.Landed => "已落地",
            _ => "待起飞",
        };
    }

    /// <summary>
    /// 一条航线该画成「已飞（实线）」与「待飞（虚线）」两段的划分。
    ///
    /// 参数都是**弧长比例**（0=出发场，1=到达场），与 <see cref="MapFlightSnapshot.mapFrac"/> 同一坐标系。
    /// 之所以抽成纯逻辑函数：这个划分是地图上最容易写错的地方（回程方向相反），
    /// 放在 UnityEngine 之外才能在离线工具里逐阶段断言。
    /// </summary>
    public struct RouteSpanSplit
    {
        /// <summary>实线段起点（弧长比例）；<c>hasSolid=false</c> 时无意义。</summary>
        public float solidFrom;
        /// <summary>实线段终点。</summary>
        public float solidTo;
        /// <summary>是否画实线。</summary>
        public bool hasSolid;
        /// <summary>虚线段起点。</summary>
        public float dashedFrom;
        /// <summary>虚线段终点。</summary>
        public float dashedTo;
        /// <summary>是否画虚线。</summary>
        public bool hasDashed;
    }

    /// <summary>
    /// 由本地时刻推出「这班飞机现在在哪一段」。**这是地图与信息卡的唯一口径**：
    /// <see cref="LiveClockLogic.MapRouteFrac"/> 只是它的一个投影（保留旧签名给其他调用方）。
    /// </summary>
    public static class MapFlightState
    {
        /// <summary>
        /// 实线/虚线划分（对齐 Airline Sim：已飞过的是实线，前方待飞的是虚线）。
        ///
        /// · 待起飞：还没飞 —— 整条虚线；
        /// · 去程在空：出发场→飞机 已飞（0→frac），飞机→到达场 待飞（frac→1）；
        /// · 过站：去程整段飞完 —— 整条实线（回程还没开始，但那条线已经飞过了）；
        /// · 回程在空：**方向相反** —— 到达场→飞机 已飞（frac→1），飞机→出发场 待飞（0→frac）；
        /// · 已落地：两段都飞完 —— 整条实线。
        ///
        /// 过站与已落地用「整条实线」而不是「整条虚线」，是因为玩家问的是「这条线我飞过没有」：
        /// 飞过就是实线。回程的待飞部分在回程开始后才会变回虚线 —— 这与上面的分段规则一致。
        /// </summary>
        public static RouteSpanSplit Split(MapFlightPhase phase, float mapFrac)
        {
            float f = mapFrac;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            var s = new RouteSpanSplit();
            switch (phase)
            {
                case MapFlightPhase.Turnaround:
                case MapFlightPhase.Landed:
                    s.hasSolid = true;
                    s.solidFrom = 0f;
                    s.solidTo = 1f;
                    break;

                case MapFlightPhase.Outbound:
                    s.hasSolid = f > 0f;
                    s.solidFrom = 0f;
                    s.solidTo = f;
                    s.hasDashed = f < 1f;
                    s.dashedFrom = f;
                    s.dashedTo = 1f;
                    break;

                case MapFlightPhase.Inbound:
                    s.hasSolid = f < 1f;
                    s.solidFrom = f;
                    s.solidTo = 1f;
                    s.hasDashed = f > 0f;
                    s.dashedFrom = 0f;
                    s.dashedTo = f;
                    break;

                default:   // Scheduled：一段都没飞
                    s.hasDashed = true;
                    s.dashedFrom = 0f;
                    s.dashedTo = 1f;
                    break;
            }
            return s;
        }

        public static MapFlightSnapshot Snapshot(DateTime localNow, string daysOfWeek, int weeklyFlights,
            string depHhmm, string arrHhmm, float distanceKm, string aircraftType, int groundStaff)
        {
            var s = new MapFlightSnapshot { phase = MapFlightPhase.Scheduled };
            DateTime today = localNow.Date;

            // 与 MapRouteFrac 同一次遍历：先看昨天（跨夜/次日凌晨仍在回程），再看今天。
            // 后处理的日期覆盖前一个 —— 「今天的班次」永远比「昨天没飞完的」更贴近玩家所见。
            for (int back = 1; back >= 0; back--)
            {
                DateTime day = today.AddDays(-back);
                if (!LiveClockLogic.OperatesOn(daysOfWeek, day, weeklyFlights)) continue;

                LiveClockLogic.RoundTripWindow(day, depHhmm, arrHhmm, distanceKm, aircraftType, groundStaff,
                    out DateTime outDep, out DateTime outArr, out DateTime retDep, out DateTime retArr);

                int legMin = Math.Max(0, (int)(outArr - outDep).TotalMinutes);

                float pOut = LiveClockLogic.AirborneProgress01(localNow, outDep, outArr);
                if (pOut >= 0f)
                {
                    s.phase = MapFlightPhase.Outbound;
                    s.inAir = true;
                    s.mapFrac = pOut;
                    s.legProgress01 = pOut;
                    s.legDep = outDep;
                    s.legArr = outArr;
                    s.legMinutes = legMin;
                    return s;                       // 在空即刻定案（与 MapRouteFrac 的早退一致：
                                                    // 跨夜回程必须先于「今天那一轮」返回）
                }

                float pRet = LiveClockLogic.AirborneProgress01(localNow, retDep, retArr);
                if (pRet >= 0f)
                {
                    s.phase = MapFlightPhase.Inbound;
                    s.inAir = true;
                    s.mapFrac = 1f - pRet;          // 地图上是折返：越飞越靠近出发场
                    s.legProgress01 = pRet;         // 但进度条按「回程这一班」从 0 数
                    s.legDep = retDep;
                    s.legArr = retArr;
                    s.legMinutes = legMin;
                    return s;                       // 在空即刻定案（同上）
                }

                if (localNow >= outArr && localNow < retDep)
                {
                    s.phase = MapFlightPhase.Turnaround;
                    s.inAir = false;
                    s.mapFrac = 1f;
                    s.legProgress01 = 0f;
                    s.legDep = retDep;
                    s.legArr = retArr;
                    s.legMinutes = legMin;
                    continue;
                }

                if (localNow >= retArr)
                {
                    // 昨天飞到很晚、今天还没起飞：如实说「已落地」，不要说「待起飞」——
                    // 玩家看的是「这架飞机现在停在哪」，而这架此刻在出发场。
                    s.phase = MapFlightPhase.Landed;
                    s.inAir = false;
                    s.mapFrac = 0f;
                    s.legProgress01 = 1f;
                    s.legDep = outDep;
                    s.legArr = outArr;
                    s.legMinutes = legMin;
                    continue;
                }

                // 今天还没到起飞时刻
                s.phase = MapFlightPhase.Scheduled;
                s.inAir = false;
                s.mapFrac = 0f;
                s.legProgress01 = 0f;
                s.legDep = outDep;
                s.legArr = outArr;
                s.legMinutes = legMin;
            }

            if (s.legMinutes <= 0)
            {
                // 今天与昨天都不飞（或被班期排除）：仍然给出一个可算的航段时长，
                // 否则信息卡会印出「0 分钟」这种显然是假的读数。
                var outDep = LiveClockLogic.DepartLocal(today, depHhmm);
                var outArr = LiveClockLogic.ArriveLocal(outDep, depHhmm, arrHhmm, distanceKm);
                s.legDep = outDep;
                s.legArr = outArr;
                s.legMinutes = Math.Max(0, (int)(outArr - outDep).TotalMinutes);
            }
            return s;
        }
    }
}
