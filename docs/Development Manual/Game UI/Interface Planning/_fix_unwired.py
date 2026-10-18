# -*- coding: utf-8 -*-
"""Delete n27_消息中心 and wire up n19_安全质量部 + n38_历史回放."""
from __future__ import annotations

import json, re
from pathlib import Path

BOARD = Path(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html')

def main():
    html = BOARD.read_text(encoding="utf-8")
    m = re.search(r"var CATALOG = (\[[\s\S]*?\]);\s*\n\s*var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("CATALOG not found")
    catalog = json.loads(m.group(1))
    edges = json.loads(m.group(2))

    # ── 1. Remove n27_消息中心 from catalog ──
    catalog = [c for c in catalog if c['id'] != 'n27_消息中心']

    # ── 2. Clean up any edges referencing n27 (none exist, but safe) ──
    edges = [e for e in edges if 'n27' not in e[0] and 'n27' not in e[1]]

    # ── 3. Clean up any clicks in other pages pointing to n27 ──
    for c in catalog:
        clicks = c.get('clicks')
        if clicks:
            c['clicks'] = [cl for cl in clicks if cl[1] != 'n27_消息中心' and '消息中心' not in cl[1]]
        items = c.get('items')
        if items:
            c['items'] = [it for it in items if '消息中心' not in it]

    # ── 4. Build ID map for quick access ──
    by = {c['id']: c for c in catalog}

    # ── 5. Wire n19_安全质量部 ──
    # n19 is L1 department; n0 bottom tab has "安全"
    new_edges_n19 = [
        # Main shell → safety tab
        ["n0_主界面框架_常驻", "n19_安全质量部", "底部Tab·安全"],
        # Safety → crisis/IRROPS (safety incidents escalate)
        ["n19_安全质量部", "n24_危机_IRROPS_弹窗", "安全事件·危机升级"],
        # Safety → event log
        ["n19_安全质量部", "n30_随机事件日志", "安全事件日志"],
        # Safety → carbon quota (environmental safety)
        ["n19_安全质量部", "n32_碳配额管理", "碳配额与环境合规"],
        # ← 返回 to dashboard
        ["n19_安全质量部", "n6_主页_Dashboard", "← 返回"],
    ]
    for e in new_edges_n19:
        if not any(x[0] == e[0] and x[1] == e[1] and x[2] == e[2] for x in edges):
            edges.append(e)

    # Also update n19 clicks to match wired targets
    if 'n19_安全质量部' in by:
        n19 = by['n19_安全质量部']
        # Update clicks from generic text to specific wired IDs
        n19_clicks_new = [
            ["调查选项", "L4 危机/IRROPS 弹窗"],
            ["申诉/接受", "L4 危机/IRROPS 弹窗"],
            ["安全事件日志", "L2 随机事件日志"],
            ["碳配额管理", "L3 碳配额管理"],
            ["← 返回", "L1 主页 Dashboard"],
        ]
        n19['clicks'] = n19_clicks_new

    # ── 6. Wire n38_历史回放 ──
    # History replay accessible from dashboard and settings
    new_edges_n38 = [
        ["n6_主页_Dashboard", "n38_历史回放", "历史回放"],
        ["n40_设置_存档", "n38_历史回放", "历史回放"],
        ["n38_历史回放", "n6_主页_Dashboard", "← 返回"],
    ]
    for e in new_edges_n38:
        if not any(x[0] == e[0] and x[1] == e[1] and x[2] == e[2] for x in edges):
            edges.append(e)

    # Update n38 clicks
    if 'n38_历史回放' in by:
        n38 = by['n38_历史回放']
        n38['clicks'] = [
            ["← 返回", "L1 主页 Dashboard"],
        ]

    # ── 7. Also update n0 clicks to include n19 safety tab ──
    if 'n0_主界面框架_常驻' in by:
        n0 = by['n0_主界面框架_常驻']
        n0_clicks = n0.get('clicks') or []
        # Check if 安全 tab click exists
        has_safety = any('安全' in (c[0] or '') or 'n19' in (c[1] or '') for c in n0_clicks)
        if not has_safety:
            n0_clicks.append(["底部 Tab 安全", "L1 安全质量部"])
        n0['clicks'] = n0_clicks

    # ── 8. Bump CONTENT_REV ──
    html2 = re.sub(r"var CONTENT_REV = '[^']*';", "var CONTENT_REV = '20260810c';", html, count=1)

    # ── 9. Replace CATALOG + DEFAULT_EDGES ──
    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html2, n = re.subn(
        r"var CATALOG = \[[\s\S]*?\];\s*\n\s*var DEFAULT_EDGES = \[[\s\S]*?\];",
        "var CATALOG = " + cat_js + ";\n  var DEFAULT_EDGES = " + edges_js + ";",
        html2, count=1,
    )
    if n != 1:
        raise SystemExit(f"replace failed {n}")

    # ── 10. Update NOTE_FEATURE_PACK: remove n27 ──
    pack_m = re.search(r"var NOTE_FEATURE_PACK = (\{[\s\S]*?\});\s*\n\s*function noteLinesToFeatures", html2)
    if pack_m:
        pack = json.loads(pack_m.group(1))
        pack.pop('n27_消息中心', None)
        # Add n19 and n38 to pack
        if 'n19_安全质量部' in by:
            pack['n19_安全质量部'] = {
                "items": by['n19_安全质量部']["items"],
                "clicks": by['n19_安全质量部']["clicks"],
                "title": by['n19_安全质量部'].get("title"),
                "meta": by['n19_安全质量部'].get("meta", ""),
                "entry": by['n19_安全质量部'].get("entry", ""),
            }
        if 'n38_历史回放' in by:
            pack['n38_历史回放'] = {
                "items": by['n38_历史回放']["items"],
                "clicks": by['n38_历史回放']["clicks"],
                "title": by['n38_历史回放'].get("title"),
                "meta": by['n38_历史回放'].get("meta", ""),
                "entry": by['n38_历史回放'].get("entry", ""),
            }
        html2 = html2[: pack_m.start(1)] + json.dumps(pack, ensure_ascii=False) + html2[pack_m.end(1):]

    BOARD.write_text(html2, encoding="utf-8")
    print("Done: removed n27, wired n19+n38; catalog", len(catalog), "edges", len(edges))


if __name__ == "__main__":
    main()
