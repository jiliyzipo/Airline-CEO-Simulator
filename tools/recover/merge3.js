// ASCII-only: rebuild InboxManager.cs.
//  - lines whose .NET-CP936 mojibake matches the pristine replay are restored from the replay
//  - remaining lines (the Phase 18 permit work added after the last transcript edit) are
//    reverse-mapped from mojibake back to original UTF-8 bytes.
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
  const cu = u >= 0x10000 ? String.fromCodePoint(u) : String.fromCharCode(u);
  if (p[0] === 'S') {
    if (!single.has(cu)) single.set(cu, parseInt(p[1], 16));
    continue;
  }
  const pair = [parseInt(p[0], 16), parseInt(p[1], 16)];
  const list = rev.get(cu);
  if (!list) rev.set(cu, [pair]);
  else if (pair[1] >= 0x40 && list[0][1] < 0x40) list.unshift(pair);
  else list.push(pair);
}

// ---- inputs ------------------------------------------------------------------
const rd = (p) => fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
const disk = rd(DISK).replace(/^\uFEFF/, '').split('\n');
const mangled = rd(DIR + 'replay_mangled.txt').replace(/^\uFEFF/, '').split('\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');

const m2r = new Map();
mangled.forEach((l, i) => { if (!m2r.has(l)) m2r.set(l, replay[i]); });

// ---- rebuild -----------------------------------------------------------------
const isCont = (b) => b >= 0x80 && b <= 0xBF;
const out = [];
const report = [];
let restored = 0, decoded = 0, losses = 0;

for (let i = 0; i < disk.length; i++) {
  const d = disk[i];
  if (m2r.has(d)) { out.push(m2r.get(d)); restored++; continue; }
  decoded++;
  const bytes = [];
  const lost = [];
  for (const ch of d) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) {
      if (single.has(ch)) { bytes.push(single.get(ch)); continue; }
      lost.push({ at: bytes.length, ch });
      bytes.push(0x3F);
      continue;
    }
    let pick = cands[0];
    if (cands.length > 1) {
      const prev = bytes.length ? bytes[bytes.length - 1] : 0x20;
      const good = cands.find((p) => isCont(p[0]) && (isCont(prev) || prev >= 0xC0 || prev < 0x80) || (p[0] >= 0xC0 && p[0] <= 0xF7));
      pick = good || cands[0];
    }
    bytes.push(pick[0], pick[1]);
  }
  const text = new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes));
  losses += lost.length;
  out.push(text);
  if (lost.length) report.push('L' + (i + 1) + ' losses=' + lost.length + ' @' + lost.map((l) => l.at).join(',') + ' :: ' + text);
}

fs.writeFileSync(DIR + 'merged_final.cs', out.join('\n'), 'utf8');
const head = 'diskLines=' + disk.length + ' restored=' + restored + ' decoded=' + decoded + ' losses=' + losses;
fs.writeFileSync(DIR + 'median_report.txt', head + '\n' + report.join('\n'), 'utf8');
console.log(head + '\nreportLines=' + report.length);
console.log('--- first decoded permit lines ---');
console.log(out.slice(405, 425).map((l, k) => (406 + k) + ': ' + l).join('\n'));
