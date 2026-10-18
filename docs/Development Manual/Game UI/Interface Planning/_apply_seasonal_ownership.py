# -*- coding: utf-8 -*-
"""Apply seasonal calendar cluster + ownership wiring from attachment docs.

Note: 归属表写 n44_航班定价，白板现网为 n49_航班定价（n44 已是航线收益分析），连线按 n49 落地。
"""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

N55 = {
    "id": "n55_季节性日历",
    "level": "L2",
    "title": "🗓️ 季节性日历 · 需求预测与旺季策略",
    "meta": "市场销售部 · 航季框架 + 节日需求曲线 + 旺季策略包",
    "badge": "新建",
    "items": [
        "航季标识：夏秋 / 冬春 · 换季倒计时（距下次更替 X 天）",
        "航季切换：左右切换或「航季对比」并排查看两季运力/航线变化",
        "航季时间轴：12 个月 · 航季起止 · 主要节假日 · 当前日期",
        "航季概览：总航班量 · 总运力 ASK · 覆盖航线数 · 同比上季变化%",
        "节日需求曲线：节前→节中→节后需求系数（0.5–3.0）；可叠加历史 vs 预测",
        "曲线形态：V型（春节/国庆）· 脉冲型（清明/端午）· 高原型（暑运）· 平缓型（平季延伸）",
        "节假日列表：春节/清明/五一/端午/暑运/中秋/国庆/圣诞元旦等；峰值系数与预测客座率",
        "关键节点：预售开启 · 运力决策截止日 · 首航日（可点进策略配置）",
        "波动性面板：历史客流同比 · 预订进度 · 平均票价趋势",
        "旺季策略包 · 运力：新开/加密 · 窄转宽 · 湿租/加班",
        "旺季策略包 · 定价：早鸟折扣 · 旺季溢价 · 舱位开放 · 差旅收紧 · 打包产品",
        "旺季策略包 · 服务：旅游航线 · 节日体验 · 特殊旅客服务",
        "旺季策略包 · 平季延伸：是否延伸淡季 · 时长 · 延伸定价系数",
        "策略操作：保存策略包 · 应用至全部适用航线 · 查看影响预测",
        "历史同期对比 · 12 个月季节性趋势图 · AO 学习建议",
    ],
    "clicks": [
        ["航季对比", "L3 航季对比详情"],
        ["需求曲线「预售开启」节点", "L3 旺季策略配置器（定价模块）"],
        ["需求曲线「运力决策截止日」节点", "L3 旺季策略配置器（运力模块）"],
        ["节假日列表中的节日", "同页切换需求曲线 + 策略包"],
        ["策略包 → 保存策略包", "L5 确认 → 生效"],
        ["策略包 → 查看影响预测", "L4 旺季影响预测弹窗"],
        ["策略包 → 应用至全部适用航线", "L5 确认 → 批量生效"],
        ["历史同期对比", "L3 历史趋势分析"],
        ["AO 学习建议 → 去配置", "定位到策略包对应模块"],
        ["← 返回", "L1 市场销售部"],
    ],
    "entry": "入口：L1 市场销售部 → 季节性日历；L2 航班定价 → 季节性趋势 Tab",
    "kind": "new",
}

N56 = {
    "id": "n56_航季对比详情",
    "level": "L3",
    "title": "🔄 航季对比 · 夏秋 vs 冬春",
    "meta": "从季节性日历 · 航季对比进入",
    "badge": "新建",
    "items": [
        "两航季并排：航线网络变化（新增 / 取消 / 保留）",
        "运力变化对比：ASK · 航班量 · 座位数",
        "时刻槽利用率对比（核心机场）",
        "航季更替影响分析（AO：哪些航线受换季影响最大）",
        "历史航季数据回顾",
    ],
    "clicks": [
        ["← 返回", "L2 季节性日历"],
    ],
    "entry": "入口：L2 季节性日历 → 航季对比",
    "kind": "new",
}

N57 = {
    "id": "n57_旺季策略配置器",
    "level": "L3",
    "title": "🚀 旺季策略配置器 · [节日名称]",
    "meta": "从季节性日历 · 需求曲线节点 / 策略包编辑进入",
    "badge": "新建",
    "items": [
        "四维度策略配置：运力 / 定价 / 服务 / 平季延伸",
        "策略影响模拟器：调参实时预估利润、客座率、成本变化",
        "策略模板库：春节标准包 · 暑运标准包等预设",
        "策略对比：当前方案 vs 默认方案 vs 去年方案",
    ],
    "clicks": [
        ["保存 / 应用", "回 L2 季节性日历并刷新策略包"],
        ["查看影响预测", "L4 旺季影响预测弹窗"],
        ["← 返回", "L2 季节性日历"],
    ],
    "entry": "入口：L2 季节性日历 → 需求曲线节点 / 策略包编辑",
    "kind": "new",
}

N58 = {
    "id": "n58_历史趋势分析",
    "level": "L3",
    "title": "📈 季节性历史趋势",
    "meta": "从季节性日历 · 历史同期对比进入",
    "badge": "新建",
    "items": [
        "过去 3 年各节假日需求曲线叠加对比",
        "各节假日客座率 / 收益 / 票价趋势图",
        "淡旺季边界识别（自动标注「平季延伸」趋势）",
        "季节性规律总结（AO 生成）",
    ],
    "clicks": [
        ["← 返回", "L2 季节性日历"],
    ],
    "entry": "入口：L2 季节性日历 → 历史同期对比",
    "kind": "new",
}

N59 = {
    "id": "n59_旺季影响预测弹窗",
    "level": "L4",
    "title": "📊 旺季影响预测",
    "meta": "弹窗 · 策略包影响预测报告",
    "badge": "弹窗",
    "items": [
        "预期利润增量",
        "客座率变化预估",
        "成本增量预估",
        "适用航线范围摘要",
        "确认关闭 / 返回修改策略",
    ],
    "clicks": [
        ["关闭 / 返回修改", "L2 季节性日历 或 L3 旺季策略配置器"],
    ],
    "entry": "入口：L2 季节性日历 / L3 旺季策略配置器 → 查看影响预测",
    "kind": "popup",
}

WIRING = [
    ["n14_市场销售部", "n55_季节性日历", "季节性日历"],
    ["n49_航班定价", "n55_季节性日历", "季节性趋势 Tab"],
    ["n44_航线收益分析", "n55_季节性日历", "季节性趋势 Tab"],
    ["n55_季节性日历", "n56_航季对比详情", "航季对比"],
    ["n55_季节性日历", "n57_旺季策略配置器", "需求曲线节点 / 策略包编辑"],
    ["n55_季节性日历", "n58_历史趋势分析", "历史同期对比"],
    ["n55_季节性日历", "n59_旺季影响预测弹窗", "查看影响预测"],
    ["n55_季节性日历", "n14_市场销售部", "← 返回"],
    ["n57_旺季策略配置器", "n55_季节性日历", "← 返回"],
    ["n57_旺季策略配置器", "n59_旺季影响预测弹窗", "查看影响预测"],
    ["n56_航季对比详情", "n55_季节性日历", "← 返回"],
    ["n58_历史趋势分析", "n55_季节性日历", "← 返回"],
]


def upsert(catalog: list, screen: dict) -> None:
    for i, c in enumerate(catalog):
        if c["id"] == screen["id"]:
            catalog[i] = dict(screen)
            return
    catalog.append(dict(screen))


def replace_click(clicks: list, old_src: str, new_pair: list) -> list:
    out = []
    replaced = False
    for c in clicks or []:
        if c[0] == old_src or (old_src in c[0] and "季节" in c[0]):
            if not replaced:
                out.append(new_pair)
                replaced = True
            continue
        # also replace destination 旺季策略 when source is 季节性
        if "季节性" in c[0] and ("旺季策略" in c[1] or c[1].startswith("L3 旺季")):
            if not replaced:
                out.append(new_pair)
                replaced = True
            continue
        out.append(c)
    if not replaced:
        out.append(new_pair)
    return out


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("CATALOG not found")
    catalog = json.loads(m.group(1))
    edges = json.loads(m.group(2))
    by = {c["id"]: c for c in catalog}

    # --- n14 marketing ---
    n14 = by["n14_市场销售部"]
    items = []
    for it in n14.get("items") or []:
        if "季节性日历" in it:
            items.append("季节性日历：节日需求曲线 + 旺季策略包 → 进入 L2 季节性日历")
        else:
            items.append(it)
    if not any("季节性日历" in x for x in items):
        items.append("季节性日历：节日需求曲线 + 旺季策略包 → 进入 L2 季节性日历")
    n14["items"] = items
    n14["clicks"] = replace_click(
        n14.get("clicks") or [],
        "季节性日历",
        ["季节性日历", "L2 季节性日历"],
    )

    # --- n49 flight pricing: add seasonal tab click ---
    n49 = by["n49_航班定价"]
    items49 = list(n49.get("items") or [])
    if not any("季节性趋势" in x for x in items49):
        items49.append("季节性趋势：需求波动与旺季策略入口（跳转 L2 季节性日历）")
    n49["items"] = items49
    clicks49 = [c for c in (n49.get("clicks") or []) if "季节性趋势" not in c[0] and c[1] != "L3 旺季策略"]
    # remove old 旺季策略 destinations if any
    clicks49 = [c for c in clicks49 if "旺季策略" not in c[1] or "季节" not in c[0]]
    if not any("季节性趋势" in c[0] for c in clicks49):
        # insert after KPI-related clicks
        clicks49.insert(1, ["季节性趋势 Tab", "L2 季节性日历"])
    n49["clicks"] = clicks49

    # --- n44 route revenue: seasonal tab → calendar ---
    n44 = by["n44_航线收益分析"]
    items44 = []
    for it in n44.get("items") or []:
        if "季节性趋势" in it or ("Tab" in it and "收益总览" in it):
            items44.append(
                "顶部 Tab：[收益总览] [航线排行] [竞对对比] [季节性趋势]（季节性趋势 → L2 季节性日历）"
            )
        else:
            items44.append(it)
    n44["items"] = items44
    n44["clicks"] = replace_click(
        n44.get("clicks") or [],
        "季节性趋势 Tab",
        ["季节性趋势 Tab", "L2 季节性日历"],
    )
    # scrub any remaining L3 旺季策略 from n44 clicks
    n44["clicks"] = [
        c if c[1] != "L3 旺季策略" else ["季节性趋势 Tab", "L2 季节性日历"]
        for c in n44["clicks"]
    ]

    for screen in (N55, N56, N57, N58, N59):
        upsert(catalog, screen)

    # remove obsolete edges: n14→something 旺季 if any; n44→old; n49→old
    def keep_edge(e):
        fr, to, label = e[0], e[1], e[2] if len(e) > 2 else ""
        # drop edges that pointed seasonal to non-calendar destinations incorrectly
        if label in ("季节性日历", "季节性趋势 Tab") and to not in (
            "n55_季节性日历",
        ):
            # keep if already correct; drop if to wrong target
            if to != "n55_季节性日历":
                return False
        return True

    edges = [e for e in edges if keep_edge(e)]

    def has(fr, to, label):
        return any(x[0] == fr and x[1] == to and x[2] == label for x in edges)

    for e in WIRING:
        if not has(e[0], e[1], e[2]):
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

    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260810a';", html2, count=1)

    # migrate custom seasonal titles
    mig_anchor = "if (title.indexOf('外部环境') >= 0) map[id] = 'n50_外部环境分析';"
    mig_extra = (
        "if (title.indexOf('外部环境') >= 0 && title.indexOf('竞争') < 0) map[id] = 'n50_外部环境分析';\n"
        "      if (title.indexOf('季节性日历') >= 0) map[id] = 'n55_季节性日历';\n"
        "      if (title.indexOf('航季对比') >= 0) map[id] = 'n56_航季对比详情';\n"
        "      if (title.indexOf('旺季策略配置') >= 0 || title === '旺季策略') map[id] = 'n57_旺季策略配置器';\n"
        "      if (title.indexOf('历史趋势') >= 0 || title.indexOf('季节性历史') >= 0) map[id] = 'n58_历史趋势分析';\n"
        "      if (title.indexOf('影响预测') >= 0) map[id] = 'n59_旺季影响预测弹窗';"
    )
    if mig_anchor in html2:
        html2 = html2.replace(mig_anchor, mig_extra, 1)

    old_filter = "if (t.indexOf('外部环境') >= 0) return false;"
    new_filter = (
        "if (t.indexOf('外部环境') >= 0 && t.indexOf('竞争') < 0) return false;\n"
        "      if (t.indexOf('季节性日历') >= 0) return false;\n"
        "      if (t.indexOf('航季对比') >= 0) return false;\n"
        "      if (t.indexOf('旺季策略') >= 0) return false;\n"
        "      if (t.indexOf('历史趋势') >= 0 || t.indexOf('季节性历史') >= 0) return false;\n"
        "      if (t.indexOf('影响预测') >= 0) return false;"
    )
    if old_filter in html2:
        html2 = html2.replace(old_filter, new_filter, 1)

    # ensure migrate adds seasonal edges
    edge_add = "['n49_航班定价','n48_辅助收入管理','辅助收入面板']"
    edge_add2 = (
        "['n49_航班定价','n48_辅助收入管理','辅助收入面板'],\n"
        "      ['n14_市场销售部','n55_季节性日历','季节性日历'],\n"
        "      ['n49_航班定价','n55_季节性日历','季节性趋势 Tab'],\n"
        "      ['n55_季节性日历','n57_旺季策略配置器','需求曲线节点 / 策略包编辑']"
    )
    if edge_add in html2:
        html2 = html2.replace(edge_add, edge_add2, 1)

    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack["n14_市场销售部"] = {
            "items": n14["items"],
            "clicks": n14["clicks"],
            "meta": n14.get("meta", ""),
            "entry": n14.get("entry", ""),
        }
        pack["n49_航班定价"] = {
            "items": n49["items"],
            "clicks": n49["clicks"],
            "title": n49.get("title"),
            "meta": n49.get("meta", ""),
            "entry": n49.get("entry", ""),
        }
        pack["n44_航线收益分析"] = {
            "items": n44["items"],
            "clicks": n44["clicks"],
            "title": n44.get("title"),
            "meta": n44.get("meta", ""),
            "entry": n44.get("entry", ""),
        }
        for screen in (N55, N56, N57, N58, N59):
            pack[screen["id"]] = {
                "items": screen["items"],
                "clicks": screen["clicks"],
                "title": screen["title"],
                "meta": screen["meta"],
                "entry": screen["entry"],
            }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1) :]

    BOARD.write_text(html2, encoding="utf-8")
    print("seasonal applied; catalog", len(catalog), "edges", len(edges))


if __name__ == "__main__":
    main()
