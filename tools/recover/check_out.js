// 校验恢复结果：字符集、括号配平、可疑模式
const fs = require('fs');
const path = process.argv[2] || 'tools/recover/merged_final2.cs';
const d = fs.readFileSync(path, 'utf8');
const lines = d.split('\n');

function count(re) { return (d.match(re) || []).length; }

console.log('file =', path);
console.log('bytes =', fs.statSync(path).size, 'lines =', lines.length);
console.log('latin1Letters =', count(/[\u00c0-\u00ff]/g));
console.log('replacementChar =', count(/\uFFFD/g));
console.log('u3016u3017 =', count(/[\u3016\u3017]/g));
console.log('braceDelta =', count(/\{/g) - count(/\}/g));
console.log('parenDelta =', count(/\(/g) - count(/\)/g));
console.log('bracketDelta =', count(/\[/g) - count(/\]/g));

// 引号奇偶（粗略：忽略转义与注释里的撇号会误报，仅作参考）
console.log('doubleQuotes =', count(/"/g));

// 可疑：中文注释附近出现半角问号 / 孤立的英文引号
const suspicious = [];
const pats = [
  ['cmt-?', /\/\/[^\n]*[\u4e00-\u9fff][^\n]*[^\w]?\?(?![^\n]*(\?|:))/],
  ['odd-symbol', /[\u2260\u2265\u2264]/],
  ['stray-quote-end', /[\u4e00-\u9fff]"\s*$/],
];
for (const [name, re] of pats) {
  const hits = [];
  lines.forEach((l, i) => { if (re.test(l)) hits.push((i + 1) + ': ' + l.trim().slice(0, 130)); });
  if (hits.length) { suspicious.push('### ' + name + ' (' + hits.length + ')'); suspicious.push(hits.slice(0, 30).join('\n')); }
}
fs.writeFileSync('tools/recover/check_out.txt', suspicious.join('\n') || '(no suspicious patterns)', 'utf8');
console.log('suspicious blocks =', suspicious.length);
