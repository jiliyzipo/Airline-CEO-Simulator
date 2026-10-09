// ASCII-only FINAL rebuild of InboxManager.cs.
// The disk file and the mangled replay text were both produced by the same .NET CP936 decoder,
// so they align almost exactly line by line; the aligned lines are restored from the pristine
// replay, and the leftover lines (the Phase 18 permit work) are reverse-mapped back to UTF-8.
const fs = require('fs');

const DIR = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/';
const DISK = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets/Scripts/Systems/InboxManager.cs';

const rev = new Map();
for (const line of fs.readFileSync(DIR + 'cp936_pairs.txt', 'ascii').split('\n')) {
  const p = line.trim().split(/\s+/);
  if (p.length !== 3 || p[0] === 'S') continue;
  const cu = String.fromCharCode(parseInt(p[2], 16));
  const pair = [parseInt(p[0], 16), parseInt(p[1], 16)];
  const list = rev.get(cu);
  if (!list) rev.set(cu, [pair]);
  else if (pair[1] >= 0x40 && list[0][1] < 0x40) list.unshift(pair);
  else list.push(pair);
}
rev.set('\u20AC', [[0x80]]);

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const disk = rd(DISK).split('\n');
const mangled = rd(DIR + 'replay_mangled.txt').split('\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');

// 1) global LCS alignment: disk line <-> mangled replay line
const n = disk.length, m = mangled.length, W = m + 1;
const dp = new Int32Array((n + 1) * W);
for (let i = n - 1; i >= 0; i--) {
  for (let k = m - 1; k >= 0; k--) {
    dp[i * W + k] = disk[i] === mangled[k]
      ? dp[(i + 1) * W + k + 1] + 1
      : Math.max(dp[(i + 1) * W + k], dp[i * W + k + 1]);
  }
}
const pairs = [];
{
  let i = 0, k = 0;
  while (i < n && k < m) {
    if (disk[i] === mangled[k]) { pairs.push([i, k]); i++; k++; }
    else if (dp[(i + 1) * W + k] >= dp[i * W + k + 1]) i++;
    else k++;
  }
}

// 2) rebuild
const out = [];
const report = [];
let restored = 0, decoded = 0, holes = 0, mergeFixed = 0;
const pairOf = new Map(pairs.map(([i, k]) => [i, replay[k]]));
for (let i = 0; i < disk.length; i++) {
  if (pairOf.has(i)) {
    const trueLine = pairOf.get(i);
    out.push(trueLine);
    restored++;
    const k = pairs.find((p) => p[0] === i)[1];
    if (mangled[k].indexOf('\n') >= 0) {
      for (let e = 1; e < 4; e++) if (replay[k + e] !== undefined) { out.push(replay[k + e]); mergeFixed++; }
    }
    continue;
  }
  decoded++;
  const bytes = [];
  const lost = [];
  for (const ch of disk[i]) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) { lost.push(bytes.length); bytes.push(0x3F); continue; }
    bytes.push(cands[0][0], cands[0][1]);
  }
  const text = new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes));
  holes += lost.length;
  out.push(text);
  report.push('L' + (i + 1) + (lost.length ? ' HOLES=' + lost.length : '') + ' | ' + text);
}

fs.writeFileSync(DIR + 'merged_final.cs', out.join('\n'), 'utf8');
fs.writeFileSync(DIR + 'merge6_report.txt',
  'diskLines=' + disk.length + ' mangledLines=' + m + ' pairs=' + pairs.length +
  ' restored=' + restored + ' decoded=' + decoded + ' explicitHoles=' + holes + ' mergeFixed=' + mergeFixed +
  '\n' + report.join('\n'), 'utf8');
console.log('diskLines=' + disk.length + ' mangledLines=' + m + ' pairs=' + pairs.length +
  ' restored=' + restored + ' decoded=' + decoded + ' explicitHoles=' + holes + ' mergeFixed=' + mergeFixed +
  ' outLines=' + out.length);
