// 编码防呆：捕获「中文注释被静默替换」这一类事故。
//
// 事故特征：某些写入路径（Write / StrReplace 在非 UTF-8 落盘时）会把**非 ASCII 字符整体替换成
// '?'（U+003F）**，文件表面仍是合法源码、编译照过，但中文注释与 XML 文档全部失效；更糟的是
// 信息已经丢失，无法从文件自身还原。本仓库已发生过多次（See tools/recover/ 与 InboxManager.corrupted.bak.cs）。
//
// 因此布两层网：
//   1) 硬错误：文件含 U+FFFD（Unicode 替换字符）——正常源码/文档里不该出现。
//   2) 基线比对：与 enc_baseline.json 里记录的「中文字符数」逐文件对比，骤降即报错。
//      这一层才真正拦得住 '?' 替换（'?' 是合法 ASCII，第 1 层看不见）。
//
// 用法：
//   node tools/semcheck/enccheck.js            # 检查（CI / 交给 run.js 调用）
//   node tools/semcheck/enccheck.js --update   # 有意增删中文后重建基线
//
// 退出码：0 = 通过；1 = 有错误。
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');
const baselinePath = path.join(__dirname, 'enc_baseline.json');

// 扫描范围：源码与文档。二进制/资源不纳入。
const EXTENSIONS = new Set(['.cs', '.md']);

// 不扫描：构建产物、Unity 缓存、以及 recover 目录（那里**故意**保存着损坏样本，
// 若纳入扫描会把「已被修复的证据」当成新事故反复报警）。
const EXCLUDED_DIRS = new Set([
  '.git', '.vs', '.idea', '.cursor',
  'node_modules', 'Library', 'Temp', 'obj', 'bin', 'Logs', 'UserSettings', 'Builds', 'out',
  'recover',
]);

// 中文（CJK 统一表意文字）计数。用 code point 迭代，避免代理对被算成两个。
function countCjk(text) {
  let n = 0;
  for (const ch of text) {
    const cp = ch.codePointAt(0);
    if (cp >= 0x4e00 && cp <= 0x9fff) n++;
  }
  return n;
}

function countReplacement(text) {
  let n = 0;
  for (const ch of text) if (ch.codePointAt(0) === 0xfffd) n++;
  return n;
}

function walk(dir, out) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return out;
  }
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (EXCLUDED_DIRS.has(e.name)) continue;
      walk(full, out);
    } else if (e.isFile() && EXTENSIONS.has(path.extname(e.name).toLowerCase())) {
      out.push(full);
    }
  }
  return out;
}

function rel(p) {
  return path.relative(repo, p).split(path.sep).join('/');
}

function scan() {
  const files = walk(repo, []).sort();
  const stats = new Map();
  for (const f of files) {
    let text;
    try {
      text = fs.readFileSync(f, 'utf8');
    } catch {
      continue;
    }
    stats.set(rel(f), { cjk: countCjk(text), fffd: countReplacement(text) });
  }
  return stats;
}

const update = process.argv.includes('--update');
const stats = scan();

// 基线只记录「含中文」的文件——没有中文的文件涨跌无意义，记了只会让基线臃肿。
const current = {};
for (const [p, s] of stats) if (s.cjk > 0) current[p] = s.cjk;

if (update) {
  const withFffd = [...stats].filter(([, s]) => s.fffd > 0).map(([p]) => p);
  if (withFffd.length) {
    console.error('拒绝更新基线：下列文件仍含 U+FFFD，请先修好再更新：');
    withFffd.forEach(p => console.error('  ' + p));
    process.exitCode = 1;
  } else {
    fs.writeFileSync(baselinePath, JSON.stringify(current, null, 2) + '\n', 'utf8');
    const total = Object.values(current).reduce((a, b) => a + b, 0);
    console.log(`enc baseline updated: ${Object.keys(current).length} files, ${total} CJK chars`);
  }
  process.exit(process.exitCode || 0);
}

let baseline = {};
let baselineMissing = false;
if (fs.existsSync(baselinePath)) {
  baseline = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
} else {
  baselineMissing = true;
}

const errors = [];
const notices = [];

// 骤降阈值：跌掉 20% 以上即视为事故，而非正常重构。
const DROP_RATIO = 0.8;

for (const [p, s] of stats) {
  if (s.fffd > 0) {
    errors.push(`${p}: 含 ${s.fffd} 个 U+FFFD（Unicode 替换字符），源码/文档不应出现`);
  }

  const b = baseline[p];
  if (b === undefined) {
    if (s.cjk > 0) notices.push(`${p}: 新增中文 ${s.cjk} 字，未在基线中（若为有意新增，运行 --update）`);
    continue;
  }

  if (s.cjk === 0) {
    errors.push(`${p}: 中文全部丢失（基线 ${b} 字 → 现在 0）——典型的 '?' 替换事故`);
  } else if (s.cjk < b * DROP_RATIO) {
    errors.push(`${p}: 中文显著减少（基线 ${b} 字 → 现在 ${s.cjk} 字）`);
  }
}

for (const p of Object.keys(baseline)) {
  if (!stats.has(p)) notices.push(`${p}: 基线中存在但文件已不在（若为有意删除，运行 --update）`);
}

console.log(`enc: 扫描 ${stats.size} 个文件（.cs/.md）· 基线 ${Object.keys(baseline).length} 个含中文文件`);
if (baselineMissing) {
  console.log('enc: 尚无基线文件，运行 `node tools/semcheck/enccheck.js --update` 建立。');
}
notices.slice(0, 20).forEach(n => console.log('  [提示] ' + n));
if (notices.length > 20) console.log(`  [提示] ...另有 ${notices.length - 20} 条`);
errors.forEach(e => console.log('  [错误] ' + e));
console.log(errors.length ? `enc: errors=${errors.length}` : 'enc: errors=0');

process.exitCode = errors.length ? 1 : 0;
