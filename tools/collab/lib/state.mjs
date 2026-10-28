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
import { freshnessSettles } from './basis.mjs';
import { rescopeVerdict } from './basis.mjs';
import { EXIT, fail } from './exit.mjs';

export function emptyState(cfg) {
  return {
    schemaVersion: 1,
    projectId: cfg.projectId,
    revision: 1,
    updatedAt: nowIso(),
    tasksIndex: {},
    issueStatus: {},
    deliveryStatus: {},
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

/**
 * Deliveries that a newer delivery replaced.
 *
 * Freshness asks one narrow question: "does the newest delivery for this task
 * still describe the current tree?" Picking strictly by createdAt answers a
 * different question. When a delivery is filed under the wrong task, the newest
 * record for task T is work that belongs to T'; it can never match T's tree, so
 * T is dragged to stale on every single sync, forever — and its
 * `freshnessNote`/`staleForReviewId` get rewritten with it, which looks like the
 * task itself changed when nothing did (S03-Docs / DEL-20261025125421-e0fbe0).
 *
 * The supersede fact has to live in shared CAS state rather than in the delivery
 * record: deliveries are immutable history, so "this one was replaced" must not
 * be written back into them. `<state>.deliveryStatus[id].status === 'superseded'`
 * is that ledger, and it is what `refreshFreshness` reads.
 */
export function supersededDeliveryIds(state) {
  const map = state?.deliveryStatus || {};
  const ids = new Set();
  for (const [deliveryId, entry] of Object.entries(map)) {
    if (entry && entry.status === 'superseded') ids.add(deliveryId);
  }
  return ids;
}

/**
 * Deliveries that the ledger says must not be taken as this task's work —
 * replaced by a newer one, or withdrawn by whoever filed them.
 *
 * Callers that ask "what is this task's latest real delivery" want this union,
 * not the two sets separately: a withdrawn delivery describes the tree exactly
 * as badly as a superseded one. Board rows and the pending list both use it.
 */
export function retiredDeliveryIds(state) {
  const map = state?.deliveryStatus || {};
  const ids = new Set();
  for (const [deliveryId, entry] of Object.entries(map)) {
    if (entry && (entry.status === 'superseded' || entry.status === 'withdrawn')) ids.add(deliveryId);
  }
  return ids;
}

/**
 * Record that a delivery is withdrawn: it was filed in error and no review may
 * ever be run against it.
 *
 * This is deliberately **not** `superseded`. Supersede answers "what replaced
 * it", which needs a successor created *after* it; the successor rule exists so
 * a later delivery cannot be claims-replaced by an earlier one. Withdrawal says
 * something different and needs no successor at all: "this filing was a mistake
 * — the changed set it claims never belonged to this task."
 *
 * The delivery record itself is never touched. Withdrawing removes a claim from
 * the ledger, never deletes evidence: `deliveries/<DEL>.json`, its snapshot and
 * any handoff stay on disk exactly as filed.
 */
export function withdrawDelivery(cfg, { deliveryId, taskId, actor, reason }) {
  if (!deliveryId) return { changed: false, entry: null, revision: loadState(cfg).revision };
  return withStateLock(cfg, `delivery-withdraw:${deliveryId}`, () => {
    const state = loadState(cfg);
    const map = { ...(state.deliveryStatus || {}) };
    const prev = map[deliveryId];
    if (prev?.status === 'withdrawn' && (prev.note || '') === (reason || '')) {
      return { changed: false, entry: prev, revision: state.revision };
    }
    const entry = {
      status: 'withdrawn',
      supersededBy: null,
      taskId: taskId || prev?.taskId || null,
      actor,
      note: reason || '',
      at: nowIso(),
    };
    map[deliveryId] = entry;
    const next = saveState(cfg, { ...state, deliveryStatus: map }, { expectedRevision: state.revision });
    recordEvent(cfg, {
      actor,
      action: 'delivery-withdrawn',
      summary: `withdrawn: ${deliveryId}`,
      data: { deliveryId, taskId: entry.taskId, reason: entry.note },
    });
    return { changed: true, entry, revision: next.revision };
  });
}

/**
 * Record that `entries` (`{ deliveryId, supersededBy, taskId }`) were replaced.
 *
 * Idempotent: an entry already marked superseded by the same successor is left
 * alone, so a reconcile pass over a stale local index never bumps the revision.
 */
export function markDeliveriesSuperseded(cfg, entries, { actor = 'system', note = '' } = {}) {
  const list = (entries || []).filter((e) => e && e.deliveryId);
  if (!list.length) return { marked: [], revision: loadState(cfg).revision };
  return withStateLock(cfg, 'delivery-supersede', () => {
    const state = loadState(cfg);
    const map = { ...(state.deliveryStatus || {}) };
    const marked = [];
    for (const e of list) {
      const prev = map[e.deliveryId];
      const sameAsBefore =
        prev?.status === 'superseded' && (prev.supersededBy || null) === (e.supersededBy || null);
      if (sameAsBefore) continue;
      map[e.deliveryId] = {
        status: 'superseded',
        supersededBy: e.supersededBy || null,
        taskId: e.taskId || null,
        actor,
        note,
        at: nowIso(),
      };
      marked.push(e.deliveryId);
    }
    if (!marked.length) return { marked, revision: state.revision };
    const next = saveState(cfg, { ...state, deliveryStatus: map }, { expectedRevision: state.revision });
    recordEvent(cfg, {
      actor,
      action: 'delivery-superseded',
      summary: `superseded: ${marked.join(', ')}`,
      data: { marked, note },
    });
    return { marked, revision: next.revision };
  });
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
    // A delivery replaced by a newer one is no longer "the newest work of this
    // task" for freshness purposes — see supersededDeliveryIds().
    const superseded = supersededDeliveryIds(loadState(cfg));
    const latestFinishedByDelivery = new Map();
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
      const latestDelivery = [...taskDeliveries].reverse().find((d) => !superseded.has(d.deliveryId)) || null;
      // S17: a rescoped task is judged on the files its delivery actually changed,
      // not on the whole tree its roots happen to cover. That also skips the
      // expensive project-wide re-read for every finished historical stage.
      const rescoped = latestDelivery && task.scopePolicy === 'changed-files';
      // S17: one verdict source for both the CLI report and the persisted state.
      const verdict = rescoped
        ? rescopeVerdict(cfg._repoRoot, latestDelivery, {
            deliveries,
            latestReviewFor: (deliveryId) => {
              if (!latestFinishedByDelivery.has(deliveryId)) {
                latestFinishedByDelivery.set(
                  deliveryId,
                  [...reviews]
                    .filter((r) => r.deliveryId === deliveryId && r.finishedAt)
                    .sort((a, b) => (Date.parse(a.finishedAt) || 0) - (Date.parse(b.finishedAt) || 0))
                    .at(-1) || null,
                );
              }
              return latestFinishedByDelivery.get(deliveryId);
            },
          })
        : null;
      // Reference-only tasks with no delivery have no implementation to compare.
      // Avoid re-reading the entire Unity asset tree six times on every watch tick.
      const fp = latestDelivery && !rescoped
        ? computeFingerprints(cfg._repoRoot, { ...fingerprintOptsForTask(cfg, task), includeGit: false })
        : null;
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
      let scopeDrift = null;
      if (rescoped) {
        // The verdict stays exactly as recorded. What changes is the question:
        // "are the bytes this verdict covered still here?" instead of "is the
        // whole project tree identical to four days ago?".
        if (pairedReview?.status === 'passed') {
          const drift = verdict.drift;
          const uncovered = verdict.uncovered;
          freshness = verdict.freshness;
          scopeDrift = verdict.scopeDrift;
          if (!drift.drifted.length) {
            note = `Historic verdict ${pairedReview.reviewId} covered ${drift.basis.length} changed files, all byte-identical to the delivery snapshot; the project-wide baseline is no longer comparable.`;
          } else if (!uncovered.length) {
            const owners = verdict.owners;
            note = `Historic verdict ${pairedReview.reviewId} covered ${drift.basis.length} changed files; ${verdict.covered} were edited later and are now owned by passed deliveries ${owners.slice(0, 3).join(', ')}${owners.length > 3 ? ` 等 ${owners.length} 个` : ''}.`;
          } else {
            note = `Historic verdict ${pairedReview.reviewId} covered ${drift.basis.length} changed files; ${verdict.covered} taken over by later passed deliveries, ${uncovered.length} still uncovered (${uncovered.slice(0, 3).join(', ')}${uncovered.length > 3 ? ' …' : ''}).`;
          }
        } else {
          freshness = 'unknown';
          note = 'scopePolicy=changed-files but the latest delivery has no passed review';
        }
      } else if (pairedReview?.status === 'passed' && latestDelivery) {
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
          // S15/AC5: `passed` is the end of the workflow. A `rework` left behind by
          // an earlier failed review must not survive a later passed verdict, or the
          // board keeps showing the impossible `rework/passed`. Staleness belongs to
          // `freshness`; the developer-side TODO text carries the re-review hint.
          const devHealed = disk.reviewStatus === 'passed' && disk.devStatus === 'rework';
          if (devHealed) disk.devStatus = 'ready_for_review';
          disk.freshness = freshness;
          disk.freshnessNote = note;
          disk.staleForReviewId = freshness === 'stale' ? pairedReview?.reviewId || null : null;
          // S17: keep the narrowed baseline's verdict on disk so the TODO text can
          // name the files nobody took over, instead of a generic "code changed".
          if (scopeDrift) disk.scopeDrift = scopeDrift;
          else delete disk.scopeDrift;
          if (freshnessChanged || devHealed || JSON.stringify(disk.scopeDrift) !== JSON.stringify(prevScopeDrift)) {
            disk.updatedAt = nowIso();
          }
          // Publish from the latest disk base even when freshness is unchanged;
          // the revision check below reconciles concurrent workflow mutations.
          writeJsonAtomic(taskPath, disk);
        };
        const diskBefore = readJson(taskPath, null) || task;
        const prevFreshness = diskBefore.freshness;
        const prevNote = diskBefore.freshnessNote;
        const prevScopeDrift = diskBefore.scopeDrift;
        mergeWrite();
        if (prevFreshness !== freshness || prevNote !== note) anyChange = true;
        const finalDisk = readJson(taskPath, null) || diskBefore;
        task.holder = finalDisk.holder ?? task.holder;
        task.devStatus = finalDisk.devStatus ?? task.devStatus;
        task.reviewStatus = finalDisk.reviewStatus ?? task.reviewStatus;
      }
      task.freshness = freshness;
      task.freshnessNote = note;
      task.scopeDrift = scopeDrift;
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
  // S16/AC6: a retired defect is not an open one. Listing wontfix/verified/closed
  // reports made both sides re-read defects nobody has to act on.
  const issues = listIssues(cfg).filter((i) => ['open', 'fixing', 'fixed'].includes(i.status));

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
    const supersede = t.supersededBy
      ? ` · 已被 ${t.supersededBy.deliveryId}（${t.supersededBy.taskId}）取代`
      : '';
    // S17: say why the verdict is not compared project-wide, so "archived" never
    // reads as "verified against today's tree".
    const scope = t.scopePolicy === 'changed-files'
      ? ` · 基线=本任务改动文件（${t.freshness === 'archived' ? '全部未变，已归档' : '有变动，见 freshnessNote'}）`
      : '';
    const latestTaskDelivery = [...deliveries].reverse().find(d => d.taskId === t.taskId);
    const latestTaskReview = latestTaskDelivery ? [...reviews].reverse().find(r => r.deliveryId === latestTaskDelivery.deliveryId) : null;
    const independence = latestTaskReview?.independent === false ? ' · 验证性质=开发自测（待独立验收）' : '';
    lines.push(
      `- **${t.taskId}** ${t.title} · 开发=${t.devStatus} · 检测=${t.reviewStatus} · freshness=${t.freshness || 'unknown'} · holder=${t.holder || '-'}${book}${ro}${independence}${supersede}${scope}`,
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

  // S14/D2: a task is settled only when its delivered identity still matches the
  // workspace. Listing settled tasks as outstanding made both sides believe there
  // was work left (and re-triggered reviews that already had a verdict).
  // S15/AC6: a task whose delivery was replaced by another task's delivery has no
  // work left either — it must not stay in the pending lists asking for a re-review.
  const settled = (t) =>
    t.reviewStatus === 'passed' && freshnessSettles(t.freshness);
  const retired = (t) => !!t.supersededBy;
  const actionable = (t) => !t.readOnlyReference && !settled(t) && !retired(t);
  const cursorTodo = [
    '# NEXT_CURSOR',
    '',
    ...tasks
      .filter(actionable)
      .filter((t) =>
        ['todo', 'developing', 'rework', 'ready_for_review'].includes(t.devStatus),
      )
      .map((t) => {
        // A task already back in rework is the developer's open work item; the
        // stale-verdict wording is only for tasks still parked at review.
        if (t.devStatus === 'rework') {
          return `- ${t.taskId}: 按缺陷修复后重新 deliver。`;
        }
        if (t.reviewStatus === 'passed') {
          // S17: for a narrowed baseline, name the files nobody took over instead of
          // the generic "the workspace changed" that made the list unactionable.
          if (t.scopeDrift && t.scopeDrift.uncoveredCount) {
            const head = t.scopeDrift.uncovered.slice(0, 3).join(', ');
            return `- ${t.taskId}: ${t.scopeDrift.uncoveredCount} 个本任务改动过的文件已被后续改动但无人复验（${head}${t.scopeDrift.uncoveredCount > 3 ? ' …' : ''}），需按当前代码复验；不要自行勾选独立验收通过。`;
          }
          return `- ${t.taskId}: 历史检测已通过，但工作区已变（freshness=${t.freshness || 'unknown'}），需按当前代码复验；不要自行勾选独立验收通过。`;
        }
        if (t.devStatus === 'ready_for_review') {
          return `- ${t.taskId}: 已交付，等待 Codex；不要自行勾选独立验收通过。`;
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
      .filter(actionable)
      .filter(
        (t) =>
          t.devStatus === 'ready_for_review' ||
          t.reviewStatus === 'passed' ||
          ['queued', 'running', 'interrupted'].includes(t.reviewStatus),
      )
      .map((t) => {
        const line = `- ${t.taskId}: 开发=${t.devStatus} 检测=${t.reviewStatus} freshness=${t.freshness || 'unknown'}`;
        return t.reviewStatus === 'passed'
          ? `${line}（历史通过仅对应旧快照，需按当前代码复验）`
          : line;
      }),
    ...issues.map(
      (i) =>
        `- 缺陷 ${i.issueId} [${i.status}${i.severity ? `/${i.severity}` : ''}] 归属建议=${i.suggestedOwner || 'cursor'} · task=${i.taskId}`,
    ),
    '',
  ];
  writeFileAtomic(dirs.nextCodex, codexTodo.join('\n'));

  return { revision: state.revision, currentPath: dirs.current };
}
