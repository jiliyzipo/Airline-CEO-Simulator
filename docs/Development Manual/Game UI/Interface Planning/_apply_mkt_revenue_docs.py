# -*- coding: utf-8 -*-
"""Apply Word annotation specs for L1 Marketing + L2 Route Revenue Analysis."""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

N14_ITEMS = [
    "目标：客座率 · 单位收益 · 品牌净值 · 市场份额",
    "核心KPI卡片×4：航线总收入 · 航线总成本 · 航线净利润 · 综合利润率（带同比趋势；与 L2 航线收益分析数字一致）",
    "航线收益排行（完整列表）：排名 · 航线 · 执飞机型 · 周班次 · 总收入 · 总成本 · 净利润 · 客座率 · 利润率 · 趋势（↗/→/↘）",
    "列表交互：点击表头排序（默认按净利润降序）；点击行 → L2 航线收益分析（选中该航线）",
    "联动入口：底部「查看完整收益分析 →」跳转 L2 航线收益分析",
    "定价策略：票价 / 折扣 / 预售",
    "竞争雷达 · 价格战事件选项",
    "季节性日历：节日需求曲线 + 旺季策略包",
    "常旅客计划 · 产品工坊 · 大客户合同 · 渠道预算",
    "品牌危机公关决策入口",
]
N14_CLICKS = [
    ["核心KPI卡片（任意一张）", "L2 航线收益分析（聚焦对应维度）"],
    ["航线收益排行中的行", "L2 航线收益分析（自动选中该航线）"],
    ["查看完整收益分析 →", "L2 航线收益分析"],
    ["常旅客计划", "L3 FFP 管理"],
    ["产品工坊", "L3 产品工坊"],
    ["季节性日历", "L3 旺季策略"],
    ["价格战 / 合同", "L4 事件弹窗"],
]

N44 = {
    "id": "n44_航线收益分析",
    "level": "L2",
    "title": "💹 航线收益分析",
    "meta": "从市场销售部进入 · 与 L1 KPI 数字联动",
    "badge": "新建",
    "items": [
        "顶部 Tab：[收益总览] [航线排行] [竞对对比] [季节性趋势]",
        "核心KPI卡片×4：总收入 · 总成本 · 净利润 · 综合利润率（与 L1 市场销售部联动，数字一致）",
        "航线收益排行（完整列表）：排名 · 航线 · 执飞机型 · 周班次 · 总收入 · 总成本 · 净利润 · 客座率 · 利润率 · 趋势；支持表头排序；点击行展开详情",
        "详情面板（点击行展开）：收入结构（环形图+占比）· 成本结构（堆叠条形图+占比）· 效率指标（客座率/RASK/CASK/盈亏平衡/日利润，带行业基准对比）",
        "AO 智能分析：右下角浮窗，按当前数据生成一句话洞察（如「燃油成本偏高，建议锁仓」）",
        "底部操作栏：导出报表 · 发送给 AO · 停飞评估 · 复制航线模型",
    ],
    "clicks": [
        ["收益排行行", "详情面板展开（同页）"],
        ["竞对对比 Tab", "L2 竞争态势/AI航司"],
        ["季节性趋势 Tab", "L3 旺季策略"],
        ["成本明细中「燃油」", "L3 对冲工作台"],
        ["成本明细中「维修」", "L1 机务工程部"],
        ["停飞评估", "L4 CEO决策会议"],
        ["复制航线模型", "L3 RPM评估台"],
        ["← 返回 / 关闭", "L1 市场销售部"],
    ],
    "entry": "入口：L1 市场销售部（点击航线排行行 / 「查看完整收益分析」）",
    "kind": "new",
}


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("CATALOG/EDGES not found")
    catalog = json.loads(m.group(1))
    edges = json.loads(m.group(2))
    by = {c["id"]: c for c in catalog}

    n14 = by["n14_市场销售部"]
    n14["items"] = N14_ITEMS
    n14["clicks"] = N14_CLICKS
    n14["meta"] = "MKT · 含航线收益总览入口"
    n14["entry"] = "下钻 → L2 航线收益分析"

    if "n44_航线收益分析" in by:
        by["n44_航线收益分析"].update(N44)
    else:
        catalog.append(dict(N44))

    def has(fr, to):
        return any(e[0] == fr and e[1] == to for e in edges)

    for e in (
        ["n14_市场销售部", "n44_航线收益分析", "收益排行/完整分析"],
        ["n44_航线收益分析", "n29_竞争态势_AI_航司", "竞对对比"],
        ["n44_航线收益分析", "n31_对冲工作台", "燃油成本"],
        ["n44_航线收益分析", "n17_机务工程部", "维修成本"],
        ["n44_航线收益分析", "n21_CEO_决策会议", "停飞评估"],
        ["n44_航线收益分析", "n35_新航线_RPM_评估台", "复制航线模型"],
        ["n44_航线收益分析", "n14_市场销售部", "返回"],
    ):
        if not has(e[0], e[1]):
            edges.append(e)

    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html2, n = re.subn(
        r"var CATALOG = \[[\s\S]*?\];\s*\n\s*var DEFAULT_EDGES = \[[\s\S]*?\];",
        "var CATALOG = " + cat_js + ";\n  var DEFAULT_EDGES = " + edges_js + ";",
        html,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"replace failed {n}")

    # bump content rev
    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260809e';", html2, count=1)

    # migrate custom 航线收益分析 -> n44
    old_mig = "var map = { 'custom_q282zgz':'n41_单航线', 'custom_uqawjw8':'n43_航班详细计划' };"
    new_mig = (
        "var map = { 'custom_q282zgz':'n41_单航线', 'custom_uqawjw8':'n43_航班详细计划' };\n"
        "    // 用户新建的「航线收益分析」并入内置 n44\n"
        "    Object.keys(state.nodes || {}).forEach(function (id) {\n"
        "      if (id === 'n44_航线收益分析') return;\n"
        "      var item = null;\n"
        "      (state.extras || []).forEach(function (x) { if (x.id === id) item = x; });\n"
        "      if (!item) return;\n"
        "      var title = String(item.title || '');\n"
        "      if (title.indexOf('航线收益分析') >= 0) map[id] = 'n44_航线收益分析';\n"
        "    });"
    )
    if old_mig in html2:
        html2 = html2.replace(old_mig, new_mig, 1)

    # Update NOTE_FEATURE_PACK entries if present
    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack["n14_市场销售部"] = {"items": N14_ITEMS, "clicks": N14_CLICKS, "meta": n14["meta"], "entry": n14["entry"]}
        pack["n44_航线收益分析"] = {
            "items": N44["items"],
            "clicks": N44["clicks"],
            "title": N44["title"],
            "meta": N44["meta"],
            "entry": N44["entry"],
        }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1) :]

    BOARD.write_text(html2, encoding="utf-8")
    print("updated board", BOARD.stat().st_size)
    print("n14 items", len(N14_ITEMS), "n44 added/updated")


if __name__ == "__main__":
    main()
