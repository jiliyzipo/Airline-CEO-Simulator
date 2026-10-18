# -*- coding: utf-8 -*-
"""Sync FLEET/NET 机队+航线网络金标准（Phase 2 Week 6）into the whiteboard."""
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

NEW_REV = "20260814c"


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


def replace_click(lst, old_dst_sub, new_dst):
    for x in lst:
        if isinstance(x, list) and len(x) >= 2 and isinstance(x[1], str) and old_dst_sub in x[1]:
            x[1] = new_dst
            return True
    return False


def main():
    h = BOARD.read_text(encoding="utf-8")

    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-14c: FLEET/NET 金标准 — n9/n10/n11/n97/n107 + n12/n41；"
        "机队残值/维护/载客配置/大修/购机会议 + 航线网络/单航线；"
        "FleetEconomics+FleetManager 回写；tools/balance FLEET/NET 年闭环；PlayMode≥3 */\n"
    )
    if "UX 2026-08-14c" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n9_机队": (
            "FLEET · 金标准收口（机队列表 · 汇总指标 · 点飞机进详情 · 购机入口→n97）",
            "金标准",
        ),
        "n10_飞机详情_履历": (
            "机队 · 飞机详情（注册号/机型/舱位/估值/维护/执飞航线 + 操作入口）",
            "金标准",
        ),
        "n11_座位配置": (
            "机队 · 载客量配置（头等/商务/经济 ±10 · 收益乘数实时 · 保存/恢复默认）",
            "金标准",
        ),
        "n97_购机决策会议": (
            "机队 · 购机会议（机型/数量/采购方式 + 五部门意见卡 + 批准/暂缓/拒绝）",
            "金标准",
        ),
        "n107_预算确认": (
            "机队 · 大修预算确认（A–D检/发动机/起落架/客舱 · 费用/停场/不可预见费 → 确认排程）",
            "金标准",
        ),
        "n12_航线网络": (
            "NET · 金标准收口（网络统计 · 航线列表 · 点击进单航线 · 开通新航线入口）",
            "金标准",
        ),
        "n41_单航线": (
            "航线网络 · 单航线（航班计划/时刻/运营表现/市场/物理/执飞注册号）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in pages.items():
        if pid not in by_id:
            raise SystemExit("missing page id: " + pid)
        by_id[pid]["meta"] = meta
        by_id[pid]["badge"] = badge

    # 点击描述对齐实装
    replace_click(by_id["n9_机队"]["clicks"],
                  "L2 飞机详情", "n10 飞机详情（FleetSubpagePanel 运行时构建）")
    replace_click(by_id["n9_机队"]["clicks"],
                  "L4 购机决策会议", "n97 购机决策会议（机型/数量/采购方式）")
    replace_click(by_id["n10_飞机详情_履历"]["clicks"],
                  "L5 确认 → 回机队", "FleetManager.TryRetireAircraft → 回收残值回机队")
    replace_click(by_id["n10_飞机详情_履历"]["clicks"],
                  "L4/L5 预算确认", "n107 预算确认（选大修类型 → 确认排程）")
    replace_click(by_id["n10_飞机详情_履历"]["clicks"],
                  "邮件 + L4 购机决策会议", "n97 购机决策会议（申请替换 1:1）")
    replace_click(by_id["n11_座位配置"]["clicks"],
                  "写回飞机详情", "ApplySeatConfig 写回飞机详情（总座位实时重算）")
    replace_click(by_id["n97_购机决策会议"]["clicks"],
                  "发 LOI → 采购流程启动", "TryAcquireFleet 写回机队（买断全款 / 租赁 10% 首付）")
    replace_click(by_id["n12_航线网络"]["clicks"],
                  "L2 单航线", "n41 单航线（RouteDetailPanel 运行时构建）")

    # n9 / n12：补已实现项
    n9_items = list(by_id["n9_机队"].get("items", []))
    n9_items.append("✅ 已实现：整行点击进 n10 飞机详情 · 「购买飞机」进 n97 购机会议 · 退役回收残值")
    n9_items.append("✅ 已实现：机队汇总（总数/适航/均龄/月维护/现金）· 推荐机型引导高亮（顾问 S4）")
    by_id["n9_机队"]["items"] = n9_items

    n12_items = list(by_id["n12_航线网络"].get("items", []))
    n12_items.append("✅ 已实现：网络统计（总航线/覆盖城市/网络日利/准点/客座）· 航线卡片列表 · 点击进 n41")
    n12_items.append("✅ 已实现：「+ 开通新航线」复用 OpenRouteModal → RPM 决策流")
    by_id["n12_航线网络"]["items"] = n12_items

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
