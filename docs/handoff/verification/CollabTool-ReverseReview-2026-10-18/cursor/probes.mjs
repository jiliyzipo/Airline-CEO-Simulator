// Cursor 独立探针：不复制交付方套件，另行构造最小夹具直接调用交付快照的模块。
// 用法：node docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/probes.mjs
//        [COLLAB_AUDIT_TOOL_SOURCE=<snapshot tools/collab>]
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync, spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const repo = process.cwd();
const tools = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || 'tools/collab');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'cursor-probe-'));
const results = [];
const record = (id, requirement, passed, detail) => {
  results.push({ id, requirement, passed, detail });
  console.log(`${passed ? 'PASS' : 'FAIL'} ${id}: ${requirement}${passed ? '' : ` :: ${JSON.stringify(detail).slice(0, 400)}`}`);
};

const cmds = await import(pathToFileURL(path.join(tools, 'lib/commands.mjs')).href);
const { loadConfig } = await import(pathToFileURL(path.join(tools, 'lib/paths.mjs')).href);

function fixture(name, mutate, { skipLoad = false } = {}) {
  const root = path.join(work, name);
  fs.mkdirSync(path.join(root, 'tools/collab'), { recursive: true });
  fs.cpSync(tools, path.join(root, 'tools/collab'), { recursive: true });
  fs.mkdirSync(path.join(root, 'docs/collaboration'), { recursive: true });
  fs.writeFileSync(path.join(root, 'docs/collaboration/PROTOCOL.md'), '# probe boundary\n');
  fs.writeFileSync(path.join(root, 'src.cs'), 'A');
  const cfgPath = path.join(root, 'tools/collab/config.json');
  const cfg = JSON.parse(fs.readFileSync(path.join(root, 'tools/collab/config.example.json'), 'utf8'));
  cfg.demoSnapshotRoots = ['src.cs'];
  if (mutate) mutate(cfg);
  fs.writeFileSync(cfgPath, JSON.stringify(cfg, null, 2));
  const loaded = skipLoad ? null : loadConfig(root);
  return { root, cfg: loaded, cfgPath };
}

// P1 (C01/C02): 未知命令与非法配置必须非零退出，不能静默成功。
{
  const unknown = spawnSync(process.execPath, [path.join(tools, 'cli.mjs'), 'definitely-not-a-command'], { encoding: 'utf8' });
  const badRoot = fixture('bad-config', (c) => { c.schemaVersion = 99; }, { skipLoad: true });
  const bad = spawnSync(process.execPath, [path.join(badRoot.root, 'tools/collab/cli.mjs'), 'status', '--json'], { encoding: 'utf8', cwd: badRoot.root });
  record('P1', 'unknown command and invalid config both exit non-zero', unknown.status === 2 && bad.status === 2, { unknown: unknown.status, badConfig: bad.status });
}

// P2 (C03): 本机 config.json 不进入实现指纹；共享示例仍然计入。
{
  const f = fixture('fp-config');
  const fpMod = await import(pathToFileURL(path.join(tools, 'lib/fingerprint.mjs')).href);
  const before = fpMod.hashManifest(fpMod.collectEntries(f.root, ['tools/collab']));
  const cfgNow = JSON.parse(fs.readFileSync(f.cfgPath, 'utf8'));
  cfgNow.demoSnapshotRoots = ['src.cs', 'docs/collaboration/PROTOCOL.md'];
  fs.writeFileSync(f.cfgPath, JSON.stringify(cfgNow, null, 2));
  const after = fpMod.hashManifest(fpMod.collectEntries(f.root, ['tools/collab']));
  record('P2', 'editing local config.json does not change the implementation fingerprint', before === after, { before, after });
}

// P3 (C09): 没有条款基线时不得宣布完成。
{
  const f = fixture('no-baseline');
  cmds.cmdInit(f.cfg, {});
  let code = null;
  let message = null;
  try {
    cmds.cmdTaskBookCheck(f.cfg, { task: 'T01', actor: 'codex' });
  } catch (e) {
    code = e.exitCode ?? null;
    message = e.message;
  }
  const recorded = !fs.existsSync(path.join(f.root, '.collab-local/taskbook/baseline.json'));
  record('P3', 'taskbook-check refuses to complete without a recorded baseline', code !== 0 && recorded, { code, message, baselineAbsent: recorded });
}

// P4 (C10): 同一身份的两进程同时投递同一 Hook，只应用一次。
{
  const f = fixture('hook-race');
  cmds.cmdInit(f.cfg, {});
  const payload = JSON.stringify({ session_id: 'probe-session', cwd: f.root });
  const driver = `
    const { cmdHook } = await import(${JSON.stringify(pathToFileURL(path.join(tools, 'lib/commands.mjs')).href)});
    const { loadConfig } = await import(${JSON.stringify(pathToFileURL(path.join(tools, 'lib/paths.mjs')).href)});
    const cfg = loadConfig(${JSON.stringify(f.root)});
    try {
      const out = cmdHook(cfg, { source: 'cursor', event: 'sessionStart' }, { stdinText: ${JSON.stringify(payload)} });
      console.log(JSON.stringify(out));
    } catch (e) {
      console.log(JSON.stringify({ error: e.message, exitCode: e.exitCode }));
      process.exitCode = e.exitCode || 1;
    }
  `;
  const driverFile = path.join(f.root, 'driver.mjs');
  fs.writeFileSync(driverFile, driver);
  const runOne = () => new Promise((resolve) => {
    const p = spawn(process.execPath, [driverFile], { cwd: f.root, stdio: ['ignore', 'pipe', 'pipe'] });
    let out = '';
    let err = '';
    p.stdout.on('data', (d) => { out += d; });
    p.stderr.on('data', (d) => { err += d; });
    p.on('close', (code) => resolve({ code, out, err }));
  });
  const both = await Promise.all([runOne(), runOne()]);
  const status = JSON.parse(fs.readFileSync(path.join(f.root, '.collab-local/hooks/state.json'), 'utf8'));
  const applied = status.counters?.applied ?? 0;
  const deduped = status.counters?.deduped ?? 0;
  // 合同：同一事件绝不重复应用；竞争失败的一方允许返回可重试的锁定(4)，重试后必须去重。
  const retry = cmds.cmdHook(f.cfg, { source: 'cursor', event: 'sessionStart' }, { stdinText: payload });
  const status2 = JSON.parse(fs.readFileSync(path.join(f.root, '.collab-local/hooks/state.json'), 'utf8'));
  record('P4', 'concurrent identical Hooks never apply twice; the loser is retryable and the retry dedupes',
    applied === 1 &&
      both.filter((r) => r.code === 0).length >= 1 &&
      both.every((r) => [0, 4].includes(r.code)) &&
      retry.deduped === true &&
      (status2.counters?.applied ?? 0) === 1,
    { applied, deduped, codes: both.map((r) => r.code), retryDeduped: retry.deduped, after: status2.counters, stderr: both.map((r) => r.err.trim().split('\n').slice(0, 3).join(' | ')) });
}

// P5 (C06/C08): 交付后改动实现，旧通过复核必须转为待复验且结论不被抹掉。
{
  const f = fixture('stale');
  f.cfg.profiles = { smoke: { kind: 'node', command: [process.execPath, '-e', 'console.log(JSON.stringify({assertionsTotal:2,assertionsFailed:0,assertionsSkipped:0}))'], resultKind: 'json' } };
  fs.writeFileSync(f.cfgPath, JSON.stringify({ ...f.cfg, _repoRoot: undefined, _configPath: undefined, _usingExample: undefined }, null, 2));
  const cfg = loadConfig(f.root);
  cmds.cmdInit(cfg, {});
  const d = cmds.cmdDeliver(cfg, { actor: 'cursor', task: 'C-DEMO', summary: 'probe stale', profiles: 'smoke' });
  const r = cmds.cmdReviewStart(cfg, { actor: 'codex', delivery: d.deliveryId, profile: 'smoke' });
  cmds.cmdReviewRun(cfg, { actor: 'codex', review: r.reviewId });
  const ev = path.join(f.root, 'ev.json');
  fs.writeFileSync(ev, JSON.stringify({
    conclusion: 'passed',
    subjectImplementationFingerprint: JSON.parse(fs.readFileSync(path.join(f.root, `docs/collaboration/deliveries/${d.deliveryId}.json`), 'utf8')).fingerprints.implementation.fingerprint,
    assertionsTotal: 2, assertionsFailed: 0, assertionsSkipped: 0,
    coverage: ['AC1', 'AC2', 'AC3'].map((id) => ({ id, result: 'passed', evidence: 'probe' })),
  }));
  cmds.cmdReviewFinish(cfg, { actor: 'codex', review: r.reviewId, evidence: ev });
  const before = JSON.parse(fs.readFileSync(path.join(f.root, 'docs/collaboration/tasks/C-DEMO.json'), 'utf8'));
  fs.writeFileSync(path.join(f.root, 'src.cs'), 'B');
  cmds.cmdSync(cfg, {});
  const after = JSON.parse(fs.readFileSync(path.join(f.root, 'docs/collaboration/tasks/C-DEMO.json'), 'utf8'));
  record('P5', 'implementation change flips freshness to stale but keeps the historical passed verdict',
    before.reviewStatus === 'passed' && before.freshness === 'current' && after.freshness === 'stale' && after.reviewStatus === 'passed',
    { before: { s: before.reviewStatus, f: before.freshness }, after: { s: after.reviewStatus, f: after.freshness } });
}

// P6 (C12/C10): 未授权的 Cursor 与跨作者操作都必须被拒绝。
{
  const f = fixture('roles');
  cmds.cmdInit(f.cfg, {});
  const d = cmds.cmdDeliver(f.cfg, { actor: 'cursor', task: 'C-DEMO', summary: 'probe roles', profiles: 'smoke' });
  let code = null;
  try {
    cmds.cmdReviewStart(f.cfg, { actor: 'cursor', delivery: d.deliveryId, profile: 'smoke' });
  } catch (e) {
    code = e.exitCode ?? null;
  }
  let codexAsCursor = null;
  try {
    cmds.cmdReviewStart(f.cfg, { actor: 'cursor', delivery: d.deliveryId, profile: 'smoke', authorization: path.join(repo, 'docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json') });
    codexAsCursor = 'accepted';
  } catch (e) {
    codexAsCursor = e.exitCode ?? 'threw';
  }
  record('P6', 'unauthorised Cursor is refused, and a grant for another delivery cannot be borrowed',
    code === 7 && codexAsCursor !== 'accepted', { unauthorised: code, borrowedGrant: codexAsCursor });
}

const summary = {
  at: new Date().toISOString(),
  subject: path.relative(repo, tools).split(path.sep).join('/'),
  work,
  total: results.length,
  passed: results.filter((r) => r.passed).length,
  failed: results.filter((r) => !r.passed).length,
  results,
};
const outDir = path.join(repo, 'docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor');
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(path.join(outDir, `probes-${new Date().toISOString().replace(/[:.]/g, '-')}.json`), JSON.stringify(summary, null, 2));
console.log(`SUMMARY ${summary.passed}/${summary.total} cursor probes passed; ${summary.failed} failed`);
process.exitCode = summary.failed ? 1 : 0;
