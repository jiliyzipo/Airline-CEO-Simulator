// 极简 Markdown → 单文件 HTML（只覆盖本仓库 handoff 文档用到的语法子集）。
// 用法：node tools/tmp/md2html.mjs <in.md> <out.html> [标题]
import { readFileSync, writeFileSync } from 'node:fs';

const [inPath, outPath, titleArg] = process.argv.slice(2);
if (!inPath || !outPath) {
  console.error('用法: node tools/tmp/md2html.mjs <in.md> <out.html> [标题]');
  process.exit(2);
}

const src = readFileSync(inPath, 'utf8').replace(/\r\n/g, '\n');
const lines = src.split('\n');

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// 行内：`code` 先占位，避免其中的 * | 被后续规则误伤
const inline = (s) => {
  const codes = [];
  let t = s.replace(/`([^`]+)`/g, (_, c) => {
    codes.push(c);
    return `\u0000${codes.length - 1}\u0000`;
  });
  t = esc(t);
  t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  t = t.replace(/\u0000(\d+)\u0000/g, (_, i) => `<code>${esc(codes[+i])}</code>`);
  return t;
};

const isTableRow = (l) => /^\s*\|.*\|\s*$/.test(l);
const isTableSep = (l) => /^\s*\|[\s:|-]+\|\s*$/.test(l) && l.includes('-');

const out = [];
let i = 0;
let para = [];

const flushPara = () => {
  if (para.length) {
    out.push(`<p>${inline(para.join(' '))}</p>`);
    para = [];
  }
};

const cells = (l) => l.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim());

while (i < lines.length) {
  const line = lines[i];

  // 代码块
  if (/^\s*```/.test(line)) {
    flushPara();
    const buf = [];
    i++;
    while (i < lines.length && !/^\s*```/.test(lines[i])) buf.push(lines[i++]);
    i++;
    out.push(`<pre><code>${esc(buf.join('\n'))}</code></pre>`);
    continue;
  }

  // 表格
  if (isTableRow(line) && i + 1 < lines.length && isTableSep(lines[i + 1])) {
    flushPara();
    const head = cells(line);
    const align = cells(lines[i + 1]).map((c) => {
      const l = c.startsWith(':'), r = c.endsWith(':');
      return l && r ? 'center' : r ? 'right' : l ? 'left' : '';
    });
    i += 2;
    const body = [];
    while (i < lines.length && isTableRow(lines[i])) body.push(cells(lines[i++]));
    const th = head
      .map((c, k) => `<th${align[k] ? ` style="text-align:${align[k]}"` : ''}>${inline(c)}</th>`)
      .join('');
    const tr = body
      .map((row) => '<tr>' + row.map((c, k) => `<td${align[k] ? ` style="text-align:${align[k]}"` : ''}>${inline(c)}</td>`).join('') + '</tr>')
      .join('\n');
    out.push(`<div class="tw"><table><thead><tr>${th}</tr></thead><tbody>\n${tr}\n</tbody></table></div>`);
    continue;
  }

  // 标题
  const h = line.match(/^(#{1,6})\s+(.*)$/);
  if (h) {
    flushPara();
    const lv = h[1].length;
    out.push(`<h${lv}>${inline(h[2])}</h${lv}>`);
    i++;
    continue;
  }

  // 分隔线
  if (/^\s*(---|\*\*\*)\s*$/.test(line)) {
    flushPara();
    out.push('<hr>');
    i++;
    continue;
  }

  // 引用
  if (/^\s*>\s?/.test(line)) {
    flushPara();
    const buf = [];
    while (i < lines.length && /^\s*>\s?/.test(lines[i])) {
      buf.push(lines[i].replace(/^\s*>\s?/, ''));
      i++;
    }
    // 引用内的列表项单独处理
    const inner = [];
    const items = [];
    for (const b of buf) {
      const m = b.match(/^\s*[-*]\s+(.*)$/);
      if (m) items.push(m[1]);
      else {
        if (items.length) {
          inner.push('<ul>' + items.map((t) => `<li>${inline(t)}</li>`).join('') + '</ul>');
          items.length = 0;
        }
        if (b.trim()) inner.push(`<p>${inline(b)}</p>`);
      }
    }
    if (items.length) inner.push('<ul>' + items.map((t) => `<li>${inline(t)}</li>`).join('') + '</ul>');
    out.push(`<blockquote>${inner.join('')}</blockquote>`);
    continue;
  }

  // 列表
  if (/^\s*[-*]\s+/.test(line)) {
    flushPara();
    const items = [];
    while (i < lines.length && /^\s*[-*]\s+/.test(lines[i])) {
      items.push(lines[i].replace(/^\s*[-*]\s+/, ''));
      i++;
    }
    out.push('<ul>' + items.map((t) => `<li>${inline(t)}</li>`).join('') + '</ul>');
    continue;
  }

  // 空行
  if (!line.trim()) {
    flushPara();
    i++;
    continue;
  }

  para.push(line.trim());
  i++;
}
flushPara();

const bodyHtml = out.join('\n');
const title = titleArg || (lines.find((l) => /^#\s/.test(l)) || '# 文档').replace(/^#\s+/, '');

const html = `<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(title)}</title>
<style>
:root{--bg:#0d1b2f;--panel:#142943;--line:#22364f;--fg:#dbe6f4;--mute:#8aa3bf;--green:#4ce39e;--blue:#4a86e8;--warn:#e4a0a7}
*{box-sizing:border-box}
body{margin:0;padding:0;background:var(--bg);color:var(--fg);
  font:15px/1.75 -apple-system,'Segoe UI','Microsoft YaHei',sans-serif}
main{max-width:1080px;margin:0 auto;padding:34px 32px 90px}
h1{font-size:26px;line-height:1.35;margin:0 0 18px;letter-spacing:.3px;color:#eef5ff}
h2{font-size:19px;margin:44px 0 14px;padding-bottom:8px;border-bottom:1px solid var(--line);color:#cfe4fb}
h3{font-size:16px;margin:30px 0 10px;color:#bcd8f5}
h4{font-size:15px;margin:22px 0 8px;color:#a8c8ea}
p{margin:12px 0}
strong{color:#eff6ff;font-weight:600}
code{background:#1b3350;padding:1.5px 6px;border-radius:4px;font-size:13px;color:#9ec9f0;
  font-family:'Cascadia Mono',Consolas,monospace}
pre{background:#0f2035;border:1px solid var(--line);border-radius:8px;padding:14px 16px;overflow:auto;margin:14px 0}
pre code{background:none;padding:0;font-size:12.5px;line-height:1.65;color:#b9d4ee}
blockquote{background:#122238;border-left:3px solid var(--blue);border-radius:0 8px 8px 0;
  margin:16px 0;padding:12px 18px;color:#b7c9dd}
blockquote p{margin:6px 0}
blockquote ul{margin:8px 0;padding-left:22px}
ul,ol{margin:10px 0;padding-left:24px}
li{margin:5px 0}
hr{border:0;border-top:1px solid var(--line);margin:34px 0}
.tw{overflow:auto;margin:16px 0;border:1px solid var(--line);border-radius:8px}
table{border-collapse:separate;border-spacing:0;width:100%;font-size:13.5px;
  font-variant-numeric:tabular-nums}
thead th{background:#192f50;color:#cddff4;font-weight:600;font-size:13px;padding:9px 12px;
  border-bottom:1px solid var(--line);white-space:nowrap;position:sticky;top:0}
tbody td{padding:8px 12px;border-bottom:1px solid #1a2c44;vertical-align:top}
tbody tr:nth-child(even) td{background:#111f33}
tbody tr:hover td{background:#1a3049}
tbody tr:last-child td{border-bottom:0}
@media (max-width:700px){main{padding:20px 16px 70px}body{font-size:14px}}
</style></head>
<body><main>
${bodyHtml}
</main></body></html>
`;

writeFileSync(outPath, html, 'utf8');
console.log(`OK ${outPath}  (${(html.length / 1024).toFixed(1)} KB)`);
