import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { collabDirs, localDirs } from './paths.mjs';
import {
  appendJsonRecord,
  ensureDir,
  FileLock,
  isPidAlive,
  listJsonRecords,
  nowIso,
  readJson,
  writeJsonAtomic,
  writeFileAtomic,
  newId,
} from './io.mjs';
import { validateEvent, validateTask, stableContractPayload } from './schemas.mjs';
import { computeFingerprints, classifyChange } from './fingerprint.mjs';
import { resolveTaskRoots } from './roots.mjs';
import { EXIT, fail } from './exit.mjs';

export function emptyState(cfg) {
  return {
    schemaVersion: 1,
    projectId: cfg.projectId,
    revision: 1,
    updatedAt: nowIso(),
    tasksIndex: {},
    issueStatus: {},
    lastDeliveryId: null,
    lastReviewId: null,
  };
}

export function loadState(cfg) {
  const dirs = collabDirs(cfg);
  return readJson(dirs.state, emptyState(cfg));
}

/** Per-process reentrancy tracking for state.lock (see withStateLock). */
const heldStateLocks = new Map();

export function withStateLock(cfg, owner, fn) {
  const lockPath = path.join(localDirs(cfg).locks, 'state.lock');
  // Reentrant within the same process: nested claim/save inside sync must not deadlock.
  const depth = heldStateLocks.get(lockPath) || 0;
  if (depth > 0) {
    heldStateLocks.set(lockPath, depth + 1);
    try {
      return fn();
    } finally {
      heldStateLocks.set(lockPath, depth);
    }
  }
  const lock = new FileLock(
    lockPath,
    owner || `${process.pid}@${os.hostname()}`,
  );
  const got = lock.acquire({ staleMs: 5 * 60 * 1000 });
  if (!got.ok) fail(EXIT.LOCKED, got.message, got);
  heldStateLocks.set(lockPath, 1);
  try {
    return fn();
  } finally {
    heldStateLocks.set(lockPath, 0);
    lock.release();
  }
}

/**
 * Serialize all mutations of one review record (run / cancel / finish),
 * so a cancelled terminal state can never be overwritten by a completing worker.
 */
export function withReviewLock(cfg, reviewId, owner, fn) {
  const lock = new FileLock(
    path.join(localDirs(cfg).locks, `review-${reviewId}.lock`),
    owner || `review:${process.pid}`,
    { reviewId },
  );
  const got = lock.acquire({ staleMs: 10 * 60 * 1000 });
  if (!got.ok) fail(EXIT.LOCKED, got.message, got);
  try {
    return fn();
  } finally {
    lock.release();
  }
}

export function saveState(cfg, state, { expectedRevision = null } = {}) {
  const dirs = collabDirs(cfg);
  const current = loadState(cfg);
  if (expectedRevision != null && current.revision !== expectedRevision) {
    fail(EXIT.CONFLICT, `CAS conflict: expected revision ${expectedRevision}, current is ${current.revision}`, {
      currentRevision: current.revision,
    });
  }
  state.revision = (current.revision || 0) + 1;
  state.updatedAt = nowIso();
  writeJsonAtomic(dirs.state, state);
  return state;
}

export function recordEvent(cfg, partial) {
  const dirs = collabDirs(cfg);
  const state = loadState(cfg);
  const event = {
    schemaVersion: 1,
    eventId: partial.eventId || newId('EVT'),
    projectId: cfg.projectId,
    revision: state.revision,
    at: nowIso(),
    actor: partial.actor || 'system',
    action: partial.action,
    taskId: partial.taskId || null,
    deliveryId: partial.deliveryId || null,
    reviewId: partial.reviewId || null,
    parentEventId: partial.parentEventId || null,
    summary: partial.summary || '',
    data: partial.data || {},
  };
  validateEvent(event);
  appendJsonRecord(dirs.events, event.eventId, event);
  return event;
}

export function loadTask(cfg, taskId) {
  const dirs = collabDirs(cfg);
  const file = path.join(dirs.tasks, `${taskId}.json`);
  const task = readJson(file, null);
  if (!task) fail(EXIT.USAGE, `Task not found: ${taskId}`);
  validateTask(task);
  return task;
}

export function saveTask(cfg, task, { expectedRevision = null, owner = null } = {}) {
  return withStateLock(cfg, owner || `saveTask:${task.taskId}`, () => {
    validateTask(task);
    const dirs = collabDirs(cfg);
    ensureDir(dirs.tasks);
    const state = loadState(cfg);
    if (expectedRevision != null && state.revision !== expectedRevision) {
      fail(EXIT.CONFLICT, `expected-revision ${expectedRevision} != ${state.revision}`, {
        currentRevision: state.revision,
      });
    }
    writeJsonAtomic(path.join(dirs.tasks, `${task.taskId}.json`), task);
    state.tasksIndex[task.taskId] = {
      title: task.title,
      devStatus: task.devStatus,
      reviewStatus: task.reviewStatus,
      holder: task.holder || null,
      freshness: task.freshness || 'unknown',
      taskBookStatus: task.taskBookStatus || null,
      updatedAt: task.updatedAt,
    };
    return saveState(cfg, state, {
      expectedRevision: expectedRevision != null ? expectedRevision : state.revision,
    });
  });
}

export function loadAck(cfg) {
  const local = localDirs(cfg);
  return readJson(local.ack, { cursor: null, codex: null });
}

export function saveAck(cfg, actor, revision) {
  const local = localDirs(cfg);
  ensureDir(local.root);
  const ack = loadAck(cfg);
  ack[actor] = { revision, at: nowIso() };
  writeJsonAtomic(local.ack, ack);
  return ack;
}

export function listDeliveries(cfg) {
  return listJsonRecords(collabDirs(cfg).deliveries).sort(compareCreationTime);
}

export function listReviews(cfg) {
  return listJsonRecords(collabDirs(cfg).reviews).sort(compareCreationTime);
}

// IDs only contain seconds plus a random suffix, so lexical order is not
// chronological within a second. All "latest" consumers must use createdAt.
function compareCreationTime(a, b) {
  const at = Date.parse(a.createdAt);
  const bt = Date.parse(b.createdAt);
  const delta = (Number.isFinite(at) ? at : 0) - (Number.isFinite(bt) ? bt : 0);
  return delta || String(a.deliveryId || a.reviewId || '').localeCompare(String(b.deliveryId || b.reviewId || ''));
}

export function listIssues(cfg) {
  // The defect report itself is immutable; later lifecycle transitions live in
  // the shared CAS state so both sides read exactly one status.
  const overrides = loadState(cfg).issueStatus || {};
  return listJsonRecords(collabDirs(cfg).issues).map((issue) => {
    const ov = overrides[issue.issueId];
    if (!ov) return issue;
    return { ...issue, reportedStatus: issue.status, status: ov.status, statusRecord: ov };
  });
}

/**
 * Input set used to decide whether a past verdict still applies to the current
 * tree. It MUST be the same set the delivery was fingerprinted with, otherwise a
 * task that pins narrower roots (C-ENG-*) would compare against the whole project
 * and look stale forever — and the stale branch then erases a real passed verdict.
 */
function fingerprintOptsForTask(cfg, task) {
  const roots = resolveTaskRoots(cfg, task);
  return {
    contractPayload: stableContractPayload(task),
    implementationRoots: roots.implementationRoots,
    verificationRoots: roots.verificationRoots,
  };
}

export function refreshFreshness(cfg, { write = true } = {}) {
  const dirs = collabDirs(cfg);
  return withStateLock(cfg, 'refreshFreshness', () => {
    // Re-read every shared record inside the transaction boundary.
    const tasks = listJsonRecords(dirs.tasks);
    const deliveries = listDeliveries(cfg);
    const reviews = listReviews(cfg);
    const results = [];
    let anyChange = false;
    const revisionAtStart = loadState(cfg).revision;

    // Recover orphaned running reviews whose worker died.
    for (const review of reviews) {
      if (review.status !== 'running' || !review.workerPid) continue;
      if (isPidAlive(review.workerPid)) continue;
      const pathReview = path.join(dirs.reviews, `${review.reviewId}.json`);
      const disk = readJson(pathReview, null);
      // Never clobber a terminal state written while we were scanning.
      if (!disk || disk.status !== 'running') continue;
      disk.status = 'interrupted';
      disk.workerPid = null;
      disk.updatedAt = nowIso();
      disk.interruptReason = `worker pid ${review.workerPid} no longer alive`;
      writeJsonAtomic(pathReview, disk);
      const taskPath = path.join(dirs.tasks, `${disk.taskId}.json`);
      const taskOnDisk = readJson(taskPath, null);
      if (taskOnDisk && taskOnDisk.reviewStatus === 'running') {
        taskOnDisk.reviewStatus = 'interrupted';
        taskOnDisk.updatedAt = nowIso();
        writeJsonAtomic(taskPath, taskOnDisk);
      }
      review.status = disk.status;
      anyChange = true;
    }

    for (const task of tasks) {
      const taskPath = path.join(dirs.tasks, `${task.taskId}.json`);
      const taskDeliveries = deliveries.filter((d) => d.taskId === task.taskId);
      const latestDelivery = [...taskDeliveries].reverse()[0] || null;
      // Reference-only tasks with no delivery have no implementation to compare.
      // Avoid re-reading the entire Unity asset tree six times on every watch tick.
      const fp = latestDelivery ? computeFingerprints(cfg._repoRoot, { ...fingerprintOptsForTask(cfg, task), includeGit: false }) : null;
      // Pair passed review strictly to the latest delivery id when present.
      const pairedReview = latestDelivery
        ? [...reviews]
            .reverse()
            .find(
              (r) =>
                r.taskId === task.taskId &&
                r.deliveryId === latestDelivery.deliveryId,
            ) || null
        : null;

      let freshness = 'unknown';
      let note = '';
      if (pairedReview?.status === 'passed' && latestDelivery) {
        const change = classifyChange(latestDelivery.fingerprints, fp);
        if (pairedReview.profileDefinition && JSON.stringify(pairedReview.profileDefinition) !== JSON.stringify(cfg.profiles?.[pairedReview.profile])) {
          change.needsReverification = true;
        }
        if (change.staleForCurrentCode || change.needsReverification) {
          freshness = 'stale';
          note = change.needsReverification
            ? `Historical review ${pairedReview.reviewId} passed delivery ${latestDelivery.deliveryId}; verification baseline changed — re-review required.`
            : `Historical review ${pairedReview.reviewId} passed delivery ${latestDelivery.deliveryId}; current workspace needs re-review.`;
        } else {
          freshness = 'current';
          note = `Review ${pairedReview.reviewId} still applies to delivery ${latestDelivery.deliveryId}.`;
        }
      } else if (latestDelivery) {
        const change = classifyChange(latestDelivery.fingerprints, fp);
        const latestReviewAny = [...reviews]
          .reverse()
          .find((r) => r.deliveryId === latestDelivery.deliveryId);
        if (latestReviewAny?.status === 'failed' || latestReviewAny?.status === 'blocked') {
          const isBlocked = latestReviewAny.status === 'blocked';
          // A blocked review produced no verifiable verdict: freshness stays unknown
          // unless the workspace has already drifted away from the delivery.
          freshness = change.staleForCurrentCode ? 'stale' : isBlocked ? 'unknown' : 'current';
          note = isBlocked
            ? `Latest delivery ${latestDelivery.deliveryId} review ${latestReviewAny.reviewId} blocked: no verifiable machine result.`
            : `Latest delivery ${latestDelivery.deliveryId} review ${latestReviewAny.reviewId} failed.`;
        } else {
          freshness = change.staleForCurrentCode || change.needsReverification ? 'stale' : 'current';
          note = change.staleForCurrentCode
            ? 'Delivery snapshot differs from current workspace.'
            : 'Delivery matches current fingerprints; awaiting review.';
        }
      }
      if (task.readOnlyReference && task.taskBookStatus) {
        note = (note ? note + ' ' : '') + `taskBookStatus=${task.taskBookStatus}`;
      }

      if (write) {
        // Merge into the on-disk record: sync owns freshness/note only. Holder and
        // workflow status written by a concurrent claim/deliver/review must survive.
        const mergeWrite = () => {
          const disk = readJson(taskPath, null);
          if (!disk) return;
          // Going stale must not destroy the historical verdict: the review file
          // stays passed and the task keeps reviewStatus=passed, with freshness
          // (and the note naming the paired review) expressing "needs re-review".
          // Erasing it here is how a tool-side fingerprint bug silently turned a
          // real passed verdict into not_run/rework.
          const freshnessChanged =
            disk.freshness !== freshness ||
            disk.freshnessNote !== note ||
            disk.staleForReviewId !== (freshness === 'stale' ? pairedReview?.reviewId || null : null);
          disk.freshness = freshness;
          disk.freshnessNote = note;
          disk.staleForReviewId = freshness === 'stale' ? pairedReview?.reviewId || null : null;
          if (freshnessChanged) disk.updatedAt = nowIso();
          // Publish from the latest disk base even when freshness is unchanged;
          // the revision check below reconciles concurrent workflow mutations.
          writeJsonAtomic(taskPath, disk);
        };
        const diskBefore = readJson(taskPath, null) || task;
        const prevFreshness = diskBefore.freshness;
        const prevNote = diskBefore.freshnessNote;
        mergeWrite();
        if (prevFreshness !== freshness || prevNote !== note) anyChange = true;
        const finalDisk = readJson(taskPath, null) || diskBefore;
        task.holder = finalDisk.holder ?? task.holder;
        task.devStatus = finalDisk.devStatus ?? task.devStatus;
        task.reviewStatus = finalDisk.reviewStatus ?? task.reviewStatus;
      }
      task.freshness = freshness;
      task.freshnessNote = note;
      results.push({
        taskId: task.taskId,
        freshness,
        note,
        implementationFingerprint: fp?.implementation.fingerprint || null,
        verificationFingerprint: fp?.verification.fingerprint || null,
        pairedDeliveryId: latestDelivery?.deliveryId || null,
        pairedReviewId: pairedReview?.reviewId || null,
      });
    }

    if (write) {
      // If another mutation (claim/deliver/review) committed while this transaction
      // was writing task files, the shared state index is authoritative for the
      // operational fields it owns. Restore them instead of leaving stale bytes.
      const stateNow = loadState(cfg);
      if (stateNow.revision !== revisionAtStart) {
        for (const [taskId, idx] of Object.entries(stateNow.tasksIndex || {})) {
          const taskPath = path.join(dirs.tasks, `${taskId}.json`);
          const disk = readJson(taskPath, null);
          if (!disk) continue;
          const holderMismatch = (idx.holder ?? null) !== (disk.holder ?? null);
          if (!holderMismatch) continue;
          disk.holder = idx.holder ?? null;
          disk.devStatus = idx.devStatus ?? disk.devStatus;
          disk.reviewStatus = idx.reviewStatus ?? disk.reviewStatus;
          writeJsonAtomic(taskPath, disk);
          const t = tasks.find((x) => x.taskId === taskId);
          if (t) {
            t.holder = disk.holder;
            t.devStatus = disk.devStatus;
            t.reviewStatus = disk.reviewStatus;
          }
        }
      }
    }

    if (write && anyChange) {
      const state = loadState(cfg);
      for (const r of results) {
        if (state.tasksIndex[r.taskId]) {
          state.tasksIndex[r.taskId].freshness = r.freshness;
        }
      }
      for (const t of tasks) {
        if (state.tasksIndex[t.taskId]) {
          state.tasksIndex[t.taskId].holder = t.holder ?? null;
          state.tasksIndex[t.taskId].devStatus = t.devStatus;
          state.tasksIndex[t.taskId].reviewStatus = t.reviewStatus;
        }
      }
      saveState(cfg, state, { expectedRevision: state.revision });
    }

    return { results, anyChange };
  });
}

export function generateSummaries(cfg) {
  return withStateLock(cfg, 'summaries', () => generateSummariesLocked(cfg));
}

function generateSummariesLocked(cfg) {
  const dirs = collabDirs(cfg);
  const state = loadState(cfg);
  const ack = loadAck(cfg);
  const tasks = listJsonRecords(dirs.tasks);
  const deliveries = listDeliveries(cfg);
  const reviews = listReviews(cfg);
  const issues = listIssues(cfg).filter((i) => i.status !== 'closed');

  const lines = [];
  lines.push(`# CURRENT · ${cfg.projectId}`);
  lines.push('');
  lines.push(`生成时间：${nowIso()}`);
  lines.push(`状态 revision：${state.revision}`);
  lines.push(
    `Cursor 最近确认读取：${ack.cursor ? `revision ${ack.cursor.revision} @ ${ack.cursor.at}` : '尚未 ack'}`,
  );
  lines.push(
    `Codex 最近确认读取：${ack.codex ? `revision ${ack.codex.revision} @ ${ack.codex.at}` : '尚未 ack'}`,
  );
  if (!ack.cursor || ack.cursor.revision !== state.revision) {
    lines.push('> 状态已更新，Cursor 尚未确认读取最新 revision。');
  }
  if (!ack.codex || ack.codex.revision !== state.revision) {
    lines.push('> 状态已更新，Codex 尚未确认读取最新 revision。');
  }
  lines.push('');
  lines.push('协作方式：手动 sync/ack + 可选前台 watch；Hook 原生接线须单独核实，计数不代表客户端已触发。');
  lines.push('## 任务');
  lines.push('');
  for (const t of tasks) {
    const book = t.taskBookStatus ? ` · 任务书=${t.taskBookStatus}` : '';
    const ro = t.readOnlyReference ? ' · 只读引用' : '';
    const latestTaskDelivery = [...deliveries].reverse().find(d => d.taskId === t.taskId);
    const latestTaskReview = latestTaskDelivery ? [...reviews].reverse().find(r => r.deliveryId === latestTaskDelivery.deliveryId) : null;
    const independence = latestTaskReview?.independent === false ? ' · 验证性质=开发自测（待独立验收）' : '';
    lines.push(
      `- **${t.taskId}** ${t.title} · 开发=${t.devStatus} · 检测=${t.reviewStatus} · freshness=${t.freshness || 'unknown'} · holder=${t.holder || '-'}${book}${ro}${independence}`,
    );
    if (t.freshnessNote) lines.push(`  - ${t.freshnessNote}`);
  }
  lines.push('');
  lines.push('## 最新交付');
  lines.push('');
  const lastD = deliveries[deliveries.length - 1];
  lines.push(lastD ? `- ${lastD.deliveryId} → ${lastD.taskId} @ ${lastD.createdAt}` : '- （无）');
  lines.push('');
  lines.push('## 最新检测');
  lines.push('');
  const lastR = reviews[reviews.length - 1];
  lines.push(
    lastR
      ? `- ${lastR.reviewId} → delivery ${lastR.deliveryId} status=${lastR.status} · ${lastR.independent === false ? '开发自测，非独立验收' : '独立检测角色'} @ ${lastR.updatedAt || lastR.createdAt}`
      : '- （无）',
  );
  lines.push('');
  lines.push('## 开放缺陷');
  lines.push('');
  if (!issues.length) lines.push('- （无）');
  for (const i of issues) {
    lines.push(`- ${i.issueId} [${i.severity}] ${i.title} · task=${i.taskId}`);
  }
  lines.push('');
  lines.push('本文件由 `node tools/collab/cli.mjs sync` 生成，请勿手工维护第二套状态。');
  lines.push('T01/T06 等只读引用的任务书状态见 taskBookStatus；不以本工具覆盖游戏任务书勾选。');
  writeFileAtomic(dirs.current, lines.join('\n') + '\n');

  const cursorTodo = [
    '# NEXT_CURSOR',
    '',
    ...tasks
      .filter((t) => !t.readOnlyReference)
      .filter((t) =>
        ['todo', 'developing', 'rework', 'ready_for_review'].includes(t.devStatus),
      )
      .map((t) => {
        if (t.devStatus === 'ready_for_review') {
          return `- ${t.taskId}: 已交付，等待 Codex；不要自行勾选独立验收通过。`;
        }
        if (t.devStatus === 'rework') {
          return `- ${t.taskId}: 按缺陷修复后重新 deliver。`;
        }
        return `- ${t.taskId}: ${t.devStatus} · holder=${t.holder || '可 claim'}`;
      }),
    '',
  ];
  writeFileAtomic(dirs.nextCursor, cursorTodo.join('\n'));

  const codexTodo = [
    '# NEXT_CODEX',
    '',
    ...tasks
      .filter((t) => !t.readOnlyReference)
      .filter(
        (t) =>
          t.devStatus === 'ready_for_review' ||
          ['queued', 'running', 'interrupted'].includes(t.reviewStatus),
      )
      .map(
        (t) =>
          `- ${t.taskId}: 开发=${t.devStatus} 检测=${t.reviewStatus} freshness=${t.freshness || 'unknown'}`,
      ),
    ...issues.map(
      (i) => `- 缺陷 ${i.issueId} 仍开放，归属建议=${i.suggestedOwner || 'cursor'}`,
    ),
    '',
  ];
  writeFileAtomic(dirs.nextCodex, codexTodo.join('\n'));

  return { revision: state.revision, currentPath: dirs.current };
}
