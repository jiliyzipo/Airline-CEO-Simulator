// Learn/validate a resolver for '?' holes: the hole consumes the last byte of a 3-byte
// character (X) plus the first byte of the following character (Y, normally ASCII).
const fs = require('fs');
const path = require('path');
const DIR = __dirname + '/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const h = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));

const out = [];
const stats = { total: 0, of2andOf0: 0, of2andAscii: 0, other: 0 };
const yHist = new Map();
for (const lh of h.holes) {
  for (const x of lh) {
    stats.total++;
    const b1 = x.bytes[0], b2 = x.bytes[1];
    if (b1.of === 2 && b2 && b2.of === 0) {
      stats.of2andOf0++;
      if (b2.b < 0x40 || b2.b === 0x7F) {
        stats.of2andAscii++;
        yHist.set(b2.b, (yHist.get(b2.b) || 0) + 1);
      }
    } else stats.other++;
  }
}
out.push(JSON.stringify(stats));
out.push('yHist=' + [...yHist.entries()].sort((a, b) => b[1] - a[1]).map(([c, n]) => '0x' + c.toString(16) + '(' + JSON.stringify(String.fromCharCode(c)) + '):' + n).join(' '));

// how ambiguous is X? candidates = corpus chars sharing the first two bytes
const corpus = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git'].includes(e.name)) continue; walk(p); }
    else if (/\.(cs|md|txt)$/i.test(e.name)) { try { corpus.push(fs.readFileSync(p, 'utf8')); } catch (e2) { } }
  }
})(ROOT);
const corpusText = corpus.join('\n');
const byPrefix = new Map();
for (const ch of corpusText) {
  const b = Buffer.from(ch, 'utf8');
  if (b.length === 3) {
    const k = b[0] + ',' + b[1];
    let s = byPrefix.get(k);
    if (!s) { s = new Set(); byPrefix.set(k, s); }
    s.add(ch);
  }
}
const sizes = new Map();
let noCand = 0;
for (const lh of h.holes) {
  for (const x of lh) {
    const b1 = x.bytes[0], b2 = x.bytes[1];
    if (b1.of !== 2 || !b2 || b2.of !== 0 || b2.b >= 0x40) continue;
    // prefix of X sits in the bytes before the hole: we do not have it here, so approximate
  }
}
out.push('corpusChars=' + corpusText.length + ' prefixes=' + byPrefix.size);
fs.writeFileSync(DIR + 'hole_analysis.txt', out.join('\n'), 'utf8');
console.log(out.join('\n'));
