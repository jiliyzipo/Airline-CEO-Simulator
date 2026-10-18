import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const mod = rel => import(pathToFileURL(path.join(root, rel)));
const cmd = await mod('tools/collab/lib/commands.mjs'), state = await mod('tools/collab/lib/state.mjs');
const { loadConfig } = await mod('tools/collab/lib/paths.mjs');
const book = await mod('tools/collab/lib/taskbook.mjs');
const cfg = loadConfig(root), sync = cmd.cmdSync(cfg);
cmd.cmdAck(cfg, { actor: 'codex', revision: sync.revision });
const sha = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const id = 'DEL-20261018071756-010504';
const delivery = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/deliveries', id + '.json')));
cmd.verifySnapshotIntegrity(root, delivery.snapshotPath, delivery);
const mismatch = delivery.snapshotManifest.entries.filter(e => !fs.existsSync(path.join(root, e.path)) || sha(path.join(root, e.path)) !== e.sha256);
const older = [];
for (const old of ['DEL-20261018051637-e21eae', 'DEL-20261018035835-766dc7']) {
  const d = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/deliveries', old + '.json')));
  cmd.verifySnapshotIntegrity(root, d.snapshotPath, d); older.push({ deliveryId: old, inputCount: d.snapshotManifest.entries.length, intact: true });
}
const grantFile = path.join(root, 'docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json');
const result = { at: new Date().toISOString(), deliveryId: id, implementationFingerprint: delivery.fingerprints.implementation.fingerprint,
  snapshotInputs: delivery.snapshotManifest.entries.length, snapshotIntact: true, unlistedAtCreation: delivery.snapshotManifest.unlistedAtCreation,
  workspaceInputMismatch: mismatch.map(e => e.path), older,
  grantSha256: sha(grantFile), revision: state.loadState(cfg).revision, ack: state.loadAck(cfg),
  task: state.loadTask(cfg, 'C-COLLAB-FINAL'), taskGate: book.computeTaskGate(cfg, 'C-COLLAB-FINAL'),
  projectGate: book.computeOverallGate(cfg),
  scope: { startedUnity: false, changedGameCode: false, closedIssues: false, changedOfficialChecklist: false, independentReviewPerformed: false } };
fs.writeFileSync(path.join(here, 'final-state.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify({ deliveryId: id, snapshotInputs: result.snapshotInputs, snapshotIntact: true,
  workspaceMatchesInputs: mismatch.length === 0, revision: result.revision, taskGateOk: result.taskGate.ok,
  checkedToolClauses: result.projectGate.toolClauses.checked, oldSnapshotsIntact: older.length, independentReviewPerformed: false }));
