using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 钉住「他航占用率 85%–92%」这个用户裁决（2026-11-06 ⑨）。
    ///
    /// 为什么需要单独钉：这个数字**只影响「池里还剩几条免费槽」**，
    /// 不改变任何一条已有不变量（容量恒等式照样成立、池永远不会被占满、申请永远可行）。
    /// 所以 `IndustryAtlasRegression` 那套断言在旧口径 45%–80% 下**照样全绿** ——
    /// 没有本用例，谁把 `rate` 改回去都不会有人发现，而玩法会静默地从
    /// 「市场是主渠道」退回「市场只是赶时间的补充」。
    ///
    /// 断言的是**可观察后果**而不是那个魔法数：每个「有容量的波段」剩余免费槽必须落在 1–3 条。
    /// 旧口径下枢纽晚峰（容量 14）会剩 5–7 条，必然越界 —— 这就是负向控制。
    /// </summary>
    public class IndustryHeldBandTests
    {
        [Test]
        public void FreeRoomPerBandStaysScarceButNeverZero()
        {
            var go = new GameObject("__industry_held__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 5000f;
                gs.hubIata = "PEK";
                StrategyManager.EnsureState(gs);

                int checkedBands = 0, tooLoose = 0, deadEnd = 0;
                string detail = "";
                var airports = AirportCatalog.Airports;
                for (int a = 0; a < airports.Count; a++)
                {
                    var ap = airports[a];
                    if (string.IsNullOrEmpty(ap.code) || !ap.coordinated) continue;
                    for (int b = 0; b < 4; b++)
                    {
                        int cap = RoutePermitLogic.SlotPoolCapacity(ap.code, b);
                        if (cap <= 0) continue;
                        checkedBands++;
                        int open = RoutePermitLogic.SlotsAvailable(gs, ap.code, b);

                        // ★2026-11-06：到港池也要各留一条。一停站各取一进一出，
                        // 若只保离港池，「落地那一刻」仍可能被占死 —— 玩家拿着起飞权却落不下去。
                        int openArr = RoutePermitLogic.SlotsAvailable(gs, ap.code, b,
                            SlotHolding.SlotKindArrival);

                        // 硬留一条：任何波段都不能被占死，否则新玩家开第一条线就卡住。
                        if (open < 1 || openArr < 1)
                        {
                            deadEnd++;
                            if (detail.Length < 400)
                                detail += $"\n　{ap.code} 波{b}：容量 {cap} 离港余 {open} · 到港余 {openArr}（被占死）";
                            continue;
                        }
                        // 稀缺：85%–92% 的口径下，余量 =（1 − 占用率）× 容量，**随容量线性放大**。
                        // ★2026-11-07：容量按真实曲线放大 3.5 倍后，同一占用率会留下 4–6 条
                        // （容量 47 时 85% 占用 = 余 8 条），旧的「容量 ≥2 最多剩 3 条」是
                        // 容量 ~14 时的形状，继续用会把正确的 85%–92% 判成「太松」。
                        // 上界取 max(3, ⌈15% × 容量⌉)：45%–80% 旧口径会留下 ≥20%，照样抓得住。
                        int looseBound = Mathf.Max(3, Mathf.CeilToInt(cap * 0.15f));
                        if (cap >= 2 && open > looseBound)
                        {
                            tooLoose++;
                            if (detail.Length < 400)
                                detail += $"\n　{ap.code} 波{b}：容量 {cap} 余 {open}（太松，像是退回 45%–80%，上界 {looseBound}）";
                        }
                    }
                }

                Assert.Greater(checkedBands, 0, "夹具失败：没扫到任何协调机场的有容量波段。");
                Assert.AreEqual(0, deadEnd,
                    $"有波段的免费槽被占死了，新玩家连第一条线都开不出来{detail}");
                Assert.AreEqual(0, tooLoose,
                    $"他航占用率不像 85%–92%（用户 2026-11-06 裁决 ⑨）：容量 ≥2 的波段应只剩"
                    + $"max(3, 15%×容量) 条以内的免费槽。"
                    + $"若确实要回退口径，请连同玩法结论一起改，别只改常数{detail}");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
