// ASCII-only: merge the pristine replay content with the mojibake file.
// A disk line whose only difference from a mangled replay line is lost-byte placeholders
// ('?' / U+FFFD) is restored from the replay. Unmatched disk lines are dumped for manual work.
const fs = require('fs');

const DISK = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets/Scripts/Systems/InboxManager.cs';
const REPLAY = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/replay_out.cs';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/merged.cs';
const UNMATCHED = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/unmatched.txt';
const LOG = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/merge_log.txt';

const gbk = new TextDecoder('gbk', { fatal: false });
const mangle = (s) => gbk.decode(Buffer.from(s, 'utf8'));

const diskText = fs.readFileSync(DISK, 'utf8');
const repText = fs.readFileSync(REPLAY, 'utf8');

const split = (s) => s.split(/\r?\n/);
const diskLines = split(diskText);
const repLines = split(repText);

const mask = (s) => s.replace(/[?\uFFFD]/g, '\u0000');
const repMap = new Map();
for (const l of repLines) {
  const k = mask(mangle(l));
  if (!repMap.has(k)) repMap.set(k, l);
}

const out = [];
const unmatched = [];
let matched = 0;
for (let i = 0; i < diskLines.length; i++) {
  const d = diskLines[i];
  const hit = repMap.get(mask(d));
  if (hit !== undefined) { matched++; out.push(hit); }
  else if (d.trim() === '') { matched++; out.push(''); }
  else { out.push(d); unmatched.push({ line: i + 1, text: d }); }
}

const head = 'diskLines=' + diskLines.length + ' repLines=' + repLines.length + ' matched=' + matched + ' unmatched=' + unmatched.length;
fs.writeFileSync(LOG, head, 'utf8');
fs.writeFileSync(UNMATCHED, unmatched.map((u) => '#' + u.line + ': ' + u.text).join('\n'), 'utf8');
fs.writeFileSync(OUT, out.join('\n'), 'utf8');
console.log(head);
