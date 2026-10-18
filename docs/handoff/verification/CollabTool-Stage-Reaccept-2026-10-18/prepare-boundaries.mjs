// Keep the original audit immutable. Publish a subject-only copy and a justified contract update.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const original = path.join(root, 'docs/handoff/verification/CollabTool-Stage-Independent-2026-10-18/stage-audit.mjs');
const text = fs.readFileSync(original, 'utf8');
const old = 'out/collab/DEL-20261018093335-80f2f4/tools/collab', subject = 'out/collab/DEL-20261018102500-1304ad/tools/collab';
if (text.split(old).length !== 2) throw Error('expected a single subject selection');
const subjectOnly = text.replace(old, subject);
fs.writeFileSync(path.join(here, 'stage-audit-subject-only.mjs'), subjectOnly, { flag: 'wx' });
const oldVerdict = "return { passed: code !== 7 && result?.status === 'passed', expected: 'an explicitly scoped reverse-review grant accepted by review-* must also work through the documented stage-review route',";
if (subjectOnly.split(oldVerdict).length !== 2) throw Error('SA05 original assertion changed unexpectedly');
let adjusted = subjectOnly.replace(oldVerdict,
  `const record = result?.reviewId ? io.readJson(path.join(f.dir, 'docs/collaboration/reviews', result.reviewId + '.json')) : null;
  return { passed: code === undefined && result?.status === 'failed' && record?.actor === 'cursor' && record?.independent === true && record.coverage.every(c => c.result === 'not_run'), expected: 'scoped authorization must work; unproved free-text criteria must remain not_run and the un-attested review must fail',`);
adjusted = adjusted.replace("import { spawn } from 'node:child_process';", "import { spawn, spawnSync } from 'node:child_process';\nimport crypto from 'node:crypto';");
const positive = `
await probe('SA05B', '限定Cursor授权加显式补证的CLI正例保留真实作者与授权哈希', 1198, 'major', () => {
  const f = fixture();
  const dev = put(f.dir, 'developer.json', { schemaVersion: 1, grantedBy: 'user', actor: 'codex', projectId: f.cfg.projectId,
    taskId: 'C-DEMO', actions: ['deliver'], allowedRoots: ['tools/collab', 'docs/collaboration/PROTOCOL.md'], statement: 'fixture development' });
  const delivered = cmd.cmdDeliver(f.cfg, { actor: 'codex', task: 'C-DEMO', authorization: dev, summary: 'attestation fixture', profiles: 'smoke' });
  const d = getDelivery(f, delivered);
  const grant = put(f.dir, 'review.json', { schemaVersion: 1, grantedBy: 'user', actor: 'cursor', developerActor: 'codex', projectId: f.cfg.projectId,
    taskId: 'C-DEMO', deliveryId: d.deliveryId, statement: 'fixture reverse review', profiles: ['smoke'],
    actions: ['review-start', 'review-run', 'review-finish', 'review-evidence', 'review-cancel'],
    subjectFingerprints: Object.fromEntries(['implementation', 'verification', 'contract'].map(p => [p, d.fingerprints[p].fingerprint])) });
  const actual = spawnSync(process.execPath, [path.join(f.dir, 'tools/collab/cli.mjs'), 'stage-review', '--delivery', d.deliveryId,
    '--actor', 'cursor', '--authorization', grant, '--coverage', 'AC1=passed,AC2=passed,AC3=passed', '--json'],
    { cwd: f.dir, encoding: 'utf8', windowsHide: true, timeout: 30000 });
  let result; try { result = JSON.parse(actual.stdout); } catch { throw Error(actual.stdout + actual.stderr); }
  const record = result.reviewId ? io.readJson(path.join(f.dir, 'docs/collaboration/reviews', result.reviewId + '.json')) : null;
  const evidence = record?.evidencePath ? io.readJson(path.join(f.dir, record.evidencePath)) : null;
  const hash = crypto.createHash('sha256').update(fs.readFileSync(grant)).digest('hex');
  return { passed: actual.status === 0 && result.status === 'passed' && record?.actor === 'cursor' && record?.independent === true &&
      record?.reviewKind === 'independent' && record?.authorization?.sha256 === hash && evidence?.attestation?.by === 'cursor' &&
      evidence?.attestation?.authorizationSha256 === hash && evidence?.attestation?.clauses?.length === 3 &&
      evidence?.coverage?.length === 3 && evidence.coverage.every(c => c.result === 'passed'),
    expected: 'explicit fixture attestation is accepted through the real CLI and retains actor, independent identity and pinned authorization hash',
    actual: { exitCode: actual.status, status: result.status, actor: record?.actor, independent: record?.independent,
      reviewKind: record?.reviewKind, attestation: evidence?.attestation, grantSha256: hash, coverage: evidence?.coverage }, fixture: f.dir };
});
`;
adjusted = adjusted.replace("await probe('SA06'", positive + "\nawait probe('SA06'");
fs.writeFileSync(path.join(here, 'stage-audit-reconciled.mjs'), adjusted, { flag: 'wx' });
const hash = s => crypto.createHash('sha256').update(s).digest('hex');
fs.writeFileSync(path.join(here, 'test-contract-change.json'), JSON.stringify({ at: new Date().toISOString(), original,
  originalSha256: hash(text), subject, subjectOnlySha256: hash(subjectOnly), reconciledSha256: hash(adjusted),
  reason: 'SA03 forbids automatic approval of free-text ACs. SA05 must prove authorization independently, preserving not_run/failure without attestation, and a separate real CLI positive must supply explicit coverage.',
  strongerChecks: ['SA05 must finish failed, not merely return any code other than 7', 'actor=cursor and independent=true',
    'unattested coverage all not_run', 'CLI positive attestation.by plus authorization SHA256 and three explicit clauses'],
  fixtureActorsAreSynthetic: true, originalAuditUnmodified: true }, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ originalSha256: hash(text), prepared: ['stage-audit-subject-only.mjs', 'stage-audit-reconciled.mjs'] }));
