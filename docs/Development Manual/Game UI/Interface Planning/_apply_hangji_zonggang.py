# -*- coding: utf-8 -*-
"""Add L2 航季总纲 (n66) + wiring from ops department."""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

N66 = {
    "id": "n66_航季总纲",
    "level": "L2",
    "title": "📋 XXXX年夏秋/冬春航季总纲",
    "meta": "运营部 · 航季顶层框架（航网/时刻/运力/保障）",
    "badge": "NEW",
    "items": [
        "航线与起降时刻：始发地、目的地机场，以及每个航班的计划起飞和到达时刻",
        "执行班期：航班在一周中的具体哪几天执飞",
        "使用机型及座位数：执飞该航线的具体飞机型号和座位配置",
        "航班号：每个航班的唯一编号",
        "执行起止日期：该航班在航季内的具体运营日期范围",
        "航班性质：正班 / 加班 / 包机等",
        "航线网络调整：新航季航线网络布局（新增 / 加密 / 调整 / 取消航线）",
        "运力投放计划：不同航线、不同区域投放的总体运力规模",
        "飞机与机组环：为整个航季的飞机周转（航班环）和机组排班提供顶层框架",
        "保障资源协调：统筹机务维修、地面服务、航食等各部门保障能力",
    ],
    "clicks": [
        ["生成航班周总计划", "L2 航班周总计划"],
        ["飞机与机组环", "L3 航班环构建器"],
        ["← 返回", "L1 运营部"],
    ],
    "entry": "入口：L1 运营部 → 航季总纲",
    "kind": "new",
}

WIRING = [
    ["n13_运营部", "n66_航季总纲", "航季总纲"],
    ["n66_航季总纲", "n42_航班周总计划", "生成航班周总计划"],
    ["n66_航季总纲", "n36_航班环构建器", "飞机与机组环"],
    ["n66_航季总纲", "n13_运营部", "← 返回"],
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

    # Update n13_运营部: add 航季总纲 entry
    n13 = by["n13_运营部"]
    items13 = list(n13.get("items") or [])
    # Replace or insert 航季 item
    replaced = False
    for i, it in enumerate(items13):
        if "航季" in it and ("夏秋" in it or "冬春" in it or "总纲" in it):
            items13[i] = "航季总纲：夏秋 / 冬春航季顶层框架（航网·时刻·运力·保障）→ 进入 L2 航季总纲"
            replaced = True
            break
    if not replaced:
        # insert after first item (目标)
        items13.insert(2, "航季总纲：夏秋 / 冬春航季顶层框架（航网·时刻·运力·保障）→ 进入 L2 航季总纲")
    n13["items"] = items13

    clicks13 = list(n13.get("clicks") or [])
    # Replace "航季切换" or add 航季总纲
    new_clicks = []
    added = False
    for c in clicks13:
        if c[0] == "航季切换" or "航季总纲" in c[0]:
            if not added:
                new_clicks.append(["航季总纲", "L2 航季总纲"])
                added = True
            continue
        new_clicks.append(c)
    if not added:
        # insert after 航班周总计划 if present
        inserted = False
        out = []
        for c in new_clicks:
            out.append(c)
            if c[0] == "航班周总计划" and not inserted:
                out.append(["航季总纲", "L2 航季总纲"])
                inserted = True
        if not inserted:
            out.insert(0, ["航季总纲", "L2 航季总纲"])
        new_clicks = out
    n13["clicks"] = new_clicks

    upsert(catalog, N66)

    def has_edge(fr, to, label):
        return any(x[0] == fr and x[1] == to and x[2] == label for x in edges)

    for e in WIRING:
        if not has_edge(e[0], e[1], e[2]):
            edges.append(e)

    # Bump CONTENT_REV so localStorage rebuilds edges
    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260810f';", html, count=1)

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

    # Migrate title map
    mig_anchor = "if (title.indexOf('战略资本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n65_竞争_战略资本';"
    mig_extra = (
        "if (title.indexOf('战略资本') >= 0 && title.indexOf('竞争') >= 0) map[id] = 'n65_竞争_战略资本';\n"
        "      if (title.indexOf('航季总纲') >= 0) map[id] = 'n66_航季总纲';"
    )
    if mig_anchor in html2:
        html2 = html2.replace(mig_anchor, mig_extra, 1)

    # Extras filter
    old_filter = "if (t.indexOf('战略资本') >= 0 && t.indexOf('竞争') >= 0) return false;"
    new_filter = (
        "if (t.indexOf('战略资本') >= 0 && t.indexOf('竞争') >= 0) return false;\n"
        "      if (t.indexOf('航季总纲') >= 0) return false;"
    )
    if old_filter in html2:
        html2 = html2.replace(old_filter, new_filter, 1)

    # NOTE_FEATURE_PACK
    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack["n13_运营部"] = {
            "items": n13["items"],
            "clicks": n13["clicks"],
            "meta": n13.get("meta", ""),
            "entry": n13.get("entry", ""),
        }
        pack["n66_航季总纲"] = {
            "items": N66["items"],
            "clicks": N66["clicks"],
            "title": N66["title"],
            "meta": N66["meta"],
            "entry": N66["entry"],
        }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1) :]

    # Ensure migrate hard-coded edges include n66
    edge_add_anchor = "['n60_竞争雷达','n61_竞对详情','竞对卡片']"
    edge_add_extra = (
        "['n60_竞争雷达','n61_竞对详情','竞对卡片'],\n"
        "      ['n13_运营部','n66_航季总纲','航季总纲'],\n"
        "      ['n66_航季总纲','n42_航班周总计划','生成航班周总计划']"
    )
    if edge_add_anchor in html2:
        html2 = html2.replace(edge_add_anchor, edge_add_extra, 1)

    BOARD.write_text(html2, encoding="utf-8")
    print("hangji zonggang applied; catalog", len(catalog), "edges", len(edges))


if __name__ == "__main__":
    main()
