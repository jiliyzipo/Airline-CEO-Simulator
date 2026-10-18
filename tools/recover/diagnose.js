// ASCII-only diagnostic: precise context for every unresolved hole, NUL and UNREP char.
const fs = require('fs');
const path = require('path');
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/diagnose.txt';

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

const out = [];
const srcOff = []; // for each output byte, file byte offset (approx)
let fileOff = 0;
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  const nb = Buffer.byteLength(ch, 'utf8');
  if (cp < 0x80) { out.push(cp); for (let k = 0; k < 1; k++) srcOff.push(fileOff); }
  else {
    const pair = gbkTable.get(ch);
    if (!pair) { out.push(0x3F); srcOff.push(fileOff); }
    else { out.push(pair[0]); srcOff.push(fileOff); out.push(pair[1]); srcOff.push(fileOff + 1); }
  }
  fileOff += nb;
}
const b = Buffer.from(out);
const isCont = (x) => x >= 0x80 && x <= 0xBF;
const isLead3 = (x) => x >= 0xE0 && x <= 0xEF;

const R0 = new TextDecoder('utf-8', { fatal: false }).decode(b);
const sp = (s, n) => s.replace(/\s+/g, ' ').slice(-n);

const rep = [];
rep.push('bytes=' + b.length + ' chars=' + t2.length);

// 1) holes that stay unresolved (use report list)
const reportTxt = fs.readFileSync('C:/Users/jiliy/Airline-CEO-Simulator/recovery_report.txt', 'utf8');
const unresolved = [...reportTxt.matchAll(/^\{"at":(\d+)/gm)].map((m) => Number(m[1]));
rep.push('unresolved holes=' + unresolved.length);
for (const at of unresolved) {
  const left = R0.slice(Math.max(0, at - 90), at).replace(/[\uFFFD\u0000]/g, '.');
  const right = R0.slice(at + 1, at + 60).replace(/[\uFFFD\u0000]/g, '.');
  rep.push('#HOLE ' + at + ' pre=' + b[at - 3].toString(16) + b[at - 2].toString(16) + ' | ' + sp(left, 45) + '[?]' + sp(right, 40));
}

// 2) NUL bytes
const nuls = [];
for (let i = 0; i < b.length; i++) if (b[i] === 0) nuls.push(i);
rep.push('NUL bytes=' + nuls.length);
for (const at of nuls) {
  const left = R0.slice(Math.max(0, at - 60), at).replace(/[\uFFFD]/g, '.');
  const right = R0.slice(at + 1, at + 40).replace(/[\uFFFD]/g, '.');
  rep.push('#NUL ' + at + ' | ' + sp(left, 30) + '<NUL>' + sp(right, 25));
}

// 3) UNREP chars: locate by scanning t2 char index -> file offset
const badList = [];
{
  let idx = 0;
  for (const ch of t2) {
    const cp = ch.codePointAt(0);
    if (cp >= 0x80 && !gbkTable.get(ch)) badList.push({ cp, charIdx: idx, off: fileOffOf(t2, idx) });
    idx++;
  }
}
function fileOffOf(s, charIdx) {
  let off = 0, i = 0;
  for (const ch of s) { if (i === charIdx) return off; off += Buffer.byteLength(ch, 'utf8'); i++; }
  return off;
}
rep.push('UNREP chars=' + badList.length);
for (const bd of badList) {
  const s = Math.max(0, bd.off - 60), e = Math.min(current.length, bd.off + 60);
  const seg = current.subarray(s, e);
  rep.push('#UNREP U+' + bd.cp.toString(16).toUpperCase() + ' fileOff=' + bd.off);
  rep.push('   hex: ' + seg.toString('hex').replace(/(..)/g, '$1 '));
  rep.push('   str: ' + JSON.stringify(new TextDecoder('utf8').decode(seg)));
  rep.push('   gbk: ' + JSON.stringify(gbkDec.decode(seg)));
}

// 4) brute force which byte pair decodes to the UNREP codepoints
const wanted = [...new Set(badList.map((x) => x.cp))];
for (const w of wanted) {
  const found = [];
  for (let x = 0; x <= 0xFF; x++)
    for (let y = 0; y <= 0xFF; y++) {
      const ch = gbkDec.decode(Buffer.from([x, y]));
      if (ch.length === 1 && ch.codePointAt(0) === w) found.push(x.toString(16) + ' ' + y.toString(16));
    }
  rep.push('#PAIR U+' + w.toString(16).toUpperCase() + ' <- ' + found.join(', '));
}

fs.writeFileSync(OUT, rep.join('\n'), 'utf8');
console.log('wrote ' + OUT + ' (' + rep.length + ' lines)');
