// Independent stage-boundary probes. Subject modules are loaded only from the delivered snapshot.
// All scenario mutations and child processes live in disposable fixtures; no Unity is launched.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../../../..');
const subject = path.join(repo, 'out/collab/DEL-20261018102500-1304ad/tools/collab');
const stage = await import(pathToFileURL(path.join(subject, 'lib/stage.mjs')));
const cmd = await import(pathToFileURL(path.join(subject, 'lib/commands.mjs')));
const state = await import(pathToFileURL(path.join(subject, 'lib/state.mjs')));
const io = await import(pathToFileURL(path.join(subject, 'lib/io.mjs')));
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'codex-stage-independent-'));
const GOOD = 'console.log(JSON.stringify({assertionsTotal:2,assertionsFailed:0,assertionsSkipped:0}))';
const results = []; let serial = 0;
function put(dir, rel, value) { const file = path.join(dir, rel); fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, typeof value === 'string' ? value : JSON.stringify(value, null, 2)); return file; }
function fixture(command = GOOD) {
  const dir = path.join(root, String(++serial)); fs.cpSync(subject, path.join(dir, 'tools/collab'), { recursive: true });
  const cfg = { ...JSON.parse(fs.readFileSync(path.join(subject, 'config.example.json'))), _repoRoot: dir,
    projectId: 'stage-independent', taskBook: { tasks: {}, completionTasks: [] },
    demoSnapshotRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'],
    profiles: { smoke: { command: [process.execPath, '-e', command], resultKind: 'json' } } };
  put(dir, 'tools/collab/config.json', cfg); put(dir, 'docs/collaboration/PROTOCOL.md', '# fixture');
  cmd.cmdInit(cfg); put(dir, 'tools/collab/proof-input.txt', 'original'); return { dir, cfg };
}
const getDelivery = (f, d) => io.readJson(path.join(f.dir, `docs/collaboration/deliveries/${d.deliveryId}.json`));
async function probe(id, title, line, severity, fn) {
  try { results.push({ id, title, file: 'tools/collab/lib/stage.mjs', line, severity, ...await fn() }); }
  catch (e) { results.push({ id, title, line, severity, passed: false, probeError: e.stack }); }
}
await probe('SA01', '预检会把执行中已被改写的快照标为 ready', 324, 'major', () => {
  const f = fixture("require('node:fs').writeFileSync('tools/collab/proof-input.txt','tampered');" + GOOD);
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  const d = getDelivery(f, close); let integrityCode = null;
  try { cmd.verifySnapshotIntegrity(f.dir, d.snapshotPath, d); } catch (e) { integrityCode = e.exitCode; }
  const entry = io.readJson(path.join(f.dir, 'out/collab/handoff/index.json')).entries.at(-1);
  return { passed: !close.preflight.ok && entry.status !== 'ready',
    expected: 'stage-close preflight must reject mutated delivery inputs and must not publish a ready handoff',
    actual: { preflightOk: close.preflight.ok, handoffStatus: entry.status, independentIntegrityCode: integrityCode }, fixture: f.dir };
});
await probe('SA02', 'Unity预检绕过另一review持有的工具资源锁', 343, 'blocker', () => {
  const f = fixture();
  // A Node placeholder under a Unity-named profile proves execution without starting any editor.
  const sentinel = path.join(f.dir, 'preflight-executed.txt');
  f.cfg.profiles['unity-editmode'] = { command: [process.execPath, '-e', `require('node:fs').writeFileSync(${JSON.stringify(sentinel)},'executed');${GOOD}`], resultKind: 'json' };
  const d = getDelivery(f, cmd.cmdDeliver(f.cfg, { actor: 'cursor', task: 'C-DEMO', summary: 'lease fixture', profiles: 'unity-editmode' }));
  const lock = new io.FileLock(path.join(f.dir, '.collab-local/locks/unity-review.lock'), 'other-review', { reviewId: 'REV-other' });
  if (!lock.acquire().ok) throw Error('fixture lease did not acquire');
  let value, code;
  try { value = stage.runProfileInSnapshot(f.cfg, d, 'unity-editmode'); } catch (e) { code = e.exitCode; }
  finally { lock.release({ force: true }); }
  const executed = fs.existsSync(sentinel);
  return { passed: !executed && code === 4,
    expected: 'Unity preflight must check/acquire the shared Unity review lease immediately before spawn; held lease returns 4 without execution',
    actual: { executed, code: code ?? null, preflightOk: value?.ok ?? null, usedRealUnity: false }, fixture: f.dir };
});
await probe('SA03', '自动证据将机器结果未覆盖的自定义验收条件写成 passed', 881, 'blocker', () => {
  const f = fixture(); const task = state.loadTask(f.cfg, 'C-DEMO');
  task.acceptanceCriteria = [{ id: 'AC1', text: 'The required-proof.txt artifact must exist and contain an approved manual verification.' }];
  state.saveTask(f.cfg, task);
  const close = stage.cmdStageClose(f.cfg, { task: 'C-DEMO', profile: 'smoke' });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId });
  const record = io.readJson(path.join(f.dir, `docs/collaboration/reviews/${review.reviewId}.json`));
  return { passed: review.status !== 'passed',
    expected: 'criteria without a trusted machine mapping or explicit reviewer verification must stay not_run/blocked; machine counters alone cannot mark every AC passed',
    actual: { status: review.status, coverage: record.coverage, requiredArtifactExists: fs.existsSync(path.join(f.dir, 'required-proof.txt')) }, fixture: f.dir };
});
await probe('SA04', '同一DEL已有正在执行的阶段检测时仍可再次启动并执行', 903, 'major', async () => {
  const f = fixture(), marker = path.join(f.dir, 'executions.txt'), release = path.join(f.dir, 'release');
  f.cfg.profiles.smoke.command = [process.execPath, '-e', `const fs=require('node:fs');fs.appendFileSync(${JSON.stringify(marker)},process.pid+'\\n');const end=Date.now()+15000;while(!fs.existsSync(${JSON.stringify(release)})){if(Date.now()>end)process.exit(2);Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,20)};${GOOD}`];
  const close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke', 'no-preflight': true });
  const script = put(f.dir, 'worker.mjs', `import{cmdStageReview}from ${JSON.stringify(pathToFileURL(path.join(subject, 'lib/stage.mjs')).href)};try{console.log(JSON.stringify(cmdStageReview(${JSON.stringify(f.cfg)},{actor:'codex',delivery:${JSON.stringify(close.deliveryId)}})))}catch(e){console.log(JSON.stringify({error:e.message,exitCode:e.exitCode}));process.exitCode=e.exitCode||1}`);
  const workers = [];
  function launch() { const p = spawn(process.execPath, [script], { cwd: f.dir, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }); let output = ''; p.stdout.on('data', d => output += d); p.stderr.on('data', d => output += d); const done = new Promise(resolve => p.on('close', code => resolve({ code, output }))); workers.push({ p, done }); }
  const count = () => fs.existsSync(marker) ? fs.readFileSync(marker, 'utf8').trim().split(/\r?\n/).filter(Boolean).length : 0;
  async function until(fn, ms) { const end = Date.now() + ms; while (!fn() && Date.now() < end) await new Promise(r => setTimeout(r, 25)); }
  let executions, finished;
  try { launch(); await until(() => count() >= 1, 10000); if (!count()) throw Error('first command never started'); launch(); await until(() => count() >= 2, 4000); executions = count(); }
  finally { put(f.dir, 'release', 'go'); finished = await Promise.all(workers.map(w => w.done)); }
  return { passed: executions === 1,
    expected: 'stage-review needs a delivery-level claim/lease so retries or two reviewers cannot create two active reviews and run the same delivery twice',
    actual: { executions, workerResults: finished, reviewCount: fs.readdirSync(path.join(f.dir, 'docs/collaboration/reviews')).length }, fixture: f.dir };
});
await probe('SA05', 'stage-review宣称支持限定Cursor授权但仍被默认角色表拦截', 908, 'major', () => {
  const f = fixture();
  const auth = put(f.dir, 'developer.json', { schemaVersion: 1, grantedBy: 'user', actor: 'codex', projectId: f.cfg.projectId, taskId: 'C-DEMO',
    actions: ['deliver'], allowedRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'], statement: 'fixture development' });
  const delivered = cmd.cmdDeliver(f.cfg, { actor: 'codex', task: 'C-DEMO', authorization: auth, summary: 'reverse fixture', profiles: 'smoke' });
  const d = getDelivery(f, delivered);
  const grant = put(f.dir, 'review.json', { schemaVersion: 1, grantedBy: 'user', actor: 'cursor', developerActor: 'codex', projectId: f.cfg.projectId,
    taskId: 'C-DEMO', deliveryId: d.deliveryId, statement: 'fixture review', profiles: ['smoke'], actions: ['review-start', 'review-run', 'review-finish', 'review-evidence', 'review-cancel'],
    subjectFingerprints: Object.fromEntries(['implementation', 'verification', 'contract'].map(p => [p, d.fingerprints[p].fingerprint])) });
  const lower = cmd.cmdReviewStart(f.cfg, { delivery: d.deliveryId, actor: 'cursor', authorization: grant, profile: 'smoke' });
  cmd.cmdReviewCancel(f.cfg, { review: lower.reviewId, actor: 'cursor', authorization: grant });
  let code, result;
  try { result = stage.cmdStageReview(f.cfg, { delivery: d.deliveryId, actor: 'cursor', authorization: grant, force: true }); } catch (e) { code = e.exitCode; }
  return { passed: code !== 7 && result?.status === 'passed', expected: 'an explicitly scoped reverse-review grant accepted by review-* must also work through the documented stage-review route',
    actual: { lowerLevelAccepted: true, stageCode: code ?? null, status: result?.status ?? null }, fixture: f.dir };
});
await probe('SA06', '正常阶段自动化正例仍可完整运行', 903, 'minor', () => {
  const f = fixture(), close = stage.cmdStageClose(f.cfg, { roots: 'tools/collab', profile: 'smoke' });
  const review = stage.cmdStageReview(f.cfg, { actor: 'codex', delivery: close.deliveryId });
  return { passed: review.status === 'passed' && review.counters.total === 2, expected: 'normal machine-only happy path remains usable', actual: { status: review.status, counters: review.counters }, fixture: f.dir };
});
const out = { at: new Date().toISOString(), subject, fixtureRoot: root, total: results.length,
  passed: results.filter(r => r.passed).length, failed: results.filter(r => !r.passed).length, results };
const destination = path.join(here, `results-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
fs.writeFileSync(destination, JSON.stringify(out, null, 2), { flag: 'wx' });
// Exit 0 with honest failed counters permits review-finish to attach detailed audit issues.
console.log(JSON.stringify({ assertionsTotal: out.total, assertionsFailed: out.failed, assertionsSkipped: 0, resultFile: destination, subject, results }));
