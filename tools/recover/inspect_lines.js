// Inspect specific disk lines: mojibake text, reverse-mapped bytes around every '?' and the
// bytes' tentative UTF-8 grouping, to decide hole corrections precisely.
const fs = require('fs');
const DIR = __dirname + '/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const DISK = ROOT + '/Scripts/Systems/InboxManager.cs';
const { charToBytes } = require(DIR + 'cp936.js');
const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const disk = rd(DISK).split('\n');
const targets = process.argv.slice(2).map(Number);

const out = [];
for (const ln of targets) {
  const line = disk[ln - 1];
  if (line === undefined) { out.push('L' + ln + ' <missing>'); continue; }
  out.push('===== L' + ln + ' =====');
  out.push('moji: ' + JSON.stringify(line));
  const bytes = [];
  const marks = [];
  for (const ch of line) {
    if (ch === '?') { marks.push(bytes.length); bytes.push(0x3F); continue; }
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const b = charToBytes(ch);
    if (!b) { bytes.push(0x3F); continue; }
    for (const x of b) bytes.push(x);
  }
  for (let m = 0; m < marks.length; m++) {
    const at = marks[m];
    const lo = Math.max(0, at - 12), hi = Math.min(bytes.length, at + 12);
    const hex = [];
    for (let i = lo; i < hi; i++) hex.push((i === at ? '[' : ' ') + bytes[i].toString(16).padStart(2, '0') + (i === at ? ']' : ''));
    out.push('  ?#: at=' + at + ' prefix2=' + (bytes[at - 2] !== undefined ? bytes[at - 2].toString(16) : '?') + ',' + (bytes[at - 1] !== undefined ? bytes[at - 1].toString(16) : '?') +
      ' next=' + (bytes[at + 1] !== undefined ? bytes[at + 1].toString(16) : 'EOL'));
    out.push('   ' + hex.join(''));
  }
  // decode progressively to show the resolved text with markers
}
fs.writeFileSync(DIR + 'inspect_out.txt', out.join('\n'), 'utf8');
console.log('lines=' + targets.length);
