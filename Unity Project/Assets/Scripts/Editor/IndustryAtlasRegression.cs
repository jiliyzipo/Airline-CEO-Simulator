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
    /// R48 行业情报两张表（★2026-11-05 用户要求）。
    ///
    /// 用户要的是「航权全景」「机场时刻」两张表。这类表最容易犯的错不是画不出来，
    /// 而是**画出来的数跟玩法对不上**：表上写着这个时刻空着，游戏却说池满了；
    /// 或者表上国航占着 3 条，我司却能把整个池子拿走。所以这里钉的是**恒等式**：
    ///
    /// <code>池容量 = 我司持有 + 他航持有 + 可申请余量</code>
    ///
    /// 以及它与 <c>RoutePermitLogic.SlotsAvailable</c> 的同一性 —— 两处一旦分家，
    /// CEO 就会照着表去做一个必然失败的申请。
    ///
    /// 另钉两件与「确定性」有关的事：
    /// <list type="bullet">
    /// <item>同一存档连查两次，表必须一模一样（用 <c>string.GetHashCode</c> 就会重启后变样）。</item>
    /// <item>他航永远占不满池子 —— 至少留 1 条，否则「开航缺槽」会变成死结，玩家没有任何办法。</item>
    /// </list>
    /// </summary>
    public static class IndustryAtlasRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__industry_atlas__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 999999f;
                gs.hubIata = "PEK";
                gs.companyNameZh = "测试航空";
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                var stg = StrategyManager.EnsureState(gs);
                if (stg == null)
                {
                    Debug.LogError("R48 夹具失败：StrategyManager.EnsureState 返回 null。");
                    return false;
                }

                bool identityOk = true;       // 容量恒等式
                bool availOk = true;          // 与 SlotsAvailable 同口径
                bool neverFull = true;        // 他航永不占满
                bool deterministic = true;    // 连查两次一致
                bool holderLenOk = true;      // 逐个槽的数组长度 = 容量
                int airportsChecked = 0, bandsChecked = 0;

                var airports = AirportCatalog.Airports;
                for (int i = 0; i < airports.Count; i++)
                {
                    var ap = airports[i];
                    if (!ap.coordinated) continue;
                    airportsChecked++;
                    for (int b = 0; b < 4; b++)
                    {
                        bandsChecked++;
                        int cap = RoutePermitLogic.SlotPoolCapacity(ap.code, b);
                        int ours = RoutePermitLogic.SlotsHeld(gs, ap.code, b);
                        int other = IndustryAirlineLogic.IndustryHeld(gs, ap.code, b);
                        int open = IndustryAirlineLogic.OpenSlots(gs, ap.code, b);

                        if (ours + other + open != cap) identityOk = false;
                        if (open != RoutePermitLogic.SlotsAvailable(gs, ap.code, b)) availOk = false;
                        if (cap >= 2 && ours == 0 && open < 1) neverFull = false;
                        if (other > cap - 1) neverFull = false;
                        if (other < 0 || open < 0) identityOk = false;

                        // ★2026-11-06：到港池也要守同一套恒等式与「永不死结」。
                        // 一停站各取一进一出；若只验离港池，玩家会拿着起飞权却落不下去 ——
                        // 那是比「开不了航」更难解释的死结。
                        int oursArr = RoutePermitLogic.PoolUse(gs, ap.code, b,
                            AirlineCEO.Data.SlotHolding.SlotKindArrival);
                        int otherArr = IndustryAirlineLogic.IndustryHeld(gs, ap.code, b,
                            AirlineCEO.Data.SlotHolding.SlotKindArrival);
                        int openArr = RoutePermitLogic.SlotsAvailable(gs, ap.code, b,
                            AirlineCEO.Data.SlotHolding.SlotKindArrival);
                        if (oursArr + otherArr + openArr != cap) identityOk = false;
                        if (cap >= 2 && oursArr == 0 && openArr < 1) neverFull = false;
                        if (otherArr > cap - 1) neverFull = false;
                        if (otherArr < 0 || openArr < 0) identityOk = false;

                        var h1 = IndustryAirlineLogic.SlotHolders(gs, ap.code, b);
                        var h2 = IndustryAirlineLogic.SlotHolders(gs, ap.code, b);
                        if (h1.Count != cap) holderLenOk = false;
                        if (h1.Count != h2.Count) deterministic = false;
                        else
                        {
                            for (int k = 0; k < h1.Count; k++)
                                if (!string.Equals(h1[k], h2[k])) deterministic = false;
                        }
                        if (h1.Count == cap)
                        {
                            int empty = 0;
                            for (int k = 0; k < h1.Count; k++) if (string.IsNullOrEmpty(h1[k])) empty++;
                            if (empty != open) identityOk = false;
                        }
                    }
                }

                if (!identityOk)
                    Debug.LogError("R48 容量恒等式不成立：我司 + 他航 + 余量 ≠ 池容量（表上的「余 N」会与玩法对不上）。");
                if (!availOk)
                    Debug.LogError("R48 表的余量与 RoutePermitLogic.SlotsAvailable 不一致 —— CEO 会照着表去申请一个必然失败的槽。");
                if (!neverFull)
                    Debug.LogError("R48 他航把某个时刻池占满了（空池机场也申请不到）—— 开航缺槽会变成死结。");
                if (!deterministic)
                    Debug.LogError("R48 同一存档连查两次，他航持有表变了 —— 用了不稳定的哈希（禁止 string.GetHashCode）。");
                if (!holderLenOk)
                    Debug.LogError("R48 逐个槽的持有者数组长度 ≠ 池容量。");

                // ———— 买空：我司把余量吃干净后，表上不该再有「—」，且余量为 0 ————
                // 用一个有余量的协调机场做，避免影响上面的恒等式统计顺序。
                bool drainOk = true;
                for (int i = 0; i < airports.Count; i++)
                {
                    var ap = airports[i];
                    if (!ap.coordinated) continue;
                    int band = -1;
                    for (int b = 0; b < 4; b++)
                        if (RoutePermitLogic.SlotsAvailable(gs, ap.code, b) > 0) { band = b; break; }
                    if (band < 0) continue;

                    var grid = ScheduleSlots.BandGridLabels(band);
                    int guard = 0;
                    while (RoutePermitLogic.SlotsAvailable(gs, ap.code, band) > 0 && guard++ < 64)
                    {
                        // 15 分钟粒度下每刻容量 0/1/2，买空要逐刻找还有余量的那一个
                        string target = null;
                        for (int g = 0; g < grid.Count; g++)
                            if (RoutePermitLogic.SlotsAvailableAt(gs, ap.code, grid[g]) > 0)
                            { target = grid[g]; break; }
                        if (target == null) break;
                        if (!StrategyManager.AcquireSlotsAt(gs, ap.code, target, 1, true, out _)) break;
                    }

                    int left = RoutePermitLogic.SlotsAvailable(gs, ap.code, band);
                    var holders = IndustryAirlineLogic.SlotHolders(gs, ap.code, band);
                    int empty = 0;
                    for (int k = 0; k < holders.Count; k++) if (string.IsNullOrEmpty(holders[k])) empty++;
                    if (left != 0 || empty != 0)
                    {
                        drainOk = false;
                        Debug.LogError($"R48 买空后仍不一致：{ap.code} {ScheduleSlots.SlotBandName(band)} "
                                       + $"余 {left}、表上空位 {empty}（期望都是 0）。");
                    }
                    break;
                }

                // ———— 航权表：每家至少一条、全部国际、不含国内航段 ————
                bool rightsOk = true;
                int totalRights = 0;
                for (int i = 0; i < IndustryAirlines.All.Length; i++)
                {
                    var a = IndustryAirlines.All[i];
                    var rights = IndustryAirlineLogic.RightsOf(a);
                    totalRights += rights.Count;
                    if (rights.Count == 0)
                    {
                        rightsOk = false;
                        Debug.LogError($"R48 他航「{a.name}」一条航权都推不出来 —— 表里会是一条光杆。");
                        continue;
                    }
                    for (int k = 0; k < rights.Count; k++)
                    {
                        string pair = rights[k][0];
                        string[] seg = pair.Split('—');
                        if (seg.Length != 2) { rightsOk = false; continue; }
                        int fd = AirportRegions.FreedomOf(a.region, seg[0], seg[1]);
                        if (fd <= 0)
                        {
                            rightsOk = false;
                            Debug.LogError($"R48 航权表出现非航权条目「{pair}」（{a.name}）—— 国内/国内载权不该进这张表。");
                        }
                    }
                }
                if (totalRights < IndustryAirlines.All.Length)
                    Debug.LogError("R48 航权条目总数少于航司家数（推导逻辑失效）。");

                // ———— 卡片数据（★2026-11-05 二次改版：文本表换成卡片视图）————
                // 钉四件事：
                //   ① 每个网格时刻都有一块（不漏渲染）；
                //   ② Σ 系列数 == 已占用槽数（不多不少，方块没有凭空增减）；
                //   ③ 绿块（整刻无人）只在**本段真有余量**时出现 —— 满池段不许出现绿色；
                //   ④ 有余量且占用少于时刻数时，绿块必须真的出现 —— 首版按顺序填槽，
                //      前几个时刻被塞满、后面全空，界面上「整段全满」却写着「余 6」，
                //      正是这条断言要挡住的自相矛盾。
                bool chipsOk = true;
                int chipBands = 0, freeChips = 0, vacantChips = 0, ownerChips = 0;
                var probe = new List<AirportInfo>();
                var allAp = AirportCatalog.Airports;
                for (int i = 0; i < allAp.Count && probe.Count < 6; i++)
                    if (allAp[i].coordinated) probe.Add(allAp[i]);

                for (int i = 0; i < probe.Count; i++)
                {
                    var ap = probe[i];
                    for (int b = 0; b < 4; b++)
                    {
                        chipBands++;
                        int cap = RoutePermitLogic.SlotPoolCapacity(ap.code, b);
                        int open = IndustryAirlineLogic.OpenSlots(gs, ap.code, b);
                        int occupied = cap - open;
                        var chips = IndustryAirlineLogic.ChipsOf(gs, ap.code, b);
                        // ★2026-11-06：期望网格改按**整点区间**（与池容量、卡片同口径），
                        // 否则「06:00 / 09:45 / 10:00…」这些肩峰刻会被判成越界块。
                        var grid = ScheduleSlots.BandGridLabels(b);

                        // ★2026-11-05 三次（用户：只展示 可申请 / 我司 / 他航 三种）：
                        // 期望值 = 网格里所有**容量 > 0** 的刻（可以持有槽的刻）。
                        // 未公布、该小时不分配、协调时段外，三类都不出块 —— 它们不是「一条归属于谁的槽」。
                        var expect = new List<string>(grid.Count);
                        for (int g = 0; g < grid.Count; g++)
                        {
                            int m = ScheduleSlots.ParseClock(grid[g]);
                            if (m >= 0 && AirlineCEO.Data.AirportCatalog.QuarterCapacity(ap.code, m) > 0)
                                expect.Add(grid[g]);
                        }

                        if (chips.Count != expect.Count)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"方块 {chips.Count} 个 ≠ 应呈现的可持有槽位 {expect.Count} 个"
                                           + $"（网格 {grid.Count} 刻，容量 0 的刻不该出块）。");
                            continue;
                        }

                        int series = 0, free = 0, vacant = 0, usedChips = 0, capSum = 0;
                        for (int k = 0; k < chips.Count; k++)
                        {
                            if (chips[k].time != expect[k])
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                               + $"第 {k} 块是 {chips[k].time}，应为 {expect[k]}（顺序或筛选错了）。");
                            }
                            series += chips[k].series;

                            // 表里只剩三种状态：可申请（free）/ 我司（mine）/ 他航（owner 非空）。
                            // 容量必然 > 0（上面已按容量筛过），故这里不需要再判「无槽」分支。
                            int m = ScheduleSlots.ParseClock(chips[k].time);
                            int qcap = AirlineCEO.Data.AirportCatalog.QuarterCapacity(ap.code, m);
                            capSum += qcap;

                            if (qcap <= 0)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                               + $"{chips[k].time} 容量 0 却出了块 —— 只允许三种时刻，其它类型必须不显示。");
                                vacant++;
                                continue;
                            }
                            if (chips[k].series > qcap)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                               + $"{chips[k].time} 放了 {chips[k].series} 条，超过该刻容量 {qcap}。");
                            }

                            if (chips[k].free) free++;
                            else if (string.IsNullOrEmpty(chips[k].owner)) chipsOk = false;
                            else { usedChips++; if (!chips[k].mine) ownerChips++; }

                            // ★2026-11-07 用户裁决：浮窗只如实报「容量 / 已占 / 可申请」，
                            // 三个数必须自洽 —— 「容量 1 · 已占 1（汉莎）· 可申请 1」那种自相矛盾，
                            // 玩家一眼就看得出是错的。故这里把不等式钉死，改口径时先红这条。
                            int depHeld = chips[k].held > 0 ? chips[k].held : chips[k].series;
                            if (chips[k].freeLeft + depHeld > qcap)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {chips[k].time} 浮窗自相矛盾："
                                               + $"容量 {qcap} · 已占 {depHeld} · 可申请 {chips[k].freeLeft}（三者相加超过容量）。");
                            }
                            // 到港侧同一条：已占 + 可申请不得超过该刻容量。
                            if (chips[k].arrHeldTotal + chips[k].arrFreeLeft > qcap)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {chips[k].time} 到港浮窗自相矛盾："
                                               + $"容量 {qcap} · 已占 {chips[k].arrHeldTotal} · 可申请 {chips[k].arrFreeLeft}。");
                            }
                            // 已占满（已占 = 容量）就不许再报「可申请」—— 这就是用户报的那条错误。
                            if (depHeld >= qcap && chips[k].freeLeft > 0)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {chips[k].time} 已占满却仍报可申请 {chips[k].freeLeft} 条"
                                               + $"（容量 {qcap} · 已占 {depHeld}）。");
                            }
                            if (chips[k].arrHeldTotal >= qcap && chips[k].arrFreeLeft > 0)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {chips[k].time} 到港已占满却仍报可申请 {chips[k].arrFreeLeft} 条"
                                               + $"（容量 {qcap} · 已占 {chips[k].arrHeldTotal}）。");
                            }
                        }
                        freeChips += free;
                        vacantChips += vacant;

                        if (capSum != cap)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"逐刻容量合计 {capSum} ≠ 时段池容量 {cap}（小时容量派生与池口径分家）。");
                        }
                        if (series != occupied)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"方块系列合计 {series} ≠ 已占用 {occupied}（池 {cap} · 余 {open}）。");
                        }
                        if (free + vacant != chips.Count - usedChips)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}：空块计数与已占用块数不自洽。");
                        }
                        if (free > 0 && open <= 0)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + "满池段却画出了绿色「可申请」方块。");
                        }
                        // 段里还有余量时，必须至少有一块能放新的槽（空块，或某个「×2」刻只放了一条）。
                        if (open > 0 && free == 0)
                        {
                            int partial = 0;
                            for (int k = 0; k < chips.Count; k++)
                            {
                                int qcap = AirlineCEO.Data.AirportCatalog.QuarterCapacity(
                                    ap.code, ScheduleSlots.ParseClock(chips[k].time));
                                if (qcap > 1 && chips[k].series > 0 && chips[k].series < qcap) partial++;
                            }
                            if (partial == 0)
                            {
                                chipsOk = false;
                                Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                               + $"余 {open} 条，却没有任何一块还能放槽。");
                            }
                        }
                    }
                }
                if (ownerChips == 0) chipsOk = false;   // 一块他航的都没有 → 推导失效

                // ———— 表里只有三种时刻（★2026-11-05 用户第三次裁决）————
                // 抽查的 6 座都是 24h 协调枢纽，它们的四个时段里「容量 0 的刻」一个都没有，
                // 也就是说「不该出块的东西被误画出来」这件事在抽查里根本走不到。
                // 故用两座例外机场各钉一条必现用例：
                //   · ZUH 限时窗口 07:00–23:00 → 早段 06:30 / 06:45 在窗口外（容量 0）⇒ **不许出块**，
                //     早段第一块必须是 07:00；
                //   · KWE 窗口含 23:00 但干线该小时容量 0 ⇒ **不许出块**。
                // 另钉一条全局口径：每块都必须是「可申请 / 我司 / 他航」三者之一，容量必须 > 0。
                bool stateOk = true;
                {
                    var zuhEarly = IndustryAirlineLogic.ChipsOf(gs, "ZUH", 0);
                    if (zuhEarly.Count == 0 || zuhEarly[0].time != "07:00")
                    {
                        stateOk = false;
                        Debug.LogError($"R48 三种时刻：ZUH 早段（窗口 07:00–23:00）第一块应为 07:00，"
                                       + $"实际 {(zuhEarly.Count == 0 ? "空" : zuhEarly[0].time)} —— "
                                       + "窗口外的 06:30 / 06:45 必须不显示。");
                    }
                    for (int k = 0; k < zuhEarly.Count; k++)
                        if (zuhEarly[k].time == "06:30" || zuhEarly[k].time == "06:45")
                        {
                            stateOk = false;
                            Debug.LogError($"R48 三种时刻：ZUH 早段出现了 {zuhEarly[k].time}（协调窗口外，容量 0），不该显示。");
                        }

                    var kweNight = IndustryAirlineLogic.ChipsOf(gs, "KWE", 3);
                    for (int k = 0; k < kweNight.Count; k++)
                        if (kweNight[k].time == "23:00")
                        {
                            stateOk = false;
                            Debug.LogError("R48 三种时刻：KWE 23:00（干线该小时容量 0）不该显示。");
                        }

                    // ———— 免槽必须**真免**（★2026-11-05 用户追问「什么叫免槽」时查出）————
                    // 卡片现在不再画免槽了，但**规则**还在：限时协调机场窗口外不需要槽。
                    // 这条断言钉的是引擎口径 —— `QuarterExempt` ⟺ `NeedsDepartureSlot(机场, 时刻) == false`。
                    // 曾经三处判据只看机场等级、只有用率统计看时刻，导致「统计认为免槽、排班认为缺槽」，
                    // 而且玩家照着窗口外时刻排班会被要求一条容量为 0、永远申请不到的槽。
                    int exemptMismatch = 0;
                    var grid24 = ScheduleSlots.SlotGrid24();
                    for (int a = 0; a < allAp.Count; a++)
                    {
                        var ap2 = allAp[a];
                        if (!ap2.coordinated) continue;
                        int windowLen = 0;
                        for (int q = 0; q < grid24.Count; q++)
                        {
                            int m = grid24[q];
                            if (AirlineCEO.Data.AirportCatalog.InCoordWindow(ap2.code, m)) windowLen++;
                            bool exempt = AirlineCEO.Data.AirportCatalog.QuarterState(ap2.code, m)
                                          == AirlineCEO.Data.AirportCatalog.QuarterExempt;
                            bool needs = RoutePermitLogic.NeedsDepartureSlot(
                                ap2.code, ScheduleSlots.FormatClock(m));
                            if (exempt == needs) exemptMismatch++;   // 免槽却要槽，或需槽却判免槽
                        }
                        // 24h 协调机场整天都在窗口内：必须一条免槽都没有（防止窗口解析退化成「处处免槽」）
                        if (windowLen == grid24.Count && windowLen > 0)
                        {
                            for (int q = 0; q < grid24.Count; q++)
                                if (AirlineCEO.Data.AirportCatalog.QuarterState(ap2.code, grid24[q])
                                    == AirlineCEO.Data.AirportCatalog.QuarterExempt) exemptMismatch++;
                        }
                    }
                    if (exemptMismatch != 0)
                    {
                        stateOk = false;
                        Debug.LogError($"R48 免槽口径：刻钟判据与排班闸门（NeedsDepartureSlot(机场, 时刻)）"
                                       + $"有 {exemptMismatch} 个刻钟不一致 —— 窗口外被当成要槽"
                                       + "（或窗口内被判免槽），排班结果必然与预期相反。");
                    }
                }

                // 过滤/排序：只看枢纽 → 结果全是枢纽；我司航网 → 必然包含基地。
                var hubs = IndustryAirlineLogic.FilteredAirports(gs, IndustryAirlineLogic.FilterHub, false, "");
                bool filterOk = hubs.Count > 0;
                for (int i = 0; i < hubs.Count; i++) if (hubs[i].tier != 0) filterOk = false;
                var mine = IndustryAirlineLogic.FilteredAirports(gs, IndustryAirlineLogic.FilterMine, false, "");
                bool hasHub = false;
                for (int i = 0; i < mine.Count; i++)
                    if (string.Equals(mine[i].code, gs.hubIata, StringComparison.OrdinalIgnoreCase)) hasHub = true;
                if (!hasHub) filterOk = false;   // 基地机场必然在我司航网里

                string rightText = IndustryAirlineLogic.RightsTableText(gs);
                string summary = IndustryAirlineLogic.SlotsSummary(gs);
                bool textOk =
                    !string.IsNullOrEmpty(rightText) && rightText.Contains(gs.companyNameZh)
                    && rightText.Contains(IndustryAirlines.All[0].name)
                    && summary.Contains("可申请余量") && summary.Contains("他航占用");
                if (!textOk)
                    Debug.LogError("R48 航权全景文本或顶部统计不完整（缺我司名/他航名/统计口径）。");

                // ———— 槽价按小时取（★2026-11-05 用户裁决 B；★2026-11-07 用户裁决 A 修订）————
                // ① 形状跟随需求：早峰 / 晚峰高，午峰居中，夜班最低 —— 价格不许与需求倒挂
                //    （改动前晚班 1.10 的需求配 0.85 的价，是全表最反直觉的一格）。
                // ② **单槽价不动**：2026-11-07 容量按「真实曲线 × 1/3.5」放大后（PEK 日容量
                //    56 → 169），用户明确裁决「单槽价格绝对不能降 —— 机场是扩建释放配额，
                //    黄金时刻的单价仍由市场需求决定」。所以这里**不再对「买下全天总价」设死基线**：
                //    总价 = 单槽价 × 槽数，必然随容量等比上升，拿它当难度尺子等于逼价格补贴容量。
                //    改为直接钉「单槽买入」这一层：
                //      · 单价只由「等级 × 小时」决定，与机场大小 / 当天放出多少槽无关；
                //      · 峰刻单价锚定 630 万（枢纽）/ 315 万（干线），夜班低于峰刻。
                bool priceOk = true;
                var coef = StrategyEconomics.HourDemandCoef;
                if (!(coef[18] > coef[12] && coef[8] >= coef[18] && coef[22] < coef[12]))
                {
                    priceOk = false;
                    Debug.LogError($"R48 槽价小时曲线形状不对：晚峰应高于午峰、早峰不低于晚峰、夜班低于午峰"
                                   + $"（早 {coef[8]:F2} / 午 {coef[12]:F2} / 晚 {coef[18]:F2} / 夜 {coef[22]:F2}）。");
                }
                float hubPeak = StrategyEconomics.SlotValueWanAtHour(0, 8);
                float trunkPeak = StrategyEconomics.SlotValueWanAtHour(1, 8);
                float hubNight = StrategyEconomics.SlotValueWanAtHour(0, 22);
                if (Mathf.Abs(hubPeak - 630f) > 0.5f || Mathf.Abs(trunkPeak - 315f) > 0.5f || hubNight >= hubPeak)
                {
                    priceOk = false;
                    Debug.LogError($"R48 单槽峰价漂移：枢纽 08:00 = {hubPeak:F0} 万（应 630）"
                                   + $" / 干线 = {trunkPeak:F0} 万（应 315） / 枢纽 22:00 = {hubNight:F0} 万（应低于峰刻）。"
                                   + "容量放大时若顺手把单价调低，黄金时刻就变成白菜价，稀缺感被抹掉。");
                }
                // 同等级、同小时，无论机场多大、当天放出多少槽，单价必须完全相同 ——
                // 这是「单槽价 = 这条时刻本身值多少」而非「机场总容量均摊」的直接判据。
                if (Mathf.Abs(StrategyEconomics.SlotValueWanAt("PEK", "08:00")
                              - StrategyEconomics.SlotValueWanAt("ATL", "08:00")) > 0.01f
                    || Mathf.Abs(StrategyEconomics.SlotValueWanAt("CTU", "08:00")
                                 - StrategyEconomics.SlotValueWanAt("XMN", "08:00")) > 0.01f)
                {
                    priceOk = false;
                    Debug.LogError("R48 单槽价跟机场规模挂钩了：PEK/ATL（同为枢纽 08:00）或 CTU/XMN"
                                   + "（同为干线 08:00）报价不一致 —— 价格只能是「等级 × 小时」的函数。");
                }
                // 只作诊断，不作断言：各等级「买下全天公布槽位」的总额（随容量增长是正常的）。
                for (int tier = 0; tier <= 1; tier++)
                {
                    string code = null;
                    for (int i = 0; i < AirportCatalog.Airports.Count; i++)
                    {
                        var a = AirportCatalog.Airports[i];
                        if (a.tier != tier) continue;
                        if (AirportCatalog.DayCapacity(a.code) == 0) continue;
                        // 只取 24 小时协调的机场，排除 ZUH / KWE 这类限时窗口的例外
                        if (!AirportCatalog.InCoordWindow(a.code, 0)) continue;
                        if (!AirportCatalog.InCoordWindow(a.code, 23 * 60 + 45)) continue;
                        code = a.code;
                        break;
                    }
                    if (code == null) continue;
                    float total = 0f;
                    var grid24 = ScheduleSlots.SlotGrid24();
                    for (int i = 0; i < grid24.Count; i++)
                    {
                        int q = AirportCatalog.QuarterCapacity(code, grid24[i]);
                        if (q > 0) total += q * StrategyEconomics.SlotValueWanAtHour(tier, grid24[i] / 60);
                    }
                    Debug.Log($"R48 槽价诊断 → {code}（{(tier == 0 ? "枢纽" : "干线")}）"
                              + $"日容量 {AirportCatalog.DayCapacity(code)} 槽 · 买下全天 ≈ {total:F0} 万"
                              + $"（单槽峰价 {StrategyEconomics.SlotValueWanAtHour(tier, 8):F0} 万；总额随容量走，不作断言）");
                }

                bool all = identityOk && availOk && neverFull && deterministic && holderLenOk
                           && drainOk && rightsOk && textOk && chipsOk && filterOk && stateOk;

                Debug.Log($"R48 行业情报表（{airportsChecked} 个协调机场 · {bandsChecked} 个时段 · "
                          + $"{IndustryAirlines.All.Length} 家他航 · {totalRights} 条航权 · "
                          + $"卡片抽检 {chipBands} 时段/{freeChips} 可申请块/{ownerChips} 他航块/{vacantChips} 越界块）"
                          + $" → 恒等式={identityOk} 余量同口径={availOk} 不占满={neverFull} "
                          + $"确定性={deterministic} 槽长={holderLenOk} 买空={drainOk} 航权={rightsOk} "
                          + $"文本={textOk} 方块={chipsOk} 三种时刻={stateOk} 筛选={filterOk} 槽价={priceOk}");
                return all && priceOk;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);            }
        }
    }
}
#endif
