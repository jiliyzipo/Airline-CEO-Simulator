// Allowed review profile for C-DEMO. Fixed command; no shell interpolation.
import fs from 'node:fs';
import path from 'node:path';

const reviewId = process.env.COLLAB_REVIEW_ID || 'unknown';
const out = {
  profile: 'smoke',
  reviewId,
  assertionsTotal: 2,
  assertionsFailed: 0,
  assertionsSkipped: 0,
  checks: [
    { name: 'cli-exists', ok: fs.existsSync(path.resolve('tools/collab/cli.mjs')) },
    { name: 'protocol-exists', ok: fs.existsSync(path.resolve('docs/collaboration/PROTOCOL.md')) },
  ],
};
const failed = out.checks.filter((c) => !c.ok);
out.assertionsFailed = failed.length;
out.assertionsTotal = out.checks.length;
console.log(JSON.stringify(out, null, 2));
if (failed.length) process.exit(1);
process.exit(0);
