// Produce a human-review report: for every low-confidence hole, print the full merged line
// with the resolved pair marked and the alternative candidates, so corrections can be
// recorded in decisions.json (keyed by "diskLine:holeIndex").
const fs = require('fs');
const DIR = __dirname + '/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const DISK = ROOT + '/Scripts/Systems/InboxManager.cs';
const { charToBytes } = require(DIR + 'cp936.js');
const { buildModel, resolveBytes } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const disk = rd(DISK).split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mapInfo = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));
const model = buildModel([replay.join('\n')]);

const n = disk.length, m = mangled.length, W = m + 1;
const dp = new Int32Array((n + 1) * W);
for (let i = n - 1; i >= 0; i--) {
  for (let k = m - 1; k >= 0; k--) {
    dp[i * W + k] = disk[i] === mangled[k] ? dp[(i + 1) * W + k + 1] + 1
      : Math.max(dp[(i + 1) * W + k], dp[i * W + k + 1]);
  }
}
const pairOf = new Map();
{
  let i = 0, k = 0;
  while (i < n && k < m) {
    if (disk[i] === mangled[k]) { pairOf.set(i, k); i++; k++; }
    else if (dp[(i + 1) * W + k] >= dp[i * W + k + 1]) i++;
    else k++;
  }
}

function toBytes(line) {
  const bytes = [];
  for (const ch of line) {
    if (ch === '?') { bytes.push(0x3F); continue; }
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const b = charToBytes(ch);
    if (!b) { bytes.push(0x3F); continue; }
    for (const x of b) bytes.push(x);
  }
  return bytes;
}

const decisions = fs.existsSync(DIR + 'decisions.json') ? JSON.parse(fs.readFileSync(DIR + 'decisions.json', 'utf8')) : {};
const out = [];
for (let i = 0; i < disk.length; i++) {
  if (pairOf.has(i)) continue;
  const bytes = toBytes(disk[i]);
  const res = resolveBytes(bytes, model);
  if (!res.holes.length) continue;
  const flagged = res.holes.some((h) => !h.x || h.margin < 1.0);
  if (!flagged) continue;
  out.push('=== L' + (i + 1) + ' holes=' + res.holes.length + '  ' + JSON.stringify(disk[i].slice(0, 40)));
  res.holes.forEach((h, idx) => {
    const key = (i + 1) + ':' + idx;
    const decided = decisions[key] ? '  [decided=' + JSON.stringify(decisions[key]) + ']' : '';
    const mark = (h.margin < 1.0 ? '*' : ' ');
    out.push('  ' + mark + ' hole#' + idx + ' key=' + key + ' margin=' + (h.margin === undefined ? '?' : h.margin.toFixed(2)) +
      ' ctx: ' + h.ctx.left2 + h.ctx.left + '⟦' + (h.x || '?') + h.y + '⟧' + h.ctx.right + h.ctx.right2 +
      decided);
    out.push('      alts=' + (h.alts || []).map((a) => JSON.stringify(a)).join(' '));
    const ps = h.pos !== undefined ? h.pos : 0;
    out.push('      full: ' + res.text.slice(0, 60) + ' ... ' + res.text.slice(Math.max(0, ps - 20), ps + 20) + ' ... ' + res.text.slice(-40));
  });
}
fs.writeFileSync(DIR + 'hole_review.txt', out.join('\n'), 'utf8');
console.log('flaggedLines=' + out.filter((l) => l.startsWith('===')).length);
