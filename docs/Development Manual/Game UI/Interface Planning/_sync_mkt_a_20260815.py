# -*- coding: utf-8 -*-
"""Sync MKT 市场销售部 批次 A 金标准（Phase 2 Week 7）into the whiteboard."""
import json
import re
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)

NEW_REV = "20260815a"


def load_array(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "[":
            depth += 1
        elif h[i] == "]":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("array not found: " + marker)


def load_object(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "{":
            depth += 1
        elif h[i] == "}":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("object not found: " + marker)


def main():
    h = BOARD.read_text(encoding="utf-8")

    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-15a: MKT 市场销售部 批次 A 金标准 — "
        "n14/n44/n49/n55 + n45/n46/n47/n48/n50/n56/n57/n58/n59 + n33/n34；"
        "需求价格弹性/票价-客座率/RASK-CASK/季节曲线/旺季策略ROI/辅助收入/FFP里程负债；"
        "MktEconomics+MktManager 回写 GameState.mkt；tools/balance MKT 年闭环；PlayMode≥3 */\n"
    )
    if "UX 2026-08-15a" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n14_市场销售部": (
            "市场 · L1 总览（航线总收入/成本/净利/利润率 + 客座率/RASK/CASK/品牌 + 航线收益排行 + 子页入口）",
            "金标准",
        ),
        "n44_航线收益分析": (
            "市场 · 航线收益分析（收益排行 + 单航线 RASK/CASK/盈亏平衡客座率 + 点行进 n45）",
            "金标准",
        ),
        "n49_航班定价": (
            "市场 · 航班定价（票价矩阵 + AI 调价建议 + 动态定价/收益仪表盘/辅助收入/FFP/外部环境入口）",
            "金标准",
        ),
        "n45_单航线定价详情": (
            "市场 · 单航线定价详情（头等/商务/高端经济/经济舱位票价乘数 stepper + 折扣 + AI 建议）",
            "金标准",
        ),
        "n46_动态定价算法": (
            "市场 · 动态定价算法（竞对权重/负载权重/节日敏感度/票价下限/每日调价次数 + 启用开关）",
            "金标准",
        ),
        "n47_收益管理仪表盘": (
            "市场 · 收益管理仪表盘（RASK/CASK/客座率/盈亏平衡/收益曲线）",
            "金标准",
        ),
        "n48_辅助收入管理": (
            "市场 · 辅助收入管理（行李/选座/退改/机上销售/非航占比 stepper + 人均辅助收入）",
            "金标准",
        ),
        "n50_外部环境分析": (
            "市场 · 外部环境分析（季节需求系数/高铁替代/竞对票价排名）",
            "金标准",
        ),
        "n55_季节性日历": (
            "市场 · 季节性日历（月份需求系数 + 节日曲线形态 + 旺季策略入口 → n56/n57/n58/n59）",
            "金标准",
        ),
        "n56_航季对比详情": (
            "市场 · 航季对比（夏秋 vs 冬春：班次/客座率/收益/单位收益对比）",
            "金标准",
        ),
        "n57_旺季策略配置器": (
            "市场 · 旺季策略配置器（运力/定价/服务/平季延伸四维度 + 保存策略包）",
            "金标准",
        ),
        "n58_历史趋势分析": (
            "市场 · 季节性历史趋势（需求/收益/客座率历史曲线）",
            "金标准",
        ),
        "n59_旺季影响预测弹窗": (
            "市场 · 旺季影响预测（利润增量/客座率增量/成本增量）",
            "金标准",
        ),
        "n33_FFP_常旅客管理": (
            "市场 · FFP 常旅客管理（累计/兑换比例 + 会员数/活跃率 + 里程负债 + 品牌联动）",
            "金标准",
        ),
        "n34_产品工坊": (
            "市场 · 产品工坊（打包产品名称/价格/预估转化率 + 上线回写）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in pages.items():
        if pid not in by_id:
            raise SystemExit("missing page id: " + pid)
        by_id[pid]["meta"] = meta
        by_id[pid]["badge"] = badge

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    # NOTE_FEATURE_PACK
    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns, ne, pack = load_object(h, marker)
        for pid in pages:
            c = by_id[pid]
            pack[pid] = {
                "items": c.get("items", []),
                "clicks": c.get("clicks", []),
            }
            if "title" in c:
                pack[pid]["title"] = c["title"]
            if "meta" in c:
                pack[pid]["meta"] = c["meta"]
            if "badge" in c:
                pack[pid]["badge"] = c["badge"]
        h = h[:ns] + json.dumps(pack, ensure_ascii=False, separators=(",", ":")) + h[ne:]
        print("updated NOTE_FEATURE_PACK")

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)


if __name__ == "__main__":
    main()
