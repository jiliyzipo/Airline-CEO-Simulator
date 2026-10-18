using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    // ============================================================
    // STG 战略发展部（Phase 2 Week 9）
    // ============================================================

        /// <summary>时刻槽持仓（n77）。</summary>
        [Serializable]
        public class SlotHolding
        {
            public string airport;        // 机场 IATA
            /// <summary>
            /// 该持仓的**具体离港时刻** `"HH:mm"`（★2026-10-15：此前是「早/中/晚/夜」）。
            ///
            /// 现实里协调人分配的单位就是「某日某刻的一次离港」：WASG 1.7.2(d) 明写航司不得故意
            /// 在明显不同于获批时刻的时间运行，1.7.2(e) 又要求一个系列 = 同一（或约同一）时刻且
            /// 整季规律分布。所以 06:30 与 08:00 是**两条槽**，「FRA 早班槽」在协调人那里不是一个
            /// 可持有的标的。旧档里的时段名由 SlotUsageLogic.MigrateSlotTimes 一次性折成峰点。
            /// </summary>
            public string slotTime = "";
            public bool inUse;            // 是否已用于航班环
            public float acquisitionCostWan; // 取得成本（万美元）；协调池分配为 0
            public int expiresQuarter;    // 到期季度（use-it-or-lose-it，旧口径）
            /// <summary>
            /// use-it-or-lose-it 的收回时点（航季 key，见 <see cref="SlotUsageState.countingKey"/>）。
            ///
            /// ★2026-10-15 新增：`expiresQuarter` 此前只写不读。但**一次都没绑上班次的持仓没有系列**，
            /// 系列考核（80%）根本裁不到它 ——「压根不飞」那一半反而漏在规则之外。这条字段让
            /// 换季清算能按「取得时点 + 2 个季度」收回闲置持仓（给 CEO 留一个航季的筹备窗口）。
            /// </summary>
            public int expiresTurn = -1;
            /// <summary>P3：被哪条航线占用（空 = 未绑定班次）。</summary>
            public string usedByRouteId = "";
            /// <summary>P3：被哪个离港航班号占用。</summary>
            public string usedByFlightNo = "";
        }

    /// <summary>
    /// 一个「时刻系列」——IATA WASG 的口径：**同一机场 + 同一具体时刻 + 同一星期几**。
    ///
    /// 这不是我们发明的粒度：WASG 8.7.1(f) 明确「一个航班若在多个星期几运行，每个星期几各算
    /// 一个独立系列」，80% 使用率逐系列判定（8.7.1(e) 同）。所以「每日一班」实际是 7 个系列，
    /// 可以周一掉、周二保 —— 这与「整条航线一个使用率」是完全不同的判法。
    ///
    /// ★2026-10-15：段的粒度由「时段」（早/中/晚/夜）改为**具体时刻** `"HH:mm"`。理由同上：
    /// WASG 把系列钉在时刻上，同段不同时刻是不同系列（06:30 天天飞、07:00 一周飞两天，
    /// 前者保权后者掉格，本就该分开判）。
    /// </summary>
    [Serializable]
    public class SlotSeries
    {
        public string airport;        // 该停站机场（本场端 / 经停端 / 对端 之一）
        public string time;           // 该系列的离港时刻 "HH:mm"（★2026-10-15：原为 int band）
        public int weekday;           // 0=周一 … 6=周日

        /// <summary>
        /// 历史优先权（historic precedence，俗称「祖父条款」）。
        /// false = 该系列已失格，下一等价航季不再自动保留。
        /// </summary>
        public bool historicPrecedence = true;

        /// <summary>本季 HBD 起「应飞」次数（分母）。</summary>
        public int due;
        /// <summary>本季 HBD 起实际执飞次数（分子）。</summary>
        public int operated;

        /// <summary>
        /// 本季「未飞但事由正当」的次数（WASG §8.8 Justified Non-Utilisation）。
        /// 只有 &gt; 0 的系列才有资格申请 JNUS —— 与现实一致：豁免必须基于真实事由，
        /// 不能对「就是不想飞」申请。
        /// </summary>
        public int justified;
        /// <summary>正当事由明细（如「无机可派×12、机场关闭×3」），供豁免申请邮件列出。</summary>
        public string jnusReasonCsv = "";

        /// <summary>最近一次裁定结果：kept / lost / jnus；空串 = 未评。</summary>
        public string lastResult = "";
        /// <summary>最近一次裁定所在航季（展示用，如「第 3 年 夏秋」）。</summary>
        public string lastEvalSeason = "";
        /// <summary>最近一次裁定的使用率（−1 = 未评）。</summary>
        public float lastUsage = -1f;
    }

    /// <summary>一条「待执行」的时刻处置（释放持仓 / 待裁豁免）。机场 × 具体时刻 × 星期几。</summary>
    [Serializable]
    public class SlotPendingAction
    {
        public string airport;
        public string time;           // "HH:mm"（★2026-10-15：原为 int band）
        public int weekday;
        /// <summary>执行所在航季 key（见 <see cref="SlotUsageState.countingKey"/> 的 key 定义）。</summary>
        public int applyKey = -1;
        /// <summary>发起该条目的邮件 id（JNUS 裁定用；直判失格时为空）。</summary>
        public string mailId = "";
        /// <summary>系列标识（展示用，如「PEK 08:00 周一」）。</summary>
        public string label = "";
    }

    /// <summary>
    /// 时刻槽使用率考核（IATA WASG §8.6 use-it-or-lose-it + §8.7 historic precedence）。
    ///
    /// 与真实规则的对应关系（全部按真实口径，不做「一年一个率」的简化）：
    /// - **判季不判年**：航季 = 2 个季度（夏秋 = Q2+Q3 / 冬春 = Q4+Q1），一年判两次。
    /// - **分系列**：机场 × 具体时刻 × 星期几，逐系列算 80%。
    /// - **HBD 起计**：只在「历史基准日」（本作取航季第 2 个季度初，对应真实 1/31 与 8/31）起
    ///   计数，此前的取消不计入 —— 等价于真实规则里「HBD 之后的取消一律算未使用」。
    /// - **JNUS 主动申请**：正当未用必须由 CEO 主动申请并由局方裁定，不申请即按失格处理。
    /// </summary>
    [Serializable]
    public class SlotUsageState
    {
        /// <summary>
        /// 正在计数的航季 key（−1 = 尚未进入任何航季的 HBD）。
        /// key = 起始年 × 2 + （0 夏秋 / 1 冬春）；世界轴上自增 1，故 key−1 恒为「上一等价航季」。
        /// </summary>
        public int countingKey = -1;

        /// <summary>本季系列清单（HBD 时快照，计数期间累加 due/operated）。</summary>
        public List<SlotSeries> series = new List<SlotSeries>();

        /// <summary>上一季评定摘要（展示用，不参与计算）。</summary>
        public string lastEvalSeason = "";
        public int lastKept;
        public int lastLost;
        public int lastJnus;
        public int lastPending;

        /// <summary>已裁定失格、待「下一等价航季开端」释放持仓的队列。</summary>
        public List<SlotPendingAction> pendingReleases = new List<SlotPendingAction>();
        /// <summary>已申请 JNUS、待 CEO 裁定的系列（裁定前不释放）。</summary>
        public List<SlotPendingAction> pendingJnus = new List<SlotPendingAction>();

        /// <summary>
        /// 时刻槽口径由「时段」迁移为「具体时刻」的一次性标记（★2026-10-15）。
        /// 旧档里 `slotTime` 存的是「早/中/晚/夜」，读档后由
        /// <see cref="AirlineCEO.Logic.SlotUsageLogic.MigrateSlotTimes"/> 折成时刻并置 true，幂等。
        /// </summary>
        public bool slotsTimeMigrated;
    }

    /// <summary>航权（n77）。type = 第一~第九航权。</summary>
    [Serializable]
    public class RouteRight    {
        public string origin;
        public string destination;
        public int freedom;           // 1~9 航权
        public string status;         // approved / negotiating / denied
        public float governmentRelationCost; // 申请消耗政府关系点数
    }

    /// <summary>联盟与合作状态（n78）。</summary>
    [Serializable]
    public class AllianceState
    {
        public int tier;              // 0非成员 1观察员 2正式 3核心
        public string allianceName = "星空联盟";
        public int codeSharePartners; // 已签约代码共享伙伴数
        public int jvRouteGroups;     // 联营航线组数
        public float revenueLiftWan;  // 联盟增量收入（万美元/年）
    }

    /// <summary>投资项目（n79，四维打分）。</summary>
    [Serializable]
    public class InvestmentProject
    {
        public string id;
        public string name;
        public float businessValue;    // 商业价值 0~100
        public float resourceFit;      // 资源适配 0~100
        public float barrier;          // 进入壁垒 0~100
        public float strategicFit;     // 战略契合 0~100
        public float npvWan;           // 净现值（万美元）
        /// <summary>立项资本支出（万美元），CEO 批准后于下季度扣除。</summary>
        public float capitalWan = 280f;
        public bool approved;
        public string stage = "立项";   // 待批/待投入/执行/已拒
    }

    /// <summary>黑天鹅应急预案（n80）。</summary>
    [Serializable]
    public class ContingencyPlan
    {
        public string trigger;         // 油价暴涨>150 / 汇率暴跌>20% / 突发安全事件…
        public string autoMeasure;     // 自动措施
        public float lossReduction;    // 损失降低% 0~1
        public bool active;
    }

    /// <summary>战略发展部聚合状态（Phase 2 Week 9 · STG）。</summary>
    [Serializable]
    public class StrategyState
    {
        public float governmentRelation = 50f;   // 政府关系点数 0~100
        public float strategicConsistency = 72f; // 战略一致性评分 0~100
        public int geoRiskIndex = 35;            // 地缘政治风险指数 0~100
        public int waiverUsed;                   // 已用战略豁免额度（每财年最多 2）
        public List<SlotHolding> slots = new List<SlotHolding>();
        /// <summary>时刻槽使用率考核（IATA WASG 80/20 · 判季 · 分系列 · HBD · JNUS）。</summary>
        public SlotUsageState slotUsage = new SlotUsageState();
        public List<RouteRight> rights = new List<RouteRight>();
        public AllianceState alliance = new AllianceState();
        public List<InvestmentProject> investments = new List<InvestmentProject>();
        public List<ContingencyPlan> contingencyPlans = new List<ContingencyPlan>();
        public int[] fiveYearPriority = { 1, 2, 3, 4, 5 }; // 航线扩张/机队升级/联盟深化/成本优化/绿色转型
    }

    [Serializable]
    public class StrategySave
    {
        public StrategyState state;
    }

    // ============================================================
    // MNT 机务工程部（Phase 2 Week 9）
    // ============================================================

    /// <summary>航材库存条目（n83）。</summary>
    [Serializable]
    public class PartStock
    {
        public string partNo;       // 件号
        public string name;         // 件名
        public string category;     // A/B/C 类
        public int quantity;        // 在库数量
        public int safetyStock;     // 安全库存线
        public float unitCostUsd;   // 单价（美元）
    }

    /// <summary>航材采购订单（n83/n105）。</summary>
    [Serializable]
    public class PurchaseOrder
    {
        public string orderNo;
        public string partNo;
        public string supplier = "OEM 原厂";
        public int quantity;
        public float unitCostUsd;
        public string status = "草稿"; // 草稿 / 已下单
        public int leadDays;          // 交期（天）
        public bool urgent;           // 紧急/停飞加急
    }

    /// <summary>OEM 供应商关系（n17/n82）。</summary>
    [Serializable]
    public class OemRelation
    {
        public string oem;          // 波音/空客/巴航
        public int stars = 2;       // 1~5 关系等级
        public float discountRate;  // 购机折扣 0~1
        public int deliveryPriority; // 交付优先级 0~2
    }

    /// <summary>定检排程（n81）。A/B/C/D 检。</summary>
    [Serializable]
    public class CheckSchedule
    {
        public string registration;  // 飞机注册号
        public string checkType;     // A/B/C/D
        public int dueMonth;         // 预计执行月份（相对当前）
        public float costWan;        // 检修成本（万美元）
        public bool conflict;        // 与航班计划冲突
    }

    /// <summary>机务工程部聚合状态（Phase 2 Week 9 · MNT）。</summary>
    [Serializable]
    public class MaintenanceState
    {
        public int strategy;              // 0全面预防 1最低合规 2智能预测
        public float budgetWan = 300f;    // 年度维修预算（万美元）
        public float budgetUsedWan = 180f;// 已执行
        public int groundedCount;         // 停飞飞机数量
        public float fleetAvailability = 96f; // 机队可用率%
        public float errorRate = 0.8f;    // 维修差错率%
        public List<PartStock> parts = new List<PartStock>();
        public List<PurchaseOrder> orders = new List<PurchaseOrder>();
        public List<OemRelation> oems = new List<OemRelation>();
        public List<CheckSchedule> checks = new List<CheckSchedule>();
        public int engineerCount = 180;   // 持证工程师总数
        public int trainingCount;         // 培训中人数
    }

    [Serializable]
    public class MaintenanceSave
    {
        public MaintenanceState state;
    }
}
