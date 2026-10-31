// Single reproducible entry point, including when invoked inside a delivery.
//
// Lessons baked in here (2026-10-26, REV-20261026023352-31f65f — "92 failed
// assertions" with no recorded cause):
//
// 1. **Evidence must outlive the run.** This file used to write every suite's
//    stdout/stderr into a fresh temp directory and print only counters. When a
//    review failed, the reason lived in a temp dir that was already gone, so the
//    verdict was undiagnosable. Logs now land in `COLLAB_RUN_DIR` (the per-run
//    directory review-run/preflight already keeps) and every failure's *message*
//    is echoed to stderr, which is itself captured as evidence.
//
// 2. **"Every test failed" is not 92 regressions.** When a whole suite fails
//    identically it is almost always the fixture, not the code under test. So the
//    two operations every fixture depends on — copying the tools tree into temp,
//    and seeding a fixture repo with `cmdInit` — are probed once and reported
//    separately, and a suite where nothing passed is flagged `fixtureBroken`
//    rather than counted as 21 verdicts.
//
// 3. **If the system temp is unwritable, use the workspace.** Sandboxes routinely
//    redirect TEMP into a package ACL directory; when the probe fails there, the
//    whole profile dies for a reason that has nothing to do with the code under
//    test. We retry in `<repo>/.collab-local/tmp`, and pass that to the suites via
//    TEMP/TMP so their own `os.tmpdir()` agrees. Both probes stay in the result.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';
import { extractFailures, extractSkips, isFixtureBroken, resolveEvidenceDir, suiteVerdict } from '../lib/suite-report.mjs';

// stage-close may ring a local handoff chime; never do that inside the suite.
process.env.COLLAB_HANDOFF_NOTIFY = process.env.COLLAB_HANDOFF_NOTIFY || '0';

const tools = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(tools, '../..');
const sleepSync = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

/** Copy the tools tree and seed a fixture repo — the shared prologue of every suite. */
async function probeIn(baseDir) {
  const result = { baseDir, copyOk: false, initOk: false, error: null, initError: null, copied: 0, ms: 0 };
  const started = Date.now();
  let dir = null;
  try {
    fs.mkdirSync(baseDir, { recursive: true });
    dir = fs.mkdtempSync(path.join(baseDir, 'collab-probe-'));
    fs.cpSync(tools, path.join(dir, 'tools/collab'), { recursive: true });
    result.copied = fs.readdirSync(path.join(dir, 'tools/collab'), { recursive: true }).length;
    if (!fs.existsSync(path.join(dir, 'tools/collab', 'config.example.json'))) {
      result.error = 'probe copy is missing config.example.json';
      return result;
    }
    result.copyOk = true;
    const cfg = {
      ...JSON.parse(fs.readFileSync(path.join(tools, 'config.example.json'), 'utf8')),
      _repoRoot: dir,
      projectId: 'probe',
      taskBook: { tasks: {}, completionTasks: [] },
    };
    fs.mkdirSync(path.join(dir, 'docs/collaboration'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'docs/collaboration/PROTOCOL.md'), '# probe');
    fs.writeFileSync(path.join(dir, 'tools/collab/config.json'), JSON.stringify(cfg, null, 2));
    const { cmdInit } = await import(pathToFileURL(path.join(tools, 'lib/commands.mjs')).href);
    cmdInit(cfg);
    const { loadTask } = await import(pathToFileURL(path.join(tools, 'lib/state.mjs')).href);
    result.initOk = loadTask(cfg, 'C-DEMO').taskId === 'C-DEMO';
    return result;
  } catch (e) {
    if (result.copyOk) result.initError = `${e.code || ''} ${e.message}`.trim();
    else result.error = `${e.code || ''} ${e.message}`.trim();
    return result;
  } finally {
    result.ms = Date.now() - started;
    try {
      if (dir) fs.rmSync(dir, { recursive: true, force: true });
    } catch {
      /* a leftover probe dir must never mask the real verdict */
    }
  }
}

// --- temp location: prefer the system temp, fall back to the workspace ---------
const systemProbe = await probeIn(os.tmpdir());
let tmpBase = os.tmpdir();
let workspaceProbe = null;
let workspaceBase = null; // created by us, removed by us
if (!systemProbe.copyOk || !systemProbe.initOk) {
  const alt = path.join(repoRoot, '.collab-local', 'tmp', `suite-${process.pid}-${Date.now()}`);
  fs.mkdirSync(alt, { recursive: true });
  workspaceBase = alt;
  workspaceProbe = await probeIn(alt);
  if (workspaceProbe.copyOk && workspaceProbe.initOk) tmpBase = alt;
}

const root = fs.mkdtempSync(path.join(tmpBase, 'collab-suite-'));
// Logs must outlive the process: reviewers need the reason, not just counters.
const evidenceDir = resolveEvidenceDir(process.env, root);
fs.mkdirSync(evidenceDir, { recursive: true });

const SUITES = ['collab.test.mjs', 'xml.test.mjs', 'closure.test.mjs', 'daily.test.mjs', 'reverse-review.test.mjs', 'stage.test.mjs', 'cleancheckout.test.mjs', 'handoff-notify.test.mjs', 'fingerprint-cache.test.mjs'];
// 120s was measured on an unrestricted shell. Inside the review sandbox every
// fork is several times slower (one run got 18/24 through a suite at 120s), and a
// wall-clock ceiling must not masquerade as a product regression. 8 minutes is
// still bounded, so a genuinely hung suite is still reported.
const SUITE_TIMEOUT_MS = Number(process.env.COLLAB_SUITE_TIMEOUT_MS) || 8 * 60 * 1000;

function runSuite(file, attempt) {
  const run = spawnSync(process.execPath, [path.join(tools, 'tests', file)], {
    cwd: root, encoding: 'utf8', windowsHide: true, timeout: SUITE_TIMEOUT_MS,
    env: {
      ...process.env,
      // Keep every suite's own `os.tmpdir()` on the location we just proved usable.
      TEMP: tmpBase,
      TMP: tmpBase,
      TMPDIR: tmpBase,
      COLLAB_AUDIT_TOOL_SOURCE: tools,
      COLLAB_AUDIT_RUN_LABEL: 'suite',
      COLLAB_HANDOFF_NOTIFY: '0',
    },
    maxBuffer: 8 * 1024 * 1024,
  });
  const suffix = attempt > 1 ? `.attempt${attempt}` : '';
  fs.writeFileSync(path.join(evidenceDir, file + suffix + '.stdout.txt'), run.stdout || '');
  fs.writeFileSync(path.join(evidenceDir, file + suffix + '.stderr.txt'), run.stderr || '');
  const passed = (run.stdout || '').match(/^PASS\b|^✓|^ok\b/gm)?.length || 0;
  const failed = ((run.stdout || '') + '\n' + (run.stderr || '')).match(/^FAIL\b|^✗|^not ok\b/gm)?.length || 0;
  const skipped = (run.stdout || '').match(/^SKIP\b|^skipped\b/gm)?.length || 0;
  const reasons = failed ? extractFailures(run.stdout, run.stderr) : { first: [], signatures: [] };
  const skipReasons = extractSkips(run.stdout);
  return {
    file,
    exitCode: run.status ?? 1,
    passed,
    failed,
    skipped,
    spawnError: run.error?.message || null,
    timedOut: run.error?.code === 'ETIMEDOUT' || run.signal === 'SIGTERM' ? true : false,
    // A suite where nothing passed and everything failed is a fixture verdict.
    fixtureBroken: isFixtureBroken(passed, failed),
    reason: reasons.signatures[0] || null,
    firstFailures: reasons.first,
    // Non-executable checks are named, never folded into the pass count.
    skipReason: skipReasons[0] || null,
    skipReasons,
    // An import-time crash prints a stack trace, not a FAIL line: keep the tail so
    // a crash can never again look like "counters are fine, cause unknown".
    stderrTail: (run.stderr || '').trim().split(/\r?\n/).slice(-6).join('\n').slice(0, 800) || null,
  };
}

const suites = [];
for (const file of SUITES) {
  let suite = runSuite(file, 1);
  if (suite.fixtureBroken) {
    // Retry once, on the record: Windows AV/sandbox hiccups do fail a fixture
    // setup transiently. Both attempts stay in the result, so nothing is hidden.
    const first = { exitCode: suite.exitCode, passed: suite.passed, failed: suite.failed, reason: suite.reason };
    sleepSync(2000);
    suite = { ...runSuite(file, 2), retried: true, firstAttempt: first };
  }
  suites.push(suite);
}

const ok = suites.every((s) => suiteVerdict(s));
const result = {
  profile: 'collab',
  assertionsTotal: suites.reduce((n, s) => n + s.passed + s.failed + s.skipped, 0),
  assertionsFailed: suites.reduce((n, s) => n + s.failed + (s.exitCode && !s.failed ? 1 : 0), 0),
  // Skipped assertions are reported, never counted as passed. A reviewer who sees
  // skipped > 0 knows the machine did not prove those checks.
  assertionsSkipped: suites.reduce((n, s) => n + s.skipped, 0),
  suites,
  // Diagnostics about *why*, reported alongside the counters — never folded into
  // them, because they are not additional passed/failed assertions.
  tempRoot: tmpBase,
  systemTempProbe: systemProbe,
  workspaceTempProbe: workspaceProbe,
  fixtureBrokenSuites: suites.filter((s) => s.fixtureBroken).map((s) => s.file),
  retriedSuites: suites.filter((s) => s.retried).map((s) => s.file),
  skippedSuites: suites.filter((s) => s.skipped > 0).map((s) => s.file),
  suiteTimeoutMs: SUITE_TIMEOUT_MS,
  evidenceDirectory: evidenceDir,
  ok,
};
fs.writeFileSync(path.join(evidenceDir, 'summary.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result));

// stderr is captured into the run's evidence by both preflight and review-run,
// so anything a human needs in order to act on a failure goes here too.
const notes = [];
if (tmpBase !== os.tmpdir()) notes.push(`system temp unusable, suites ran under ${tmpBase}`);
for (const s of suites.filter((x) => x.retried)) {
  notes.push(`${s.file}: first attempt ${s.firstAttempt.passed}/${s.firstAttempt.passed + s.firstAttempt.failed} (${s.firstAttempt.reason || 'no message'}); retry ${s.passed}/${s.passed + s.failed}`);
}
// Skips are announced even when the profile passes: the reader must know which
// assertions the machine did *not* prove, and why.
for (const s of suites.filter((x) => x.skipped > 0)) {
  notes.push(`skipped ${s.skipped} in ${s.file}: ${s.skipReasons.join(' | ') || 'no reason recorded'}`);
}
if (!ok) {
  const lines = [`collab profile failed: ${result.assertionsFailed} failed assertion(s)`];
  for (const p of [systemProbe, workspaceProbe].filter(Boolean)) {
    if (!p.copyOk) lines.push(`fixture probe @ ${p.baseDir}: tree copy broke: ${p.error}`);
    else if (!p.initOk) lines.push(`fixture probe @ ${p.baseDir}: cmdInit broke: ${p.initError}`);
  }
  for (const s of suites.filter((x) => x.failed > 0 || x.exitCode !== 0)) {
    lines.push(`- ${s.file} exit=${s.exitCode} passed=${s.passed} failed=${s.failed}${s.timedOut ? ' (timed out)' : ''}${s.retried ? ' (retried)' : ''}`);
    for (const r of s.firstFailures) lines.push(`    ${r}`);
    if (!s.firstFailures.length && s.spawnError) lines.push(`    spawn: ${s.spawnError}`);
  }
  notes.push(...lines);
}
if (notes.length) console.error(notes.join('\n'));

// Do not leave a full copy of the suite fixtures behind. Only the directory this
// process created is removed, and only when it does not contain the evidence we
// were asked to produce (a standalone run keeps its evidence inside the temp root).
if (workspaceBase) {
  const containsEvidence = path.resolve(evidenceDir).startsWith(path.resolve(workspaceBase) + path.sep);
  if (containsEvidence) {
    console.error(`kept ${workspaceBase}: it holds this run's evidence (${evidenceDir}); delete it after reading`);
  } else {
    try {
      fs.rmSync(workspaceBase, { recursive: true, force: true });
    } catch (e) {
      console.error(`could not remove ${workspaceBase}: ${e.message}`);
    }
  }
}

process.exitCode = ok ? 0 : 1;
