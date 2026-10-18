# -*- coding: utf-8 -*-
"""Fix wrong n5 edges, port offset, return-arrow, no card piercing."""
from __future__ import annotations

import json
import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

# Bogus "确认" edges wrongly mapped to 年度目标确认
REMOVE_EDGES = {
    ("n10_飞机详情_履历", "n5_年度目标确认"),
    ("n23_部门负责人任命", "n5_年度目标确认"),
    ("n31_对冲工作台", "n5_年度目标确认"),
    ("n32_碳配额管理", "n5_年度目标确认"),
}

NODE_PORT_OLD = """  function nodePort(id, which) {
    var el = nodesRoot.querySelector('.node[data-id="' + id + '"]');
    var n = state.nodes[id];
    if (!n) return { x: 0, y: 0 };
    var w = 360;
    var h = el ? el.offsetHeight : 220;
    if (which === 'top') return { x: n.x + w / 2, y: n.y };
    if (which === 'bottom') return { x: n.x + w / 2, y: n.y + h };
    if (which === 'left') return { x: n.x, y: n.y + h / 2 };
    if (which === 'right') return { x: n.x + w, y: n.y + h / 2 };
    return { x: n.x + w / 2, y: n.y + h };
  }"""

NODE_PORT_NEW = """  function nodePort(id, which) {
    var el = nodesRoot.querySelector('.node[data-id="' + id + '"]');
    var n = state.nodes[id];
    if (!n) return { x: 0, y: 0 };
    var w = 360;
    var h = el ? el.offsetHeight : 220;
    // 端口外移，连线停在卡片外，避免箭头/线段穿进正文
    var OUT = 12;
    if (which === 'top') return { x: n.x + w / 2, y: n.y - OUT };
    if (which === 'bottom') return { x: n.x + w / 2, y: n.y + h + OUT };
    if (which === 'left') return { x: n.x - OUT, y: n.y + h / 2 };
    if (which === 'right') return { x: n.x + w + OUT, y: n.y + h / 2 };
    return { x: n.x + w / 2, y: n.y + h + OUT };
  }"""

SCORE_OLD = """  function scorePath(pts, rects, fromId, toId) {
    if (!pts || pts.length < 2) return { hits: 9999, len: 1e9 };
    var hits = 0, len = 0;
    for (var i = 1; i < pts.length; i++) {
      var a = pts[i - 1], b = pts[i];
      len += Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]);
      var ign = {};
      // only the stub leaving/entering a port may clip its own card
      if (i === 1) ign[fromId] = true;
      if (i === pts.length - 1) ign[toId] = true;
      if (segHits(a, b, rects, ign)) hits++;
    }
    return { hits: hits, len: len };
  }"""

SCORE_NEW = """  function scorePath(pts, rects, fromId, toId) {
    if (!pts || pts.length < 2) return { hits: 9999, len: 1e9 };
    var hits = 0, len = 0;
    // 端口已在卡片外：任何穿过卡片的段都计罚，禁止「最后一段无视目标卡」穿正文
    for (var i = 1; i < pts.length; i++) {
      var a = pts[i - 1], b = pts[i];
      len += Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]);
      if (segHits(a, b, rects, {})) hits += 1;
    }
    return { hits: hits, len: len };
  }"""

# Return edges (下级→上级): arrow on parent bottom(output) looks wrong.
# Keep hierarchy ports but omit arrow; label already has ←.
RENDER_MARKER_FIX_OLD = """      if (r.bundled) {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.pts) + '"></path>';
        if (r.ptsEnd) html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.ptsEnd) + '" style="marker-end:url(#arrowHead)"></path>';
      } else {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '" style="marker-end:url(#arrowHead)"></path>';
      }"""

RENDER_MARKER_FIX_NEW = """      var isUpReturn = r.kind === 'up'; // 下级→上级：箭头若画在上级 bottom(输出点)会误导
      var arrowStyle = isUpReturn ? '' : ' style="marker-end:url(#arrowHead)"';
      if (r.bundled) {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.pts) + '"></path>';
        if (r.ptsEnd) html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.ptsEnd) + '"' + arrowStyle + '></path>';
      } else {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"' + arrowStyle + '></path>';
      }"""

TOP_MARKER_OLD = """        top += '<path class="edge selected raised" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '" style="marker-end:url(#arrowHeadHotTop)"></path>';"""

TOP_MARKER_NEW = """        var upSel = r.kind === 'up';
        top += '<path class="edge selected raised" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"' + (upSel ? '' : ' style="marker-end:url(#arrowHeadHotTop)"') + '></path>';"""


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")

    m = re.search(r"var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("DEFAULT_EDGES not found")
    edges = json.loads(m.group(1))
    before = len(edges)
    edges = [e for e in edges if (e[0], e[1]) not in REMOVE_EDGES]
    after = len(edges)
    html = html[: m.start(1)] + json.dumps(edges, ensure_ascii=False) + html[m.end(1) :]

    if NODE_PORT_OLD not in html:
        raise SystemExit("nodePort block not found")
    html = html.replace(NODE_PORT_OLD, NODE_PORT_NEW, 1)

    if SCORE_OLD not in html:
        raise SystemExit("scorePath block not found")
    html = html.replace(SCORE_OLD, SCORE_NEW, 1)

    if RENDER_MARKER_FIX_OLD not in html:
        raise SystemExit("render marker block not found")
    html = html.replace(RENDER_MARKER_FIX_OLD, RENDER_MARKER_FIX_NEW, 1)

    if TOP_MARKER_OLD not in html:
        raise SystemExit("top marker not found")
    html = html.replace(TOP_MARKER_OLD, TOP_MARKER_NEW, 1)

    html = html.replace("var CONTENT_REV = '20260810h';", "var CONTENT_REV = '20260810i';", 1)

    BOARD.write_text(html, encoding="utf-8")
    s = html.split("<script>")[1].split("</script>")[0]
    print("removed_edges", before - after, "now", after)
    print("CONTENT_REV", "20260810i" in html)
    print("OUT port", "var OUT = 12" in s)
    print("isUpReturn", "isUpReturn" in s)
    print("braces", s.count("{"), s.count("}"))


if __name__ == "__main__":
    main()
