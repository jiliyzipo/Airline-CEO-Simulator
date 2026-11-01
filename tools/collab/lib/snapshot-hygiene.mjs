import fs from 'node:fs';
import path from 'node:path';

/**
 * Delivery snapshots accumulate Unity's caches.
 *
 * A `unity-*` preflight runs inside the snapshot, so Unity creates
 * `Unity Project/Library` there — about 1.9 GB per run. That cache is never part
 * of the evidence: `ARTIFACT_DIR_NAMES` treats it as a build artifact, `collectEntries`
 * never lists it, and `verifySnapshotIntegrity` ignores it. It is pure leftover, but
 * nothing ever deleted it, so 23 snapshots had quietly grown to 42 GB.
 *
 * The rule below is deliberately narrow:
 *   - only directories whose *name* is a known artifact name are considered,
 *   - a file that the manifest lists is NEVER deleted, even if it sits inside one,
 *   - declared run outputs (the result file and per-profile outputs) are preserved,
 *   - anything else in there is regenerable and goes.
 *
 * Keeping the last two exceptions is what makes this safe to run automatically:
 * `unity-editmode` writes its NUnit XML into `Unity Project/Logs/`, which is also
 * an artifact directory. Deleting that blindly would erase the very evidence the
 * review is about to read.
 */
export const SNAPSHOT_ARTIFACT_DIRS = new Set([
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
 * Artifact trees that carry *diagnostics* rather than pure cache.
 *
 * When a profile run fails, `Logs/` (and anything the run dumped under `out/`) is
 * the evidence a reviewer reads to understand the failure. Those trees are kept
 * whole in that case; on success they are debris like everything else.
 */
export const DIAGNOSTIC_ARTIFACT_DIRS = new Set(['Logs', 'out']);

const norm = (p) => String(p).replace(/\\/g, '/');

/**
 * Delete regenerable build/run artifacts inside a snapshot.
 *
 * @param {string} snapAbs absolute snapshot directory
 * @param {object} opts
 * @param {Set<string>|string[]} [opts.keep] manifest-listed paths that must survive
 * @param {string[]} [opts.protect] extra paths to keep (result files, run outputs)
 * @returns {{freedBytes:number, removedFiles:number, keptFiles:number, removedDirs:number}}
 */
export function pruneSnapshotArtifacts(snapAbs, { keep = [], protect = [] } = {}) {
  const keepSet = new Set([...keep].map(norm));
  const protectSet = new Set([...protect].filter(Boolean).map(norm));
  const stats = { freedBytes: 0, removedFiles: 0, keptFiles: 0, removedDirs: 0 };

  const isProtected = (rel) => keepSet.has(rel) || protectSet.has(rel);

  /** Delete every unprotected file under `relDir`, then drop emptied directories. */
  const prune = (relDir) => {
    let entries;
    try {
      entries = fs.readdirSync(path.join(snapAbs, relDir), { withFileTypes: true });
    } catch {
      return false; // vanished or unreadable: nothing to own here
    }
    let remaining = 0;
    for (const e of entries) {
      const rel = `${relDir}/${e.name}`;
      if (e.isDirectory()) {
        if (prune(rel)) remaining++;
        else continue;
        continue;
      }
      if (isProtected(rel)) {
        stats.keptFiles++;
        remaining++;
        continue;
      }
      let bytes = 0;
      try {
        bytes = fs.statSync(path.join(snapAbs, rel)).size;
        fs.rmSync(path.join(snapAbs, rel), { force: true });
      } catch {
        remaining++;
        continue;
      }
      stats.freedBytes += bytes;
      stats.removedFiles++;
    }
    if (remaining === 0) {
      try {
        fs.rmdirSync(path.join(snapAbs, relDir));
        stats.removedDirs++;
        return false;
      } catch {
        return true; // could not remove (locked); report as still present
      }
    }
    return true;
  };

  /** Find artifact directories at any depth; do not descend into them twice. */
  const walk = (relDir) => {
    let entries;
    try {
      entries = fs.readdirSync(path.join(snapAbs, relDir), { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      if (!e.isDirectory()) continue;
      const rel = relDir ? `${relDir}/${e.name}` : e.name;
      if (SNAPSHOT_ARTIFACT_DIRS.has(e.name)) {
        prune(rel);
        continue;
      }
      walk(rel);
    }
  };

  if (!fs.existsSync(snapAbs)) return stats;
  walk('');
  return stats;
}

/**
 * Prune a snapshot after a profile run has executed inside it.
 *
 * `stage-close` reclaimed these artifacts; `stage-review` used to leave them, so
 * every independent review quietly added ~1.9 GB of `Library` to its delivery
 * snapshot. This is the shared entry point for both.
 *
 * Policy:
 *   - manifest-listed files always survive (they are the delivery's identity);
 *   - the declared result file and declared run outputs survive (the review
 *     record cites their hashes);
 *   - when the run is *accepted* everything else regenerable goes;
 *   - when it is not, the diagnostic trees (`DIAGNOSTIC_ARTIFACT_DIRS`) are kept
 *     whole, because that is what a reviewer needs to see why the run failed.
 *
 * "Accepted" is deliberately not "exit code 0". A profile can exit 0 and still
 * fail: NUnit reports assertion failures in its result file, and the snapshot can
 * be mutated by the run itself (`postSnapshot.ok === false`). Both are failures
 * whose evidence lives in `Logs/` and `out/`, so the caller must decide — this
 * helper only enforces the policy once told.
 *
 * @param {string} snapAbs absolute snapshot directory
 * @param {object} opts
 * @param {string[]} [opts.keep] manifest-listed paths
 * @param {string[]} [opts.protect] extra paths to keep (result file, run outputs)
 * @param {boolean} [opts.ok] whether the run passed review (exit code, machine
 *   results and snapshot integrity all together); false widens the keep set
 * @returns {{stats:object, policy:string}}
 */
export function pruneSnapshotAfterRun(snapAbs, { keep = [], protect = [], ok = true } = {}) {
  const accepted = ok === true;
  const protectList = [...protect].filter(Boolean);
  if (!accepted) {
    const diagnostics = [];
    collectDiagnosticFiles(snapAbs, '', diagnostics);
    protectList.push(...diagnostics);
  }
  const stats = pruneSnapshotArtifacts(snapAbs, { keep, protect: protectList });
  return { stats, policy: accepted ? 'success' : 'failure-kept-diagnostics' };
}

/** Recursively list every file under `snapAbs/<relDir>` as snapshot-relative posix paths. */
function collectFilesUnder(snapAbs, relDir, out) {
  let entries;
  try {
    entries = fs.readdirSync(path.join(snapAbs, relDir), { withFileTypes: true });
  } catch {
    return; // directory absent: nothing to protect
  }
  for (const e of entries) {
    const rel = `${relDir}/${e.name}`;
    if (e.isDirectory()) {
      collectFilesUnder(snapAbs, rel, out);
      continue;
    }
    out.push(norm(rel));
  }
}

/**
 * Collect every file inside any directory named like a diagnostic artifact.
 *
 * Artifact trees sit at arbitrary depth (`Unity Project/Logs`, `tools/semcheck/obj`),
 * so matching on the directory *name* — the same rule `pruneSnapshotArtifacts` uses —
 * is the only way to protect exactly the trees it would otherwise delete.
 */
function collectDiagnosticFiles(snapAbs, relDir, out) {
  let entries;
  try {
    entries = fs.readdirSync(path.join(snapAbs, relDir), { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    const rel = relDir ? `${relDir}/${e.name}` : e.name;
    if (DIAGNOSTIC_ARTIFACT_DIRS.has(e.name)) {
      collectFilesUnder(snapAbs, rel, out);
      continue; // already collected the whole subtree
    }
    collectDiagnosticFiles(snapAbs, rel, out);
  }
}
