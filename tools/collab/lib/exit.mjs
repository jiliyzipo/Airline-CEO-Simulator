/** Fixed exit codes for collab CLI (see docs/collaboration/PROTOCOL.md). */
export const EXIT = {
  OK: 0,
  USAGE: 2,
  CONFLICT: 3,
  LOCKED: 4,
  CHECK_FAILED: 5,
  EVIDENCE: 6,
  FORBIDDEN: 7,
  INTERNAL: 1,
};

export function fail(code, message, extra = {}) {
  const err = new Error(message);
  err.exitCode = code;
  err.extra = extra;
  throw err;
}
