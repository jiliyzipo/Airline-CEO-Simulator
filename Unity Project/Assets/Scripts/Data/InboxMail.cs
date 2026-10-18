using System;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 收件箱邮件（GDB 白板 n7「完整邮件模型」）：持久化到 GameState，随季度推进。
    /// 待办邮件（pending/deferred）自收件季度起，超过 deadlineQuarters 个季度仍未处理即「到期」，
    /// 到期后强制处理（批准/拒绝）方可继续游戏。
    /// </summary>
    [Serializable]
    public class InboxMail
    {
        public string id;
        public string sender;      // 发件部门/人
        public string role;        // 角色/说明
        public string glyph;       // 头像字
        public string time;        // 收件时分（如 "20:05"）；旧档可能仍是财年标签
        /// <summary>收件瞬间 UTC ticks；0 表示旧档，打开时按当前游戏时钟补记。</summary>
        public long receivedUtcTicks;
        public string subject;
        public string body;
        public string status = "pending"; // pending / approved / deferred / rejected
        /// <summary>CEO 决策会议对该函的裁决结果（approved/deferred/rejected）；空串=尚未上会。
        /// 已上会裁决的邮件回收件箱后不再显示底部决策按钮（暂缓=补条件中，条件齐备可重新召集）。</summary>
        public string meetingOutcome = "";
        public bool read;                 // 点开详情后为已读；角标只计未读
        public string tag = "";           // 紧急 / 提案 等角标
        /// <summary>report / regulatory / media / alert</summary>
        public string kind = "report";
        /// <summary>open_route / none</summary>
        public string action = "";
        public string origin = "";
        public string dest = "";
        /// <summary>开航提案经停 IATA，可空。</summary>
        public string stopover = "";
        public bool effectPending;
        public int effectYear;
        public int effectQuarter;
        public int receivedYear;
        public int receivedQuarter;
        public int deadlineQuarters = 2;  // 自收件季度起允许搁置的季度数

        // ———— 航线准入许可（Phase 18）————
        // CEO 批准开航计划 ≠ 可以飞：还须办理航线经营许可、航权额度、航班时刻并过公示期。
        // 本组字段承载在开航提案邮件上，随该邮件一起存档（open_route 邮件即"许可办理单"）。
        /// <summary>准入阶段：0=未受理 1=审查中 2=公示期 3=已发证 4=未获批</summary>
        public int permitStage;
        /// <summary>预计批复季度（每季推进一次复核）。</summary>
        public int permitDueYear;
        public int permitDueQuarter;
        /// <summary>是否国际/地区航线（需航权额度 + 对方许可 + 时刻）。</summary>
        public bool permitInternational;
        /// <summary>受理时冻结的通过率（重复办理会下调）。</summary>
        public float permitChance;
        /// <summary>已递交次数（被否后重申则 +1）。</summary>
        public int permitAttempts;
        /// <summary>本许可是否已遭遇公示异议（只触发一次）。</summary>
        public bool permitObjection;
        /// <summary>许可类型/状态说明（如「国内航线经营许可 · 审查中」）。</summary>
        public string permitNote = "";
        /// <summary>当前受阻原因（航权 / 时刻）；空串表示前置齐备。</summary>
        public string permitGate = "";
        /// <summary>受理所在季度（用于生成节点进度表）。</summary>
        public int permitAcceptYear;
        public int permitAcceptQuarter;
        /// <summary>已上报到第几个节点：1=受理 2=审查 3=航权/时刻 4=公示 5=批复。</summary>
        public int permitNode;

        // ———— 运力调整呈报（CEO 指令 → 运营部执行 → 呈报待批复）————
        /// <summary>本次呈报建立的航班环 ID（退回时撤销）。</summary>
        public string ringIdsCsv = "";
        /// <summary>CEO 原始指令（多轮修改时保留最近一条）。</summary>
        public string directiveNote = "";
        /// <summary>
        /// 本次呈报的**目标**每日波次（CEO 要了几段往返）：正数 = 目标段数；**-1 = 指令要求维持现状**；
        /// 0 = 旧档未记录。
        ///
        /// ★2026-10-16 第四十二次修订。批复回执要分「已生效 / 部分生效 / 未变动」三档，而回执在被点的
        /// 那一刻只能读到**现在**几段（`RouteData.FlightCount`）—— 目标段数只在呈报当时知道，不随呈报
        /// 一起存下来，回执就无从判断「这次调令到底落地了没有」，只能凭一句常量写「已生效」。
        /// </summary>
        public int capTargetWaves;
        /// <summary>本呈报执行**前**的每日波次（用于区分「一班没动」与「动了但没排满」）。</summary>
        public int capBeforeWaves;
        /// <summary>
        /// 本次呈报「想要但排不出」的缺口段（渲染好的原文，与呈报正文同源）。
        /// 批复回执照抄一段并写明「本批复不视为该缺口已解决」，免得回执只报一句「已生效」。
        /// </summary>
        public string capGapSection = "";
        /// <summary>呈报所依据的排班引擎修订号；低于 CapacityDirectiveLogic.EngineRev 则自动重编。</summary>
        public int capEngineRev;
        /// <summary>本函关联的业务主键（如过渡询价的报价 id，逗号分隔）。</summary>
        public string payloadCsv = "";

        // ———— 换季运力评审（每季度一次，运营部呈报待批复）————
        /// <summary>评审稿次（CEO 提出修改后 +1）。</summary>
        public int seasonRev;
        /// <summary>本期建议加密的航线 ID（逗号分隔）。</summary>
        public string seasonDensifyCsv = "";
        /// <summary>本期建议减班的航线 ID（逗号分隔）。</summary>
        public string seasonTrimCsv = "";

        // ———— 时刻槽使用率考核（每航季一次，局方裁定）————
        /// <summary>待裁定的时刻系列（`机场|时段|星期` 逗号分隔，见 SlotUsageLogic.EncodeSeries）。</summary>
        public string slotSeriesCsv = "";

        // ———— 航班时刻加机呈报（运营部提案 → CEO 批准后才落班）————
        /// <summary>
        /// 待批的加机提案，每项 `机号|时段|离港|到港`，条目间逗号分隔（见 FlightProposalLogic）。
        /// 提案只存在这里，批准那一刻才写进 RouteData.flights —— 退回即等于没提过。
        /// </summary>
        public string flightProposalsCsv = "";

        // ———— 时刻槽申请单（n98 点时段却缺槽 → 运营部呈报申请，附拟加班次缘由）————
        /// <summary>
        /// 本时刻槽申请为谁而提（如「B-2006 拟加 FRA—CDG 的晚班 18:00」）。
        /// 交割回执照抄一句，免得只报「FRA 晚班 1 个」，CEO 事后不知道是要给哪条线加机。
        /// </summary>
        public string slotReason = "";

        /// <summary>
        /// 本函涉及的时刻槽**具体时刻**（`"HH:mm"`，★2026-10-15）。
        /// 时刻槽的身份就是「机场 × 时刻 × 星期」，买卖/申请/交割单必须写死这一刻；
        /// 旧档此字段为空，读取时由 `InboxManager.SlotMailClock` 回退到 `dest` 再回退到时段峰点。
        /// </summary>
        public string slotClock = "";

        /// <summary>
        /// 「时刻变更（改时）」申请单的**原时刻**（`"HH:mm"`）：本线在该站**已有**的那条槽。
        /// ★2026-10-16 第四十一次修订：现实里改时不是新申请，而是把**已分配到的系列**挪一挪
        /// （WASG 9.1.3 的 slot adjustments / 8.10.1 的 change of use）。所以单子上必须同时写下
        /// 「从哪一刻」与「到哪一刻」，否则交割时无从知道该退还哪一条、该换成哪一条。
        /// </summary>
        public string slotFromClock = "";

        /// <summary>
        /// 本时刻槽单**为哪条航线而提**（`routeId`；空 = 战略部自主提案，不属于任何航线的缺口）。
        /// ★2026-10-15：交割后要「自动重排本线并呈报」，就得在单子上**结构化**地记住是哪条线 ——
        /// 去 `slotReason` 那句人话里解析航线名不是办法。交割回执与自动重排都靠本字段。
        /// </summary>
        public string slotRouteId = "";

        /// <summary>
        /// 本时刻槽单是**为哪一刻的班次**而提（`"HH:mm"`，往返的**去程离港**时刻）。
        /// ★2026-10-15：申请单上的 <see cref="slotClock"/> 写的是**缺槽的那一端**（可能是对端回程离港），
        /// 而交割后要补的是「去程 07:30 那一段往返」—— 两者不是同一个时刻。少了本字段，
        /// 交割后自动重排就只能拿回程时刻去排去程，排出来是一条没人要的班次。
        /// </summary>
        public string slotFlightClock = "";

        // ———— 加班时刻槽备选（运营部按整张窗口网格提名 → CEO 点选一刻）————
        /// <summary>
        /// 备选往返时刻对，见 <see cref="AirlineCEO.Logic.SlotCandidateLogic.Encode"/>（复用 `payloadCsv`）。
        /// </summary>
        // （备选表就存在 payloadCsv 里，与执飞飞机提名同源；此处不再另设字段。）
    }

    [Serializable]
    public class InboxMailListSave
    {
        public InboxMail[] items;
    }

    /// <summary>已写好、尚未送达收件箱的邮件（会商/回执按办公节奏延迟投递）。</summary>
    [Serializable]
    public class PendingInboxMail
    {
        public long deliverUtcTicks;
        public InboxMail mail;
    }

    [Serializable]
    public class PendingInboxListSave
    {
        public PendingInboxMail[] items;
    }
}
