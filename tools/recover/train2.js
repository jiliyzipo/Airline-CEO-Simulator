// Structured-perceptron training for the hole resolver: learn feature weights that pick the
// right (X, Y) pair for every ground-truth hole.
const fs = require('fs');
const DIR = __dirname + '/';
const { buildModel } = require(DIR + 'resolver.js');

const rd = (p) => fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n');
const replay = rd(DIR + 'replay_out.cs').split('\n');
const mangled = rd(DIR + 'mangle_js.txt').split('\n');
const holesTruth = JSON.parse(fs.readFileSync(DIR + 'holes.json', 'utf8'));
const model = buildModel([replay.join('\n')]);
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

const Y_PRIOR = new Map([['"', 163], [' ', 69], ['<', 22], ['\n', 2], ['/', 2], ['1', 2], ['0', 1], ['2', 1], ['3', 1], ['7', 1]]);
const Y_CANDIDATES = (() => {
  const cs = [' ', '"', '<', '/', '>', '\n', ',', '.', ':', ';', '(', ')', '=', '&', '-', '?',
    '!', "'", '*', '+', '%', '#', '|', '~', '^', '$', '\\', '@', '\t', '[', ']', '{', '}', '`'];
  for (let d = 0x30; d <= 0x39; d++) cs.push(String.fromCharCode(d));
  return cs.map((c) => ({ ch: c, b: c.codePointAt(0) })).filter((x) => x.b < 0x40 || x.b === 0x7F);
})();

const FEATS = ['t_left_x', 'b_left_x', 'b_x_y', 'b_y_right', 'b_x_right', 't_x_y_right', 't_left_x_y', 'u_x', 'u_y', 'y_prior', 'b_x_y_right'];

function featVec(x, y, ctx) {
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
}

// training samples
const samples = [];
for (let k = 0; k < holesTruth.holes.length; k++) {
  const hs = holesTruth.holes[k];
  if (!hs.length) continue;
  const line = mangled[k];
  for (const h of hs) {
    const cands = model.byPrefix.get(h.prev[0] + ',' + h.prev[1]);
    if (!cands || !cands.size) continue;
    const col = h.col;
    const ctx = {
      left: line[col - 2] || '',
      left2: col - 3 >= 0 ? (line[col - 3] || '') : '',
      right: line[col + 1] || '',
      right2: line[col + 2] || '',
    };
    const rows = [];
    for (const [x] of cands) for (const yc of Y_CANDIDATES) rows.push({ x, y: yc.ch, f: featVec(x, yc.ch, ctx) });
    samples.push({ rows, tx: h.x, ty: h.y });
  }
}

const W = new Float64Array(FEATS.length).fill(1);
function score(f) { let s = 0; for (let i = 0; i < W.length; i++) s += W[i] * f[i]; return s; }
function evaluate() {
  let both = 0, xok = 0, yok = 0;
  for (const s of samples) {
    let best = null;
    for (const r of s.rows) { const v = score(r.f); if (!best || v > best.v) best = { v, r }; }
    if (best.r.x === s.tx && best.r.y === s.ty) both++;
    if (best.r.x === s.tx) xok++;
    if (best.r.y === s.ty) yok++;
  }
  return { both, xok, yok, n: samples.length };
}

const start = evaluate();
let bestW = Float64Array.from(W), bestR = start;
for (let epoch = 0; epoch < 30; epoch++) {
  for (const s of samples) {
    let best = null, truth = null;
    for (const r of s.rows) {
      const v = score(r.f);
      if (!best || v > best.v) best = { v, r };
      if (r.x === s.tx && r.y === s.ty) truth = r;
    }
    if (!truth || truth === best.r) continue;
    for (let i = 0; i < W.length; i++) W[i] += 0.05 * (truth.f[i] - best.r.f[i]) / (1 + Math.abs(truth.f[i] - best.r.f[i]));
  }
  const r = evaluate();
  if (r.both > bestR.both) { bestR = r; bestW = Float64Array.from(W); }
}
const out = [];
out.push('samples=' + samples.length);
out.push('uniform: both=' + start.both + '/' + start.n + ' x=' + start.xok + ' y=' + start.yok);
out.push('trained: both=' + bestR.both + '/' + bestR.n + ' x=' + bestR.xok + ' y=' + bestR.yok);
out.push('weights=' + FEATS.map((k, i) => k + '=' + bestW[i].toFixed(2)).join(' '));
fs.writeFileSync(DIR + 'train_report.txt', out.join('\n'), 'utf8');
const wjson = {};
FEATS.forEach((k, i) => { wjson[k] = Math.round(bestW[i] * 100) / 100; });
fs.writeFileSync(DIR + 'weights.json', JSON.stringify(wjson, null, 1), 'utf8');
console.log(out.join('\n'));
