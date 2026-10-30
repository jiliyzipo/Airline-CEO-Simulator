using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace SlotCheck
{
    /// <summary>
    /// 「逐停站时刻槽」纯规则离线校验（不依赖 Unity 运行时，也不依赖 GameState）。
    ///
    /// ★2026-10-16 口径（CEO 第十八次问询）：**一条槽 = 某停站某次停留的一对「到场 + 离场」**。
    ///   本场端（去程起飞一次 + 回程落地一次，同一次停留）= 1 条；
    ///   对端（到场 + 过站 + 回程起飞，同一次停留）= 1 条；
    ///   经停端（去程落一次、回程再落一次）= **2 条**。
    /// 一个往返（无经停）因此要 2 条、有经停要 4 条 —— 这正是「只申请起飞机场」那种读法的反面。
    /// 这里只核对与 GameState 无关的部分（需求枚举、回程时刻推导、分端祖父判定）。
    /// 涉及持仓占用/购买/缺槽归因的部分需要 GameState（MonoBehaviour，离线无法构造），
    /// 由 Unity 编辑器菜单 LogicSelfTests 覆盖（含 ReleaseByRoute / BindRouteFlights / BandGapText）。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args != null && Array.IndexOf(args, "--deadband") >= 0) { DeadBandProbe.Run(); return 0; }
            // 诊断：把 CEO 的原话原样喂给 Parse（沿词表、验证意图与班次解析），用于复现报障。
            if (args != null)
            {
                int ci = Array.IndexOf(args, "--cmd");
                if (ci >= 0 && ci + 1 < args.Length) { ShowCmd(args[ci + 1]); return 0; }
            }

            int fail = 0;

            // —— 1. 协调机场判定：两端都只看「离港」是否协调 ——
            Check("SZX 离港需槽", RoutePermitLogic.NeedsDepartureSlot("SZX"), ref fail);
            Check("PEK 离港需槽", RoutePermitLogic.NeedsDepartureSlot("PEK"), ref fail);
            Check("SHA 离港需槽", RoutePermitLogic.NeedsDepartureSlot("SHA"), ref fail);
            Check("非协调机场 AKL 离港无需槽", !RoutePermitLogic.NeedsDepartureSlot("AKL"), ref fail);

            // —— 2. 单班次（无经停）：本场端 + 对端各一条需求 ——
            var route = MakeRoute("SZ100", "08:00", "10:30", destination: "PEK");
            var needs = RoutePermitLogic.SlotNeeds(route);
            Console.WriteLine("R14 逐停站槽需求：" + Describe(needs));
            Check("往返班次两端各一条（共 2 条）", needs.Count == 2, ref fail);
            Check("本场端 = SZX 早（去程起飞的这一次停留）", needs.Count > 0
                && needs[0].airport == "SZX" && needs[0].band == 0 && !needs[0].inbound
                && needs[0].station == RoutePermitLogic.StationOrigin, ref fail);
            Check("对端 = PEK（到场 + 回程起飞同属这一次停留）", needs.Count > 1
                && needs[1].airport == "PEK" && needs[1].inbound
                && needs[1].station == RoutePermitLogic.StationDest, ref fail);
            Check("[关键] 每条需求都成对带上「到场 + 离场」两个时刻（只写离场 = 只说半条）",
                needs.Count == 2
                && !string.IsNullOrEmpty(needs[0].arrTime) && !string.IsNullOrEmpty(needs[0].time)
                && !string.IsNullOrEmpty(needs[1].arrTime) && !string.IsNullOrEmpty(needs[1].time), ref fail);
            Check("本场端的到场 = 回程落地（与去程离场同属一对）",
                needs.Count == 2 && ScheduleSlots.ParseClock(needs[0].arrTime) > ScheduleSlots.ParseClock(needs[0].time), ref fail);
            Check("对端航班号与去程相差 1", needs.Count > 1 && needs[1].flightNo == "SZ101", ref fail);
            Check("两端航班号不同（去程号/回程号）",
                needs.Count > 1 && needs[0].flightNo != needs[1].flightNo, ref fail);
            Check("停站全称：本场端 / 对端（不得再出现「去程离港端」这种半条说法）",
                RoutePermitLogic.LegName(needs[0]) == "本场端"
                && RoutePermitLogic.LegName(needs[1]) == "对端", ref fail);

            // —— 2b. ★经停航线：经停站一趟往返停两次 = 两对槽（共 4 条需求）——
            //     这是「经停机场不需要时刻槽」那种读法的正面对照：经停站既落又起，
            //     WASG 1.6.1 下每一次落地与起飞都是独立的一次 movement，故两对槽缺一不可。
            var viaRoute = MakeRoute("SZ150", "08:00", "12:00", destination: "PEK");
            viaRoute.stopover = "SHA";
            var viaNeeds = RoutePermitLogic.SlotNeeds(viaRoute);
            Console.WriteLine("R14 经停航线槽需求：" + Describe(viaNeeds));
            Check("[关键] 有经停的往返共 **4** 条需求（本场端 1 + 经停端·去程 1 + 对端 1 + 经停端·回程 1）",
                viaNeeds.Count == 4, ref fail);
            Check("[关键] 经停站（SHA）要办 **2** 条：去程落一次、回程再落一次",
                CountAirport(viaNeeds, "SHA") == 2, ref fail);
            Check("经停端的两次停留分列去程/回程（方向不合并成一条）",
                viaNeeds.Exists(n => n.airport == "SHA" && !n.inbound && n.station == RoutePermitLogic.StationVia)
                && viaNeeds.Exists(n => n.airport == "SHA" && n.inbound && n.station == RoutePermitLogic.StationVia), ref fail);
            Check("停站全称：经停端（去程）/ 经停端（回程）",
                RoutePermitLogic.LegName(RoutePermitLogic.StationVia, false) == "经停端（去程）"
                && RoutePermitLogic.LegName(RoutePermitLogic.StationVia, true) == "经停端（回程）", ref fail);
            Check("[关键] 经停站的两条槽时刻不同（去程落与回程落不是同一次停留）",
                viaNeeds.Find(n => n.airport == "SHA" && !n.inbound).time
                != viaNeeds.Find(n => n.airport == "SHA" && n.inbound).time, ref fail);
            Check("经停航线仍保留本场端与对端各 1 条",
                CountAirport(viaNeeds, "SZX") == 1 && CountAirport(viaNeeds, "PEK") == 1, ref fail);
            Check("经停站到场早于该站离场（同一次停留的一对里，landing 在 take-off 前）",
                viaNeeds.FindAll(n => n.airport == "SHA").TrueForAll(n =>
                    ScheduleSlots.ParseClock(n.arrTime) < ScheduleSlots.ParseClock(n.time)), ref fail);
            // 经停站若为非协调机场，则经停端不产生需求，只剩两端 2 条。
            var viaFree = MakeRoute("SZ160", "08:00", "12:00", destination: "PEK");
            viaFree.stopover = "AKL";
            Check("经停站非协调 → 经停端不占槽（需求退回 2 条）",
                RoutePermitLogic.SlotNeeds(viaFree).Count == 2, ref fail);

            // —— 2c. WaveEndNeeds（开单的清单）同样逐停站，经停端不得被漏掉 ——
            var waveEnds = RoutePermitLogic.WaveEndNeeds(viaRoute, "08:00");
            Check("[关键] WaveEndNeeds 逐停站开列（经停线 4 条，不再只列本场端）",
                waveEnds.Count == 4 && CountAirport(waveEnds, "SHA") == 2, ref fail);
            Check("WaveEndNeeds 的时刻一律吸附到系列网格（可申请的那一刻）",
                waveEnds.TrueForAll(n => RoutePermitLogic.SlotRequestClock(n.time) == n.time), ref fail);

            // —— 3. 回程离港时刻 = 去程 + 轮挡 + 过站 ——
            int block = LiveClockLogic.BlockMinutes(route.depTime, route.arrTime, route.distanceKm);
            int turn = LiveClockLogic.TurnMinutes(route.aircraftType, route.groundStaffAssigned);
            int expect = LiveClockLogic.ParseMinutes(route.depTime) + block + turn;
            int returnMin = RoutePermitLogic.ReturnDepMinutes(route.depTime, route.arrTime,
                route.distanceKm, route.aircraftType, route.groundStaffAssigned);
            Console.WriteLine($"R14 回程离港 {ScheduleSlots.FormatClock(returnMin)}"
                + $"（去程 {route.depTime} + 轮挡 {block} 分 + 过站 {turn} 分）");
            Check("回程离港 = 去程 + 轮挡 + 过站", returnMin == expect % (24 * 60), ref fail);
            Check("回程离港晚于去程离港", returnMin > LiveClockLogic.ParseMinutes(route.depTime), ref fail);
            Check("回程端时段取自回程离港时刻（中班）", needs.Count > 1 && needs[1].band == 1, ref fail);

            // —— 4. 跨夜回程：红眼去程的回程落在次日凌晨，仍要占对端该时刻的槽 ——
            var redEye = MakeRoute("SZ200", "22:00", "00:30", destination: "PEK");
            int redReturn = RoutePermitLogic.ReturnDepMinutes(redEye.depTime, redEye.arrTime,
                redEye.distanceKm, redEye.aircraftType, redEye.groundStaffAssigned);
            var redNeeds = RoutePermitLogic.SlotNeeds(redEye);
            Console.WriteLine($"R14 红眼回程离港 {ScheduleSlots.FormatClock(redReturn)} → "
                + $"{ScheduleSlots.SlotBandName(redNeeds.Count > 1 ? redNeeds[1].band : -1)}");
            Check("跨夜回程仍在 0–1439 分钟范围内", redReturn >= 0 && redReturn < 24 * 60, ref fail);
            Check("跨夜回程时刻早于去程（次日凌晨）", redReturn < LiveClockLogic.ParseMinutes(redEye.depTime), ref fail);
            Check("跨夜回程仍生成两端需求", redNeeds.Count == 2, ref fail);

            // —— 5. 拟排时段（尚无班次）：BandNeeds 同样两端各一条 ——
            var noonNeeds = RoutePermitLogic.BandNeeds(route, 1);
            Console.WriteLine("R14 午班两端需求：" + Describe(noonNeeds));
            Check("午班两端各一条需求", noonNeeds.Count == 2, ref fail);
            Check("午班去程端时段 = 午（1）", noonNeeds.Count > 0 && noonNeeds[0].band == 1, ref fail);
            // ★2026-10-16 第十八次修订：两处（BandNeeds 与 ReturnTimeOf）自此都走
            //   `ProbeArrivalClock`（离场 + 单程轮挡），回程端时段**随波次走**；
            //   旧口径把首班到场钟点当探测时刻的对端钟点，回程端段位恒定不变。
            Check("午班回程端时段 = 按代表时刻预估（= 12:00 + 单程轮挡 + 过站，与 ReturnBandOf 同源）",
                noonNeeds.Count > 1 && noonNeeds[1].band == RoutePermitLogic.ReturnBandOf(route, 1), ref fail);

            // —— 6. 分端祖父条款：两端各自依据本端在飞时刻 ——
            Check("本航线在飞班次含去程早班（去程端祖父）", RoutePermitLogic.RouteHasBand(route, 0), ref fail);
            Check("本航线在飞班次不含去程中班", !RoutePermitLogic.RouteHasBand(route, 1), ref fail);
            Check("本航线在飞班次含回程中班（回程端祖父）",
                RoutePermitLogic.RouteHasReturnBand(route, 1), ref fail);
            Check("本航线在飞班次不含回程早班（回程端无祖父）",
                !RoutePermitLogic.RouteHasReturnBand(route, 0), ref fail);

            // —— 7. 多班次航线：需求按班次数倍增，且两端成对 ——
            var twin = MakeRoute("SZ300", "08:00", "10:30", destination: "PEK");
            twin.flights.Add(new RouteFlight
            {
                registration = "B-9002",
                depTime = "08:45",
                arrTime = "11:15"
            });
            var twinNeeds = RoutePermitLogic.SlotNeeds(twin);
            Console.WriteLine($"R14 双班次需求 {twinNeeds.Count} 条：" + Describe(twinNeeds));
            Check("双班次共 4 条需求（每班两端）", twinNeeds.Count == 4, ref fail);
            Check("第 1 班两端：SZX 去程 + PEK 回程",
                twinNeeds.Count == 4 && twinNeeds[0].airport == "SZX" && !twinNeeds[0].inbound
                && twinNeeds[1].airport == "PEK" && twinNeeds[1].inbound, ref fail);
            Check("第 2 班两端：SZX 去程 + PEK 回程",
                twinNeeds.Count == 4 && twinNeeds[2].airport == "SZX" && !twinNeeds[2].inbound
                && twinNeeds[3].airport == "PEK" && twinNeeds[3].inbound, ref fail);

            // —— 8. 非协调对端：只在本场一端要槽 ——
            var freeDest = MakeRoute("SZ400", "08:00", "10:30", destination: "AKL");
            var freeNeeds = RoutePermitLogic.SlotNeeds(freeDest);
            Console.WriteLine("R14 非协调对端需求：" + Describe(freeNeeds));
            Check("非协调对端只生成 1 条需求（本场端）", freeNeeds.Count == 1, ref fail);
            Check("该端为本场 SZX（去程起飞的这一次停留）",
                freeNeeds.Count == 1 && freeNeeds[0].airport == "SZX" && !freeNeeds[0].inbound, ref fail);

            // —— 9. 两端均非协调：不需要任何时刻槽 ——
            var noneCoord = MakeRoute("SZ500", "08:00", "10:30", destination: "AKL", origin: "AKL");
            Check("两端均非协调 → 0 条需求", RoutePermitLogic.SlotNeeds(noneCoord).Count == 0, ref fail);

            // —— 10. R18 时刻槽使用率考核：航季数学 / 星期几口径 / 系列编解码 ——
            //     本段只覆盖**不碰 GameState** 的纯函数；计数、评定、失格释放、JNUS 裁定
            //     需要 MonoBehaviour，由 Unity 菜单 LogicSelfTests 覆盖。
            Check("阈值 = 80%（WASG 8.6.1，不是 85%）",
                Math.Abs(SlotUsageLogic.Threshold - 0.80f) < 1e-6f, ref fail);

            int sum26 = SlotUsageLogic.KeyOf(2026, 2);
            int win26 = SlotUsageLogic.KeyOf(2026, 4);
            Check("夏秋 Q2 与 Q3 同属一个航季", sum26 == SlotUsageLogic.KeyOf(2026, 3), ref fail);
            Check("冬春 Q4 与次年 Q1 同属一个航季",
                win26 == SlotUsageLogic.KeyOf(2027, 1), ref fail);
            Check("夏秋 → 冬春：key + 1", sum26 + 1 == win26, ref fail);
            Check("冬春 → 次年夏秋：key + 1", win26 + 1 == SlotUsageLogic.KeyOf(2027, 2), ref fail);
            Check("上一等价航季恒为 key − 1（跨年不做年份加减）",
                SlotUsageLogic.KeyOf(2027, 2) - 1 == win26 && win26 - 1 == sum26, ref fail);
            Check("夏秋 key 偶 / 冬春 key 奇",
                SlotUsageLogic.SeasonOf(sum26) == SlotUsageLogic.Summer
                && SlotUsageLogic.SeasonOf(win26) == SlotUsageLogic.Winter, ref fail);
            Check("Q1 归上一年起算的冬春",
                SlotUsageLogic.StartYearOf(SlotUsageLogic.KeyOf(2026, 1)) == 2025, ref fail);
            Check("夏秋 HBD = 同年 Q3",
                SlotUsageLogic.HbdYearOf(sum26) == 2026
                && SlotUsageLogic.HbdQuarterOf(sum26) == 3, ref fail);
            Check("冬春 HBD = 起始年次年的 Q1",
                SlotUsageLogic.HbdYearOf(win26) == 2027
                && SlotUsageLogic.HbdQuarterOf(win26) == 1, ref fail);
            Check("换季清算季 = 夏秋 Q2 / 冬春 Q4",
                SlotUsageLogic.StartQuarterOf(sum26) == 2
                && SlotUsageLogic.StartQuarterOf(win26) == 4, ref fail);
            bool quartersExclusive = true;
            for (int q = 1; q <= 4; q++)
                if (SlotUsageLogic.IsSeasonStartQuarter(q) == SlotUsageLogic.IsHbdQuarter(q))
                    quartersExclusive = false;   // 每个季度必须恰好命中一个
            Check("四个季度各归「换季清算」或「HBD 起计」之一", quartersExclusive, ref fail);
            Console.WriteLine($"R18 航季：" + $"2026Q2={sum26} 2026Q4={win26} "
                + $"2027Q1={SlotUsageLogic.KeyOf(2027, 1)} 2027Q2={SlotUsageLogic.KeyOf(2027, 2)}"
                + $" · {SlotUsageLogic.LabelOf(win26)} HBD={SlotUsageLogic.HbdYearOf(win26)}"
                + $"Q{SlotUsageLogic.HbdQuarterOf(win26)}");
            Check("航季标签含年份与季节",
                SlotUsageLogic.LabelOf(sum26).Contains("2026")
                && SlotUsageLogic.LabelOf(sum26).Contains("夏秋"), ref fail);

            Check("星期几下标：周一 = 0 / 周日 = 6",
                SlotUsageLogic.WeekdayOf(new DateTime(2026, 10, 5)) == 0
                && SlotUsageLogic.WeekdayOf(new DateTime(2026, 10, 11)) == 6, ref fail);
            Check("系列标签 = 机场 + 具体时刻 + 星期几（周一…周日）",
                SlotUsageLogic.SeriesLabel("PEK", "08:00", 0) == "PEK 08:00 周一"
                && SlotUsageLogic.SeriesLabel("PEK", "18:00", 6) == "PEK 18:00 周日", ref fail);

            // 按 weekday 拆系列：同机场同时刻的不同星期必须判成不同系列（周一可掉、周二可留）
            var cmpMon = new SlotSeries { airport = "PEK", time = "08:00", weekday = 0 };
            var cmpTue = new SlotSeries { airport = "PEK", time = "08:00", weekday = 1 };
            Check("同机场同时刻不同星期 = 不同系列",
                SlotUsageLogic.SeriesLabel(cmpMon.airport, cmpMon.time, cmpMon.weekday)
                != SlotUsageLogic.SeriesLabel(cmpTue.airport, cmpTue.time, cmpTue.weekday), ref fail);
            Check("同机场同星期不同时刻 = 不同系列（06:30 与 07:00 各判各的 80/20）",
                SlotUsageLogic.SeriesLabel("PEK", "06:30", 0) != SlotUsageLogic.SeriesLabel("PEK", "07:00", 0), ref fail);

            var encList = new List<SlotSeries> { cmpMon, new SlotSeries { airport = "SZX", time = "18:00", weekday = 4 } };
            string encCsv = SlotUsageLogic.EncodeSeries(encList);
            Check("系列编码 = 机场|时刻|星期，逗号分隔", encCsv == "PEK|08:00|0,SZX|18:00|4", ref fail);
            var decList = SlotUsageLogic.DecodeSeries(encCsv);
            Check("系列编解码往返一致",
                decList.Count == 2 && decList[0].airport == "PEK" && decList[0].time == "08:00"
                && decList[0].weekday == 0 && decList[1].airport == "SZX"
                && decList[1].time == "18:00" && decList[1].weekday == 4, ref fail);
            Check("空值安全（无系列 / 无邮件正文）",
                SlotUsageLogic.DecodeSeries(null).Count == 0
                && SlotUsageLogic.DecodeSeries("").Count == 0
                && SlotUsageLogic.EncodeSeries(null) == "", ref fail);
            Check("越界与残缺片段被跳过（存档脏数据不抛异常）",
                SlotUsageLogic.DecodeSeries("PEK|99:99|0,oops,AKL|08:00|7,SZX|08:00|2").Count == 1, ref fail);
            Check("null GameState 安全（读档早期不崩）",
                SlotUsageLogic.HasAnyHistoricPrecedence(null, "PEK", "08:00")
                && SlotUsageLogic.HasHistoricPrecedence(null, "PEK", "08:00", 0), ref fail);

            Check("正当事由文案（WASG 8.8 落地口径）",
                SlotUsageLogic.JustifiedLabel(GroundReason.None) == ""
                && SlotUsageLogic.JustifiedLabel(GroundReason.NoAssignment) == "\u65e0\u673a\u53ef\u6d3e"
                && SlotUsageLogic.JustifiedLabel(GroundReason.Maintenance) == "\u505c\u573a\u68c0\u4fee", ref fail);

            var shownSeries = new SlotSeries
            {
                airport = "PEK", time = "08:00", weekday = 0,
                due = 100, operated = 80, justified = 20,
                jnusReasonCsv = "\u65e0\u673a\u53ef\u6d3e\u00d720", lastResult = "pending_jnus"
            };
            string seriesLine = SlotUsageLogic.FormatSeriesLine(shownSeries);
            Console.WriteLine("R18 系列现状：" + seriesLine);
            Check("系列现状显示 执飞/应飞 与百分比",
                seriesLine.Contains("80/100") && seriesLine.Contains("80%")
                && seriesLine.Contains("PEK 08:00 周一"), ref fail);
            Check("系列现状带正当未用明细与待裁状态",
                seriesLine.Contains("\u65e0\u673a\u53ef\u6d3e\u00d720")
                && seriesLine.Contains("\u5f85\u7533\u8bf7"), ref fail);
            Check("80% 恰好达标：保留历史优先权（不判失格）",
                SlotUsageLogic.ResultLabel("kept", true).Contains("\u4fdd\u7559"), ref fail);
            Check("失格与待裁状态文案可区分",
                SlotUsageLogic.ResultLabel("lost", false).Contains("\u5df2\u5931\u683c")
                && SlotUsageLogic.ResultLabel("pending_jnus", true).Contains("\u5f85\u7533\u8bf7"), ref fail);

            // —— 11. R19 运力调整指令：自然语言输入 vs 内部规范词 ——
            //     CEO 在单航线页底部那个框里是**自由输入**，系统靠两条通道解析：
            //     CapacityDirectiveLogic.Parse（本地关键词）+ 分析服务 JSON（MergeAiJson 覆盖）。
            //     Parse 是纯函数，可以离线钉住；这里要验的是「措辞没落在词表上时会发生什么」。
            Console.WriteLine("R19 运力指令解析（自由措辞 vs 规范词）");
            ShowCmd("加密到每天3班，早班到红眼都排上");
            ShowCmd("削减到每天1班");
            ShowCmd("维持现状不变");
            ShowCmd("这条线不赚钱，把班次收一收");   // 危险样例：想减班，但「收一收」不在词表
            ShowCmd("别飞那么多了");                 // 危险样例：只表达了减班语气
            ShowCmd("这条线亏得厉害，先停一班");     // 「停」不在词表
            ShowCmd("把运力提到最大");
            ShowCmd("把FR106/FR107调整为每日一段往返。");   // 报障原句：航班号 + 「调整为」+ 中文数词
            ShowCmd("把班期调整为每日");                     // 只说班期、不说班次 → 仍该请 CEO 补一句

            var cSpec = CapacityDirectiveLogic.Parse("加密到每天3班");
            Check("规范词「加密到每天3班」→ 增班 3 段，可直接执行（不用确认）",
                cSpec.intent == "increase" && cSpec.intentKnown && cSpec.waves == 3
                && !CapacityDirectiveLogic.NeedsConfirm(cSpec), ref fail);
            var cCut = CapacityDirectiveLogic.Parse("削减到每天1班");
            Check("规范词「削减到每天1班」→ 减班 1 段，可直接执行",
                cCut.intent == "reduce" && cCut.intentKnown && cCut.waves == 1
                && !CapacityDirectiveLogic.NeedsConfirm(cCut), ref fail);
            var cKeep = CapacityDirectiveLogic.Parse("维持现状不变");
            Check("规范词「维持现状不变」→ 维持，无副作用可直接执行",
                cKeep.intent == "keep" && cKeep.intentKnown
                && !CapacityDirectiveLogic.NeedsConfirm(cKeep), ref fail);
            var cAll = CapacityDirectiveLogic.Parse("把运力提到最大");
            Check("规范词「提到最大」→ 挖满全天时段（4 个时段已说清，不用确认）",
                cAll.maximize && cAll.deps != null && cAll.deps.Length == 4
                && !CapacityDirectiveLogic.NeedsConfirm(cAll), ref fail);
            var cSlots = CapacityDirectiveLogic.Parse("早班和晚班各排一班");
            Check("规范词「早班/晚班」→ 解析出对应时刻",
                cSlots.deps != null && cSlots.deps.Length == 2, ref fail);
            var cInc = CapacityDirectiveLogic.Parse("加密");
            Check("只说方向没说量（「加密」）→ 需确认（量由系统猜，必须过目）",
                cInc.intentKnown && CapacityDirectiveLogic.NeedsConfirm(cInc), ref fail);

            // —— 本段是这个 TODO 存在的原因：旧版 intent 默认 increase，于是这些措辞**
            //    想减班却被办成增班**，而且「班次」二字还会点亮 maximize（1 班直排 4 段）。
            var cVague = CapacityDirectiveLogic.Parse("这条线不赚钱，把班次收一收");
            Check("[回归] 「把班次收一收」→ 不再猜成增班，转人工确认",
                !cVague.intentKnown && !cVague.maximize
                && CapacityDirectiveLogic.NeedsConfirm(cVague), ref fail);
            var cStop = CapacityDirectiveLogic.Parse("这条线亏得厉害，先停一班");
            Check("[回归] 「先停一班」→ 不再猜成增班，转人工确认",
                !cStop.intentKnown && CapacityDirectiveLogic.NeedsConfirm(cStop), ref fail);
            var cLess = CapacityDirectiveLogic.Parse("别飞那么多了");
            Check("[回归] 「别飞那么多了」→ 不再猜成增班，转人工确认",
                !cLess.intentKnown && CapacityDirectiveLogic.NeedsConfirm(cLess), ref fail);
            Check("[回归] 「班次」不再触发「挖满全天」（它是描述班次最常用的词，太宽泛）",
                !cVague.maximize && !CapacityDirectiveLogic.Parse("班次太多了").maximize, ref fail);

            // 分析服务给了意图即视为读懂（哪怕关键词一个都没命中）
            var cAi = CapacityDirectiveLogic.Parse("这条线不赚钱，把班次收一收");
            CapacityDirectiveLogic.MergeAiJson(cAi, "{\"intent\":\"reduce\",\"waves\":1,\"rationale\":\"减班\"}");
            Check("分析服务给出意图 → 视为读懂，可执行且取到减班",
                cAi.intentKnown && cAi.intent == "reduce" && !CapacityDirectiveLogic.NeedsConfirm(cAi), ref fail);

            // 预览与执行同源：PlanLine 说的段数必须就是 ResolveDeps 会用的段数
            var cPlan = CapacityDirectiveLogic.Parse("加密到每天3班");
            var cPlanRoute = MakeRoute("SZ900", "08:00", "10:30", destination: "PEK");
            string planLine = CapacityDirectiveLogic.PlanLine(cPlanRoute, cPlan);
            Check("预览含目标段数、时段与当前班次（与实际执行同源）",
                planLine.Contains("3 段往返") && planLine.Contains("08:00")
                && planLine.Contains("当前 1 段"), ref fail);
            Console.WriteLine("  R19 预览示例：" + planLine);
            Check("[回归] 无航线上下文时预览不崩（换线/刷新间隙会走到，过去这里会 NRE）",
                CapacityDirectiveLogic.PlanLine(null, cPlan).Contains("3 段往返")
                && CapacityDirectiveLogic.ConfirmPrompt(null, cVague, false).Length > 0, ref fail);
            string confirmPrompt = CapacityDirectiveLogic.ConfirmPrompt(null, cVague, false);
            Console.WriteLine("  R19 确认示例：" + confirmPrompt.Replace("\n", " / "));
            Check("待确认提示：说明没读懂什么 + 给改写范例 + 标注解析通道",
                confirmPrompt.Contains("没读懂") && confirmPrompt.Contains("加密到每天")
                && confirmPrompt.Contains("关键词兜底"), ref fail);
            Check("待确认提示在分析服务可用时标注「分析服务」",
                CapacityDirectiveLogic.ConfirmPrompt(null, cVague, true).Contains("分析服务"), ref fail);

            string hint = CapacityDirectiveLogic.KeywordHint();
            Check("规范词速查含增/减/维持/时段/班期五类关键词",
                hint.Contains("加密") && hint.Contains("削减") && hint.Contains("保持")
                && hint.Contains("早班") && hint.Contains("每日"), ref fail);
            Check("规范词速查写明了「时段 ↔ 时刻」的对应",
                hint.Contains("08:00") && hint.Contains("12:00")
                && hint.Contains("18:00") && hint.Contains("22:00"), ref fail);

            // —— 排班改稿通道（`ApplyFromCeoNote`）的两条口径 ——
            // ① 显式时段不能被当成「随便挑」丢掉；② 读不出任何可执行信息时不得静默重掷时段。
            OpsScheduleLogic.ParseCeoNote("换到红眼", out bool redEyeAllDay, out bool redEyeMax);
            var redEyeDir = CapacityDirectiveLogic.Parse("换到红眼");
            Check("[回归] 「换到红眼」：非全天，但显式时段被读出（旧版会当「随便挑」按 revision 取模丢掉）",
                !redEyeAllDay && !redEyeMax && redEyeDir.deps != null
                && redEyeDir.deps.Length == 1 && redEyeDir.deps[0] == "22:00", ref fail);

            bool Understood(string note)
            {
                var g = CapacityDirectiveLogic.Parse(note);
                return g.maximize || !string.IsNullOrEmpty(g.days)
                       || (g.deps != null && g.deps.Length > 0);
            }
            Check("排班改稿：说了时段/班期/铺满 → 视为读懂，放行执行",
                Understood("换到红眼") && Understood("改成一三五") && Understood("早班到红眼都排上"),
                ref fail);
            Check("[回归] 排班改稿：说了话但读不出任何可执行信息 → 拒编（旧版静默按 revision 重掷一个时段）",
                !Understood("换 4 号机") && !Understood("看着办吧"), ref fail);

            var cKeepDesc = CapacityDirectiveLogic.Parse("维持现状不变");
            Check("[回归] 维持现状的回显不再自相矛盾地说「在现有基础上加 1 段往返」",
                !cKeepDesc.analysis.Contains("加 1 段"), ref fail);
            var cVagueDesc = CapacityDirectiveLogic.Parse("随你看着办");
            Check("[回归] 未读懂的回显如实写「未明确（待确认）」",
                cVagueDesc.analysis.Contains("未明确"), ref fail);

            R20LearnedLexicon(ref fail);
            R21FlightProposals(ref fail);
            R37SlotClock(ref fail);
            R38SlotCandidates(ref fail);
            R39SlotBothEnds(ref fail);
            R24AircraftNomination(ref fail);
            R25NoDeadBand(ref fail);
            R28StatedTarget(ref fail);
            R40PlaneSilhouettes(ref fail);

            Console.WriteLine(fail == 0 ? "SLOTCHECK OK" : $"SLOTCHECK FAIL {fail}");
            return fail == 0 ? 0 : 1;
        }

        // ———————————————————— R25 远程线「可指飞」死区（2026-10-13 CEO 报障） ————————————————————
        //
        // 报障原文：FRA–ATL 7,409 km，运营部逐机核对后「机队里没有能飞本线的飞机」，
        // 唯一的宽体候选 B-2007 被「值勤超限：07:00 报到 → 次日 02:28 落地 共 19.5h > 上限 15h」否掉。
        //
        // 根因不是航程，也不是「航班时间写错」：值勤预算把去程+回程合并成一个值勤期算，
        // 而外站过夜口径原先只在「单段 > 9h」才启用 —— 7,409 km 折算 8.72h 差 0.28h 够不着门槛，
        // 于是被按当天往返硬算，必然超限。结果是 **5,500–7,650 km 整段死区**：宽体航程够、
        // 值勤却超限；窄体航程又不够 → 任何机型都指派不进去。
        static void R25NoDeadBand(ref int fail)
        {
            Console.WriteLine("\nR25 远程线可指飞（死区 / 归因 / 需求口径）");

            // —— 1. 无死区不变量：3,000–12,500 km 每 250 km，至少有一种机型飞得了 ——
            string[] types = { "A320neo", "A321LR", "A330-300", "A350-900", "B787-9", "B777-300ER" };
            var holes = new List<string>();
            for (int km = 3000; km <= 12500; km += 250)
            {
                bool any = false;
                foreach (var t in types) if (CanFlyStub(km, t)) { any = true; break; }
                if (!any) holes.Add($"{km} km");
            }
            Check($"3,000–12,500 km 不存在「所有机型都派不进」的死区（实测空洞："
                  + (holes.Count == 0 ? "无" : string.Join("、", holes)) + "）", holes.Count == 0, ref fail);

            // —— 2. 判据是结果导向（当天往返装不下 → 外站过夜），不再是「单段 > 9h」的魔数 ——
            var atl = MakeProbeRoute(7409, "A330-300");
            Check($"FRA–ATL 7,409 km（单程 {OpsRotationLogic.BlockOf(atl) / 60f:0.##}h < 9h 魔数）现在可指派",
                CanFlyStub(7409, "A330-300") && OpsRotationLogic.BlockOf(atl) < OpsRotationLogic.LongHaulLegMinutes, ref fail);
            // 真·超远程：单段本身就超值勤 —— 外站过夜也救不了，必须如实拒绝（别把驳回吞成通过）
            var ultra = MakeProbeRoute(14500, "A350-900ULR");
            bool ultraOk = CanFlyStub(14500, "A350-900ULR");
            string ultraWhy = ProbeWhy(14500, "A350-900ULR");
            Check($"[关键] 超远程 14,500 km（单段 {OpsRotationLogic.BlockOf(ultra) / 60f:0.#}h，连单段都超值勤）"
                  + $"航程够也必须拒绝：可飞={ultraOk} 原因=「{ultraWhy}」",
                OpsScheduleLogic.RangeCovers("A350-900ULR", ultra) && !ultraOk
                && ultraWhy.Contains("值勤超限") && ultraWhy.Contains("单段"), ref fail);

            // —— 3. 归因：真正拦因要落到「值勤超限」，且不得再劝 CEO 去买更远航程的飞机 ——
            var km7409 = MakeProbeRoute(7409, "A330-300");
            string dutyBody = AircraftNominationLogic.FormatBlockedBody(null, km7409, new List<string>
            {
                "B-2000　A320neo　航程不足（6300 km＋减载余量 50 km < 本线 7409 km）",
                "B-2007　A330-300　班次 1（早班 08:00）排不进：值勤超限：07:00 报到 → 次日 02:28 落地 共 19.5h > 上限 15h"
            });
            Check("汇报件点明真正拦因是值勤超限（旧版一律归成航程）",
                dutyBody.Contains("值勤超限") && dutyBody.Contains("真正拦因"), ref fail);
            Check("[回归] 拦因不是航程时，不得再给「采购/租赁满足航程的机型」这条建议（照办＝白买飞机）",
                !dutyBody.Contains("采购/租赁满足航程的机型"), ref fail);

            var km12000 = MakeProbeRoute(12000, "B777-300ER");
            string rangeBody = AircraftNominationLogic.FormatBlockedBody(null, km12000, new List<string>
            {
                "B-1　A320neo　航程不足（6300 km＋减载余量 50 km < 本线 12000 km）"
            });
            Check("拦因确实是航程时，仍给出采购/湿租建议",
                rangeBody.Contains("航程不足") && rangeBody.Contains("采购/租赁满足航程的机型"), ref fail);
            Check("汇报件始终写明「无需批复」",
                dutyBody.Contains("无需批复") && rangeBody.Contains("无需批复"), ref fail);

            // —— 4. 需求口径：装不下就说「缺班次」，不再印「座级 ≥ 26088 座」这种没有的飞机 ——
            string needSeats = AircraftNominationLogic.SeatDemandNote(81732f / 4f, 81732f);
            Check("[回归] 单班需求超出在册最大座级时，不许再写「座级 ≥ 26088 座」",
                !needSeats.Contains("座级 ≥ 26") && !needSeats.Contains("座级 ≥ 2"), ref fail);
            Check("改为说「缺的是班次」并给出所需周班次",
                needSeats.Contains("缺班次") && needSeats.Contains("班/周"), ref fail);
            string okSeats = AircraftNominationLogic.SeatDemandNote(240f, 240f * 7f);
            Check("需求装得下时照常说座级（含软顶上座率换算）",
                okSeats.Contains("单班座级 ≥ 273 座"), ref fail);
        }

        /// <summary>R25 用：距离/机型定的探针航线（班期与轮挡真值一致，不吃历史占位跨度）。</summary>
        static RouteData MakeProbeRoute(int km, string type)
        {
            var r = new RouteData
            {
                routeId = $"R-R25-{km}-{type}",
                origin = "FRA",
                destination = "ATL",
                distanceKm = km,
                aircraftType = type,
                flightNumber = "FR106",
                registration = "",
                depTime = "08:00",
                arrTime = "",
                daysOfWeek = "每日",
                weeklyFlights = 7,
                groundStaffAssigned = 4,
                baseDailyProfit = 10f,
                dailyProfit = 10f
            };
            r.SyncFlightsFromPrimary();
            return r;
        }

        static bool CanFlyStub(int km, string type)
            => ProbeTake(km, type, out _);

        static string ProbeWhy(int km, string type)
        {
            ProbeTake(km, type, out string why);
            return why;
        }

        /// <summary>问权威判据本身（CanTake）：这一架飞不飞得了本线的一个往返。</summary>
        static bool ProbeTake(int km, string type, out string why)
        {
            var route = MakeProbeRoute(km, type);
            if (!OpsScheduleLogic.RangeCovers(type, route)) { why = "航程不足"; return false; }
            var ac = new Aircraft { registration = "B-9999", type = type, isActive = true,
                                    status = AircraftStatuses.Airworthy, age = 2 };
            var plan = new OpsRotationLogic.RotationPlan();
            return OpsRotationLogic.CanTake(route, plan, ac, "08:00", null, false, out why, out _);
        }

        // ———————————————————— R20 运力指令「记忆」（已确认原话） ————————————————————
        //
        // 记忆的存储归宿主（Unity 侧 DirectiveLexicon，走 PlayerPrefs），判定归纯函数层。
        // 这里注入一个桩，把「记忆怎么参与解析」这件事离线钉住 —— 不碰 UnityEngine。
        static readonly Dictionary<string, CapacityDirectiveLogic.LearnedDirective> Stub = new();

        static bool StubLookup(string norm, out CapacityDirectiveLogic.LearnedDirective learned)
            => Stub.TryGetValue(norm, out learned);

        static void R20LearnedLexicon(ref int fail)
        {
            Console.WriteLine("R20 运力指令记忆（已确认原话 → 关键词）");
            Stub.Clear();
            Stub["把班次收一收"] = new CapacityDirectiveLogic.LearnedDirective
            { intent = "reduce", waves = 1 };

            CapacityDirectiveLogic.SetLearnedLookup(StubLookup);
            try
            {
                // —— 归一化：同一句话的不同写法必须落到同一个键 ——
                Check("归一化：首尾/连续空白压平",
                    CapacityDirectiveLogic.NormalizeRaw("  把班次   收一收 ") == "把班次 收一收", ref fail);
                Check("归一化：中文标点丢弃",
                    CapacityDirectiveLogic.NormalizeRaw("把班次，收一收。") == "把班次收一收", ref fail);
                Check("归一化：保留冒号（时刻 08:00 靠它）",
                    CapacityDirectiveLogic.NormalizeRaw("换到 08:00。") == "换到 08:00", ref fail);
                Check("归一化：空输入安全",
                    CapacityDirectiveLogic.NormalizeRaw(null) == "" && CapacityDirectiveLogic.NormalizeRaw("   ") == "", ref fail);

                // —— 记忆命中：这是玩家自己教过的说法，必须认 ——
                var hit = CapacityDirectiveLogic.Parse("把班次收一收。");   // 带标点，归一后应命中
                Check("记忆命中：拿到玩家确认过的方向与量",
                    hit.fromLearned && hit.intentKnown && hit.intent == "reduce" && hit.waves == 1, ref fail);
                Check("记忆命中：不再需要确认（玩家已经教过一次，不该再问第二遍）",
                    !CapacityDirectiveLogic.NeedsConfirm(hit), ref fail);
                Check("记忆命中：解析说明标出依据是记忆",
                    hit.analysis.Contains("记忆"), ref fail);
                Check("通道标签：记忆优先于「分析服务/关键词兜底」",
                    CapacityDirectiveLogic.ChannelLabel(hit, true).Contains("记忆")
                    && CapacityDirectiveLogic.ChannelLabel(hit, false).Contains("记忆"), ref fail);
                Console.WriteLine($"  R20 记忆命中：「把班次收一收」→ intent={hit.intent} waves={hit.waves}"
                    + $"（关键词本会判 {CapacityDirectiveLogic.Parse("别的说法").intent} 那一类，即没读懂）");

                // —— 记忆压倒关键词猜测：方向以玩家教过的为准 ——
                Stub["加密到每天3班"] = new CapacityDirectiveLogic.LearnedDirective
                { intent = "reduce", waves = 1 };
                var over = CapacityDirectiveLogic.Parse("加密到每天3班");   // 关键词会读成 increase/3
                Check("[关键] 记忆优先于关键词猜测（教过 reduce 就按 reduce）",
                    over.fromLearned && over.intent == "reduce" && over.waves == 1, ref fail);

                // —— 记忆压倒模型：方向不被改写；记忆没钉住的量可由模型补 ——
                Stub["这条线收着点"] = new CapacityDirectiveLogic.LearnedDirective
                { intent = "reduce" };                                   // waves 未钉住
                var over2 = CapacityDirectiveLogic.Parse("这条线收着点");
                CapacityDirectiveLogic.MergeAiJson(over2, "{\"intent\":\"increase\",\"waves\":3,\"rationale\":\"模型想加密\"}");
                Check("[关键] 模型不得改写玩家确认过的方向",
                    over2.intent == "reduce", ref fail);
                Check("记忆没钉住量时，允许模型补上下文相关的幅度",
                    over2.waves == 3, ref fail);

                var over3 = CapacityDirectiveLogic.Parse("把班次收一收");
                CapacityDirectiveLogic.MergeAiJson(over3, "{\"intent\":\"increase\",\"waves\":3}");
                Check("[关键] 记忆钉住的量也不被模型改写（仍是 1 段）",
                    over3.intent == "reduce" && over3.waves == 1, ref fail);

                // —— 未命中：必须原样退回关键词兜底，记忆不能污染没学过的说法 ——
                var miss = CapacityDirectiveLogic.Parse("加密到每天2班");
                Check("未命中记忆：正常走关键词兜底",
                    !miss.fromLearned && miss.intent == "increase" && miss.waves == 2, ref fail);
                var miss2 = CapacityDirectiveLogic.Parse("别飞那么多了");
                Check("未命中记忆：仍未读懂的措辞照样转人工确认",
                    !miss2.fromLearned && !miss2.intentKnown
                    && CapacityDirectiveLogic.NeedsConfirm(miss2), ref fail);

                // —— 精确匹配，不做泛化：「把班次收一收」不等于「收一收」 ——
                Check("记忆只记原话，不做近义泛化（换个说法不认）",
                    !CapacityDirectiveLogic.Parse("收一收").fromLearned, ref fail);

                // —— 通道标签三态 ——
                var plain = CapacityDirectiveLogic.Parse("加密到每天3班");
                Stub.Clear();
                var plain2 = CapacityDirectiveLogic.Parse("加密到每天3班");
                Check("通道标签：默认关键词兜底 / 传入 fromAi 时报分析服务",
                    CapacityDirectiveLogic.ChannelLabel(plain2, false).Contains("关键词兜底")
                    && CapacityDirectiveLogic.ChannelLabel(plain2, true).Contains("分析服务"), ref fail);
            }
            finally
            {
                // 干净收场：记忆是全局状态，漏了会串到后面的用例
                Stub.Clear();
                CapacityDirectiveLogic.SetLearnedLookup(null);
            }

            var after = CapacityDirectiveLogic.Parse("把班次收一收");
            Check("注销记忆后：同句话退回「没读懂 → 转人工确认」",
                !after.fromLearned && !after.intentKnown
                && CapacityDirectiveLogic.NeedsConfirm(after), ref fail);
        }

        /// <summary>
        /// R21：航班时刻加机提案的纯函数段（编解码 / 时刻推算 / 去重键 / 落班可行性）。
        /// 带状态段（呈报 → 批准才落班 / 退回即无痕 / 一封累加）在 LogicSelfTests R21，那边有 GameState。
        /// </summary>
        static void R21FlightProposals(ref int fail)
        {
            Console.WriteLine("R21 加机呈报（编解码与口径）：");

            var list = new List<FlightProposalLogic.Proposal>
            {
                new FlightProposalLogic.Proposal { reg = "B-2006", slotLabel = "早班", dep = "08:00", arr = "10:30" },
                new FlightProposalLogic.Proposal { reg = "B-2007", slotLabel = "红眼", dep = "22:00", arr = "00:30" }
            };
            string csv = FlightProposalLogic.Encode(list);
            Check("编码 = `机号|时段|离港|到港`，条目间逗号", csv == "B-2006|早班|08:00|10:30,B-2007|红眼|22:00|00:30", ref fail);

            var back = FlightProposalLogic.Decode(csv);
            Check("往返一致（条数）", back.Count == 2, ref fail);
            Check("往返一致（逐字段）", back.Count == 2
                && back[0].reg == "B-2006" && back[0].slotLabel == "早班" && back[0].dep == "08:00" && back[0].arr == "10:30"
                && back[1].reg == "B-2007" && back[1].slotLabel == "红眼" && back[1].arr == "00:30", ref fail);
            Check("空值安全（无提案）", FlightProposalLogic.Encode(null) == ""
                && FlightProposalLogic.Decode(null).Count == 0 && FlightProposalLogic.Decode("").Count == 0, ref fail);
            Check("残缺片段跳过（存档脏数据不抛异常）",
                FlightProposalLogic.Decode("B-1,|,B-3|晚班").Count == 1, ref fail);
            Check("缺时刻的旧档按现档口径补齐",
                FlightProposalLogic.Decode("B-1|早班").Count == 1
                && FlightProposalLogic.Decode("B-1|早班")[0].dep == ScheduleSlots.DepOf("早班"), ref fail);

            // 去重键：机号 + **具体时刻**（★2026-10-15：由「机号 × 时段」改为「机号 × 时刻」）
            Check("去重键 = 机号|时刻", FlightProposalLogic.KeyOf("B-2006", "08:00") == FlightProposalLogic.KeyOf(list[0]), ref fail);
            Check("同时刻不同机号 = 不同键", FlightProposalLogic.KeyOf("B-1", "08:00") != FlightProposalLogic.KeyOf("B-2", "08:00"), ref fail);
            Check("同机号不同时刻 = 不同键", FlightProposalLogic.KeyOf("B-1", "08:00") != FlightProposalLogic.KeyOf("B-1", "18:00"), ref fail);
            Check("[关键] 同机号同**时段**、不同时刻 = 不同键（06:30 与 07:00 是两条槽、两个班次）",
                FlightProposalLogic.KeyOf("B-1", "06:30") != FlightProposalLogic.KeyOf("B-1", "07:00"), ref fail);
            Check("时段名入参折成该波峰点（旧界面兼容路径）",
                FlightProposalLogic.KeyOf("B-1", "早班") == FlightProposalLogic.KeyOf("B-1", ScheduleSlots.DepOf("早班")), ref fail);
            Check("IndexOf 命中/未命中", FlightProposalLogic.IndexOf(list, "B-2007", "22:00") == 1
                && FlightProposalLogic.IndexOf(list, "B-9", "22:00") == -1, ref fail);

            // 时刻推算：与轮挡同口径，且跨夜不越界
            var probe = MakeRoute("SZ500", "08:00", "10:30", "PEK");
            var made = FlightProposalLogic.Make(probe, "B-2006", "18:00");
            Check("提案离港 = 指定时刻（传时段名才折峰点）",
                made.dep == "18:00" && FlightProposalLogic.Make(probe, "B-1", "晚班").dep == ScheduleSlots.DepOf("晚班"), ref fail);
            Check("时段名由时刻反推（07:00 → 早班）", FlightProposalLogic.Make(probe, "B-1", "07:00").slotLabel == "早班", ref fail);
            Check("提案到港 = 离港 + 轮挡",
                made.arr == FlightProposalLogic.ArrAfter(made.dep, OpsEconomics.BlockHours(1940f)), ref fail);
            Check("跨夜到港不写成 25:30（回绕到次日凌晨）", FlightProposalLogic.ArrAfter("22:00", 3f) == "01:00", ref fail);
            Check("到港按 HH:mm 两位补零", FlightProposalLogic.ArrAfter("08:00", 0.5f) == "08:30", ref fail);

            // 已在飞判定：同机同时刻算重复，别的时刻不算（同一需求波里也能再排一班）
            probe.EnsureFlights();
            Check("同机同时刻 = 已在执飞", FlightProposalLogic.HasFlight(probe, "B-9001", probe.depTime), ref fail);
            Check("[关键] 同机不同时刻 = 未执飞（同一需求波内可再加密）",
                !FlightProposalLogic.HasFlight(probe, "B-9001", "07:00")
                && !FlightProposalLogic.HasFlight(probe, "B-9001", "18:00"), ref fail);
            Check("容差内视作同一班（08:05 ≈ 08:00）", FlightProposalLogic.HasFlight(probe, "B-9001", "08:05"), ref fail);
            Check("容差外视作另一班（08:20 ≠ 08:00）", !FlightProposalLogic.HasFlight(probe, "B-9001", "08:20"), ref fail);
            Check("空航线安全", !FlightProposalLogic.HasFlight(null, "B-1", "08:00"), ref fail);

            // 单班日利口径：呈报里写的与落班后写进班次的必须同源
            float pWan = FlightProposalLogic.DailyProfitOf(null, probe, "08:00");
            Check("单班日利可算且不依赖 GameState（null 安全）", pWan != 0f, ref fail);
            Check("同参数两次调用结果一致（无隐藏状态）",
                Math.Abs(FlightProposalLogic.DailyProfitOf(null, probe, "08:00") - pWan) < 0.0001f, ref fail);
            Check("需求高的时段日利更高（需求乘数确实参与了）",
                FlightProposalLogic.DailyProfitOf(null, probe, "18:00")
                > FlightProposalLogic.DailyProfitOf(null, probe, "22:00"), ref fail);
        }

        /// <summary>
        /// R37：时刻槽精确到**时刻**（2026-10-15 CEO 问询：「0630 的槽能不能再排 0700」）。
        ///
        /// 现实锚点（IATA WASG）：
        /// - 1.7.2(d)：航司不得故意在**明显不同于获批时刻**的时间运行 → 槽的身份是时刻，不是「早班」；
        /// - 1.7.2(e)：一个系列 = 同一（或约同一）时刻 + 同一星期几 + 整季规律分布；
        /// - 8.6.1：80/20 考核按**系列**算 → 06:30 天天飞、07:00 一周飞两天，本就该各判各的。
        ///
        /// 本段钉住纯函数那一半：网格步长 / 解析吸附 / 容差 / 系列身份 / 时段名兼容折算 /
        /// 免费渠道与付费渠道的分野。带状态那半（持仓、池容量、80/20 结算、闲置收回、
        /// 同段多波的排班落点）在 LogicSelfTests。
        /// </summary>
        static void R37SlotClock(ref int fail)
        {
            Console.WriteLine("\nR37 时刻槽精确到时刻（网格 / 容差 / 系列身份 / 双渠道）：");

            // —— 1. 时刻网格：30 分步长，落在需求波窗口内，含窗口首末刻 ——
            Check("网格步长 = 30 分钟、一条持仓可服务的偏差 = 15 分钟",
                ScheduleSlots.SlotGridStepMinutes == 30 && ScheduleSlots.SlotToleranceMinutes == 15, ref fail);
            var morning = ScheduleSlots.SlotGridLabels(0);
            Check("[关键] 早班网格 = 06:30 / 07:00 / 07:30 / 08:00 / 08:30 / 09:00 / 09:30",
                morning.Count == 7 && morning[0] == "06:30" && morning[1] == "07:00"
                && morning[2] == "07:30" && morning[3] == "08:00" && morning[6] == "09:30", ref fail);
            Check("[关键] 同一需求波能排多波（早班可选时刻 ≥ 4 个；旧口径一天一段只出一波）",
                morning.Count >= 4, ref fail);
            var noon = ScheduleSlots.SlotGridLabels(1);
            var night = ScheduleSlots.SlotGridLabels(3);
            Check("午班网格 11:30–14:00 含末刻", noon.Count >= 4 && noon[0] == "11:30"
                && noon[noon.Count - 1] == "14:00", ref fail);
            Check("红眼网格 20:00–23:00 含末刻", night.Count >= 4 && night[0] == "20:00"
                && night[night.Count - 1] == "23:00", ref fail);
            bool allInWindow = true;
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                var grid = ScheduleSlots.SlotGrid(b);
                for (int k = 0; k < grid.Count; k++)
                    if (!ScheduleSlots.InWindow(b, grid[k])) allInWindow = false;
            }
            Check("网格点全部落在各自的窗口内（不会给出飞不了的需求波）", allInWindow, ref fail);

            // —— 2. 解析 / 格式化 / 环状偏差 ——
            Check("`HH:mm` 解析 ↔ 格式化互逆", ScheduleSlots.ParseClock("07:30") == 450
                && ScheduleSlots.FormatClock(450) == "07:30", ref fail);
            Check("脏时刻解析成 −1（不抛异常，脏存档不炸）",
                ScheduleSlots.ParseClock("") < 0 && ScheduleSlots.ParseClock("abc") < 0
                && ScheduleSlots.ParseClock("25:00") < 0 && ScheduleSlots.ParseClock("07:60") < 0
                && ScheduleSlots.ParseClock("晚班") < 0, ref fail);
            Check("格式化回绕（1440 → 00:00，−30 → 23:30）",
                ScheduleSlots.FormatClock(1440) == "00:00" && ScheduleSlots.FormatClock(-30) == "23:30", ref fail);
            Check("环状偏差：06:30 与 07:00 差 30 分；23:50 与 00:10 差 20 分",
                ScheduleSlots.ClockDiff("06:30", "07:00") == 30
                && ScheduleSlots.ClockDiff("23:50", "00:10") == 20, ref fail);
            Check("[关键] 容差语义：06:30 的槽接不了 07:00 的班，但接得了 06:35 的班",
                ScheduleSlots.ClockDiff("06:30", "07:00") > ScheduleSlots.SlotToleranceMinutes
                && ScheduleSlots.ClockDiff("06:30", "06:35") <= ScheduleSlots.SlotToleranceMinutes, ref fail);
            Check("吸附到最近的网格点（07:14 → 07:00 / 07:20 → 07:30）",
                ScheduleSlots.FormatClock(ScheduleSlots.SnapToGrid(0, 7 * 60 + 14)) == "07:00"
                && ScheduleSlots.FormatClock(ScheduleSlots.SnapToGrid(0, 7 * 60 + 20)) == "07:30", ref fail);

            // —— 3. 旧档兼容：时段名折成该波峰点，且幂等 ——
            Check("时段名 → 峰点：早班 08:00 / 午班 12:00 / 晚班 18:00 / 红眼 22:00",
                ScheduleSlots.BandNameToClock("早班") == "08:00"
                && ScheduleSlots.BandNameToClock("午班") == "12:00"
                && ScheduleSlots.BandNameToClock("晚班") == "18:00"
                && ScheduleSlots.BandNameToClock("红眼") == "22:00", ref fail);
            Check("已是时刻则原样返回（幂等，迁移可反复跑）",
                ScheduleSlots.BandNameToClock("07:00") == "07:00"
                && ScheduleSlots.BandNameToClock(ScheduleSlots.BandNameToClock("晚班")) == "18:00", ref fail);
            Check("短名兼容（早/中/晚/夜 与 早班 同源）",
                ScheduleSlots.BandNameToClock("早") == ScheduleSlots.BandNameToClock("早班")
                && ScheduleSlots.BandNameToClock("夜") == ScheduleSlots.BandNameToClock("红眼"), ref fail);

            // —— 4. 需求系数按**时刻**取（06:30 与 09:00 同属早班，吃同一波系数）——
            Check("需求系数按时刻归波：06:30 与 08:00 同系数，18:00 最高、22:00 最低",
                Math.Abs(ScheduleSlots.DemandOfTime("06:30") - ScheduleSlots.DemandOfTime("08:00")) < 0.0001f
                && ScheduleSlots.DemandOfTime("18:00") > ScheduleSlots.DemandOfTime("06:30")
                && ScheduleSlots.DemandOfTime("22:00") < ScheduleSlots.DemandOfTime("06:30"), ref fail);
            Check("时段名入参仍按旧口径取系数（兼容路径不塌）",
                Math.Abs(ScheduleSlots.DemandOfTime("晚班") - ScheduleSlots.DemandOf("晚班")) < 0.0001f, ref fail);

            // —— 5. 系列身份：机场 × **时刻** × 星期 ——
            string s0630 = SlotUsageLogic.SeriesLabel("FRA", "06:30", 0);
            string s0700 = SlotUsageLogic.SeriesLabel("FRA", "07:00", 0);
            Check("[关键] 系列标签写具体时刻、不写「早班」",
                s0630.Contains("06:30") && !s0630.Contains("早班"), ref fail);
            Check("[关键] 同机场同星期、不同时刻 = 两个系列（06:30 ≠ 07:00，各判各的 80/20）",
                s0630 != s0700, ref fail);
            Check("同机场同时刻、不同星期 = 两个系列", s0630 != SlotUsageLogic.SeriesLabel("FRA", "06:30", 1), ref fail);
            Check("时段名入参按峰点折算（`早班` 与 `08:00` 同系列）",
                SlotUsageLogic.SeriesLabel("FRA", "早班", 3) == SlotUsageLogic.SeriesLabel("FRA", "08:00", 3), ref fail);
            Check("星期是 0–6 的整数（0 = 周一）",
                SlotUsageLogic.SeriesLabel("FRA", "06:30", 0).Contains("周一")
                && SlotUsageLogic.SeriesLabel("FRA", "06:30", 6).Contains("周日"), ref fail);

            // —— 6. 系列编解码：时刻写进邮件字段后能原样回溯 ——
            var seriesList = new List<SlotSeries>
            {
                new SlotSeries { airport = "FRA", time = "06:30", weekday = 0 },
                new SlotSeries { airport = "CDG", time = "18:00", weekday = 3 }
            };
            string seriesCsv = SlotUsageLogic.EncodeSeries(seriesList);
            var seriesBack = SlotUsageLogic.DecodeSeries(seriesCsv);
            Check("系列编解码 = `机场|时刻|星期`，往返一致",
                seriesCsv == "FRA|06:30|0,CDG|18:00|3" && seriesBack.Count == 2
                && seriesBack[0].time == "06:30" && seriesBack[1].time == "18:00", ref fail);
            Check("系列解码：既非时刻也非旧下标（0–3）的条目跳过（脏存档不炸）",
                SlotUsageLogic.DecodeSeries("FRA|99:99|0,CDG|18:00|9").Count == 0, ref fail);
            Check("[关键] 系列解码兼容旧档的时段下标写法（`0` → 08:00、`3` → 22:00）",
                SlotUsageLogic.DecodeSeries("FRA|0|0").Count == 1
                && SlotUsageLogic.DecodeSeries("FRA|0|0")[0].time == "08:00"
                && SlotUsageLogic.DecodeSeries("FRA|3|0")[0].time == "22:00", ref fail);
            Check("系列解码把实际离港时刻归到网格（06:14 → 06:30 所在网格 06:30）",
                SlotUsageLogic.DecodeSeries("FRA|06:14|0").Count == 1
                && SlotUsageLogic.DecodeSeries("FRA|06:14|0")[0].time == SlotUsageLogic.SeriesTimeOf("06:14"), ref fail);
            Check("系列列表空值安全", SlotUsageLogic.EncodeSeries(null) == ""
                && SlotUsageLogic.DecodeSeries(null).Count == 0, ref fail);

            // —— 7. 两条渠道是两码事：协调池分配免费，二级市场购入付费 ——
            // 现实（WASG 5.1 / 8.1）：协调人从池里分配是行政职能，不收费；收钱的只有航司之间的二次交易。
            // 本作把「免费渠道」钉在 apply_slot 这个动作上，`buy_slot` 才是付款那条。
            Check("[关键] 协调池申请是免费渠道（apply_slot）",
                InboxManager.ApplySlotAction == "apply_slot", ref fail);
            Check("[关键] 免费渠道与付费渠道不是同一个动作（否则会重复收一道钱）",
                InboxManager.ApplySlotAction != "buy_slot", ref fail);
            Check("二手购入有市价（付费渠道才有价，免费渠道不该照价扣款）",
                StrategyEconomics.SlotValueWan("FRA", 0) > 0f, ref fail);
        }

        /// <summary>
        /// R38：加班**时刻备选**（2026-10-15 CEO 第十五次问询：「是否应该在邮件提供几个备选时刻槽，并分析优缺点」）。
        ///
        /// 问询背后的实情：改前运营部只报「办不成」——拿 CEO 指名的那一刻去问，问不出来就把缺槽记在那一刻上，
        /// 邮件申请的也就是那一刻，CEO 从头到尾看不见「还有别的选法、代价分别是什么」。而排班器是**从峰点
        /// 往后搜**的，于是「手上握着 07:30 的槽、调令写 08:00」会被报成「08:00 缺槽要去买」，是一张**假缺口**。
        ///
        /// 本段钉住纯函数那一半：整窗口网格（含**早于**峰点的时刻）/ 备选身份是往返时刻对 /
        /// 排序口径 / 编解码 / 脏数据 / 通知口径无备选不得冒充有备选 / 申请时刻一律吸附到系列。
        /// 带状态那半（两端可得性档 → 备选生成 → CEO 点选 → 交割后自动重排）在 LogicSelfTests R38。
        /// </summary>
        static void R38SlotCandidates(ref int fail)
        {
            Console.WriteLine("\nR38 加班时刻备选（整窗口网格 / 往返对 / 排序 / 编解码 / 吸附）：");

            // —— 1. [关键] 申请时刻一律吸附到**系列**时刻，绝不开 07:40 这种非系列单 ——
            // 机场从不公布 07:40 的系列；排班器给的是 5 分钟粒度的实际离港（07:40），
            // 直接拿去申请会开出一张买不到对应系列的**空单**。
            Check("[关键] 申请时刻必须吸附到系列网格（07:40 → 07:30，不是照抄 07:40）",
                RoutePermitLogic.SlotRequestClock("07:40") == "07:30", ref fail);
            Check("[关键] 吸附取最近的系列点（07:46 → 08:00）",
                RoutePermitLogic.SlotRequestClock("07:46") == "08:00", ref fail);
            Check("吸附幂等（已是系列时刻则原样返回）",
                RoutePermitLogic.SlotRequestClock("07:30") == "07:30", ref fail);
            string low = RoutePermitLogic.SlotRequestClock("15:00");
            Check("低谷时刻吸附到最近的系列点（15:00 → 14:00，且落在某个需求波窗口内）",
                low == "14:00" && ScheduleSlots.InWindow(1, ScheduleSlots.ParseClock(low)), ref fail);
            Check("脏时刻不炸也不改写成别的时刻（解析不出则原样返回，交由调用方拦）",
                RoutePermitLogic.SlotRequestClock("") == "" && RoutePermitLogic.SlotRequestClock("abc") == "abc",
                ref fail);

            // —— 2. 备选身份是**一段往返时刻对**，不是「一个时刻」 ——
            // 去程离港定了，回程离港由轮挡 + 过站推出；两端各有各的池况与价格，
            // 备选之间真正的差异大多出自对端那一刻，而不是「早 30 分钟」本身。
            var opt = new SlotCandidateLogic.SlotOption
            {
                depClock = "07:30",
                pegClock = "08:00",
                band = 0,
                outAirport = "FRA",
                inAirport = "CDG",
                inClock = "10:15",
                outState = SlotCandidateLogic.EndReady,
                inState = SlotCandidateLogic.EndApply,
                priceWan = 46f,
                demand = 1.32f,
                aircraftOk = true,
                readyNow = false,
                sameDay = true,
                windowMin = 300
            };
            string one = SlotCandidateLogic.Encode(new List<SlotCandidateLogic.SlotOption> { opt });
            var back = SlotCandidateLogic.Decode(one);
            Check("[关键] 备选编解码往返一致（时刻 / 两端档 / 回程时刻 / 波次 / 价 / 系数 / 两枚布尔）",
                back.Count == 1 && back[0].depClock == "07:30"
                && back[0].outState == SlotCandidateLogic.EndReady
                && back[0].inState == SlotCandidateLogic.EndApply
                && back[0].inClock == "10:15" && back[0].band == 0
                && Math.Abs(back[0].priceWan - 46f) < 0.01f
                && Math.Abs(back[0].demand - 1.32f) < 0.01f
                && !back[0].readyNow && back[0].aircraftOk, ref fail);
            Check("备选条数按条编码（两条 → 两条；空表 → 空串）",
                SlotCandidateLogic.Decode(SlotCandidateLogic.Encode(new List<SlotCandidateLogic.SlotOption>
                    { opt, new SlotCandidateLogic.SlotOption { depClock = "09:00" } })).Count == 2
                && SlotCandidateLogic.Encode(new List<SlotCandidateLogic.SlotOption>()) == ""
                && SlotCandidateLogic.Encode(null) == "", ref fail);
            Check("[关键] 首条 = 运营部推荐（「批准」不带时刻时默认走它，故首条身份必须能被点选精确指回）",
                one.Length > 0 && SlotCandidateLogic.IndexOf(back, back[0].depClock) == 0, ref fail);

            // —— 3. 点选必须精确落在那一条上，认不出就打回（不替 CEO 挑相近的买下去）——
            var three = new List<SlotCandidateLogic.SlotOption>
            {
                new SlotCandidateLogic.SlotOption { depClock = "07:00" },
                new SlotCandidateLogic.SlotOption { depClock = "07:30" },
                new SlotCandidateLogic.SlotOption { depClock = "09:00" }
            };
            Check("[关键] 点选按时刻精确命中（07:30 → 第 2 条）",
                SlotCandidateLogic.IndexOf(three, "07:30") == 1, ref fail);
            Check("[关键] 不在备选表里的时刻一律认不出（返回 −1 → 调用方打回，不猜相近的）",
                SlotCandidateLogic.IndexOf(three, "08:00") < 0
                && SlotCandidateLogic.IndexOf(three, "") < 0
                && SlotCandidateLogic.IndexOf(null, "07:00") < 0, ref fail);

            // —— 4. 脏数据不炸：解不出的片段跳过，空/残缺串给空表 ——
            // 备选的身份是**第一字段（时刻）**；没有时刻的片段认不出是哪条备选，只能跳过 ——
            // 编不出身份就不要放进表里（否则点选时会出现一条点不中的空候选）。
            Check("脏备选串不炸（空串 / 只有时刻没有两端档 / 空时刻 / 全空字段）",
                SlotCandidateLogic.Decode("").Count == 0
                && SlotCandidateLogic.Decode("07:30").Count == 0
                && SlotCandidateLogic.Decode(";|;;").Count == 0
                && SlotCandidateLogic.Decode("|ready|ready").Count == 0
                && SlotCandidateLogic.Decode(";07:00|ready|ready;").Count == 1, ref fail);

            // —— 5. 单端可得性档只说事实，四档各有其名 ——
            Check("单端可得性四档命名（即刻可排 / 需申请 / 拿不到 / 无需槽）",
                SlotCandidateLogic.EndStateName(SlotCandidateLogic.EndReady) == "即刻可排"
                && SlotCandidateLogic.EndStateName(SlotCandidateLogic.EndApply) == "需申请"
                && SlotCandidateLogic.EndStateName(SlotCandidateLogic.EndBlocked) == "拿不到"
                && SlotCandidateLogic.EndStateName(SlotCandidateLogic.EndNone) == "无需槽", ref fail);
            Check("两端都不必办 = 即刻可排（readyNow）；缺任一档 = 需要动作",
                new SlotCandidateLogic.SlotOption { readyNow = true }.NeedsAction == false
                && new SlotCandidateLogic.SlotOption { readyNow = false }.NeedsAction == true, ref fail);

            // —— 6. 通知口径：有备选才叫「请选定一个」，一个都排不出就说「无槽可得（汇报）」 ——
            // 绝不开一张**没有选项**的选择题：那是把「办不成」包装成需要 CEO 决策的样子。
            var route = MakeRoute("FR510", "07:30", "10:15", "FRA", "CDG");
            string withOpts = SlotCandidateLogic.Subject(route, false, 3);
            string noOpts = SlotCandidateLogic.Subject(route, true);
            Check("[关键] 有备选 → 「3 个备选（请选定一个）」",
                withOpts.Contains("3 个备选") && withOpts.Contains("请选定一个"), ref fail);
            Check("[关键] 一个备选都排不出 → 「无槽可得（汇报）」，措辞里不许出现「请选定」",
                noOpts.Contains("无槽可得") && !noOpts.Contains("请选定") && !noOpts.Contains("备选"), ref fail);

            // —— 7. 正文必须**逐条列出利弊**，且「你原本要的那一刻」要被标出来 ——
            // 没有利弊分析的备选表只是把选择困难推给 CEO；没有「原计划」标记则看不出是往前还是往后挪。
            var routeB = MakeRoute("FR520", "08:00", "10:45", "FRA", "CDG");
            var shown = new List<SlotCandidateLogic.SlotOption>
            {
                new SlotCandidateLogic.SlotOption
                {
                    depClock = "07:30", pegClock = "08:00", band = 0, outAirport = "FRA", inAirport = "CDG",
                    inClock = "10:15", outState = SlotCandidateLogic.EndReady, inState = SlotCandidateLogic.EndReady,
                    readyNow = true, aircraftOk = true, sameDay = true, windowMin = 300,
                    demand = 1.32f, pros = "有利一", cons = "代价一"
                }
            };
            string body = SlotCandidateLogic.FormatBody(null, routeB, shown, "08:00");
            Check("[关键] 正文逐条给出有利 / 代价（备选件不是一张只有时刻的清单）",
                body.Contains("有利一") && body.Contains("代价一") && body.Contains("候选 1"), ref fail);
            Check("[关键] 正文标出「较你原本要的 08:00 早 30 分」（否则看不出往前还是往后挪）",
                body.Contains("08:00") && body.Contains("早"), ref fail);
            Check("正文写清口径（一停站一对槽：到场 + 离场 / 经停站要 2 对 / 免费与付费两条渠道）",
                body.Contains("一停站一对槽") && body.Contains("本场端") && body.Contains("对端")
                && body.Contains("经停站") && body.Contains("协调池") && body.Contains("二级市场"), ref fail);
            Check("正文交代批准后的动作（开申请单 → 交割 → 自动重排并呈报）",
                body.Contains("批准后的动作") && body.Contains("自动重排"), ref fail);
            // ★T05.6：每个备选都要有「预计办理时点」，且不得只剩「下季」这种相对说法
            Check("[关键] 每个备选给出「办理时点」一行（T05.6：旧版只有「下季交割」，玩家换算不出赛季）",
                body.Contains("办理时点"), ref fail);
            Check("无 GameState 的离线路径也要给得出时点（退回「本季 / 下季」，不得空着）",
                body.Contains("办理时点　本季") || body.Contains("办理时点　下季"), ref fail);

            // —— 8. 受阻汇报件必须给「下一步」，不许只报坏消息 ——
            string blocked = SlotCandidateLogic.FormatBlockedBody(null, routeB, "08:00");
            Check("受阻汇报给三条下一步（等释放 / 换波 / 查持仓）+ 明示无需批复",
                blocked.Contains("下一步") && blocked.Contains("无需批复"), ref fail);
        }

        /// <summary>
        /// R39：**每一个停站**都要办（2026-10-16 CEO 报障：「增加航班量需申请时刻槽，而审批中只有起飞机场的时刻槽，
        /// 没有到达机场的时刻槽？？」）。
        ///
        /// 根因不在「落地机场不需要槽」（n77 的已入账时刻槽里 LHR/CDG/AMS/IST 都躺着槽），而在**归因与办单共用一个字段**：
        /// `DiagnoseClock` 把 `TryShortClock` 返回的**第一个**缺端写进 `WaveShortage.slotAirport`，
        /// 而 `QueueSlotApplications` 直接拿这个字段当清单用 —— 本场端在需求列表里排下标 0、新波次在本场必然没有持仓，
        /// 于是「只办一端」在实际运行中**恒定**表现为「只办起飞机场」，对端一次都没被提起过。
        ///
        /// ★2026-10-16（第十八次问询）追加：清单的单位从「两端」升到**逐停站** —— 经停线的一次往返要 4 条
        /// （经停端 2 条，见 R14 的经停段），故 `WaveEndNeeds` 逐条 `StationCalls` 开列，一条都不能漏。
        ///
        /// 本段钉住纯函数那一半：`WaveEndNeeds` 一次给全各停站（本场端在前 / 对端在后 / 时刻均已吸附到系列网格 /
        /// 回程时刻随去程走 / 非协调站不进清单）。带状态那半（真开出多张单、单号与去重按航线、呈报逐停站列）在
        /// LogicSelfTests P3。
        /// </summary>
        static void R39SlotBothEnds(ref int fail)
        {
            Console.WriteLine("\nR39 往返两端都要办（两端清单 / 时刻吸附 / 回程随去程 / 非协调端不办）：");

            // 两端都是协调机场（FRA 与 CDG 的客流等级都是枢纽/干线），去程给一个**非系列**的 5 分钟粒度时刻。
            var route = MakeRoute("FR610", "07:40", "09:10", "CDG", "FRA");
            var ends = RoutePermitLogic.WaveEndNeeds(route, "07:40");

            // ★2026-10-16 第十八次修订（**对端刻必须随波次走**）：对端停留的两刻
            //   = 该波（吸附后的）离场 + 单程轮挡（到场）+ 过站（离场），一律再吸附到系列网格。
            //   旧口径是 `ReturnDepMinutes(dep, route.arrTime, …)` —— 把 `route.arrTime`
            //   （**首班**的到场钟点）当成探测时刻的**对端钟点**喂进 `RoundTripOf`，
            //   而 `LiveClockLogic.BlockMinutes` 会把「探测刻 − 首班到场刻」算成**跨夜环绕差**，
            //   于是整条线只有一个对端刻（= 首班到场 + 过站），且它常常荒谬到「到场早于离场」。
            //   后果不是文案难看：`RouteHasTime` 会拿这个荒谬刻去和已排班次比对，
            //   命中就判「该端有历史优先权（祖父条款）」→ `CanApplyEnd` = false →
            //   **落地机场的申请单永远开不出来**（CEO 报障「只申请起飞机场」的最后一道成因）。
            //   现在与排班器 `OpsRotationLogic.WindowOf`（dep + BlockOf + TurnOf）同口径。
            int blockMin = OpsRotationLogic.BlockOf(route);
            int turnMin = OpsRotationLogic.TurnOf(route);
            string wantIn = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(
                ScheduleSlots.ParseClock("07:30") + blockMin + turnMin));

            Check("[关键] 一个往返查出**两条**槽需求：本场端 + 对端",
                ends.Count == 2, ref fail);
            Check("[关键] 第一条是**本场端**（下标 0；旧版只读 TryShortClock 的返回值 → 恒定只有它）",
                ends.Count > 0 && ends[0].airport == route.origin && !ends[0].inbound, ref fail);
            Check("[关键] 第二条是**对端**（落地机场那一条，旧版一次都没开过）",
                ends.Count > 1 && ends[1].airport == route.destination && ends[1].inbound, ref fail);
            Check("[关键] 本场端时刻吸附到系列（07:40 → 07:30），不是照抄排班器的 5 分钟粒度",
                ends.Count > 0 && ends[0].time == "07:30", ref fail);
            Check("[关键] 对端时刻＝该波（吸附后）离场 + 单程轮挡 + 过站，**再吸附**到系列（各停站各按自己的系列申请）",
                ends.Count > 1 && ends[1].time == wantIn && ScheduleSlots.ParseClock(ends[1].time) >= 0, ref fail);
            Check("各停站的时段（band）各按自己的时刻反推（对端不是复用本场的时段）",
                ends.Count > 1 && ends[1].band == ScheduleSlots.BandOfDep(ends[1].time)
                && ends[0].band == ScheduleSlots.BandOfDep(ends[0].time), ref fail);

            // 口径同源：清单必须就是「排班闸门问的那一份」，不是另立一套需求。
            var raw = RoutePermitLogic.ClockNeeds(route, "07:30");
            Check("[关键] 清单与排班闸门同源：WaveEndNeeds ＝ ClockNeeds(吸附后时刻)，条数与两端逐一对应",
                ends.Count == raw.Count
                && ends[0].airport == raw[0].airport && ends[1].airport == raw[1].airport, ref fail);

            // 回程端时刻必须与排班闸门**同一处算出来**（不得在清单里另立一套回程口径）。
            Check("[关键] 回程端时刻与排班闸门同源（清单对端刻 ＝ ClockNeeds 对端刻，同一处 StationCalls 派生）",
                ends.Count > 1 && raw.Count > 1
                && ends[1].time == RoutePermitLogic.SlotRequestClock(raw[1].time), ref fail);

            // ★2026-10-16 第十八次修订的**回归钉子**：对端刻必须随波次走。
            //   旧口径下整条线共用一个对端刻，于是「晚班缺对端槽」被误判成祖父条款保住而不开单 ——
            //   这正是 CEO 报障「往返只申请了起飞机场的时刻槽」的成因，钉子留在这里防止它复活。
            var lateEnds = RoutePermitLogic.WaveEndNeeds(route, "18:00");
            Check("[关键] 对端刻**随波次走**（早班 07:30 与晚班 18:00 的对端刻不得相同 —— 旧口径下全波共用一刻）",
                ends.Count > 1 && lateEnds.Count > 1 && ends[1].time != lateEnds[1].time, ref fail);
            Check("对端**到场**刻也随波次走（到场刻同样是排班器算出来的那一刻，不是常量）",
                ends.Count > 1 && lateEnds.Count > 1 && ends[1].arrTime != lateEnds[1].arrTime, ref fail);

            // 两端是两条**各自独立**的槽：机场与时刻都不该重合（混成一条会立刻显形）。
            Check("[关键] 两端是两条独立的槽：对端机场 ≠ 本场机场，对端刻 ≠ 本场刻",
                ends.Count > 1 && ends[0].airport != ends[1].airport && ends[0].time != ends[1].time, ref fail);

            Console.WriteLine($"　两端清单（07:40 起飞）：去程离港 {ends[0].airport} {ends[0].time}"
                              + $"　回程离港 {ends[1].airport} {ends[1].time}"
                              + $"（= 07:30 + 轮挡 {blockMin} + 过站 {turnMin} 后吸附；"
                              + $"晚班 18:00 的对端刻 {lateEnds[1].time} —— 各波各不相同）");

            // 非协调/目录外机场那一端不进清单（支线机场本就无需时刻槽，别凭空开一张单）。
            var routeSmall = MakeRoute("FR620", "07:30", "09:00", "ZZZ", "FRA");
            var endsSmall = RoutePermitLogic.WaveEndNeeds(routeSmall, "07:30");
            Check("非协调（或目录外）的那一端不进清单：往返只办本场一条",
                endsSmall.Count == 1 && endsSmall[0].airport == "FRA", ref fail);

            Check("时刻读不出就返回空表（不凭空给两端清单，交由调用方如实报告）",
                RoutePermitLogic.WaveEndNeeds(route, "abc").Count == 0
                && RoutePermitLogic.WaveEndNeeds(route, "").Count == 0
                && RoutePermitLogic.WaveEndNeeds(null, "07:30").Count == 0, ref fail);
        }

        /// <summary>
        /// R24：执飞飞机提名的纯函数段（航程真值与余量方向 / 指令分流 / 混写交代 / 编解码 / 文案）。
        /// 带状态段（硬门槛逐机筛选 → 呈报不落班 → 批准才写机号 → 退回无痕 → 无机可派发汇报件）在
        /// LogicSelfTests R24，那边有 GameState。
        /// </summary>
        static void R24AircraftNomination(ref int fail)
        {
            Console.WriteLine("R24 执飞飞机提名（航程真值 / 解析分流 / 编解码）：");

            // —— 1. 航程用目录真值，且 50 km 余量必须加在**机型侧** ——
            // 旧写法 `航程 ≥ 距离 + 50` 方向反了：把 A321LR 从 7,409 km 的线上剔掉，
            // 与「保留余量就是为了别把这类机型判死」的说明自相矛盾（CEO 报障截图里正是这条线）。
            var far = MakeRoute("SZ900", "08:00", "10:30", "ATL");
            far.distanceKm = 7409f;
            Check("航程取目录真值（A321LR = 7,400 km）", Math.Abs(OpsScheduleLogic.RangeKm("A321LR") - 7400f) < 0.5f, ref fail);
            Check("航程取目录真值（A320neo = 6,300 km）", Math.Abs(OpsScheduleLogic.RangeKm("A320neo") - 6300f) < 0.5f, ref fail);
            Check("目录外机型航程不可核实（返回 0，不得兜底 8,000）",
                !OpsScheduleLogic.RangeKnown("B999") && OpsScheduleLogic.RangeKm("B999") == 0f, ref fail);
            Check("[关键] 余量加在机型侧：A321LR（7,400）够得着 7,409 km 的线", OpsScheduleLogic.RangeCovers("A321LR", far), ref fail);
            Check("[关键] 6,300 km 的窄体仍不得放行", !OpsScheduleLogic.RangeCovers("A320neo", far), ref fail);
            Check("余量有上限：7,400 + 50 也够不着 7,500 km 的线",
                !OpsScheduleLogic.RangeCovers("A321LR", MakeRouteFar("SZ901", 7500f)), ref fail);
            Check("本线需求 = 航线距离本身（文案里的「本线需 N km」与航线页一致）",
                Math.Abs(OpsScheduleLogic.RequiredRangeKm(far) - 7409f) < 0.5f, ref fail);

            // —— 2. 指令分流：派机令不该再被判成「没读懂想增班还是减班」 ——
            // ★ 这是本轮 CEO 报障的原句（截图里它被回了「没读懂…拟定加密至 1 段往返」）
            var dNom = CapacityDirectiveLogic.Parse("请安排合适的飞机执飞此航班");
            Check("[关键] 「请安排合适的飞机执飞此航班」→ intent=assign",
                dNom.intent == "assign" && dNom.intentKnown, ref fail);
            Check("[关键] 派机令不进「需确认」分支（无副作用，不需要猜方向）",
                !CapacityDirectiveLogic.NeedsConfirm(dNom), ref fail);
            Check("派机令的预览说的是「提名候选」，不是加密班次",
                CapacityDirectiveLogic.PlanLine(null, dNom).Contains("提名"), ref fail);
            Check("规范词速查里能查到「派机」（玩家得知道说什么才算数）",
                CapacityDirectiveLogic.KeywordHint().Contains("派机"), ref fail);

            string[] assignPhrases = { "换一架飞机执飞本线", "指派飞机", "派机", "无机可派", "复航" };
            bool allAssign = true;
            for (int i = 0; i < assignPhrases.Length; i++)
                if (CapacityDirectiveLogic.Parse(assignPhrases[i]).intent != "assign") allAssign = false;
            Check("同族说法一律归 assign（换一架 / 指派飞机 / 派机 / 无机可派 / 复航）", allAssign, ref fail);

            // 词表刻意不宽：单纯出现「飞机」二字是抱怨，不是改派指令（否则一句抱怨就会换掉整机队）
            var dComplain = CapacityDirectiveLogic.Parse("这架飞机排得太满");
            Check("「这架飞机排得太满」不得被判成改派（抱怨 ≠ 派机令）",
                dComplain.intent != "assign" && CapacityDirectiveLogic.NeedsConfirm(dComplain), ref fail);

            // —— 2b. 同日第二次报障：CEO 改口说「请给此航段分配可执飞的飞机」 ——
            // 一个词都不在枚举表上，于是又回到「没读懂…想增班还是减班」。枚举短语这条路有尽头，
            // 故补结构性判据（飞机身份词 + 指派动词），并把确认文案改成问「改派飞机还是调班次」。
            var dCeo2 = CapacityDirectiveLogic.Parse("请给此航段分配可执飞的飞机");
            Check("[关键] 「请给此航段分配可执飞的飞机」→ intent=assign（未枚举说法靠结构判据兜住）",
                dCeo2.intent == "assign" && dCeo2.intentKnown, ref fail);

            string[] assignVariants =
            {
                "给这条线选一架飞机", "谁来执飞这条线", "这条线换宽体飞", "用哪架飞机飞这条线",
                "把 A330 调到本线", "给此航段配机"
            };
            bool allVariant = true;
            for (int i = 0; i < assignVariants.Length; i++)
                if (CapacityDirectiveLogic.Parse(assignVariants[i]).intent != "assign") allVariant = false;
            Check("未枚举的说法也归 assign（选一架 / 谁来执飞 / 换宽体 / 用哪架 / 调 A330 / 配机）",
                allVariant, ref fail);

            // 结构判据两半缺一不可：只有身份词 → 是抱怨；只有动词 → 是排班。两者都不得判成派机。
            string[] notAssign =
            {
                "这架飞机排得太满", "这架飞机的日利用率太低", "给我看看这架飞机的维修记录",
                "换到红眼", "换到每天12班", "调整到每天2班", "调整班次到每天 2 班",
                "运力不足，加密到每天 3 班", "改成一三五", "给此航段安排一下"
            };
            bool allNot = true;
            for (int i = 0; i < notAssign.Length; i++)
                if (CapacityDirectiveLogic.Parse(notAssign[i]).intent == "assign") allNot = false;
            Check("[关键] 只有身份词（抱怨）或只有动词（排班）都不得判成派机", allNot, ref fail);

            // —— 2c. 方向没读懂时：不冒充方向，也不给「按 Enter 一眼执行」 ——
            var dUnknown = CapacityDirectiveLogic.Parse("请您斟酌一下");
            Check("[关键] 方向没读懂时不得给「按 Enter 执行」（否则 CEO 替系统的猜背书）",
                !CapacityDirectiveLogic.CanConfirmOnEnter(dUnknown), ref fail);
            string unkPrompt = CapacityDirectiveLogic.ConfirmPrompt(null, dUnknown, false);
            Check("[关键] 未读懂的确认文案不得写「加密至…」（旧版把默认增班写成「拟定」）",
                !unkPrompt.Contains("加密至") && !unkPrompt.Contains("按 Enter 执行"), ref fail);
            Check("方向已知、只是没说幅度 → 仍可 Enter 确认（回显请确认这档保留）",
                CapacityDirectiveLogic.CanConfirmOnEnter(CapacityDirectiveLogic.Parse("加密一下")), ref fail);
            Check("谈了量但没谈方向 → 回显读出量、明说方向待定",
                CapacityDirectiveLogic.Parse("把班次收到 2 段").intentKnown == false
                && CapacityDirectiveLogic.PlanLine(null, CapacityDirectiveLogic.Parse("把班次收到 2 段")).Contains("待您明确"), ref fail);
            Check("提到飞机却没读懂方向 → 问法必须问「改派执飞飞机/调整班次」",
                CapacityDirectiveLogic.MentionsAircraft("请给此航段分配可执飞的飞机")
                && CapacityDirectiveLogic.MentionsAircraft("把 A330 调过来")
                && !CapacityDirectiveLogic.MentionsAircraft("把班次收一收")
                && CapacityDirectiveLogic.ConfirmPrompt(null,
                       CapacityDirectiveLogic.Parse("嗯，这个飞机的事你看着办"), false).Contains("改派执飞飞机"), ref fail);

            // 模型无权把已明说的派机令拉回增/减班
            var dAi = CapacityDirectiveLogic.Parse("请安排合适的飞机执飞此航班");
            CapacityDirectiveLogic.MergeAiJson(dAi, "{\"intent\":\"increase\",\"waves\":1}");
            Check("[关键] 分析服务不得把派机令改写成增班", dAi.intent == "assign", ref fail);

            // —— 3. 混写交代：一句话里同时写了改派与增减班，不得静默丢掉后半句 ——
            Check("纯派机令不出现「另有班次调整」的提示",
                CapacityDirectiveLogic.MixedCapacityNote("请安排合适的飞机执飞此航班") == "", ref fail);
            Check("[关键] 「换机型并减到每天 1 班」须如实交代「只办了改派」",
                CapacityDirectiveLogic.MixedCapacityNote("换机型并减到每天 1 班").Contains("只办"), ref fail);
            Check("混写提示会出现在该指令的解读里（邮件与回执同源）",
                CapacityDirectiveLogic.Parse("换机型并减到每天 1 班").analysis.Contains("只办"), ref fail);

            // —— 4. 提名编解码（存在邮件 payloadCsv 里，跨存档往返必须一致） ——
            var cands = new List<AircraftNominationLogic.Candidate>
            {
                new AircraftNominationLogic.Candidate { reg = "B-8802", type = "A321LR", seats = 180, profitWan = 3.2f, deltaWan = -1.5f },
                new AircraftNominationLogic.Candidate { reg = "B-8803", type = "A330-300", seats = 300, profitWan = 5.1f, deltaWan = 0.4f }
            };
            string nomCsv = AircraftNominationLogic.Encode(cands);
            Check("编码 = `机号|机型|座位|投影日利|较现状`，条目间逗号",
                nomCsv == "B-8802|A321LR|180|3.20|-1.50,B-8803|A330-300|300|5.10|0.40", ref fail);
            var nomBack = AircraftNominationLogic.Decode(nomCsv);
            Check("往返一致（条数 / 逐字段）", nomBack.Count == 2
                && nomBack[0].reg == "B-8802" && nomBack[0].type == "A321LR" && nomBack[0].seats == 180
                && Math.Abs(nomBack[0].profitWan - 3.2f) < 0.001f && Math.Abs(nomBack[0].deltaWan + 1.5f) < 0.001f
                && nomBack[1].seats == 300, ref fail);
            Check("空值安全（无机可派时不会有 payload）",
                AircraftNominationLogic.Encode(null) == "" && AircraftNominationLogic.Decode(null).Count == 0
                && AircraftNominationLogic.Decode("").Count == 0, ref fail);
            Check("残缺片段跳过（存档脏数据不抛异常）",
                AircraftNominationLogic.Decode("B-1,|A320,B-3|A330|300").Count == 1, ref fail);
            Check("候选上限 2–3 架（多了 CEO 挑不动）",
                AircraftNominationLogic.MaxCandidates >= 2 && AircraftNominationLogic.MaxCandidates <= 3, ref fail);
            Check("IndexOf 命中/未命中（按机号，大小写不敏感）",
                AircraftNominationLogic.IndexOf(cands, "b-8803") == 1
                && AircraftNominationLogic.IndexOf(cands, "B-9") == -1, ref fail);

            // —— 5. 文案：提名的「筛选口径」必须把硬门槛逐条摊给 CEO 看 ——
            var nomBody = AircraftNominationLogic.FormatBody(null, far, cands, new List<string> { "B-8801　A320neo　航程不足" });
            Check("公告标题：有机可派说「提名」，无机可派说「无合适运力」",
                AircraftNominationLogic.Subject(far, false).Contains("提名")
                && AircraftNominationLogic.Subject(far, true).Contains("无合适运力"), ref fail);
            Check("正文列出七条硬门槛（①…⑦）",
                nomBody.Contains("① ") && nomBody.Contains("② ") && nomBody.Contains("③ ") && nomBody.Contains("④ ")
                && nomBody.Contains("⑤ ") && nomBody.Contains("⑥ ") && nomBody.Contains("⑦ "), ref fail);
            Check("正文写明排序口径与「批准后才写机号」",
                nomBody.Contains("排序口径") && nomBody.Contains("批准后的动作"), ref fail);
            Check("正文带上逐机落选归因（为什么不派它）", nomBody.Contains("B-8801"), ref fail);
            Check("正文带候选机号与其投影日利", nomBody.Contains("B-8802") && nomBody.Contains("B-8803"), ref fail);
            Check("无机可派时写明「无需批复」并给出处置方向",
                AircraftNominationLogic.FormatBlockedBody(null, far, new List<string> { "B-1　A320neo　航程不足" })
                    .Contains("无需批复"), ref fail);

            R26QueryReply(ref fail);
        }

        // ———————————————————— R26 排班咨询答复（2026-10-13 第三次报障） ————————————————————
        //
        // 报障原文（单航线页指令框）：「为何FR106/107每周三，周五，周日不安排执飞？」
        // 他只是在问，旧版却**真的去提名机号**了 —— 句子里有「安排」（指派动词表里就有），
        // 又有「FR106」（字母 2 位 + 数字 3 位，与机型代号 A330 同形，被 HasTypeCodeHint 认成机型）。
        //
        // 与 R24 的分工：R24 钉「派机令怎么解析」，本段钉「**问句不得被解析成任何令**」，
        // 以及答复件的话题识别（问的是哪一点）。
        // 带状态段（投不投递答复件 / 改不改运力）在 LogicSelfTests R26，那边有 GameState。
        static void R26QueryReply(ref int fail)
        {
            Console.WriteLine("\nR26 排班咨询答复（问句不得当令 / 话题识别）：");

            // —— 1. 报障原句：含「安排」与同形航班号，仍必须判成询问 ——
            string[] asks =
            {
                "为何FR106/107每周三，周五，周日不安排执飞？",
                "为什么不是每天都有航班",
                "这条线怎么只排 1 班",
                "哪几天没有航班",
                "为什么只有早班",
                "B-2007排不进的原因是什么？",
                "值勤为什么会超限？",
                "这条线亏钱吗",
                "每周飞几班",
                "时刻槽为什么不够？",
                "B-2007排不进的原因：执勤超限。是不是说周一周二的航班无法按时起飞？"
            };
            bool allAsk = true;
            for (int i = 0; i < asks.Length; i++)
                if (!CapacityDirectiveLogic.LooksLikeQuestion(asks[i])) allAsk = false;
            Check("[关键] 疑问句一律判成询问（含「不安排执飞」与同形航班号 FR106）", allAsk, ref fail);

            var dRepro = CapacityDirectiveLogic.Parse("为何FR106/107每周三，周五，周日不安排执飞？");
            Check("[关键] 报障原句不得解析成派机令（旧版 intent=assign，运营部真去提名机号了）",
                dRepro.intent != "assign", ref fail);
            Check("[关键] 报障原句标记为询问、且进「需确认」分支（不执行）",
                dRepro.query && CapacityDirectiveLogic.NeedsConfirm(dRepro), ref fail);
            Check("询问的预览说的是「转呈运营部答复」，不是加密班次",
                CapacityDirectiveLogic.PlanLine(null, dRepro).Contains("答复"), ref fail);
            Check("询问的确认文案先说明「不改动本线运力与机号」，并写清答复在哪",
                CapacityDirectiveLogic.ConfirmPrompt(null, dRepro, false).Contains("不会被改动")
                && CapacityDirectiveLogic.ConfirmPrompt(null, dRepro, false).Contains("收件箱"), ref fail);

            // —— 2. 反例：请求型问句与真调令都**不得**被当成咨询 ——
            // 「能不能派一架飞机来飞？」是在请办事（派机零副作用，照办更贴心）；
            // 「能不能加密到每天 3 班？」是带量的方向请求；带增减词的问句同理。
            Check("[关键] 请求型问句不算咨询（「能否派一架飞机来飞本线？」仍按派机办）",
                !CapacityDirectiveLogic.LooksLikeQuestion("能否派一架飞机来飞本线？")
                && CapacityDirectiveLogic.Parse("能否派一架飞机来飞本线？").intent == "assign", ref fail);
            Check("带方向词的问句不算咨询（「能不能加密到每天 3 班？」走调令）",
                !CapacityDirectiveLogic.LooksLikeQuestion("能不能加密到每天 3 班？"), ref fail);
            Check("无问号的调令不算咨询",
                !CapacityDirectiveLogic.LooksLikeQuestion("加密到每天 3 班")
                && !CapacityDirectiveLogic.LooksLikeQuestion("削减到每天 1 班")
                && !CapacityDirectiveLogic.LooksLikeQuestion("安排一架合适的飞机执飞此航班"), ref fail);
            Check("空串安全", !CapacityDirectiveLogic.LooksLikeQuestion(null)
                && !CapacityDirectiveLogic.LooksLikeQuestion("   "), ref fail);

            // —— 3. 分析服务无权把问句改写成令（否则在线时又回到「真去办事」） ——
            var dAiQ = CapacityDirectiveLogic.Parse("为何不安排执飞？");
            CapacityDirectiveLogic.MergeAiJson(dAiQ, "{\"intent\":\"increase\",\"waves\":2}");
            Check("[关键] 分析服务不得把询问改写成增班", dAiQ.query && !dAiQ.intentKnown, ref fail);

            // —— 4. 话题识别：问的是哪一点（多个话题各答一段） ——
            var t1 = OpsQueryLogic.TopicsOf("为何FR106/107每周三，周五，周日不安排执飞？");
            Check("报障原句 → 班期类话题（不含「收益」）",
                t1.Contains(OpsQueryLogic.TopicDays) && !t1.Contains(OpsQueryLogic.TopicEcon), ref fail);
            Check("「为什么只有一班，还能加密吗」→ 运力上限",
                OpsQueryLogic.TopicsOf("为什么只有一班，还能加密吗？").Contains(OpsQueryLogic.TopicCapacity), ref fail);
            Check("「这条线现在谁在飞」→ 执飞机",
                OpsQueryLogic.TopicsOf("这条线现在谁在飞？").Contains(OpsQueryLogic.TopicAircraft), ref fail);
            Check("「时刻槽为什么不够」→ 时刻槽",
                OpsQueryLogic.TopicsOf("时刻槽为什么不够？").Contains(OpsQueryLogic.TopicSlot), ref fail);
            Check("「为什么会值勤超限」→ 值勤",
                OpsQueryLogic.TopicsOf("为什么会值勤超限？").Contains(OpsQueryLogic.TopicDuty), ref fail);
            Check("「这条线亏钱吗」→ 收益",
                OpsQueryLogic.TopicsOf("这条线亏钱吗？").Contains(OpsQueryLogic.TopicEcon), ref fail);
            // ★2026-10-14 第十次问询的原话：这一问要同时答班期（一周七趟）、执飞机与值勤（机乘轮换）。
            // 旧版只认得「飞机」，答成「谁在飞」—— 三根轴里最要紧的机组那一段根本没出场。
            var tCycle = OpsQueryLogic.TopicsOf("如果用两组机乘人员轮换，那么一架飞机回程 00:46 落地后，"
                + "也可以在 05:30 报到。这样是不是一架飞机也可以安排一周七趟往返？");
            Check("[关键] 「一周七趟 + 两组机乘轮换」同时命中 班期 / 执飞机 / 值勤（旧版只命中「执飞机」）",
                tCycle.Contains(OpsQueryLogic.TopicDays) && tCycle.Contains(OpsQueryLogic.TopicAircraft)
                && tCycle.Contains(OpsQueryLogic.TopicDuty), ref fail);
            Check("量词问法也算班期（「这条线一周能排几趟？」）",
                OpsQueryLogic.TopicsOf("这条线一周能排几趟？").Contains(OpsQueryLogic.TopicDays), ref fail);
            Check("认不出问啥时话题为空（兜底段负责请 CEO 换说法，不硬凑解释）",
                OpsQueryLogic.TopicsOf("嗯这个事你看着办").Count == 0, ref fail);
            Check("话题上限（多了信里印不下，CEO 也读不完）",
                OpsQueryLogic.TopicsOf("班期为什么不对，是不是没飞机、值勤超时、时刻槽也不够、还亏钱").Count <= 3, ref fail);
            Check("话题中文名可读（收件箱标题里直接印）",
                OpsQueryLogic.TopicLabel(OpsQueryLogic.TopicDays) == "班期"
                && OpsQueryLogic.TopicLabel(OpsQueryLogic.TopicEcon) == "收益"
                && OpsQueryLogic.TopicTitles(new List<string> { OpsQueryLogic.TopicDays, OpsQueryLogic.TopicCapacity })
                    == "班期 · 运力上限", ref fail);
            Check("空话题给「未分类」而不是空串", OpsQueryLogic.TopicTitles(null) == "未分类", ref fail);

            R27RouteKey(ref fail);
        }

        // ———————————————————— R27 航线键解析（2026-10-13 CEO 第四次报障） ————————————————————
        //
        // 报障原文：「点击法兰克福FRA--亚特兰大ATL，跳转的是法兰克福--伦敦希思罗LHR页面」。
        //
        // 根因不在链接包得不好，而在**键的两种形态没被同一处认下来**：
        // ① 真 routeId 是 `{出发}-{到达}-{序号}`（TakeoverWorldBuilder / ActionExecutor 造它时加的序号），
        //    各页面点进来用的是这一种；
        // ② 正文里的航线链接由 TermNavMap 从文案里的「FRA—ATL」现包，**手上只有两个三字码、没有序号**。
        // 旧 RouteDetailPanel 只做 ① 的精确比对，比对失败就 `activeRoutes[0]` 兜底 ——
        // 于是「点 FRA—ATL」永远打开列表第一条（本例恰好是 FRA—LHR），既不报错也不提示。
        //
        // 本段钉 RouteData.FindByKey 的三档：精确 / 机场对（正反都认）/ 解析不出返回 null。
        static void R27RouteKey(ref int fail)
        {
            Console.WriteLine("\nR27 航线键解析（正文链接的机场对 vs 页面用的 routeId）：");

            var routes = new List<RouteData>
            {
                new RouteData { routeId = "FRA-LHR-1", origin = "FRA", destination = "LHR" },
                new RouteData { routeId = "FRA-ATL-2", origin = "FRA", destination = "ATL" }
            };

            Check("① 精确 routeId（各页面点进来的键）",
                RouteData.FindByKey(routes, "FRA-ATL-2")?.routeId == "FRA-ATL-2", ref fail);
            Check("① routeId 大小写不敏感",
                RouteData.FindByKey(routes, "fra-atl-2")?.routeId == "FRA-ATL-2", ref fail);
            Check("[关键] ② 机场对（正文链接只有 FRA—ATL，没有序号）",
                RouteData.FindByKey(routes, "FRA-ATL")?.routeId == "FRA-ATL-2", ref fail);
            Check("[关键] ② 不得落到列表第一条（报障：点 FRA—ATL 打开了 FRA—LHR）",
                RouteData.FindByKey(routes, "FRA-ATL")?.routeId != "FRA-LHR-1", ref fail);
            Check("② 反写方向也认（ATL—FRA 仍是同一条线）",
                RouteData.FindByKey(routes, "ATL-FRA")?.routeId == "FRA-ATL-2", ref fail);
            Check("② 小写三字码也认", RouteData.FindByKey(routes, "fra-atl")?.routeId == "FRA-ATL-2", ref fail);
            Check("② 旧版拼出来的怪键（routeId + 三字码）也能救回来",
                RouteData.FindByKey(routes, "FRA-ATL-2-ATL")?.routeId == "FRA-ATL-2", ref fail);
            Check("③ 未知航线对 → null（调用方如实说「未选择航线」，不猜第一条）",
                RouteData.FindByKey(routes, "FRA-PEK") == null, ref fail);
            Check("③ 未知 routeId → null", RouteData.FindByKey(routes, "R-NOPE") == null, ref fail);
            Check("③ 空键 / 空列表不炸也不猜",
                RouteData.FindByKey(routes, "") == null
                && RouteData.FindByKey(routes, null) == null
                && RouteData.FindByKey(null, "FRA-ATL") == null, ref fail);
            Check("③ 两段都不是三字码的键不进机场对档（R-PEK-SHA 不会误配）",
                RouteData.FindByKey(routes, "R-PEK-SHA") == null, ref fail);
        }

        // ———————————————————— R28 照目标下达的班次调令（2026-10-13 CEO 第四次报障） ————————————————————
        //
        // 报障原文：「发布增加每日一段往返的指令，收件箱没有收到市场部反馈邮件」，
        // 输入框里那句是「把FR106/FR107调整为每日一段往返。」。两处根因：
        // ① **航班号被当成机型代号**：`HasTypeCodeHint` 旧版只认形状（1–3 字母 + 2–4 位数字），
        //    FR106 与 A330 同形 → 它与「调整」里的「调」凑成「身份词 + 指派动词」→ 判成派机令，
        //    运营部去提名机号（本线只有一架 A330，提名不出来就发「运力核对汇报」），班次一点没动；
        // ② **中文数词没读出来**：「一段往返」的量是用汉字写的，两条 WAVES 正则只认阿拉伯数字 →
        //    waves=0，即便走对路径也会退化成「现状 + 1 段」，把「每日一段」办成「每日两段」。
        //
        // 本段钉纯函数部分：意图不得读成 assign、量读得出、机型仍认得出、只写目标时方向能定下来。
        static void R28StatedTarget(ref int fail)
        {
            Console.WriteLine("\nR28 照目标下达的班次调令（航班号不是机型代号 / 中文数词 / 只写目标）：");

            // —— ① 报障原句：不得读成派机令，量必须读得出来 ——
            string raw = "把FR106/FR107调整为每日一段往返。";
            var d = CapacityDirectiveLogic.Parse(raw);
            Check("[关键] 报障原句不再读成派机令（旧版 intent=assign → 班次一点没动）",
                d.intent != "assign", ref fail);
            Check("[关键] 航班号 FR106 不再算飞机身份词（与 A330 同形，这是误判的入口）",
                !CapacityDirectiveLogic.MentionsAircraft(raw), ref fail);
            Check("[关键] 中文数词「一段往返」读得出量（waves=1）",
                d.waves == 1, ref fail);
            Check("「每日」班期读得出", d.days == "每日", ref fail);

            // 报障**文字**本身（「发布增加每日一段往返的指令」）：方向词 + 中文数词齐备，必须能直接办。
            // 旧版「一段」读不出量 → waves=0 → 即便方向是 increase 也仍要请 CEO 确认（等于没办）。
            var dInc = CapacityDirectiveLogic.Parse("增加每日一段往返");
            Check("[关键] 「增加每日一段往返」量读得出（waves=1）且方向为 increase（旧版卡在确认）",
                dInc.waves == 1 && dInc.intentKnown && dInc.intent == "increase"
                && !CapacityDirectiveLogic.NeedsConfirm(dInc), ref fail);

            // —— ② 机型代号仍必须认得出（别把误判修成「派机令全不认」） ——
            string[] typeCodes = { "把A330调到本线", "把B787-9调到本线", "把CRJ900调到本线",
                                   "把A321LR调到本线", "调 B-2007 到本线", "把C919调过来" };
            string missedType = "";
            for (int i = 0; i < typeCodes.Length; i++)
                if (CapacityDirectiveLogic.Parse(typeCodes[i]).intent != "assign") missedType += "「" + typeCodes[i] + "」";
            Check("[关键] 真的机型/机号仍判为派机令" + (missedType.Length == 0 ? "" : "（漏：" + missedType + "）"),
                missedType.Length == 0, ref fail);

            // —— ③ 反过来：航司两字码的航班号不得再被当机型（FR/SU/CZ/MU/HU/MF 都是航司码） ——
            string[] flightNos = { "把FR106/FR107调整为每日一段往返", "SU100 加密到每天 2 班",
                                   "CZ3456 削减到每天 1 班", "把HU7801调整为每日两班" };
            string badFlight = "";
            for (int i = 0; i < flightNos.Length; i++)
                if (CapacityDirectiveLogic.Parse(flightNos[i]).intent == "assign") badFlight += "「" + flightNos[i] + "」";
            Check("[关键] 航司两字码 + 数字的航班号不当机型代号" + (badFlight.Length == 0 ? "" : "（误：" + badFlight + "）"),
                badFlight.Length == 0, ref fail);
            Check("混合编号也不当机型（3U1234 = 川航航班号）",
                CapacityDirectiveLogic.Parse("3U1234 加密到每天 2 班").intent != "assign", ref fail);

            // —— ④ 中文数词全族 ——
            Check("「加密到每天两班」= 2 段（且挖满全天）",
                CapacityDirectiveLogic.Parse("加密到每天两班").waves == 2, ref fail);
            Check("「削减到每天一班」= 1 段",
                CapacityDirectiveLogic.Parse("削减到每天一班").waves == 1, ref fail);
            Check("「增加三段往返」= 3 段（无动词修饰时取句中的量）",
                CapacityDirectiveLogic.Parse("增加三段往返").waves == 3, ref fail);
            Check("「由每日一段增加到每日四段」取目标 4，而不是现状 1",
                CapacityDirectiveLogic.Parse("由每日一段增加到每日四段").waves == 4, ref fail);
            // 负例：指代不当量（「这一段」「每一段」不是要他排 1 段）
            Check("[关键] 「这一段运力太紧，把它加密」不得读成 1 段（指代不是量）",
                CapacityDirectiveLogic.Parse("这一段运力太紧，把它加密").waves == 0, ref fail);
            Check("[关键] 「每一段航班都要保证同机连班」不得读成 1 段",
                CapacityDirectiveLogic.Parse("每一段航班都要保证同机连班").waves == 0, ref fail);

            // —— ⑤ 只写目标、不说增减：「调整为 N 段往返」的方向由现状与目标比出来 ——
            // 现状：1 段/日 · 周一/三/五/日（每周 4 班），与截图同形。
            var route = MakeRoute("FR106", "08:00", "10:30", "ATL", "FRA");
            route.daysOfWeek = "周一/周三/周五/周日";
            route.weeklyFlights = 4;
            route.SyncFlightsFromPrimary();
            Check("现状 1 段/日（后续断言的前提）", route.FlightCount == 1, ref fail);

            var dTarget = CapacityDirectiveLogic.Parse("把FR106/FR107调整为每日一段往返。");
            Check("仅 Parse 时方向仍未知（「调整为」本身没有方向）",
                !dTarget.intentKnown && CapacityDirectiveLogic.NeedsConfirm(dTarget), ref fail);
            CapacityDirectiveLogic.ResolveStatedTarget(route, dTarget);
            Check("[关键] 定方向后不必请 CEO 重写（1 段 → 每日 1 段：班期由周 4 班加密到 7 班）",
                dTarget.intentKnown && dTarget.statedTarget && dTarget.intent == "increase"
                && !CapacityDirectiveLogic.NeedsConfirm(dTarget), ref fail);

            var dUp = CapacityDirectiveLogic.Parse("把FR106/FR107调整为每天四班");
            CapacityDirectiveLogic.ResolveStatedTarget(route, dUp);
            Check("[关键] 目标高于现状 → 加密（1 → 4 段/日）",
                dUp.waves == 4 && dUp.intent == "increase" && dUp.intentKnown, ref fail);

            // 现状 4 段/日：目标是 1 段 → 必须判「削减」，而不是照默认的增班办反
            var route4 = MakeRoute("FR106", "08:00", "10:30", "ATL", "FRA");
            route4.flights.Clear();
            string[] fourDeps = { "08:00", "12:00", "18:00", "22:00" };
            for (int i = 0; i < fourDeps.Length; i++)
                route4.flights.Add(new RouteFlight
                {
                    registration = "B-9001", depTime = fourDeps[i], arrTime = "10:30",
                    daysOfWeek = "每日", weeklyFlights = 7
                });
            var dCut = CapacityDirectiveLogic.Parse("把FR106/FR107调整为每天一班");
            CapacityDirectiveLogic.ResolveStatedTarget(route4, dCut);
            Check("[关键] 目标低于现状 → 削减（4 → 1 段/日，不得按默认增班办反）",
                route4.FlightCount == 4 && dCut.intent == "reduce" && dCut.intentKnown, ref fail);

            // 没写量（只说班期）：不在这里补方向 —— 否则会落进「现状 + 1 段」的兜底，替 CEO 定了他没说的量
            var dDaysOnly = CapacityDirectiveLogic.Parse("把班期调整为每日");
            CapacityDirectiveLogic.ResolveStatedTarget(route, dDaysOnly);
            Check("[关键] 只写班期、没写班次 → 仍不代他定方向（留给确认闸门请他补一句）",
                !dDaysOnly.intentKnown, ref fail);

            R29AircraftLinks(ref fail);
            R30FlightNumbers(ref fail);
            R31WeekGrid(ref fail);
            R32TurnStructure(ref fail);
            R33CrewDutySplit(ref fail);
            R34CrewCycleCapacity(ref fail);
            R35CrewGate(ref fail);
            R36CrewTwoPosts(ref fail);
        }

        // ——————————— R35 机组产能是排班的第四道硬闸门（2026-10-14 CEO 第十一次问询） ———————————
        //
        // 原话：「当 CEO 输入『增加此航线的航班量』，那么除了要获得更多的时刻槽，也要考虑增加轮换机组。」
        //
        // 这句话把「加密」拆成了**两份要同时到位的资源清单**：时刻槽 + 机组。旧版排班器只查前者
        // （以及机队/值勤），机组只写在呈报的影响段里当提示 —— 于是槽批下来了、飞机也在，
        // 班次照排，却没人执飞。本段钉住**纯函数**那一半（需求算术 / 产能反解 / 缺员文案）；
        // 带状态那一半（ApplyWaves 真按产能截断、超出记 kind=crew、呈报走两阶段）在 LogicSelfTests R35。
        static void R35CrewGate(ref int fail)
        {
            Console.WriteLine("\nR35 机组产能 = 排班第四道硬闸门（需求算术 / 产能反解 / 缺员文案）：");

            // —— 需求侧：外站换组的洲际线，每日 N 段 = 3N 组 = 9N 名飞行员（每组 3 人制）——
            var atl = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            atl.distanceKm = 7409f;
            atl.aircraftType = "A330-300";
            atl.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300");
            atl.SyncFlightsFromPrimary();

            int perDuty = OpsRotationLogic.PilotsPerDuty(OpsRotationLogic.BlockOf(atl));
            Console.WriteLine($"　FRA–ATL 单段轮挡 {OpsRotationLogic.BlockOf(atl) / 60f:0.##}h → 每组 {perDuty} 名飞行员");
            Check("[关键] 洲际线需求 = 3N 组 / 9N 名：每日 1 段→3 组 9 名、2 段→6 组 18 名、3 段→9 组 27 名"
                  + "（加密一档就多 9 名飞行员 —— 这是「只买槽不补人」会撞的墙）",
                CrewCapacityLogic.RouteSets(atl, 1) == 3 && CrewCapacityLogic.RoutePilots(atl, 1) == 9
                && CrewCapacityLogic.RouteSets(atl, 2) == 6 && CrewCapacityLogic.RoutePilots(atl, 2) == 18
                && CrewCapacityLogic.RouteSets(atl, 3) == 9 && CrewCapacityLogic.RoutePilots(atl, 3) == 27, ref fail);

            var shape = OpsRotationLogic.RouteCrewNeed(atl, 1).shape;
            Check("口径取自 OpsRotationLogic（不另立一套算术）：FRA–ATL 走「外站换组」"
                  + "（过站不含机组休息 → 一趟往返 = 两个值勤期）",
                shape == OpsRotationLogic.RoundTripShape.CrewSwap, ref fail);

            // —— 产能反解：MaxTripsWithin 是需求算术的逆函数（逐段扫描，段数单调不减）——
            //    本节的预算只卡**驾驶舱**（客舱给 NoLimit），把飞行员的轴单独隔离出来验；
            //    客舱那一轴（每 50 座 1 名、两岗取小）在 R36。
            Console.WriteLine("　产能反解：预算 8→0 段 / 9→1 / 17→1 / 18→2 / 26→2 / 27→3 / 36→4");
            int NoCab = CrewCapacityLogic.NoLimit;
            Check("[关键] 预算 8 名 → 连每日 1 段（要 9 名）都排不起：返回 0（而不是「四舍五入给 1 段」）",
                CrewCapacityLogic.MaxTripsWithin(atl, 8, NoCab) == 0, ref fail);
            Check("[关键] 预算 9 / 17 名 → 只够 1 段；18 / 26 → 2 段；27 → 3 段；36 → 4 段（= 时刻波上限）",
                CrewCapacityLogic.MaxTripsWithin(atl, 9, NoCab) == 1
                && CrewCapacityLogic.MaxTripsWithin(atl, 17, NoCab) == 1
                && CrewCapacityLogic.MaxTripsWithin(atl, 18, NoCab) == 2
                && CrewCapacityLogic.MaxTripsWithin(atl, 26, NoCab) == 2
                && CrewCapacityLogic.MaxTripsWithin(atl, 27, NoCab) == 3
                && CrewCapacityLogic.MaxTripsWithin(atl, 36, NoCab) == 4, ref fail);
            Check("产能上界不越过时刻波数（最多 4 段/日）—— 机组再多也不能凭空多出时段",
                CrewCapacityLogic.MaxTripsWithin(atl, 9999, NoCab) == ScheduleSlots.DepTimes.Length, ref fail);

            // 单调性：预算越多，能排的段数只能不降（否则排班侧「加人反被截」）
            bool mono = true;
            int prev = -1;
            for (int b = 0; b <= 40; b++)
            {
                int got = CrewCapacityLogic.MaxTripsWithin(atl, b, NoCab);
                if (got < prev) { mono = false; break; }
                prev = got;
            }
            Check("产能随预算是单调不减的（加人只会放行更多班次，绝不会反把已有班次截掉）", mono, ref fail);

            // —— 无 GameState（离线纯函数）时一律放行：排班器不能因为读不到在册就把班次排空 ——
            Check("[关键] 读不到在册/编制（null 状态）时返回 NoLimit 放行 —— 离线试算/读档早期不得静默砍班",
                CrewCapacityLogic.MaxCrewedTrips(null, atl) == CrewCapacityLogic.NoLimit
                && CrewCapacityLogic.MaxTripsWithin(null, 9, 9) == CrewCapacityLogic.NoLimit
                && CrewCapacityLogic.CanCrew(null, atl, 4, out _), ref fail);

            // —— 短程同组往返：组数走值勤预算折算，不是 3N（同一套算术的另一支）——
            var sh = MakeRoute("MU5101", "08:00", "10:10", "SHA");
            sh.distanceKm = 1100f;
            sh.aircraftType = "A320neo";
            sh.SyncFlightsFromPrimary();
            var shNeed = OpsRotationLogic.RouteCrewNeed(sh, 2);
            int shSets = shNeed.sets, shPilots = shNeed.pilots;
            var shShape = shNeed.shape;
            Console.WriteLine($"　MU5101（短程）：2 段/日 → {shSets} 组 / {shPilots} 名，形状 {shShape}");
            Check("短程线走 SingleDuty 分支：组数按「一组一天能塞几圈」折算（不套 3N 的洲际口径）",
                shShape == OpsRotationLogic.RoundTripShape.SingleDuty && shSets >= 1
                && shSets < 3 * 2 && shPilots == shSets * OpsRotationLogic.PilotsPerDuty(OpsRotationLogic.BlockOf(sh)), ref fail);

            // —— 余量判定：预算扣掉其它航线占用后，才轮到本线 ——
            Check("[关键] CanCrew 是可判的：预算够就放行、不够给出原因（原因须含「需 N 组 / N 名」与「本线可用」两截）",
                CrewCapacityLogic.CanCrew(null, atl, 1, out string whyOk) && whyOk.Length == 0, ref fail);

            // —— 文案：缺口类型码是 crew，短名是「缺机组」（呈报/单航线页按它分类）——
            Check("[关键] 缺口类型码 kind = \"crew\"、短名「缺机组」—— 与 no_slot / duty / curfew / no_aircraft 并列，UI 据此分流",
                CrewCapacityLogic.KindCrew == "crew" && CrewCapacityLogic.Label == "缺机组", ref fail);

            // —— 文案：Remedy 必须点明「与时刻槽并列」这件 CEO 原话里的事 ——
            string remedy = CrewCapacityLogic.Remedy(null, atl);
            Check("[关键] 处置文案把机组与时刻槽写成**并列两道闸门**（「只批槽不补机组，班次排出来也没人飞」）"
                  + "—— 这正是第十一次问询的原意",
                remedy.Contains("并列") && remedy.Contains("时刻槽"), ref fail);

            // —— Details 的边界：连 1 段都排不起时如实说「暂无可用机组」，不返回空 ——
            var det = CrewCapacityLogic.Details(null, atl, 4);
            Check("逐线明细在拿不到在册时不空手（离线也返回一行可读口径，呈报不会出现空 bullet）",
                det != null && det.Count > 0, ref fail);
        }

        // —— R36：机组＝驾驶舱＋客舱（2026-10-14 CEO 第十三次问询「机组是包括乘务人员吗？」） ——
        //
        // CEO 裁决：**把客舱也做成真闸门**。语义拆成三步，本段逐条钉死纯函数那一半
        //  ① **组数两岗共用**：一个轮转班底里两岗一起走 → 组数只有 CrewSetsForDaily 一个算术；
        //  ② **每组人数两岗各算各的**：驾驶舱看值勤时长（PilotsPerDuty，长程 4 人制），
        //     客舱看座位（CabinPerDuty，每 50 座 1 名）—— 与人力口径的客舱编制公式共用同一个座位单位常量；
        //  ③ **两岗取小**：飞行员与乘务员不可互替，任一岗到顶则该段排不出 → MaxTripsWithin 取 min。
        // 带状态那一半（CabinFilled / 口径退化 / 呈报点名）在 LogicSelfTests R35 场景 A–D。
        static void R36CrewTwoPosts(ref int fail)
        {
            Console.WriteLine("\nR36 机组 = 驾驶舱 + 客舱（组数共用 / 人数各算 / 两岗取小）：");

            var atl = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            atl.distanceKm = 7409f;
            atl.aircraftType = "A330-300";
            atl.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300");
            atl.SyncFlightsFromPrimary();

            int block = OpsRotationLogic.BlockOf(atl);
            int perDuty = OpsRotationLogic.PilotsPerDuty(block);
            int perDutyCabin = OpsRotationLogic.CabinPerDuty(atl.aircraftType);
            Console.WriteLine($"　A330-300：座级 {EconomyBalanceParams.DefaultSeatsFor(atl.aircraftType)} 座 → "
                              + $"每组客舱 {perDutyCabin} 名（每 {HrEconomics.SeatsPerCabinCrewUnit} 座 1 名）"
                              + $"· 每组驾驶舱 {perDuty} 名（轮挡 {block / 60f:0.##}h）");

            // ② 客舱配员：按座位 1/50 向上取整、下限 1；座级必须与编制同源（DefaultSeatsFor）
            Check("[关键] 客舱每组人数按座位配员（不按值勤时长）：每 50 座 1 名、向上取整 —— "
                  + "宽体座级 290 → 6 名；窄体座级 180 → 4 名；机型名给空/未登记时走窄体兜底座级（仍 ≥ 1 名）",
                perDutyCabin == 6
                && OpsRotationLogic.CabinPerDuty("A320neo") == 4
                && OpsRotationLogic.CabinPerDuty("") == 4
                && OpsRotationLogic.CabinPerDuty(null) == 4
                && OpsRotationLogic.CabinPerDuty("") >= 1, ref fail);

            // ★座级权威只有一处：GameState.GetFleetStaffing（推编制）→ EconomyBalanceParams.DefaultSeatsFor。
            //   客舱每班配员必须读**同一个**座级，否则「每班配员 × 4 组 + 备份 = 编制」这条闭环会在
            //   两个座级之间悄悄断开（新建 Aircraft 的 economySeats 默认 180 → TotalSeats 180，
            //   而目录 maxPassengers(A330-300) = 300：按目录算就变成编制 18 名要养 4 组 × 6 = 24 名配员）。
            bool sameSeatAuthority = true;
            string[] seatTypes = { "A330-300", "A380-800", "A350-1000", "B787-9", "A320neo", "B737-800" };
            for (int i = 0; i < seatTypes.Length; i++)
            {
                string t = seatTypes[i];
                if (OpsRotationLogic.CabinPerDuty(t) !=
                    (int)Math.Ceiling(EconomyBalanceParams.DefaultSeatsFor(t)
                                      / (float)HrEconomics.SeatsPerCabinCrewUnit)) { sameSeatAuthority = false; break; }
            }
            // 该断言必须有鉴别力：至少存在「目录座级」与「座级权威」会给出**不同**配员的机型，
            // 否则把 CabinPerDuty 改成读目录也不会有任何用例变红（等于白守卫）。
            int a380ByAuthority = OpsRotationLogic.CabinPerDuty("A380-800");
            int a380ByCatalog = (int)Math.Ceiling(CapacityEconomics.SeatsOfType("A380-800")
                                                 / (float)HrEconomics.SeatsPerCabinCrewUnit);
            bool discriminating = a380ByCatalog > 0 && a380ByCatalog != a380ByAuthority;
            Console.WriteLine($"　座级权威鉴别力：A380-800 按编制口径 {a380ByAuthority} 名"
                              + $"· 按目录 maxPassengers({CapacityEconomics.SeatsOfType("A380-800")}) 会得 {a380ByCatalog} 名"
                              + "（两者必须相等才算「同源」—— 取编制口径）");
            Check("[关键] 客舱每班配员的座级与**客舱编制**同源（`EconomyBalanceParams.DefaultSeatsFor`，"
                  + "即 GetFleetStaffing 用的那个口径）—— 不得改用机型目录的 maxPassengers："
                  + "两者同一机型并不相等（A380-800：编制口径 6 vs 目录 11），"
                  + "混用会让「配员 × 4 组 + 备份 = 编制」这条闭环断开",
                sameSeatAuthority && discriminating && a380ByAuthority == 6, ref fail);

            // ① 组数两岗共用：同一个 need 结构里 sets 只有一个值，两岗人数都由它乘出来
            var n1 = OpsRotationLogic.RouteCrewNeed(atl, 1);
            var n2 = OpsRotationLogic.RouteCrewNeed(atl, 2);
            var n3 = OpsRotationLogic.RouteCrewNeed(atl, 3);
            Console.WriteLine($"　需求：1 段 → {n1.sets} 组（驾驶舱 {n1.pilots} / 客舱 {n1.cabin}）"
                              + $"· 2 段 → {n2.sets} 组（{n2.pilots} / {n2.cabin}）"
                              + $"· 3 段 → {n3.sets} 组（{n3.pilots} / {n3.cabin}）");
            Check("[关键] 组数两岗共用、人数两岗分算：3N 组 → 驾驶舱 9N 名 · 客舱 18N 名"
                  + "（每日 1 段 3 组 = 9 + 18；2 段 6 组 = 18 + 36；3 段 9 组 = 27 + 54）",
                n1.sets == 3 && n1.pilots == 9 && n1.cabin == 18
                && n2.sets == 6 && n2.pilots == 18 && n2.cabin == 36
                && n3.sets == 9 && n3.pilots == 27 && n3.cabin == 54, ref fail);

            Check("两个人数都严格由同一个 sets 派生（改了组数算术，两岗一起改 —— 不允许某一岗偷偷另乘一套）",
                CrewCapacityLogic.RouteSets(atl, 3) == n3.sets
                && CrewCapacityLogic.RoutePilots(atl, 3) == n3.sets * perDuty
                && CrewCapacityLogic.RouteCabin(atl, 3) == n3.sets * perDutyCabin, ref fail);

            // ③ 两岗取小：驾驶舱够、客舱不够时，产能必须由**客舱**决定
            Console.WriteLine("　两岗取小：驾驶舱给足（9999）而客舱只给 40 名 → 18N ≤ 40 → 2 段");
            Check("[关键] 两岗取小 —— 驾驶舱再富余，客舱不够也只排得出客舱那档；"
                  + "反向同理（客舱给足时由驾驶舱决定），两向都不得互相顶替",
                CrewCapacityLogic.MaxTripsWithin(atl, 9999, 40) == 2
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, 36) == 2
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, 35) == 1
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, 18) == 1
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, 17) == 0
                && CrewCapacityLogic.MaxTripsWithin(atl, 27, 9999) == 3
                && CrewCapacityLogic.MaxTripsWithin(atl, 26, 9999) == 2, ref fail);

            Check("任一岗预算为 0 或负数 → 连 1 段都排不出（返回 0，而不是「另一岗够就放行」）",
                CrewCapacityLogic.MaxTripsWithin(atl, 0, 9999) == 0
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, 0) == 0
                && CrewCapacityLogic.MaxTripsWithin(atl, -5, 9999) == 0
                && CrewCapacityLogic.MaxTripsWithin(atl, 9999, -5) == 0, ref fail);

            // 与人力口径的客舱**编制**公式同一套座位单位常量：编制 = 每组人数 × 4 组 + 每架 4 名备份
            int est1 = HrEconomics.OperationalEstablishment("乘务员", 1, 1, 300);
            int est2 = HrEconomics.OperationalEstablishment("乘务员", 2, 2, 600);
            Console.WriteLine($"　口径闭合：客舱编制 1 架 = {est1} 名、2 架 = {est2} 名"
                              + $"（= 座位/{HrEconomics.SeatsPerCabinCrewUnit}×{HrEconomics.CabinCrewPerFiftySeats}"
                              + $" + 架数×{HrEconomics.CabinCrewPerAircraft}）");
            // 取座级为 50 的整数倍（300 / 600）时闭环**严格**成立：每班配员（ceil(300/50)=6）× 4 组 + 4 备份 = 28。
            // 若实际配置座数不是 50 的整数倍，配员这条按「每班向上取整」、编制那条按「总量摊入后四舍五入」，
            // 两者可取到相差 1 组量级（≤ 4 名）—— 这是两套算术的取整差，不是第二套座位口径；
            // 座位单位常量 `SeatsPerCabinCrewUnit` 两处共用一个，才是本条要钉死的东西。
            Check("[关键] 客舱每班配员与客舱编制**共用同一座位单位常量**（`SeatsPerCabinCrewUnit`）："
                  + "300 座 → 每组 6 名、1 架编制 28 名（6×4 组 + 4 备份）、2 架 56 名 —— 不存在第二套座位口径",
                perDutyCabin == 6 && est1 == 28 && est2 == 56
                && perDutyCabin * (int)HrEconomics.CabinCrewPerFiftySeats
                   + 1 * (int)HrEconomics.CabinCrewPerAircraft == est1, ref fail);


            // 短程线同样走两岗：客舱由座位定，与驾驶舱的值勤折算互不影响
            var sh = MakeRoute("MU5101", "08:00", "10:10", "SHA");
            sh.distanceKm = 1100f;
            sh.aircraftType = "A320neo";
            sh.SyncFlightsFromPrimary();
            var shNeed = OpsRotationLogic.RouteCrewNeed(sh, 2);
            Console.WriteLine($"　短程 MU5101：{shNeed.sets} 组 → 驾驶舱 {shNeed.pilots} 名 / 客舱 {shNeed.cabin} 名");
            Check("短程线同口径：客舱 = 组数 × A320neo 座位/50，与驾驶舱的值勤折算各自独立",
                shNeed.cabin == shNeed.sets * OpsRotationLogic.CabinPerDuty(sh.aircraftType)
                && shNeed.cabin > 0, ref fail);
        }

        // ————————————— R34 两组机组轮换 → 一架飞机能否一周七趟（2026-10-14 CEO 第十次问询） —————————————
        //
        // 问：「如果用两组机乘人员轮换，那么一架飞机回程 00:46 落地轮挡后，也可以在 05:30 报到。
        // 这样是不是一架飞机也可以安排一周七趟往返？」
        // 「能不能」这个词会把三根不同的轴搅在一起，必须分开算 —— 且三根轴的**结论互不相同**：
        //   ① 机体占用：一趟往返占机 18h16m，回场 00:46 → 次日 06:30 再出发，本场 5h44m，正好 24h 一圈
        //      → **每天一圈排得开**，一周七趟在机体与时刻槽上成立；
        //   ② 机组：05:30 报到的是**另一组**，不是刚落地那组（同组只剩 4h44m < 10h 最低休息）；
        //      且**两组不够、要三组** —— 一组每 3 天最多飞 2 段（飞完去程人在 ATL，只能接次日回程；
        //      落地 FRA 00:46 后又接不上当日/次日早班，必空一天）→ 组数 = 3 × 每日圈数；
        //   ③ 班次模型：日利用率目标 10h ÷ 单圈 18.27h ≈ 0.55 班/日 → 建议每周 4 班
        //      （这是**建议值**，不是闸门 —— ★CEO 定：调令优先，见轴⑤）。
        // 本轮另外两处按 CEO 裁决改了口径：
        //   轴④ 「日利用率」统一按「一圈 2 段」（旧版一处 2 段、一处 1 段，差一倍）；
        //   轴⑤ 「加密到每天 N 班」现在真能抬动周班次（旧版恒由模型现算，是一条死路）。
        static void R34CrewCycleCapacity(ref int fail)
        {
            Console.WriteLine("\nR34 两组机组轮换 → 一架飞机能不能一周七趟（机体 / 机组 / 班次模型 三根轴）：");

            var r = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            r.distanceKm = 7409f;
            r.aircraftType = "A330-300";
            r.daysOfWeek = "周一/三/五/日";
            r.weeklyFlights = 4;
            r.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300");
            r.SyncFlightsFromPrimary();

            // —— 轴① 机体占用：24h 一圈 ——
            var w = OpsRotationLogic.WindowOf(r, r.depTime);
            int ready = w.end + w.turn + OpsRotationLogic.RotationGapMinutes;
            int nextDep = w.start + 1440;                       // 次日同刻再出发
            Console.WriteLine($"　轴① 机体：{r.depTime} 出发 · {OpsRotationLogic.FormatMinute(w.end)} 回场"
                              + $"（占机 {(w.end - w.start) / 60f:0.#}h）· 可再出发 {OpsRotationLogic.FormatMinute(ready)}"
                              + $" · 次日同刻再出发余量 {(nextDep - ready) / 60f:0.#}h");
            Check("[关键] 每天一圈机体不冲突：占机 18h16m + 本场 5h44m = 24h 一圈，回场 00:46 后次日 06:30 再出发仍有余量"
                  + "（故「一周七趟」在机体与时刻槽上排得开）",
                w.end - w.start == 1096 && ready < nextDep && nextDep - ready > 0, ref fail);

            // —— 轴② 机组：报到的是另一组，且两组不够 ——
            int retArr = w.end;                                 // 1486 = 次日 00:46 落地
            int outArr = w.start + w.block;                     // 913 = 当日 15:13 落 ATL
            int backReport = 1440 + 15 * 60 + 3;                // 次日 15:03 驻站回程组报到
            int sameNextMorning = 1440 + 5 * 60 + 30;           // 次日 05:30 报到（用户设想的那一档）
            int nextOutMorning = 2 * 1440 + 5 * 60 + 30;        // 再下一日 05:30 报到（同组真正接得上的最早去程）
            Console.WriteLine($"　轴② 机组：回程组落地 {OpsRotationLogic.FormatMinute(retArr)} → 次日 05:30 报到"
                              + $" = {(sameNextMorning - retArr) / 60f:0.#}h（最低 {OpsRotationLogic.MinRestMinutes / 60f:0.#}h）"
                              + $"\n　　　　　去程组落 ATL {OpsRotationLogic.FormatMinute(outArr)} → 次日回程 "
                              + $"{OpsRotationLogic.FormatMinute(backReport)} 报到 = {(backReport - outArr) / 60f:0.#}h（可接）"
                              + $"\n　　　　　同组落地后再接去程：最近一档 {OpsRotationLogic.FormatMinute(nextOutMorning)}"
                              + $" = {(nextOutMorning - retArr) / 60f:0.#}h（接不上当日早班 → 必空一天）");
            Check("[关键] 「回程 00:46 落地那组 05:30 再报到」不成立：只隔 4h44m < 最低休息 10h"
                  + "（05:30 报到的是**另一组**：去程组）",
                sameNextMorning - retArr == 284 && sameNextMorning - retArr < OpsRotationLogic.MinRestMinutes, ref fail);
            Check("[关键] 外站换组轮换本身成立：去程组落 ATL 15:13 → 次日 15:03 回程报到 = 23h50m ≥ 10h",
                backReport - outArr == 1430 && backReport - outArr >= OpsRotationLogic.MinRestMinutes, ref fail);
            Check("[关键] 同组落地 FRA 后必空一天：最近能接的去程是「再下一日」05:30 = 28h44m"
                  + "（当日早班接不上）→ 一组每 3 天只能飞 2 段",
                nextOutMorning - retArr == 1724 && nextOutMorning - retArr >= OpsRotationLogic.MinRestMinutes, ref fail);

            float perCrewPerDay = 2f / 3f;                      // 一组每 3 天 2 段
            float needPerDay = 2f;                              // 每天 2 个值勤期（去程 + 回程）
            float crewsNeeded = needPerDay / perCrewPerDay;
            Console.WriteLine($"　轴② 组数：每天 2 段 × 7 天 = 14 段/周；一组 {perCrewPerDay:0.##} 段/天"
                              + $" → 两组 {2f * perCrewPerDay:0.##} 段/天 < 2 段/天（不够），需要 {crewsNeeded:0.#} 组");
            Check("[关键] 两组机组轮换**不够**、要三组（14 段/周 ÷ 每组 4.67 段/周 ≈ 3）——"
                  + "用户设想的「两组」撑不起每日一班",
                crewsNeeded > 2f && crewsNeeded <= 3f, ref fail);

            // —— 轴③ 班次模型：4 班/周是「日利用率目标」给的，不是能力不足 ——
            var freq = RouteFrequencyModel.Compute("FRA", "ATL", 7409f, "A330-300");
            int blockMin = RouteFrequencyModel.BlockMinutes(7409f, "A330-300");
            float cycleH = (2f * blockMin + EconomyBalanceParams.BaseTurnMinutes) / 60f;
            float utilAt4 = 4f * OpsEconomics.RoundTripSectors * blockMin / 60f / 7f;
            float utilAt7 = 7f * OpsEconomics.RoundTripSectors * blockMin / 60f / 7f;
            Console.WriteLine($"　轴③ 班次模型：单圈 {cycleH:0.##}h；供给侧 日利用率目标 "
                              + $"{EconomyBalanceParams.TargetDailyUtilizationHours:0.#}h ÷ 单圈 = {freq.flightsByAircraft:0.###} 班/日；"
                              + $"需求侧 {freq.demandPaxPerDay:0} 人/日 → {freq.flightsByDemand:0.###} 班/日"
                              + $"\n　　　　　→ 取小 × 7 = 建议每周 {freq.weeklyFlights} 班（{freq.daysOfWeek}）；"
                              + $"现排 4 班 = {utilAt4:0.##}h/天（贴目标），每日七趟 = {utilAt7:0.##}h/天（超目标 "
                              + $"{(utilAt7 / EconomyBalanceParams.TargetDailyUtilizationHours - 1f) * 100f:0}%）");
            Check($"[关键] 「4 班/周」的出处是班次模型而不是能力：供给侧 {freq.flightsByAircraft:0.##} 班/日 < 需求侧"
                  + $" {freq.flightsByDemand:0.##} 班/日 → 模型卡在单机日利用率目标（"
                  + $"{EconomyBalanceParams.TargetDailyUtilizationHours:0.#}h）上，给 {freq.weeklyFlights} 班"
                  + "（这是**建议值**，不是闸门 —— 闸门是时刻槽与值勤，CEO 可用调令越过，见轴⑤）",
                freq.weeklyFlights == 4 && freq.flightsByAircraft < 1f
                && freq.flightsByAircraft < freq.flightsByDemand, ref fail);
            Check("现排 4 班/周 摊到 7 天 ≈ 9.96h/天 —— 正好贴着 10h 目标；每日七趟则 17.4h/天（超目标 74%）",
                Math.Abs(utilAt4 - EconomyBalanceParams.TargetDailyUtilizationHours) < 0.2f && utilAt7 > 17f, ref fail);

            // —— 轴④ 「日利用率」四处口径（★2026-10-14 CEO 裁决：统一为「一圈 2 段」）——
            // 旧版一处算 2 段、一处算 1 段，于是同一架飞机 / 同一条线 / 同一周出现两个答案（9.96 vs 4.98），
            // 效率乘数被压在 0.80 —— 同一件事只能有一套算术。
            var met = OpsEconomics.Evaluate(7409f, 4, "A330-300", r.groundStaffAssigned, 88f);
            float perTripDay = OpsEconomics.BlockHours(7409f) * OpsEconomics.RoundTripSectors;   // 执飞日口径
            Console.WriteLine("　轴④ 「日利用率」四处口径（★已统一为「一圈 2 段」）："
                              + $"\n　　　　　班次模型　2 段 × 4 班 ÷ 7 天（周均）　{utilAt4:0.##}h/日（拿它比 10h 目标 → 给 4 班/周）"
                              + $"\n　　　　　经济乘数　2 段 × 4 班 ÷ 7 天（周均）　{met.dailyUtilizationHours:0.##}h/日（效率乘数 {met.scheduleEfficiency:0.##}）"
                              + $"\n　　　　　季度复核　2 段 × 每日波次（执飞日）　{perTripDay:0.##}h/日（阈值 4h → 加密 / "
                              + $"{SeasonReviewLogic.TrimUtilHours:0}h → 减班）"
                              + $"\n　　　　　飞机详情　2 段（执飞日当天）　　　　{perTripDay:0.##}h（本线一天一圈）");
            Check("[关键] 「一圈 2 段」已统一：班次模型（周均）与经济乘数（周均）逐位相同"
                  + " —— 旧版差一倍（9.96 vs 4.98）、效率乘数被误压到 0.80",
                Math.Abs(met.dailyUtilizationHours - utilAt4) < 0.01f, ref fail);
            Check("[关键] 执飞日口径同样是 2 段：季度复核 = 飞机详情 = 17.43h，且超过减班阈值 13h"
                  + "（周均与执飞日只差**分母**：÷7 是周均、÷执飞日 是当天 —— 段数不再各说各话）",
                perTripDay > 17f && perTripDay > SeasonReviewLogic.TrimUtilHours
                && Math.Abs(perTripDay - utilAt4 * 7f / 4f) < 0.05f, ref fail);

            // —— 轴⑤ 「加密到每天 N 班」能不能把周班次抬到 7？（★2026-10-14 CEO 裁决：调令优先）——
            // `CapacityDirectiveLogic.Execute` 现在把「班期天数 × 波次数」当 honorWeekly 交给 `ApplyWaves`：
            //   weeklyTarget = honorWeekly > 0 ? ClampWeekly(honorWeekly) : 模型建议值
            //   weeklyEach   = PerWaveWeekly(weeklyTarget, 实际排得出的波次)
            //   route.weeklyFlights = weeklyEach × 波次
            // 旧版**无论 CEO 说什么**都由模型现算周班次（本线恒 4 班），波次只重新摊分它：
            // 1 波→4 班、3 波→3 班，`ReconcileDays("每日", 4) ≠ "每日"` → 本线永远排不出「每日」，
            // 而呈报里「先改周班次（加密/削减）」是**死路**（加密改的是波次，周班次每次都按模型重算）。
            int modelWeekly = freq.weeklyFlights;
            string wl = "";
            bool dailyHonored = false;
            for (int wv = 1; wv <= 3; wv++)
            {
                var dir = CapacityDirectiveLogic.Parse($"加密到每天 {wv} 班");
                int honor = string.IsNullOrEmpty(dir.days) ? 0 : RouteFrequencyModel.DayCount(dir.days) * wv;
                int target = honor > 0 ? RouteFrequencyModel.ClampWeekly(honor) : modelWeekly;
                int each = RouteFrequencyModel.PerWaveWeekly(target, wv);
                string got = RouteFrequencyModel.ReconcileDays("每日", each);
                if (got == "每日") dailyHonored = true;
                wl += $"{(wl.Length > 0 ? " / " : "")}{wv} 波→{each}×{wv}={each * wv} 班（{got}）";
            }
            Console.WriteLine($"　轴⑤ 调令「加密到每天 N 班」的周班次去向（模型建议 {modelWeekly} 班）：{wl}");
            Check("[关键] 「加密到每天 N 班」解析出「每日」班期 —— 班期空着就会退化成 honorWeekly=0"
                  + "（又回到模型现算），那等于白加密",
                CapacityDirectiveLogic.Parse("加密到每天 1 班").days == "每日"
                && RouteFrequencyModel.DayCount("每日") == 7, ref fail);
            Check("[关键] 调令现在真能抬动周班次：每日 1 班 → 7 班/周、每日 2 班 → 14、每日 3 班 → 21，"
                  + "且班期落定为「每日」（旧版恒按模型 4 班 → 每日永远不生效，加密反把周班次从 4 摊成 3）",
                dailyHonored, ref fail);
            Check("[关键] 判据不是橡皮图章：若仍按模型建议值（4 班）摊分，「每日」就落不了地"
                  + "（3 波只摊成 1×3=3 班、ReconcileDays(\"每日\", 4) ≠ \"每日\"）—— 旧版正是如此",
                RouteFrequencyModel.ReconcileDays("每日", modelWeekly) != "每日"
                && RouteFrequencyModel.PerWaveWeekly(modelWeekly, 3) == 1, ref fail);
        }

        // ————————————— R33 过站不含机组休息 → 外站换组（2026-10-14 CEO 第九次报障） —————————————
        //
        // 报障原文：「FRA-ATL过站时间包括机乘人员的休息时间吗？」
        //
        // 答案：**不包括**。过站公式（`OpsEconomics.EstimateTurnMinutes`）只有地面作业 ——
        // 50 分基准 + 缺勤地勤 ×12 分；值勤口径（`BudgetOf`）把「报到 → 最后落地」当一个**连续**
        // 值勤期算，过站那 50 分夹在中间属**值勤时间**。机组休息只出现在别处：值勤期之间 ≥10h、
        // 对方站换组/驻站的休息、以及宽体增援的**空中**休息（巡航段，不在过站里）。
        //
        // 这一问牵出本作最长洲际线的一处硬矛盾：FRA–ATL 06:30 出发（往返 18h16m）同组连飞 =
        // 报到 05:30 → 回场 00:46+1 = **19.3h**，而上限只有 14.5h（A330-300 Class 2 / 3 人制 15h，
        // 报到 05:30 再 −30 分）。旧版**判据**早已承认装不下（走「外站过夜」分支放行），
        // **记账**却把它记成一个 19.3h 的值勤期 → 飞机详情印出「实耗 19.3h（… ≤15h）｜剩余 −4.8h」。
        //
        // 现统一为**外站换组**：飞机照样 50 分过站当天回，回程由**对方站驻站机组**执飞 ——
        // 一趟往返 = 两个值勤期（去程组 9.7h + 驻站回程组 9.7h），判据与记账共用 `DutyShapeOf`。
        // 本段是纯函数：钉住两段的算术、两个上限、以及「过站不产生任何休息余量」这一条。
        static void R33CrewDutySplit(ref int fail)
        {
            Console.WriteLine("\nR33 过站不含机组休息 → 外站换组（一趟往返 = 两个值勤期）：");

            var r = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            r.distanceKm = 7409f;
            r.aircraftType = "A330-300";
            r.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300");
            r.SyncFlightsFromPrimary();
            var f = r.flights[0];
            FlightScheduleFormat.WindowTimes(r, f, out int dep, out int arr, out int rdep, out int rarr);
            int block = arr - dep;
            int turn = rdep - arr;

            var shape = OpsRotationLogic.DutyShapeOf(r.aircraftType, dep, block, turn, out var outb, out var inb);
            var merged = OpsRotationLogic.BudgetOf(r.aircraftType, dep - OpsRotationLogic.ReportBeforeMinutes, block, 2);
            int mergedLen = rarr - (dep - OpsRotationLogic.ReportBeforeMinutes);

            Console.WriteLine($"　实得：去程组 {OpsRotationLogic.FormatMinute(outb.report)}→{OpsRotationLogic.FormatMinute(outb.end)}"
                              + $" 实耗 {outb.Length / 60f:0.#}h / 剩余 {outb.Remaining / 60f:0.#}h"
                              + $"　｜　驻站回程组 {OpsRotationLogic.FormatMinute(inb.report)}→{OpsRotationLogic.FormatMinute(inb.end)}"
                              + $" 实耗 {inb.Length / 60f:0.#}h / 剩余 {inb.Remaining / 60f:0.#}h"
                              + $"　｜　同组连飞 {mergedLen / 60f:0.#}h > 上限 {merged.limit / 60f:0.#}h");

            Check("[关键] FRA–ATL 判为**外站换组**（不是「一个 19.3h 的值勤期」）",
                shape == OpsRotationLogic.RoundTripShape.CrewSwap, ref fail);
            Check("[关键] 同组连飞确实装不下（19.3h > 14.5h）—— 拆开不是为了好看，是唯一出路",
                mergedLen > merged.limit && mergedLen / 60f > 19f && merged.limit == 870, ref fail);
            Check("[关键] 两段各自在上限内：去程组 9.7h、驻站回程组 9.7h（均 ≤ 14.5h）",
                outb.Length <= outb.limit && inb.Length <= inb.limit
                && outb.Length == block + OpsRotationLogic.ReportBeforeMinutes
                && inb.Length == block + OpsRotationLogic.ReportBeforeMinutes, ref fail);
            Check("[关键] 剩余不为负（旧版屏幕上的「剩余 −4.8h」就是这么来的：记成一个 19.3h 的值勤期）",
                outb.Remaining > 0 && inb.Remaining > 0, ref fail);
            Check("[关键] 过站不给机组任何休息余量：去程组止于落地、回程组起于报到，两段连正间隔都没有",
                outb.end == arr && inb.report == rdep - OpsRotationLogic.ReportBeforeMinutes
                && inb.report < outb.end && turn < OpsRotationLogic.MinRestMinutes, ref fail);

            // 地勤缺 2 人 → 过站 74 分：过站变长既不给机组换来休息、也不缩短回程值勤（回程上限不变）
            int turn2 = turn + 24;
            var shape2 = OpsRotationLogic.DutyShapeOf(r.aircraftType, dep, block, turn2, out var outb2, out var inb2);
            Check("[关键] 过站变长（50→74 分）不改变回程值勤长度与上限（过站不算休息，故换不来任何减负）",
                shape2 == OpsRotationLogic.RoundTripShape.CrewSwap
                && inb2.Length == inb.Length && inb2.limit == inb.limit
                && inb2.report - inb.report == 24, ref fail);

            // 短程线仍是「同组往返一个值勤期」—— 换组口径不得把短程线也拆开
            var sh = MakeRoute("FR1", "06:30", "08:20", "PEK", "SZX");
            sh.distanceKm = 1900f;
            sh.aircraftType = "A320neo";
            sh.SyncFlightsFromPrimary();
            FlightScheduleFormat.WindowTimes(sh, sh.flights[0], out int sdep, out int sarr, out int srdep, out _);
            var shShape = OpsRotationLogic.DutyShapeOf(sh.aircraftType, sdep, sarr - sdep, srdep - sarr, out _, out _);
            Check("短程线仍判为同组往返（一个值勤期）—— 换组只针对装不下的洲际往返",
                shShape == OpsRotationLogic.RoundTripShape.SingleDuty, ref fail);

            // 真·超远程：连单段都超上限 → Infeasible（不许被换组口径吞成通过）
            var ultra = MakeProbeRoute(14500, "A350-900ULR");
            int ub = OpsRotationLogic.BlockOf(ultra);
            var ultraShape = OpsRotationLogic.DutyShapeOf("A350-900ULR", 8 * 60, ub, 50, out var ul, out _);
            Check("[关键] 14,500 km（单段本身就超值勤）判为 Infeasible —— 换组也救不了，必须如实拒绝",
                ultraShape == OpsRotationLogic.RoundTripShape.Infeasible && ul.Length > ul.limit, ref fail);

            // 判据侧：该线仍可指派；机队口径显式回报换组两段（供呈报）
            Check("[关键] FRA–ATL 仍可指派（换组口径不得把这条线判死）",
                CanFlyStub(7409, "A330-300"), ref fail);

            // 本机当日已排班时不得改按外站换组（换组需该机当日独占）—— 文案须明说
            // 走的是窄体口径：宽体任何既存值勤都会先把航段数顶到 4 > 增援上限 3，拦因先落在航段上限。
            var seed = MakeRoute("FR105", "03:00", "04:30", "MAD", "FRA");
            seed.distanceKm = 1500f;
            seed.aircraftType = "A321LR";
            seed.SyncFlightsFromPrimary();
            var acNb = new Aircraft { registration = "B-9999", type = "A321LR", isActive = true,
                                      status = AircraftStatuses.Airworthy, age = 2 };
            var plan2 = new OpsRotationLogic.RotationPlan();
            plan2.Assign("B-9999", OpsRotationLogic.WindowOf(seed, "03:00"));
            var rNb = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            rNb.distanceKm = 7409f;
            rNb.aircraftType = "A321LR";
            rNb.SyncFlightsFromPrimary();
            bool taken = OpsRotationLogic.CanTake(rNb, plan2, acNb, "08:00", null, false, out string takenWhy, out _);
            Check($"[关键] 本机当日已排班 → 不得改按外站换组（可飞={taken}，理由=「{takenWhy}」）",
                !taken && takenWhy.Contains("值勤超限") && takenWhy.Contains("不能改按外站换组"), ref fail);
        }

        // ———————————— R32 一趟往返的三段结构与过站时长（2026-10-14 CEO 第八次报障） ————————————
        //
        // 报障原文：「过站 50 分钟，看时刻表上的空余时间，感觉都是在 ATL 过站；而且空余时间感觉也超过了 50 分钟。」
        //
        // 根因不在数据、在**画法**：旧版把一趟往返画成一整条长条（06:30 → 次日 00:46 共 18 小时多），
        // 于是「过站 50 分」在图上**根本没有对应的像素**；屏幕上唯一看得见的空档是本场地面
        // （回场 00:46 → 次日出发 06:30，5 小时 44 分），看着就像「在对方站过站、还远超 50 分」。
        // 现把一趟往返画成三段：去程段 → **过站条（橙，在对方站）** → 回程段。
        // 本段是纯函数：钉住三段的算术（两段航程等长、过站 = 回程起飞 − 去程落地 = TurnMinutes、
        // 三段之和 = 占窗），并顺便钉住「起点已过午夜的那一段」不再被拆错（dayOffset）。
        static void R32TurnStructure(ref int fail)
        {
            Console.WriteLine("\nR32 一趟往返的三段结构（去程段 / 过站段 / 回程段）：");

            // 报障同形的线：FRA—ATL 7409 km、06:30 出发、A330-300、**地勤配足**（故过站就是基准 50 分）
            var r = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            r.distanceKm = 7409f;
            r.aircraftType = "A330-300";
            r.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff("A330-300");
            r.daysOfWeek = "周一/三/五/日";
            r.weeklyFlights = 4;
            r.SyncFlightsFromPrimary();
            var f = r.flights[0];

            FlightScheduleFormat.WindowTimes(r, f, out int dep, out int arr, out int rdep, out int rarr);
            int turn = rdep - arr;
            int expectTurn = LiveClockLogic.TurnMinutes(r.aircraftType, r.groundStaffAssigned);

            Console.WriteLine($"　实得：去程 {ScheduleSlots.FormatClock(dep)}→{ScheduleSlots.FormatClock(arr)}"
                              + $"　过站 {turn} 分（在 {r.destination}）"
                              + $"　回程 {ScheduleSlots.FormatClock(rdep % FlightScheduleFormat.MinutesPerDay)}"
                              + $"→{ScheduleSlots.FormatClock(rarr % FlightScheduleFormat.MinutesPerDay)}（次日）"
                              + $"　往返 {FlightScheduleFormat.Duration(rarr - dep)}");

            Check("[关键] 配足地勤时过站 = 基准 50 分（与 CEO 看到的「过站 50 分」一致）",
                turn == 50 && turn == expectTurn, ref fail);
            Check("[关键] 两段航程等长（去程段 = 回程段），过站夹在中间 → 过站在**对方站**",
                arr - dep == rarr - rdep && rdep - arr == turn, ref fail);
            Check("[关键] 三段之和 = 一次往返占窗（旧版画成一整条，看不见其中任何一段）",
                (arr - dep) + turn + (rarr - rdep) == rarr - dep, ref fail);

            // 地勤不足则过站变长（模型口径：缺 1 人 +12 分）—— 图上橙条也该跟着变长
            var short2 = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            short2.distanceKm = 7409f;
            short2.aircraftType = "A330-300";
            short2.groundStaffAssigned = Math.Max(0, OpsEconomics.RecommendedGroundStaff("A330-300") - 2);
            short2.SyncFlightsFromPrimary();
            FlightScheduleFormat.WindowTimes(short2, short2.flights[0], out _, out int a2, out int d2, out _);
            Check("[关键] 地勤缺人则过站变长（橙条随模型走，不是写死的 50 分）",
                d2 - a2 == 74 && d2 - a2 > turn, ref fail);

            Check("去程落地仍在当天（不跨午夜的去程段）：arr < 1440 且 arr > dep",
                dep < FlightScheduleFormat.MinutesPerDay && arr < FlightScheduleFormat.MinutesPerDay, ref fail);
            Check("回程落地已过午夜（跨夜尾段存在，不能只画主段）",
                rarr > FlightScheduleFormat.MinutesPerDay, ref fail);

            // 拆窗：去程段（当天）与回程段（主段到轴底 + 次日尾段）
            FlightScheduleFormat.SplitWindow(dep, arr, out int o1, out int s1, out int e1, out int t1);
            Check("去程段不跨午夜：无次日偏移、无尾段",
                o1 == 0 && t1 == 0 && s1 == dep && e1 == arr, ref fail);

            FlightScheduleFormat.SplitWindow(rdep, rarr, out int o2, out int s2, out int e2, out int t2);
            Check("[关键] 回程段拆成「主段到 24:00 + 次日尾段」，且主段不被丢弃（第七次报障的病）",
                o2 == 0 && s2 == rdep && e2 == FlightScheduleFormat.MinutesPerDay
                && t2 == rarr - FlightScheduleFormat.MinutesPerDay && t2 > 0, ref fail);

            // ★起点本身已过午夜的那一段（22:00 出发的红眼波）：整段平移一天，不得当成「主段 + 尾段」
            FlightScheduleFormat.SplitWindow(
                FlightScheduleFormat.MinutesPerDay + 543, FlightScheduleFormat.MinutesPerDay + 1066,
                out int o3, out int s3, out int e3, out int t3);
            Check("[关键] 起点已过午夜的航段整体后移一天（否则会被画成 00:00 起飞、凭空多出尾段）",
                o3 == 1 && s3 == 543 && e3 == 1066 && t3 == 0, ref fail);

            // 窗口口径仍与 WindowMinutes 一致（周计划/单航线页的时刻列靠它）
            FlightScheduleFormat.WindowMinutes(r, f, out int w0, out int w1);
            Check("WindowMinutes = （去程起飞, 回程落地）：与四段口径同源",
                w0 == dep && w1 == rarr, ref fail);
        }

        // ———————— R31 周计划与单航线的排班口径（2026-10-14 CEO 第七次报障） ————————
        //
        // 报障原文：「周计划里 FR148 是周一三五日，单航线 FR148/FR149 是周一二四六。请更正。
        // 　　　　　　请全局检测周计划和单航线的排班错误。」
        //
        // 两处缺陷，都是**同一份数据、两种画法**：
        // ① 周视图的星期圈错开一天：`AsWeekGridUI.Place` 拆跨夜窗时，把主段的 end 传成 1440，
        //    又用 `end >= 1440 ? end - 1440 : end` 归一 → 主段变 0 分钟被丢，**只剩次日凌晨的尾段**
        //    → 周一/三/五/日出发的班，画在周二/四/六/一的列上（正是报障截图的错开一天）。
        // ② 时刻列两个口径：周计划印 `depTime-arrTime`（**去程落地** 15:13），
        //    单航线页印 `outDepFull–inArrFull`（**回程落地** 次日 00:46）—— 同一条线两个「时刻」。
        //
        // 改法：拆窗与占窗收口到 `FlightScheduleFormat`（`WindowMinutes` / `SplitWindow` /
        // `GridRowDays` / `WindowLabel`），周视图与周计划/单航线/邮件**共用**；
        // 并新增 `AuditSchedule` 把航线级（周计划那一行）与班次级（单航线页）逐条对账。
        //
        // 第九次报障（2026-10-14，CEO 原话「这样排班可以排成每日一班，为何又说只能排周一三五日？」）：
        // 上一版为「尾段那天也别漏画」把次日也预标成有班（`GridDaysOf`），而 `Draw` 是按星期**整天
        // 重绘整趟往返** → 七列全画满，看着就是每日一班。**正解：列 = 执飞日，跨午夜由各段自己
        // 按 `SplitWindow` 的算术落到次日列**（06:30 那班只留 46 分细条，红眼班次日整上午都该在）。
        // 本段是纯函数：先复现旧拆窗的丢弃（把病钉住），再验新拆窗与对账。
        static void R31WeekGrid(ref int fail)
        {
            Console.WriteLine("\nR31 周计划与单航线的排班口径（星期圈 / 时刻列）：");

            // 报障同形的线：FRA—ATL，06:30 出发、回程次日 00:46 落地、班期 周一/三/五/日
            var r = MakeRoute("FR148", "06:30", "15:13", "ATL", "FRA");
            r.distanceKm = 7409f;
            r.aircraftType = "A330-300";
            r.daysOfWeek = "周一/三/五/日";
            r.weeklyFlights = 4;
            r.SyncFlightsFromPrimary();
            var f = r.flights[0];

            FlightScheduleFormat.WindowMinutes(r, f, out int s, out int e);
            var trip = FlightScheduleFormat.Trip(r, f);
            Check("[关键] 占窗跨午夜（去程 06:30 出发、回程次日落地 → end > 1440）",
                s == 390 && e > FlightScheduleFormat.MinutesPerDay
                && trip.outDepFull == "06:30", ref fail);

            // ① 旧拆窗的病：递归把主段的 end 传成 1440，旧归一式 `end >= 1440 ? end - 1440 : end`
            //    把 1440 变成 0 → e <= s → **主段被丢**，屏幕上只剩次日凌晨的尾段
            int legacyPassedEnd = FlightScheduleFormat.MinutesPerDay;   // 旧递归传给主段的 end
            int legacyE = legacyPassedEnd >= FlightScheduleFormat.MinutesPerDay
                ? legacyPassedEnd - FlightScheduleFormat.MinutesPerDay
                : legacyPassedEnd;
            Check("[关键] 旧式归一确实会吃掉主段（1440 → 0，e <= s 主段被丢）",
                legacyE == 0 && legacyE <= s, ref fail);

            // 新拆窗：主段 06:30→24:00，尾段 = 次日 00:00 → 回程落地
            FlightScheduleFormat.SplitWindow(s, e, out int ms, out int me, out int te);
            Check("[关键] 新拆窗主段画到轴底（06:30→24:00）且尾段 = 回程落地分钟数",
                ms == 390 && me == FlightScheduleFormat.MinutesPerDay
                && te == e - FlightScheduleFormat.MinutesPerDay
                && te == LiveClockLogic.ParseMinutes(trip.inArrFull), ref fail);

            // ② 星期圈：**只**标执飞日（一三五日）—— ★尾段那天不得预标
            var grid = FlightScheduleFormat.GridRowDays(f);
            bool departOk = grid[0] && grid[2] && grid[4] && grid[6];          // 一 三 五 日
            bool tailQuiet = !grid[1] && !grid[3] && !grid[5];                  // 二 四 六 不得因尾段被点亮
            Check("[关键] 星期圈 = 执飞日（一三五日）；二四六**不**因回程落地而点亮（否则读成「每日一班」）",
                departOk && tailQuiet, ref fail);
            Check("执飞日与周计划班期同口径（DaysOf 仍是一三五日）",
                FlightScheduleFormat.DaysSummary(FlightScheduleFormat.DaysOf(f.daysOfWeek)) == "周一/三/五/日",
                ref fail);

            // ③ 时刻列：周计划与单航线页印同一个串（去程起飞 → 回程落地，跨夜标 +1）
            string label = FlightScheduleFormat.WindowLabel(r, f);
            Check("[关键] 时刻列统一为往返占窗（06:30-回程落地+1）",
                label == $"{trip.outDepFull}-{trip.inArrFull}+1" && label.EndsWith("+1"), ref fail);

            // ③-b 跨午夜的那部分**怎么落列**：按时刻算，不按星期预标（第九次报障的正解）。
            //      06:30 那班只跨 46 分（次日凌晨一根细条）；22:00 红眼班次日整个上午都在飞 ——
            //      两者都必须由 SplitWindow 算出来，而不是「把次日整列也重绘一遍」。
            var redEye = MakeRoute("FR300", "22:00", "06:43", "ATL", "FRA");
            redEye.distanceKm = 7409f; redEye.aircraftType = "A330-300";
            redEye.SyncFlightsFromPrimary();
            var rf = redEye.flights[0];
            FlightScheduleFormat.WindowTimes(redEye, rf, out _, out int roArr, out int rrDep, out int rrArr);
            FlightScheduleFormat.SplitWindow(LiveClockLogic.ParseMinutes("22:00"), roArr,
                out int oOff, out int oS, out int oE, out int oTail);
            FlightScheduleFormat.SplitWindow(rrDep, rrArr, out int rOff, out int rS, out int rE, out int rTail);
            Check("[关键] 红眼班去程只画到轴底、余下落到次日（不是把整趟重绘到次日）",
                oOff == 0 && oS == 1320 && oE == FlightScheduleFormat.MinutesPerDay
                && oTail == roArr - FlightScheduleFormat.MinutesPerDay && oTail > 0, ref fail);
            Check("[关键] 红眼班过站与回程整段已过午夜 → dayOffset=1，整块画在次日列",
                rOff == 1 && rTail == 0
                && rS == rrDep - FlightScheduleFormat.MinutesPerDay
                && rE - rS == rrArr - rrDep, ref fail);

            // 不跨夜的线不得带 +1，也不得凭空多出尾段列
            var r2 = MakeRoute("FR100", "08:00", "08:46", "LHR", "FRA");
            r2.SyncFlightsFromPrimary();
            var f2 = r2.flights[0];
            var trip2 = FlightScheduleFormat.Trip(r2, f2);
            FlightScheduleFormat.WindowMinutes(r2, f2, out int s2, out int e2);
            FlightScheduleFormat.SplitWindow(s2, e2, out int ms2, out int me2, out int te2);
            Check("不跨夜的线：无尾段、时刻列不带 +1（且与 Trip 同口径）",
                e2 <= FlightScheduleFormat.MinutesPerDay && te2 == 0
                && FlightScheduleFormat.WindowLabel(r2, f2) == $"{trip2.outDepFull}-{trip2.inArrFull}"
                && !FlightScheduleFormat.WindowLabel(r2, f2).EndsWith("+1"), ref fail);

            // ④ 全局对账：航线级（周计划那一行）与班次级（单航线页）必须自洽
            var issues = FlightScheduleFormat.AuditSchedule(new List<RouteData> { r, r2 });
            Check("[关键] 自洽的世界对账零问题（班期并集 = 航线班期、周班之和 = 航线周班、时刻同源）",
                issues.Count == 0, ref fail);

            // 对账要能抓出「周计划一行与班次不一致」的世界
            var bad = MakeRoute("FR200", "08:00", "10:30", "PEK", "FRA");
            bad.daysOfWeek = "每日"; bad.weeklyFlights = 7;
            bad.SyncFlightsFromPrimary();
            bad.flights[0].daysOfWeek = "周一/三/五";   // 班次班期与航线班期对不上
            var badIssues = FlightScheduleFormat.AuditSchedule(new List<RouteData> { bad });
            Check("[关键] 班期对不上时审计必须点名（别让对账变成橡皮图章）",
                badIssues.Count > 0 && badIssues[0].Contains("班期"), ref fail);
        }

        // ———————————————————— R30 航班号的唯一性（2026-10-13 CEO 第六次报障） ————————————————————
        //
        // 报障原文：「周计划里 FRA-ATL 是 FR106，FRA-ORD 是 FR107。而 FRA-ATL 单航线是 FR106/FR107。」
        //
        // 根因是两条各自都对的规则叠在一起：① `Numbers()` 让回程号 = 去程号 + 1、第 index 个波次再 +2；
        // ② 建线时基号**连号**分配（`{航司码}{100 + i}`）。第 i 条线因此要占 `base … base+7`，
        // 而第 i+1 条线的基号 `base+1` 正好落在里头 —— 1 个波次也撞，4 个波次（周班 ≥ 8）整块重叠。
        // 界面白板 n42/n43 明写「航班号：唯一识别代码」，故这里把「唯一」钉成不变量。
        //
        // 本段是纯函数：造一个与截图同形的 8 条线世界（含 4 波次的 LHR），
        // 先证明**连号分配必然撞号**（把病钉住，别让判据悄悄失效），再证明**块分配零撞号**。
        static void R30FlightNumbers(ref int fail)
        {
            Console.WriteLine("\nR30 航班号的唯一性（回程号 ≠ 下一条线的去程号）：");

            // ① 旧世界：基号连号（100 + i）—— 报障现场。8 条线，LHR 4 波次（26 班/周）。
            var legacy = new List<RouteData>();
            string[] ods = { "LHR", "CDG", "AMS", "IST", "MUC", "MAD", "ATL", "ORD" };
            int[] weekly = { 26, 25, 25, 14, 18, 11, 4, 4 };
            for (int i = 0; i < ods.Length; i++)
            {
                var r = MakeRoute($"FR{100 + i}", "08:00", "10:30", ods[i], "FRA");
                r.weeklyFlights = weekly[i];
                r.daysOfWeek = "每日";
                r.flights.Clear();
                int[] waves = AirlineCEO.Logic.RouteFrequencyModel.WavesOf(weekly[i]);
                for (int w = 0; w < waves.Length; w++)
                    r.flights.Add(new RouteFlight
                    {
                        registration = "B-2007",
                        depTime = AirlineCEO.Logic.RouteFrequencyModel.WaveDeparture(w),
                        arrTime = "10:30",
                        daysOfWeek = "每日",
                        weeklyFlights = waves[w]
                    });
                legacy.Add(r);
            }

            var legacyConflicts = FlightNumberLedger.Conflicts(legacy);
            // 报障的那一处必须被检出：FRA—ATL 的回程号 FR107 撞上 FRA—ORD 的去程号（基号 FR107）
            // 报障那一处必须被检出：FR107 被**多条线**共用，且 ATL 与 ORD 都在其中
            // （实测 FR107 先被 LHR 的第 4 班回程占去 —— 26 班/周 = 4 个波次要占 FR100…FR107，
            //   随后 CDG / AMS / MUC / MAD / ATL / ORD 全撞上来；比报障看到的还要广）
            bool hitAtAtlOrd = false;
            string fr107Who = "";
            for (int i = 0; i < legacyConflicts.Count; i++)
            {
                var c = legacyConflicts[i];
                if (c.number != "FR107") continue;
                fr107Who += $"[{c.keeper} ↔ {c.taker}] ";
                if (c.keeper.Contains("ATL") || c.taker.Contains("ATL")) hitAtAtlOrd = true;
            }
            bool ordAlsoOn107 = fr107Who.Contains("ORD");
            hitAtAtlOrd = hitAtAtlOrd && ordAlsoOn107;
            Check("[关键] 连号分配必然撞号（旧世界确实有冲突，别让判据悄悄失效）",
                legacyConflicts.Count > 0, ref fail);
            Check("[关键] 报障那一处被点名：FR107 被多条线共用（ATL 与 ORD 都在其中）"
                  + (hitAtAtlOrd ? "" : "（实得：" + (fr107Who.Length == 0 ? "FR107 无冲突" : fr107Who.Trim()) + "）"),
                hitAtAtlOrd, ref fail);

            // 顺带证明 ATL 的 4 号也在被人用：LHR 有 4 个波次要占 FR100..FR107
            var atlPair = new List<string>();
            FlightScheduleFormat.Numbers(legacy[6], 0, out string atlOut, out string atlIn);
            Check("ATL 单航线页显示的正是「FR106 / FR107」（与报障截图一致）",
                atlOut == "FR106" && atlIn == "FR107", ref fail);

            // ② 新世界：块分配（块首 100 / 108 / 116 …）—— 同样 8 条线、同样波次
            var fixedWorld = new List<RouteData>();
            for (int i = 0; i < ods.Length; i++)
            {
                var src = legacy[i];
                var r = MakeRoute($"FR{FlightScheduleFormat.BaseNumberOf(i)}", "08:00", "10:30", ods[i], "FRA");
                r.weeklyFlights = src.weeklyFlights;
                r.daysOfWeek = src.daysOfWeek;
                r.flights = new List<RouteFlight>(src.flights);
                fixedWorld.Add(r);
            }
            var fixedConflicts = FlightNumberLedger.Conflicts(fixedWorld);
            Check("[关键] 块分配后全公司零撞号（" +
                  (fixedConflicts.Count == 0 ? "全绿" : "仍有 " + fixedConflicts.Count + " 处") + "）",
                fixedConflicts.Count == 0, ref fail);

            // ③ 块的算术：4 个波次要 8 个号，块距 = 8；任意两个波次的号都落在自己块内
            Check("块距 = 8（4 波次 × 去程/回程），块首 100 / 108 / 116",
                FlightScheduleFormat.BlockStride == 8
                && FlightScheduleFormat.BaseNumberOf(0) == 100
                && FlightScheduleFormat.BaseNumberOf(1) == 108
                && FlightScheduleFormat.BaseNumberOf(2) == 116, ref fail);

            FlightScheduleFormat.Numbers(fixedWorld[6], 0, out string aOut, out string aIn);
            FlightScheduleFormat.Numbers(fixedWorld[7], 0, out string oOut, out string oIn);
            Check("[关键] 报障的两条线从此各用各的号（ATL 与 ORD 不再交集）",
                aOut != oOut && aIn != oIn && aIn != oOut, ref fail);

            // ④ 前缀与数字的取法（审计靠它认号）
            Check("PrefixOf / NumberOf 认得航班号（FR106 → FR / 106）",
                FlightScheduleFormat.PrefixOf("FR106") == "FR"
                && FlightScheduleFormat.NumberOf("FR106") == 106
                && FlightScheduleFormat.NumberOf("FR") == -1
                && FlightScheduleFormat.NumberOf(null) == -1, ref fail);

            // ⑤ 兜底号不得再用哈希（哈希会撞号）：同一批航线里取出的兜底号必须谁都没占
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fixedWorld.Count; i++)
            {
                FlightScheduleFormat.Numbers(fixedWorld[i], 0, out string o1, out string i1);
                used.Add(o1); used.Add(i1);
            }
            Check("审计清单与撞号清单口径一致（无冲突时 Report 也报「全部唯一」）",
                FlightNumberLedger.Report(fixedWorld).Contains("全部唯一"), ref fail);
        }

        // ———————————————————— R29 邮件正文里的对象链接（2026-10-13 CEO 第五次报障） ————————————————————
        //
        // 报障原文：「红框中的飞机注册号没有和 B-2000 一样点击跳转飞机详情页的设置」——
        // 同一封「逐机归因」名单里，**第一架 B-2000 是链接、后面七架都不是**。
        //
        // 根因不在链接包得不好，而在**判「这个位置是不是已经在标记里」时把裸的小于号当成了标签**：
        // `InsideMarkup` 取「最近一个 `<`」与「最近一个 `>`」比大小，而这份正文（纯文本、零标签）
        // 里恰好写了比较式「航程不足（6300 km ＋ 减载余量 50 km < 本线 7409 km）」——
        // 于是第一架之后，每个机号前面都有个 `<`、而全串根本没有 `>`（gt = -1），
        // `lt > gt` 恒成立 → 一律判成「已在标记里」→ **整份名单只剩第一架可点**。
        // 一句小于号废掉其后所有链接，且不报错、不告警 —— 与 §17.9 那次「机场对只认一种键」
        // 同一类病：**页面看着完全正常，只是点不动**。
        //
        // 本段钉：① 报障那份名单里每一个机号都必须是 `aircraft:` 链接；② 裸的小于号不再污染后文；
        // ③ 真标记内的文本仍不得重复包装（别把修好的判据改回「到处都包」）。
        static void R29AircraftLinks(ref int fail)
        {
            Console.WriteLine("\nR29 邮件正文里的对象链接（裸「<」不得吃掉后文链接）：");

            // —— ① 报障正文：逐机归因名单，比较式里带裸的小于号 ——
            string body =
                "运营部按「安排合适飞机执飞 FRA—ATL」逐机核对，**机队里没有能飞本线的飞机**，故无法提名。\n\n"
                + "本线要求　航程 ≥ 7409 km（机型侧另给 50 km 减载余量）\n"
                + "机队现状　最长航程 11750 km（B-2009 A330-300），加余量后 11800 km，**航程富余 4391 km**。\n\n"
                + "逐机归因：\n"
                + "　·　B-2000 A330neo 航程不足（6300 km ＋ 减载余量 50 km < 本线 7409 km）\n"
                + "　·　B-2001 737-800 航程不足（5436 km ＋ 减载余量 50 km < 本线 7409 km）\n"
                + "　·　B-2002 A321 航程不足（5950 km ＋ 减载余量 50 km < 本线 7409 km）\n"
                + "　·　B-2007 A330-300 本线由该机执飞\n";
            string outBody = TermNavMap.Decorate(body);

            string[] regs = { "B-2000", "B-2001", "B-2002", "B-2007", "B-2009" };
            string missed = "";
            for (int i = 0; i < regs.Length; i++)
                if (!outBody.Contains("<link=aircraft:" + regs[i] + ">")) missed += regs[i] + " ";
            Check("[关键] 报障名单里每个机号都是可点链接（旧版只有第一架可点）"
                  + (missed.Length == 0 ? "" : "（漏：" + missed.Trim() + "）"),
                missed.Length == 0, ref fail);

            Check("[关键] 摆在比较式**前面**的机号也必须可点（小于号在它之后）",
                outBody.Contains("<link=aircraft:B-2009>"), ref fail);

            // —— ② 裸的小于号不得污染后文 ——
            string ltBody = TermNavMap.Decorate("航程不足（5950 km < 本线 7409 km）\n　·　B-2003 A330-300 能飞");
            Check("[关键] 裸「<」之后的对象仍可点（旧版被当成「已在标记里」而跳过）",
                ltBody.Contains("<link=aircraft:B-2003>"), ref fail);

            // —— ③ 真标记内的文本不得重复包装（判据放宽不能宽到「到处都包」）——
            string once = TermNavMap.Decorate("　·　B-2003 A330-300 能飞");
            int n = 0, at = 0;
            while ((at = once.IndexOf("<link=aircraft:B-2003>", at, StringComparison.Ordinal)) >= 0) { n++; at++; }
            Check("同一机号只包一次（不重复嵌套）", n == 1, ref fail);

            // —— ④ 正文里已有真链接时，其后文本仍不得被跳过 ——
            string mixed = TermNavMap.Decorate(TermNavMap.Decorate("北京首都 B-2004 A320neo\n　·　B-2005 737-800"));
            Check("[关键] 二次 Decorate 不得把已链接片段之后的机号漏掉",
                mixed.Contains("<link=aircraft:B-2004>") && mixed.Contains("<link=aircraft:B-2005>"), ref fail);

            // —— ⑤ 「去某页办这件事」的显式页面链接必须与关键词链接同口径，且能解析回同一页 ——
            // 单航线页「点此串环 ›」就是这类：文案写着一个页面名，落地却要精确到 pageId。
            // 判据分两段：包出来的 markup 里带的 id 必须是 page: 前缀；TryResolve 解回去必须是原页。
            string n98 = TermNavMap.PageLink("n98_航线航班环选择器", "点此串环 ›");
            bool wrapped = n98.Contains("<link=page:n98_航线航班环选择器>") && n98.Contains("点此串环 ›");
            bool resolved = TermNavMap.TryResolve("page:n98_航线航班环选择器", out var hitRing)
                            && hitRing.pageId == "n98_航线航班环选择器";
            Check("[关键] PageLink 包出的页面链接可解析回原页面（串环入口点得动）",
                wrapped && resolved, ref fail);
            Check("PageLink 传空页 id 时原样返回文案（不产出死链）",
                TermNavMap.PageLink("", "点此串环 ›") == "点此串环 ›"
                && TermNavMap.PageLink("n9_机队", null) == "", ref fail);
        }

        // ———————————————————— R40 地图机型剪影（2026-10-30 CEO：白三角换成机型图标） ————————————————————
        //
        // 报障原文：「地图上的飞机用的白色三角形，换成执飞的机型，用相应的机型图标」。
        // 这里不验「好不好看」（那是人眼的事），只验三件机器可判定的事：
        // ① 机型代号分到哪个外形族（分错了，图标就永远画错）；
        // ② 剪影本身是否成立（非空、左右对称、不吃图边）；
        // ③ 六个族是否真的**互不相同**（全画成同一个形状等于没换）。
        static void R40PlaneSilhouettes(ref int fail)
        {
            Console.WriteLine("\nR40 地图机型剪影（几台发动机 / 装在哪儿 / 机翼姿势）：");

            var cases = new (string type, AircraftSilhouette want, string why)[]
            {
                ("A321", AircraftSilhouette.Narrowbody, "窄体双发"),
                ("B737-800", AircraftSilhouette.Narrowbody, "窄体双发"),
                ("C919", AircraftSilhouette.Narrowbody, "窄体双发"),
                ("B787-9", AircraftSilhouette.Widebody, "宽体双发"),
                ("A330-900", AircraftSilhouette.Widebody, "宽体双发"),
                ("B777-300ER", AircraftSilhouette.Widebody, "宽体双发"),
                ("A380-800", AircraftSilhouette.QuadJet, "四发"),
                ("B747-8", AircraftSilhouette.QuadJet, "四发"),
                ("A340-600", AircraftSilhouette.QuadJet, "四发"),
                ("MD-11", AircraftSilhouette.TriJet, "三发"),
                ("ATR72-600", AircraftSilhouette.Turboprop, "涡桨（平直翼 + 桨盘）"),
                ("DHC8-400", AircraftSilhouette.Turboprop, "涡桨（平直翼 + 桨盘）"),
                ("CRJ900", AircraftSilhouette.RegionalJet, "尾吊双发支线"),
                ("ERJ145", AircraftSilhouette.RegionalJet, "尾吊双发支线"),
            };
            for (int i = 0; i < cases.Length; i++)
            {
                var got = AircraftSilhouetteGeometry.Classify(cases[i].type);
                Check($"{cases[i].type} → {cases[i].why}"
                      + (got == cases[i].want ? "" : $"（实得 {AircraftSilhouetteGeometry.Label(got)}）"),
                    got == cases[i].want, ref fail);
            }

            // 目录/存档里可能留着旧代号或不存在的型号：兜底必须是「能画出来」，不能是空
            Check("目录外的机型代号兜底为窄体（不返回空、不抛异常）",
                AircraftSilhouetteGeometry.Classify("XX-999") == AircraftSilhouette.Narrowbody
                && AircraftSilhouetteGeometry.Classify(null) == AircraftSilhouette.Narrowbody
                && AircraftSilhouetteGeometry.Classify("") == AircraftSilhouette.Narrowbody, ref fail);

            // 尺寸必须按翼展拉开：A380 与 CRJ 在 40 px 上还要能看出大小差
            float sQuad = AircraftSilhouetteGeometry.IconSizePx(AircraftSilhouette.QuadJet);
            float sWide = AircraftSilhouetteGeometry.IconSizePx(AircraftSilhouette.Widebody);
            float sNarrow = AircraftSilhouetteGeometry.IconSizePx(AircraftSilhouette.Narrowbody);
            float sProp = AircraftSilhouetteGeometry.IconSizePx(AircraftSilhouette.Turboprop);
            Check($"图标尺寸随翼展递减 四发 {sQuad:F0} > 宽体 {sWide:F0} > 窄体 {sNarrow:F0} > 涡桨 {sProp:F0}",
                sQuad > sWide && sWide > sNarrow && sNarrow > sProp && sProp >= 26f && sQuad <= 50f, ref fail);

            // 剪影本身的几何不变量
            const int N = 64;
            var kinds = (AircraftSilhouette[])Enum.GetValues(typeof(AircraftSilhouette));
            var covs = new Dictionary<AircraftSilhouette, float[]>();
            for (int k = 0; k < kinds.Length; k++)
                covs[kinds[k]] = AircraftSilhouetteGeometry.Coverage(kinds[k], N);

            for (int k = 0; k < kinds.Length; k++)
            {
                var cov = covs[kinds[k]];
                string name = AircraftSilhouetteGeometry.Label(kinds[k]);
                float sum = 0f, maxAsym = 0f;
                int minRow = N, maxRow = -1, minCol = N, maxCol = -1;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float a = cov[y * N + x];
                        sum += a;
                        float d = Math.Abs(a - cov[y * N + (N - 1 - x)]);
                        if (d > maxAsym) maxAsym = d;
                        if (a <= 0.02f) continue;
                        if (y < minRow) minRow = y;
                        if (y > maxRow) maxRow = y;
                        if (x < minCol) minCol = x;
                        if (x > maxCol) maxCol = x;
                    }

                Check($"{name}：剪影非空且不糊成一片（覆盖 {sum / (N * N) * 100f:F1}%）",
                    sum / (N * N) > 0.05f && sum / (N * N) < 0.75f, ref fail);
                Check($"{name}：左右对称（最大偏差 {maxAsym:F3}）", maxAsym <= 0.02f, ref fail);

                if (maxRow < 0) { Check($"{name}：有空包围盒", false, ref fail); continue; }
                // 归一化包围盒（-1..1）：机翼要够宽、机身要够长，否则「认不出是飞机」
                float bx0 = minCol * 2f / N - 1f, bx1 = (maxCol + 1) * 2f / N - 1f;
                float by0 = minRow * 2f / N - 1f, by1 = (maxRow + 1) * 2f / N - 1f;
                Check($"{name}：翼展 {bx1 - bx0:F2} / 机长 {by1 - by0:F2} 都够辨认",
                    bx1 - bx0 >= 0.45f && by1 - by0 >= 0.60f, ref fail);
                Check($"{name}：包围盒不贴图边（描边放大时不会被裁）",
                    bx0 > -0.99f && bx1 < 0.99f && by0 > -0.99f && by1 < 0.99f, ref fail);
            }

            for (int a = 0; a < kinds.Length; a++)
                for (int b = a + 1; b < kinds.Length; b++)
                {
                    var ca = covs[kinds[a]];
                    var cb = covs[kinds[b]];
                    int diff = 0;
                    for (int i = 0; i < ca.Length; i++)
                        if (Math.Abs(ca[i] - cb[i]) > 0.5f) diff++;
                    Check($"{AircraftSilhouetteGeometry.Label(kinds[a])} ≠ {AircraftSilhouetteGeometry.Label(kinds[b])}"
                          + $"（不同的像素 {diff} 个）",
                        diff > ca.Length / 100, ref fail);
                }
        }

        /// <summary>R19 诊断行：打印一条 CEO 指令的实际解析结果（walk the parse）。</summary>
        static void ShowCmd(string text)
        {            var d = CapacityDirectiveLogic.Parse(text);
            Console.WriteLine($"  「{text}」");
            Console.WriteLine($"     → intent={d.intent}{(d.intentKnown ? "" : "(未明)")} waves={d.waves}"
                + $" maximize={d.maximize} deps={(d.deps == null ? "—" : string.Join("/", d.deps))}"
                + $" days={(string.IsNullOrEmpty(d.days) ? "—" : d.days)} ring={d.buildRing}"
                + $"｜需确认={CapacityDirectiveLogic.NeedsConfirm(d)}");
            if (CapacityDirectiveLogic.NeedsConfirm(d))
                Console.WriteLine($"     拟定：{CapacityDirectiveLogic.PlanLine(null, d)}");
        }

        static RouteData MakeRoute(string flightNo, string dep, string arr,
            string destination, string origin = "SZX")
        {
            var route = new RouteData
            {
                routeId = $"R-{origin}-{destination}-{flightNo}",
                origin = origin,
                destination = destination,
                distanceKm = 1940f,
                aircraftType = "A320neo",
                flightNumber = flightNo,
                registration = "B-9001",
                depTime = dep,
                arrTime = arr,
                daysOfWeek = "每日",
                weeklyFlights = 7,
                groundStaffAssigned = 3,
                baseDailyProfit = 10f,
                dailyProfit = 10f
            };
            route.SyncFlightsFromPrimary();
            return route;
        }

        /// <summary>R24 用：只关心「本线多远」的航线（其余字段不参与航程判定）。</summary>
        static RouteData MakeRouteFar(string flightNo, float km)
        {
            var r = MakeRoute(flightNo, "08:00", "10:30", "PEK");
            r.distanceKm = km;
            return r;
        }

        static string Describe(List<RoutePermitLogic.SlotNeed> needs)
        {
            var parts = new List<string>();
            for (int i = 0; i < needs.Count; i++)
                parts.Add($"{RoutePermitLogic.LegName(needs[i])} {needs[i].airport}"
                    + $"{ScheduleSlots.SlotBandName(needs[i].band)} {needs[i].flightNo}"
                    + $"（{RoutePermitLogic.LegClocks(needs[i])}）");
            return parts.Count == 0 ? "（无）" : string.Join("、", parts);
        }

        /// <summary>需求清单里落在某机场的条数（经停端应为 2：去程落一次 + 回程落一次）。</summary>
        static int CountAirport(List<RoutePermitLogic.SlotNeed> needs, string airport)
        {
            int n = 0;
            for (int i = 0; i < needs.Count; i++)
                if (string.Equals(needs[i].airport, airport, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        static void Check(string name, bool ok, ref int fail)
        {
            if (!ok) fail++;
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
        }
    }
}
