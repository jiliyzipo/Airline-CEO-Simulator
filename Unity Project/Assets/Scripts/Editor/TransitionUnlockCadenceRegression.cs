#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 过渡经营「市场开通通告 → 询价请示」的送达次序回归（2026-11-03 用户裁定）。
    ///
    /// 裁定原文：「应该先收到开通包机、湿租、培训的业务邮件，再收到询价的邮件。
    /// 而且询价邮件需要隔几个小时再收到。」
    ///
    /// 旧版两处毛病：
    /// <list type="number">
    /// <item>通告与询价都用 `ProgressReady`（同一个 +6 小时）投递，同一分钟并列躺进收件箱；</item>
    /// <item>通告在报价已存在时被写成**待批件**并带上单号，`FlushInquiry` 紧接着又把同一批单号
    ///   另发一封 → 两封待批件抢同一批报价，签掉一封，另一封的单号就作废。</item>
    /// </list>
    ///
    /// 现在：通告恒为汇报件、不带单号；询价另函，晚 `InquiryHours` 小时；全公司只有一封询价件。
    /// </summary>
    public static class TransitionUnlockCadenceRegression
    {
        public static bool Run()
        {
            bool ok = true;

            // ———— ① 节奏函数本身：询价必须严格晚于进度汇报 ————
            var t0 = new DateTime(2026, 11, 4, 4, 0, 0, DateTimeKind.Utc);
            long gap = (long)(InboxCadence.InquiryReady(t0) - InboxCadence.ProgressReady(t0)).TotalHours;
            if (gap != InboxCadence.InquiryHours || gap <= 0)
            {
                Debug.LogError($"R49 ① 询价与通告只隔 {gap} 小时（应为 {InboxCadence.InquiryHours}）——"
                               + "同一分钟落进收件箱，看不出「先开通、再询价」。");
                ok = false;
            }

            var go = new GameObject("__transition_cadence_test_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.EnsureDefaults();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.transitionContracts = new List<TransitionContract>();
                gs.transitionOffers = new List<TransitionOffer>();
                gs.transitionIntroSent = true;      // 跳过开局引导信，只看解锁那两封
                gs.transitionCrewMailSent = false;
                gs.transitionInstructorMailSent = false;

                gs.staffPool.Add(new PersonnelData
                {
                    id = "r49_crew", fullName = "自测·飞行运行主管", department = "OPS",
                    jobTitle = TransitionBusinessLogic.CrewPost, isOperational = true, onMarket = false
                });
                gs.staffPool.Add(new PersonnelData
                {
                    id = "r49_inst", fullName = "自测·客舱经理", department = "OPS",
                    jobTitle = TransitionBusinessLogic.InstructorPost, isOperational = true, onMarket = false
                });
                gs.transitionOffers.Add(new TransitionOffer
                {
                    id = "to_wet_r49", kind = TransitionKinds.Wet, client = "蓝洋航空",
                    rateWanPerDay = 1.20f, totalDays = TransitionBusinessLogic.LeaseDays
                });

                if (!TransitionBusinessLogic.CrewReady(gs) || !TransitionBusinessLogic.InstructorReady(gs))
                {
                    Debug.LogError("R49 夹具失败：解锁门槛岗位没有到位。");
                    return false;
                }

                TransitionBusinessLogic.OnDayTick(gs);

                long announceAt = -1, inquiryAt = -1;
                InboxMail announce = null, inquiry = null;
                for (int i = 0; i < gs.pendingInbox.Count; i++)
                {
                    var p = gs.pendingInbox[i];
                    if (p?.mail?.subject == null) continue;
                    if (announce == null && p.mail.subject.Contains("市场已开通"))
                    {
                        announce = p.mail;
                        announceAt = p.deliverUtcTicks;
                    }
                    if (inquiry == null && p.mail.subject.Contains("询价"))
                    {
                        inquiry = p.mail;
                        inquiryAt = p.deliverUtcTicks;
                    }
                }

                // ———— ② 通告必须是汇报件、不带单号 ————
                bool announceOk = announce != null
                                  && announce.status == "approved"
                                  && string.IsNullOrEmpty(announce.payloadCsv);
                if (!announceOk)
                {
                    Debug.LogError($"R49 ② 开通通告不是纯汇报件："
                                   + (announce == null
                                       ? "根本没排这封"
                                       : $"status={announce.status} payload={announce.payloadCsv}")
                                   + " —— 待批件会与随后的询价件抢同一批报价。");
                    ok = false;
                }

                // ———— ③ 询价必须晚几个小时到，且带着报价单号 ————
                bool inquiryOk = inquiry != null
                                 && inquiry.action == TransitionBusinessLogic.InquiryAction
                                 && (inquiry.payloadCsv ?? "").Contains("to_wet_r49")
                                 && inquiryAt - announceAt == InboxCadence.InquiryHours * TimeSpan.TicksPerHour;
                if (!inquiryOk)
                {
                    Debug.LogError($"R49 ③ 询价次序不对："
                                   + (inquiry == null
                                       ? "没排询价件"
                                       : $"payload={inquiry.payloadCsv} 间隔={(inquiryAt - announceAt) / (double)TimeSpan.TicksPerHour:F1} 小时"
                                         + $"（应为 {InboxCadence.InquiryHours}）")
                                   + " —— 用户要的是「先开通、隔几小时再收到询价」。");
                    ok = false;
                }

                // ———— ④ 同一批报价只能有一封待批询价件 ————
                int pendingInquiries = 0;
                for (int i = 0; i < gs.pendingInbox.Count; i++)
                    if (gs.pendingInbox[i]?.mail != null
                        && gs.pendingInbox[i].mail.action == TransitionBusinessLogic.InquiryAction) pendingInquiries++;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null
                        && gs.inbox[i].action == TransitionBusinessLogic.InquiryAction) pendingInquiries++;
                if (pendingInquiries != 1)
                {
                    Debug.LogError($"R49 ④ 待批询价件有 {pendingInquiries} 封（应恰好 1 封）——"
                                   + "两封会互相作废对方的报价单号。");
                    ok = false;
                }

                Debug.Log($"R49 过渡经营开通节奏 → 间隔 {(inquiryAt - announceAt) / (double)TimeSpan.TicksPerHour:F0} 小时"
                          + $"（期望 {InboxCadence.InquiryHours}）· 通告汇报件 {announceOk}"
                          + $" · 询价带单号 {inquiryOk} · 待批询价件 {pendingInquiries}（期望 1）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return ok;
        }
    }
}
#endif
