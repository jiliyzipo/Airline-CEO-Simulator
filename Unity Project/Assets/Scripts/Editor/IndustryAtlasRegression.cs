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

                    var grid = ScheduleSlots.SlotGridLabels(band);
                    string clock = grid[0];
                    int guard = 0;
                    while (RoutePermitLogic.SlotsAvailable(gs, ap.code, band) > 0 && guard++ < 64)
                        if (!StrategyManager.AcquireSlotsAt(gs, ap.code, clock, 1, true, out _)) break;

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
                        var grid = ScheduleSlots.SlotGridLabels(b);

                        if (chips.Count != grid.Count)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"方块 {chips.Count} 个 ≠ 网格时刻 {grid.Count} 个。");
                            continue;
                        }

                        int series = 0, free = 0, vacant = 0, usedChips = 0;
                        for (int k = 0; k < chips.Count; k++)
                        {
                            if (chips[k].time != grid[k]) chipsOk = false;
                            series += chips[k].series;
                            if (chips[k].free) free++;
                            else if (chips[k].vacant) vacant++;
                            else if (string.IsNullOrEmpty(chips[k].owner)) chipsOk = false;
                            else { usedChips++; if (!chips[k].mine) ownerChips++; }
                        }
                        freeChips += free;
                        vacantChips += vacant;

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
                        if (vacant > 0 && open > 0)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"本段还有 {open} 条可申请，却有 {vacant} 个「无槽」灰块把余量藏起来了。");
                        }
                        if (open > 0 && occupied < grid.Count && free == 0)
                        {
                            chipsOk = false;
                            Debug.LogError($"R48 {ap.code} {ScheduleSlots.SlotBandName(b)}："
                                           + $"余 {open} 条、占用 {occupied} < 时刻 {grid.Count}，"
                                           + "却没有一块空的 —— 槽被挤在时段开头了。");
                        }
                    }
                }
                if (ownerChips == 0) chipsOk = false;   // 一块他航的都没有 → 推导失效

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

                bool all = identityOk && availOk && neverFull && deterministic && holderLenOk
                           && drainOk && rightsOk && textOk && chipsOk && filterOk;

                Debug.Log($"R48 行业情报表（{airportsChecked} 个协调机场 · {bandsChecked} 个时段 · "
                          + $"{IndustryAirlines.All.Length} 家他航 · {totalRights} 条航权 · "
                          + $"卡片抽检 {chipBands} 时段/{freeChips} 可申请块/{vacantChips} 无槽块/{ownerChips} 他航块）"
                          + $" → 恒等式={identityOk} 余量同口径={availOk} 不占满={neverFull} "
                          + $"确定性={deterministic} 槽长={holderLenOk} 买空={drainOk} 航权={rightsOk} "
                          + $"文本={textOk} 方块={chipsOk} 筛选={filterOk}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);            }
        }
    }
}
#endif
