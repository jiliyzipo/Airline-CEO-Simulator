import fs from 'node:fs';
import path from 'node:path';

/**
 * Sweeping fixture trees left behind by earlier runs.
 *
 * Kept out of `run-all.mjs` so it can be tested on its own: importing the runner
 * executes every suite, which makes the sweep impossible to exercise in isolation.
 *
 * The rule that matters: a tree is only removed when nothing about it could still
 * matter. Deleting on the root directory's mtime alone was wrong twice over — a
 * child process writes files, not the parent entry, so an hours-old root can hold
 * activity from seconds ago; and a tree retained after a failure is *evidence*,
 * whose entire purpose is to outlive the run. Both mistakes destroyed real
 * diagnostics (ISSUE-20261101061656-eee4ee). Age is therefore judged on the newest
 * entry anywhere in the tree, retained failures carry `KEEP-EVIDENCE.json`, and
 * ownership markers make a tree permanently ineligible. When in doubt, keep: a
 * leftover tree costs disk, a deleted one costs the failure.
 *
 * `releaseOwnedRoots` cleans the two roots this process created. Its containment
 * test used to be `resolve(evidenceDir).startsWith(resolve(owned) + sep)`, which is
 * false when the two paths are *equal* — and for a standalone `run-all` they are
 * exactly equal, because the run puts its own logs and summary at the temp root.
 * The whole evidence directory was therefore deleted on exit
 * (ISSUE-20261101063120-97dd41). Equality has to count as containment.
 */
export const KEEP_MARKER_FILE = 'KEEP-EVIDENCE.json';

// A tree holding any of these may still have an owner that will come back to it
// (a lease, a recorded PID, a worker's execution log). Age cannot disprove that.
const LIVE_MARKER_RE = /(^owner\.json$|\.owner\.json$|^lease[^/]*\.json$|^\.lease$|^[^/]*\.pid$|^executions\.txt$)/;

/**
 * Is `child` the same directory as `parent`, or somewhere inside it?
 *
 * The name says "contains" and equality counts: a path trivially contains itself.
 * `startsWith(parent + sep)` alone does not, which is how the evidence directory
 * got deleted — see the module comment.
 */
export function containsPath(parent, child) {
  const p = path.resolve(parent);
  const c = path.resolve(child);
  const norm = (s) => (process.platform === 'win32' ? s.toLowerCase() : s);
  return norm(p) === norm(c) || norm(c).startsWith(norm(p) + path.sep);
}

/**
 * Remove the roots this process created, unless one of them holds this run's
 * evidence. A surviving root on a failed run gets `KEEP-EVIDENCE.json` so the
 * sweep in a later run leaves it alone too — retaining the tree is pointless if
 * the next run deletes it.
 */
export function releaseOwnedRoots(owned, evidenceDir, { failed = false } = {}) {
  const outcomes = [];
  for (const dir of [...new Set((owned || []).filter(Boolean))]) {
    if (evidenceDir && containsPath(dir, evidenceDir)) {
      if (failed) {
        try {
          fs.writeFileSync(
            path.join(dir, KEEP_MARKER_FILE),
            `${JSON.stringify({ keptAt: new Date().toISOString(), reason: 'failed run; evidence retained', evidenceDir }, null, 2)}\n`,
            'utf8',
          );
        } catch {
          // Losing the marker is bad; hiding the failed run behind a throw is worse.
        }
      }
      outcomes.push({ dir, removed: false, reason: 'holds this run\'s evidence' });
      continue;
    }
    try {
      fs.rmSync(dir, { recursive: true, force: true });
      outcomes.push({ dir, removed: true });
    } catch (e) {
      outcomes.push({ dir, removed: false, reason: e.message });
    }
  }
  return outcomes;
}

const FIXTURE_PREFIX_RE =
  /^collab-(stage|daily|reverse|suite|probe|devtest|ccv|nogit|ccv-gate|book|issue|hook|fresh|snap|prune|lock|atom|git-ceiling)/;

/** Walk a tree: newest mtime anywhere, plus whether it must be kept. */
export function inspectTree(dir) {
  let newest = 0;
  let keep = false;
  let live = false;
  let files = 0;
  const pending = [dir];
  while (pending.length) {
    const cur = pending.pop();
    let entries;
    try {
      entries = fs.readdirSync(cur, { withFileTypes: true });
    } catch {
      return { error: 'unreadable' };
    }
    for (const e of entries) {
      if (e.name === KEEP_MARKER_FILE) keep = true;
      else if (LIVE_MARKER_RE.test(e.name)) live = true;
      const abs = path.join(cur, e.name);
      if (e.isDirectory()) pending.push(abs);
      else files += 1;
      try {
        const mtime = fs.statSync(abs).mtimeMs;
        if (mtime > newest) newest = mtime;
      } catch {
        // Vanished mid-scan means something is writing there right now.
        live = true;
      }
    }
  }
  try {
    const rootMtime = fs.statSync(dir).mtimeMs;
    if (rootMtime > newest) newest = rootMtime;
  } catch {
    return { error: 'vanished' };
  }
  return { newest, keep, live, files };
}

/**
 * Remove fixture trees under `baseDir` that are provably abandoned.
 * Returns what was removed and, per kept tree, why — silence about a kept tree
 * looks identical to a leak that nobody noticed.
 */
export function pruneStaleFixtures(baseDir, maxAgeMs = 6 * 3600 * 1000, { now = Date.now() } = {}) {
  let entries = [];
  try {
    entries = fs.readdirSync(baseDir, { withFileTypes: true });
  } catch {
    return { removed: 0, kept: 0, skipped: [] };
  }
  const cutoff = now - maxAgeMs;
  let removed = 0;
  let kept = 0;
  const skipped = [];
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    if (!FIXTURE_PREFIX_RE.test(e.name)) continue;
    const abs = path.join(baseDir, e.name);
    const info = inspectTree(abs);
    if (info.error) {
      kept += 1;
      skipped.push(`${e.name}: ${info.error}`);
      continue;
    }
    if (info.keep) {
      kept += 1;
      skipped.push(`${e.name}: retains ${KEEP_MARKER_FILE}`);
      continue;
    }
    if (info.live) {
      kept += 1;
      skipped.push(`${e.name}: ownership marker present`);
      continue;
    }
    if (info.newest > cutoff) {
      kept += 1;
      skipped.push(`${e.name}: written ${Math.round((now - info.newest) / 60000)}m ago`);
      continue;
    }
    try {
      fs.rmSync(abs, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 });
      removed += 1;
    } catch {
      kept += 1;
      skipped.push(`${e.name}: locked`);
    }
  }
  return { removed, kept, skipped };
}
