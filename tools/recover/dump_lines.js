// ASCII-only diagnostic: dump each unresolved hole's full line with markers.
const fs = require('fs');
const path = require('path');
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/recovery_lines.txt';

const gbkDec = new TextDecoder('gbk', { fatal: false });

// find byte(s) that decode to U+E7C8
const hits = [];
for (let x = 0; x <= 0xFF; x++) {
  const ch = gbkDec.decode(Buffer.from([x]));
  if (ch.length === 1 && ch.codePointAt(0) === 0xE7C8) hits.push('single 0x' + x.toString(16));
}
for (let lead = 0x81; lead <= 0xFE; lead++) {
  for (const trail of [0x7F, 0x40, 0x41]) {
    const ch = gbkDec.decode(Buffer.from([lead, trail]));
    if (ch.length === 1 && ch.codePointAt(0) === 0xE7C8) hits.push('pair ' + lead.toString(16) + ' ' + trail.toString(16));
  }
}
fs.writeFileSync(OUT, 'U+E7C8 sources: ' + (hits.join(', ') || 'none found') + '\n\n', 'utf8');

const gbkTable = new Map();
for (let lead = 0x81; lead <= 0xFE; lead++)
  for (let trail = 0x40; trail <= 0xFE; trail++) {
    if (trail === 0x7F) continue;
    const ch = gbkDec.decode(Buffer.from([lead, trail]));
    if (ch.length === 1 && !gbkTable.has(ch)) gbkTable.set(ch, [lead, trail]);
  }
gbkTable.set('\u20AC', [0x80]);

const t2 = new TextDecoder('utf-8').decode(fs.readFileSync(TARGET));
const out = [];
const badAt = new Set();
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) { out.push(cp); continue; }
  const pair = gbkTable.get(ch);
  if (!pair) { badAt.add(out.length); out.push(0x3F); continue; }
  out.push(pair[0], pair[1]);
}
const b = Buffer.from(out);

const isCont = (x) => x >= 0x80 && x <= 0xBF;
const isLead3 = (x) => x >= 0xE0 && x <= 0xEF;
const marks = new Map();
for (let i = 2; i < b.length; i++) {
  if (b[i] !== 0x3F) continue;
  if (isLead3(b[i - 2]) && isCont(b[i - 1])) marks.set(i, 'h' + b[i - 2].toString(16) + b[i - 1].toString(16));
}
for (const i of badAt) marks.set(i, 'UNREP');

// group by line
const lines = [];
let lineStart = 0;
for (let i = 0; i <= b.length; i++) {
  if (i === b.length || b[i] === 0x0A) {
    lines.push([lineStart, i]);
    lineStart = i + 1;
  }
}

const rep = [];
let unresolvedIdx = 0;
const unresolvedText = fs.readFileSync('C:/Users/jiliy/Airline-CEO-Simulator/recovery_report.txt', 'utf8');
const unresolvedList = [];
for (const m of unresolvedText.matchAll(/^\{"at":(\d+)/gm)) unresolvedList.push({ at: Number(m[1]) });
const want = new Set(unresolvedList.filter((x) => typeof x.at === 'number').map((x) => x.at));

for (let li = 0; li < lines.length; li++) {
  const [s, e] = lines[li];
  const inLine = [];
  for (let i = s; i < e; i++) if (marks.has(i) && want.has(i)) inLine.push(i);
  if (!inLine.length) continue;
  rep.push('---- line ' + (li + 1) + ' ----');
  const bytes = [];
  for (let i = s; i < e; i++) {
    if (marks.has(i) && want.has(i)) {
      bytes.push(Buffer.from('<<' + marks.get(i) + ':' + i + '>>', 'utf8'));
      // 跳过该 hole 序列的延续字节，避免重复标
      if (marks.get(i) !== 'UNREP') { i += 0; }
    } else if (b[i] === 0) {
      bytes.push(Buffer.from('<NUL>', 'utf8'));
    } else {
      bytes.push(Buffer.from([b[i]]));
    }
  }
  let text = Buffer.concat(bytes).toString('utf8');
  text = text.replace(/\uFFFD/g, '\u00B7');
  rep.push(text);
  rep.push('');
}
fs.appendFileSync(OUT, rep.join('\n'), 'utf8');
console.log('wrote ' + OUT);
console.log(rep.slice(0, 3).join('\n'));
