// ASCII-only recovery v5. Rebuilds the original UTF-8 bytes from the GBK-mojibake text,
// then fills every lost byte using (1) decisions.json, (2) the exact edit history in
// tr_edits.txt, (3) other repo sources.
const fs = require('fs');
const path = require('path');

const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const REPORT = 'C:/Users/jiliy/Airline-CEO-Simulator/r5_report.txt';
const DECISIONS = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/decisions.json';

const gbkDec = new TextDecoder('gbk', { fatal: false });
const gbkTable = new Map();
for (let lead = 0x81; lead <= 0xFE; lead++)
  for (let trail = 0x40; trail <= 0xFE; trail++) {
    if (trail === 0x7F) continue;
    const ch = gbkDec.decode(Buffer.from([lead, trail]));
    if (ch.length === 1 && !gbkTable.has(ch)) gbkTable.set(ch, [lead, trail]);
  }

const t2 = new TextDecoder('utf-8').decode(fs.readFileSync(TARGET));
const bytes = [];
const marks = new Map();
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) { bytes.push(cp); continue; }
  const pair = gbkTable.get(ch);
  if (!pair) { marks.set(bytes.length, { kind: 'unrep', cp }); bytes.push(0x3F); continue; }
  for (const x of pair) bytes.push(x);
}
const isCont = (x) => x >= 0x80 && x <= 0xBF;
const isLead3 = (x) => x >= 0xE0 && x <= 0xEF;
for (let i = 2; i < bytes.length; i++)
  if (bytes[i] === 0x3F && isLead3(bytes[i - 2]) && isCont(bytes[i - 1]) && !marks.has(i))
    marks.set(i, { kind: 'hole', p: [bytes[i - 2], bytes[i - 1]] });
const b = Buffer.from(bytes);

// corpus
const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git', 'recover', '_Recovery'].includes(e.name)) continue; walk(p); }
    else if (/\.(cs|md|txt|json)$/i.test(e.name) && p.replace(/\\/g, '/') !== TARGET) files.push(p);
  }
})(ROOT);
const trPath = 'C:/Users/jiliy/Airline-CEO-Simulator/tr_edits.txt';
let corpusTr = '';
try { corpusTr = new TextDecoder('utf-8').decode(fs.readFileSync(trPath)); } catch (e) { }
let corpusRepo = '';
for (const f of files) { try { corpusRepo += '\n' + new TextDecoder('utf-8').decode(fs.readFileSync(f)); } catch (e) { } }
const uni = new Map();
for (const ch of corpusRepo + corpusTr) if (/[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF]/.test(ch)) uni.set(ch, (uni.get(ch) || 0) + 1);

const R = new TextDecoder('utf-8', { fatal: false }).decode(b);
const charIndexAt = new Int32Array(b.length + 1).fill(-1);
{
  const dec = new TextDecoder('utf-8', { fatal: false });
  let ci = 0, i = 0;
  while (i < b.length) {
    let len = 1; const c = b[i];
    if (c >= 0x80) { if (c >= 0xF0) len = 4; else if (c >= 0xE0) len = 3; else if (c >= 0xC2) len = 2; }
    const slice = b.subarray(i, Math.min(b.length, i + len));
    for (let k = 0; k < slice.length; k++) charIndexAt[i + k] = ci;
    ci += [...dec.decode(slice)].length; i += slice.length;
  }
  charIndexAt[b.length] = ci;
}
const markCharIdx = new Map();
for (const [j] of marks) { const cj = charIndexAt[j]; if (cj >= 0) markCharIdx.set(cj, j); }

const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const decisions = fs.existsSync(DECISIONS) ? JSON.parse(fs.readFileSync(DECISIONS, 'utf8')) : {};

const report = [];
let fDec = 0, fTr = 0, fRepo = 0, open = 0;
const inserts = [];

for (const [i, mk] of marks) {
  const need = mk.kind === 'unrep' ? 3 : 1;
  const manual = decisions[String(i)];
  const okManual = (() => {
    if (!manual) return false;
    const u = Buffer.from(manual, 'utf8');
    if (u.length !== need) return false;
    if (need === 1 && (u[0] !== mk.p[0] || u[1] !== mk.p[1])) return false;
    return true;
  })();

  const ctxOf = (start, end) => {
    let s = new TextDecoder('utf-8', { fatal: false }).decode(b.subarray(start, end));
    s = s.replace(/(?:\uFFFD)+\??/g, '\u0001');
    const parts = [];
    for (const ch of s) {
      if (ch === '\u0001') { parts.push(need === 1 ? '([\\s\\S])' : '([\\s\\S])'); continue; }
      if (ch === '\r') { parts.push('\\r?'); continue; }
      parts.push(esc(ch));
    }
    return parts;
  };
  const onlyCtx = (parts) => parts.map((p) => (p === '([\\s\\S])' ? '[\\s\\S]' : p));
  const LParts = ctxOf(Math.max(0, i - 48), mk.kind === 'hole' ? i - 2 : i);
  const RParts = ctxOf(i + 1, Math.min(b.length, i + 48));

  const search = (text) => {
    for (let n = 16; n >= 4; n--) {
      if (LParts.length < n || RParts.length < n) continue;
      let re, midIdx;
      const L = onlyCtx(LParts.slice(-n)).join('');
      const Rr = RParts.slice(0, n).map((p) => (p === '([\\s\\S])' ? '[\\s\\S]' : p)).join('');
      try { re = new RegExp(L + '([\\s\\S])' + Rr, 'g'); } catch (e) { continue; }
      const counts = new Map();
      let m;
      while ((m = re.exec(text)) !== null) {
        const ch = m[m.length - 1];
        if (ch !== '\n' && ch !== '\r') {
          const u = Buffer.from(ch, 'utf8');
          if (need === 1 ? (u.length === 3 && u[0] === mk.p[0] && u[1] === mk.p[1]) : u.length === 3)
            counts.set(ch, (counts.get(ch) || 0) + 1);
        }
        if (m.index === re.lastIndex) re.lastIndex++;
      }
      if (counts.size) {
        let best = null, bs = -1;
        for (const ch of counts.keys()) { const s = counts.get(ch) * 1000 + Math.min(999, uni.get(ch) || 0); if (s > bs) { bs = s; best = ch; } }
        return { ch: best, n, uniq: counts.size, hits: counts.get(best) };
      }
    }
    return null;
  };

  let chosen = null, src = '';
  if (okManual) { chosen = manual; src = 'decision'; fDec++; }
  else { const r = search(corpusTr); if (r) { chosen = r.ch; src = 'tr(n=' + r.n + ',u=' + r.uniq + ',h=' + r.hits + ')'; fTr++; } }
  if (!chosen) { const r = search(corpusRepo); if (r) { chosen = r.ch; src = 'repo(n=' + r.n + ',u=' + r.uniq + ',h=' + r.hits + ')'; fRepo++; } }
  if (!chosen) { open++; report.push('OPEN ' + i + ' kind=' + mk.kind + (mk.cp !== undefined ? ' cp=U+' + mk.cp.toString(16).toUpperCase() : ' pre=' + mk.p.map((x) => x.toString(16)).join(' ')) + ' ctx=' + R.slice(Math.max(0, ci - 30), ci + 30).replace(/[\uFFFD]/g, '.')); continue; }

  const u = Buffer.from(chosen, 'utf8');
  if (need === 1) b[i] = u[2];
  else { b[i] = u[0]; inserts.push({ at: i + 1, bytes: [u[1], u[2]] }); }
  if (src !== 'decision') report.push('FIX ' + i + ' ' + src + ' -> ' + chosen + ' U+' + chosen.codePointAt(0).toString(16).toUpperCase() + ' | ' + R.slice(Math.max(0, ci - 30), ci + 30).replace(/[\uFFFD]/g, '.'));
}

const head = ['bytes=' + b.length + ' marks=' + marks.size + ' decision=' + fDec + ' transcript=' + fTr + ' repo=' + fRepo + ' open=' + open];
inserts.sort((x, y) => y.at - x.at);
let fb = Buffer.from(b);
for (const ins of inserts) fb = Buffer.concat([fb.subarray(0, ins.at), Buffer.from(ins.bytes), fb.subarray(ins.at)]);
let holesLeft = 0;
for (let i = 2; i < fb.length; i++) if (fb[i] === 0x3F && isLead3(fb[i - 2]) && isCont(fb[i - 1])) holesLeft++;
let valid = true;
try { new TextDecoder('utf-8', { fatal: true }).decode(fb); } catch (e) { valid = false; }
head.push('finalBytes=' + fb.length + ' valid=' + valid + ' holesLeft=' + holesLeft + ' inserts=' + inserts.length);
const fin = head.concat(report).join('\n');
fs.writeFileSync(REPORT, fin, 'utf8');
if (valid && holesLeft === 0 && open === 0) { fs.writeFileSync(TARGET, fb); fs.appendFileSync(REPORT, '\nWROTE FILE'); }
else fs.appendFileSync(REPORT, '\nNOT WRITTEN');
console.log(head.join('\n'));
