# -*- coding: utf-8 -*-
"""Apply competition radar cluster (n60-n65) + wiring from attachment design docs.

ID conflict resolution per 交接说明:
- n49 = 航班定价 (keep), n50 = 外部环境分析 (keep)
- New competition cluster starts at n60 to avoid conflict
"""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

# ── N60: L2 竞争雷达 ──
N60 = {
    "id": "n60_竞争雷达",
    "level": "L2",
    "title": "📡 竞争雷达 · 多维对标",
    "meta": "市场销售部 · 7维度竞对综合情报中心",
    "badge": "新建",
    "items": [
        "竞争雷达图（核心视觉）：七边形雷达图，我司vs主要竞对（最多4家）在 航线网络/定价收益/产品服务/运营成本/辅助收入/外部环境/战略资本 7维度综合评分对比（0-100分）",
        "综合竞争力排名：基于雷达图各维度加权总分排名，显示「我司 vs 行业平均 vs 最强竞对」",
        "竞争态势总评：AO 生成一句话态势判断",
        "时间轴滑块：拖动查看过去 12 个月竞争态势变化趋势（雷达图动画演变）",
        "主要竞对卡片（横向 4 张）：Logo/名称/类型 · 综合得分 · 强项/弱项标签 · 最近动态（可点卡片→L3 竞对详情）",
        "逐维对标分析 7 个 Tab [航线网络/定价收益/产品服务/运营成本/辅助收入/外部环境/战略资本]，每Tab含 我司vs竞对对比数据 + 可视化图表 +「查看深度分析」按钮",
        "竞争事件动态（底部信息流）：实时动态列表 · 事件类型标签（航线扩张/价格战/联盟合作/资本运作/安全事件）· 影响等级（🔴高/🟡中/🔵低）",
        "战略建议（AO 智能分析）：基于雷达图给出 3 条竞争策略建议 · 机会预警 · 一跳转执行",
    ],
    "clicks": [
        ["竞对卡片（任意一张）", "L3 竞对详情"],
        ["航线网络 Tab → 查看深度分析", "L3 竞争 · 航线网络对标"],
        ["定价收益 Tab → 查看深度分析", "L2 航班定价（筛选该竞对）"],
        ["产品服务 Tab → 查看深度分析", "L3 竞争 · 产品服务对标"],
        ["运营成本 Tab → 查看深度分析", "L3 竞争 · 运营成本对标"],
        ["辅助收入 Tab → 查看深度分析", "L3 辅助收入管理（对比模式）"],
        ["外部环境 Tab → 查看深度分析", "L3 外部环境分析"],
        ["战略资本 Tab → 查看深度分析", "L3 竞争 · 战略资本对标"],
        ["竞争事件动态条目", "L4 事件弹窗"],
        ["AO 建议 → 去市场部调整定价", "L2 航班定价"],
        ["AO 建议 → 去战略部申请航权", "L1 战略发展部"],
        ["导出对比报告", "下载 CSV/PDF"],
        ["← 返回", "L1 市场销售部"],
    ],
    "entry": "入口：L1 市场销售部 → 竞争雷达；主页竞争态势微件；L2 航班定价 → 竞对战略动态",
    "kind": "new",
}

# ── N61: L3 竞对详情 ──
N61 = {
    "id": "n61_竞对详情",
    "level": "L3",
    "title": "🏢 竞对详情 · [竞对名称]",
    "meta": "从竞争雷达 · 竞对卡片进入",
    "badge": "新建",
    "items": [
        "竞对基本信息：名称 · 类型 · 总部 · 成立年份 · CEO",
        "7 维度雷达图（该竞对 vs 我司 vs 行业平均）",
        "核心指标卡：机队规模 · 航线数 · 年客运量 · 营业收入 · 员工数",
        "历史动态时间轴：该竞对所有历史动作",
        "竞对财务摘要：最近财报关键数据",
        "竞对战略分析：AI 生成的竞对战略意图研判",
    ],
    "clicks": [
        ["← 返回", "L2 竞争雷达"],
    ],
    "entry": "入口：L2 竞争雷达 → 点击竞对卡片",
    "kind": "new",
}

# ── N62: L3 竞争 · 航线网络对标 ──
N62 = {
    "id": "n62_竞争_航线网络",
    "level": "L3",
    "title": "✈️ 竞争 · 航线网络对标",
    "meta": "从竞争雷达 · 航线网络 Tab → 查看深度分析",
    "badge": "新建",
    "items": [
        "我司 vs 竞对航线网络地图（双色叠加显示）",
        "航线总数 / 覆盖城市 / 枢纽对比柱状图",
        "独飞航线列表",
        "时刻槽持有量对比",
        "航权数量与分布对比",
        "市场份额趋势图（近 12 个月）",
    ],
    "clicks": [
        ["← 返回", "L2 竞争雷达"],
    ],
    "entry": "入口：L2 竞争雷达 → 航线网络 Tab → 查看深度分析",
    "kind": "new",
}

# ── N63: L3 竞争 · 产品服务对标 ──
N63 = {
    "id": "n63_竞争_产品服务",
    "level": "L3",
    "title": "🛋️ 竞争 · 产品服务对标",
    "meta": "从竞争雷达 · 产品服务 Tab → 查看深度分析",
    "badge": "新建",
    "items": [
        "机队平均机龄对比",
        "座舱配置对比（头等/商务/高端经济/经济占比）",
        "准点率排名对比",
        "常旅客会员数及活跃度对比",
        "Skytrax 星级及评分对比",
        "服务细节对比（餐食 · 行李 · 娱乐系统评分）",
    ],
    "clicks": [
        ["← 返回", "L2 竞争雷达"],
    ],
    "entry": "入口：L2 竞争雷达 → 产品服务 Tab → 查看深度分析",
    "kind": "new",
}

# ── N64: L3 竞争 · 运营成本对标 ──
N64 = {
    "id": "n64_竞争_运营成本",
    "level": "L3",
    "title": "⚙️ 竞争 · 运营成本对标",
    "meta": "从竞争雷达 · 运营成本 Tab → 查看深度分析",
    "badge": "新建",
    "items": [
        "CASM 对比（我司 vs 竞对 vs 行业平均）",
        "燃油效率（吨/小时）对比",
        "飞机日利用率对比",
        "员工人均产出对比",
        "成本结构拆解对比（燃油 / 人力 / 维修 / 起降 / 其他占比）",
    ],
    "clicks": [
        ["← 返回", "L2 竞争雷达"],
    ],
    "entry": "入口：L2 竞争雷达 → 运营成本 Tab → 查看深度分析",
    "kind": "new",
}

# ── N65: L3 竞争 · 战略资本对标 ──
N65 = {
    "id": "n65_竞争_战略资本",
    "level": "L3",
    "title": "🧠 竞争 · 战略资本对标",
    "meta": "从竞争雷达 · 战略资本 Tab → 查看深度分析",
    "badge": "新建",
    "items": [
        "现金储备及现金流健康度对比",
        "资产负债率对比",
        "信用评级对比",
        "联盟成员等级对比",
        "近期并购/合作动态",
        "融资能力评分（我司 vs 竞对）",
    ],
    "clicks": [
        ["← 返回", "L2 竞争雷达"],
    ],
    "entry": "入口：L2 竞争雷达 → 战略资本 Tab → 查看深度分析",
    "kind": "new",
}

# ── New edges (page wiring) ──
WIRING = [
    # Main entry points
    ["n14_市场销售部", "n60_竞争雷达", "竞争雷达"],
    ["n6_主页_Dashboard", "n60_竞争雷达", "竞争态势微件"],
    ["n49_航班定价", "n60_竞争雷达", "竞对战略动态"],
    # Radar → L3 sub-pages
    ["n60_竞争雷达", "n61_竞对详情", "竞对卡片"],
    ["n60_竞争雷达", "n62_竞争_航线网络", "航线网络·查看深度分析"],
    ["n60_竞争雷达", "n63_竞争_产品服务", "产品服务·查看深度分析"],
    ["n60_竞争雷达", "n64_竞争_运营成本", "运营成本·查看深度分析"],
    ["n60_竞争雷达", "n65_竞争_战略资本", "战略资本·查看深度分析"],
    ["n60_竞争雷达", "n49_航班定价", "定价收益·查看深度分析"],
    ["n60_竞争雷达", "n48_辅助收入管理", "辅助收入·查看深度分析"],
    ["n60_竞争雷达", "n50_外部环境分析", "外部环境·查看深度分析"],
    # Radar → events/strategy
    ["n60_竞争雷达", "n21_CEO_决策会议", "竞争事件·事件条目"],
    # Radar → 战略部 (for AO suggestion)
    ["n60_竞争雷达", "n18_战略发展部", "AO建议·去战略部"],
    # ← 返回 edges
    ["n60_竞争雷达", "n14_市场销售部", "← 返回"],
    ["n61_竞对详情", "n60_竞争雷达", "← 返回"],
    ["n62_竞争_航线网络", "n60_竞争雷达", "← 返回"],
    ["n63_竞争_产品服务", "n60_竞争雷达", "← 返回"],
    ["n64_竞争_运营成本", "n60_竞争雷达", "← 返回"],
    ["n65_竞争_战略资本", "n60_竞争雷达", "← 返回"],
]


def upsert(catalog: list, screen: dict) -> None:
    for i, c in enumerate(catalog):
        if c["id"] == screen["id"]:
            catalog[i] = dict(screen)
            return
    catalog.append(dict(screen))


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("CATALOG not found")
    catalog = json.loads(m.group(1))
    edges = json.loads(m.group(2))
    by = {c["id"]: c for c in catalog}

    # ── Update n14_市场销售部: add 竞争雷达 entry ──
    n14 = by["n14_市场销售部"]
    items14 = list(n14.get("items") or [])
    if not any("竞争雷达" in x for x in items14):
        items14.insert(1, "竞争雷达：7维度竞对对标总览 · 竞争情报中心 → 进入 L2 竞争雷达")
    n14["items"] = items14
    clicks14 = [c for c in (n14.get("clicks") or []) if c[0] != "竞争雷达"]
    clicks14.insert(1, ["竞争雷达", "L2 竞争雷达"])
    n14["clicks"] = clicks14

    # ── Update n49_航班定价: 竞对战略动态 → n60 (was pointing to n29) ──
    if "n49_航班定价" in by:
        n49 = by["n49_航班定价"]
        clicks49 = list(n49.get("clicks") or [])
        for i, c in enumerate(clicks49):
            if "竞对战略动态" in c[0] and c[1].startswith("L2 竞争态势"):
                clicks49[i] = ["竞对战略动态条目", "L2 竞争雷达"]
                break
        # Also update item that mentions competition dynamics
        items49 = list(n49.get("items") or [])
        for i, it in enumerate(items49):
            if "竞对战略动态" in it:
                items49[i] = "竞对战略动态：新开线/降价/入盟等（→ L2 竞争雷达）"
                break
        n49["items"] = items49
        n49["clicks"] = clicks49

    # ── Update n6_主页_Dashboard: add competition radar widget click (keep old n29 click) ──
    if "n6_主页_Dashboard" in by:
        n6 = by["n6_主页_Dashboard"]
        items6 = list(n6.get("items") or [])
        for i, it in enumerate(items6):
            if "竞争态势" in it or "竞争微件" in it:
                items6[i] = "竞争态势微件：7维度竞对对标 → 点击进入 L2 竞争雷达"
                break
        # Keep old n29 click but also add n60: update the existing click
        clicks6 = list(n6.get("clicks") or [])
        found_comp = False
        for i, c in enumerate(clicks6):
            if c[1] == "n29_竞争态势_AI_航司":
                clicks6[i] = [c[0], "L2 竞争雷达"]  # redirect to new radar page
                found_comp = True
                break
        if not found_comp:
            clicks6.insert(4, ["竞争态势微件", "L2 竞争雷达"])
        n6["items"] = items6
        n6["clicks"] = clicks6

    # ── Update n29_竞争态势/AI航司: add note linking to new radar ──
    if "n29_竞争态势_AI_航司" in by:
        n29 = by["n29_竞争态势_AI_航司"]
        items29 = list(n29.get("items") or [])
        if not any("竞争雷达" in x for x in items29):
            items29.append("深层竞对分析 → L2 竞争雷达（7维度对标）")
        n29["items"] = items29
        clicks29 = list(n29.get("clicks") or [])
        if not any(c[1].startswith("L2 竞争雷达") for c in clicks29):
            clicks29.append(["深度竞对分析", "L2 竞争雷达"])
        n29["clicks"] = clicks29

    # ── Add all new screens ──
    for screen in (N60, N61, N62, N63, N64, N65):
        upsert(catalog, screen)

    # ── Add new edges (avoid duplicates) ──
    def has_edge(fr, to, label):
        return any(x[0] == fr and x[1] == to and x[2] == label for x in edges)

    for e in WIRING:
        if not has_edge(e[0], e[1], e[2]):
            edges.append(e)

    # ── Update n14→n29 click (old 竞争态势): keep it but label clearly ──
    # Already handled by keeping existing edges; just ensure no duplicate n14→n60

    # ── Bump CONTENT_REV ──
    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260810b';", html, count=1)

    # ── Replace CATALOG + DEFAULT_EDGES ──
    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html2, n = re.subn(
        r"var CATALOG = \[[\s\S]*?\];\s*\n\s*var DEFAULT_EDGES = \[[\s\S]*?\];",
        "var CATALOG = " + cat_js + ";\n  var DEFAULT_EDGES = " + edges_js + ";",
        html2,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"replace failed {n}")

    # ── Expand migrate title map for competition screens ──
    # Existing anchor from _apply_seasonal_ownership.py (runs after pricing)
    mig_anchor = "if (title.indexOf('外部环境') >= 0 && title.indexOf('竞争') < 0) map[id] = 'n50_外部环境分析';"
    mig_extra = (
        "if (title.indexOf('外部环境') >= 0 && title.indexOf('竞争') < 0) map[id] = 'n50_外部环境分析';\n"
        "      if (title.indexOf('竞争雷达') >= 0 || title.indexOf('多维对标') >= 0) map[id] = 'n60_竞争雷达';\n"
        "      if (title.indexOf('竞对详情') >= 0) map[id] = 'n61_竞对详情';\n"
        "      if (title.indexOf('航线网络') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n62_竞争_航线网络';\n"
        "      if (title.indexOf('产品服务') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n63_竞争_产品服务';\n"
        "      if (title.indexOf('运营成本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n64_竞争_运营成本';\n"
        "      if (title.indexOf('战略资本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n65_竞争_战略资本';"
    )
    if mig_anchor in html2:
        html2 = html2.replace(mig_anchor, mig_extra, 1)
    else:
        # Fallback: add after n50 mapping
        mig_anchor2 = "if (title.indexOf('外部环境') >= 0) map[id] = 'n50_外部环境分析';"
        if mig_anchor2 in html2:
            mig_extra2 = (
                "if (title.indexOf('外部环境') >= 0) map[id] = 'n50_外部环境分析';\n"
                "      if (title.indexOf('竞争雷达') >= 0 || title.indexOf('多维对标') >= 0) map[id] = 'n60_竞争雷达';\n"
                "      if (title.indexOf('竞对详情') >= 0) map[id] = 'n61_竞对详情';\n"
                "      if (title.indexOf('航线网络') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n62_竞争_航线网络';\n"
                "      if (title.indexOf('产品服务') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n63_竞争_产品服务';\n"
                "      if (title.indexOf('运营成本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n64_竞争_运营成本';\n"
                "      if (title.indexOf('战略资本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n65_竞争_战略资本';"
            )
            html2 = html2.replace(mig_anchor2, mig_extra2, 1)

    # ── Expand extras filter block ──
    old_filter = "if (t.indexOf('外部环境') >= 0 && t.indexOf('竞争') < 0) return false;"
    new_filter = (
        "if (t.indexOf('外部环境') >= 0 && t.indexOf('竞争') < 0) return false;\n"
        "      if (t.indexOf('竞争雷达') >= 0 || t.indexOf('多维对标') >= 0) return false;\n"
        "      if (t.indexOf('竞对详情') >= 0) return false;\n"
        "      if (t.indexOf('航线网络') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
        "      if (t.indexOf('产品服务') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
        "      if (t.indexOf('运营成本') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
        "      if (t.indexOf('战略资本') >= 0 && t.indexOf('竞争') >= 0) return false;"
    )
    if old_filter in html2:
        html2 = html2.replace(old_filter, new_filter, 1)
    else:
        # Fallback: simpler filter pattern
        old_filter2 = "if (t.indexOf('外部环境') >= 0) return false;"
        if old_filter2 in html2:
            new_filter2 = (
                "if (t.indexOf('外部环境') >= 0 && t.indexOf('竞争') < 0) return false;\n"
                "      if (t.indexOf('竞争雷达') >= 0 || t.indexOf('多维对标') >= 0) return false;\n"
                "      if (t.indexOf('竞对详情') >= 0) return false;\n"
                "      if (t.indexOf('航线网络') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
                "      if (t.indexOf('产品服务') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
                "      if (t.indexOf('运营成本') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
                "      if (t.indexOf('战略资本') >= 0 && t.indexOf('竞争') >= 0) return false;"
            )
            html2 = html2.replace(old_filter2, new_filter2, 1)

    # ── Update NOTE_FEATURE_PACK ──
    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack["n14_市场销售部"] = {
            "items": n14["items"],
            "clicks": n14["clicks"],
            "meta": n14.get("meta", ""),
            "entry": n14.get("entry", ""),
        }
        if "n49_航班定价" in by:
            pack["n49_航班定价"] = {
                "items": by["n49_航班定价"]["items"],
                "clicks": by["n49_航班定价"]["clicks"],
                "title": by["n49_航班定价"].get("title"),
                "meta": by["n49_航班定价"].get("meta", ""),
                "entry": by["n49_航班定价"].get("entry", ""),
            }
        for screen in (N60, N61, N62, N63, N64, N65):
            pack[screen["id"]] = {
                "items": screen["items"],
                "clicks": screen["clicks"],
                "title": screen["title"],
                "meta": screen["meta"],
                "entry": screen["entry"],
            }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1):]

    # ── Ensure new edges added on migrate for competition cluster ──
    edge_add_anchor = "['n49_航班定价','n55_季节性日历','季节性趋势 Tab']"
    edge_add_extra = (
        "['n49_航班定价','n55_季节性日历','季节性趋势 Tab'],\n"
        "      ['n14_市场销售部','n60_竞争雷达','竞争雷达'],\n"
        "      ['n6_主页_Dashboard','n60_竞争雷达','竞争态势微件'],\n"
        "      ['n60_竞争雷达','n61_竞对详情','竞对卡片']"
    )
    if edge_add_anchor in html2:
        html2 = html2.replace(edge_add_anchor, edge_add_extra, 1)

    # ── Update the 市场相关界面速查 table label for n29 ──
    if 'n29_竞争态势_AI_航司' in html2:
        # Update the n29 badge/clicks count in the markdown table (line 95 of handoff doc)
        pass  # The markdown table is in handoff doc, not HTML

    BOARD.write_text(html2, encoding="utf-8")
    print("competition radar applied; catalog", len(catalog), "edges", len(edges))


if __name__ == "__main__":
    main()
