import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {
  IMPLEMENTATION_ROOTS,
  VERIFICATION_ROOTS,
  collectEntries,
  hashManifest,
} from './fingerprint.mjs';
import { ensureDir } from './io.mjs';
import { EXIT, fail } from './exit.mjs';

function copyFileStable(src, dest) {
  ensureDir(path.dirname(dest));
  const bytes = fs.readFileSync(src);
  fs.writeFileSync(dest, bytes);
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

/**
 * Export an isolated snapshot. Retries if source changes mid-copy.
 * Never tests against a mixed version.
 */
export function exportStableSnapshot(
  repoRoot,
  destRoot,
  { maxAttempts = 3, roots = null } = {},
) {
  const useRoots =
    roots || [...new Set([...IMPLEMENTATION_ROOTS, ...VERIFICATION_ROOTS])];
  const rootsList = useRoots;
  let lastErr = null;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const before = collectEntries(repoRoot, rootsList);
    const beforeHash = hashManifest(before);
    if (fs.existsSync(destRoot)) {
      fail(EXIT.CONFLICT, `Snapshot destination already exists: ${destRoot}`);
    }
    ensureDir(destRoot);
    const copied = [];
    try {
      for (const e of before) {
        const src = path.join(repoRoot, e.path);
        const dest = path.join(destRoot, e.path);
        const hash = copyFileStable(src, dest);
        if (hash !== e.sha256) {
          throw Object.assign(new Error(`Hash drift while copying ${e.path}`), {
            code: 'DRIFT',
          });
        }
        copied.push({ path: e.path, sha256: hash, bytes: e.bytes });
      }
      const settingsRel = 'Unity Project/ProjectSettings/ProjectSettings.asset';
      const settingsPath = path.join(destRoot, settingsRel);
      let overlayApplied = false;
      if (fs.existsSync(settingsPath)) {
        const text = fs
          .readFileSync(settingsPath, 'utf8')
          .replace(/^(\s*companyName:).*$/m, '$1 AirlineCEORegression')
          .replace(
            /^(\s*productName:).*$/m,
            `$1 IsolatedBaseline_${path.basename(destRoot)}`,
          );
        fs.writeFileSync(settingsPath, text);
        overlayApplied = true;
      }
      // Record the post-overlay (as-executed) hash of every snapshot file so the
      // integrity check can validate the full set, including ProjectSettings.
      for (const e of copied) {
        if (overlayApplied && e.path === settingsRel) {
          e.snapshotSha256 = crypto
            .createHash('sha256')
            .update(fs.readFileSync(path.join(destRoot, e.path)))
            .digest('hex');
        } else {
          e.snapshotSha256 = e.sha256;
        }
      }
      const after = collectEntries(repoRoot, rootsList);
      const afterHash = hashManifest(after);
      if (afterHash !== beforeHash) {
        throw Object.assign(new Error('Source changed during snapshot copy'), {
          code: 'SOURCE_CHANGED',
        });
      }
      const manifest = {
        schemaVersion: 1,
        attempt,
        sourceFingerprint: beforeHash,
        copiedFileCount: copied.length,
        overlay:
          'Only exported ProjectSettings companyName/productName changed for player-data isolation when present.',
        cleanGitCheckoutVerified: false,
        entries: copied,
      };
      fs.writeFileSync(
        path.join(destRoot, 'collab-snapshot-manifest.json'),
        JSON.stringify(manifest, null, 2) + '\n',
      );
      return manifest;
    } catch (e) {
      lastErr = e;
      fs.rmSync(destRoot, { recursive: true, force: true });
      if (e.code !== 'SOURCE_CHANGED' && e.code !== 'DRIFT') throw e;
    }
  }
  fail(
    EXIT.CHECK_FAILED,
    `Failed to create stable snapshot after ${maxAttempts} attempts: ${lastErr?.message || lastErr}`,
  );
}
