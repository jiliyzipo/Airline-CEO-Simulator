// 语义检查：分别编译「运行时代码」与「测试代码」两个程序集（对应 Unity 的两条 asmdef 边界），
// 只挑出 error CS####，并按文件归组输出。生成 csproj：node tools/semcheck/gen.js
//
// 分两次编译的原因见 gen.js 头部：单工程编译时引用集是并集，测试里碰到 TMP 类型也会全绿，
// 而 Unity 会报 CS0012（测试 asmdef 拿不到 Unity.TextMeshPro）。
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');

const errRe = /^(.*?)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.*?)\s*\[/gm;
const cscRe = /error\s+(CS\d{4})/g;

/** 编译一个工程，返回去重后的错误行与原始错误个数。 */
function build(projName, run = spawnSync) {
  const r = run('dotnet', ['build', path.join(__dirname, projName), '-v', 'q', '-nologo'], {
    cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024
  });
  const out = (r.stdout || '') + (r.stderr || '');
  const seen = new Map();
  let m;
  while ((m = errRe.exec(out)) !== null) {
    const file = m[1].replace(/^.*[\\/](Assets[\\/].*)$/, '$1');
    const key = `${file}(${m[2]},${m[3]}): ${m[4]} ${m[5]}`;
    if (!seen.has(key)) seen.set(key, true);
  }
  const lines = [...seen.keys()];
  const failed = r.status !== 0 || !!r.error || !!r.signal || lines.length > 0;
  const diagnostic = failed && lines.length === 0
    ? `build failed: exit=${r.status}, signal=${r.signal || 'none'}, error=${r.error?.message || 'none'}\n${out.trim().slice(-4000)}`
    : '';
  return { lines, raw: (out.match(cscRe) || []).length, failed, diagnostic };
}

function main() {
const scripts = build('semcheck.csproj');
const tests = build('semcheck_tests.csproj');

console.log(`errors=${scripts.lines.length} (raw=${scripts.raw})`);
scripts.lines.slice(0, 80).forEach(l => console.log('  ' + l));
console.log(`tests-asm: errors=${tests.lines.length} (raw=${tests.raw})`);
tests.lines.slice(0, 80).forEach(l => console.log('  ' + l));
if (scripts.diagnostic) console.error('[scripts] ' + scripts.diagnostic);
if (tests.diagnostic) console.error('[tests] ' + tests.diagnostic);

const lines = [
  ...scripts.lines.map(l => '[scripts] ' + l),
  ...tests.lines.map(l => '[tests] ' + l),
  ...(scripts.diagnostic ? ['[scripts] ' + scripts.diagnostic] : []),
  ...(tests.diagnostic ? ['[tests] ' + tests.diagnostic] : [])
];
fs.writeFileSync(path.join(__dirname, 'semcheck_report.txt'), lines.join('\n') || '(clean)', 'utf8');

// 追加：中文注释编码防呆（enccheck.js）。
// 「非 ASCII → '?'」这类事故**不产生任何 CS#### 编译错误**，编译照过、semcheck 全绿，
// 所以必须单独拦一道，否则中文注释静默丢失而无人察觉。见 tools/recover/ 的历史事故。
const enc = spawnSync('node', [path.join(__dirname, 'enccheck.js')], { cwd: repo, encoding: 'utf8' });
process.stdout.write(enc.stdout || '');
if (enc.stderr) process.stderr.write(enc.stderr);
const encFailed = enc.status !== 0;

process.exitCode = (scripts.failed || tests.failed || encFailed) ? 1 : 0;
}

module.exports = { build };
if (require.main === module) main();
