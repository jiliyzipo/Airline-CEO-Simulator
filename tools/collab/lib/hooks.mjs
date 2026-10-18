import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { localDirs } from './paths.mjs';
import { ensureDir, FileLock, nowIso, readJson, writeJsonAtomic } from './io.mjs';
import { EXIT, fail } from './exit.mjs';

/**
 * Hook adapters only translate events into the same CLI. They never keep a
 * second copy of task state and never infer "task complete": the strongest
 * thing a hook may do is refresh collaboration state (sync).
 */
export const HOOK_SUPPORT = {
  cursor: {
    events: ['sessionStart', 'afterFileEdit', 'stop', 'taskEnd'],
    configHint: 'project-level .cursor/hooks.json (adapter example: tools/collab/adapters/cursor-hooks.json.example)',
  },
  codex: {
    events: ['SessionStart', 'PostToolUse', 'Stop', 'sessionStart', 'toolEnd', 'turnEnd'],
    configHint: 'project .codex/hooks.json; requires client trust (see adapters/codex-hooks.json.example)',
  },
};

const FILE_EVENT_RE = /file|edit|save/i;
const MAX_SEEN = 400;

function statePath(cfg) {
  return path.join(localDirs(cfg).root, 'hooks', 'state.json');
}
function logPath(cfg) {
  return path.join(localDirs(cfg).root, 'hooks', 'events.jsonl');
}

function emptyState() {
  return { schemaVersion: 1, seen: {}, lastAppliedAt: {}, counters: { applied: 0, deduped: 0 } };
}

export function loadHookState(cfg) {
  return readJson(statePath(cfg), emptyState());
}

function stableStringify(value) {
  if (value === null || typeof value !== 'object') return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`;
  const keys = Object.keys(value).sort();
  return `{${keys.map((k) => `${JSON.stringify(k)}:${stableStringify(value[k])}`).join(',')}}`;
}

export function hookKey({ source, event, payload, idempotencyKey }) {
  const body = idempotencyKey
    ? `id:${source}:${event}:${idempotencyKey}`
    : stableStringify({
        source,
        event,
        session: payload?.sessionId || payload?.session_id || null,
        cwd: payload?.cwd || null,
        file: payload?.file || payload?.path || payload?.file_path || null,
        tool: payload?.tool || payload?.toolName || null,
        turn: payload?.turnId || payload?.turn_id || null,
        call: payload?.tool_use_id || payload?.tool_call_id || payload?.event_id || null,
        edits: payload?.edits || null,
        fileVersion: payload?.fileVersion || null,
        text: payload?.text || payload?.message || null,
      });
  return crypto.createHash('sha256').update(body, 'utf8').digest('hex');
}

/**
 * Record and apply one hook event.
 * @param apply function that performs the real work (a sync), returning {revision}
 */
export function runHook(cfg, args) {
  // Keep dedupe read, application and state publication in one critical section.
  // A competing client can retry after LOCKED and then observe the recorded key.
  const lock = new FileLock(path.join(localDirs(cfg).locks, 'hooks.lock'), `hook:${process.pid}`);
  const got = lock.acquire();
  if (!got.ok) fail(EXIT.LOCKED, got.message, got);
  try {
    return runHookUnlocked(cfg, args);
  } finally {
    lock.release();
  }
}

function runHookUnlocked(cfg, { source, event, payload = {}, idempotencyKey = null, now = Date.now(), apply }) {
  if (!HOOK_SUPPORT[source]) {
    fail(EXIT.USAGE, `unknown hook source: ${source} (expected cursor|codex)`);
  }
  if (!event || typeof event !== 'string') fail(EXIT.USAGE, 'hook event name is required');
  if (typeof apply !== 'function') fail(EXIT.INTERNAL, 'hook apply callback missing');

  const enabled = cfg.hooks?.enabled !== false;
  const dedupeWindowMs = Number(cfg.hooks?.dedupeWindowMs ?? 60_000);
  const debounceMs = Number(cfg.hooks?.debounceMs ?? 2000);
  const key = hookKey({ source, event, payload, idempotencyKey });
  const state = loadHookState(cfg);
  const previous = state.seen?.[key] || null;
  const isFileEvent = FILE_EVENT_RE.test(event);

  const record = (result) => {
    ensureDir(path.dirname(logPath(cfg)));
    fs.appendFileSync(
      logPath(cfg),
      JSON.stringify({
        at: nowIso(),
        source,
        event,
        key: key.slice(0, 16),
        supportedEvent: HOOK_SUPPORT[source].events.includes(event),
        enabled,
        ...result,
      }) + '\n',
      'utf8',
    );
  };

  if (!enabled) {
    const result = {
      source,
      event,
      key: key.slice(0, 16),
      applied: false,
      deduped: false,
      reason: 'hooks disabled in config; manual sync/deliver flow is unaffected',
      revision: null,
    };
    record(result);
    return result;
  }

  if (previous && now - previous.at < dedupeWindowMs) {
    state.counters = state.counters || { applied: 0, deduped: 0 };
    state.counters.deduped += 1;
    writeJsonAtomic(statePath(cfg), state);
    const result = {
      source,
      event,
      key: key.slice(0, 16),
      applied: false,
      deduped: true,
      reason: `duplicate event within ${dedupeWindowMs}ms; nothing re-applied`,
      previousAt: new Date(previous.at).toISOString(),
      revision: previous.revision ?? null,
    };
    record(result);
    return result;
  }

  const lastApplied = state.lastAppliedAt?.[source] || 0;
  if (isFileEvent && lastApplied && now - lastApplied < debounceMs && previous) {
    state.counters = state.counters || { applied: 0, deduped: 0 };
    state.counters.deduped += 1;
    writeJsonAtomic(statePath(cfg), state);
    const result = {
      source,
      event,
      key: key.slice(0, 16),
      applied: false,
      deduped: true,
      reason: `debounced: last ${source} sync ${now - lastApplied}ms ago (< ${debounceMs}ms)`,
      revision: null,
    };
    record(result);
    return result;
  }

  const applied = apply();
  const revision = applied?.revision ?? null;
  state.seen = state.seen || {};
  state.seen[key] = { at: now, revision };
  const keys = Object.keys(state.seen);
  if (keys.length > MAX_SEEN) {
    keys
      .sort((a, b) => state.seen[a].at - state.seen[b].at)
      .slice(0, keys.length - MAX_SEEN)
      .forEach((k) => delete state.seen[k]);
  }
  state.lastAppliedAt = { ...(state.lastAppliedAt || {}), [source]: now };
  state.counters = state.counters || { applied: 0, deduped: 0 };
  state.counters.applied += 1;
  state.updatedAt = nowIso();
  ensureDir(path.dirname(statePath(cfg)));
  writeJsonAtomic(statePath(cfg), state);

  const result = {
    source,
    event,
    key: key.slice(0, 16),
    applied: true,
    deduped: false,
    reason: 'synced collaboration state (hooks never create deliveries)',
    revision,
  };
  record(result);
  return result;
}

export function hookStatus(cfg) {
  const state = loadHookState(cfg);
  const logAbs = logPath(cfg);
  let recent = [];
  if (fs.existsSync(logAbs)) {
    const lines = fs.readFileSync(logAbs, 'utf8').trim().split(/\r?\n/).filter(Boolean);
    recent = lines.slice(-5).map((l) => {
      try {
        return JSON.parse(l);
      } catch {
        return { malformed: true };
      }
    });
  }
  return {
    enabled: cfg.hooks?.enabled !== false,
    debounceMs: Number(cfg.hooks?.debounceMs ?? 2000),
    dedupeWindowMs: Number(cfg.hooks?.dedupeWindowMs ?? 60_000),
    adapters: HOOK_SUPPORT,
    clients: Object.fromEntries(['cursor', 'codex'].map(source => [source, {
      mode: cfg.hooks?.clients?.[source]?.mode || 'manual',
      reason: cfg.hooks?.clients?.[source]?.reason || 'Native client loading and trust are unverified. Use manual sync or foreground watch.',
      configFound: fs.existsSync(path.join(cfg._repoRoot, `.${source}/hooks.json`)),
      nativeVerified: false,
      cliAppliedDoesNotProveNativeTrigger: true,
    }])),
    counters: state.counters || { applied: 0, deduped: 0 },
    lastAppliedAt: state.lastAppliedAt || {},
    recent,
    manualFallback: 'Hooks are optional: sync/deliver/review-* work without them.',
  };
}
