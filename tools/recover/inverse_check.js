// Check how faithfully the naive inverse map (mojibake char -> one byte pair) reproduces the
// original UTF-8 byte stream: decode every hole-free mangled line and compare with the truth.
const fs = require('fs');
const DIR = __dirname + '/';
const { charToBytes, rev, fwd } = require(DIR + 'cp936.js');
const { decodeLoose } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const holes = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8')).holes;

// how many mojibake chars have more than one possible byte pair?
let multi = 0, single = 0;
const multiMap = new Map();
for (const [ch, r] of rev) {
  const n = [...fwd.entries()].filter(([, c]) => c === ch).length;
  if (n > 1) { multi++; multiMap.set(ch, n); } else single++;
}

let checked = 0, ok = 0, bad = 0, badBytes = 0;
const samples = [];
for (let k = 0; k < mangled.length; k++) {
  if ((holes[k] || []).length) continue;
  const truth = (JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8')).map[k] || []).map((j) => replay[j]).join('\n');
  const bytes = [];
  for (const ch of mangled[k]) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const b = charToBytes(ch);
    if (!b) { bytes.push(0x3F); continue; }
    for (const x of b) bytes.push(x);
  }
  const got = decodeLoose(bytes, 1e9).join('');
  checked++;
  if (got === truth) ok++;
  else {
    bad++;
    if (samples.length < 12) samples.push('L' + (k + 1) + '\n truth=' + JSON.stringify(truth.slice(0, 80)) + '\n got  =' + JSON.stringify(got.slice(0, 80)));
  }
}
const out = [];
out.push('inverseMap: charsWithOnePair=' + single + ' charsWithManyPairs=' + multi);
out.push('holeFreeLinesChecked=' + checked + ' exact=' + ok + ' mismatched=' + bad);
out.push(samples.join('\n'));
fs.writeFileSync(DIR + 'inverse_report.txt', out.join('\n'), 'utf8');
console.log(out.slice(0, 3).join('\n'));
