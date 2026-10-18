// ASCII-only recovery v4: remove inserted NUL bytes and fill holes using the exact edit
// history extracted from the agent transcript (tr_edits.txt) plus repo sources.
const fs = require('fs');
const path = require('path');

const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const REPORT = 'C:/Users/jiliy/Airline-CEO-Simulator/r4_report.txt';
const DECISIONS = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/decisions.json';

const gbkDec = new TextDecoder('gbk', { fatal: false });
const gbkTable = new Map();
for (let lead = 0x81; lead <= 0xFE; lead++)
  for (let trail = 0x40; trail <= 0xFE; trail++) {
    if (trail === 0x7F) continue;
    const ch = gbkDec.decode(Buffer.from([lead, trail]));
    if (ch.length === 1 && !gbkTable.has(ch)) gbkTable.set(ch, [lead, trail]);
  }
gbkTable.set('\u20AC', [0x80]);

// ---- rebuild original byte stream from the mojibake text ----
const t2 = new TextDecoder('utf-8').decode(fs.readFileSync(TARGET));
const bytes = [];
const marks = new Map(); // index -> {kind, p, cp}
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) { bytes.push(cp); continue; }
  const pair = gbkTable.get(ch);
  if (!pair) { marks.set(bytes.length, { kind: 'unrep', cp }); bytes.push(0x3F); continue; }
  bytes.push(pair[0], pair[1]);
}
const isCont = (x) => x >= 0x80 && x <= 0xBF;
const isLead3 = (x) => x >= 0xE0 && x <= 0xEF;
for (let i = 2; i < bytes.length; i++)
  if (bytes[i] === 0x3F && isLead3(bytes[i - 2]) && isCont(bytes[i - 1]) && !marks.has(i))
    marks.set(i, { kind: 'hole', p: [bytes[i - 2], bytes[i - 1]] });

// ---- drop NUL bytes (they are stray insertions) ----
const b2 = [];
const remap = new Int32Array(bytes.length + 1).fill(-1);
let nuls = 0;
for (let i = 0; i < bytes.length; i++) {
  remap[i] = b2.length;
  if (bytes[i] === 0) { nuls++; continue; }
  b2.push(bytes[i]);
}
remap[bytes.length] = b2.length;
const b = Buffer.from(b2);
const m2 = new Map();
for (const [i, mk] of marks) {
  const ni = remap[i];
  if (ni >= 0) m2.set(ni, mk);
}

// ---- corpus ----
const files = [];
(function walk(dir, depth) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git', 'recover', '_Recovery'].includes(e.name)) continue; walk(p, depth + 1); }
    else if (/\.(cs|md|txt|json)$/i.test(e.name) && p.replace(/\\/g, '/') !== TARGET) files.push(p);
  }
})(ROOT, 0);
const extra = ['C:/Users/jiliy/Airline-CEO-Simulator/tr_edits.txt'];
let corpus = '';
const sources = [];
for (const f of files.concat(extra)) {
  try { const s = new TextDecoder('utf-8').decode(fs.readFileSync(f)); corpus += '\n' + s; sources.push(f); } catch (e) { }
}
let corpusTr = '';
try { corpusTr = new TextDecoder('utf-8').decode(fs.readFileSync(extra[0])); } catch (e) { }

const uni = new Map();
for (const ch of corpus) if (/[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF]/.test(ch)) uni.set(ch, (uni.get(ch) || 0) + 1);

// ---- lenient decode with byte -> char index map ----
const R = new TextDecoder('utf-8', { fatal: false }).decode(b);
const charIndexAt = new Int32Array(b.length + 1).fill(-1);
{
  const dec = new TextDecoder('utf-8', { fatal: false });
  let ci = 0, i = 0;
  while (i < b.length) {
    let len = 1; const c = b[i];
    if (c >= 0x80) { if (c >= 0xF0) len = 4; else if (c >= 0xE0) len = 3; else if (c >= 0xC2) len = 2; }
    const slice = b.subarray(i, Math.min(b.length, i + len));
    const s = dec.decode(slice);
    for (let k = 0; k < slice.length; k++) charIndexAt[i + k] = ci;
    ci += [...s].length; i += slice.length;
  }
  charIndexAt[b.length] = ci;
}

const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const decisions = fs.existsSync(DECISIONS) ? JSON.parse(fs.readFileSync(DECISIONS, 'utf8')) : {};
const report = [];
let fixedTr = 0, fixedRepo = 0, fixedDec = 0, unresolved = 0;
const insertions = []; // extra bytes to insert after position

for (const [i, mk] of m2) {
  const manual = decisions[String(i)];
  const needBytes = mk.kind === 'unrep' ? 3 : 1;
  const okManual = (() => {
    if (!manual) return false;
    const u = Buffer.from(manual, 'utf8');
    if (u.length !== needBytes) return false;
    if (needBytes === 1 && (u[0] !== mk.p[0] || u[1] !== mk.p[1])) return false;
    return true;
  })();

  const ci = charIndexAt[i];
  const leftChars = [], rightChars = [];
  const markCharIdx = new Set();
  for (const [j, mj] of m2) { const cj = charIndexAt[j]; if (cj >= 0) markCharIdx.add(cj); }
  for (let k = ci - 1; k >= 0 && leftChars.length < 14; k--) { const ch = R[k]; if (ch === '\uFFFD') continue; leftChars.unshift(markCharIdx.has(k) ? '[\s\S]' : esc(ch)); }
  for (let k = ci + 1; k < R.length && rightChars.length < 14; k++) { const ch = R[k]; if (ch === '\uFFFD') continue; rightChars.push(markCharIdx.has(k) ? '[\s\S]' : esc(ch)); }

  const tryCorpus = (text) => {
    for (let n = 14; n >= 4; n--) {
      if (leftChars.length < n || rightChars.length < n) continue;
      const L = leftChars.slice(-n).join(''), Rr = rightChars.slice(0, n).join('');
      let re;
      try { re = new RegExp(L + '([\\s\\S])' + Rr, 'g'); } catch (e) { continue; }
      const counts = new Map();
      let m;
      while ((m = re.exec(text)) !== null) {
        const ch = m[1];
        if (ch === '\n' || ch === '\r') continue;
        const u = Buffer.from(ch, 'utf8');
        if (needBytes === 1) { if (u.length === 3 && u[0] === mk.p[0] && u[1] === mk.p[1]) counts.set(ch, (counts.get(ch) || 0) + 1); }
        else { if (u.length === 3) counts.set(ch, (counts.get(ch) || 0) + 1); }
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
  if (okManual) { chosen = manual; src = 'decision'; }
  if (!chosen) { const r = tryCorpus(corpusTr); if (r) { chosen = r.ch; src = 'transcript(n=' + r.n + ',uniq=' + r.uniq + ',hits=' + r.hits + ')'; } }
  if (!chosen) { const r = tryCorpus(corpus); if (r) { chosen = r.ch; src = 'repo(n=' + r.n + ',uniq=' + r.uniq + ',hits=' + r.hits + ')'; } }

  if (!chosen) { unresolved++; report.push('UNRESOLVED ' + i + ' kind=' + mk.kind + ' ctx=' + R.slice(Math.max(0, ci - 25), ci + 25).replace(/[\uFFFD]/g, '.')); continue; }
  if (src === 'decision') fixedDec++;
  else if (src.startsWith('transcript')) fixedTr++;
  else fixedRepo++;

  const u = Buffer.from(chosen, 'utf8');
  if (needBytes === 1) b[i] = u[2];
  else { b[i] = u[0]; insertions.push({ at: i + 1, bytes: [u[1], u[2]] }); }
  if (src !== 'decision') report.push('FIX ' + i + ' ' + src + ' -> ' + chosen + ' U+' + chosen.codePointAt(0).toString(16).toUpperCase() + ' ctx=' + R.slice(Math.max(0, ci - 25), ci + 25).replace(/[\uFFFD]/g, '.'));
}

report.push('nulsRemoved=' + nuls + ' transcriptFixes=' + fixedTr + ' repoFixes=' + fixedRepo + ' decisionFixes=' + fixedDec + ' unresolved=' + unresolved);

// apply insertions (descending order)
insertions.sort((x, y) => y.at - x.at);
let finalBuf = Buffer.from(b);
for (const ins of insertions) finalBuf = Buffer.concat([finalBuf.subarray(0, ins.at), Buffer.from(ins.bytes), finalBuf.subarray(ins.at)]);

let valid = true;
try { new TextDecoder('utf-8', { fatal: true }).decode(finalBuf); } catch (e) { valid = false; }
let holesLeft = 0;
for (let i = 2; i < finalBuf.length; i++) if (finalBuf[i] === 0x3F && isLead3(finalBuf[i - 2]) && isCont(finalBuf[i - 1])) holesLeft++;
let nulsLeft = 0;
for (const x of finalBuf) if (x === 0) nulsLeft++;
report.push('valid=' + valid + ' holesLeft=' + holesLeft + ' nulsLeft=' + nulsLeft + ' bytes=' + finalBuf.length);
fs.writeFileSync(REPORT, report.join('\n'), 'utf8');
if (valid && holesLeft === 0 && nulsLeft === 0 && unresolved === 0) {
  fs.writeFileSync(TARGET, finalBuf);
  report.push('WROTE FILE');
  fs.writeFileSync(REPORT, report.join('\n'), 'utf8');
} else {
  report.push('NOT WRITTEN (writes only when clean)');
  fs.writeFileSync(REPORT, report.join('\n'), 'utf8');
}
console.log(report.slice(-4).join('\n'));
