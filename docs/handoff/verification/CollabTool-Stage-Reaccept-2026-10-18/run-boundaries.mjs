import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../../../..');
const summary = [];
for (const name of ['stage-audit-subject-only', 'stage-audit-reconciled']) {
  const r = spawnSync(process.execPath, [path.join(here, name + '.mjs')], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 90000, maxBuffer: 8 * 1024 * 1024 });
  fs.writeFileSync(path.join(here, name + '.stdout.txt'), r.stdout || '', { flag: 'wx' });
  fs.writeFileSync(path.join(here, name + '.stderr.txt'), r.stderr || '', { flag: 'wx' });
  const parsed = JSON.parse(r.stdout);
  summary.push({ name, exitCode: r.status, total: parsed.assertionsTotal, failed: parsed.assertionsFailed, results: parsed.resultFile,
    stdoutSha256: crypto.createHash('sha256').update(r.stdout).digest('hex') });
  console.log(JSON.stringify(summary.at(-1)));
}
const out = { at: new Date().toISOString(), summary, passed: summary[0].total === 6 && summary[0].failed === 1 && summary[1].total === 7 && summary[1].failed === 0 };
fs.writeFileSync(path.join(here, 'boundary-summary.json'), JSON.stringify(out, null, 2), { flag: 'wx' });
process.exitCode = out.passed ? 0 : 1;
