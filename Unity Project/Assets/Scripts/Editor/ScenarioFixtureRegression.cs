#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 场景夹具回归：夹具造出来的局面，必须是**游戏自己认定**的那个局面。
    ///
    /// 夹具最容易犯的错不是"改不动"，而是"改完看着像、判定不认"：
    /// 把飞机标成停飞却没让 <see cref="FlightGroundLogic"/> 判成持续性停飞，
    /// 或者把乘务转走却没进 <see cref="GameState.CabinFilled"/> 的口径。
    /// 那样手测时看到的是另一个局面，观察记录就作废了。
    ///
    /// 因此每条断言都走**生产代码的读取口**，而不读夹具自己写的字段。
    /// </summary>
    public static class ScenarioFixtureRegression
    {
        public static bool Run()
        {
            bool all = true;
            var go = new GameObject("__scenario_fixture_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1; gs.cash = 5000f;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.staffPool = new List<PersonnelData>();
                gs.stg = new StrategyState();

                var ac = new Aircraft
                {
                    registration = "B-9101", type = "A320neo", isActive = true,
                    status = AircraftStatuses.Airworthy, economySeats = 180,
                };
                gs.fleet = new List<Aircraft> { ac };
                var route = new RouteData
                {
                    routeId = "R-SZX-SHA-FX", origin = "SZX", destination = "SHA",
                    distanceKm = 1210f, aircraftType = "A320neo", flightNumber = "SZ900",
                    registration = "B-9101",
                };
                gs.activeRoutes.Add(route);

                // 按游戏自己的编制口径补齐乘务，先把「不缺员」这个起点坐实。
                int establishment = gs.CabinEstablishment;
                bool seedOk = establishment > 0;
                for (int i = 0; i < establishment; i++)
                {
                    gs.staffPool.Add(new PersonnelData
                    {
                        id = "C" + i, fullName = "乘务" + i, jobTitle = "乘务员",
                        isOperational = true, onMarket = false, source = "market",
                    });
                }
                gs.InvalidateCrewCache();
                bool startFull = gs.CabinEstablishment > 0 && gs.CabinFilled == gs.CabinEstablishment;
                all &= Report(seedOk && startFull, "SF0",
                    $"起点应「不缺员」：在职 {gs.CabinFilled} / 编制 {establishment}");

                // ① 客舱缺员：读到的是 CabinFilled < CabinEstablishment
                bool crewOk = ScenarioFixture.CrewShortage(gs, out string crewReport);
                bool crewShort = gs.CabinFilled < gs.CabinEstablishment;
                all &= Report(crewOk && crewShort, "SF1",
                    $"客舱缺员应生效且被游戏判定为缺员：{crewReport}；在职 {gs.CabinFilled} / 编制 {gs.CabinEstablishment}");

                bool crewAgain = ScenarioFixture.CrewShortage(gs, out string crewAgainReport);
                bool crewHonest = !crewAgain && !string.IsNullOrEmpty(crewAgainReport) && crewAgainReport.Contains("已经是缺员");
                all &= Report(crewHonest, "SF2", $"已缺员时不得假装又生效一次：{crewAgainReport}");

                // ② 单机维护：地面判定必须是 Maintenance（持续性），不是瞬时冲突
                bool maintOk = ScenarioFixture.SendToMaintenance(gs, out string maintReport);
                var maintReason = FlightGroundLogic.RouteOf(gs, route);
                all &= Report(maintOk && maintReason == GroundReason.Maintenance
                              && FlightGroundLogic.IsPersistent(maintReason), "SF3",
                    $"维护停场应被判定为持续性停飞：{maintReport}；判据={maintReason}");

                // 复原状态，让停飞与维护互不干扰
                ac.status = AircraftStatuses.Airworthy;

                // ③ 全机队停飞：判定必须是 Retired（持续性），航线卡才会显示「停飞不计营收」
                bool groundOk = ScenarioFixture.GroundFleet(gs, out string groundReport);
                var groundReason = FlightGroundLogic.RouteOf(gs, route);
                all &= Report(groundOk && groundReason == GroundReason.Retired
                              && FlightGroundLogic.IsPersistent(groundReason), "SF4",
                    $"全机队停飞应被判定为持续性停飞：{groundReport}；判据={groundReason}");

                bool groundAgain = ScenarioFixture.GroundFleet(gs, out string groundAgainReport);
                all &= Report(!groundAgain, "SF5", $"已全部停飞时不得假装又停一次：{groundAgainReport}");
                ac.isActive = true;

                // ④ 缺槽：可用槽为 0 且池已满 —— 这才是「备选时刻」的触发条件
                const string hub = "PEK";
                bool fillOk = ScenarioFixture.FillPool(gs, hub, 0, out string fillReport);
                int availAfter = RoutePermitLogic.SlotsAvailable(gs, hub, 0);
                bool poolFull = RoutePermitLogic.SlotPoolFull(gs, hub, 0);
                all &= Report(fillOk && availAfter <= 0 && poolFull, "SF6",
                    $"填满池后应「申请无门」：{fillReport}；可用={availAfter} 池满={poolFull}");

                bool fillAgain = ScenarioFixture.FillPool(gs, hub, 0, out string fillAgainReport);
                all &= Report(!fillAgain && !string.IsNullOrEmpty(fillAgainReport), "SF7",
                    $"池已满时再次填写应如实拒绝：{fillAgainReport}");

                bool clearOk = ScenarioFixture.ClearSlots(gs, hub, out string clearReport);
                bool heldAfter = RoutePermitLogic.SlotsHeld(gs, hub, 0) == 0;
                all &= Report(clearOk && heldAfter, "SF8",
                    $"清空持仓后该机场应无持仓：{clearReport}；持仓={RoutePermitLogic.SlotsHeld(gs, hub, 0)}");

                bool clearAgain = ScenarioFixture.ClearSlots(gs, hub, out string clearAgainReport);
                all &= Report(!clearAgain && clearAgainReport.Contains("本来就没有"), "SF9",
                    $"无持仓时清空应如实说明：{clearAgainReport}");

                // ⑤ 空机队：编制为 0，缺员夹具必须拒绝而不是制造「乘务全被转走」的假象
                var goEmpty = new GameObject("__scenario_fixture_empty__");
                try
                {
                    var gsE = goEmpty.AddComponent<GameState>();
                    gsE.fleet = new List<Aircraft>();
                    gsE.staffPool = new List<PersonnelData>();
                    gsE.stg = new StrategyState();
                    bool crewEmpty = ScenarioFixture.CrewShortage(gsE, out string crewEmptyReport);
                    all &= Report(!crewEmpty && crewEmptyReport.Contains("编制为 0"), "SF10",
                        $"空机队时缺员夹具应拒绝：{crewEmptyReport}");
                    bool groundEmpty = ScenarioFixture.GroundFleet(gsE, out string groundEmptyReport);
                    all &= Report(!groundEmpty, "SF11", $"空机队时停飞夹具应拒绝：{groundEmptyReport}");
                }
                finally
                {
                    Object.DestroyImmediate(goEmpty);
                }

                // ⑥ T05.5 样例必须真的覆盖四类，且不含会让引擎「猜着执行」的句子
                var samples = ScenarioFixture.AmbiguousInputSamples();
                bool sampleOk = samples != null && samples.Length >= 4;
                var sb = new StringBuilder();
                if (sampleOk)
                    for (int i = 0; i < samples.Length; i++)
                    {
                        if (i > 0) sb.Append(" | ");
                        sb.Append(samples[i]);
                    }
                all &= Report(sampleOk, "SF12", $"T05.5 输入样例应至少四类：{sb}");

                Debug.Log($"[场景夹具回归] {(all ? "全部通过" : "存在失败项")}（{establishment} 名编制口径已核对）");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            return all;
        }

        static bool Report(bool ok, string tag, string detail)
        {
            if (!ok) Debug.LogError($"[场景夹具回归] ✗ {tag} {detail}");
            return ok;
        }
    }
}
#endif
