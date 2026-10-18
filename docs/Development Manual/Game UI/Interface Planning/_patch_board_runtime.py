# -*- coding: utf-8 -*-
"""Patch whiteboard runtime: migrate old storage, fix edge labels, tip text."""
from pathlib import Path
import re
import json

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")

    # Fix DEFAULT_EDGES labels/links
    m = re.search(r"var DEFAULT_EDGES = (\[[\s\S]*?\]);", html)
    if not m:
        raise SystemExit("DEFAULT_EDGES missing")
    edges = json.loads(m.group(1))
    new_edges = []
    for e in edges:
        if e[0] == "n6_主页_Dashboard" and e[1] == "n27_消息中心":
            continue  # messages unified into inbox
        if e[0] == "n7_收件箱" and e[1] == "n12_航线网络" and e[2] == "批准开航":
            e = [e[0], e[1], "批准"]
        new_edges.append(e)
    html = html[: m.start(1)] + json.dumps(new_edges, ensure_ascii=False) + html[m.end(1) :]

    # Update n40 settings items for display mode (companion to n1 note)
    m2 = re.search(r'"id": "n40_设置_存档".*?"items": (\[[\s\S]*?\])', html)
    if m2:
        items = json.loads(m2.group(1))
        if not any("全屏" in x for x in items):
            items.insert(1, "显示模式：全屏 或 窗口")
            html = html[: m2.start(1)] + json.dumps(items, ensure_ascii=False) + html[m2.end(1) :]

    old_load = """  function load() {
    try {
      var d = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
      if (!d) return false;
      state.nodes = d.nodes || {};
      state.edges = d.edges || [];
      state.cam = d.cam || state.cam;
      state.extras = d.extras || [];
      return true;
    } catch (e) { return false; }
  }"""

    new_load = """  var LEGACY_STORAGE_KEYS = ['airline_ceo_ui_board_v2_full'];
  var APPLIED_NOTE_IDS = {
    'n0_主界面框架_常驻':1,'n1_主菜单':1,'n2_玩家资料创建':1,'n3_公司注册':1,
    'n5_年度目标确认':1,'n6_主页_Dashboard':1,'n7_收件箱':1,'n9_机队':1,
    'n10_飞机详情_履历':1,'n12_航线网络':1,'n13_运营部':1
  };
  function migrateLayoutState() {
    var map = { 'custom_q282zgz':'n41_单航线', 'custom_uqawjw8':'n43_航班详细计划' };
    Object.keys(map).forEach(function (oldId) {
      if (state.nodes[oldId]) {
        var pos = state.nodes[oldId];
        if (!state.nodes[map[oldId]]) state.nodes[map[oldId]] = { x: pos.x, y: pos.y, note: '' };
        delete state.nodes[oldId];
      }
    });
    state.extras = (state.extras || []).filter(function (x) {
      return x && x.id !== 'custom_q282zgz' && x.id !== 'custom_uqawjw8' && x.id !== 'n14_市场销售部';
    });
    // 仅首次迁移时清空「已应用批注」页的旧黄框，避免后续新批注被冲掉
    var clearedKey = 'airline_ceo_ui_board_notes_cleared_20260809';
    if (!localStorage.getItem(clearedKey)) {
      Object.keys(APPLIED_NOTE_IDS).forEach(function (id) {
        if (state.nodes[id]) state.nodes[id].note = '';
      });
      localStorage.setItem(clearedKey, '1');
    }
    state.edges = (state.edges || []).map(function (e) {
      var fr = map[e.from] || e.from;
      var to = map[e.to] || e.to;
      var label = e.label || '';
      if (fr === 'n7_收件箱' && to === 'n12_航线网络' && label === '批准开航') label = '批准';
      if (fr === 'n10_飞机详情_履历' && to === 'n11_座位配置') label = '载客量配置';
      return { id: e.id || uid('e'), from: fr, to: to, label: label };
    }).filter(function (e) {
      return !(e.from === 'n0_主界面框架_常驻' && e.to === 'n27_消息中心')
        && !(e.from === 'n6_主页_Dashboard' && e.to === 'n27_消息中心');
    });
    var has = {};
    state.edges.forEach(function (e) { has[e.from + '>' + e.to] = true; });
    [
      ['n12_航线网络','n41_单航线','点航线'],
      ['n13_运营部','n42_航班周总计划','航班周总计划'],
      ['n42_航班周总计划','n43_航班详细计划','点航班行']
    ].forEach(function (t) {
      if (!has[t[0] + '>' + t[1]]) state.edges.push({ id: uid('e'), from: t[0], to: t[1], label: t[2] });
    });
  }
  function load() {
    try {
      var raw = localStorage.getItem(STORAGE_KEY);
      var fromLegacy = false;
      if (!raw) {
        for (var i = 0; i < LEGACY_STORAGE_KEYS.length; i++) {
          raw = localStorage.getItem(LEGACY_STORAGE_KEYS[i]);
          if (raw) { fromLegacy = true; break; }
        }
      }
      var d = JSON.parse(raw || 'null');
      if (!d) return false;
      state.nodes = d.nodes || {};
      state.edges = d.edges || [];
      state.cam = d.cam || state.cam;
      state.extras = d.extras || [];
      migrateLayoutState();
      if (fromLegacy) save();
      return true;
    } catch (e) { return false; }
  }"""

    if old_load not in html:
        raise SystemExit("old load() block not found")
    html = html.replace(old_load, new_load, 1)

    # import path also migrate
    html = html.replace(
        """        if (d.nodes) state.nodes = d.nodes;
        if (d.edges) state.edges = d.edges;
        if (d.cam) state.cam = d.cam;
        if (d.extras) state.extras = d.extras;
        ensureDefaults(); applyCam(); renderNodes(); save();
        setStatus('已导入 JSON（含新建界面与批注）');""",
        """        if (d.nodes) state.nodes = d.nodes;
        if (d.edges) state.edges = d.edges;
        if (d.cam) state.cam = d.cam;
        if (d.extras) state.extras = d.extras;
        migrateLayoutState();
        ensureDefaults(); applyCam(); renderNodes(); save();
        setStatus('已导入 JSON（批注页内容已写入内置目录；布局已合并）');""",
        1,
    )

    html = html.replace(
        '    <span class="hint">可新建界面并连线。批注/新界面自动保存在本机。</span>',
        '    <span class="hint">2026-08-09 已应用部分批注到卡片内容。未批注页可继续标注。布局可用「导入 JSON」恢复。</span>',
        1,
    )

    BOARD.write_text(html, encoding="utf-8")
    print("runtime patched", BOARD.stat().st_size)


if __name__ == "__main__":
    main()
