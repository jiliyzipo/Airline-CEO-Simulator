// 轻量 C# 词法检查：忽略注释/字符串/字符字面量后，检查括号与引号配平
const fs = require('fs');
const path = process.argv[2] || 'tools/recover/merged_final2.cs';
const src = fs.readFileSync(path, 'utf8');

let i = 0, line = 1, col = 1;
const stack = [];
const errors = [];
const lineCode = [];       // 每行剥离注释/字面量后的代码
let cur = '';

function push(ch) { cur += ch; }
function newline() { lineCode[line] = cur; cur = ''; line++; col = 1; }

let pendingStart = null;
while (i < src.length) {
  const c = src[i];
  const c2 = src.slice(i, i + 2);

  if (c === '\n') { i++; newline(); continue; }

  if (c2 === '//') {                       // 行注释
    while (i < src.length && src[i] !== '\n') i++;
    continue;
  }
  if (c2 === '/*') {                       // 块注释
    const startLine = line;
    i += 2;
    while (i < src.length && src.slice(i, i + 2) !== '*/') {
      if (src[i] === '\n') { newline(); i++; } else i++;
    }
    if (i >= src.length) errors.push('unterminated block comment from line ' + startLine);
    i += 2;
    continue;
  }
  if (c === '"') {                         // 普通字符串
    const startLine = line;
    i++;
    let closed = false;
    while (i < src.length) {
      if (src[i] === '\\') { i += 2; continue; }
      if (src[i] === '"') { i++; closed = true; break; }
      if (src[i] === '\n') break;
      i++;
    }
    if (!closed) errors.push('unterminated string at line ' + startLine + ': ' + src.slice(src.lastIndexOf('\n', i) + 1, i + 1).trim().slice(0, 100));
    continue;
  }
  if (c === '@' && src[i + 1] === '"') {   // verbatim 字符串
    i += 2;
    while (i < src.length && src[i] !== '"') { if (src[i] === '\n') { newline(); } i++; }
    i++;
    continue;
  }
  if (c === "'") {                         // 字符字面量
    i++;
    while (i < src.length && src[i] !== "'") { if (src[i] === '\\') i++; i++; }
    i++;
    continue;
  }

  if (c === '{' || c === '(' || c === '[') { stack.push({ ch: c, line }); push(c); i++; continue; }
  if (c === '}' || c === ')' || c === ']') {
    const open = { '}': '{', ')': '(', ']': '[' }[c];
    const top = stack.pop();
    if (!top) errors.push('unexpected ' + c + ' at line ' + line);
    else if (top.ch !== open) errors.push('mismatch: ' + top.ch + ' (line ' + top.line + ') closed by ' + c + ' at line ' + line);
    push(c); i++; continue;
  }
  push(c); i++;
}
lineCode[line] = cur;
for (const t of stack) errors.push('unclosed ' + t.ch + ' from line ' + t.line);

console.log('errors =', errors.length);
errors.slice(0, 40).forEach(e => console.log('  ' + e));
fs.writeFileSync('tools/recover/lex_check.txt', errors.join('\n') || '(clean)', 'utf8');
