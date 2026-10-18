# -*- coding: utf-8 -*-
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BOARD = ROOT / "Airline_CEO__UI交互白板 V1.0.html"
EXPORT = Path(r"c:\Users\jiliy\Downloads\UI交互白板_含新界面_2026-08-09.json")
OUT = ROOT / "UI交互白板_已应用批注_2026-08-09.json"


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    if "airline_ceo_ui_board_v3_noted_20260809" not in html:
        raise SystemExit("storage key not bumped")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);", html)
    cat = {c["id"]: c for c in json.loads(m.group(1))}
    checks = [
        ("n0_主界面框架_常驻", "取消 8 倍速"),
        ("n2_玩家资料创建", "个人形象"),
        ("n6_主页_Dashboard", "宏观经济指示灯"),
        ("n13_运营部", "航班周总计划"),
    ]
    for nid, needle in checks:
        if needle not in "".join(cat[nid]["items"]):
            raise SystemExit(f"missing {needle} in {nid}")
    if "载客量配置" not in cat["n11_座位配置"]["title"]:
        raise SystemExit("n11 title not renamed")
    for nid in ("n41_单航线", "n42_航班周总计划", "n43_航班详细计划"):
        if nid not in cat:
            raise SystemExit(f"missing {nid}")
    print("spot checks OK; catalog size", len(cat))

    export = json.loads(EXPORT.read_text(encoding="utf-8"))
    applied = {k for k, v in export.get("nodes", {}).items() if (v.get("note") or "").strip()}
    nodes = dict(export.get("nodes") or {})
    for k in applied:
        if k in nodes:
            nodes[k]["note"] = ""

    old_to_new = {
        "custom_q282zgz": "n41_单航线",
        "custom_uqawjw8": "n43_航班详细计划",
    }
    for old, new in old_to_new.items():
        if old in nodes:
            pos = nodes.pop(old)
            nodes[new] = {"x": pos.get("x", 100), "y": pos.get("y", 100), "note": ""}

    ops = nodes.get("n13_运营部") or {"x": 900, "y": 400}
    if "n42_航班周总计划" not in nodes:
        nodes["n42_航班周总计划"] = {"x": ops["x"] + 420, "y": ops["y"], "note": ""}
    if "n41_单航线" not in nodes:
        rt = nodes.get("n12_航线网络") or {"x": 900, "y": 200}
        nodes["n41_单航线"] = {"x": rt["x"] + 420, "y": rt["y"], "note": ""}
    if "n43_航班详细计划" not in nodes:
        wp = nodes["n42_航班周总计划"]
        nodes["n43_航班详细计划"] = {"x": wp["x"] + 420, "y": wp["y"], "note": ""}

    edges = []
    seen = set()

    def add(e: dict) -> None:
        key = (e["from"], e["to"], e.get("label", ""))
        if key in seen:
            return
        seen.add(key)
        edges.append(e)

    for e in export.get("edges") or []:
        fr = old_to_new.get(e.get("from"), e.get("from"))
        to = old_to_new.get(e.get("to"), e.get("to"))
        if fr == "n0_主界面框架_常驻" and to == "n27_消息中心":
            continue
        add(
            {
                "id": e.get("id") or f"e_{fr}_{to}",
                "from": fr,
                "to": to,
                "label": e.get("label", ""),
            }
        )

    for e in (
        {"id": "e_route_single", "from": "n12_航线网络", "to": "n41_单航线", "label": "点航线"},
        {"id": "e_ops_week", "from": "n13_运营部", "to": "n42_航班周总计划", "label": "航班周总计划"},
        {"id": "e_week_detail", "from": "n42_航班周总计划", "to": "n43_航班详细计划", "label": "点航班行"},
    ):
        add(e)

    for e in edges:
        if e["from"] == "n10_飞机详情_履历" and e["to"] == "n11_座位配置":
            e["label"] = "载客量配置"

    extras = []
    for ex in export.get("extras") or []:
        if ex.get("id") in ("custom_q282zgz", "custom_uqawjw8", "n14_市场销售部"):
            continue
        extras.append(ex)

    out = {
        "version": 3,
        "cam": export.get("cam") or {"x": 30, "y": 30, "scale": 0.72},
        "nodes": nodes,
        "edges": edges,
        "extras": extras,
        "note": "已按 2026-08-09 批注更新内置界面内容；批注栏已清空。请用白板「导入」加载本文件。",
    }
    OUT.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    print("wrote", OUT)
    print("nodes", len(nodes), "edges", len(edges), "extras", len(extras), "cleared", len(applied))


if __name__ == "__main__":
    main()
