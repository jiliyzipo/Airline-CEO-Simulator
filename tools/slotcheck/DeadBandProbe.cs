// 远程线「可指飞」体检：扫描「本线距离 × 机型 → 能否指派飞机」，一眼看出死区。
// 用法：dotnet run --project tools/slotcheck -- --deadband
//
// 2026-10-13 由 CEO 报障（FRA–ATL 7,409 km 无任何飞机可派）引出：值勤预算原先按「当天往返」
// 合并算，而外站过夜口径只在「单段 > 9h」时启用 —— 于是 5,500–7,650 km 之间宽体航程够、
// 值勤却必然超限，窄体航程又不够，成了谁也派不进去的死区。
// 判据改成「当天往返装不进上限就走外站过夜」后，本表应**不存在整行皆✗**的距离。
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace SlotCheck
{
    static class DeadBandProbe
    {
        enum Fit { No, Merged, Layover }

        public static void Run()
        {
            var probe = MakeRoute(7409, "A330-300");
            var demand = RouteGravityModel.Evaluate(null, "FRA", "ATL", 7409f);
            Console.WriteLine($"FRA–ATL 周客流 = {demand.weeklyPax:N0} 人次/周（单向）"
                              + $"　每周班次 = {CapacityEconomics.WeeklyFlightsOf(probe)}"
                              + $"　在册最大座级 = {CapacityEconomics.MaxSeatsInCatalog()} 座");

            string[] types = { "A320neo", "A321LR", "A330-300", "A350-900", "B787-9", "B777-300ER" };
            Console.WriteLine();
            Console.WriteLine(Pad("距离km", 8) + Pad("轮挡h", 7) + Pad("往返值勤h", 11) + "| "
                              + string.Join("  ", Array.ConvertAll(types, t => Pad(t, 12))));
            foreach (int km in new[] { 3000, 4000, 4500, 5000, 5300, 5500, 6000, 6300, 6600, 7000, 7409, 7650, 8000, 9000, 10000, 11000, 12000 })
            {
                var row = new List<string>();
                foreach (var t in types) row.Add(Pad(Cell(km, t), 12));

                var r = MakeRoute(km, types[0]);
                double blockH = OpsRotationLogic.BlockOf(r) / 60.0;
                double duty = (OpsRotationLogic.BlockOf(r) * 2 + OpsRotationLogic.TurnOf(r)
                               + OpsRotationLogic.ReportBeforeMinutes) / 60.0;
                Console.WriteLine(Pad(km.ToString(), 8) + Pad(blockH.ToString("0.##"), 7) + Pad(duty.ToString("0.#"), 11)
                                  + "| " + string.Join("  ", row));
            }
            Console.WriteLine("\n注：可(往返)=当天往返装得下；可(隔夜)=当天往返装不下，但按外站过夜（回程次日）可飞。");
        }

        static string Cell(int km, string type)
        {
            var route = MakeRoute(km, type);
            if (!OpsScheduleLogic.RangeCovers(type, route)) return "航程✗";
            var fit = FitOf(route, type);
            if (fit == Fit.No) return "值勤✗";
            return fit == Fit.Layover ? "可(隔夜)" : "可(往返)";
        }

        /// <summary>直接问排班判据本身 —— 不再复制一份算术，免得探针与实现漂移。</summary>
        static Fit FitOf(RouteData route, string type)
        {
            int block = OpsRotationLogic.BlockOf(route);
            int reportAt = 8 * 60 - OpsRotationLogic.ReportBeforeMinutes;
            var ac = new Aircraft { registration = "B-0001", type = type, isActive = true,
                                    status = AircraftStatuses.Airworthy, age = 2 };
            var plan = new OpsRotationLogic.RotationPlan();
            if (!OpsRotationLogic.CanTake(route, plan, ac, "08:00", null, false, out _, out _)) return Fit.No;

            var merged = OpsRotationLogic.BudgetOf(type, reportAt, block, 2);
            bool mergedFits = 2 <= merged.sectorCap
                              && block * 2 + OpsRotationLogic.TurnOf(route) + OpsRotationLogic.ReportBeforeMinutes
                                 <= merged.limit;
            return mergedFits ? Fit.Merged : Fit.Layover;
        }

        static string Pad(string s, int w) => (s ?? "").PadRight(w);

        static RouteData MakeRoute(int km, string type)
        {
            var r = new RouteData
            {
                routeId = $"R-PROBE-{km}",
                origin = "FRA",
                destination = "ATL",
                distanceKm = km,
                aircraftType = type,
                flightNumber = "FR106",
                registration = "",
                depTime = "08:00",
                arrTime = "",
                daysOfWeek = "每日",
                weeklyFlights = 7,
                groundStaffAssigned = 4,
                baseDailyProfit = 10f,
                dailyProfit = 10f
            };
            r.SyncFlightsFromPrimary();
            return r;
        }
    }
}
