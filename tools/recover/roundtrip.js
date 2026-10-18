// Ground-truth experiment: mangle the pristine replay, then run the reverse-mapping
// recovery on every mangled line and compare against the pristine line. This tells us
// exactly what information a '?' hole destroys (how many bytes, which ones).
const fs = require('fs');
const DIR = __dirname + '/';
const { rev, charToBytes } = require(DIR + 'cp936.js');
const map = JSON.parse(fs.readFileSync(DIR + 'mangle_map.json', 'utf8'));

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');

const report = [];
let holes = 0, asciiLost = 0, charLost2 = 0, charUnknown = 0, multiHole = 0;
const asciiHist = new Map();

// reverse-map a mangled line into bytes, marking each '?' as a hole
function toBytes(line) {
  const bytes = [];
  const holes = [];
  for (const ch of line) {
    if (ch === '?') { holes.push(bytes.length); bytes.push(0x3F); continue; }
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const b = charToBytes(ch);
    if (!b) { bytes.push(0x3F); continue; }
    for (const x of b) bytes.push(x);
  }
  return { bytes, holes };
}

for (let k = 0; k < mangled.length; k++) {
  const srcs = map.map[k] || [];
  let line = mangled[k];
  const truth = srcs.map((j) => replay[j]).join('\n');
  // Try to recover: replace the '?' holes using the mangler's own inverse
  const { bytes, holes: hs } = toBytes(line);
  if (!hs.length) continue;
  holes += hs.length;
  if (hs.length > 1) multiHole++;
  // ground truth: run the mangler forward again on the true line(s) to find where the bytes went
  // instead, examine the true line's utf-8 bytes and compare with what we can see
  const trueBytes = Buffer.from(truth, 'utf8');
  // find the alignment: everything before the hole should match; check the true bytes around it
  const info = [];
  for (const at of hs) {
    // bytes[at] is the marker; the consumed pair was (trueByte[at], trueByte[at+1])?
    const lead = trueBytes[at], trail = trueBytes[at + 1];
    info.push('at=' + at + ' lead=0x' + (lead === undefined ? '??' : lead.toString(16)) +
      ' trail=' + (trail === undefined ? '??' : '0x' + trail.toString(16) + ' (' + JSON.stringify(String.fromCharCode(trail)) + ')'));
    if (trail !== undefined && trail < 0x40) {
      asciiLost++;
      asciiHist.set(trail, (asciiHist.get(trail) || 0) + 1);
    }
    if (lead !== undefined && lead >= 0xC2 && lead <= 0xDF) charLost2++;
    if (lead !== undefined && (lead === 0x7F || lead === 0xFF || lead < 0x3F)) charUnknown++;
  }
  if (report.length < 40) {
    report.push('L' + (k + 1) + ' srcs=' + srcs.join(',') + ' holes=' + hs.length + '\n   ' + info.join('\n   '));
  }
}
const out = [];
out.push('totalHoles=' + holes + ' linesWithMultiHole=' + multiHole + ' asciiByteLost=' + asciiLost +
  ' twoByteCharLost=' + charLost2 + ' otherLead=' + charUnknown);
out.push('asciiHist=' + [...asciiHist.entries()].sort((a, b) => b[1] - a[1]).map(([c, n]) => '0x' + c.toString(16) + ':' + n).join(' '));
out.push('');
out.push(report.join('\n'));
fs.writeFileSync(DIR + 'roundtrip_report.txt', out.join('\n'), 'utf8');
console.log(out[0]);
console.log(out[1]);
