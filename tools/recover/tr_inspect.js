// ASCII-only: inspect transcript lines that contain permitStage / QueueOpenRouteStudy.
const fs = require('fs');
const T = 'C:/Users/jiliy/.cursor/projects/c-Users-jiliy-Airline-CEO-Simulator/agent-transcripts/b2b336c9-307a-44d1-8019-851af4182a0d/b2b336c9-307a-44d1-8019-851af4182a0d.jsonl';
const lines = fs.readFileSync(T, 'utf8').split('\n');
const out = [];
lines.forEach((l, i) => {
  if (l.indexOf('permitStage') < 0 && l.indexOf('QueueOpenRouteStudy') < 0) return;
  let j = null;
  try { j = JSON.parse(l); } catch (e) { out.push('line ' + (i + 1) + ' len=' + l.length + ' JSON.parse failed'); return; }
  const keys = Object.keys(j);
  const role = j.role || (j.message && j.message.role) || '';
  const kind = j.type || '';
  out.push('line ' + (i + 1) + ' len=' + l.length + ' role=' + role + ' type=' + kind + ' keys=' + keys.join(','));
  const s = JSON.stringify(j).slice(0, 200);
  out.push('   head: ' + s);
});
fs.writeFileSync('C:/Users/jiliy/Airline-CEO-Simulator/tr_inspect.txt', out.join('\n'), 'utf8');
console.log(out.join('\n').slice(0, 3000));
