import {
  IMPLEMENTATION_ROOTS,
  VERIFICATION_ROOTS,
  dedupeRoots,
} from './fingerprint.mjs';

const norm = (p) => String(p).replace(/\\/g, '/');

/**
 * Resolve the snapshot/fingerprint input set for one task.
 *
 * Precedence:
 *  1. task-declared roots (`snapshotRoots` + exact `snapshotFiles`) — a real
 *     engineering task can then pin exactly what its profiles read;
 *  2. the configured demo root set for the demo task (narrow on purpose);
 *  3. the project-wide implementation ∪ verification roots.
 *
 * The declared file list is part of the identity, so a delivery cannot be
 * "current" while its snapshot silently gained or lost an input file.
 */
export function resolveTaskRoots(cfg, task) {
  if (task && Array.isArray(task.snapshotRoots) && task.snapshotRoots.length > 0) {
    const files = (task.snapshotFiles || []).map(norm);
    const roots = dedupeRoots([...task.snapshotRoots.map(norm), ...files]);
    return {
      implementationRoots: roots,
      verificationRoots: roots,
      extraFiles: files,
      source: 'task',
    };
  }
  if (task && task.taskId === cfg.demoTaskId && Array.isArray(cfg.demoSnapshotRoots)) {
    const files = (cfg.demoSnapshotFiles || []).map(norm);
    const roots = dedupeRoots([...cfg.demoSnapshotRoots.map(norm), ...files]);
    return {
      implementationRoots: roots,
      verificationRoots: roots,
      extraFiles: files,
      source: 'demo',
    };
  }
  return {
    implementationRoots: IMPLEMENTATION_ROOTS,
    verificationRoots: VERIFICATION_ROOTS,
    extraFiles: [],
    source: 'default',
  };
}
