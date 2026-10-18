# -*- coding: utf-8 -*-
"""Fix edge routing: no card crossings, edges above cards, remove paintRoutes crash."""
from __future__ import annotations

import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

CSS_OLD = """    #nodes { position: absolute; left: 0; top: 0; z-index: 2; }
    #edges { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 1; pointer-events: none; overflow: visible; }
    #edgesTop { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 4; pointer-events: none; overflow: visible; }"""

CSS_NEW = """    #nodes { position: absolute; left: 0; top: 0; z-index: 2; }
    #edges { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 3; pointer-events: none; overflow: visible; }
    #edgesTop { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 5; pointer-events: none; overflow: visible; }"""

HELP_OLD = """    · 普通连线在卡片下层、只走卡片间空隙；相近线合干<br>
    · 点击连线→最上层亮黄完整路径；再点取消<br>"""

HELP_NEW = """    · 连线在卡片上层、正交折线只走卡片间空隙（不穿正文）；相近线合干<br>
    · 点击连线→亮黄完整路径置顶；再点取消<br>"""

ROUTE_START = "  /** Build free horizontal / vertical channel coordinates from gaps between cards. */"
ROUTE_END = "  var MERGE_Y = 180;"

# Everything from buildChannels through bentPath, replaced; MERGE_Y kept as anchor after insert
NEW_ROUTE = r'''  /** Build free horizontal / vertical channel coordinates from gaps between cards. */
  function buildChannels(rects) {
    var hCh = [], vCh = [];
    if (!rects.length) return { h: [200], v: [200] };

    var bands = rects.map(function (r) { return { a: r.y, b: r.b }; })
      .sort(function (p, q) { return p.a - q.a; });
    var merged = [];
    bands.forEach(function (b) {
      if (!merged.length || b.a > merged[merged.length - 1].b + 8) merged.push({ a: b.a, b: b.b });
      else merged[merged.length - 1].b = Math.max(merged[merged.length - 1].b, b.b);
    });
    var minY = merged[0].a - 100;
    var prev = minY;
    merged.forEach(function (m) {
      if (m.a - prev > 28) hCh.push(Math.round((prev + m.a) / 2));
      prev = m.b;
    });
    hCh.push(Math.round(prev + 80));

    var xbands = rects.map(function (r) { return { a: r.x, b: r.r }; })
      .sort(function (p, q) { return p.a - q.a; });
    var xmerged = [];
    xbands.forEach(function (b) {
      if (!xmerged.length || b.a > xmerged[xmerged.length - 1].b + 8) xmerged.push({ a: b.a, b: b.b });
      else xmerged[xmerged.length - 1].b = Math.max(xmerged[xmerged.length - 1].b, b.b);
    });
    var minX = xmerged[0].a - 100;
    var prevx = minX;
    xmerged.forEach(function (m) {
      if (m.a - prevx > 28) vCh.push(Math.round((prevx + m.a) / 2));
      prevx = m.b;
    });
    vCh.push(Math.round(prevx + 80));

    hCh.unshift(Math.round(minY - 60));
    vCh.unshift(Math.round(minX - 60));
    return { h: uniqSorted(hCh), v: uniqSorted(vCh) };
  }
  function uniqSorted(arr) {
    var out = [];
    arr.slice().sort(function (a, b) { return a - b; }).forEach(function (v) {
      if (!out.length || Math.abs(out[out.length - 1] - v) > 4) out.push(v);
    });
    return out;
  }
  function nearestChannel(val, channels) {
    var best = channels[0], bd = Math.abs(val - best);
    for (var i = 1; i < channels.length; i++) {
      var d = Math.abs(val - channels[i]);
      if (d < bd) { bd = d; best = channels[i]; }
    }
    return best;
  }
  function simplifyPts(pts) {
    if (!pts || pts.length < 2) return pts || [];
    var out = [pts[0]];
    for (var i = 1; i < pts.length; i++) {
      var p = pts[i], q = out[out.length - 1];
      if (Math.abs(p[0] - q[0]) < 1 && Math.abs(p[1] - q[1]) < 1) continue;
      if (out.length >= 2) {
        var r = out[out.length - 2];
        if ((Math.abs(r[0] - q[0]) < 1 && Math.abs(q[0] - p[0]) < 1) ||
            (Math.abs(r[1] - q[1]) < 1 && Math.abs(q[1] - p[1]) < 1)) {
          out[out.length - 1] = p;
          continue;
        }
      }
      out.push(p);
    }
    return out;
  }
  function ptsToPath(pts) {
    if (!pts || !pts.length) return '';
    var d = 'M ' + Math.round(pts[0][0]) + ' ' + Math.round(pts[0][1]);
    for (var i = 1; i < pts.length; i++) d += ' L ' + Math.round(pts[i][0]) + ' ' + Math.round(pts[i][1]);
    return d;
  }
  function segHits(p0, p1, rects, ignore) {
    if (Math.abs(p0[0] - p1[0]) < 1) return vHits(p0[0], p0[1], p1[1], rects, ignore);
    return hHits(p0[1], p0[0], p1[0], rects, ignore);
  }
  function scorePath(pts, rects, fromId, toId) {
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
  }
  function pickBest(cands, rects, fromId, toId) {
    var best = null, bestS = null;
    cands.forEach(function (pts) {
      var p = simplifyPts(pts);
      if (p.length < 2) return;
      var s = scorePath(p, rects, fromId, toId);
      if (!bestS || s.hits < bestS.hits || (s.hits === bestS.hits && s.len < bestS.len)) {
        best = p; bestS = s;
      }
    });
    return best;
  }
  /**
   * Orthogonal bent route that prefers free gutters between cards.
   * Scores many HVH / VHV / outer-rail candidates; never prefers a path that cuts card bodies.
   */
  function routeInGutters(a, b, fromPort, toPort, fromId, toId, rects) {
    var ch = buildChannels(rects);
    var cands = [];
    function add(pts) { cands.push(pts); }

    // 1) Simple 3-bend via each H bus
    ch.h.forEach(function (busY) {
      add([[a.x, a.y], [a.x, busY], [b.x, busY], [b.x, b.y]]);
    });
    // 2) Simple 3-bend via each V alley
    ch.v.forEach(function (alley) {
      add([[a.x, a.y], [alley, a.y], [alley, b.y], [b.x, b.y]]);
    });
    // 3) Outer / column rails: escape → rail → travel → approach
    var rails = [];
    if (ch.v.length) {
      rails.push(ch.v[0], ch.v[ch.v.length - 1]);
      if (ch.v.length > 2) rails.push(ch.v[Math.floor(ch.v.length / 2)]);
    }
    var yEsc = nearestChannel(a.y, ch.h);
    var yApp = nearestChannel(b.y, ch.h);
    rails.forEach(function (rail) {
      add([[a.x, a.y], [a.x, yEsc], [rail, yEsc], [rail, yApp], [b.x, yApp], [b.x, b.y]]);
      ch.h.forEach(function (busY) {
        add([[a.x, a.y], [a.x, yEsc], [rail, yEsc], [rail, busY], [b.x, busY], [b.x, b.y]]);
      });
    });
    // 4) Top/bottom rails for side links
    var hRails = [];
    if (ch.h.length) {
      hRails.push(ch.h[0], ch.h[ch.h.length - 1]);
    }
    hRails.forEach(function (hRail) {
      add([[a.x, a.y], [a.x, hRail], [b.x, hRail], [b.x, b.y]]);
      ch.v.forEach(function (alley) {
        add([[a.x, a.y], [a.x, hRail], [alley, hRail], [alley, b.y], [b.x, b.y]]);
        add([[a.x, a.y], [alley, a.y], [alley, hRail], [b.x, hRail], [b.x, b.y]]);
      });
    });

    var best = pickBest(cands, rects, fromId, toId);
    if (best) return best;
    // last resort: mid-Y bent (may still clip; z-order keeps it visible)
    var midY = Math.round((a.y + b.y) / 2);
    return simplifyPts([[a.x, a.y], [a.x, midY], [b.x, midY], [b.x, b.y]]);
  }
  function bentPath(x1, y1, x2, y2, fromPort) {
    var midY = Math.round((y1 + y2) / 2);
    if (fromPort === 'right' || fromPort === 'left') {
      var midX = Math.round((x1 + x2) / 2);
      return ptsToPath([[x1, y1], [midX, y1], [midX, y2], [x2, y2]]);
    }
    return ptsToPath([[x1, y1], [x1, midY], [x2, midY], [x2, y2]]);
  }

'''

BUNDLE_START = "  function buildBundledRoutes() {"
BUNDLE_END = "  function labelPos(pts) {"

NEW_BUNDLE = r'''  function buildBundledRoutes() {
    var rects = getObstacles();
    var routes = [];
    state.edges.forEach(function (e) {
      if (!state.nodes[e.from] || !state.nodes[e.to]) return;
      var ports = edgePorts(e.from, e.to);
      var a = nodePort(e.from, ports.from);
      var b = nodePort(e.to, ports.to);
      var pts = routeInGutters(a, b, ports.from, ports.to, e.from, e.to, rects);
      var kind = (ports.from === 'bottom' && ports.to === 'top') ? 'down'
              : (ports.from === 'top' && ports.to === 'bottom') ? 'up' : 'side';
      var bus = kind === 'side'
        ? (pts.length >= 3 ? pts[Math.min(2, pts.length - 1)][0] : (a.x + b.x) / 2)
        : (pts.length >= 3 ? pts[Math.min(2, pts.length - 1)][1] : (a.y + b.y) / 2);
      routes.push({
        e: e, ports: ports, a: a, b: b, kind: kind,
        pts: pts, hitPts: pts, bus: bus,
        midX: (a.x + b.x) / 2, midY: (a.y + b.y) / 2
      });
    });

    var parent = routes.map(function (_, i) { return i; });
    function find(i) { return parent[i] === i ? i : (parent[i] = find(parent[i])); }
    function unite(i, j) { i = find(i); j = find(j); if (i !== j) parent[j] = i; }

    for (var i = 0; i < routes.length; i++) {
      for (var j = i + 1; j < routes.length; j++) {
        var A = routes[i], B = routes[j];
        if (A.kind !== B.kind) continue;
        if (A.kind === 'down' || A.kind === 'up') {
          if (Math.abs(A.bus - B.bus) <= MERGE_Y && Math.abs(A.midX - B.midX) <= MERGE_X * 2.2) unite(i, j);
          if (A.kind === 'down' && A.e.from === B.e.from) unite(i, j);
          if (A.kind === 'up' && A.e.to === B.e.to) unite(i, j);
        } else if (Math.abs(A.bus - B.bus) <= MERGE_X && Math.abs(A.midY - B.midY) <= MERGE_Y * 2) {
          unite(i, j);
        }
      }
    }

    var groups = {};
    routes.forEach(function (r, idx) {
      var root = find(idx);
      (groups[root] = groups[root] || []).push(idx);
    });

    var trunks = [];
    var ch = buildChannels(rects);
    Object.keys(groups).forEach(function (root) {
      var idxs = groups[root];
      if (idxs.length < 2) {
        idxs.forEach(function (i) { routes[i].bundled = false; });
        return;
      }
      var rs = idxs.map(function (i) { return routes[i]; });
      var kind = rs[0].kind;

      if (kind === 'down' || kind === 'up') {
        var allX = [];
        rs.forEach(function (r) { allX.push(r.a.x, r.b.x); });
        var minX = Math.min.apply(null, allX), maxX = Math.max.apply(null, allX);
        var yA = rs.reduce(function (s, r) { return s + r.a.y; }, 0) / rs.length;
        var yB = rs.reduce(function (s, r) { return s + r.b.y; }, 0) / rs.length;
        // pick a bus Y that is clear across the whole span AND clear stubs for every member
        var busY = null;
        var hCands = ch.h.slice().sort(function (p, q) {
          return Math.abs(p - (yA + yB) / 2) - Math.abs(q - (yA + yB) / 2);
        });
        for (var hi = 0; hi < hCands.length; hi++) {
          var y = hCands[hi];
          if (hHits(y, minX, maxX, rects, {})) continue;
          var okAll = true;
          for (var ri = 0; ri < rs.length; ri++) {
            var rr = rs[ri];
            var ignF = {}, ignT = {};
            ignF[rr.e.from] = true; ignT[rr.e.to] = true;
            if (vHits(rr.a.x, rr.a.y, y, rects, ignF) || vHits(rr.b.x, y, rr.b.y, rects, ignT)) {
              okAll = false; break;
            }
          }
          if (okAll) { busY = y; break; }
        }
        if (busY == null) {
          // cannot safely bundle — keep individual gutter routes
          rs.forEach(function (r) { r.bundled = false; });
          return;
        }
        trunks.push({
          key: 'g' + root,
          d: ptsToPath([[minX, busY], [maxX, busY]]),
          edgeIds: rs.map(function (r) { return r.e.id; }),
          busY: busY
        });
        rs.forEach(function (r) {
          var use = simplifyPts([
            [r.a.x, r.a.y], [r.a.x, busY], [r.b.x, busY], [r.b.x, r.b.y]
          ]);
          r.bundled = true;
          r.bus = busY;
          r.hitPts = use;
          r.pts = simplifyPts([[r.a.x, r.a.y], [r.a.x, busY]]);
          r.ptsEnd = simplifyPts([[r.b.x, busY], [r.b.x, r.b.y]]);
        });
      } else {
        var allY = [];
        rs.forEach(function (r) { allY.push(r.a.y, r.b.y); });
        var minY = Math.min.apply(null, allY), maxY = Math.max.apply(null, allY);
        var xA = rs.reduce(function (s, r) { return s + r.a.x; }, 0) / rs.length;
        var xB = rs.reduce(function (s, r) { return s + r.b.x; }, 0) / rs.length;
        var alleyX = null;
        var vCands = ch.v.slice().sort(function (p, q) {
          return Math.abs(p - (xA + xB) / 2) - Math.abs(q - (xA + xB) / 2);
        });
        for (var vi = 0; vi < vCands.length; vi++) {
          var x = vCands[vi];
          if (vHits(x, minY, maxY, rects, {})) continue;
          var okS = true;
          for (var sj = 0; sj < rs.length; sj++) {
            var sr = rs[sj];
            var igF2 = {}, igT2 = {};
            igF2[sr.e.from] = true; igT2[sr.e.to] = true;
            if (hHits(sr.a.y, sr.a.x, x, rects, igF2) || hHits(sr.b.y, x, sr.b.x, rects, igT2)) {
              okS = false; break;
            }
          }
          if (okS) { alleyX = x; break; }
        }
        if (alleyX == null) {
          rs.forEach(function (r) { r.bundled = false; });
          return;
        }
        trunks.push({
          key: 'g' + root,
          d: ptsToPath([[alleyX, minY], [alleyX, maxY]]),
          edgeIds: rs.map(function (r) { return r.e.id; }),
          alleyX: alleyX
        });
        rs.forEach(function (r) {
          var use = simplifyPts([
            [r.a.x, r.a.y], [alleyX, r.a.y], [alleyX, r.b.y], [r.b.x, r.b.y]
          ]);
          r.bundled = true;
          r.bus = alleyX;
          r.hitPts = use;
          r.pts = simplifyPts([[r.a.x, r.a.y], [alleyX, r.a.y]]);
          r.ptsEnd = simplifyPts([[alleyX, r.b.y], [r.b.x, r.b.y]]);
        });
      }
    });

    return { routes: routes, trunks: trunks };
  }
'''

RENDER_OLD = """  function renderEdges() {
    var built = buildBundledRoutes();

    // bottom layer (under cards): non-selected
    var html = edgesSvg.querySelector('defs').outerHTML;
    html += paintRoutes({ querySelector: function () { return null; } }, built.routes, built.trunks, false).replace(/^/, '');
    // paintRoutes without defs — rebuild manually
    html = edgesSvg.querySelector('defs').outerHTML;
    built.trunks.forEach(function (t) {
      if (state.selectedEdge && t.edgeIds.indexOf(state.selectedEdge) >= 0) return;
      html += '<path class="edge bundle-trunk" data-trunk="' + escapeHtml(t.key) + '" d="' + t.d + '"></path>';
    });
    built.routes.forEach(function (r) {
      if (isEdgeSelected(r.e.id)) return;
      var hitPts = r.hitPts || r.pts;
      html += '<path class="edge-hit" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"></path>';
      html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.pts) + '" style="marker-end:url(#arrowHead)"></path>';
      if (r.ptsEnd) html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.ptsEnd) + '" style="marker-end:url(#arrowHead)"></path>';
      if (r.e.label) {
        var lp = labelPos(hitPts);
        html += '<text x="' + lp.x + '" y="' + lp.y + '" fill="#8aa0b8" font-size="11" text-anchor="middle">' + escapeHtml(r.e.label) + '</text>';
      }
    });
    if (link && link.x2 != null) {
      html += '<path class="temp" d="' + bentPath(link.x1, link.y1, link.x2, link.y2, link.fromPort || 'bottom') + '"></path>';
    }
    edgesSvg.innerHTML = html;
    bindEdgeHits(edgesSvg);

    // top layer (above cards): selected full path only
    var top = edgesTopSvg.querySelector('defs').outerHTML;
    if (state.selectedEdge) {
      built.trunks.forEach(function (t) {
        if (t.edgeIds.indexOf(state.selectedEdge) < 0) return;
        top += '<path class="edge bundle-trunk selected raised" d="' + t.d + '"></path>';
      });
      built.routes.forEach(function (r) {
        if (!isEdgeSelected(r.e.id)) return;
        var hitPts = r.hitPts || r.pts;
        top += '<path class="edge-hit" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"></path>';
        top += '<path class="edge selected raised" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '" style="marker-end:url(#arrowHeadHotTop)"></path>';
        if (r.e.label) {
          var lp2 = labelPos(hitPts);
          top += '<text x="' + lp2.x + '" y="' + lp2.y + '" font-size="11" text-anchor="middle">' + escapeHtml(r.e.label) + '</text>';
        }
      });
    }
    edgesTopSvg.innerHTML = top;
    bindEdgeHits(edgesTopSvg);
  }"""

RENDER_NEW = """  function renderEdges() {
    var built = buildBundledRoutes();

    // main layer (above cards): full visible orthogonal routes
    var html = edgesSvg.querySelector('defs').outerHTML;
    built.trunks.forEach(function (t) {
      if (state.selectedEdge && t.edgeIds.indexOf(state.selectedEdge) >= 0) return;
      html += '<path class="edge bundle-trunk" data-trunk="' + escapeHtml(t.key) + '" d="' + t.d + '"></path>';
    });
    built.routes.forEach(function (r) {
      if (isEdgeSelected(r.e.id)) return;
      var hitPts = r.hitPts || r.pts;
      html += '<path class="edge-hit" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"></path>';
      if (r.bundled) {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.pts) + '"></path>';
        if (r.ptsEnd) html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.ptsEnd) + '" style="marker-end:url(#arrowHead)"></path>';
      } else {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '" style="marker-end:url(#arrowHead)"></path>';
      }
      if (r.e.label) {
        var lp = labelPos(hitPts);
        html += '<text x="' + lp.x + '" y="' + lp.y + '" fill="#8aa0b8" font-size="11" text-anchor="middle">' + escapeHtml(r.e.label) + '</text>';
      }
    });
    if (link && link.x2 != null) {
      html += '<path class="temp" d="' + bentPath(link.x1, link.y1, link.x2, link.y2, link.fromPort || 'bottom') + '"></path>';
    }
    edgesSvg.innerHTML = html;
    bindEdgeHits(edgesSvg);

    // top layer: selected full path only (bright)
    var top = edgesTopSvg.querySelector('defs').outerHTML;
    if (state.selectedEdge) {
      built.trunks.forEach(function (t) {
        if (t.edgeIds.indexOf(state.selectedEdge) < 0) return;
        top += '<path class="edge bundle-trunk selected raised" d="' + t.d + '"></path>';
      });
      built.routes.forEach(function (r) {
        if (!isEdgeSelected(r.e.id)) return;
        var hitPts = r.hitPts || r.pts;
        top += '<path class="edge-hit" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '"></path>';
        top += '<path class="edge selected raised" data-id="' + r.e.id + '" d="' + ptsToPath(hitPts) + '" style="marker-end:url(#arrowHeadHotTop)"></path>';
        if (r.e.label) {
          var lp2 = labelPos(hitPts);
          top += '<text x="' + lp2.x + '" y="' + lp2.y + '" font-size="11" text-anchor="middle">' + escapeHtml(r.e.label) + '</text>';
        }
      });
    }
    edgesTopSvg.innerHTML = top;
    bindEdgeHits(edgesTopSvg);
  }"""


def replace_between(html: str, start: str, end: str, new_mid: str) -> str:
    i = html.find(start)
    j = html.find(end)
    if i < 0 or j < 0 or j <= i:
        raise SystemExit(f"markers not found: start={i} end={j}")
    return html[:i] + new_mid + html[j:]


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")

    if CSS_OLD not in html:
        raise SystemExit("CSS block not found")
    html = html.replace(CSS_OLD, CSS_NEW, 1)

    if HELP_OLD not in html:
        raise SystemExit("help text not found")
    html = html.replace(HELP_OLD, HELP_NEW, 1)

    html = html.replace("var CONTENT_REV = '20260810g';", "var CONTENT_REV = '20260810h';", 1)

    # widen gutters so channels exist
    html = html.replace(
        "var NODE_W = 360, GAP_X = 100, GAP_Y = 120;",
        "var NODE_W = 360, GAP_X = 140, GAP_Y = 180;",
        1,
    )
    html = html.replace("var GAP_Y = 140;", "var GAP_Y = 200;", 1)

    html = replace_between(html, ROUTE_START, ROUTE_END, NEW_ROUTE)
    html = replace_between(html, BUNDLE_START, BUNDLE_END, NEW_BUNDLE)

    if RENDER_OLD not in html:
        # try softer: just kill paintRoutes line if present
        if "paintRoutes" in html:
            html = html.replace(
                "    html += paintRoutes({ querySelector: function () { return null; } }, built.routes, built.trunks, false).replace(/^/, '');\n"
                "    // paintRoutes without defs — rebuild manually\n"
                "    html = edgesSvg.querySelector('defs').outerHTML;\n",
                "",
                1,
            )
            print("WARN: partial paintRoutes strip only")
        else:
            raise SystemExit("renderEdges block not found")
    else:
        html = html.replace(RENDER_OLD, RENDER_NEW, 1)

    # obstacle pad a bit smaller so gutters are usable
    html = html.replace("var PAD = 18;", "var PAD = 10;", 1)

    BOARD.write_text(html, encoding="utf-8")

    s = html.split("<script>")[1].split("</script>")[0]
    print("CONTENT_REV", "20260810h" in html)
    print("paintRoutes gone", "paintRoutes" not in s)
    print("z-index edges 3", "z-index: 3; pointer-events: none; overflow: visible; }" in html)
    print("scorePath", "function scorePath" in s)
    print("braces", s.count("{"), s.count("}"))


if __name__ == "__main__":
    main()
