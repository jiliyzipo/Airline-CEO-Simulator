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
  listIssues,
  listReviews,
  loadState,
  loadTask,
  recordEvent,
} from './state.mjs';
import { assertRoleAction, validateIssue } from './schemas.mjs';
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
  if ((has(/\.cs$/) || has(/^tools\/semcheck\//)) && available('compile')) {
    return { profile: 'compile', reason: 'C# sources changed' };
  }
  if (has(/^tools\/slotcheck\//) && available('slot')) {
    return { profile: 'slot', reason: 'slot assertions changed' };
  }
  if (available('smoke')) return { profile: 'smoke', reason: 'documentation-only change' };
  fail(EXIT.USAGE, 'no usable profile configured', { available: Object.keys(cfg.profiles || {}) });
}

const AC_TEMPLATES = {
  'unity-editmode': [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    'Unity EditMode 套件在隔离快照内真实执行，本 run 生成的 NUnit XML 全部用例通过',
    '测试计数与机器结果一致：total>0、failed=0、skipped<total',
  ],
  compile: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '语义编译检查通过：缺失类型/重复定义/编码门禁均为 0 失败',
    '检查报告来自本次 run，且计数与退出码一致',
  ],
  slot: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '槽位逻辑断言全部通过（[PASS] 与 SLOTCHECK 汇总一致）',
    '断言计数与机器结果一致，无跳过掩盖失败',
  ],
  collab: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '协作工具回归套件全部通过（隔离夹具，不启动 Unity）',
    '各子套件退出码为 0 且断言计数与机器结果一致',
  ],
  smoke: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '冒烟 profile 输出可解析的机器结果 JSON 且断言计数合法',
    '文档与索引引用在本快照内可解析（无断链路径）',
  ],
};

export function acceptanceCriteriaFor(profile, title) {
  const texts = AC_TEMPLATES[profile] || AC_TEMPLATES.smoke;
  return texts.map((text, i) => ({ id: `AC${i + 1}`, text: `${text}（${title}）` }));
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
 */
export function machineCoverageFor(task, profile) {
  const expected = new Map(acceptanceCriteriaFor(profile, task.taskId).map((ac) => [ac.id, ac.text]));
  const rows = (task.acceptanceCriteria || []).map((ac) => {
    const id = String(ac.id);
    const want = expected.get(id);
    if (want != null && want === String(ac.text)) return { id, machine: true, reason: null };
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
    const limit = timeoutMs || (needsUnity ? 45 * 60 * 1000 : 10 * 60 * 1000);
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

    const machine = parseRunResult(profile, run, cfg._repoRoot);
    const ok = run.exitCode === 0 && machine.ok === true && !res.error && integrity.ok;
    const reason = ok
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
  return withHandoffLock(cfg, () => {
    ensureDir(handoffDir(cfg));
    const index = readHandoffIndex(cfg);
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
    const file = path.join(handoffDir(cfg), `${entry.deliveryId}.md`);
    fs.writeFileSync(file, renderHandoff(entry), 'utf8');
    return relPosix(cfg._repoRoot, file);
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
  lines.push(
    '',
    '## 验收条件',
    '',
    ...entry.acceptanceCriteria.map((ac) => `- [${ac.id}] ${ac.text}`),
    '',
  );
  const unmapped = (entry.machineCoverage?.rows || []).filter((r) => !r.machine);
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
  lines.push(
    '## 检测命令（Codex 一条命令）',
    '',
    '```powershell',
    `node tools/collab/cli.mjs stage-review --delivery ${entry.deliveryId} --actor codex`,
    '```',
    '',
    '该命令会自动完成 review-start → review-run → 证据 → review-finish，',
    '失败时自动生成 ISSUE 文件，不需要手写证据 JSON。',
  );
  return lines.join('\n');
}

/* ------------------------------------------------------------------ */
/* stage-open                                                          */
/* ------------------------------------------------------------------ */

function boardRows(cfg) {
  const tasks = listJsonRecords(collabDirs(cfg).tasks);
  const deliveries = listJsonRecords(collabDirs(cfg).deliveries);
  const reviews = listReviews(cfg);
  const issues = listIssues(cfg);
  return tasks
    .filter((t) => !t.readOnlyReference)
    .map((t) => {
      const d = deliveries.filter((x) => x.taskId === t.taskId).at(-1) || null;
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
  const pending = readHandoffIndex(cfg).entries.filter((e) => e.status === 'ready');
  return { ...board, report: renderBoard(board, pending) };
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
  const pending = readHandoffIndex(cfg).entries.filter((e) => e.status === 'ready');
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
    lines.push(`| ${t.taskId} | ${t.devStatus}/${t.reviewStatus} | ${t.freshness} | ${t.latestDelivery || '-'} | ${t.openIssues} |`);
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
    machineCoverage: machineCoverageFor(task, profile),
    summary: summaryText,
    preflight: preflight
      ? { ok: preflight.ok, exitCode: preflight.exitCode, reason: preflight.reason, counters: preflight.counters }
      : null,
    createdAt: nowIso(),
  };
  const handoffFile = writeHandoff(cfg, entry);

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
  reportLines.push(
    '',
    preflight && !preflight.ok
      ? '请先修复预检失败项，再重跑 stage-close 生成新的 DEL；本次 DEL 不要交给 Codex。'
      : `交给 Codex 一条命令：node tools/collab/cli.mjs stage-review --delivery ${delivered.deliveryId} --actor codex`,
    '',
    `交接说明：${handoffFile}`,
  );

  return {
    ok: !!(preflight && preflight.ok),
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
    nextCommand: `node tools/collab/cli.mjs stage-review --delivery ${delivered.deliveryId} --actor codex`,
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
  const entries = readHandoffIndex(cfg).entries
    .filter((e) => e.status === 'ready')
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

/** The reviewer's concrete defect, written as a first-class ISSUE record. */
function recordFailureIssue(cfg, { delivery, review, task, run, machine, runError, profile }) {
  const issue = {
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

  // Authorize before any convenience return so "already reviewed" can never be
  // used to bypass permission on the delivery (SA05).
  let authRecord = null;
  if (actor === 'cursor') {
    authRecord = reviewerAuthorization(cfg, target, actor, 'review-start', args.authorization, profile);
  }

  const existing = terminalReviewFor(cfg, target.deliveryId);
  if (existing && !force) return settledResult(cfg, target, existing);

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
    // Re-check under the lease: the previous owner may have finished meanwhile.
    const settled = terminalReviewFor(cfg, target.deliveryId);
    if (settled && !force) return settledResult(cfg, target, settled);
    return executeStageReview(cfg, { actor, delivery: target, profile, args, authRecord });
  } finally {
    lease.release();
  }
}

function executeStageReview(cfg, { actor, delivery, profile, args, authRecord = null }) {
  const started = cmdReviewStart(cfg, {
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
  // SA03: the machine result only vouches for clauses this profile generated.
  const coverage = review
    ? machineCoverageFor(task, review.profile || profile)
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
/* stage-loop: one entry point that does not need either side awake    */
/* ------------------------------------------------------------------ */

export function cmdStageReport(cfg) {
  const board = cmdStageBoard(cfg);
  const index = readHandoffIndex(cfg);
  const pending = index.entries.filter((e) => e.status === 'ready');
  return {
    ...board,
    handoffs: index.entries.slice(-10),
    report: renderBoard(board, pending),
  };
}
