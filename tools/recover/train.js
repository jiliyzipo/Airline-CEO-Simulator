// Learn weights for the hole resolver from the ground-truth holes (mangle.js/holes.json),
// then report accuracy so resolver.js can bake in the winning feature weights.
const fs = require('fs');
const DIR = __dirname + '/';
const { charToBytes } = require(DIR + 'cp936.js');
const { buildModel } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const holesTruth = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));
const model = buildModel([replay.join('\n')]);

// n-gram counts with backoff-friendly accessors
const tri = new Map(), bi = new Map(), uni = new Map();
const corpus = model.corpus;
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

const Y_PRIOR = new Map([['"', 163], [' ', 69], ['<', 22], ['\n', 2], ['/', 2], ['1', 2], ['0', 1], ['2', 1], ['3', 1], ['7', 1]]);
const Y_CANDIDATES = (() => {
  const cs = [' ', '"', '<', '/', '>', '\n', ',', '.', ':', ';', '(', ')', '=', '&', '-', '?',
    '!', "'", '*', '+', '%', '#', '|', '~', '^', '$', '\\', '@', '\t', '[', ']', '{', '}', '`'];
  for (let d = 0x30; d <= 0x39; d++) cs.push(String.fromCharCode(d));
  return cs.map((c) => ({ ch: c, b: c.codePointAt(0) })).filter((x) => x.b < 0x40 || x.b === 0x7F);
})();

// Build the training set: for every truth hole, all (x, y) candidates + features.
const samples = [];
let noCandHoles = 0;
for (let k = 0; k < holesTruth.holes.length; k++) {
  const hs = holesTruth.holes[k];
  if (!hs.length) continue;
  const line = mangled[k];
  // column of the hole in the mangled line -> use truth line for context
  for (let n = 0; n < hs.length; n++) {
    const h = hs[n];
    const truth = (holesTruth.map[k] || []).map((j) => replay[j]).join('\n');
    // derive context from the truth by rebuilding the resolved text incrementally
    const col = h.col;
    const left = line[col - 2] !== undefined ? line[col - 2] : '';   // char before the pair char
    // NOTE: line[col-1] is the pair char standing for X's first two bytes
    const cands = model.byPrefix.get(h.prev[0] + ',' + h.prev[1]);
    if (!cands || !cands.size) { noCandHoles++; continue; }
    const xcands = [...cands.entries()];
    // ground truth x/y
    const tx = h.x, ty = h.y;
    samples.push({ k, n, line, col, left, xcands, tx, ty, h, truth });
  }
}

// We need the right context, which sits after the '?' in the mangled line:
// the char after the hole is the char after Y in the mangled line.
function contexts(s) {
  const { line, col } = s;
  const right = line[col + 1] || '';
  const right2 = line[col + 2] || '';
  const left = s.left;
  const left2 = col - 3 >= 0 ? (line[col - 3] || '') : '';
  return { left, left2, right, right2 };
}

const feats = [];
for (const s of samples) {
  const ctx = contexts(s);
  const row = [];
  for (const [x] of s.xcands) {
    for (const yc of Y_CANDIDATES) {
      const y = yc.ch;
      const f = {
        x, y,
        t_left_x: T(ctx.left2 + ctx.left + x),
        b_left_x: B(ctx.left + x),
        b_x_y: B(x + y),
        b_y_right: B(y + ctx.right),
        b_x_right: B(x + ctx.right),
        t_x_y_right: T(x + y + ctx.right),
        t_left_x_y: T(ctx.left + x + y),
        u_x: U(x),
        u_y: U(y),
        y_prior: Math.log(1 + (Y_PRIOR.get(y) || 0)),
        match: (x === s.tx && y === s.ty) ? 1 : 0,
        xmatch: x === s.tx ? 1 : 0,
      };
      row.push(f);
    }
  }
  feats.push({ row, sample: s });
}

// grid search over weights
const keys = ['t_left_x', 'b_left_x', 'b_x_y', 'b_y_right', 'b_x_right', 't_x_y_right', 't_left_x_y', 'u_x', 'y_prior'];
function evaluate(w) {
  let both = 0, xok = 0, yok = 0, n = 0;
  for (const { row, sample } of feats) {
    let best = null;
    for (const f of row) {
      let s = 0;
      for (const k of keys) s += (w[k] || 0) * f[k];
      if (!best || s > best.s) best = { s, f };
    }
    n++;
    if (best.f.x === sample.tx && best.f.y === sample.ty) both++;
    if (best.f.x === sample.tx) xok++;
    if (best.f.y === sample.ty) yok++;
  }
  return { both, xok, yok, n };
}

let best = null;
const grid = {
  t_left_x: [0, 0.5, 1, 2], b_left_x: [0, 0.5, 1, 2], b_x_y: [0, 0.5, 1, 2],
  b_y_right: [0, 0.5, 1, 2, 3], b_x_right: [0, 0.5, 1, 2], t_x_y_right: [0, 1, 2, 3],
  t_left_x_y: [0, 0.5, 1, 2], u_x: [0, 0.2, 0.5, 1], y_prior: [0, 0.5, 1, 2, 3],
};
// random search is cheaper than a full grid
let rnd = 12345;
const rand = () => { rnd = (rnd * 1103515245 + 12345) & 0x7fffffff; return rnd / 0x7fffffff; };
for (let iter = 0; iter < 4000; iter++) {
  const w = {};
  for (const k of keys) {
    const g = grid[k];
    w[k] = iter < keys.length * 4 && iter < 60 ? 1 : g[Math.floor(rand() * g.length)];
  }
  const r = evaluate(w);
  if (!best || r.both > best.r.both) best = { w: { ...w }, r };
}
const out = [];
out.push('samples=' + samples.length + ' noCandHoles=' + noCandHoles);
out.push('best=' + JSON.stringify(best.w));
out.push('both=' + best.r.both + '/' + best.r.n + ' xOk=' + best.r.xok + ' yOk=' + best.r.yok);
// baseline: uniform weights on all features
const base = evaluate(Object.fromEntries(keys.map((k) => [k, 1])));
out.push('uniform all-1: both=' + base.both + '/' + base.n + ' xOk=' + base.xok + ' yOk=' + base.yok);
fs.writeFileSync(DIR + 'train_report.txt', out.join('\n'), 'utf8');
console.log(out.join('\n'));
