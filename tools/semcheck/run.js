// 语义检查：编译 Unity 全量脚本，只挑出 error CS####，并按文件归组输出。
// 生成 csproj：node tools/semcheck/gen.js
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');
const proj = path.join(__dirname, 'semcheck.csproj');

const r = spawnSync('dotnet', ['build', proj, '-v', 'q', '-nologo'], {
  cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024
});
const out = (r.stdout || '') + (r.stderr || '');

const errRe = /^(.*?)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.*?)\s*\[/gm;
const seen = new Map();
let m;
while ((m = errRe.exec(out)) !== null) {
  const file = m[1].replace(/^.*[\\/](Assets[\\/].*)$/, '$1');
  const key = `${file}(${m[2]},${m[3]}): ${m[4]} ${m[5]}`;
  if (!seen.has(key)) seen.set(key, true);
}

const cscRe = /error\s+(CS\d{4})/g;
const total = (out.match(cscRe) || []).length;

const lines = [...seen.keys()];
console.log(`errors=${lines.length} (raw=${total})`);
lines.slice(0, 80).forEach(l => console.log('  ' + l));
fs.writeFileSync(path.join(__dirname, 'semcheck_report.txt'), lines.join('\n') || '(clean)', 'utf8');

// 追加：中文注释编码防呆（enccheck.js）。
// 「非 ASCII → '?'」这类事故**不产生任何 CS#### 编译错误**，编译照过、semcheck 全绿，
// 所以必须单独拦一道，否则中文注释静默丢失而无人察觉。见 tools/recover/ 的历史事故。
const enc = spawnSync('node', [path.join(__dirname, 'enccheck.js')], { cwd: repo, encoding: 'utf8' });
process.stdout.write(enc.stdout || '');
if (enc.stderr) process.stderr.write(enc.stderr);
const encFailed = enc.status !== 0;

process.exitCode = (lines.length || encFailed) ? 1 : 0;
