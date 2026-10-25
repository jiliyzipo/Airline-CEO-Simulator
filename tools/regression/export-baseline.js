// Export a declared source set without caches, credentials, Git mutation or deletion.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const repo = path.resolve(__dirname, '../..');
const destination = path.resolve(repo, process.argv[2] || 'Unity Project/Builds/Baseline-20261017');
const underBuilds = destination.startsWith(path.join(repo, 'Unity Project', 'Builds') + path.sep);
const shortExport = path.dirname(destination) === path.join(repo, 'out') && /^b17(?:-[a-z0-9-]+)?$/.test(path.basename(destination));
if (!underBuilds && !shortExport) throw new Error('Use project Builds or a dedicated out/b17 export directory');
if (fs.existsSync(destination)) throw new Error('Export already exists; use a new directory name');
const roots = ['Unity Project/Assets', 'Unity Project/Packages', 'Unity Project/ProjectSettings',
  'tools/balance', 'tools/semcheck', 'tools/slotcheck', 'tools/regression',
  'docs/AI_COLLABORATION_GUIDE.md', 'docs/ProjectManifest.md',
  'docs/Architecture/SystemArchitecture.md', 'docs/Architecture/CurrentRules_2026-10-17.md',
  'docs/GameDesignBible/MVP/V3.0/航班排班与时刻槽_重构方案_v1.0.md',
  'docs/GameDesignBible/MVP/V3.0/CEO视角重构_设计定稿_v1.0.md'];
const tracked = new Set(execFileSync('git', ['ls-files', '-z'], { cwd: repo, encoding: 'utf8' }).split('\0'));
const entries = [], excluded = [];
function walk(relative) {
  const absolute = path.join(repo, relative);
  const name = path.basename(relative);
  if (fs.statSync(absolute).isDirectory()) {
    if (['bin', 'obj', 'out', 'Library', 'Temp', '.git'].includes(name)) { excluded.push(relative); return; }
    for (const child of fs.readdirSync(absolute).sort()) walk(relative + '/' + child);
    return;
  }
  if (/SegoeUISymbol\.ttf(?:\.meta)?$|\.log$|\.bak$|semcheck_report\.txt$|channel_report\.txt$/.test(name)) {
    excluded.push(relative); return;
  }
  const bytes = fs.readFileSync(absolute);
  if (/\.(asset|json|txt|ya?ml)$/.test(name)) {
    const text = bytes.toString('utf8');
    if (/^[ \t]*(?:apiKey|accessToken|secret)[ \t]*:[ \t]*[^\s"']+/mi.test(text))
      throw new Error('Nonempty credential field; refusing to export: ' + relative);
  }
  const target = path.join(destination, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, bytes);
  entries.push({ path: relative, bytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    tracked: tracked.has(relative) });
}
for (const root of roots) walk(root);
for (const name of ['README.md', '.gitignore']) walk(name);
// The exported project's identity isolates PlayerPrefs and persistentDataPath from the player's project.
const settings = path.join(destination, 'Unity Project/ProjectSettings/ProjectSettings.asset');
fs.writeFileSync(settings, fs.readFileSync(settings, 'utf8')
  .replace(/^(\s*companyName:).*$/m, '$1 AirlineCEORegression')
  .replace(/^(\s*productName:).*$/m, '$1 IsolatedBaseline_' + path.basename(destination)));
const manifest = { schemaVersion: 1, sourceHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).trim(),
  sourceIncludesUncommittedFiles: true, cleanGitCheckoutVerified: false,
  nodeVersion: process.version,
  dotnetVersion: execFileSync('dotnet', ['--version'], { encoding: 'utf8' }).trim(),
  fingerprintScope: 'Unity assets/packages/settings and tools; documentation is hashed individually but excluded from the runtime fingerprint.',
  sourceFingerprint: crypto.createHash('sha256').update(entries.filter(e => e.path.startsWith('Unity Project/') || e.path.startsWith('tools/'))
    .map(e => e.path + ':' + e.sha256).join('\n')).digest('hex'),
  overlay: 'Only exported ProjectSettings companyName/productName changed to isolate player data.', entries, excluded };
const report = path.join(repo, 'docs/handoff/Baseline_2026-10-17.json');
const body = JSON.stringify(manifest, null, 2) + '\n';
// 先把清单写进导出目录（它才是本次运行的权威副本），再回写仓库里的交接清单。
// 回写偶然会撞上 ERROR_USER_MAPPED_FILE：别的进程（编辑器/索引器/杀软）在这几百毫秒里
// 正把这个 260 KB 的 JSON 映射着读。此时导出本身已经完成，不该整体判失败 —— 重试几次即可。
fs.writeFileSync(path.join(destination, 'baseline-manifest.json'), body);
let written = false;
for (let attempt = 0; attempt < 5 && !written; attempt++) {
  try { fs.writeFileSync(report, body); written = true; }
  catch (e) {
    if (attempt === 4) throw new Error('Export done but could not update ' + report + ' (' + e.code + '). '
      + 'Copy ' + path.join(destination, 'baseline-manifest.json') + ' over it manually; do not re-export.');
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 250);
  }
}
console.log(JSON.stringify({ destination, files: entries.length, untrackedRequired: entries.filter(e => !e.tracked).length,
  bytes: entries.reduce((n, e) => n + e.bytes, 0), manifest: report }));
