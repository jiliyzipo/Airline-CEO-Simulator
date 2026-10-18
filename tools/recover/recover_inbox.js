// ASCII-only recovery tool v3.
// Repairs a file damaged by a GBK round-trip: rebuilds the original UTF-8 bytes,
// auto-fixes holes that can be inferred, and applies a manual decision table
// (tools/recover/decisions.json: { "<byteIndex>": "<char>" }) for the rest.
const fs = require('fs');
const path = require('path');

const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const REPORT = 'C:/Users/jiliy/Airline-CEO-Simulator/recovery_report.txt';
const DETAIL = 'C:/Users/jiliy/Airline-CEO-Simulator/recovery_detail.txt';
const DECISIONS = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/decisions.json';

// ---------- cp936 encoder ----------
const gbkDec = new TextDecoder('gbk', { fatal: false });
const gbkTable = new Map();
for (let lead = 0x81; lead <= 0xFE; lead++)
  for (let trail = 0x40; trail <= 0xFE; trail++) {
    if (trail === 0x7F) continue;
    const ch = gbkDec.decode(Buffer.from([lead, trail]));
    if (ch.length === 1 && !gbkTable.has(ch)) gbkTable.set(ch, [lead, trail]);
  }
gbkTable.set('\u20AC', [0x80]);

const current = fs.readFileSync(TARGET);
const t2 = new TextDecoder('utf-8').decode(current);
const outBytes = [];
const badChars = [];
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) { outBytes.push(cp); continue; }
  const pair = gbkTable.get(ch);
  if (!pair) { badChars.push({ cp, at: outBytes.length }); outBytes.push(0x3F); continue; }
  outBytes.push(pair[0], pair[1]);
}
const b = Buffer.from(outBytes);

const isCont = (x) => x >= 0x80 && x <= 0xBF;
const isLead3 = (x) => x >= 0xE0 && x <= 0xEF;
const holes = [];
for (let i = 2; i < b.length; i++) {
  if (b[i] !== 0x3F) continue;
  if (isLead3(b[i - 2]) && isCont(b[i - 1])) holes.push({ i, p: [b[i - 2], b[i - 1]] });
}
for (const bc of badChars) holes.push({ i: bc.at, p: [0, 0], unrep: true, cp: bc.cp });

// lenient decode + byte->char map
const R0 = new TextDecoder('utf-8', { fatal: false }).decode(b);
const charIndexAt = new Int32Array(b.length + 1).fill(-1);
{
  let ci = 0, i = 0;
  const dec = new TextDecoder('utf-8', { fatal: false });
  while (i < b.length) {
    let len = 1;
    const c = b[i];
    if (c >= 0x80) { if (c >= 0xF0) len = 4; else if (c >= 0xE0) len = 3; else if (c >= 0xC2) len = 2; else len = 1; }
    const slice = b.subarray(i, Math.min(b.length, i + len));
    const s = dec.decode(slice);
    for (let k = 0; k < slice.length; k++) charIndexAt[i + k] = ci;
    ci += [...s].length;
    i += slice.length;
  }
  charIndexAt[b.length] = ci;
}

// corpus + stats
const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git', 'recover'].includes(e.name)) continue; walk(p); }
    else if (/\.(cs|md|txt|json)$/i.test(e.name) && p.replace(/\\/g, '/') !== TARGET) files.push(p);
  }
})(ROOT);
const docs = 'C:/Users/jiliy/Airline-CEO-Simulator/docs';
if (fs.existsSync(docs)) (function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p); else if (/\.(md|txt|json|cs)$/i.test(e.name)) files.push(p);
  }
})(docs);
let corpus = '';
for (const f of files) { try { corpus += '\n' + new TextDecoder('utf-8').decode(fs.readFileSync(f)); } catch (e) { } }

const uni = new Map(), bi = new Map();
const isHan = (ch) => /[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF\u2000-\u206F\u2190-\u21FF\u2200-\u22FF]/.test(ch);
{
  let prev = null;
  for (const ch of corpus) {
    if (isHan(ch)) { uni.set(ch, (uni.get(ch) || 0) + 1); if (prev !== null) { const k = prev + ch; bi.set(k, (bi.get(k) || 0) + 1); } prev = ch; }
    else prev = null;
  }
}

const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const hard = new Map([['E380', '\u3000'], ['E289', '\u2248'], ['E286', '\u2192'], ['E28C', '\u2191']]);
const decisions = fs.existsSync(DECISIONS) ? JSON.parse(fs.readFileSync(DECISIONS, 'utf8')) : {};

let fixedAuto = 0, fixedManual = 0;
const unresolved = [];

for (const h of holes) {
  const manual = decisions[String(h.i)];
  if (manual) {
    const u = Buffer.from(manual, 'utf8');
    if (u.length === 3 && h.p[0] === u[0] && h.p[1] === u[1]) { b[h.i] = u[2]; fixedManual++; continue; }
    if (h.unrep) { /* unrepresentable: handled below via reconstruction */ }
  }
  if (h.unrep) { unresolved.push({ at: h.i, unrep: true, cp: h.cp }); continue; }

  const ci = charIndexAt[h.i];
  const scanLeft = (from, n) => { const o = []; let k = from; while (k >= 0 && o.length < n) { const ch = R0[k]; if (ch !== '\uFFFD' && ch !== '?') o.unshift(ch); k--; } return o.join(''); };
  const scanRight = (from, n) => { const o = []; let k = from; while (k < R0.length && o.length < n) { const ch = R0[k]; if (ch !== '\uFFFD' && ch !== '?') o.push(ch); k++; } return o.join(''); };
  const left = scanLeft(ci - 1, 6), right = scanRight(ci + 1, 6);
  const L1 = left.slice(-1), R1 = right.slice(0, 1);

  const okBytes = (ch) => { const u = Buffer.from(ch, 'utf8'); return (u.length === 3 && u[0] === h.p[0] && u[1] === h.p[1]) ? u : null; };
  const score = (ch) => { if (!isHan(ch)) return -1; let s = Math.min(200, uni.get(ch) || 0) * 0.02; if (L1) s += (bi.get(L1 + ch) || 0); if (R1) s += (bi.get(ch + R1) || 0); return s; };

  let best = null, bestScore = -1, secondScore = -1;
  for (let c3 = 0x80; c3 <= 0xBF; c3++) {
    const u = Buffer.from([h.p[0], h.p[1], c3]);
    const ch = new TextDecoder('utf-8').decode(u);
    if (ch.length !== 1) continue;
    const s = score(ch);
    if (s > bestScore) { secondScore = bestScore; bestScore = s; best = { ch, u, s }; }
    else if (s > secondScore) secondScore = s;
  }
  let chosen = null;
  for (const [ln, rn] of [[4, 4], [3, 3], [2, 2]]) {
    if (left.length < ln || right.length < rn) continue;
    const re = new RegExp(esc(left.slice(-ln)) + '([\\s\\S])' + esc(right.slice(0, rn)), 'g');
    const counts = new Map();
    let m;
    while ((m = re.exec(corpus)) !== null) { const u = okBytes(m[1]); if (u) counts.set(m[1], (counts.get(m[1]) || 0) + 1); if (m.index === re.lastIndex) re.lastIndex++; }
    if (counts.size >= 1) {
      let bch = null, bs = -1;
      for (const ch of counts.keys()) { const s = score(ch) * 10 + counts.get(ch); if (s > bs) { bs = s; bch = ch; } }
      chosen = { ch: bch, u: okBytes(bch), src: 'corpus' + ln + 'x' + counts.size };
      break;
    }
  }
  if (!chosen) {
    const key = h.p[0].toString(16).toUpperCase().padStart(2, '0') + h.p[1].toString(16).toUpperCase().padStart(2, '0');
    const hc = hard.get(key);
    if (hc && okBytes(hc)) chosen = { ch: hc, u: okBytes(hc), src: 'hard' };
  }
  if (!chosen && best && bestScore > 0 && bestScore > secondScore * 1.15) chosen = { ch: best.ch, u: best.u, src: 'ngram' };
  if (chosen) { b[h.i] = chosen.u[2]; fixedAuto++; continue; }

  // collect detail for manual decision
  const cands = [];
  for (let c3 = 0x80; c3 <= 0xBF; c3++) {
    const u = Buffer.from([h.p[0], h.p[1], c3]);
    const ch = new TextDecoder('utf-8').decode(u);
    if (ch.length !== 1) continue;
    cands.push(ch + '(U+' + ch.codePointAt(0).toString(16).toUpperCase() + ',n=' + (uni.get(ch) || 0) + ')');
  }
  const ctxRaw = R0.slice(Math.max(0, ci - 60), ci + 60)
    .replace(/[\uFFFD]/g, '_').replace(/\u0000/g, '<NUL>');
  unresolved.push({ at: h.i, prefix: h.p[0].toString(16) + ' ' + h.p[1].toString(16), ctx: ctxRaw, cands: cands.join(' ') });
}

// ---- unrepresentable-char holes: original byte was a lone cp936 byte (1 byte) ----
// find which single byte decodes to the PUA char, then try to splice it back.
const singleMap = new Map();
for (let x = 0x80; x <= 0xFF; x++) {
  const ch = gbkDec.decode(Buffer.from([x]));
  if (ch.length === 1 && !singleMap.has(ch)) singleMap.set(ch, x);
}
for (const u of unresolved) {
  if (!u.unrep) continue;
  const ch = String.fromCodePoint(u.cp);
  const byte = singleMap.get(ch);
  u.singleByte = byte !== undefined ? byte : null;
}

const report = [], detail = [];
report.push('bytes=' + b.length + ' holes=' + holes.length + ' autoFixed=' + fixedAuto + ' manualFixed=' + fixedManual + ' unresolved=' + unresolved.filter((x) => !x.unrep).length + ' unrep=' + unresolved.filter((x) => x.unrep).length);
report.push('');
report.push('--- unresolved ---');
for (const u of unresolved) {
  report.push(JSON.stringify(u).slice(0, 400));
  detail.push('=== byteIndex=' + u.at + (u.unrep ? '  (unrepresentable U+' + u.cp.toString(16).toUpperCase() + ', singleByte=' + u.singleByte + ')' : '  prefix=' + u.prefix));
  if (u.ctx) detail.push('   ctx: ' + u.ctx);
  if (u.cands) detail.push('   cands: ' + u.cands);
  detail.push('');
}

let valid = true, holesLeft = 0;
try { new TextDecoder('utf-8', { fatal: true }).decode(b); } catch (e) { valid = false; }
for (let i = 2; i < b.length; i++)
  if (b[i] === 0x3F && isLead3(b[i - 2]) && isCont(b[i - 1])) holesLeft++;

// NUL diagnostics
let nuls = 0;
for (let i = 0; i < b.length; i++) if (b[i] === 0) nuls++;
report.push('');
report.push('strict utf8 valid=' + valid + ' holesLeft=' + holesLeft + ' NULbytes=' + nuls + ' unresolvedTotal=' + unresolved.length);

fs.writeFileSync(REPORT, report.join('\n'), 'utf8');
fs.writeFileSync(DETAIL, detail.join('\n'), 'utf8');
if (valid && holesLeft === 0) { fs.writeFileSync(TARGET, b); console.log('WROTE. bytes=' + b.length); }
else console.log('not written. valid=' + valid + ' holesLeft=' + holesLeft + ' files=' + REPORT + ' ' + DETAIL);
console.log(report.join('\n').slice(0, 400));
