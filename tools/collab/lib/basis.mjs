import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const norm = (p) => String(p).replace(/\\/g, '/');

/**
 * Does this freshness value mean "the review's byte-level claim still holds"?
 *
 * Two states qualify, and they qualify for the *same* reason — the delivered
 * identity is still an honest description of the workspace:
 *   - `current`: nothing that task changed has moved since.
 *   - `archived`: it moved, but every drifted byte was taken over by a later
 *     delivery that itself passed review (see `rescopeVerdict`).
 *
 * This lived in two places and the copies disagreed: `state.mjs` accepted both
 * when deciding whether a task was settled (so it vanished from NEXT_CURSOR /
 * NEXT_CODEX), while `taskbook.computeTaskGate` demanded `current` exactly. The
 * result was a task that the board called finished and the gate called blocked —
 * and because the gate feeds the dependency check, archiving a task could never
 * unblock its dependents. S16 → S17 → S18 → S19 sat blocked that way even though
 * every link had a passed, independent review of bytes that were still present.
 *
 * One predicate, both callers, so the two answers can never diverge again.
 */
export function freshnessSettles(freshness) {
  return freshness === 'current' || freshness === 'archived';
}

/**
 * S17: what did this delivery actually change, and are those bytes still here?
 *
 * The freshness rule used to compare the *whole* project-wide snapshot roots
 * against the tree. A task that pinned `Unity Project/Assets` therefore went
 * `stale` the moment anybody touched any file anywhere under Assets — even when
 * every file that task had actually edited was byte-identical. Seventeen finished
 * stages piled up in the pending lists that way, each demanding a re-verification
 * that would only ever re-prove the same snapshot.
 *
 * So freshness for such a task is decided on the delivery's own change set:
 *
 *  - `basis`     the paths the delivery recorded as changed (`files.workspaceChanges`)
 *  - `intact`    those paths still hash to exactly what the snapshot captured
 *  - `drifted`   those paths were edited again afterwards — a real reason to re-review
 *
 * The comparison is against the delivery snapshot manifest, not against whatever
 * the task's roots resolve to today, so the answer is a statement about recorded
 * bytes rather than about a moving target.
 */
export function changedFileBasis(delivery) {
  const changes = Array.isArray(delivery?.files?.workspaceChanges) ? delivery.files.workspaceChanges : [];
  const manifest = new Map(
    (delivery?.snapshotManifest?.entries || []).map((e) => [norm(e.path), e.sha256]),
  );
  const basis = [];
  for (const c of changes) {
    const rel = norm(c?.path || '');
    if (!rel) continue;
    // A deletion is part of the change set, but there is no byte to compare later.
    const deleted = /^\s*(D|DD)/.test(String(c.status || '')) || c.status === 'removed';
    basis.push({ path: rel, sha256: manifest.get(rel) || null, deleted });
  }
  return { changes: changes.length, basis, manifest };
}

/**
 * @returns {{ basis, intact, drifted, unverifiable, note }}
 *   drifted      paths whose current bytes differ from the snapshot
 *   unverifiable paths the snapshot did not record (no hash to compare against)
 */
export function changedFileDrift(repoRoot, delivery) {
  const { basis, manifest } = changedFileBasis(delivery);
  if (!basis.length) {
    return {
      basis: [],
      intact: [],
      drifted: [],
      unverifiable: [],
      note: 'this delivery recorded no changed files, so there is no narrowed baseline to compare',
    };
  }
  const intact = [];
  const drifted = [];
  const unverifiable = [];
  for (const b of basis) {
    if (b.deleted) {
      // The file is supposed to be gone; it coming back is a drift.
      const abs = path.join(repoRoot, b.path);
      if (fs.existsSync(abs)) drifted.push(b.path);
      else intact.push(b.path);
      continue;
    }
    const want = b.sha256 || manifest.get(b.path);
    if (!want) {
      unverifiable.push(b.path);
      continue;
    }
    const abs = path.join(repoRoot, b.path);
    if (!fs.existsSync(abs)) {
      drifted.push(b.path);
      continue;
    }
    const got = crypto.createHash('sha256').update(fs.readFileSync(abs)).digest('hex');
    if (got === want) intact.push(b.path);
    else drifted.push(b.path);
  }
  return { basis: basis.map((b) => b.path), intact, drifted, unverifiable, note: null };
}

/**
 * S17: did some later, independently reviewed delivery take over these bytes?
 *
 * A finished task drifting is not automatically the developer's TODO. If a later
 * delivery changed the same file and passed independent review, and the file has
 * not moved since, then the current bytes are covered by *that* verdict — the old
 * task should point at it instead of asking for a re-verification that would only
 * re-prove the same snapshot.
 *
 * The check is exact on purpose: the newest delivery that touched the path must
 * have a passed review AND the file must still hash to what that delivery captured.
 * Anything else stays uncovered, so the task keeps asking for real work.
 */
export function coverageByLaterDelivery(repoRoot, paths, { deliveries, latestReviewFor, after }) {
  const covered = [];
  const uncovered = [];
  const afterAt = Date.parse(after?.createdAt) || 0;
  for (const rel of paths) {
    const candidates = deliveries
      .filter(
        (d) =>
          d.deliveryId !== after?.deliveryId &&
          (Date.parse(d.createdAt) || 0) > afterAt &&
          (d.files?.workspaceChanges || []).some((c) => String(c?.path || '').replace(/\\/g, '/') === rel),
      )
      .sort((a, b) => (Date.parse(b.createdAt) || 0) - (Date.parse(a.createdAt) || 0));
    const owner = candidates[0] || null;
    const review = owner ? latestReviewFor(owner.deliveryId) : null;
    if (!owner || review?.status !== 'passed') {
      uncovered.push(rel);
      continue;
    }
    const want = (owner.snapshotManifest?.entries || []).find((e) => String(e.path).replace(/\\/g, '/') === rel)?.sha256;
    const abs = path.join(repoRoot, rel);
    if (!want || !fs.existsSync(abs)) {
      uncovered.push(rel);
      continue;
    }
    const got = crypto.createHash('sha256').update(fs.readFileSync(abs)).digest('hex');
    if (got === want) covered.push({ path: rel, deliveryId: owner.deliveryId, reviewId: review.reviewId, taskId: owner.taskId });
    else uncovered.push(rel);
  }
  return { covered, uncovered };
}

/**
 * One place decides a narrowed baseline's verdict, so the command that prints it
 * and the state that persists it can never disagree.
 *
 * That disagreement was a real defect: `task-rescope` reported "still needs
 * re-review" while the very next refresh archived the same task, because the
 * command only looked at raw drift and ignored the takeover rule.
 */
export function rescopeVerdict(repoRoot, delivery, { deliveries = [], latestReviewFor = () => null } = {}) {
  const drift = changedFileDrift(repoRoot, delivery);
  const takeover = drift.drifted.length
    ? coverageByLaterDelivery(repoRoot, drift.drifted, { deliveries, after: delivery, latestReviewFor })
    : { covered: [], uncovered: [] };
  const uncovered = takeover.uncovered;
  const owners = [...new Set(takeover.covered.map((c) => c.deliveryId))];
  const scopeDrift = {
    basis: drift.basis.length,
    intact: drift.intact.length,
    covered: takeover.covered.length,
    uncovered: uncovered.slice(0, 10),
    uncoveredCount: uncovered.length,
  };
  return {
    drift,
    ...scopeDrift,
    owners,
    freshness: uncovered.length ? 'stale' : 'archived',
    scopeDrift,
  };
}

export function driftedSummary(drift, limit = 5) {
  if (!drift || !drift.drifted.length) return '';
  const head = drift.drifted.slice(0, limit).join(', ');
  const more = drift.drifted.length > limit ? ` 等 ${drift.drifted.length} 个` : '';
  return `${head}${more}`;
}
