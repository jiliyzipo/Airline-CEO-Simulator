// Fourth-round §14 verification driver.
// Runs the *unchanged* external suites (developer tests, baseline 24, supplemental 12,
// third-round 8 boundary cases) against one explicit tool source and records every
// stdout + results.json so the evidence is reproducible from the repository root.
//
// Usage:
//   node docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs
//   node .../run-verification.mjs --subject out/collab/DEL-xxx/tools/collab --label delivery
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../../../..');
const argv = process.argv.slice(2);
const arg = (name, fallback) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : fallback;
};
const subject = path.resolve(repo, arg('subject', 'tools/collab'));
const label = arg('label', 'current');
const outDir = path.join(here, 'evidence', label);
fs.mkdirSync(outDir, { recursive: true });

const newestResults = (root, since) => {
  let best = null;
  const walk = (dir) => {
    if (!fs.existsSync(dir)) return;
    for (const name of fs.readdirSync(dir)) {
      const abs = path.join(dir, name);
      const st = fs.statSync(abs);
      if (st.isDirectory()) walk(abs);
      else if (name === 'results.json' && st.mtimeMs >= since) {
        if (!best || st.mtimeMs > best.mtimeMs) best = { path: abs, mtimeMs: st.mtimeMs };
      }
    }
  };
  walk(root);
  return best?.path || null;
};

// The developer suite expects a repo-shaped root (docs/collaboration + tool placeholders).
// Running it in a disposable scaffold keeps the real repository state untouched and makes
// workspace and snapshot evidence directly comparable (same method as baseline case P01).
function scaffoldForDeveloperTests(sourceTools) {
  const root = path.join(repo, '.collab-local', `devtests-${label}-${Date.now()}`);
  fs.mkdirSync(path.join(root, 'docs/collaboration'), { recursive: true });
  fs.writeFileSync(path.join(root, 'docs/collaboration/PROTOCOL.md'), '# disposable fixture\n');
  fs.cpSync(sourceTools, path.join(root, 'tools/collab'), { recursive: true });
  const put = (rel, body) => {
    const abs = path.join(root, rel);
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, body);
  };
  put('tools/semcheck/run.js', '// fixture existence only, never executed');
  put('tools/regression/export-baseline.js', '// fixture existence only');
  put('tools/slotcheck/slotcheck.csproj', '<Project/>');
  return root;
}

const devScaffold = scaffoldForDeveloperTests(subject);
const suites = [
  {
    id: 'developer-tests',
    script: path.join(devScaffold, 'tools/collab/tests/collab.test.mjs'),
    cwd: devScaffold,
    resultsRoot: null,
  },
  {
    id: 'baseline24',
    script: path.join(here, 'baseline24-terminal.mjs'),
    cwd: repo,
    env: { COLLAB_AUDIT_RUN_LABEL: `${label}-baseline24` },
    resultsRoot: path.join(repo, '.collab-local/acceptance-20261017', `${label}-baseline24`),
  },
  {
    id: 'supplemental',
    script: path.join(repo, 'docs/handoff/verification/CollabTool-Reaccept-2026-10-17/supplemental.mjs'),
    cwd: repo,
    env: { COLLAB_AUDIT_RUN_LABEL: `${label}-supplemental` },
    resultsRoot: path.join(repo, '.collab-local/reaccept-20261017', `${label}-supplemental`),
  },
  {
    id: 'boundary8',
    script: path.join(here, 'edge8-terminal.mjs'),
    cwd: repo,
    env: { COLLAB_AUDIT_RUN_LABEL: `${label}-boundary8` },
    resultsRoot: path.join(repo, '.collab-local/reaccept-20261017', `${label}-boundary8`),
  },
];

const summary = {
  date: new Date().toISOString(),
  subjectToolsPath: subject,
  label,
  suites: [],
};
let failed = 0;
for (const s of suites) {
  const since = Date.now() - 1000;
  const r = spawnSync(process.execPath, [s.script, '--json'], {
    cwd: s.cwd,
    encoding: 'utf8',
    env: { ...process.env, ...(s.env || {}), COLLAB_AUDIT_TOOL_SOURCE: subject },
  });
  const stdout = r.stdout || '';
  const stderr = r.stderr || '';
  fs.writeFileSync(path.join(outDir, `${s.id}.stdout.txt`), stdout, 'utf8');
  fs.writeFileSync(path.join(outDir, `${s.id}.stderr.txt`), stderr, 'utf8');
  const resultsPath = s.resultsRoot ? newestResults(s.resultsRoot, since) : null;
  let counts = null;
  if (resultsPath) {
    const parsed = JSON.parse(fs.readFileSync(resultsPath, 'utf8'));
    counts = { total: parsed.total, passed: parsed.passed, failed: parsed.failed };
    fs.copyFileSync(resultsPath, path.join(outDir, `${s.id}.results.json`));
  }
  const okLines = (stdout.match(/^(ok|PASS)\s/gm) || []).length;
  const badLines = (stdout.match(/^(not ok|FAIL)\s/gm) || []).length;
  const entry = {
    id: s.id,
    exitCode: r.status,
    ok: r.status === 0,
    counts,
    passLines: okLines,
    failLines: badLines,
    script: path.relative(repo, s.script).split(path.sep).join('/'),
    cwd: path.relative(repo, s.cwd).split(path.sep).join('/'),
    resultsPath: resultsPath ? path.relative(repo, resultsPath).split(path.sep).join('/') : null,
  };
  if (!entry.ok) failed += 1;
  summary.suites.push(entry);
  process.stdout.write(
    `${entry.ok ? 'PASS' : 'FAIL'} ${s.id} exit=${r.status} ` +
      `counts=${counts ? `${counts.passed}/${counts.total}` : 'n/a'}\n`,
  );
}
summary.failedSuites = failed;
fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
process.stdout.write(`SUMMARY ${suites.length - failed}/${suites.length} suites passed\n`);
process.stdout.write(`EVIDENCE ${path.relative(repo, outDir).split(path.sep).join('/')}\n`);
process.exitCode = failed ? 1 : 0;
