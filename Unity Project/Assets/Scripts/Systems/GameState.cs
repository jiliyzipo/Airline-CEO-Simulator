using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// MVP 全局游戏状态（现金、日期、董事会满意度、航线列表等）。
    /// </summary>
    public class GameState : MonoBehaviour
    {
        public static GameState Instance { get; private set; }

        [Header("初始状态")]
        public float cash = 500f;              // 万美元
        public float monthlyBurnRate = 80f;    // 万美元/月（固定开销）
        public int year = 1;
        public int quarter = 1;                // 1~4
        /// <summary>当前倍速档（0=1× / 1=2× / 2=3×）。暂停不写入此字段。</summary>
        public int speedLevel = 0;
        /// <summary>本季第几天（1~90）。</summary>
        public int simDay = 1;
        /// <summary>游戏时钟：时（0~23，UTC）。</summary>
        public int simHour;
        /// <summary>游戏时钟：分（0~59，UTC）。</summary>
        public int simMinute;
        /// <summary>本季已按落地计入的航线利润（万美元），季报直接读取，不按预测追补未飞收入。</summary>
        public float liveQuarterIncome;
        /// <summary>本季已按日计提的固定成本（万美元）。</summary>
        public float liveQuarterExpense;
        /// <summary>游戏公历（UTC 日期，与 simHour/simMinute 组成同一瞬间）。</summary>
        public int wallYear;
        public int wallMonth;
        public int wallDay;
        public float boardSatisfaction = 50f;  // 0~100
        /// <summary>是否已开通至少一条航线（存档字段名历史遗留；UI 不出现「首航」）。</summary>
        public bool firstFlightCompleted;

        [Header("航空顾问开局引导（v1.1）")]
        /// <summary>0=S0 … 6=S6，7=已完成。</summary>
        public int advisorTutorialStep;
        public bool advisorTutorialDeferred;
        public bool advisorTutorialCompleted;
        /// <summary>0=无；1=等待收件箱操作；2=等待机队「分配」操作。</summary>
        public int advisorTutorialAwaitPage;
        /// <summary>S1「发起决策会议」后置真：收件箱已留一份深圳—北京提案邮件（作为记录）。</summary>
        public bool advisorTutorialMailLeft;

        [Header("开局引导 · 排班调整（S4）")]
        public int tutorialWeeklyFlights = 7;
        public string tutorialDaysOfWeek = "每日";
        public string tutorialTimeSlot = "早班";
        public string tutorialDepTime = "08:00";
        public string tutorialArrTime = "10:30";

        [Header("市场（Phase 2）")]
        public float fuelPricePerTon = 850f;   // 美元/吨，季度事件可波动

        [Header("航线（Phase 1）")]
        public List<RouteData> activeRoutes = new List<RouteData>();

        [Header("航班环（Phase 2 OPS）")]
        public List<FlightRing> flightRings = new List<FlightRing>();

        [Header("董事会目标（Phase 5）")]
        public List<BoardTarget> currentTargets = new List<BoardTarget>();
        public int targetsFiscalYear;
        /// <summary>本届董事会董事 id（n4 抽卡选座结果，8 席 + CEO 1 席）；空则用默认全库。</summary>
        public List<string> boardMemberIds = new List<string>();

        [Header("收件箱（GDB 白板 n7 · 完整邮件模型）")]
        public List<InboxMail> inbox = new List<InboxMail>();
        /// <summary>已拟稿、尚未投递的邮件（会商/回执按办公日延迟）。</summary>
        public List<PendingInboxMail> pendingInbox = new List<PendingInboxMail>();
        public List<PersonnelData> staffPool = new List<PersonnelData>();
        public bool staffBootstrapped;
        /// <summary>非总监岗位招聘案（寻访 / 面试 / 待批 / 入职通知）。</summary>
        public List<StaffRecruitCase> staffRecruits = new List<StaffRecruitCase>();

        [Header("过渡业务（开局三架飞机的盈利设计）")]
        /// <summary>在租合同：占用飞机若干天、逐日入账。占用期间飞机不可分配航线。</summary>
        public List<TransitionContract> transitionContracts = new List<TransitionContract>();
        /// <summary>待接单报价（干租/湿租客户常驻，包机/培训机会短促）。</summary>
        public List<TransitionOffer> transitionOffers = new List<TransitionOffer>();
        /// <summary>本季过渡业务已入账金额（万美元），仅用于财务面板报告，季末清零。</summary>
        public float liveQuarterTransitionIncome;
        /// <summary>是否已投递《开航前过渡业务建议》，避免重复发信。</summary>
        public bool transitionIntroSent;
        /// <summary>机组到位后是否已发「湿租/包机市场开通」进度函。</summary>
        public bool transitionCrewMailSent;
        /// <summary>教员到位后是否已发「培训市场开通」进度函。</summary>
        public bool transitionInstructorMailSent;
        public List<UsedListing> usedMarket = new List<UsedListing>();
        public int usedMarketYear;
        public int usedMarketQuarter;
        public List<CityEconomyDelta> cityEconomyDeltas = new List<CityEconomyDelta>();
        public int cityEconomyRevision;
        public float yearNetProfit;          // 本财年累计净利润（万美元）
        public float yearStartCash;
        public float loadFactorPercent = 55f;
        public float safetyIndex = 82f;          // 与 safetyRating 同步，供董事会目标用
        public SafetyRating safetyRating = SafetyRating.B; // Phase 7：初始 B
        public List<SafetyEventLog> safetyEventLog = new List<SafetyEventLog>();
        public bool boardTargetsAnnounced;
        /// <summary>已确认年度目标的财年；与 year 相等才算本财年已确认（n5 / 愿景）。</summary>
        public int annualGoalsConfirmedYear;

        public bool AnnualGoalsConfirmed =>
            annualGoalsConfirmedYear == year && currentTargets != null && currentTargets.Count > 0;

        [Header("难度与终局（Phase 6）")]
        public DifficultyMode difficulty = DifficultyMode.Normal;
        public bool gameOver;
        public string gameOverReason = "";
        public int consecutiveFailedVotes;
        public int probationProtectionsUsed;

        [Header("终局与遗产结算（GDB 第4章 · P0）")]
        /// <summary>CEO 出生年份（用于 65 岁光荣退休触发）。</summary>
        public int ceoBirthYear = 1985;
        /// <summary>胜利通关（光荣退休）而非失败 Game Over。</summary>
        public bool gameOverVictory;
        /// <summary>解锁奖励：传奇模式 / 新游戏+ / 快速模式。</summary>
        public bool unlockedLegendMode;
        public bool unlockedNewGamePlus;
        public bool unlockedFastMode;
        /// <summary>最后一次遗产结算回显（结算 UI 用）。</summary>
        public float lastLegacyScore;
        public string lastLegacyGrade = "";

        [Header("玩法模式（GDB 第4/31章 · P0 遗留）")]
        /// <summary>当前局玩法模式：0 标准 / 1 传奇 / 2 新游戏+ / 3 快速。</summary>
        public int activeMode;
        /// <summary>传奇模式：AI 竞争压力增强标记。</summary>
        public bool legendModeActive;
        /// <summary>快速模式：随机事件频率减半标记。</summary>
        public bool fastModeActive;
        /// <summary>铁人模式：禁手动存档 + 季度自动存档实时覆盖 + 遗产评分 ×1.2（GDB 31.1）。</summary>
        public bool ironmanModeActive;

        [Header("渐进式解锁与失误保护（GDB 第28章 · P3 体验收口）")]
        /// <summary>失误保护剩余次数（见习期共 3 次，每财年最多 1 次）。</summary>
        public int mistakeProtectionRemaining = 3;
        /// <summary>最近一次使用失误保护的财年（用于「每财年最多 1 次」判定）。</summary>
        public int mistakeProtectionUsedYear;
        /// <summary>季度初现金快照（万美元，判定「净亏损超现金 15%」用）。</summary>
        public float quarterStartCash;
        /// <summary>季度初董事会满意度快照（判定「季度降幅 > 20 点」用）。</summary>
        public float quarterStartSatisfaction;

        [Header("教程 / 词典 / 提示 / 消息 / 速度（GDB 28/29/30 · P3 体验收口）")]
        /// <summary>准点率连续低于 70% 的季度数（智能提示「准点率持续恶化」用）。</summary>
        public int onTimeLowQuarters;

        [Header("全球航空动态消息流（GDB 22/29 · P2 无痕）")]
        /// <summary>统一动态消息流（外部数据 + 程序生成事件混合，玩家无法分辨来源）。</summary>
        public System.Collections.Generic.List<AirlineCEO.Data.DynamicFeedItem> dynamicFeed = new System.Collections.Generic.List<AirlineCEO.Data.DynamicFeedItem>();
        /// <summary>连续盈利季度数（触发「太安逸」监管/反垄断类事件）。</summary>
        public int profitStreak;
        /// <summary>油价连续平淡季度数（触发「供应收紧」油价事件）。</summary>
        public int oilStableQuarters;
        /// <summary>连续无重大事件季度数（触发「制造商交付延迟」等事件）。</summary>
        public int quietQuarters;
        /// <summary>已映射到游戏时间轴的外部事件队列。</summary>
        public List<AirlineCEO.Data.ExternalEvent> externalEvents = new List<AirlineCEO.Data.ExternalEvent>();
        /// <summary>外部事件导致的机场起降冻结（IATA；空=全网）。</summary>
        public string externalFreezeRegion = "";
        public long externalFreezeUntilTicks;

        [Header("终局 · 软性失败与重大空难（GDB 4.2 · P0 遗留）")]
        /// <summary>安全评级连续维持 D 级的季度数（监管接管用）。</summary>
        public int safetyConsecutiveDQuarters;
        /// <summary>重大空难是否已发生（评级归零 → 立即 Game Over）。</summary>
        public bool majorCrashOccurred;
        /// <summary>最近一次失败类型（0 无 / 1 敌意收购 / 2 监管接管），UI 区分展示。</summary>
        public int lastDefeatKind;

        [Header("AOG 危机（GDD 7.2）")]
        /// <summary>是否已触发过脚本化 AOG（生命周期内仅一次）。</summary>
        public bool hasAOGTriggered;
        /// <summary>玩家最终选择：0 未选 / 1 调机备援 / 2 延误维修 / 3 仅咨询过顾问（最终会落为 1 或 2）。</summary>
        public int aogChoice;
        /// <summary>是否咨询过机务顾问（选项 3）。</summary>
        public bool aogAdvisorConsulted;
        /// <summary>准点率示意（延误维修会下降）。</summary>
        public float onTimePercent = 88f;

        [Header("人事 / 部门任命（GDD 第6章 · Phase 8）")]
        /// <summary>CEO 国籍，用于文化圈适配（效率公式）。</summary>
        public string ceoNationality = "中国";
        /// <summary>开局资料（Phase 1.4）：公司中文名 / 英文名 / CEO 显示名。</summary>
        public string companyNameZh = "天际航空";
        public string companyNameEn = "Skyline Air";
        public string ceoDisplayName = "CEO";
        public string ceoGender = "男";
        /// <summary>合同长度（财年）。</summary>
        public int ceoContractYears = 4;
        /// <summary>合同起始财年。</summary>
        public int ceoContractStartYear = 1;
        /// <summary>税后周薪（万美元，象征性小量级）。</summary>
        public float ceoWeeklyPayWan = 0.12f;
        /// <summary>年度奖金（万美元）。</summary>
        public float ceoBonusWan = 0.4f;
        public string companyIcao = "SKY";
        /// <summary>航司 IATA 二字码（如 CA / U2）。</summary>
        public string companyIata = "NV";
        public string hubIata = "PEK";
        /// <summary>除总部枢纽外已购买的运营基地（IATA）。</summary>
        public List<string> companyBases = new List<string>();

        [Header("接管开局（FM 式）")]
        public bool takeoverMode;
        public string takeoverAirlineId;
        public bool takeoverBriefingShown;
        public int crewVacancyPercent;
        public string ceoEducation = "";
        public string ceoCareer = "";
        public string ceoTrait = "";
        public List<LegacyIssue> legacyIssues = new List<LegacyIssue>();

        [Header("燃油对冲与融资（GDD 7.4 · Phase 9）")]
        public List<HedgeContract> hedgeContracts = new List<HedgeContract>();
        public List<Loan> activeLoans = new List<Loan>();
        /// <summary>近若干季油价，供财务面板简易走势。</summary>
        public List<float> fuelPriceHistory = new List<float>();
        /// <summary>Debug/危机用的临时月烧钱加成；跑道恢复后清零。</summary>
        public float financeCrisisBurnBonus;
        /// <summary>是否已看过燃油对冲新手引导（财务顾问弹窗）。</summary>
        public bool hasSeenHedgeTutorial;

        [Header("财务部（Phase 2 Week 5 · FIN）")]
        public List<BudgetItem> budgetItems = new List<BudgetItem>();
        public int budgetFiscalYear;
        public bool budgetSubmitted;
        public bool budgetApproved;
        /// <summary>累计排放（吨 CO2）· 已购配额（吨）· 碳价（美元/吨）。</summary>
        public float carbonEmissionsTons;
        public float carbonPurchasedQuotaTons;
        public float carbonPricePerTon = 85f;
        public RestructuringState restructuring = new RestructuringState();
        /// <summary>PDP 购机应付款（万美元）。</summary>
        public float pdpPayableWan;
        /// <summary>汇率对冲比例 0~80%。</summary>
        public float fxHedgeRatio;

        [Header("汇率（Phase 20）")]
        /// <summary>人民币兑美元汇率：1 美元 = N 元。上行 = 人民币贬值。</summary>
        public float usdCny = 7.10f;
        /// <summary>近若干季汇率，供宏观/财务面板走势。</summary>
        public List<float> usdCnyHistory = new List<float>();
        /// <summary>累计汇兑损益（万美元，正 = 累计收益）。</summary>
        public float cumulativeFxPnlWan;

        [Header("季末一次性项（K12）")]
        /// <summary>
        /// 上一季**实际**发生的季末一次性项（万美元，正 = 增现金）。
        /// 与 <see cref="EffectiveMonthlyBurn"/> 彻底分开：它不参与月烧钱、不参与跑道分母。
        /// 由 <c>TimeManager</c> 在结算时逐段取现金差实测得到。
        /// </summary>
        public OneOffTally lastOneOff = new OneOffTally();

        [Header("财务细分（GDB 第15章 · P2 长线运营）")]
        /// <summary>航线补贴记录列表（GDB 15.4）。</summary>
        public List<RouteSubsidy> routeSubsidies = new List<RouteSubsidy>();
        /// <summary>飞机采购订单簿（GDB 15.6 PDP）。</summary>
        public List<AircraftOrder> aircraftOrders = new List<AircraftOrder>();
        /// <summary>航空保险覆盖档位（GDB 15.11）。</summary>
        public InsuranceCoverage insuranceCoverage = InsuranceCoverage.Full;
        /// <summary>累计已付保费（万美元）。</summary>
        public float cumulativeInsurancePremiumWan;
        /// <summary>累计理赔收入（万美元）。</summary>
        public float cumulativeInsuranceClaimWan;
        /// <summary>燃油采购状态（GDB 15.12）。</summary>
        public FuelProcurementState fuelProcurement = new FuelProcurementState();
        /// <summary>碳合规状态（GDB 15.13 EU ETS + CORSIA）。</summary>
        public CarbonComplianceState carbonCompliance = new CarbonComplianceState();
        /// <summary>本财年已缴企业所得税（万美元，年结写入）。</summary>
        public float yearTaxPaidWan;
        /// <summary>累计已缴企业所得税（万美元）。</summary>
        public float cumulativeTaxPaidWan;

        [Header("市场销售部（Phase 2 Week 7 · MKT）")]
        /// <summary>定价 / 辅助收入 / FFP / 产品工坊 / 旺季策略 聚合状态。</summary>
        public MktState mkt = new MktState();

        [Header("货运业务（GDB 14.6 · P2 长线运营）")]
        /// <summary>货运独立核算状态（腹舱 / 纯货机 / KPI）。</summary>
        public CargoState cargo = new CargoState();

        [Header("战略发展部（Phase 2 Week 9 · STG）")]
        /// <summary>航权/时刻槽/联盟/投资并购/预案/五年规划 聚合状态。</summary>
        public StrategyState stg = new StrategyState();

        [Header("非航收入与临空商业（GDB 26.2 · P2 长线运营）")]
        /// <summary>枢纽临空商业生态（免税店/广告/贵宾厅/货运站）状态。</summary>
        public AirportCommerceState commerce = new AirportCommerceState();

        [Header("机务工程部（Phase 2 Week 9 · MNT）")]
        /// <summary>维修策略/OEM关系/航材库存/采购订单/定检/培训 聚合状态。</summary>
        public MaintenanceState mnt = new MaintenanceState();

        [Header("人力资源部（Phase 2 Week 10 · HR）")]
        /// <summary>招聘管道/培训/绩效薪酬/员工关系/工会谈判/合规 聚合状态。</summary>
        public HrState hr = new HrState();

        [Header("安全质量部（Phase 2 Week 10 · SAF）")]
        /// <summary>SMS/监察/事件调查/局方关系/安全文化/预算 聚合状态。</summary>
        public SafetyState saf = new SafetyState();

        [Header("跨部门决策流（Phase 3 Week 11 · CROSS）")]
        /// <summary>CEO 决策会议历史 + 补条件表单（n21/n104/n23 共用）。</summary>
        public DecisionState decision = new DecisionState();

        [Header("机队（Phase 10）")]
        public List<Aircraft> fleet = new List<Aircraft>();
        /// <summary>下一架注册号序号（B-1001 起，值为 1,2,3…）。</summary>
        public int nextRegistrationNumber = 1;

        [Header("AI 竞争对手（Phase 11）")]
        public List<AIAirline> aiAirlines = new List<AIAirline>();
        public float playerMarketShare;
        public float brandAwareness = 70f;
        public float codeShareLoadBonus;
        public bool codeShareActive;
        public string codeSharePartnerId;
        public int lastCodeShareYear;
        public string pendingCodeShareFromId;
        public string lastCompetitionLog;
        public List<float> playerShareHistory = new List<float>();
        public int nextAiSpawnYear;

        [Header("随机事件（Phase 12）")]
        public List<RandomEventLogEntry> randomEventLog = new List<RandomEventLogEntry>();
        public List<string> randomEventOneShots = new List<string>();
        public string pendingChainEventId;
        public int pendingChainQuarters;
        public string lastRandomEventSummary;
        /// <summary>连续未触发负面「红色危机」的季度数（坏运保护 / Pity Timer）。</summary>
        public int quartersSinceRedCrisis;
        /// <summary>各分类单财年触发计数（同分类限流）。</summary>
        public List<EventCategoryCounter> eventCategoryCounters = new List<EventCategoryCounter>();

        [Header("宏观经济（Phase 13）")]
        public float gdpGrowthRate = 3f;       // −10～+10%，每财年 Q1 更新
        public string economicPhase = "boom";  // "boom" / "recession" / "recovery"
        public int phaseDuration;              // 当前阶段持续季度数

        [Header("战略性扩张提案（GDD 7.7）")]
        /// <summary>提案流程是否已终结（接受或拒绝后为 true；暂缓不置 true）。</summary>
        public bool hasExpansionProposalTriggered;
        /// <summary>暂缓次数（最多 3）。</summary>
        public int expansionProposalDelayCount;
        public bool expansionAccepted;
        public bool expansionRejected;
        /// <summary>启动资金（万美元）。</summary>
        public float expansionProposalCashDeduction = 500f;
        /// <summary>「伟大航路」成就。</summary>
        public bool greatRouteUnlocked;
        /// <summary>本财年是否已处理过提案（暂缓后同年不再弹）。</summary>
        public int lastExpansionOfferYear;

        /// <summary>
        /// 所有开通航线的**月度**净现金 ＋ 机组战力质量乘数。
        ///
        /// <see cref="RouteData.dailyProfit"/> 是执飞当天的净额，故按班期折算执飞天数
        /// （<see cref="LiveClockLogic.OperatingDaysPerMonth"/>），不能一律 ×30：
        /// 2~3 班/周的亏损线一个月只飞 8~13 天，×30 会把计划口径压得比实收现金低两成。
        /// 乘数在未建立机组时取 HrEconomics.CrewUnstaffedMultiplier（0.85，严格劣于任何已建机组），
        /// 故开局有机队但无建模机组时航线利润打 85 折——这是刻意的：
        /// 若不折损，「永不招机组」就成了免费满利润的最优解，渠道系统形同虚设。
        ///
        /// 无执飞机的航线一律不进本口径：本属性仅是月度经营预测与跑道输入。
        /// 季度现金按实际落地结算，不使用此预测追补未执行航班。
        /// 判据与实时模拟、界面、地图共用 <see cref="FlightGroundLogic"/>，不再各处分叉。
        /// </summary>
        public float monthlyRevenue
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < activeRoutes.Count; i++)
                {
                    var r = activeRoutes[i];
                    if (r == null) continue;
                    // 持续性停飞（未指派 / 机号不在册 / 退役转售 / 停场 / 投入过渡业务）不计营收；
                    // 仅「该机正在执飞它班」的瞬时冲突不算停飞——它会在起飞窗口内自愈。
                    if (FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(this, r))) continue;
                    sum += r.dailyProfit * LiveClockLogic.OperatingDaysPerMonth(r.daysOfWeek, r.weeklyFlights);
                }
                return sum * CrewQualityMultiplier;
            }
        }

        public float FleetMonthlyLease
        {
            get
            {
                if (fleet == null) return 0f;
                float s = 0f;
                for (int i = 0; i < fleet.Count; i++)
                {
                    var a = fleet[i];
                    if (a == null || a.status == AircraftStatuses.Retired) continue;
                    s += UnityEngine.Mathf.Max(0f, a.monthlyLeaseWan);
                }
                return s;
            }
        }

        /// <summary>机队月维护费（万美元）。</summary>
        public float FleetMonthlyMaintenance =>
            FleetManager.Instance != null ? FleetManager.Instance.CalcMonthlyMaintenance(this) : 0f;

        /// <summary>
        /// 机队月均航空保险保费（万美元/月）= 季度保费 / 3。全险按季一次性扣现金
        /// （<c>FinanceManager.ChargeQuarterlyInsurance</c>），故跑道必须含它；
        /// 不进逐日计提，因为实际记账就不是逐日。
        /// </summary>
        public float FleetMonthlyInsurance =>
            AirlineCEO.Logic.InsuranceEconomics.QuarterlyPremiumWan(
                FleetManager.FleetTotalValue(this), safetyRating, insuranceCoverage) / 3f;

        /// <summary>过渡业务月收入估计（万美元/月）：在租合同的日收入 × 30。</summary>
        public float FleetMonthlyTransitionIncome =>
            AirlineCEO.Logic.TransitionBusinessLogic.MonthlyIncome(this);

        // ============================================================
        // 运行岗（渠道招聘）编制 / 薪酬 / 战力
        //
        // 这些岗位（飞行员、机务、乘务）不在 StaffEstablishment 编制席里，
        // 此前既无编制上限、薪酬也从不入账、战力也无人消费 —— 渠道选择因此毫无后果。
        // ============================================================

        /// <summary>在役机队规模（架）。</summary>
        public int ActiveFleetCount
        {
            get { GetFleetStaffing(out int n, out _, out _); return n; }
        }

        /// <summary>在役机队统计：架数 / 其中宽体数 / 总座位数（供运行岗编制推导）。</summary>
        public void GetFleetStaffing(out int activeFleet, out int widebody, out int totalSeats)
        {
            activeFleet = 0; widebody = 0; totalSeats = 0;
            if (fleet == null) return;
            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null || !a.isActive || a.status == AircraftStatuses.Retired) continue;
                activeFleet++;
                if (AirlineCEO.Logic.EconomyBalanceParams.IsWidebody(a.type)) widebody++;
                int seats = a.TotalSeats;
                totalSeats += seats > 0 ? seats
                    : AirlineCEO.Logic.EconomyBalanceParams.DefaultSeatsFor(a.type);
            }
        }

        /// <summary>
        /// 在职运行岗人员（按岗位关键字筛选，与 HrManager.DeptForPosition 同口径）。
        /// 只统计已到岗者（onMarket == false），不含介绍所候选人。
        /// </summary>
        public System.Collections.Generic.List<PersonnelData> OperationalStaff(string keyword1, string keyword2 = null)
        {
            var list = new System.Collections.Generic.List<PersonnelData>();
            if (staffPool == null) return list;
            for (int i = 0; i < staffPool.Count; i++)
            {
                var p = staffPool[i];
                if (p == null || p.onMarket) continue;
                string t = p.jobTitle ?? "";
                bool hit = t.Contains(keyword1) || (keyword2 != null && t.Contains(keyword2));
                if (hit) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// 是否渠道雇来的飞行序列运行岗（与 OperationalEstablishment 同类别，但不分配列表）。
        ///
        /// 两道防线缺一不可：编制席「飞行运行主管」也含「飞行」关键字，
        /// 若不剔除会把同一个人的战力重复计入 DeptStaffLift 与机组乘数两处，
        /// 并虚增飞行员在职数导致招募被误封。
        ///   ① isOperational：编制席恒为 false（薪酬在聚合口径内、战力已由 DeptStaffLift 消费）；
        ///   ② 职务名不在 StaffEstablishment 编制表内：防止 isOperational 被其它路径误置。
        /// </summary>
        static bool IsPilot(PersonnelData p) =>
            p.isOperational
            && AirlineCEO.Logic.HrEconomics.OperationalCategory(p.jobTitle)
                == AirlineCEO.Logic.HrEconomics.OpsCategory.Pilot
            && !AirlineCEO.Logic.StaffEstablishment.IsEstablishmentJobTitle(p.jobTitle);

        /// <summary>飞行员编制（应配人数）：按在役机队规模推导。</summary>
        public int PilotEstablishment
        {
            get
            {
                GetFleetStaffing(out int n, out int wide, out int seats);
                return AirlineCEO.Logic.HrEconomics.OperationalEstablishment("飞行员", n, wide, seats);
            }
        }

        /// <summary>在职飞行员人数（无分配，供 hot path 使用）。</summary>
        public int PilotFilled
        {
            get
            {
                if (staffPool == null) return 0;
                int c = 0;
                for (int i = 0; i < staffPool.Count; i++)
                {
                    var p = staffPool[i];
                    if (p == null || p.onMarket) continue;
                    if (IsPilot(p)) c++;
                }
                return c;
            }
        }

        /// <summary>
        /// 是否渠道雇来的**客舱乘务**运行岗（乘务员 / 客舱服务）。
        ///
        /// 与 <see cref="IsPilot"/> 同构、**同一道两道防线**（缺一不可）：
        ///   ① isOperational：编制席恒为 false（薪酬在聚合口径内）；
        ///   ② 职务名不在 StaffEstablishment 编制表内：防止 isOperational 被其它路径误置
        ///      而把「客舱运行主管」这类编制席算成乘务员，虚增在职数导致招募被误封。
        ///
        /// ★2026-10-14 第十三次问询：此前全公司**没有一处**读「在岗乘务员多少人」——
        /// 乘务只有编制公式、招聘渠道、稀缺度与证照，没有任何出口可供排班闸门消费。
        /// </summary>
        static bool IsCabinCrew(PersonnelData p) =>
            p.isOperational
            && AirlineCEO.Logic.HrEconomics.OperationalCategory(p.jobTitle)
                == AirlineCEO.Logic.HrEconomics.OpsCategory.CabinCrew
            && !AirlineCEO.Logic.StaffEstablishment.IsEstablishmentJobTitle(p.jobTitle);

        /// <summary>客舱乘务编制（应配人数）：按在役机队规模与总座位数推导（每 50 座 4 名 + 每架 4 名备份）。</summary>
        public int CabinEstablishment
        {
            get
            {
                GetFleetStaffing(out int n, out int wide, out int seats);
                return AirlineCEO.Logic.HrEconomics.OperationalEstablishment("乘务员", n, wide, seats);
            }
        }

        /// <summary>在职客舱乘务人数（无分配，供 hot path 使用）。</summary>
        public int CabinFilled
        {
            get
            {
                if (staffPool == null) return 0;
                int c = 0;
                for (int i = 0; i < staffPool.Count; i++)
                {
                    var p = staffPool[i];
                    if (p == null || p.onMarket) continue;
                    if (IsCabinCrew(p)) c++;
                }
                return c;
            }
        }

        /// <summary>
        /// 运行岗月度薪酬（万美元/月）：渠道雇来的运行序列人员实发年薪 ÷ 12。
        ///
        /// 这是**增量**而非替换：GameState.LaborCostAnnualWan（每机费率 × 在役机队，5 机基准 1200 万/年）
        /// 是既有聚合口径，
        /// 覆盖未被逐人建模的全体员工；运行岗是聚合之外新增的编制，故只能叠加。
        /// 接通后，猎头年薪（11.6 万）比校招（6.0 万）高 93% 才真正产生现金后果。
        ///
        /// 两类人不计入，以避开与聚合口径 LaborCostAnnualWan 的重叠
        /// （该聚合现已真实扣现金，故此处排除是**防双计**，不再是前置守卫）：
        ///   ① 内部转岗者（source==internal）—— 从既有编制席搬来，薪酬本已在聚合口径内；
        ///   ② 编制席职务名 —— 同理，其薪酬已在聚合口径内。
        ///
        /// ⚠ 勿与另一只桶混淆：此处排除的是「按人的**固定**年薪」；
        /// 而 RPMCalculator 的 crew(0.12 万/blockHour) 是「按航班的**可变**机组成本」（飞才有）。
        /// 二者互补，同计是正确成本结构，**不是**双计。
        /// （机组战力乘数仍会计入内转者，他们确实在飞，战力该生效。）
        /// </summary>
        public float OperationalPayrollMonthlyWan
        {
            get
            {
                if (staffPool == null) return 0f;
                float sum = 0f;
                for (int i = 0; i < staffPool.Count; i++)
                {
                    var p = staffPool[i];
                    if (p == null || p.onMarket) continue;
                    if (!p.isOperational) continue;      // 只计运行序列；编制席薪酬已在聚合口径内
                    if (AirlineCEO.Logic.StaffEstablishment.IsEstablishmentJobTitle(p.jobTitle)) continue;
                    // 内转者（从既有编制席调入）与接管局继承的机组，薪酬都已含在
                    // 「存量人力成本」聚合口径内（后者按 240 万/机/年覆盖 450 人规模的既有组织），
                    // 计入本桶即与聚合口径重叠。
                    string src = AirlineCEO.Logic.HrEconomics.NormalizeSource(p.source);
                    if (src == AirlineCEO.Logic.HrEconomics.SourceInternal
                        || src == AirlineCEO.Logic.HrEconomics.SourceInherited) continue;
                    if (p.baseSalary > 0f) sum += p.baseSalary;
                }
                return AirlineCEO.Logic.HrEconomics.AnnualToMonthlyWan(sum);
            }
        }

        /// <summary>
        /// 存量人力成本（万美元/年）：每机费率 × 在役机队 + 一次性增量。
        /// 机队扩张/退役自动缩放（不再是与规模脱节的固定值）。
        /// </summary>
        public float LaborCostAnnualWan =>
            AirlineCEO.Logic.HrEconomics.EffectiveAnnualLaborCostWan(
                ActiveFleetCount,
                hr != null ? hr.laborCostPerAircraftWan : 0f,
                hr != null ? hr.laborCompAdditiveWan : 0f);

        /// <summary>
        /// 全公司总人数（派生）：每机人数 × 在役机队 + 一次性增量。
        ///
        /// 与 <see cref="LaborCostAnnualWan"/> 同一套口径形状，故人数与人力成本随机队同步缩放。
        /// 此前读的是 <c>hr.totalHeadcount</c>（默认 450 的固定值），扩机队后人数不涨，
        /// 使培训 ROI（按人均预算）与工会谈判成本（按人头计费）在大小航司间失真。
        /// </summary>
        public int TotalHeadcount =>
            AirlineCEO.Logic.HrEconomics.EffectiveHeadcount(
                ActiveFleetCount,
                hr != null ? hr.headcountPerAircraft : 0f,
                hr != null ? hr.headcountAdditive : 0);

        /// <summary>
        /// 存量人力成本（万美元/月）：未逐人建模的全公司员工（由 <see cref="LaborCostAnnualWan"/> 折算）。
        ///
        /// 与 <see cref="OperationalPayrollMonthlyWan"/> 互补、不重叠——后者是渠道雇来的运行序列
        /// **增量**编制（已排除内转者与编制席）。接通后 HR 的「人力成本占营收（目标 &lt;25%）」
        /// 不再是影子指标：调薪 / 裁员决策会真实改变现金与跑道。
        /// </summary>
        public float LaborCostMonthlyWan =>
            AirlineCEO.Logic.HrEconomics.MonthlyLaborCostWan(LaborCostAnnualWan);

        // 机组战力乘数按游戏日缓存：monthlyRevenue 是 hot path（每次 UI 刷新都会走），
        // 而 staffPool 有数百人，逐次遍历会白白吃 GC 与 CPU。战力只随「日」变化
        // （EfficiencyAt 用 ticks 比较交接期截止），日粒度缓存不损失精度。
        //
        // 键必须取自 SimUtc 而非 wallYear/wallMonth/wallDay：三者为 0 时（新建 GameState、
        // 未走开局流程）SimUtc 会回退到现实 UTC，直接用字段会让键恒为 0 → 缓存永不失效。
        long _crewCacheDay = long.MinValue;
        // 初值必须与「未建机组」一致，不能是 1f：否则任何绕过重算的读取路径
        // 都会拿到免费满利润的中性值，重新开出 HrEconomics 里刚堵上的漏洞。
        float _crewCacheValue = AirlineCEO.Logic.HrEconomics.CrewUnstaffedMultiplier;

        /// <summary>
        /// 机组战力质量乘数（施加于航线日利润）。
        /// 未建立机组时为 CrewUnstaffedMultiplier（严格劣于任何已建机组），
        /// 让「招任何人」都优于「不招人」，同时各渠道的 efficiencyBonus 差真正影响经营。
        /// </summary>
        public float CrewQualityMultiplier
        {
            get
            {
                long day = SimUtc.Date.Ticks;
                if (day == _crewCacheDay) return _crewCacheValue;
                _crewCacheValue = ComputeCrewQualityMultiplier();
                _crewCacheDay = day;
                return _crewCacheValue;
            }
        }

        /// <summary>
        /// 重算机组战力乘数（招聘/离职/转岗后应调用，或等日切自动刷新）。
        /// 传入**编制**而非只传人数：乘数按覆盖率在「未建机组保底」与「质量乘数」之间插值，
        /// 见 HrEconomics.CrewQualityMultiplier(filled, establishment, avgEff)。
        /// </summary>
        public float ComputeCrewQualityMultiplier()
        {
            // staffPool 为空 / 无机长 → 走同一条权威公式的「未建机组」分支，
            // 不在这里另写常量，避免两处定义漂移。
            if (staffPool == null)
                return AirlineCEO.Logic.HrEconomics.CrewQualityMultiplier(0, 0, 0f);
            long now = SimUtc.Ticks;
            int n = 0;
            float sum = 0f;
            for (int i = 0; i < staffPool.Count; i++)
            {
                var p = staffPool[i];
                if (p == null || p.onMarket) continue;
                if (!IsPilot(p)) continue;
                n++;
                sum += p.EfficiencyAt(now);   // 含内部转岗交接期打折
            }
            if (n == 0)
                return AirlineCEO.Logic.HrEconomics.CrewQualityMultiplier(0, 0, 0f);
            return AirlineCEO.Logic.HrEconomics.CrewQualityMultiplier(n, PilotEstablishment, sum / n);
        }

        /// <summary>
        /// 使机组战力缓存失效。人员变动（录用 / 退回 / 转岗 / 解聘）后调用，
        /// 让新战力当天即生效，而不必等到日切才刷新。
        /// </summary>
        public void InvalidateCrewCache() => _crewCacheDay = long.MinValue;

        /// <summary>
        /// 国际运行资质人数：海外引进渠道到岗的运行序列人员（飞行员等）。
        /// 国际/地区航线许可的前置条件之一（民航局要求承运人具备国际运行经验机组），
        /// 这是「海外引进」渠道的专属生态位——其余渠道（校招/猎头/内转）不提供该资质。
        /// </summary>
        public int OverseasCrewCount
        {
            get
            {
                if (staffPool == null) return 0;
                int c = 0;
                for (int i = 0; i < staffPool.Count; i++)
                {
                    var p = staffPool[i];
                    if (p == null || p.onMarket || !p.isOperational) continue;
                    // 编制席职务名不算运行资质：他们已在既有编制口径内，
                    // 与 IsPilot / OperationalPayrollMonthlyWan 保持同一道不变量。
                    if (AirlineCEO.Logic.StaffEstablishment.IsEstablishmentJobTitle(p.jobTitle)) continue;
                    if (AirlineCEO.Logic.HrEconomics.NormalizeSource(p.source)
                        != AirlineCEO.Logic.HrEconomics.SourceOverseas) continue;
                    c++;
                }
                return c;
            }
        }

        /// <summary>过渡业务在租合同数（架）。</summary>
        public int TransitionLeasedCount =>
            AirlineCEO.Logic.TransitionBusinessLogic.ContractCount(this);

        /// <summary>
        /// 净月烧钱（开销 + 机队维护 + 租赁 + CEO + 运行岗薪酬 + 存量人力 + 月均保费 + 月均还本付息
        /// − 航线月收入 − 过渡业务月收入，下限 0 便于跑道计算）。
        ///
        /// 含还本付息是**唯一正确**的口径：贷款虽在季末一次扣，但它是合同强制的现金流出，
        /// 跑道衡量的就是「现金还能撑多久」。漏掉它会让局内读数比选司预览长 20~32%
        /// （★2 9.7 vs 8.0 / ★5 3.6 vs 3.0，2026-10-21 K11 修复）。
        /// 口径与选司预览的 <c>AirlineProfile.monthlyNetBurn</c> 逐项对齐，改一处必须同时改另一处。
        /// </summary>
        public float EffectiveMonthlyBurn
        {
            get
            {
                float ceoMonth = CeoMonthlyPayWan;
                float net = monthlyBurnRate + FleetMonthlyMaintenance + FleetMonthlyLease + ceoMonth
                    + OperationalPayrollMonthlyWan
                    + LaborCostMonthlyWan
                    + FleetMonthlyInsurance
                    + LoanManager.MonthlyDebtService(this)
                    + Mathf.Max(0f, financeCrisisBurnBonus) - monthlyRevenue - FleetMonthlyTransitionIncome;
                return net > 0f ? net : 0f;
            }
        }

        public readonly List<string> decisionLog = new List<string>();

        public event Action OnStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureDefaults();
        }

        /// <summary>
        /// 补齐懒建的容器 / 子状态对象，并把派生字段（安全指数、期初现金）对齐到当前值。
        /// Awake 会调用；<see cref="ResetForNewGame"/> 会在「临时模板实例」上补一次 ——
        /// 模板的 Awake 发现已有 Instance 会立刻早退，跑不到这段。
        /// 编辑模式下 Awake 不执行，隔离回归可手动调一次拿到完整状态。
        /// </summary>
        public void EnsureDefaults()
        {
            if (activeRoutes == null)
                activeRoutes = new List<RouteData>();
            if (currentTargets == null)
                currentTargets = new List<BoardTarget>();
            if (safetyEventLog == null)
                safetyEventLog = new List<SafetyEventLog>();
            if (hedgeContracts == null)
                hedgeContracts = new List<HedgeContract>();
            if (activeLoans == null)
                activeLoans = new List<Loan>();
            if (fuelPriceHistory == null)
                fuelPriceHistory = new List<float>();
            if (usdCnyHistory == null)
                usdCnyHistory = new List<float>();
            if (usdCny <= 0f)
                usdCny = AirlineCEO.Logic.FxEconomics.DefaultUsdCny;
            if (usdCnyHistory.Count == 0)
                usdCnyHistory.Add(usdCny);
            if (fuelPriceHistory.Count == 0)
                fuelPriceHistory.Add(fuelPricePerTon);
            if (budgetItems == null)
                budgetItems = new List<BudgetItem>();
            if (restructuring == null)
                restructuring = new RestructuringState();
            if (mkt == null)
                mkt = new MktState();
            if (cargo == null)
                cargo = new CargoState();
            if (routeSubsidies == null)
                routeSubsidies = new List<RouteSubsidy>();
            if (transitionContracts == null)
                transitionContracts = new List<TransitionContract>();
            if (transitionOffers == null)
                transitionOffers = new List<TransitionOffer>();
            if (aircraftOrders == null)
                aircraftOrders = new List<AircraftOrder>();
            if (fuelProcurement == null)
                fuelProcurement = new FuelProcurementState();
            if (carbonCompliance == null)
                carbonCompliance = new CarbonComplianceState();
            if (stg == null)
                stg = new StrategyState();
            if (commerce == null)
                commerce = new AirportCommerceState();
            if (mnt == null)
                mnt = new MaintenanceState();
            if (hr == null)
                hr = new HrState();
            if (saf == null)
                saf = new SafetyState();
            if (decision == null)
                decision = new DecisionState();
            if (fleet == null)
                fleet = new List<Aircraft>();
            if (nextRegistrationNumber < 1)
                nextRegistrationNumber = 1;
            if (aiAirlines == null)
                aiAirlines = new List<AIAirline>();
            if (randomEventLog == null)
                randomEventLog = new List<RandomEventLogEntry>();
            if (randomEventOneShots == null)
                randomEventOneShots = new List<string>();
            if (boardMemberIds == null)
                boardMemberIds = new List<string>();
            if (legacyIssues == null)
                legacyIssues = new List<LegacyIssue>();
            if (inbox == null)
                inbox = new List<InboxMail>();
            if (pendingInbox == null)
                pendingInbox = new List<PendingInboxMail>();
            SyncSafetyIndexFromRating();
            yearStartCash = cash;
        }

        /// <summary>
        /// 丢弃本局内存状态，回到「刚构造出来」的出厂值。
        ///
        /// 用在设置菜单的「退出到开始界面」：回到开始界面后，下一局（新游戏 / 从零创建）必须是干净的 ——
        /// 否则它会继承上一局的财年、机队、航线、董事会与收件箱。读档不受影响：读档是往一个
        /// **出厂状态**里灌存档值，与「进程重启后读档」完全同一条路径。
        ///
        /// 刻意**不手写字段清单**：类里 100 多个状态字段还在长，手写清单一定漂移
        /// （清档键表硬编码就漏过 19 个键，见 <c>ClearGameSaveMenu</c> 的注释）。
        /// 这里新建一个临时实例当模板，用反射把它的字段值抄回来：字段初值即出厂值，新增字段自动覆盖。
        ///
        /// 两个例外：
        /// <list type="bullet">
        /// <item>事件 / 委托字段是**会话内的订阅关系**（<see cref="OnStateChanged"/> 上有 HUD 订阅），
        /// 抄掉就等于让刷新逻辑静默失效，故跳过；</item>
        /// <item>集合沿用原有实例（先 Clear 再填），避免别处缓存的 <c>List</c> 引用突然指向一片旧数据。</item>
        /// </list>
        /// </summary>
        public void ResetForNewGame()
        {
            // 已有 Instance，模板的 Awake 会立刻 Destroy 自己并早退（不抢 Instance、不进 DontDestroyOnLoad），
            // 字段初值在 AddComponent 时就已就位，这一帧内仍可当「出厂值模板」读。
            var templateGo = new GameObject("GameStateDefaultsTemplate");
            var template = templateGo.AddComponent<GameState>();
            template.EnsureDefaults(); // 补上 Awake 早退时跳过的懒建对象（mkt / cargo / stg …）

            var fields = typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;

                object current = field.GetValue(this);

                // 集合：沿用原实例，只把内容换成模板的（出厂状态一律是空表）。
                // 顺带覆盖 readonly 集合（decisionLog 就是），它们不能整字段赋值。
                if (current is IList currentList && !field.FieldType.IsArray)
                {
                    currentList.Clear();
                    if (field.GetValue(template) is IList freshList)
                        for (int k = 0; k < freshList.Count; k++)
                            currentList.Add(freshList[k]);
                    continue;
                }

                // readonly 标量没有赋值入口；它本来就是字段初值，重置时没必要（也无法）改写。
                if (field.IsInitOnly || field.IsLiteral) continue;

                field.SetValue(this, field.GetValue(template));
            }

            // 模板的 Awake 在播放态已经请求过销毁；编辑态（回归测试）不走 Awake，这里补一次。
            if (Application.isPlaying) Destroy(templateGo);
            else DestroyImmediate(templateGo);
            SyncSafetyIndexFromRating();
            yearStartCash = cash;
            NotifyChanged();
        }

        /// <summary>字母评级 → 数值指数（董事会安全目标）。</summary>
        public void SyncSafetyIndexFromRating()
        {
            safetyIndex = safetyRating switch
            {
                SafetyRating.A => 92f,
                SafetyRating.B => 82f,
                SafetyRating.C => 65f,
                SafetyRating.D => 45f,
                _ => 82f
            };
        }

        public void AddSafetyEventLog(SafetyEventLog entry)
        {
            if (safetyEventLog == null)
                safetyEventLog = new List<SafetyEventLog>();
            safetyEventLog.Add(entry);
            if (safetyEventLog.Count > 40)
                safetyEventLog.RemoveAt(0);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public string DateLabel => $"第 {year} 年 Q{quarter}";

        public int CeoAge
        {
            get
            {
                int y = wallYear > 0 ? wallYear : DateTime.UtcNow.Year;
                return Mathf.Max(18, y - ceoBirthYear);
            }
        }

        public int CeoContractEndYear => ceoContractStartYear + Mathf.Max(1, ceoContractYears) - 1;

        /// <summary>税后周薪折合月薪（万美元），计入季度支出与现金跑道。</summary>
        public float CeoMonthlyPayWan => Mathf.Max(0f, ceoWeeklyPayWan) * 52f / 12f;

        public int CeoContractYearsRemaining =>
            Mathf.Max(0, CeoContractEndYear - Mathf.Max(1, year) + 1);

        public void EnsureCeoContractDefaults()
        {
            if (string.IsNullOrEmpty(ceoGender)) ceoGender = "男";
            if (string.IsNullOrEmpty(ceoNationality)) ceoNationality = "中国";
            if (ceoContractYears <= 0) ceoContractYears = 4;
            if (ceoContractStartYear <= 0) ceoContractStartYear = year > 0 ? year : 1;
            if (ceoWeeklyPayWan <= 0f) ceoWeeklyPayWan = 0.12f;
            if (ceoBonusWan < 0f) ceoBonusWan = 0.4f;
        }

        /// <summary>董事会续约：新合同自 nextStartYear 起，长度仍为 ceoContractYears。</summary>
        public void RenewCeoContract(int nextStartYear)
        {
            EnsureCeoContractDefaults();
            ceoContractStartYear = Mathf.Max(1, nextStartYear);
        }

        /// <summary>每季游戏天数（用于连续时钟跨季结算）。</summary>
        public const int DaysPerQuarter = 90;

        /// <summary>本机时区相对 UTC 的偏移（含夏令时）。</summary>
        public static TimeSpan LocalUtcOffset => DateTimeOffset.Now.Offset;

        static readonly string[] WeekdayZh = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        /// <summary>游戏 UTC 瞬间（公历日期 + 时分）。</summary>
        public DateTime SimUtc
        {
            get
            {
                int y = wallYear > 0 ? wallYear : DateTime.UtcNow.Year;
                int mo = wallMonth >= 1 && wallMonth <= 12 ? wallMonth : DateTime.UtcNow.Month;
                int dim = DateTime.DaysInMonth(y, mo);
                int d = wallDay >= 1 && wallDay <= dim ? wallDay : 1;
                return new DateTime(y, mo, d, Mathf.Clamp(simHour, 0, 23), Mathf.Clamp(simMinute, 0, 59), 0, DateTimeKind.Utc);
            }
        }

        public void SetSimUtc(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            wallYear = utc.Year;
            wallMonth = utc.Month;
            wallDay = utc.Day;
            simHour = utc.Hour;
            simMinute = utc.Minute;
        }

        /// <summary>游戏 UTC 时刻，如 09:05。</summary>
        public string GameUtcClockLabel => $"{simHour:00}:{simMinute:00}";

        /// <summary>游戏本地时刻 HH:mm。</summary>
        public string GameLocalClockLabel
        {
            get
            {
                var local = SimUtc + LocalUtcOffset;
                return $"{local.Hour:00}:{local.Minute:00}";
            }
        }

        /// <summary>顶栏本地：XXXX年XX月XX日  星期几  00:00</summary>
        public string GameLocalDateTimeLabel
        {
            get
            {
                var local = SimUtc + LocalUtcOffset;
                return $"{local.Year}年{local.Month:00}月{local.Day:00}日  {WeekdayZh[(int)local.DayOfWeek]}  {local.Hour:00}:{local.Minute:00}";
            }
        }

        /// <summary>
        /// 检查游戏时钟与现实 UTC 是否一致；不一致则对齐到现实 UTC（日期+时/分）。
        /// 财年 / 季度不改。返回是否做过校正。
        /// </summary>
        public bool AlignClockToRealWorld()
        {
            var utc = DateTime.UtcNow;
            utc = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
            if (SimUtc == utc) return false;
            SetSimUtc(utc);
            return true;
        }

        /// <summary>顶栏用完整本地日期时间。</summary>
        public string DateTimeLabel => GameLocalDateTimeLabel;

        public void NotifyChanged() => OnStateChanged?.Invoke();

        public void AddCash(float amount)
        {
            cash += amount;
            NotifyChanged();
        }

        public void AddDecisionLog(string entry)
        {
            decisionLog.Add($"[{DateLabel}] {entry}");
            if (decisionLog.Count > 50)
                decisionLog.RemoveAt(0);
        }

        public bool HasRoute(string origin, string destination)
        {
            string o = origin?.Trim() ?? "";
            string d = destination?.Trim() ?? "";
            for (int i = 0; i < activeRoutes.Count; i++)
            {
                if (string.Equals(activeRoutes[i].origin, o, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(activeRoutes[i].destination, d, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void SetFromSave(float cashValue, int yearValue, int quarterValue, float satisfaction, bool firstFlight)
        {
            cash = cashValue;
            year = yearValue;
            quarter = quarterValue;
            boardSatisfaction = satisfaction;
            firstFlightCompleted = firstFlight;
            NotifyChanged();
        }
    }
}
