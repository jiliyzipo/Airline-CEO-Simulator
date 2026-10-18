// Stdin bridge for optional project hooks. Never ack, deliver or start tests.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const source = process.argv[2], event = process.argv[3];
try {
  if (!['cursor', 'codex'].includes(source) || !event) throw Error('expected cursor|codex and event');
  const raw = fs.readFileSync(0, 'utf8').replace(/^\uFEFF/, '');
  const payload = JSON.parse(raw || '{}');
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) throw Error('payload must be an object');
  const command = payload.command || payload.tool_input?.command || payload.tool_input?.cmd || '';
  if (typeof command === 'string' && /tools[\\/]collab[\\/]|Collab\.ps1/.test(command)) {
    console.log('{}'); process.exit(0);
  }
  const file = payload.file_path || payload.file || payload.path;
  if (typeof file === 'string') {
    const abs = path.resolve(root, file), rel = path.relative(root, abs);
    if (rel.startsWith('..') || path.isAbsolute(rel)) throw Error('event path is outside project');
    payload.fileVersion = fs.existsSync(abs) && fs.statSync(abs).isFile()
      ? crypto.createHash('sha256').update(fs.readFileSync(abs)).digest('hex') : 'missing';
  }
  let result;
  for (let attempt = 0; attempt < 4; attempt++) {
    result = spawnSync(process.execPath, [path.join(root, 'tools/collab/cli.mjs'), 'hook',
      '--source', source, '--event', event, '--json'], { cwd: root, input: JSON.stringify(payload), encoding: 'utf8', windowsHide: true });
    if (result.status !== 4) break;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 200 * (attempt + 1));
  }
  if (result.status !== 0) {
    console.error(`collab hook not applied (exit ${result.status}); run Collab.ps1 sync manually.`);
  }
  // CLI JSON is recorded locally; it is not a native hook response schema.
  console.log('{}');
} catch (e) {
  console.error(`collab hook: ${e.message}; use manual sync.`);
  console.log('{}'); process.exitCode = 2;
}
