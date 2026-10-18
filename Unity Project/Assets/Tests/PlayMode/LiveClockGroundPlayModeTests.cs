using System;
using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// 执行层「无机航线不得起飞」回归。
    ///
    /// 旧缺陷：<c>LiveClockSimulator.TryTakeoff</c> 写成
    /// <c>var ac = FindAircraft(...); if (ac != null) {…}</c> ——
    /// <c>ac == null</c>（机号为空 / 机号已退役转售 / 老存档残留）时整段校验被跳过，直接
    /// <c>airborne = true</c>，于是「没有执飞机」的班次照飞、照在 <c>Land()</c> 计营收，
    /// 还不消耗航时与机损 —— 等于凭空的收入（「幽灵航班」）。
    ///
    /// 判据统一在 <see cref="FlightGroundLogic"/>（执行层与界面共用），本测试覆盖三条路径：
    /// ① 无执飞机 → 不起飞、不计营收、如实呈报；② 补机 → 复航且营收入账；③ 退役机 → 再次拦下。
    /// 本地↔UTC 换算走 <see cref="GameState.LocalUtcOffset"/>，不硬编码时区。
    /// </summary>
    public class LiveClockGroundPlayModeTests : IsolatedPlayModeFixture
    {
        static DateTime Loc(int h, int m) => new DateTime(2026, 10, 1, h, m, 0);
        static DateTime Utc(DateTime local) => local - GameState.LocalUtcOffset;

        static RouteData MakeRoute(string registration)
        {
            var r = new RouteData
            {
                routeId = "R-GROUND", origin = "PEK", destination = "SHA", distanceKm = 1100f,
                aircraftType = "A320neo", flightNumber = "NG100", registration = registration,
                depTime = "08:00", arrTime = "10:30", daysOfWeek = "每日", weeklyFlights = 7,
                baseDailyProfit = 12f, dailyProfit = 12f, groundStaffAssigned = 2
            };
            r.SyncFlightsFromPrimary();
            return r;
        }

        static bool HasLog(GameState gs, string keyword)
        {
            for (int i = 0; i < gs.decisionLog.Count; i++)
                if (gs.decisionLog[i] != null && gs.decisionLog[i].IndexOf(keyword, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }

        [UnityTest]
        public IEnumerator GroundedRoute_NeverTakesOff_ThenResumesWhenAircraftAssigned()
        {
            var gsGo = new GameObject("LiveClockGroundGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            var route = MakeRoute("");
            gs.activeRoutes.Add(route);

            // —— ① 无执飞机：本地 07:50 → 13:00 覆盖去程起飞/落地 + 回程起飞/落地 ——
            gs.SetSimUtc(Utc(Loc(7, 50)));
            gs.liveQuarterIncome = 0f;
            LiveClockSimulator.Advance(gs, Utc(Loc(7, 50)), Utc(Loc(13, 0)));

            var flight = route.flights[0];
            Assert.IsFalse(flight.airborne, "无机可派不得起飞");
            Assert.IsTrue(string.IsNullOrEmpty(flight.lastOpKey), "从未起飞就不该留下起飞键");
            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "没有执飞机就不得计入任何航班营收");
            Assert.IsTrue(HasLog(gs, "【停飞】"), "应如实呈报停飞原因");
            Assert.AreEqual(GroundReason.NoAssignment, FlightGroundLogic.RouteOf(gs, route));
            // 计划口径（季结按 monthlyRevenue×3 轧差）同样不得算：否则幽灵收入只是改在季末结账
            Assert.AreEqual(0f, gs.monthlyRevenue, 1e-4f, "无执飞机的航线不得计入计划收入（季末也不得照付）");

            // —— ② 补上执飞机：同一窗口必须复航并正常计营收 ——
            gs.fleet.Add(new Aircraft
            {
                registration = "B-9001", type = "A320neo", isActive = true,
                status = AircraftStatuses.Airworthy, age = 3, condition = 100f
            });
            route.registration = "B-9001";
            route.SyncFlightsFromPrimary();
            route.flights[0].registration = "B-9001";
            gs.SetSimUtc(Utc(Loc(7, 50)));
            LiveClockSimulator.Advance(gs, Utc(Loc(7, 50)), Utc(Loc(13, 0)));

            Assert.Greater(gs.liveQuarterIncome, 0f, "有执飞机后应正常入账（一趟往返两段）");
            Assert.IsTrue(HasLog(gs, "【复航】"), "复航应如实呈报");
            Assert.AreEqual(GroundReason.None, FlightGroundLogic.RouteOf(gs, route), "复航后整线判据应转为可执飞");
            Assert.Greater(gs.monthlyRevenue, 0f, "复航后该线必须回到计划收入口径");

            // —— ③ 退役该机：机号还留在航线记录里，同样不得起飞（老存档典型残留）——
            var ac = gs.fleet[0];
            ac.isActive = false;
            ac.status = AircraftStatuses.Retired;
            var f = route.flights[0];
            f.airborne = false;
            f.lastOpKey = null;
            gs.liveQuarterIncome = 0f;
            gs.SetSimUtc(Utc(Loc(7, 50)));
            LiveClockSimulator.Advance(gs, Utc(Loc(7, 50)), Utc(Loc(13, 0)));

            Assert.IsFalse(f.airborne, "退役/转售的飞机不得起飞");
            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "退役机不得计入营收");
            Assert.AreEqual(GroundReason.Retired, FlightGroundLogic.RouteOf(gs, route));
            Assert.AreEqual(0f, gs.monthlyRevenue, 1e-4f, "退役机所在航线必须退出计划收入口径");
            Assert.AreEqual("（已退役/转售）", FlightGroundLogic.RegNote(gs, "B-9001"),
                "界面机号标注必须与执行层同一判据");

            // 限定命名空间：本文件有 using System，裸 Object 会在 System.Object 与 UnityEngine.Object 之间二义。
            UnityEngine.Object.Destroy(gsGo);
            yield return null;
        }
    }
}
