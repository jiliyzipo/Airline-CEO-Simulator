import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { FileLock, writeJsonAtomic, ensureDir } from '../lib/io.mjs';
import { validateDelivery, validateTask, assertRoleAction } from '../lib/schemas.mjs';
import { EXIT } from '../lib/exit.mjs';
import { classifyChange, hashManifest } from '../lib/fingerprint.mjs';
import {
  parseNunitCounters,
  parseSemcheckText,
  parseSlotcheckText,
  resolveRunOutputRels,
  verifySnapshotIntegrity,
  cmdIssueUpdate,
} from '../lib/commands.mjs';
import {
  computeTaskGate,
  readClause,
  recordBaseline,
  taskBookSummary,
  writeTotalCheckbox,
} from '../lib/taskbook.mjs';
import { runHook } from '../lib/hooks.mjs';
import { listIssues, refreshFreshness } from '../lib/state.mjs';
import { computeFingerprints } from '../lib/fingerprint.mjs';
import { stableContractPayload } from '../lib/schemas.mjs';
import { resolveTaskRoots } from '../lib/roots.mjs';
import { isArtifactDirName, isGeneratedFileName } from '../lib/fingerprint.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const toolRoot = path.resolve(__dirname, '..');

/**
 * Developer tests must never write into the tree they are testing: when they are
 * executed from a delivery snapshot, `init/ack/claim` would otherwise create
 * collaboration state inside the delivery inputs (see FR-01). Every run therefore
 * happens in a disposable, repo-shaped scaffold.
 * Set COLLAB_KEEP_DEV_FIXTURE=1 to keep it for inspection.
 */
function buildScaffold() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-devtest-'));
  const put = (rel, body) => {
    const abs = path.join(root, rel);
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, body);
  };
  fs.mkdirSync(path.join(root, 'docs/collaboration'), { recursive: true });
  put('docs/collaboration/PROTOCOL.md', '# disposable developer-test fixture\n');
  fs.cpSync(toolRoot, path.join(root, 'tools/collab'), { recursive: true });
  // Existence-only placeholders for the tools `doctor` probes.
  put('tools/semcheck/run.js', '// fixture: never executed\n');
  put('tools/regression/export-baseline.js', '// fixture: never executed\n');
  put('tools/slotcheck/slotcheck.csproj', '<Project/>\n');
  return root;
}

const repo = buildScaffold();
const cli = path.join(repo, 'tools/collab/cli.mjs');
process.on('exit', () => {
  if (process.env.COLLAB_KEEP_DEV_FIXTURE) return;
  try {
    fs.rmSync(repo, { recursive: true, force: true });
  } catch {
    /* best effort */
  }
});

function run(args, { expectCode = 0 } = {}) {
  const r = spawnSync(process.execPath, [cli, ...args, '--json'], {
    cwd: repo,
    encoding: 'utf8',
  });
  const code = r.status == null ? 1 : r.status;
  let json = null;
  try {
    json = JSON.parse(r.stdout || '{}');
  } catch {
    json = { raw: r.stdout, err: r.stderr };
  }
  if (code !== expectCode) {
    throw new Error(
      `expected exit ${expectCode} got ${code}\nstdout=${r.stdout}\nstderr=${r.stderr}`,
    );
  }
  return { code, json, stderr: r.stderr };
}

let failed = 0;
function test(name, fn) {
  try {
    fn();
    console.log(`ok - ${name}`);
  } catch (e) {
    failed++;
    console.error(`not ok - ${name}`);
    console.error(e);
  }
}

test('schema rejects developer independentReviewPassed', () => {
  assert.throws(
    () =>
      validateDelivery({
        schemaVersion: 1,
        deliveryId: 'x',
        taskId: 'C-DEMO',
        taskRevision: 1,
        actor: 'cursor',
        tool: 'cursor',
        summary: 's',
        files: {},
        selfTest: {},
        git: {},
        fingerprints: {},
        snapshotPath: 'out/x',
        createdAt: new Date().toISOString(),
        independentReviewPassed: true,
      }),
    (e) => e.exitCode === EXIT.FORBIDDEN,
  );
});

test('schema rejects codex claim via role gate', () => {
  assert.throws(() => assertRoleAction('codex', 'claim'), (e) => e.exitCode === EXIT.FORBIDDEN);
});

test('task requires acceptanceCriteria', () => {
  assert.throws(
    () =>
      validateTask({
        schemaVersion: 1,
        projectId: 'p',
        taskId: 'T',
        title: 't',
        revision: 1,
        devStatus: 'todo',
        reviewStatus: 'not_run',
        scope: [],
        acceptanceCriteria: [],
        createdAt: 't',
        updatedAt: 't',
      }),
    (e) => e.exitCode === EXIT.USAGE,
  );
});

test('file lock is exclusive', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-lock-'));
  const lockPath = path.join(dir, 't.lock');
  const a = new FileLock(lockPath, 'a');
  const b = new FileLock(lockPath, 'b');
  assert.equal(a.acquire().ok, true);
  assert.equal(b.acquire().ok, false);
  assert.equal(a.release().ok, true);
  assert.equal(b.acquire().ok, true);
  b.release({ force: true });
});

test('file lock does not steal a lock whose payload is still being written', () => {
  // Reproduces the TH-05 double-execution race: an empty lock file is "not yet
  // initialized", not "abandoned"; reclaiming it would hand the lease to two
  // processes at once.
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-lock-race-'));
  const lockPath = path.join(dir, 'r.lock');
  fs.writeFileSync(lockPath, '');
  const contender = new FileLock(lockPath, 'contender');
  assert.equal(contender.acquire().ok, false);
  // Age alone cannot prove that an unreadable lock has a dead owner.
  const old = new Date(Date.now() - 60_000);
  fs.utimesSync(lockPath, old, old);
  assert.equal(contender.acquire().ok, false);
  assert.equal(fs.readFileSync(lockPath, 'utf8'), '');
});

test('nunit parser requires every counter attribute', () => {
  assert.equal(parseNunitCounters('<test-run total="1" result="Passed"/>'), null);
  assert.equal(
    parseNunitCounters('<test-run total="2" passed="2" failed="0" result="Passed"/>'),
    null,
  );
  assert.equal(
    parseNunitCounters('<test-run total="2" passed="2" failed="" skipped="0" result="Passed"/>'),
    null,
  );
});

test('nunit parser rejects roots concealing failed cases', () => {
  const concealed =
    '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">' +
    '<test-case result="Failed"/></test-run>';
  assert.equal(parseNunitCounters(concealed), null);
});

test('nunit parser accepts a real Unity-shaped result', () => {
  const xml =
    '<test-run id="2" testcasecount="2" result="Passed" total="2" passed="2" failed="0" ' +
    'inconclusive="0" skipped="0" asserts="0">' +
    '<test-case id="1" result="Passed"/><test-case id="2" result="Passed"/></test-run>';
  const counters = parseNunitCounters(xml);
  assert.deepEqual(counters, { total: 2, failed: 0, skipped: 0, passed: 2 });
});

test('nunit parser requires a complete XML document, not just a root tag', () => {
  // FR-03: a truncated write leaves the opening tag intact; that must not pass.
  const truncated = '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">';
  assert.equal(parseNunitCounters(truncated), null);
  const noRootClose =
    '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">' +
    '<test-case id="1" result="Passed"/>';
  assert.equal(parseNunitCounters(noRootClose), null);
  const twoRoots =
    '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>' +
    '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>';
  assert.equal(parseNunitCounters(twoRoots), null);
  const badNesting =
    '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">' +
    '<test-suite></test-run></test-suite>';
  assert.equal(parseNunitCounters(badNesting), null);
  const duplicateAttr =
    '<test-run total="1" total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>';
  assert.equal(parseNunitCounters(duplicateAttr), null);
});

test('nunit parser handles nested suites, comments and CDATA', () => {
  const xml = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<!-- generated by the fixture -->',
    '<test-run id="2" total="3" passed="3" failed="0" skipped="0" result="Passed">',
    '  <test-suite type="TestSuite" name="A &amp; B">',
    '    <test-case id="1" result="Passed"><output><![CDATA[all <ok>]]></output></test-case>',
    '    <test-suite name="nested"><test-case id="2" result="Passed"/></test-suite>',
    '  </test-suite>',
    '  <test-case id="3" result="Passed"/>',
    '</test-run>',
  ].join('\n');
  assert.deepEqual(parseNunitCounters(xml), { total: 3, failed: 0, skipped: 0, passed: 3 });
});

test('nunit parser still refuses valid documents that report failures', () => {
  const failing =
    '<test-run total="2" passed="1" failed="1" skipped="0" result="Failed">' +
    '<test-case id="1" result="Passed"/><test-case id="2" result="Failed"/></test-run>';
  const counters = parseNunitCounters(failing);
  assert.ok(counters && counters.failed === 1, 'failure counters must survive');
});

test('slotcheck adapter derives counters and verifies its summary', () => {
  const ok = ['  [PASS] a', '  [PASS] b', 'SLOTCHECK OK'].join('\n');
  assert.equal(parseSlotcheckText(ok, 0).ok, true);
  const failing = ['  [PASS] a', '  [FAIL] b', 'SLOTCHECK FAIL 1'].join('\n');
  assert.equal(parseSlotcheckText(failing, 1).ok, false);
  // Summary disagrees with the detail lines: not a usable result.
  assert.equal(parseSlotcheckText(['  [PASS] a', 'SLOTCHECK FAIL 3'].join('\n'), 1), null);
  assert.equal(parseSlotcheckText('unrelated text', 0), null);
});

test('semcheck adapter requires all three gates', () => {
  const clean = 'errors=0 (raw=0)\ntests-asm: errors=0 (raw=0)\nenc: errors=0';
  assert.equal(parseSemcheckText(clean, 0).ok, true);
  const dirty = 'errors=0 (raw=0)\ntests-asm: errors=2 (raw=2)\nenc: errors=0';
  assert.equal(parseSemcheckText(dirty, 1).ok, false);
  assert.equal(parseSemcheckText('errors=0 (raw=0)', 0), null);
  // Non-zero exit with zero parsed gates is an infrastructure failure, not a pass.
  const infra = parseSemcheckText(clean, 1);
  assert.equal(infra.ok, false);
});

test('atomic write replaces file', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-atom-'));
  const file = path.join(dir, 's.json');
  writeJsonAtomic(file, { n: 1 });
  writeJsonAtomic(file, { n: 2 });
  assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).n, 2);
});

test('fingerprint classify detects impl change', () => {
  const a = {
    implementation: { fingerprint: 'aaa' },
    verification: { fingerprint: 'vvv' },
    contract: { fingerprint: 'ccc' },
  };
  const b = {
    implementation: { fingerprint: 'bbb' },
    verification: { fingerprint: 'vvv' },
    contract: { fingerprint: 'ccc' },
  };
  const c = classifyChange(a, b);
  assert.equal(c.implementationChanged, true);
  assert.equal(c.staleForCurrentCode, true);
});

test('report-only entries do not change hash when empty list stable', () => {
  assert.equal(hashManifest([]), hashManifest([]));
});

test('doctor runs', () => {
  const { json } = run(['doctor']);
  assert.equal(typeof json.ok, 'boolean');
  assert.ok(Array.isArray(json.checks));
});

test('init is idempotent', () => {
  run(['init']);
  const second = run(['init']);
  assert.ok(second.json.skipped?.length >= 1);
});

test('status and sync work after init', () => {
  const st = run(['status']);
  assert.ok(st.json.revision >= 1);
  const sy = run(['sync']);
  assert.ok(sy.json.revision >= 1);
  assert.ok(fs.existsSync(path.join(repo, 'docs/collaboration/CURRENT.md')));
});

test('ack CAS fails on wrong revision', () => {
  run(['ack', '--actor', 'cursor', '--revision', '999999'], { expectCode: EXIT.CONFLICT });
});

test('cursor cannot review-finish', () => {
  run(
    ['review-finish', '--actor', 'cursor', '--review', 'nope', '--evidence', 'x'],
    { expectCode: EXIT.FORBIDDEN },
  );
});

test('second claim of held task conflicts', () => {
  // Ensure demo exists and is free.
  run(['init']);
  const status = run(['status']).json;
  const demo = status.tasks.find((t) => t.taskId === 'C-DEMO');
  if (demo?.holder) {
    run(['release', '--task', 'C-DEMO', '--actor', demo.holder]);
  }
  const rev = run(['status']).json.revision;
  run(['ack', '--actor', 'cursor', '--revision', String(rev)]);
  run(['claim', '--task', 'C-DEMO', '--actor', 'cursor', '--expected-revision', String(run(['status']).json.revision)]);
  // Simulate another holder by rewriting holder then claiming as different actor is forbidden for codex;
  // claim again as cursor is ok (same holder). Conflict: set holder to other via file then claim.
  const taskFile = path.join(repo, 'docs/collaboration/tasks/C-DEMO.json');
  const task = JSON.parse(fs.readFileSync(taskFile, 'utf8'));
  // release first to drop lock, then claim and force conflict via lock file second process style:
  run(['release', '--task', 'C-DEMO', '--actor', 'cursor']);
  const rev2 = run(['status']).json.revision;
  run(['ack', '--actor', 'cursor', '--revision', String(rev2)]);
  run(['claim', '--task', 'C-DEMO', '--actor', 'cursor', '--expected-revision', String(rev2)]);
  // Same-holder re-claim without release should also require fresh ack of bumped revision.
  const rev3 = run(['status']).json.revision;
  run(['claim', '--task', 'C-DEMO', '--actor', 'cursor', '--expected-revision', String(rev3)], {
    expectCode: EXIT.CONFLICT,
  });
  run(['ack', '--actor', 'cursor', '--revision', String(rev3)]);
  run(['claim', '--task', 'C-DEMO', '--actor', 'cursor', '--expected-revision', String(rev3)]);
  run(['release', '--task', 'C-DEMO', '--actor', 'cursor']);
  assert.ok(true);
});

/* ------------------------------------------------------------------ *
 * C09: task-book mapping, completion gate and conflict handling
 * ------------------------------------------------------------------ */

const BOOK = [
  '## 3. 主任务总清单',
  '',
  '- [ ] **T9：总任务。** 状态：未完成。',
  '- [x] **T9B：另一个任务。** 状态：已验收。',
  '',
  '## 4. T9 · 分项检查',
  '',
  '- [x] T9.1 第一项。',
  '- [ ] T9.2 第二项。',
  '',
  '## 5. T9B · 另一个分项',
  '',
  '- [x] T9B.1 已完成。',
  '',
].join('\n');

function bookFixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-book-'));
  fs.mkdirSync(path.join(root, 'docs/collaboration/tasks'), { recursive: true });
  fs.writeFileSync(path.join(root, 'docs/book.md'), BOOK, 'utf8');
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
    taskBook: {
      path: 'docs/book.md',
      tasks: {
        T9: { sectionHeading: '## 4. T9 · 分项检查', itemPrefix: 'T9' },
        T9B: { sectionHeading: '## 5. T9B · 另一个分项', itemPrefix: 'T9B' },
      },
      completionTasks: ['T9', 'T9B'],
    },
  };
  const task = (taskId, extra = {}) => {
    const t = {
      schemaVersion: 1,
      projectId: 'fixture',
      taskId,
      title: taskId,
      revision: 1,
      devStatus: 'todo',
      reviewStatus: 'not_run',
      freshness: 'unknown',
      holder: null,
      scope: ['fixture'],
      acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
      dependencies: [],
      readOnlyReference: true,
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
      ...extra,
    };
    fs.writeFileSync(
      path.join(root, 'docs/collaboration/tasks', `${taskId}.json`),
      JSON.stringify(t, null, 2),
    );
    return t;
  };
  return { root, cfg, task, bookPath: path.join(root, 'docs/book.md') };
}

test('task book mapping finds one total clause and its sub-items', () => {
  const { cfg } = bookFixture();
  const clause = readClause(cfg, 'T9');
  assert.equal(clause.ok, true);
  assert.equal(clause.total.line, 3);
  assert.equal(clause.items.length, 2);
  assert.equal(clause.items.filter((i) => i.checked).length, 1);
});

test('task book mapping refuses an ambiguous total clause', () => {
  const f = bookFixture();
  fs.appendFileSync(f.bookPath, '- [ ] **T9：重复的总项。**\n', 'utf8');
  const clause = readClause(f.cfg, 'T9');
  assert.equal(clause.ok, false);
  assert.match(clause.reason, /2 checkbox lines/);
});

test('completion gate blocks until sub-items are checked, then checks the total clause', () => {
  const f = bookFixture();
  f.task('T9');
  recordBaseline(f.cfg, 'T9');
  const blocked = computeTaskGate(f.cfg, 'T9');
  assert.equal(blocked.ok, false);
  assert.ok(blocked.blockers.some((b) => b.id === 'sub-items'));
  assert.throws(
    () => writeTotalCheckbox(f.cfg, 'T9'),
    (e) => e.exitCode === EXIT.CHECK_FAILED,
  );
  assert.match(fs.readFileSync(f.bookPath, 'utf8'), /- \[ \] \*\*T9：/);

  fs.writeFileSync(f.bookPath, BOOK.replace('- [ ] T9.2', '- [x] T9.2'), 'utf8');
  recordBaseline(f.cfg, 'T9');
  const passed = computeTaskGate(f.cfg, 'T9');
  assert.equal(passed.ok, true, JSON.stringify(passed.blockers));
  const written = writeTotalCheckbox(f.cfg, 'T9', { actor: 'codex' });
  assert.equal(written.changed, true);
  assert.match(fs.readFileSync(f.bookPath, 'utf8'), /- \[x\] \*\*T9：/);
  const again = writeTotalCheckbox(f.cfg, 'T9', { actor: 'codex' });
  assert.equal(again.alreadyChecked, true);
});

test('clause edited after baseline is reported and never silently checked', () => {
  const f = bookFixture();
  f.task('T9');
  recordBaseline(f.cfg, 'T9');
  fs.writeFileSync(f.bookPath, BOOK.replace('- [ ] T9.2', '- [x] T9.2').replace('状态：未完成。', '状态：已被人工修改。'), 'utf8');
  const gate = computeTaskGate(f.cfg, 'T9');
  assert.equal(gate.ok, false);
  assert.ok(gate.blockers.some((b) => b.id === 'clause-unchanged'));
  assert.throws(
    () => writeTotalCheckbox(f.cfg, 'T9'),
    (e) => e.exitCode === EXIT.CHECK_FAILED,
  );
});

test('a baseline recorded under an older hash scheme is re-recorded, not reported as an edit', () => {
  const f = bookFixture();
  f.task('T9');
  recordBaseline(f.cfg, 'T9');
  // Simulate an upgrade: the stored baseline predates the current scheme and its
  // hash therefore cannot match, even though the clause text never changed.
  const baselineFile = path.join(f.root, '.collab-local/taskbook/baseline.json');
  const stored = JSON.parse(fs.readFileSync(baselineFile, 'utf8'));
  stored.tasks.T9.scheme = 1;
  stored.tasks.T9.clauseHash = 'deadbeef'.repeat(8);
  fs.writeFileSync(baselineFile, JSON.stringify(stored), 'utf8');

  const gate = computeTaskGate(f.cfg, 'T9');
  assert.equal(gate.ok, false);
  const baselineCheck = gate.checks.find((c) => c.id === 'clause-baseline');
  assert.equal(baselineCheck.ok, false);
  assert.match(baselineCheck.detail, /tool change, not a clause edit/);
  const unchanged = gate.checks.find((c) => c.id === 'clause-unchanged');
  assert.equal(unchanged.detail.startsWith('comparison skipped'), true);
  assert.equal(unchanged.detail.includes('clause text changed'), false);

  // Re-recording under the current scheme clears the blocker without touching
  // the task book, and the summary stops claiming the clause changed.
  const redo = recordBaseline(f.cfg, 'T9');
  assert.equal(redo.scheme, 2);
  assert.equal(redo.schemeUpgraded, true);
  assert.equal(redo.changedSincePrevious, false, 'a scheme upgrade must not claim a clause edit');
  const summary = taskBookSummary(f.cfg).find((t) => t.taskId === 'T9');
  assert.equal(summary.baselineStale, false);
  assert.equal(summary.clauseChanged, false);
});

test('defect status has one source: role-gated transitions recorded in shared state', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-issue-'));
  fs.mkdirSync(path.join(root, 'docs/collaboration/issues'), { recursive: true });
  fs.writeFileSync(
    path.join(root, 'docs/collaboration/issues/ISSUE-1.json'),
    JSON.stringify({
      schemaVersion: 1,
      issueId: 'ISSUE-1',
      taskId: 'C-DEMO',
      deliveryId: 'DEL-1',
      reviewId: 'REV-1',
      title: 'fixture defect',
      repro: 'r',
      expected: 'e',
      actual: 'a',
      evidence: 'x',
      severity: 'major',
      suggestedOwner: 'cursor',
      status: 'open',
      createdAt: '2026-01-01T00:00:00.000Z',
    }),
  );
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
  };

  // Only Codex/user may verify or close; Cursor may report fixing/fixed.
  assert.throws(
    () => cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'closed', actor: 'cursor' }),
    (e) => e.exitCode === EXIT.FORBIDDEN,
  );
  const fixing = cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'fixing', actor: 'cursor' });
  assert.equal(fixing.from, 'open');
  assert.equal(fixing.to, 'fixing');
  assert.equal(listIssues(cfg)[0].status, 'fixing');
  assert.equal(listIssues(cfg)[0].reportedStatus, 'open');

  // An illegal jump is refused instead of being silently accepted.
  assert.throws(
    () => cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'verified', actor: 'codex' }),
    (e) => e.exitCode === EXIT.CONFLICT,
  );
  cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'fixed', actor: 'cursor' });
  cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'verified', actor: 'codex' });
  const closed = cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'closed', actor: 'codex' });
  assert.equal(closed.to, 'closed');
  assert.equal(listIssues(cfg)[0].status, 'closed');

  // Re-opening is a user decision, not something Codex grants itself.
  assert.throws(
    () => cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'open', actor: 'codex' }),
    (e) => e.exitCode === EXIT.FORBIDDEN,
  );
  const reopened = cmdIssueUpdate(cfg, { issue: 'ISSUE-1', status: 'open', actor: 'user' });
  assert.equal(reopened.to, 'open');
});

test('clean checkout requirement blocks T02-style totals even when everything else passes', () => {
  const f = bookFixture();
  f.cfg.taskBook.tasks.T9.requiresCleanCheckout = true;
  f.task('T9');
  fs.writeFileSync(f.bookPath, BOOK.replace('- [ ] T9.2', '- [x] T9.2'), 'utf8');
  recordBaseline(f.cfg, 'T9');
  const gate = computeTaskGate(f.cfg, 'T9');
  assert.equal(gate.ok, false);
  assert.ok(gate.blockers.some((b) => b.id === 'clean-git-checkout'));
});

/* ------------------------------------------------------------------ *
 * C10: hook adapter dedupe / debounce / disabled
 * ------------------------------------------------------------------ */

function hookFixture(hooks) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-hook-'));
  fs.mkdirSync(path.join(root, 'docs/collaboration'), { recursive: true });
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
    hooks,
  };
  return { root, cfg };
}

test('hook applies once and suppresses the duplicate event', () => {
  const { root, cfg } = hookFixture({ enabled: true, debounceMs: 0, dedupeWindowMs: 60_000 });
  let applied = 0;
  const apply = () => {
    applied += 1;
    return { revision: 7 };
  };
  const payload = { sessionId: 's1', file: 'a.cs' };
  const first = runHook(cfg, { source: 'cursor', event: 'afterFileEdit', payload, now: 1000, apply });
  const second = runHook(cfg, { source: 'cursor', event: 'afterFileEdit', payload, now: 2000, apply });
  assert.equal(first.applied, true);
  assert.equal(first.revision, 7);
  assert.equal(second.deduped, true);
  assert.match(second.reason, /duplicate/);
  assert.equal(applied, 1);
  assert.equal(fs.existsSync(path.join(root, 'docs/collaboration/deliveries')), false);
});

test('hook preserves rapid distinct edits and dedupes only the same file version', () => {
  const { cfg } = hookFixture({ enabled: true, debounceMs: 2000, dedupeWindowMs: 60_000 });
  let applied = 0;
  const apply = () => {
    applied += 1;
    return { revision: 1 };
  };
  runHook(cfg, { source: 'cursor', event: 'afterFileEdit', payload: { file: 'a.cs' }, now: 10_000, apply });
  const debounced = runHook(cfg, {
    source: 'cursor',
    event: 'afterFileEdit',
    payload: { file: 'b.cs' },
    now: 10_500,
    apply,
  });
  assert.equal(debounced.applied, true);
  assert.equal(applied, 2);
  const later = runHook(cfg, {
    source: 'cursor',
    event: 'afterFileEdit',
    payload: { file: 'b.cs' },
    now: 20_000,
    apply,
  });
  assert.equal(later.deduped, true);
  const changed = runHook(cfg, { source: 'cursor', event: 'afterFileEdit', payload: { file: 'b.cs', fileVersion: 'new-bytes' }, now: 20_100, apply });
  assert.equal(changed.applied, true);
  assert.equal(applied, 3);
});

test('disabled hooks never apply work and say so', () => {
  const { cfg } = hookFixture({ enabled: false, debounceMs: 0, dedupeWindowMs: 60_000 });
  let applied = 0;
  const result = runHook(cfg, {
    source: 'codex',
    event: 'turnEnd',
    payload: {},
    now: 1,
    apply: () => {
      applied += 1;
      return { revision: 1 };
    },
  });
  assert.equal(result.applied, false);
  assert.match(result.reason, /disabled/);
  assert.equal(applied, 0);
});

/* ------------------------------------------------------------------ *
 * C07 supporting rules
 * ------------------------------------------------------------------ */

test('hook payload tolerates a Windows BOM but still rejects garbage', () => {
  const payloadPath = path.join(repo, 'bom-payload.json');
  fs.writeFileSync(payloadPath, '\uFEFF{"sessionId":"bom"}\n', 'utf8');
  const ok = run([
    'hook',
    '--source',
    'cursor',
    '--event',
    'sessionStart',
    '--payload',
    'bom-payload.json',
  ]);
  assert.equal(ok.json.event, 'sessionStart');
  assert.equal(ok.json.source, 'cursor');

  fs.writeFileSync(payloadPath, '\uFEFFnot json\n', 'utf8');
  const bad = run(
    ['hook', '--source', 'cursor', '--event', 'sessionStart', '--payload', 'bom-payload.json'],
    { expectCode: EXIT.USAGE },
  );
  assert.match(bad.json.error, /not valid JSON/);
});

test('freshness compares the task-declared roots and staleness keeps the passed verdict', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-fresh-'));
  const put = (rel, body) => {
    const abs = path.join(root, rel);
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, body);
  };
  put('Assets/A.cs', 'class A {}\n');
  put('tools/collab/other.mjs', 'export const other = 1;\n');
  const task = {
    schemaVersion: 1,
    projectId: 'fixture',
    taskId: 'T9',
    title: 'fixture',
    revision: 1,
    devStatus: 'ready_for_review',
    reviewStatus: 'passed',
    scope: ['fixture'],
    acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
    dependencies: [],
    readOnlyReference: false,
    snapshotRoots: ['Assets'],
    createdAt: '2026-01-01T00:00:00.000Z',
    updatedAt: '2026-01-01T00:00:00.000Z',
  };
  put('docs/collaboration/tasks/T9.json', JSON.stringify(task));
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
    demoTaskId: 'C-DEMO',
  };
  const fp = computeFingerprints(root, {
    implementationRoots: ['Assets'],
    verificationRoots: ['Assets'],
    contractPayload: stableContractPayload(task),
  });
  put(
    'docs/collaboration/deliveries/DEL-1.json',
    JSON.stringify({
      schemaVersion: 1,
      deliveryId: 'DEL-1',
      taskId: 'T9',
      fingerprints: fp,
      createdAt: '2026-01-02T00:00:00.000Z',
    }),
  );
  put(
    'docs/collaboration/reviews/REV-1.json',
    JSON.stringify({
      reviewId: 'REV-1',
      taskId: 'T9',
      deliveryId: 'DEL-1',
      status: 'passed',
      createdAt: '2026-01-02T00:01:00.000Z',
    }),
  );

  // Declared roots == delivery roots, so the pinned task is current even though
  // the project-wide defaults would also see tools/collab.
  refreshFreshness(cfg, { write: true });
  let disk = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/tasks/T9.json'), 'utf8'));
  assert.equal(disk.freshness, 'current', disk.freshnessNote);
  assert.equal(disk.reviewStatus, 'passed');

  // A change inside the pinned roots makes it stale, but the historical verdict
  // stays visible instead of being erased.
  put('Assets/A.cs', 'class A { int x; }\n');
  refreshFreshness(cfg, { write: true });
  disk = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/tasks/T9.json'), 'utf8'));
  assert.equal(disk.freshness, 'stale');
  assert.equal(disk.reviewStatus, 'passed', 'a stale tree must not erase the passed verdict');
  assert.equal(disk.staleForReviewId, 'REV-1');

  // A change outside the pinned roots does not.
  put('Assets/A.cs', 'class A {}\n');
  put('tools/collab/other.mjs', 'export const other = 2;\n');
  refreshFreshness(cfg, { write: true });
  disk = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/tasks/T9.json'), 'utf8'));
  assert.equal(disk.freshness, 'current');
  assert.equal(disk.staleForReviewId, null);
});

test('task-declared snapshot roots take precedence over the demo roots', () => {
  const cfg = { demoTaskId: 'C-DEMO', demoSnapshotRoots: ['tools/collab'], demoSnapshotFiles: ['x.md'] };
  const declared = resolveTaskRoots(cfg, { taskId: 'C-ENG', snapshotRoots: ['Unity Project/Assets'], snapshotFiles: ['a.md'] });
  assert.deepEqual(declared.implementationRoots, ['Unity Project/Assets', 'a.md']);
  assert.equal(declared.source, 'task');
  const demo = resolveTaskRoots(cfg, { taskId: 'C-DEMO' });
  assert.equal(demo.source, 'demo');
});

test('generated run output is allowed inside a snapshot but source edits are not', () => {
  assert.equal(isGeneratedFileName('semcheck_report.txt'), true);
  assert.equal(isGeneratedFileName('Assets/Scripts/Game.cs'), false);
  assert.equal(isArtifactDirName('UserSettings'), true);
  assert.equal(isArtifactDirName('Assets'), false);
});

test('profile run outputs normalize but may never declare source or traverse', () => {
  assert.deepEqual(
    resolveRunOutputRels({ runOutputs: ['tools/semcheck/semcheck_report.txt'] }),
    ['tools/semcheck/semcheck_report.txt'],
  );
  assert.deepEqual(
    resolveRunOutputRels({ runOutputs: ['\\.\\tools\\semcheck\\semcheck_report.txt'] }),
    ['tools/semcheck/semcheck_report.txt'],
  );
  assert.deepEqual(resolveRunOutputRels({}), []);
  assert.throws(
    () => resolveRunOutputRels({ runOutputs: ['tools/semcheck/run.js'] }),
    /must not declare a source file/,
  );
  assert.throws(
    () => resolveRunOutputRels({ runOutputs: ['Assets/Scripts/Smuggled.cs'] }),
    /must not declare a source file/,
  );
  assert.throws(
    () => resolveRunOutputRels({ runOutputs: ['../outside.txt'] }),
    /invalid runOutputs path/,
  );
});

test('declared run output is tolerated in a snapshot, every other stray file is not', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-snap-'));
  const snapRel = 'out/collab/DEL-TEST';
  const snapAbs = path.join(root, snapRel);
  fs.mkdirSync(path.join(snapAbs, 'tools'), { recursive: true });
  const sourcePath = path.join(snapAbs, 'tools', 'tool.js');
  fs.mkdirSync(path.dirname(sourcePath), { recursive: true });
  fs.writeFileSync(sourcePath, 'console.log(1);\n');
  const sha = crypto.createHash('sha256').update(fs.readFileSync(sourcePath)).digest('hex');
  fs.writeFileSync(
    path.join(snapAbs, 'collab-snapshot-manifest.json'),
    JSON.stringify({ entries: [{ path: 'tools/tool.js', sha256: sha, snapshotSha256: sha }] }),
  );
  const delivery = {
    deliveryId: 'DEL-TEST',
    snapshotManifest: { entries: [{ path: 'tools/tool.js', sha256: sha, snapshotSha256: sha }] },
  };

  // Baseline: a clean snapshot verifies.
  assert.ok(verifySnapshotIntegrity(root, snapRel, delivery));

  // The tool writes its own report: refused until the profile declares it.
  const report = path.join(snapAbs, 'tools', 'semcheck_report.txt');
  fs.writeFileSync(report, 'errors=0\n');
  assert.throws(
    () => verifySnapshotIntegrity(root, snapRel, delivery),
    /unlisted files: tools\/semcheck_report.txt/,
  );
  assert.ok(
    verifySnapshotIntegrity(root, snapRel, delivery, {
      runOutputRels: resolveRunOutputRels({ runOutputs: ['tools/semcheck_report.txt'] }),
    }),
  );

  // A smuggled source file is still refused even with the declaration present.
  fs.writeFileSync(path.join(snapAbs, 'tools', 'Smuggled.cs'), 'class X {}\n');
  assert.throws(
    () =>
      verifySnapshotIntegrity(root, snapRel, delivery, {
        runOutputRels: resolveRunOutputRels({ runOutputs: ['tools/semcheck_report.txt'] }),
      }),
    /unlisted files: tools\/Smuggled\.cs/,
  );

  // A listed source edited after the run is still a hard failure.
  fs.unlinkSync(path.join(snapAbs, 'tools', 'Smuggled.cs'));
  fs.writeFileSync(sourcePath, 'console.log(2);\n');
  assert.throws(
    () =>
      verifySnapshotIntegrity(root, snapRel, delivery, {
        runOutputRels: resolveRunOutputRels({ runOutputs: ['tools/semcheck_report.txt'] }),
      }),
    /snapshot tampered: tools\/tool\.js/,
  );
});

console.log(failed ? `\n${failed} failed` : '\nall tests passed');
process.exit(failed ? 1 : 0);
