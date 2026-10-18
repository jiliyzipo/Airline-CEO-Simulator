// Recheck existing independent evidence and byte bindings; do not rerun suites or edit business inputs.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const read = rel => JSON.parse(fs.readFileSync(path.resolve(root, rel)));
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const mod = rel => import(pathToFileURL(path.join(root, rel)));
const { verifySnapshotIntegrity } = await mod('tools/collab/lib/commands.mjs');
const { validateReview } = await mod('tools/collab/lib/schemas.mjs');
const { collectEntries, hashManifest } = await mod('tools/collab/lib/fingerprint.mjs');
const { computeTaskGate, computeOverallGate } = await mod('tools/collab/lib/taskbook.mjs');
const { loadConfig } = await mod('tools/collab/lib/paths.mjs');
const cfg = loadConfig(root), id = 'REV-20261018072743-74a233';
const r = read('docs/collaboration/reviews/' + id + '.json'); validateReview(r);
assert.equal(r.status, 'passed'); assert.equal(r.actor, 'cursor'); assert.equal(r.independent, true);
assert.equal(r.reviewException, true); assert.equal(r.profile, 'collab'); assert.equal(r.runs.length, 1);
const d = read('docs/collaboration/deliveries/' + r.deliveryId + '.json'); assert.equal(d.actor, 'codex');
verifySnapshotIntegrity(root, d.snapshotPath, d);
const mismatch = d.snapshotManifest.entries.filter(e => sha(path.join(root, e.path)) !== e.sha256);
assert.equal(mismatch.length, 0); assert.equal(d.snapshotManifest.entries.length, 35);
const run = r.runs[0]; assert.equal(run.runToken, '30cc87cca6933a0d'); assert.equal(run.exitCode, 0); assert.equal(run.postSnapshot.ok, true);
function bindings(review) {
  const run = review.runs.at(-1); assert.equal(run.exitCode, 0);
  return ['stdout', 'stderr'].map(kind => {
    const binding = run[kind], file = path.join(root, binding.path), bytes = fs.readFileSync(file);
    assert.equal(bytes.length, binding.bytes); assert.equal(sha(file), binding.sha256);
    assert.equal(binding.runToken, run.runToken);
    return { kind, path: binding.path, bytes: bytes.length, sha256: binding.sha256 };
  });
}
const logs = bindings(r), stdout = read(run.stdout.path);
assert.equal(stdout.assertionsTotal, 112); assert.equal(stdout.assertionsFailed, 0); assert.equal(stdout.assertionsSkipped, 0);
assert.equal(stdout.ok, true); assert.deepEqual(stdout.suites.map(s => s.passed), [38, 26, 13, 21, 14]);
assert(stdout.suites.every(s => s.exitCode === 0 && s.failed === 0));
assert.deepEqual(r.assertions, { total: 112, failed: 0, skipped: 0 });
assert(r.coverage.length === 3 && r.coverage.every(c => c.result === 'passed'));
assert.equal(sha(path.join(root, 'docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json')), r.authorization.sha256);
const externalRoot = 'docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/evidence/cursor-' + id;
const external = read(externalRoot + '/summary.json'); assert.equal(external.failedSuites, 0);
assert.equal(path.resolve(external.subjectToolsPath), path.join(root, d.snapshotPath, 'tools/collab'));
const externalCounts = [];
for (const [suite, total] of [['baseline24', 24], ['supplemental', 12], ['boundary8', 8]]) {
  const detail = read(externalRoot + '/' + suite + '.results.json');
  assert.equal(detail.total, total); assert.equal(detail.passed, total); assert.equal(detail.failed, 0);
  const entry = external.suites.find(s => s.id === suite); assert.equal(entry.exitCode, 0);
  const text = fs.readFileSync(path.join(root, externalRoot, suite + '.stdout.txt'), 'utf8');
  assert.equal((text.match(/^PASS\b/gm) || []).length, total); assert.equal((text.match(/^FAIL\b/gm) || []).length, 0);
  externalCounts.push({ suite, total, resultSha256: sha(path.join(root, externalRoot, suite + '.results.json')) });
}
const xmlRel = 'docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/xml12-' + id + '.json';
const xml = read(xmlRel); assert.equal(xml.total, 12); assert.equal(xml.passed, 12); assert.equal(xml.failed, 0);
assert.equal(xml.subjectManifest, hashManifest(collectEntries(path.join(root, d.snapshotPath), ['tools/collab'])));
const probesRel = 'docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/probes-2026-10-18T07-33-43-014Z.json';
const probes = read(probesRel); assert.equal(probes.passed, 6); assert(probes.results.every(p => p.passed));
const restoration = read('docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/workspace-restore-2026-10-18T07-27-13-725Z.json');
const restored = restoration.entries.filter(e => e.action === 'restore'); assert.equal(restored.length, 24);
for (const e of restored) {
  assert.equal(sha(path.join(root, e.backup)), e.before); assert.equal(e.after, e.expected);
  assert.equal(sha(path.join(root, e.path)), e.expected);
}
const engineering = [];
for (const [rev, total] of [['REV-20261018033359-8f89f1', 3], ['REV-20261018033157-6550bc', 371], ['REV-20261018033542-0f4993', 25]]) {
  const record = read('docs/collaboration/reviews/' + rev + '.json');
  assert.equal(record.status, 'passed'); assert.equal(record.assertions.total, total); assert.equal(record.assertions.failed, 0);
  const bound = bindings(record), result = record.runs.at(-1).resultFile;
  if (result) {
    assert.equal(result.producedByRun, true); assert.equal(result.before.exists, false);
    assert.equal(sha(path.join(root, record.snapshotPath, result.path)), result.sha256);
  }
  engineering.push({ reviewId: rev, profile: record.profile, assertions: record.assertions, bindings: bound, resultFileSha256: result?.sha256 || null, rerunThisTurn: false });
}
const taskGate = computeTaskGate(cfg, d.taskId); assert.equal(taskGate.ok, true);
const adapter = fs.readFileSync(path.join(root, 'tools/collab/adapters/client-hook.mjs'), 'utf8');
assert(adapter.includes('result.status !== 4') && adapter.includes('attempt < 4'));
const evidenceFiles = ['docs/collaboration/reviews/' + id + '.json', r.evidencePath, probesRel, xmlRel,
  'docs/handoff/CollabTool_Cursor_IndependentReverseReview_2026-10-18.md'];
const out = { at: new Date().toISOString(), ok: true, reviewId: id, deliveryId: d.deliveryId, logs,
  snapshotInputs: 35, workspaceInputMismatch: [], assertions: r.assertions, externalCounts,
  xml: { passed: 12, total: 12, subjectManifest: xml.subjectManifest }, probes: { passed: 6, total: 6,
    qualification: 'P3 exercises missing taskbook, not an isolated missing-baseline case; use closure G01/G09 for baseline boundary.' },
  restoredBackupHashesVerified: restored.length, driftSourceAttribution: 'unknown; backup hashes verify differing prior bytes, not which process wrote them',
  engineering, taskGate, projectGate: computeOverallGate(cfg), adapterAlreadyRetriesExit4: true,
  evidenceFiles: evidenceFiles.map(rel => ({ path: rel, sha256: sha(path.join(root, rel)) })) };
fs.writeFileSync(path.join(here, 'codex-closeout-audit.json'), JSON.stringify(out, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ ok: true, reviewId: id, snapshotInputs: 35, assertions: out.assertions,
  external: externalCounts.map(e => e.total), xml: 12, probes: 6, backupHashes: restored.length, taskGateOk: taskGate.ok }));
