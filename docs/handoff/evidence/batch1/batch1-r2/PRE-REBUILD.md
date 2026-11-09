# PRE-BATCH1 重建说明

生成时间：2026-11-09T12:10:32.000Z
生成脚本：`tools/regression/reconstruct-pre-batch1.mjs`
目标文件：`Unity Project/Assets/Scripts/Data/AirportCatalog.cs`

## 这是什么

一份**快照副本**里的 Data 层，被还原成「批次一抽取（CapacityService / 机场索引）之前」的形状，
用于跑「同一修订版测试 × 同一初始状态 × 同一输入序列」的前后对照。**不是** git 里的历史字节。

## 为什么不用仓库里的历史快照

仓库里最近的交付快照（`out/collab/DEL-20261107143823-54ca1d`，55,374 B）仍是
旧「小时曲线 + QuarterSplit」容量口径；`Unity Project/Builds/Baseline-20261017-v2` 更早。
不存在同时满足「跑道数平铺」与「未抽取」的可追溯快照 —— 11-09 上午先改容量口径、
随后才做本批抽取，两步都落在同一段未提交工作区里。

拿 11-07 曲线版当基线，19,968 格会**必然全不同**，那是口径差异、不是抽取差异，属错误对照。

## 改了哪六处（全部在同一个文件）

1. 静态构造不再建 `_indexByCode`；
2. `slotCapacity` 由本地 `PoolsRaw` 求和；
3. `QuarterCapacity` 由本地 `QuarterCapacityRaw` 计算（含协调时段 / 宵禁判定）；
4. `HourCapacity` 逐刻累加；
5. `DayCapacity` 逐小时累加；
6. `FindByCode` 回到 `StringComparison.OrdinalIgnoreCase` 线性扫描。

签名与结构沿用 11-07 快照里的真实形状（`QuarterCapacityRaw(code, tier, coordWindow, minutes)`、
线性 `FindByCode`、`DayCapacity` 逐小时累加），只把算法换成 11-09 已生效的平铺口径 ——
**被对照的只有「抽取」这一件事**，不含容量口径变化。

`CapacityService.cs` 等文件**保留在副本里但不被 Data 层调用**，只为让 Editor 程序集编译通过。

## 哈希

- 改动前 `AirportCatalog.cs`：`4b2df5c72672ff9799d32b75d7408dfe178707e2800ed0ea2359c23587f10399`
- 改动后 `AirportCatalog.cs`：`c4962ad55b5732d6b72cb4150ae5d609f85e0cd52e3fa46fbac1383cdaaaa596`
- 字节数：49056 → 50750
- 替换处数：6/6

## 与历史交付快照的形状核对

从 `out/collab/DEL-20261107143823-54ca1d/…/AirportCatalog.cs` 读到的抽取前形状与本重建逐项一致：

```csharp
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
```
