import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const deliveryId = 'DEL-20261018093335-80f2f4';
const subject = path.join(root, 'out/collab', deliveryId, 'tools/collab');
const cmd = await import(pathToFileURL(path.join(subject, 'lib/commands.mjs')));
const { loadConfig } = await import(pathToFileURL(path.join(subject, 'lib/paths.mjs')));
const cfg = loadConfig(root);
// An audit-only profile, frozen in this REV; no production config file is changed.
cfg.profiles['independent-stage'] = { kind: 'node', command: [process.execPath, path.join(here, 'stage-audit.mjs')], resultKind: 'json' };
const started = cmd.cmdReviewStart(cfg, { delivery: deliveryId, actor: 'codex', profile: 'independent-stage' });
const runResult = cmd.cmdReviewRun(cfg, { review: started.reviewId, actor: 'codex' });
const review = JSON.parse(fs.readFileSync(path.join(root, 'docs/collaboration/reviews', started.reviewId + '.json')));
const run = review.runs.at(-1), output = JSON.parse(fs.readFileSync(path.join(root, run.stdoutPath)));
const bugs = output.results.filter(r => !r.passed);
const evidence = {
  conclusion: bugs.length ? 'failed' : 'passed', subjectImplementationFingerprint: review.subjectFingerprints.implementation,
  assertionsTotal: output.assertionsTotal, assertionsFailed: output.assertionsFailed, assertionsSkipped: 0,
  coverage: [
    { id: 'AC1', result: 'passed', evidence: 'Snapshot integrity was checked by review-run before and after; no live production inputs are modified.' },
    { id: 'AC2', result: bugs.length ? 'failed' : 'passed', evidence: `Existing collab regression REV-20261018095043-e1c6f3:126/126; independent stage boundary probes ${output.assertionsTotal - output.assertionsFailed}/${output.assertionsTotal}. Results: ${output.resultFile}` },
    { id: 'AC3', result: 'passed', evidence: `Machine counts honestly record ${output.assertionsTotal} total/${output.assertionsFailed} failed/0 skipped; no failed audit is claimed passed.` },
  ],
  issues: bugs.map(b => ({
    title: `${b.id} ${b.title}`,
    repro: `node docs/handoff/verification/CollabTool-Stage-Independent-2026-10-18/stage-audit.mjs ; subject=${deliveryId}; case=${b.id}; fixture=${b.fixture || 'see results'}`,
    expected: b.expected || 'Probe must execute and uphold the stated invariant',
    actual: JSON.stringify(b.actual || { probeError: b.probeError }),
    evidence: `${path.relative(root, output.resultFile).replace(/\\/g, '/')}#${b.id}`,
    severity: b.probeError ? 'major' : b.severity, suggestedOwner: 'cursor',
  })),
  note: 'Supplementary independent audit; earlier 126/126 machine-pass REV is preserved, not overwritten. Defects are in delivered stage automation, not Unity/game code.',
};
const file = path.join(here, started.reviewId + '.evidence.json'); fs.writeFileSync(file, JSON.stringify(evidence, null, 2), { flag: 'wx' });
const result = cmd.cmdReviewFinish(cfg, { review: started.reviewId, actor: 'codex', evidence: file });
fs.writeFileSync(path.join(here, 'review-result.json'), JSON.stringify({ started, runResult, result, evidenceFile: file, auditResultFile: output.resultFile }, null, 2), { flag: 'wx' });
console.log(JSON.stringify(result));
