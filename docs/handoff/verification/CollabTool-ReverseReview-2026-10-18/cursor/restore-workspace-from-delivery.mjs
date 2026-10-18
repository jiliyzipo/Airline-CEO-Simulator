// Restore the workspace copies of a delivery's snapshot inputs from the immutable snapshot.
// Usage: node docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/restore-workspace-from-delivery.mjs --delivery <ID> [--apply]
// Default is a dry run. Backs up every replaced file before writing.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const repo = process.cwd();
const args = process.argv.slice(2);
const deliveryId = args[args.indexOf('--delivery') + 1];
const apply = args.includes('--apply');
if (!deliveryId) {
  console.error('need --delivery <ID>');
  process.exit(2);
}

const delivery = JSON.parse(fs.readFileSync(path.join(repo, 'docs/collaboration/deliveries', `${deliveryId}.json`), 'utf8'));
const snap = path.join(repo, delivery.snapshotPath);
const sha = (f) => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const backupRoot = path.join(repo, '.collab-local/tmp/quarantine', `workspace-drift-${stamp}`);

const report = { at: new Date().toISOString(), deliveryId, snapshot: delivery.snapshotPath, apply, backupRoot: null, entries: [] };
for (const e of delivery.snapshotManifest.entries) {
  const src = path.join(snap, e.path);
  const dst = path.join(repo, e.path);
  const srcOk = fs.existsSync(src) && sha(src) === e.sha256;
  const dstExists = fs.existsSync(dst);
  const dstSha = dstExists ? sha(dst) : null;
  const action = !srcOk ? 'skip-snapshot-corrupt' : dstSha === e.sha256 ? 'identical' : 'restore';
  const row = { path: e.path, expected: e.sha256, before: dstSha, action };
  if (apply && action === 'restore') {
    const backup = path.join(backupRoot, e.path);
    fs.mkdirSync(path.dirname(backup), { recursive: true });
    if (dstExists) fs.copyFileSync(dst, backup);
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.copyFileSync(src, dst);
    row.after = sha(dst);
    row.backup = path.relative(repo, backup).split(path.sep).join('/');
    row.restored = row.after === e.sha256;
  }
  report.entries.push(row);
  console.log(`${action.padEnd(20)} ${e.path}`);
}
report.summary = {
  total: report.entries.length,
  identical: report.entries.filter((r) => r.action === 'identical').length,
  restored: report.entries.filter((r) => r.action === 'restore').length,
  corruptSource: report.entries.filter((r) => r.action === 'skip-snapshot-corrupt').length,
};
if (apply) {
  report.backupRoot = path.relative(repo, backupRoot).split(path.sep).join('/');
  const allMatch = report.entries.every((r) => (r.after ?? r.before) === r.expected);
  report.workspaceMatchesDelivery = allMatch;
  const outDir = path.join(repo, 'docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor');
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, `workspace-restore-${stamp}.json`), JSON.stringify(report, null, 2));
  console.log(`\nworkspaceMatchesDelivery=${allMatch} restored=${report.summary.restored}`);
}
