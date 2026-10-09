// Resolver for CP936 '?' holes.
//
// Ground truth (learned from mangle.js/holes.json, 264/264 holes):
//   the decoder consumed byte p and p+1 where p is the LAST byte of a 3-byte UTF-8
//   character X (so X's first two bytes are visible right before the '?'), and p+1 is
//   the first byte of the following character Y, which is always an ASCII byte < 0x40.
//   A '?' in mojibake therefore stands for "X(3 bytes) + Y(1 ascii byte)".
//
// The resolver therefore (a) enumerates the corpus characters sharing X's two-byte
// prefix and (b) enumerates plausible ASCII bytes for Y, then picks the pair with the
// best corpus language score.
const fs = require('fs');
const path = require('path');

const ROOT = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets';
const SKIP = /InboxManager\.cs$/i;

function buildModel(extraTexts) {
  const texts = [];
  (function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) { if (['Library', 'Temp', 'obj', 'node_modules', '.git'].includes(e.name)) continue; walk(p); }
      else if (/\.(cs|md|txt)$/i.test(e.name) && !SKIP.test(p)) { try { texts.push(fs.readFileSync(p, 'utf8')); } catch (e2) { } }
    }
  })(ROOT);
  for (const t of extraTexts || []) texts.push(t);
  const corpus = texts.join('\n');

  const uni = new Map();
  const bi = new Map();
  for (let i = 0; i < corpus.length; i++) {
    const c = corpus[i];
    uni.set(c, (uni.get(c) || 0) + 1);
    if (i + 1 < corpus.length) {
      const k = c + corpus[i + 1];
      bi.set(k, (bi.get(k) || 0) + 1);
    }
  }
  const byPrefix = new Map();
  for (const ch of corpus) {
    const b = Buffer.from(ch, 'utf8');
    if (b.length !== 3) continue;
    const k = b[0] + ',' + b[1];
    let s = byPrefix.get(k);
    if (!s) { s = new Map(); byPrefix.set(k, s); }
    s.set(ch, (s.get(ch) || 0) + 1);
  }
  return { corpus, uni, bi, byPrefix, total: corpus.length };
}

// plausible ASCII bytes that can be swallowed as the trail of an invalid CP936 pair
const Y_CANDIDATES = (() => {
  const cs = [' ', '"', '<', '/', '>', '\n', ',', '.', ':', ';', '(', ')', '=', '&', '-', '?',
    '!', "'", '*', '+', '%', '#', '|', '~', '^', '$', '\\', '@', '\t', '[', ']', '{', '}', '`'];
  for (let d = 0x30; d <= 0x39; d++) cs.push(String.fromCharCode(d));
  return cs.map((c) => ({ ch: c, b: c.codePointAt(0) })).filter((x) => x.b < 0x40 || x.b === 0x7F);
})();

const Y_PRIOR = new Map([['"', 163], [' ', 69], ['<', 22], ['\n', 2], ['/', 2], ['0', 1], ['1', 2], ['2', 1], ['3', 1], ['7', 1]]);

// Feature weights learned by train2.js (structured perceptron over the ground-truth holes).
const FEATS = ['t_left_x', 'b_left_x', 'b_x_y', 'b_y_right', 'b_x_right', 't_x_y_right', 't_left_x_y', 'u_x', 'u_y', 'y_prior', 'b_x_y_right'];
let WEIGHTS = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
try {
  const w = JSON.parse(fs.readFileSync(__dirname + '/weights.json', 'utf8').replace(/^\uFEFF/, ''));
  const arr = FEATS.map((k) => (typeof w[k] === 'number' ? w[k] : 1));
  if (arr.some((v) => v !== 0)) WEIGHTS = arr;
} catch (e) { /* uniform weights */ }

function initScorer(model) {
  const corpus = model.corpus;
  const tri = new Map(), bi = new Map(), uni = new Map();
  for (let i = 0; i < corpus.length; i++) {
    const c = corpus[i];
    uni.set(c, (uni.get(c) || 0) + 1);
    if (i + 1 < corpus.length) {
      const k2 = c + corpus[i + 1];
      bi.set(k2, (bi.get(k2) || 0) + 1);
      if (i + 2 < corpus.length) {
        const k3 = c + corpus[i + 1] + corpus[i + 2];
        tri.set(k3, (tri.get(k3) || 0) + 1);
      }
    }
  }
  const T = (s) => Math.log(1 + (tri.get(s) || 0));
  const B = (s) => Math.log(1 + (bi.get(s) || 0));
  const U = (s) => Math.log(1 + (uni.get(s) || 0));
  return function featVec(x, y, ctx) {
    return [
      T(ctx.left2 + ctx.left + x),
      B(ctx.left + x),
      B(x + y),
      B(y + ctx.right),
      B(x + ctx.right),
      T(x + y + ctx.right),
      T(ctx.left + x + y),
      U(x),
      U(y),
      Math.log(1 + (Y_PRIOR.get(y) || 0)),
      B(x + y) + 0.5 * B(y + ctx.right),
    ];
  };
}

function dot(f) { let s = 0; for (let i = 0; i < f.length; i++) s += WEIGHTS[i] * f[i]; return s; }

// Resolve all holes in a byte array previously reverse-mapped from mojibake.
// `decisions` maps "<lineNo>:<holeIndex>" to the literal replacement text.
// Returns { text, holes: [{at, x, y, margin}] }.
function resolveBytes(bytes, model, opts) {
  opts = opts || {};
  if (!model._scorer) model._scorer = initScorer(model);
  const featVec = model._scorer;
  const decisions = opts.decisions || {};
  const lineNo = opts.lineNo;
  const isComment = !!opts.isComment;
  const holes = [];
  let i = 0;
  let idx = 0;
  let text = '';
  while (i < bytes.length) {
    const b = bytes[i];
    const isHole = b !== undefined && b >= 0xE0 && b <= 0xEF &&
      bytes[i + 1] >= 0x80 && bytes[i + 1] <= 0xBF && bytes[i + 2] === 0x3F;
    if (isHole) {
      const lead = b, cont = bytes[i + 1];
      const rest = decodeLoose(bytes.slice(i + 3), 8);
      const ctx = {
        left: text ? text[text.length - 1] : '',
        left2: text.length > 1 ? text[text.length - 2] : '',
        right: rest[0] || '',
        right2: rest[1] || '',
      };
      const key = lineNo + ':' + idx;
      if (decisions[key] !== undefined) {
        holes.push({ at: i, pos: text.length, x: null, y: null, margin: 99, ctx, decided: decisions[key] });
        text += decisions[key];
        i += 3;
        idx++;
        continue;
      }
      const cands = [...(model.byPrefix.get(lead + ',' + cont) || new Map()).entries()];
      let scored = [];
      for (const [x] of cands) {
        for (const yc of Y_CANDIDATES) {
          scored.push({ x, y: yc.ch, s: dot(featVec(x, yc.ch, ctx)) });
        }
      }
      scored.sort((a, b2) => b2.s - a.s);
      if (!scored.length) {
        holes.push({ at: i, x: null, y: ' ', margin: 0, ctx });
        text += '\u3016';
        i += 3;
        idx++;
        continue;
      }
      const best = scored[0];
      const margin = scored.length > 1 ? best.s - scored[1].s : 99;
      // In a comment, a hole at the very end of the line cannot be a quote/tag: drop the
      // swallowed ASCII byte instead of inventing a visible character.
      let x = best.x, y = best.y;
      if (isComment && ctx.right === '' && (y === '"' || y === '<' || y === '/')) y = '';
      holes.push({ at: i, pos: text.length, x, y, margin, ctx, alts: scored.slice(1, 4).map((c) => c.x + c.y) });
      text += x + y;
      i += 3;
      idx++;
      continue;
    }
    const ch = decodeOne(bytes, i);
    text += ch.text;
    i += ch.len;
  }
  return { text, holes };
}

function decodeOne(bytes, i) {
  const b = bytes[i];
  if (b === undefined) return { text: '', len: 1 };
  if (b < 0x80) return { text: String.fromCharCode(b), len: 1 };
  let need = 0;
  if (b >= 0xC2 && b <= 0xDF) need = 2;
  else if (b >= 0xE0 && b <= 0xEF) need = 3;
  else if (b >= 0xF0 && b <= 0xF4) need = 4;
  if (!need) return { text: '\uFFFD', len: 1 };
  const seq = bytes.slice(i, i + need);
  if (seq.length < need || seq.slice(1).some((c) => c < 0x80 || c > 0xBF)) {
    return { text: '\uFFFD', len: 1 };
  }
  const cp = need === 2 ? ((seq[0] & 0x1F) << 6) | (seq[1] & 0x3F)
    : need === 3 ? ((seq[0] & 0x0F) << 12) | ((seq[1] & 0x3F) << 6) | (seq[2] & 0x3F)
      : ((seq[0] & 0x07) << 18) | ((seq[1] & 0x3F) << 12) | ((seq[2] & 0x3F) << 6) | (seq[3] & 0x3F);
  return { text: String.fromCodePoint(cp), len: need };
}

function decodeLoose(bytes, maxChars) {
  const out = [];
  let i = 0;
  while (i < bytes.length && out.length < (maxChars || 4)) {
    const r = decodeOne(bytes, i);
    if (r.text === '\uFFFD' && bytes[i] === 0x3F) return out; // stop at the next hole
    if (r.text) out.push(r.text);
    i += r.len;
  }
  return out;
}

module.exports = { buildModel, resolveBytes, decodeOne, decodeLoose, Y_CANDIDATES, WEIGHTS, FEATS };
