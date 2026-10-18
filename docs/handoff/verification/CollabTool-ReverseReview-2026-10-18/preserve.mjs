import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const base = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(base, '../../../..');
const file = path.join(base, 'preservation-before.json');
const digest = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
function list(dir) {
  if (!fs.existsSync(path.join(root, dir))) return [];
  const all = [];
  for (const ent of fs.readdirSync(path.join(root, dir), { withFileTypes: true })) {
    const rel = dir + '/' + ent.name;
    if (ent.isDirectory()) all.push(...list(rel));
    else if (ent.isFile()) all.push(rel);
  }
  return all;
}
if (process.argv[2] === 'before') {
  if (fs.existsSync(file)) throw Error('preservation baseline already exists');
  const paths = [
    ...['Unity Project/Assets', 'Unity Project/Packages', 'Unity Project/ProjectSettings',
      ...['deliveries', 'reviews', 'issues', 'events'].map(n => 'docs/collaboration/' + n)].flatMap(list),
    'docs/Development Manual/Development Task Book/Task book/Airline_CEO_Dev_Task_2026-10-17.md',
    'docs/Development Manual/Development Task Book/Task book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md',
  ];
  const entries = paths.sort().map(p => ({ path: p, sha256: digest(path.join(root, p)) }));
  fs.writeFileSync(file, JSON.stringify({ at: new Date().toISOString(), entries }, null, 2));
  console.log(JSON.stringify({ entries: entries.length, file }));
} else {
  const before = JSON.parse(fs.readFileSync(file));
  const changed = before.entries.filter(e => !fs.existsSync(path.join(root, e.path)) || digest(path.join(root, e.path)) !== e.sha256);
  const out = { at: new Date().toISOString(), total: before.entries.length, changed, ok: changed.length === 0 };
  fs.writeFileSync(path.join(base, 'preservation-after.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out));
  process.exitCode = out.ok ? 0 : 1;
}
