import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const mod = rel => import(pathToFileURL(path.join(root, rel)));
const cmd = await mod('tools/collab/lib/commands.mjs'), state = await mod('tools/collab/lib/state.mjs');
const book = await mod('tools/collab/lib/taskbook.mjs'), { loadConfig } = await mod('tools/collab/lib/paths.mjs');
const cfg = loadConfig(root), read = rel => JSON.parse(fs.readFileSync(path.join(root, rel)));
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const sync = cmd.cmdSync(cfg); cmd.cmdAck(cfg, { actor: 'codex', revision: sync.revision });
const d = read('docs/collaboration/deliveries/DEL-20261018093335-80f2f4.json');
cmd.verifySnapshotIntegrity(root, d.snapshotPath, d);
const mismatch = d.snapshotManifest.entries.filter(e => sha(path.join(root, e.path)) !== e.sha256).map(e => e.path);
const reviews = [];
for (const id of ['REV-20261018095043-e1c6f3', 'REV-20261018095357-18f7dd']) {
  const r = read('docs/collaboration/reviews/' + id + '.json'), run = r.runs.at(-1);
  const bindings = ['stdout', 'stderr'].map(kind => {
    const b = run[kind], file = path.join(root, b.path); assert.equal(sha(file), b.sha256); assert.equal(fs.statSync(file).size, b.bytes);
    return { kind, path: b.path, bytes: b.bytes, sha256: b.sha256 };
  });
  assert.equal(run.postSnapshot.ok, true);
  reviews.push({ reviewId: id, status: r.status, assertions: r.assertions, profile: r.profile, bindings, issueIds: r.issues });
}
assert.equal(reviews[0].status, 'passed'); assert.equal(reviews[0].assertions.total, 126);
assert.equal(reviews[1].status, 'failed'); assert.equal(reviews[1].assertions.failed, 5);
const issueIds = reviews[1].issueIds, issues = state.listIssues(cfg).filter(i => issueIds.includes(i.issueId));
assert.equal(issues.length, 5); assert(issues.every(i => i.status === 'open'));
const preservation = read('docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/preservation-before.json');
const game = preservation.entries.filter(e => e.path.startsWith('Unity Project/') || e.path.endsWith('Airline_CEO_Dev_Task_2026-10-17.md'));
const changedGame = game.filter(e => sha(path.join(root, e.path)) !== e.sha256).map(e => e.path); assert.equal(changedGame.length, 0);
const result = { at: new Date().toISOString(), deliveryId: d.deliveryId, snapshotFiles: d.snapshotManifest.entries.length,
  snapshotIntact: true, workspaceMismatch: mismatch, reviews, issues: issues.map(i => ({ issueId: i.issueId, title: i.title, severity: i.severity, status: i.status })),
  task: state.loadTask(cfg, d.taskId), taskGate: book.computeTaskGate(cfg, d.taskId), revision: state.loadState(cfg).revision,
  ack: state.loadAck(cfg), originalGameFilesChecked: game.length, changedGame, sourceCodeEditsDuringInspection: false };
assert.equal(result.task.reviewStatus, 'failed'); assert.equal(result.taskGate.ok, false);
fs.writeFileSync(path.join(here, 'final-state.json'), JSON.stringify(result, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ delivery: d.deliveryId, finalReview: reviews[1].reviewId, verdict: 'failed', snapshotFiles: result.snapshotFiles,
  snapshotIntact: true, workspaceMismatch: mismatch, openIssues: issues.length, revision: result.revision, gameFilesUnchanged: game.length }));
