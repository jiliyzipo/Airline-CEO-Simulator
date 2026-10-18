// Single reproducible entry point, including when invoked inside a delivery.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
const tools = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-suite-'));
const suites = [];
for (const file of ['collab.test.mjs', 'xml.test.mjs', 'closure.test.mjs', 'daily.test.mjs', 'reverse-review.test.mjs', 'stage.test.mjs']) {
  const run = spawnSync(process.execPath, [path.join(tools, 'tests', file)], {
    cwd: root, encoding: 'utf8', windowsHide: true, timeout: 120000,
    env: { ...process.env, COLLAB_AUDIT_TOOL_SOURCE: tools, COLLAB_AUDIT_RUN_LABEL: 'suite' }, maxBuffer: 8 * 1024 * 1024,
  });
  fs.writeFileSync(path.join(root, file + '.stdout.txt'), run.stdout || '');
  fs.writeFileSync(path.join(root, file + '.stderr.txt'), run.stderr || '');
  const passed = (run.stdout || '').match(/^PASS\b|^✓|^ok\b/gm)?.length || 0;
  const failed = ((run.stdout || '') + '\n' + (run.stderr || '')).match(/^FAIL\b|^✗|^not ok\b/gm)?.length || 0;
  suites.push({ file, exitCode: run.status ?? 1, passed, failed, spawnError: run.error?.message || null });
}
const ok = suites.every(s => s.exitCode === 0 && s.passed > 0 && s.failed === 0);
const result = { profile: 'collab', assertionsTotal: suites.reduce((n, s) => n + s.passed + s.failed, 0),
  assertionsFailed: suites.reduce((n, s) => n + s.failed + (s.exitCode && !s.failed ? 1 : 0), 0), assertionsSkipped: 0,
  suites, evidenceDirectory: root, ok };
fs.writeFileSync(path.join(root, 'summary.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result));
process.exitCode = ok ? 0 : 1;
