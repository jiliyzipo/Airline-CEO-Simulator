# -*- coding: utf-8 -*-
"""Add L2 flight pricing + 4 L3 screens and wiring from Word specs."""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

N49 = {
    "id": "n49_航班定价",
    "level": "L2",
    "title": "💵 航班定价",
    "meta": "市场销售部 · 票价 / 动态定价 / 辅助收入",
    "badge": "新建",
    "items": [
        "核心KPI×4：平均票价（近7天加权，绿↑/红↓对比上周）· 客座率（绿>80%/黄70–80%/红<70%）· 收益率Yield · RASM vs CASM（绿 RASM>CASM）",
        "多等级舱位看板：头等/商务/高端经济/经济 · 当前票价 · 座位余量 · 折扣率",
        "基础经济舱开关：不含行李/不可退改，吸引价格敏感旅客",
        "票价矩阵表：横轴航线 × 纵轴舱位；单元格可点调价",
        "批量调价：按区域 / 航线组 / 舱位，百分比或固定金额",
        "实时竞对票价监测：核心航线竞对票价、趋势、我司排名（高/中/低）",
        "AI 动态调价建议：↑/↓/- 及预期客座率/利润影响",
        "调价日志：时间 · 航线 · 原价/新价 · 执行人（AI/CEO），可回退",
        "收益管理仪表盘入口：客座率/收益率/RASK/CASK 趋势（近30天）",
        "辅助收入面板：行李/选座/机上销售/退改签收入与占比",
        "忠诚度计划联动：会员数 · 活跃度 · 联名卡/里程利润贡献",
        "打包产品推荐：机票+行李/贵宾厅等，预估转化与额外收入",
        "高铁竞争热力图：≤1000km 航线替代威胁（高/中/低）",
        "竞对战略动态：新开线/降价/入盟等（来自竞争态势）",
    ],
    "clicks": [
        ["平均票价 / 客座率 / 收益率 KPI", "L3 收益管理仪表盘"],
        ["某航线舱位行", "L3 单航线定价详情"],
        ["AI 动态调价建议卡片", "L3 动态定价算法面板"],
        ["辅助收入面板", "L3 辅助收入管理"],
        ["忠诚度计划联动", "L3 FFP 管理"],
        ["高铁竞争热力图", "L3 外部环境分析"],
        ["竞对战略动态条目", "L2 竞争态势/AI航司"],
        ["价格战事件", "L4 事件弹窗"],
        ["← 返回", "L1 市场销售部"],
    ],
    "entry": "入口：L1 市场销售部 → 定价策略",
    "kind": "new",
}

N45 = {
    "id": "n45_单航线定价详情",
    "level": "L3",
    "title": "🎯 单航线定价详情",
    "meta": "从航班定价 · 航线舱位行进入",
    "badge": "新建",
    "items": [
        "航线基本信息：OD · 机型 · 班次 · 航程",
        "全舱位票价矩阵：头等/商务/高端经济/经济，支持直接修改",
        "竞对在该航线的票价对比（柱状图）",
        "历史票价走势（近30天 vs 竞对均价）",
        "调价模拟器：调价 → 实时预估客座率、利润变化",
        "一键应用 AI 建议 或 手动确认调价",
    ],
    "clicks": [
        ["确认调价", "回 L2 航班定价并刷新数据"],
        ["← 返回", "L2 航班定价"],
    ],
    "entry": "入口：L2 航班定价 → 航线舱位行",
    "kind": "new",
}

N46 = {
    "id": "n46_动态定价算法",
    "level": "L3",
    "title": "🤖 动态定价算法面板",
    "meta": "从航班定价 · AI 调价建议进入",
    "badge": "新建",
    "items": [
        "算法开关：启用 / 暂停 AI 自动调价",
        "调价参数：竞对票价权重（0–100%）· 航班负载权重（0–100%）",
        "节假日敏感度：低 / 中 / 高",
        "最低票价保护：不得低于成本的 X%",
        "调价频率限制：每日最多调价次数",
        "算法历史决策记录：输入参数与结果",
        "手动覆盖：一键暂停某航线自动调价",
    ],
    "clicks": [
        ["保存设置", "回 L2 航班定价"],
        ["手动覆盖", "L3 单航线定价详情"],
        ["← 返回", "L2 航班定价"],
    ],
    "entry": "入口：L2 航班定价 → AI 调价建议卡片",
    "kind": "new",
}

N47 = {
    "id": "n47_收益管理仪表盘",
    "level": "L3",
    "title": "📈 收益管理仪表盘",
    "meta": "从航班定价 · 核心 KPI 进入",
    "badge": "新建",
    "items": [
        "客座率趋势图（近30天/90天，按航线/区域筛选）",
        "收益率趋势图（近30天/90天）",
        "RASK vs CASM 对比图（差距；绿色为盈利区间）",
        "航线收益排行（与 L1 市场部联动）",
        "按区域 / 机型 / 航线类型下钻分析",
        "导出报表（CSV/PDF）",
    ],
    "clicks": [
        ["航线收益排行行", "L2 航线收益分析"],
        ["← 返回", "L2 航班定价"],
    ],
    "entry": "入口：L2 航班定价 → 任意核心 KPI",
    "kind": "new",
}

N48 = {
    "id": "n48_辅助收入管理",
    "level": "L3",
    "title": "💳 辅助收入管理",
    "meta": "从航班定价 · 辅助收入面板进入",
    "badge": "新建",
    "items": [
        "行李费：各航线/舱位行李额度与收费标准",
        "选座费：紧急出口、前排、靠窗等座位溢价",
        "退改签手续费：按舱位/提前天数动态费率表",
        "机上销售：餐饮、免税品等销售收入",
        "非航收入汇总：联名卡分润 · 保险佣金 · 租车/酒店佣金",
        "各辅助收入项的趋势图与占比分析",
    ],
    "clicks": [
        ["← 返回", "L2 航班定价"],
    ],
    "entry": "入口：L2 航班定价 → 辅助收入面板",
    "kind": "new",
}

N50 = {
    "id": "n50_外部环境分析",
    "level": "L3",
    "title": "🚄 外部环境分析",
    "meta": "高铁替代等外部威胁 · 自航班定价热力图进入",
    "badge": "新建",
    "items": [
        "高铁替代威胁：≤1000km 航线等级（高/中/低）与明细",
        "替代交通对客流/票价敏感度的影响摘要",
        "可联动单航线或收益分析查看受影响航线",
    ],
    "clicks": [
        ["受影响航线", "L2 航线收益分析 或 L3 单航线定价详情"],
        ["← 返回", "L2 航班定价"],
    ],
    "entry": "入口：L2 航班定价 → 高铁竞争热力图",
    "kind": "new",
}

WIRING = [
    # from 页面间连线建议.docx
    ["n14_市场销售部", "n49_航班定价", "定价策略"],
    ["n49_航班定价", "n45_单航线定价详情", "航线舱位行"],
    ["n49_航班定价", "n46_动态定价算法", "AI 调价建议卡片"],
    ["n49_航班定价", "n47_收益管理仪表盘", "任意核心 KPI"],
    ["n49_航班定价", "n48_辅助收入管理", "辅助收入面板"],
    ["n49_航班定价", "n29_竞争态势_AI_航司", "竞对战略动态"],
    ["n49_航班定价", "n33_FFP_常旅客管理", "忠诚度计划联动"],
    ["n45_单航线定价详情", "n49_航班定价", "← 返回"],
    ["n46_动态定价算法", "n45_单航线定价详情", "手动覆盖"],
    # extras from 航班定价.docx clicks
    ["n49_航班定价", "n50_外部环境分析", "高铁竞争热力图"],
    ["n49_航班定价", "n14_市场销售部", "← 返回"],
    ["n46_动态定价算法", "n49_航班定价", "← 返回"],
    ["n47_收益管理仪表盘", "n49_航班定价", "← 返回"],
    ["n47_收益管理仪表盘", "n44_航线收益分析", "航线收益排行行"],
    ["n48_辅助收入管理", "n49_航班定价", "← 返回"],
    ["n50_外部环境分析", "n49_航班定价", "← 返回"],
    ["n45_单航线定价详情", "n49_航班定价", "确认调价"],
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

    # Update L1 marketing: 定价策略 click → L2 航班定价
    n14 = by["n14_市场销售部"]
    items = list(n14.get("items") or [])
    # ensure pricing strategy wording
    replaced = False
    for i, it in enumerate(items):
        if "定价策略" in it:
            items[i] = "定价策略：票价 / 折扣 / 预售 → 进入 L2 航班定价"
            replaced = True
            break
    if not replaced:
        items.insert(2, "定价策略：票价 / 折扣 / 预售 → 进入 L2 航班定价")
    n14["items"] = items
    clicks = [c for c in (n14.get("clicks") or []) if c[0] != "定价策略"]
    clicks.insert(0, ["定价策略", "L2 航班定价"])
    n14["clicks"] = clicks

    for screen in (N49, N45, N46, N47, N48, N50):
        upsert(catalog, screen)

    def has(fr, to, label=None):
        for e in edges:
            if e[0] == fr and e[1] == to and (label is None or e[2] == label):
                return True
        return False

    for e in WIRING:
        if not has(e[0], e[1], e[2]):
            # also skip duplicate from-to with different label if same path already exists? keep distinct labels
            if not any(x[0] == e[0] and x[1] == e[1] and x[2] == e[2] for x in edges):
                edges.append(e)

    # avoid duplicate 确认调价 and ← 返回 both to same - both are fine with different labels

    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html2, n = re.subn(
        r"var CATALOG = \[[\s\S]*?\];\s*\n\s*var DEFAULT_EDGES = \[[\s\S]*?\];",
        "var CATALOG = " + cat_js + ";\n  var DEFAULT_EDGES = " + edges_js + ";",
        html,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"catalog replace failed {n}")

    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260809f';", html2, count=1)

    # expand migrate title map for custom pricing screens
    mig_anchor = "if (title.indexOf('航线收益分析') >= 0) map[id] = 'n44_航线收益分析';"
    mig_extra = (
        "if (title.indexOf('航线收益分析') >= 0) map[id] = 'n44_航线收益分析';\n"
        "      if (title.indexOf('航班定价') >= 0 && title.indexOf('单航线') < 0) map[id] = 'n49_航班定价';\n"
        "      if (title.indexOf('单航线定价') >= 0) map[id] = 'n45_单航线定价详情';\n"
        "      if (title.indexOf('动态定价') >= 0) map[id] = 'n46_动态定价算法';\n"
        "      if (title.indexOf('收益管理仪表盘') >= 0) map[id] = 'n47_收益管理仪表盘';\n"
        "      if (title.indexOf('辅助收入') >= 0) map[id] = 'n48_辅助收入管理';\n"
        "      if (title.indexOf('外部环境') >= 0) map[id] = 'n50_外部环境分析';"
    )
    if mig_anchor in html2:
        html2 = html2.replace(mig_anchor, mig_extra, 1)

    # filter extras by title keywords already covered by 航线收益 + map[id]
    # also filter known titles in extras filter block
    old_filter = "if (String(x.title || '').indexOf('航线收益分析') >= 0) return false;"
    new_filter = (
        "var t = String(x.title || '');\n"
        "      if (t.indexOf('航线收益分析') >= 0) return false;\n"
        "      if (t.indexOf('航班定价') >= 0) return false;\n"
        "      if (t.indexOf('单航线定价') >= 0) return false;\n"
        "      if (t.indexOf('动态定价') >= 0) return false;\n"
        "      if (t.indexOf('收益管理仪表盘') >= 0) return false;\n"
        "      if (t.indexOf('辅助收入') >= 0) return false;\n"
        "      if (t.indexOf('外部环境') >= 0) return false;"
    )
    if old_filter in html2:
        html2 = html2.replace(old_filter, new_filter, 1)

    # NOTE_FEATURE_PACK update
    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack["n14_市场销售部"] = {
            "items": n14["items"],
            "clicks": n14["clicks"],
            "meta": n14.get("meta", ""),
            "entry": n14.get("entry", ""),
        }
        for screen in (N49, N45, N46, N47, N48, N50):
            pack[screen["id"]] = {
                "items": screen["items"],
                "clicks": screen["clicks"],
                "title": screen["title"],
                "meta": screen["meta"],
                "entry": screen["entry"],
            }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1) :]

    # ensure new edges added on migrate for pricing cluster
    edge_add_anchor = "['n42_航班周总计划','n43_航班详细计划','点航班行']"
    edge_add_extra = (
        "['n42_航班周总计划','n43_航班详细计划','点航班行'],\n"
        "      ['n14_市场销售部','n49_航班定价','定价策略'],\n"
        "      ['n49_航班定价','n45_单航线定价详情','航线舱位行'],\n"
        "      ['n49_航班定价','n46_动态定价算法','AI 调价建议卡片'],\n"
        "      ['n49_航班定价','n47_收益管理仪表盘','任意核心 KPI'],\n"
        "      ['n49_航班定价','n48_辅助收入管理','辅助收入面板']"
    )
    if edge_add_anchor in html2:
        html2 = html2.replace(edge_add_anchor, edge_add_extra, 1)

    BOARD.write_text(html2, encoding="utf-8")
    print("ok screens", [s["id"] for s in (N49, N45, N46, N47, N48, N50)], "edges+", len(WIRING))


if __name__ == "__main__":
    main()
