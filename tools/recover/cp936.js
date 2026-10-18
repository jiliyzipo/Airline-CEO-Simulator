// Shared CP936 helpers: forward (byte pair -> char) and reverse (char -> byte pair) maps.
const fs = require('fs');
const DIR = __dirname + '/';

const lines = fs.readFileSync(DIR + 'cp936_pairs_full.txt', 'utf8').split('\n');
const fwd = new Map(); // "lead,trail" -> char
const rev = new Map(); // char -> {lead, trail}
for (const line of lines) {
  if (!line) continue;
  const tab = line.indexOf('\t');
  if (tab < 0) continue;
  const key = line.slice(0, tab);
  const ch = String.fromCharCode(parseInt(line.slice(tab + 1), 10));
  fwd.set(key, ch);
  if (!rev.has(ch)) {
    const [l, t] = key.split(',').map(Number);
    rev.set(ch, { lead: l, trail: t });
  }
}

// Bytes emitted by encoding `ch` in CP936 (UTF-8 fallback: the original UTF-8 bytes).
function charToBytes(ch) {
  const cp = ch.codePointAt(0);
  if (cp < 0x80) return [cp];
  if (cp === 0x20AC) return [0x80];
  if (cp === 0xF8F5) return [0xFF];
  const r = rev.get(ch);
  if (r) return [r.lead, r.trail];
  return null;
}

module.exports = { fwd, rev, charToBytes };
