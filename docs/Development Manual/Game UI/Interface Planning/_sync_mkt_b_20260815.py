# -*- coding: utf-8 -*-
"""Sync MKT 市场销售部 批次 B 金标准（Phase 2 Week 8）into the whiteboard."""
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

NEW_REV = "20260815b"


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
        "  /* UX 2026-08-15b: MKT 市场销售部 批次 B 金标准 — "
        "n60 竞争雷达 + n61-n65 竞对标 + n29 竞争态势/AI航司 + "
        "n67 大客户合同 + n68 渠道预算 + n69 分销 + n70 直销 + n71 渠道ROI + "
        "n72 品牌危机公关 + n73 舆情监测 + n74 危机响应 + n75 品牌修复；"
        "7维对标/综合竞争力/渠道ROI/CAC/提直降代/品牌信任/危机分级；"
        "MktCompetitionEconomics+MktManager 回写 GameState.mkt；tools/balance MKT B 年闭环；PlayMode≥3 */\n"
    )
    if "UX 2026-08-15b" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n60_竞争雷达": (
            "市场 · 竞争雷达（7维对标雷达/综合竞争力排名/态势判断/竞对卡片→n61/逐维深度分析/竞争事件流/AO战略建议）",
            "金标准",
        ),
        "n61_竞对详情": (
            "市场 · 竞对详情（基本信息/7维雷达/核心指标/历史动态/财务摘要/战略研判）",
            "金标准",
        ),
        "n62_竞争_航线网络": (
            "市场 · 竞争航线网络对标（航线数/覆盖城市/份额/独飞优势对比）",
            "金标准",
        ),
        "n63_竞争_产品服务": (
            "市场 · 竞争产品服务对标（品牌声誉/准点率/Skytrax/FFP会员对比）",
            "金标准",
        ),
        "n64_竞争_运营成本": (
            "市场 · 竞争运营成本对标（CASK/负债率/机队日利用率对比）",
            "金标准",
        ),
        "n65_竞争_战略资本": (
            "市场 · 竞争战略资本对标（现金/负债率/信用评级/联盟/融资能力对比）",
            "金标准",
        ),
        "n29_竞争态势_AI_航司": (
            "市场 · 竞争态势/AI航司（份额饼图/竞对列表/竞合事件/价格战响应/深度分析→n60）",
            "金标准",
        ),
        "n67_大客户合同": (
            "市场 · 大客户合同（签约看板/客户分级/前返后返/差旅管理/快速签约回写）",
            "金标准",
        ),
        "n68_渠道预算": (
            "市场 · 渠道预算（年度总预算/使用率/提直降代滑块/GDS/OTA/TMC/返点 stepper/渠道健康度/→n69/n70/n71）",
            "金标准",
        ),
        "n69_分销渠道管理": (
            "市场 · 分销渠道管理（GDS/OTA/TMC/旅行社/佣金策略/附加费开关/合规看板）",
            "金标准",
        ),
        "n70_直销渠道管理": (
            "市场 · 直销渠道管理（官网/APP/呼叫中心/品牌推广/直销效能对比/CAC）",
            "金标准",
        ),
        "n71_渠道ROI分析": (
            "市场 · 渠道ROI分析（分销vs直销ROI对比/渠道集中度/钻取分销/直销）",
            "金标准",
        ),
        "n72_品牌危机公关": (
            "市场 · 品牌危机公关（信任指数/危机等级/三大风险看板/实时舆情流/→n73/n74/n75/预警演练）",
            "金标准",
        ),
        "n73_舆情监测中心": (
            "市场 · 舆情监测中心（情感打分/舆情条目/分级预警规则/AI话术助手）",
            "金标准",
        ),
        "n74_危机响应指挥部": (
            "市场 · 危机响应指挥部（响应等级/5步流程/声明模板/推进阶段/保存回写）",
            "金标准",
        ),
        "n75_品牌修复计划": (
            "市场 · 品牌修复计划（信任恢复预估/整改措施清单/新增措施回写/竞对恢复对比）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in pages.items():
        if pid not in by_id:
            raise SystemExit("missing page id: " + pid)
        by_id[pid]["meta"] = meta
        by_id[pid]["badge"] = badge

    # n14 总览 meta 追加批次 B 入口
    if "n14_市场销售部" in by_id:
        by_id["n14_市场销售部"]["meta"] = (
            "市场 · L1 总览（航线收入/成本/净利/利润率 + 客座率/RASK/CASK/品牌 + 航线收益排行 + "
            "子页入口含 竞争雷达/大客户合同/渠道预算/品牌危机公关）"
        )

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
