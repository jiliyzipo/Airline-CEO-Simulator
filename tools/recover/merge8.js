// ASCII-only: rebuild InboxManager.cs, then resolve every lost byte.
//  - lines aligned with the mangled replay are restored verbatim from the pristine replay
//  - the remaining lines (Phase 18 permit work) are reverse-mapped from CP936 mojibake
//  - every loss is a single trailing byte of a 3-byte character, so the candidate set is small;
//    it is resolved from decisions.json or, failing that, reported with context for manual choice.
const fs = require('fs');
const path = require('path');

const DIR = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/';
const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const DISK = ROOT + '/Scripts/Systems/InboxManager.cs';
const DECISIONS = DIR + 'decisions.json';

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

// ---- corpus + candidate tables ----------------------------------------------
const corpus = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git'].includes(e.name)) continue; walk(p); }
    else if (/\.(cs|md|txt)$/i.test(e.name) && p !== DISK.replace(/\//g, path.sep)) {
      try { corpus.push(new TextDecoder('utf-8').decode(fs.readFileSync(p))); } catch (e2) { }
    }
  }
})(ROOT);
corpus.push(rd(DIR + 'replay_out.cs'));
const corpusText = corpus.join('\n');
const freq = new Map();
for (const ch of corpusText) if (ch.codePointAt(0) > 0x7F) freq.set(ch, (freq.get(ch) || 0) + 1);

// candidates sharing a 3-byte prefix
function candidatesFor(lead, cont) {
  const out = [];
  for (const [ch, c] of freq) {
    const b = Buffer.from(ch, 'utf8');
    if (b.length === 3 && b[0] === lead && b[1] === cont) out.push([ch, c]);
  }
  out.sort((a, b) => b[1] - a[1]);
  return out;
}

const decisions = fs.existsSync(DECISIONS) ? JSON.parse(fs.readFileSync(DECISIONS, 'utf8')) : {};

// bigram model over the corpus, used to pick between same-prefix candidates
const bi = new Map();
for (let k = 0; k + 1 < corpusText.length; k++) {
  const a = corpusText[k];
  if (a.codePointAt(0) < 0x80) continue;
  const key = a + corpusText[k + 1];
  bi.set(key, (bi.get(key) || 0) + 1);
}
const bg = (a, b) => Math.log(1 + (bi.get(a + b) || 0));
const ug = (c) => Math.log(1 + (freq.get(c) || 0));
const score = (cands, left, right) => cands.map(([c, f]) => [c, 1.2 * bg(left, c) + 1.2 * bg(c, right) + 0.4 * ug(c) + 0.8 * Math.log(1 + f)])
  .sort((a, b) => b[1] - a[1]);

// ---- rebuild -----------------------------------------------------------------
const out = [];
const log = [];
let restored = 0, decoded = 0, losses = 0, fixed = 0, open = 0;

for (let i = 0; i < disk.length; i++) {
  if (trueLineOf.has(i)) { out.push(trueLineOf.get(i)); restored++; continue; }
  decoded++;
  const bytes = [];
  const sites = [];
  for (const ch of disk[i]) {
    const cp = ch.codePointAt(0);
    if (cp < 0x80) { bytes.push(cp); continue; }
    const cands = rev.get(ch);
    if (!cands) { bytes.push(0x3F); continue; }
    bytes.push(cands[0][0], cands[0][1]);
  }
  for (let k = 2; k < bytes.length; k++) {
    if (bytes[k] !== 0x3F) continue;
    const lead = bytes[k - 2], cont = bytes[k - 1];
    if (lead >= 0xE0 && lead <= 0xEF && cont >= 0x80 && cont <= 0xBF) {
      sites.push({ at: k, lead, cont });
      bytes[k] = 0xE0 | 0x0F;   // 0xEF is a lead byte with no continuation -> U+FFFD
    }
  }
  // rebuild text with one unique placeholder per site
  const parts = [];
  let prev = 0;
  const sorted = sites.slice().sort((a, b) => a.at - b.at);
  sorted.forEach((s, idx) => {
    parts.push(new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes.slice(prev, s.at))));
    parts.push('\uE000');    // placeholder
    prev = s.at + 1;
  });
  parts.push(new TextDecoder('utf-8', { fatal: false }).decode(Buffer.from(bytes.slice(prev))));
  let text = parts.join('').replace(/[\uFFFD]+/g, '\uE000');
  losses += sorted.length;

  // resolve placeholders
  let idx = 0;
  while (text.indexOf('\uE000') >= 0 && idx < 60) {
    const pos = text.indexOf('\uE000');
    const site = sorted[text.slice(0, pos).split('\uE000').length - 1] || null;
    const key = (i + 1) + ':' + (text.slice(0, pos).split('\uE000').length - 1);
    let choice = decisions[key] || null;
    const cands = site ? candidatesFor(site.lead, site.cont) : [];
    const left = text[pos - 1] || '';
    const right = text[pos + 1] || '';
    const ranked = cands.length ? score(cands, left, right) : [];
    const margin = ranked.length > 1 ? ranked[0][1] - ranked[1][1] : 99;
    if (!choice && ranked.length && margin >= 0.6) choice = ranked[0][0];
    if (choice && choice !== '?') {
      text = text.slice(0, pos) + choice + text.slice(pos + 1);
      fixed++;
      if (margin < 1.5) log.push('SOFT L' + (i + 1) + ' key=' + key + ' -> ' + choice + ' (' + ranked.slice(0, 3).map((r) => r[0]).join('') + ') :: ' + text.slice(Math.max(0, pos - 34), pos + 34));
      idx++;
      continue;
    }
    open++;
    log.push('OPEN L' + (i + 1) + ' key=' + key + ' pre=' +
      (site ? site.lead.toString(16) + ',' + site.cont.toString(16) : '?') + ' cands=' +
      ranked.slice(0, 8).map((c) => c[0]).join('') + ' :: ' + text.slice(Math.max(0, pos - 34), pos + 34));
    text = text.slice(0, pos) + '\u3016' + text.slice(pos + 1);
    idx++;
  }
  out.push(text);
}

fs.writeFileSync(DIR + 'merged_final.cs', out.join('\n'), 'utf8');
fs.writeFileSync(DIR + 'fix_report.txt', 'disk=' + disk.length + ' restored=' + restored + ' decoded=' + decoded +
  ' losses=' + losses + ' fixed=' + fixed + ' open=' + open + '\n' + log.join('\n'), 'utf8');
console.log('restored=' + restored + ' decoded=' + decoded + ' losses=' + losses + ' fixed=' + fixed + ' open=' + open);
