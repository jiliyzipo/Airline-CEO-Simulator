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
 * Exit codes: 0 passed · 1 failed/error · 2 no answer (bridge not installed, editor busy
 * in Play mode, or OFF file present) · 3 usage.
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const triggerDir = path.join(repo, 'out', 'ai-trigger');
const resultDir = path.join(repo, 'out', 'ai-result');

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
const timeoutSec = Number(arg('timeout', '180'));
const stamp = new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
const id = `AI-${stamp}-${crypto.randomBytes(3).toString('hex')}`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const offFile = path.join(triggerDir, 'OFF');
if (fs.existsSync(offFile)) {
  console.error(`bridge disabled: ${offFile} exists. Delete it (or use the menu) to re-enable.`);
  process.exit(2);
}

fs.mkdirSync(triggerDir, { recursive: true });
fs.mkdirSync(resultDir, { recursive: true });
const requestPath = path.join(triggerDir, `${id}.json`);
fs.writeFileSync(
  requestPath,
  JSON.stringify({ id, suite, requestedAt: new Date().toISOString() }, null, 2) + '\n',
  'utf8',
);
console.log(`request ${id} → ${path.relative(repo, requestPath)}`);

/** The bridge archives the request on pickup; that's the first proof it is alive. */
let pickedUp = false;
const deadline = Date.now() + timeoutSec * 1000;
const resultPath = path.join(resultDir, `${id}.json`);

while (Date.now() < deadline) {
  await sleep(700);
  if (!pickedUp && !fs.existsSync(requestPath)) pickedUp = true;
  if (!fs.existsSync(resultPath)) continue;

  // The bridge writes the .json before the .log; wait for the log so callers can tail it.
  const logPath = path.join(resultDir, `${id}.log`);
  if (!fs.existsSync(logPath)) continue;

  let result;
  try {
    result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
  } catch {
    continue; // still being written
  }

  const log = fs.readFileSync(logPath, 'utf8');
  const failures = log.split(/\r?\n/).filter((l) => l.includes('✗'));
  console.log(
    `status=${result.status} ok=${result.ok} durationMs=${result.durationMs} ` +
      `unity=${result.unityVersion} scene=${result.scenePath || '(untitled)'} dirty=${result.sceneDirty}`,
  );
  if (failures.length) {
    console.log('--- failing assertions ---');
    for (const line of failures) console.log(line);
  }
  if (result.error) console.log(`error: ${result.error}`);

  if (result.status === 'passed') process.exit(0);
  if (result.status === 'refused') {
    console.error(`refused: ${result.error}`);
    process.exit(2);
  }
  process.exit(1);
}

if (!pickedUp) {
  console.error(
    `no answer in ${timeoutSec}s and the request was never picked up.\n` +
      `Most likely the bridge is not compiled into the open editor yet: switch to Unity and let it\n` +
      `finish recompiling (AirlineCEO.Editor), then rerun. Check out/ai-trigger/OFF is absent.`,
  );
} else {
  console.error(
    `request was picked up but produced no result in ${timeoutSec}s — the editor may have entered\n` +
      `Play mode mid-run, crashed, or the suite is still running. Check the Unity Console.`,
  );
}
process.exit(2);
