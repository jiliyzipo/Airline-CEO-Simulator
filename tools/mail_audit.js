// 审计所有游戏脚本里 new InboxMail 初始化块：
// 找出「没有 action（或 action 为空串）」且「status 缺省=pending」的邮件 —— 这类邮件在收件箱里
// 既不需要决策（现在已隐藏按钮），又会被 HasDueMails 判为到期待办，可能造成软锁。
const fs = require("fs");
const path = require("path");

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith(".cs")) out.push(p);
  }
  return out;
}

const files = walk("Unity Project/Assets/Scripts", []);
let count = 0;
const bad = [];
for (const file of files) {
  const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    if (!/new InboxMail/.test(lines[i])) continue;
    let depth = 0, j = i, chunk = [];
    for (; j < lines.length; j++) {
      chunk.push(lines[j]);
      for (const ch of lines[j]) { if (ch === "{") depth++; else if (ch === "}") depth--; }
      if (j > i && depth <= 0) break;
    }
    count++;
    const text = chunk.join("\n");
    const actionLiteral = /action\s*=\s*"([^"]*)"/.exec(text);
    const actionParam = /action\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*[,}]/.exec(text);
    const status = /status\s*=\s*"([^"]*)"/.exec(text);
    const hasAction = (actionLiteral && actionLiteral[1].length > 0) || !!actionParam;
    const st = status ? status[1] : "pending";
    if (!hasAction && (st === "pending" || st === "deferred")) {
      const sender = (/sender\s*=\s*"([^"]*)"/.exec(text) || [])[1] || "?";
      const subject = (/subject\s*=\s*"([^"]*)"/.exec(text) || [])[1] || "?";
      bad.push({ file, line: i + 1, sender, subject, status: st });
    }
  }
}
console.log(`扫描文件 ${files.length} 个，new InboxMail 块 ${count} 个`);
if (bad.length === 0) console.log("未发现「无 action 且待办」的邮件：隐藏按钮不会造成软锁");
else {
  console.log("发现可疑（无 action 却仍待办 → 会被到期待办阻断，需确认是否真无需决策）：");
  for (const b of bad) console.log(`  ${b.file}:${b.line} [${b.sender}] ${b.subject} status=${b.status}`);
}
