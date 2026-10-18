import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url)), repo = path.resolve(here, '../../../..');
const deliveryId = 'DEL-20261018071756-010504';
const d = JSON.parse(fs.readFileSync(path.join(repo, 'docs/collaboration/deliveries', deliveryId + '.json')));
const grant = {
  schemaVersion: 1, grantedBy: 'user', actor: 'cursor', developerActor: 'codex', projectId: 'Airline-CEO-Simulator',
  taskId: d.taskId, deliveryId,
  actions: ['review-start', 'review-run', 'review-evidence', 'review-finish', 'review-cancel'], profiles: ['collab'],
  subjectFingerprints: Object.fromEntries(['implementation', 'verification', 'contract'].map(p => [p, d.fingerprints[p].fingerprint])),
  statement: '在用户授权 Codex 完成协调工具全部开发、并要求处理 Cursor 反馈的范围内，准备本次限定的 Cursor 反向独立复验声明。它只允许 Cursor 检测此指定的 Codex 工具交付，不表示已复验或已通过；不允许冒用身份或修改游戏验收结论。',
  authorizationBasis: ['用户原话：授权你完成协调工具的全部开发工作', '用户确认：已经让Cursor保持现状。Codex 接手修复后，准备可供 Cursor 真实身份复验的交付。'],
  constraints: ['不得改写原有 DEL/REV/ISSUE/event 历史', '不启动 Unity、不修改游戏业务与游戏任务书',
    '不 commit/push、不改全局客户端设置', '不授权 Cursor 缺陷 verified/closed 或任务书勾选、全局 recover',
    '不是操作系统身份凭据，执行者必须如实使用自身 actor'],
};
const target = path.join(repo, 'docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json');
fs.writeFileSync(target, JSON.stringify(grant, null, 2) + '\n', { flag: 'wx' });
const copied = [];
for (const [name, source] of [
  ['initial-sandbox-0-of-14', 'C:/Users/jiliy/AppData/Local/Packages/sandbox.{df11a8b1-ef2e-48a2-92b8-f00cc56646f8}/AC/Temp/collab-reverse-uCD1s1/results.json'],
  ['initial-fixture-11-of-14', 'C:/Users/jiliy/AppData/Local/Temp/collab-reverse-ztihz7/results.json'],
  ['fixture-13-of-14', 'C:/Users/jiliy/AppData/Local/Temp/collab-reverse-6zF4Ck/results.json'],
  ['targeted-14-of-14', 'C:/Users/jiliy/AppData/Local/Temp/collab-reverse-nnZcHm/results.json'],
]) {
  if (!fs.existsSync(source)) continue;
  const dest = path.join(here, name + '.json'); fs.copyFileSync(source, dest, fs.constants.COPYFILE_EXCL); copied.push(name);
}
console.log(JSON.stringify({ target, deliveryId, grantSha256: crypto.createHash('sha256').update(fs.readFileSync(target)).digest('hex'), copied }));
