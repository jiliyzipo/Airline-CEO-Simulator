// ASCII-only definitive diagnostic: hex + text window for every unresolved hole and every NUL byte.
const fs = require('fs');
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const TARGET = ROOT + '/Scripts/Systems/InboxManager.cs';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/diag2.txt';

const gbkDec = new TextDecoder('gbk', { fatal: false });
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
for (const ch of t2) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) { out.push(cp); continue; }
  const pair = gbkTable.get(ch);
  if (!pair) { out.push(0x3F); continue; }
  out.push(pair[0], pair[1]);
}
const b = Buffer.from(out);

const rep = ['bytes=' + b.length];
const hexw = (i, n) => {
  const s = Math.max(0, i - n), e = Math.min(b.length, i + n + 1);
  return b.subarray(s, e).toString('hex').replace(/(..)/g, '$1 ').trim();
};
const textw = (i, n) => {
  const s = Math.max(0, i - n), e = Math.min(b.length, i + n + 1);
  const seg = Buffer.from(b.subarray(s, e));
  seg[i - s] = 0x40; // '@' marker
  return new TextDecoder('utf-8').decode(seg).replace(/\uFFFD/g, '\u00B7').replace(/\u0000/g, '@NUL@');
};

const reportTxt = fs.readFileSync('C:/Users/jiliy/Airline-CEO-Simulator/recovery_report.txt', 'utf8');
const unresolved = [...reportTxt.matchAll(/^\{"at":(\d+)/gm)].map((m) => Number(m[1]));
const nuls = [];
for (let i = 0; i < b.length; i++) if (b[i] === 0) nuls.push(i);

rep.push('=== HOLES (unresolved) ' + unresolved.length + ' ===');
for (const i of unresolved) {
  const pre = b[i - 3].toString(16) + b[i - 2].toString(16) + b[i - 1].toString(16);
  rep.push('H ' + i + ' prev3=' + pre.replace(/^0/, '') + ' | ' + textw(i, 30));
  rep.push('   hex ' + hexw(i, 12));
}
rep.push('');
rep.push('=== NULs ' + nuls.length + ' ===');
for (const i of nuls) {
  rep.push('N ' + i + ' | ' + textw(i, 30));
  rep.push('   hex ' + hexw(i, 12));
}
fs.writeFileSync(OUT, rep.join('\n'), 'utf8');
console.log('wrote ' + OUT + ' holes=' + unresolved.length + ' nuls=' + nuls.length);
