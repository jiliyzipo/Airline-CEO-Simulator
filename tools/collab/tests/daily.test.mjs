import assert from 'node:assert/strict';
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
fs.writeFileSync(path.join(root, 'results.json'), JSON.stringify({ root, source, total: results.length, passed: results.length - failed, failed, results }, null, 2));
console.log(`SUMMARY ${results.length - failed}/${results.length} daily checks; evidence=${root}`);
process.exitCode = failed ? 1 : 0;
