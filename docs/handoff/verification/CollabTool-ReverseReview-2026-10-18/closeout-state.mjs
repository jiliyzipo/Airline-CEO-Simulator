import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const mod = rel => import(pathToFileURL(path.join(root, rel)));
const cmd = await mod('tools/collab/lib/commands.mjs'), state = await mod('tools/collab/lib/state.mjs');
const book = await mod('tools/collab/lib/taskbook.mjs'), { loadConfig } = await mod('tools/collab/lib/paths.mjs');
const cfg = loadConfig(root), sync = cmd.cmdSync(cfg); cmd.cmdAck(cfg, { actor: 'codex', revision: sync.revision });
const taskGate = book.computeTaskGate(cfg, 'C-COLLAB-FINAL'), gate = book.computeOverallGate(cfg);
assert.equal(taskGate.ok, true); assert.equal(gate.toolClauses.checked, 12); assert.equal(gate.toolClauses.error, null);
assert.equal(gate.openIssues.length, 0); assert.equal(gate.ok, false);
const issue = state.listIssues(cfg).find(i => i.issueId === 'ISSUE-20261018041847-27c5bf'); assert.equal(issue.status, 'closed');
const original = JSON.parse(fs.readFileSync(path.join(here, 'preservation-before.json')));
const allowed = 'docs/Development Manual/Development Task Book/Task book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md';
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const changed = original.entries.filter(e => !fs.existsSync(path.join(root, e.path)) || sha(path.join(root, e.path)) !== e.sha256);
assert.deepEqual(changed.map(e => e.path), [allowed]);
const d = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/deliveries/DEL-20261018071756-010504.json')));
cmd.verifySnapshotIntegrity(root, d.snapshotPath, d);
assert(d.snapshotManifest.entries.every(e => sha(path.join(root, e.path)) === e.sha256));
const out = { at: new Date().toISOString(), revision: state.loadState(cfg).revision, ack: state.loadAck(cfg),
  taskGate, projectGate: gate, issue, officialChecklist: { checked: 12, total: 12, scope: 'manual sync/ack and optional foreground watch; native Hooks unverified' },
  preservation: { baselineFiles: original.entries.length, unchanged: original.entries.length - changed.length, authorizedChanges: changed.map(e => e.path) },
  acceptedDeliveryInputsStillMatchWorkspace: true, independentReviewId: 'REV-20261018072743-74a233' };
fs.writeFileSync(path.join(here, 'closeout-state.json'), JSON.stringify(out, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ revision: out.revision, checklist: '12/12', issue: issue.status, toolTaskGateOk: taskGate.ok,
  projectGateOk: gate.ok, openIssues: gate.openIssues.length, preservationUnchanged: out.preservation.unchanged, acceptedInputsMatch: true }));
