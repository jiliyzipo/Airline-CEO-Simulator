// Stage automation: one command per side, without weakening any guarantee.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Never ring the local handoff chime while exercising stage-close in fixtures.
process.env.COLLAB_HANDOFF_NOTIFY = '0';

const source = path.resolve(
  process.env.COLLAB_AUDIT_TOOL_SOURCE || path.join(path.dirname(fileURLToPath(import.meta.url)), '..'),
);
const cmd = await import(pathToFileURL(path.join(source, 'lib/commands.mjs')));
const stage = await import(pathToFileURL(path.join(source, 'lib/stage.mjs')));
const state = await import(pathToFileURL(path.join(source, 'lib/state.mjs')));
const book = await import(pathToFileURL(path.join(source, 'lib/taskbook.mjs')));
const io = await import(pathToFileURL(path.join(source, 'lib/io.mjs')));

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-stage-'));
let serial = 0;
let failed = 0;
const results = [];

function put(dir, file, value) {
  const abs = path.join(dir, file);
  fs.mkdirSync(path.dirname(abs), { recursive: true });
  fs.writeFileSync(abs, typeof value === 'string' ? value : JSON.stringify(value, null, 2));
  return abs;
}

const GOOD = 'console.log(JSON.stringify({assertionsTotal:2,assertionsFailed:0,assertionsSkipped:0}))';
const BAD_COUNTERS = 'console.log(JSON.stringify({assertionsTotal:2,assertionsFailed:1,assertionsSkipped:0}))';
const BAD_EXIT = 'console.log(JSON.stringify({assertionsTotal:1,assertionsFailed:1,assertionsSkipped:0}));process.exit(1)';

function fixture(command = GOOD) {
  const dir = path.join(root, String(++serial));
  fs.cpSync(source, path.join(dir, 'tools/collab'), { recursive: true });
  const base = JSON.parse(fs.readFileSync(path.join(source, 'config.example.json'), 'utf8'));
  const cfg = {
    ...base,
    _repoRoot: dir,
    projectId: 'stage-fixture',
    taskBook: { tasks: {}, completionTasks: [] },
    profiles: {
      smoke: { kind: 'node', command: ['node', '-e', command], resultKind: 'json' },
    },
  };
  put(dir, 'tools/collab/config.json', cfg);
  put(dir, 'docs/collaboration/PROTOCOL.md', '# fixture protocol');
  cmd.cmdInit(cfg);
  put(dir, 'tools/collab/stage-input.txt', `stage input ${serial}`);
  return { dir, cfg };
}

const throwsCode = (fn, code) => assert.throws(fn, (e) => e.exitCode === code);
const NO_UNITY = () => ({ checked: true, running: false, pids: [] });
async function test(id, name, fn) {
  try {
    await fn();
    results.push({ id, name, passed: true });
    console.log(`PASS ${id} ${name}`);
  } catch (e) {
    failed += 1;
    results.push({ id, name, passed: false, error: e.stack });
    console.log(`FAIL ${id} ${name}: ${e.message}`);
  }
}

await test('T01', 'stage-open syncs, acks and returns the board', () => {
  const f = fixture();
  const out = stage.cmdStageOpen(f.cfg, { actor: 'cursor' });
  assert.equal(out.ack.actor, 'cursor');
  assert.equal(out.ack.revision, state.loadState(f.cfg).revision);
  assert.equal(state.loadAck(f.cfg).cursor.revision, out.ack.revision);
  assert(Array.isArray(out.tasks));
  assert(out.report.includes('同步完成'));
});

await test('T02', 'stage-close creates a stage task, delivers, preflights and writes a handoff', () => {
  const f = fixture();
  const out = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  assert.equal(out.exitCode, 0);
  assert.equal(out.ok, true);
  assert.match(out.deliveryId, /^DEL-/);
  const task = state.loadTask(f.cfg, out.taskId);
  assert.equal(task.sourceTaskBook, 'stage:auto');
  assert(task.acceptanceCriteria.length >= 3);
  assert.equal(task.reviewStatus, 'queued');
  const delivery = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${out.deliveryId}.json`));
  assert.equal(delivery.actor, 'cursor');
  assert.equal(delivery.recommendedProfiles[0], 'smoke');
  assert(delivery.summary.includes('阶段'));
  assert(fs.existsSync(path.join(f.dir, out.handoffFile)), 'handoff markdown must exist');
  const index = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json'));
  assert.equal(index.entries.at(-1).status, 'ready');
  assert.equal(out.preflight.ok, true);
});

await test('T03', 'a failing preflight blocks handoff and stage-review refuses to pick it', () => {
  const f = fixture(BAD_COUNTERS);
  const out = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  assert.equal(out.exitCode, 5);
  assert.equal(out.ok, false);
  const index = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json'));
  assert.equal(index.entries.at(-1).status, 'preflight-failed');
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'codex' }), 2);
});

await test('T04', 'stage-review runs the whole reviewer loop in one call and passes the gate', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex' });
  assert.equal(review.exitCode, 0);
  assert.equal(review.status, 'passed');
  assert.deepEqual(review.counters, { total: 2, failed: 0, skipped: 0 });
  assert.equal(book.computeTaskGate(f.cfg, close.taskId).ok, true);
  const record = io.readJson(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`));
  assert.equal(record.actor, 'codex');
  assert.equal(record.independent, true);
  assert.equal(record.status, 'passed');
});

await test('T05', 'assertion failure inside a successful process becomes a first-class ISSUE', () => {
  const f = fixture(BAD_COUNTERS);
  // Preflight would block the handoff, so deliver without it.
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke', 'no-preflight': true });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex' });
  assert.equal(review.exitCode, 5);
  assert.equal(review.status, 'failed');
  assert.equal(review.issues.length, 1);
  const issue = io.readJson(path.join(f.dir, `docs/collaboration/issues/${review.issues[0]}.json`));
  assert.equal(issue.taskId, close.taskId);
  assert.equal(issue.reviewId, review.reviewId);
  assert.match(issue.actual, /failed|断言|total/i);
  const fix = stage.cmdStageFix(f.cfg);
  assert.equal(fix.issues.length, 1);
  assert(fix.report.includes(review.issues[0]));
});

await test('T06', 'a non-zero exit is terminal for review-run and still records a defect', () => {
  const f = fixture(BAD_EXIT);
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke', 'no-preflight': true });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex' });
  assert.equal(review.exitCode, 5);
  assert.equal(review.status, 'failed');
  assert.equal(review.issues.length, 1);
  const issue = io.readJson(path.join(f.dir, `docs/collaboration/issues/${review.issues[0]}.json`));
  assert.equal(issue.severity, 'blocker');
  assert.equal(state.loadTask(f.cfg, close.taskId).devStatus, 'rework');
});

await test('T07', 'stage-review refuses actor=cursor without a scoped authorization', () => {
  const f = fixture();
  stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'cursor' }), 7);
});

await test('T08', 'a finished delivery is never reviewed twice', () => {
  const f = fixture();
  stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  const first = stage.cmdStageReview(f.cfg, { actor: 'codex' });
  const before = fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length;
  const second = stage.cmdStageReview(f.cfg, { actor: 'codex' });
  const after = fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length;
  assert.equal(second.alreadyReviewed, true);
  assert.equal(second.reviewId, first.reviewId);
  assert.equal(after, before);
});

await test('T09', 'an unchanged workspace refuses to produce an empty delivery', () => {
  const f = fixture();
  stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  throwsCode(() => stage.cmdStageClose(f.cfg, { profile: 'smoke' }), 5);
});

await test('T10', 'after a pass, new changes get a fresh stage task instead of rewriting history', () => {
  const f = fixture();
  const first = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  stage.cmdStageReview(f.cfg, { actor: 'codex' });
  put(f.dir, 'tools/collab/stage-input.txt', 'next stage');
  const second = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  assert.notEqual(second.taskId, first.taskId);
  assert.equal(fs.existsSync(path.join(f.dir, `docs/collaboration/deliveries/${first.deliveryId}.json`)), true);
});

await test('T11', 'profile selection follows the changed paths', () => {
  const cfg = {
    profiles: { 'unity-editmode': {}, compile: {}, collab: {}, slot: {}, smoke: {}, balance: {} },
  };
  assert.equal(stage.selectProfile(cfg, [{ path: 'Unity Project/Assets/Scripts/Logic/A.cs' }]).profile, 'unity-editmode');
  assert.equal(stage.selectProfile(cfg, [{ path: 'tools/collab/lib/io.mjs' }]).profile, 'collab');
  assert.equal(stage.selectProfile(cfg, [{ path: 'Unity Project/Assets/Tests/EditMode/X.cs' }]).profile, 'unity-editmode');
  assert.equal(stage.selectProfile(cfg, [{ path: 'docs/readme.md' }]).profile, 'smoke');
  assert.equal(stage.selectProfile(cfg, [{ path: 'a.cs' }], { profile: 'slot' }).profile, 'slot');
  // tools/balance 的 C# 改动必须走现金桥闸门，而不是退化成只编译 Unity Assets 的 compile：
  // 后者根本不编译 tools/balance，等于「闸门没碰到被改的代码」。
  assert.equal(stage.selectProfile(cfg, [{ path: 'tools/balance/Sim/CashBridge.cs' }]).profile, 'balance');
  assert.equal(stage.selectProfile(cfg, [{ path: 'tools/balance/README.md' }]).profile, 'balance');
});

await test('T12', 'dry-run inspects without writing any collaborative record', () => {
  const f = fixture();
  const before = fs.readdirSync(path.join(f.dir, 'docs/collaboration/deliveries')).length;
  const out = stage.cmdStageClose(f.cfg, { profile: 'smoke', 'dry-run': true });
  const after = fs.readdirSync(path.join(f.dir, 'docs/collaboration/deliveries')).length;
  assert.equal(out.dryRun, true);
  assert.equal(after, before);
  assert.match(out.report, /dry-run/);
});

await test('T13', 'stage-review only picks deliveries that stage-close handed off', () => {
  const f = fixture();
  stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  fs.rmSync(path.join(f.dir, 'out/collab/handoff/index.json'), { force: true });
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'codex' }), 2);
  const board = stage.cmdStageFix(f.cfg);
  assert.equal(board.ok, true);
});

await test('T14', '--roots narrows the delivery snapshot to the declared scope', () => {
  const f = fixture();
  const out = stage.cmdStageClose(f.cfg, {
    task: 'S01-TOOL',
    title: 'tool-only stage',
    profile: 'smoke',
    roots: 'tools/collab',
  });
  assert.equal(out.exitCode, 0);
  const task = state.loadTask(f.cfg, 'S01-TOOL');
  assert.deepEqual(task.snapshotRoots, ['tools/collab']);
  const delivery = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${out.deliveryId}.json`));
  assert(delivery.snapshotManifest.entries.length > 0);
  assert(
    delivery.snapshotManifest.entries.every((e) => String(e.path).replace(/\\/g, '/').startsWith('tools/collab/')),
  );
});

await test('T15', 'a docs-only stage delivers its docs and can run its own profile', () => {
  const dir = path.join(root, String(++serial));
  fs.cpSync(source, path.join(dir, 'tools/collab'), { recursive: true });
  const base = JSON.parse(fs.readFileSync(path.join(source, 'config.example.json'), 'utf8'));
  const cfg = {
    ...base,
    _repoRoot: dir,
    projectId: 'stage-fixture',
    taskBook: { tasks: {}, completionTasks: [] },
    // The real smoke profile reads the CLI and the protocol file; using it here
    // is what proves the snapshot actually contains what the profile reads.
    profiles: { smoke: base.profiles.smoke },
  };
  put(dir, 'tools/collab/config.json', cfg);
  put(dir, 'docs/collaboration/PROTOCOL.md', '# fixture protocol');
  put(dir, 'docs/handoff/base.md', '# base');
  cmd.cmdInit(cfg);

  const git = (argv) => spawnSync('git', argv, { cwd: dir, encoding: 'utf8', windowsHide: true });
  assert.equal(git(['init', '-q', '-b', 'main']).status, 0);
  git(['config', 'user.name', 'fixture']);
  git(['config', 'user.email', 'fixture@example.invalid']);
  assert.equal(git(['add', '-A']).status, 0);
  assert.equal(git(['commit', '-q', '-m', 'init']).status, 0);

  put(dir, 'docs/handoff/new-note.md', '# new note');
  const dry = stage.cmdStageClose(cfg, { 'dry-run': true });
  assert.equal(dry.dryRun, true);
  assert.equal(dry.profile, 'smoke');
  assert.ok(
    dry.wouldCreateTask.snapshotRoots.includes('docs/handoff'),
    JSON.stringify(dry.wouldCreateTask.snapshotRoots),
  );
  assert.ok(
    dry.wouldCreateTask.snapshotRoots.includes('tools/collab'),
    'the profile support root must be declared',
  );
  const covered = (file) =>
    (dry.wouldCreateTask.snapshotFiles || []).includes(file) ||
    dry.wouldCreateTask.snapshotRoots.some((r) => file === r || file.startsWith(`${r}/`));
  assert.ok(
    covered('docs/collaboration/PROTOCOL.md'),
    'the file the profile reads must be in the declared scope',
  );

  // And the real run must pass preflight: previously the snapshot held no docs
  // at all, so the smoke check failed on a file the export never copied.
  const out = stage.cmdStageClose(cfg, {});
  assert.equal(out.exitCode, 0);
  const task = state.loadTask(cfg, out.taskId);
  assert.ok(task.snapshotRoots.includes('docs/handoff'));
  const delivery = io.readJson(path.join(dir, `docs/collaboration/deliveries/${out.deliveryId}.json`));
  const paths = delivery.snapshotManifest.entries.map((e) => String(e.path).replace(/\\/g, '/'));
  assert.ok(paths.includes('docs/handoff/new-note.md'), 'the delivered document is in the snapshot');
  assert.equal(out.preflight?.ok, true);
});

/* --- independent-review regressions: SA01..SA05 --------------------------- */
await test('SA03', 'an acceptance criterion without a machine mapping is never written as passed', () => {
  const f = fixture();
  const task = state.loadTask(f.cfg, 'C-DEMO');
  task.acceptanceCriteria = [
    { id: 'AC1', text: 'The required-proof.txt artifact must exist and contain an approved manual verification.' },
  ];
  state.saveTask(f.cfg, task);

  const close = stage.cmdStageClose(f.cfg, { task: 'C-DEMO', profile: 'smoke' });
  // S16/AC5: the clause cannot be proved by any run, so the review is refused
  // before it starts instead of producing a failure that teaches nothing.
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId }), 2);
  assert.equal(
    fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).filter((x) => x.endsWith('.json')).length,
    0,
    'no review record may exist for an attempt that provably cannot conclude',
  );
  assert.equal(fs.existsSync(path.join(f.dir, 'required-proof.txt')), false);

  // The same delivery can still be closed only by an explicit, attributable
  // reviewer attestation for the unmapped clause.
  const attested = stage.cmdStageReview(f.cfg, {
    actor: 'codex',
    delivery: close.deliveryId,
    coverage: 'AC1=passed',
  });
  assert.equal(attested.status, 'passed');
  const evidence = JSON.parse(
    fs.readFileSync(
      path.join(f.dir, io.readJson(path.join(f.dir, `docs/collaboration/reviews/${attested.reviewId}.json`)).evidencePath),
      'utf8',
    ),
  );
  assert.equal(evidence.attestation.by, 'codex');
  assert.deepEqual(evidence.attestation.clauses, [{ id: 'AC1', result: 'passed' }]);
  assert(evidence.coverage.every((c) => c.result === 'passed'));

  // An unknown criterion id is a usage error, never a silent pass.
  throwsCode(
    () =>
      stage.cmdStageReview(f.cfg, {
        actor: 'codex',
        delivery: close.deliveryId,
        force: true,
        coverage: 'AC9=passed',
      }),
    2,
  );
});

await test('SA02', 'unity preflight never executes while another review holds the shared lease', () => {
  const f = fixture();
  const sentinel = path.join(f.dir, 'preflight-executed.txt');
  f.cfg.profiles['unity-editmode'] = {
    command: [process.execPath, '-e', `require('node:fs').writeFileSync(${JSON.stringify(sentinel)},'executed');${GOOD}`],
    resultKind: 'json',
  };
  const delivered = cmd.cmdDeliver(f.cfg, {
    actor: 'cursor',
    task: 'C-DEMO',
    summary: 'lease fixture',
    profiles: 'unity-editmode',
  });
  const delivery = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${delivered.deliveryId}.json`));

  const lock = new io.FileLock(
    path.join(f.dir, '.collab-local/locks/unity-review.lock'),
    'other-review',
    { reviewId: 'REV-other' },
  );
  assert.equal(lock.acquire().ok, true);
  let code = null;
  try {
    stage.runProfileInSnapshot(f.cfg, delivery, 'unity-editmode', { unityCheck: NO_UNITY });
  } catch (e) {
    code = e.exitCode;
  } finally {
    lock.release({ force: true });
  }
  assert.equal(code, 4, 'a held Unity lease must refuse the preflight');
  assert.equal(fs.existsSync(sentinel), false, 'the profile must not have executed');

  // Occupancy that cannot be verified must also fail closed.
  const unknown = () => ({ checked: false, running: null, pids: [], error: 'probe failed' });
  throwsCode(() => stage.runProfileInSnapshot(f.cfg, delivery, 'unity-editmode', { unityCheck: unknown }), 4);

  // A free lease lets the same preflight run normally.
  const ok = stage.runProfileInSnapshot(f.cfg, delivery, 'unity-editmode', { unityCheck: NO_UNITY });
  assert.equal(ok.ok, true);
  assert.equal(ok.exitCode, 0);
  assert.equal(fs.existsSync(sentinel), true);
  assert.equal(fs.existsSync(path.join(f.dir, '.collab-local/locks/unity-review.lock')), false, 'lease released');
});

await test('SA01', 'a preflight that mutates its snapshot input cannot be handed off as ready', () => {
  const f = fixture(
    "require('node:fs').writeFileSync('tools/collab/stage-input.txt','tampered');" + GOOD,
  );
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  assert.equal(close.exitCode, 5);
  assert.equal(close.preflight.ok, false);
  assert.match(close.preflight.reason, /integrity/i);

  const entry = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json')).entries.at(-1);
  assert.notEqual(entry.status, 'ready');

  const delivery = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${close.deliveryId}.json`));
  let integrityCode = null;
  try {
    cmd.verifySnapshotIntegrity(f.dir, delivery.snapshotPath, delivery);
  } catch (e) {
    integrityCode = e.exitCode;
  }
  assert.equal(integrityCode, 5);

  // The polluted delivery must not be picked by the reviewer either.
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'codex' }), 2);
});

await test('SA04', 'two concurrent stage-review commands execute the delivery only once', async () => {
  const f = fixture();
  const marker = path.join(f.dir, 'executions.txt');
  const release = path.join(f.dir, 'release');
  f.cfg.profiles.smoke.command = [
    process.execPath,
    '-e',
    `const fs=require('node:fs');fs.appendFileSync(${JSON.stringify(marker)},process.pid+'\\n');` +
      `const end=Date.now()+20000;while(!fs.existsSync(${JSON.stringify(release)})){` +
      `if(Date.now()>end)process.exit(2);Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,20)};` +
      GOOD,
  ];
  put(f.dir, 'tools/collab/config.json', f.cfg);
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke', 'no-preflight': true });

  const worker = put(
    f.dir,
    'worker.mjs',
    `import{cmdStageReview}from ${JSON.stringify(pathToFileURL(path.join(source, 'lib/stage.mjs')).href)};` +
      `try{console.log(JSON.stringify(cmdStageReview(${JSON.stringify(f.cfg)},{actor:'codex',delivery:${JSON.stringify(close.deliveryId)}})))}` +
      `catch(e){console.log(JSON.stringify({error:e.message,exitCode:e.exitCode}));process.exitCode=e.exitCode||1}`,
  );

  const { spawn } = await import('node:child_process');
  const launched = [];
  const launch = () => {
    const cp = spawn(process.execPath, [worker], { cwd: f.dir, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let out = '';
    cp.stdout.on('data', (d) => { out += d; });
    cp.stderr.on('data', (d) => { out += d; });
    launched.push({ cp, done: new Promise((r) => cp.on('close', (code) => r({ code, out }))) });
  };
  const count = () =>
    fs.existsSync(marker) ? fs.readFileSync(marker, 'utf8').trim().split(/\r?\n/).filter(Boolean).length : 0;
  const until = async (fn, ms) => {
    const end = Date.now() + ms;
    while (!fn() && Date.now() < end) await new Promise((r) => setTimeout(r, 25));
  };

  let executions = 0;
  let outcomes = [];
  try {
    launch();
    await until(() => count() >= 1, 15000);
    assert(count() >= 1, 'first command never started');
    launch();
    await until(() => count() >= 2, 4000);
    executions = count();
  } finally {
    put(f.dir, 'release', 'go');
    outcomes = (await Promise.all(launched.map((w) => w.done))).map((r) => {
      try {
        return JSON.parse(r.out.trim().split('\n').filter(Boolean).at(-1));
      } catch {
        return { code: r.code, out: r.out.slice(-400) };
      }
    });
  }
  assert.equal(executions, 1, `profile ran ${executions} times; outcomes=${JSON.stringify(outcomes)}`);
  assert.equal(fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length, 1);
  assert(outcomes.some((o) => o.status === 'passed' || o.exitCode === 3 || o.exitCode === 5));
});

await test('SA05', 'a scoped reverse-review grant accepted by review-* also works through stage-review', () => {
  const f = fixture();
  // Focus this probe on authorization, so give the task a machine-mapped contract.
  const demo = state.loadTask(f.cfg, 'C-DEMO');
  demo.acceptanceCriteria = stage.acceptanceCriteriaFor('smoke', 'C-DEMO');
  state.saveTask(f.cfg, demo);
  const devGrant = put(f.dir, 'developer.json', {
    schemaVersion: 1,
    grantedBy: 'user',
    actor: 'codex',
    projectId: f.cfg.projectId,
    taskId: 'C-DEMO',
    actions: ['deliver'],
    allowedRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'],
    statement: 'fixture development',
  });
  const delivered = cmd.cmdDeliver(f.cfg, {
    actor: 'codex',
    task: 'C-DEMO',
    authorization: devGrant,
    summary: 'reverse fixture',
    profiles: 'smoke',
  });
  const delivery = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${delivered.deliveryId}.json`));

  const grantBody = {
    schemaVersion: 1,
    grantedBy: 'user',
    actor: 'cursor',
    developerActor: 'codex',
    projectId: f.cfg.projectId,
    taskId: 'C-DEMO',
    deliveryId: delivery.deliveryId,
    statement: 'fixture review',
    profiles: ['smoke'],
    actions: ['review-start', 'review-run', 'review-finish', 'review-evidence', 'review-cancel'],
    subjectFingerprints: Object.fromEntries(
      ['implementation', 'verification', 'contract'].map((p) => [p, delivery.fingerprints[p].fingerprint]),
    ),
  };
  const grant = put(f.dir, 'review.json', grantBody);

  const review = stage.cmdStageReview(f.cfg, {
    delivery: delivery.deliveryId,
    actor: 'cursor',
    authorization: grant,
  });
  assert.equal(review.status, 'passed');
  const record = io.readJson(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`));
  assert.equal(record.actor, 'cursor');
  assert.equal(record.reviewKind, 'independent');
  assert(record.authorization?.sha256);

  // Wrong delivery / missing grant must still be refused.
  const other = put(f.dir, 'review-wrong.json', { ...grantBody, deliveryId: 'DEL-19700101000000-000000' });
  throwsCode(
    () => stage.cmdStageReview(f.cfg, { delivery: delivery.deliveryId, actor: 'cursor', authorization: other, force: true }),
    7,
  );
  throwsCode(() => stage.cmdStageReview(f.cfg, { delivery: delivery.deliveryId, actor: 'cursor' }), 7);
});

await test('SA06', 'the normal machine-only happy path still passes end to end', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId });
  assert.equal(review.status, 'passed');
  assert.equal(review.counters.total, 2);
});

await test('SUP01', 'a retired delivery stops pinning its task to stale', () => {
  const f = fixture();
  const first = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  const taskId = first.taskId;
  assert.equal(state.loadTask(f.cfg, taskId).freshness, 'current');

  // A newer record for the same task whose snapshot no longer describes this tree
  // — exactly what a mislabeled close looks like from the task's point of view.
  const src = JSON.parse(
    fs.readFileSync(path.join(f.dir, 'docs/collaboration/deliveries', `${first.deliveryId}.json`), 'utf8'),
  );
  const ghostId = 'DEL-29990101000000-ghost1';
  put(f.dir, `docs/collaboration/deliveries/${ghostId}.json`, {
    ...src,
    deliveryId: ghostId,
    createdAt: new Date(Date.parse(src.createdAt) + 60_000).toISOString(),
    fingerprints: {
      ...src.fingerprints,
      implementation: { ...src.fingerprints.implementation, fingerprint: 'deadbeef'.repeat(8) },
    },
  });

  cmd.cmdSync(f.cfg, {});
  const lied = state.loadTask(f.cfg, taskId);
  assert.equal(lied.freshness, 'stale');
  assert.equal(lied.freshnessNote, 'Delivery snapshot differs from current workspace.');

  // Retiring the liar must fall back to the delivery that still matches the tree.
  const marked = state.markDeliveriesSuperseded(
    f.cfg,
    [{ deliveryId: ghostId, supersededBy: first.deliveryId, taskId }],
    { actor: 'cursor', note: 'test' },
  );
  assert.deepEqual(marked.marked, [ghostId]);
  cmd.cmdSync(f.cfg, {});
  const task = state.loadTask(f.cfg, taskId);
  assert.equal(task.freshness, 'current');
  assert.equal(task.freshnessNote, 'Delivery matches current fingerprints; awaiting review.');
});

await test('SUP02', 'reconciling the handoff index into the ledger is idempotent', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  // Second close for the same task supersedes the first in the handoff index.
  put(f.dir, 'tools/collab/stage-input.txt', 'second input');
  const second = stage.cmdStageClose(f.cfg, { task: close.taskId, roots: 'tools/collab', profile: 'smoke' });

  const ledger = state.loadState(f.cfg).deliveryStatus || {};
  assert.equal(ledger[close.deliveryId]?.status, 'superseded');
  assert.equal(ledger[close.deliveryId]?.supersededBy, second.deliveryId);

  const again = stage.reconcileSupersededLedger(f.cfg, { actor: 'cursor' });
  assert.deepEqual(again.marked, [], 'an already-recorded supersede must not be rewritten');
});

await test('SUP03', 'stage-close can retire a delivery filed under another task', () => {
  const f = fixture();
  const mislabeled = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });

  // A second, unrelated task delivers the same work; it declares the old one dead.
  const def = {
    taskId: 'X-Retire',
    title: 'fixture task that retires a mislabeled delivery',
    scope: ['fixture'],
    acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
    snapshotRoots: ['tools/collab'],
  };
  const defPath = put(f.dir, 'tools/collab/retire-def.json', def);
  cmd.cmdTaskAdd(f.cfg, { definition: defPath, actor: 'cursor' });
  put(f.dir, 'tools/collab/stage-input.txt', 'retire input');
  const retiring = stage.cmdStageClose(f.cfg, {
    task: 'X-Retire',
    roots: 'tools/collab',
    profile: 'smoke',
    supersedes: mislabeled.deliveryId,
  });

  const ledger = state.loadState(f.cfg).deliveryStatus[mislabeled.deliveryId];
  assert.equal(ledger.status, 'superseded');
  assert.equal(ledger.supersededBy, retiring.deliveryId);
  assert.deepEqual(
    retiring.sweptHandoffs,
    [mislabeled.deliveryId],
    'a delivery retired across tasks must leave the pending-review list',
  );
  assert.deepEqual(
    stage.cmdStageBoard(f.cfg).tasks.find((t) => t.taskId === mislabeled.taskId).latestDelivery,
    null,
    'a retired delivery must not be reported as the task\'s latest work',
  );
  // Naming a delivery that does not exist is a typo, and a typo must not silently
  // retire nothing.
  assert.throws(
    () =>
      stage.cmdStageClose(f.cfg, {
        task: 'X-Retire',
        roots: 'tools/collab',
        profile: 'smoke',
        supersedes: 'DEL-19700101000000-000000',
      }),
    (e) => e.exitCode === 2,
  );
});

/* ------------------------------------------------------------------ *
 * SUP04: 已取代的交付不能继续挂在「待 Codex 检测」上
 * ------------------------------------------------------------------ */

await test('SUP04', 'a superseded delivery is never listed as pending review', () => {
  const f = fixture();
  const stale = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });

  // The S26 → S27 shape: a second task carries the same work forward, and the
  // older delivery is retired afterwards. Nothing about the old snapshot is wrong;
  // it is simply no longer what anyone should measure.
  const def = {
    taskId: 'X-Replacement',
    title: 'fixture task that carries work forward',
    scope: ['fixture'],
    acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
    snapshotRoots: ['tools/collab'],
  };
  cmd.cmdTaskAdd(f.cfg, { definition: put(f.dir, 'tools/collab/replacement-def.json', def), actor: 'cursor' });
  put(f.dir, 'tools/collab/stage-input.txt', 'replacement input');
  const successor = stage.cmdStageClose(f.cfg, { task: 'X-Replacement', roots: 'tools/collab', profile: 'smoke' });

  const boardBefore = stage.cmdStageBoard(f.cfg);
  assert.ok(boardBefore.report.includes(stale.deliveryId), 'both deliveries start out pending');

  const superseded = stage.cmdStageSupersede(f.cfg, {
    delivery: stale.deliveryId,
    by: successor.deliveryId,
    actor: 'cursor',
  });
  assert.deepEqual(superseded.sweptHandoffs, [stale.deliveryId], 'the local index is swept on supersede');

  const board = stage.cmdStageBoard(f.cfg);
  assert.ok(!board.report.includes(stale.deliveryId), 'a retired delivery must not be named as pending');
  assert.ok(board.report.includes(successor.deliveryId), 'the live delivery is still pending');

  // The write-time sweep is a cache repair, not the guarantee: a fresh clone has
  // no `out/` at all, and any stale copy of the index would otherwise resurrect
  // the retired delivery. Rebuild the lying index deliberately.
  const indexPath = path.join(f.dir, 'out/collab/handoff/index.json');
  const index = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
  put(f.dir, 'out/collab/handoff/index.json', {
    ...index,
    entries: index.entries.map((e) =>
      e.deliveryId === stale.deliveryId ? { ...e, status: 'ready', supersededBy: undefined } : e,
    ),
  });
  const reopened = stage.cmdStageBoard(f.cfg);
  assert.ok(
    !reopened.report.includes(stale.deliveryId),
    'the ledger, not the local index, decides what is still pending',
  );
});

/* ------------------------------------------------------------------ *
 * SUP05: 已退场的任务不能被当成「进行中」续做
 * ------------------------------------------------------------------ */

await test('SUP05', 'stage-close does not resume a task that was superseded', () => {
  const f = fixture();
  const retired = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });

  const def = {
    taskId: 'X-Successor',
    title: 'fixture task that takes over',
    scope: ['fixture'],
    acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
    snapshotRoots: ['tools/collab'],
  };
  cmd.cmdTaskAdd(f.cfg, { definition: put(f.dir, 'tools/collab/successor-def.json', def), actor: 'cursor' });
  put(f.dir, 'tools/collab/stage-input.txt', 'successor input');
  stage.cmdStageClose(f.cfg, {
    task: 'X-Successor',
    roots: 'tools/collab',
    profile: 'smoke',
    supersedes: retired.deliveryId,
  });
  assert.ok(state.loadTask(f.cfg, retired.taskId).supersededBy, 'the old task is retired');

  // The next close names no task. An open-but-retired task would be picked up as
  // "the stage in progress" and force --refresh-ac on work nobody will ever ship.
  put(f.dir, 'tools/collab/stage-input.txt', 'third input');
  const next = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  assert.notEqual(next.taskId, retired.taskId, 'a retired task must not be silently resumed');
  assert.notEqual(next.taskId, 'X-Successor', 'a passed task must not be resumed either');
});

/* ------------------------------------------------------------------ *
 * S21: a reused stage task must not keep another profile's contract
 * ------------------------------------------------------------------ */

await test('T09', 'stage-close refuses a task whose AC were generated for a different profile', () => {
  const f = fixture();
  // The fixture ships one profile; add a second one whose AC template differs.
  f.cfg.profiles.collab = { kind: 'node', command: f.cfg.profiles.smoke.command, resultKind: 'json' };
  io.writeJsonAtomic(path.join(f.dir, 'tools/collab/config.json'), f.cfg);

  const first = stage.cmdStageClose(f.cfg, { profile: 'collab', 'no-preflight': true });
  assert.equal(first.exitCode, 0);
  const task = state.loadTask(f.cfg, first.taskId);
  assert.match(task.acceptanceCriteria[1].text, /协作工具回归套件/);

  // Same task, a profile that proves something else entirely (this is the S20
  // shape: a blocked unity-editmode run created the task, the retry ran collab).
  put(f.dir, 'tools/collab/stage-input.txt', `stage input again ${serial}`);
  throwsCode(() => stage.cmdStageClose(f.cfg, { task: first.taskId, profile: 'smoke', 'no-preflight': true }), 2);

  // Nothing may have been written by the refused close.
  const deliveries = fs.readdirSync(path.join(f.dir, 'docs/collaboration/deliveries'));
  assert.equal(deliveries.length, 1, 'a refused close must not file a delivery');
  assert.match(state.loadTask(f.cfg, first.taskId).acceptanceCriteria[1].text, /协作工具回归套件/);
});

await test('T10', '--refresh-ac re-derives the criteria so the same task can close under a new profile', () => {
  const f = fixture();
  f.cfg.profiles.collab = { kind: 'node', command: f.cfg.profiles.smoke.command, resultKind: 'json' };
  io.writeJsonAtomic(path.join(f.dir, 'tools/collab/config.json'), f.cfg);

  const first = stage.cmdStageClose(f.cfg, { profile: 'collab', 'no-preflight': true });
  const before = state.loadTask(f.cfg, first.taskId);
  put(f.dir, 'tools/collab/stage-input.txt', `stage input once more ${serial}`);

  const second = stage.cmdStageClose(f.cfg, {
    task: first.taskId,
    profile: 'smoke',
    'no-preflight': true,
    'refresh-ac': true,
  });
  assert.equal(second.exitCode, 0);
  const after = state.loadTask(f.cfg, first.taskId);
  assert.match(after.acceptanceCriteria[1].text, /冒烟 profile/);
  assert.equal(after.revision, before.revision + 1, 're-deriving criteria is a task revision');
  // The old contract is preserved in the event log, not silently replaced.
  const events = fs.readdirSync(path.join(f.dir, 'docs/collaboration/events'))
    .map((n) => io.readJson(path.join(f.dir, 'docs/collaboration/events', n)))
    .filter((e) => e.action === 'stage-refresh-ac');
  assert.equal(events.length, 1);
  assert.equal(events[0].data.from, 'collab');
  assert.equal(events[0].data.to, 'smoke');
  assert.match(events[0].data.previous[1].text, /协作工具回归套件/);
});

/* ------------------------------------------------------------------ *
 * stage-retract: a delivery filed under the wrong task
 * ------------------------------------------------------------------ */

await test('R01', 'stage-retract withdraws a wrong-task delivery without deleting anything', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  const handoff = path.join(f.dir, `out/collab/handoff/${close.deliveryId}.md`);
  assert.match(fs.readFileSync(handoff, 'utf8'), /stage-review --delivery/, 'a ready handoff offers the command');

  const out = stage.cmdStageRetract(f.cfg, {
    delivery: close.deliveryId,
    reason: '归属错误：卷入其他任务改动，本阶段无自有改动',
  });
  assert.equal(out.exitCode, 0);
  assert.equal(out.ok, true);

  // ① the ledger records it, and distinguishes it from supersede
  const entry = state.loadState(f.cfg).deliveryStatus[close.deliveryId];
  assert.equal(entry.status, 'withdrawn');
  assert.equal(entry.supersededBy, null, 'withdrawal needs no successor');
  assert.match(entry.note, /归属错误/);

  // ② nothing is deleted: delivery, snapshot and handoff all survive
  assert(fs.existsSync(path.join(f.dir, `docs/collaboration/deliveries/${close.deliveryId}.json`)));
  assert(fs.existsSync(handoff));

  // ③ the handoff stops offering a review command, and says why
  const text = fs.readFileSync(handoff, 'utf8');
  assert(!/stage-review --delivery/.test(text), 'a withdrawn handoff must not offer the command');
  assert.match(text, /已撤回/);

  // ④ the pending list stops naming it
  const board = stage.cmdStageBoard(f.cfg);
  assert(!board.report.includes(close.deliveryId), 'a withdrawn delivery must leave the pending list');
  const index = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json'));
  assert.equal(index.entries.at(-1).status, 'withdrawn');
});

await test('R02', 'stage-retract refuses a reviewed delivery: a verdict is history', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  stage.cmdStageReview(f.cfg, { actor: 'codex' });

  throwsCode(() => stage.cmdStageRetract(f.cfg, { delivery: close.deliveryId, reason: '想把通过记录撤掉' }), 3);
  // Refusal must not have written the ledger.
  assert.equal((state.loadState(f.cfg).deliveryStatus || {})[close.deliveryId], undefined);
  const index = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json'));
  assert.equal(index.entries.at(-1).status, 'accepted', 'a reviewed handoff keeps its verdict');
});

await test('R03', 'stage-retract refuses the reviewer, a missing reason, and a superseded delivery', () => {
  const f = fixture();
  const first = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  put(f.dir, 'tools/collab/stage-input.txt', `second ${serial}`);
  const second = stage.cmdStageClose(f.cfg, { task: first.taskId, profile: 'smoke' });

  // The reviewer may find a delivery should go, but must not erase what it reviews.
  throwsCode(() => stage.cmdStageRetract(f.cfg, { delivery: second.deliveryId, reason: 'x', actor: 'codex' }), 7);
  // A reason is the whole record; without one the withdrawal is unreadable.
  throwsCode(() => stage.cmdStageRetract(f.cfg, { delivery: second.deliveryId }), 2);
  throwsCode(() => stage.cmdStageRetract(f.cfg, { delivery: second.deliveryId, reason: '   ' }), 2);
  // Superseded is already a recorded fact; withdrawing it would blur the two.
  stage.cmdStageSupersede(f.cfg, { delivery: first.deliveryId, by: second.deliveryId, actor: 'cursor' });
  throwsCode(() => stage.cmdStageRetract(f.cfg, { delivery: first.deliveryId, reason: '想改成撤回' }), 3);
  assert.equal(state.loadState(f.cfg).deliveryStatus[first.deliveryId].status, 'superseded');
});

await test('R04', 'a withdrawn delivery is not this task\'s latest work in board or freshness', () => {
  const f = fixture();
  const first = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  stage.cmdStageRetract(f.cfg, { delivery: first.deliveryId, reason: '误打包他人改动' });

  // The board must fall back to "no live delivery" rather than name the withdrawn one.
  const board = stage.cmdStageBoard(f.cfg);
  const row = board.tasks.find((t) => t.taskId === first.taskId);
  assert.equal(row.latestDelivery, null, 'a withdrawn delivery is not the task latest work');
});

await test('R05', 'explicit stage-review on a withdrawn delivery is refused and creates no review', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  stage.cmdStageRetract(f.cfg, { delivery: close.deliveryId, reason: '归属错误' });
  const reviewsBefore = fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length;

  // AUDIT-R05: filtering the pending list is not access control — naming the
  // withdrawn delivery explicitly used to create a REV and report passed.
  throwsCode(() => stage.cmdStageReview(f.cfg, { delivery: close.deliveryId, actor: 'codex' }), 3);
  // The lower-level entry point must refuse too, not just the sugar on top.
  throwsCode(() => cmd.cmdReviewStart(f.cfg, { delivery: close.deliveryId, actor: 'codex' }), 3);

  assert.equal(fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length, reviewsBefore, 'no review may be created');
  const issues = fs.readdirSync(path.join(f.dir, 'docs/collaboration/issues')).length;
  assert.equal(issues, 0, 'a refusal must not manufacture a defect either');
  assert.equal(state.loadState(f.cfg).deliveryStatus[close.deliveryId].status, 'withdrawn');
});

await test('R06', 'explicit stage-review on a superseded delivery is refused, and --dry-run does not plan it', () => {
  const f = fixture();
  const first = stage.cmdStageClose(f.cfg, { profile: 'smoke' });
  put(f.dir, 'tools/collab/stage-input.txt', `successor ${serial}`);
  const second = stage.cmdStageClose(f.cfg, { task: first.taskId, profile: 'smoke' });
  stage.cmdStageSupersede(f.cfg, { delivery: first.deliveryId, by: second.deliveryId, actor: 'cursor' });

  throwsCode(() => stage.cmdStageReview(f.cfg, { delivery: first.deliveryId, actor: 'codex' }), 3);
  // A preview must not describe a plan for something that may not run.
  throwsCode(
    () => stage.cmdStageReview(f.cfg, { delivery: first.deliveryId, actor: 'codex', 'dry-run': true }),
    3,
  );
});

console.log(JSON.stringify({ assertionsTotal: results.length, assertionsFailed: failed, assertionsSkipped: 0 }));
process.exitCode = failed ? 1 : 0;
