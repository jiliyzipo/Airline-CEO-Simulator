// Minimal reproduction: two concurrent identical Hook payloads against one fixture repo.
// Usage: node docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/hook-race-repro.mjs
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const repo = process.cwd();
const tools = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || 'tools/collab');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'hook-race-'));
fs.mkdirSync(path.join(root, 'tools/collab'), { recursive: true });
fs.cpSync(tools, path.join(root, 'tools/collab'), { recursive: true });
const cfg = JSON.parse(fs.readFileSync(path.join(tools, 'config.example.json'), 'utf8'));
cfg.demoSnapshotRoots = ['src.cs'];
fs.writeFileSync(path.join(root, 'tools/collab/config.json'), JSON.stringify(cfg, null, 2));

const driver = `
  const { cmdHook } = await import(${JSON.stringify(pathToFileURL(path.join(tools, 'lib/commands.mjs')).href)});
  const { loadConfig } = await import(${JSON.stringify(pathToFileURL(path.join(tools, 'lib/paths.mjs')).href)});
  const cfg = loadConfig(${JSON.stringify(root)});
  const out = cmdHook(cfg, { source: 'cursor', event: 'sessionStart' }, { stdinText: ${JSON.stringify('{"session_id":"race","cwd":"' + root.replace(/\\/g, '\\\\') + '"}')} });
  console.log(JSON.stringify(out));
`;
const driverFile = path.join(root, 'driver.mjs');
fs.writeFileSync(driverFile, driver);

const runOne = (n) => new Promise((resolve) => {
  const p = spawn(process.execPath, [driverFile], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
  let out = '';
  let err = '';
  p.stdout.on('data', (d) => { out += d; });
  p.stderr.on('data', (d) => { err += d; });
  p.on('close', (code) => resolve({ n, code, out: out.trim(), err: err.trim() }));
});

const both = await Promise.all([runOne(1), runOne(2)]);
for (const r of both) {
  console.log(`--- worker ${r.n} exit=${r.code}`);
  console.log(r.out ? `stdout: ${r.out}` : 'stdout: (empty)');
  if (r.err) console.log(`stderr:\n${r.err}`);
}
const stateFile = path.join(root, '.collab-local/hooks/state.json');
console.log('--- state', fs.existsSync(stateFile) ? JSON.stringify(JSON.parse(fs.readFileSync(stateFile, 'utf8')).counters) : 'missing');
console.log(`fixture: ${root}`);
