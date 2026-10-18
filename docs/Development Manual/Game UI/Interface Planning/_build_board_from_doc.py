# -*- coding: utf-8 -*-
"""Build full-content UI whiteboard HTML from Airline_CEO__UI交互图 V1.0.html."""
from __future__ import annotations

import html as html_lib
import json
import re
from pathlib import Path

DOC = Path(__file__).with_name("Airline_CEO__UI交互图 V1.0.html")
BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")
OUT_JSON = Path(__file__).with_name("_board_catalog.json")


def strip(s: str) -> str:
    s = re.sub(r"<br\s*/?>", "\n", s, flags=re.I)
    s = re.sub(r"</?(strong|b|em|i|span|code)[^>]*>", "", s, flags=re.I)
    s = re.sub(r"<[^>]+>", "", s)
    return html_lib.unescape(s).strip()


def extract_card_chunk(body: str, start: int) -> tuple[str, str, int]:
    m = re.match(r'<div class="card([^"]*)">', body[start:])
    if not m:
        return "", "", start
    i = start + m.end()
    depth = 1
    while i < len(body) and depth:
        next_open = body.find("<div", i)
        next_close = body.find("</div>", i)
        if next_close < 0:
            break
        if next_open >= 0 and next_open < next_close:
            depth += 1
            i = next_open + 4
        else:
            depth -= 1
            i = next_close + len("</div>")
    return m.group(1), body[start + m.end() : i - len("</div>")], i


def parse_page_meta(text: str) -> list[dict]:
    m = re.search(r"var PAGE_META = (\[[\s\S]*?\n  \]);", text)
    if not m:
        return []
    blocks = re.findall(
        r"\{\s*match:\s*'([^']+)',\s*level:\s*'([^']+)',\s*clicks:\s*\[([\s\S]*?)\]\s*\}",
        m.group(1),
    )
    out = []
    for match, level, clicks_raw in blocks:
        clicks = re.findall(r"\['([^']*)',\s*'([^']*)'\]", clicks_raw)
        out.append({"match": match, "level": level, "clicks": [list(c) for c in clicks]})
    return out


def parse_cards(text: str) -> list[dict]:
    cards = []
    for part in re.split(r"<section", text)[1:]:
        body = part.split("</section>", 1)[0]
        # skip architecture-only sections that have no real cards content? keep all cards
        pos = 0
        while True:
            start = body.find('<div class="card', pos)
            if start < 0:
                break
            classes, inner, end = extract_card_chunk(body, start)
            pos = end
            nm = re.search(r'<div class="name">(.*?)</div>', inner, re.S)
            if not nm:
                continue
            name = strip(nm.group(1))
            meta_m = re.search(r'<div class="meta">(.*?)</div>', inner, re.S)
            meta = strip(meta_m.group(1)) if meta_m else ""
            badge_m = re.search(r'<span class="badge[^"]*">(.*?)</span>', inner, re.S)
            badge = strip(badge_m.group(1)) if badge_m else ""
            ul_m = re.search(r"<ul[^>]*>(.*?)</ul>", inner, re.S)
            items = (
                [strip(x) for x in re.findall(r"<li>(.*?)</li>", ul_m.group(1), re.S)]
                if ul_m
                else []
            )
            for ov in re.findall(r'class="ov">([^<]+)</div>', inner):
                items.append(strip(ov))
            for d in re.findall(r"<div>([^<]+)</div>", inner):
                t = strip(d)
                if t and len(t) < 40 and any(
                    k in t
                    for k in (
                        "LOGO",
                        "CEO",
                        "主页",
                        "收件箱",
                        "机队",
                        "航线",
                        "运营",
                        "市场",
                        "财务",
                        "战略",
                        "机务",
                        "人力",
                        "安全",
                        "董事会",
                        "消息",
                        "搜索",
                        "日期",
                        "暂停",
                        "继续",
                        "世界",
                        "航线发",
                        "枢纽",
                        "蓝色",
                        "AO",
                        "Tab",
                    )
                ):
                    items.append(t)
            seen, uniq = set(), []
            for x in items:
                if x and x not in seen:
                    seen.add(x)
                    uniq.append(x)
            entry_m = re.search(r'<div class="entry">(.*?)</div>', inner, re.S)
            entry = strip(entry_m.group(1)) if entry_m else ""
            kind = (
                "new"
                if "new" in classes
                else ("popup" if "popup" in classes else ("shell" if "shell" in classes else "core"))
            )
            # skip section-五 summary-only shell with only jump bullets and no name match? keep
            if "返回路径" in body and "L2/L3" in "".join(uniq):
                continue
            cards.append(
                {
                    "name": name,
                    "meta": meta,
                    "badge": badge,
                    "items": uniq,
                    "entry": entry,
                    "kind": kind,
                }
            )
    return cards


def slug(name: str, i: int) -> str:
    base = re.sub(r"[^\w\u4e00-\u9fff]+", "_", name).strip("_")[:28] or f"page_{i}"
    return f"n{i}_{base}"


def assign_level(card: dict, meta_list: list[dict]) -> str:
    name = card["name"]
    for m in meta_list:
        if m["match"] in name:
            return m["level"]
    if any(k in name for k in ("主菜单", "玩家资料", "公司注册", "董事会组建", "年度目标", "主界面框架")):
        return "L0"
    if any(k in name for k in ("决策会议", "危机", "紧急融资", "任命", "财年结算", "投票")):
        return "L4"
    if any(k in name for k in ("确认", "补条件", "条款")):
        return "L5"
    if any(k in name for k in ("座位", "对冲", "碳配额", "FFP", "产品工坊", "RPM", "航班环", "工会", "预算编制")):
        return "L3"
    if any(
        k in name
        for k in (
            "飞机详情",
            "航空事务官",
            "消息中心",
            "宏观",
            "竞争",
            "随机事件",
            "飞行学院",
            "设置",
            "历史回放",
        )
    ):
        return "L2"
    if card["kind"] == "shell":
        return "L0"
    if card["kind"] == "popup":
        return "L4"
    return "L1"


def clicks_for(card: dict, meta_list: list[dict]) -> list[list[str]]:
    for m in meta_list:
        if m["match"] in card["name"]:
            return m["clicks"]
    return []


def build_catalog(cards: list[dict], meta_list: list[dict]) -> list[dict]:
    catalog = []
    for i, card in enumerate(cards):
        catalog.append(
            {
                "id": slug(card["name"], i),
                "level": assign_level(card, meta_list),
                "title": card["name"],
                "meta": card["meta"],
                "badge": card["badge"]
                or (
                    "NEW"
                    if card["kind"] == "new"
                    else ("弹窗" if card["kind"] == "popup" else ("框架" if card["kind"] == "shell" else "补齐"))
                ),
                "items": card["items"],
                "entry": card["entry"],
                "clicks": clicks_for(card, meta_list),
                "kind": card["kind"],
            }
        )
    return catalog


def default_edges(catalog: list[dict]) -> list[list[str]]:
    def find(*keys: str) -> str | None:
        for c in catalog:
            if all(k in c["title"] for k in keys):
                return c["id"]
        for c in catalog:
            if any(k in c["title"] for k in keys):
                return c["id"]
        return None

    pairs = [
        (("主菜单",), ("玩家资料",), "新游戏"),
        (("玩家资料",), ("公司注册",), "下一步"),
        (("公司注册",), ("董事会组建",), "确认注册"),
        (("董事会组建",), ("年度目标",), "确认董事会"),
        (("年度目标",), ("主页",), "开始经营"),
        (("主菜单",), ("主页",), "读档"),
        (("主菜单",), ("设置",), "设置"),
        (("主菜单",), ("飞行学院",), "飞行学院"),
        (("主页",), ("收件箱",), "待办/快讯"),
        (("主页",), ("财务部",), "现金跑道"),
        (("主页",), ("董事会",), "满意度"),
        (("主页",), ("宏观经济",), "宏观灯"),
        (("主页",), ("竞争态势",), "竞争微件"),
        (("主页",), ("消息中心",), "铃铛"),
        (("主页",), ("航空事务官",), "AO头像"),
        (("收件箱",), ("决策会议",), "发起会议"),
        (("收件箱",), ("航线网络",), "批准开航"),
        (("机队",), ("飞机详情",), "点飞机"),
        (("机队",), ("决策会议",), "+购买飞机"),
        (("飞机详情",), ("座位配置",), "座位配置"),
        (("飞机详情",), ("航班环",), "分配航线"),
        (("航线网络",), ("RPM",), "+开通新航线"),
        (("RPM",), ("决策会议",), "提交开线"),
        (("运营部",), ("航班环",), "航班环"),
        (("运营部",), ("危机",), "IRROPS"),
        (("市场销售部",), ("FFP",), "FFP"),
        (("市场销售部",), ("产品工坊",), "产品工坊"),
        (("财务部",), ("对冲",), "对冲"),
        (("财务部",), ("碳配额",), "碳配额"),
        (("财务部",), ("预算编制",), "预算"),
        (("财务部",), ("紧急融资",), "紧急融资"),
        (("战略发展部",), ("RPM",), "RPM评估"),
        (("人力",), ("工会",), "工会谈判"),
        (("董事会",), ("财年结算",), "财年结算"),
    ]
    edges = []
    for a_keys, b_keys, label in pairs:
        a, b = find(*a_keys), find(*b_keys)
        if a and b and a != b:
            edges.append([a, b, label])
    for c in catalog:
        for clk, dst in c["clicks"]:
            tokens = [t for t in re.findall(r"[\u4e00-\u9fffA-Za-z0-9]{2,}", dst) if t not in ("L0", "L1", "L2", "L3", "L4", "L5")]
            best = None
            for other in catalog:
                if other["id"] == c["id"]:
                    continue
                score = sum(1 for t in tokens if t in other["title"])
                if score > 0 and (best is None or score > best[0]):
                    best = (score, other["id"])
            if best:
                edges.append([c["id"], best[1], clk])
    seen, uniq = set(), []
    for e in edges:
        key = (e[0], e[1])
        if key in seen:
            continue
        seen.add(key)
        uniq.append(e)
    return uniq


def write_board(catalog: list[dict], edges: list[list[str]]) -> None:
    cat_js = json.dumps(catalog, ensure_ascii=False)
    edges_js = json.dumps(edges, ensure_ascii=False)
    html = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Airline CEO · UI 交互白板（完整内容 · 拖拽连线）</title>
  <style>
    :root {{
      --bg: #071018; --panel: #0d1a2d; --line: #1e3a5f; --accent: #4da6ff;
      --text: #e6eef8; --muted: #8aa0b8;
    }}
    * {{ box-sizing: border-box; margin: 0; padding: 0; }}
    html, body {{ height: 100%; overflow: hidden; font-family: "Segoe UI", "Noto Sans SC", "Microsoft YaHei", sans-serif; background: var(--bg); color: var(--text); }}
    .toolbar {{
      position: fixed; top: 0; left: 0; right: 0; z-index: 50;
      display: flex; flex-wrap: wrap; gap: 8px; align-items: center;
      padding: 10px 14px; background: rgba(13,26,45,0.96); border-bottom: 1px solid var(--line);
    }}
    .toolbar h1 {{ font-size: 15px; font-weight: 700; margin-right: 6px; }}
    .toolbar .hint {{ font-size: 11px; color: var(--muted); margin-right: auto; max-width: 420px; }}
    .toolbar button, .toolbar a.btn {{
      cursor: pointer; border: 1px solid var(--line); background: #13253f; color: #cfe4ff;
      border-radius: 8px; padding: 6px 11px; font-size: 12px; font-family: inherit; text-decoration: none;
    }}
    .toolbar button.primary {{ background: rgba(26,92,255,0.35); border-color: rgba(77,166,255,0.6); }}
    .toolbar button.danger {{ color: #ffb0b0; border-color: rgba(220,80,80,0.5); }}
    .toolbar .status {{ font-size: 11px; color: #9ec8ff; }}
    .legend {{
      position: fixed; top: 58px; left: 12px; z-index: 40; display: flex; flex-wrap: wrap; gap: 6px;
      max-width: 520px; pointer-events: none;
    }}
    .chip {{ font-size: 10px; padding: 3px 8px; border-radius: 999px; border: 1px solid var(--line); background: rgba(13,26,45,0.9); color: var(--muted); }}
    .chip.L0 {{ color:#e6d4ff; border-color:rgba(180,120,255,.5); }}
    .chip.L1 {{ color:#b8d9ff; border-color:rgba(77,166,255,.5); }}
    .chip.L2 {{ color:#b8f0d4; border-color:rgba(62,207,142,.5); }}
    .chip.L3 {{ color:#ffd59a; border-color:rgba(230,162,60,.5); }}
    .chip.L4 {{ color:#ffd0dc; border-color:rgba(255,143,171,.5); }}
    .chip.L5 {{ color:#d6ecff; border-color:rgba(154,208,255,.5); }}
    #viewport {{
      position: absolute; inset: 52px 0 0 0; overflow: hidden; cursor: grab;
      background: radial-gradient(ellipse at 20% 0%, rgba(26,92,255,0.12), transparent 40%), linear-gradient(180deg, #050b14, #0a1628);
    }}
    #viewport.panning {{ cursor: grabbing; }}
    #viewport.connecting {{ cursor: crosshair; }}
    #world {{ position: absolute; left: 0; top: 0; width: 1px; height: 1px; transform-origin: 0 0; }}
    #edges {{ position: absolute; left: 0; top: 0; width: 12000px; height: 20000px; pointer-events: none; overflow: visible; }}
    #edges path.edge {{ fill: none; stroke: #4da6ff; stroke-width: 2.2; marker-end: url(#arrowHead); pointer-events: stroke; cursor: pointer; }}
    #edges path.edge:hover {{ stroke: #ffd36a; stroke-width: 3; }}
    #edges path.edge.selected {{ stroke: #ff8fab; stroke-width: 3; }}
    #edges path.temp {{ fill: none; stroke: #ffe9a0; stroke-width: 2; stroke-dasharray: 6 4; marker-end: url(#arrowHeadTemp); pointer-events: none; }}
    #edges text {{ pointer-events: none; }}

    .node {{
      position: absolute; width: 360px;
      background: linear-gradient(180deg, #152a44, #0d1a2d);
      border: 2px solid #2a4a6a; border-radius: 12px;
      box-shadow: 0 10px 28px rgba(0,0,0,0.35); user-select: none; cursor: grab;
    }}
    .node:active {{ cursor: grabbing; }}
    .node.selected {{ box-shadow: 0 0 0 2px #ffe9a0, 0 10px 28px rgba(0,0,0,0.35); }}
    .node.L0 {{ border-color: #b478ff; }}
    .node.L1 {{ border-color: #4da6ff; }}
    .node.L2 {{ border-color: #3ecf8e; }}
    .node.L3 {{ border-color: #e6a23c; }}
    .node.L4 {{ border-color: #ff8fab; }}
    .node.L5 {{ border-color: #9ad0ff; }}
    .node .hd {{ display: flex; align-items: center; gap: 6px; padding: 8px 10px 6px; border-bottom: 1px solid rgba(77,166,255,0.15); }}
    .node .lv {{ font-size: 10px; font-weight: 800; padding: 2px 6px; border-radius: 999px; background: rgba(0,0,0,0.35); }}
    .node.L0 .lv {{ color:#e6d4ff; }} .node.L1 .lv {{ color:#b8d9ff; }} .node.L2 .lv {{ color:#b8f0d4; }}
    .node.L3 .lv {{ color:#ffd59a; }} .node.L4 .lv {{ color:#ffd0dc; }} .node.L5 .lv {{ color:#d6ecff; }}
    .node .title {{ font-size: 13px; font-weight: 700; color: #cfe4ff; line-height: 1.3; flex: 1; }}
    .node .badge {{ font-size: 10px; font-weight: 800; padding: 2px 7px; border-radius: 999px; border: 1px solid rgba(230,162,60,0.55); color: #ffd59a; background: rgba(230,162,60,0.15); }}
    .node .meta {{ padding: 6px 10px 2px; font-size: 11px; color: #8aa0b8; }}
    .node .items {{ padding: 4px 12px 8px 26px; font-size: 11.5px; color: #c5d4e6; line-height: 1.45; }}
    .node .items li {{ margin: 3px 0; }}
    .node .entry {{ margin: 0 10px 8px; font-size: 11px; color: #7fb0e6; border-top: 1px dashed rgba(77,166,255,0.25); padding-top: 6px; }}
    .node .clicks {{ margin: 0 8px 8px; padding: 7px 8px; border-radius: 8px; border: 1px solid rgba(77,166,255,0.35); background: rgba(8,20,38,0.75); }}
    .node .clicks .ttl {{ font-size: 11px; font-weight: 700; color: #9ec8ff; margin-bottom: 5px; }}
    .node .clicks table {{ width: 100%; border-collapse: collapse; font-size: 11px; }}
    .node .clicks td {{ padding: 3px 2px; vertical-align: top; color: #c5d4e6; border-top: 1px dashed rgba(77,166,255,0.15); }}
    .node .clicks tr:first-child td {{ border-top: none; }}
    .node .clicks .clk {{ color: #ffe9a0; font-weight: 700; width: 44%; }}
    .node .clicks .arrow {{ color: #4da6ff; width: 14px; text-align: center; }}
    .node .clicks .dst {{ color: #7dffa8; }}
    .node .note {{
      display: block; margin: 0 8px 10px; width: calc(100% - 16px); min-height: 56px; resize: vertical;
      border: 1.5px solid #e6c200; border-radius: 8px; background: #fff3bf; color: #3a2e00;
      font-size: 12px; font-family: inherit; padding: 7px 8px; line-height: 1.45;
    }}
    .node .note::placeholder {{ color: #a09050; }}
    .node .ports {{ position: absolute; inset: 0; pointer-events: none; }}
    .port {{
      position: absolute; width: 14px; height: 14px; border-radius: 50%;
      border: 2px solid #0b1a33; pointer-events: auto; cursor: crosshair;
      transform: translate(-50%, -50%);
    }}
    .port.out {{ left: 100%; top: 48px; background: #7dffa8; }}
    .port.in {{ left: 0; top: 48px; background: #ffd36a; }}
    .port:hover {{ transform: translate(-50%, -50%) scale(1.25); }}
    .help {{
      position: fixed; right: 12px; bottom: 12px; z-index: 40; width: 280px; font-size: 11px; line-height: 1.55;
      background: rgba(13,26,45,0.94); border: 1px solid var(--line); border-radius: 10px; padding: 10px 12px; color: var(--muted);
    }}
    .help b {{ color: #cfe4ff; }}
  </style>
</head>
<body>
  <div class="toolbar">
    <h1>UI 交互白板 · 完整内容</h1>
    <span class="hint">与文档版同步：功能列表 + 点击跳转均保留。拖卡片移动；绿点→黄点连线。</span>
    <button type="button" class="primary" id="btnAuto">按层级自动排布</button>
    <button type="button" id="btnDelEdge" class="danger">删除选中连线</button>
    <button type="button" id="btnClearEdges" class="danger">清空连线</button>
    <button type="button" id="btnExport">导出 JSON</button>
    <button type="button" id="btnReset">重置布局</button>
    <a class="btn" href="./Airline_CEO__UI交互图%20V1.0.html">← 文档版</a>
    <span class="status" id="status">就绪</span>
  </div>
  <div class="legend">
    <span class="chip L0">L0</span><span class="chip L1">L1</span><span class="chip L2">L2</span>
    <span class="chip L3">L3</span><span class="chip L4">L4</span><span class="chip L5">L5</span>
    <span class="chip">内容来自交互图全文，可批注修改</span>
  </div>
  <div id="viewport">
    <div id="world">
      <svg id="edges">
        <defs>
          <marker id="arrowHead" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#4da6ff"></path></marker>
          <marker id="arrowHeadTemp" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ffe9a0"></path></marker>
          <marker id="arrowHeadHot" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L8,3 L0,6 Z" fill="#ff8fab"></path></marker>
        </defs>
      </svg>
      <div id="nodes"></div>
    </div>
  </div>
  <div class="help">
    <b>操作</b><br>
    · 拖拽卡片自由移动<br>
    · 绿点拖到黄点：画跳转箭头<br>
    · 单击箭头后 Delete 删除<br>
    · 空白拖动画布，滚轮缩放<br>
    · 黄框写批注（自动保存）
  </div>
<script>
(function () {{
  var STORAGE_KEY = 'airline_ceo_ui_board_v2_full';
  var CATALOG = {cat_js};
  var DEFAULT_EDGES = {edges_js};

  var viewport = document.getElementById('viewport');
  var world = document.getElementById('world');
  var nodesRoot = document.getElementById('nodes');
  var edgesSvg = document.getElementById('edges');
  var statusEl = document.getElementById('status');
  var state = {{ nodes: {{}}, edges: [], cam: {{ x: 30, y: 30, scale: 0.72 }}, selectedEdge: null }};
  var drag = null, pan = null, link = null;

  function uid(p) {{ return p + '_' + Math.random().toString(36).slice(2, 9); }}
  function setStatus(t) {{ statusEl.textContent = t; }}
  function save() {{
    localStorage.setItem(STORAGE_KEY, JSON.stringify({{ nodes: state.nodes, edges: state.edges, cam: state.cam }}));
    setStatus('已保存 · 节点 ' + CATALOG.length + ' · 连线 ' + state.edges.length);
  }}
  function load() {{
    try {{
      var d = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
      if (!d) return false;
      state.nodes = d.nodes || {{}};
      state.edges = d.edges || [];
      state.cam = d.cam || state.cam;
      return true;
    }} catch (e) {{ return false; }}
  }}
  function autoLayout() {{
    var cols = {{ L0:0, L1:1, L2:2, L3:3, L4:4, L5:5 }};
    var counters = {{ L0:0, L1:0, L2:0, L3:0, L4:0, L5:0 }};
    CATALOG.forEach(function (item) {{
      var c = cols[item.level] || 0;
      var r = counters[item.level]++;
      var x = 40 + c * 420;
      var y = 40 + r * 560;
      if (!state.nodes[item.id]) state.nodes[item.id] = {{ x:x, y:y, note:'' }};
      else {{ state.nodes[item.id].x = x; state.nodes[item.id].y = y; }}
    }});
  }}
  function ensureDefaults() {{
    CATALOG.forEach(function (item) {{
      if (!state.nodes[item.id]) state.nodes[item.id] = {{ x:40, y:40, note:'' }};
    }});
    // drop stale node ids
    Object.keys(state.nodes).forEach(function (id) {{
      if (!CATALOG.some(function (c) {{ return c.id === id; }})) delete state.nodes[id];
    }});
    if (!state.edges.length) {{
      state.edges = DEFAULT_EDGES.map(function (e) {{
        return {{ id: uid('e'), from: e[0], to: e[1], label: e[2] || '' }};
      }});
    }}
  }}
  function applyCam() {{
    world.style.transform = 'translate(' + state.cam.x + 'px,' + state.cam.y + 'px) scale(' + state.cam.scale + ')';
  }}
  function nodeOut(id) {{
    var el = nodesRoot.querySelector('.node[data-id="' + id + '"]');
    var n = state.nodes[id];
    var h = el ? el.offsetHeight : 200;
    return {{ x: n.x + 360, y: n.y + Math.min(56, h * 0.2) }};
  }}
  function nodeIn(id) {{
    var el = nodesRoot.querySelector('.node[data-id="' + id + '"]');
    var n = state.nodes[id];
    var h = el ? el.offsetHeight : 200;
    return {{ x: n.x, y: n.y + Math.min(56, h * 0.2) }};
  }}
  function curve(x1,y1,x2,y2) {{
    var dx = Math.max(50, Math.abs(x2-x1)*0.45);
    return 'M '+x1+' '+y1+' C '+(x1+dx)+' '+y1+', '+(x2-dx)+' '+y2+', '+x2+' '+y2;
  }}
  function escapeHtml(s) {{
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {{
      return ({{'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}})[c];
    }});
  }}
  function renderEdges() {{
    var html = edgesSvg.querySelector('defs').outerHTML;
    state.edges.forEach(function (e) {{
      if (!state.nodes[e.from] || !state.nodes[e.to]) return;
      var a = nodeOut(e.from), b = nodeIn(e.to);
      var sel = state.selectedEdge === e.id;
      var marker = sel ? 'url(#arrowHeadHot)' : 'url(#arrowHead)';
      html += '<path class="edge'+(sel?' selected':'')+'" data-id="'+e.id+'" d="'+curve(a.x,a.y,b.x,b.y)+'" style="marker-end:'+marker+'"></path>';
      if (e.label) {{
        html += '<text x="'+((a.x+b.x)/2)+'" y="'+((a.y+b.y)/2-8)+'" fill="#8aa0b8" font-size="11" text-anchor="middle">'+escapeHtml(e.label)+'</text>';
      }}
    }});
    if (link && link.x2 != null) html += '<path class="temp" d="'+curve(link.x1,link.y1,link.x2,link.y2)+'"></path>';
    edgesSvg.innerHTML = html;
    edgesSvg.querySelectorAll('path.edge').forEach(function (p) {{
      p.addEventListener('mousedown', function (ev) {{
        ev.stopPropagation();
        state.selectedEdge = p.getAttribute('data-id');
        renderEdges();
        setStatus('已选中连线 · Delete 删除');
      }});
    }});
  }}
  function renderNodes() {{
    nodesRoot.innerHTML = '';
    CATALOG.forEach(function (item) {{
      var pos = state.nodes[item.id];
      var el = document.createElement('div');
      el.className = 'node ' + item.level;
      el.style.left = pos.x + 'px';
      el.style.top = pos.y + 'px';
      el.dataset.id = item.id;
      var itemsHtml = (item.items || []).map(function (it) {{ return '<li>' + escapeHtml(it) + '</li>'; }}).join('');
      var clicksHtml = '';
      if (item.clicks && item.clicks.length) {{
        clicksHtml = '<div class="clicks"><div class="ttl">本页可点击 → 跳转</div><table>' +
          item.clicks.map(function (pair) {{
            return '<tr><td class="clk">'+escapeHtml(pair[0])+'</td><td class="arrow">→</td><td class="dst">'+escapeHtml(pair[1])+'</td></tr>';
          }}).join('') + '</table></div>';
      }}
      el.innerHTML =
        '<div class="hd"><span class="lv">'+item.level+'</span><div class="title">'+escapeHtml(item.title)+'</div>'+
          (item.badge ? '<span class="badge">'+escapeHtml(item.badge)+'</span>' : '') + '</div>' +
        (item.meta ? '<div class="meta">'+escapeHtml(item.meta)+'</div>' : '') +
        '<ul class="items">'+itemsHtml+'</ul>' +
        (item.entry ? '<div class="entry">'+escapeHtml(item.entry)+'</div>' : '') +
        clicksHtml +
        '<textarea class="note" placeholder="批注 / 待修改：点此输入……">'+escapeHtml(pos.note || '')+'</textarea>' +
        '<div class="ports"><div class="port in" data-port="in" title="连入"></div><div class="port out" data-port="out" title="连出"></div></div>';
      nodesRoot.appendChild(el);

      el.querySelector('.note').addEventListener('input', function (ev) {{
        state.nodes[item.id].note = ev.target.value; save();
      }});
      el.querySelector('.note').addEventListener('mousedown', function (ev) {{ ev.stopPropagation(); }});

      el.addEventListener('mousedown', function (ev) {{
        if (ev.target.classList.contains('port') || ev.target.tagName === 'TEXTAREA') return;
        ev.preventDefault();
        drag = {{ id:item.id, ox:ev.clientX, oy:ev.clientY, sx:state.nodes[item.id].x, sy:state.nodes[item.id].y }};
        el.classList.add('selected');
      }});
      el.querySelector('.port.out').addEventListener('mousedown', function (ev) {{
        ev.stopPropagation(); ev.preventDefault();
        var p = nodeOut(item.id);
        link = {{ from:item.id, x1:p.x, y1:p.y, x2:p.x, y2:p.y }};
        viewport.classList.add('connecting');
        setStatus('拖到目标黄点松手完成连线');
      }});
      el.querySelector('.port.in').addEventListener('mouseup', function (ev) {{
        if (!link) return;
        ev.stopPropagation();
        finishLink(item.id);
      }});
    }});
    // ports vertical center-ish after layout
    renderEdges();
  }}
  function finishLink(toId) {{
    if (!link || !link.from) return;
    if (link.from === toId) {{ link=null; viewport.classList.remove('connecting'); renderEdges(); return; }}
    var exists = state.edges.some(function (e) {{ return e.from===link.from && e.to===toId; }});
    if (!exists) {{
      var label = prompt('连线说明（可点击文案，可留空）:', '') || '';
      state.edges.push({{ id: uid('e'), from: link.from, to: toId, label: label }});
      save();
    }}
    link = null; viewport.classList.remove('connecting'); renderEdges();
    setStatus(exists ? '已存在相同连线' : '已添加连线');
  }}
  function screenToWorld(cx, cy) {{
    var rect = viewport.getBoundingClientRect();
    return {{ x:(cx-rect.left-state.cam.x)/state.cam.scale, y:(cy-rect.top-state.cam.y)/state.cam.scale }};
  }}
  viewport.addEventListener('mousedown', function (ev) {{
    if (ev.target !== viewport && ev.target !== world && ev.target !== edgesSvg && ev.target !== nodesRoot) return;
    if (link) return;
    pan = {{ ox:ev.clientX, oy:ev.clientY, sx:state.cam.x, sy:state.cam.y }};
    viewport.classList.add('panning');
    state.selectedEdge = null; renderEdges();
  }});
  window.addEventListener('mousemove', function (ev) {{
    if (drag) {{
      var dx=(ev.clientX-drag.ox)/state.cam.scale, dy=(ev.clientY-drag.oy)/state.cam.scale;
      state.nodes[drag.id].x = Math.round(drag.sx+dx);
      state.nodes[drag.id].y = Math.round(drag.sy+dy);
      var el = nodesRoot.querySelector('.node[data-id="'+drag.id+'"]');
      if (el) {{ el.style.left=state.nodes[drag.id].x+'px'; el.style.top=state.nodes[drag.id].y+'px'; }}
      renderEdges(); return;
    }}
    if (pan) {{
      state.cam.x = pan.sx + (ev.clientX-pan.ox);
      state.cam.y = pan.sy + (ev.clientY-pan.oy);
      applyCam(); return;
    }}
    if (link) {{
      var w = screenToWorld(ev.clientX, ev.clientY);
      link.x2=w.x; link.y2=w.y; renderEdges();
    }}
  }});
  window.addEventListener('mouseup', function (ev) {{
    if (drag) {{ drag=null; document.querySelectorAll('.node.selected').forEach(function(n){{n.classList.remove('selected');}}); save(); }}
    if (pan) {{ pan=null; viewport.classList.remove('panning'); save(); }}
    if (link) {{
      var t = ev.target;
      if (!(t && t.classList && t.classList.contains('port') && t.dataset.port==='in')) {{
        var w = screenToWorld(ev.clientX, ev.clientY);
        var hit = null;
        CATALOG.forEach(function (item) {{
          var el = nodesRoot.querySelector('.node[data-id="'+item.id+'"]');
          var n = state.nodes[item.id];
          var h = el ? el.offsetHeight : 400;
          if (w.x>=n.x && w.x<=n.x+360 && w.y>=n.y && w.y<=n.y+h) hit = item.id;
        }});
        if (hit && hit !== link.from) finishLink(hit);
        else {{ link=null; viewport.classList.remove('connecting'); renderEdges(); setStatus('已取消连线'); }}
      }}
    }}
  }});
  viewport.addEventListener('wheel', function (ev) {{
    ev.preventDefault();
    var rect = viewport.getBoundingClientRect();
    var mx = ev.clientX-rect.left, my = ev.clientY-rect.top;
    var before = screenToWorld(ev.clientX, ev.clientY);
    var next = Math.min(1.6, Math.max(0.3, state.cam.scale * (ev.deltaY>0 ? 0.9 : 1.1)));
    state.cam.scale = next;
    state.cam.x = mx - before.x * next;
    state.cam.y = my - before.y * next;
    applyCam(); save();
  }}, {{ passive:false }});
  window.addEventListener('keydown', function (ev) {{
    if ((ev.key==='Delete'||ev.key==='Backspace') && state.selectedEdge && document.activeElement.tagName!=='TEXTAREA') {{
      ev.preventDefault();
      state.edges = state.edges.filter(function (e) {{ return e.id !== state.selectedEdge; }});
      state.selectedEdge = null; renderEdges(); save(); setStatus('已删除连线');
    }}
  }});
  document.getElementById('btnAuto').onclick = function () {{ autoLayout(); renderNodes(); save(); setStatus('已按层级排布'); }};
  document.getElementById('btnDelEdge').onclick = function () {{
    if (!state.selectedEdge) {{ alert('请先单击一条箭头'); return; }}
    state.edges = state.edges.filter(function (e) {{ return e.id !== state.selectedEdge; }});
    state.selectedEdge=null; renderEdges(); save();
  }};
  document.getElementById('btnClearEdges').onclick = function () {{
    if (!confirm('清空全部连线？')) return;
    state.edges=[]; state.selectedEdge=null; renderEdges(); save();
  }};
  document.getElementById('btnExport').onclick = function () {{
    var blob = new Blob([JSON.stringify({{nodes:state.nodes, edges:state.edges}}, null, 2)], {{type:'application/json'}});
    var a=document.createElement('a'); a.href=URL.createObjectURL(blob);
    a.download='UI交互白板_完整_'+new Date().toISOString().slice(0,10)+'.json'; a.click();
  }};
  document.getElementById('btnReset').onclick = function () {{
    if (!confirm('重置布局与推荐连线？（批注也会清空）')) return;
    localStorage.removeItem(STORAGE_KEY);
    state.nodes={{}}; state.edges=[]; state.cam={{x:30,y:30,scale:0.72}};
    autoLayout(); ensureDefaults(); applyCam(); renderNodes(); save();
  }};

  var loaded = load();
  if (!loaded) autoLayout();
  ensureDefaults();
  // if catalog ids changed, force relayout when mismatch
  var missing = CATALOG.some(function (c) {{ return state.nodes[c.id]==null; }});
  if (missing) autoLayout();
  ensureDefaults();
  applyCam();
  renderNodes();
  setStatus(loaded ? '已恢复白板（完整内容）' : '已载入文档版全部界面内容');
}})();
</script>
</body>
</html>
"""
    BOARD.write_text(html, encoding="utf-8")
    print("wrote", BOARD, "bytes", BOARD.stat().st_size)


def main() -> None:
    text = DOC.read_text(encoding="utf-8")
    cards = parse_cards(text)
    # filter out section 五 return-path card
    cards = [c for c in cards if not (c["name"] == "" or "返回路径" in "".join(c["items"][:1]))]
    # also drop the shell that is only return path - check title empty unlikely
    cards = [c for c in cards if "L2/L3" not in (c["items"][0] if c["items"] else "")]
    meta = parse_page_meta(text)
    catalog = build_catalog(cards, meta)
    edges = default_edges(catalog)
    OUT_JSON.write_text(
        json.dumps({"catalog": catalog, "edges": edges}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    write_board(catalog, edges)
    print("catalog", len(catalog), "edges", len(edges))


if __name__ == "__main__":
    main()
