// Reporting contract for the `collab` profile (run-all.mjs).
//
// Why this is a module and not a few inline helpers: on 2026-10-26 a review of
// DEL-20261026022132-804f18 reported "92 failed assertions" and nothing else —
// the per-suite output had been written into a temp directory that no longer
// existed, and the aggregate JSON carried counters without a single message. A
// verdict nobody can act on is not a verdict. The parts that must never regress:
//
//   resolveEvidenceDir — where the logs go, so they outlive the process
//   extractFailures    — the cause, deduplicated, so 21 identical failures read
//                        as one problem instead of 21
//   extractSkips       — a check the environment could not run, named
//   suiteVerdict       — skips never become a silent pass
import path from 'node:path';

/**
 * Prefer the caller-provided per-run directory (review-run and the delivery
 * preflight both pass COLLAB_RUN_DIR); fall back to the temp root so a bare
 * `node tools/collab/tests/run-all.mjs` still writes somewhere.
 */
export function resolveEvidenceDir(env, tempRoot) {
  const runDir = env?.COLLAB_RUN_DIR;
  if (typeof runDir === 'string' && runDir.trim()) return path.join(runDir, 'collab-suite');
  return tempRoot;
}

/**
 * Turn suite output into the reason(s) a human needs. The signature drops the
 * per-test identifier *and* the per-test name, so one broken fixture that fails
 * every test in a suite collapses to a single cause instead of burying it.
 */
export function extractFailures(stdout, stderr, limit = 4) {
  const lines = `${stdout || ''}\n${stderr || ''}`
    .split(/\r?\n/)
    .filter((l) => /^(FAIL|✗|not ok)\b/.test(l.trim()));
  const seen = new Map();
  for (const line of lines) {
    const message = line.trim().replace(/^(FAIL|✗|not ok)\s+\S+\s+/, '');
    const key = signatureOf(message);
    if (!seen.has(key)) seen.set(key, message.slice(0, 400));
  }
  return { first: lines.slice(0, limit), signatures: [...seen.values()].slice(0, limit) };
}

// Windows/filesystem errno codes are the tell of a broken fixture: every test in
// the suite reports its own name, but the errno tail is identical. Only these
// known codes are collapsed — an arbitrary uppercase word must not merge two
// genuinely different assertion failures into one.
const ERRNO = /\b(EACCES|EPERM|ENOENT|EBUSY|EEXIST|EINVAL|ENOTDIR|ENOTEMPTY|EMFILE|ENOSPC|EISDIR|EXDEV|ETIMEDOUT)\b/;

function signatureOf(message) {
  const hit = message.match(ERRNO);
  if (hit) {
    const tailStart = message.indexOf(hit[0]);
    return `${hit[0]}: ${message.slice(tailStart + hit[0].length).replace(/^:\s*/, '')}`.slice(0, 160);
  }
  return message.slice(0, 160);
}

/** A suite where nothing passed is a fixture verdict, not N product regressions. */
export function isFixtureBroken(passed, failed) {
  return failed > 0 && passed === 0;
}

/**
 * Skips are a third outcome, never a fourth kind of pass. A skipped assertion
 * says "this environment could not execute the check" — the reviewer must see
 * the reason and decide, which is why they are extracted and not just counted.
 */
export function extractSkips(stdout, limit = 4) {
  const lines = `${stdout || ''}`
    .split(/\r?\n/)
    .filter((l) => /^SKIP\b/.test(l.trim()));
  const seen = new Map();
  for (const line of lines) {
    const trimmed = line.trim();
    // `SKIP <id> <name>: <reason>` — the reason is what a human acts on, and it
    // is identical across every check blocked by the same capability gap.
    const at = trimmed.indexOf(': ');
    const reason = at >= 0 ? trimmed.slice(at + 2) : trimmed.replace(/^SKIP\s+/, '');
    const key = reason.slice(0, 160);
    if (!seen.has(key)) seen.set(key, reason.slice(0, 400));
  }
  return [...seen.values()].slice(0, limit);
}

/**
 * The profile verdict. A skip is not a failure, but a suite where *every* check
 * was skipped proves nothing: `passed > 0` keeps an all-skipped run from
 * passing, while `failed === 0` keeps a skip from hiding a real regression.
 */
export function suiteVerdict({ exitCode, passed, failed }) {
  return exitCode === 0 && failed === 0 && passed > 0;
}
