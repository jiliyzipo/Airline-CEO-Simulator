# 系统架构与回归边界 · 2026-10-17

## 技术与状态权威

Unity 6000.3.21f1 / C#；uGUI、TMP 和运行时面板工厂；Resources/UIConfig 配置页标识及导航。DataBase/ScriptableObject 提供目录，GameState 保存当前局飞机、航线、班次、机组、合同、申请和经营指标。UI 不应复制业务状态或自行推断“已生效”。

MVPBootstrap 确保管理器和 UI 启动，SetupFlowController 完成新建/接管/读档并开放 PrefabNavIntegration 的 GameplayReady。默认构建场景已指向 MVP V1.0，SampleScene 仅是模板遗留。独立 Player 构建未作为本阶段已验证产物。

## 指令至执飞

1. CapacityDirectiveLogic 区分询问与明确指令，OpsQueryLogic 只回答查询；歧义与不识别的交互由 UI/神经匹配入口处理。
2. 运力执行形成事实呈报，InboxManager 保存原目标、指令前班次和缺口；重复申请与被取代呈报有独立状态。
3. CEO 批准后，申请进入 effectPending。机场/具体 HH:mm/航线/目标波是关联依据，不把一个需求波误当一条槽。
4. ProcessPendingEffects 按 year/quarter 到期交割；RoutePermitLogic 计算起点、终点及经停往返各站需求。改时失败保留旧持仓。
5. 自动重排评估飞机、OpsRotationLogic 的占机/值勤和 CrewCapacityLogic 的驾驶舱/客舱约束；生成加机待批件后仍需 CEO 批复。
6. LiveClockSimulator 处理起飞、落地、机损、槽使用率与实际收入；FlightGroundLogic 是执行、列表、详情和地图共用的停飞判据。无机、退役、维护和过渡占机不得生成收入。
7. 回执按实际班次与目标分为未变动/部分生效/已生效，不把授权和办理写成执行完成。

## 时间与账务

TimeManager.AdvanceGameMinutes 是时钟入口；按至多 1440 分钟和季度边界切片，先运行 LiveClockSimulator、外部事件、招聘、人力和投递，再关闭季度。自然走时和开发 AdvanceQuarter 共用同一条链，后者只加速模拟剩余时间。

航班落地入 liveQuarterIncome；本地跨日扣维护、租赁、管理开销、CEO 工资、增量运行人员工资、存量人力及危机加成，入 liveQuarterExpense。过渡合同收入入 liveQuarterTransitionIncome。季报读取已记数额，重置分桶，只为联盟等独立季度项目再记账。贷款现金扣本息，利润只扣利息。货运、非航、税务、对冲、汇兑、保险及订单由各管理器在既定季度/年度事件结算，不能再次混入航班预测补差。

monthlyRevenue 是经营预测与跑道输入，不是现金日记账；dailyProfit 已是扣航班变动成本后的贡献，不能再当票务毛收入。部门预算分摊仍采用预测，不等同于逐日现金账。数值单位主要为万美元（字段保留 Wan 后缀）；float 有精度限制，回归对现金采用明确容差。

## 存档

SaveManager 用 PlayerPrefs 键和 JSON 保存状态；时间、实时季度分桶、机队、航线内 flights、pendingInbox 和已批准待执行邮件都需连贯恢复。旧档缺失集合/字段由各 Ensure 与默认值归一；本轮没有更换键或数据结构。申请待批、待交割、待加机批复、空中四个节点已纳入集成测试。

隔离导出仅覆盖导出副本的 companyName/productName，改变 PlayerPrefs 与 persistentDataPath 命名空间。禁止把完整 LogicSelfTests 的 DirectiveLexicon.Clear 带到玩家工程；RunUnityTests 拒绝非隔离公司标识。

## 本轮架构收口与保留评估

R42 从大型 LogicSelfTests 提取到 CapacityReceiptRegression，旧入口调用同一实现。编译检查与 Unity 回归启动/结果解析各有固定工具入口；测试基类负责失败后的单例清理，避免一个断言失败污染后续用例。跨季与开发推进共用时钟，减少两套现金判据。

InboxManager 仍承担申请、决策、到期、交割、重排和回执，是主要热点。下一步按“申请与幂等键”“交割执行”“运力重排”“回执生成”四边界拆分；优先抽纯决策和执行结果对象，保留原公开入口、Mail 字段和存档键。不能在未稳定的预算和长期平衡边界上一次拆完。

运力相关共享逻辑应留在 RoutePermitLogic / OpsRotationLogic / CrewCapacityLogic，UI 只传意图并呈现结果。导航应走 PrefabNavIntegration 和稳定 routeId。FinancePanelUI 本轮仅恢复缺失的明细入口，没有改成另一套导航体系。继续保留大型类是当前降低兼容风险的有意选择；本阶段不做无回归支撑的广泛重构。

## 验证限度

编译检查不证明执飞；PlayMode 断言不等于人眼逐像素验收；离线一年矩阵不等于 Runtime 两年生命周期或联网服务通过。具体通过数、失败输入、源码指纹和剩余问题见最新交接与任务书。
