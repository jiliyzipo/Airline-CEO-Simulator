import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { collabDirs } from './paths.mjs';
import { resolveTaskRoots } from './roots.mjs';
import { stableContractPayload } from './schemas.mjs';
import { isArtifactDirName, isGeneratedFileName } from './fingerprint.mjs';
import { listJsonRecords, writeJsonAtomic, nowIso } from './io.mjs';
import { generateSummaries, listDeliveries, loadState, recordEvent, saveState, withStateLock } from './state.mjs';
import { reconcileManagedCheckboxes } from './taskbook.mjs';
import { EXIT, fail } from './exit.mjs';
const sha = s => crypto.createHash('sha256').update(s).digest('hex');
const contains = (r, p) => p === r || p.startsWith(r + '/');
const ignored = p => p.split('/').some(s => isArtifactDirName(s)) || isGeneratedFileName(path.basename(p)) || /\.tmp$|\.publish-lock$/.test(p);

// Fast conservative invalidation, not a cached claim of a successful hash check.
// A later explicit sync rehashes bytes and may restore applicability after undo.
export function invalidatePaths(cfg, paths) {
  paths = [...new Set(paths.map(p => p.replace(/\\/g, '/')).filter(p => !ignored(p)))];
  if (!paths.length) return [];
  return withStateLock(cfg, 'watch-invalidate', () => {
    const dirs = collabDirs(cfg), deliveries = listDeliveries(cfg), state = loadState(cfg), changed = [];
    for (const task of listJsonRecords(dirs.tasks)) {
      if (task.readOnlyReference) continue;
      const delivery = [...deliveries].reverse().find(d => d.taskId === task.taskId);
      if (!delivery) continue;
      const roots = resolveTaskRoots(cfg, task);
      const taskFile = `${cfg.collaborationRoot}/tasks/${task.taskId}.json`;
      const contractChanged = paths.includes(taskFile) && sha(JSON.stringify(stableContractPayload(task))) !== delivery.fingerprints.contract.fingerprint;
      const relevant = paths.filter(p => [...roots.implementationRoots, ...roots.verificationRoots].some(r => contains(r, p)));
      if (!contractChanged && !relevant.length) continue;
      if (task.freshness === 'stale') continue;
      task.freshness = 'stale';
      task.freshnessNote = `Watch observed changed input (${contractChanged ? 'acceptance contract' : relevant[0]}); run sync to rehash. Historical reviews remain unchanged.`;
      task.updatedAt = nowIso();
      writeJsonAtomic(path.join(dirs.tasks, `${task.taskId}.json`), task);
      if (state.tasksIndex[task.taskId]) state.tasksIndex[task.taskId].freshness = 'stale';
      changed.push(task.taskId);
    }
    if (changed.length) {
      saveState(cfg, state, { expectedRevision: state.revision });
      reconcileManagedCheckboxes(cfg);
      recordEvent(cfg, { actor: 'system', action: 'watch-invalidate', summary: `changed inputs: ${changed.join(', ')}`, data: { taskIds: changed, paths: paths.slice(0, 30) } });
      generateSummaries(cfg);
    }
    return changed;
  });
}

function metadata(cfg, roots) {
  const result = new Map();
  const walk = rel => {
    if (ignored(rel)) return;
    const abs = path.join(cfg._repoRoot, rel);
    if (!fs.existsSync(abs)) return;
    const stat = fs.lstatSync(abs, { bigint: true });
    if (stat.isSymbolicLink()) return;
    if (stat.isDirectory()) for (const name of fs.readdirSync(abs)) walk(`${rel}/${name}`);
    else result.set(rel, `${stat.size}:${stat.mtimeNs}:${stat.ctimeNs}`);
  };
  for (const root of roots) walk(root);
  return result;
}

export function startWatch(cfg, args, sync) {
  const interval = Number(args.interval || 3) * 1000;
  if (!Number.isFinite(interval) || interval < 250 || interval > 4000) fail(EXIT.USAGE, 'watch interval must be between 0.25 and 4 seconds');
  console.error(`collab watch pid=${process.pid} cwd=${cfg._repoRoot} intervalMs=${interval}; Ctrl+C to stop`);
  sync(cfg); // Establish initial byte-verified state before announcing readiness.
  const roots = () => {
    const ids = new Set(listDeliveries(cfg).map(d => d.taskId));
    const tasks = listJsonRecords(collabDirs(cfg).tasks).filter(t => ids.has(t.taskId));
    return [...new Set(tasks.flatMap(t => {
      const r = resolveTaskRoots(cfg, t);
      return [...r.implementationRoots, ...r.verificationRoots, `${cfg.collaborationRoot}/tasks/${t.taskId}.json`];
    }))];
  };
  let before = metadata(cfg, roots()), pending = new Set(), timerPending = null;
  const flush = () => {
    timerPending = null;
    try {
      const changed = invalidatePaths(cfg, [...pending]); pending.clear();
      if (changed.length) console.log(JSON.stringify({ at: nowIso(), revision: loadState(cfg).revision, staleTasks: changed }));
    } catch (e) { console.error(`watch retry pending: ${e.message}`); }
  };
  const queue = rel => { pending.add(rel); if (!timerPending) timerPending = setTimeout(flush, 50); };
  const watchers = [];
  for (const rel of roots()) {
    const abs = path.join(cfg._repoRoot, rel);
    if (!fs.existsSync(abs)) continue;
    try {
      const isDir = fs.statSync(abs).isDirectory();
      const watcher = fs.watch(abs, { recursive: isDir }, (_kind, name) => queue(isDir ? `${rel}/${String(name || '')}`.replace(/\\/g, '/') : rel));
      watcher.on('error', e => console.error('watch notifications unavailable, polling continues:', e.message));
      watchers.push(watcher);
    } catch (e) { console.error('watch notifications unavailable, polling continues:', e.message); }
  }
  const timer = setInterval(() => {
    try {
      const after = metadata(cfg, roots());
      for (const p of new Set([...before.keys(), ...after.keys()])) if (before.get(p) !== after.get(p)) queue(p);
      before = after;
      if (pending.size && !timerPending) timerPending = setTimeout(flush, 50);
    } catch (e) { console.error('watch scan failed:', e.message); }
  }, interval);
  const stop = () => { clearInterval(timer); clearTimeout(timerPending); watchers.forEach(w => w.close()); process.exit(0); };
  process.on('SIGINT', stop); process.on('SIGTERM', stop);
  console.log(JSON.stringify({ watching: true, ready: true, pid: process.pid, cwd: cfg._repoRoot, intervalMs: interval, mode: 'conservative-invalidation; explicit sync verifies bytes' }));
  return { watching: true, pid: process.pid };
}
