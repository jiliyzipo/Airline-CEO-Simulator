import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const mod = rel => import(pathToFileURL(path.join(root, rel)));
const cmd = await mod('tools/collab/lib/commands.mjs'), state = await mod('tools/collab/lib/state.mjs');
const book = await mod('tools/collab/lib/taskbook.mjs'), { loadConfig } = await mod('tools/collab/lib/paths.mjs');
const cfg = loadConfig(root), read = rel => JSON.parse(fs.readFileSync(path.resolve(root, rel)));
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const revId = 'REV-20261018103950-3b9460', r = read('docs/collaboration/reviews/' + revId + '.json');
assert.equal(r.status, 'passed'); assert.equal(r.actor, 'codex'); assert.equal(r.independent, true);
assert.deepEqual(r.assertions, { total: 132, failed: 0, skipped: 0 });
const d = read('docs/collaboration/deliveries/' + r.deliveryId + '.json'); assert.equal(d.deliveryId, 'DEL-20261018102500-1304ad');
cmd.verifySnapshotIntegrity(root, d.snapshotPath, d);
assert(d.snapshotManifest.entries.every(e => sha(path.join(root, e.path)) === e.sha256));
const run = r.runs.at(-1); assert.equal(run.exitCode, 0); assert.equal(run.postSnapshot.ok, true);
for (const kind of ['stdout', 'stderr']) {
  const binding = run[kind], file = path.join(root, binding.path); assert.equal(sha(file), binding.sha256);
  assert.equal(fs.statSync(file).size, binding.bytes); assert.equal(binding.runToken, run.runToken);
}
const machine = read(run.stdoutPath); assert.equal(machine.assertionsTotal, 132); assert.equal(machine.assertionsFailed, 0); assert.equal(machine.ok, true);
const boundary = read(path.join(here, 'boundary-summary.json')); assert.equal(boundary.passed, true);
const legacy = read(boundary.summary[0].results), current = read(boundary.summary[1].results);
assert.deepEqual(legacy.results.filter(p => !p.passed).map(p => p.id), ['SA05']);
assert.equal(current.total, 7); assert.equal(current.failed, 0); assert(current.results.every(p => p.passed));
assert.equal(current.results.find(p => p.id === 'SA05').actual.status, 'failed');
const attested = current.results.find(p => p.id === 'SA05B').actual;
assert.equal(attested.actor, 'cursor'); assert.equal(attested.independent, true); assert.equal(attested.attestation.by, 'cursor');
assert.equal(attested.attestation.authorizationSha256, attested.grantSha256);
const contract = read(path.join(here, 'test-contract-change.json'));
assert.equal(sha(contract.original), contract.originalSha256);
const ids = ['ISSUE-20261018095403-f1e86c', 'ISSUE-20261018095403-f4f17e', 'ISSUE-20261018095403-4c0790',
  'ISSUE-20261018095403-a7a8ab', 'ISSUE-20261018095403-dd65a3'];
const preservation = read('docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/preservation-before.json');
const game = preservation.entries.filter(e => e.path.startsWith('Unity Project/') || e.path.endsWith('Airline_CEO_Dev_Task_2026-10-17.md'));
assert(game.every(e => sha(path.join(root, e.path)) === e.sha256));
const oldFiles = ['docs/collaboration/reviews/REV-20261018095357-18f7dd.json', ...ids.map(id => 'docs/collaboration/issues/' + id + '.json')];
const before = oldFiles.map(file => ({ path: file, sha256: sha(path.join(root, file)) }));
const proof = { at: new Date().toISOString(), reviewId: revId, deliveryId: d.deliveryId, runToken: run.runToken,
  assertions: r.assertions, snapshotInputFiles: d.snapshotManifest.entries.length, snapshotAndWorkspaceIntact: true,
  boundaries: { subjectOnly: { passed: 5, total: 6, failedCase: 'SA05' }, reconciled: { passed: 7, total: 7 } },
  attested, originalAuditUnmodified: true, originalGameFilesUnchanged: game.length, oldImmutableRecords: before };
fs.writeFileSync(path.join(here, 'verified-evidence.json'), JSON.stringify(proof, null, 2), { flag: 'wx' });
const transitions = [];
for (const id of ids) {
  const currentIssue = state.listIssues(cfg).find(i => i.issueId === id); assert.equal(currentIssue.status, 'fixed');
  for (const status of ['verified', 'closed']) {
    transitions.push(cmd.cmdIssueUpdate(cfg, { actor: 'codex', issue: id, status,
      note: `Independent reacceptance ${revId}, final DEL ${d.deliveryId}:132/132 and reconciled boundary7/7; legacy SA05 contract conflict preserved and split into no-attestation refusal plus explicit CLI attestation positive. Proof docs/handoff/verification/CollabTool-Stage-Reaccept-2026-10-18/verified-evidence.json` }));
  }
  console.log(JSON.stringify({ issue: id, status: 'closed' }));
}
assert(before.every(e => sha(path.join(root, e.path)) === e.sha256));
const sync = cmd.cmdSync(cfg); cmd.cmdAck(cfg, { actor: 'codex', revision: sync.revision });
const taskGate = book.computeTaskGate(cfg, d.taskId), projectGate = book.computeOverallGate(cfg);
assert.equal(taskGate.ok, true); assert(ids.every(id => state.listIssues(cfg).find(i => i.issueId === id).status === 'closed'));
assert.equal(projectGate.ok, false);
const result = { at: new Date().toISOString(), proof, transitions, taskGate, projectGate,
  revision: state.loadState(cfg).revision, ack: state.loadAck(cfg), oldImmutableRecordsUnchanged: true };
fs.writeFileSync(path.join(here, 'closeout-state.json'), JSON.stringify(result, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ reviewId: revId, result: 'passed', closed: ids.length, taskGateOk: taskGate.ok, projectGateOk: projectGate.ok, revision: result.revision }));
