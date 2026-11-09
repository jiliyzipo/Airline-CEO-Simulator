#!/usr/bin/env node
/**
 * 把一份**已导出的快照**改造成「批次一抽取前」的重建对照（pre）。
 *
 * 用途（2026-11-09 Codex 裁决）：申请序列要做前后对照，但仓库里没有一份
 * 同时满足「跑道数平铺」与「未抽取 CapacityService」的可追溯快照 ——
 * 11-09 上午先改容量口径，随后才做本批抽取，两步都在同一段未提交工作区里。
 * 11-07 的交付快照仍是旧「小时曲线 + QuarterSplit」口径，拿它当基线会让
 * 19,968 格必然不同（口径差异，不是抽取差异），属错误对照。
 *
 * 因此这里用**脚本**而不是手工改：在一份快照副本上把 Data 层还原成抽取前的形状，
 * 每一步都要求「原文恰好出现一次」，否则直接失败（防止源码漂移后静默改错东西）。
 *
 * 还原的六处（全部在 Assets/Scripts/Data/AirportCatalog.cs）：
 *   1. 静态构造不再建 `_indexByCode`；
 *   2. `slotCapacity` 由本地 `PoolsRaw` 求和；
 *   3. `QuarterCapacity` 由本地 `QuarterCapacityRaw` 计算（含宵禁/协调时段判定）；
 *   4. `HourCapacity` 逐刻累加；
 *   5. `DayCapacity` 逐小时累加；
 *   6. `FindByCode` 回到 `StringComparison.OrdinalIgnoreCase` 线性扫描。
 *
 * 结构与签名沿用 2026-11-07 交付快照里的真实形状（`QuarterCapacityRaw(code, tier,
 * coordWindow, minutes)`、线性 `FindByCode`、`DayCapacity` 逐小时累加），只把算法换成
 * 11-09 已生效的平铺口径 —— 也就是说：**被对照的是「抽取」这一件事**，不是容量口径。
 *
 * 用法：
 *   node tools/regression/reconstruct-pre-batch1.mjs "D:\Project\_snapshot_pre"
 *   其中参数是**已经 export-snapshot.mjs 导出过**的快照根目录（含 Unity Project/）。
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const target = process.argv[2];
if (!target) {
  console.error('usage: node tools/regression/reconstruct-pre-batch1.mjs <snapshotRoot>');
  process.exit(2);
}

const rel = 'Unity Project/Assets/Scripts/Data/AirportCatalog.cs';
const file = path.join(target, rel);
if (!fs.existsSync(file)) {
  console.error(`not found: ${file}`);
  process.exit(2);
}

const before = fs.readFileSync(file, 'utf8');
let text = before;

/**
 * 断言原文恰好出现一次再替换。返回替换次数，便于最后汇总。
 */
function replaceOnce(label, from, to) {
  const count = text.split(from).length - 1;
  if (count !== 1) {
    console.error(`[pre-rebuild] 失败：片段「${label}」出现 ${count} 次（期望 1 次）。`);
    console.error('源码可能已漂移，请先更新本脚本的片段，不要手工改快照。');
    process.exit(1);
  }
  text = text.replace(from, to);
  return 1;
}

let changes = 0;

changes += replaceOnce(
  '1 建索引',
  `            // ★2026-11-09 批次一（M1）：先建代码索引再逐个推导。
            // 顺序有讲究 —— 索引只依赖 \`code\`，而 \`code\` 在字段初始化器里就已全部写入；
            // 等级 / 协调时段则要等循环逐行推导，所以索引可以早建、属性必须晚用。
            for (int i = 0; i < All.Length; i++) _indexByCode[All[i].code] = i;
`,
  `            // [PRE-BATCH1 重建] 抽取前没有代码索引：FindByCode 走线性扫描，故此处不建表。
`,
);

changes += replaceOnce(
  '2 池容量来源',
  `                // 池容量是**派生缓存**，唯一算法在 CapacityService（C0：跑道数平铺）。
                a.slotCapacity = CapacityService.Pools(a.code, a.tier, a.coordWindow);`,
  `                // [PRE-BATCH1 重建] 池容量在本地内联求和，不经 CapacityService。
                a.slotCapacity = PoolsRaw(a.code, a.tier, a.coordWindow);`,
);

changes += replaceOnce(
  '3 逐刻容量',
  `        public static int QuarterCapacity(string code, int minutes)
            => CapacityService.Quarter(code, minutes);`,
  `        public static int QuarterCapacity(string code, int minutes)
            => QuarterCapacityRaw(code, TierOfCode(code), CoordWindowOf(code), minutes);

        // ———— [PRE-BATCH1 重建] 抽取前内联在 AirportCatalog 里的容量算法 ————
        // 与 CapacityService 是同一套 C0 口径（跑道数平铺 / 宵禁 0 / 协调时段外 0），
        // 签名与结构沿用 2026-11-07 交付快照里的真实形状，差别只在「算法放在哪里」。
        static int QuarterCapacityRaw(string code, int tier, string coordWindow, int minutes)
        {
            if (string.IsNullOrEmpty(coordWindow)) return 0;
            int m = ((minutes % 1440) + 1440) % 1440;
            if (!InCoordWindowText(coordWindow, m)) return 0;
            if (InCurfewWindow(code, m)) return 0;
            return RunwayCountOf(code, tier);
        }

        static int[] PoolsRaw(string code, int tier, string coordWindow)
        {
            if (string.IsNullOrEmpty(coordWindow)) return new[] { 0, 0, 0, 0 };
            var r = new int[4];
            for (int band = 0; band < 4; band++)
            {
                int total = 0;
                var grid = ScheduleSlots.BandGrid(band);
                for (int i = 0; i < grid.Count; i++)
                    total += QuarterCapacityRaw(code, tier, coordWindow, grid[i]);
                r[band] = total;
            }
            return r;
        }`,
);

changes += replaceOnce(
  '4 日容量',
  `        public static int DayCapacity(string code)
            => CapacityService.Day(code);`,
  `        public static int DayCapacity(string code)
        {
            // [PRE-BATCH1 重建] 抽取前逐小时累加。
            int n = 0;
            for (int h = 0; h < 24; h++) n += HourCapacity(code, h);
            return n;
        }`,
);

changes += replaceOnce(
  '5 小时容量',
  `        public static int HourCapacity(string code, int hour)
            => CapacityService.Hour(code, hour);`,
  `        public static int HourCapacity(string code, int hour)
        {
            // [PRE-BATCH1 重建] 抽取前把该小时四个 15 分钟刻相加。
            if (hour < 0 || hour > 23) return 0;
            int step = ScheduleSlots.SlotGridStepMinutes;
            int n = 0;
            for (int q = 0; q < 60; q += step) n += QuarterCapacity(code, hour * 60 + q);
            return n;
        }`,
);

changes += replaceOnce(
  '6 代码查找',
  `            return _indexByCode.TryGetValue(code, out int i) ? All[i] : default;`,
  `            // [PRE-BATCH1 重建] 抽取前是线性扫描（StringComparison.OrdinalIgnoreCase 语义）。
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].code, code, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return default;`,
);

fs.writeFileSync(file, text, 'utf8');

const hash = (s) => crypto.createHash('sha256').update(s).digest('hex');
const note = `# PRE-BATCH1 重建说明

生成时间：${new Date().toISOString()}
生成脚本：\`tools/regression/reconstruct-pre-batch1.mjs\`
目标文件：\`${rel}\`

## 这是什么

一份**快照副本**里的 Data 层，被还原成「批次一抽取（CapacityService / 机场索引）之前」的形状，
用于跑「同一修订版测试 × 同一初始状态 × 同一输入序列」的前后对照。**不是** git 里的历史字节。

## 为什么不用仓库里的历史快照

仓库里最近的交付快照（\`out/collab/DEL-20261107143823-54ca1d\`，55,374 B）仍是
旧「小时曲线 + QuarterSplit」容量口径；\`Unity Project/Builds/Baseline-20261017-v2\` 更早。
不存在同时满足「跑道数平铺」与「未抽取」的可追溯快照 —— 11-09 上午先改容量口径、
随后才做本批抽取，两步都落在同一段未提交工作区里。

拿 11-07 曲线版当基线，19,968 格会**必然全不同**，那是口径差异、不是抽取差异，属错误对照。

## 改了哪六处（全部在同一个文件）

1. 静态构造不再建 \`_indexByCode\`；
2. \`slotCapacity\` 由本地 \`PoolsRaw\` 求和；
3. \`QuarterCapacity\` 由本地 \`QuarterCapacityRaw\` 计算（含协调时段 / 宵禁判定）；
4. \`HourCapacity\` 逐刻累加；
5. \`DayCapacity\` 逐小时累加；
6. \`FindByCode\` 回到 \`StringComparison.OrdinalIgnoreCase\` 线性扫描。

签名与结构沿用 11-07 快照里的真实形状（\`QuarterCapacityRaw(code, tier, coordWindow, minutes)\`、
线性 \`FindByCode\`、\`DayCapacity\` 逐小时累加），只把算法换成 11-09 已生效的平铺口径 ——
**被对照的只有「抽取」这一件事**，不含容量口径变化。

\`CapacityService.cs\` 等文件**保留在副本里但不被 Data 层调用**，只为让 Editor 程序集编译通过。

## 哈希

- 改动前 \`AirportCatalog.cs\`：\`${hash(before)}\`
- 改动后 \`AirportCatalog.cs\`：\`${hash(text)}\`
- 字节数：${Buffer.byteLength(before)} → ${Buffer.byteLength(text)}
- 替换处数：${changes}/6

## 与历史交付快照的形状核对

从 \`out/collab/DEL-20261107143823-54ca1d/…/AirportCatalog.cs\` 读到的抽取前形状与本重建逐项一致：

\`\`\`csharp
public static AirportInfo FindByCode(string code)
{
    for (int i = 0; i < All.Length; i++)
        if (string.Equals(All[i].code, code, StringComparison.OrdinalIgnoreCase))
            return All[i];
    return default;
}

public static int QuarterCapacity(string code, int minutes)
    => QuarterCapacityRaw(code, TierOfCode(code), CoordWindowOf(code), minutes);

public static int DayCapacity(string code)
{
    int n = 0;
    for (int h = 0; h < 24; h++) n += HourCapacity(code, h);
    return n;
}
\`\`\`
`;

fs.writeFileSync(path.join(target, 'PRE-REBUILD.md'), note, 'utf8');
console.log(`pre-rebuild ok: ${changes} 处替换 → ${file}`);
console.log(`  before ${hash(before).slice(0, 16)}… → after ${hash(text).slice(0, 16)}…`);
