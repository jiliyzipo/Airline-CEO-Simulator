# -*- coding: utf-8 -*-
"""Add Word (.docx) attachment support to annotation panels in the whiteboard."""
from pathlib import Path
import re

BOARD = Path(__file__).with_name("Airline_CEO__UI交互白板 V1.0.html")


def main() -> None:
    html = BOARD.read_text(encoding="utf-8")
    if "btn-doc-attach" in html and "DOC_DB_NAME" in html:
        print("already patched")
        return

    # --- CSS ---
    css_old = """    .node .note {
      display: block; margin: 0 8px 10px; width: calc(100% - 16px); min-height: 56px; resize: vertical;
      border: 1.5px solid #e6c200; border-radius: 8px; background: #fff3bf; color: #3a2e00;
      font-size: 12px; font-family: inherit; padding: 7px 8px; line-height: 1.45;
    }
    .node .note::placeholder { color: #a09050; }"""

    css_new = """    .node .note-panel {
      margin: 0 8px 10px; border: 1.5px solid #e6c200; border-radius: 8px; background: #fff3bf;
      overflow: hidden;
    }
    .node .note-panel .note-label {
      font-size: 10px; font-weight: 700; color: #6a5600; padding: 5px 8px 0; letter-spacing: 0.02em;
    }
    .node .note {
      display: block; margin: 0; width: 100%; min-height: 48px; resize: vertical;
      border: none; border-radius: 0; background: transparent; color: #3a2e00;
      font-size: 12px; font-family: inherit; padding: 6px 8px; line-height: 1.45;
    }
    .node .note::placeholder { color: #a09050; }
    .node .note-doc {
      border-top: 1px dashed rgba(160,130,0,0.45); padding: 6px 8px 8px; background: rgba(255,255,255,0.35);
    }
    .node .note-doc .doc-row { display:flex; align-items:center; gap:6px; flex-wrap:wrap; }
    .node .note-doc .doc-name {
      flex:1; min-width: 120px; font-size: 11px; color: #3a2e00; font-weight: 600;
      overflow:hidden; text-overflow:ellipsis; white-space:nowrap;
    }
    .node .note-doc .doc-name.empty { color:#8a7420; font-weight:500; }
    .node .note-doc button {
      cursor:pointer; font-size:10px; font-family:inherit; border-radius:5px;
      border:1px solid rgba(120,90,0,0.45); background:#fff8d6; color:#3a2e00;
      padding:3px 7px; white-space:nowrap;
    }
    .node .note-doc button:hover { background:#ffe9a0; }
    .node .note-doc button.danger { color:#8a2020; border-color:rgba(160,60,60,0.45); }
    .doc-preview-modal .modal { width: min(920px, 96vw); max-height: 92vh; }
    .doc-preview-modal .doc-preview-body {
      background:#fff; color:#1a1a1a; border-radius:8px; padding:16px 18px;
      max-height: 70vh; overflow:auto; font-size:13px; line-height:1.55;
      border:1px solid #d0d7de;
    }
    .doc-preview-modal .doc-preview-body h1 { font-size:20px; margin:12px 0 8px; }
    .doc-preview-modal .doc-preview-body h2 { font-size:17px; margin:12px 0 6px; }
    .doc-preview-modal .doc-preview-body h3 { font-size:15px; margin:10px 0 6px; }
    .doc-preview-modal .doc-preview-body p { margin: 0 0 8px; }
    .doc-preview-modal .doc-preview-body ul, .doc-preview-modal .doc-preview-body ol { margin: 0 0 10px 1.4em; }
    .doc-preview-modal .doc-preview-body table {
      border-collapse: collapse; width: 100%; margin: 10px 0; font-size: 12px;
    }
    .doc-preview-modal .doc-preview-body th,
    .doc-preview-modal .doc-preview-body td {
      border: 1px solid #c5cdd6; padding: 6px 8px; vertical-align: top;
    }
    .doc-preview-modal .doc-preview-body th { background: #f0f3f7; font-weight: 700; }
    .doc-preview-modal .doc-preview-meta { font-size:12px; color:#8aa0b8; margin-bottom:10px; }"""

    if css_old not in html:
        raise SystemExit("CSS block not found")
    html = html.replace(css_old, css_new, 1)

    # --- help + toolbar hint ---
    html = html.replace(
        "    · 黄框批注自动保存<br>\n"
        "    · <b>批注→功能框</b>：把批注整理成功能点写入功能列表并清空黄框<br>\n"
        "    · 导出/导入 JSON 可备份批注与新界面",
        "    · 黄框：短文本批注自动保存<br>\n"
        "    · <b>添加 Word</b>：可挂 .docx（表格/多级标题），支持预览与下载<br>\n"
        "    · <b>批注→功能框</b>：文本批注整理进功能列表（不清空 Word 附件）<br>\n"
        "    · 导出/导入 JSON 可备份批注、Word 附件与新界面",
        1,
    )
    html = html.replace(
        '    <span class="hint">批注写在黄框 → 点「批注→功能框」整理进功能列表。未批注页可继续标注。</span>',
        '    <span class="hint">黄框可写短批注，也可「添加 Word」挂表格/分级文档。导出 JSON 会带上附件。</span>',
        1,
    )

    # --- modals + file input ---
    modal_insert = """
  <div class="modal-mask doc-preview-modal" id="docPreviewModal">
    <div class="modal">
      <h2 id="docPreviewTitle">Word 批注预览</h2>
      <div class="doc-preview-meta" id="docPreviewMeta"></div>
      <div class="doc-preview-body" id="docPreviewBody">加载中…</div>
      <div class="actions">
        <button type="button" id="btnDocPreviewDownload">下载原文件</button>
        <button type="button" class="primary" id="btnDocPreviewClose">关闭</button>
      </div>
    </div>
  </div>
  <input type="file" id="docAttachFile" accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document" style="display:none">
"""
    if 'id="docPreviewModal"' not in html:
        html = html.replace(
            '  <div class="modal-mask" id="screenModal">',
            modal_insert + '  <div class="modal-mask" id="screenModal">',
            1,
        )

    # --- mammoth script ---
    if "mammoth.browser.min.js" not in html:
        html = html.replace(
            "<script>\n(function () {",
            '<script src="./vendor/mammoth.browser.min.js"></script>\n<script>\n(function () {',
            1,
        )

    # --- ensureDefaults note shape: keep note string; add doc meta optional ---
    # Patch renderNodes note HTML
    old_note = """        '<textarea class="note" placeholder="批注 / 待修改：点此输入……">'+escapeHtml(pos.note || '')+'</textarea>' +
        btns +"""

    new_note = """        notePanelHtml(pos) +
        btns +"""

    if old_note not in html:
        raise SystemExit("note render block not found")
    html = html.replace(old_note, new_note, 1)

    # After note textarea listeners, wire doc buttons - replace the note listener block
    old_listeners = """      el.querySelector('.note').addEventListener('input', function (ev) {
        state.nodes[item.id].note = ev.target.value; save();
      });
      el.querySelector('.note').addEventListener('mousedown', function (ev) { ev.stopPropagation(); });
      el.querySelector('.btn-edit').addEventListener('mousedown', function (ev) { ev.stopPropagation(); });"""

    new_listeners = """      bindNotePanel(el, item.id);
      el.querySelector('.btn-edit').addEventListener('mousedown', function (ev) { ev.stopPropagation(); });"""

    if old_listeners not in html:
        raise SystemExit("note listeners not found")
    html = html.replace(old_listeners, new_listeners, 1)

    # Insert Word/IndexedDB helpers before save()
    helpers = r'''
  var DOC_DB_NAME = 'airline_ceo_ui_board_docs_v1';
  var DOC_STORE = 'docs';
  var attachTargetId = null;
  var previewTargetId = null;

  function notePanelHtml(pos) {
    var doc = pos && pos.doc;
    var nameHtml = doc && doc.name
      ? ('📄 ' + escapeHtml(doc.name) + (doc.size ? ' · ' + formatBytes(doc.size) : ''))
      : '尚未添加 Word 附件（适合表格 / 多级标题）';
    var nameCls = doc && doc.name ? 'doc-name' : 'doc-name empty';
    return '' +
      '<div class="note-panel">' +
        '<div class="note-label">批注 / 修改</div>' +
        '<textarea class="note" placeholder="短文本批注（可选）……">'+escapeHtml((pos && pos.note) || '')+'</textarea>' +
        '<div class="note-doc">' +
          '<div class="doc-row">' +
            '<div class="'+nameCls+'">'+nameHtml+'</div>' +
            '<button type="button" class="btn-doc-attach">添加 Word</button>' +
            (doc && doc.name ? '<button type="button" class="btn-doc-preview">预览</button>' : '') +
            (doc && doc.name ? '<button type="button" class="btn-doc-download">下载</button>' : '') +
            (doc && doc.name ? '<button type="button" class="danger btn-doc-remove">移除</button>' : '') +
          '</div>' +
        '</div>' +
      '</div>';
  }
  function formatBytes(n) {
    n = Number(n) || 0;
    if (n < 1024) return n + ' B';
    if (n < 1024*1024) return (n/1024).toFixed(1) + ' KB';
    return (n/1024/1024).toFixed(1) + ' MB';
  }
  function openDocDb() {
    return new Promise(function (resolve, reject) {
      var req = indexedDB.open(DOC_DB_NAME, 1);
      req.onupgradeneeded = function () {
        var db = req.result;
        if (!db.objectStoreNames.contains(DOC_STORE)) db.createObjectStore(DOC_STORE);
      };
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error); };
    });
  }
  function idbPutDoc(id, record) {
    return openDocDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx = db.transaction(DOC_STORE, 'readwrite');
        tx.objectStore(DOC_STORE).put(record, id);
        tx.oncomplete = function () { resolve(); };
        tx.onerror = function () { reject(tx.error); };
      });
    });
  }
  function idbGetDoc(id) {
    return openDocDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx = db.transaction(DOC_STORE, 'readonly');
        var req = tx.objectStore(DOC_STORE).get(id);
        req.onsuccess = function () { resolve(req.result || null); };
        req.onerror = function () { reject(req.error); };
      });
    });
  }
  function idbDelDoc(id) {
    return openDocDb().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx = db.transaction(DOC_STORE, 'readwrite');
        tx.objectStore(DOC_STORE).delete(id);
        tx.oncomplete = function () { resolve(); };
        tx.onerror = function () { reject(tx.error); };
      });
    });
  }
  function abToBase64(buf) {
    var bytes = new Uint8Array(buf);
    var chunk = 0x8000;
    var parts = [];
    for (var i = 0; i < bytes.length; i += chunk) {
      parts.push(String.fromCharCode.apply(null, bytes.subarray(i, i + chunk)));
    }
    return btoa(parts.join(''));
  }
  function base64ToAb(b64) {
    var bin = atob(b64);
    var bytes = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes.buffer;
  }
  function downloadArrayBuffer(filename, buf, mime) {
    var blob = new Blob([buf], { type: mime || 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = filename || 'annotation.docx';
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1500);
  }
  function bindNotePanel(el, id) {
    var ta = el.querySelector('.note');
    if (ta) {
      ta.addEventListener('input', function (ev) {
        if (!state.nodes[id]) return;
        state.nodes[id].note = ev.target.value; save();
      });
      ta.addEventListener('mousedown', function (ev) { ev.stopPropagation(); });
    }
    el.querySelectorAll('.note-doc button').forEach(function (btn) {
      btn.addEventListener('mousedown', function (ev) { ev.stopPropagation(); });
    });
    var attachBtn = el.querySelector('.btn-doc-attach');
    if (attachBtn) attachBtn.addEventListener('click', function (ev) {
      ev.stopPropagation();
      attachTargetId = id;
      document.getElementById('docAttachFile').click();
    });
    var previewBtn = el.querySelector('.btn-doc-preview');
    if (previewBtn) previewBtn.addEventListener('click', function (ev) {
      ev.stopPropagation(); openDocPreview(id);
    });
    var dlBtn = el.querySelector('.btn-doc-download');
    if (dlBtn) dlBtn.addEventListener('click', function (ev) {
      ev.stopPropagation(); downloadNodeDoc(id);
    });
    var rmBtn = el.querySelector('.btn-doc-remove');
    if (rmBtn) rmBtn.addEventListener('click', function (ev) {
      ev.stopPropagation();
      if (!confirm('移除此界面的 Word 批注附件？')) return;
      idbDelDoc(id).then(function () {
        if (state.nodes[id]) state.nodes[id].doc = null;
        renderNodes(); save();
        setStatus('已移除 Word 附件');
      }).catch(function (err) { alert('移除失败：' + err); });
    });
  }
  function downloadNodeDoc(id) {
    idbGetDoc(id).then(function (rec) {
      if (!rec || !rec.data) { alert('未找到附件数据，请重新添加 Word 文件'); return; }
      downloadArrayBuffer(rec.name || 'annotation.docx', rec.data, rec.mime);
    }).catch(function (err) { alert('下载失败：' + err); });
  }
  function openDocPreview(id) {
    previewTargetId = id;
    var meta = state.nodes[id] && state.nodes[id].doc;
    var modal = document.getElementById('docPreviewModal');
    document.getElementById('docPreviewTitle').textContent = (meta && meta.name) ? meta.name : 'Word 批注预览';
    document.getElementById('docPreviewMeta').textContent = meta
      ? ('界面：' + id + ' · ' + formatBytes(meta.size || 0) + ' · 表格与标题将尽量还原（.docx）')
      : '';
    document.getElementById('docPreviewBody').innerHTML = '正在解析 Word…';
    modal.classList.add('show');
    idbGetDoc(id).then(function (rec) {
      if (!rec || !rec.data) {
        document.getElementById('docPreviewBody').innerHTML = '<p>未找到附件数据。</p>';
        return;
      }
      if (typeof mammoth === 'undefined' || !mammoth.convertToHtml) {
        document.getElementById('docPreviewBody').innerHTML =
          '<p>预览库未加载。请点「下载原文件」用 Word / WPS 打开查看表格与分级。</p>';
        return;
      }
      return mammoth.convertToHtml({ arrayBuffer: rec.data }).then(function (result) {
        document.getElementById('docPreviewBody').innerHTML = result.value || '<p>（文档无可见内容）</p>';
      });
    }).catch(function (err) {
      document.getElementById('docPreviewBody').innerHTML = '<p>预览失败：' + escapeHtml(String(err)) + '</p>';
    });
  }
  async function collectDocsForExport() {
    var docs = {};
    var ids = Object.keys(state.nodes || {});
    for (var i = 0; i < ids.length; i++) {
      var id = ids[i];
      var meta = state.nodes[id] && state.nodes[id].doc;
      if (!meta || !meta.name) continue;
      try {
        var rec = await idbGetDoc(id);
        if (!rec || !rec.data) continue;
        docs[id] = {
          name: rec.name || meta.name,
          mime: rec.mime || meta.mime || 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          size: rec.size || meta.size || 0,
          base64: abToBase64(rec.data)
        };
      } catch (e) {}
    }
    return docs;
  }
  async function restoreDocsFromExport(docs) {
    if (!docs) return 0;
    var n = 0;
    var ids = Object.keys(docs);
    for (var i = 0; i < ids.length; i++) {
      var id = ids[i];
      var d = docs[id];
      if (!d || !d.base64) continue;
      var buf = base64ToAb(d.base64);
      await idbPutDoc(id, {
        name: d.name || 'annotation.docx',
        mime: d.mime || 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        size: d.size || buf.byteLength,
        data: buf,
        updatedAt: new Date().toISOString()
      });
      if (state.nodes[id]) {
        state.nodes[id].doc = {
          name: d.name || 'annotation.docx',
          mime: d.mime,
          size: d.size || buf.byteLength,
          updatedAt: new Date().toISOString()
        };
      }
      n++;
    }
    return n;
  }

'''

    if "DOC_DB_NAME" not in html:
        # insert before function save(
        anchor = "  function save() {"
        if anchor not in html:
            raise SystemExit("save() anchor missing")
        html = html.replace(anchor, helpers + anchor, 1)

    # Wire file input + preview modal + replace export/import
    wire = r'''
  document.getElementById('docAttachFile').onchange = function (ev) {
    var file = ev.target.files && ev.target.files[0];
    var id = attachTargetId;
    ev.target.value = '';
    attachTargetId = null;
    if (!file || !id) return;
    var lower = (file.name || '').toLowerCase();
    if (!lower.endsWith('.docx')) {
      alert('请使用 .docx 格式（Word / WPS 另存为「Word 文档 *.docx」）。旧版 .doc 需先转换。');
      return;
    }
    if (file.size > 8 * 1024 * 1024) {
      alert('单个 Word 附件请控制在 8MB 以内，便于导出备份。');
      return;
    }
    var reader = new FileReader();
    reader.onload = function () {
      var buf = reader.result;
      idbPutDoc(id, {
        name: file.name,
        mime: file.type || 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        size: file.size,
        data: buf,
        updatedAt: new Date().toISOString()
      }).then(function () {
        if (!state.nodes[id]) state.nodes[id] = { x: 0, y: 0, note: '' };
        state.nodes[id].doc = {
          name: file.name,
          mime: file.type || 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          size: file.size,
          updatedAt: new Date().toISOString()
        };
        renderNodes(); save();
        setStatus('已添加 Word 批注：' + file.name);
      }).catch(function (err) { alert('保存附件失败：' + err); });
    };
    reader.readAsArrayBuffer(file);
  };
  document.getElementById('btnDocPreviewClose').onclick = function () {
    document.getElementById('docPreviewModal').classList.remove('show');
    previewTargetId = null;
  };
  document.getElementById('btnDocPreviewDownload').onclick = function () {
    if (previewTargetId) downloadNodeDoc(previewTargetId);
  };
  document.getElementById('docPreviewModal').addEventListener('mousedown', function (ev) {
    if (ev.target.id === 'docPreviewModal') {
      document.getElementById('docPreviewModal').classList.remove('show');
      previewTargetId = null;
    }
  });

'''

    if "docAttachFile').onchange" not in html and 'docAttachFile").onchange' not in html:
        html = html.replace(
            "  document.getElementById('btnExport').onclick = function () {",
            wire + "  document.getElementById('btnExport').onclick = function () {",
            1,
        )

    # Replace export handler with async version
    old_export = """  document.getElementById('btnExport').onclick = function () {
    var blob = new Blob([JSON.stringify({
      nodes: state.nodes,
      edges: state.edges,
      cam: state.cam,
      extras: state.extras || [],
      exportedAt: new Date().toISOString()
    }, null, 2)], {type:'application/json'});
    var a=document.createElement('a'); a.href=URL.createObjectURL(blob);
    a.download='UI交互白板_含新界面_'+new Date().toISOString().slice(0,10)+'.json'; a.click();
  };"""

    new_export = """  document.getElementById('btnExport').onclick = async function () {
    setStatus('正在打包 Word 附件…');
    try {
      var docs = await collectDocsForExport();
      var payload = {
        nodes: state.nodes,
        edges: state.edges,
        cam: state.cam,
        extras: state.extras || [],
        docs: docs,
        exportedAt: new Date().toISOString()
      };
      var blob = new Blob([JSON.stringify(payload, null, 2)], {type:'application/json'});
      var a=document.createElement('a'); a.href=URL.createObjectURL(blob);
      a.download='UI交互白板_含新界面_'+new Date().toISOString().slice(0,10)+'.json'; a.click();
      var n = Object.keys(docs).length;
      setStatus('已导出 JSON' + (n ? ('（含 ' + n + ' 个 Word 附件）') : ''));
    } catch (err) {
      alert('导出失败：' + err);
      setStatus('导出失败');
    }
  };"""

    if old_export not in html:
        raise SystemExit("export handler not found")
    html = html.replace(old_export, new_export, 1)

    old_import = """  document.getElementById('importFile').onchange = function (ev) {
    var file = ev.target.files && ev.target.files[0];
    if (!file) return;
    var reader = new FileReader();
    reader.onload = function () {
      try {
        var d = JSON.parse(reader.result);
        if (d.nodes) state.nodes = d.nodes;
        if (d.edges) state.edges = d.edges;
        if (d.cam) state.cam = d.cam;
        if (d.extras) state.extras = d.extras;
        migrateLayoutState();
        ensureDefaults(); applyCam(); renderNodes(); save();
        setStatus('已导入 JSON（批注页内容已写入内置目录；布局已合并）');
      } catch (err) {
        alert('导入失败：JSON 格式不正确');
      }
      ev.target.value = '';
    };
    reader.readAsText(file, 'utf-8');
  };"""

    new_import = """  document.getElementById('importFile').onchange = function (ev) {
    var file = ev.target.files && ev.target.files[0];
    if (!file) return;
    var reader = new FileReader();
    reader.onload = async function () {
      try {
        var d = JSON.parse(reader.result);
        if (d.nodes) state.nodes = d.nodes;
        if (d.edges) state.edges = d.edges;
        if (d.cam) state.cam = d.cam;
        if (d.extras) state.extras = d.extras;
        migrateLayoutState();
        ensureDefaults();
        var docCount = await restoreDocsFromExport(d.docs || {});
        applyCam(); renderNodes(); save();
        setStatus('已导入 JSON' + (docCount ? ('（恢复 ' + docCount + ' 个 Word 附件）') : '（批注/布局已合并）'));
      } catch (err) {
        alert('导入失败：JSON 格式不正确');
      }
      ev.target.value = '';
    };
    reader.readAsText(file, 'utf-8');
  };"""

    if old_import not in html:
        raise SystemExit("import handler not found")
    html = html.replace(old_import, new_import, 1)

    # Reset should clear indexeddb docs too - best effort
    old_reset = """  document.getElementById('btnReset').onclick = function () {
    if (!confirm('重置布局与推荐连线？（批注和新建界面也会清空）')) return;
    localStorage.removeItem(STORAGE_KEY);
    state.nodes={}; state.edges=[]; state.extras=[]; state.cam={x:30,y:30,scale:0.72};
    autoLayout(); ensureDefaults(); applyCam(); renderNodes(); save();
  };"""

    new_reset = """  document.getElementById('btnReset').onclick = function () {
    if (!confirm('重置布局与推荐连线？（文本批注、Word 附件和新建界面也会清空）')) return;
    localStorage.removeItem(STORAGE_KEY);
    state.nodes={}; state.edges=[]; state.extras=[]; state.cam={x:30,y:30,scale:0.72};
    openDocDb().then(function (db) {
      var tx = db.transaction(DOC_STORE, 'readwrite');
      tx.objectStore(DOC_STORE).clear();
    }).catch(function () {});
    autoLayout(); ensureDefaults(); applyCam(); renderNodes(); save();
  };"""

    if old_reset not in html:
        raise SystemExit("reset handler not found")
    html = html.replace(old_reset, new_reset, 1)

    # applyNotesToFeatures: don't mention clearing word; update confirm if needed - optional
    html = html.replace(
        "将把各卡片黄框批注整理写入上方「功能」列表，并清空已处理批注。继续？",
        "将把各卡片短文本批注整理写入「功能」列表并清空文本批注（Word 附件保留）。继续？",
        1,
    )

    BOARD.write_text(html, encoding="utf-8")
    print("patched", BOARD.name, BOARD.stat().st_size)


if __name__ == "__main__":
    main()
