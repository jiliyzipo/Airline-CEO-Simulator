// Validate the hole resolver end-to-end: feed it the mangled replay lines, compare the
// reconstructed text with the pristine replay lines.
const fs = require('fs');
const DIR = __dirname + '/';
const { charToBytes, rev } = require(DIR + 'cp936.js');
const { buildModel, resolveBytes } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const holesTruth = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));

const model = buildModel([rd(DIR + 'replay_out.cs')]);

function toBytes(line) {
  const bytes = [];
  let unmapped = 0;
  for (const ch of line) {
    if (ch === '?') { bytes.push(0x3F); continue; }
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const b = charToBytes(ch);
    if (!b) { bytes.push(0x3F); unmapped++; continue; }
    for (const x of b) bytes.push(x);
  }
  return { bytes, unmapped };
}

let linesWithHoles = 0, exact = 0, notExact = 0, xOk = 0, yOk = 0, bothOk = 0, holes = 0, unmappedTotal = 0;
const bad = [];
for (let k = 0; k < mangled.length; k++) {
  const truth = (holesTruth.map[k] || []).map((j) => replay[j]).join('\n');
  const { holes: hh } = holesTruth.holes[k] ? { holes: holesTruth.holes[k] } : { holes: [] };
  if (!hh.length) {
    // no holes: reverse map and compare (should round-trip)
    continue;
  }
  linesWithHoles++;
  const { bytes, unmapped } = toBytes(mangled[k]);
  unmappedTotal += unmapped;
  const res = resolveBytes(bytes, model);
  holes += res.holes.length;
  const truthX = hh.map((x) => x.x);
  const truthY = hh.map((x) => x.y);
  let lineBad = false;
  res.holes.forEach((h, idx) => {
    if (h.x === truthX[idx]) xOk++; else lineBad = true;
    if (h.y === truthY[idx]) yOk++; else lineBad = true;
    if (h.x === truthX[idx] && h.y === truthY[idx]) bothOk++;
  });
  if (res.text === truth) exact++; else {
    notExact++;
    if (bad.length < 25) {
      const t = hh.map((x) => x.x + x.y).join('');
      const g = res.holes.map((x) => (x.x || '?') + x.y).join('');
      bad.push('L' + (k + 1) + ' truthHoles=[' + t + '] got=[' + g + ']\n   truth=' + JSON.stringify(truth.slice(0, 150)) + '\n   got  =' + JSON.stringify(res.text.slice(0, 150)));
    }
  }
}
const report = [
  'linesWithHoles=' + linesWithHoles + ' holes=' + holes + ' unmappedChars=' + unmappedTotal,
  'exactLines=' + exact + ' notExact=' + notExact,
  'X ok=' + xOk + '/' + holes + '  Y ok=' + yOk + '/' + holes + '  both=' + bothOk + '/' + holes,
  '',
  bad.join('\n'),
].join('\n');
fs.writeFileSync(DIR + 'resolver_report.txt', report, 'utf8');
console.log(report.split('\n').slice(0, 4).join('\n'));
