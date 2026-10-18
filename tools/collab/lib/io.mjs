import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';

export function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

export function readJson(file, fallback = null) {
  if (!fs.existsSync(file)) return fallback;
  try {
    const text = fs.readFileSync(file, 'utf8');
    if (!text.trim()) return fallback;
    return JSON.parse(text);
  } catch {
    return fallback;
  }
}

export function writeJsonAtomic(file, data) {
  writeFileAtomic(file, JSON.stringify(data, null, 2) + '\n');
}

function sleepSync(ms) {
  const sab = new Int32Array(new SharedArrayBuffer(4));
  Atomics.wait(sab, 0, 0, ms);
}

/**
 * Windows antivirus/indexers can hold a freshly written temp file for a few
 * milliseconds; a single rename then fails with EPERM/EBUSY. Retry briefly
 * instead of losing a state write (observed on this machine).
 */
function renameWithRetry(from, to) {
  let lastErr;
  for (let attempt = 0; attempt < 10; attempt++) {
    try {
      fs.renameSync(from, to);
      return;
    } catch (e) {
      lastErr = e;
      if (!['EPERM', 'EACCES', 'EBUSY'].includes(e.code)) break;
      sleepSync(15 * (attempt + 1));
    }
  }
  throw lastErr;
}

/** Write `text` via a temp file + rename so readers never see half a file. */
export function writeFileAtomic(file, text) {
  ensureDir(path.dirname(file));
  const tmp = file + '.' + process.pid + '.' + crypto.randomBytes(4).toString('hex') + '.tmp';
  fs.writeFileSync(tmp, text, 'utf8');
  try {
    renameWithRetry(tmp, file);
  } catch (e) {
    try {
      fs.unlinkSync(tmp);
    } catch {
      /* leave nothing behind when we can */
    }
    throw e;
  }
}

export function appendJsonRecord(dir, id, data) {
  ensureDir(dir);
  const file = path.join(dir, `${id}.json`);
  if (fs.existsSync(file)) {
    const err = new Error(`Record already exists and is immutable: ${file}`);
    err.code = 'IMMUTABLE';
    throw err;
  }
  // A per-ID exclusive publication guard closes the exists/rename race without
  // requiring hardlinks (which the Windows agent sandbox does not permit).
  const guard = file + '.publish-lock';
  const fd = fs.openSync(guard, 'wx');
  try {
    if (fs.existsSync(file)) throw Object.assign(new Error(`Immutable record exists: ${file}`), { code: 'IMMUTABLE' });
    writeJsonAtomic(file, data);
  } finally { fs.closeSync(fd); fs.unlinkSync(guard); }
  return file;
}

export function listJsonRecords(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs
    .readdirSync(dir)
    .filter((n) => n.endsWith('.json'))
    .sort()
    .map((n) => readJson(path.join(dir, n)));
}

export class FileLock {
  constructor(lockPath, owner, extra = {}) {
    this.lockPath = lockPath;
    this.owner = owner;
    this.extra = extra || {};
  }

  acquire({ staleMs = 30 * 60 * 1000, _reclaimAttempted = false } = {}) {
    ensureDir(path.dirname(this.lockPath));
    const payload = {
      owner: this.owner,
      pid: process.pid,
      machine: os.hostname(),
      startedAt: new Date().toISOString(),
      heartbeatAt: new Date().toISOString(),
      ...this.extra,
    };
    try {
      const fd = fs.openSync(this.lockPath, 'wx');
      try {
        fs.writeFileSync(fd, JSON.stringify(payload, null, 2) + '\n');
        fs.fsyncSync(fd);
      } finally {
        fs.closeSync(fd);
      }
      return { ok: true, lock: payload };
    } catch (e) {
      if (e.code !== 'EEXIST') throw e;
      let existing = readJson(this.lockPath, null);
      // A competitor that just created the file may not have flushed its payload
      // yet. Never reclaim a lock whose bytes are still being written: that race
      // would let two processes own the same lease (see TH-05/T07).
      if (!existing) {
        let ageMs = Infinity;
        try {
          ageMs = Date.now() - fs.statSync(this.lockPath).mtimeMs;
        } catch {
          /* file vanished between open and stat: treat as released */
          return this.acquire({ staleMs, _reclaimAttempted: true });
        }
        if (ageMs < 5000) {
          return {
            ok: false,
            existing: null,
            staleSuspect: false,
            pidAlive: true,
            message: 'Lock is being initialized by another process; retry shortly.',
          };
        }
        existing = readJson(this.lockPath, null);
      }
      const age = existing?.heartbeatAt
        ? Date.now() - Date.parse(existing.heartbeatAt)
        : Infinity;
      const pidAlive = existing?.pid ? isPidAlive(existing.pid) : false;
      // Corrupt lock or dead lock owner: reclaim once (never use this class for Unity project locks).
      if (!_reclaimAttempted && existing?.machine === os.hostname() &&
          Number.isInteger(existing?.pid) && existing.pid > 0 && !pidAlive) {
        try {
          fs.unlinkSync(this.lockPath);
          return this.acquire({ staleMs, _reclaimAttempted: true });
        } catch {
          /* fall through */
        }
      }
      return {
        ok: false,
        existing,
        staleSuspect: age > staleMs && !pidAlive,
        pidAlive,
        message:
          `Lock held by ${existing?.owner || 'unknown'} pid=${existing?.pid || '?'} ` +
          `(alive=${pidAlive}, ageMs=${Number.isFinite(age) ? age : 'unknown'}). ` +
          `Do not delete Unity project locks. Use collab unlock only for this tool's locks.`,
      };
    }
  }

  release({ force = false } = {}) {
    if (!fs.existsSync(this.lockPath)) return { ok: true };
    const existing = readJson(this.lockPath, {});
    if (!force && existing.pid && existing.pid !== process.pid && isPidAlive(existing.pid)) {
      return { ok: false, existing, message: 'Lock owned by another live process' };
    }
    fs.unlinkSync(this.lockPath);
    return { ok: true };
  }
}

export function isPidAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    // Permission failures cannot prove a process is dead.
    return error.code !== 'ESRCH';
  }
}

export function nowIso() {
  return new Date().toISOString();
}

export function newId(prefix) {
  const stamp = new Date().toISOString().replace(/[-:TZ.]/g, '').slice(0, 14);
  const rand = crypto.randomBytes(3).toString('hex');
  return `${prefix}-${stamp}-${rand}`;
}
