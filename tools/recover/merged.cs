using System;
using System.Collections.Generic;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 鏀朵欢绠辩鐞嗭紙GDB 鐧芥澘 n7銆屽畬鏁撮偖浠舵ā鍨嬨€嶏級锛氶偖浠跺鍒犳敼銆? 鏃ワ紙瀛ｅ害锛夋埅姝㈠垽瀹氥€佸埌鏈熼樆鏂€?
    /// 鏃堕棿鍗曚綅鎸夋父鎴忓搴﹁锛氬緟鍔為偖浠惰嚜鏀朵欢瀛ｅ害璧凤紝瓒呰繃 deadlineQuarters 涓搴︿粛鏈鐞嗗嵆鍒版湡銆?
    /// </summary>
    public static class InboxManager
    {
        /// <summary>寰呭姙閭欢鍏佽鎼佺疆鐨勫搴︽暟锛堥粯璁?2 涓搴?鈮?鍗婂勾锛夈€?/summary>
        public const int DefaultDeadlineQuarters = 2;

        /// <summary>鏈鏁板彉鍖栵紙搴曟爮瑙掓爣鍒锋柊锛夈€?/summary>
        public static event System.Action Changed;

        public static int Turn(int year, int quarter) => year * 4 + (quarter - 1);

        /// <summary>閭欢鏄惁銆屾湭澶勭悊銆嶏紙寰呭姙鐘舵€侊紝鍚殏缂擄級銆?/summary>
        public static bool IsOpen(InboxMail m)
            => m != null && (m.status == "pending" || m.status == "deferred");

        /// <summary>鍓╀綑鍙悂缃搴︽暟锛堝彲涓鸿礋 = 宸插埌鏈燂級銆?/summary>
        public static int RemainingQuarters(InboxMail m, int year, int quarter)
        {
            if (m == null) return 0;
            int due = Turn(m.receivedYear, m.receivedQuarter) + m.deadlineQuarters;
            return due - Turn(year, quarter);
        }

        /// <summary>鏄惁宸插埌鏈燂紙瓒呰繃鎴浠嶅緟澶勭悊锛夈€?/summary>
        public static bool IsDue(InboxMail m, int year, int quarter)
            => IsOpen(m) && RemainingQuarters(m, year, quarter) < 0;

        public static void Add(GameState gs, InboxMail mail)
        {
            if (gs == null || mail == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            StampReceived(gs, mail);
            gs.inbox.Insert(0, mail);
            Changed?.Invoke();
        }

        /// <summary>璁颁笅娓告垙鍏巻鐬棿锛屼緵鍒楄〃鎸夋棩鍒嗙粍銆佹樉绀烘椂鍒嗐€?/summary>
        public static void StampReceived(GameState gs, InboxMail mail, int minuteNudge = 0)
        {
            if (mail == null) return;
            bool wasEmpty = mail.receivedUtcTicks <= 0;
            if (wasEmpty && gs != null)
            {
                var utc = gs.SimUtc;
                int fromY = mail.receivedYear > 0 ? mail.receivedYear : gs.year;
                int fromQ = mail.receivedQuarter > 0 ? mail.receivedQuarter : gs.quarter;
                int qDelta = Turn(gs.year, gs.quarter) - Turn(fromY, fromQ);
                if (qDelta > 0)
                    utc = utc.AddDays(-90 * qDelta);
                if (minuteNudge > 0)
                    utc = utc.AddMinutes(-minuteNudge);
                mail.receivedUtcTicks = utc.Ticks;
            }
            if (mail.receivedUtcTicks <= 0)
                mail.receivedUtcTicks = DateTime.UtcNow.Ticks;
            mail.time = InboxTime.Clock(mail);
            if (gs != null)
            {
                if (mail.receivedYear <= 0) mail.receivedYear = gs.year;
                if (mail.receivedQuarter <= 0) mail.receivedQuarter = gs.quarter;
            }
        }

        /// <summary>鐐瑰紑閭欢璇︽儏鍚庢爣璁板凡璇伙紙閲嶅鐐瑰紑涓嶉噸澶嶈鏁帮級銆?/summary>
        public static void MarkRead(GameState gs, string id)
        {
            if (gs == null || gs.inbox == null || string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id || m.read) continue;
                m.read = true;
                Changed?.Invoke();
                return;
            }
        }

        /// <summary>鏍囪閭欢鐘舵€侊紙鎵瑰噯/鏆傜紦/鎷掔粷锛夛紝骞跺啓鍐崇瓥鏃ュ織銆?/summary>
        public static void Mark(GameState gs, string id, string status)
        {
            if (gs == null || gs.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                if (gs.inbox[i].id == id)
                {
                    gs.inbox[i].status = status;
                    gs.AddDecisionLog($"鏀朵欢绠眥StatusLabel(status)}锛歿gs.inbox[i].subject}");
                    Changed?.Invoke();
                    return;
                }
            }
        }

        public static void Normalize(InboxMail m)
        {
            if (m == null) return;
            if (m.id == "m_route" && string.IsNullOrEmpty(m.action))
            {
                m.kind = "report";
                m.action = "open_route";
                m.origin = "PEK";
                m.dest = "SHA";
                if (!string.IsNullOrEmpty(m.body) && m.body.IndexOf("鍏抽敭璇?, StringComparison.Ordinal) < 0)
                    m.body = "甯傚満閮ㄥ缓璁珛鍗冲紑閫?PEK鈥揝HA銆俓n\n寤鸿锛氬寳浜閮解€斾笂娴疯櫣妗ャ€傚惎鍔ㄨ祫閲?300 涓囥€傝鐐瑰叧閿瘝鏌ラ槄鑸嚎缃戠粶 鎴?RPM锛屽啀鎵瑰噯銆傛壒鍑嗗悗涓嬪搴︽姇鍏ヨ繍钀ャ€?;
            }
            else if (m.id == "m_tutorial_szx_pek" && string.IsNullOrEmpty(m.action))
            {
                m.kind = "report";
                m.action = "open_route";
                m.origin = "SZX";
                m.dest = "PEK";
            }
            else if (m.id == "m_board" && string.IsNullOrEmpty(m.kind))
                m.kind = "report";
        }

        public static string KindLabel(string kind) => kind switch
        {
            "regulatory" => "鐩戠",
            "media" => "濯掍綋",
            "alert" => "璀︽姤",
            _ => "姹囨姤"
        };

        /// <summary>鎵瑰噯寮€鑸嚎锛氭湰瀛ｅ彧钀藉喅绛栵紝涓嬪搴︽墠鐪熸寮€閫氾紙寤惰繜鍚庢灉锛夈€?/summary>
        public static bool Approve(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "閭欢鏃犳晥銆?;
                return false;
            }

            Normalize(mail);
            if (mail.action == "open_route")
                return AcceptRoutePermit(gs, mail, out message);

            if (mail.action == "permit_objection")
                return ApprovePermitObjection(gs, mail, out message);

            if (mail.action == "permit_retry")
                return ApprovePermitRetry(gs, mail, out message);

            if (mail.action == "codeshare" || mail.action == "alliance_join"
                || mail.action == "buy_slot" || mail.action == "apply_right"
                || mail.action == "investment")
            {
                NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
                mail.effectPending = true;
                mail.effectYear = ey;
                mail.effectQuarter = eq;
                Mark(gs, mail.id, "approved");
                if (mail.action == "codeshare")
                    message = $"宸叉壒鍑嗕笌{(string.IsNullOrEmpty(mail.dest) ? "鍚堜綔鑸徃" : mail.dest)}鐨勪唬鐮佸叡浜€傚崗璁皢浜庣{ey}骞?Q{eq} 鐢熸晥銆?;
                else if (mail.action == "alliance_join")
                    message = $"宸叉壒鍑嗗姞鍏ユ槦绌鸿仈鐩燂紙瑙傚療鍛橈級銆傚叆鐩熸墜缁皢浜庣{ey}骞?Q{eq} 鍔炵粨銆?;
                else if (mail.action == "buy_slot")
                {
                    string apt = string.IsNullOrEmpty(mail.origin) ? (gs.hubIata ?? "HUB") : mail.origin;
                    int band = StrategyManager.ParseSlotBand(mail.dest);
                    message = $"宸叉壒鍑嗚喘涔?{apt} {StrategyManager.SlotBandName(band)}鏃舵鏃跺埢妲姐€傚皢浜庣{ey}骞?Q{eq} 鎵ｆ浜ゅ壊銆?;
                }
                else if (mail.action == "investment")
                    message = $"宸叉壒鍑嗘姇璧勯」鐩€傝祫鏈皢浜庣{ey}骞?Q{eq} 鎶曞叆骞惰繘鍏ユ墽琛屻€?;
                else
                    message = $"宸叉壒鍑嗙敵璇?{mail.origin}鈥攞mail.dest} 鑸潈銆傚皢浜庣{ey}骞?Q{eq} 娑堣€楁斂搴滃叧绯诲苟鑾锋壒銆?;
                return true;
            }

            if (mail.action == "finance_crisis")
            {
                message = "璇锋墦寮€鏉℃纭鎴栭噸缁勯〉绛剧讲鍚庡啀鐢熸晥銆?;
                return false;
            }

            if (mail.action == "parts_order")
                return ApprovePartsOrder(gs, mail, out message);

            if (mail.action == "comp_review")
                return ApproveCompensation(gs, mail, out message);

            if (mail.action == "annual_goals")
            {
                if (BoardManager.Instance == null || !BoardManager.Instance.ConfirmAnnualGoals())
                {
                    message = "灏氭棤骞村害鐩爣鍙‘璁ゃ€?;
                    return false;
                }
                Mark(gs, mail.id, "approved");
                message = $"宸茬‘璁ょ {gs.year} 璐㈠勾骞村害鐩爣銆傚彲鍦ㄦ効鏅〉鎸佺画璺熻釜杩涘害銆?;
                return true;
            }

            if (mail.action == "capacity_plan")
            {
                Mark(gs, mail.id, "approved");
                gs.AddDecisionLog("杩愬姏瑙勫垝锛氭壒鍑嗚ˉ寮猴紝杩涘叆璐満鍐崇瓥浼氳銆?);
                message = "宸叉壒鍑嗚繍鍔涜ˉ寮恒€傝鍦ㄨ喘鏈哄喅绛栦細璁€夊畾寮曡繘鏂规銆?;
                return true;
            }

            if (mail.action == "ops_schedule")
                return ApproveOpsSchedule(gs, mail, out message);

            if (mail.action == "reg_inspect")
            {
                message = "璇烽€夋嫨鍏ㄥ姏閰嶅悎 / 閲嶇偣鏁存敼 / 鐢宠寤舵湡銆?;
                return false;
            }

            Mark(gs, mail.id, "approved");
            message = "宸叉壒鍑嗐€?;
            return true;
        }

        public static void ProcessPendingEffects(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox != null)
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || !m.effectPending) continue;
                    if (Turn(gs.year, gs.quarter) < Turn(m.effectYear, m.effectQuarter)) continue;
                    m.effectPending = false;
                    if (m.action == "open_route")
                    {
                        // Phase 18锛氬凡鍙楃悊鐨勫噯鍏ヨ鍙寜瀛ｆ帹杩涳紙瀹℃煡鈫掑叕绀衡啋鍙戣瘉/鍚﹀喅锛?
                        if (m.permitStage >= RoutePermitLogic.StageReview)
                        {
                            AdvanceRoutePermit(gs, m);
                            continue;
                        }

                        if (ActionExecutor.Instance == null)
                        {
                            var go = new GameObject("ActionExecutor");
                            go.AddComponent<ActionExecutor>();
                        }

                        var advice = new AdvisorResponse
                        {
                            suggestedAction = "open_route",
                            message = m.subject,
                            data = new AdvisorDataPayload
                            {
                                origin = m.origin,
                                destination = m.dest,
                                stopover = m.stopover
                            }
                        };
                        var result = ActionExecutor.Instance.Execute(SuggestedAction.OpenRoute, advice);
                        Add(gs, new InboxMail
                        {
                            id = "fx_" + m.id + "_" + gs.year + gs.quarter,
                            sender = "杩愯惀閮?,
                            role = "鎵ц鍥炴墽",
                            glyph = "杩?,
                            kind = "report",
                            subject = result.success
                                ? $"鑸嚎 {m.origin}-{m.dest} 宸叉姇鍏ヨ繍钀?
                                : $"鑸嚎 {m.origin}-{m.dest} 鏈兘寮€閫?,
                            body = result.message ?? "",
                            status = "approved",
                            tag = result.success ? "鍥炴墽" : "鍙楅樆",
                            receivedYear = gs.year,
                            receivedQuarter = gs.quarter,
                            time = gs.DateLabel,
                            deadlineQuarters = 1
                        });
                        continue;
                    }

                    if (m.action == "codeshare" || m.action == "alliance_join")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok = m.action == "codeshare"
                            ? StrategyManager.Instance.SignCodeshare(m.dest, out execMsg)
                            : StrategyManager.Instance.JoinAsObserver(out execMsg);
                        AddExecReceipt(gs, m, ok, ok
                                ? (m.action == "codeshare" ? "浠ｇ爜鍏变韩宸茬敓鏁? : "鍏ョ洘鎵嬬画宸插姙缁?)
                                : (m.action == "codeshare" ? "浠ｇ爜鍏变韩鏈兘绛剧讲" : "鍏ョ洘鏈兘瀹屾垚"),
                            execMsg);
                        continue;
                    }

                    if (m.action == "buy_slot" || m.action == "apply_right")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok;
                        if (m.action == "buy_slot")
                        {
                            string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "PEK") : m.origin;
                            ok = StrategyManager.Instance.AcquireSlot(apt, StrategyManager.ParseSlotBand(m.dest), out execMsg);
                        }
                        else
                        {
                            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
                            string d = string.IsNullOrEmpty(m.dest) ? "SIN" : m.dest;
                            ok = StrategyManager.Instance.ApplyRouteRight(o, d, 5, out execMsg);
                        }
                        AddExecReceipt(gs, m, ok,
                            ok ? (m.action == "buy_slot" ? "鏃跺埢妲藉凡浜ゅ壊" : "鑸潈宸茶幏鎵?)
                               : (m.action == "buy_slot" ? "鏃跺埢妲芥湭鑳借喘鍏? : "鑸潈鐢宠鏈幏鎵?),
                            execMsg);
                        continue;
                    }

                    if (m.action == "investment")
                    {
                        EnsureStrategyManager();
                        bool ok = StrategyManager.Instance.ExecuteInvestment(m.origin, out string execMsg);
                        AddExecReceipt(gs, m, ok,
                            ok ? "鎶曡祫璧勬湰宸叉姇鍏ワ紝椤圭洰杩涘叆鎵ц" : "鎶曡祫鏈兘鎶曞叆",
                            execMsg);
                        continue;
                    }

                    if (m.action == "parts_order")
                    {
                        EnsureMaintenanceManager();
                        bool ok = MaintenanceManager.Instance.ConfirmOrder(m.origin, out string execMsg);
                        AddExecReceipt(gs, m, ok,
                            ok ? "鑸潗宸查噰璐叆搴? : "鑸潗閲囪喘鏈兘鍏ュ簱",
                            execMsg);
                    }
                }
            }

            ExpireOpenFinanceCrisis(gs);
            MaybeOfferAlliance(gs);
            MaybeOfferIrrops(gs);
            MaybeOfferRightsAndSlots(gs);
            MaybeOfferUnion(gs);
            MaybeOfferFinanceCrisis(gs);
            MaybeOfferOpsSchedule(gs);
            MaybeOfferInvestment(gs);
            MaybeOfferPartsOrder(gs);
            MaybeOfferInspection(gs);
            MaybeOfferCompensation(gs);
            MaybeOfferAnnualGoals(gs);
            MaybeOfferCapacity(gs);
            SyncFeedIntoInbox(gs);
        }

        /// <summary>娑堟伅涓績/蹇骞跺叆鏀朵欢绠憋紝閬垮厤鐙珛娑堟伅鏍忋€?/summary>
        public static void SyncFeedIntoInbox(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var items = MessageCenter.All;
            if (items == null || items.Count == 0) return;
            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                var msg = items[i];
                if (msg == null) continue;
                string id = "msg_" + msg.id;
                if (HasMailId(gs, id)) continue;
                int p = msg.priority;
                string kind = p == (int)MessagePriority.Emergency ? "alert"
                    : p == (int)MessagePriority.Warning ? "media" : "report";
                string role = p == (int)MessagePriority.Emergency ? "绱ф€ュ揩璁?
                    : p == (int)MessagePriority.Warning ? "棰勮蹇"
                    : p == (int)MessagePriority.Success ? "杩涘睍蹇" : "鍔ㄦ€佸揩璁?;
                gs.inbox.Insert(0, new InboxMail
                {
                    id = id,
                    sender = string.IsNullOrEmpty(msg.deptName) ? "蹇" : msg.deptName,
                    role = role,
                    glyph = "璁?,
                    kind = kind,
                    action = "news",
                    origin = msg.deptId ?? "",
                    subject = msg.title ?? "",
                    body = string.IsNullOrEmpty(msg.body) ? (msg.title ?? "") : msg.body,
                    status = "approved",
                    read = msg.read,
                    tag = p == (int)MessagePriority.Emergency ? "绱ф€? : "蹇",
                    receivedYear = msg.year > 0 ? msg.year : gs.year,
                    receivedQuarter = msg.quarter > 0 ? msg.quarter : gs.quarter,
                    deadlineQuarters = 1
                });
                StampReceived(gs, gs.inbox[0]);
                any = true;
            }
            if (any) Changed?.Invoke();
        }

        #region 鑸嚎鍑嗗叆璁稿彲锛圥hase 18锛?

        /// <summary>
        /// CEO 鎵瑰噯寮€鑸鍒掞細鍙惤"鎵瑰噯"锛岄殢鍗宠浆鍏ュ眬鏂瑰噯鍏ュ姙鐞嗭紙璁稿彲 / 鑸潈 / 鏃跺埢 + 鍏ず锛夈€?
        /// 鎵瑰噯 鈮?鍙互椋炩€斺€旇繖涓庣幇瀹炰竴鑷达細鑸嚎缁忚惀璁稿彲鏍稿噯鍚庢墠鎺掔彮寮€鑸€?
        /// </summary>
        public static bool AcceptRoutePermit(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }

            string o = string.IsNullOrEmpty(mail.origin) ? "PEK" : mail.origin;
            string d = string.IsNullOrEmpty(mail.dest) ? "SHA" : mail.dest;
            mail.origin = o;
            mail.dest = d;
            if (gs.HasRoute(o, d))
            {
                message = $"鑸嚎 {o}-{d} 宸插湪杩愯惀锛屾棤闇€閲嶅鐢宠璁稿彲銆?;
                return false;
            }

            bool intl = RoutePermitLogic.IsInternational(o, d);
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "鍥介檯/鍦板尯鑸嚎缁忚惀璁稿彲" : "鍥藉唴鑸嚎缁忚惀璁稿彲";

            mail.permitAttempts = Mathf.Max(1, mail.permitAttempts);
            mail.permitInternational = intl;
            mail.permitChance = RoutePermitLogic.PassChance(gs, o, d, intl, mail.permitAttempts);
            mail.permitStage = RoutePermitLogic.StageReview;
            mail.permitNote = kind + " 路 瀹℃煡涓?;
            mail.permitObjection = false;

            int quarters = RoutePermitLogic.ReviewQuarters(intl, mail.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            mail.permitDueYear = ey;
            mail.permitDueQuarter = eq;
            mail.effectPending = true;
            mail.effectYear = ey;
            mail.effectQuarter = eq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            mail.permitGate = gap == 1 ? "鑸潈" : gap == 2 ? "鏃跺埢" : "";

            Mark(gs, mail.id, "approved");
            mail.status = "approved";

            AddPermitReceipt(gs, mail, "admit", $"鍑嗗叆鍙楃悊锛歿o}-{d} {kind}",
                BuildAdmitBody(gs, o, d, mail, intl, needsSlot, gapNote, quarters, ey, eq), "鍙楃悊");

            message = $"宸叉壒鍑嗗紑閫?{o}-{d}銆傝繍钀ラ儴宸插悜{(intl ? "姘戣埅灞€" : "鍦板尯绠＄悊灞€")}閫掍氦{kind}鐢宠锛?
                    + $"棰勮 绗瑊ey}骞?Q{eq} 鎵瑰鈥斺€旀壒鍑嗕笉绛変簬鍙互椋烇紝椤诲緟璁稿彲鍙戣瘉銆?;
            if (gap != 0)
                message += $" 娉ㄦ剰锛歿gapNote}锛堟湭鍔炲Ε鍒欒鍙皢鎸傝捣锛?;

            gs.AddDecisionLog($"鍑嗗叆锛氬彈鐞?{o}-{d} {kind}锛岄璁?绗瑊ey}骞?Q{eq} 鎵瑰");
            gs.NotifyChanged();
            return true;
        }

        static string BuildAdmitBody(GameState gs, string o, string d, InboxMail mail,
            bool intl, bool needsSlot, string gapNote, int quarters, int ey, int eq)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("鍙楃悊鍙凤細").Append(mail.id).Append(" 路 绗?).Append(mail.permitAttempts).Append("娆￠€掍氦\n");
            sb.Append($"鑸嚎锛歿o} 鈫?{d}锛坽(intl ? "鍥介檯/鍦板尯" : "鍥藉唴")}鑸嚎锛塡n");
            sb.Append($"璁稿彲绫诲瀷锛歿mail.permitNote}\n");
            sb.Append($"鍔炵悊鏃堕檺锛歿quarters} 涓搴?路 棰勮 绗瑊ey}骞?Q{eq} 鎵瑰\n\n");

            sb.Append("鈥斺€?鍔炵悊鐜妭 鈥斺€擻n");
            sb.Append("1) 鑸嚎缁忚惀璁稿彲锛氫富绠℃満鏋勫鏌ヨ繍鍔涘畨鎺掋€佸畨鍏ㄨ繍琛岃兘鍔涗笌甯傚満绔炰簤鐘跺喌\n");
            if (intl)
                sb.Append("2) 鑸潈棰濆害锛氬崰涓ゅ浗鑸┖杩愯緭鍗忓畾鍐呯殑杩愬姏棰濆害涓庢寚瀹氭壙杩愪汉鍚嶉\n");
            else
                sb.Append("2) 鑸潈棰濆害锛氬浗鍐呰埅绾夸笉娑夊強鑸潈\n");
            sb.Append("3) 鑸彮鏃跺埢锛?)
              .Append(needsSlot ? "鍗忚皟鏈哄満闇€鎸夊崗璋冨鍙栧緱鏃跺埢妲斤紙鏈埅绾块渶鍗犵敤 1 涓級" : "鎸夌幇琛屾椂鍒绘墽琛岋紝鏃犻渶鏂板鏃跺埢妲?)
              .Append("\n");
            sb.Append("4) 鍏ず鏈燂細灞€鏂瑰叕绀猴紝绔炲鍙彁鍑哄紓璁紙鍙兘椤哄欢瀹℃壒锛塡n\n");

            sb.Append("鈥斺€?閫氳繃鐜囧弬鑰?鈥斺€擻n");
            sb.Append(RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, mail.permitAttempts)).Append('\n');
            sb.Append($"褰撳墠浼扮畻锛歿mail.permitChance * 100f:F0}%\n\n");

            sb.Append(gapNote);
            if (string.IsNullOrEmpty(gapNote))
                sb.Append("鍓嶇疆榻愬锛氳埅鏉冧笌鏃跺埢鏃犵己鍙ｏ紝鎸夋湡瀹℃煡銆?);
            sb.Append("\n\n璇风煡鎮夛細璁稿彲鏈彂璇佸墠涓嶅緱鍞エ寮€鑸€傚彲鍦ㄣ€岃埅鏉冧笌鏃跺埢妲姐€嶉〉璺熻釜鍔炵悊杩涘害銆?);
            return sb.ToString();
        }

        /// <summary>鎸夊鎺ㄨ繘鍑嗗叆璁稿彲锛氬墠缃牎楠?鈫?鍏ず寮傝 鈫?鎵瑰锛堝彂璇佸垯鎵ц寮€閫氾級銆?/summary>
        static void AdvanceRoutePermit(GameState gs, InboxMail m)
        {
            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
            string d = string.IsNullOrEmpty(m.dest) ? "SHA" : m.dest;
            bool intl = m.permitInternational || RoutePermitLogic.IsInternational(o, d);
            m.permitInternational = intl;
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "鍥介檯/鍦板尯鑸嚎缁忚惀璁稿彲" : "鍥藉唴鑸嚎缁忚惀璁稿彲";

            if (gs.HasRoute(o, d))
            {
                m.permitStage = RoutePermitLogic.StageGranted;
                m.effectPending = false;
                m.permitNote = "鑸嚎宸插湪杩愯惀";
                return;
            }

            // 鈥斺€?鍓嶇疆锛氳埅鏉冮搴?/ 鏃跺埢妲?鈥斺€?
            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            if (gap != 0)
            {
                if (gap == 1)
                {
                    QueueRightDecision(gs, o, d, null, out _);
                    m.permitGate = "鑸潈";
                }
                else
                {
                    string apt = string.IsNullOrEmpty(gs.hubIata) ? o : gs.hubIata;
                    QueueSlotDecision(gs, apt, 1, null, out _);
                    m.permitGate = "鏃跺埢";
                }

                DeferPermit(m, 2);
                m.permitNote = kind + " 路 寰? + m.permitGate;
                AddPermitReceipt(gs, m, "gate", $"鍑嗗叆鍙楅樆锛歿o}-{d} 寰厈m.permitGate}灏辩华",
                    $"鍑嗗叆瀹℃煡鏆傚仠锛歿gapNote}\n\n宸茶鎴樼暐鍙戝睍閮ㄥ湪鏀朵欢绠辨彁鍑簕(gap == 1 ? "鑸潈鐢宠" : "鏃跺埢妲借喘涔?)}鏂规銆俓n"
                    + $"鍔炲Ε鍚庤鍙户缁鏌ワ紝褰撳墠棰勮 绗瑊m.permitDueYear}骞?Q{m.permitDueQuarter} 澶嶆牳銆?, "鍙楅樆");
                return;
            }
            m.permitGate = "";

            // 鈥斺€?鍏ず鏈熷紓璁紙鍚屼竴璁稿彲浠ヤ竴娆′负闄愶級鈥斺€?
            float objectionChance = intl ? 0.35f : 0.18f;
            if (m.permitStage == RoutePermitLogic.StageReview && !m.permitObjection
                && UnityEngine.Random.value < objectionChance)
            {
                m.permitObjection = true;
                m.permitStage = RoutePermitLogic.StageNotice;
                DeferPermit(m, 1);
                m.permitNote = kind + " 路 鍏ず鏈燂紙鏈夊紓璁級";

                Add(gs, new InboxMail
                {
                    id = "pmobj_" + m.id + "_" + m.permitAttempts,
                    sender = "杩愯惀閮?,
                    role = "鍑嗗叆鍏ず",
                    glyph = "杩?,
                    kind = "regulatory",
                    action = "permit_objection",
                    origin = o,
                    dest = d,
                    stopover = m.stopover ?? "",
                    subject = $"鍏ず寮傝锛歿o}-{d} 鑸嚎缁忚惀璁稿彲琚彁鍑哄紓璁?,
                    body =
                        $"鍙楃悊鍙?{m.id}\n\n" +
                        $"灞€鏂瑰湪鍏ず鏈熷唴鏀跺埌寮傝锛堟秹鍙婃椂鍒绘椂娈典笌杩愬姏鎶曟斁锛夛紝瑕佹眰琛ュ厖鏉愭枡锛屽鎵归『寤朵竴瀛ｃ€俓n" +
                        $"褰撳墠棰勮鎵瑰锛氱{m.permitDueYear}骞?Q{m.permitDueQuarter}銆俓n\n" +
                        "鍙€夊簲瀵癸細\n" +
                        "  鎵瑰噯锛堝叕鍏筹級锛氬姩鐢ㄦ斂搴滃叧绯荤偣鏁?10 鐐癸紝鎺ㄥ姩寮傝鎾ゅ洖锛岃鍙洖鍒板師鑺傚\n" +
                        "  鏆傜紦锛堟帴鍙楀欢鏈燂級锛氭寜椤哄欢鍚庣殑瀛ｅ害缁х画绛夊緟鎵瑰\n" +
                        "  鎷掔粷锛堟斁寮冿級锛氱粓姝㈡湰娆¤鍙姙鐞哱n\n" +
                        "娉細寮傝璁板綍浼氬奖鍝嶅悗缁叕绀轰笌鑸潈璋堝垽锛屽繀瑕佹椂鍏堟敼鍠勫眬鏂瑰叧绯汇€?,
                    tag = "鍏ず",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
                AddPermitReceipt(gs, m, "objection", $"鍑嗗叆鍏ず鍙楅樆锛歿o}-{d} 鏀跺埌寮傝",
                    $"鍏ず鏈熷唴鏀跺埌绔炲寮傝锛屽鎵归『寤朵竴瀛ｏ紝璇︾粏搴斿瑙併€屽叕绀哄紓璁€嶉偖浠躲€?, "鍙楅樆");
                return;
            }

            // 鈥斺€?鎵瑰 鈥斺€?
            m.effectPending = false;
            float chance = m.permitChance > 0f
                ? m.permitChance
                : RoutePermitLogic.PassChance(gs, o, d, intl, Mathf.Max(1, m.permitAttempts));

            if (UnityEngine.Random.value >= chance)
            {
                m.permitStage = RoutePermitLogic.StageDenied;
                m.permitNote = kind + " 路 鏈幏鎵?;
                Mark(gs, m.id, "rejected");
                Add(gs, new InboxMail
                {
                    id = "pmretry_" + m.id + "_" + m.permitAttempts,
                    sender = "杩愯惀閮?,
                    role = "鍑嗗叆鍔炵悊",
                    glyph = "杩?,
                    kind = "regulatory",
                    action = "permit_retry",
                    origin = o,
                    dest = d,
                    stopover = m.stopover ?? "",
                    subject = $"涓嶄簣璁稿彲锛歿o}-{d} {kind}鏈幏鎵瑰噯",
                    body =
                        $"鍙楃悊鍙?{m.id} 路 绗瑊m.permitAttempts}娆￠€掍氦\n\n" +
                        $"灞€鏂瑰 {o}-{d} 鐨剓kind}浣滃嚭涓嶄簣璁稿彲鍐冲畾銆俓n\n" +
                        "鈥斺€?鏈幏鎵硅瘎浼?鈥斺€擻n" +
                        RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, m.permitAttempts) + "\n" +
                        $"鏈浼扮畻閫氳繃鐜?{chance * 100f:F0}%锛屾湭鍛戒腑銆俓n\n" +
                        "鍙€夛細\n" +
                        "  鎵瑰噯锛堥噸鏂伴€掍氦锛夛細鍐嶅鏇存參锛?1 瀛ｏ級锛岄€氳繃鐜囦笅璋?12%\n" +
                        "  鎷掔粷锛堟斁寮冿級锛氱粓姝㈣鑸嚎鍑嗗叆锛屽彲鍙︽嫨鑸嚎\n\n" +
                        "寤鸿锛氬厛鎻愬崌瀹夊叏璇勭骇/灞€鏂瑰叧绯讳笌鏀垮簻鍏崇郴锛屾垨鍏堝彇寰楄埅鏉冮搴︿笌鏃跺埢妲斤紝鍐嶉噸鏂伴€掍氦銆?,
                    tag = "鏈幏鎵?,
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
                gs.AddDecisionLog($"鍑嗗叆锛歿o}-{d} {kind}鏈幏鎵癸紙浼扮畻閫氳繃鐜?{chance * 100f:F0}%锛?);
                gs.NotifyChanged();
                return;
            }

            // 鈥斺€?鍙戣瘉 鈫?寮€鑸墽琛?鈥斺€?
            m.permitStage = RoutePermitLogic.StageGranted;
            m.permitNote = kind + " 路 宸插彂璇?;
            Mark(gs, m.id, "approved");
            bool slotTaken = false;
            if (needsSlot) slotTaken = RoutePermitLogic.ConsumeSlot(gs, fromHub ? (gs.hubIata ?? o) : o);

            EnsureActionExecutor();
            var advice = new AdvisorResponse
            {
                suggestedAction = "open_route",
                message = m.subject,
                data = new AdvisorDataPayload
                {
                    origin = o,
                    destination = d,
                    stopover = m.stopover,
                    profit = RoutePermitLogic.EstimateDailyProfitWan(gs, o, d, m.stopover)
                }
            };
            var result = ActionExecutor.Instance.ExecuteOpenRouteFromPermit(advice);

            if (result != null && result.success)
            {
                AddPermitReceipt(gs, m, "grant", $"鑸嚎 {o}-{d} 宸茶幏璁稿彲骞舵姇鍏ヨ繍钀?,
                    $"{kind}宸叉牳鍑唟(slotTaken ? "锛屽苟鍗犵敤 1 涓椂鍒绘Ы" : "")}銆俓n\n{result.message}", "鍙戣瘉");
            }
            else
            {
                // 璁稿彲璇佸凡鍒版墜浣嗗叕鍙告潯浠朵笉瓒筹紙鐜伴噾/杩愬姏锛夆啋 閫€鍥炲鏌ラ槦鍒楋紝鍔炲Ε鍚庡啀寮€鑸?
                m.permitStage = RoutePermitLogic.StageReview;
                m.permitNote = kind + " 路 宸叉牳鍑嗭紙寰呭叕鍙告潯浠跺氨缁級";
                DeferPermit(m, 2);
                AddPermitReceipt(gs, m, "grantblocked", $"鍑嗗叆宸叉牳鍑嗭紝浣?{o}-{d} 鏆傛棤娉曞紑鑸?,
                    $"{kind}宸叉牳鍑嗭紝浣嗗紑鑸墽琛屽彈闃伙細\n\n{(result != null ? result.message : "鏈煡鍘熷洜")}\n\n"
                    + $"寰呯幇閲?杩愬姏灏辩华鍚庤嚜鍔ㄩ噸璇曪紙棰勮 绗瑊m.permitDueYear}骞?Q{m.permitDueQuarter}锛夈€?, "鍙楅樆");
            }
            gs.NotifyChanged();
        }

        /// <summary>鍏ず寮傝锛氭壒鍑?= 鍔ㄧ敤鏀垮簻鍏崇郴鍏叧锛屾妸璁稿彲杩涘害鎷夊洖鍘熻鍒掋€?/summary>
        public static bool ApprovePermitObjection(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) { message = "鎴樼暐鏁版嵁涓嶅彲鐢ㄣ€?; return false; }
            const float cost = 10f;
            if (stg.governmentRelation < cost)
            {
                message = $"鏀垮簻鍏崇郴鐐规暟涓嶈冻锛堝叕鍏抽渶 {cost:F0} 鐐癸紝褰撳墠 {stg.governmentRelation:F0}锛夈€傚彲閫夋嫨銆屾帴鍙楀欢鏈熴€嶆垨鍏堝仛灞€鏂瑰叧绯汇€?;
                return false;
            }
            stg.governmentRelation -= cost;

            var permit = FindPermitMail(gs, mail.origin, mail.dest);
            string dueLabel = "";
            if (permit != null)
            {
                NextQuarter(gs.year, gs.quarter, out int ny, out int nq);
                if (Turn(ny, nq) < Turn(permit.permitDueYear, permit.permitDueQuarter))
                {
                    permit.permitDueYear = ny;
                    permit.permitDueQuarter = nq;
                    permit.effectYear = ny;
                    permit.effectQuarter = nq;
                    permit.effectPending = true;
                }
                dueLabel = $" 璁稿彲棰勮 绗瑊permit.permitDueYear}骞?Q{permit.permitDueQuarter} 澶嶆牳銆?;
            }

            Mark(gs, mail.id, "approved");
            message = $"宸插叕鍏筹紙娑堣€?{cost:F0} 鏀垮簻鍏崇郴鐐癸級锛屽紓璁挙鍥烇紝璁稿彲鍥炲埌鍘熻妭濂忋€倇dueLabel}";
            gs.AddDecisionLog($"鍑嗗叆锛氬叕鍏冲紓璁?{mail.origin}-{mail.dest}锛?{cost:F0} 鏀垮簻鍏崇郴锛?);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>鏈幏鎵瑰悗閲嶆柊閫掍氦锛堟洿蹇笉浜嗭細瀹℃煡 +1 瀛ｃ€侀€氳繃鐜囦笅璋冿級銆?/summary>
        public static bool ApprovePermitRetry(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }

            string o = string.IsNullOrEmpty(mail.origin) ? "PEK" : mail.origin;
            string d = string.IsNullOrEmpty(mail.dest) ? "SHA" : mail.dest;
            var permit = FindPermitMail(gs, o, d);
            if (permit == null)
            {
                message = "鏈壘鍒板搴旂殑璁稿彲鍔炵悊鍗曪紝璇烽噸鏂扮敱杩愯惀閮ㄥ彫闆嗙爺绌躲€?;
                return false;
            }

            bool intl = permit.permitInternational || RoutePermitLogic.IsInternational(o, d);
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "鍥介檯/鍦板尯鑸嚎缁忚惀璁稿彲" : "鍥藉唴鑸嚎缁忚惀璁稿彲";

            permit.permitAttempts = Mathf.Min(9, Mathf.Max(1, permit.permitAttempts) + 1);
            permit.permitInternational = intl;
            permit.permitChance = RoutePermitLogic.PassChance(gs, o, d, intl, permit.permitAttempts);
            permit.permitStage = RoutePermitLogic.StageReview;
            permit.permitObjection = false;
            permit.permitNote = kind + " 路 閲嶇敵瀹℃煡涓?;

            int quarters = RoutePermitLogic.ReviewQuarters(intl, permit.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            permit.permitDueYear = ey;
            permit.permitDueQuarter = eq;
            permit.effectPending = true;
            permit.effectYear = ey;
            permit.effectQuarter = eq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            permit.permitGate = gap == 1 ? "鑸潈" : gap == 2 ? "鏃跺埢" : "";

            Mark(gs, mail.id, "approved");
            AddPermitReceipt(gs, permit, "retry", $"鍑嗗叆閲嶇敵锛歿o}-{d} 绗瑊permit.permitAttempts}娆￠€掍氦",
                $"閲嶆柊閫掍氦{kind}锛堢{permit.permitAttempts}娆★級锛氬鏌?{quarters} 涓搴︼紝浼扮畻閫氳繃鐜?{permit.permitChance * 100f:F0}%銆俓n"
                + $"棰勮 绗瑊ey}骞?Q{eq} 鎵瑰銆俓n\n" + (string.IsNullOrEmpty(gapNote) ? "鍓嶇疆榻愬銆? : gapNote), "鍙楃悊");

            message = $"宸查噸鏂伴€掍氦 {o}-{d} 鍑嗗叆鐢宠锛堢{permit.permitAttempts}娆★級锛岄璁?绗瑊ey}骞?Q{eq} 鎵瑰銆?;
            gs.AddDecisionLog($"鍑嗗叆锛氶噸鐢?{o}-{d}锛堢{permit.permitAttempts}娆★級");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>鏀惧純鍑嗗叆鐢宠锛堝紓璁?鏈幏鎵归偖浠朵笂閫夋嫨銆屾斁寮冦€嶏級銆?/summary>
        public static bool RejectPermitRequest(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            var permit = FindPermitMail(gs, mail.origin, mail.dest);
            if (permit != null)
            {
                permit.permitStage = RoutePermitLogic.StageNone;
                permit.permitGate = "";
                permit.permitNote = "宸叉斁寮?;
                permit.effectPending = false;
            }
            Mark(gs, mail.id, "rejected");
            message = $"宸叉斁寮?{mail.origin}-{mail.dest} 鐨勫噯鍏ョ敵璇凤紝璁稿彲鍔炵悊缁堟锛堟棩鍚庡彲閲嶆柊鍙泦鐮旂┒锛夈€?;
            gs.AddDecisionLog("鍑嗗叆锛氭斁寮?" + mail.origin + "-" + mail.dest + " 鑸嚎缁忚惀璁稿彲鐢宠");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>鎵胯浇鍑嗗叆璁稿彲鐨勩€屽姙鐞嗗崟銆嶏紳瀵瑰簲鑸嚎鐨勫紑鑸彁妗堥偖浠躲€?/summary>
        public static InboxMail FindPermitMail(GameState gs, string origin, string dest)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "open_route") continue;
                if (m.permitStage < RoutePermitLogic.StageReview) continue;
                if (string.Equals(m.origin, origin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.dest, dest, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        /// <summary>鎶婅鍙鏍稿搴﹂『寤?n 瀛ｏ紙骞朵繚鎸佸緟鍔烇級銆?/summary>
        static void DeferPermit(InboxMail m, int quarters)
        {
            int ey = m.permitDueYear > 0 ? m.permitDueYear : m.effectYear;
            int eq = m.permitDueQuarter > 0 ? m.permitDueQuarter : m.effectQuarter;
            for (int i = 0; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            m.permitDueYear = ey;
            m.permitDueQuarter = eq;
            m.effectYear = ey;
            m.effectQuarter = eq;
            m.effectPending = true;
        }

        static void AddPermitReceipt(GameState gs, InboxMail src, string kind, string subject, string body, string tag)
        {
            Add(gs, new InboxMail
            {
                id = "pm_" + kind + "_" + src.id + "_" + gs.year + "q" + gs.quarter,
                sender = "杩愯惀閮?,
                role = "鍑嗗叆鍔炵悊",
                glyph = "杩?,
                kind = "report",
                action = "permit_progress",
                origin = src.origin,
                dest = src.dest,
                stopover = src.stopover ?? "",
                subject = subject,
                body = body ?? "",
                status = "approved",
                tag = tag,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static void EnsureActionExecutor()
        {
            if (ActionExecutor.Instance != null) return;
            var go = new GameObject("ActionExecutor");
            go.AddComponent<ActionExecutor>();
        }

        #endregion

        static void AddExecReceipt(GameState gs, InboxMail src, bool ok, string subject, string body)
        {
            Add(gs, new InboxMail
            {
                id = "fx_" + src.id + "_" + gs.year + gs.quarter,
                sender = "鎴樼暐鍙戝睍閮?,
                role = "鎵ц鍥炴墽",
                glyph = "鎴?,
                kind = "report",
                subject = subject,
                body = body ?? "",
                status = "approved",
                tag = ok ? "鍥炴墽" : "鍙楅樆",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static void EnsureStrategyManager()
        {
            if (StrategyManager.Instance != null) return;
            var go = new GameObject("StrategyManager");
            go.AddComponent<StrategyManager>();
        }

        static void EnsureHrManager()
        {
            if (HrManager.Instance != null) return;
            var go = new GameObject("HrManager");
            go.AddComponent<HrManager>();
        }

        static void EnsureMaintenanceManager()
        {
            if (MaintenanceManager.Instance != null) return;
            var go = new GameObject("MaintenanceManager");
            go.AddComponent<MaintenanceManager>();
        }

        static void EnsureSafetyManager()
        {
            if (SafetyManager.Instance != null) return;
            var go = new GameObject("SafetyManager");
            go.AddComponent<SafetyManager>();
        }

        static bool HasMailId(GameState gs, string id)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return true;
            return false;
        }

        static bool HasOpenAction(GameState gs, string action)
        {
            if (gs?.inbox == null) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != action) continue;
                if (IsOpen(m) || m.effectPending) return true;
            }
            return false;
        }

        /// <summary>鎴樼暐閮ㄦ妸鑱旂洘璋堝垽鍋氭垚寰呭喅绛栭偖浠讹紙瀹氱 n78/n102锛夈€?/summary>
        public static void MaybeOfferAlliance(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.alliance == null) return;

            if (!HasMailId(gs, "m_codeshare_sin") && !HasOpenAction(gs, "codeshare")
                && stg.alliance.codeSharePartners <= 0)
            {
                Add(gs, BuildAllianceMail(gs, "m_codeshare_sin", "codeshare", "SIN", "鏂板姞鍧¤埅绌?,
                    "寤鸿涓庢柊鍔犲潯鑸┖绛剧讲浠ｇ爜鍏变韩",
                    "鎴樼暐鍙戝睍閮ㄥ缓璁笌鏂板姞鍧¤埅绌哄仛浠ｇ爜鍏变韩锛岃ˉ浜氭床缃戠粶鐭澘銆俓n\n" +
                    "寤鸿锛氭壒鍑嗗悗涓嬪搴︾绾︼紝绛剧害璐?25 涓囥€傚彲鍏堢偣鍏抽敭璇嶆煡鐪嬭仈鐩熶笌鎴樼暐鍚堜綔 鎴?鎰挎櫙銆俓n" +
                    "鎷掔粷鍒欑淮鎸佺嫭绔嬭繍钀ワ紝鍚庣画浠嶅彲鍦ㄦ効鏅〉鑷璋堝垽銆?));
                return;
            }

            if (!HasMailId(gs, "m_alliance_join") && !HasOpenAction(gs, "alliance_join")
                && stg.alliance.tier <= 0 && stg.alliance.codeSharePartners > 0)
            {
                Add(gs, BuildAllianceMail(gs, "m_alliance_join", "alliance_join", "", "鏄熺┖鑱旂洘",
                    "寤鸿浠ヨ瀵熷憳韬唤鍔犲叆鏄熺┖鑱旂洘",
                    "鎴樼暐鍙戝睍閮ㄨ涓轰唬鐮佸叡浜凡钀藉湴锛屽缓璁敵璇锋槦绌鸿仈鐩熻瀵熷憳甯綅銆俓n\n" +
                    "寤鸿锛氭壒鍑嗗悗涓嬪搴﹀姙缁擄紝鍏ョ洘璐?50 涓囷紝鏈洪槦闇€ 鈮? 鏋躲€傚彲鐐硅仈鐩熶笌鎴樼暐鍚堜綔 鏌ョ湅绛夌骇涓庢敹鐩娿€俓n" +
                    "姝ｅ紡鎴愬憳浠嶉渶鏈洪槦 鈮?0 鏋讹紝鍦ㄦ効鏅〉鐢宠鍗囩骇銆?));
            }
        }

        public static void MaybeOfferInvestment(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || (gs.year == 1 && gs.quarter < 2)) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.investments == null) return;
            if (stg.investments.Count > 0 || HasOpenAction(gs, "investment")) return;
            var p = StrategyManager.CatalogHub();
            stg.investments.Add(p);
            QueueInvestmentMail(gs, p);
        }

        public static bool QueueInvestmentMail(GameState gs, InvestmentProject p)
        {
            if (gs == null || p == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = "m_inv_" + (string.IsNullOrEmpty(p.id) ? "x" : p.id);
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || m.id != id) continue;
                    if (IsOpen(m) || m.effectPending) return true;
                    m.status = "pending";
                    m.read = false;
                    m.effectPending = false;
                    m.receivedYear = gs.year;
                    m.receivedQuarter = gs.quarter;
                    m.time = gs.DateLabel;
                    m.subject = $"璇锋壒鍑嗘姇璧勩€寋p.name}銆?;
                    Changed?.Invoke();
                    return true;
                }
            }
            float score = StrategyEconomics.InvestmentScore(p.businessValue, p.resourceFit, p.barrier, p.strategicFit);
            float irr = StrategyEconomics.InvestmentIrr(p.npvWan, p.capitalWan < 1f ? 280f : p.capitalWan, 5);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "鎴樼暐鍙戝睍閮?,
                role = "鎶曡祫鎻愭",
                glyph = "鎴?,
                kind = "report",
                action = "investment",
                origin = p.id,
                dest = p.name,
                subject = $"璇锋壒鍑嗘姇璧勩€寋p.name}銆?,
                body = $"鎴樼暐鍙戝睍閮ㄥ缓璁珛椤广€寋p.name}銆嶃€俓n\n" +
                       $"鍥涚淮璇勫垎銆€缁煎悎 {score:F0} 鍒嗭紙鍟嗕笟 {p.businessValue:F0} / 璧勬簮 {p.resourceFit:F0} / 澹佸瀿 {p.barrier:F0} / 濂戝悎 {p.strategicFit:F0}锛塡n" +
                       $"璧勬湰鏀嚭銆€{p.capitalWan:F0} 涓囷紙鎵瑰噯鍚庝笅瀛ｅ害鎵ｉ櫎锛塡n" +
                       $"NPV銆€{p.npvWan:F0} 涓囥€€IRR 绾?{irr:F0}%\n\n" +
                       "寤鸿锛氱偣鎴樼暐鎶曡祫涓庡苟璐?鏍稿缁勫悎鍚庡啀鎵瑰噯銆傛嫆缁濆垯鏈涓嶆姇鍏ヨ祫鏈€?,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static bool RejectInvestment(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureStrategyManager();
            string pid = mail != null ? mail.origin : null;
            bool ok = StrategyManager.Instance != null && StrategyManager.Instance.RejectInvestment(pid, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "宸叉嫆缁濊鎶曡祫鎻愭銆?;
            return true;
        }

        public static bool ApprovePartsOrder(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureMaintenanceManager();
            var mnt = MaintenanceManager.EnsureState(gs);
            var order = mnt?.orders?.Find(o => o != null && o.orderNo == mail.origin);
            if (order == null) { message = "鏈壘鍒板搴旈噰璐鍗曘€?; return false; }
            if (order.urgent)
            {
                bool ok = MaintenanceManager.Instance.ConfirmOrder(order.orderNo, out message);
                Mark(gs, mail.id, "approved");
                mail.effectPending = false;
                return ok;
            }
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            mail.effectPending = true;
            mail.effectYear = ey;
            mail.effectQuarter = eq;
            Mark(gs, mail.id, "approved");
            float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
            message = $"宸叉壒鍑嗚埅鏉愰噰璐?{order.orderNo}銆傚皢浜庣{ey}骞?Q{eq} 鎵ｆ {total:F2} 涓囧苟鍏ュ簱銆?;
            return true;
        }

        public static bool RejectPartsOrder(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureMaintenanceManager();
            bool ok = MaintenanceManager.Instance != null
                && mail != null
                && MaintenanceManager.Instance.CancelOrder(mail.origin, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "宸叉嫆缁濊鑸潗閲囪喘銆?;
            return true;
        }

        public static void OnPartsOrderFilled(GameState gs, string orderNo)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(orderNo)) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "parts_order" || m.origin != orderNo) continue;
                m.effectPending = false;
                if (IsOpen(m)) Mark(gs, m.id, "approved");
            }
        }

        public static bool QueuePartsMail(GameState gs, PurchaseOrder order)
        {
            if (gs == null || order == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = "m_po_" + order.orderNo;
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m != null && m.id == id && (IsOpen(m) || m.effectPending)) return true;
                }
            }
            float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "鏈哄姟宸ョ▼閮?,
                role = "鑸潗閲囪喘",
                glyph = "鏈?,
                kind = "alert",
                action = "parts_order",
                origin = order.orderNo,
                dest = order.partNo,
                subject = order.urgent
                    ? $"绱ф€ワ細鍋滈缂轰欢 {order.partNo} 璇锋壒鍑嗛噰璐?
                    : $"缂轰欢棰勮锛氳鎵瑰噯閲囪喘 {order.partNo}",
                body = $"鏈哄姟宸ョ▼閮ㄦ姤鍛婁欢鍙?{order.partNo} 搴撳瓨浣庝簬瀹夊叏绾匡紝宸叉嫙璁㈠崟 {order.orderNo}銆俓n\n" +
                       $"鏁伴噺 {order.quantity}銆€鍗曚环 {order.unitCostUsd:F0} 缇庡厓銆€鍚堣 {total:F2} 涓嘰n" +
                       $"渚涘簲鍟?{order.supplier}銆€浜ゆ湡 {order.leadDays} 澶┿€€{(order.urgent ? "绱ф€?AOG" : "甯歌琛ヨ揣")}\n\n" +
                       (order.urgent
                           ? "寤鸿锛氱珛鍗虫壒鍑嗭紝鏈鎵ｆ鍏ュ簱锛岄伩鍏嶆寔缁仠椋炪€俓n"
                           : "寤鸿锛氭壒鍑嗗悗涓嬪搴︽墸娆惧叆搴撱€傚彲鍏堢偣鑸潗涓庝緵搴旈摼 鎴?璁㈠崟纭 鏍稿銆俓n") +
                       "鎷掔粷鍒欒鍗曞彇娑堬紝缂轰欢椋庨櫓缁х画銆?,
                tag = order.urgent ? "绱ф€? : "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferPartsOrder(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || (gs.year == 1 && gs.quarter < 2)) return;
            if (HasOpenAction(gs, "parts_order")) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            if (mnt.parts.Count == 0)
            {
                mnt.parts.Add(new PartStock { partNo = "PN-ENG-001", name = "鍙戝姩鏈哄崟鍏冧綋瀵嗗皝浠?, category = "A", quantity = 6, safetyStock = 10, unitCostUsd = 120000f });
                mnt.parts.Add(new PartStock { partNo = "PN-HYD-002", name = "娑插帇浣滃姩绛?, category = "B", quantity = 1, safetyStock = 8, unitCostUsd = 80000f });
            }
            bool shortage = false;
            for (int i = 0; i < mnt.parts.Count; i++)
                if (mnt.parts[i] != null && mnt.parts[i].quantity < mnt.parts[i].safetyStock) shortage = true;
            if (!shortage) return;
            var draft = mnt.orders.Find(o => o != null && o.status == "鑽夌");
            if (draft == null)
            {
                EnsureMaintenanceManager();
                MaintenanceManager.Instance.CreateOrder("PN-HYD-002", "OEM 鍘熷巶", 6, 80000f, false, out _);
                draft = mnt.orders.Find(o => o != null && o.status == "鑽夌");
            }
            if (draft != null) QueuePartsMail(gs, draft);
        }

        public static bool ApplyInspection(GameState gs, InboxMail mail, int choice, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            EnsureSafetyManager();
            string reg = string.IsNullOrEmpty(mail.origin) ? "CAAC" : mail.origin;
            bool ok = SafetyManager.Instance.RespondInspection(reg, choice, out message);
            if (!ok) return false;
            string status = choice == 2 ? "rejected" : "approved";
            Mark(gs, mail.id, status);
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "瀹夊叏璐ㄩ噺閮?,
                role = "鎵ц鍥炴墽",
                glyph = "瀹?,
                kind = "report",
                subject = "灞€鏂规鏌ュ簲瀵瑰凡钀界洏",
                body = message ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static void MaybeOfferInspection(GameState gs)
        {
            MaybeOfferInspection(gs, false);
        }

        public static void MaybeOfferInspection(GameState gs, bool force)
        {
            if (gs == null) return;
            if (HasOpenAction(gs, "reg_inspect")) return;
            if (!force)
            {
                if (gs.year < 1 || (gs.year == 1 && gs.quarter < 3)) return;
                if (HasMailId(gs, "m_caac_inspect")) return;
            }
            var saf = SafetyManager.EnsureState(gs);
            char band = SafetyEconomics.RatingBand(saf.ratingScore);
            string id = HasMailId(gs, "m_caac_inspect")
                ? "m_caac_inspect_" + gs.year + gs.quarter
                : "m_caac_inspect";
            if (HasMailId(gs, id)) return;
            Add(gs, new InboxMail
            {
                id = id,
                sender = "瀹夊叏璐ㄩ噺閮?,
                role = "灞€鏂规鏌?,
                glyph = "瀹?,
                kind = "regulatory",
                action = "reg_inspect",
                origin = "CAAC",
                dest = "缁煎悎",
                subject = "CAAC 缁煎悎妫€鏌ヤ复杩戯紝璇烽€夋嫨搴斿鏂规",
                body = $"灞€鏂瑰皢瀵规湰鍏徃杩涜缁煎悎妫€鏌ャ€傚綋鍓嶅畨鍏ㄨ瘎绾?{band} 绾э紙{saf.ratingScore:F0}锛夈€俓n\n" +
                       "鈸?鍏ㄥ姏閰嶅悎锛氭垚鏈害 15 涓囷紝鍏崇郴鏀瑰杽锛屽彲鑳芥毚闇查棶棰樸€俓n" +
                       "鈸?閲嶇偣鏁存敼鍚庤繋妫€锛氭垚鏈害 35 涓囷紝鍙戠幇椤瑰噺灏戙€俓n" +
                       "鈸?鐢宠寤舵湡锛氭垚鏈害 8 涓囷紝灞€鏂瑰叧绯讳笅闄嶏紝璇勭骇鎵垮帇銆俓n\n" +
                       "璇峰湪鏈偖浠堕€夋嫨锛屾垨鐐规鏌ュ簲瀵?鎵撳紑鏂规瀵圭収銆傚彲鍚屾椂鏌ョ湅灞€鏂逛笌娉曡浜嬪姟銆?,
                tag = "鐩戠",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        public static bool QueueCompensationMail(GameState gs, string kind)
        {
            if (gs == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            kind = HrManager.NormalizeKind(kind);
            var hr = HrManager.EnsureState(gs);
            string id = "m_comp_" + kind + "_" + gs.year;
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m != null && m.id == id && (IsOpen(m) || m.effectPending)) return true;
                }
                return false;
            }
            float cost = HrManager.CompCostWan(hr, kind);
            string title = HrManager.CompTitle(kind);
            string effect = kind == "raise"
                ? "鎵瑰噯鍚庣珛鍗虫墸娆撅紝骞村害钖叕姹犱笂璋冪害 3.5%锛屽憳宸ヤ笌宸ヤ細婊℃剰搴︿笂鍗囥€?
                : kind == "exec"
                    ? "鎵瑰噯鍚庣珛鍗虫墸娆撅紝钖叕姹犲鍔犻珮绠℃縺鍔辨垚鏈紝婊℃剰搴﹀皬骞呬笂鍗囥€?
                    : "鎵瑰噯鍚庣珛鍗充粠鐜伴噾鍒掑叆濂栭噾姹狅紙钖叕姹犳€婚涓嶅彉锛夛紝鍛樺伐婊℃剰搴︿笂鍗囥€?;
            Add(gs, new InboxMail
            {
                id = id,
                sender = "浜哄姏璧勬簮閮?,
                role = "钖叕鏂规",
                glyph = "浜?,
                kind = "report",
                action = "comp_review",
                origin = kind,
                dest = cost.ToString("F0"),
                subject = $"璇峰鎵广€寋title}銆嶆柟妗?,
                body = $"浜哄姏璧勬簮閮ㄦ彁浜title}锛岃 CEO 鎷嶆澘銆俓n\n" +
                       $"鐜伴噾鏀嚭銆€绾?{cost:F0} 涓嘰n" +
                       $"褰撳墠钖叕姹犮€€{hr.laborCostWan:F0} 涓囥€€鍛樺伐婊℃剰搴?{hr.employeeSatisfaction:F0}\n" +
                       $"浜哄姏鎴愭湰鍗犺惀鏀躲€€{HrEconomics.LaborCostRatio(hr.laborCostWan, gs.monthlyRevenue * 12f) * 100:F0}%锛堢洰鏍?<25%锛塡n\n" +
                       effect + "\n鎷掔粷鍒欑淮鎸佸師钖叕鏂规锛屾弧鎰忓害涓庢祦澶遍闄╂壙鍘嬨€俓n鍙偣缁╂晥涓庤柂閰?鏍稿缁撴瀯鍚庡啀鎵广€?,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferCompensation(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || gs.quarter != 4) return;
            if (HasOpenAction(gs, "comp_review")) return;
            string id = "m_comp_raise_" + gs.year;
            if (HasMailId(gs, id)) return;
            QueueCompensationMail(gs, "raise");
        }

        public static bool QueueAnnualGoalsMail(GameState gs)
        {
            if (gs == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (gs.AnnualGoalsConfirmed) return false;
            string id = "m_annual_goals_" + gs.year;
            if (HasMailId(gs, id) || HasOpenAction(gs, "annual_goals")) return false;
            if (BoardManager.Instance != null)
                BoardManager.Instance.EnsureAnnualTargets();
            int n = gs.currentTargets != null ? gs.currentTargets.Count : 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"钁ｄ簨浼氳 CEO 纭绗?{gs.year} 璐㈠勾鑰冩牳娓呭崟锛堝叡 {n} 椤癸級銆?);
            sb.AppendLine();
            if (gs.currentTargets != null)
            {
                for (int i = 0; i < gs.currentTargets.Count && i < 6; i++)
                {
                    var t = gs.currentTargets[i];
                    if (t == null) continue;
                    sb.AppendLine($"路 {t.ProgressLabel}");
                }
            }
            sb.AppendLine();
            sb.Append("鎵瑰噯鍗崇‘璁ゆ湰璐㈠勾鐩爣銆備篃鍙偣骞村害鐩爣 鍦ㄦ効鏅〉纭銆傛嫆缁濆垯鏆傜紦锛岃懀浜嬩細婊℃剰搴︿笅闄嶃€?);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "钁ｄ簨浼?,
                role = "骞村害鐩爣",
                glyph = "钁?,
                kind = "report",
                action = "annual_goals",
                subject = $"璇风‘璁ょ {gs.year} 璐㈠勾骞村害鐩爣",
                body = sb.ToString().TrimEnd(),
                tag = "鑰冩牳",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferAnnualGoals(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1) return;
            if (gs.AnnualGoalsConfirmed) return;
            if (HasOpenAction(gs, "annual_goals")) return;
            QueueAnnualGoalsMail(gs);
        }

        public static bool RejectAnnualGoals(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "閭欢鏃犳晥銆?;
                return false;
            }
            gs.boardSatisfaction = UnityEngine.Mathf.Clamp(gs.boardSatisfaction - 4f, 0f, 100f);
            BoardManager.Instance?.ApplySatisfactionDeltaToMembers(-4f);
            Mark(gs, mail.id, "rejected");
            gs.AddDecisionLog("骞村害鐩爣锛氭殏缂撶‘璁ゃ€?);
            message = "宸叉殏缂撶‘璁ゃ€傚彲绋嶅悗鍦ㄦ効鏅€屽勾搴︾洰鏍囥€嶉〉纭銆傝懀浜嬩細婊℃剰搴?-4銆?;
            gs.NotifyChanged();
            return true;
        }

        public static bool QueueCapacityMail(GameState gs, int gap)
        {
            if (gs == null || gap <= 0) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "capacity_plan")) return true;
            string id = "m_capacity_" + gs.year + "_q" + gs.quarter;
            if (HasMailId(gs, id)) return false;
            Add(gs, new InboxMail
            {
                id = id,
                sender = "杩愯惀閮?,
                role = "杩愬姏瑙勫垝",
                glyph = "杩?,
                kind = "report",
                action = "capacity_plan",
                origin = gap.ToString(),
                subject = $"杩愬姏缂哄彛绾?{gap} 鏋讹紝璇峰喅绛栬ˉ寮?,
                body = $"閫傝埅杩愬姏涓嶈冻浠ヨ鐩栧湪钀ヨ埅缃戯紝缂哄彛绾?{gap} 鏋讹紙缂哄彛瓒呰繃 20% 涓虹孩鐏級銆俓n\n" +
                       "鎵瑰噯鍚庤繘鍏ヨ喘鏈哄喅绛栦細璁紙鏂版満 / 浜屾墜 / 绉熻祦锛夈€傛嫆缁濆垯鏈涓嶅紩杩涳紝鑸綉鍙兘缁х画瓒呰礋鑽枫€俓n" +
                       "鍙偣杩愬姏瑙勫垝 鏌ョ湅浜斿勾鏈洪槦璁″垝鍚庡啀鎵广€?,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferCapacity(GameState gs)
        {
            if (gs == null) return;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            if (!CapacityPlanning.NeedsDecision(routes, airworthy)) return;
            QueueCapacityMail(gs, gap);
        }

        public static bool RejectCapacity(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "閭欢鏃犳晥銆?;
                return false;
            }
            gs.boardSatisfaction = UnityEngine.Mathf.Clamp(gs.boardSatisfaction - 2f, 0f, 100f);
            Mark(gs, mail.id, "rejected");
            gs.AddDecisionLog("杩愬姏瑙勫垝锛氭湰瀛ｄ笉寮曡繘銆?);
            message = "宸叉嫆缁濇湰瀛ｅ紩杩涖€傝繍鍔涚己鍙ｄ粛鍦紝鍙◢鍚庝粠鏈洪槦涓庤繍鍔涜鍒掗〉鍐嶆彁銆?;
            gs.NotifyChanged();
            return true;
        }

        public static bool ApproveCompensation(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureHrManager();
            string kind = mail != null ? mail.origin : "bonus";
            bool ok = HrManager.Instance.ApplyCompensation(kind, out message);
            if (!ok) return false;
            Mark(gs, mail.id, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "浜哄姏璧勬簮閮?,
                role = "鎵ц鍥炴墽",
                glyph = "浜?,
                kind = "report",
                subject = HrManager.CompTitle(kind) + "宸插叆璐?,
                body = message ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static bool RejectCompensation(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureHrManager();
            string kind = mail != null ? mail.origin : "bonus";
            bool ok = HrManager.Instance.RejectCompensation(kind, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "宸叉嫆缁濊钖叕鏂规锛岃柂閰睜缁存寔鍘熺姸銆?;
            return true;
        }

        static InboxMail BuildAllianceMail(GameState gs, string id, string action, string origin, string dest,
            string subject, string body)
        {
            return new InboxMail
            {
                id = id,
                sender = "鎴樼暐鍙戝睍閮?,
                role = "鑱旂洘鎻愭",
                glyph = "鎴?,
                kind = "report",
                action = action,
                origin = origin,
                dest = dest,
                subject = subject,
                body = body,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            };
        }

        /// <summary>鏋㈢航鏃跺埢妲?+ PEK鈥揝IN 绗簲鑸潈鎻愭锛堝畾绋?n77 / P12锛夈€?/summary>
        public static void MaybeOfferRightsAndSlots(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) return;

            string hub = string.IsNullOrEmpty(gs.hubIata) ? "PEK" : gs.hubIata;
            if ((stg.slots == null || stg.slots.Count == 0)
                && !HasMailId(gs, "m_slot_hub") && !HasOpenAction(gs, "buy_slot"))
            {
                QueueSlotDecision(gs, hub, 1, "m_slot_hub", out _);
                return;
            }

            if (!StrategyManager.HasRouteRight(gs, "PEK", "SIN")
                && !HasMailId(gs, "m_right_pek_sin") && !HasOpenAction(gs, "apply_right"))
            {
                QueueRightDecision(gs, "PEK", "SIN", "m_right_pek_sin", out _);
            }
        }

        public static bool QueueSlotDecision(GameState gs, string airport, int band, string id, out string message)
        {
            message = "";
            if (gs == null) { message = "娓告垙鏈氨缁?; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "buy_slot"))
            {
                message = "宸叉湁鏃跺埢妲芥彁妗堝湪鏀朵欢绠卞緟鎵广€?;
                return false;
            }
            if (!string.IsNullOrEmpty(id) && HasMailId(gs, id))
            {
                message = "璇ユ椂鍒绘Ы鎻愭宸叉彁浜よ繃銆?;
                return false;
            }

            string apt = string.IsNullOrEmpty(airport) ? (gs.hubIata ?? "PEK") : airport;
            int b = Mathf.Clamp(band, 0, 3);
            float cost = StrategyEconomics.SlotValueWan(0, b);
            string mailId = string.IsNullOrEmpty(id) ? "m_slot_" + apt + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId))
            {
                message = "鏈宸叉彁浜ゅ悓鏃跺埢妲芥彁妗堛€?;
                return false;
            }

            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "鎴樼暐鍙戝睍閮?,
                role = "鑸潈鎻愭",
                glyph = "鎴?,
                kind = "report",
                action = "buy_slot",
                origin = apt,
                dest = b.ToString(),
                subject = $"寤鸿璐拱 {apt} {StrategyManager.SlotBandName(b)}鏃舵鏃跺埢妲?,
                body = $"鏋㈢航 {apt} 涓椂娈垫椂鍒绘Ы浼颁环绾?{cost:F0} 涓囥€俓n\n" +
                    "寤鸿锛氭壒鍑嗗悗涓嬪搴︽墸娆句氦鍓层€傚彲鐐瑰叧閿瘝鏌ョ湅鑸潈涓庢椂鍒绘Ы銆俓n" +
                    "鎷掔粷鍒欐湰瀛ｄ笉鎵╁鏃跺埢锛岄珮宄伴毦浠ュ姞瀵嗐€?,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "宸叉彁浜ゆ敹浠剁锛氳鎵瑰噯璐拱鏃跺埢妲斤紝涓嬪搴︽墸娆句氦鍓层€?;
            return true;
        }

        public static bool QueueRightDecision(GameState gs, string origin, string dest, string id, out string message)
        {
            message = "";
            if (gs == null) { message = "娓告垙鏈氨缁?; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "apply_right"))
            {
                message = "宸叉湁鑸潈鐢宠鍦ㄦ敹浠剁寰呮壒銆?;
                return false;
            }
            string o = string.IsNullOrEmpty(origin) ? "PEK" : origin;
            string d = string.IsNullOrEmpty(dest) ? "SIN" : dest;
            string mailId = string.IsNullOrEmpty(id) ? "m_right_" + o + "_" + d + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId))
            {
                message = "璇ヨ埅鏉冪敵璇峰凡鎻愪氦杩囥€?;
                return false;
            }

            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "鎴樼暐鍙戝睍閮?,
                role = "鑸潈鎻愭",
                glyph = "鎴?,
                kind = "report",
                action = "apply_right",
                origin = o,
                dest = d,
                subject = $"寤鸿鐢宠 {o}鈥攞d} 绗簲鑸潈",
                body = $"绔炴爣璇勫垎鍏紡锛?.4脳缁忔祹鎬?+ 0.25脳鎶曡祫鎵胯 + 0.2脳鏀垮簻鍏崇郴 + 0.15脳鎴樼暐濂戝悎銆俓n" +
                    $"鑾锋壒灏嗘秷鑰楁斂搴滃叧绯荤偣鏁帮紙绾?{15f + 5 * 2f:F0}锛夈€俓n\n" +
                    "寤鸿锛氭壒鍑嗗悗涓嬪搴﹀姙缁撱€傚彲鐐硅埅鏉冧笌鏃跺埢妲?鏌ョ湅鎸佷粨銆傛嫆缁濆垯缁存寔鐜版湁鑸潈缃戠粶銆?,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "宸叉彁浜ゆ敹浠剁锛氳鎵瑰噯鑸潈鐢宠锛屼笅瀛ｅ害娑堣€楀叧绯荤偣骞惰幏鎵广€?;
            return true;
        }

        public static string FormatUnionMailBody(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            var u = hr?.union;
            if (u == null) return "宸ヤ細鏁版嵁涓嶅彲鐢ㄣ€?;
            return $"宸ヤ細婊℃剰搴?{u.satisfaction:F0} 路 缃㈠伐鍊掕鏃?{u.strikeCountdown} 鏈?路 CBA 鍒版湡 {u.cbaExpiresIn} 鏈?路 缃㈠伐椋庨櫓 {hr.strikeRisk:F0}\n\n" +
                "璇夋眰鍒楄〃锛氬姞钖?8% 路 鏀瑰杽鎺掔彮 路 澧炲姞骞村亣 路 鎻愰珮澶滅彮琛ヨ创\n\n" +
                "璁╂锛氭垚鏈珮 / 婊℃剰搴︹啈 / 椋庨櫓鈫揬n" +
                "寮虹‖锛氭垚鏈綆 / 婊℃剰搴︹啌 / 椋庨櫓鈫慭n" +
                "鎷栧欢锛氭姌涓紙鎴愭湰涓庨闄╁眳涓級\n\n" +
                "璇风洿鎺ュ湪鏈偖浠堕€夋嫨濮挎€侊紝缁撴灉鍥炲啓鍛樺伐婊℃剰搴︿笌鍑嗙偣鐜囪仈鍔紝骞堕璀﹀仠椋為闄┿€?;
        }

        public static void CompleteUnionMail(string mailId, string summary)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "浜哄姏璧勬簮閮?,
                role = "鎵ц鍥炴墽",
                glyph = "浜?,
                kind = "report",
                subject = "宸ヤ細璋堝垽宸茶惤鐩?,
                body = summary ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        /// <summary>0璁╂ 1寮虹‖ 2鎷栧欢銆備笌 n37 宸ヤ細璋堝垽妗屽悓涓€濂?HrManager.NegotiateUnion銆?/summary>
        public static bool ApplyUnion(GameState gs, InboxMail mail, int choice, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            EnsureHrManager();
            bool ok = HrManager.Instance.NegotiateUnion(choice, out message);
            if (!ok) return false;
            string status = choice == 0 ? "approved" : choice == 1 ? "rejected" : "deferred";
            Mark(gs, mail.id, status);
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "浜哄姏璧勬簮閮?,
                role = "鎵ц鍥炴墽",
                glyph = "浜?,
                kind = "report",
                subject = "宸ヤ細璋堝垽宸茶惤鐩?,
                body = message ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static bool RejectUnion(GameState gs, InboxMail mail, out string message)
        {
            return ApplyUnion(gs, mail, 1, out message);
        }

        /// <summary>CBA 灏嗗埌鏈熸垨鍔宠祫鍘嬪姏楂樻椂鍙戝伐浼氳皥鍒ら偖浠讹紙瀹氱 n37 / P13锛夈€?/summary>
        public static void MaybeOfferUnion(GameState gs)
        {
            if (gs == null) return;
            if (gs.year <= 1 && gs.quarter <= 1) return;
            if (HasOpenAction(gs, "union_talk")) return;
            string id = "m_union_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;

            var hr = HrManager.EnsureState(gs);
            if (hr?.union == null) return;
            bool due = hr.union.cbaExpiresIn <= 9
                || hr.strikeRisk >= 40f
                || hr.union.satisfaction < 55f;
            if (!due) return;

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            Add(gs, new InboxMail
            {
                id = id,
                sender = "浜哄姏璧勬簮閮?,
                role = "鍔宠祫鎻愭",
                glyph = "浜?,
                kind = hr.strikeRisk >= 50f ? "alert" : "report",
                action = "union_talk",
                subject = hr.union.cbaExpiresIn <= 6
                    ? "闆嗕綋鍗忚鍗冲皢鍒版湡锛岃鎷嶆澘璋堝垽濮挎€?
                    : "宸ヤ細婊℃剰搴︽壙鍘嬶紝寤鸿鍚姩璋堝垽",
                body = FormatUnionMailBody(gs),
                tag = hr.strikeRisk >= 50f ? "绱ф€? : "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        /// <summary>鐜伴噾璺戦亾 &lt; 3 鏈堟椂鍙戝嵄鏈哄喅绛栭偖浠讹紙瀹氱 n25/n103/n106 路 P16锛夈€?/summary>
        public static void MaybeOfferFinanceCrisis(GameState gs)
        {
            if (gs == null || gs.gameOver) return;
            if (gs.restructuring != null && gs.restructuring.filed) return;
            if (HasOpenAction(gs, "finance_crisis")) return;
            string id = "m_fin_crisis_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            if (!LoanManager.IsFinanceUnlocked(gs)) return;

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string dest = RecommendFinanceDest(gs);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "璐㈠姟閮?,
                role = "鍗辨満鎻愭",
                glyph = "璐?,
                kind = "alert",
                action = "finance_crisis",
                dest = dest,
                subject = "鐜伴噾璺戦亾杩囩煭锛岃鎷嶆澘绱ф€ヨ瀺璧?,
                body = FormatFinanceCrisisBody(gs, dest),
                tag = "绱ф€?,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        public static string RecommendFinanceDest(GameState gs)
        {
            if (gs == null) return ((int)FinancingKind.Bridge).ToString();
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var rating = LoanManager.GetCreditRating(gs);
            if (runway < 2f && (rating < CreditRating.BBB || gs.boardSatisfaction < 40f))
                return "restructure";
            if (rating >= CreditRating.BBB) return ((int)FinancingKind.Bank).ToString();
            if (gs.boardSatisfaction >= 40f) return ((int)FinancingKind.Equity).ToString();
            return ((int)FinancingKind.Bridge).ToString();
        }

        public static string FormatFinanceCrisisBody(GameState gs, string dest)
        {
            if (gs == null) return "璐㈠姟鏁版嵁涓嶅彲鐢ㄣ€?;
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var rating = LoanManager.GetCreditRating(gs);
            FinanceEconomics.CrisisInaction(gs.EffectiveMonthlyBurn, out float fee, out float bonus);
            string rec = dest == "restructure"
                ? "鐮翠骇淇濇姢閲嶇粍锛圕hapter 11锛?
                : dest == ((int)FinancingKind.Equity).ToString() ? "鑲′笢澧炶祫"
                : dest == ((int)FinancingKind.Bridge).ToString() ? "楂樻伅杩囨ˉ"
                : "閾惰璐锋";
            return $"鐜伴噾 {gs.cash:F0} 涓?路 {CashRunnelCalculator.FormatPlayerLabel(runway)} 路 淇＄敤 {LoanManager.CreditLabel(rating)} 路 钁ｄ簨浼?{gs.boardSatisfaction:F0}\n\n" +
                $"寤鸿锛氭壒鍑嗗悗鎵撳紑鏉℃纭 鎴?绱ф€ヨ瀺璧?绛剧讲锛堟帹鑽?{rec}锛夈€傚彲鐐归噸缁勬潯浠?鏌ョ湅鐮翠骇淇濇姢鏂规銆俓n" +
                $"鎷掔粷鎴栧埌鏈燂細渚涘簲鍟嗗偓娆剧害 {fee:F0} 涓囷紝鏈堢儳閽?+{bonus:F0} 涓囷紝钁ｄ簨浼氭弧鎰忓害 鈭?2銆?;
        }

        public static bool IsFinanceRestructure(InboxMail mail)
            => mail != null && (mail.dest == "restructure" || mail.dest == "ch11");

        public static int ParseFinanceKind(InboxMail mail)
        {
            if (mail == null || string.IsNullOrEmpty(mail.dest)) return (int)FinancingKind.Bank;
            int k;
            return int.TryParse(mail.dest, out k) ? k : (int)FinancingKind.Bank;
        }

        public static bool RejectFinanceCrisis(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            ApplyFinanceCrisisPenalty(gs, out message);
            Mark(gs, mail.id, "rejected");
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "璐㈠姟閮?,
                role = "鎵ц鍥炴墽",
                glyph = "璐?,
                kind = "alert",
                subject = "鏈瀺璧勶細娴佸姩鎬х户缁伓鍖?,
                body = message,
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static void ApplyFinanceCrisisPenalty(GameState gs, out string message)
        {
            message = "";
            if (gs == null) { message = "娓告垙鏈氨缁€?; return; }
            FinanceEconomics.CrisisInaction(gs.EffectiveMonthlyBurn, out float fee, out float bonus);
            gs.cash -= fee;
            gs.yearNetProfit -= fee;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 12f, 0f, 100f);
            gs.financeCrisisBurnBonus += bonus;
            message = $"鏈噰鍙栬瀺璧勶細鐜伴噾 鈭抺fee:F0} 涓囷紙渚涘簲鍟嗗偓娆撅級锛屾湀鐑ч挶 +{bonus:F0} 涓囷紝钁ｄ簨浼氭弧鎰忓害 鈭?2 鈫?{gs.boardSatisfaction:F0}銆?;
            gs.AddDecisionLog("璐㈠姟锛? + message);
            gs.NotifyChanged();
        }

        public static void ExpireOpenFinanceCrisis(GameState gs)
        {
            if (gs?.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "finance_crisis" || !IsOpen(m)) continue;
                if (!IsDue(m, gs.year, gs.quarter)) continue;
                RejectFinanceCrisis(gs, m, out _);
            }
        }

        public static void CompleteFinanceCrisisMail(string mailId, string summary)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "璐㈠姟閮?,
                role = "鎵ц鍥炴墽",
                glyph = "璐?,
                kind = "report",
                subject = "绱ф€ヨ瀺璧勬潯娆惧凡鐢熸晥",
                body = summary ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static string OpsScheduleMailId(string routeId) => "m_ops_sked_" + (routeId ?? "x");

        static RouteData FindRoute(GameState gs, InboxMail mail)
        {
            if (gs?.activeRoutes == null || mail == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(mail.origin) && r.routeId == mail.origin)
                    return r;
                if (r.origin == mail.origin && r.destination == mail.dest)
                    return r;
            }
            return null;
        }

        /// <summary>杩愯惀閮ㄥ彫闆嗗悇閮ㄩ棬鐮旂┒鍚庯紝鎶婂紑鑸彁妗堝憟 CEO 鏀朵欢绠卞喅绛栥€?/summary>
        public static bool QueueOpenRouteStudy(GameState gs, string origin, string dest, string via,
            string subject, string body)
        {
            if (gs == null || string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string viaPart = string.IsNullOrEmpty(via) ? "" : "_" + via;
            string id = "m_open_" + origin + "_" + dest + viaPart;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id) continue;
                if (IsOpen(m))
                {
                    m.subject = subject;
                    m.body = body;
                    m.origin = origin;
                    m.dest = dest;
                    m.stopover = via ?? "";
                    m.time = gs.DateLabel;
                    Changed?.Invoke();
                    return true;
                }
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "杩愯惀閮?,
                role = "寮€鑸爺绌朵細",
                glyph = "杩?,
                kind = "report",
                action = "open_route",
                origin = origin,
                dest = dest,
                stopover = via ?? "",
                subject = subject,
                body = body,
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static bool QueueOpsSchedule(GameState gs, RouteData route)
        {
            if (gs == null || route == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = OpsScheduleMailId(route.routeId);
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || m.id != id) continue;
                    if (IsOpen(m))
                    {
                        m.body = OpsScheduleLogic.FormatPlanBody(route);
                        m.time = gs.DateLabel;
                        Changed?.Invoke();
                        return true;
                    }
                }
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "杩愯惀閮?,
                role = "鑸彮璁″垝",
                glyph = "杩?,
                kind = "report",
                action = "ops_schedule",
                origin = route.routeId,
                dest = route.destination,
                subject = $"璇锋壒鍑?{route.origin}鈥攞route.destination} 鎵ч鏈轰笌鑸彮璁″垝",
                body = OpsScheduleLogic.FormatPlanBody(route),
                tag = "鎻愭",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferOpsSchedule(GameState gs)
        {
            if (gs?.activeRoutes == null) return;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || !r.schedulePending) continue;
                QueueOpsSchedule(gs, r);
            }
        }

        public static bool ApproveOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null) { message = "鏈壘鍒板搴旇埅绾裤€?; return false; }
            route.schedulePending = false;
            route.SyncPrimaryFromFlights();
            Mark(gs, mail.id, "approved");
            message = $"宸叉壒鍑?{route.origin}鈥攞route.destination}锛歿route.registration} 鎸?{route.depTime} {route.daysOfWeek} 杩愯銆?;
            gs.AddDecisionLog("杩愯惀锛? + message);
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "杩愯惀閮?,
                role = "鎵ц鍥炴墽",
                glyph = "杩?,
                kind = "report",
                subject = $"{route.origin}鈥攞route.destination} 鑸彮璁″垝宸茬敓鏁?,
                body = message,
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            gs.NotifyChanged();
            return true;
        }

        /// <summary>CEO 鎻愬嚭淇敼锛氭寜鎵瑰锛堝強鍙€?AI 鐝 JSON锛夐噸缂栧悗鍐嶆姤銆?/summary>
        public static bool ReviseOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            return ReviseOpsSchedule(gs, mail, null, null, out message);
        }

        public static bool ReviseOpsSchedule(GameState gs, InboxMail mail, string ceoNote, string aiJson, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null) { message = "鏈壘鍒板搴旇埅绾裤€?; return false; }
            float baseline = route.baseDailyProfit > 0.01f ? route.baseDailyProfit : route.dailyProfit;
            if (string.IsNullOrWhiteSpace(ceoNote))
            {
                route.opsPlanRevision++;
                if (!OpsScheduleLogic.ApplyDraft(route, gs.fleet, gs.activeRoutes, route.opsPlanRevision))
                {
                    message = "杩愯惀閮ㄦ殏鏃犳浛浠ｉ鏈烘垨鏃跺埢锛岃鎵瑰噯鐜扮鎴栭€€鍥炪€?;
                    return false;
                }
                OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
                mail.body = OpsScheduleLogic.FormatPlanBody(route);
            }
            else
            {
                if (!OpsScheduleLogic.ApplyFromCeoNoteOrAi(route, gs.fleet, gs.activeRoutes, ceoNote, aiJson, out string opsReply))
                {
                    message = "杩愯惀閮ㄦ殏鏃犳浛浠ｉ鏈烘垨鏃跺埢锛岃鎵瑰噯鐜扮鎴栭€€鍥炪€?;
                    return false;
                }
                OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
                mail.body = OpsScheduleLogic.FormatPlanBody(route, ceoNote, opsReply);
            }
            mail.subject = $"璇锋壒鍑?{route.origin}鈥攞route.destination} 鑸彮璁″垝锛堟敼绋?{route.opsPlanRevision + 1}锛?;
            mail.status = "pending";
            mail.read = false;
            message = "杩愯惀閮ㄥ凡鎸夋偍鐨勬壒澶嶈皟鏁磋埅鐝鍒掞紝璇峰啀瀹°€?;
            gs.AddDecisionLog("杩愯惀锛? + message + $" {route.registration} {route.depTime}");
            Changed?.Invoke();
            gs.NotifyChanged();
            return true;
        }

        public static bool RejectOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null) { message = "鏈壘鍒板搴旇埅绾裤€?; return false; }
            route.schedulePending = true;
            Mark(gs, mail.id, "rejected");
            message = $"宸查€€鍥?{route.origin}鈥攞route.destination} 鎺掔彮鏂规锛屾湰鑸嚎鏆備笉鎵ц鑸彮銆?;
            gs.AddDecisionLog("杩愯惀锛? + message);
            gs.NotifyChanged();
            return true;
        }

        public static void NextQuarter(int year, int quarter, out int nextYear, out int nextQuarter)
        {
            nextQuarter = quarter + 1;
            nextYear = year;
            if (nextQuarter > 4)
            {
                nextQuarter = 1;
                nextYear++;
            }
        }

        /// <summary>褰撳墠鏄惁鏈夊埌鏈熸湭澶勭悊鐨勫緟鍔為偖浠讹紙闃绘柇鏃堕棿鎺ㄨ繘锛夈€?/summary>
        public static bool HasDueMails(GameState gs)
        {
            if (gs == null || gs.inbox == null) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsDue(gs.inbox[i], gs.year, gs.quarter)) return true;
            return false;
        }

        /// <summary>鍙栫涓€灏佸埌鏈熼偖浠讹紙寮哄埗璺宠浆瀵硅薄锛夈€?/summary>
        public static InboxMail FirstDueMail(GameState gs)
        {
            if (gs == null || gs.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsDue(gs.inbox[i], gs.year, gs.quarter)) return gs.inbox[i];
            return null;
        }

        /// <summary>寰呭鐞嗛偖浠舵暟锛堟壒鍑?鎷掔粷鍓嶏級銆?/summary>
        public static int OpenCount(GameState gs)
        {
            if (gs == null || gs.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsOpen(gs.inbox[i])) n++;
            return n;
        }

        /// <summary>鏈閭欢鏁帮紙搴曟爮绾㈢偣锛氱偣寮€璇︽儏鍚庡噺灏戯紝鍏ㄩ儴鐐瑰紑鍚庝负 0锛夈€?/summary>
        public static int UnreadCount(GameState gs)
        {
            if (gs == null || gs.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && !gs.inbox[i].read) n++;
            return n;
        }

        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case "approved": return "宸叉壒鍑?;
                case "deferred": return "宸叉殏缂?;
                case "rejected": return "宸叉嫆缁?;
                default: return "寰呭鐞?;
            }
        }

        public static OpsIrropsLogic.IrropsKind ParseIrropsKind(InboxMail mail)
        {
            if (mail == null || !int.TryParse(mail.origin, out int k))
                return OpsIrropsLogic.IrropsKind.Weather;
            if (k < 0) k = 0;
            if (k > 3) k = 3;
            return (OpsIrropsLogic.IrropsKind)k;
        }

        public static void ApplyIrropsOutcome(GameState gs, OpsIrropsLogic.IrropsKind kind, OpsIrropsLogic.Outcome outcome)
        {
            if (gs == null) return;
            gs.cash += outcome.cashDeltaWan;
            gs.onTimePercent = Mathf.Clamp(gs.onTimePercent + outcome.onTimeDelta, 40f, 99f);
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + outcome.boardDelta, 0f, 100f);
            gs.AddDecisionLog(
                $"鑸彮寮傚父[{OpsIrropsLogic.KindLabel(kind)}] {outcome.label}锛? +
                $"鐜伴噾 {outcome.cashDeltaWan:F0}锛屽噯鐐?{outcome.onTimeDelta:+0;-0}锛岃懀浜嬩細 {outcome.boardDelta:+0;-0}銆?);
            gs.NotifyChanged();
        }

        public static void CompleteIrropsMail(string mailId, OpsIrropsLogic.Outcome outcome)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "杩愯惀閮?,
                role = "鎵ц鍥炴墽",
                glyph = "杩?,
                kind = "report",
                subject = "鑸彮寮傚父宸插缃細" + (outcome.label ?? ""),
                body = outcome.summary ?? "",
                status = "approved",
                tag = "鍥炴墽",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        public static bool RejectIrrops(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "閭欢鏃犳晥銆?; return false; }
            var kind = ParseIrropsKind(mail);
            var outcome = OpsIrropsLogic.Resolve(kind, OpsIrropsLogic.Choice.C);
            ApplyIrropsOutcome(gs, kind, outcome);
            Mark(gs, mail.id, "rejected");
            message = "宸叉嫆缁濅笓椤瑰缃紝鎸夋柟妗?C 鏀跺彛锛? + outcome.summary;
            return true;
        }

        /// <summary>瀛ｅ害闅忔満娉ㄥ叆 IRROPS 璀︽姤閭欢锛堝畾绋?n24锛夈€?/summary>
        public static void MaybeOfferIrrops(GameState gs)
        {
            if (gs == null) return;
            if (gs.year <= 1 && gs.quarter <= 1) return;
            if (HasOpenAction(gs, "irrops")) return;
            if (OfferedIrropsThisQuarter(gs)) return;
            int key = gs.year * 4 + gs.quarter;
            if ((key * 13 + 7) % 10 >= 4) return;

            var kinds = new[]
            {
                OpsIrropsLogic.IrropsKind.Weather,
                OpsIrropsLogic.IrropsKind.Aog,
                OpsIrropsLogic.IrropsKind.Atc,
                OpsIrropsLogic.IrropsKind.CrewShortage
            };
            var kind = kinds[Mathf.Abs(key) % kinds.Length];
            string route = "鏋㈢航";
            if (gs.activeRoutes != null && gs.activeRoutes.Count > 0 && gs.activeRoutes[0] != null)
                route = $"{gs.activeRoutes[0].origin}-{gs.activeRoutes[0].destination}";

            Add(gs, new InboxMail
            {
                id = "m_irrops_" + gs.year + "q" + gs.quarter,
                sender = "杩愯惀閮?,
                role = "杩愯鎺у埗",
                glyph = "杩?,
                kind = "alert",
                action = "irrops",
                origin = ((int)kind).ToString(),
                dest = route,
                subject = $"銆愯鎶ャ€憑OpsIrropsLogic.KindLabel(kind)} 路 {route}",
                body = OpsIrropsLogic.ScenarioBody(kind) +
                    "\n\n寤鸿锛氱偣鎵瑰噯鎵撳紑鑸彮寮傚父 澶勭疆寮圭獥锛圓/B/C锛夈€傛嫆缁濆垯鎸夋渶鐪佹柟妗?C 鏀跺彛銆傚彲鐐瑰叧閿瘝鏌ョ湅鑸嚎缃戠粶銆?,
                tag = "绱ф€?,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static bool OfferedIrropsThisQuarter(GameState gs)
        {
            if (gs?.inbox == null) return false;
            string id = "m_irrops_" + gs.year + "q" + gs.quarter;
            return HasMailId(gs, id);
        }
    }
}
