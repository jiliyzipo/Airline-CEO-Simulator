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
