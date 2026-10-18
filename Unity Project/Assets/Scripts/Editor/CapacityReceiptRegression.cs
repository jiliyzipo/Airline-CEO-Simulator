#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;
namespace AirlineCEO.EditorTools {
/// <summary>Isolated receipt regression. Does not run or clear the player directive lexicon.</summary>
public static class CapacityReceiptRegression { public static void Run() {
var goR42 = new GameObject("__r42_receipt_gs__");
                try
                {
                    var gsR42 = goR42.AddComponent<GameState>();
                    gsR42.year = 1; gsR42.quarter = 1; gsR42.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)); gsR42.cash = 50000f;
                    gsR42.onTimePercent = 88f;
                    gsR42.stg = new StrategyState();
                    gsR42.inbox = new List<InboxMail>();
                    gsR42.pendingInbox = new List<PendingInboxMail>();
                    gsR42.flightRings = new List<FlightRing>();
                    gsR42.fleet = new List<Aircraft>
                    {
                        new Aircraft { registration = "B-3000", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 }
                    };
                    var rR42 = new RouteData
                    {
                        routeId = "R-PEK-SHA-R42", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                        aircraftType = "A320neo", flightNumber = "NS242", registration = "B-3000",
                        depTime = "06:30", arrTime = "07:46", daysOfWeek = "每日", weeklyFlights = 7,
                        baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                    };
                    rR42.SyncFlightsFromPrimary();
                    gsR42.activeRoutes = new List<RouteData> { rR42 };

                    int wavesR42 = rR42.FlightCount;
                    var repR42 = CapacityDirectiveLogic.Execute(gsR42, rR42, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
                    InboxManager.QueueCapacityReport(gsR42, rR42, repR42);
                    var repMailA = gsR42.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction);
                    bool factsStamped = repMailA != null
                                        && repMailA.capTargetWaves == 3 && repMailA.capBeforeWaves == wavesR42
                                        && !string.IsNullOrEmpty(repMailA.capGapSection);
                    Debug.Log($"R42 呈报留存 → 目标={repMailA?.capTargetWaves}（期望 3）　指令前={repMailA?.capBeforeWaves}（期望 {wavesR42}）"
                              + $"　缺口段已留={!string.IsNullOrEmpty(repMailA?.capGapSection)}（期望 True）"
                              + $"　实排 {rR42.FlightCount} 班（期望 {wavesR42}：缺槽排不出）　{factsStamped}");

                    // ③／④ 重复下同一条令：不再叠单；旧呈报转为「已作废」
                    int applyBefore = gsR42.inbox.FindAll(x => x != null && x.action == InboxManager.ApplySlotAction).Count;
                    var repR42b = CapacityDirectiveLogic.Execute(gsR42, rR42, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
                    InboxManager.QueueCapacityReport(gsR42, rR42, repR42b);
                    int applyAfter = gsR42.inbox.FindAll(x => x != null && x.action == InboxManager.ApplySlotAction).Count;
                    var repMailB = gsR42.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(x));
                    bool superseded = repMailA != null && repMailA.status == InboxManager.SupersededStatus
                                      && InboxManager.StatusLabel(repMailA.status) == "已作废"
                                      && !InboxManager.IsOpen(repMailA) && repMailB != null && repMailB != repMailA;
                    Debug.Log($"R42 重复下令 → 申请单 {applyBefore} → {applyAfter}（期望不变：同线同刻不叠单）"
                              + $"　旧呈报状态={InboxManager.StatusLabel(repMailA?.status)}（期望 已作废）"
                              + $"　新呈报另起={repMailB != null && repMailB != repMailA}"
                              + $"　{((applyAfter == applyBefore && superseded) ? "✓" : "✗")}");

                    // ①／② 批准这封「先批时刻槽」的呈报 → 回执必须说「未变动」，不许说「已生效」
                    InboxManager.ApproveCapacityDirective(gsR42, repMailB, out string _);
                    var receiptA = LastPendingMail(gsR42, "fx_cap_");
                    string subjA = receiptA?.subject ?? "";
                    string bodyA = receiptA?.body ?? "";
                    bool inFlightOk = applyAfter <= 0 || bodyA.IndexOf("待您批准", StringComparison.Ordinal) >= 0;
                    bool unchangedOk = receiptA != null
                                       && subjA.IndexOf("未变动", StringComparison.Ordinal) >= 0
                                       && subjA.IndexOf("已生效", StringComparison.Ordinal) < 0
                                       && bodyA.IndexOf("一班未动", StringComparison.Ordinal) >= 0
                                       && bodyA.IndexOf("运力缺口", StringComparison.Ordinal) >= 0
                                       && bodyA.IndexOf("不视为缺口时段已解决", StringComparison.Ordinal) >= 0
                                       && inFlightOk;
                    Debug.Log($"R42 缺槽期回执 → 主题=「{subjA}」（期望含「未变动」、不含「已生效」）"
                              + $"　照抄缺口={bodyA.IndexOf("运力缺口", StringComparison.Ordinal) >= 0}"
                              + $"　列在办槽单={inFlightOk}（申请单 {applyAfter} 张）"
                              + $"　{((unchangedOk) ? "✓" : "✗")}\n　正文\n{bodyA}");

                    // 先重现原夹具：一架早班飞机并不保证能接晚班；交割也只会呈报，不代 CEO 批准。
                    SettleSlots(gsR42);
                    var repR42c = CapacityDirectiveLogic.Execute(gsR42, rR42, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
                    Debug.Log("R42_TRACE post-settlement " + repR42c.shortageSection + " crew=" + repR42c.crewSection); InboxManager.QueueCapacityReport(gsR42, rR42, repR42c);
                    var repMailC = gsR42.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(x));
                    InboxManager.ApproveCapacityDirective(gsR42, repMailC, out string _);
                    var constrainedReceipt = LastPendingMail(gsR42, "fx_cap_");
                    Require(rR42.FlightCount == wavesR42 && constrainedReceipt != null
                            && constrainedReceipt.subject.Contains("未变动"), "resource-limited receipt remains honest");
                    Require(gsR42.inbox.Exists(x => x != null && x.action == InboxManager.AssignFlightAction
                        && InboxManager.IsOpen(x)), "slot settlement creates an assignment awaiting CEO approval");

                    // 正例显式补足飞机；仍经过真实申请、季度交割和 CEO 批准，不预塞全时刻槽或绕过闸门。
                    gsR42.fleet.Add(new Aircraft { registration = "B-3001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 });
                    gsR42.fleet.Add(new Aircraft { registration = "B-3002", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 });
                    InboxMail effectiveReceipt = null;
                    for (int round = 0; round < 6 && rR42.FlightCount < 3; round++)
                    {
                        var positive = CapacityDirectiveLogic.Execute(gsR42, rR42,
                            CapacityDirectiveLogic.Parse("增加到每日3段往返"));
                        Require(positive.ok, "positive directive must produce a report");
                        InboxManager.QueueCapacityReport(gsR42, rR42, positive);
                        var positiveMail = gsR42.inbox.Find(x => x != null
                            && x.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(x));
                        Require(InboxManager.ApproveCapacityDirective(gsR42, positiveMail, out string note), note);
                        if (rR42.FlightCount != positive.beforeWaves)
                            effectiveReceipt = LastPendingMail(gsR42, "fx_cap_");
                        Debug.Log("R42_PROGRESS round=" + round + " flights=" + rR42.FlightCount
                            + " shortages=" + positive.shortageSection);
                        if (rR42.FlightCount < 3)
                        {
                            SettleSlots(gsR42);
                            ApproveAssignments(gsR42);
                        }
                    }
                    var receiptC = effectiveReceipt;
                    string subjC = receiptC?.subject ?? "";
                    bool effectiveOk = receiptC != null && rR42.FlightCount == 3
                                       && subjC.IndexOf("未变动", StringComparison.Ordinal) < 0
                                       && (subjC.IndexOf("已生效", StringComparison.Ordinal) >= 0
                                           || subjC.IndexOf("部分生效", StringComparison.Ordinal) >= 0);
                    Debug.Log($"R42 槽交割后回执 → 实排 {wavesR42} → {rR42.FlightCount} 班　主题=「{subjC}」"
                              + $"（期望含「已生效」或「部分生效」、不含「未变动」）　{((effectiveOk) ? "✓" : "✗")}");

                    // ⑥ 纯函数裁定分档：维持现状 / 一班没动 / 目标已达 三档不得混淆
                    var rVd = new RouteData
                    {
                        routeId = "R-VD", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                        aircraftType = "A320neo", depTime = "06:30", arrTime = "07:46",
                        daysOfWeek = "每日", weeklyFlights = 7
                    };
                    rVd.SyncFlightsFromPrimary();
                    bool verdictOk = CapacityDirectiveLogic.VerdictOf(rVd, -1, 1) == CapacityDirectiveLogic.CapVerdict.Keep
                                     && CapacityDirectiveLogic.VerdictOf(rVd, 3, 1) == CapacityDirectiveLogic.CapVerdict.Unchanged
                                     && CapacityDirectiveLogic.VerdictOf(rVd, 1, 1) == CapacityDirectiveLogic.CapVerdict.Unchanged
                                     && CapacityDirectiveLogic.VerdictOf(rVd, 0, 1) == CapacityDirectiveLogic.CapVerdict.Unknown
                                     && CapacityDirectiveLogic.VerdictOf(null, 3, 1) == CapacityDirectiveLogic.CapVerdict.Partial;
                    Debug.Log($"R42 裁定分档 → 维持现状={CapacityDirectiveLogic.VerdictWord(CapacityDirectiveLogic.VerdictOf(rVd, -1, 1))}"
                              + $"　目标3现状1={CapacityDirectiveLogic.VerdictWord(CapacityDirectiveLogic.VerdictOf(rVd, 3, 1))}（期望 未变动）"
                              + $"　目标1现状1={CapacityDirectiveLogic.VerdictWord(CapacityDirectiveLogic.VerdictOf(rVd, 1, 1))}（期望也是 未变动：一班没动就不说生效）"
                              + $"　旧档={CapacityDirectiveLogic.VerdictWord(CapacityDirectiveLogic.VerdictOf(rVd, 0, 1))}（期望 已确认）　{((verdictOk) ? "✓" : "✗")}");
if (!(factsStamped && applyAfter == applyBefore && superseded && unchangedOk && effectiveOk && verdictOk))
throw new Exception("R42_FAIL facts=" + factsStamped + " duplicate=" + (applyAfter == applyBefore) + " superseded=" + superseded + " unchanged=" + unchangedOk + " effective=" + effectiveOk + " verdict=" + verdictOk);
Debug.Log("R42_PASS: all six checks passed");

                }
                finally
                {
                    GameObject.DestroyImmediate(goR42);
                }

                
}
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new Exception("R42 assertion: " + reason);
        }

        static void SettleSlots(GameState gs)
        {
            var mails = gs.inbox.FindAll(x => x != null && InboxManager.IsOpen(x)
                && (x.action == InboxManager.ApplySlotAction || x.action == InboxManager.ChangeSlotAction));
            foreach (var mail in mails)
            {
                Require(InboxManager.Approve(gs, mail, out string note), note);
                Debug.Log("R42_STAGE approved " + mail.origin + " " + mail.slotClock + " " + note);
            }
            gs.quarter++;
            if (gs.quarter > 4) { gs.quarter = 1; gs.year++; }
            gs.SetSimUtc(gs.SimUtc.AddDays(GameState.DaysPerQuarter));
            InboxManager.ProcessPendingEffects(gs);
            InboxManager.DeliverDue(gs, gs.SimUtc.AddDays(1));
            Debug.Log("R42_STAGE settled " + JsonUtility.ToJson(gs.stg));
        }

        static void ApproveAssignments(GameState gs)
        {
            var mails = gs.inbox.FindAll(x => x != null && x.action == InboxManager.AssignFlightAction
                && InboxManager.IsOpen(x));
            foreach (var mail in mails)
            {
                Require(InboxManager.ApproveAssignFlight(gs, mail, out string note), note);
                Debug.Log("R42_STAGE assignment " + note);
            }
        }
        static InboxMail LastPendingMail(GameState gs, string idPrefix)
        {
            if (gs?.pendingInbox == null) return null;
            for (int i = gs.pendingInbox.Count - 1; i >= 0; i--)
            {
                var m = gs.pendingInbox[i] != null ? gs.pendingInbox[i].mail : null;
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (m.id.StartsWith(idPrefix, StringComparison.Ordinal)) return m;
            }
            return null;
        }


} }
#endif
