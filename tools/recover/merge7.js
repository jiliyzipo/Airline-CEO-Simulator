// ASCII-only: rebuild + resolve losses.
//  - disk lines that line up with the mangled replay are restored verbatim from the pristine replay
//  - the remaining lines (Phase 18 permit work) are reverse-mapped, and every lost byte is resolved
//    by (1) decisions.json, (2) a corpus search with wildcards for the other losses.
const fs = require('fs');
const path = require('path');

const DIR = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const DISK = ROOT + '/Scripts/Systems/InboxManager.cs';
const DECISIONS = DIR + 'decisions.json';
const OUT = DIR + 'merged_final.cs';
const REC = DIR + 'losses.json';
const LOG = DIR + 'fix_report.txt';

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

// ---- alignment ---------------------------------------------------------------
const n = disk.length, m = mangled.length, W = m + 1;
const dp = new Int32Array((n + 1) * W);
for (let i = n - 1; i >= 0; i--) {
  for (let k = m - 1; k >= 0; k--) {
    dp[i * W + k] = disk[i] === mangled[k] ? dp[(i + 1) * W + k + 1] + 1
      : Math.max(dp[(i + 1) * W + k], dp[i * W + k + 1]);
  }
}
const trueLineOf = new Map();
{
  let i = 0, k = 0;
  while (i < n && k < m) {
    if (disk[i] === mangled[k]) { trueLineOf.set(i, replay[k]); i++; k++; }
    else if (dp[(i + 1) * W + k] >= dp[i * W + k + 1]) i++;
    else k++;
  }
}

// ---- corpus ------------------------------------------------------------------
const corpus = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git'].includes(e.name)) continue; walk(p); }
    else if (/\.(cs|md|txt|json)$/i.test(e.name) && p !== DISK.replace(/\//g, path.sep)) {
      try { corpus.push(new TextDecoder('utf-8').decode(fs.readFileSync(p))); } catch (e2) { }
    }
  }
})(ROOT);
corpus.push(rd(DIR + 'replay_out.cs'));
const corpusText = corpus.join('\n');

const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const decisions = fs.existsSync(DECISIONS) ? JSON.parse(fs.readFileSync(DECISIONS, 'utf8')) : {};

// ---- rebuild -----------------------------------------------------------------
const out = [];
const records = [];
const log = [];
let restored = 0, decoded = 0, lossTotal = 0, lossFixed = 0, lossOpen = 0;

for (let i = 0; i < disk.length; i++) {
  if (trueLineOf.has(i)) { out.push(trueLineOf.get(i)); restored++; continue; }
  decoded++;
  const bytes = [];
  for (const ch of disk[i]) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) { bytes.push(0x3F); continue; }
    bytes.push(cands[0][0], cands[0][1]);
  }
  // a '?' that completes an otherwise truncated 3-byte sequence is a lost byte, not a literal '?'
  const losses = [];
  for (let k = 2; k < bytes.length; k++) {
    if (bytes[k] !== 0x3F) continue;
    const lead = bytes[k - 2], cont = bytes[k - 1];
    if (lead >= 0xE0 && lead <= 0xEF && cont >= 0x80 && cont <= 0xBF) {
      losses.push({ atByte: k, prefix: [(lead & 0x0F) << 4 | ((cont >> 2) & 0x0F)].length ? [lead, cont] : [] });
      bytes[k] = 0xFD;   // UTF-8 invalid marker byte (never produced by CP936 pairs here)
    }
  }
  for (let k = 0; k < bytes.length; k++) if (bytes[k] === 0xFD) bytes[k] = 0xFF;  // force U+FFFD
  // decode with instrumentation: find U+FFFD positions
  let text = new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes));
  lossTotal += losses.length;

  // resolve every U+FFFD by context search
  let guard = 0;
  while (text.indexOf('\uFFFD') >= 0 && guard++ < 40) {
    const pos = text.indexOf('\uFFFD');
    const key = (i + 1) + ':' + pos;
    let choice = decisions[key] || null;
    if (!choice) {
      const before = text.slice(Math.max(0, pos - 24), pos);
      const after = text.slice(pos + 1, pos + 25);
      const L = esc(before.split('').map((c) => (c === '\uFFFD' ? '[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]?' : c)).join(''));
      const R = esc(after.split('').map((c) => (c === '\uFFFD' ? '[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]?' : c)).join(''));
      const re = new RegExp(L + '([\\u4e00-\\u9fff\\u3000-\\u303f\\uff00-\\uffef])' + R, 'g');
      const seen = new Map();
      let mm;
      while ((mm = re.exec(corpusText)) !== null) { const c = mm[1]; seen.set(c, (seen.get(c) || 0) + 1); if (mm.index === re.lastIndex) re.lastIndex++; }
      if (seen.size >= 1) {
        let best = null, bs = -1;
        for (const [c, cnt] of seen) { if (cnt > bs) { bs = cnt; best = c; } }
        choice = best;
      }
    }
    if (choice) { text = text.slice(0, pos) + choice + text.slice(pos + 1); lossFixed++; }
    else {
      lossOpen++;
      log.push('OPEN L' + (i + 1) + ' pos=' + pos + ' :: ' + text.slice(Math.max(0, pos - 30), pos + 30));
      text = text.slice(0, pos) + '\u3016' + text.slice(pos + 1);
    }
  }
  out.push(text);
  records.push({ line: i + 1, losses: losses.length, text });
}

fs.writeFileSync(OUT, out.join('\n'), 'utf8');
fs.writeFileSync(REC, JSON.stringify(records.filter((r) => r.losses), null, 0), 'utf8');
fs.writeFileSync(LOG, 'disk=' + disk.length + ' restored=' + restored + ' decoded=' + decoded +
  ' lossTotal=' + lossTotal + ' lossFixed=' + lossFixed + ' lossOpen=' + lossOpen + '\n' + log.join('\n'), 'utf8');
console.log('restored=' + restored + ' decoded=' + decoded + ' lossTotal=' + lossTotal + ' lossFixed=' + lossFixed + ' lossOpen=' + lossOpen + ' outLines=' + out.length);
