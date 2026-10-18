# Airline CEO Simulator · 后台开发任务清单

基于 UI 交互白板（103 页 + 334 条导航连线）+ GDD v1.0 系统设计

> 按**系统模块 → 数据实体 → 业务逻辑 → API 接口**分解


## 一、核心基础设施层（贯穿全游戏）


### A. GameState 全局状态机
> 优先级：P0 · 无此系统无法启动


- 管理游戏生命周期：主菜单 → 建档 → 经营 → 暂停 → 年终结算 → 终局
- 状态切换事件广播（事件总线模式）
- 全局时间锚点：Year/Quarter/Month/Week/Day/Minute 五级时钟
- 全局计数器：财年数、总飞行小时、累计旅客数



### A. TimeManager 时间与速度系统
> 优先级：P0


- 支持 1×/2×/4×/暂停 四种速率
- 每 Tick（1分钟内）批量推进：航班位置、油耗、维修剩余、事件冷却
- 暂停态：冻结所有推进逻辑，允许玩家操作 UI（任命/查看数据/配置）
- 8× 速仅用于空档期「快进到下一事件」



### A. SaveManager 存档引擎
> 优先级：P0


- F5 快存 / F9 快读 / 自动存档槽（3个循环 + 1个临时）
- 存档格式：完整 GameState JSON + 增量差分 + 二进制快照
- 存档元数据：日期、现金、旅客数、游戏时长、屏幕截图
- 新游戏+（NGP）继承：幕僚经验、部分目标进度、解锁内容
- Cloud Save 预留接口



### A. EventBus 事件总线 / Socket
> 优先级：P0


- 跨系统的 Pub/Sub 消息通道
- 例：油价更新 → FIN 对冲结算 + MKT 动态定价 + OPS 燃油计算
- 支持消息优先级、延迟发送（如「3天后董事会收到报告」）
- 事件日志记录（用于 History Replay 系统）



## 二、建档与公司基础数据


### B. PlayerProfile 玩家档案
> 优先级：P0 · n2 UI


- 姓名、性别、年龄、国籍、CEO 形象
- 关联私人幕僚 AO（小航）→ 初始经验/偏好
- 游戏偏好：语言、货币显示、速度默认值、色盲模式



### B. CompanyProfile 公司数据
> 优先级：P0 · n3 UI


- 航司名称（中/英）、ICAO 三字码、LOGO、品牌色、涂装
- 总部基地（IATA 机场代码）、本位币
- 商业模式：全服务航空/低成本/支线/包机 → 影响后续经济参数
- 初始资金、股东结构 → 映射首届董事会权重



### B. DifficultyConfig 难度配置
> 优先级：P0 · n3 UI


- 三个预设 + 自定义滑条（资金/竞争强度/事件频率/燃油波动）
- 影响：初始资金、目标难度、AI 竞对侵略性
- 写入 GameState 全局引用



## 三、收件箱与 AO 助手


### C. InboxManager 收件箱引擎
> 优先级：P0 · n7 UI


- 邮件实体：subject、body、attachments（数据表/图表/PDP预览）、优先级
- 发件人：某部门负责人 / AO / 董事会 / 外部系统自动生成
- 操作按钮：批准 / 发起会议 / 直接确认 / 暂缓 / 撤销 / 拒绝
- 7日截止规则：待办事件自收件日起第7日到期 → 强制跳转该邮件 → 须操作后方可继续
- 已读/未读状态管理、按日期/部门/类型筛选
- 邮件列表分页（保留最近200条）



### C. AOAgent 航空事务官 (AO)
> 优先级：P1 · n8 UI


- 对话系统：ADVISOR / WARNING / CRISIS 三种模式
- 信息推敲：根据当前 GameState + 各系统指标，生成一句话建议
- 航线收益分析智能洞察（→ n44 UI）
- 旺季策略最佳方案推荐（→ n55 UI）  
- 招聘策略智能推荐（→ n99 UI）
- AO 经验积累与能力成长（分析准确率提升）



### C. TermDictionary 术语词典
> 优先级：P2 · n0 UI


- 每遇到 gdd 标记的专业术语 → 虚线下划线 → hover 弹出 Tooltip
- 术语实体：英文名、中文名、定义、关联部门、参见其他术语
- 支持全文检索 + 按部首拼音索引



## 四、董事会系统 (Ch5-9)


### D. BoardSystem 董事会核心
> 优先级：P0 · n20 UI


- 9人董事会：席位管理、任期倒计时、派系分布饼图
- 人格属性：17位董事候选池，每人含头像、派系标签、偏好的投票倾向
- 权重计算：(角色固有权重 + 专长加成 + 化学反应影响) → 投票时的力度
- 年度换届流程：续聘成功/解雇结局分支



### D. ChemistryEngine 化学反应引擎
> 优先级：P0 · Ch7


- B-01~B-23 共23种化学反应规则集
- 匹配逻辑：遍历所有董事/部门负责人对 → 检测触发条件 → 计算影响值
- 影响输出：董事会满意度 +/-、投票倾向偏移、部门效率系数
- 可随时间衰减或增强



### D. ObjectiveSystem CEO目标系统
> 优先级：P0 · Ch8 · n20 UI


- T-01~T-23 共23个目标池
- 四类：长期战略目标、短期经营目标、持续性目标、事件触发目标
- 目标状态：待分配 / 进行中 / 已完成 / 失败 / 放弃
- 每财年初生成2-5个目标（从池中按条件抽取）
- 实时进度追踪 → 年度考核里程碑（n20 UI 目标追踪器）



### D. YearEndEvaluator 年度考核引擎
> 优先级：P0 · Ch9 · n26 UI


- 董事会满意度综合评分（目标完成度 + 财务表现 + 安全记录 + 增长指标）
- S/A/B/C/D 五级评级
- 结果分支：续聘—设定新目标 / 解雇—游戏结束或重开
- 遗产计算：退任后解锁的内容、NGP 继承值



## 五、管理团队系统 (Ch10-12)


### E. DepartmentManager 部门负责人管理
> 优先级：P0 · n23 UI / n18 人力部


- 70位候选人池（7部门 × 10人）→ 人物属性表
- 任命/解雇/续聘 操作 → 影响部门效率系数
- 副手任命：副手提供「专长加成」例：极地运营专长 → 降低该部门极地航线燃油成本5%
- 部门 KPI 仪表（每个部门4-5项 KPI）
- 管理团队化学反应 M-01~M-07



### E. DepartmentKPI 部门 KPI 联动
> 优先级：P0 · Ch12


- 每个部门的 KPI 实时从 GameState 计算
- KPI 变化 → 部门负责人效率 → 该部门相关系统的输出修正
- 例：运营部准点率 KPI → 航班环构建器 + 延误赔偿模型 + 旅客满意度



## 六、七部门核心业务系统


### F. OPS 运营部 (n13) · 航班环构建器 + IRROPS
> 优先级：P0 · n13 / n36 / n42 / n43 / n66


- FlightRingBuilder：航班环数据实体（环编号、覆盖 OD 列表、每日序列、过站序列）
- 航班环优化算法：AI 方案选型 vs 手动连班
- 航季总纲：夏秋/冬春航季框架 → 航班周总计划生成
- 航班详细计划：OD、航班号、机型、班次、班期、时刻、机组摘要
- 冲突检测：宵禁/噪音/CAT III/机型适航/FTL 时间限制
- DelayCalculator：延误传播模型 × 赔偿计算
- IRROPSManager：危机决策（快速修补/彻底修复/调机换机）



### F. MKT 市场销售部 (n14) · 收益管理 + 竞争雷达
> 优先级：P0 · n14 / n44 / n49 / n55 / n60 / n48


- RouteProfitAnalyzer：航线收益分析/RASK/CASK/盈亏平衡/日利润
- DynamicPricingEngine：AI 调价算法 × 竞对票价监测 × 多等级舱位
- FareClassSystem：头等/商务/高端经济/经济 仓位、票价、折扣率、座位余量
- AncillaryRevenue：行李费/选座费/退改签/机上销售/联名卡 收入模型
- CompetitiveRadar：7维度雷达图（航线网络/定价收益/产品服务/运营成本/辅助收入/外部环境/战略资本）
- SeasonalCalendar：50+ 节假日 + 需求曲线 + 旺季策略包
- FreightCalculator：货运独立核算模型
- 品牌净值：品牌信誉度 × 事故/服务事件影响



### F. FIN 财务部 (n15) · 现金流 + 对冲 + 碳配额
> 优先级：P0 · n15 / n22 / n25 / n31 / n32 / n103 / n106


- CashFlowEngine：现金入账/出账流水、现金跑道计算（月数）、现金跑道红黄绿灯
- TreasuryManager：银行存款余额、贷款利率联动信用评级、紧急融资窗口
- HedgingEngine：燃油/汇率对冲（比例0-80%、期限3/6/12月、盈亏结算）
- CarbonQuotaEngine：EU ETS + CORSIA + 中国碳市场 三重机制
  碳排放量 = 航班耗油量 × 排放因子 (3.15t CO₂/t fuel)
  碳成本 = 航线排放量 × 当前碳价
- BudgetSystem：年度预算编制 → 各部门限额 → 使用率进度条 → 追加申请
- TaxEngine：不同国家的增值税/所得税/起降税计算
- InsuranceManager：航空保险管理（机身险/第三者责任险/战争险）
- FuelManager：燃油价格数据源（Jet A1）、区域溢价系数、自动采购预警
- PDPManager：飞机预付款（Pre-Delivery Payment）应付节奏



### F. MNT 机务工程部 (n17) · 维修排程 + 航材供应链
> 优先级：P0 · n17 / n81 / n82 / n83 / n84 / n85 / n105


- MaintenanceScheduler：A检(1-2d $5-15万)/B检(3-7d $20-50万)/C检(1-2w $100-500万)/D检(3-8w $500-5000万) 定检排程
- AOGManager：AOG 应急状态（快速修补/彻底修复/调机换机）、AOG 平均恢复时间
- PartsSupplyChain：ABC分类航材库存、安全库存线、再订购点、断供预警
- SupplierManager：OEM 关系等级（普通→重要→战略伙伴→启动客户）→ 交付/折扣/响应
- LeasedReturnManager：退租检/客改货 → 退租成本 $500-2000万
- EngineManager：发动机/起落架大修周期 + 不可预见费 +10-15%
- MaintenanceBudget：维修预算来源（年度预算/追加/应急储备金/延期风控）



### F. HR 人力资源部 (n18) · 招聘 + 培训 + 工会
> 优先级：P0 · n18 / n86 / n87 / n88 / n89 / n90 / n91 / n37 / n99


- StaffManager：飞行员/乘务/机务/签派/地服/行政 人员池 → 人数/资质/满意度/疲劳度
- RecruitmentPipeline：候选人漏斗（简历筛选→面试→背调→体检→offer→到岗）
- 全球人才紧缺指数：飞行员/机务/乘务 分岗位动态紧缺度 → 招聘成本±10-25%
- TrainingManager：自建培训中心容量/CAAC-FAA-EASA资质/机型改装/复训周期
- PerformanceRating：S/A/B/C/D 五档强制分布 → 每部门评级分布
- SalaryBenchmark：薪酬市场分位P25/P50/P75 → 人力成本占营收比目标<25%
- UnionNegotiation：工会满意度/罢工倒计时/CBA 到期提醒 → 让步/强硬/拖延三姿态
- FatigueManager：飞行时间限制 FTL/FDTL、机组疲劳指数



### F. SAF 安全质量部 (n19) · 安全评级 + 事故调查
> 优先级：P0 · n19 / n92 / n93 / n94 / n95 / n96 / n100 / n101 / n30


- SafetyRatingEngine：0-100动态分 (A≥90/B70-89/C50-69/D<50) → D级触发局方接管 → 特大空难Game Over
- 影响因子：事故等级/审计结果/局方评价/安全投资/事件频率/碳配额合规
- AccidentInvestigation：事故征候/一般/重大/特大 四等级 → 快速/标准/深度 三档调查
- AuditManager：年度审计计划 → 飞行/机务/客舱/地服 各部门审计进度
- FindingTracker：不符合项（观察项/一般/重大）→ 整改关闭率 → 重复问题标记
- QARAnalyzer：飞行品质监控 → 超限事件统计 → 各机型/机场/飞行员不安全事件比率
- RegulatoryManager：CAAC/FAA/EASA 关系状态/检查日历/处罚-申诉决策/运行许可认证
- SafetyCultureScale：惩罚型 ↔ 鼓励报告型 滑动切换 → 安全文化成熟度 SCMM
- ConsequenceQuantifier：品牌损失(-5~-70)/保险赔付/诉讼赔偿/监管禁飞时长/总经济损失



### F. STG 战略发展部 (n16) · 航权 + 联盟 + 并购
> 优先级：P0 · n16 / n76 / n77 / n78 / n79 / n80 / n35 / n102


- FleetPlanner：五年机队规划 → 选型策略（宽体/窄体/支线）→ OEM 关系红利
- TrafficRightsManager：航权申请/转让/竞标 → 地缘政治风险 × 执照覆盖率
- SlotManager：核心机场起降时刻槽交易/租借 → 稀缺度 × 溢价
- AllianceSystem：星空/天合/寰宇 三大联盟对比雷达图 → 入盟进度/退出代价
- CodeShareManager：代码共享合作伙伴列表/共享收入分成比例/航线互补度
- JointVentureManager：联营航线组/收入成本分摊/反垄断合规审查
- MAATargetRadar：并购目标筛选（机队规模/航线互补度/财务健康/估值）
- StrategicInvestment：战略投资组合 → 政府关系点数 → 补助金争取
- GeopoliticalRisk：飞越高风险国家航路费+20%/航权谈判难度升级 → 联动 SAF



## 七、外部生态引擎


### G. MacroEconomyEngine 宏观经济引擎
> 优先级：P1 · Ch20 · n28 UI


- 四阶段循环：繁荣 → 衰退 → 萧条 → 恢复
- 核心变量：GDP 增速、油价 (Jet A1)、汇率、利率
- 外部数据源对接（可选实时 API 或模拟生成器）
- 传导机制：GDP → 需求弹性 / 油价 → 燃油成本 / 汇率 → 美元债务 / 利率 → 融资成本
- 预测模式（离线/弱网）：基于30天历史数据的线性回归预测
- 扩张指示器 UI（n28 宏观经济面板）



### G. AICompetitorSystem AI 竞争对手
> 优先级：P1 · Ch21 · n60 UI


- 竞对实体：名称/类型/总部/机队/航线网络/财务状况/策略标签
- AI 状态机：Expanding(扩张) / Consolidating(巩固) / Aggressive(激进) / Defensive(防守)
- 21.4 市场份额动态计算：对飞航线压迫机制（周班次 < 竞对60% → 品牌认知度每季 -2%）
- 竞合关系：代码共享 / 联营 JV / 竞争 / 敌对
- 竞对历史动作时间轴、竞对财务摘要
- AI 决策周期：每季度进行一次战略评估+行动



### G. RandomEventEngine 随机事件引擎
> 优先级：P1 · Ch22 · n30 UI


- 事件池结构 (Event Pool Schema)：ID/类型/权重/前置条件/后果/冷却时间
- 四级事件：Minor(轻微)/Moderate(中度)/Severe(严重)/Catastrophic(灾难)
- 事件连锁反应 (Event Chain Reaction)：A事件触发 → B事件概率 ↑
- 防骚扰机制：同类事件最小间隔、每季度事件上限
- 部门联动接口：每个事件定义受影响系统和影响参数
- 实时数据接入：极端天气/火山灰 → 区域航线准点率 -20~-50%



### G. SeasonalCalendarEngine 季节性需求引擎
> 优先级：P1 · Ch23 · n55 UI


- 50+ 全球节假日矩阵（春节/清明/五一/端午/暑运/中秋/国庆/圣诞/元旦 + 全球区域节日）
- 各节日需求曲线：节前→节中→节后需求系数 (0.5-3.0)
- 旺季策略包预设：定价/运力/促销/人力调配 → 自动在节日前执行
- 与航线收益模型联动：需求系数 × 基本客流 → 预期收入
- 与人力联动：旺季临时增员/加班成本



## 八、横切系统与关键模型


### H. RPM RouteProfitabilityModel 航线盈利能力评估
> 优先级：P0 · Ch25 · n35 UI


- 核心公式：
  航线综合评分 = [ (预估客流 × 季节性系数) × 票价弹性 ] 
  - [ 航段成本（燃油+起降+航路费） + 碳配额成本 ] 
  - [ 航权获取难度 × 战略部效率 ] 
  + 宏观经济景气系数 
  - 竞对同航线份额压迫系数
- 变量输入：班次、机型、时刻 → 实时重算评分
- 可视化：ROI 区间分布图、盈亏平衡点、敏感性分析
- AO 生成一句话洞察



### H. CrossDeptLinkage 跨部门联动矩阵
> 优先级：P1 · Ch24


- 维持 7×7 联动关系矩阵
- 每个联动：源系统 → 目标系统、数据流向、影响权重
- 例：维修窗口占用 → 运营部航班环缺口；安全事故 → 市场部品牌净值波动
- 变更检测：数据变化超过阈值 → 通知相关 UI 更新



### H. BrandSystem 品牌信誉度 + FFP
> 优先级：P1 · Ch26 · n33 / n72 / n75


- 品牌信誉度 (0-100)：事故/准点率/服务评分/媒体报道 → 动态修正
- FFP 常旅客计划：会员分级/里程积累/兑换/联名卡/活跃度
- 品牌修复计划 → n75 UI
- 品牌危机公关 → n72 UI



### H. NonAeroRevenue 非航收入模型
> 优先级：P2 · n48 UI


- 机上销售（餐饮/免税品）
- 行务费：行李费/选座费/退改签手续费
- 联名卡分润/保险佣金/租车酒店佣金
- 地面商业：贵宾室/广告/停车场



### H. HistoryReplay 历史回放
> 优先级：P2 · n38 UI


- 关键决策节点时间轴
- 分支系统：「如果当时…」What-If 节点快照
- 只读/不修改存档
- 选择分支后 → 沙盒模拟（不污染主存档）



## 九、决策与工作流系统


### I. CEODecisionMeeting 决策会议引擎
> 优先级：P0 · n21 / n97 UI


- 五部门意见卡模型：财务/运营/市场/人力/安全（🟢绿=推荐 / 🟡黄=有条件 / 🔴红=反对）
- 每张意见卡：颜色圆点 + 量化指标 + 一句话建议
- 审批流程：批准 → 生效并关闭 / 暂缓 → L5补条件表单 / 拒绝 → 标记搁置
- 首航 AO 快闪简化会议 → 其后重大投资强制完整会议
- 购机决策会议专用（n97）：五部门意见 + PDP/交付窗口/OEM折扣



### I. AircraftPurchaseEngine 购机决策引擎
> 优先级：P0 · n97 UI


- 机型数据库：目录价、座位数、航程、燃油效率、OEM关系折扣率
- 四种采购方式：自有买断/融资租赁/经营租赁/二手引进
- 每种方式：首付款、月供/租金、折旧策略、残值预估
- 硬约束校验：预算超限/机型不适航/无可用时刻槽/无适配机组资质
- PDP 预付节点对接财务部



### I. RecruitmentEngine 招聘决策引擎
> 优先级：P1 · n99 UI


- 四种策略比选：批量校招/猎头挖角/海外引进/内部转岗
- 成本-周期-成功率三轴对比（联动全球人才紧缺指数）
- 背调深度选择：基础背调(标准)/深度背调(高管/安全敏感岗)
- 入职培训衔接：培训中心容量/该岗位培训周期/排期冲突



### I. RegulatoryInspectionEngine 局方检查应对
> 优先级：P1 · n100 UI


- 检查实体：类型/日期/范围/团队规模/持续天数
- 预检自查仪表：完成率/已通过项/待改进项/红色高风险项
- 三策比选：全力配合/重点整改后迎检/申请延期
- 各策略对安全评级影响预估 (±N分)
- 确认后：按部门生成整改检查单 → 分发收件箱



### I. NegotiationEngine 谈判引擎
> 优先级：P1 · n102 UI


- 6条核心条款：分成比例/合作范围/协议期限/排他条款/终止条款/品牌联名
- 三姿态博弈：强硬(风险高/条款优)/折中(AO推荐)/让步(成功率高/条款劣)
- 对方反馈模型：接受/还价/拒绝 → 满意度变化
- 最多3轮（超出自破）
- 反垄断红线自动检测（市场份额超限→合规警告）



### I. BankruptcyRestructuringEngine 破产重组引擎
> 优先级：P1 · n25 / n103 / n106 UI


- 10条强制重组条件 → 四类：资产处置/人员调整/治理合规/财务约束
- 每条条件右侧：影响量化/关键董事立场/替代方案链接
- 重组方案汇总仪表：总回笼资金/剩余运力/跑道延长预期/重组成功概率
- 重组后2年内盈利 → 恢复；2年未盈利 → 清算 Game Over
- 紧急融资/条款确认（n103）：5种融资方式完整条款 + 法务审查



## 十、数据与基础设施


### J. GameDatabase 游戏数据库设计
> 优先级：P0 · 全系统


- 核心实体 ER 图：
  Player ← Company ← Fleet(Aircraft) ← Routes ← Flights
  BoardMembers(9) ← Chemistry ← Objectives(23)
  DepartmentManagers(7) ← StaffPool(各岗位) ← Training/Performance
  Competitors(N) ← MarketData ← PriceMonitoring
  InboxItems(200) ← Attachments
  Events(池) ← EventLog(历史)
- 每个实体：主键 (GUID)、时间戳、版本号、存档槽隔离
- 数据持久化层：Save Load 快照 → JSON/二进制 + SQLite 本地缓存



### J. TickExecutionPipeline Tick 执行管线
> 优先级：P0 · Ch2 核心循环


- 每个 Tick(1分钟) 内执行顺序：
  1. TimeManager 推进时间
  2. MacroEconomyEngine 更新宏观变量
  3. FuelManager 采购/消耗
  4. OPS FlightScheduler 推进飞机位置
  5. MNT MaintenanceClock 累计飞行小时/循环
  6. HR FatigueManager 累计机组飞行时间
  7. RevenueCalculator 计算旅客收入
  8. CashFlowEngine 结算现金流
  9. RandomEventEngine 检测事件触发
  10. EventBus 广播所有变更
  11. UI 批量刷新（限帧率 60fps）



### J. ServerAPI 后端 API 接口
> 优先级：P1 · Ch32


- RESTful 接口分模块：
  GET  /game/state          - 当前 GameState 快照
  POST /game/speed          - 设置速度 ×/暂停
  POST /game/save           - 存档
  GET  /ops/flights         - 航班列表 + 状态
  POST /ops/route/analyze   - RPM 航线分析
  POST /board/vote          - 董事会投票
  GET  /inbox               - 收件箱列表
  POST /inbox/:id/action    - 邮件操作
  POST /fleet/purchase      - 购机决策
  GET  /fin/cashflow        - 现金流报表
  GET  /competitors         - 竞对列表
  GET  /events/log          - 事件日志
- 所有接口返回 JSON + ETag + Cache-Control
- WebSocket 推送：实时数据变更（Tick完成事件）

