/**
 * Stage automation: the "one sentence per side" loop.
 *
 * The manual flow needs a dozen commands and hand-written JSON per checkpoint.
 * This module keeps every guarantee of the underlying protocol (append-only
 * snapshots, scoped roles, run-token evidence, single source of truth for
 * defects) but turns each side of the loop into one command:
 *
 *   Cursor:  stage-close   -> task definition (auto) + summary (auto) + DEL
 *                             + preflight run of the deciding profile
 *                             + handoff note for the reviewer
 *   Codex:   stage-review  -> review-start + review-run + evidence + finish
 *                             + ISSUE files on failure
 *
 * Deliberate non-goals:
 *  - It never forges another actor. `stage-close` is cursor-only and
 *    `stage-review` refuses to run as cursor without a scoped authorization.
 *  - It never re-runs a finished review or rewrites a record.
 *  - A preflight run is a developer self-check. It can only block a handoff;
 *    it can never produce a passed verdict.
 */
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { collabDirs, localDirs, relPosix } from './paths.mjs';
import { EXIT, fail } from './exit.mjs';
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
import {
  IMPLEMENTATION_ROOTS,
  VERIFICATION_ROOTS,
  collectEntries,
  dedupeRoots,
  gitMeta,
  isArtifactDirName,
  isGeneratedFileName,
  snapshotRootsFor,
} from './fingerprint.mjs';

const dedupePaths = dedupeRoots;
import { resolveTaskRoots } from './roots.mjs';
import {
  generateSummaries,
  listDeliveries,
  listIssues,
  listReviews,
  loadState,
  loadTask,
  markDeliveriesSuperseded,
  recordEvent,
  retiredDeliveryIds,
  deliveryRetirement,
  saveState,
  saveTask,
  withdrawDelivery,
  withStateLock,
} from './state.mjs';
import { acceptanceCriteriaFor, clauseDriftWarning } from './clauses.mjs';
export { acceptanceCriteriaFor, clauseDrift, clauseDriftWarning } from './clauses.mjs';import { assertRoleAction, validateIssue } from './schemas.mjs';
import { pruneSnapshotArtifacts } from './snapshot-hygiene.mjs';
import {
  cmdAck,
  cmdDeliver,
  cmdReviewFinish,
  cmdReviewRun,
  cmdReviewStart,
  cmdSync,
  cmdTaskAdd,
  parseRunResult,
  resolveRunOutputRels,
  unityRunning,
  verifySnapshotIntegrity,
} from './commands.mjs';
import { reviewerAuthorization } from './authorization.mjs';
import { notifyHandoffReady } from './handoff-notify.mjs';

const norm = (p) => String(p).replace(/\\/g, '/');
const sha256Bytes = (buf) => crypto.createHash('sha256').update(buf).digest('hex');

/* ------------------------------------------------------------------ */
/* Workspace inspection                                                */
/* ------------------------------------------------------------------ */

const AREA_RULES = [
  [/^Unity Project\/Assets\/Scripts\/([^/]+)\//, (m) => m[1]],
  [/^Unity Project\/Assets\/([^/]+)\//, (m) => `Assets-${m[1]}`],
  [/^Unity Project\/Packages\//, () => 'Packages'],
  [/^Unity Project\/ProjectSettings\//, () => 'ProjectSettings'],
  [/^tools\/collab\//, () => 'Collab'],
  [/^tools\/semcheck\//, () => 'Semcheck'],
  [/^tools\/slotcheck\//, () => 'Slotcheck'],
  [/^tools\/regression\//, () => 'Regression'],
  [/^tools\/balance\//, () => 'Balance'],
  [/^tools\//, () => 'Tools'],
];

function areaOf(file) {
  const p = norm(file);
  for (const [re, fn] of AREA_RULES) {
    const m = p.match(re);
    if (m) return fn(m);
  }
  return 'Docs';
}

function slug(text) {
  const cleaned = String(text).replace(/[^A-Za-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return (cleaned || 'Stage').slice(0, 24);
}

function latestDeliveryForTask(cfg, taskId) {
  const rows = listJsonRecords(collabDirs(cfg).deliveries)
    .filter((d) => d.taskId === taskId)
    .sort((a, b) => (Date.parse(a.createdAt) || 0) - (Date.parse(b.createdAt) || 0));
  return rows.at(-1) || null;
}

function scopeRoots(cfg, task) {
  const roots = resolveTaskRoots(cfg, task);
  return snapshotRootsFor(roots.implementationRoots, roots.verificationRoots);
}

/**
 * What actually changed for this task since its last delivery.
 *
 * With an earlier delivery the comparison is hash-based on the same snapshot
 * roots, so a file that was edited and reverted is not "changed". Without one
 * (first delivery of a stage) git porcelain is the only honest signal.
 */
export function diffSinceDelivery(cfg, task) {
  const roots = scopeRoots(cfg, task);
  const previous = latestDeliveryForTask(cfg, task.taskId);
  const current = collectEntries(cfg._repoRoot, roots);
  const currentMap = new Map(current.map((e) => [e.path, e.sha256]));

  if (previous) {
    const previousMap = new Map();
    for (const part of ['implementation', 'verification']) {
      for (const e of previous.fingerprints?.[part]?.entries || []) previousMap.set(norm(e.path), e.sha256);
    }
    const changed = [];
    for (const [file, sha] of currentMap) {
      if (!previousMap.has(file)) changed.push({ path: file, status: 'added' });
      else if (previousMap.get(file) !== sha) changed.push({ path: file, status: 'modified' });
    }
    for (const file of previousMap.keys()) {
      if (!currentMap.has(file)) changed.push({ path: file, status: 'removed' });
    }
    return { base: previous.deliveryId, roots, changed, previous };
  }

  const git = gitMeta(cfg._repoRoot);
  const inScope = (p) => roots.some((r) => p === r || p.startsWith(r + '/'));
  const normalizeStatus = (raw) => {
    const s = String(raw).trim();
    if (!s || s === '??' || s.startsWith('A')) return 'added';
    if (s.startsWith('D')) return 'removed';
    return 'modified';
  };
  const changed = (git.changes || [])
    .map((c) => ({ path: norm(c.path), status: normalizeStatus(c.status) }))
    .filter((c) => inScope(c.path))
    .sort((a, b) => a.path.localeCompare(b.path));
  if (changed.length > 0) return { base: 'git', roots, changed, previous: null };

  // No VCS signal at all (fixtures / exported trees): the whole scope is the
  // honest changed set, because nothing can be compared to a baseline.
  if (!git.head) {
    return {
      base: 'scope',
      roots,
      changed: current.map((e) => ({ path: e.path, status: 'present' })),
      previous: null,
    };
  }

  // Docs-only stage: still deliverable, but clearly labelled.
  const fallback = (git.changes || [])
    .map((c) => ({ path: norm(c.path), status: normalizeStatus(c.status) }))
    .filter((c) => !c.path.split('/').some((seg) => isArtifactDirName(seg)))
    .filter((c) => !isGeneratedFileName(path.basename(c.path)))
    .sort((a, b) => a.path.localeCompare(b.path));
  return { base: 'git-all', roots, changed: fallback, previous: null };
}

function summaryByArea(changed) {
  const map = new Map();
  for (const row of changed) {
    const area = areaOf(row.path);
    const entry = map.get(area) || { area, added: 0, modified: 0, removed: 0 };
    if (row.status.startsWith('A') || row.status === 'added') entry.added += 1;
    else if (row.status.startsWith('D') || row.status === 'removed') entry.removed += 1;
    else entry.modified += 1;
    map.set(area, entry);
  }
  return [...map.values()].sort((a, b) => b.added + b.modified + b.removed - (a.added + a.modified + a.removed));
}

/* ------------------------------------------------------------------ */
/* Profile + acceptance criteria selection                             */
/* ------------------------------------------------------------------ */

const PROFILE_ORDER = ['unity-editmode', 'compile', 'slot', 'collab', 'smoke'];

/**
 * What the deciding profile itself reads. A snapshot that omits these cannot
 * run its own check, so an auto-created stage declares them explicitly instead
 * of hoping the default roots happen to cover them.
 *
 * `smoke` reads the CLI and the collaboration protocol; a docs-only stage has
 * neither in its changed set, so it must ask for them.
 */
const PROFILE_SUPPORT_ROOTS = {
  smoke: ['tools/collab'],
};
const PROFILE_SUPPORT_FILES = {
  smoke: ['docs/collaboration/PROTOCOL.md'],
};

/**
 * Snapshot scope implied by the changed files.
 *
 * Without this, a docs-only stage got the code roots and a snapshot that did
 * not contain a single delivered document: the delivery claimed a change the
 * reviewer could not see, and the smoke preflight failed on a file the export
 * never copied. Directories are taken two segments deep ('Unity Project/Assets',
 * 'docs/handoff'), root-level files are pinned by path because their directory
 * is the whole repository.
 */
export function scopeFromChanges(changed) {
  const roots = new Set();
  const files = new Set();
  for (const row of changed) {
    const p = norm(row.path);
    if (!p) continue;
    const segments = p.split('/').filter(Boolean);
    if (segments.length <= 1) files.add(p);
    else roots.add(segments.slice(0, 2).join('/'));
  }
  return { roots: [...roots].sort(), files: [...files].sort() };
}

/**
 * Pick the single deciding profile for the stage.
 *
 * One review can only carry one profile, and `review-finish` requires every
 * acceptance criterion to be passed by that review. So the stage must be
 * designed around one deciding check instead of pretending four profiles
 * each cover all criteria.
 */
export function selectProfile(cfg, changed, options = {}) {
  const paths = changed.map((c) => norm(c.path));
  const has = (re) => paths.some((p) => re.test(p));
  const available = (name) => !!cfg.profiles?.[name];

  if (options.profile) {
    if (!available(options.profile)) {
      fail(EXIT.USAGE, `unknown profile: ${options.profile}`, { available: Object.keys(cfg.profiles || {}) });
    }
    return { profile: options.profile, reason: 'explicit --profile' };
  }

  const wantsUnity = has(/^Unity Project\//);
  const unityUsable = wantsUnity && available('unity-editmode') && options.unity !== false;
  if (unityUsable) {
    return { profile: 'unity-editmode', reason: 'Unity assets/source changed' };
  }
  if (has(/^tools\/collab\//) && available('collab')) {
    return { profile: 'collab', reason: 'collaboration tooling changed' };
  }
  if (has(/^tools\/balance\//) && available('balance')) {
    return { profile: 'balance', reason: 'balance/accounting simulator changed' };
  }
  if ((has(/\.cs$/) || has(/^tools\/semcheck\//)) && available('compile')) {
    return { profile: 'compile', reason: 'C# sources changed' };
  }
  if (has(/^tools\/slotcheck\//) && available('slot')) {
    return { profile: 'slot', reason: 'slot assertions changed' };
  }
  if (available('smoke')) return { profile: 'smoke', reason: 'documentation-only change' };
  fail(EXIT.USAGE, 'no usable profile configured', { available: Object.keys(cfg.profiles || {}) });
}

/**
 * Which acceptance criteria can this profile actually *prove*?
 *
 * SA03: a machine counter total/failed/skipped cannot demonstrate an arbitrary
 * business condition, a manual step, or the existence of an artifact. So an AC
 * is machine-covered only when it is byte-identical to the clause this profile's
 * template generated for this exact task. Anything hand-written, edited, added
 * or reordered breaks the match and must be verified explicitly by a human
 * reviewer — the automation stays silent instead of inventing a pass.
 *
 * `counters` closes the same hole from the other side: a clause that promises
 * "the suite passed" cannot be proved by a run in which some assertions were
 * skipped because the environment could not execute them. Skipped > 0 therefore
 * downgrades every machine-mapped clause to "needs the reviewer's own words".
 */
export function machineCoverageFor(task, profile, counters = null) {
  const skipped = Number.isInteger(counters?.skipped) ? counters.skipped : 0;
  const expected = new Map(acceptanceCriteriaFor(profile, task.taskId).map((ac) => [ac.id, ac.text]));
  const rows = (task.acceptanceCriteria || []).map((ac) => {
    const id = String(ac.id);
    const want = expected.get(id);
    if (want != null && want === String(ac.text)) {
      if (skipped > 0) {
        return {
          id,
          machine: false,
          reason: `本次运行有 ${skipped} 项断言因环境不可执行而跳过，机器计数不能证明 ${id}`,
        };
      }
      return { id, machine: true, reason: null };
    }
    return {
      id,
      machine: false,
      reason:
        want == null
          ? `profile ${profile} 没有为 ${id} 生成机器条款`
          : `${id} 已被改写为自定义条款，机器计数无法证明它`,
    };
  });
  return {
    ok: rows.length > 0 && rows.every((r) => r.machine),
    rows,
    reason: rows.filter((r) => !r.machine).map((r) => r.reason).join('；') || null,
  };
}

/* ------------------------------------------------------------------ */
/* Stage task resolution                                               */
/* ------------------------------------------------------------------ */

function isTaskBookClause(cfg, taskId) {
  return !!cfg.taskBook?.tasks?.[taskId];
}

function stageTasks(cfg) {
  return listJsonRecords(collabDirs(cfg).tasks)
    .filter((t) => /^S\d{2}-/.test(t.taskId) || String(t.sourceTaskBook || '').startsWith('stage:'))
    .filter((t) => !isTaskBookClause(cfg, t.taskId) && t.taskId !== cfg.demoTaskId)
    .filter((t) => !t.readOnlyReference)
    .sort((a, b) => (Date.parse(a.createdAt) || 0) - (Date.parse(b.createdAt) || 0));
}

function isOpenEnoughToContinue(task) {
  // 已被取代的任务不是「进行中」，是「已退场」。它剩下的交付本来就该由继任者覆盖，
  // 续做它只会逼着人给一份死代码 --refresh-ac，然后把陈旧的验收条件重新签一遍。
  // S26 → S27 的跨任务取代就踩过：`stage-close` 不带 --task 时把 S26 当成最新未完成任务，
  // 于是拒绝在 unity-editmode 下开新阶段，只报「AC 是 smoke 生成的」。
  if (task.supersededBy) return false;
  if (task.reviewStatus === 'passed') return false;
  return ['failed', 'queued', 'interrupted', 'blocked', 'running', 'not_run'].includes(task.reviewStatus) ||
    ['developing', 'rework', 'todo'].includes(task.devStatus);
}

function nextStageTaskId(cfg, areas) {
  const existing = stageTasks(cfg);
  let n = existing.length + 1;
  for (;;) {
    const id = `S${String(n).padStart(2, '0')}-${slug(areas[0] || 'Stage')}`;
    if (!fs.existsSync(path.join(collabDirs(cfg).tasks, `${id}.json`))) return id;
    n += 1;
  }
}

function writeDefinition(cfg, definition) {
  const dir = path.join(localDirs(cfg).root, 'stage');
  ensureDir(dir);
  const file = path.join(dir, `${definition.taskId}.definition.json`);
  writeJsonAtomic(file, definition);
  return file;
}

/* ------------------------------------------------------------------ */
/* Preflight: run the deciding profile inside the delivery snapshot     */
/* ------------------------------------------------------------------ */

function fileDigest(abs) {
  if (!abs || !fs.existsSync(abs)) return null;
  const bytes = fs.readFileSync(abs);
  return { sha256: sha256Bytes(bytes), bytes: bytes.length };
}

function tail(text, limit = 2400) {
  const s = String(text || '');
  return s.length <= limit ? s : s.slice(-limit);
}

function sleepSync(ms) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}

/**
 * The shared Unity lease used by `review-run`, acquired by the preflight too.
 *
 * SA02: a preflight must never execute while another review holds the lease, and
 * must re-check the user's editor immediately before spawning. Both checks fail
 * closed: an unverifiable occupancy is a refusal, never an implicit "go".
 */
function acquireUnityPreflightLease(cfg, delivery, runToken, unityCheck) {
  const u = unityCheck();
  if (!u.checked) fail(EXIT.LOCKED, `cannot verify Unity occupancy: ${u.error || 'unknown'}`, u);
  if (u.running) {
    fail(EXIT.LOCKED, 'Unity is occupied; save and close it, then re-run stage-close', {
      pids: u.pids,
      hint: '无需重做别的步骤：保存关闭 Unity 后重跑同一条命令即可',
    });
  }
  const lockPath = path.join(localDirs(cfg).locks, 'unity-review.lock');
  const existing = readJson(lockPath, null);
  if (existing) {
    fail(
      EXIT.LOCKED,
      `Unity review lease is held by ${existing.owner || 'another review'} ` +
        `(reviewId=${existing.reviewId || 'n/a'}); a preflight must not run under it`,
      existing,
    );
  }
  const leaseId = `preflight-${delivery.deliveryId}-${runToken}`;
  const lock = new FileLock(lockPath, `preflight@${os.hostname()}`, {
    reviewId: leaseId,
    kind: 'preflight',
  });
  const got = lock.acquire({ staleMs: 45 * 60 * 1000 });
  if (!got.ok) fail(EXIT.LOCKED, got.message, got);
  return {
    reviewId: leaseId,
    release() {
      // Only release a lease this preflight actually owns.
      const payload = readJson(lockPath, null);
      if (payload && payload.reviewId === leaseId) lock.release({ force: true });
    },
  };
}

/**
 * Run one profile inside the delivery snapshot as a **developer preflight**.
 *
 * Guarantees (SA01/SA02):
 *  - the snapshot is verified against its manifest *before* anything executes;
 *  - a Unity-named profile takes the shared `unity-review.lock` lease and
 *    re-checks editor occupancy immediately before spawning;
 *  - the snapshot is verified again afterwards, so a run that mutates or
 *    smuggles input can never be reported as a clean preflight;
 *  - `ok` is therefore "inputs intact + process succeeded + machine result
 *    supports success", never a verdict.
 */
export function runProfileInSnapshot(cfg, delivery, profileName, options = {}) {
  const { timeoutMs, unityCheck = unityRunning } = options;
  const profile = cfg.profiles?.[profileName];
  if (!profile?.command) fail(EXIT.USAGE, `unknown or incomplete profile: ${profileName}`);
  const snapAbs = path.join(cfg._repoRoot, delivery.snapshotPath);
  if (!fs.existsSync(snapAbs)) fail(EXIT.CHECK_FAILED, `snapshot missing: ${delivery.snapshotPath}`);

  const runToken = crypto.randomBytes(8).toString('hex');
  const runOutputRels = resolveRunOutputRels(profile);
  const resultRel = profile.resultFile
    ? norm(String(profile.resultFile).replace(/\{runToken\}/g, runToken))
    : null;
  const integrityOpts = { resultFileRel: resultRel, runOutputRels };
  const cmd = profile.command.map((a) =>
    String(a).replace(/\{runToken\}/g, runToken).replace(/\{reviewId\}/g, `preflight-${delivery.deliveryId}`),
  );
  const resultAbs = resultRel ? path.resolve(snapAbs, resultRel) : null;
  const before = fileDigest(resultAbs);

  // A delivery that is already damaged must never be executed at all.
  verifySnapshotIntegrity(cfg._repoRoot, delivery.snapshotPath, delivery, integrityOpts);

  const needsUnity = String(profileName).startsWith('unity');
  const lease = needsUnity ? acquireUnityPreflightLease(cfg, delivery, runToken, unityCheck) : null;

  try {
    // The editor may have been reopened between delivery export and preflight.
    if (needsUnity) {
      const u = unityCheck();
      if (!u.checked) {
        fail(EXIT.LOCKED, `cannot verify Unity occupancy immediately before preflight: ${u.error || 'unknown'}`, u);
      }
      if (u.running) {
        fail(EXIT.LOCKED, 'Unity became occupied before the preflight started; save and close it, then re-run', u);
      }
    }

    const logDir = path.join(localDirs(cfg).root, 'logs', `preflight-${delivery.deliveryId}`, `run-${runToken}`);
    ensureDir(logDir);
    const started = Date.now();
    // A Node-only profile is bounded by the per-suite limit in run-all.mjs (8 min);
    // the outer bound must stay above the sum of all suites on a slow, sandboxed
    // runner, or a wall clock becomes a verdict. Unity still gets a long lease.
    const limit = timeoutMs || (needsUnity ? 45 * 60 * 1000 : 20 * 60 * 1000);
    const res = spawnSync(cmd[0], cmd.slice(1), {
      cwd: snapAbs,
      encoding: 'utf8',
      windowsHide: true,
      timeout: limit,
      maxBuffer: 32 * 1024 * 1024,
      env: process.env,
    });
    const durationMs = Date.now() - started;
    const stdout = res.stdout || '';
    const stderr = res.stderr || res.error?.message || '';
    const stdoutFile = path.join(logDir, 'stdout.txt');
    const stderrFile = path.join(logDir, 'stderr.txt');
    fs.writeFileSync(stdoutFile, stdout);
    fs.writeFileSync(stderrFile, stderr);

    const after = fileDigest(resultAbs);
    const run = {
      runToken,
      exitCode: Number.isInteger(res.status) ? res.status : 1,
      cwd: snapAbs,
      stdout: {
        path: relPosix(cfg._repoRoot, stdoutFile),
        sha256: sha256Bytes(Buffer.from(stdout, 'utf8')),
        bytes: Buffer.byteLength(stdout, 'utf8'),
        runToken,
      },
      stderr: {
        path: relPosix(cfg._repoRoot, stderrFile),
        sha256: sha256Bytes(Buffer.from(stderr, 'utf8')),
        bytes: Buffer.byteLength(stderr, 'utf8'),
        runToken,
      },
      stdoutPath: relPosix(cfg._repoRoot, stdoutFile),
      stderrPath: relPosix(cfg._repoRoot, stderrFile),
    };
    if (resultRel) {
      run.resultFile = {
        path: resultRel,
        producedByRun: !!(after && (!before || before.sha256 !== after.sha256)),
        sha256: after?.sha256 || null,
      };
    }

    // SA01: an executed snapshot that no longer matches its manifest cannot be
    // handed off, even if the command itself printed a perfect result.
    let integrity = { ok: true, reason: null, code: null };
    try {
      verifySnapshotIntegrity(cfg._repoRoot, delivery.snapshotPath, delivery, integrityOpts);
    } catch (e) {
      integrity = { ok: false, reason: e.message, code: e.exitCode ?? null };
    }

    // A `unity-*` run leaves ~1.9 GB of `Library` behind, and nothing ever removed
    // it: 23 historical snapshots had quietly reached 42 GB. It is regenerable debris
    // (the manifest never lists it, and the integrity check above ignores it by
    // design), so drop it as soon as the verdict is computed. Declared run outputs —
    // including the NUnit XML that lives inside `Logs/` — are preserved.
    const pruned = pruneSnapshotArtifacts(snapAbs, {
      keep: (delivery.snapshotManifest?.entries || []).map((e) => e.path),
      protect: [resultRel, ...runOutputRels, 'collab-snapshot-manifest.json'],
    });

    const machine = parseRunResult(profile, run, cfg._repoRoot);
    const ok = run.exitCode === 0 && machine.ok === true && !res.error && integrity.ok;    const reason = ok
      ? 'developer preflight passed in the delivery snapshot (not independent acceptance)'
      : !integrity.ok
        ? `snapshot integrity: ${integrity.reason}`
        : machine.reason || res.error?.message || 'profile failed';
    return {
      profile: profileName,
      ok,
      exitCode: run.exitCode,
      durationMs,
      timedOut: !!res.error && /ETIMEDOUT|timed out/i.test(String(res.error.message || '')),
      reason,
      counters: machine.counters || null,
      integrity,
      stdoutPath: run.stdoutPath,
      stderrPath: run.stderrPath,
      stdoutTail: tail(stdout),
      stderrTail: tail(stderr),
      runToken,
      machine,
      artifacts: pruned,
    };
  } finally {
    if (lease) lease.release();
  }
}

/* ------------------------------------------------------------------ */
/* Handoff index                                                       */
/* ------------------------------------------------------------------ */

function handoffDir(cfg) {
  return path.join(cfg._repoRoot, 'out', 'collab', 'handoff');
}

function handoffIndexPath(cfg) {
  return path.join(handoffDir(cfg), 'index.json');
}

function readHandoffIndex(cfg) {
  const idx = readJson(handoffIndexPath(cfg), null);
  return idx && typeof idx === 'object' && Array.isArray(idx.entries) ? idx : { schemaVersion: 1, entries: [] };
}

function writeHandoff(cfg, entry) {
  const { file, superseded } = withHandoffLock(cfg, () => {
    ensureDir(handoffDir(cfg));
    const index = readHandoffIndex(cfg);
    const replaced = index.entries.filter(
      (e) => e.deliveryId !== entry.deliveryId && e.taskId === entry.taskId && e.status === 'ready',
    );
    const rest = index.entries
      .filter((e) => e.deliveryId !== entry.deliveryId)
      .map((e) =>
        e.taskId === entry.taskId && e.status === 'ready'
          ? { ...e, status: 'superseded', supersededBy: entry.deliveryId }
          : e,
      );
    writeJsonAtomic(handoffIndexPath(cfg), {
      schemaVersion: 1,
      updatedAt: nowIso(),
      entries: [...rest, entry],
    });
    const target = path.join(handoffDir(cfg), `${entry.deliveryId}.md`);
    fs.writeFileSync(target, renderHandoff(entry), 'utf8');
    return {
      file: relPosix(cfg._repoRoot, target),
      superseded: replaced.map((e) => ({
        deliveryId: e.deliveryId,
        supersededBy: entry.deliveryId,
        taskId: e.taskId,
      })),
    };
  });
  // The index above is a local cache (`out/` is ignored); the ledger that
  // freshness reads lives in shared CAS state. Record outside the handoff lock:
  // nesting handoff-lock → state-lock inside the closure would deadlock against
  // any path that takes them in the other order.
  if (superseded.length) {
    markDeliveriesSuperseded(cfg, superseded, {
      actor: entry.actor || 'system',
      note: `superseded by ${entry.deliveryId}`,
    });
  }
  return file;
}

/**
 * Copy supersede facts from the local handoff index into the shared ledger.
 *
 * Needed because the index lives under `out/` (gitignored) while the ledger is
 * committed: a fresh clone sees the ledger only. Idempotent, so it is safe to
 * run on every stage close and on demand.
 */
export function reconcileSupersededLedger(cfg, { actor = 'system' } = {}) {
  const index = readHandoffIndex(cfg);
  const superseded = index.entries
    .filter((e) => e.status === 'superseded' && e.supersededBy)
    .map((e) => ({ deliveryId: e.deliveryId, supersededBy: e.supersededBy, taskId: e.taskId }));
  return markDeliveriesSuperseded(cfg, superseded, {
    actor,
    note: 'reconciled from handoff index',
  });
}

/**
 * The handoff index is shared mutable state written by both sides. Every
 * read-modify-write goes through this lock so a concurrent reviewer can never
 * commit an outcome that a parallel closure overwrites (SA04).
 */
function withHandoffLock(cfg, fn) {
  const lockPath = path.join(localDirs(cfg).locks, 'handoff-index.lock');
  const lock = new FileLock(lockPath, `handoff@${os.hostname()}`, { kind: 'handoff-index' });
  const deadline = Date.now() + 20000;
  for (;;) {
    const got = lock.acquire({ staleMs: 5 * 60 * 1000 });
    if (got.ok) break;
    if (Date.now() > deadline) {
      fail(EXIT.CONFLICT, `handoff index is busy: ${got.message}`, got);
    }
    sleepSync(50);
  }
  try {
    return fn();
  } finally {
    lock.release();
  }
}

const HANDOFF_LABEL = {
  ready: '可检测',
  'preflight-failed': '预检未通过（不要送检）',
  accepted: '检测通过',
  rejected: '检测未通过（见缺陷）',
  superseded: '已被新交付取代',
  withdrawn: '已撤回（归属错误，不要检测）',
};

function updateHandoffOutcome(cfg, deliveryId, { reviewId, status, issues }) {
  return withHandoffLock(cfg, () => {
    const index = readHandoffIndex(cfg);
    const entry = index.entries.find((e) => e.deliveryId === deliveryId);
    if (!entry) return null;
    entry.review = { reviewId, status, at: nowIso(), issues: issues || [] };
    entry.status = status === 'passed' ? 'accepted' : 'rejected';
    writeJsonAtomic(handoffIndexPath(cfg), { ...index, updatedAt: nowIso() });
    const file = path.join(handoffDir(cfg), `${deliveryId}.md`);
    fs.writeFileSync(file, renderHandoff(entry), 'utf8');
    return relPosix(cfg._repoRoot, file);
  });
}

function renderHandoff(entry) {
  const lines = [
    `# 交付待检测 · ${entry.deliveryId}`,
    '',
    `- 任务：${entry.taskId} · ${entry.title}`,
    `- 状态：${HANDOFF_LABEL[entry.status] || entry.status}`,
    `- 检测 profile：${entry.profile}`,
    `- 预检：${entry.preflight?.ok ? '通过' : entry.preflight ? '未通过' : '未执行'}`,
    `- 生成时间：${entry.createdAt}`,
  ];
  if (entry.review) {
    lines.push(`- 检测记录：${entry.review.reviewId} → ${entry.review.status}（${entry.review.at}）`);
    if (entry.review.issues?.length) lines.push(`- 缺陷：${entry.review.issues.join('、')}`);
  }
  if (entry.status === 'withdrawn') {
    // A withdrawn delivery must not look runnable. The command block below is
    // what a reviewer actually copies, so it has to disappear here rather than
    // be contradicted by a warning underneath it.
    lines.push(
      '',
      '> ⛔ 本交付已撤回，**不要检测**。',
      `> 撤回时间：${entry.withdrawnAt || '—'} · 操作人：${entry.withdrawnBy || '—'}`,
      `> 原因：${entry.withdrawnReason || '（未记录）'}`,
      '> 交付记录、快照与指纹保留在原处作为历史证据，只是不再属于任何待检测清单。',
      '',
    );
  }
  lines.push(
    '',
    '## 验收条件',
    '',
    ...entry.acceptanceCriteria.map((ac) => `- [${ac.id}] ${ac.text}`),
    '',
  );
  const unmapped = (entry.machineCoverage?.rows || []).filter((r) => !r.machine);
  // S16/AC4: the command below must be one the reviewer can actually run. When a
  // clause cannot be machine-proved, the bare command is guaranteed to fail — so
  // say so in the warning AND ship the exact command that succeeds.
  const coverageArg = unmapped.length
    ? ` --coverage "${unmapped.map((r) => `${r.id}=passed`).join(',')}"`
    : '';
  if (unmapped.length) {
    lines.push(
      '> ⚠ 机器验证覆盖：以下条款不由 profile 自动判定，检测者必须显式补证（`--coverage AC1=passed,...`），',
      '> 否则 stage-review 会把它们记为 not_run 并判定本次检测未通过。',
      '',
      ...unmapped.map((r) => `> - ${r.id}：${r.reason}`),
      '',
    );
  }
  lines.push(
    '## 变更摘要',
    '',
    entry.summary,
    '',
  );
  if (!entry.preflight?.ok) {
    lines.push('## 预检失败详情', '', '```', String(entry.preflight?.reason || '').slice(0, 2000), '```', '');
  }
  if (entry.status === 'withdrawn') return lines.join('\n');
  lines.push(
    '## 检测命令（Codex 一条命令）',
    '',
    '```powershell',
    `node tools/collab/cli.mjs stage-review --delivery ${entry.deliveryId} --actor codex${coverageArg}`,
    '```',
    '',
    '该命令会自动完成 review-start → review-run → 证据 → review-finish，',
    '失败时自动生成 ISSUE 文件，不需要手写证据 JSON。',
  );
  if (unmapped.length) {
    lines.push(
      '',
      `上面的 --coverage 已按顶部警告逐条列出 ${unmapped.length} 个需补证条款，与警告清单一致；`,
      'PowerShell 下必须保留引号，否则逗号会把参数拆成多个 argv。',
      '补证是检测者的签署声明：请先核对每条证据，再执行。',
    );
  }
  return lines.join('\n');
}

/* ------------------------------------------------------------------ */
/* stage-open                                                          */
/* ------------------------------------------------------------------ */

function boardRows(cfg) {
  const tasks = listJsonRecords(collabDirs(cfg).tasks);
  const deliveries = listDeliveries(cfg);
  const reviews = listReviews(cfg);
  const issues = listIssues(cfg);
  // Same rule freshness uses: a retired delivery is not this task's latest work.
  // "Retired" covers both replaced and withdrawn — either way it no longer
  // describes what this task should be judged on.
  const retired = retiredDeliveryIds(loadState(cfg));
  return tasks
    .filter((t) => !t.readOnlyReference)
    .map((t) => {
      const d =
        deliveries.filter((x) => x.taskId === t.taskId && !retired.has(x.deliveryId)).at(-1) || null;
      const r = d ? reviews.filter((x) => x.deliveryId === d.deliveryId).at(-1) || null : null;
      const open = issues.filter((i) => i.taskId === t.taskId && ['open', 'fixing', 'fixed'].includes(i.status));
      return {
        taskId: t.taskId,
        title: t.title,
        devStatus: t.devStatus,
        reviewStatus: r?.status || t.reviewStatus,
        freshness: t.freshness,
        holder: t.holder || null,
        latestDelivery: d?.deliveryId || null,
        latestReview: r?.reviewId || null,
        supersededBy: t.supersededBy || null,
        openIssues: open.length,
      };
    })
    .sort((a, b) => a.taskId.localeCompare(b.taskId));
}

export function cmdStageBoard(cfg) {
  const state = loadState(cfg);
  const board = {
    revision: state.revision,
    lastDeliveryId: state.lastDeliveryId || null,
    tasks: boardRows(cfg),
  };
  const { kept: pending } = pendingHandoffs(cfg, board);
  return { ...board, report: renderBoard(board, pending) };
}

/**
 * 「待 Codex 检测」清单的**唯一**取数口径。
 *
 * 一个交付要出现在清单上，必须三条同时成立：
 *  ① handoff 索引里还是 `ready`（没人检测过、也没被新交付顶掉）；
 *  ② 共享台账 `state.deliveryStatus` 里没被登记为 `superseded` / `withdrawn`；
 *  ③ 它所属的任务没被别的任务取代（跨任务取代时，旧任务整个退场）。
 *
 * 为什么不能只信 ①：handoff 索引在 `out/` 下（gitignored），而 `stage-supersede`
 * 改的是共享台账与任务文件。两边一旦不同步，已取代的交付就会一直挂在清单上，
 * 催人去给旧代码补跑一次检测 —— S26 / `DEL-20261027115400-6ee0b2` 就是这么挂了两天的：
 * 台账与任务文件都写着 superseded，只有本地索引还写着 ready。
 *
 * 撤回同理，且更危险：一份归属错误的交付在清单上多挂一天，就可能有人照着它
 * 跑完检测并签出一份覆盖别人代码的通过记录（DEL-20261028105347-defc40）。
 *
 * 代价只是几次内存查表；换来的是「清单上出现的，都是真的还要测的」。
 */
function pendingHandoffs(cfg, board = null) {
  const state = loadState(cfg);
  const retiredIds = retiredDeliveryIds(state);
  const rows = board?.tasks || boardRows(cfg);
  const supersededTasks = new Set(rows.filter((t) => t.supersededBy).map((t) => t.taskId));

  const kept = [];
  const dropped = [];
  for (const e of readHandoffIndex(cfg).entries) {
    if (e.status === 'withdrawn') continue;
    if (e.status !== 'ready') continue;
    let why = null;
    const ledger = state.deliveryStatus?.[e.deliveryId];
    if (retiredIds.has(e.deliveryId)) {
      why =
        ledger?.status === 'withdrawn'
          ? `已撤回：${ledger.note || '（未记录原因）'}`
          : `台账已登记被 ${ledger?.supersededBy || '后续交付'} 取代`;
    } else if (supersededTasks.has(e.taskId)) {
      why = `任务 ${e.taskId} 已被 ${rows.find((t) => t.taskId === e.taskId)?.supersededBy?.taskId || '后续任务'} 取代`;
    }
    if (why) dropped.push({ ...e, why });
    else kept.push(e);
  }
  return { kept, dropped };
}

/**
 * 把一个任务标成「已被别的任务取代」。
 *
 * 交付被取代 ≠ 任务退场：只有跨任务取代时，旧任务才真的没有活着的交付了。
 * 不写这个标记，任务会永远停在 `queued`，被 `stage-close` 当成「进行中的阶段」
 * 续做，逼人给一份死代码重新生成验收条件 —— S26 就这么被捡回来过。
 *
 * 幂等：同一个继任者重复登记不会改写已有的时间戳与说明。
 */
function retireTask(cfg, { taskId, successor, actor, note }) {
  if (!taskId || taskId === successor.taskId) return { changed: false, supersededBy: null };
  return withStateLock(cfg, `task-supersede:${taskId}`, () => {
    const task = loadTask(cfg, taskId);
    const already =
      task.supersededBy &&
      task.supersededBy.deliveryId === successor.deliveryId &&
      task.supersededBy.taskId === successor.taskId;
    if (already) return { changed: false, supersededBy: task.supersededBy };
    task.supersededBy = {
      deliveryId: successor.deliveryId,
      taskId: successor.taskId,
      actor,
      note,
      at: nowIso(),
    };
    task.updatedAt = nowIso();
    writeJsonAtomic(path.join(collabDirs(cfg).tasks, `${taskId}.json`), task);
    const state = loadState(cfg);
    if (state.tasksIndex[taskId]) state.tasksIndex[taskId].updatedAt = task.updatedAt;
    saveState(cfg, state, { expectedRevision: state.revision });
    return { changed: true, supersededBy: task.supersededBy };
  });
}

/**
 * 把待检测 handoff 条目就地标成 `superseded`。
 *
 * 命中条件：交付本身被点名取代，或它所属的任务整个退场。
 * 只动 `ready` 的条目 —— `accepted` / `rejected` 已经是历史结论，改了就是篡改记录。
 * 返回被清掉的 deliveryId，供调用方在报告里点名。
 */
function sweepSupersededHandoffs(cfg, { deliveryIds = [], taskIds = [] }, supersededBy) {
  const byId = new Set(deliveryIds);
  const byTask = new Set(taskIds);
  if (!byId.size && !byTask.size) return [];
  return withHandoffLock(cfg, () => {
    const index = readHandoffIndex(cfg);
    const hit = index.entries.filter(
      (e) => e.status === 'ready' && (byId.has(e.deliveryId) || byTask.has(e.taskId)),
    );
    if (!hit.length) return [];
    const ids = new Set(hit.map((e) => e.deliveryId));
    const entries = index.entries.map((e) =>
      ids.has(e.deliveryId) ? { ...e, status: 'superseded', supersededBy } : e,
    );
    writeJsonAtomic(handoffIndexPath(cfg), { schemaVersion: 1, updatedAt: nowIso(), entries });
    for (const e of entries) {
      if (!ids.has(e.deliveryId)) continue;
      const file = path.join(handoffDir(cfg), `${e.deliveryId}.md`);
      if (fs.existsSync(file)) fs.writeFileSync(file, renderHandoff(e), 'utf8');
    }
    return [...ids];
  });
}

/**
 * Copy supersede facts from the local handoff index into the shared ledger.
 *
 * The index under `out/` is gitignored; the ledger in `state.json` is committed.
 * Without this, a task whose newest delivery was replaced by one filed under a
 * different task stays stale forever, because its own newest record can never
 * match its tree (S03-Docs / DEL-20261025125421-e0fbe0).
 */
export function cmdStageReconcile(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'sync');
  const result = reconcileSupersededLedger(cfg, { actor });
  const sync = cmdSync(cfg, {});
  return {
    ok: true,
    revision: sync.revision,
    marked: result.marked,
    report: result.marked.length
      ? `已登记 ${result.marked.length} 条被取代的交付：${result.marked.join(', ')}\nrevision=${sync.revision}`
      : `无需登记：交付台账已与 handoff 索引一致。\nrevision=${sync.revision}`,
    exitCode: EXIT.OK,
  };
}

export function cmdStageOpen(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'sync');
  const sync = cmdSync(cfg, {});
  let ack;
  try {
    ack = cmdAck(cfg, { actor, revision: sync.revision });
  } catch (e) {
    if (e.exitCode !== EXIT.CONFLICT) throw e;
    const retry = cmdSync(cfg, {});
    ack = cmdAck(cfg, { actor, revision: retry.revision });
  }
  const board = cmdStageBoard(cfg);
  const { kept: pending } = pendingHandoffs(cfg, board);
  return {
    ...board,
    ack: { actor, revision: ack.revision },
    pendingHandoffs: pending.map((e) => ({ deliveryId: e.deliveryId, taskId: e.taskId, profile: e.profile })),
    report: `同步完成，已以 ${actor} 身份 ack revision ${ack.revision}\n\n${renderBoard(board, pending)}`,
  };
}

function renderBoard(board, pending) {
  const lines = [`当前 revision=${board.revision}`, ''];
  lines.push('| 任务 | 状态 | 复验 | 最新交付 | 未关缺陷 |');
  lines.push('| --- | --- | --- | --- | --- |');
  for (const t of board.tasks) {
    const status = t.supersededBy ? `superseded/${t.reviewStatus}` : `${t.devStatus}/${t.reviewStatus}`;
    const delivery = t.supersededBy
      ? `→ ${t.supersededBy.deliveryId}（${t.supersededBy.taskId}）`
      : t.latestDelivery || '-';
    lines.push(`| ${t.taskId} | ${status} | ${t.freshness} | ${delivery} | ${t.openIssues} |`);
  }
  if (pending.length) {
    lines.push('', '待 Codex 检测：');
    for (const p of pending) lines.push(`- ${p.deliveryId}（${p.taskId} · ${p.profile}）`);
  }
  return lines.join('\n');
}

/* ------------------------------------------------------------------ */
/* stage-close                                                         */
/* ------------------------------------------------------------------ */

/**
 * Which profile generated a task's acceptance criteria?
 *
 * A stage task created by `stage-close` carries an AC set derived from the
 * profile that was picked *then*. Reusing that task under a different profile
 * keeps the old contract, which can be unsatisfiable. S20 is the case in point:
 * a `unity-editmode` run was blocked by an open editor and had already written
 * the task; the retry ran `--profile collab` and delivered
 * DEL-20261027031726-30842b, whose AC2 still demanded "EditMode really ran"
 * for a snapshot that does not even contain the Unity project. Codex could not
 * sign it — correctly.
 *
 * Identified by exact text match against each profile's template, so a
 * hand-written AC set (which matches nothing) is never second-guessed.
 */
function criteriaProfile(cfg, acceptanceCriteria, taskId) {
  const mine = (acceptanceCriteria || []).map((a) => String(a.text || '').trim());
  if (mine.length === 0) return null;
  for (const name of Object.keys(cfg.profiles || {})) {
    const tpl = acceptanceCriteriaFor(name, taskId).map((a) => String(a.text || '').trim());
    if (tpl.length === mine.length && tpl.every((t, i) => t === mine[i])) return name;
  }
  return null;
}

function buildSummary({ task, profile, diff, preflight, preflightPlanned, note, gaps }) {
  const areas = summaryByArea(diff.changed);
  const lines = [
    `阶段：${task.title}`,
    `profile：${profile}`,
    `对比基准：${diff.base}`,
    `改动文件：${diff.changed.length}`,
    `分布：${areas.map((a) => `${a.area}(+${a.added}/~${a.modified}/-${a.removed})`).join('，') || '无'}`,
    '',
    '重点文件：',
    ...diff.changed.slice(0, 25).map((c) => `- ${c.status.startsWith('D') || c.status === 'removed' ? '删除' : c.status.startsWith('A') || c.status === 'added' ? '新增' : '修改'} ${c.path}`),
    ...(diff.changed.length > 25 ? [`- …… 其余 ${diff.changed.length - 25} 个文件见指纹清单`] : []),
    '',
    preflight
      ? `预检（${profile}）：${preflight.ok ? '通过' : '未通过'} · exit=${preflight.exitCode} · ${preflight.reason}`
      : preflightPlanned === false
        ? '预检：未执行（--no-preflight）'
        : '预检：交付后自动在快照内执行，结果见交接单',
  ];
  if (note) lines.push('', `说明：${note}`);
  if (gaps) lines.push('', `已知缺口：${gaps}`);
  return lines.join('\n');
}

export function cmdStageClose(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  assertRoleAction(actor, 'deliver');

  // An explicit retirement of someone else's delivery. Needed because writeHandoff
  // only supersedes handoffs filed under the *same* task: when a delivery was
  // filed under the wrong task, its own task keeps treating it as the newest work
  // and stays stale forever unless a human says "this one is dead" (S03-Docs /
  // DEL-20261025125421-e0fbe0). Validated up front so a typo fails before a
  // delivery is written.
  const declared = String(args.supersedes || '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
  for (const id of declared) requireDelivery(cfg, id);

  const sync = cmdSync(cfg, {});
  try {
    cmdAck(cfg, { actor, revision: sync.revision });
  } catch (e) {
    if (e.exitCode !== EXIT.CONFLICT) throw e;
  }

  const known = cfg.taskBook?.tasks || {};
  let task = null;
  if (args.task) {
    const existing = fs.existsSync(path.join(collabDirs(cfg).tasks, `${args.task}.json`))
      ? loadTask(cfg, args.task)
      : null;
    if (existing) {
      if (existing.readOnlyReference) fail(EXIT.FORBIDDEN, `${args.task} is a read-only reference task`);
      task = existing;
    }
  } else {
    const candidates = stageTasks(cfg).filter(isOpenEnoughToContinue);
    task = candidates.at(-1) || null;
  }

  // First pass only needs the changed set; task scope may not exist yet.
  // `--roots` narrows the snapshot/fingerprint set for self-contained changes
  // (e.g. tools/collab), which keeps a 36k-file Unity tree out of every delivery.
  const requestedRoots = args.roots
    ? String(args.roots).split(',').map((s) => norm(s.trim())).filter(Boolean)
    : null;
  const probeTask = task || (requestedRoots
    ? { taskId: args.task || '__probe__', snapshotRoots: requestedRoots }
    : { taskId: args.task || '__probe__' });
  let diff = diffSinceDelivery(cfg, probeTask);

  if (diff.changed.length === 0 && args.force !== true && args.force !== 'true') {
    fail(EXIT.CHECK_FAILED, `no changes since ${diff.previous?.deliveryId || 'the last delivery'}; nothing to deliver`, {
      taskId: probeTask.taskId,
      previousDelivery: diff.previous?.deliveryId || null,
    });
  }
  if (diff.changed.length > (args['max-files'] ? Number(args['max-files']) : 4000)) {
    fail(EXIT.CHECK_FAILED, `changed file count ${diff.changed.length} looks wrong; use --max-files to override`, {
      changed: diff.changed.length,
    });
  }

  const areas = summaryByArea(diff.changed);
  const areaNames = areas.map((a) => a.area);

  if (!task) {
    const taskId = args.task || nextStageTaskId(cfg, areaNames);
    const pick = selectProfile(cfg, diff.changed, { profile: args.profile, unity: args.unity });
    // Declare what this stage actually delivers plus what its profile reads.
    // Keeping the default roots and adding the changed scope means a code stage
    // behaves exactly as before while a docs stage stops exporting an empty set.
    const derived = scopeFromChanges(diff.changed);
    // `--roots` stays exact: the caller pinned the scope, and silently widening
    // it would make the delivery disagree with the declared intent.
    const snapshotRoots = requestedRoots
      ? requestedRoots
      : dedupePaths([
          ...IMPLEMENTATION_ROOTS,
          ...VERIFICATION_ROOTS,
          ...derived.roots,
          ...(PROFILE_SUPPORT_ROOTS[pick.profile] || []),
        ]);
    const snapshotFiles = dedupePaths([
      ...derived.files,
      ...(requestedRoots ? [] : PROFILE_SUPPORT_FILES[pick.profile] || []),
    ]).filter((f) => !snapshotRoots.some((r) => f === r || f.startsWith(`${r}/`)));
    const definition = {
      taskId,
      title: args.title || `阶段 ${taskId}：${areaNames.slice(0, 3).join('/') || '改动'}`,
      scope: [
        `本阶段改动范围：${areaNames.slice(0, 6).join('、') || '文档'}`,
        `改动文件数 ${diff.changed.length}，基准 ${diff.base}`,
        '不含本阶段未改动的模块与未列入验收条件的重构',
      ],
      acceptanceCriteria: acceptanceCriteriaFor(pick.profile, taskId),
      dependencies: [],
      sourceTaskBook: 'stage:auto',
      snapshotRoots,
      ...(snapshotFiles.length > 0 ? { snapshotFiles } : {}),
    };
    if (args['dry-run']) {
      return {
        ok: false,
        dryRun: true,
        wouldCreateTask: definition,
        profile: pick.profile,
        profileReason: pick.reason,
        changed: diff.changed.length,
        areas: areas.slice(0, 10),
        report: `[dry-run] 将创建任务 ${taskId}，profile=${pick.profile}（${pick.reason}），改动 ${diff.changed.length} 个文件`,
        exitCode: EXIT.OK,
      };
    }
    const file = writeDefinition(cfg, definition);
    cmdTaskAdd(cfg, { actor, definition: file });
    task = loadTask(cfg, taskId);
    // Recompute against the real task roots so nothing outside the declared
    // scope leaks into the summary or the snapshot.
    diff = diffSinceDelivery(cfg, task);
  } else if (!task.acceptanceCriteria?.length) {
    fail(EXIT.USAGE, `task ${task.taskId} has no acceptanceCriteria; fix its definition before delivering`);
  }

  const pick = selectProfile(cfg, diff.changed, { profile: args.profile, unity: args.unity });
  const profile = pick.profile;

  // A reused task keeps the AC it was created with. If those were generated for a
  // different profile, this close would produce a delivery that cannot honestly be
  // reviewed (see `criteriaProfile`). Refuse to write it; `--refresh-ac` is the
  // explicit way to say "same task, new contract".
  const acProfile = criteriaProfile(cfg, task.acceptanceCriteria, task.taskId);
  const refreshAc = args['refresh-ac'] === true || args['refresh-ac'] === 'true';
  if (acProfile && acProfile !== profile && !refreshAc) {
    fail(
      EXIT.USAGE,
      `task ${task.taskId} has acceptance criteria generated for profile ${acProfile}, ` +
        `but this close runs ${profile}; the reviewer could not sign ${acProfile}'s clauses`,
      {
        taskId: task.taskId,
        criteriaProfile: acProfile,
        profile,
        hint:
          `pass --refresh-ac to regenerate the acceptance criteria for ${profile} ` +
          `(records a new task revision), or re-run with --profile ${acProfile}`,
      },
    );
  }
  if (acProfile && acProfile !== profile && refreshAc) {
    const refreshed = acceptanceCriteriaFor(profile, task.taskId);
    recordEvent(cfg, {
      actor,
      action: 'stage-refresh-ac',
      taskId: task.taskId,
      summary: `acceptance criteria re-derived: ${acProfile} -> ${profile}`,
      data: { from: acProfile, to: profile, previous: task.acceptanceCriteria },
    });
    task = { ...task, acceptanceCriteria: refreshed, revision: (task.revision || 1) + 1, updatedAt: nowIso() };
    saveTask(cfg, task);
  }

  // Unity refuses to run while the user's editor holds the project.
  if (String(profile).startsWith('unity') && args['dry-run'] !== true) {
    const u = unityRunning();
    if (!u.checked) fail(EXIT.LOCKED, `cannot verify Unity occupancy: ${u.error}`, u);
    if (u.running) {
      fail(EXIT.LOCKED, 'Unity is open; save and close it, then re-run stage-close', {
        pids: u.pids,
        hint: '无需重做别的步骤：保存关闭 Unity 后重跑同一条命令即可',
      });
    }
  }

  if (args['dry-run']) {
    return {
      ok: false,
      dryRun: true,
      taskId: task.taskId,
      title: task.title,
      profile,
      profileReason: pick.reason,
      changed: diff.changed.length,
      areas: areas.slice(0, 10),
      acceptanceCriteria: task.acceptanceCriteria,
      report: `[dry-run] 任务 ${task.taskId}，profile=${profile}（${pick.reason}），改动 ${diff.changed.length} 个文件`,
      exitCode: EXIT.OK,
    };
  }

  // Auto summary file: operators never hand-write one.
  const preflightPlanned = args['no-preflight'] !== true;
  const summaryText = buildSummary({
    task,
    profile,
    diff,
    preflight: null,
    preflightPlanned,
    note: args.note,
    gaps: args.gaps,
  });
  const summaryDir = path.join(localDirs(cfg).root, 'stage');
  ensureDir(summaryDir);
  const summaryFile = path.join(summaryDir, `${task.taskId}-summary.txt`);
  fs.writeFileSync(summaryFile, summaryText, 'utf8');

  const delivered = cmdDeliver(cfg, {
    task: task.taskId,
    actor,
    'summary-file': summaryFile,
    model: args.model || process.env.COLLAB_MODEL || 'cursor-stage',
    profiles: profile,
    gaps: args.gaps || '',
    'self-test-notes': preflightPlanned
      ? `stage-close preflight: ${profile}`
      : 'stage-close without preflight (developer self-check only)',
    risk: args.risk || 'none stated',
  });

  const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${delivered.deliveryId}.json`));

  let preflight = null;
  if (preflightPlanned) {
    preflight = runProfileInSnapshot(cfg, delivery, profile, {
      timeoutMs: args['timeout-ms'] ? Number(args['timeout-ms']) : undefined,
    });
  }

  const entry = {
    deliveryId: delivered.deliveryId,
    taskId: task.taskId,
    title: task.title,
    profile,
    status: !preflight || preflight.ok ? 'ready' : 'preflight-failed',
    acceptanceCriteria: task.acceptanceCriteria,
    machineCoverage: machineCoverageFor(task, profile, preflight?.counters),
    summary: summaryText,
    preflight: preflight
      ? { ok: preflight.ok, exitCode: preflight.exitCode, reason: preflight.reason, counters: preflight.counters }
      : null,
    supersedes: declared,
    createdAt: nowIso(),
  };
  // Heal the local index → shared ledger gap before writing this entry, so a
  // close on a machine whose `out/` was wiped still leaves the ledger correct.
  reconcileSupersededLedger(cfg, { actor });
  const handoffFile = writeHandoff(cfg, entry);
  let swept = [];
  const retiredTasks = [];
  if (declared.length) {
    const declaredDeliveries = declared.map((id) => ({ id, taskId: requireDelivery(cfg, id).taskId }));
    markDeliveriesSuperseded(
      cfg,
      declaredDeliveries.map((d) => ({
        deliveryId: d.id,
        supersededBy: delivered.deliveryId,
        taskId: d.taskId,
      })),
      { actor, note: `declared superseded at ${delivered.deliveryId} close` },
    );
    // 新交付的 taskId 与旧交付不同时，writeHandoff 的同任务清扫扫不到它；
    // 这里按 deliveryId 直接补扫，免得旧快照继续挂在待检清单上。
    swept = sweepSupersededHandoffs(cfg, { deliveryIds: declared }, delivered.deliveryId);
    // 跨任务取代时同时让旧任务退场：只清交付不动任务，旧任务会继续以「进行中」
    // 的身份被下一次 stage-close 续做 —— 与 stage-supersede 必须同一个口径。
    for (const d of declaredDeliveries) {
      const retire = retireTask(cfg, {
        taskId: d.taskId,
        successor: { deliveryId: delivered.deliveryId, taskId: task.taskId },
        actor,
        note: `declared superseded at ${delivered.deliveryId} close`,
      });
      if (retire.changed) retiredTasks.push(d.taskId);
    }
  }

  recordEvent(cfg, {
    actor,
    action: 'stage-close',
    taskId: task.taskId,
    deliveryId: delivered.deliveryId,
    summary: `stage-close profile=${profile} preflight=${preflight ? (preflight.ok ? 'pass' : 'fail') : 'skipped'}`,
    data: { profile, handoffFile },
  });
  generateSummaries(cfg);

  const reportLines = [
    `交付已生成：${delivered.deliveryId}`,
    `任务：${task.taskId} · ${task.title}`,
    `检测 profile：${profile}（${pick.reason}）`,
    `改动文件：${diff.changed.length}`,
    `预检：${preflight ? (preflight.ok ? '✅ 通过' : '❌ 未通过') : '未执行'}`,
  ];
  if (preflight && !preflight.ok) {
    reportLines.push('', `原因：${preflight.reason}`, '', '输出尾部：', '```', tail(preflight.stdoutTail, 1500), '```');
  }
  // S16/AC7: rewriting a template clause silently demotes it from machine-proved
  // to reviewer-attested. Say so now, while it is still cheap to fix.
  const driftWarning = clauseDriftWarning(cfg, task, profile);
  if (driftWarning) reportLines.push('', driftWarning);
  // S16/AC4: the single command handed to Codex must be the one that succeeds.
  const unmapped = (entry.machineCoverage?.rows || []).filter((r) => !r.machine).map((r) => r.id);
  const coverageArg = unmapped.length ? ` --coverage "${unmapped.map((id) => `${id}=passed`).join(',')}"` : '';
  const nextCommand = `node tools/collab/cli.mjs stage-review --delivery ${delivered.deliveryId} --actor codex${coverageArg}`;
  const ready = !!(preflight && preflight.ok);
  // Local convenience only: clipboard + chime. Never forges Codex; never blocks
  // the delivery. Tests set COLLAB_HANDOFF_NOTIFY=0.
  const notify = ready
    ? notifyHandoffReady({ nextCommand, handoffFile, deliveryId: delivered.deliveryId })
    : { enabled: false, clipboard: false, chime: false, nextCommand: null };

  reportLines.push(
    '',
    preflight && !preflight.ok
      ? '请先修复预检失败项，再重跑 stage-close 生成新的 DEL；本次 DEL 不要交给 Codex。'
      : `交给 Codex 一条命令：${nextCommand}`,
    '',
    `交接说明：${handoffFile}`,
  );
  if (swept.length) {
    reportLines.push('', `已从待检测清单撤下 ${swept.join(', ')}（被本次交付取代）。`);
  }
  if (retiredTasks.length) {
    reportLines.push(`任务 ${retiredTasks.join(', ')} 标记为已被取代，不会再被后续 stage-close 续做。`);
  }
  if (ready && notify.enabled) {
    reportLines.push(
      notify.clipboard
        ? '本机：检测命令已写入剪贴板（到 Codex 会话直接粘贴即可）。'
        : '本机：未能写入剪贴板，请手工复制上面的检测命令。',
      notify.chime ? '本机：已播放交接提示音。' : '本机：提示音未启动（可忽略）。',
      '关闭提示：环境变量 COLLAB_HANDOFF_NOTIFY=0',
    );
  }

  return {
    ok: ready,
    taskId: task.taskId,
    deliveryId: delivered.deliveryId,
    profile,
    profileReason: pick.reason,
    changedFiles: diff.changed.length,
    areas: areas.slice(0, 10),
    preflight: preflight
      ? { ok: preflight.ok, exitCode: preflight.exitCode, reason: preflight.reason, counters: preflight.counters }
      : null,
    handoffFile,
    nextCommand,
    sweptHandoffs: swept,
    retiredTasks,
    notify,
    report: reportLines.join('\n'),
    exitCode: preflight && !preflight.ok ? EXIT.CHECK_FAILED : EXIT.OK,
  };
}

/* ------------------------------------------------------------------ */
/* stage-review (Codex side, one command)                              */
/* ------------------------------------------------------------------ */

function terminalReviewFor(cfg, deliveryId) {
  return listReviews(cfg)
    .filter((r) => r.deliveryId === deliveryId && r.finishedAt)
    .at(-1) || null;
}

function latestPendingDelivery(cfg) {
  // Only deliveries that stage-close explicitly handed off are eligible.
  // Scanning all historical DELs would resurrect legacy snapshots from earlier
  // tool versions and review something the developer never submitted.
  // 走 pendingHandoffs 而非直接读索引：被取代的交付不能再被自动挑中，
  // 否则 Codex 不带 --delivery 跑 stage-review 时会去测一份已经作废的快照。
  const entries = pendingHandoffs(cfg).kept
    .sort((a, b) => (Date.parse(a.createdAt) || 0) - (Date.parse(b.createdAt) || 0));
  for (let i = entries.length - 1; i >= 0; i -= 1) {
    const e = entries[i];
    const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${e.deliveryId}.json`), null);
    if (!delivery) continue;
    const done = listReviews(cfg).some(
      (r) => r.deliveryId === delivery.deliveryId && ['passed', 'failed', 'blocked', 'cancelled'].includes(r.status) && r.finishedAt,
    );
    if (!done) return delivery;
  }
  return null;
}

/**
 * A retired delivery must never gain a new verdict.
 *
 * `pendingHandoffs` keeps retired deliveries out of the **automatic** pick, and
 * that was all the first implementation of `stage-retract` did — treating a
 * filtered list as if it were access control. Codex's AUDIT-R05 showed the gap:
 * after withdrawing a fixture delivery, `stage-review --delivery <DEL>` still
 * created a REV and reported `passed`. A list nobody reads is not a guard.
 *
 * Refusing here also protects the handoff index: `updateHandoffOutcome` writes
 * `accepted`/`rejected` unconditionally, so a review of a retired delivery would
 * quietly overwrite the `withdrawn` / `superseded` entry and put the delivery
 * back in front of whoever reads the handoff next.
 *
 * Called twice by `cmdStageReview` — before the lease (nothing started, no suite
 * run, no lock taken) and again under it, because a withdrawal can land while
 * this process waits for the lock.
 */
function assertNotRetired(cfg, deliveryId) {
  const entry = deliveryRetirement(loadState(cfg), deliveryId);
  if (!entry) return;
  const detail = {
    deliveryId,
    status: entry.status,
    actor: entry.actor || null,
    at: entry.at || null,
  };
  if (entry.status === 'withdrawn') {
    fail(
      EXIT.CONFLICT,
      `${deliveryId} was withdrawn by ${entry.actor || 'unknown'} at ${entry.at || '?'}: ` +
        `${entry.note || 'no reason recorded'} — a withdrawn delivery is never reviewed`,
      detail,
    );
  }
  fail(
    EXIT.CONFLICT,
    `${deliveryId} was superseded by ${entry.supersededBy || 'a later delivery'}; review the successor instead`,
    detail,
  );
}

/** The reviewer's concrete defect, written as a first-class ISSUE record. */
function recordFailureIssue(cfg, { delivery, review, task, run, machine, runError, profile }) {  const issue = {
    schemaVersion: 1,
    issueId: newId('ISSUE'),
    taskId: delivery.taskId,
    deliveryId: delivery.deliveryId,
    reviewId: review.reviewId,
    title: `profile ${profile} 未通过（${delivery.deliveryId}）`,
    repro: `在交付快照运行 profile ${profile}，runToken=${run?.runToken || 'n/a'}`,
    expected: '退出码 0，机器结果 total>0、failed=0，且断言计数与证据一致',
    actual: runError
      ? `运行中断：${runError.message}`
      : `exit=${run?.exitCode ?? 'n/a'}；${machine?.reason || '机器结果不支持通过'}`,
    evidence: run?.stdoutPath || null,
    severity: 'blocker',
    suggestedOwner: 'cursor',
    status: 'open',
    createdAt: nowIso(),
  };
  validateIssue(issue);
  appendJsonRecord(collabDirs(cfg).issues, issue.issueId, issue);
  recordEvent(cfg, {
    actor: review.actor,
    action: 'review-finish',
    taskId: delivery.taskId,
    deliveryId: delivery.deliveryId,
    reviewId: review.reviewId,
    summary: `stage-review failed; issue ${issue.issueId}`,
    data: { status: 'failed', issues: [issue.issueId] },
  });
  return issue;
}

function buildEvidence(cfg, {
  delivery,
  review,
  task,
  machine,
  run,
  wantsPass,
  machineOk,
  coverageRows,
  attestation,
  authRecord,
  actor,
}) {
  const runToken = run?.runToken || null;
  const binding = `profile=${review.profile} runToken=${runToken}`;
  const counterText = machine.counters ? ` counters=${JSON.stringify(machine.counters)}` : '';
  const issues = [];

  // One defect per root cause: a broken profile, and clauses nobody proved.
  if (!machineOk) {
    issues.push({
      title: `profile ${review.profile} 未通过`,
      repro: `在交付快照运行 profile ${review.profile}（runToken=${runToken}）`,
      expected: '退出码 0，且机器结果声明 total>0、failed=0',
      actual: `exit=${run?.exitCode}；${machine.reason || '机器结果不支持通过'}`,
      evidence: run?.stdoutPath || null,
      severity: 'blocker',
      suggestedOwner: 'cursor',
    });
  }
  const unproved = coverageRows.filter((r) => r.machine !== true && r.attested !== true);
  if (unproved.length) {
    issues.push({
      title: '验收条件缺少机器验证，且检测者未显式补证',
      repro: `任务 ${task.taskId} 的验收条件与 profile ${review.profile} 的机器契约不一致`,
      expected: '机器可验证的条款由 profile 覆盖；其余条款必须由检测者用 --coverage 显式声明，或判定失败',
      actual: unproved.map((r) => `${r.id}（${r.reason || '无机器映射'}）`).join('；'),
      evidence: run?.stdoutPath || null,
      severity: 'blocker',
      suggestedOwner: 'codex',
    });
  }

  return {
    conclusion: wantsPass ? 'passed' : 'failed',
    subjectImplementationFingerprint: delivery.fingerprints.implementation.fingerprint,
    // S14/D1: review-finish enforces the full triplet; a test-only supersede keeps
    // the same implementation hash, so it must still bind verification+contract.
    subjectFingerprints: {
      implementation: delivery.fingerprints.implementation.fingerprint,
      verification: delivery.fingerprints.verification.fingerprint,
      contract: delivery.fingerprints.contract.fingerprint,
    },
    assertionsTotal: machine.counters?.total ?? 0,
    assertionsFailed: machine.counters?.failed ?? 0,
    assertionsSkipped: machine.counters?.skipped ?? 0,
    coverage: coverageRows.map((r) => ({
      id: r.id,
      // SA03: an unproved criterion is never written as passed.
      result: wantsPass ? 'passed' : machineOk ? 'not_run' : 'failed',
      evidence:
        `${binding}${counterText}` +
        (r.machine ? ' | machine-mapped' : r.attested ? ' | reviewer-attested' : ' | no machine mapping'),
    })),
    ...(attestation.length
      ? {
          attestation: {
            by: actor,
            at: nowIso(),
            clauses: attestation,
            ...(authRecord?.sha256 ? { authorizationSha256: authRecord.sha256 } : {}),
          },
        }
      : {}),
    note: wantsPass
      ? 'stage-review: passed is bound to this run token, snapshot hash, machine counters, and an acceptance contract that is either machine-mapped or explicitly attested by the named reviewer.'
      : 'stage-review: failure recorded with concrete ISSUEs; no criterion is claimed passed without a machine mapping or an explicit reviewer attestation.',
    ...(issues.length ? { issues } : {}),
  };
}

/**
 * Explicit reviewer attestation for clauses a profile cannot prove.
 * Format: `--coverage AC1=passed,AC2=passed`. This is the only way an
 * unmapped clause can reach `passed`, and the claim is recorded with the
 * reviewer's identity (and the pinned grant hash on a reverse review).
 */
function parseAttestation(raw, criteria) {
  if (!raw) return [];
  const known = new Set(criteria.map((ac) => ac.id));
  const rows = [];
  for (const pair of String(raw).split(',').map((s) => s.trim()).filter(Boolean)) {
    const [id, result = ''] = pair.split('=');
    if (!known.has(id)) fail(EXIT.USAGE, `--coverage names an unknown criterion: ${id}`, { known: [...known] });
    if (result !== 'passed') {
      fail(EXIT.USAGE, `--coverage only accepts id=passed (got ${id}=${result || '(empty)'})`);
    }
    rows.push({ id, result: 'passed' });
  }
  return rows;
}

/** Load one delivery, or fail with a precise reason. */
function requireDelivery(cfg, deliveryId) {
  const delivery = readJson(path.join(collabDirs(cfg).deliveries, `${deliveryId}.json`), null);
  if (!delivery) fail(EXIT.USAGE, `delivery not found: ${deliveryId}`);
  return delivery;
}

function settledResult(cfg, delivery, review, extra = {}) {
  return {
    ok: review.status === 'passed',
    deliveryId: delivery.deliveryId,
    reviewId: review.reviewId,
    status: review.status,
    alreadyReviewed: true,
    issues: review.issues || [],
    report: `${delivery.deliveryId} 已有终态检测：${review.reviewId} → ${review.status}（未重复执行）`,
    exitCode: review.status === 'passed' ? EXIT.OK : EXIT.CHECK_FAILED,
    ...extra,
  };
}

/**
 * The reviewer's one-command loop: start + run + evidence + finish.
 *
 * SA04: the whole selection/creation/execution/outcome-commit sequence runs under
 * a per-delivery lease, and the terminal-state check is repeated *inside* it.
 * Two identical commands can therefore never execute the same delivery twice,
 * while unrelated deliveries keep running in parallel.
 * SA05: a scoped reverse-review grant accepted by review-* is accepted here too,
 * because the role gate defers to the same authorization for actor=cursor.
 */
export function cmdStageReview(cfg, args = {}) {
  const actor = args.actor || 'codex';
  const force = args.force === true || args.force === 'true';

  const named = args.delivery ? requireDelivery(cfg, args.delivery) : null;

  if (actor === 'cursor') {
    // A cursor grant is bound to one DEL, fingerprint set and action list, so an
    // auto-picked delivery can never be covered by it.
    if (!named) {
      fail(EXIT.FORBIDDEN, 'stage-review as cursor must name the delivery covered by --authorization (--delivery <id>)');
    }
    if (!args.authorization) {
      fail(EXIT.FORBIDDEN, 'stage-review as cursor requires a scoped --authorization file');
    }
  } else {
    assertRoleAction(actor, 'review-start');
  }

  const target = named || latestPendingDelivery(cfg);
  if (!target) {
    // Nothing pending: report the last verdict instead of inventing work, so a
    // repeated `stage review` is idempotent rather than a usage error.
    const all = listJsonRecords(collabDirs(cfg).deliveries).sort(
      (a, b) => (Date.parse(a.createdAt) || 0) - (Date.parse(b.createdAt) || 0),
    );
    const last = all.at(-1) || null;
    const done = last ? terminalReviewFor(cfg, last.deliveryId) : null;
    if (done) return settledResult(cfg, last, done);
    fail(EXIT.USAGE, 'no pending delivery to review');
  }

  const profile = args.profile || target.recommendedProfiles?.[0] || 'smoke';

  // Before anything else that could create work: a retired delivery is not a
  // reviewable subject, whether it was named explicitly or picked automatically.
  // Placed ahead of the dry-run return too — a preview that plans a withdrawn
  // delivery would be advice to do something forbidden.
  assertNotRetired(cfg, target.deliveryId);

  // Authorize before any convenience return so "already reviewed" can never be
  // used to bypass permission on the delivery (SA05).
  let authRecord = null;
  if (actor === 'cursor') {
    authRecord = reviewerAuthorization(cfg, target, actor, 'review-start', args.authorization, profile);
  }

  const existing = terminalReviewFor(cfg, target.deliveryId);
  const dryRun = args['dry-run'] === true || args['dry-run'] === 'true';
  if (dryRun) {
    // S15/AC4: a preview must not touch anything. It runs after authorization
    // (so it cannot be used to probe a delivery you may not review), and returns
    // before the lease, review-start, run, issue or ledger write.
    return planStageReview(cfg, { actor, target, profile, existing, args });
  }
  if (existing && !force) return settledResult(cfg, target, existing);

  // S16/AC5: refuse a review that cannot possibly pass, before spending a full
  // suite run and writing a misleading blocker. The clause set is known up front;
  // only the attestation is missing.
  assertReviewable(cfg, { target, profile, args });

  const leasePath = path.join(localDirs(cfg).locks, `stage-review-${target.deliveryId}.lock`);
  const lease = new FileLock(leasePath, `${actor}@${os.hostname()}`, {
    deliveryId: target.deliveryId,
    profile,
    kind: 'stage-review',
  });
  const got = lease.acquire({ staleMs: 2 * 60 * 60 * 1000 });
  if (!got.ok) {
    // Retry semantics: if the owner already produced a verdict, return it.
    const settled = terminalReviewFor(cfg, target.deliveryId);
    if (settled) return settledResult(cfg, target, settled);
    fail(
      EXIT.CONFLICT,
      `delivery ${target.deliveryId} already has an active stage review ` +
        `(${got.existing?.owner || 'unknown'} pid=${got.existing?.pid || '?'}); refusing to execute it twice`,
      { deliveryId: target.deliveryId, activeReview: got.existing || null },
    );
  }

  try {
    // Re-check under the lease: the previous owner may have finished meanwhile,
    // and a withdrawal may have landed while this process waited for the lock.
    assertNotRetired(cfg, target.deliveryId);
    const settled = terminalReviewFor(cfg, target.deliveryId);
    if (settled && !force) return settledResult(cfg, target, settled);
    return executeStageReview(cfg, { actor, delivery: target, profile, args, authRecord });
  } finally {
    lease.release();
  }
}

/**
 * S16/AC5: fail fast on a review that the clause contract already rules out.
 *
 * A delivery whose criteria were hand-written cannot be proved by a machine count,
 * and no amount of running the suite changes that. Running anyway costs minutes,
 * then writes a blocker that says what the caller could have been told immediately.
 * Refusing up front keeps the ledger honest: a review only exists for attempts that
 * could actually conclude.
 */
function assertReviewable(cfg, { target, profile, args }) {
  const task = loadTask(cfg, target.taskId);
  const coverage = machineCoverageFor(task, profile, null);
  const attestedIds = new Set(parseAttestation(args.coverage, task.acceptanceCriteria || []).map((a) => a.id));
  const missing = coverage.rows.filter((r) => !r.machine && !attestedIds.has(r.id)).map((r) => r.id);
  if (!missing.length) return;
  const suggestion = `--coverage "${coverage.rows.map((r) => `${r.id}=passed`).join(',')}"`;
  fail(
    EXIT.USAGE,
    `stage-review would fail by construction: ${missing.join(', ')} cannot be machine-proved for profile ${profile} and no --coverage attestation was given. ` +
      `Nothing was run. Attest after checking the evidence: ${suggestion}`,
    { deliveryId: target.deliveryId, profile, needsAttestation: missing, suggestedCoverage: suggestion },
  );
}

/**
 * S15/AC4: read-only preview of what `stage-review` would do.
 *
 * Reads the delivery, task, profile and snapshot; reports the machine-mapped
 * clauses, the clauses only a reviewer may attest, and whether a verdict already
 * exists. Writes nothing at all — no review, no issue, no revision, no lock.
 */
function planStageReview(cfg, { actor, target, profile, existing, args }) {
  const task = loadTask(cfg, target.taskId);
  const coverage = machineCoverageFor(task, profile, null);
  const attestation = parseAttestation(args.coverage, task.acceptanceCriteria || []);
  const attestedIds = new Set(attestation.map((a) => a.id));
  const rows = coverage.rows.map((row) => ({ ...row, attested: !row.machine && attestedIds.has(row.id) }));
  const missing = rows.filter((r) => !r.machine && !r.attested).map((r) => r.id);

  let snapshot = { ok: true, reason: null };
  try {
    verifySnapshotIntegrity(cfg._repoRoot, target.snapshotPath, target, {});
  } catch (e) {
    snapshot = { ok: false, reason: String(e.message || e) };
  }

  const lines = [
    `[dry-run] 交付 ${target.deliveryId}（任务 ${target.taskId}）`,
    `[dry-run] profile：${profile}`,
    `[dry-run] 快照完整性：${snapshot.ok ? '通过' : `失败：${snapshot.reason}`}`,
    existing
      ? `[dry-run] 已有终态检测 ${existing.reviewId} → ${existing.status}；真实执行会直接返回该结论（除非 --force）`
      : '[dry-run] 尚无终态检测记录',
    `[dry-run] 机器可判定条款：${rows.filter((r) => r.machine).map((r) => r.id).join(', ') || '（无）'}`,
    `[dry-run] 需检测者补证：${rows.filter((r) => !r.machine).map((r) => r.id).join(', ') || '（无）'}`,
    missing.length
      ? `[dry-run] 尚未补证（真实执行会被判 not_run 并因此不通过）：${missing.join(', ')}`
      : '[dry-run] 补证已覆盖全部非机器条款',
    '[dry-run] 本次未写入任何记录。',
  ];

  return {
    ok: true,
    dryRun: true,
    actor,
    deliveryId: target.deliveryId,
    taskId: target.taskId,
    profile,
    snapshot,
    existingReview: existing ? { reviewId: existing.reviewId, status: existing.status } : null,
    machineMapped: rows.filter((r) => r.machine).map((r) => r.id),
    attested: rows.filter((r) => !r.machine && r.attested).map((r) => r.id),
    needsAttestation: missing,
    report: lines.join('\n'),
    exitCode: EXIT.OK,
  };
}

function executeStageReview(cfg, { actor, delivery, profile, args, authRecord = null }) {  const started = cmdReviewStart(cfg, {
    delivery: delivery.deliveryId,
    actor,
    profile,
    ...(args.authorization ? { authorization: args.authorization } : {}),
  });

  let runError = null;
  try {
    cmdReviewRun(cfg, {
      review: started.reviewId,
      actor,
      ...(args.authorization ? { authorization: args.authorization } : {}),
    });
  } catch (e) {
    runError = e;
  }

  const review = readJson(path.join(collabDirs(cfg).reviews, `${started.reviewId}.json`), null);
  const run = review?.runs?.at(-1) || null;
  const task = loadTask(cfg, delivery.taskId);
  const machine = run
    ? parseRunResult(review.profileDefinition || cfg.profiles[profile], run, cfg._repoRoot)
    : { ok: false, reason: runError?.message || 'run did not execute' };
  const machineOk = !!machine.ok && run?.exitCode === 0 && !runError;
  // SA03: the machine result only vouches for clauses this profile generated,
  // and only when the run actually executed every assertion it counted.
  const coverage = review
    ? machineCoverageFor(task, review.profile || profile, machine.counters)
    : { ok: false, rows: [], reason: 'review record unavailable' };
  // SA03: an unmapped clause may reach passed only through an explicit,
  // attributable reviewer attestation (`--coverage AC1=passed`).
  const attestation = parseAttestation(args.coverage, task.acceptanceCriteria || []);
  const attestedIds = new Set(attestation.map((a) => a.id));
  const coverageRows = coverage.rows.map((row) => ({
    ...row,
    attested: !row.machine && attestedIds.has(row.id),
  }));
  const allProved = coverageRows.length > 0 && coverageRows.every((r) => r.machine || r.attested);
  const wantsPass = machineOk && allProved;

  // review-run terminalises the review as `failed` on a non-zero exit, so there
  // is nothing left for review-finish to decide. The reviewer records the defect
  // itself instead of leaving a terminal failure without an actionable ISSUE.
  if (review && review.finishedAt && ['failed', 'interrupted', 'cancelled'].includes(review.status)) {
    const issue = recordFailureIssue(cfg, { delivery, review, task, run, machine, runError, profile });
    updateHandoffOutcome(cfg, delivery.deliveryId, {
      reviewId: review.reviewId,
      status: review.status,
      issues: [issue.issueId],
    });
    return {
      ok: false,
      deliveryId: delivery.deliveryId,
      reviewId: review.reviewId,
      status: review.status,
      profile,
      counters: machine.counters || null,
      issues: [issue.issueId],
      nextCommand: 'node tools/collab/cli.mjs stage-fix',
      report: [
        `交付：${delivery.deliveryId}`,
        `检测：${review.reviewId} → ${review.status}`,
        `profile：${profile}`,
        run ? `退出码：${run.exitCode}` : '',
        machine.reason ? `原因：${machine.reason}` : '',
        '',
        `已自动登记缺陷：${issue.issueId}`,
        '修复入口：node tools/collab/cli.mjs stage-fix',
      ].filter(Boolean).join('\n'),
      exitCode: EXIT.CHECK_FAILED,
    };
  }

  // A failure before execution (lock, snapshot, authorization) is not a verdict.
  if (runError) {
    throw runError;
  }

  const evidence = buildEvidence(cfg, {
    delivery,
    review: { ...started, profile },
    task,
    machine,
    run,
    wantsPass,
    machineOk,
    coverageRows,
    attestation,
    authRecord,
    actor,
  });
  const evidenceDir = path.join(localDirs(cfg).root, 'evidence');
  ensureDir(evidenceDir);
  const evidenceFile = path.join(evidenceDir, `${started.reviewId}-${Date.now()}.json`);
  fs.writeFileSync(evidenceFile, JSON.stringify(evidence, null, 2), 'utf8');

  let finished;
  try {
    finished = cmdReviewFinish(cfg, {
      review: started.reviewId,
      actor,
      evidence: evidenceFile,
      ...(args.authorization ? { authorization: args.authorization } : {}),
    });
  } catch (e) {
    // A failed run usually leaves the review terminal already; surface both.
    const disk = readJson(path.join(collabDirs(cfg).reviews, `${started.reviewId}.json`), null);
    return {
      ok: false,
      deliveryId: delivery.deliveryId,
      reviewId: started.reviewId,
      status: disk?.status || 'unknown',
      error: e.message,
      runError: runError?.message || null,
      preflight: disk?.machineResult || null,
      report: [
        `检测未完成：${started.reviewId}`,
        `原因：${e.message}`,
        runError ? `运行错误：${runError.message}` : '',
      ].filter(Boolean).join('\n'),
      exitCode: e.exitCode || EXIT.CHECK_FAILED,
    };
  }

  const createdIssues = finished.issues || [];
  updateHandoffOutcome(cfg, delivery.deliveryId, {
    reviewId: finished.reviewId,
    status: finished.status,
    issues: createdIssues,
  });

  const lines = [
    `交付：${delivery.deliveryId}`,
    `检测：${finished.reviewId} → ${finished.status}`,
    `profile：${profile}`,
    machine.counters ? `断言：total=${machine.counters.total} failed=${machine.counters.failed} skipped=${machine.counters.skipped}` : '断言：无机器计数',
  ];
  if (createdIssues.length) {
    lines.push('', '自动登记缺陷：', ...createdIssues.map((id) => `- ${id}`));
  }
  lines.push('', finished.status === 'passed' ? '该阶段检测通过。' : '未通过：请在 Cursor 侧运行 stage-fix 查看修复清单。');

  return {
    ok: finished.status === 'passed',
    deliveryId: delivery.deliveryId,
    reviewId: finished.reviewId,
    status: finished.status,
    profile,
    counters: machine.counters || null,
    issues: createdIssues,
    freshness: finished.freshness,
    nextCommand: finished.status === 'passed' ? null : 'node tools/collab/cli.mjs stage-fix',
    report: lines.join('\n'),
    exitCode: finished.status === 'passed' ? EXIT.OK : EXIT.CHECK_FAILED,
  };
}

/* ------------------------------------------------------------------ */
/* stage-fix: the developer's actionable list                          */
/* ------------------------------------------------------------------ */

export function cmdStageFix(cfg, args = {}) {
  const issues = listIssues(cfg).filter((i) => ['open', 'fixing', 'fixed'].includes(i.status));
  const pending = readHandoffIndex(cfg).entries.filter((e) => e.status === 'preflight-failed');

  const reviews = listReviews(cfg);
  const deliveries = listJsonRecords(collabDirs(cfg).deliveries);
  const latestByTask = new Map();
  for (const d of deliveries) {
    const cur = latestByTask.get(d.taskId);
    if (!cur || (Date.parse(d.createdAt) || 0) > (Date.parse(cur.createdAt) || 0)) latestByTask.set(d.taskId, d);
  }
  const failed = [];
  for (const d of latestByTask.values()) {
    const r = reviews.filter((x) => x.deliveryId === d.deliveryId && x.finishedAt).at(-1);
    if (!r || !['failed', 'blocked'].includes(r.status)) continue;
    if (issues.some((i) => i.reviewId === r.reviewId)) continue;
    failed.push(r);
  }

  const lines = [];
  if (!issues.length && !failed.length && !pending.length) {
    lines.push('当前没有需要修复的缺陷。');
  }
  if (issues.length) {
    lines.push(`未关闭缺陷：${issues.length}`, '');
    for (const i of issues) {
      lines.push(`### ${i.issueId} · ${i.title}`);
      lines.push(`- 严重度：${i.severity} · 状态：${i.status} · 归属：${i.suggestedOwner || 'cursor'}`);
      if (i.deliveryId) lines.push(`- 交付：${i.deliveryId}${i.reviewId ? ` · 检测：${i.reviewId}` : ''}`);
      if (i.repro) lines.push(`- 复现：${i.repro}`);
      if (i.expected) lines.push(`- 期望：${i.expected}`);
      if (i.actual) lines.push(`- 实际：${i.actual}`);
      lines.push('');
    }
  }
  if (failed.length) {
    lines.push('未转成缺陷的失败检测：', '');
    for (const r of failed) {
      lines.push(`- ${r.reviewId}（${r.profile}）→ ${r.status}：${r.machineResult?.reason || r.integrityFailure || '见运行输出'}`);
    }
    lines.push('');
  }
  if (pending.length) {
    lines.push('预检未通过的交付（不要送检）：', '');
    for (const e of pending) lines.push(`- ${e.deliveryId}：${e.preflight?.reason || '预检失败'}`);
    lines.push('');
  }

  return {
    ok: !issues.length && !failed.length && !pending.length,
    issues: issues.map((i) => ({ issueId: i.issueId, title: i.title, severity: i.severity, status: i.status })),
    failedReviews: failed.map((r) => r.reviewId),
    blockedDeliveries: pending.map((e) => e.deliveryId),
    nextCommand: 'node tools/collab/cli.mjs stage-close',
    report: lines.join('\n'),
    exitCode: EXIT.OK,
  };
}

/* ------------------------------------------------------------------ */
/* stage-supersede (record a replacement after the fact)               */
/* ------------------------------------------------------------------ */

/**
 * S15/AC6: `stage-close --supersedes` can only declare a replacement while the
 * successor is being written. When a later stage keeps working on the same roots,
 * the earlier delivery is replaced in substance but no command can say so, so its
 * task sits at `passed/stale` and keeps asking for a re-review that would just
 * re-prove the old code.
 *
 * This records the fact after the fact. It never rewrites the old review (the
 * verdict really was passed for that snapshot) and it is idempotent.
 */
export function cmdStageSupersede(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  if (!['cursor', 'codex', 'user'].includes(actor)) {
    fail(EXIT.FORBIDDEN, `unknown actor: ${actor}`, { allowed: ['cursor', 'codex', 'user'] });
  }
  const supersededId = args.delivery;
  const successorId = args.by;
  if (!supersededId) fail(EXIT.USAGE, 'stage-supersede requires --delivery <superseded DEL>');
  if (!successorId) fail(EXIT.USAGE, 'stage-supersede requires --by <successor DEL>');
  if (supersededId === successorId) fail(EXIT.USAGE, 'a delivery cannot supersede itself');
  const superseded = requireDelivery(cfg, supersededId);
  const successor = requireDelivery(cfg, successorId);
  if ((Date.parse(superseded.createdAt) || 0) > (Date.parse(successor.createdAt) || 0)) {
    fail(
      EXIT.CONFLICT,
      `successor ${successorId} was delivered before ${supersededId}; a later delivery cannot be replaced by an earlier one`,
      { superseded: { id: supersededId, at: superseded.createdAt }, successor: { id: successorId, at: successor.createdAt } },
    );
  }

  const note = args.note ? String(args.note) : `declared superseded by ${successorId}`;
  const marked = markDeliveriesSuperseded(
    cfg,
    [{ deliveryId: supersededId, supersededBy: successorId, taskId: superseded.taskId }],
    { actor, note },
  );

  // 同步清理本地 handoff 索引：被取代的交付如果还留在 `ready`，
  // `out/collab/handoff/<DEL>.md` 会继续写着「可检测」，board 也会继续点名要人测。
  // 台账（state.json）与索引（out/）是两处存储，只有在一处写入时顺手扫另一处，
  // 才不会出现「台账说已取代、索引说待检测」的分裂 —— S26 就是这样挂住的。
  let swept = [];
  if (superseded.taskId !== successor.taskId) {
    swept = sweepSupersededHandoffs(cfg, { taskIds: [superseded.taskId] }, successorId);
  }

  // Cross-task: the old task has no live delivery left, so record what replaced it
  // instead of leaving it to look permanently stale.
  const retire =
    superseded.taskId === successor.taskId
      ? { changed: false, supersededBy: null }
      : retireTask(cfg, {
          taskId: superseded.taskId,
          successor: { deliveryId: successorId, taskId: successor.taskId },
          actor,
          note,
        });
  const taskSuperseded = retire.supersededBy;
  const taskChanged = retire.changed;

  if (!marked.marked.length && !taskChanged && !swept.length) {
    const sync = cmdSync(cfg, {});
    return {
      ok: true,
      unchanged: true,
      deliveryId: supersededId,
      supersededBy: successorId,
      taskId: superseded.taskId,
      taskSuperseded,
      sweptHandoffs: [],
      revision: sync.revision,
      report: `已登记过：${supersededId} 已被 ${successorId} 取代；未做改动（revision=${sync.revision}）。`,
      exitCode: EXIT.OK,
    };
  }

  recordEvent(cfg, {
    actor,
    action: 'stage-supersede',
    taskId: superseded.taskId,
    deliveryId: supersededId,
    summary: `${supersededId} superseded by ${successorId}`,
    data: { supersededBy: successorId, successorTaskId: successor.taskId, crossTask: superseded.taskId !== successor.taskId },
  });
  const sync = cmdSync(cfg, {});
  const crossTask = superseded.taskId !== successor.taskId;
  return {
    ok: true,
    deliveryId: supersededId,
    supersededBy: successorId,
    taskId: superseded.taskId,
    taskSuperseded,
    sweptHandoffs: swept,
    revision: sync.revision,
    report: [
      `已登记：${supersededId}（${superseded.taskId}）被 ${successorId}（${successor.taskId}）取代`,
      crossTask
        ? `任务 ${superseded.taskId} 标记为已被取代，不再出现在待办清单里；旧检测记录保持原样。`
        : '同一任务内的取代；任务状态不变，旧检测记录保持原样。',
      swept.length
        ? `已从待检测清单撤下 ${swept.join(', ')}（handoff 索引同步改为 superseded）。`
        : null,
      `revision=${sync.revision}`,
    ]
      .filter(Boolean)
      .join('\n'),
    exitCode: EXIT.OK,
  };
}

/* ------------------------------------------------------------------ */
/* stage-retract                                                       */
/* ------------------------------------------------------------------ */

/**
 * Withdraw a delivery that was filed under the wrong task.
 *
 * Why this is not `stage-supersede`: supersede records *what replaced it* and
 * therefore requires a successor created later. The delivery this exists for
 * (`DEL-20261028105347-defc40`) has no such successor — it belongs to no task at
 * all. Its 104 changed files were the accumulated work of a dozen other stages;
 * the stage it was filed under had no changes of its own left to deliver.
 *
 * Rules, all of them fail-closed:
 *  · a delivery that was ever reviewed cannot be withdrawn — a verdict is
 *    history, and withdrawing it would erase the evidence of what was judged;
 *  · a superseded delivery cannot be withdrawn — "replaced" is already a
 *    stronger, recorded fact;
 *  · a reason is mandatory, because the whole point is to say on the record
 *    why this filing was wrong;
 *  · the reviewer is not an allowed actor. Codex may find that a delivery
 *    should be withdrawn, but erasing the subject of a review is the filing
 *    side's (or the user's) act, never the reviewer's.
 *
 * Nothing is deleted: the delivery record, its snapshot and its handoff file
 * all stay. The ledger gains `withdrawn`, the handoff loses its review command,
 * and the pending list stops naming it.
 */
export function cmdStageRetract(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  if (!['cursor', 'user'].includes(actor)) {
    fail(EXIT.FORBIDDEN, `stage-retract may not be run as ${actor}`, {
      allowed: ['cursor', 'user'],
      why: '检测方不能撤回被检测对象；请由提交方或用户执行',
    });
  }
  const deliveryId = args.delivery;
  if (!deliveryId) fail(EXIT.USAGE, 'stage-retract requires --delivery <DEL>');
  const reason = String(args.reason || '').trim();
  if (!reason) {
    fail(EXIT.USAGE, 'stage-retract requires --reason <text>: a withdrawal without a stated reason is unreadable history', {
      hint: '写明这份交付为什么不该存在（归属错误 / 范围错误 / 误打包他人改动）',
    });
  }

  const delivery = requireDelivery(cfg, deliveryId);
  const state = loadState(cfg);
  const ledger = state.deliveryStatus?.[deliveryId];

  if (ledger?.status === 'superseded') {
    fail(EXIT.CONFLICT, `${deliveryId} is already superseded by ${ledger.supersededBy}; nothing to withdraw`, {
      deliveryId,
      supersededBy: ledger.supersededBy || null,
    });
  }

  const reviews = listReviews(cfg).filter((r) => r.deliveryId === deliveryId);
  const terminal = reviews.filter((r) => ['passed', 'failed'].includes(r.status));
  if (terminal.length) {
    fail(
      EXIT.CONFLICT,
      `${deliveryId} already has a recorded verdict (${terminal.map((r) => r.reviewId).join(', ')}); a reviewed delivery cannot be withdrawn`,
      { deliveryId, reviews: terminal.map((r) => ({ reviewId: r.reviewId, status: r.status })) },
    );
  }
  const inFlight = reviews.filter((r) => !['passed', 'failed', 'cancelled'].includes(r.status));
  if (inFlight.length) {
    fail(EXIT.CONFLICT, `${deliveryId} has a review in progress (${inFlight.map((r) => r.reviewId).join(', ')}); cancel it first`, {
      deliveryId,
      reviews: inFlight.map((r) => ({ reviewId: r.reviewId, status: r.status })),
    });
  }

  const withdraw = withdrawDelivery(cfg, {
    deliveryId,
    taskId: delivery.taskId,
    actor,
    reason,
  });

  // Same two-store problem as supersede: the ledger is shared state, the handoff
  // index lives under `out/`. Leave them split and the board keeps asking someone
  // to review a delivery that must never be reviewed.
  const swept = withHandoffLock(cfg, () => {
    const index = readHandoffIndex(cfg);
    const entry = index.entries.find((e) => e.deliveryId === deliveryId);
    // Only a handoff that nobody has judged may be re-labelled. `accepted` /
    // `rejected` are conclusions; rewriting them would be tampering with history.
    if (!entry || !['ready', 'preflight-failed'].includes(entry.status)) return [];
    entry.status = 'withdrawn';
    entry.withdrawnReason = reason;
    entry.withdrawnAt = nowIso();
    entry.withdrawnBy = actor;
    writeJsonAtomic(handoffIndexPath(cfg), { ...index, updatedAt: nowIso() });
    const file = path.join(handoffDir(cfg), `${deliveryId}.md`);
    if (fs.existsSync(file)) fs.writeFileSync(file, renderHandoff(entry), 'utf8');
    return [deliveryId];
  });

  if (!withdraw.changed && !swept.length) {
    const sync = cmdSync(cfg, {});
    return {
      ok: true,
      unchanged: true,
      deliveryId,
      taskId: delivery.taskId,
      revision: sync.revision,
      report: `已撤回：${deliveryId}（revision=${sync.revision}）；未做改动。`,
      exitCode: EXIT.OK,
    };
  }

  recordEvent(cfg, {
    actor,
    action: 'stage-retract',
    taskId: delivery.taskId,
    deliveryId,
    summary: `withdrawn ${deliveryId}: ${reason}`,
    data: { reason, sweptHandoffs: swept },
  });
  const sync = cmdSync(cfg, {});
  return {
    ok: true,
    deliveryId,
    taskId: delivery.taskId,
    reason,
    sweptHandoffs: swept,
    revision: sync.revision,
    report: [
      `已撤回：${deliveryId}（${delivery.taskId}）`,
      `原因：${reason}`,
      swept.length
        ? `已从待检测清单撤下 ${swept.join(', ')}；交接单改为「已撤回」并删除检测命令。`
        : 'handoff 索引里没有可改的待检测条目（可能已被检测过或从未写出）。',
      `原交付记录、快照与指纹保留不删；任务 ${delivery.taskId} 自身状态不变。`,
      `revision=${sync.revision}`,
    ].join('\n'),
    exitCode: EXIT.OK,
  };
}

/* ------------------------------------------------------------------ */
/* stage-loop: one entry point that does not need either side awake    */
/* ------------------------------------------------------------------ */

export function cmdStageReport(cfg) {
  const board = cmdStageBoard(cfg);
  const index = readHandoffIndex(cfg);
  const { kept: pending } = pendingHandoffs(cfg, board);
  return {
    ...board,
    handoffs: index.entries.slice(-10),
    report: renderBoard(board, pending),
  };
}
