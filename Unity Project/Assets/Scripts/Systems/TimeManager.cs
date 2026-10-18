using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 财年心跳：连续游戏时钟按倍速推进；跨季时自动结算。
    /// 手动推进季度仅作开发工具（见 Settings / F7）。
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        /// <summary>开局进入主页后为 true，设置流期间不走表。</summary>
        public static bool SimulationEnabled { get; set; }

        public event Action<QuarterResult> OnQuarterAdvanced;
        public event Action<YearEndResult> OnYearEnded;
        /// <summary>游戏时钟每推进整分钟时触发（供顶栏刷新）。</summary>
        public event Action OnClockTick;

        [Header("随机事件")]
        [Range(0f, 1f)] public float oilEventChance = 0.55f;
        public float oilShockMin = -0.12f; // -12%
        public float oilShockMax = 0.18f;  // +18%

        float _minuteAccum;
        bool _advancing;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 进入游戏或读档：强制 1×，沿用当前游戏时钟（不拉齐现实 UTC）。
        /// 新开局在设置流里已对齐现实；读档恢复存档时分与公历日期。
        /// </summary>
        public void BeginPlaySession()
        {
            var gs = GameState.Instance;
            if (gs != null)
                gs.speedLevel = GameSpeed.L1;
            _minuteAccum = 0f;
            GameSpeed.Set(GameSpeed.L1);
            OnClockTick?.Invoke();
        }

        void Update()
        {
            if (!SimulationEnabled || _advancing) return;
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver || GameSpeed.IsPaused) return;
            if (InboxManager.HasDueMails(gs))
            {
                GameSpeed.Pause();
                return;
            }

            float rate = GameSpeed.GameMinutesPerSecond;
            if (rate <= 0f) return;

            _minuteAccum += Time.unscaledDeltaTime * rate;
            int whole = (int)_minuteAccum;
            if (whole <= 0) return;
            _minuteAccum -= whole;
            AdvanceGameMinutes(gs, whole);
        }

        /// <summary>推进若干游戏分钟；跨过季末则自动调用 <see cref="AdvanceQuarter"/>。</summary>
        public void AdvanceGameMinutes(GameState gs, int minutes)
            => AdvanceMinutesCore(gs, minutes);

        QuarterResult AdvanceMinutesCore(GameState gs, int minutes)
        {
            if (gs == null || minutes <= 0 || _advancing) return null;

            const long minutesPerQuarter = (long)GameState.DaysPerQuarter * 24L * 60L;
            int remaining = minutes;
            QuarterResult lastClosed = null;
            while (remaining > 0 && !gs.gameOver)
            {
                long within = ((long)Mathf.Clamp(gs.simDay, 1, GameState.DaysPerQuarter) - 1) * 1440L
                    + Mathf.Clamp(gs.simHour, 0, 23) * 60L + Mathf.Clamp(gs.simMinute, 0, 59);
                // Do not let LiveClockSimulator discard elapsed days through its catch-up cap.
                // Close the old quarter only after its final flight/cost/mail ticks have run.
                int step = (int)Math.Min(remaining, Math.Min(LiveClockLogic.MaxCatchUpMinutes, minutesPerQuarter - within));
                DateTime fromUtc = gs.SimUtc;
                gs.SetSimUtc(fromUtc.AddMinutes(step));
                gs.simDay = (int)Math.Min(GameState.DaysPerQuarter, (within + step) / 1440L + 1);
                LiveClockSimulator.Advance(gs, fromUtc, gs.SimUtc);
                ExternalEventManager.Tick(gs, fromUtc, gs.SimUtc);
                StaffRecruitment.Tick(gs, gs.SimUtc);
                HrManager.TickDay(gs, gs.SimUtc);
                InboxManager.DeliverDue(gs, gs.SimUtc);
                remaining -= step;
                if (within + step >= minutesPerQuarter)
                {
                    lastClosed = CloseQuarter();
                    if (lastClosed == null || !lastClosed.success) break;
                }
                OnClockTick?.Invoke();
            }
            return lastClosed;
        }

        /// <summary>开发推进：实际模拟本季余下时间，与自然跨季共用执飞、成本和结算路径。</summary>
        public QuarterResult AdvanceQuarter()
        {
            var gs = GameState.Instance;
            if (gs == null || _advancing || gs.gameOver)
                return new QuarterResult { success = false, message = "状态未就绪、结算中或游戏已结束。" };
            int elapsed = (Mathf.Clamp(gs.simDay, 1, GameState.DaysPerQuarter) - 1) * 1440
                + Mathf.Clamp(gs.simHour, 0, 23) * 60 + Mathf.Clamp(gs.simMinute, 0, 59);
            return AdvanceMinutesCore(gs, GameState.DaysPerQuarter * 1440 - elapsed)
                ?? new QuarterResult { success = false, message = "未完成季度推进。" };
        }

        QuarterResult CloseQuarter()
        {
            if (_advancing)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "季度结算进行中，请稍候。"
                };
            }

            _advancing = true;
            try
            {
                return AdvanceQuarterCore();
            }
            finally
            {
                _advancing = false;
            }
        }

        QuarterResult AdvanceQuarterCore()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "游戏状态未就绪，无法推进季度。"
                };
            }

            if (gs.gameOver)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "游戏已结束：" + (gs.gameOverReason ?? "董事会已解雇 CEO。")
                };
            }

            // GDD 7.2 / 7.7：确保 Manager；触发标记在推进后写入 result
            EnsureAOGManager();
            EnsureExpansionManager();

            int fromYear = gs.year;
            int fromQuarter = gs.quarter;

            // P3 体验收口：季度初快照（失误保护·时间回溯的「回到本季度初」节点）
            MistakeProtectionManager.CaptureQuarterStart(gs);

            // Realized operating cash is authoritative. A quarter-end forecast cannot pay
            // for cancelled, unflown or only recently added service retroactively.
            float liveIn = gs.liveQuarterIncome;
            float liveEx = gs.liveQuarterExpense;
            StrategyManager.RefreshAllianceLift(gs);
            float allianceIncome = gs.stg?.alliance != null ? gs.stg.alliance.revenueLiftWan / 4f : 0f;
            float quarterIncome = liveIn + allianceIncome;
            // 部门口径支出（分摊到预算项做超支问责）
            float deptQuarterExpense = (gs.monthlyBurnRate + gs.FleetMonthlyMaintenance + gs.FleetMonthlyLease + gs.CeoMonthlyPayWan) * 3f;
            // Payroll, maintenance, lease and crisis costs have already been booked daily.
            float quarterExpense = liveEx;
            float net = allianceIncome;

            // 2.3 更新现金 + 本财年净利润累计
            gs.cash += net;
            gs.yearNetProfit += net;
            gs.liveQuarterIncome = 0f;
            gs.liveQuarterExpense = 0f;

            // 见习期软保护（GDB 28.1）：季度支出按部门份额分摊累计，供财年末超支问责
            // 只分摊部门口径支出，运行人员薪资不进部门预算问责
            FinanceManager.AccrueBudgetSpend(gs, deptQuarterExpense);

            if (BoardManager.Instance != null)
                BoardManager.UpdateSimulatedKpis(gs);

            // Phase 15 修复：对冲结算应使用本季度（波动前）的油价，而非新季波动后价格
            // 先记录历史价格用于图表展示，再执行对冲结算
            string hedgeText = "";
            string eventText = "";
            float quarterEndPrice = gs.fuelPricePerTon;
            if (HedgeManager.Instance != null)
            {
                HedgeManager.Instance.PushPriceHistory(quarterEndPrice);
                hedgeText = HedgeManager.Instance.SettleQuarter(quarterEndPrice);
            }
            if (!string.IsNullOrEmpty(hedgeText))
            {
                eventText = hedgeText;
                InboxManager.NotifyHedgeSettle(gs, hedgeText);
            }

            // 汇率（Phase 20）：与油价对冲同口径 —— 先按漂移前汇率结算本季汇兑损益，再漂移汇率
            string fxText = AirlineCEO.Logic.FxEconomics.SettleAndDrift(gs);
            if (!string.IsNullOrEmpty(fxText))
                eventText = string.IsNullOrEmpty(eventText) ? fxText : eventText + "\n" + fxText;

            // Phase 9 + 13：油价数据源统一到 MacroEconomyManager（季度漂移 + 财年 Q1 宏观倾向，口径对齐 tools/balance）
            if (MacroEconomyManager.Instance != null)
                MacroEconomyManager.Instance.ApplyFuelTick(gs);
            else
            {
                var goMacro = new GameObject("MacroEconomyManager");
                goMacro.AddComponent<MacroEconomyManager>();
                MacroEconomyManager.Instance.ApplyFuelTick(gs);
            }

            // 现实行情（默认开启，可在 n116 关掉）：与上面的季度漂移分开结算，收敛说明进季报 + 单独发一封行情通报
            string quoteText = MacroEconomyManager.ConvergeToRealQuote(gs);
            if (!string.IsNullOrEmpty(quoteText))
            {
                eventText = string.IsNullOrEmpty(eventText) ? quoteText : eventText + "\n" + quoteText;
                InboxManager.NotifyFuelQuoteConverge(gs, quoteText);
            }

            string loanText = "";
            if (LoanManager.Instance != null)
                loanText = LoanManager.Instance.ProcessQuarterRepayments();
            if (!string.IsNullOrEmpty(loanText))
                eventText = string.IsNullOrEmpty(eventText) ? loanText : eventText + "\n" + loanText;

            LoanManager.ClearCrisisBurnIfRecovered(gs);

            string safetyText = "";
            if (SafetyEventManager.Instance != null)
                safetyText = SafetyEventManager.Instance.TryRollQuarterEvent(gs);
            else
            {
                var go = new GameObject("SafetyEventManager");
                go.AddComponent<SafetyEventManager>();
                safetyText = SafetyEventManager.Instance.TryRollQuarterEvent(gs);
            }

            if (!string.IsNullOrEmpty(safetyText))
                eventText = string.IsNullOrEmpty(eventText) ? safetyText : eventText + "\n" + safetyText;

            // P2 长线运营：货运业务独立核算（腹舱 + 纯货机）
            string cargoText = "";
            if (CargoManager.Instance != null)
                cargoText = CargoManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("CargoManager");
                go.AddComponent<CargoManager>();
                cargoText = CargoManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(cargoText))
                eventText = string.IsNullOrEmpty(eventText) ? cargoText : eventText + "\n" + cargoText;

            // P2 长线运营：非航收入与临空商业（GDB 26.2）
            string commerceText = "";
            if (AirportCommerceManager.Instance != null)
                commerceText = AirportCommerceManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("AirportCommerceManager");
                go.AddComponent<AirportCommerceManager>();
                commerceText = AirportCommerceManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(commerceText))
                eventText = string.IsNullOrEmpty(eventText) ? commerceText : eventText + "\n" + commerceText;

            AirlineCEO.Logic.CityEconomyLogic.TickQuarter(gs);
            HrManager.TickQuarter(gs);

            // 过渡业务（开局三架飞机）：季末刷新报价板并结清本季报告计数
            TransitionBusinessLogic.OnQuarterTick(gs);

            // P2 长线运营：财务细分季度结算（补贴/燃油预警/PDP 里程碑/保险）
            string financeText = "";
            if (FinanceManager.Instance != null)
                financeText = FinanceManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("FinanceManager");
                go.AddComponent<FinanceManager>();
                financeText = FinanceManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(financeText))
                eventText = string.IsNullOrEmpty(eventText) ? financeText : eventText + "\n" + financeText;

            // 分类收集：对冲、贷款、安全、货运、非航商业、财务 → 其他运营记录
            string otherEventText = ConcatLines(hedgeText, ConcatLines(loanText, ConcatLines(safetyText, ConcatLines(cargoText, ConcatLines(commerceText, financeText)))));

            // Phase 11：AI 竞对行为 + 市场份额 / 压力
            string aiText = "";
            if (AIManager.Instance != null)
                aiText = AIManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("AIManager");
                go.AddComponent<AIManager>();
                aiText = AIManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(aiText))
                eventText = string.IsNullOrEmpty(eventText) ? aiText : eventText + "\n" + aiText;

            string compText = "";
            if (CompetitionManager.Instance != null)
                compText = CompetitionManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("CompetitionManager");
                go.AddComponent<CompetitionManager>();
                compText = CompetitionManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(compText))
                eventText = string.IsNullOrEmpty(eventText) ? compText : eventText + "\n" + compText;

            string aiEventText = ConcatLines(aiText, compText);

            // Phase 12：随机事件（可占用大油价槽）
            string randomText = "";
            if (RandomEventManager.Instance != null)
                randomText = RandomEventManager.Instance.OnQuarterTick();
            else
            {
                var go = new GameObject("RandomEventManager");
                go.AddComponent<RandomEventManager>();
                randomText = RandomEventManager.Instance.OnQuarterTick();
            }
            if (!string.IsNullOrEmpty(randomText))
                eventText = string.IsNullOrEmpty(eventText) ? randomText : eventText + "\n" + randomText;

            string randomEventText = randomText;

            // 大油价：仅当本季随机事件未占用油价槽
            string oilText = "";
            bool oilBlocked = RandomEventManager.Instance != null &&
                              RandomEventManager.Instance.ConsumedOilSlotThisQuarter;
            if (!oilBlocked)
            {
                oilText = TryOilEvent(gs);
                if (!string.IsNullOrEmpty(oilText))
                {
                    eventText = string.IsNullOrEmpty(eventText) ? oilText : eventText + "\n" + oilText;
                    InboxManager.NotifyFuelMove(gs, oilText);
                }
            }

            // 2.4 推进季度
            bool yearEnded = false;
            gs.quarter += 1;
            if (gs.quarter > 4)
            {
                gs.quarter = 1;
                gs.year += 1;
                yearEnded = true;
            }
            // 新季财年日从 D1 起；公历时钟不重置（顶栏继续跟游戏瞬间走）
            gs.simDay = 1;
            _minuteAccum = 0f;

            string summary =
                $"季度结算：实际航班收入 {liveIn:F0} 万 − 实际日常支出 {liveEx:F0} 万；" +
                $"联盟季度收入 {allianceIncome:F0} 万；本次入账 {(net >= 0 ? "+" : "")}{net:F0} 万。" +
                $" 时间 {Label(fromYear, fromQuarter)} → {gs.DateLabel}。";

            gs.AddDecisionLog(summary + (string.IsNullOrEmpty(eventText) ? "" : " " + eventText));

            var result = new QuarterResult
            {
                success = true,
                fromYear = fromYear,
                fromQuarter = fromQuarter,
                toYear = gs.year,
                toQuarter = gs.quarter,
                quarterIncome = quarterIncome,
                quarterExpense = quarterExpense,
                netChange = net,
                cashAfter = gs.cash,
                eventText = eventText,
                yearEnded = yearEnded,
                message = summary + (string.IsNullOrEmpty(eventText) ? "" : "\n" + eventText),
                aiEventText = aiEventText,
                randomEventText = randomEventText,
                otherEventText = otherEventText
            };

            // 破产检测（现金为负）
            string urgentText = "";
            if (gs.cash < 0f)
            {
                float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                result.isBankruptWarning = true;
                urgentText = $"现金已为负（{gs.cash:F0} 万）！" +
                    $"{CashRunnelCalculator.FormatPlayerLabel(runway)}，请尽快融资或削减成本。";
                result.message +=
                    $"\n⚠ 破产警告：{urgentText}";
            }

            // 油价事件：现金为负时归入紧急，否则归入随机事件
            if (!string.IsNullOrEmpty(oilText))
            {
                if (gs.cash < 0f)
                {
                    urgentText = string.IsNullOrEmpty(urgentText)
                        ? oilText
                        : urgentText + "\n" + oilText;
                }
                else
                {
                    result.randomEventText = string.IsNullOrEmpty(result.randomEventText)
                        ? oilText
                        : result.randomEventText + "\n" + oilText;
                }
            }
            result.urgentEventText = urgentText;

            OnQuarterAdvanced?.Invoke(result);

            InboxManager.ProcessPendingEffects(gs);
            UsedAircraftMarket.Ensure(gs, refreshQuarter: true);

            // Phase 8：季末评估部门 KPI（达标则下季效率 +5%）
            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.OnQuarterEndEvaluateKpi();

            // 2.5 必须先 OnYearEnd（用旧目标结算），再 NotifyChanged，避免中途重生目标、清零 yearNetProfit
            if (yearEnded)
            {
                var yearResult = OnYearEnd(gs, fromYear);
                result.yearEnd = yearResult;
                result.toYear = gs.year;
                result.toQuarter = gs.quarter;
                result.message += "\n" + yearResult.message;
                OnYearEnded?.Invoke(yearResult);

                // 见习期软保护（GDB 28.1）：财年末超支问责（第 2 财年惩罚减半）
                string overrunText = FinanceManager.SettleBudgetOverrun(gs);
                if (!string.IsNullOrEmpty(overrunText))
                {
                    result.message += "\n" + overrunText;
                    result.yearEnd.satisfactionAfter = gs.boardSatisfaction;
                }

                // Phase 8：财年末忠诚度结算（可能触发离职）
                if (DepartmentManager.Instance != null)
                    DepartmentManager.Instance.OnYearEndLoyalty();

                // Phase 10：机龄 +1
                if (FleetManager.Instance != null)
                    FleetManager.Instance.OnYearEndAgeFleet();

                // Phase 13：宏观经济每年 Q1 更新 GDP + 阶段
                if (MacroEconomyManager.Instance != null)
                {
                    string macroMsg = MacroEconomyManager.Instance.OnNewFiscalYear(gs, gs.year);
                    result.macroEventText = macroMsg;
                    result.message += "\n" + macroMsg;
                }

                // GDB 26.3：财年末生成时间胶囊快照（历史回放 / What-If 分支，终局也保留）
                ArchiveManager.CaptureTimeCapsule(gs);
            }

            // P0 遗留：软性失败判定（敌意收购 / 监管接管）
            var defeat = EndgameEvaluator.CheckDefeat(gs);
            if (defeat != null)
            {
                EndgameEvaluator.ApplyDefeat(gs, defeat);
                string defeatText = $"🚨 {defeat.title}：{defeat.body}";
                result.defeatText = defeatText;
                result.urgentEventText = string.IsNullOrEmpty(result.urgentEventText)
                    ? defeatText
                    : result.urgentEventText + "\n" + defeatText;
                result.message += "\n" + defeatText;
                gs.AddDecisionLog(defeatText);
            }

            gs.NotifyChanged();

            // GDD 7.2：进入第 1 年 Q2/Q3 后标记待触发 AOG（Q2 未触发则 Q3 强制）
            if (AOGManager.Instance != null && AOGManager.Instance.ShouldTriggerAOG())
            {
                result.pendingAOG = true;
                result.message += "\n⚠ 停飞危机待处置（飞机突发故障）。";
            }

            // GDD 7.7：第 4 财年 Q1（或 Q2 强制）战略性扩张提案
            if (ExpansionProposalManager.Instance != null && ExpansionProposalManager.Instance.ShouldTriggerProposal())
            {
                result.pendingExpansionProposal = true;
                result.message += "\n🌏 战略发展部提交了扩张提案。";
            }

            // 现金流吃紧：跑道 < 3 月 → 通知 UI/AI 推送 + 解锁紧急融资
            // Phase 15：现金为负时也触发警告（此前 gs.cash >= 0f 导致最危险的时刻反而不提示）
            float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            if (months < 3f && !gs.gameOver)
            {
                result.needsFinanceWarning = true;
                result.financeWarning =
                    $"财务顾问：账上钱不多了（{CashRunnelCalculator.FormatPlayerLabel(months)}），" +
                    "建议打开「财务」使用紧急融资，或削减成本、暂缓扩张。";
            }

            // P3 体验收口：失误保护·时间回溯（季度末检查重大失误，命中则回滚到季度初）
            bool safetyOccurred = !string.IsNullOrEmpty(safetyText);
            string rollbackText = MistakeProtectionManager.CheckAndMaybeRollback(gs, safetyOccurred);
            if (!string.IsNullOrEmpty(rollbackText))
            {
                result.mistakeRollbackText = rollbackText;
                result.message += "\n" + rollbackText;
                gs.AddDecisionLog(rollbackText);
                // 已回滚到季度初：跳过季度自动存档，让玩家重新决策本季度
                return result;
            }

            // P2 无痕：全球航空动态消息流（外部数据 + 程序生成事件，统一伪装为新闻，GDB 22/29）
            if (!gs.gameOver)
            {
                string feedText = DynamicFeedManager.OnQuarterTick(gs, net);
                if (!string.IsNullOrEmpty(feedText))
                {
                    result.feedText = feedText;
                    result.message += "\n\n📰 全球航空动态：\n" + feedText;
                }
            }

            // P3 体验收口：智能提示 + 消息中心推送 + 红色危机强制暂停（GDB 28.4 / 29.1 / 30.2）
            if (!gs.gameOver)
            {
                // 1) 季度结算信息消息（信息级）
                MessageCenter.Push(MessagePriority.Info, "n13_运营部", "运营部",
                    $"第 {gs.year} 年 Q{gs.quarter} 结算",
                    result.message.Length > 160 ? result.message.Substring(0, 160) + "…" : result.message,
                    gs.DateLabel, gs.year, gs.quarter);

                // 2) 智能提示（现金 / 飞行员缺口 / 战略一致性 / 准点率）
                string alertText = SmartAlertManager.OnQuarterEnd(gs);
                if (!string.IsNullOrEmpty(alertText))
                    result.message += "\n" + alertText;

                // 3) 红色危机强制暂停（AOG / 现金为负 / 跑道 < 2 月）
                float runwayMonths = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                bool redCrisis = result.pendingAOG || result.isBankruptWarning
                    || (runwayMonths >= 0f && runwayMonths < 2f);
                if (!string.IsNullOrEmpty(result.defeatText)) redCrisis = false;
                if (redCrisis)
                {
                    string body = result.pendingAOG
                        ? "飞机突发故障停场（AOG），请立即处置。"
                        : result.isBankruptWarning
                            ? "现金为负，濒临破产，请立即融资或削减成本。"
                            : $"现金跑道仅剩 {runwayMonths:F1} 个月，请立即应对。";
                    MessageCenter.Push(MessagePriority.Emergency, "n13_运营部", "运营部",
                        "紧急：需立即决策", body, gs.DateLabel, gs.year, gs.quarter);
                    GameSpeed.Pause();
                }
            }

            // GDB 白板 n7：待办邮件到期 → 强制暂停 + 阻断时间推进（须批准/拒绝后方可继续）
            if (!gs.gameOver && InboxManager.HasDueMails(gs))
            {
                var due = InboxManager.FirstDueMail(gs);
                result.pendingDueMail = true;
                string dueBody = due != null
                    ? $"「{due.subject}」已到期，请前往收件箱处理（批准 / 拒绝）。"
                    : "有到期待办邮件未处理，请前往收件箱处理。";
                MessageCenter.Push(MessagePriority.Emergency, "n7_收件箱", "收件箱",
                    "待办到期", dueBody, gs.DateLabel, gs.year, gs.quarter);
                GameSpeed.Pause();
            }

            // GDB 31.1：季度结束自动存档（铁人模式实时覆盖单槽；普通模式滚动 12 节点）
            if (!gs.gameOver && SaveManager.Instance != null)
                SaveManager.Instance.AutoSave();

            // GDB 38：匿名遥测（季度推进节点）
            TelemetryManager.OnQuarterAdvanced(gs);

            return result;
        }

        /// <summary>财年末：先用本财年目标快照结算，再生成新年目标。</summary>
        public YearEndResult OnYearEnd(GameState gs, int finishedYear)
        {
            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            // 快照：税前年净利（企业所得税结算用，BoardManager 会清零 yearNetProfit）
            float yearProfitWan = gs.yearNetProfit;

            // 快照，避免结算过程中列表被替换
            var snapshot = gs.currentTargets != null
                ? new List<BoardTarget>(gs.currentTargets)
                : new List<BoardTarget>();

            var eval = YearEndEvaluator.Evaluate(
                snapshot,
                finishedYear,
                gs != null ? gs.difficulty : DifficultyMode.Normal,
                gs != null ? gs.ceoBirthYear : 0,
                gs);

            float delta = BoardEconomics.SatisfactionDelta(eval.outcome, eval.completionRate);
            if (gs.cash < 0f) delta -= 5f;

            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + delta, 0f, 100f);

            if (BoardManager.Instance != null)
                BoardManager.Instance.ApplySatisfactionDeltaToMembers(delta * 0.5f);

            // P2 长线运营：财年末财务细分结算文案（续聘路径填充）
            string financeYearText = "";

            if (eval.outcome == VoteOutcome.Fired)
            {
                gs.consecutiveFailedVotes++;
                gs.gameOver = true;
                gs.gameOverVictory = false;
                gs.gameOverReason = eval.body;
                gs.year = finishedYear;
                gs.quarter = 4;
            }
            else if (eval.outcome == VoteOutcome.Retired)
            {
                gs.gameOver = true;
                gs.gameOverVictory = true;
                gs.gameOverReason = eval.body;
                gs.year = finishedYear;
                gs.quarter = 4;
                gs.lastLegacyScore = eval.legacy != null ? eval.legacy.total : 0f;
                gs.lastLegacyGrade = eval.legacy != null ? eval.legacy.grade : "C";
                gs.unlockedLegendMode = true;
                gs.unlockedNewGamePlus = true;
                gs.unlockedFastMode = true;
            }
            else
            {
                if (eval.outcome == VoteOutcome.ProbationContinue)
                    gs.probationProtectionsUsed++;
                else
                    gs.consecutiveFailedVotes = 0;

                if (eval.contractRenewed)
                    gs.RenewCeoContract(finishedYear + 1);

                if (eval.outcome == VoteOutcome.Reappointed && gs.ceoBonusWan > 0f)
                {
                    gs.cash -= gs.ceoBonusWan;
                    financeYearText = $"CEO 年终奖金 −{gs.ceoBonusWan:F2} 万美元。";
                }

                if (BoardManager.Instance != null)
                    BoardManager.Instance.OnNewFiscalYearStarted(gs.year);

                // P2 长线运营：财年末财务细分结算（企业所得税 + 碳配额 EU ETS/CORSIA）
                if (FinanceManager.Instance != null)
                {
                    string tax = FinanceManager.Instance.SettleAnnualFinance(gs, finishedYear, yearProfitWan);
                    financeYearText = string.IsNullOrEmpty(financeYearText)
                        ? tax
                        : financeYearText + "\n" + tax;
                }
            }

            string msg =
                $"年度结算（第 {finishedYear} 年）：完成率 {eval.completionRate:F0}%（{eval.targetsCompleted}/{eval.targetsTotal}）。" +
                $"【{eval.title}】{eval.body}";

            if (!string.IsNullOrEmpty(financeYearText))
                msg += "\n" + financeYearText;

            gs.AddDecisionLog(msg);

            return new YearEndResult
            {
                finishedYear = finishedYear,
                satisfactionDelta = delta,
                satisfactionAfter = gs.boardSatisfaction,
                targetsTotal = eval.targetsTotal,
                targetsCompleted = eval.targetsCompleted,
                completionRate = eval.completionRate,
                reappointmentThreshold = eval.reappointmentThreshold,
                message = msg,
                showSettlementPopup = true,
                voteOutcome = eval.outcome,
                voteTitle = eval.title,
                voteBody = eval.body,
                gameOver = eval.outcome == VoteOutcome.Fired || eval.outcome == VoteOutcome.Retired,
                shouldResetTargets = eval.outcome != VoteOutcome.Fired && eval.outcome != VoteOutcome.Retired,
                victory = eval.outcome == VoteOutcome.Retired,
                legacy = eval.legacy,
                legacyUnlockText = eval.outcome == VoteOutcome.Retired
                    ? "已解锁：传奇模式 · 新游戏+ · 快速模式"
                    : "",
                targetSnapshot = snapshot
            };
        }

        /// <summary>季度净利计入本财年累计（在推进前由外部调用，或在本方法内计入）。</summary>
        public void AccrueYearNetProfit(GameState gs, float quarterNet)
        {
            if (gs == null) return;
            gs.yearNetProfit += quarterNet;
        }

        private string TryOilEvent(GameState gs)
        {
            if (UnityEngine.Random.value > oilEventChance)
                return "";

            float shock = UnityEngine.Random.Range(oilShockMin, oilShockMax);
            float before = gs.fuelPricePerTon;
            gs.fuelPricePerTon = Mathf.Max(400f, before * (1f + shock));
            string dir = shock >= 0 ? "上涨" : "下跌";
            return $"事件：航油价格{dir} {Mathf.Abs(shock) * 100f:F0}%（{before:F0}→{gs.fuelPricePerTon:F0} 美元/吨）。";
        }

        private static string ConcatLines(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + "\n" + b;
        }

        private static void EnsureAOGManager()
        {
            if (AOGManager.Instance != null) return;
            var go = new GameObject("AOGManager");
            go.AddComponent<AOGManager>();
        }

        private static void EnsureExpansionManager()
        {
            if (ExpansionProposalManager.Instance != null) return;
            var go = new GameObject("ExpansionProposalManager");
            go.AddComponent<ExpansionProposalManager>();
        }

        private static string Label(int y, int q) => $"第 {y} 年 Q{q}";
    }

    [Serializable]
    public class QuarterResult
    {
        public bool success;
        public string message;
        public int fromYear;
        public int fromQuarter;
        public int toYear;
        public int toQuarter;
        public float quarterIncome;
        public float quarterExpense;
        public float netChange;
        public float cashAfter;
        public string eventText;
        public bool yearEnded;
        public bool isBankruptWarning;
        public bool needsFinanceWarning;
        public string financeWarning;
        public YearEndResult yearEnd;
        /// <summary>GDD 7.2：本季推进后应弹出 AOG 危机。</summary>
        public bool pendingAOG;
        /// <summary>GDD 7.7：本季推进后应弹出战略性扩张提案。</summary>
        public bool pendingExpansionProposal;

        // Phase 12+：弹窗分类展示（避免所有消息混作一团）
        public string urgentEventText = "";    // 🚨 紧急：破产、油价冲击+现金为负
        public string randomEventText = "";    // 📋 随机事件（台风/暴雪等）
        public string aiEventText = "";        // 📋 AI 竞对动态
        public string otherEventText = "";     // 📋 其他运营记录（对冲/贷款/安全/竞争压力）
        public string macroEventText = "";     // Phase 13：宏观经济展望报告（每年 Q1）
        public string defeatText = "";         // P0 遗留：软性失败（敌意收购/监管接管）文案
        /// <summary>P3 体验收口：失误保护「时间回溯」已触发（回滚到季度初）。</summary>
        public string mistakeRollbackText = "";
        /// <summary>P2 无痕：全球航空动态消息流本季摘要（新闻社/机构名）。</summary>
        public string feedText = "";
        /// <summary>GDB 白板 n7：有到期未处理的待办邮件，须处理后才能继续（强制暂停 + 跳转收件箱）。</summary>
        public bool pendingDueMail;
    }

    [Serializable]
    public class YearEndResult
    {
        public int finishedYear;
        public float satisfactionDelta;
        public float satisfactionAfter;
        public string message;
        public int targetsTotal;
        public int targetsCompleted;
        public float completionRate;
        public float reappointmentThreshold;
        public bool showSettlementPopup;

        // Phase 6 续聘投票
        public VoteOutcome voteOutcome;
        public string voteTitle;
        public string voteBody;
        public bool gameOver;
        public bool shouldResetTargets;

        // GDB 第4章：光荣退休（胜利通关）
        public bool victory;
        public LegacyScoreResult legacy;
        public string legacyUnlockText;

        /// <summary>本财年目标的快照（含完成状态与当前值），供 VoteUI 展示逐项勾叉。</summary>
        public List<BoardTarget> targetSnapshot;
    }
}
