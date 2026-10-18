// ASCII-only: align the mojibake file with the pristine replay content using a line-level LCS
// over canonicalized lines (placeholders and PUA collapsed, absorbed ASCII tolerated).
// Matched disk lines are restored from the replay; disk-only lines are dumped for manual work.
const fs = require('fs');

const DISK = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets/Scripts/Systems/InboxManager.cs';
const REPLAY = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/replay_out.cs';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/merged.cs';
const UNMATCHED = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/unmatched.txt';
const LOG = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/merge_log.txt';

const gbk = new TextDecoder('gbk', { fatal: false });
const mangle = (s) => gbk.decode(Buffer.from(s, 'utf8'));

const canon = (s) => s
  .replace(/[\uE000-\uF8FF]/g, '\u0002')
  .replace(/[\uFFFD?]+[A-Za-z0-9_]{0,3}/g, '\u0001');

const diskText = fs.readFileSync(DISK, 'utf8').replace(/\r\n/g, '\n');
const repText = fs.readFileSync(REPLAY, 'utf8').replace(/\r\n/g, '\n');
const D = diskText.split('\n');
const P = repText.split('\n');
const cd = D.map(canon);
const cp = P.map(canon);

const n = cd.length, m = cp.length;
const W = m + 1;
const dp = new Int32Array((n + 1) * W);
for (let i = n - 1; i >= 0; i--) {
  for (let j = m - 1; j >= 0; j--) {
    dp[i * W + j] = cd[i] === cp[j]
      ? dp[(i + 1) * W + j + 1] + 1
      : Math.max(dp[(i + 1) * W + j], dp[i * W + j + 1]);
  }
}

const out = [];
const unmatched = [];
let pairs = 0, onlyDisk = 0;
let i = 0, j = 0;
while (i < n && j < m) {
  if (cd[i] === cp[j]) {
    pairs++;
    out.push(P[j]);
    i++; j++;
  } else if (dp[(i + 1) * W + j] >= dp[i * W + j + 1]) {
    onlyDisk++;
    out.push(D[i]);
    if (D[i].trim() !== '') unmatched.push({ line: i + 1, text: D[i] });
    i++;
  } else {
    // line present only in the replay: a reverted/other edit; keep it out
    j++;
  }
}
while (i < n) { onlyDisk++; out.push(D[i]); if (D[i].trim() !== '') unmatched.push({ line: i + 1, text: D[i] }); i++; }

const head = 'diskLines=' + n + ' repLines=' + m + ' paired=' + pairs + ' diskOnly=' + onlyDisk + ' unmatchedNonBlank=' + unmatched.length;
fs.writeFileSync(LOG, head, 'utf8');
fs.writeFileSync(UNMATCHED, unmatched.map((u) => '#' + u.line + ': ' + u.text).join('\n'), 'utf8');
fs.writeFileSync(OUT, out.join('\n'), 'utf8');
console.log(head);
