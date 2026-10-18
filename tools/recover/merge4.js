// ASCII-only: rebuild InboxManager.cs by aligning the mojibake disk file against the pristine
// replay content (monotone alignment using ASCII skeletons + edit distance). Paired lines are
// restored from the replay; the rest (the Phase 18 permit work) are reverse-mapped from IEEE CP936.
const fs = require('fs');

const DIR = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/';
const DISK = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets/Scripts/Systems/InboxManager.cs';

// ---- .NET CP936 reverse table -------------------------------------------------
const rev = new Map();
const single = new Map();
for (const line of fs.readFileSync(DIR + 'cp936_pairs.txt', 'ascii').split('\n')) {
  const p = line.trim().split(/\s+/);
  if (p.length !== 3) continue;
  const u = parseInt(p[2], 16);
  const cu = String.fromCharCode(u);
  if (p[0] === 'S') { if (!single.has(cu)) single.set(cu, parseInt(p[1], 16)); continue; }
  const pair = [parseInt(p[0], 16), parseInt(p[1], 16)];
  const list = rev.get(cu);
  if (!list) rev.set(cu, [pair]);
  else if (pair[1] >= 0x40 && list[0][1] < 0x40) list.unshift(pair);
  else list.push(pair);
}
rev.set('\u20AC', [[0x80, null]]);

const key = (s) => s.replace(/[^\x20-\x7E]/g, '').replace(/\?/g, '');
const lev = (a, b) => {
  const n = a.length, m = b.length;
  if (!n) return m; if (!m) return n;
  let prev = new Array(m + 1), cur = new Array(m + 1);
  for (let j = 0; j <= m; j++) prev[j] = j;
  for (let i = 1; i <= n; i++) {
    cur[0] = i;
    for (let j = 1; j <= m; j++) cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
    const t = prev; prev = cur; cur = t;
  }
  return prev[m];
};
const sim = (a, b) => { const L = Math.max(a.length, b.length); return L ? 1 - lev(a, b) / L : 1; };

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const disk = rd(DISK).split('\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const dk = disk.map(key);
const rk = replay.map(key);

// ---- monotone alignment ------------------------------------------------------
const pair = new Array(disk.length).fill(-1);
let j = 0;
for (let i = 0; i < disk.length; i++) {
  let best = -1, bestScore = 0;
  const lim = Math.min(replay.length, j + 10);
  for (let k = j; k < lim; k++) {
    const s = sim(dk[i], rk[k]);
    const bonus = 1 - (k - j) * 0.01;
    if (s * bonus > bestScore) { bestScore = s * bonus; best = k; }
  }
  if (best >= 0 && bestScore >= 0.82) { pair[i] = best; j = best + 1; }
}

// ---- rebuild -----------------------------------------------------------------
const out = [];
const report = [];
let restored = 0, decoded = 0, losses = 0;
for (let i = 0; i < disk.length; i++) {
  if (pair[i] >= 0) { out.push(replay[pair[i]]); restored++; continue; }
  decoded++;
  const bytes = [];
  const lost = [];
  for (const ch of disk[i]) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) { lost.push(bytes.length); bytes.push(0x3F); continue; }
    const p = cands[0];
    bytes.push(p[0]);
    if (p[1] !== null) bytes.push(p[1]);
  }
  const text = new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes));
  losses += lost.length;
  out.push(text);
  if (lost.length) report.push('L' + (i + 1) + ' losses=' + lost.length + ' :: ' + text);
}

fs.writeFileSync(DIR + 'merged_final.cs', out.join('\n'), 'utf8');
fs.writeFileSync(DIR + 'merge4_report.txt', 'diskLines=' + disk.length + ' restored=' + restored + ' decoded=' + decoded + ' losses=' + losses + '\n' + report.join('\n'), 'utf8');
console.log('diskLines=' + disk.length + ' restored=' + restored + ' decoded=' + decoded + ' losses=' + losses + ' reportLines=' + report.length);
