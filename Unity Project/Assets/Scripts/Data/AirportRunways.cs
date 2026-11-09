using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 逐场**跑道数** —— ★2026-11-09 起参与玩法：它**就是**每个 15 分钟刻的离港槽容量
    /// （到港池同数；一条槽 = 一次落地 + 一次起飞）。
    ///
    /// 数据源：<c>tools/slotreport/runways.tsv</c>，后者源自
    /// <c>docs/handoff/2026-11-06_机场时刻容量对照表.md</c> §4，逐条标了
    /// CAAC（官方）/ 公开评估 / 按跑道推算 三种来源。
    ///
    /// 本文件是该 tsv 的**运行时副本**（Unity 构建里读不到 tools/ 下的 tsv）：
    /// 改跑道数必须两处同改；<c>tools/slotreport</c> 每次出表都会比对两处数值并打印告警。
    /// 生成脚本：<c>out/tmp/gen-runways.mjs</c>，口径说明见
    /// <c>docs/handoff/2026-11-09_时刻容量改为跑道数.md</c>。
    /// </summary>
    internal static class AirportRunways
    {
        /// <summary>IATA → 跑道条数（建成并投入运行的条数；PKX 为 4，容量按三跑运行时取 4 见口径说明）。</summary>
        internal static readonly Dictionary<string, int> Counts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
            { "ADD", 2 },
            { "AMS", 6 },
            { "ARN", 3 },
            { "ATL", 5 },
            { "BCN", 3 },
            { "BKK", 2 },
            { "BNE", 2 },
            { "BOG", 2 },
            { "BOM", 2 },
            { "BOS", 6 },
            { "CAI", 3 },
            { "CAN", 5 },
            { "CDG", 4 },
            { "CGK", 2 },
            { "CKG", 4 },
            { "CLT", 4 },
            { "CPH", 2 },
            { "CSX", 2 },
            { "CTU", 2 },
            { "DEL", 3 },
            { "DEN", 6 },
            { "DFW", 7 },
            { "DLC", 1 },
            { "DOH", 2 },
            { "DTW", 6 },
            { "DXB", 2 },
            { "EZE", 2 },
            { "FCO", 4 },
            { "FOC", 2 },
            { "FRA", 4 },
            { "GRU", 2 },
            { "HAK", 2 },
            { "HEL", 3 },
            { "HGH", 2 },
            { "HKG", 3 },
            { "HND", 4 },
            { "HRB", 2 },
            { "IAH", 5 },
            { "ICN", 3 },
            { "IST", 4 },
            { "JFK", 4 },
            { "JNB", 2 },
            { "KIX", 2 },
            { "KMG", 3 },
            { "KUL", 3 },
            { "KWE", 2 },
            { "LAS", 4 },
            { "LAX", 4 },
            { "LHR", 2 },
            { "LIM", 2 },
            { "MAD", 4 },
            { "MCO", 4 },
            { "MEL", 2 },
            { "MIA", 4 },
            { "MNL", 2 },
            { "MSP", 4 },
            { "MUC", 2 },
            { "NKG", 2 },
            { "NRT", 2 },
            { "ORD", 8 },
            { "PEK", 3 },
            { "PHX", 3 },
            { "PKX", 4 },
            { "PVG", 4 },
            { "SAN", 1 },
            { "SCL", 2 },
            { "SEA", 3 },
            { "SFO", 4 },
            { "SHA", 2 },
            { "SIN", 2 },
            { "SVO", 2 },
            { "SYD", 3 },
            { "SYX", 1 },
            { "SZX", 3 },
            { "TAO", 2 },
            { "TFU", 3 },
            { "TNA", 1 },
            { "TPE", 2 },
            { "TSN", 2 },
            { "URC", 3 },
            { "VIE", 2 },
            { "WUH", 3 },
            { "XIY", 3 },
            { "XMN", 1 },
            { "YVR", 3 },
            { "YYZ", 5 },
            { "ZRH", 3 },
            { "ZUH", 1 },
            };
    }
}
