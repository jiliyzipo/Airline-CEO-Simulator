import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
const source = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
const cmd = await import(pathToFileURL(path.join(source, 'lib/commands.mjs')));
const state = await import(pathToFileURL(path.join(source, 'lib/state.mjs')));
const book = await import(pathToFileURL(path.join(source, 'lib/taskbook.mjs')));
const io = await import(pathToFileURL(path.join(source, 'lib/io.mjs')));
const hooks = await import(pathToFileURL(path.join(source, 'lib/hooks.mjs')));
const stage = await import(pathToFileURL(path.join(source, 'lib/stage.mjs')));
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-daily-'));
let serial = 0, failed = 0;
const results = [];
const put = (dir, file, value) => { const abs = path.join(dir, file); fs.mkdirSync(path.dirname(abs), { recursive: true }); fs.writeFileSync(abs, typeof value === 'string' ? value : JSON.stringify(value, null, 2)); };
function fixture() {
  const dir = path.join(root, String(++serial));
  fs.cpSync(source, path.join(dir, 'tools/collab'), { recursive: true });
  const cfg = { ...JSON.parse(fs.readFileSync(path.join(source, 'config.example.json'))), _repoRoot: dir,
    projectId: 'daily', taskBook: { tasks: {}, completionTasks: [] }, hooks: { enabled: true, debounceMs: 2000, dedupeWindowMs: 60000 } };
  put(dir, 'tools/collab/config.json', cfg);
  put(dir, 'docs/collaboration/PROTOCOL.md', '# fixture protocol');
  cmd.cmdInit(cfg);
  const task = state.loadTask(cfg, 'C-DEMO');
  task.acceptanceCriteria = [{ id: 'AC1', text: 'real smoke runs and its inputs exist' }, { id: 'AC2', text: 'operator verifies the case' }];
  state.saveTask(cfg, task);
  return { dir, cfg, task };
}
const throwsCode = (fn, code) => assert.throws(fn, e => e.exitCode === code);
const waitFor = async fn => { const end = Date.now() + 15000; while (!fn()) { if (Date.now() > end) throw Error('bounded worker timeout'); await new Promise(r => setTimeout(r, 30)); } };
function run(f, args, input) { const r = spawnSync(process.execPath, [path.join(f.dir, 'tools/collab/cli.mjs'), ...args, '--json'], { cwd: f.dir, encoding: 'utf8', input, windowsHide: true, timeout: 30000 }); return { ...r, json: (() => { try { return JSON.parse(r.stdout); } catch { return null; } })() }; }
function delivered(f, args = {}) { return cmd.cmdDeliver(f.cfg, { task: 'C-DEMO', actor: 'cursor', summary: 'daily fixture', ...args }); }
function reviewing(f, delivery) { const r = cmd.cmdReviewStart(f.cfg, { actor: 'codex', delivery: delivery.deliveryId }); assert.equal(cmd.cmdReviewRun(f.cfg, { actor: 'codex', review: r.reviewId }).exitCode, 0); return r; }
async function test(id, name, fn) { try { await fn(); results.push({ id, name, passed: true }); console.log(`PASS ${id} ${name}`); } catch (e) { failed++; results.push({ id, name, passed: false, error: e.stack }); console.log(`FAIL ${id} ${name}: ${e.message}`); } }
await test('D01', 'doctor is read-only and CLI counters do not verify native hooks', () => {
  const f = fixture();
  const list = dir => fs.readdirSync(dir, { recursive: true }).sort();
  const before = list(f.dir);
  hooks.runHook(f.cfg, { source: 'cursor', event: 'sessionStart', idempotencyKey: 'same', apply: () => ({ revision: 1 }) });
  hooks.runHook(f.cfg, { source: 'cursor', event: 'sessionStart', idempotencyKey: 'same', apply: () => ({ revision: 1 }) });
  const afterHooks = list(f.dir);
  const d = cmd.cmdDoctor(f.cfg);
  assert.deepEqual(list(f.dir), afterHooks); assert.equal(d.hookCapability.verified, false);
  assert.equal(d.hookCapability.clients.cursor.mode, 'manual'); assert(before.length > 0);
});
await test('D02', 'malformed or scalar stdin is rejected instead of syncing', () => {
  const f = fixture();
  for (const text of ['not JSON', '[]', 'null', '1']) throwsCode(() => cmd.cmdHook(f.cfg, { source: 'codex', event: 'Stop' }, { stdinText: text }), 2);
});
await test('D03', 'idempotency keys are separate for each source and event', () => {
  const f = fixture(); let count = 0;
  for (const source of ['cursor', 'codex']) for (const event of ['sessionStart', 'stop']) hooks.runHook(f.cfg, { source, event, idempotencyKey: '1', apply: () => { count++; return {}; } });
  assert.equal(count, 4);
});
await test('D04', 'rapid distinct edits are not discarded by debounce', () => {
  const f = fixture(); let count = 0;
  for (const file of ['a.cs', 'b.cs']) hooks.runHook(f.cfg, { source: 'cursor', event: 'afterFileEdit', payload: { file }, apply: () => { count++; return {}; } });
  assert.equal(count, 2);
});
await test('D05', 'Codex delivery needs scoped authorization and self-check cannot pass independent gate', () => {
  const f = fixture();
  throwsCode(() => delivered(f, { actor: 'codex' }), 7);
  const grant = { schemaVersion: 1, grantedBy: 'user', actor: 'codex', projectId: 'daily', taskId: 'C-DEMO', actions: ['deliver'], statement: 'fixture authorization', allowedRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'] };
  put(f.dir, 'grant.json', grant);
  const d = delivered(f, { actor: 'codex', authorization: path.join(f.dir, 'grant.json') });
  const r = reviewing(f, d);
  assert.equal(io.readJson(path.join(f.dir, `docs/collaboration/reviews/${r.reviewId}.json`)).independent, false);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').ok, false);
  grant.allowedRoots = ['tools/other']; put(f.dir, 'grant.json', grant);
  throwsCode(() => delivered(f, { actor: 'codex', authorization: path.join(f.dir, 'grant.json') }), 7);
});
await test('D06', 'evidence draft defaults uncovered ACs to not_run and explicit coverage can finish', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f, d);
  const partial = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed' });
  assert.deepEqual(partial.notPassed, ['AC2']);
  throwsCode(() => cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: partial.evidencePath }), 6);
  const full = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed', note: 'checked fixture inputs and run' });
  assert.equal(cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: full.evidencePath }).status, 'passed');
  assert.equal(book.computeTaskGate(f.cfg, 'C-DEMO').ok, true);
});
await test('D07', 'append-only publication cannot replace an existing audit record', () => {
  const f = fixture(), dir = path.join(f.dir, 'audit');
  io.appendJsonRecord(dir, 'ONE', { a: 1 });
  assert.throws(() => io.appendJsonRecord(dir, 'ONE', { a: 2 }));
  assert.equal(io.readJson(path.join(dir, 'ONE.json')).a, 1);
});
await test('D08', 'foreign-machine or corrupt locks are not reclaimed', () => {
  const f = fixture(), file = path.join(f.dir, 'lock');
  put(f.dir, 'lock', { machine: 'another-computer', pid: 2147483000, owner: 'external' });
  assert.equal(new io.FileLock(file, 'me').acquire().ok, false);
  put(f.dir, 'lock', 'broken'); fs.utimesSync(file, new Date(0), new Date(0));
  assert.equal(new io.FileLock(file, 'me').acquire().ok, false);
  assert.equal(fs.readFileSync(file, 'utf8'), 'broken');
});
await test('D09', 'claim detects overlap and permits explicitly disjoint task roots', () => {
  const f = fixture();
  for (const [id, roots] of [['A', ['tools/collab']], ['B', ['tools/collab']], ['C', ['tools/other']]]) {
    put(f.dir, `def-${id}.json`, { taskId: id, title: id, scope: roots, snapshotRoots: roots, acceptanceCriteria: [{ id: 'AC1', text: 'check' }] });
    cmd.cmdTaskAdd(f.cfg, { actor: 'cursor', definition: path.join(f.dir, `def-${id}.json`) });
  }
  const claim = id => { cmd.cmdAck(f.cfg, { actor: 'cursor', revision: state.loadState(f.cfg).revision }); return cmd.cmdClaim(f.cfg, { actor: 'cursor', task: id }); };
  claim('A'); throwsCode(() => claim('B'), 3); claim('C');
});
await test('D10', 'duplicate task headings and sub-item IDs cannot pass mapping', () => {
  const f = fixture(); f.cfg.taskBook = { path: 'book.md', tasks: { A: { sectionHeading: '## A' } } };
  put(f.dir, 'book.md', '- [ ] **A: total**\n## A\n- [x] A.1 one\n## A\n- [x] A.2 two');
  assert.equal(book.readClause(f.cfg, 'A').ok, false);
  put(f.dir, 'book.md', '- [ ] **A: total**\n## A\n- [x] A.1 one\n- [x] A.1 duplicate');
  assert.equal(book.readClause(f.cfg, 'A').ok, false);
});
await test('D11', 'only tool-owned managed checkboxes are revoked, preserving manual clause conflicts', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  f.cfg.taskBook = { path: 'book.md', tasks: { 'C-DEMO': { sectionHeading: '## C-DEMO' } }, completionTasks: ['C-DEMO'] };
  put(f.dir, 'book.md', '- [ ] **C-DEMO: total**\n## C-DEMO\n- [x] C-DEMO.1 done');
  book.recordBaseline(f.cfg, 'C-DEMO'); book.writeTotalCheckbox(f.cfg, 'C-DEMO');
  const t = state.loadTask(f.cfg, 'C-DEMO'); t.freshness = 'stale'; state.saveTask(f.cfg, t);
  assert.equal(book.reconcileManagedCheckboxes(f.cfg)[0].revoked, true);
  assert.equal(book.readClause(f.cfg, 'C-DEMO').total.checked, false);
  t.freshness = 'current'; state.saveTask(f.cfg, t); book.writeTotalCheckbox(f.cfg, 'C-DEMO');
  put(f.dir, 'book.md', fs.readFileSync(path.join(f.dir, 'book.md'), 'utf8').replace('C-DEMO.1 done', 'C-DEMO.1 changed requirement'));
  t.freshness = 'stale'; state.saveTask(f.cfg, t);
  assert.equal(book.reconcileManagedCheckboxes(f.cfg)[0].conflict, true);
  assert.equal(book.readClause(f.cfg, 'C-DEMO').total.checked, true);
});
await test('D12', 'foreground watch refreshes changed input within five seconds without ack', async () => {
  const f = fixture(); delivered(f); cmd.cmdSync(f.cfg);
  const watch = spawn(process.execPath, [path.join(f.dir, 'tools/collab/cli.mjs'), 'watch', '--interval', '0.5'], { cwd: f.dir, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  let stdout = '', stderr = '';
  watch.stdout.on('data', d => stdout += d); watch.stderr.on('data', d => stderr += d);
  try {
    await waitFor(() => stdout.includes('"ready":true'));
    const at = Date.now(); put(f.dir, 'tools/collab/watch-input.txt', 'changed');
    await waitFor(() => state.loadTask(f.cfg, 'C-DEMO').freshness === 'stale');
    assert(Date.now() - at < 5000, stderr);
    assert.equal(state.loadAck(f.cfg).codex, null);
  } finally { watch.kill(); await new Promise(r => watch.on('close', r)); }
});
await test('D13', 'PowerShell wrapper preserves explicit profiles and evidence drafting works', () => {
  const f = fixture(); put(f.dir, 'summary.txt', 'wrapper fixture');
  const script = path.join(f.dir, 'tools/collab/Collab.ps1');
  const r = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script, 'deliver', 'C-DEMO', 'summary.txt', 'fixture', 'smoke,collab'], { cwd: f.dir, encoding: 'utf8', windowsHide: true, timeout: 30000 });
  assert.equal(r.status, 0, r.stderr); const d = JSON.parse(r.stdout);
  assert.deepEqual(io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${d.deliveryId}.json`)).recommendedProfiles, ['smoke', 'collab']);
});
await test('D14', 'per-command help exits zero without mutating records', () => {
  const f = fixture(); const rev = state.loadState(f.cfg).revision;
  for (const name of ['deliver', 'review-run', 'review-evidence', 'watch']) { const r = run(f, [name, '--help']); assert.equal(r.status, 0); assert(r.json.help); }
  assert.equal(state.loadState(f.cfg).revision, rev);
  assert.equal(run(f, ['not-a-command']).status, 2);
});
await test('D15', 'dead review worker is interrupted without deleting user Unity locks', () => {
  const f = fixture(), d = delivered(f), r = cmd.cmdReviewStart(f.cfg, { delivery: d.deliveryId, actor: 'codex' });
  const file = path.join(f.dir, `docs/collaboration/reviews/${r.reviewId}.json`), review = io.readJson(file);
  Object.assign(review, { status: 'running', workerPid: 2147483000 }); io.writeJsonAtomic(file, review);
  put(f.dir, 'Unity Project/Temp/UnityLockfile', 'user lock');
  cmd.cmdReviewRecover(f.cfg, { actor: 'codex' });
  assert.equal(io.readJson(file).status, 'interrupted');
  assert.equal(fs.readFileSync(path.join(f.dir, 'Unity Project/Temp/UnityLockfile'), 'utf8'), 'user lock');
});
await test('D16', 'client bridge consumes stdin but never creates a delivery or auto-ack', () => {
  const f = fixture();
  const bridge = path.join(f.dir, 'tools/collab/adapters/client-hook.mjs');
  const invoke = input => spawnSync(process.execPath, [bridge, 'codex', 'Stop'], { cwd: f.dir, encoding: 'utf8', input, windowsHide: true, timeout: 30000 });
  const r = invoke('\uFEFF{"session_id":"fixture","turn_id":"one"}');
  assert.equal(r.status, 0, r.stderr); assert.deepEqual(JSON.parse(r.stdout), {});
  assert.equal(hooks.hookStatus(f.cfg).counters.applied, 1);
  assert.equal(state.loadAck(f.cfg).codex, null); assert.equal(state.listDeliveries(f.cfg).length, 0);
  assert.equal(invoke('invalid').status, 2);
});
await test('D17', 'invalid config roots, duplicate ACs and traversal snapshot roots are rejected', () => {
  const f = fixture();
  put(f.dir, 'tools/collab/config.json', { ...f.cfg, snapshotRoot: '../elsewhere' });
  const r = run(f, ['status']); assert.equal(r.status, 2); assert.equal(r.json.ok, false);
  for (const patch of [{ snapshotRoots: ['../private'] }, { acceptanceCriteria: [{id:'X',text:'one'},{id:'X',text:'two'}] }]) {
    put(f.dir, 'invalid-task.json', {taskId:'BAD', title:'bad', scope:[], acceptanceCriteria:[{id:'AC1',text:'one'}], ...patch});
    throwsCode(() => cmd.cmdTaskAdd(f.cfg, {actor:'cursor', definition:path.join(f.dir,'invalid-task.json')}), 2);
  }
});
await test('D18', 'unknown delivery profiles fail before exporting a snapshot', () => {
  const f = fixture(); throwsCode(() => delivered(f, {profiles:'invented'}), 2);
  assert.equal(state.listDeliveries(f.cfg).length, 0);
});
await test('D19', 'gate rechecks current bytes even when the operator forgot sync', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f,d);
  const ev = cmd.cmdReviewEvidence(f.cfg,{review:r.reviewId,conclusion:'passed',coverage:'AC1=passed,AC2=passed'});
  cmd.cmdReviewFinish(f.cfg,{actor:'codex',review:r.reviewId,evidence:ev.evidencePath});
  assert.equal(cmd.cmdGate(f.cfg,{task:'C-DEMO'}).ok,true);
  put(f.dir,'tools/collab/new-untracked-input.txt','new input');
  assert.equal(cmd.cmdGate(f.cfg,{task:'C-DEMO'}).ok,false);
  assert.equal(state.loadTask(f.cfg,'C-DEMO').freshness,'stale');
});
await test('D20', 'review freezes its profile and changed local profile cannot inherit a current pass', () => {
  const f=fixture(),d=delivered(f),r=cmd.cmdReviewStart(f.cfg,{actor:'codex',delivery:d.deliveryId,profile:'smoke'});
  f.cfg.profiles.smoke={kind:'node',command:['node','does-not-exist.mjs'],resultKind:'json'};
  assert.equal(cmd.cmdReviewRun(f.cfg,{actor:'codex',review:r.reviewId}).exitCode,0);
  const ev=cmd.cmdReviewEvidence(f.cfg,{review:r.reviewId,conclusion:'passed',coverage:'AC1=passed,AC2=passed'});
  cmd.cmdReviewFinish(f.cfg,{actor:'codex',review:r.reviewId,evidence:ev.evidencePath});
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg,'C-DEMO').freshness,'stale');
  assert.equal(book.computeTaskGate(f.cfg,'C-DEMO').ok,false);
});
await test('D21', 'machine-local config is excluded from shareable delivery inputs', () => {
  const f=fixture(),d=delivered(f);
  const record=state.listDeliveries(f.cfg).find(row=>row.deliveryId===d.deliveryId);
  assert.equal(record.snapshotManifest.entries.some(e=>e.path==='tools/collab/config.json'),false);
  assert.equal(fs.existsSync(path.join(f.dir,record.snapshotPath,'tools/collab/config.json')),false);
  assert(record.snapshotManifest.entries.some(e=>e.path==='tools/collab/config.example.json'));
});
await test('D22', 'evidence bound to one delivery cannot be reused when only the verification fingerprint differs', () => {
  const f = fixture(), d1 = delivered(f), r1 = reviewing(f, d1);
  const ev1 = cmd.cmdReviewEvidence(f.cfg, { review: r1.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  assert.deepEqual(Object.keys(ev1.evidence.subjectFingerprints).sort(), ['contract', 'implementation', 'verification']);
  assert.equal(cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r1.reviewId, evidence: ev1.evidencePath }).status, 'passed');
  // tools/collab/tests/ is excluded from the implementation identity by policy:
  // this is exactly the test-only supersede that must not inherit the old evidence.
  put(f.dir, 'tools/collab/tests/verification-only-marker.txt', 'verification-only change');
  const d2 = delivered(f, { summary: 'verification-only supersede' });
  const rec = state.listDeliveries(f.cfg);
  const rec1 = rec.find((row) => row.deliveryId === d1.deliveryId), rec2 = rec.find((row) => row.deliveryId === d2.deliveryId);
  assert.equal(rec2.fingerprints.implementation.fingerprint, rec1.fingerprints.implementation.fingerprint, 'fixture must not move the implementation identity');
  assert.notEqual(rec2.fingerprints.verification.fingerprint, rec1.fingerprints.verification.fingerprint);
  const r2 = reviewing(f, d2);
  throwsCode(() => cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r2.reviewId, evidence: ev1.evidencePath }), 6);
  const ev2 = cmd.cmdReviewEvidence(f.cfg, { review: r2.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  assert.equal(cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r2.reviewId, evidence: ev2.evidencePath }).status, 'passed');
});
await test('D23', 'evidence without the full fingerprint triplet is rejected instead of silently matched', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  const draft = io.readJson(ev.evidencePath);
  const fps = draft.subjectFingerprints;
  // `undefined` disappears from the JSON body: that is the "field missing" case.
  const write = patch => { const file = path.join(f.dir, `ev-${serial}.json`); put(f.dir, path.basename(file), { ...draft, ...patch }); return file; };
  throwsCode(() => cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: write({ subjectFingerprints: undefined }) }), 6);
  // The legacy implementation-only binding is not enough any more.
  throwsCode(() => cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: write({ subjectFingerprints: { implementation: fps.implementation } }) }), 6);
  throwsCode(() => cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: write({ subjectFingerprints: { ...fps, verification: '0'.repeat(64) } }) }), 6);
  assert.equal(cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: write({}) }).status, 'passed');
});
await test('D24', 'settled tasks leave the pending lists while a stale pass stays with its reason', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  const dir = path.join(f.dir, 'docs/collaboration');
  const read = (file) => fs.readFileSync(path.join(dir, file), 'utf8');
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'current');
  assert.equal(/^- C-DEMO:/m.test(read('NEXT_CURSOR.md')), false, 'a settled task must not be listed as pending for the developer');
  assert.equal(/^- C-DEMO:/m.test(read('NEXT_CODEX.md')), false, 'a settled task must not be listed as pending detection');
  // Drift under the pinned roots keeps the historical verdict but re-opens detection.
  put(f.dir, 'tools/collab/after-pass.txt', 'drift');
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'stale');
  assert.match(read('NEXT_CODEX.md'), /- C-DEMO: 开发=\S+ 检测=passed freshness=stale（历史通过仅对应旧快照，需按当前代码复验）/);
  assert.match(read('NEXT_CURSOR.md'), /- C-DEMO: 历史检测已通过，但工作区已变（freshness=stale）/);
  // A task that actually failed keeps the actionable wording instead of a
  // "needs re-review" note the developer cannot act on.
  const back = state.loadTask(f.cfg, 'C-DEMO');
  back.devStatus = 'rework'; back.reviewStatus = 'failed'; state.saveTask(f.cfg, back);
  cmd.cmdSync(f.cfg);
  assert.match(read('NEXT_CURSOR.md'), /- C-DEMO: 按缺陷修复后重新 deliver/);
});
await test('D25', 'an undeclared option is a usage error instead of a silently ignored no-op', () => {
  const f = fixture(), d = delivered(f);
  const typo = run(f, ['review-finish', '--review', 'REV-anything', '--coverge', 'AC1=passed']);
  assert.equal(typo.status, 2, typo.stdout);
  assert.match(typo.json.error, /--coverge/);
  assert(typo.json.allowed.includes('coverage'), 'the error must name the options the command does accept');
  // The exact mistake that performed a review instead of previewing it.
  const bogus = run(f, ['stage-board', '--dry-run']);
  assert.equal(bogus.status, 2); assert.match(bogus.json.error, /--dry-run/);
  // Declared options keep working untouched.
  assert.equal(run(f, ['status']).status, 0);
  assert.equal(run(f, ['stage-review', '--delivery', d.deliveryId, '--dry-run']).status, 0);
});
await test('D26', 'stage-review --dry-run previews without creating a review, an issue or a revision', () => {
  const f = fixture(), d = delivered(f);
  const dir = name => fs.readdirSync(path.join(f.dir, `docs/collaboration/${name}`)).length;
  const before = { reviews: dir('reviews'), issues: dir('issues'), revision: state.loadState(f.cfg).revision };
  const plan = stage.cmdStageReview(f.cfg, { delivery: d.deliveryId, actor: 'codex', 'dry-run': true });
  assert.equal(plan.dryRun, true);
  assert.deepEqual(plan.needsAttestation, ['AC1', 'AC2'], 'the preview must name every clause the reviewer still has to attest');
  assert.equal(dir('reviews'), before.reviews, 'a preview must not write a review');
  assert.equal(dir('issues'), before.issues, 'a preview must not write an issue');
  assert.equal(state.loadState(f.cfg).revision, before.revision, 'a preview must not bump the revision');
  // And the real run still behaves exactly as before.
  const real = run(f, ['stage-review', '--delivery', d.deliveryId, '--actor', 'codex', '--coverage', 'AC1=passed,AC2=passed']);
  assert.equal(real.status, 0, real.stdout);
  assert.equal(dir('reviews'), before.reviews + 1);
});
await test('D27', 'a passed verdict clears the rework flag, and a failure puts it back', () => {
  const f = fixture(), d = delivered(f);
  // Simulate the state a failed review leaves behind.
  const before = state.loadTask(f.cfg, 'C-DEMO'); before.devStatus = 'rework'; state.saveTask(f.cfg, before);
  const r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  let task = state.loadTask(f.cfg, 'C-DEMO');
  assert.equal(task.reviewStatus, 'passed'); assert.equal(task.freshness, 'current');
  assert.equal(task.devStatus, 'ready_for_review', 'passed must not stay in rework');
  // Drift keeps the historical verdict and the finished workflow: staleness is
  // expressed by freshness, not by resurrecting a rework flag.
  put(f.dir, 'tools/collab/drift-after-pass.txt', 'drift');
  cmd.cmdSync(f.cfg);
  task = state.loadTask(f.cfg, 'C-DEMO');
  assert.equal(task.freshness, 'stale');
  assert.equal(task.devStatus, 'ready_for_review');
  assert.equal(task.reviewStatus, 'passed');
});
await test('D28', 'a delivery can be superseded after the fact without rewriting its review', () => {
  const f = fixture();
  for (const id of ['C-X', 'C-Y']) {
    put(f.dir, `def-${id}.json`, { taskId: id, title: id, scope: ['tools/collab'], snapshotRoots: ['tools/collab'], snapshotFiles: ['docs/collaboration/PROTOCOL.md'], acceptanceCriteria: [{ id: 'AC1', text: 'first' }, { id: 'AC2', text: 'second' }] });
    cmd.cmdTaskAdd(f.cfg, { actor: 'cursor', definition: path.join(f.dir, `def-${id}.json`) });
  }
  const deliver = id => cmd.cmdDeliver(f.cfg, { task: id, actor: 'cursor', summary: `fixture ${id}` });
  const dX = deliver('C-X');
  const rX = cmd.cmdReviewStart(f.cfg, { actor: 'codex', delivery: dX.deliveryId });
  assert.equal(cmd.cmdReviewRun(f.cfg, { actor: 'codex', review: rX.reviewId }).exitCode, 0);
  const evX = cmd.cmdReviewEvidence(f.cfg, { review: rX.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: rX.reviewId, evidence: evX.evidencePath });
  const reviewPath = path.join(f.dir, `docs/collaboration/reviews/${rX.reviewId}.json`);
  const reviewBytes = fs.readFileSync(reviewPath, 'utf8');
  const dY = deliver('C-Y');

  const first = stage.cmdStageSupersede(f.cfg, { delivery: dX.deliveryId, by: dY.deliveryId, actor: 'cursor' });
  assert.equal(first.deliveryId, dX.deliveryId); assert.equal(first.supersededBy, dY.deliveryId);
  const ledger = state.loadState(f.cfg).deliveryStatus[dX.deliveryId];
  assert.equal(ledger.status, 'superseded'); assert.equal(ledger.supersededBy, dY.deliveryId); assert.equal(ledger.taskId, 'C-X');
  assert.equal(state.loadTask(f.cfg, 'C-X').supersededBy.deliveryId, dY.deliveryId);
  assert.equal(fs.readFileSync(reviewPath, 'utf8'), reviewBytes, 'the historical review must not be rewritten');
  cmd.cmdSync(f.cfg);
  const read = file => fs.readFileSync(path.join(f.dir, `docs/collaboration/${file}`), 'utf8');
  assert.equal(/^- C-X:/m.test(read('NEXT_CURSOR.md')), false, 'a superseded task is not pending developer work');
  assert.equal(/^- C-X:/m.test(read('NEXT_CODEX.md')), false, 'a superseded task is not pending detection');
  assert.match(read('CURRENT.md'), /已被 DEL-\S+（C-Y）取代/);

  const revision = state.loadState(f.cfg).revision;
  const again = stage.cmdStageSupersede(f.cfg, { delivery: dX.deliveryId, by: dY.deliveryId, actor: 'cursor' });
  assert.equal(again.unchanged, true); assert.equal(state.loadState(f.cfg).revision, revision, 'repeating the declaration must not bump the revision');
  throwsCode(() => stage.cmdStageSupersede(f.cfg, { delivery: dX.deliveryId, by: dX.deliveryId }), 2);
  throwsCode(() => stage.cmdStageSupersede(f.cfg, { delivery: dX.deliveryId, by: 'DEL-nope' }), 2);
  throwsCode(() => stage.cmdStageSupersede(f.cfg, { delivery: dY.deliveryId, by: dX.deliveryId }), 3);
});
await test('D29', 'the handoff ships the review command that actually succeeds', () => {
  const f = fixture();
  const tmpl = stage.acceptanceCriteriaFor('smoke', 'C-DEMO');
  const t = state.loadTask(f.cfg, 'C-DEMO');
  t.acceptanceCriteria = [tmpl[0], tmpl[1], { id: 'AC3', text: `${tmpl[2].text}，并核对机器计数` }];
  state.saveTask(f.cfg, t);
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke', task: 'C-DEMO' });
  const handoff = fs.readFileSync(path.join(f.dir, close.handoffFile), 'utf8');
  // Exactly the clauses the warning lists must appear in the recommended command.
  assert.match(handoff, /--coverage "AC3=passed"/, handoff);
  assert.equal(/--coverage "AC1/.test(handoff), false, 'machine-mapped clauses must not be written into the attestation');
  assert.match(close.nextCommand, /--coverage "AC3=passed"/);
  // Run the command exactly as printed, minus the program prefix.
  const printed = handoff.split('\n').find((l) => l.includes('stage-review --delivery'));
  const argv = printed.trim().replace(/^node tools\/collab\/cli\.mjs /, '').match(/"[^"]*"|\S+/g).map((s) => s.replace(/^"|"$/g, ''));
  const review = run(f, argv);
  assert.equal(review.status, 0, review.stdout);
  assert.equal(review.json.status, 'passed');
});
await test('D30', 'a review that cannot pass is refused before the suite runs', () => {
  const f = fixture();
  const close = stage.cmdStageClose(f.cfg, { profile: 'smoke', 'no-preflight': true, task: 'C-DEMO' });
  const dir = name => fs.readdirSync(path.join(f.dir, `docs/collaboration/${name}`)).length;
  const before = { reviews: dir('reviews'), issues: dir('issues'), revision: state.loadState(f.cfg).revision };
  // The fixture criteria are hand-written, so no run can prove them.
  throwsCode(() => stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId }), 2);
  assert.equal(dir('reviews'), before.reviews, 'a refused review must not create a review record');
  assert.equal(dir('issues'), before.issues, 'a refused review must not invent a defect');
  assert.equal(state.loadState(f.cfg).revision, before.revision);
  // The refusal names what it needs, and that is enough to proceed.
  try { stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId }); } catch (e) {
    assert.deepEqual(e.extra.needsAttestation, ['AC1', 'AC2']);
    assert.match(e.extra.suggestedCoverage, /AC1=passed,AC2=passed/);
    assert.match(e.message, /Nothing was run/);
  }
  const ok = stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId, coverage: 'AC1=passed,AC2=passed' });
  assert.equal(ok.exitCode, 0); assert.equal(ok.status, 'passed');
});
await test('D31', 'a retired defect is not listed as open work', () => {
  const f = fixture();
  const issue = {
    schemaVersion: 1, issueId: 'ISSUE-20261027010101-abcdef', taskId: 'C-DEMO', deliveryId: 'DEL-20261027010101-abcdef',
    title: 'retired fixture defect', expected: 'x', actual: 'y', severity: 'major', status: 'open',
    suggestedOwner: 'cursor', createdAt: new Date().toISOString(),
  };
  io.appendJsonRecord(path.join(f.dir, 'docs/collaboration/issues'), issue.issueId, issue);
  cmd.cmdSync(f.cfg);
  const read = (file) => fs.readFileSync(path.join(f.dir, `docs/collaboration/${file}`), 'utf8');
  assert.equal(read('NEXT_CODEX.md').includes(issue.issueId), true);
  cmd.cmdIssueUpdate(f.cfg, { actor: 'codex', issue: issue.issueId, status: 'wontfix', note: 'fixture retirement' });
  cmd.cmdSync(f.cfg);
  for (const file of ['CURRENT.md', 'NEXT_CODEX.md', 'NEXT_CURSOR.md']) {
    assert.equal(read(file).includes(issue.issueId), false, `${file} must not list a wontfix defect`);
  }
  assert.equal(fs.existsSync(path.join(f.dir, `docs/collaboration/issues/${issue.issueId}.json`)), true, 'the report itself is immutable history');
});
await test('D32', 'rewriting a template clause warns that it lost machine verification', () => {
  const f = fixture();
  const tmpl = stage.acceptanceCriteriaFor('collab', 'S16-FIXTURE');
  const def = { taskId: 'S16-FIXTURE', title: 'fixture', scope: ['tools/collab'], snapshotRoots: ['tools/collab'],
    acceptanceCriteria: [tmpl[0], { id: 'AC2', text: '我自己写的第二条' }] };
  put(f.dir, 'def-drift.json', def);
  const added = cmd.cmdTaskAdd(f.cfg, { actor: 'cursor', definition: path.join(f.dir, 'def-drift.json') });
  assert.equal(added.warnings.length, 1);
  assert.match(added.warnings[0], /AC2/); assert.match(added.warnings[0], /降级/); assert.match(added.warnings[0], /人工补证/);
  // Verbatim clauses raise nothing.
  const clean = { ...def, taskId: 'S16-CLEAN', acceptanceCriteria: stage.acceptanceCriteriaFor('collab', 'S16-CLEAN') };
  put(f.dir, 'def-clean.json', clean);
  const silent = cmd.cmdTaskAdd(f.cfg, { actor: 'cursor', definition: path.join(f.dir, 'def-clean.json') });
  assert.equal(silent.warnings, undefined);
});
// S17 fixture: fabricate a settled historical delivery whose recorded change set
// is a handful of files, so rescope can be exercised without a Unity tree.
function settledHistorical(f, { driftOne = false } = {}) {
  const files = ['tools/collab/lib/paths.mjs', 'tools/collab/lib/exit.mjs'];
  const d = delivered(f);
  const rec = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${d.deliveryId}.json`));
  // Only the recorded change set is synthetic: both paths already exist in the
  // snapshot manifest, so the hashes compared later are the real captured bytes.
  rec.files = {
    changedNote: 'fixture',
    workspaceChanges: files.map((p) => ({ status: ' M', path: p })),
  };
  fs.writeFileSync(
    path.join(f.dir, `docs/collaboration/deliveries/${d.deliveryId}.json`),
    JSON.stringify(rec, null, 2),
  );
  const r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  if (driftOne) put(f.dir, files[0], '// edited after the verdict\n');
  const read = (file) => fs.readFileSync(path.join(f.dir, `docs/collaboration/${file}`), 'utf8');
  return { delivery: d, review: r, files, read };
}
await test('D33', 'rescope narrows a finished task to its own change set and archives it', () => {
  const f = fixture();
  const { delivery, review, read } = settledHistorical(f);
  // The project-wide baseline is already gone (the fixture gains a file), which is
  // exactly the situation that made every old stage permanently stale.
  put(f.dir, 'tools/collab/unrelated-new-file.txt', 'x');
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'stale');
  const reviewBytes = fs.readFileSync(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`), 'utf8');

  const out = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO' });
  assert.equal(out.freshness, 'archived');
  assert.equal(out.drifted.length, 0);
  const task = state.loadTask(f.cfg, 'C-DEMO');
  assert.equal(task.scopePolicy, 'changed-files');
  assert.equal(task.rescope.basisCount, 2);
  assert.equal(fs.readFileSync(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`), 'utf8'), reviewBytes);
  assert.equal(/^- C-DEMO:/m.test(read('NEXT_CURSOR.md')), false, 'an archived task is not pending developer work');
  assert.equal(/^- C-DEMO:/m.test(read('NEXT_CODEX.md')), false, 'an archived task is not pending detection');
  assert.match(read('CURRENT.md'), /基线=本任务改动文件（全部未变，已归档）/);
  // Unrelated churn anywhere else must not resurrect it.
  put(f.dir, 'tools/collab/another-new-file.txt', 'y');
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'archived');
  assert.equal(delivery.deliveryId, state.loadTask(f.cfg, 'C-DEMO').rescope.deliveryId);
});
await test('D34', 'rescope keeps a task pending when its own changed files were edited again', () => {
  const f = fixture();
  const { files, read } = settledHistorical(f, { driftOne: true });
  const out = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO' });
  assert.equal(out.freshness, 'stale');
  assert.deepEqual(out.drifted, [files[0]]);
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'stale', 'drift must survive a resync');
  const line = read('NEXT_CURSOR.md').split('\n').find((l) => l.startsWith('- C-DEMO:'));
  assert.match(line, /无人复验/);
  assert.match(read('CURRENT.md'), /基线=本任务改动文件（有变动/);
});
await test('D35', 'rescope refuses a delivery that recorded no change set', () => {
  const f = fixture(), d = delivered(f), r = reviewing(f, d);
  const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
  cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  throwsCode(() => cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO' }), 2);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').scopePolicy, undefined, 'a refusal must not write a half-applied policy');
});
await test('D36', 'drift taken over by a later passed delivery archives instead of demanding rework', () => {
  const f = fixture();
  const fileA = 'tools/collab/lib/paths.mjs';
  const addLater = (from, to, { older = false } = {}) => {
    const rec = io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${from.deliveryId}.json`));
    rec.files = { changedNote: 'fixture', workspaceChanges: [{ status: ' M', path: to }] };
    // Delivery ids/timestamps are second-granular; the takeover rule needs a
    // strictly later delivery, so the fixture pins the first one in the past.
    if (older) rec.createdAt = new Date(Date.parse(rec.createdAt) - 60000).toISOString();
    fs.writeFileSync(path.join(f.dir, `docs/collaboration/deliveries/${from.deliveryId}.json`), JSON.stringify(rec, null, 2));
  };
  const appendToA = (note) => {
    const abs = path.join(f.dir, fileA);
    // Keep the file valid JS: a review runs the project's own suite against it.
    fs.writeFileSync(abs, fs.readFileSync(abs, 'utf8') + `\n// ${note}\n`);
  };
  const pass = (d) => {
    const r = reviewing(f, d);
    const ev = cmd.cmdReviewEvidence(f.cfg, { review: r.reviewId, conclusion: 'passed', coverage: 'AC1=passed,AC2=passed' });
    cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: ev.evidencePath });
  };
  const d1 = delivered(f);
  addLater(d1, fileA, { older: true });
  pass(d1);
  // A later stage edits the same file and gets its own passed verdict.
  appendToA('later stage edit');
  put(f.dir, 'def-later.json', {
    taskId: 'C-LATER', title: 'later', scope: ['tools/collab'], snapshotRoots: ['tools/collab', 'docs/collaboration'],
    acceptanceCriteria: [{ id: 'AC1', text: 'first' }, { id: 'AC2', text: 'second' }],
  });
  cmd.cmdTaskAdd(f.cfg, { actor: 'cursor', definition: path.join(f.dir, 'def-later.json') });
  const d2 = cmd.cmdDeliver(f.cfg, { task: 'C-LATER', actor: 'cursor', summary: 'later fixture' });
  addLater(d2, fileA);
  pass(d2);

  const dry = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO', 'dry-run': true });
  assert.equal(dry.freshness, 'archived', 'the preview must expose the verdict it would write');
  assert.match(dry.report, /接手/);
  assert.equal(/不归档/.test(dry.report), false, 'a task about to be archived must not be announced as still stale');
  const out = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO' });
  assert.equal(out.freshness, dry.freshness, 'the preview and the applied verdict must agree');
  assert.deepEqual(out.drifted, [fileA]);
  cmd.cmdSync(f.cfg);
  const task = state.loadTask(f.cfg, 'C-DEMO');
  assert.equal(task.freshness, 'archived', 'a later passed delivery owns the current bytes');
  assert.equal(task.scopeDrift.covered, 1);
  assert.equal(task.scopeDrift.uncoveredCount, 0);
  assert.match(task.freshnessNote, new RegExp(d2.deliveryId));
  assert.equal(/^- C-DEMO:/m.test(fs.readFileSync(path.join(f.dir, 'docs/collaboration/NEXT_CURSOR.md'), 'utf8')), false);

  // Edited again after that verdict: nobody owns the bytes, so the task is pending
  // again and the TODO names the file.
  appendToA('edited with nobody watching');
  cmd.cmdSync(f.cfg);
  assert.equal(state.loadTask(f.cfg, 'C-DEMO').freshness, 'stale');
  const line = fs.readFileSync(path.join(f.dir, 'docs/collaboration/NEXT_CURSOR.md'), 'utf8')
    .split('\n').find((l) => l.startsWith('- C-DEMO:'));
  assert.match(line, /无人复验/);
  assert.match(line, /paths\.mjs/);
});
await test('D37', 'the rescope preview and the persisted state tell the same story when files stay uncovered', () => {
  const f = fixture();
  const { files } = settledHistorical(f, { driftOne: true });
  const dry = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO', 'dry-run': true });
  assert.equal(dry.freshness, 'stale');
  assert.deepEqual(dry.uncovered, [files[0]]);
  assert.match(dry.report, /无人复验/);
  assert.match(dry.report, new RegExp(files[0].replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  const applied = cmd.cmdTaskRescope(f.cfg, { task: 'C-DEMO' });
  assert.equal(applied.freshness, dry.freshness);
  cmd.cmdSync(f.cfg);
  const task = state.loadTask(f.cfg, 'C-DEMO');
  assert.equal(task.freshness, dry.freshness, 'what the preview announced is what sync keeps');
  assert.deepEqual(task.scopeDrift.uncovered, [files[0]]);
});
fs.writeFileSync(path.join(root, 'results.json'), JSON.stringify({ root, source, total: results.length, passed: results.length - failed, failed, results }, null, 2));
console.log(`SUMMARY ${results.length - failed}/${results.length} daily checks; evidence=${root}`);
process.exitCode = failed ? 1 : 0;
