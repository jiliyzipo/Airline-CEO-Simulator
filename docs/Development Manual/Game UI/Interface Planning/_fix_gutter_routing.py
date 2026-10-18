# -*- coding: utf-8 -*-
"""Fix edge routing: gutter-only channels, edges under cards, selected on top layer."""
from __future__ import annotations

import re
from pathlib import Path

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")

html = BOARD.read_text(encoding="utf-8")

# --- CSS z-order ---
old_css = """    #nodes { position: absolute; left: 0; top: 0; z-index: 1; }
    #edges { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 3; pointer-events: none; overflow: visible; }"""

new_css = """    #nodes { position: absolute; left: 0; top: 0; z-index: 2; }
    #edges { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 1; pointer-events: none; overflow: visible; }
    #edgesTop { position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; z-index: 4; pointer-events: none; overflow: visible; }
    #edgesTop path.edge-hit {
      fill: none; stroke: rgba(255,224,102,0.01); stroke-width: 18;
      pointer-events: stroke; cursor: pointer;
    }
    #edgesTop path.edge {
      fill: none; stroke: #ffe066; stroke-width: 3.6;
      filter: drop-shadow(0 0 6px rgba(255,224,102,0.95));
      marker-end: url(#arrowHeadHotTop); pointer-events: none;
    }
    #edgesTop path.edge.bundle-trunk {
      stroke: #ffe066; stroke-width: 4; marker-end: none;
      filter: drop-shadow(0 0 6px rgba(255,224,102,0.95));
    }
    #edgesTop text { pointer-events: none; fill: #ffe066; font-weight: 700; }"""

if old_css not in html:
    raise SystemExit("CSS block not found")
html = html.replace(old_css, new_css, 1)

# --- HTML structure: edges under nodes, edgesTop above ---
old_world = """    <div id="world">
      <div id="nodes"></div>
      <svg id="edges">
        <defs>
          <marker id="arrowHead" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#4da6ff"></path></marker>
          <marker id="arrowHeadTemp" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe9a0"></path></marker>
          <marker id="arrowHeadHot" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe066"></path></marker>
        </defs>
      </svg>
    </div>"""

new_world = """    <div id="world">
      <svg id="edges">
        <defs>
          <marker id="arrowHead" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#4da6ff"></path></marker>
          <marker id="arrowHeadTemp" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe9a0"></path></marker>
          <marker id="arrowHeadHot" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe066"></path></marker>
        </defs>
      </svg>
      <div id="nodes"></div>
      <svg id="edgesTop">
        <defs>
          <marker id="arrowHeadHotTop" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe066"></path></marker>
        </defs>
      </svg>
    </div>"""

if old_world not in html:
    raise SystemExit("world HTML not found")
html = html.replace(old_world, new_world, 1)

# --- JS: edgesTop ref ---
html = html.replace(
    "  var edgesSvg = document.getElementById('edges');",
    "  var edgesSvg = document.getElementById('edges');\n  var edgesTopSvg = document.getElementById('edgesTop');",
    1,
)

# Replace from getObstacles through renderEdges (keep escapeHtml after)
start = html.find("  function getObstacles() {")
end = html.find("  function escapeHtml(s) {")
if start < 0 or end < 0:
    raise SystemExit("route block markers not found: %s %s" % (start, end))

NEW_ROUTING = r'''  function getObstacles() {
    var rects = [];
    var PAD = 18;
    getCatalog().forEach(function (item) {
      var n = state.nodes[item.id];
      if (!n) return;
      var el = nodesRoot.querySelector('.node[data-id="' + item.id + '"]');
      var h = el ? Math.max(el.offsetHeight, 180) : estimateH(item);
      rects.push({
        id: item.id,
        x: n.x - PAD, y: n.y - PAD,
        r: n.x + 360 + PAD, b: n.y + h + PAD,
        cx: n.x + 180, cy: n.y + h / 2
      });
    });
    return rects;
  }
  function hHits(y, x1, x2, rects, ignore) {
    var lo = Math.min(x1, x2), hi = Math.max(x1, x2);
    for (var i = 0; i < rects.length; i++) {
      var r = rects[i];
      if (ignore && ignore[r.id]) continue;
      if (y > r.y && y < r.b && hi > r.x && lo < r.r) return true;
    }
    return false;
  }
  function vHits(x, y1, y2, rects, ignore) {
    var lo = Math.min(y1, y2), hi = Math.max(y1, y2);
    for (var i = 0; i < rects.length; i++) {
      var r = rects[i];
      if (ignore && ignore[r.id]) continue;
      if (x > r.x && x < r.r && hi > r.y && lo < r.b) return true;
    }
    return false;
  }
  /** Build free horizontal / vertical channel coordinates from gaps between cards. */
  function buildChannels(rects) {
    var hCh = [], vCh = [];
    if (!rects.length) return { h: [200], v: [200] };

    // Horizontal channels: midpoints of Y-gaps between occupied bands
    var bands = rects.map(function (r) { return { a: r.y, b: r.b }; })
      .sort(function (p, q) { return p.a - q.a; });
    var merged = [];
    bands.forEach(function (b) {
      if (!merged.length || b.a > merged[merged.length - 1].b + 8) merged.push({ a: b.a, b: b.b });
      else merged[merged.length - 1].b = Math.max(merged[merged.length - 1].b, b.b);
    });
    var minY = merged[0].a - 80;
    var prev = minY;
    merged.forEach(function (m) {
      if (m.a - prev > 24) hCh.push(Math.round((prev + m.a) / 2));
      prev = m.b;
    });
    hCh.push(Math.round(prev + 60));

    // Vertical channels: midpoints of X-gaps
    var xbands = rects.map(function (r) { return { a: r.x, b: r.r }; })
      .sort(function (p, q) { return p.a - q.a; });
    var xmerged = [];
    xbands.forEach(function (b) {
      if (!xmerged.length || b.a > xmerged[xmerged.length - 1].b + 8) xmerged.push({ a: b.a, b: b.b });
      else xmerged[xmerged.length - 1].b = Math.max(xmerged[xmerged.length - 1].b, b.b);
    });
    var minX = xmerged[0].a - 80;
    var prevx = minX;
    xmerged.forEach(function (m) {
      if (m.a - prevx > 24) vCh.push(Math.round((prevx + m.a) / 2));
      prevx = m.b;
    });
    vCh.push(Math.round(prevx + 60));

    // also add outer rails
    hCh.unshift(Math.round(minY - 40));
    vCh.unshift(Math.round(minX - 40));
    return { h: hCh, v: vCh };
  }
  function nearestChannel(val, channels) {
    var best = channels[0], bd = Math.abs(val - best);
    for (var i = 1; i < channels.length; i++) {
      var d = Math.abs(val - channels[i]);
      if (d < bd) { bd = d; best = channels[i]; }
    }
    return best;
  }
  function pickHChannelBetween(yA, yB, x1, x2, channels, rects, ignore) {
    var lo = Math.min(yA, yB), hi = Math.max(yA, yB);
    var mid = (lo + hi) / 2;
    // prefer channels strictly between the two ports
    var cands = channels.filter(function (y) { return y > lo + 6 && y < hi - 6; });
    if (!cands.length) cands = channels.slice();
    cands.sort(function (a, b) { return Math.abs(a - mid) - Math.abs(b - mid); });
    for (var i = 0; i < cands.length; i++) {
      if (!hHits(cands[i], x1, x2, rects, ignore)) return cands[i];
    }
    // fallback: any channel that clears the span
    for (var j = 0; j < channels.length; j++) {
      if (!hHits(channels[j], x1, x2, rects, ignore)) return channels[j];
    }
    return nearestChannel(mid, channels);
  }
  function pickVChannelBetween(xA, xB, y1, y2, channels, rects, ignore) {
    var lo = Math.min(xA, xB), hi = Math.max(xA, xB);
    var mid = (lo + hi) / 2;
    var cands = channels.filter(function (x) { return x > lo + 6 && x < hi - 6; });
    if (!cands.length) cands = channels.slice();
    cands.sort(function (a, b) { return Math.abs(a - mid) - Math.abs(b - mid); });
    for (var i = 0; i < cands.length; i++) {
      if (!vHits(cands[i], y1, y2, rects, ignore)) return cands[i];
    }
    for (var j = 0; j < channels.length; j++) {
      if (!vHits(channels[j], y1, y2, rects, ignore)) return channels[j];
    }
    return nearestChannel(mid, channels);
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
  /**
   * Route ONLY along free channels between cards.
   * Pattern: port → nearest channel → travel on channels → port.
   * Never draws a long segment across a card body.
   */
  function routeInGutters(a, b, fromPort, toPort, fromId, toId, rects) {
    var ignore = {};
    // do NOT ignore endpoint cards for long segments — only allow leaving the port
    var ch = buildChannels(rects);
    var pts;

    if ((fromPort === 'bottom' && toPort === 'top') || (fromPort === 'top' && toPort === 'bottom')) {
      var busY = pickHChannelBetween(a.y, b.y, a.x, b.x, ch.h, rects, ignore);
      // verticals at port X must reach bus without crossing OTHER cards
      var x1 = a.x, x2 = b.x;
      if (vHits(x1, a.y, busY, rects, (function () { var ig = {}; ig[fromId] = true; return ig; })())) {
        x1 = pickVChannelBetween(a.x - 50, a.x + 50, a.y, busY, ch.v, rects, ignore);
      }
      if (vHits(x2, busY, b.y, rects, (function () { var ig = {}; ig[toId] = true; return ig; })())) {
        x2 = pickVChannelBetween(b.x - 50, b.x + 50, busY, b.y, ch.v, rects, ignore);
      }
      // re-pick bus if horizontal still hits
      if (hHits(busY, x1, x2, rects, ignore)) {
        busY = pickHChannelBetween(a.y, b.y, x1, x2, ch.h, rects, ignore);
      }
      // if still blocked, go around via outer vertical rail
      if (hHits(busY, x1, x2, rects, ignore) || vHits(x1, a.y, busY, rects, (function(){var ig={};ig[fromId]=1;return ig;})())) {
        var rail = ch.v[0]; // leftmost outer
        var bus2 = pickHChannelBetween(a.y, b.y, rail, Math.max(a.x, b.x), ch.h, rects, ignore);
        pts = [[a.x, a.y], [a.x, bus2], [rail, bus2], [rail, busY], [b.x, busY], [b.x, b.y]];
        // try rightmost if left still bad
        if (hHits(bus2, a.x, rail, rects, ignore)) {
          rail = ch.v[ch.v.length - 1];
          bus2 = pickHChannelBetween(a.y, b.y, Math.min(a.x, b.x), rail, ch.h, rects, ignore);
          pts = [[a.x, a.y], [a.x, bus2], [rail, bus2], [rail, busY], [b.x, busY], [b.x, b.y]];
        }
      } else {
        pts = [[a.x, a.y], [x1, a.y], [x1, busY], [x2, busY], [x2, b.y], [b.x, b.y]];
      }
    } else {
      // side: use vertical alley channel
      var alley = pickVChannelBetween(a.x, b.x, a.y, b.y, ch.v, rects, ignore);
      var y1 = a.y, y2 = b.y;
      if (hHits(y1, a.x, alley, rects, (function(){var ig={};ig[fromId]=1;return ig;})())) {
        y1 = pickHChannelBetween(a.y - 40, a.y + 40, a.x, alley, ch.h, rects, ignore);
      }
      if (hHits(y2, alley, b.x, rects, (function(){var ig={};ig[toId]=1;return ig;})())) {
        y2 = pickHChannelBetween(b.y - 40, b.y + 40, alley, b.x, ch.h, rects, ignore);
      }
      if (vHits(alley, y1, y2, rects, ignore)) {
        alley = pickVChannelBetween(a.x, b.x, y1, y2, ch.v, rects, ignore);
      }
      // outer bypass
      if (vHits(alley, y1, y2, rects, ignore)) {
        var hRail = ch.h[0];
        alley = pickVChannelBetween(a.x, b.x, hRail, Math.max(a.y, b.y), ch.v, rects, ignore);
        pts = [[a.x, a.y], [a.x, hRail], [alley, hRail], [alley, b.y], [b.x, b.y]];
      } else {
        pts = [[a.x, a.y], [a.x, y1], [alley, y1], [alley, y2], [b.x, y2], [b.x, b.y]];
      }
    }
    return simplifyPts(pts);
  }
  function bentPath(x1, y1, x2, y2, fromPort) {
    var midY = Math.round((y1 + y2) / 2);
    if (fromPort === 'right' || fromPort === 'left') {
      var midX = Math.round((x1 + x2) / 2);
      return ptsToPath([[x1, y1], [midX, y1], [midX, y2], [x2, y2]]);
    }
    return ptsToPath([[x1, y1], [x1, midY], [x2, midY], [x2, y2]]);
  }

  var MERGE_Y = 180;
  var MERGE_X = 300;

  function buildBundledRoutes() {
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
        var busY = pickHChannelBetween(yA, yB, minX, maxX, ch.h, rects, {});
        trunks.push({
          key: 'g' + root,
          d: ptsToPath([[minX, busY], [maxX, busY]]),
          edgeIds: rs.map(function (r) { return r.e.id; }),
          busY: busY
        });
        rs.forEach(function (r) {
          var full = routeInGutters(r.a, r.b, r.ports.from, r.ports.to, r.e.from, r.e.to, rects);
          // force shared bus Y into path when possible
          var forced = simplifyPts([
            [r.a.x, r.a.y], [r.a.x, busY], [r.b.x, busY], [r.b.x, r.b.y]
          ]);
          // if forced horizontal clear, use it; else keep full gutter route
          var use = !hHits(busY, r.a.x, r.b.x, rects, {}) ? forced : full;
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
        var alleyX = pickVChannelBetween(xA, xB, minY, maxY, ch.v, rects, {});
        trunks.push({
          key: 'g' + root,
          d: ptsToPath([[alleyX, minY], [alleyX, maxY]]),
          edgeIds: rs.map(function (r) { return r.e.id; }),
          alleyX: alleyX
        });
        rs.forEach(function (r) {
          var full = routeInGutters(r.a, r.b, r.ports.from, r.ports.to, r.e.from, r.e.to, rects);
          var forced = simplifyPts([
            [r.a.x, r.a.y], [alleyX, r.a.y], [alleyX, r.b.y], [r.b.x, r.b.y]
          ]);
          var use = !vHits(alleyX, r.a.y, r.b.y, rects, {}) ? forced : full;
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
  function labelPos(pts) {
    if (!pts || pts.length < 2) return { x: 0, y: 0 };
    var mid = Math.floor(pts.length / 2);
    var a = pts[Math.max(0, mid - 1)], b = pts[Math.min(pts.length - 1, mid)];
    return { x: (a[0] + b[0]) / 2, y: (a[1] + b[1]) / 2 - 8 };
  }
  function isEdgeSelected(id) {
    return state.selectedEdge === id;
  }
  function toggleEdgeSelect(id) {
    if (state.selectedEdge === id) {
      state.selectedEdge = null;
      setStatus('已取消选中连线');
    } else {
      state.selectedEdge = id;
      setStatus('已选中连线（置顶完整路径 · 再点取消 · Delete 删除）');
    }
    renderEdges();
  }
  function paintRoutes(svg, routes, trunks, selectedOnly) {
    var html = svg.querySelector('defs') ? svg.querySelector('defs').outerHTML : '';
    var selId = state.selectedEdge;

    trunks.forEach(function (t) {
      var isSelGroup = selId && t.edgeIds.indexOf(selId) >= 0;
      if (selectedOnly && !isSelGroup) return;
      if (!selectedOnly && isSelGroup) return;
      html += '<path class="edge bundle-trunk' + (selectedOnly ? ' selected raised' : '') + '" data-trunk="' + escapeHtml(t.key) + '" d="' + t.d + '"></path>';
    });

    routes.forEach(function (r) {
      var sel = isEdgeSelected(r.e.id);
      if (selectedOnly && !sel) return;
      if (!selectedOnly && sel) return;

      var hitPts = r.hitPts || r.pts;
      var visPts = selectedOnly ? hitPts : r.pts;
      var dHit = ptsToPath(hitPts);
      var dVis = ptsToPath(visPts);
      var marker = selectedOnly ? 'url(#arrowHeadHotTop)' : 'url(#arrowHead)';
      if (!selectedOnly) marker = 'url(#arrowHead)';
      html += '<path class="edge-hit" data-id="' + r.e.id + '" d="' + dHit + '"></path>';
      html += '<path class="edge' + (selectedOnly ? ' selected raised' : '') + '" data-id="' + r.e.id + '" d="' + dVis + '" style="marker-end:' + (selectedOnly ? 'url(#arrowHeadHotTop)' : 'url(#arrowHead)') + '"></path>';
      if (r.ptsEnd && !selectedOnly) {
        html += '<path class="edge" data-id="' + r.e.id + '" d="' + ptsToPath(r.ptsEnd) + '" style="marker-end:url(#arrowHead)"></path>';
      }
      if (r.e.label) {
        var lp = labelPos(hitPts);
        html += '<text x="' + lp.x + '" y="' + lp.y + '" fill="' + (selectedOnly ? '#ffe066' : '#8aa0b8') + '" font-size="11" text-anchor="middle">' + escapeHtml(r.e.label) + '</text>';
      }
    });
    return html;
  }
  function bindEdgeHits(svg) {
    svg.querySelectorAll('path.edge-hit').forEach(function (p) {
      p.addEventListener('mousedown', function (ev) {
        ev.stopPropagation();
        ev.preventDefault();
        toggleEdgeSelect(p.getAttribute('data-id'));
      });
    });
  }
  function renderEdges() {
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
  }

'''

html = html[:start] + NEW_ROUTING + html[end:]

# help text
html = html.replace(
    "· 连线只走卡片之间的空白槽位（绕障折线），相近连线自动合干<br>\n    · 点击连线置顶显示完整路径（亮黄）；再点同一条取消<br>",
    "· 普通连线在卡片下层、只走卡片间空隙；相近线合干<br>\n    · 点击连线→最上层亮黄完整路径；再点取消<br>",
)

# viewport mousedown may reference edgesTop
html = html.replace(
    "if (ev.target !== viewport && ev.target !== world && ev.target !== edgesSvg && ev.target !== nodesRoot) return;",
    "if (ev.target !== viewport && ev.target !== world && ev.target !== edgesSvg && ev.target !== edgesTopSvg && ev.target !== nodesRoot) return;",
    1,
)

BOARD.write_text(html, encoding="utf-8")

# verify
s = html.split("<script>")[1].split("</script>")[0]
print("braces", s.count("{"), s.count("}"))
print("edgesTop", "edgesTopSvg" in s)
print("buildChannels", "buildChannels" in s)
print("ok")
