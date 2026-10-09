// JS re-implementation of the .NET CP936 mangling that corrupted InboxManager.cs,
// with a byte->source-line map so we can translate mangled line indexes back to
// the pristine (replay_out.cs) line indexes.
const fs = require('fs');
const DIR = __dirname + '/';
const SRC = DIR + 'replay_out.cs';
const OUT = DIR + 'mangle_js.txt';
const MAPOUT = DIR + 'mangle_map.json';

const { fwd: pairs } = require(DIR + 'cp936.js');

function readNorm(p) {
  return fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
}

const text = readNorm(SRC);
const LINES = text.split('\n');

// ---- 1. UTF-8 encode, tracking which source line every byte came from.
const bytes = [];
const byteLine = [];
const byteChar = []; // index into text of the char each byte belongs to
const byteOfChar = []; // which byte of that char (0-based)
let line = 0;
let ci = 0;
for (const ch of text) {
  const cp = ch.codePointAt(0);
  const seq = [];
  if (cp < 0x80) seq.push(cp);
  else if (cp < 0x800) seq.push(0xC0 | (cp >> 6), 0x80 | (cp & 63));
  else if (cp < 0x10000) seq.push(0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 63), 0x80 | (cp & 63));
  else seq.push(0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 63), 0x80 | ((cp >> 6) & 63), 0x80 | (cp & 63));
  seq.forEach((b, idx) => { bytes.push(b); byteLine.push(line); byteChar.push(ci); byteOfChar.push(idx); });
  if (ch === '\n') line++;
  ci++;
}
const TEXT = [...text];
const CHARINFO = (idx) => (idx >= 0 && idx < TEXT.length ? TEXT[idx] : null);

// ---- 2. Decode bytes with the CP936 tables.
// chars: array of {ch, lines:Set}
const chars = [];
let i = 0;
let invalid1 = 0, invalid2 = 0;
while (i < bytes.length) {
  const b = bytes[i];
  // 0x00-0x7F are plain ASCII in CP936; 0x80 is the euro sign; 0xFF stands alone.
  if (b < 0x80) {
    chars.push({ ch: String.fromCharCode(b), l0: byteLine[i], l1: byteLine[i] });
    i++;
    continue;
  }
  if (b === 0x80 || b === 0xFF) {
    chars.push({ ch: b === 0x80 ? '\u20AC' : '\uF8F5', l0: byteLine[i], l1: byteLine[i] });
    i++;
    continue;
  }
  // lead byte: .NET consumes the trail byte even when the pair is invalid,
  // emitting '?' for the pair (this is what eats newlines and merges lines).
  if (i + 1 < bytes.length) {
    const p = pairs.get(b + ',' + bytes[i + 1]);
    if (p !== undefined) {
      chars.push({ ch: p, l0: byteLine[i], l1: byteLine[i + 1] });
      i += 2;
      continue;
    }
    invalid2++;
    chars.push({ ch: '?', l0: byteLine[i], l1: byteLine[i + 1], hole: [i, i + 1] });
    i += 2;
    continue;
  }
  invalid1++;
  chars.push({ ch: '?', l0: byteLine[i], l1: byteLine[i], hole: [i] });
  i++;
}

// ---- 3. Split decoded stream into mangled lines, recording source lines.
const mangledLines = [];
const map = []; // map[k] = [srcLineIdx, ...]
const lineHoles = []; // lineHoles[k] = [{col, bytes:[{b,ch,of}]}]
let cur = '';
let curSrc = new Set();
let curHoles = [];
function flush() {
  const srcs = [];
  for (let j = 0; j < LINES.length; j++) if (curSrc.has(j)) srcs.push(j);
  mangledLines.push(cur);
  map.push(srcs);
  lineHoles.push(curHoles);
  cur = '';
  curSrc = new Set();
  curHoles = [];
}
for (const c of chars) {
  curSrc.add(c.l0);
  curSrc.add(c.l1);
  if (c.hole) {
    const b0 = c.hole[0];
    const rec = {
      col: cur.length,
      prev: [b0 - 2 >= 0 ? bytes[b0 - 2] : null, b0 - 1 >= 0 ? bytes[b0 - 1] : null],
      x: CHARINFO(byteChar[b0]),       // the 3-byte char whose last byte was eaten
      b1: bytes[b0],
      b2: c.hole[1] !== undefined ? bytes[c.hole[1]] : null,
      y: c.hole[1] !== undefined ? CHARINFO(byteChar[c.hole[1]]) : null,
      next: c.hole[1] !== undefined ? CHARINFO(byteChar[c.hole[1] + 1]) : CHARINFO(byteChar[b0 + 1]),
      line: b0 >= 0 ? byteLine[b0] : null,
    };
    curHoles.push(rec);
  }
  const parts = String(c.ch).split('\n');
  for (let p = 0; p < parts.length; p++) {
    if (p > 0) flush();
    cur += parts[p];
  }
}
flush();

fs.writeFileSync(OUT, mangledLines.join('\n'), 'utf8');
fs.writeFileSync(MAPOUT, JSON.stringify({ map, lines: mangledLines }), 'utf8');
fs.writeFileSync(DIR + 'holes.json', JSON.stringify({ holes: lineHoles, map }), 'utf8');

const pw = readNorm(DIR + 'replay_mangled.txt').split('\n');
console.log('jsLines=' + mangledLines.length + ' pwLines=' + pw.length +
  ' invalidPair=' + invalid2 + ' invalidSingle=' + invalid1);
let diffs = 0;
const max = Math.max(mangledLines.length, pw.length);
for (let k = 0; k < max; k++) {
  if (mangledLines[k] !== pw[k]) {
    if (diffs < 5) {
      console.log('DIFF line ' + (k + 1) + '\n  js=' + JSON.stringify((mangledLines[k] || '').slice(0, 120)) + '\n  pw=' + JSON.stringify((pw[k] || '').slice(0, 120)));
    }
    diffs++;
  }
}
console.log('diffs=' + diffs);
