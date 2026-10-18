// Stage automation: one command per side, without weakening any guarantee.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

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
    profiles: { 'unity-editmode': {}, compile: {}, collab: {}, slot: {}, smoke: {} },
  };
  assert.equal(stage.selectProfile(cfg, [{ path: 'Unity Project/Assets/Scripts/Logic/A.cs' }]).profile, 'unity-editmode');
  assert.equal(stage.selectProfile(cfg, [{ path: 'tools/collab/lib/io.mjs' }]).profile, 'collab');
  assert.equal(stage.selectProfile(cfg, [{ path: 'Unity Project/Assets/Tests/EditMode/X.cs' }]).profile, 'unity-editmode');
  assert.equal(stage.selectProfile(cfg, [{ path: 'docs/readme.md' }]).profile, 'smoke');
  assert.equal(stage.selectProfile(cfg, [{ path: 'a.cs' }], { profile: 'slot' }).profile, 'slot');
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

/* --- independent-review regressions: SA01..SA05 --------------------------- */

await test('SA03', 'an acceptance criterion without a machine mapping is never written as passed', () => {
  const f = fixture();
  const task = state.loadTask(f.cfg, 'C-DEMO');
  task.acceptanceCriteria = [
    { id: 'AC1', text: 'The required-proof.txt artifact must exist and contain an approved manual verification.' },
  ];
  state.saveTask(f.cfg, task);

  const close = stage.cmdStageClose(f.cfg, { task: 'C-DEMO', profile: 'smoke' });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId });
  assert.notEqual(review.status, 'passed');
  assert.equal(review.exitCode, 5);

  const record = io.readJson(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`));
  assert.equal(record.coverage.length, 1);
  assert.notEqual(record.coverage[0].result, 'passed');
  assert.equal(fs.existsSync(path.join(f.dir, 'required-proof.txt')), false);

  const issues = review.issues.map((id) => io.readJson(path.join(f.dir, `docs/collaboration/issues/${id}.json`)));
  assert(issues.some((i) => /机器验证/.test(i.title)));

  // The same delivery can still be closed only by an explicit, attributable
  // reviewer attestation for the unmapped clause.
  const attested = stage.cmdStageReview(f.cfg, {
    actor: 'codex',
    delivery: close.deliveryId,
    force: true,
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

console.log(JSON.stringify({ assertionsTotal: results.length, assertionsFailed: failed, assertionsSkipped: 0 }));
process.exitCode = failed ? 1 : 0;
