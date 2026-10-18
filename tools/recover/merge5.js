// ASCII-only FINAL rebuild of InboxManager.cs.
// 1) pair the .NET-mangled replay text with the pristine replay text (tiny walk, tolerating the
//    few line merges the mangling causes) to learn mangled-line -> true-line;
// 2) restore every disk line whose mangled form is known to be unchanged;
// 3) reverse-map the remaining lines (the Phase 18 permit work) back to original UTF-8 bytes.
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

// 1) mangled -> true line, via a global LCS over exact line equality
const m2r = new Map();
let merges = 0;
{
  const n = mangled.length, m = replay.length, W = m + 1;
  const dp = new Int32Array((n + 1) * W);
  for (let i = n - 1; i >= 0; i--) {
    for (let j2 = m - 1; j2 >= 0; j2--) {
      dp[i * W + j2] = mangled[i] === replay[j2]
        ? dp[(i + 1) * W + j2 + 1] + 1
        : Math.max(dp[(i + 1) * W + j2], dp[i * W + j2 + 1]);
    }
  }
  let i = 0, j2 = 0;
  while (i < n && j2 < m) {
    if (mangled[i] === replay[j2]) {
      if (!m2r.has(mangled[i])) m2r.set(mangled[i], replay[j2]);
      i++; j2++;
    } else if (dp[(i + 1) * W + j2] >= dp[i * W + j2 + 1]) { i++; merges++; }
    else { j2++; merges++; }
  }
}

// 2/3) rebuild
const out = [];
const report = [];
let restored = 0, decoded = 0, holes = 0;
for (let i = 0; i < disk.length; i++) {
  const d = disk[i];
  if (m2r.has(d)) { out.push(m2r.get(d)); restored++; continue; }
  decoded++;
  const bytes = [];
  const lost = [];
  let pending = [];
  for (const ch of d) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) { lost.push(bytes.length); bytes.push(0x3F); continue; }
    bytes.push(cands[0][0], cands[0][1]);
  }
  const text = new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes));
  holes += lost.length;
  out.push(text);
  report.push((lost.length ? 'HOLES=' + lost.length + ' ' : '       ') + 'L' + (i + 1) + ' | ' + text);
}

fs.writeFileSync(DIR + 'merged_final.cs', out.join('\n'), 'utf8');
fs.writeFileSync(DIR + 'merge5_report.txt',
  'diskLines=' + disk.length + ' restored=' + restored + ' decoded=' + decoded + ' merges=' + merges + '\n' +
  report.join('\n'), 'utf8');
console.log('diskLines=' + disk.length + ' restored=' + restored + ' decoded=' + decoded + ' merges=' + merges);
console.log('--- decoded lines ---');
console.log(report.join('\n'));
