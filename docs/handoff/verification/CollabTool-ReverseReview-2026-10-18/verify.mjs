// Explicit subject, append-only output directories; test fixtures never touch live game data.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), repo = path.resolve(here, '../../../..');
const args = process.argv.slice(2), arg = (key, fallback) => { const i = args.indexOf('--' + key); return i < 0 ? fallback : args[i + 1]; };
const subject = path.resolve(repo, arg('subject', 'tools/collab'));
const label = arg('label', new Date().toISOString().replace(/[:.]/g, '-'));
if (!/^[A-Za-z0-9_.-]+$/.test(label)) throw Error('label must be one path component');
const out = path.join(here, label); fs.mkdirSync(out, { recursive: false });
const env = { ...process.env, COLLAB_AUDIT_TOOL_SOURCE: subject, COLLAB_AUDIT_RUN_LABEL: 'reverse-' + label };
const entries = [];
for (const [id, script, extra] of [
  ['all', path.join(subject, 'tests/run-all.mjs'), []],
  ['external', 'docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/run-external.mjs', ['--subject', subject, '--label', 'reverse-' + label]],
  ['xml-gate', 'docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs', []],
]) {
  const r = spawnSync(process.execPath, [script, ...extra], { cwd: repo, env, encoding: 'utf8', windowsHide: true,
    timeout: 240000, maxBuffer: 16 * 1024 * 1024 });
  for (const [name, text] of [['stdout', r.stdout || ''], ['stderr', r.stderr || '']]) fs.writeFileSync(path.join(out, id + '.' + name + '.txt'), text);
  const entry = { id, exitCode: r.status ?? 1, error: r.error?.message || null,
    stdoutSha256: crypto.createHash('sha256').update(r.stdout || '').digest('hex') };
  if (id === 'all') {
    try {
      const summary = JSON.parse(r.stdout); entry.counts = summary;
      fs.cpSync(summary.evidenceDirectory, path.join(out, 'suite-logs'), { recursive: true, filter: p =>
        fs.statSync(p).isDirectory() || /\.(?:txt|json)$/.test(p) });
    } catch { /* Invalid/missing JSON remains a failing suite. */ }
  }
  if (id === 'external') {
    const evidence = path.join(repo, 'docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/evidence', 'reverse-' + label);
    if (fs.existsSync(evidence)) {
      fs.cpSync(evidence, path.join(out, 'external'), { recursive: true });
      entry.summary = JSON.parse(fs.readFileSync(path.join(evidence, 'summary.json')));
    }
  }
  if (id === 'xml-gate') {
    const result = (r.stdout || '').match(/RESULT (.+results\.json)/);
    if (result && fs.existsSync(result[1])) fs.copyFileSync(result[1], path.join(out, 'xml-gate.results.json'));
  }
  entries.push(entry); console.log(`${entry.exitCode === 0 ? 'PASS' : 'FAIL'} ${id} exit=${entry.exitCode}`);
}
const summary = { at: new Date().toISOString(), subject, label, entries, allPassed: entries.every(e => e.exitCode === 0) };
fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ out, allPassed: summary.allPassed })); process.exitCode = summary.allPassed ? 0 : 1;
