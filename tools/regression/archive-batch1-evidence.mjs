#!/usr/bin/env node
/**
 * 归档批次一的测试证据：XML / 日志 / SLOTSEQ transcript / 源码哈希 / 前后对照 diff。
 *
 * 为什么需要（2026-11-09 Codex 裁决 4）：上一轮报告引用的快照目录被清理后，
 * 「99 行 transcript 完全一致」只剩报告里的一句话，复查方无法独立比对。
 * 现在每跑一次都落一份带运行目录、带源码哈希的证据，且**不覆盖旧运行**。
 *
 * 用法：
 *   node tools/regression/archive-batch1-evidence.mjs \
 *     --run batch1-r2 \
 *     --artifact post-final=D:\Project\_snapshot_post#Batch1PostFull \
 *     --artifact post-seq=D:\Project\_snapshot_post#Batch1PostSeq \
 *     --artifact pre-seq=D:\Project\_snapshot_pre#Batch1PreSeq \
 *     [--pre D:\Project\_snapshot_pre]
 *
 * 产物（全部只写不改，运行目录一旦存在就报错退出）：
 *   out/analysis/batch1-evidence/<run>/manifest.json
 *   out/analysis/batch1-evidence/<run>/source-hashes.txt
 *   out/analysis/batch1-evidence/<run>/<label>/<RunName>.xml|.log
 *   out/analysis/batch1-evidence/<run>/<label>.slotseq.txt     （抽取 SLOTSEQ| 行）
 *   out/analysis/batch1-evidence/<run>/compare-slotseq.txt     （有 pre-seq + post-seq 时才生成）
 *   out/analysis/batch1-evidence/<run>/pre-rebuild.diff        （给了 --pre 才生成）
 *   docs/handoff/evidence/batch1/<run>/…                       （同一份精简文本，随仓库留档）
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const argv = process.argv.slice(2);
function arg(name, fallback = null) {
  const i = argv.indexOf(`--${name}`);
  if (i < 0) return fallback;
  const v = argv[i + 1];
  if (!v || v.startsWith('--')) throw new Error(`--${name} 缺少值`);
  return v;
}
function argAll(name) {
  const out = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === `--${name}`) {
      const v = argv[i + 1];
      if (!v || v.startsWith('--')) throw new Error(`--${name} 缺少值`);
      out.push(v);
    }
  }
  return out;
}

const run = arg('run');
if (!run) {
  console.error('usage: node tools/regression/archive-batch1-evidence.mjs --run <id> --artifact <label>=<dir>#<runName> [--pre <dir>]');
  process.exit(2);
}

const repoRoot = process.cwd();
const outRoot = path.join(repoRoot, 'out', 'analysis', 'batch1-evidence', run);
const docRoot = path.join(repoRoot, 'docs', 'handoff', 'evidence', 'batch1', run);

if (fs.existsSync(outRoot) || fs.existsSync(docRoot)) {
  console.error(`运行目录已存在，不覆盖旧结果：${outRoot}`);
  console.error('换一个 --run 名字（例如 batch1-r3）再跑。');
  process.exit(1);
}

const sha256 = (buf) => crypto.createHash('sha256').update(buf).digest('hex');
const readJson = (p) => JSON.parse(fs.readFileSync(p, 'utf8'));

function mkdir(p) {
  fs.mkdirSync(p, { recursive: true });
}

function git(args) {
  try {
    return execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8' }).trim();
  } catch (e) {
    return `(git ${args.join(' ')} failed: ${e.message.split('\n')[0]})`;
  }
}

/** 从 JUnit XML 里读总数（不引第三方解析库）。 */
function parseJunit(xml) {
  const grab = (attr) => {
    const m = xml.match(new RegExp(`${attr}="(\\d+)"`));
    return m ? Number(m[1]) : null;
  };
  const cases = [];
  const re = /<test-case\b[^>]*\/>/g;
  let m;
  while ((m = re.exec(xml)) !== null) {
    const tag = m[0];
    const name = (tag.match(/fullname="([^"]*)"/) || [])[1] || (tag.match(/name="([^"]*)"/) || [])[1] || '';
    const res = (tag.match(/result="([^"]*)"/) || [])[1] || '';
    cases.push({ name, result: res });
  }
  return {
    total: grab('total'),
    passed: grab('passed'),
    failed: grab('failed'),
    skipped: grab('skipped'),
    inconclusive: grab('inconclusive'),
    cases,
  };
}

/** 从日志里抽出 SLOTSEQ| 行（去掉 Unity 可能加的前缀）。 */
function extractSlotSeq(logText) {
  const lines = logText.split(/\r?\n/);
  const out = [];
  for (const line of lines) {
    const i = line.indexOf('SLOTSEQ|');
    if (i >= 0) out.push(line.slice(i + 'SLOTSEQ|'.length));
  }
  return out;
}

/** 抽 K19 的那一行诊断，便于人工核对场景前提。 */
function extractK19(logText) {
  return logText
    .split(/\r?\n/)
    .filter((l) => l.includes('K19 时刻槽提案现金跑道') || l.includes('真实信件：请求='))
    .map((l) => l.replace(/^.*?(K19 时刻现金跑道|K19 时刻槽提案现金跑道)/, '$1').trim());
}

const manifest = {
  schemaVersion: 1,
  run,
  createdAt: new Date().toISOString(),
  repo: repoRoot,
  git: {
    head: git(['rev-parse', 'HEAD']),
    describe: git(['describe', '--always', '--dirty']),
    dirtyFiles: git(['status', '--porcelain']).split(/\r?\n/).filter(Boolean).length,
  },
  unityVersion: null,
  artifacts: [],
  preSnapshot: arg('pre'),
};

// 被测源码：改动过的生产文件 + 两个回归入口 + NUnit 入口 + 快照/重建脚本。
const sourceFiles = [
  'Unity Project/Assets/Scripts/Data/CapacityService.cs',
  'Unity Project/Assets/Scripts/Data/AirportCatalog.cs',
  'Unity Project/Assets/Scripts/Editor/CapacityServiceRegression.cs',
  'Unity Project/Assets/Scripts/Editor/SlotSequenceRegression.cs',
  'Unity Project/Assets/Scripts/Editor/SlotCashHintRegression.cs',
  'Unity Project/Assets/Scripts/Editor/RegressionTriggerBridge.cs',
  'Unity Project/Assets/Scripts/Editor/LogicSelfTests.cs',
  'Unity Project/Assets/Tests/EditMode/CapacityServiceEditModeTests.cs',
  'Unity Project/Assets/Tests/EditMode/SlotSequenceEditModeTests.cs',
  'tools/regression/export-snapshot.mjs',
  'tools/regression/reconstruct-pre-batch1.mjs',
  'tools/regression/archive-batch1-evidence.mjs',
  'tools/regression/RunUnityTests.ps1',
];

mkdir(outRoot);
mkdir(docRoot);

const hashLines = ['# 被测源码 SHA-256（归档时刻的工作区快照，未提交状态）', ''];
for (const rel of sourceFiles) {
  const abs = path.join(repoRoot, rel);
  if (!fs.existsSync(abs)) {
    hashLines.push(`MISSING  ${rel}`);
    continue;
  }
  hashLines.push(`${sha256(fs.readFileSync(abs))}  ${rel}`);
}
fs.writeFileSync(path.join(outRoot, 'source-hashes.txt'), hashLines.join('\n') + '\n', 'utf8');

for (const spec of argAll('artifact')) {
  const eq = spec.indexOf('=');
  const hashAt = spec.lastIndexOf('#');
  if (eq < 0 || hashAt < eq) {
    console.error(`--artifact 格式应为 <label>=<snapshotDir>#<runName>，收到：${spec}`);
    process.exit(2);
  }
  const label = spec.slice(0, eq);
  const snapshotDir = spec.slice(eq + 1, hashAt);
  const runName = spec.slice(hashAt + 1);

  const logDir = path.join(snapshotDir, 'Unity Project', 'Logs');
  const candidates = fs.existsSync(logDir) ? fs.readdirSync(logDir) : [];
  const xmlName = candidates.find(
    (f) => f.toLowerCase() === `tasks20261017_${runName}.xml`.toLowerCase(),
  );
  const logName = candidates.find(
    (f) => f.toLowerCase() === `tasks20261017_${runName}.log`.toLowerCase(),
  );
  if (!xmlName) {
    console.error(`[archive] 找不到 ${label} 的 XML：${path.join(logDir, `Tasks20261017_${runName}.xml`)}`);
    process.exit(1);
  }

  const destDir = path.join(outRoot, label);
  mkdir(destDir);
  const xmlBuf = fs.readFileSync(path.join(logDir, xmlName));
  fs.writeFileSync(path.join(destDir, xmlName), xmlBuf);
  const summary = parseJunit(xmlBuf.toString('utf8'));

  let logBuf = null;
  let slotSeqPath = null;
  let slotSeqCount = 0;
  if (logName) {
    logBuf = fs.readFileSync(path.join(logDir, logName));
    fs.writeFileSync(path.join(destDir, logName), logBuf);
    const text = logBuf.toString('utf8');
    const slotSeq = extractSlotSeq(text);
    slotSeqCount = slotSeq.length;
    if (slotSeq.length > 0) {
      slotSeqPath = path.join(outRoot, `${label}.slotseq.txt`);
      fs.writeFileSync(slotSeqPath, slotSeq.join('\n') + '\n', 'utf8');
    }
    const k19 = extractK19(text);
    if (k19.length > 0) {
      fs.writeFileSync(path.join(outRoot, `${label}.k19.txt`), k19.join('\n') + '\n', 'utf8');
    }
  }

  manifest.artifacts.push({    label,
    snapshotDir,
    runName,
    xml: path.relative(repoRoot, path.join(destDir, xmlName)),
    xmlSha256: sha256(xmlBuf),
    log: logName ? path.relative(repoRoot, path.join(destDir, logName)) : null,
    logSha256: logBuf ? sha256(logBuf) : null,
    summary,
    slotSeqLines: slotSeqCount,
    slotSeqPath: slotSeqPath ? path.relative(repoRoot, slotSeqPath) : null,
  });
}

// 前后对照：只做「同一修订版测试 × 重建对照 vs 当前实现」的 transcript 逐行 diff。
// 优先按标签后缀配对（pre-seq ↔ post-seq），避免拿 pre-seq 去和 post-full 比。
function suffixOf(label) {
  const i = label.indexOf('-');
  return i < 0 ? label : label.slice(i + 1);
}
const prePool = manifest.artifacts.filter((a) => a.slotSeqLines > 0 && a.label.startsWith('pre'));
const postPool = manifest.artifacts.filter((a) => a.slotSeqLines > 0 && a.label.startsWith('post'));
let preArt = null;
let postArt = null;
for (const p of prePool) {
  const match = postPool.find((q) => suffixOf(q.label) === suffixOf(p.label));
  if (match) {
    preArt = p;
    postArt = match;
    break;
  }
}
if (!preArt) {
  preArt = prePool[0] ?? null;
  postArt = postPool[0] ?? null;
}
if (preArt && postArt) {
  const list = (relPath) => fs.readFileSync(path.join(repoRoot, relPath), 'utf8').split(/\r?\n/).filter((l) => l.length);
  const a = list(preArt.slotSeqPath);
  const b = list(postArt.slotSeqPath);
  const cmp = [];
  cmp.push(`# SLOTSEQ 前后对照`);
  cmp.push(`# pre : ${preArt.label} (${preArt.slotSeqLines} 行)`);
  cmp.push(`# post: ${postArt.label} (${postArt.slotSeqLines} 行)`);
  cmp.push('');
  const n = Math.max(a.length, b.length);
  let diffs = 0;
  for (let i = 0; i < n; i++) {
    const la = a[i] ?? '<missing>';
    const lb = b[i] ?? '<missing>';
    if (la !== lb) {
      diffs++;
      cmp.push(`-L${i + 1} pre : ${la}`);
      cmp.push(`+L${i + 1} post: ${lb}`);
    }
  }
  cmp.push('');
  cmp.push(diffs === 0 ? `RESULT: identical（${a.length} 行逐行相同）` : `RESULT: ${diffs} 行不同`);
  fs.writeFileSync(path.join(outRoot, 'compare-slotseq.txt'), cmp.join('\n') + '\n', 'utf8');
  manifest.slotSeqCompare = {
    pre: preArt.label,
    post: postArt.label,
    preLines: a.length,
    postLines: b.length,
    diffLines: diffs,
    identical: diffs === 0,
  };
}

// 重建对照的 diff：把 pre 快照的 AirportCatalog.cs 与当前工作区版本做统一 diff。
const preDir = manifest.preSnapshot;
if (preDir) {
  const preFile = path.join(preDir, 'Unity Project', 'Assets', 'Scripts', 'Data', 'AirportCatalog.cs');
  const curFile = path.join(repoRoot, 'Unity Project', 'Assets', 'Scripts', 'Data', 'AirportCatalog.cs');
  if (fs.existsSync(preFile) && fs.existsSync(curFile)) {
    let diff = '';
    try {
      diff = execFileSync(
        'git',
        ['diff', '--no-index', '--no-color', '--src-prefix=a/pre/', '--dst-prefix=b/current/', '--', preFile, curFile],
        { cwd: repoRoot, encoding: 'utf8' },
      );
    } catch (e) {
      // git diff 有差异时退出码为 1，stdout 仍在。
      diff = e.stdout ? e.stdout.toString() : '';
    }
    fs.writeFileSync(path.join(outRoot, 'pre-rebuild.diff'), diff, 'utf8');
    manifest.preRebuildDiff = 'pre-rebuild.diff';
    const note = path.join(preDir, 'PRE-REBUILD.md');
    if (fs.existsSync(note)) {
      fs.copyFileSync(note, path.join(outRoot, 'PRE-REBUILD.md'));
      manifest.preRebuildNote = 'PRE-REBUILD.md';
    }
  }
}

fs.writeFileSync(path.join(outRoot, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');

// 精简文本副本：随仓库留档（out/ 是被 gitignore 的生成目录）。
const slim = [
  'manifest.json',
  'source-hashes.txt',
  'compare-slotseq.txt',
  'pre-rebuild.diff',
  'PRE-REBUILD.md',
];
mkdir(docRoot);
for (const name of slim) {
  const src = path.join(outRoot, name);
  if (fs.existsSync(src)) fs.copyFileSync(src, path.join(docRoot, name));
}
for (const a of manifest.artifacts) {
  if (a.slotSeqPath) fs.copyFileSync(path.join(repoRoot, a.slotSeqPath), path.join(docRoot, path.basename(a.slotSeqPath)));
  const k19 = path.join(outRoot, `${a.label}.k19.txt`);
  if (fs.existsSync(k19)) fs.copyFileSync(k19, path.join(docRoot, path.basename(k19)));
  // XML / 完整日志体积可能很大，只把 XML 一起留档（便于复核计数）。
  const xmlAbs = path.join(repoRoot, a.xml);
  if (fs.existsSync(xmlAbs)) fs.copyFileSync(xmlAbs, path.join(docRoot, path.basename(a.xml)));
}
fs.writeFileSync(
  path.join(docRoot, 'SUMMARY.md'),
  [
    `# 批次一证据 · ${run}`,
    '',
    `- 归档时间：${manifest.createdAt}`,
    `- git：${manifest.git.head} (${manifest.git.describe})，脏文件 ${manifest.git.dirtyFiles} 个`,
    ...manifest.artifacts.map(
      (a) =>
        `- ${a.label}（${a.runName}）：total=${a.summary.total} passed=${a.summary.passed} failed=${a.summary.failed} skipped=${a.summary.skipped}`,
    ),
    manifest.slotSeqCompare
      ? `- SLOTSEQ 对照：${manifest.slotSeqCompare.pre} vs ${manifest.slotSeqCompare.post} → ${
          manifest.slotSeqCompare.identical ? 'identical' : `${manifest.slotSeqCompare.diffLines} 行不同`
        }（${manifest.slotSeqCompare.preLines}/${manifest.slotSeqCompare.postLines} 行）`
      : '- SLOTSEQ 对照：本次未生成（缺 pre 或 post）',
    '',
    '完整产物（含完整日志）在 `out/analysis/batch1-evidence/' + run + '/`。',
  ].join('\n') + '\n',
  'utf8',
);

console.log(`evidence ok: ${path.relative(repoRoot, outRoot)}`);
console.log(`  artifacts: ${manifest.artifacts.length}`);
if (manifest.slotSeqCompare) {
  console.log(
    `  slotseq: ${manifest.slotSeqCompare.identical ? 'identical' : 'DIFF'} (${manifest.slotSeqCompare.preLines} vs ${manifest.slotSeqCompare.postLines})`,
  );
}
