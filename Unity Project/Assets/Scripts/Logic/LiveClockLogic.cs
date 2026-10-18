using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 1× 与现实时钟对齐后的班期/轮挡纯函数（起飞降落对照本地时刻）。
    /// </summary>
    public static class LiveClockLogic
    {
        public const int MaxCatchUpMinutes = 24 * 60;

        public static int ParseMinutes(string hhmm)
        {
            if (string.IsNullOrEmpty(hhmm)) return 8 * 60;
            int colon = hhmm.IndexOf(':');
            int h = 8, m = 0;
            if (colon > 0)
            {
                int.TryParse(hhmm.Substring(0, colon), out h);
                if (colon + 1 < hhmm.Length)
                    int.TryParse(hhmm.Substring(colon + 1), out m);
            }
            else if (hhmm.Length >= 2)
                int.TryParse(hhmm.Substring(0, 2), out h);
            if (h < 0) h = 0;
            if (h > 23) h = 23;
            if (m < 0) m = 0;
            if (m > 59) m = 59;
            return h * 60 + m;
        }

        /// <summary>轮挡分钟：优先到达−起飞（可跨夜），否则用距离巡航估算。</summary>
        public static int BlockMinutes(string depHhmm, string arrHhmm, float distanceKm)
        {
            int dep = ParseMinutes(depHhmm);
            int arr = ParseMinutes(arrHhmm);
            int span = arr - dep;
            if (span <= 0) span += 24 * 60;
            int fromDist = (int)Math.Round(OpsEconomics.BlockHours(distanceKm) * 60.0);
            if (fromDist < 45) fromDist = 45;
            if (span < 40) return fromDist;
            return span;
        }

        public static bool OperatesOn(string daysOfWeek, DateTime localDay, int weeklyFlights)
        {
            var d = localDay.Date;
            string s = daysOfWeek ?? "";
            if (s.Length == 0 || s.IndexOf("每日", StringComparison.Ordinal) >= 0
                || s.IndexOf("每天", StringComparison.Ordinal) >= 0)
            {
                int w = weeklyFlights <= 0 ? 7 : weeklyFlights;
                if (w >= 7) return true;
                return (d.DayOfYear % 7) < w;
            }

            int dow = (int)d.DayOfWeek; // 0=Sun
            if (ContainsZhWeekday(s, dow)) return true;
            char num = dow == 0 ? '7' : (char)('0' + dow);
            return s.IndexOf(num) >= 0;
        }

        static bool ContainsZhWeekday(string s, int dowSun0)
        {
            switch (dowSun0)
            {
                case 0: return s.IndexOf('日') >= 0 || s.IndexOf('天') >= 0;
                case 1: return s.IndexOf('一') >= 0;
                case 2: return s.IndexOf('二') >= 0;
                case 3: return s.IndexOf('三') >= 0;
                case 4: return s.IndexOf('四') >= 0;
                case 5: return s.IndexOf('五') >= 0;
                case 6: return s.IndexOf('六') >= 0;
                default: return false;
            }
        }

        public static DateTime DepartLocal(DateTime localDay, string depHhmm)
        {
            int m = ParseMinutes(depHhmm);
            var d = localDay.Date;
            return new DateTime(d.Year, d.Month, d.Day, m / 60, m % 60, 0, DateTimeKind.Unspecified);
        }

        public static DateTime ArriveLocal(DateTime departLocal, string depHhmm, string arrHhmm, float distanceKm)
        {
            return departLocal.AddMinutes(BlockMinutes(depHhmm, arrHhmm, distanceKm));
        }

        /// <summary>0 在起点，1 在终点。未在空中返回 −1。</summary>
        public static float AirborneProgress01(DateTime localNow, DateTime departLocal, DateTime arriveLocal)
        {
            if (localNow < departLocal || localNow > arriveLocal) return -1f;
            double span = (arriveLocal - departLocal).TotalMinutes;
            if (span < 1) return 1f;
            double t = (localNow - departLocal).TotalMinutes / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return (float)t;
        }

        /// <summary>过站分钟（去程落地后至回程起飞）。</summary>
        public static int TurnMinutes(string aircraftType, int groundStaffAssigned)
        {
            int t = (int)Math.Round(OpsEconomics.EstimateTurnMinutes(aircraftType, groundStaffAssigned));
            if (t < 45) t = 45;
            return t;
        }

        public static void RoundTripWindow(DateTime localDay, string depHhmm, string arrHhmm, float distanceKm,
            string aircraftType, int groundStaff,
            out DateTime outDep, out DateTime outArr, out DateTime retDep, out DateTime retArr)
        {
            outDep = DepartLocal(localDay, depHhmm);
            outArr = ArriveLocal(outDep, depHhmm, arrHhmm, distanceKm);
            int turn = TurnMinutes(aircraftType, groundStaff);
            retDep = outArr.AddMinutes(turn);
            retArr = retDep.AddMinutes(BlockMinutes(depHhmm, arrHhmm, distanceKm));
        }

        /// <summary>
        /// 地图进度：0 在出发机场，1 在到达机场。去程 0→1，过站停 1，回程 1→0。
        /// </summary>
        public static float MapRouteFrac(DateTime localNow, string daysOfWeek, int weeklyFlights,
            string depHhmm, string arrHhmm, float distanceKm, string aircraftType, int groundStaff, out bool inAir)
        {
            inAir = false;
            DateTime today = localNow.Date;
            float parked = 0f;
            for (int back = 1; back >= 0; back--)
            {
                DateTime day = today.AddDays(-back);
                if (!OperatesOn(daysOfWeek, day, weeklyFlights)) continue;
                RoundTripWindow(day, depHhmm, arrHhmm, distanceKm, aircraftType, groundStaff,
                    out DateTime outDep, out DateTime outArr, out DateTime retDep, out DateTime retArr);

                float pOut = AirborneProgress01(localNow, outDep, outArr);
                if (pOut >= 0f)
                {
                    inAir = true;
                    return pOut;
                }
                float pRet = AirborneProgress01(localNow, retDep, retArr);
                if (pRet >= 0f)
                {
                    inAir = true;
                    return 1f - pRet;
                }
                if (localNow >= outArr && localNow < retDep)
                    parked = 1f;
                else if (localNow >= retArr)
                    parked = 0f;
                else if (localNow < outDep)
                    parked = 0f;
            }
            return parked;
        }

        public static string OpKey(DateTime localDepart)
        {
            return localDepart.ToString("yyyyMMddHHmm");
        }

        /// <summary>
        /// 日固定成本（万美元/日）。
        /// operationalPayrollMonth 为运行岗（飞行员/机务/乘务）月度实发薪酬；
        /// laborCostMonth 为存量人力成本（未逐人建模的全公司员工）月度现金成本。
        /// 两者默认 0 —— 传 0 时结果与既有行为完全一致，保护回归基线。
        /// </summary>
        public static float DailyFixedCostWan(float monthlyBurn, float fleetMaint, float fleetLease, float ceoMonth, float crisisBonus, float operationalPayrollMonth = 0f, float laborCostMonth = 0f)
        {
            float month = monthlyBurn + fleetMaint + fleetLease + ceoMonth;
            if (crisisBonus > 0f) month += crisisBonus;
            if (operationalPayrollMonth > 0f) month += operationalPayrollMonth;
            if (laborCostMonth > 0f) month += laborCostMonth;
            if (month < 0f) month = 0f;
            return month / 30f;
        }
    }
}
