// ASCII-only: replay every InboxManager.cs edit found in the agent transcript, in order,
// to rebuild the exact pre-corruption content. Each StrReplace must find its old_string;
// failures are reported with a JSON-safe context window.
const fs = require('fs');

const T = 'C:/Users/jiliy/.cursor/projects/c-Users-jiliy-Airline-CEO-Simulator/agent-transcripts/b2b336c9-307a-44d1-8019-851af4182a0d/b2b336c9-307a-44d1-8019-851af4182a0d.jsonl';
const OUT = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/replay_out.cs';
const LOG = 'C:/Users/jiliy/Airline-CEO-Simulator/tools/recover/replay_log.txt';

const lines = fs.readFileSync(T, 'utf8').split('\n');
let cur = null;
const log = [];
let step = 0, applied = 0, failed = 0;

const moji = (s) => /[\uFFFD]|杩|閮|鏀|鐨|鍏|鍙|涓|鏂|鐜|鍚|閿/.test(s);
const show = (s, n) => JSON.stringify(String(s).slice(0, n));

for (let i = 0; i < lines.length; i++) {
  const l = lines[i];
  if (l.indexOf('InboxManager.cs') < 0) continue;
  let j;
  try { j = JSON.parse(l); } catch (e) { continue; }
  const content = (j.message && j.message.content) || [];
  if (!Array.isArray(content)) continue;
  for (const c of content) {
    if (c.type !== 'tool_use') continue;
    const inp = c.input || {};
    const p = String(inp.path || inp.file_path || '');
    if (p.indexOf('InboxManager.cs') < 0) continue;
    step++;
    if (c.name === 'Write') {
      cur = inp.contents || '';
      log.push('#' + step + ' L' + (i + 1) + ' Write len=' + cur.length + ' moji=' + moji(cur));
      continue;
    }
    if (c.name === 'StrReplace' || c.name === 'Edit') {
      const oldS = inp.old_string, newS = inp.new_string;
      if (cur == null) { cur = ''; log.push('#' + step + ' L' + (i + 1) + ' StrReplace with NO base content'); }
      const idx = cur.indexOf(oldS);
      if (idx < 0) {
        failed++;
        log.push('#' + step + ' L' + (i + 1) + ' StrReplace FAILED len=' + String(oldS).length +
          ' mojiOld=' + moji(oldS) + ' mojiNew=' + moji(newS) +
          '\n    old=' + show(oldS, 120) + '\n    new=' + show(newS, 120));
      } else {
        applied++;
        const before = cur.length;
        cur = inp.replace_all ? cur.split(oldS).join(newS) : cur.slice(0, idx) + newS + cur.slice(idx + oldS.length);
        log.push('#' + step + ' L' + (i + 1) + ' StrReplace ok ' + before + '->' + cur.length +
          ' mojiOld=' + moji(oldS) + ' mojiNew=' + moji(newS));
      }
    }
  }
}

let valid = true;
try { new TextDecoder('utf-8', { fatal: true }).decode(Buffer.from(cur, 'utf8')); } catch (e) { valid = false; }
const head = 'steps=' + step + ' applied=' + applied + ' failed=' + failed + ' finalLen=' + (cur ? cur.length : 0) +
  ' lines=' + (cur ? cur.split('\n').length : 0) + ' moji=' + moji(cur) +
  ' validUtf8=' + valid;
fs.writeFileSync(LOG, head + '\n' + log.join('\n'), 'utf8');
if (cur) fs.writeFileSync(OUT, cur, 'utf8');
console.log(head);
console.log('--- tail log ---');
console.log(log.slice(-25).join('\n'));
