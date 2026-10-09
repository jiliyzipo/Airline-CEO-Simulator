// Rebuild InboxManager.cs:
//  * LCS-align the corrupted file on disk with the mangled pristine replay; every line
//    that still matches is restored verbatim (using mangle_map.json to translate a mangled
//    line index into the pristine line index(es), including merged lines).
//  * every remaining line (the Phase 18 permit work that was added *after* the corruption)
//    is reverse-mapped from the CP936 mojibake and its '?' holes are resolved by resolver.js.
const fs = require('fs');
const DIR = __dirname + '/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const DISK = ROOT + '/Scripts/Systems/InboxManager.cs';
const { charToBytes, rev } = require(DIR + 'cp936.js');
const { buildModel, resolveBytes, WEIGHTS } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const disk = rd(DISK).split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mapInfo = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));
const mangleMap = mapInfo.map;

// ---- alignment ---------------------------------------------------------------
const n = disk.length, m = mangled.length, W = m + 1;
const dp = new Int32Array((n + 1) * W);
for (let i = n - 1; i >= 0; i--) {
  for (let k = m - 1; k >= 0; k--) {
    dp[i * W + k] = disk[i] === mangled[k] ? dp[(i + 1) * W + k + 1] + 1
      : Math.max(dp[(i + 1) * W + k], dp[i * W + k + 1]);
  }
}
const pairOf = new Map(); // disk line index -> mangled line index
let aligned = 0;
{
  let i = 0, k = 0;
  while (i < n && k < m) {
    if (disk[i] === mangled[k]) { pairOf.set(i, k); aligned++; i++; k++; }
    else if (dp[(i + 1) * W + k] >= dp[i * W + k + 1]) i++;
    else k++;
  }
}

const model = buildModel([replay.join('\n')]);
const decisions = fs.existsSync(DIR + 'decisions.json')
  ? JSON.parse(fs.readFileSync(DIR + 'decisions.json', 'utf8').replace(/^\uFEFF/, '')) : {};

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

const out = [];
const report = [];
let restored = 0, decoded = 0, holes = 0, unmappedTotal = 0, weird = 0;
const lowConf = [];
for (let i = 0; i < disk.length; i++) {
  if (pairOf.has(i)) {
    const k = pairOf.get(i);
    const srcs = mangleMap[k] || [];
    out.push(srcs.map((j) => replay[j]).join('\n'));
    restored++;
    continue;
  }
  decoded++;
  const { bytes, unmapped } = toBytes(disk[i]);
  unmappedTotal += unmapped;
  const raw = disk[i];
  const isComment = /(^|\s)(\/\/|\*|#)/.test(raw);
  const res = resolveBytes(bytes, model, { decisions, lineNo: i + 1, isComment });
  holes += res.holes.length;
  for (const h of res.holes) {
    if (!h.x || h.margin < 1.0) {
      const ps = h.pos !== undefined ? h.pos : 0;
      lowConf.push('L' + (i + 1) + ' margin=' + (h.margin === undefined ? '?' : h.margin.toFixed(2)) +
        ' picked=' + (h.x || '\u3016') + JSON.stringify(h.y) +
        ' alts=' + (h.alts || []).join('') +
        ' :: ' + res.text.slice(Math.max(0, ps - 30), ps) + '【' + (h.x || '?') + h.y + '】' + res.text.slice(ps + 2, ps + 32));
    }
  }
  if (res.text.indexOf('\uFFFD') >= 0) weird++;
  out.push(res.text);
}

fs.writeFileSync(DIR + 'merged_final2.cs', out.join('\n'), 'utf8');
report.push('diskLines=' + disk.length + ' aligned=' + aligned + ' restored=' + restored + ' decoded=' + decoded);
report.push('holes=' + holes + ' unmappedChars=' + unmappedTotal + ' linesWithFFFD=' + weird);
report.push('weights=' + WEIGHTS.join(','));
report.push('lowConfidence=' + lowConf.length);
report.push(lowConf.join('\n'));
fs.writeFileSync(DIR + 'merge9_report.txt', report.join('\n'), 'utf8');
console.log(report.slice(0, 4).join('\n'));
