// ASCII-only: inspect raw bytes of the target file at key regions.
const fs = require('fs');
const T = 'C:/Users/jiliy/Airline-CEO-Simulator/Unity Project/Assets/Scripts/Systems/InboxManager.cs';
const b = fs.readFileSync(T);
const gbk = new TextDecoder('gbk', { fatal: false });
let fffd = 0;
for (let i = 0; i + 2 < b.length; i++) if (b[i] === 0xEF && b[i + 1] === 0xBF && b[i + 2] === 0xBD) fffd++;
let q = 0;
for (const x of b) if (x === 0x3F) q++;
let lf = 0;
for (const x of b) if (x === 0x0A) lf++;
const lines = [];
const needles = ['case "approved"', 'role = "执行回执"', 'glyph = "战"', 'permitStage = RoutePermitLogic.StageRev'];
// needles with Chinese are not useful since we can't write Chinese here; use byte search on ASCII only.
const asciiNeedles = ['case "approved"', 'region ', 'AddPermitReceipt', 'RejectPermitRequest'];
lines.push('len=' + b.length + ' U+FFFD=' + fffd + ' 0x3F=' + q + ' LF=' + lf);
for (const n of asciiNeedles) {
  const idx = b.indexOf(Buffer.from(n, 'utf8'));
  lines.push('needle ' + JSON.stringify(n) + ' at ' + idx);
  if (idx >= 0) {
    const s = Math.max(0, idx - 40), e = Math.min(b.length, idx + n.length + 40);
    const seg = b.subarray(s, e);
    lines.push('  hex: ' + seg.toString('hex').replace(/(..)/g, '$1 '));
    lines.push('  utf8: ' + JSON.stringify(new TextDecoder('utf8').decode(seg)));
    lines.push('  gbk : ' + JSON.stringify(gbk.decode(seg)));
  }
}
fs.writeFileSync('C:/Users/jiliy/Airline-CEO-Simulator/inspect_bytes.txt', lines.join('\n'), 'utf8');
console.log(lines.join('\n'));
