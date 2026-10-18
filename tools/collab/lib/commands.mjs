import fs from 'node:fs';
import { developerAuthorization, reviewerAuthorization } from './authorization.mjs';
import { startWatch } from './watch.mjs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { collabDirs, localDirs, snapshotRoot } from './paths.mjs';
import {
  FileLock,
  appendJsonRecord,
  ensureDir,
  listJsonRecords,
  newId,
  nowIso,
  readJson,
  writeJsonAtomic,
} from './io.mjs';
import { EXIT, fail } from './exit.mjs';
import {
  assertRoleAction,
  stableContractPayload,
  validateDelivery,
  validateIssue,
  validateReview,
  validateTask,
} from './schemas.mjs';
import {
  classifyChange,
  collectEntries,
  computeFingerprints,
  hashManifest,
  IMPLEMENTATION_ROOTS,
  snapshotRootsFor,
  VERIFICATION_ROOTS,
} from './fingerprint.mjs';
import { exportStableSnapshot } from './snapshot.mjs';
import { collectElements, parseXmlDocument } from './xml.mjs';
import { resolveTaskRoots } from './roots.mjs';
import {
  computeOverallGate,
  computeTaskGate,
  mappingFor,
  recordBaseline,
  taskBookSummary,
  writeTotalCheckbox,
  reconcileManagedCheckboxes,
} from './taskbook.mjs';
import { hookStatus, runHook } from './hooks.mjs';
import {
  generateSummaries,
  loadAck,
  loadState,
  loadTask,
  listIssues,
  listReviews,
  recordEvent,
  refreshFreshness,
  saveAck,
  saveState,
  withReviewLock,
  withStateLock,
} from './state.mjs';

export function parseArgs(argv) {
  const out = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (!next || next.startsWith('--')) out[key] = true;
      else {
        out[key] = next;
        i++;
      }
    } else out._.push(a);
  }
  return out;
}

export function unityRunning() {
  try {
    if (process.platform !== 'win32') return { checked: true, running: false, pids: [] };
    const r = spawnSync(
      'powershell.exe',
      [
        '-NoProfile',
        '-Command',
        "Get-CimInstance Win32_Process -Filter \"Name = 'Unity.exe'\" | Select-Object -ExpandProperty ProcessId",
      ],
      { encoding: 'utf8' },
    );
    if (r.error || (r.status !== 0 && r.status != null)) {
      return {
        checked: false,
        running: null,
        pids: [],
        error: r.error?.message || `powershell exit ${r.status}: ${(r.stderr || '').slice(0, 200)}`,
      };
    }
    const pids = (r.stdout || '')
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter(Boolean);
    return { checked: true, running: pids.length > 0, pids };
  } catch (e) {
    return { checked: false, running: null, pids: [], error: String(e.message || e) };
  }
}

function taskRoots(cfg, taskId) {
  const task = taskId ? readJson(path.join(collabDirs(cfg).tasks, `${taskId}.json`), null) : null;
  const resolved = resolveTaskRoots(cfg, task);
  return {
    implementationRoots: resolved.implementationRoots,
    verificationRoots: resolved.verificationRoots,
    extraFiles: resolved.extraFiles,
    source: resolved.source,
  };
}

/**
 * Directory names whose contents are build/run artifacts, not reviewable source.
 * A test command legitimately emits compiler output (bin/obj), Unity logs
 * (Library/Temp/Logs) and similar under these; they must not be mistaken for a
 * smuggled-in source file, yet a stray `.cs` outside them is still rejected.
 * Mirrors fingerprint.EXCLUDE_DIR_NAMES so snapshots and integrity agree.
 */
const ARTIFACT_DIR_NAMES = new Set([
  'bin',
  'obj',
  'out',
  'Library',
  'Temp',
  'Logs',
  'Builds',
  'Build',
  'UserSettings',
  '.git',
  '.collab-local',
  '.collab-run',
  'node_modules',
]);

/**
 * Unity writes IDE project files next to the project (outside Assets/Packages/
 * ProjectSettings). They are editor-generated, never reviewable source, and must
 * not be mistaken for a smuggled file after a Unity run.
 */
const UNITY_GENERATED_TOPLEVEL_RE = /^Unity Project\/(?:[^/]+\.(?:csproj|sln|slnx)|\.vsconfig)$/;

function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

/**
 * Files inside a snapshot that are not in its manifest and not covered by the
 * artifact/result-file allowances. Used both to verify before/after a run and to
 * refuse a brand-new delivery that was already polluted at export time.
 */
function listUnlistedSnapshotFiles(snapAbs, listedPaths, allowedExtra = []) {
  const listed = listedPaths instanceof Set ? listedPaths : new Set(listedPaths);
  const allowed = new Set(['collab-snapshot-manifest.json', ...allowedExtra]);
  const extra = [];
  const walk = (rel) => {
    const abs = rel ? path.join(snapAbs, rel) : snapAbs;
    for (const child of fs.readdirSync(abs)) {
      const childRel = rel ? `${rel}/${child}` : child;
      const childAbs = path.join(snapAbs, childRel);
      if (fs.statSync(childAbs).isDirectory()) {
        // Never descend into build/run artifact directories: everything under
        // them is generated output, allowed and unbounded in size.
        if (ARTIFACT_DIR_NAMES.has(child)) continue;
        walk(childRel);
      } else {
        const norm = childRel.replace(/\\/g, '/');
        if (UNITY_GENERATED_TOPLEVEL_RE.test(norm)) continue;
        if (!listed.has(norm) && !allowed.has(norm)) extra.push(norm);
      }
    }
  };
  walk('');
  return extra.sort();
}

/**
 * Verify a snapshot is intact and free of smuggled source.
 * opts.resultFileRel: a declared run-output file that may appear unlisted.
 * opts.runOutputRels: literal files the profile's own command writes into the
 * snapshot (e.g. a tool report). They are outputs, not delivery inputs, so they
 * may appear after a run; anything else unlisted is still a hard failure.
 * Listed source files are always hash-checked (detects execution-time edits);
 * unlisted files are rejected unless they live under a build-artifact directory
 * or are the declared result file.
 */
export function verifySnapshotIntegrity(repoRoot, snapshotRel, delivery, opts = {}) {
  const snapAbs = path.join(repoRoot, snapshotRel);
  if (!fs.existsSync(snapAbs)) fail(EXIT.CHECK_FAILED, `snapshot missing: ${snapshotRel}`);
  const manifestPath = path.join(snapAbs, 'collab-snapshot-manifest.json');
  if (!fs.existsSync(manifestPath)) {
    fail(EXIT.CHECK_FAILED, `snapshot manifest missing under ${snapshotRel}`);
  }
  const manifest = readJson(manifestPath);
  const expected = delivery.snapshotManifest?.entries || manifest.entries || [];
  if (!Array.isArray(expected) || expected.length === 0) {
    fail(EXIT.CHECK_FAILED, 'snapshot manifest has no entries; cannot verify integrity');
  }

  // 1) Every listed file must exist and match its post-overlay (as-executed) hash.
  //    This is what catches source mutated *during* a run (TH-04).
  const listed = new Set();
  const settingsRel = 'Unity Project/ProjectSettings/ProjectSettings.asset';
  for (const e of expected) {
    const rel = String(e.path).replace(/\\/g, '/');
    listed.add(rel);
    const file = path.join(snapAbs, rel);
    if (!fs.existsSync(file)) fail(EXIT.CHECK_FAILED, `snapshot file missing: ${e.path}`);
    // Legacy deliveries predate snapshotSha256; for the overlaid settings file fall
    // back to the isolation-identity check below instead of the raw source hash.
    const expectedHash =
      e.snapshotSha256 || (rel === settingsRel ? null : e.sha256);
    if (expectedHash) {
      const actual = sha256File(file);
      if (actual !== expectedHash) {
        fail(EXIT.CHECK_FAILED, `snapshot tampered: ${e.path}`);
      }
    }
  }

  // 2) No extra *source* files beyond the manifest may appear in the snapshot
  //    (a stray .cs would become an implicit compile input). Build artifacts and
  //    the declared result file are permitted so a second review can re-run the
  //    same immutable inputs without a manual clean (TH-06).
  const allowedExtra = [];
  if (opts.resultFileRel) allowedExtra.push(String(opts.resultFileRel).replace(/\\/g, '/'));
  if (Array.isArray(opts.runOutputRels)) {
    allowedExtra.push(...opts.runOutputRels.map((r) => String(r).replace(/\\/g, '/')));
  }
  const extra = listUnlistedSnapshotFiles(snapAbs, listed, allowedExtra);
  if (extra.length > 0) {
    fail(EXIT.CHECK_FAILED, `snapshot contains unlisted files: ${extra.join(', ')}`);
  }

  // 3) ProjectSettings.asset must keep the isolation overlay (no restore to the
  //    real game identity) when the snapshot manifest recorded one.
  if (listed.has(settingsRel)) {
    const text = fs.readFileSync(path.join(snapAbs, settingsRel), 'utf8');
    const hasCompany = /^\s*companyName:/m.test(text);
    if (hasCompany && !/companyName:\s*AirlineCEORegression/.test(text)) {
      fail(
        EXIT.CHECK_FAILED,
        'snapshot ProjectSettings lost isolation companyName overlay',
      );
    }
  }
  return snapAbs;
}

function judgeCounters(counters, extra = {}) {
  const isNonNegInt = (v) => Number.isInteger(v) && v >= 0;
  if (!isNonNegInt(counters.total) || !isNonNegInt(counters.failed) || !isNonNegInt(counters.skipped)) {
    return { ok: false, reason: 'machine result counters are not non-negative integers', counters };
  }
  if (counters.total === 0) return { ok: false, reason: 'machine result has zero assertions', counters };
  if (counters.failed > 0) {
    return { ok: false, reason: `machine result reports ${counters.failed} failed assertion(s)`, counters };
  }
  if (counters.skipped >= counters.total) {
    return { ok: false, reason: 'machine result: all assertions skipped', counters, hasEvidence: true };
  }
  return { ok: true, counters, parsed: { ...extra, ...counters }, hasEvidence: true };
}

/**
 * Parse NUnit3 result XML: <test-run total= passed= failed= skipped= result=>.
 * Strict (TH-02):
 *  - every required counter attribute must be present, non-empty and a plain
 *    non-negative integer (missing attrs are rejected, never coerced to 0);
 *  - individual <test-case> results are counted and cross-checked against the
 *    root summary — a root claiming Passed while any case Failed is rejected;
 *  - counters must be internally consistent (passed+failed+skipped <= total).
 * Returns counters, or null when the document is not a trustworthy NUnit result.
 */
/**
 * Parse an NUnit result document. The bytes must form ONE complete, well-formed
 * XML document — a truncated file that merely opens with `<test-run ...>` is a
 * broken run, not a passing one. Returns { counters } or { reason }.
 */
export function parseNunitResult(xmlText) {
  const doc = parseXmlDocument(xmlText);
  if (!doc.ok) {
    return { reason: `result file is not a well-formed XML document: ${doc.reason} (offset ${doc.at})` };
  }
  const root = doc.root;
  if (!root || root.name.toLowerCase() !== 'test-run') {
    return { reason: `result root element is <${root ? root.name : '?'}>, expected <test-run>` };
  }
  const attr = (name) => {
    const hit = Object.keys(root.attrs).find((k) => k.toLowerCase() === name);
    return hit === undefined ? null : root.attrs[hit];
  };
  const toCount = (v) => {
    if (v === null || v === undefined) return null;
    const raw = String(v).trim();
    if (!/^\d+$/.test(raw)) return null;
    const n = Number(raw);
    return Number.isSafeInteger(n) ? n : null;
  };
  const total = toCount(attr('total'));
  const failed = toCount(attr('failed'));
  const skipped = toCount(attr('skipped'));
  const passed = toCount(attr('passed'));
  // Required attribute missing, empty, non-numeric or overflow: refuse.
  if (total === null || failed === null || skipped === null || passed === null) {
    return { reason: 'test-run is missing a valid passed/failed/skipped/total counter' };
  }
  if (passed + failed + skipped > total) {
    return { reason: 'test-run counters are inconsistent (passed+failed+skipped > total)' };
  }

  // Cross-check the root summary against every individual test case.
  const caseNodes = collectElements(root, 'test-case');
  let failedCases = 0;
  let passedCases = 0;
  for (const node of caseNodes) {
    const hit = Object.keys(node.attrs).find((k) => k.toLowerCase() === 'result');
    const res = String(hit === undefined ? '' : node.attrs[hit]).trim().toLowerCase();
    if (res === 'failed') failedCases += 1;
    else if (res === 'passed') passedCases += 1;
  }
  if (failedCases > failed || passedCases > passed) {
    return { reason: 'test-run summary conceals failed or passed test cases' };
  }

  const result = String(attr('result') || '').trim().toLowerCase();
  if (result !== 'passed' || failed > 0 || failedCases > 0) {
    return { counters: { total, failed: Math.max(failed, failedCases, 1), skipped, passed } };
  }
  if (total === 0) return { reason: 'test-run reports zero tests' };
  if (passed === 0) return { reason: 'test-run reports zero passed tests' };
  return { counters: { total, failed, skipped, passed } };
}

/** Backwards-compatible wrapper: counters or null when the document is unusable. */
export function parseNunitCounters(xmlText) {
  if (typeof xmlText !== 'string') return null;
  const parsed = parseNunitResult(xmlText);
  return parsed.counters || null;
}

/** Extract {total,failed,skipped} from a machine-result JSON object, or null. */
export function countersFromJsonObject(obj) {
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return null;
  const hasCounters = ['assertionsTotal', 'assertionsFailed', 'assertionsSkipped']
    .some((k) => obj[k] !== undefined);
  if (!hasCounters) return null;
  const total = obj.assertionsTotal;
  const failed = obj.assertionsFailed;
  const skipped = obj.assertionsSkipped;
  const isNonNegInt = (v) => Number.isInteger(v) && v >= 0;
  if (!isNonNegInt(total) || !isNonNegInt(failed) || !isNonNegInt(skipped)) return null;
  return { total, failed, skipped };
}

function findJsonObjectInText(text) {
  try {
    const whole = JSON.parse(text);
    if (whole && typeof whole === 'object') return whole;
  } catch {
    /* fall through to per-line scanning */
  }
  const lines = text.split(/\r?\n/).filter((l) => l.trim());
  for (let i = lines.length - 1; i >= 0; i--) {
    try {
      const candidate = JSON.parse(lines[i]);
      if (candidate && typeof candidate === 'object') return candidate;
    } catch {
      /* keep scanning */
    }
  }
  return null;
}

/**
 * Adapter: tools/slotcheck prints `  [PASS]/[FAIL] name` lines and a final
 * `SLOTCHECK OK` or `SLOTCHECK FAIL <n>` summary. Counters are derived from the
 * individual check lines and must agree with the summary; anything else is not
 * a result. Returns null when this adapter does not recognize the output.
 */
export function parseSlotcheckText(text, exitCode = 0) {
  if (!/^\s*SLOTCHECK (?:OK|FAIL \d+)\s*$/m.test(text)) return null;
  const passCount = (text.match(/^\s*\[PASS\]/gm) || []).length;
  const failCount = (text.match(/^\s*\[FAIL\]/gm) || []).length;
  const summary = text.match(/SLOTCHECK FAIL (\d+)/);
  if (summary && Number(summary[1]) !== failCount) return null; // inconsistent
  if (!summary && failCount > 0) return null;
  if (passCount + failCount === 0) return null;
  if (exitCode !== 0 && failCount === 0) {
    return {
      ok: false,
      reason: `slotcheck exited ${exitCode} despite reporting no failed check`,
      counters: { total: passCount + failCount, failed: failCount, skipped: 0 },
    };
  }
  return judgeCounters(
    { total: passCount + failCount, failed: failCount, skipped: 0 },
    { adapter: 'slotcheck' },
  );
}

/**
 * Adapter: tools/semcheck prints three gate lines — `errors=N (raw=M)`,
 * `tests-asm: errors=N (raw=M)` and `enc: errors=N`. All three gates must be
 * present; a gate with errors>0 is a failed assertion. Returns null when the
 * output is not recognizable semcheck output.
 */
export function parseSemcheckText(text, exitCode = 0) {
  const g1 = text.match(/^errors=(\d+)/m);
  const g2 = text.match(/^tests-asm: errors=(\d+)/m);
  const g3 = text.match(/^enc: errors=(\d+)/m);
  if (!g1 || !g2 || !g3) return null;
  const errs = [g1, g2, g3].map((m) => Number(m[1]));
  const failedGates = errs.filter((n) => n > 0).length;
  const counters = { total: 3, failed: failedGates, skipped: 0 };
  if (exitCode !== 0 && failedGates === 0) {
    return {
      ok: false,
      reason: `semcheck exited ${exitCode} despite zero parsed errors (infrastructure failure)`,
      counters,
    };
  }
  return judgeCounters(counters, { adapter: 'semcheck' });
}

/** NUnit summary line printed by tools/regression/RunUnityTests.ps1. */
function parseNunitSummaryLine(text) {
  const m = text.match(/NUNIT\s+result=(\w+)\s+passed=(\d+)\s+failed=(\d+)\s+skipped=(\d+)/i);
  if (!m) return null;
  const result = m[1];
  const passed = Number(m[2]);
  const failed = Number(m[3]);
  const skipped = Number(m[4]);
  const counters = { total: passed + failed + skipped, failed, skipped, passed };
  if (result.toLowerCase() !== 'passed') {
    return { ok: false, reason: `NUnit result=${result}`, counters };
  }
  if (passed <= 0) return { ok: false, reason: 'NUnit reports zero passed tests', counters };
  return judgeCounters(counters, { adapter: 'nunit-summary-line', line: m[0] });
}

/**
 * Parse the real machine result of a run. Strict by design (TH-01..TH-03):
 *  - `resultKind` declares the only accepted result format:
 *    'json' | 'nunit' | 'slotcheck' | 'semcheck' | 'text' (tries known adapters);
 *  - a declared `resultFile` MUST exist, MUST have been produced by this very
 *    run (run-record binding: pre/post SHA256), MUST be unchanged at judgement
 *    time, and MUST parse under the declared kind — stdout is never a fallback;
 *  - unknown, malformed, empty or adapter-unrecognized output is not a result:
 *    it can only support blocked/failed, never passed. There is no "unverified
 *    pass" path any more.
 * Returns { ok, parsed, counters, reason }.
 */
export function parseRunResult(profile, run, repoRoot) {
  const kind = profile?.resultKind || (profile?.kind === 'node' ? 'json' : 'text');
  // Verify the recorded bytes *before* parsing anything (FR-02): a file that no
  // longer matches its run binding is not evidence, whatever it now contains.
  const stdoutEvidence = verifyRunEvidence(repoRoot, run, 'stdout');
  if (!stdoutEvidence.ok) {
    return {
      ok: false,
      hasEvidence: true,
      evidenceTampered: stdoutEvidence.bound,
      reason: stdoutEvidence.reason,
    };
  }
  const text = stdoutEvidence.text;
  const exitCode = Number.isInteger(run.exitCode) ? run.exitCode : 0;

  // Declared machine result file: bound to this run, parsed under its declared
  // kind, with no stdout fallback (TH-01/T02, TH-03/T05).
  if (profile?.resultFile) {
    const rel = resolveResultFileRel(profile, run.runToken);
    const binding = run.resultFile;
    if (!binding || binding.path !== rel) {
      return {
        ok: false,
        hasEvidence: true,
        reason: `declared result file is not bound to this run: ${rel} (run predates result binding or profile changed)`,
      };
    }
    if (!binding.producedByRun) {
      return {
        ok: false,
        hasEvidence: true,
        reason: `declared result file was not produced by this run (pre-existing or leftover): ${rel}`,
      };
    }
    const abs = path.resolve(run.cwd || repoRoot, rel);
    if (!fs.existsSync(abs)) {
      return {
        ok: false,
        hasEvidence: true,
        reason: `declared result file disappeared after run: ${rel}`,
      };
    }
    const sha = sha256File(abs);
    if (binding.sha256 !== sha) {
      return {
        ok: false,
        hasEvidence: true,
        reason: `declared result file changed after run: ${rel} (recorded=${binding.sha256} actual=${sha})`,
      };
    }
    const body = fs.readFileSync(abs, 'utf8');
    const fileKind =
      kind === 'text' ? (rel.toLowerCase().endsWith('.xml') ? 'nunit' : 'json') : kind;
    if (fileKind === 'nunit') {
      const parsedXml = parseNunitResult(body);
      if (!parsedXml.counters) {
        return {
          ok: false,
          hasEvidence: true,
          reason: `declared result file is not a usable NUnit XML result: ${rel}: ${parsedXml.reason}`,
        };
      }
      return judgeCounters(parsedXml.counters, { file: rel, sha256: sha });
    }
    let parsed = null;
    try {
      parsed = JSON.parse(body);
    } catch {
      parsed = null;
    }
    const counters = countersFromJsonObject(parsed);
    if (!counters) {
      return {
        ok: false,
        hasEvidence: true,
        reason: `declared result file is not a valid JSON machine result with assertion counters: ${rel}`,
      };
    }
    return judgeCounters(counters, { file: rel, sha256: sha });
  }

  if (text.trim().length === 0) {
    return {
      ok: false,
      hasEvidence: false,
      empty: true,
      reason: 'stdout is empty; no real result produced',
    };
  }

  if (kind === 'json') {
    const parsed = findJsonObjectInText(text);
    if (!parsed) {
      return {
        ok: false,
        hasEvidence: false,
        reason: 'declared JSON profile produced no machine result JSON in stdout',
      };
    }
    const counters = countersFromJsonObject(parsed);
    if (!counters) {
      return {
        ok: false,
        hasEvidence: false,
        reason: 'machine result JSON has no assertion counters',
        parsed,
      };
    }
    return judgeCounters(counters, { adapter: 'stdout-json' });
  }

  if (kind === 'nunit') {
    const line = parseNunitSummaryLine(text);
    if (line) return line;
    return {
      ok: false,
      hasEvidence: false,
      reason: 'declared NUnit profile produced neither a bound result file nor a NUNIT summary line',
    };
  }

  if (kind === 'slotcheck') {
    const r = parseSlotcheckText(text, exitCode);
    return r || {
      ok: false,
      hasEvidence: false,
      reason: 'declared slotcheck profile output not recognized',
    };
  }

  if (kind === 'semcheck') {
    const r = parseSemcheckText(text, exitCode);
    return r || {
      ok: false,
      hasEvidence: false,
      reason: 'declared semcheck profile output not recognized',
    };
  }

  // kind === 'text': only explicitly adapted, tested formats count as results.
  const adapted =
    parseNunitSummaryLine(text) ||
    parseSlotcheckText(text, exitCode) ||
    parseSemcheckText(text, exitCode);
  if (adapted) return adapted;
  return {
    ok: false,
    hasEvidence: false,
    reason:
      'stdout carries no recognized machine result; declare resultKind/resultFile ' +
      'in the profile (unrecognized text can never support passed)',
  };
}

/** A profile that declares a result contract must honour it exactly. */
function profileDeclaresResultContract(profile) {
  return !!(profile && (profile.resultKind || profile.resultFile));
}

export function cmdDoctor(cfg) {
  const dirs = collabDirs(cfg);
  const local = localDirs(cfg);
  const checks = [];
  const push = (name, ok, detail) => checks.push({ name, ok, detail });
  push('node', true, process.version);
  push('repoRoot', fs.existsSync(cfg._repoRoot), cfg._repoRoot);
  push('config', true, cfg._usingExample ? 'using config.example.json (run init)' : cfg._configPath);
  push('collaborationRoot', fs.existsSync(dirs.root), dirs.root);
  try {
    let ancestor = local.tmp;
    while (!fs.existsSync(ancestor)) ancestor = path.dirname(ancestor);
    fs.accessSync(ancestor, fs.constants.W_OK);
    push('localTmpWritable', true, `read-only access check: ${ancestor}`);
  } catch (e) {
    push('localTmpWritable', false, String(e.message || e));
  }
  const unity = unityRunning();
  push(
    'unityOccupancyCheck',
    unity.checked,
    unity.checked
      ? unity.running
        ? `Unity running pids=${unity.pids.join(',')}; wait for user save/close`
        : 'no Unity.exe detected'
      : `unavailable: ${unity.error}`,
  );
  for (const tool of [
    'tools/semcheck/run.js',
    'tools/regression/export-baseline.js',
    'tools/slotcheck/slotcheck.csproj',
  ]) {
    push(tool, fs.existsSync(path.join(cfg._repoRoot, tool)), tool);
  }
  const dotnet = spawnSync('dotnet', ['--version'], { encoding: 'utf8', windowsHide: true, timeout: 5000 });
  const dotnetAvailable = dotnet.status === 0;
  const editorVersionFile = path.join(cfg._repoRoot, 'Unity Project/ProjectSettings/ProjectVersion.txt');
  const editorVersion = fs.existsSync(editorVersionFile) ? fs.readFileSync(editorVersionFile, 'utf8').match(/m_EditorVersion:\s*(\S+)/)?.[1] || null : null;
  const runnerFile = path.join(cfg._repoRoot, 'tools/regression/RunUnityTests.ps1');
  const runnerDefault = fs.existsSync(runnerFile) ? fs.readFileSync(runnerFile, 'utf8').match(/\[string\]\$UnityPath\s*=\s*'([^']+)'/)?.[1] : null;
  const command = cfg.profiles?.['unity-editmode']?.command || [];
  const unityArg = command.indexOf('-UnityPath');
  const configuredEditor = unityArg >= 0 ? command[unityArg + 1] : null;
  const editorCandidates = [configuredEditor, runnerDefault, editorVersion ? path.join(process.env.ProgramFiles || 'C:/Program Files', 'Unity/Hub/Editor', editorVersion, 'Editor/Unity.exe') : null].filter(Boolean);
  const editorPath = editorCandidates.find(p => fs.existsSync(p)) || editorCandidates[0] || null;
  const generatedProjects = ['tools/semcheck/semcheck.csproj', 'tools/semcheck/semcheck_tests.csproj'].map(rel => {
    const abs = path.join(cfg._repoRoot, rel);
    const text = fs.existsSync(abs) ? fs.readFileSync(abs, 'utf8') : '';
    const refs = [...text.matchAll(/<HintPath>([^<]+)<\/HintPath>/g)].map(m => path.resolve(path.dirname(abs), m[1].replace(/&amp;/g, '&')));
    return { path: rel, exists: fs.existsSync(abs), references: refs.length, missingReferences: refs.filter(p => !fs.existsSync(p)) };
  });
  const cursorHook = path.join(cfg._repoRoot, 'tools/collab/adapters/cursor-hooks.json.example');
  const codexHook = path.join(cfg._repoRoot, 'tools/collab/adapters/codex-hooks.toml.example');
  push(
    'hooksAdaptersPresent',
    fs.existsSync(cursorHook) && fs.existsSync(codexHook),
    cfg.hooks?.enabled
      ? 'hooks enabled in config — verify client wiring separately'
      : 'adapters present; hooks disabled by default (manual CLI complete)',
  );

  // A general config.toml is not proof that this project's hook is registered.
  const warnings = [];
  const clientConfigs = [
    path.join(cfg._repoRoot, '.cursor/hooks.json'),
    path.join(cfg._repoRoot, '.codex/hooks.json'),
  ];
  const present = clientConfigs.filter((p) => fs.existsSync(p));
  if (cfg.hooks?.enabled && present.length === 0) {
    warnings.push(
      'hooks.enabled=true but no project client hook config found; native events are unverified. ' +
        'Manual CLI (sync/deliver/review-*) is unaffected and remains the authoritative loop.',
    );
  }
  const hookState = hookStatus(cfg);
  // CLI counters prove CLI execution only, never native client wiring/trust.
  const hookVerified = false;
  push(
    'hooksAdapter',
    true,
    `${hookState.enabled ? 'enabled' : 'disabled'} · applied=${hookState.counters.applied} deduped=${hookState.counters.deduped} · ` +
      (hookVerified
        ? 'native wiring verified'
        : 'native wiring unverified; use manual sync or watch'),
  );
  if (!hookVerified) {
    warnings.push(
      'Native client Hook loading/trust is unverified. CLI applied/deduped counters only verify CLI behavior; manual sync/ack and watch remain authoritative.',
    );
  }

  const book = taskBookSummary(cfg);
  const bookPath = cfg.taskBook?.path ? path.join(cfg._repoRoot, cfg.taskBook.path) : null;
  const bookPresent = !!bookPath && fs.existsSync(bookPath);
  const bookBroken = book.filter((b) => !b.ok);
  const bookConflicts = book.filter((b) => b.clauseChanged);
  push(
    'taskBookMapping',
    bookPresent ? bookBroken.length === 0 : true,
    !bookPresent
      ? `task book not found in this checkout: ${cfg.taskBook?.path || '(unset)'} (mapping not evaluated)`
      : book.length === 0
        ? 'no taskBook.tasks mapping configured'
        : `${book.length} mapped (${book.filter((b) => b.baselineRecorded).length} with baseline)` +
          (bookBroken.length ? `; broken: ${bookBroken.map((b) => b.taskId).join(',')}` : '') +
          (bookConflicts.length ? `; clause changed: ${bookConflicts.map((b) => b.taskId).join(',')}` : ''),
  );
  if (!bookPresent) {
    warnings.push(
      `task book missing at ${cfg.taskBook?.path}; task-book mapping and completion gate cannot be evaluated here.`,
    );
  }
  for (const b of bookBroken) {
    warnings.push(`task book mapping ${b.taskId}: ${b.reason}`);
  }
  for (const b of bookConflicts) {
    warnings.push(
      `task book clause ${b.taskId} changed since its baseline; re-baseline only after reviewing the edit.`,
    );
  }

  const ok = checks.every((c) => c.ok);
  return {
    ok,
    checks,
    warnings,
    prerequisites: {
      dotnet: { available: dotnetAvailable, version: dotnetAvailable ? dotnet.stdout.trim() : null, requiredFor: ['compile', 'slot'] },
      unity: { editorVersion, editorPath, editorCandidates, editorExists: !!editorPath && fs.existsSync(editorPath), requiredFor: ['unity-editmode', 'unity-playmode'] },
      generatedProjects,
      note: 'Read-only checks; importing Unity, generating csproj and restoring .NET dependencies are separate operations. Missing engineering prerequisites do not imply the Node-only collab profile failed.',
    },
    hookCapability: {
      configEnabled: !!cfg.hooks?.enabled,
      clientConfigsFound: present,
      verified: hookVerified,
      clients: hookState.clients,
      counters: hookState.counters,
      manualFallback: hookState.manualFallback,
    },
    taskBook: {
      path: cfg.taskBook?.path || null,
      mapped: book.map((b) => ({
        taskId: b.taskId,
        totalChecked: b.totalChecked,
        subItems: `${b.checkedSubItems}/${b.subItems}`,
        baselineRecorded: b.baselineRecorded,
        clauseChanged: b.clauseChanged,
      })),
    },
    exitCode: ok ? EXIT.OK : EXIT.CHECK_FAILED,
  };
}

/* ------------------------------------------------------------------ *
 * Task definitions, task-book mapping and completion gate (C09)
 * ------------------------------------------------------------------ */

export function cmdTaskAdd(cfg, args) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'task-add');
  const definitionPath = args.definition;
  if (!definitionPath) fail(EXIT.USAGE, 'task-add requires --definition <file.json>');
  const abs = path.resolve(definitionPath);
  if (!fs.existsSync(abs)) fail(EXIT.USAGE, `definition file not found: ${definitionPath}`);
  let def;
  try {
    def = JSON.parse(fs.readFileSync(abs, 'utf8'));
  } catch (e) {
    fail(EXIT.USAGE, `definition is not valid JSON: ${e.message}`);
  }
  if (!def.taskId) fail(EXIT.USAGE, 'definition.taskId is required');
  const target = path.join(collabDirs(cfg).tasks, `${def.taskId}.json`);
  if (fs.existsSync(target)) {
    fail(EXIT.CONFLICT, `task already exists and will not be overwritten: ${def.taskId}`, {
      taskId: def.taskId,
    });
  }
  const now = nowIso();
  const task = {
    schemaVersion: 1,
    projectId: cfg.projectId,
    taskId: def.taskId,
    title: def.title,
    revision: 1,
    devStatus: 'todo',
    reviewStatus: 'not_run',
    freshness: 'unknown',
    holder: null,
    scope: Array.isArray(def.scope) ? def.scope : [],
    acceptanceCriteria: Array.isArray(def.acceptanceCriteria) ? def.acceptanceCriteria : [],
    dependencies: Array.isArray(def.dependencies) ? def.dependencies : [],
    blockers: [],
    sourceTaskBook: def.sourceTaskBook || null,
    createdAt: now,
    updatedAt: now,
  };
  if (Array.isArray(def.snapshotRoots)) task.snapshotRoots = def.snapshotRoots.map((p) => String(p));
  if (Array.isArray(def.snapshotFiles)) task.snapshotFiles = def.snapshotFiles.map((p) => String(p));
  if (def.readOnlyReference === true) task.readOnlyReference = true;
  if (def.taskBookStatus) task.taskBookStatus = def.taskBookStatus;
  validateTask(task);
  withStateLock(cfg, `task-add:${task.taskId}`, () => {
    if (fs.existsSync(target)) fail(EXIT.CONFLICT, `task already exists and will not be overwritten: ${task.taskId}`);
    writeJsonAtomic(target, task);
    const state = loadState(cfg);
    state.tasksIndex[task.taskId] = {
      title: task.title,
      devStatus: task.devStatus,
      reviewStatus: task.reviewStatus,
      holder: null,
      freshness: task.freshness,
      taskBookStatus: task.taskBookStatus || null,
      updatedAt: task.updatedAt,
    };
    saveState(cfg, state, { expectedRevision: state.revision });
  });
  recordEvent(cfg, {
    actor,
    action: 'task-add',
    taskId: task.taskId,
    summary: `task-add ${task.taskId}`,
  });
  generateSummaries(cfg);
  const roots = resolveTaskRoots(cfg, task);
  return {
    taskId: task.taskId,
    file: path.relative(cfg._repoRoot, target).split(path.sep).join('/'),
    snapshotRoots: roots.implementationRoots,
    rootsSource: roots.source,
  };
}

export function cmdTasks(cfg) {
  const book = taskBookSummary(cfg);
  const state = loadState(cfg);
  const tasks = listJsonRecords(collabDirs(cfg).tasks).map((t) => {
    const mapped = book.find((b) => b.taskId === t.taskId) || null;
    const roots = resolveTaskRoots(cfg, t);
    return {
      taskId: t.taskId,
      title: t.title,
      devStatus: t.devStatus,
      reviewStatus: t.reviewStatus,
      freshness: t.freshness || 'unknown',
      holder: t.holder || null,
      readOnlyReference: !!t.readOnlyReference,
      rootsSource: roots.source,
      roots: roots.implementationRoots,
      taskBook: mapped
        ? {
            totalChecked: mapped.totalChecked,
            subItems: `${mapped.checkedSubItems}/${mapped.subItems}`,
            baselineRecorded: mapped.baselineRecorded,
            clauseChanged: mapped.clauseChanged,
            reason: mapped.reason,
          }
        : null,
    };
  });
  return {
    revision: state.revision,
    taskBookPath: cfg.taskBook?.path || null,
    toolClausesPath: cfg.taskBook?.toolClauses?.path || null,
    tasks,
    mapped: book,
    unmapped: tasks.filter((t) => !t.taskBook).map((t) => t.taskId),
  };
}

export function cmdGate(cfg, args) {
  // A gate is about the current bytes, not the last time someone remembered sync.
  refreshFreshness(cfg, { write: true });
  if (args.task) {
    const gate = computeTaskGate(cfg, args.task);
    return { ...gate, exitCode: gate.ok ? EXIT.OK : EXIT.CHECK_FAILED };
  }
  const overall = computeOverallGate(cfg);
  return { ...overall, exitCode: overall.ok ? EXIT.OK : EXIT.CHECK_FAILED };
}

/**
 * Defect lifecycle. Issue records stay immutable (they are the defect report),
 * so a status transition is recorded once in the shared CAS state and read back
 * through listIssues(). Without this a failed review would block the completion
 * gate forever, which is the "one status source" problem C09 names.
 */
const ISSUE_TRANSITIONS = {
  open: ['fixing', 'fixed', 'wontfix', 'closed'],
  fixing: ['fixed', 'wontfix', 'open'],
  fixed: ['verified', 'wontfix', 'open'],
  verified: ['closed', 'open'],
  closed: ['open'],
  wontfix: ['open'],
};
const ISSUE_ROLE_STATUS = {
  cursor: ['fixing', 'fixed'],
  codex: ['fixing', 'fixed', 'verified', 'closed', 'wontfix'],
  user: ['fixing', 'fixed', 'verified', 'closed', 'wontfix', 'open'],
};

export function cmdIssueUpdate(cfg, args) {
  const actor = args.actor || 'codex';
  const issueId = args.issue;
  if (!issueId) fail(EXIT.USAGE, 'issue-update requires --issue');
  const status = String(args.status || '').trim();
  if (!status) fail(EXIT.USAGE, 'issue-update requires --status');
  const allowed = ISSUE_ROLE_STATUS[actor];
  if (!allowed) fail(EXIT.FORBIDDEN, `unknown actor: ${actor}`);
  if (!allowed.includes(status)) {
    fail(EXIT.FORBIDDEN, `${actor} may not set issue status "${status}" (allowed: ${allowed.join(', ')})`);
  }
  const issuePath = path.join(collabDirs(cfg).issues, `${issueId}.json`);
  const issue = readJson(issuePath, null);
  if (!issue) fail(EXIT.USAGE, `issue not found: ${issueId}`);
  return withStateLock(cfg, `issue-update:${issueId}`, () => {
    const state = loadState(cfg);
    const effective = state.issueStatus?.[issueId]?.status || issue.status;
    if (!ISSUE_TRANSITIONS[effective]?.includes(status)) {
      fail(
        EXIT.CONFLICT,
        `illegal issue transition ${effective} -> ${status} for ${issueId}`,
        { issueId, from: effective, to: status },
      );
    }
    const entry = {
      status,
      previous: effective,
      actor,
      note: args.note ? String(args.note) : '',
      at: nowIso(),
    };
    const next = saveState(cfg, {
      ...state,
      issueStatus: { ...(state.issueStatus || {}), [issueId]: entry },
    }, { expectedRevision: state.revision });
    const event = recordEvent(cfg, {
      actor,
      action: 'issue-status',
      taskId: issue.taskId || null,
      deliveryId: issue.deliveryId || null,
      reviewId: issue.reviewId || null,
      summary: `${issueId}: ${effective} -> ${status}`,
      data: { issueId, from: effective, to: status, note: entry.note },
    });
    generateSummaries(cfg);
    refreshFreshness(cfg);
    return {
      issueId,
      taskId: issue.taskId,
      from: effective,
      to: status,
      revision: next.revision,
      eventId: event.eventId,
      actor,
    };
  });
}

export function cmdTaskBookBaseline(cfg, args) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'taskbook-baseline');
  const ids = args.task ? [args.task] : Object.keys(cfg.taskBook?.tasks || {});
  if (ids.length === 0) fail(EXIT.USAGE, 'no taskBook.tasks mapping configured');
  const results = withStateLock(cfg, 'taskbook-baseline', () => ids.map((id) => recordBaseline(cfg, id)));
  recordEvent(cfg, {
    actor,
    action: 'taskbook-baseline',
    summary: `taskbook-baseline ${ids.join(',')}`,
    data: { results },
  });
  return { results };
}

export function cmdTaskBookCheck(cfg, args) {
  const actor = args.actor || 'codex';
  assertRoleAction(actor, 'taskbook-check');
  if (!args.task) fail(EXIT.USAGE, 'taskbook-check requires --task');
  const written = withStateLock(cfg, 'taskbook-check', () => writeTotalCheckbox(cfg, args.task, { actor }));
  recordEvent(cfg, {
    actor,
    action: 'taskbook-check',
    taskId: args.task,
    summary: written.changed
      ? `taskbook-check ${args.task} checked line ${written.line}`
      : `taskbook-check ${args.task} already checked`,
    data: written,
  });
  generateSummaries(cfg);
  return written;
}

/* ------------------------------------------------------------------ *
 * Hook adapters (C10): translate one client event into the same CLI.
 * ------------------------------------------------------------------ */

export function cmdHook(cfg, args, options = {}) {
  const source = args.source;
  if (!source) fail(EXIT.USAGE, 'hook requires --source cursor|codex');
  if (!args.event) fail(EXIT.USAGE, 'hook requires --event <name>');
  const actor = args.actor || (source === 'cursor' ? 'cursor' : 'codex');
  assertRoleAction(actor, 'hook');

  let payload = {};
  // Hook clients on Windows may hand us a UTF-8 BOM (PowerShell 5 redirection,
  // some editors). A BOM is not JSON, so strip it instead of rejecting the event.
  const stripBom = (s) => String(s).replace(/^\uFEFF/, '');
  if (args['payload-json']) {
    try {
      payload = JSON.parse(stripBom(args['payload-json']));
    } catch (e) {
      fail(EXIT.USAGE, `--payload-json is not valid JSON: ${e.message}`);
    }
  } else if (args.payload) {
    const abs = path.resolve(args.payload);
    if (!fs.existsSync(abs)) fail(EXIT.USAGE, `payload file not found: ${args.payload}`);
    try {
      payload = JSON.parse(stripBom(fs.readFileSync(abs, 'utf8')));
    } catch (e) {
      fail(EXIT.USAGE, `payload file is not valid JSON: ${e.message}`);
    }
  } else if (options.stdinText && options.stdinText.trim()) {
    try {
      payload = JSON.parse(stripBom(options.stdinText));
    } catch {
      fail(EXIT.USAGE, 'stdin payload is not valid JSON');
    }
  }
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) fail(EXIT.USAGE, 'hook payload must be a JSON object');
  const result = runHook(cfg, {
    source,
    event: args.event,
    payload,
    idempotencyKey: args['idempotency-key'] || null,
    apply: () => cmdSync(cfg, {}),
  });
  return result;
}

export function cmdHooksStatus(cfg) {
  return hookStatus(cfg);
}

export function cmdReviewEvidence(cfg, args) {
  assertReviewActor(args, 'review-evidence');
  const review = readJson(path.join(collabDirs(cfg).reviews, `${args.review}.json`));
  if (!review) fail(EXIT.USAGE, 'review not found');
  assertReviewAccess(cfg, args, 'review-evidence', review);
  if (!review || isTerminalReview(review)) fail(EXIT.CONFLICT, 'review must exist and be nonterminal');
  const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${review.deliveryId}.json`));
  const task = loadTask(cfg, review.taskId);
  const run = review.runs?.at(-1);
  if (!run) fail(EXIT.EVIDENCE, 'run the review before drafting evidence');
  for (const which of ['stdout', 'stderr']) {
    const check = verifyRunEvidence(cfg._repoRoot, run, which);
    if (!check.ok) fail(EXIT.CHECK_FAILED, check.reason);
  }
  const parsed = parseRunResult(review.profileDefinition || cfg.profiles[review.profile], run, cfg._repoRoot);
  if (!parsed.counters) fail(EXIT.EVIDENCE, 'no verified machine counters; evidence cannot be drafted');
  const rows = new Map(String(args.coverage || '').split(',').filter(Boolean).map(pair => {
    const [id, result] = pair.split('=');
    if (!task.acceptanceCriteria.some(ac => ac.id === id) || !['passed', 'failed', 'not_run', 'not_applicable'].includes(result)) {
      fail(EXIT.USAGE, 'coverage must use existing AC IDs and explicit result, e.g. AC1=passed');
    }
    return [id, result];
  }));
  const conclusion = args.conclusion || 'failed';
  if (!['passed', 'failed'].includes(conclusion)) fail(EXIT.USAGE, 'conclusion must be passed|failed');
  if (conclusion === 'passed' && (!parsed.ok || run.exitCode !== 0)) fail(EXIT.EVIDENCE, 'machine result does not support passed');
  const evidence = {
    conclusion, subjectImplementationFingerprint: delivery.fingerprints.implementation.fingerprint,
    assertionsTotal: parsed.counters.total, assertionsFailed: parsed.counters.failed, assertionsSkipped: parsed.counters.skipped,
    coverage: task.acceptanceCriteria.map(ac => ({ id: ac.id, result: rows.get(ac.id) || 'not_run',
      evidence: args.note || `Explicit operator coverage declaration for run ${run.runToken}; inspect the bound output.` })),
    note: 'Draft only: review-finish independently revalidates bytes, snapshot and every required AC.',
  };
  const dir = path.join(localDirs(cfg).root, 'evidence');
  const id = `${review.reviewId}-${newId('EVD')}`;
  const file = appendJsonRecord(dir, id, evidence);
  return { evidencePath: file, evidence, notPassed: evidence.coverage.filter(c => c.result !== 'passed').map(c => c.id) };
}

export function cmdInit(cfg) {
  const dirs = collabDirs(cfg);
  const local = localDirs(cfg);
  const created = [];
  const skipped = [];
  const mkdir = (p) => {
    if (fs.existsSync(p)) skipped.push(p);
    else {
      ensureDir(p);
      created.push(p);
    }
  };
  for (const p of [
    dirs.root,
    dirs.tasks,
    dirs.deliveries,
    dirs.reviews,
    dirs.events,
    dirs.issues,
    local.root,
    local.locks,
    local.tmp,
    local.logs,
    snapshotRoot(cfg),
  ]) {
    mkdir(p);
  }

  const cfgPath = path.join(cfg._repoRoot, 'tools/collab/config.json');
  if (!fs.existsSync(cfgPath)) {
    fs.copyFileSync(path.join(cfg._repoRoot, 'tools/collab/config.example.json'), cfgPath);
    created.push(cfgPath);
  } else skipped.push(cfgPath);

  if (!fs.existsSync(dirs.state)) {
    writeJsonAtomic(dirs.state, {
      schemaVersion: 1,
      projectId: cfg.projectId,
      revision: 1,
      updatedAt: nowIso(),
      tasksIndex: {},
    });
    created.push(dirs.state);
  } else skipped.push(dirs.state);

  const demoPath = path.join(dirs.tasks, 'C-DEMO.json');
  if (!fs.existsSync(demoPath)) {
    writeJsonAtomic(demoPath, {
      schemaVersion: 1,
      projectId: cfg.projectId,
      taskId: 'C-DEMO',
      title: '协作工具演示任务（专用验收，不改真实游戏任务结论）',
      revision: 1,
      devStatus: 'todo',
      reviewStatus: 'not_run',
      freshness: 'unknown',
      holder: null,
      scope: [
        '仅用于 Cursor×Codex 协作工具 MVP 手动闭环演练',
        '不得为演示而更改 T01～T06 真实验收勾选',
      ],
      acceptanceCriteria: [
        { id: 'AC1', text: 'claim→deliver→review-start→review-run→review-finish 可走通' },
        { id: 'AC2', text: '失败可生成缺陷；修复后新交付可复验；旧报告不可覆盖' },
        { id: 'AC3', text: '实现指纹变化后旧 passed 对当前代码显示 stale/待复验' },
      ],
      dependencies: [],
      blockers: [],
      sourceTaskBook:
        'docs/Development Manual/Development Task Book/Task book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md §14',
      createdAt: nowIso(),
      updatedAt: nowIso(),
    });
    created.push(demoPath);
  } else skipped.push(demoPath);

  const refs = [
    { taskId: 'T01', title: 'R42 交割后加班失败', taskBookStatus: 'accepted' },
    { taskId: 'T02', title: '可复现开发基线', taskBookStatus: 'partial_open' },
    { taskId: 'T03', title: '指令至起飞主链路', taskBookStatus: 'partial_open' },
    { taskId: 'T04', title: '经营账务与长线平衡', taskBookStatus: 'partial_open' },
    { taskId: 'T05', title: '时间策略交互体验', taskBookStatus: 'partial_open' },
    { taskId: 'T06', title: '文档与架构收口', taskBookStatus: 'accepted' },
  ];
  for (const r of refs) {
    const p = path.join(dirs.tasks, `${r.taskId}.json`);
    if (fs.existsSync(p)) {
      skipped.push(p);
      continue;
    }
    writeJsonAtomic(p, {
      schemaVersion: 1,
      projectId: cfg.projectId,
      taskId: r.taskId,
      title: r.title,
      revision: 1,
      devStatus: 'todo',
      reviewStatus: 'not_run',
      freshness: 'unknown',
      holder: null,
      scope: ['以任务书原文为准；本文件仅为协作索引'],
      acceptanceCriteria: [{ id: 'REF', text: '以任务书原文为准；本文件仅为协作索引' }],
      dependencies: [],
      blockers: [],
      readOnlyReference: true,
      taskBookStatus: r.taskBookStatus,
      sourceTaskBook:
        'docs/Development Manual/Development Task Book/Task book/Airline_CEO_Dev_Task_2026-10-17.md',
      createdAt: nowIso(),
      updatedAt: nowIso(),
    });
    created.push(p);
  }

  recordEvent(cfg, {
    actor: 'user',
    action: 'init',
    summary: `init created=${created.length} skipped=${skipped.length}`,
    data: { created, skipped },
  });
  generateSummaries(cfg);
  return { created, skipped, message: 'init complete; existing rules/state were not overwritten' };
}

export function cmdStatus(cfg) {
  const state = loadState(cfg);
  return {
    revision: state.revision,
    updatedAt: state.updatedAt,
    ack: loadAck(cfg),
    tasks: listJsonRecords(collabDirs(cfg).tasks).map((t) => ({
      taskId: t.taskId,
      title: t.title,
      devStatus: t.devStatus,
      reviewStatus: t.reviewStatus,
      freshness: t.freshness,
      holder: t.holder,
      taskBookStatus: t.taskBookStatus || null,
      readOnlyReference: !!t.readOnlyReference,
    })),
    openIssues: listIssues(cfg).filter((i) => i.status !== 'closed').map((i) => i.issueId),
    reviewAssessments: listReviews(cfg).map(r => ({ reviewId: r.reviewId, deliveryId: r.deliveryId,
      status: r.status, independent: r.independent ?? null, reviewKind: r.reviewKind || 'legacy-role-record' })),
  };
}

export function cmdSync(cfg) {
  const before = loadState(cfg).revision;
  const { results, anyChange } = refreshFreshness(cfg, { write: true });
  const taskBookChanges = withStateLock(cfg, 'taskbook-reconcile', () => reconcileManagedCheckboxes(cfg));
  const summary = generateSummaries(cfg);
  const after = loadState(cfg).revision;
  if (anyChange) {
    recordEvent(cfg, {
      actor: 'system',
      action: 'sync',
      summary: `sync revision=${after}`,
      data: { freshness: results },
    });
  }
  return {
    revision: after,
    revisionBumped: after !== before,
    anyChange,
    taskBookChanges,
    freshness: results,
    ack: loadAck(cfg),
    currentPath: summary.currentPath,
  };
}

export function cmdAck(cfg, args) {
  const actor = args.actor;
  const revision = Number(args.revision);
  if (!actor || !revision) fail(EXIT.USAGE, 'ack requires --actor and --revision');
  assertRoleAction(actor, 'ack');
  return withStateLock(cfg, `ack:${actor}`, () => {
  const state = loadState(cfg);
  if (revision !== state.revision) {
    fail(EXIT.CONFLICT, `ack revision ${revision} != current ${state.revision}`, {
      currentRevision: state.revision,
    });
  }
  const ack = saveAck(cfg, actor, revision);
  recordEvent(cfg, { actor, action: 'ack', summary: `ack revision ${revision}`, data: { revision } });
  generateSummaries(cfg);
  return { ack, revision };
  });
}

function assertScopeAvailable(cfg, task) {
  const wanted = resolveTaskRoots(cfg, task).implementationRoots;
  const overlaps = (a, b) => a === b || a.startsWith(b + '/') || b.startsWith(a + '/');
  const conflicts = listJsonRecords(collabDirs(cfg).tasks).filter(t =>
    t.taskId !== task.taskId && t.holder && resolveTaskRoots(cfg, t).implementationRoots.some(a => wanted.some(b => overlaps(a, b))));
  if (conflicts.length) fail(EXIT.CONFLICT, 'task source scope overlaps an active claim', { tasks: conflicts.map(t => t.taskId) });
}

export function cmdClaim(cfg, args) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'claim');
  const taskId = args.task;
  if (!taskId) fail(EXIT.USAGE, 'claim requires --task');
  const expected = args['expected-revision'] != null ? Number(args['expected-revision']) : null;

  return withStateLock(cfg, `claim:${actor}:${taskId}`, () => {
    const state = loadState(cfg);
    const ack = loadAck(cfg);
    if (!ack[actor] || ack[actor].revision !== state.revision) {
      fail(EXIT.CONFLICT, `ack current revision ${state.revision} before claim (actor=${actor})`, {
        currentRevision: state.revision,
        ack: ack[actor] || null,
      });
    }
    if (expected != null && state.revision !== expected) {
      fail(EXIT.CONFLICT, `expected-revision ${expected} != ${state.revision}`, {
        currentRevision: state.revision,
      });
    }
    const task = loadTask(cfg, taskId);
    if (task.readOnlyReference) {
      fail(EXIT.FORBIDDEN, `${taskId} is a read-only task-book reference; use C-DEMO for tool drills`);
    }
    assertScopeAvailable(cfg, task);
    if (task.holder && task.holder !== actor) {
      fail(EXIT.CONFLICT, `task ${taskId} already held by ${task.holder}`);
    }
    const taskLock = new FileLock(
      path.join(localDirs(cfg).locks, `task-${taskId}.lock`),
      `${actor}@${os.hostname()}`,
    );
    const got = taskLock.acquire();
    if (!got.ok && !(String(got.existing?.owner || '').startsWith(actor))) {
      fail(EXIT.LOCKED, got.message, got);
    }
    validateTask(task);
    task.holder = actor;
    task.devStatus = task.devStatus === 'todo' ? 'developing' : task.devStatus;
    task.updatedAt = nowIso();
    const dirs = collabDirs(cfg);
    writeJsonAtomic(path.join(dirs.tasks, `${task.taskId}.json`), task);
    state.tasksIndex[task.taskId] = {
      title: task.title,
      devStatus: task.devStatus,
      reviewStatus: task.reviewStatus,
      holder: task.holder,
      freshness: task.freshness || 'unknown',
      taskBookStatus: task.taskBookStatus || null,
      updatedAt: task.updatedAt,
    };
    const saved = saveState(cfg, state, { expectedRevision: state.revision });
    recordEvent(cfg, { actor, action: 'claim', taskId, summary: `claimed ${taskId}` });
    generateSummaries(cfg);
    return { taskId, holder: actor, revision: saved.revision };
  });
}

export function cmdRelease(cfg, args) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'release');
  const taskId = args.task;
  if (!taskId) fail(EXIT.USAGE, 'release requires --task');
  return withStateLock(cfg, `release:${taskId}`, () => {
    const task = loadTask(cfg, taskId);
    if (task.holder && task.holder !== actor) {
      fail(EXIT.CONFLICT, `cannot release; holder is ${task.holder}`);
    }
    task.holder = null;
    task.updatedAt = nowIso();
    const dirs = collabDirs(cfg);
    writeJsonAtomic(path.join(dirs.tasks, `${task.taskId}.json`), task);
    const state = loadState(cfg);
    if (state.tasksIndex[taskId]) state.tasksIndex[taskId].holder = null;
    saveState(cfg, state, { expectedRevision: state.revision });
    new FileLock(path.join(localDirs(cfg).locks, `task-${taskId}.lock`), actor).release({
      force: true,
    });
    recordEvent(cfg, { actor, action: 'release', taskId, summary: `released ${taskId}` });
    generateSummaries(cfg);
    return { taskId, holder: null };
  });
}

export function cmdDeliver(cfg, args) {
  const actor = args.actor || 'cursor';
  if (actor !== 'codex') assertRoleAction(actor, 'deliver');
  const taskId = args.task;
  if (!taskId) fail(EXIT.USAGE, 'deliver requires --task');
  const task = loadTask(cfg, taskId);
  assertScopeAvailable(cfg, task);
  const authorization = developerAuthorization(cfg, task, actor, 'deliver', args.authorization);
  if (task.readOnlyReference) fail(EXIT.FORBIDDEN, 'cannot deliver read-only reference task');
  if (!task.acceptanceCriteria?.length) fail(EXIT.USAGE, 'task missing acceptanceCriteria');
  if (task.holder && task.holder !== actor) {
    fail(EXIT.CONFLICT, `deliver denied; holder is ${task.holder}`);
  }

  let summary = args.summary || '';
  if (args['summary-file']) {
    summary = fs.readFileSync(path.resolve(args['summary-file']), 'utf8').trim();
  }
  if (!summary) fail(EXIT.USAGE, 'deliver requires --summary or --summary-file');

  const model = args.model || process.env.COLLAB_MODEL || 'unknown';
  const profiles = String(args.profiles || 'smoke').split(',').map(p => p.trim()).filter(Boolean);
  if (!profiles.length || profiles.some(p => !cfg.profiles?.[p])) fail(EXIT.USAGE, 'delivery references an unknown profile', { available: Object.keys(cfg.profiles || {}) });
  const roots = taskRoots(cfg, taskId);
  const snapRootList = snapshotRootsFor(roots.implementationRoots, roots.verificationRoots);
  const fingerprints = computeFingerprints(cfg._repoRoot, {
    contractPayload: stableContractPayload(task),
    implementationRoots: roots.implementationRoots,
    verificationRoots: roots.verificationRoots,
  });
  const expectedSnapFp = hashManifest(collectEntries(cfg._repoRoot, snapRootList));
  const deliveryId = newId('DEL');
  const snapDir = path.join(snapshotRoot(cfg), deliveryId);
  const snap = exportStableSnapshot(cfg._repoRoot, snapDir, { roots: snapRootList });
  if (snap.sourceFingerprint !== expectedSnapFp) {
    fs.rmSync(snapDir, { recursive: true, force: true });
    fail(EXIT.CHECK_FAILED, 'snapshot fingerprint mismatch after export');
  }
  // A delivery must be clean the moment it is created: if anything outside the
  // manifest (typically generated collaboration state written by a self-test run
  // inside the delivery directory) is already present, refuse to publish it.
  const unlistedAtCreation = listUnlistedSnapshotFiles(
    snapDir,
    snap.entries.map((e) => String(e.path).replace(/\\/g, '/')),
  );
  if (unlistedAtCreation.length > 0) {
    fs.rmSync(snapDir, { recursive: true, force: true });
    fail(
      EXIT.CHECK_FAILED,
      'snapshot export produced unlisted files (delivery inputs must stay immutable): ' +
        unlistedAtCreation.join(', '),
    );
  }

  const delivery = {
    schemaVersion: 1,
    deliveryId,
    taskId,
    taskRevision: task.revision,
    actor,
    tool: actor,
    ...(authorization ? { authorization, developmentException: true } : {}),
    model,
    summary,
    files: {
      changedNote: args.files || 'see fingerprints.implementation.entries / git porcelain',
      workspaceChanges: (fingerprints.git.changes || []).filter(row => snapRootList.some(root => row.path === root || row.path.startsWith(root + '/') || row.previousPath === root || row.previousPath?.startsWith(root + '/'))),
    },
    businessRisk: args.risk || 'none stated',
    selfTest: {
      performed: args['self-test'] !== 'false',
      notes: args['self-test-notes'] || 'developer self-test; not independent acceptance',
    },
    knownGaps: args.gaps ? String(args.gaps).split(';') : [],
    git: fingerprints.git,
    fingerprints,
    recommendedProfiles: profiles,
    snapshotPath: path.relative(cfg._repoRoot, snapDir).split(path.sep).join('/'),
    snapshotManifest: {
      sourceFingerprint: snap.sourceFingerprint,
      copiedFileCount: snap.copiedFileCount,
      cleanGitCheckoutVerified: false,
      unlistedAtCreation: unlistedAtCreation.length,
      entries: snap.entries,
    },
    independentReviewPassed: false,
    createdAt: nowIso(),
  };
  validateDelivery(delivery);
  appendJsonRecord(collabDirs(cfg).deliveries, deliveryId, delivery);

  withStateLock(cfg, `deliver:${taskId}`, () => {
    const t = loadTask(cfg, taskId);
    t.devStatus = 'ready_for_review';
    t.reviewStatus = 'queued';
    t.freshness = 'current';
    t.updatedAt = nowIso();
    writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${taskId}.json`), t);
    const state = loadState(cfg);
    state.lastDeliveryId = deliveryId;
    if (state.tasksIndex[taskId]) {
      state.tasksIndex[taskId].devStatus = t.devStatus;
      state.tasksIndex[taskId].reviewStatus = t.reviewStatus;
      state.tasksIndex[taskId].freshness = t.freshness;
    }
    saveState(cfg, state, { expectedRevision: state.revision });
  });

  recordEvent(cfg, {
    actor,
    action: 'deliver',
    taskId,
    deliveryId,
    summary: `delivered ${deliveryId}`,
    data: authorization ? { authorization, independence: 'developer self-check requires a separate reviewer' } : {},
  });
  generateSummaries(cfg);
  return {
    deliveryId,
    taskId,
    snapshotPath: delivery.snapshotPath,
    implementationFingerprint: fingerprints.implementation.fingerprint,
    revision: loadState(cfg).revision,
  };
}

export function cmdReviewStart(cfg, args) {
  const actor = args.actor || 'codex';
  assertReviewActor(args, 'review-start');
  const deliveryId = args.delivery;
  if (!deliveryId) fail(EXIT.USAGE, 'review-start requires --delivery');
  const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${deliveryId}.json`), null);
  if (!delivery) fail(EXIT.USAGE, `delivery not found: ${deliveryId}`);

  const profile = args.profile || delivery.recommendedProfiles?.[0] || 'smoke';
  const authorization = reviewerAuthorization(cfg, delivery, actor, 'review-start', args.authorization, profile);
  if (!cfg.profiles || !cfg.profiles[profile]) {
    fail(EXIT.USAGE, `unknown profile: ${profile}`, {
      available: Object.keys(cfg.profiles || {}),
    });
  }
  const reviewId = newId('REV');
  const needsUnity = String(profile).startsWith('unity');
  if (needsUnity) {
    const u = unityRunning();
    if (!u.checked) fail(EXIT.LOCKED, `cannot verify Unity occupancy: ${u.error}`, u);
    if (u.running) {
      fail(EXIT.LOCKED, 'Unity is occupied; wait for user to save and close, then resume', u);
    }
    const unityLock = new FileLock(
      path.join(localDirs(cfg).locks, 'unity-review.lock'),
      `${actor}@${os.hostname()}`,
      { reviewId },
    );
    const got = unityLock.acquire();
    if (!got.ok) fail(EXIT.LOCKED, got.message, got);
  }

  // Bind to delivery snapshot path; integrity is enforced at review-run (before command).
  const snapAbs = path.join(cfg._repoRoot, delivery.snapshotPath);
  if (!fs.existsSync(snapAbs)) {
    if (needsUnity) releaseUnityLockIfOwned(cfg, reviewId, actor);
    fail(EXIT.CHECK_FAILED, `snapshot missing: ${delivery.snapshotPath}`);
  }

  const review = {
    schemaVersion: 1,
    reviewId,
    deliveryId,
    taskId: delivery.taskId,
    taskRevision: delivery.taskRevision,
    actor,
    status: 'queued',
    profile,
    profileDefinition: JSON.parse(JSON.stringify(cfg.profiles[profile])),
    toolVersion: 'collab-cli/1.3.0',
    independent: delivery.actor !== actor,
    reviewKind: delivery.actor === actor ? 'developer-self-check' : 'independent',
    ...(authorization ? { reviewException: true, authorization } : {}),
    subjectFingerprints: {
      implementation: delivery.fingerprints.implementation.fingerprint,
      verification: delivery.fingerprints.verification.fingerprint,
      contract: delivery.fingerprints.contract.fingerprint,
    },
    snapshotPath: delivery.snapshotPath,
    createdAt: nowIso(),
    updatedAt: nowIso(),
    runs: [],
    coverage: [],
    issues: [],
  };
  validateReview(review);
  appendJsonRecord(collabDirs(cfg).reviews, reviewId, review);
  withStateLock(cfg, `review-start:${review.taskId}`, () => {
    const task = loadTask(cfg, delivery.taskId);
    task.reviewStatus = 'queued';
    task.updatedAt = nowIso();
    writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${task.taskId}.json`), task);
    const state = loadState(cfg);
    if (state.tasksIndex[task.taskId]) state.tasksIndex[task.taskId].reviewStatus = 'queued';
    saveState(cfg, state, { expectedRevision: state.revision });
  });
  recordEvent(cfg, {
    actor,
    action: 'review-start',
    taskId: delivery.taskId,
    deliveryId,
    reviewId,
    summary: `review-start ${reviewId} profile=${profile}`,
    ...(authorization ? { data: { reviewException: true, authorization } } : {}),
  });
  generateSummaries(cfg);
  return {
    reviewId,
    deliveryId,
    profile,
    status: 'queued',
    note: 'review-start does not execute tests; bound to delivery snapshot',
  };
}

/**
 * Release the Unity review lock only when this review actually owns it.
 * Never delete locks belonging to another review, another actor, or Unity itself.
 */
function releaseUnityLockIfOwned(cfg, reviewId, actor) {
  const lockPath = path.join(localDirs(cfg).locks, 'unity-review.lock');
  const payload = readJson(lockPath, null);
  if (payload && payload.reviewId === reviewId) {
    new FileLock(lockPath, `${actor}@${os.hostname()}`, { reviewId }).release({
      force: true,
    });
    return true;
  }
  return false;
}

/** Terminal review statuses: a finished review record is immutable. */
const TERMINAL_REVIEW_STATUSES = new Set(['passed', 'failed', 'cancelled', 'blocked']);

function isTerminalReview(review) {
  return !!review && TERMINAL_REVIEW_STATUSES.has(review.status) && !!review.finishedAt;
}

function assertReviewAccess(cfg, args, action, review) {
  const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${review.deliveryId}.json`), null);
  if (!delivery) fail(EXIT.EVIDENCE, 'delivery missing for review');
  return reviewerAuthorization(cfg, delivery, args.actor || 'codex', action, args.authorization, review.profile, review);
}

function assertReviewActor(args, action) {
  if (args.actor === 'cursor' && args.authorization) return; // Scoped check follows after loading the subject.
  assertRoleAction(args.actor || 'codex', action === 'review-evidence' ? 'review-finish' : action);
}

function fileState(abs) {
  if (!abs || !fs.existsSync(abs)) return { exists: false, sha256: null, bytes: 0, mtimeMs: 0 };
  const bytes = fs.readFileSync(abs);
  const st = fs.statSync(abs);
  return {
    exists: true,
    sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    bytes: bytes.length,
    mtimeMs: st.mtimeMs,
  };
}

/** Write run evidence, refusing to replace an existing file (one run, one file). */
function writeEvidenceOnce(abs, contents) {
  ensureDir(path.dirname(abs));
  try {
    fs.writeFileSync(abs, contents, { flag: 'wx' });
  } catch (e) {
    if (e.code === 'EEXIST') {
      fail(EXIT.CONFLICT, `run evidence already exists and will not be overwritten: ${abs}`);
    }
    throw e;
  }
}

/**
 * Verify a run's stored stdout/stderr bytes are exactly what the run recorded.
 * Returns { ok, reason, text } — a missing/changed file is evidence failure, not
 * "whatever is on disk now".
 */
function verifyRunEvidence(repoRoot, run, which /* 'stdout' | 'stderr' */) {
  const binding = run?.[which];
  const legacyPath = run?.[`${which}Path`];
  if (!binding) {
    return {
      ok: false,
      bound: false,
      reason:
        `run ${run?.runToken || '(legacy)'} has no ${which} content binding; ` +
        'this review cannot be judged passed — start a new review with the current tool',
    };
  }
  if (!binding.path || !binding.sha256 || !Number.isInteger(binding.bytes)) {
    return { ok: false, bound: false, reason: `run ${which} binding is incomplete` };
  }
  if (binding.runToken && run.runToken && binding.runToken !== run.runToken) {
    return { ok: false, bound: true, reason: `${which} binding belongs to another run token` };
  }
  if (legacyPath && legacyPath !== binding.path) {
    return { ok: false, bound: true, reason: `${which} path disagrees with its binding` };
  }
  const abs = path.join(repoRoot, binding.path);
  if (!fs.existsSync(abs)) {
    return { ok: false, bound: true, reason: `${which} evidence file is missing: ${binding.path}` };
  }
  const bytes = fs.readFileSync(abs);
  if (bytes.length !== binding.bytes) {
    return {
      ok: false,
      bound: true,
      reason: `${which} evidence size changed (recorded=${binding.bytes} actual=${bytes.length}): ${binding.path}`,
    };
  }
  const sha = crypto.createHash('sha256').update(bytes).digest('hex');
  if (sha !== binding.sha256) {
    return {
      ok: false,
      bound: true,
      reason: `${which} evidence content changed after the run (recorded=${binding.sha256} actual=${sha}): ${binding.path}`,
    };
  }
  return { ok: true, bound: true, text: bytes.toString('utf8'), path: binding.path };
}

/**
 * Resolve a profile's declared result path for one run. `{runToken}` makes the
 * path unique per execution so a leftover file from an earlier run can never be
 * mistaken for this run's result (TH-03).
 */
function resolveResultFileRel(profile, runToken) {
  if (!profile?.resultFile) return null;
  return String(profile.resultFile)
    .replace(/\{runToken\}/g, runToken || '')
    .replace(/\\/g, '/');
}

/**
 * A profile's command may legitimately write a report next to its sources (for
 * example tools/semcheck/run.js writes semcheck_report.txt). Those files are run
 * outputs, not reviewable inputs, and must not be confused with a smuggled file.
 * Only the exact literal paths declared in config are tolerated, and a declared
 * output may never look like source, so this cannot be used to smuggle code in.
 */
const RUN_OUTPUT_SOURCE_RE =
  /\.(cs|asmdef|asmref|js|mjs|cjs|ts|tsx|csproj|sln|slnx|props|targets|uxml|uss|shader|hlsl|compute|dll|so|jar)$/i;

export function resolveRunOutputRels(profile) {
  const declared = Array.isArray(profile?.runOutputs) ? profile.runOutputs : [];
  const rels = [];
  for (const raw of declared) {
    const rel = String(raw).replace(/\\/g, '/').replace(/^(?:\.?\/)+/, '');
    if (!rel || rel.includes('..')) {
      fail(EXIT.USAGE, `profile declares an invalid runOutputs path: ${raw}`);
    }
    if (RUN_OUTPUT_SOURCE_RE.test(rel)) {
      fail(EXIT.USAGE, `profile runOutputs must not declare a source file: ${rel}`);
    }
    rels.push(rel);
  }
  return rels;
}

/**
 * Run one review's profile exactly once.
 *
 * Guarantees added for the third-round findings:
 *  - TH-05: an exclusive run lease (`review-run-<id>.lock`, atomic create) is held
 *    for the whole execution, so two processes can never execute the same review;
 *    a second caller gets CONFLICT with the active owner instead of executing.
 *  - TH-03: a declared result file is bound to this run (pre/post state, runToken,
 *    profile, delivery) and its SHA256 is stored; leftovers cannot be reused.
 *  - TH-04: the executed snapshot is re-verified after the command, so source
 *    mutated during the run fails the run instead of passing.
 */
export function cmdReviewRun(cfg, args) {
  const actor = args.actor || 'codex';
  assertReviewActor(args, 'review-run');
  const reviewId = args.review;
  if (!reviewId) fail(EXIT.USAGE, 'review-run requires --review');
  const reviewPath = path.join(collabDirs(cfg).reviews, `${reviewId}.json`);
  const review = readJson(reviewPath, null);
  if (!review) fail(EXIT.USAGE, `review not found: ${reviewId}`);
  assertReviewAccess(cfg, args, 'review-run', review);
  if (isTerminalReview(review)) {
    fail(EXIT.CONFLICT, `review ${reviewId} already finished as ${review.status}`);
  }

  const delivery = readJson(
    path.join(collabDirs(cfg).deliveries, `${review.deliveryId}.json`),
    null,
  );
  if (!delivery) fail(EXIT.EVIDENCE, 'delivery missing for review');

  const profileName = review.profile;
  const profile = review.profileDefinition || cfg.profiles?.[profileName];
  if (!profile?.command) fail(EXIT.USAGE, `unknown or incomplete profile: ${profileName}`);

  if (String(profileName).startsWith('unity')) {
    const u = unityRunning();
    if (!u.checked) fail(EXIT.LOCKED, `cannot verify Unity occupancy: ${u.error}`, u);
    if (u.running) fail(EXIT.LOCKED, 'Unity occupied; close user Unity then retry', u);
    const unityLockPath = path.join(localDirs(cfg).locks, 'unity-review.lock');
    const payload = readJson(unityLockPath, null);
    if (payload && payload.reviewId !== reviewId) {
      fail(EXIT.LOCKED, `unity-review lock owned by review ${payload.reviewId}`, payload);
    }
    if (!payload) {
      const got = new FileLock(unityLockPath, `${actor}@${os.hostname()}`, {
        reviewId,
      }).acquire();
      if (!got.ok) fail(EXIT.LOCKED, got.message, got);
    }
  }

  const cmd = profile.command.map(String);
  const runToken = crypto.randomBytes(8).toString('hex');
  const resolvedResultRel = resolveResultFileRel(profile, runToken);
  const runOutputRels = resolveRunOutputRels(profile);
  const cmdResolved = cmd.map((a) =>
    String(a).replace(/\{runToken\}/g, runToken).replace(/\{reviewId\}/g, reviewId),
  );
  let snapAbs;
  try {
    snapAbs = verifySnapshotIntegrity(cfg._repoRoot, review.snapshotPath, delivery, {
      resultFileRel: resolvedResultRel,
      runOutputRels,
    });
  } catch (e) {
    // A review that cannot even start must not keep the Unity review lease, or
    // the next review would be blocked by a lock no run is using.
    releaseUnityLockIfOwned(cfg, reviewId, actor);
    throw e;
  }

  // Execution lease: acquired atomically before any state write, released only
  // after the terminal write-back. Duplicate execution is impossible by design.
  const lease = new FileLock(
    path.join(localDirs(cfg).locks, `review-run-${reviewId}.lock`),
    `${actor}@${os.hostname()}`,
    { reviewId, runToken },
  );
  const leaseGot = lease.acquire({ staleMs: 60 * 60 * 1000 });
  if (!leaseGot.ok) {
    fail(
      EXIT.CONFLICT,
      `review ${reviewId} already has an active run (${leaseGot.existing?.owner || 'unknown'} ` +
        `pid=${leaseGot.existing?.pid || '?'}); refusing to execute a second time`,
      { reviewId, activeRun: leaseGot.existing || null },
    );
  }

  try {
    // Mark running under the per-review lock; refuse if a terminal state landed
    // meanwhile or another worker owns the active run token.
    withReviewLock(cfg, reviewId, `run-start:${actor}`, () => {
      const disk = readJson(reviewPath, null);
      if (!disk) fail(EXIT.USAGE, `review not found: ${reviewId}`);
      if (isTerminalReview(disk)) {
        fail(EXIT.CONFLICT, `review ${reviewId} already finished as ${disk.status}`);
      }
      if (disk.activeRunToken && disk.activeRunToken !== runToken) {
        fail(EXIT.CONFLICT, `review ${reviewId} already has an active run token`, {
          activeRunToken: disk.activeRunToken,
        });
      }
      const merged = {
        ...disk,
        status: 'running',
        workerPid: process.pid,
        activeRunToken: runToken,
        activeRunStartedAt: nowIso(),
        updatedAt: nowIso(),
      };
      Object.assign(review, merged);
      writeJsonAtomic(reviewPath, merged);
    });
    withStateLock(cfg, `review-run:${review.taskId}`, () => {
      const task = loadTask(cfg, review.taskId);
      task.reviewStatus = 'running';
      task.updatedAt = nowIso();
      writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${task.taskId}.json`), task);
      const state = loadState(cfg);
      if (state.tasksIndex[task.taskId]) state.tasksIndex[task.taskId].reviewStatus = 'running';
      saveState(cfg, state, { expectedRevision: state.revision });
    });

    const logDir = path.join(localDirs(cfg).logs, reviewId);
    ensureDir(logDir);
    // Per-run directory: run outputs never mix with another execution's evidence.
    const runDir = path.join(logDir, `run-${runToken}`);
    ensureDir(runDir);
    const resultAbs = resolvedResultRel ? path.resolve(snapAbs, resolvedResultRel) : null;
    const resultBefore = fileState(resultAbs);

    const started = nowIso();
    const result = spawnSync(cmdResolved[0], cmdResolved.slice(1), {
      cwd: snapAbs,
      encoding: 'utf8',
      env: {
        ...process.env,
        COLLAB_REVIEW_ID: reviewId,
        COLLAB_RUN_TOKEN: runToken,
        COLLAB_RUN_DIR: runDir,
        COLLAB_DELIVERY_ID: review.deliveryId,
        COLLAB_SNAPSHOT: review.snapshotPath,
        COLLAB_SNAPSHOT_ABS: snapAbs,
      },
    });
    const finished = nowIso();
    // Evidence is written once per run, into that run's own directory, with
    // exclusive creation: a later run can never overwrite an earlier run's bytes,
    // and the recorded hash always describes what this process actually saw.
    const stdoutPath = path.join(runDir, 'stdout.txt');
    const stderrPath = path.join(runDir, 'stderr.txt');
    writeEvidenceOnce(stdoutPath, result.stdout || '');
    writeEvidenceOnce(stderrPath, result.stderr || '');
    const stdoutState = fileState(stdoutPath);
    const stderrState = fileState(stderrPath);

    // Bind the declared result file to this run: it must have been (re)produced
    // by this process, and its content hash is frozen into the run record.
    const resultAfter = fileState(resultAbs);
    const resultBinding = resolvedResultRel
      ? {
          path: resolvedResultRel,
          runToken,
          profile: profileName,
          deliveryId: review.deliveryId,
          before: resultBefore,
          after: resultAfter,
          producedByRun:
            resultAfter.exists &&
            (!resultBefore.exists ||
              resultBefore.sha256 !== resultAfter.sha256 ||
              resultBefore.mtimeMs !== resultAfter.mtimeMs),
          sha256: resultAfter.sha256,
        }
      : null;

    const relToRepo = (abs) => path.relative(cfg._repoRoot, abs).split(path.sep).join('/');
    const run = {
      at: started,
      finishedAt: finished,
      runToken,
      profile: profileName,
      deliveryId: review.deliveryId,
      subjectImplementationFingerprint: delivery.fingerprints.implementation.fingerprint,
      command: cmdResolved,
      cwd: snapAbs,
      exitCode: result.status == null ? 1 : result.status,
      stdoutPath: relToRepo(stdoutPath),
      stderrPath: relToRepo(stderrPath),
      stdout: {
        path: relToRepo(stdoutPath),
        sha256: stdoutState.sha256,
        bytes: stdoutState.bytes,
        runToken,
      },
      stderr: {
        path: relToRepo(stderrPath),
        sha256: stderrState.sha256,
        bytes: stderrState.bytes,
        runToken,
      },
      signal: result.signal || null,
      resultFile: resultBinding,
    };

    // TH-04: re-verify the snapshot that actually executed. Source mutated during
    // the run (or a smuggled file) invalidates this run.
    let postSnapshot = { ok: true, reason: null };
    try {
      verifySnapshotIntegrity(cfg._repoRoot, review.snapshotPath, delivery, {
        resultFileRel: resolvedResultRel,
        runOutputRels,
      });
    } catch (e) {
      postSnapshot = { ok: false, reason: String(e.message || e) };
    }
    run.postSnapshot = postSnapshot;
    if (result.error) run.spawnError = String(result.error.message || result.error);

    // Write back under the per-review lock; never overwrite a terminal state
    // (e.g. cancel committed while this worker was executing) or another token.
    let cancelledDuringRun = false;
    withReviewLock(cfg, reviewId, `run-end:${actor}`, () => {
      const disk = readJson(reviewPath, null) || review;
      const merged = {
        ...disk,
        runs: [...(disk.runs || []), run],
        updatedAt: finished,
        workerPid: null,
        activeRunToken: null,
      };
      if (isTerminalReview(disk)) {
        cancelledDuringRun = true;
        merged.status = disk.status;
        merged.finishedAt = disk.finishedAt;
        merged.cancelReason = disk.cancelReason;
      } else if (disk.activeRunToken && disk.activeRunToken !== runToken) {
        merged.status = disk.status;
        merged.activeRunToken = disk.activeRunToken;
        merged.workerPid = disk.workerPid ?? null;
        run.staleWorkerWrite = true;
      } else if (!postSnapshot.ok) {
        // Not resumable: the executed snapshot no longer matches the delivery.
        merged.status = 'failed';
        merged.finishedAt = finished;
        merged.integrityFailure = postSnapshot.reason;
      } else if (result.error || run.exitCode !== 0) {
        merged.status = 'failed';
        merged.finishedAt = finished;
      } else {
        merged.status = 'running';
      }
      Object.assign(review, merged);
      writeJsonAtomic(reviewPath, merged);
    });
    if (review.status === 'failed' && String(profileName).startsWith('unity')) {
      // A terminal failure means this review will never resume: free the Unity
      // lease so the next review is not blocked by a dead record. Only a lock
      // whose payload names this review is touched.
      releaseUnityLockIfOwned(cfg, reviewId, actor);
    }

    withStateLock(cfg, `review-run-end:${review.taskId}`, () => {
      const task = loadTask(cfg, review.taskId);
      if (!cancelledDuringRun) {
        task.reviewStatus =
          review.status === 'failed' ? 'failed' : review.status === 'running' ? 'running' : task.reviewStatus;
        if (review.status === 'failed') task.devStatus = 'rework';
        task.updatedAt = nowIso();
        writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${task.taskId}.json`), task);
      }
      const state = loadState(cfg);
      if (state.tasksIndex[task.taskId]) {
        state.tasksIndex[task.taskId].reviewStatus = task.reviewStatus;
        state.tasksIndex[task.taskId].devStatus = task.devStatus;
      }
      saveState(cfg, state, { expectedRevision: state.revision });
    });

    recordEvent(cfg, {
      actor,
      action: 'review-run',
      reviewId,
      taskId: review.taskId,
      deliveryId: review.deliveryId,
      summary: `review-run exit=${run.exitCode} cwd=snapshot status=${review.status}`,
      data: {
        exitCode: run.exitCode,
        cwd: snapAbs,
        cancelledDuringRun,
        runToken,
        resultFile: resolvedResultRel,
        resultFileProducedByRun: resultBinding?.producedByRun ?? null,
        resultFileSha256: resultBinding?.sha256 ?? null,
        postSnapshotOk: postSnapshot.ok,
        postSnapshotReason: postSnapshot.reason,
      },
    });
    generateSummaries(cfg);

    if (!postSnapshot.ok) {
      fail(
        EXIT.CHECK_FAILED,
        `executed snapshot changed during the run; review ${reviewId} cannot pass: ${postSnapshot.reason}`,
        { reviewId, runToken, postSnapshot },
      );
    }

    return {
      reviewId,
      exitCode: run.exitCode,
      status: review.status,
      cancelledDuringRun,
      runToken,
      stdoutPath: run.stdoutPath,
      stderrPath: run.stderrPath,
      externalExitCode: run.exitCode,
      cwd: snapAbs,
      resultFile: resultBinding
        ? {
            path: resolvedResultRel,
            producedByRun: resultBinding.producedByRun,
            sha256: resultBinding.sha256,
          }
        : null,
    };
  } finally {
    lease.release();
  }
}

export function cmdReviewFinish(cfg, args) {
  const actor = args.actor || 'codex';
  assertReviewActor(args, 'review-finish');
  const reviewId = args.review;
  if (!reviewId) fail(EXIT.USAGE, 'review-finish requires --review');
  if ((args.passed === true || args.passed === 'true') && !args.evidence) {
    fail(EXIT.FORBIDDEN, 'cannot finish with --passed alone; provide --evidence file');
  }
  const evidencePath = args.evidence;
  if (!evidencePath) fail(EXIT.EVIDENCE, 'review-finish requires --evidence <file>');
  const absEvidence = path.resolve(evidencePath);
  if (!fs.existsSync(absEvidence)) fail(EXIT.EVIDENCE, `evidence file missing: ${absEvidence}`);
  let evidence;
  try {
    evidence = JSON.parse(fs.readFileSync(absEvidence, 'utf8'));
  } catch (e) {
    fail(EXIT.EVIDENCE, `evidence is not valid JSON: ${e.message}`);
  }
  if (!evidence || typeof evidence !== 'object' || Array.isArray(evidence)) {
    fail(EXIT.EVIDENCE, 'evidence must be a JSON object');
  }

  const reviewPath = path.join(collabDirs(cfg).reviews, `${reviewId}.json`);
  return withReviewLock(cfg, reviewId, `finish:${actor}`, () => {
    const review = readJson(reviewPath, null);
    if (!review) fail(EXIT.USAGE, `review not found: ${reviewId}`);
    assertReviewAccess(cfg, args, 'review-finish', review);
    if (isTerminalReview(review)) {
      fail(EXIT.CONFLICT, `review ${reviewId} is immutable terminal status=${review.status}`);
    }

    const delivery = readJson(
      path.join(collabDirs(cfg).deliveries, `${review.deliveryId}.json`),
      null,
    );
    if (!delivery) fail(EXIT.EVIDENCE, 'delivery missing for review');
    const profile = review.profileDefinition || cfg.profiles?.[review.profile] || {};
    // resultFile is bound to the run token of the executed run (TH-03).
    const lastRunEntry = review.runs[review.runs.length - 1] || null;
    const resultFileRel = resolveResultFileRel(profile, lastRunEntry?.runToken);
    const runOutputRels = resolveRunOutputRels(profile);

    // Optional CLI conveniences so operators need not hand-write JSON counters.
    if (args.coverage) {
      evidence.coverage = String(args.coverage)
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)
        .map((pair) => {
          const [id, result = 'passed', ...rest] = pair.split('=');
          return { id: id.trim(), result: result.trim(), evidence: rest.join('=').trim() || 'CLI --coverage' };
        });
    }
    if (!Array.isArray(evidence.coverage) || evidence.coverage.length === 0) {
      fail(EXIT.EVIDENCE, 'evidence.coverage required (or pass --coverage AC1=passed,AC2=passed)');
    }
    if (evidence.resultFileMissing) fail(EXIT.EVIDENCE, 'evidence declares missing result file');

    const wantsPass = evidence.conclusion === 'passed';
    if (evidence.conclusion !== 'passed' && evidence.conclusion !== 'failed') {
      fail(EXIT.EVIDENCE, 'evidence.conclusion must be passed|failed');
    }

    const lastRun = review.runs[review.runs.length - 1];
    if (!lastRun) fail(EXIT.EVIDENCE, 'review-run must execute before review-finish');
    if (!lastRun.stdoutPath || !fs.existsSync(path.join(cfg._repoRoot, lastRun.stdoutPath))) {
      fail(EXIT.EVIDENCE, 'real machine result missing (stdout from review-run)');
    }
    // FR-02: the recorded stdout bytes must still be the bytes this run produced.
    const evidenceCheck = verifyRunEvidence(cfg._repoRoot, lastRun, 'stdout');
    if (!evidenceCheck.ok) {
      fail(
        EXIT.CHECK_FAILED,
        `review-finish refused: ${evidenceCheck.reason}`,
        { reviewId, runToken: lastRun.runToken || null, binding: lastRun.stdout || null },
      );
    }
    const stderrCheck = verifyRunEvidence(cfg._repoRoot, lastRun, 'stderr');
    if (!stderrCheck.ok) {
      fail(
        EXIT.CHECK_FAILED,
        `review-finish refused: ${stderrCheck.reason}`,
        { reviewId, runToken: lastRun.runToken || null, binding: lastRun.stderr || null },
      );
    }
    if (lastRun.exitCode !== 0 && wantsPass) {
      fail(EXIT.EVIDENCE, 'external command non-zero; cannot conclude passed');
    }
    if (args['auto-counters'] || args['from-result']) {
      const auto = parseRunResult(profile, lastRun, cfg._repoRoot);
      if (!auto.counters) {
        fail(EXIT.EVIDENCE, 'cannot auto-fill counters: machine result has no counters');
      }
      evidence.assertionsTotal = auto.counters.total;
      evidence.assertionsFailed = auto.counters.failed;
      evidence.assertionsSkipped = auto.counters.skipped;
    }

    // Counters must be finite non-negative integers, never coerced from strings.
    const rawCounters = {
      total: evidence.assertionsTotal,
      failed: evidence.assertionsFailed,
      skipped: evidence.assertionsSkipped,
    };
    for (const [k, v] of Object.entries(rawCounters)) {
      if (!Number.isInteger(v) || v < 0) {
        fail(EXIT.EVIDENCE, `evidence.${k} must be a non-negative integer (got ${JSON.stringify(v)})`);
      }
    }
    const assertionsTotal = rawCounters.total;
    const assertionsFailed = rawCounters.failed;
    const assertionsSkipped = rawCounters.skipped;
    if (assertionsFailed > assertionsTotal) {
      fail(EXIT.EVIDENCE, 'evidence.assertionsFailed exceeds assertionsTotal');
    }
    if (assertionsSkipped > assertionsTotal) {
      fail(EXIT.EVIDENCE, 'evidence.assertionsSkipped exceeds assertionsTotal');
    }

    if (!evidence.subjectImplementationFingerprint) {
      fail(EXIT.EVIDENCE, 'evidence.subjectImplementationFingerprint required');
    }
    if (
      evidence.subjectImplementationFingerprint !==
      delivery.fingerprints.implementation.fingerprint
    ) {
      fail(EXIT.EVIDENCE, 'evidence fingerprint does not match delivery implementation fingerprint');
    }

    let task = loadTask(cfg, review.taskId);
    if (wantsPass) {
      if (assertionsFailed > 0) fail(EXIT.EVIDENCE, 'cannot pass with failed assertions');
      if (assertionsTotal === 0) fail(EXIT.EVIDENCE, 'cannot pass with zero assertions');
      if (assertionsSkipped >= assertionsTotal) {
        fail(EXIT.EVIDENCE, 'cannot pass when all assertions are skipped');
      }
      const requiredIds = (task.acceptanceCriteria || []).map((a) => a.id);
      for (const id of requiredIds) {
        const row = evidence.coverage.find((c) => c.id === id);
        if (!row || row.result !== 'passed') {
          fail(EXIT.EVIDENCE, `required criterion ${id} not passed (got ${row?.result || 'missing'})`);
        }
      }
      if (!lastRun.runToken || !lastRun.postSnapshot) {
        fail(
          EXIT.EVIDENCE,
          'run is not bound to a run token / post-run snapshot check; re-run this review with the current tool',
        );
      }
    }

    // Parse the actual machine result and bind the conclusion to it (strict).
    const machine = parseRunResult(profile, lastRun, cfg._repoRoot);

    // Recheck the workspace *and* the snapshot that actually executed (TH-04).
    // A snapshot that no longer matches its manifest can never support passed.
    try {
      verifySnapshotIntegrity(cfg._repoRoot, review.snapshotPath, delivery, {
        resultFileRel,
        runOutputRels,
      });
    } catch (e) {
      fail(
        EXIT.CHECK_FAILED,
        `review-finish refused: executed snapshot does not match delivery ${delivery.deliveryId}: ${e.message}`,
        { reviewId, snapshotPath: review.snapshotPath },
      );
    }
    if (wantsPass && lastRun.postSnapshot && lastRun.postSnapshot.ok === false) {
      fail(EXIT.CHECK_FAILED, `review-finish refused: run ${lastRun.runToken} saw the snapshot change: ${lastRun.postSnapshot.reason}`);
    }

    if (wantsPass && machine.counters) {
      const mismatch = ['total', 'failed', 'skipped'].filter(
        (k) => machine.counters[k] !== rawCounters[k],
      );
      if (mismatch.length) {
        fail(
          EXIT.EVIDENCE,
          `evidence counters disagree with machine result on ${mismatch.join(', ')}: ` +
            `machine=${JSON.stringify(machine.counters)} evidence=${JSON.stringify(rawCounters)}`,
        );
      }
    }

    // Unverified machine evidence may never produce passed.
    //  - a profile that declares a result contract, an empty/unparseable result,
    //    or parsed contradicting counters => hard refusal (exit 6);
    //  - otherwise the review is recorded as terminal `blocked` (never passed).
    if (wantsPass && !machine.ok) {
      const decisive =
        machine.hasEvidence || machine.empty || profileDeclaresResultContract(profile);
      if (!decisive) {
        const blocked = {
          ...review,
          status: 'blocked',
          coverage: evidence.coverage,
          assertions: {
            total: assertionsTotal,
            failed: assertionsFailed,
            skipped: assertionsSkipped,
          },
          machineResult: {
            parsed: machine.parsed || null,
            counters: machine.counters || null,
            unverified: true,
            reason: machine.reason || null,
          },
          blockedReason: `machine result does not support an independent verdict: ${machine.reason}`,
          evidencePath: path.relative(cfg._repoRoot, absEvidence).split(path.sep).join('/'),
          finishedAt: nowIso(),
          workerPid: null,
          activeRunToken: null,
        };
        blocked.updatedAt = blocked.finishedAt;
        validateReview(blocked);
        writeJsonAtomic(reviewPath, blocked);
        withStateLock(cfg, `review-finish:${review.taskId}`, () => {
          const t = loadTask(cfg, review.taskId);
          t.reviewStatus = 'blocked';
          t.devStatus = 'rework';
          t.freshness = 'unknown';
          t.freshnessNote = `Review ${reviewId} blocked: profile ${review.profile} produced no verifiable machine result.`;
          t.updatedAt = nowIso();
          writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${t.taskId}.json`), t);
          const state = loadState(cfg);
          if (state.tasksIndex[t.taskId]) {
            state.tasksIndex[t.taskId].reviewStatus = t.reviewStatus;
            state.tasksIndex[t.taskId].devStatus = t.devStatus;
            state.tasksIndex[t.taskId].freshness = t.freshness;
          }
          saveState(cfg, state, { expectedRevision: state.revision });
        });
        releaseUnityLockIfOwned(cfg, reviewId, actor);
        recordEvent(cfg, {
          actor,
          action: 'review-finish',
          taskId: review.taskId,
          deliveryId: review.deliveryId,
          reviewId,
          summary: `review-finish blocked: ${machine.reason}`,
          data: { status: 'blocked', reason: machine.reason },
        });
        generateSummaries(cfg);
        return {
          reviewId,
          status: 'blocked',
          blockedReason: blocked.blockedReason,
          machineResult: blocked.machineResult,
          exitCode: EXIT.EVIDENCE,
        };
      }
      fail(EXIT.EVIDENCE, `machine result does not support passed: ${machine.reason}`);
    }

    // Recheck workspace vs delivery at finish time.
    const roots = taskRoots(cfg, review.taskId);
    const taskNow = loadTask(cfg, review.taskId);
    const currentFp = computeFingerprints(cfg._repoRoot, {
      contractPayload: stableContractPayload(taskNow),
      implementationRoots: roots.implementationRoots,
      verificationRoots: roots.verificationRoots,
    });
    const change = classifyChange(delivery.fingerprints, currentFp);
    const profileChanged = !!review.profileDefinition && JSON.stringify(review.profileDefinition) !== JSON.stringify(cfg.profiles?.[review.profile]);
    if (profileChanged) { change.profileChanged = true; change.needsReverification = true; }

    review.status = wantsPass ? 'passed' : 'failed';
    review.coverage = evidence.coverage;
    review.assertions = {
      total: assertionsTotal,
      failed: assertionsFailed,
      skipped: assertionsSkipped,
    };
    review.machineResult = {
      parsed: machine.parsed || null,
      counters: machine.counters || null,
      unverified: !!machine.unverified,
      reason: machine.reason || null,
    };
    review.evidencePath = path.relative(cfg._repoRoot, absEvidence).split(path.sep).join('/');
    review.finishedAt = nowIso();
    review.updatedAt = review.finishedAt;
    review.workerPid = null;
    review.activeRunToken = null;
    review.cleanGitCheckoutVerified = false;
    review.finishApplicability = {
      workspaceMatchesDelivery: !change.staleForCurrentCode && !change.needsReverification,
      change,
    };
    review.resultEvidence = {
      runToken: lastRun.runToken || null,
      resultFile: lastRun.resultFile || null,
      postSnapshotOk: lastRun.postSnapshot?.ok ?? null,
      snapshotPath: review.snapshotPath,
    };
    validateReview(review);
    writeJsonAtomic(reviewPath, review);

    withStateLock(cfg, `review-finish:${review.taskId}`, () => {
      const task = loadTask(cfg, review.taskId);
      task.reviewStatus = review.status;
      if (review.status === 'passed') {
        task.freshness =
          change.staleForCurrentCode || change.needsReverification ? 'stale' : 'current';
        task.freshnessNote = task.freshness === 'stale'
          ? `Review ${review.reviewId} passed delivery ${review.deliveryId}; workspace already differs — pending re-review.`
          : `Review ${review.reviewId} applies to delivery ${review.deliveryId}.`;
        if (task.freshness === 'stale') task.devStatus = 'rework';
      } else {
        task.devStatus = 'rework';
        task.freshness = 'current';
      }
      task.updatedAt = nowIso();
      writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${task.taskId}.json`), task);
      const state = loadState(cfg);
      if (state.tasksIndex[task.taskId]) {
        state.tasksIndex[task.taskId].reviewStatus = task.reviewStatus;
        state.tasksIndex[task.taskId].devStatus = task.devStatus;
        state.tasksIndex[task.taskId].freshness = task.freshness;
      }
      saveState(cfg, state, { expectedRevision: state.revision });
    });

    const createdIssues = [];
    if (review.status === 'failed' && Array.isArray(evidence.issues)) {
      for (const iss of evidence.issues) {
        const issueId = iss.issueId || newId('ISSUE');
        const issue = {
          schemaVersion: 1,
          issueId,
          taskId: review.taskId,
          deliveryId: review.deliveryId,
          reviewId,
          title: iss.title || 'unnamed issue',
          repro: iss.repro || '',
          expected: iss.expected || '',
          actual: iss.actual || '',
          evidence: iss.evidence || review.evidencePath,
          severity: iss.severity || 'major',
          suggestedOwner: iss.suggestedOwner || 'cursor',
          status: 'open',
          createdAt: nowIso(),
        };
        validateIssue(issue);
        appendJsonRecord(collabDirs(cfg).issues, issueId, issue);
        createdIssues.push(issueId);
        review.issues.push(issueId);
      }
      writeJsonAtomic(reviewPath, review);
    }

    // Release the Unity review lock only if this review owns it.
    releaseUnityLockIfOwned(cfg, reviewId, actor);

    recordEvent(cfg, {
      actor,
      action: 'review-finish',
      taskId: review.taskId,
      deliveryId: review.deliveryId,
      reviewId,
      summary: `review-finish ${review.status}`,
      data: { status: review.status, issues: createdIssues },
    });
    generateSummaries(cfg);
    return {
      reviewId,
      status: review.status,
      issues: createdIssues,
      freshness: loadTask(cfg, review.taskId).freshness,
      revision: loadState(cfg).revision,
      machineResult: review.machineResult,
    };
  });
}

export function cmdReviewCancel(cfg, args) {
  const actor = args.actor || 'codex';
  assertReviewActor(args, 'review-cancel');
  const reviewId = args.review;
  if (!reviewId) fail(EXIT.USAGE, 'review-cancel requires --review');
  const reviewPath = path.join(collabDirs(cfg).reviews, `${reviewId}.json`);
  return withReviewLock(cfg, reviewId, `cancel:${actor}`, () => {
    const review = readJson(reviewPath, null);
    if (!review) fail(EXIT.USAGE, `review not found: ${reviewId}`);
    assertReviewAccess(cfg, args, 'review-cancel', review);
    if (isTerminalReview(review)) {
      fail(EXIT.CONFLICT, `review already terminal: ${review.status}`);
    }
    review.status = 'cancelled';
    review.finishedAt = nowIso();
    review.updatedAt = review.finishedAt;
    review.cancelReason = args.reason || 'cancelled by operator';
    review.workerPid = null;
    review.activeRunToken = null;
    writeJsonAtomic(reviewPath, review);
    withStateLock(cfg, `review-cancel:${review.taskId}`, () => {
      const task = loadTask(cfg, review.taskId);
      task.reviewStatus = 'cancelled';
      task.updatedAt = nowIso();
      writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${task.taskId}.json`), task);
      const state = loadState(cfg);
      if (state.tasksIndex[task.taskId]) state.tasksIndex[task.taskId].reviewStatus = 'cancelled';
      saveState(cfg, state, { expectedRevision: state.revision });
    });
    releaseUnityLockIfOwned(cfg, reviewId, actor);
    recordEvent(cfg, {
      actor,
      action: 'review-cancel',
      reviewId,
      taskId: review.taskId,
      summary: `cancelled ${reviewId}`,
    });
    generateSummaries(cfg);
    return { reviewId, status: 'cancelled' };
  });
}

export function cmdReviewRecover(cfg, args) {
  const actor = args.actor || 'codex';
  assertRoleAction(actor, 'review-recover');
  const { results } = refreshFreshness(cfg, { write: true });
  // Reclaim only this tool's Unity review lease when the review that owns it can
  // no longer run. A live review, another tool's lease, or user Unity projects are
  // never touched.
  const releasedLeases = [];
  const lockPath = path.join(localDirs(cfg).locks, 'unity-review.lock');
  const lockPayload = readJson(lockPath, null);
  if (lockPayload?.reviewId) {
    const owner = readJson(
      path.join(collabDirs(cfg).reviews, `${lockPayload.reviewId}.json`),
      null,
    );
    const endedStatus = ['passed', 'failed', 'cancelled', 'blocked'];
    const terminal = owner ? isTerminalReview(owner) || endedStatus.includes(owner.status) : false;
    const deadWorker = owner?.workerPid ? !isPidAlive(owner.workerPid) : true;
    const collectGarbage = !owner || terminal || (owner.status === 'running' && deadWorker);
    if (collectGarbage) {
      if (releaseUnityLockIfOwned(cfg, lockPayload.reviewId, actor)) {
        releasedLeases.push(lockPayload.reviewId);
      }
    }
  }
  generateSummaries(cfg);
  const interrupted = listJsonRecords(collabDirs(cfg).reviews).filter(
    (r) => r.status === 'interrupted',
  );
  return {
    recovered: interrupted.map((r) => r.reviewId),
    releasedUnityLeases: releasedLeases,
    freshness: results,
    note:
      'Dead review workers marked interrupted; only this tool\'s own unity-review lease is reclaimed. ' +
      'Unity process locks and user instances are never deleted.',
  };
}

export function cmdWatch(cfg, args) {
  return startWatch(cfg, args, cmdSync);
}
