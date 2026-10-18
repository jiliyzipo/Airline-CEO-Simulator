// Reverse-review exceptions stay scoped; every write is in an isolated fixture.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
const source = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
const cmd = await import(pathToFileURL(path.join(source, 'lib/commands.mjs')));
const state = await import(pathToFileURL(path.join(source, 'lib/state.mjs')));
const io = await import(pathToFileURL(path.join(source, 'lib/io.mjs')));
const schema = await import(pathToFileURL(path.join(source, 'lib/schemas.mjs')));
const book = await import(pathToFileURL(path.join(source, 'lib/taskbook.mjs')));
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-reverse-'));
let serial = 0;
const results = [];
function put(dir, rel, body) {
  const file = path.join(dir, rel); fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, typeof body === 'string' ? body : JSON.stringify(body, null, 2)); return file;
}
function fixture(developer = 'codex') {
  const dir = path.join(root, String(++serial));
  fs.cpSync(source, path.join(dir, 'tools/collab'), { recursive: true });
  const cfg = { ...JSON.parse(fs.readFileSync(path.join(source, 'config.example.json'))), _repoRoot: dir,
    projectId: 'reverse', taskBook: { tasks: {}, completionTasks: [] } };
  put(dir, 'tools/collab/config.json', cfg); put(dir, 'docs/collaboration/PROTOCOL.md', '# fixture');
  cmd.cmdInit(cfg);
  const task = state.loadTask(cfg, 'C-DEMO');
  task.acceptanceCriteria = [{ id: 'AC1', text: 'smoke executes' }, { id: 'AC2', text: 'reviewer checks inputs' }];
  state.saveTask(cfg, task);
  const devFile = put(dir, 'dev.json', { schemaVersion: 1, grantedBy: 'user', actor: 'codex', projectId: cfg.projectId,
    taskId: task.taskId, actions: ['deliver'], statement: 'fixture user-authorized development',
    allowedRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'] });
  const delivery = cmd.cmdDeliver(cfg, { actor: developer, task: task.taskId, summary: 'fixture', authorization: devFile });
  const d = io.readJson(path.join(dir, `docs/collaboration/deliveries/${delivery.deliveryId}.json`));
  const grant = { schemaVersion: 1, grantedBy: 'user', actor: 'cursor', developerActor: 'codex', projectId: cfg.projectId,
    taskId: task.taskId, deliveryId: d.deliveryId, statement: 'fixture reverse review',
    actions: ['review-start', 'review-run', 'review-evidence', 'review-finish', 'review-cancel'], profiles: ['smoke'],
    subjectFingerprints: Object.fromEntries(['implementation', 'verification', 'contract'].map(p => [p, d.fingerprints[p].fingerprint])) };
  const authorization = put(dir, 'review-grant.json', grant);
  return { dir, cfg, d, grant, authorization, args: { actor: 'cursor', authorization } };
}
const denied = fn => assert.throws(fn, e => e.exitCode === 7);
const fails = (fn, code) => assert.throws(fn, e => e.exitCode === code);
const read = (f, r) => io.readJson(path.join(f.dir, `docs/collaboration/reviews/${r.reviewId}.json`));
const start = (f, args = {}) => cmd.cmdReviewStart(f.cfg, { ...f.args, delivery: f.d.deliveryId, ...args });
const run = (f, r, args = {}) => cmd.cmdReviewRun(f.cfg, { ...f.args, review: r.reviewId, ...args });
const evidence = (f, r, args = {}) => cmd.cmdReviewEvidence(f.cfg, { ...f.args, review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed', ...args });
const finish = (f, r, ev, args = {}) => cmd.cmdReviewFinish(f.cfg, { ...f.args, review: r.reviewId, evidence: ev.evidencePath, ...args });
async function test(id, name, fn) {
  try { await fn(); results.push({ id, name, passed: true }); console.log(`PASS ${id} ${name}`); }
  catch (e) { results.push({ id, name, passed: false, error: e.stack }); console.log(`FAIL ${id} ${name}: ${e.message}`); }
}
await test('R01', 'ordinary Cursor still cannot start, finish, draft or cancel a review', () => {
  const f = fixture();
  for (const action of ['review-start', 'review-run', 'review-finish', 'review-cancel']) denied(() => schema.assertRoleAction('cursor', action));
  denied(() => start(f, { authorization: undefined }));
  denied(() => cmd.cmdReviewFinish(f.cfg, { actor: 'cursor', review: 'nope', evidence: 'missing' }));
  denied(() => cmd.cmdReviewEvidence(f.cfg, { actor: 'cursor', review: 'nope' }));
});
await test('R02', 'a reverse-review grant cannot approve a Cursor-authored delivery', () => {
  const f = fixture('cursor'); denied(() => start(f));
});
await test('R03', 'project, task, delivery, every fingerprint, profile and action must match', () => {
  const f = fixture();
  for (const key of ['projectId', 'taskId', 'deliveryId']) {
    put(f.dir, 'review-grant.json', { ...f.grant, [key]: key === 'deliveryId' ? 'DEL-other' : 'other' }); denied(() => start(f));
  }
  for (const p of ['implementation', 'verification', 'contract']) {
    put(f.dir, 'review-grant.json', { ...f.grant, subjectFingerprints: { ...f.grant.subjectFingerprints, [p]: '0'.repeat(64) } }); denied(() => start(f));
  }
  for (const patch of [{ profiles: ['collab'] }, { actions: ['review-run'] }, { actor: 'codex' }, { grantedBy: 'system' }, { actions: ['deliver'] }]) {
    put(f.dir, 'review-grant.json', { ...f.grant, ...patch }); denied(() => start(f));
  }
  assert.equal(fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length, 0);
});
await test('R04', 'authorized Cursor creates and finishes its own independent bound smoke REV', () => {
  const f = fixture(), r = start(f);
  assert.equal(run(f, r).exitCode, 0);
  const ev = evidence(f, r); assert.equal(finish(f, r, ev).status, 'passed');
  const record = read(f, r);
  assert.equal(record.actor, 'cursor'); assert.equal(record.independent, true); assert.equal(record.reviewException, true);
  assert.equal(record.reviewKind, 'independent'); assert.match(record.authorization.sha256, /^[0-9a-f]{64}$/);
  assert.equal(record.authorization.deliveryId, f.d.deliveryId);
  assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').ok, true);
  const event = io.listJsonRecords(path.join(f.dir, 'docs/collaboration/events')).find(e => e.reviewId === r.reviewId && e.action === 'review-start');
  assert.equal(event.actor, 'cursor'); assert.equal(event.data.authorization.sha256, record.authorization.sha256);
});
await test('R05', 'the grant bytes are pinned and cannot change between review commands', () => {
  const f = fixture(), r = start(f); fs.appendFileSync(f.authorization, '\n');
  denied(() => run(f, r)); assert.equal(read(f, r).runs.length, 0);
  denied(() => cmd.cmdReviewCancel(f.cfg, { ...f.args, review: r.reviewId }));
});
await test('R06', 'another actor cannot run, draft, finish or cancel a reviewer-owned record', () => {
  const f = fixture(), r = start(f), at = fs.readFileSync(path.join(f.dir, `docs/collaboration/reviews/${r.reviewId}.json`), 'utf8');
  denied(() => run(f, r, { actor: 'codex' }));
  denied(() => evidence(f, r, { actor: 'codex' }));
  denied(() => cmd.cmdReviewCancel(f.cfg, { actor: 'codex', review: r.reviewId }));
  assert.equal(run(f, r).exitCode, 0); const ev = evidence(f, r);
  denied(() => finish(f, r, ev, { actor: 'codex' }));
  assert.equal(read(f, r).actor, 'cursor'); assert(at.includes('queued'));
});
await test('R07', 'reverse review still rejects tampered bound stdout', () => {
  const f = fixture(), r = start(f); run(f, r); const ev = evidence(f, r);
  const record = read(f, r); const log = record.runs.at(-1).stdout.path;
  fs.appendFileSync(path.join(f.dir, log), 'tampered'); fails(() => finish(f, r, ev), 5);
});
await test('R08', 'reverse review requires complete AC coverage and cannot finish twice', () => {
  const f = fixture(), r = start(f); run(f, r);
  const partial = evidence(f, r, { coverage: 'AC1=passed' }); fails(() => finish(f, r, partial), 6);
  const full = evidence(f, r); finish(f, r, full);
  const file = path.join(f.dir, `docs/collaboration/reviews/${r.reviewId}.json`), before = fs.readFileSync(file);
  fails(() => finish(f, r, full), 3); assert.deepEqual(fs.readFileSync(file), before);
});
await test('R09', 'authorized cancellation is terminal; user retains administrative cancellation', () => {
  const f = fixture(), r = start(f);
  assert.equal(cmd.cmdReviewCancel(f.cfg, { ...f.args, review: r.reviewId }).status, 'cancelled');
  fails(() => run(f, r), 3); const r2 = start(f);
  assert.equal(cmd.cmdReviewCancel(f.cfg, { actor: 'user', review: r2.reviewId }).status, 'cancelled');
});
await test('R10', 'Codex self-check remains nonindependent and cannot complete the task', () => {
  const f = fixture(), r = cmd.cmdReviewStart(f.cfg, { actor: 'codex', delivery: f.d.deliveryId });
  cmd.cmdReviewRun(f.cfg, { actor: 'codex', review: r.reviewId });
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  assert.equal(read(f, r).independent, false); assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').ok, false);
});
await test('R11', 'a self-checked required profile cannot borrow a different profile independent pass', () => {
  const f = fixture(), r = start(f); run(f, r); finish(f, r, evidence(f, r));
  f.cfg.taskBook = { path: 'book.md', tasks: { 'C-DEMO': { sectionHeading: '## C-DEMO', requiresProfiles: ['smoke', 'compile'] } }, completionTasks: [] };
  put(f.dir, 'book.md', '- [ ] **C-DEMO: total**\n## C-DEMO\n- [x] C-DEMO.1 accepted');
  book.recordBaseline(f.cfg, 'C-DEMO');
  const record = read(f, r);
  const old = { ...record, reviewId: 'REV-older', profile: 'compile', independent: false,
    actor: 'codex', createdAt: '2026-01-01T00:00:00Z', reviewException: undefined, authorization: undefined };
  put(f.dir, 'docs/collaboration/reviews/REV-older.json', old);
  assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').checks.find(p => p.id === 'required-profiles').ok, false);
  old.independent = true; put(f.dir, 'docs/collaboration/reviews/REV-older.json', old);
  assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').checks.find(p => p.id === 'required-profiles').ok, true);
});
await test('R12', 'schema rejects Cursor records without the matching grant, hash or independent identity', () => {
  const f = fixture(), r = start(f), good = read(f, r); schema.validateReview(good);
  for (const patch of [{ authorization: null }, { reviewException: false }, { independent: false }, { deliveryId: 'DEL-wrong' },
    { authorization: { ...good.authorization, sha256: 'bad' } }]) {
    assert.throws(() => schema.validateReview({ ...good, ...patch }));
  }
});
await test('R13', 'review grant does not grant issue closure, taskbook writes or global recovery', () => {
  const f = fixture();
  denied(() => cmd.cmdIssueUpdate(f.cfg, { ...f.args, issue: 'ISSUE-fixture', status: 'verified' }));
  denied(() => cmd.cmdTaskBookCheck(f.cfg, { ...f.args, task: 'C-DEMO' }));
  denied(() => cmd.cmdReviewRecover(f.cfg, { ...f.args }));
});
await test('R14', 'PowerShell wrapper preserves real Cursor actor and scoped grant', () => {
  const f = fixture();
  const r = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(f.dir, 'tools/collab/Collab.ps1'),
    'review-start', f.d.deliveryId, 'smoke', 'cursor', f.authorization], { cwd: f.dir, encoding: 'utf8', windowsHide: true, timeout: 30000 });
  assert.equal(r.status, 0, r.stdout + r.stderr); assert.equal(read(f, JSON.parse(r.stdout)).actor, 'cursor');
});
const out = { at: new Date().toISOString(), subjectToolsPath: source, total: results.length,
  passed: results.filter(r => r.passed).length, failed: results.filter(r => !r.passed).length, results };
put(root, 'results.json', out);
console.log(`SUMMARY ${out.passed}/${out.total} reverse-review checks passed; RESULT ${path.join(root, 'results.json')}`);
process.exitCode = out.failed ? 1 : 0;
