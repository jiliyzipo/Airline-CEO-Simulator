import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

/**
 * Temp fixture directories that clean themselves up.
 *
 * Suites used to call `fs.mkdtempSync` directly and never remove the result. One
 * `run-all` run left ~40 trees behind, and the system temp directory had
 * accumulated 3,296 of them / 5.6 GiB before anyone noticed. Nothing in a green
 * run needs them afterwards, so ownership is inverted: whoever creates one here
 * gets it removed once the suite reports its verdict.
 *
 * Deleting from a module-load `exit` listener is not enough. `lib/hashcache.mjs`
 * also registers `process.once('exit')` to flush its cache, and exit listeners run
 * in registration order — so an early hook deleted the tree and the cache flush
 * then recreated it with a lone `fingerprint-hash-cache.json` inside. That is why
 * `finalizeFixtures()` must be called at the *end* of a suite: its listener is
 * registered last, so it runs last.
 *
 * A failing run keeps everything, because the tree *is* the evidence.
 */
const created = [];

export function mkTemp(prefix) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  created.push(dir);
  return dir;
}

function sweep() {
  for (const dir of created) {
    try {
      fs.rmSync(dir, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 });
    } catch (e) {
      // A handle still open (Windows) must not turn a green run red, but staying
      // silent keeps the leak invisible.
      console.error(`夹具未能清理（${e.code || 'ERR'}）：${dir}`);
    }
  }
}

/** Register `fn` to run on exit, after any hook registered earlier. */
export function deferCleanup(fn) {
  process.on('exit', fn);
}

/**
 * Call once, at the end of a suite, with the verdict.
 * Green → every fixture this process created is removed. Failed → all kept.
 */
export function finalizeFixtures(failed) {
  process.on('exit', () => {
    if (failed) {
      if (created.length) {
        console.error(`夹具保留（有失败），共 ${created.length} 个：\n  ${created.join('\n  ')}`);
      }
      return;
    }
    sweep();
  });
}
