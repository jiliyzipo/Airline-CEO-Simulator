#!/usr/bin/env node
/**
 * Run one whitelisted regression inside the already-open Unity editor.
 *
 *   node tools/regression/ai-run.mjs --suite scenario-fixture [--timeout 180]
 *
 * Why this exists: `RunUnityTests.ps1` refuses to start while any Unity process is
 * alive, and it only accepts the isolated `AirlineCEORegression` export. So any Unity
 * change used to need the editor closed before it could be self-checked. The editor-side
 * bridge (`Assets/Scripts/Editor/RegressionTriggerBridge.cs`) closes that gap without
 * touching save data or the project lock: this script drops a request file into
 * `out/ai-trigger/`, the bridge runs the named suite on the main thread, and the result
 * comes back as `out/ai-result/<id>.json`.
 *
 * Stale-assembly guard: Unity compiles scripts only when the editor regains focus, even
 * with Asset Pipeline → Auto Refresh **enabled**. Before requesting anything we compare the
 * newest `.cs` mtime against the compiled assemblies; if the source is newer we first send
 * the bridge's `__refresh` pseudo-suite (one `AssetDatabase.Refresh()`), wait for the
 * assemblies to be rewritten, and only then run the suite. Without this a run silently
 * describes the previous build and still reports `passed`.
 *
 * Exit codes: 0 passed · 1 failed/error · 2 no answer (bridge not installed, editor busy
 * in Play mode, OFF file present, or assembly could not be refreshed) · 3 usage.
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const triggerDir = path.join(repo, 'out', 'ai-trigger');
const resultDir = path.join(repo, 'out', 'ai-result');
const assetsDir = path.join(repo, 'Unity Project', 'Assets');
const asmDir = path.join(repo, 'Unity Project', 'Library', 'ScriptAssemblies');

/** 本项目自己的程序集；外部包（UnityEngine.*、nunit 等）不参与新旧比对。 */
const PROJECT_ASSEMBLIES = [
  'AirlineCEO.Runtime.dll',
  'AirlineCEO.Editor.dll',
  'AirlineCEO.EditModeTests.dll',
];

/** 伪 suite：让编辑器做一次 AssetDatabase.Refresh()，替代"手动点一下 Unity 窗口"。 */
const REFRESH_SUITE = '__refresh';

function arg(name, fallback = null) {
  const i = process.argv.indexOf(`--${name}`);
  if (i < 0) return fallback;
  const v = process.argv[i + 1];
  return v && !v.startsWith('--') ? v : true;
}

const suite = arg('suite');
if (!suite || suite === true) {
  console.error('usage: node tools/regression/ai-run.mjs --suite <name> [--timeout 180]');
  process.exit(3);
}
if (suite === REFRESH_SUITE) {
  console.error(`${REFRESH_SUITE} is internal: it is sent automatically when the assembly is stale.`);
  process.exit(3);
}
const timeoutSec = Number(arg('timeout', '180'));
const refreshTimeoutSec = Number(arg('refresh-timeout', '300'));

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const stamp = () => new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
const iso = (ms) => new Date(ms).toISOString();

function newestAssemblyMs() {
  let newest = 0;
  for (const name of PROJECT_ASSEMBLIES) {
    const file = path.join(asmDir, name);
    if (!fs.existsSync(file)) continue;
    newest = Math.max(newest, fs.statSync(file).mtimeMs);
  }
  return newest || null;
}

function newestSource() {
  let best = { ms: 0, file: null };
  const walk = (dir) => {
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      const abs = path.join(dir, e.name);
      if (e.isDirectory()) {
        walk(abs);
      } else if (e.name.endsWith('.cs')) {
        const ms = fs.statSync(abs).mtimeMs;
        if (ms > best.ms) best = { ms, file: path.relative(repo, abs) };
      }
    }
  };
  walk(assetsDir);
  return best;
}

/** @returns {null | {file: string, sourceMs: number, asmMs: number}} */
function staleInfo() {
  const asmMs = newestAssemblyMs();
  // 程序集目录还没生成：桥自己会报"未编译"，这里不抢它的活。
  if (!asmMs) return null;
  const src = newestSource();
  if (!src.file || src.ms <= asmMs) return null;
  return { file: src.file, sourceMs: src.ms, asmMs };
}

/**
 * 发一个请求并等结果。
 * @returns {Promise<{status: string, ok: boolean, result: object|null, log: string|null}>}
 *          status 为 `timeout` 表示没有得到结果。
 */
async function sendRequest(suiteName, timeout, { quietStatus = false } = {}) {
  const id = `AI-${stamp()}-${crypto.randomBytes(3).toString('hex')}`;
  const requestPath = path.join(triggerDir, `${id}.json`);
  const resultPath = path.join(resultDir, `${id}.json`);
  const logPath = path.join(resultDir, `${id}.log`);

  fs.mkdirSync(triggerDir, { recursive: true });
  fs.mkdirSync(resultDir, { recursive: true });
  fs.writeFileSync(
    requestPath,
    JSON.stringify({ id, suite: suiteName, requestedAt: new Date().toISOString() }, null, 2) + '\n',
    'utf8',
  );
  console.log(`request ${id} (${suiteName}) → ${path.relative(repo, requestPath)}`);

  /** The bridge archives the request on pickup; that's the first proof it is alive. */
  let pickedUp = false;
  const deadline = Date.now() + timeout * 1000;

  while (Date.now() < deadline) {
    await sleep(700);
    if (!pickedUp && !fs.existsSync(requestPath)) pickedUp = true;
    if (!fs.existsSync(resultPath)) continue;

    // The bridge writes the .json before the .log; wait for the log so callers can tail it.
    if (!fs.existsSync(logPath)) continue;

    let result;
    try {
      result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
    } catch {
      continue; // still being written
    }

    const log = fs.readFileSync(logPath, 'utf8');
    // 失败的两种写法都要显示：`✗` 是较老的回归风格，`[Error]` 是 Debug.LogError 被桥捕获后的样子。
    // 只认 `✗` 会让新写的回归红了却看不到原因，只能去翻日志文件。
    const failures = log.split(/\r?\n/).filter((l) => l.includes('✗') || l.includes('[Error]'));

    if (!quietStatus) {
      console.log(
        `status=${result.status} ok=${result.ok} durationMs=${result.durationMs} ` +
          `unity=${result.unityVersion} scene=${result.scenePath || '(untitled)'} dirty=${result.sceneDirty}`,
      );
    }
    if (failures.length) {
      console.log('--- failing assertions ---');
      for (const line of failures) console.log(line);
    }
    if (result.error) console.log(`error: ${result.error}`);

    return { status: result.status, ok: result.ok === true, result, log };
  }

  if (!pickedUp) {
    console.error(
      `no answer in ${timeout}s and the request was never picked up.\n` +
        `Most likely the bridge is not compiled into the open editor yet: switch to Unity and let it\n` +
        `finish recompiling (AirlineCEO.Editor), then rerun. Check out/ai-trigger/OFF is absent.`,
    );
  } else {
    console.error(
      `request was picked up but produced no result in ${timeout}s — the editor may have entered\n` +
        `Play mode mid-run, crashed, or the suite is still running. Check the Unity Console.`,
    );
  }
  return { status: 'timeout', ok: false, result: null, log: null };
}

/**
 * 等到程序集比 `sourceMs` 新，也就是编辑器真的把那批改动编译进去了。
 * @returns {Promise<boolean>}
 */
async function waitForFreshAssembly(sourceMs, timeout) {
  const deadline = Date.now() + timeout * 1000;
  let announced = false;
  while (Date.now() < deadline) {
    const asmMs = newestAssemblyMs();
    if (asmMs && asmMs > sourceMs) {
      // 程序集刚写盘时编辑器可能还在收尾（域重载、脚本重新加载），稍等再发请求。
      await sleep(1500);
      return true;
    }
    if (!announced) {
      console.log(`waiting for Unity to recompile (assemblies still older than ${iso(sourceMs)})...`);
      announced = true;
    }
    await sleep(1000);
  }
  return false;
}

async function main() {
  const offFile = path.join(triggerDir, 'OFF');
  if (fs.existsSync(offFile)) {
    console.error(`bridge disabled: ${offFile} exists. Delete it (or use the menu) to re-enable.`);
    return 2;
  }

  const stale = staleInfo();
  if (stale) {
    console.log(
      `stale assembly: ${stale.file} (${iso(stale.sourceMs)}) is newer than the compiled ` +
        `assemblies (${iso(stale.asmMs)}).`,
    );
    console.log('asking the editor to refresh and recompile first (previously this needed a manual click on Unity)...');

    const refreshed = await sendRequest(REFRESH_SUITE, 120, { quietStatus: true });
    if (refreshed.status !== 'refreshed') {
      console.error(
        `refresh request did not succeed (status=${refreshed.status}).\n` +
          `Switch to the Unity window once and let it finish compiling, then rerun.`,
      );
      return 2;
    }

    if (!(await waitForFreshAssembly(stale.sourceMs, refreshTimeoutSec))) {
      console.error(
        `assemblies were still older than ${iso(stale.sourceMs)} after ${refreshTimeoutSec}s.\n` +
          `Unity may be showing a compile error (check its Console) or sitting in Play mode.\n` +
          `Refusing to run: a pass now would describe the previous build, not your edit.`,
      );
      return 2;
    }
    console.log('assemblies refreshed; running the suite on the new build.');
  }

  const outcome = await sendRequest(suite, timeoutSec);
  if (outcome.status === 'passed') return 0;
  if (outcome.status === 'refused') {
    console.error(`refused: ${outcome.result?.error ?? '(no reason given)'}`);
    return 2;
  }
  if (outcome.status === 'timeout') return 2;
  return 1;
}

process.exit(await main());
