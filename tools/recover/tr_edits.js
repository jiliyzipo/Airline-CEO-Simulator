// ASCII-only: extract all InboxManager.cs edits from the transcript.
const fs = require('fs');
const T = 'C:/Users/jiliy/.cursor/projects/c-Users-jiliy-Airline-CEO-Simulator/agent-transcripts/b2b336c9-307a-44d1-8019-851af4182a0d/b2b336c9-307a-44d1-8019-851af4182a0d.jsonl';
const lines = fs.readFileSync(T, 'utf8').split('\n');
const parts = [];
const summary = [];
lines.forEach((l, i) => {
  if (l.indexOf('InboxManager.cs') < 0) return;
  let j;
  try { j = JSON.parse(l); } catch (e) { return; }
  const content = (j.message && j.message.content) || [];
  if (!Array.isArray(content)) return;
  for (const c of content) {
    if (c.type !== 'tool_use') continue;
    const inp = c.input || {};
    const p = String(inp.path || inp.file_path || '');
    if (p.indexOf('InboxManager.cs') < 0) continue;
    const text = inp.contents || inp.new_string || '';
    summary.push('line ' + (i + 1) + ' tool=' + c.name + ' bytes=' + Buffer.byteLength(text));
    if (text) parts.push('=== line ' + (i + 1) + ' tool=' + c.name + ' ===\n' + text);
  }
});
fs.writeFileSync('C:/Users/jiliy/Airline-CEO-Simulator/tr_edits.txt', parts.join('\n'), 'utf8');
fs.writeFileSync('C:/Users/jiliy/Airline-CEO-Simulator/tr_edits_index.txt', summary.join('\n'), 'utf8');
console.log('edits=' + parts.length);
console.log(summary.join('\n'));
