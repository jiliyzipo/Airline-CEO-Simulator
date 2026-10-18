import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { stableContractPayload } from './schemas.mjs';

export const FINGERPRINT_POLICY_VERSION = 2;

const EXCLUDE_DIR_NAMES = new Set([
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
  'node_modules',
]);

const EXCLUDE_FILE_RE =
  /SegoeUISymbol\.ttf(?:\.meta)?$|\.log$|\.bak$|semcheck_report\.txt$|channel_report\.txt$|^\.env$/i;

/** Generated report/log files that exist only as run output, never as source. */
export function isGeneratedFileName(name) {
  return EXCLUDE_FILE_RE.test(name);
}

/** Build/run artifact directory names (never reviewable source). */
export function isArtifactDirName(name) {
  return EXCLUDE_DIR_NAMES.has(name);
}

/** Generated collaboration traffic must never feed the implementation identity. */
const COLLAB_ROOT = 'docs/collaboration';
const COLLAB_EXCLUDED_DIRS = new Set([
  'events',
  'deliveries',
  'reviews',
  'issues',
  'tasks',
]);
const COLLAB_EXCLUDED_FILES = new Set([
  'state.json',
  'CURRENT.md',
  'NEXT_CURSOR.md',
  'NEXT_CODEX.md',
]);

function isGeneratedCollabPath(norm) {
  if (!norm.startsWith(`${COLLAB_ROOT}/`)) return false;
  const rest = norm.slice(COLLAB_ROOT.length + 1);
  const first = rest.split('/')[0];
  if (COLLAB_EXCLUDED_DIRS.has(first)) return true;
  if (!rest.includes('/') && COLLAB_EXCLUDED_FILES.has(rest)) return true;
  return false;
}

/** Roots scanned for implementation fingerprint. */
export const IMPLEMENTATION_ROOTS = [
  'Unity Project/Assets',
  'Unity Project/Packages',
  'Unity Project/ProjectSettings',
  'tools/balance',
  'tools/semcheck',
  'tools/slotcheck',
  'tools/regression',
  'tools/collab',
];

/** Verification tools and in-engine tests. */
export const VERIFICATION_ROOTS = [
  'Unity Project/Assets/Tests',
  'tools/semcheck',
  'tools/slotcheck',
  'tools/regression',
  'tools/balance',
  'tools/collab/tests',
  'tools/collab/config.example.json',
];

const IMPL_EXCLUDE_PREFIXES = [
  'Unity Project/Assets/Tests/',
  // Verification-owned tool tests must not mutate implementation identity.
  'tools/collab/tests/',
];

function sha256Bytes(buf) {
  return crypto.createHash('sha256').update(buf).digest('hex');
}

function walkFiles(repoRoot, relative, out) {
  const absolute = path.join(repoRoot, relative);
  if (!fs.existsSync(absolute)) return;
  const st = fs.lstatSync(absolute);
  if (st.isSymbolicLink()) throw new Error(`Refusing snapshot symlink/junction: ${relative}`);
  const name = path.basename(relative);
  if (st.isDirectory()) {
    if (isArtifactDirName(name)) return;
    const normDir = relative.replace(/\\/g, '/');
    if (isGeneratedCollabPath(`${normDir}/x`)) return;
    for (const child of fs.readdirSync(absolute).sort()) {
      walkFiles(repoRoot, `${relative}/${child}`.replace(/\\/g, '/'), out);
    }
    return;
  }
  if (isGeneratedFileName(name)) return;
  const norm = relative.replace(/\\/g, '/');
  if (norm === 'tools/collab/config.json') return; // machine-local config is never shared
  if (isGeneratedCollabPath(norm)) return;
  const bytes = fs.readFileSync(absolute);
  if (/\.(asset|json|txt|ya?ml)$/i.test(name)) {
    const text = bytes.toString('utf8');
    if (/^[ \t]*(?:apiKey|accessToken|secret)[ \t]*:[ \t]*[^\s"']+/mi.test(text)) {
      const err = new Error(`Refusing to fingerprint credential field in ${relative}`);
      err.code = 'CREDENTIAL';
      throw err;
    }
  }
  out.push({
    path: relative.replace(/\\/g, '/'),
    bytes: bytes.length,
    sha256: sha256Bytes(bytes),
  });
}

export function collectEntries(repoRoot, roots) {
  const raw = [];
  for (const root of roots) walkFiles(repoRoot, root, raw);
  const map = new Map();
  for (const e of raw) map.set(e.path, e);
  return [...map.values()].sort((a, b) => a.path.localeCompare(b.path));
}

export function dedupeRoots(roots) {
  return [...new Set(roots.map((r) => r.replace(/\\/g, '/')))];
}

export function hashManifest(entries) {
  return sha256Bytes(
    Buffer.from(entries.map((e) => `${e.path}:${e.sha256}`).join('\n'), 'utf8'),
  );
}

export function gitMeta(repoRoot) {
  try {
    const head = execFileSync('git', ['rev-parse', 'HEAD'], {
      cwd: repoRoot,
      encoding: 'utf8',
    }).trim();
    const branch = execFileSync('git', ['rev-parse', '--abbrev-ref', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim();
    const status = execFileSync('git', ['status', '--porcelain=v1', '-z', '--untracked-files=all'], {
      cwd: repoRoot,
      encoding: 'utf8',
      maxBuffer: 16 * 1024 * 1024,
    });
    const parts = status.split('\0'), changes = [];
    for (let i = 0; i < parts.length; i++) {
      if (!parts[i]) continue;
      const statusCode = parts[i].slice(0, 2), file = parts[i].slice(3);
      const row = { status: statusCode, path: file };
      if (/[RC]/.test(statusCode)) row.previousPath = parts[++i];
      changes.push(row);
    }
    const tracked = new Set(
      execFileSync('git', ['ls-files', '-z'], { cwd: repoRoot, encoding: 'utf8' })
        .split('\0')
        .filter(Boolean),
    );
    return {
      head,
      branch,
      changes,
      dirty: status.trim().length > 0,
      porcelain: status,
      tracked,
      cleanGitCheckoutVerified: false,
    };
  } catch {
    return {
      head: null,
      dirty: true,
      porcelain: '',
      tracked: new Set(),
      cleanGitCheckoutVerified: false,
    };
  }
}

function filterImplementationEntries(entries) {
  return entries.filter(
    (e) => !IMPL_EXCLUDE_PREFIXES.some((p) => e.path.startsWith(p)),
  );
}

export function computeFingerprints(
  repoRoot,
  {
    contractPaths = [],
    contractPayload = null,
    implementationRoots = null,
    verificationRoots = null,
    includeGit = true,
  } = {},
) {
  const implRoots = dedupeRoots(implementationRoots || IMPLEMENTATION_ROOTS);
  const verRoots = dedupeRoots(verificationRoots || VERIFICATION_ROOTS);
  let implEntries = filterImplementationEntries(collectEntries(repoRoot, implRoots));
  const verEntries = collectEntries(repoRoot, verRoots);

  let contractEntries = [];
  let contractFingerprint;
  if (contractPayload != null) {
    const body = JSON.stringify(contractPayload);
    contractFingerprint = sha256Bytes(Buffer.from(body, 'utf8'));
    contractEntries = [
      {
        path: '(stable-contract-payload)',
        bytes: Buffer.byteLength(body),
        sha256: contractFingerprint,
      },
    ];
  } else {
    for (const p of contractPaths) {
      const rel = p.replace(/\\/g, '/');
      const abs = path.join(repoRoot, rel);
      if (!fs.existsSync(abs)) continue;
      // Task JSON: hash stable acceptance fields only (ignore holder/status/timestamps).
      if (/(^|\/)tasks\/[^/]+\.json$/i.test(rel)) {
        try {
          const task = JSON.parse(fs.readFileSync(abs, 'utf8'));
          if (task && Array.isArray(task.acceptanceCriteria)) {
            const body = JSON.stringify(stableContractPayload(task));
            const digest = sha256Bytes(Buffer.from(body, 'utf8'));
            contractEntries.push({
              path: '(stable-contract-payload)',
              bytes: Buffer.byteLength(body),
              sha256: digest,
            });
            continue;
          }
        } catch {
          /* fall through to raw file hash */
        }
      }
      walkFiles(repoRoot, rel, contractEntries);
    }
    contractEntries.sort((a, b) => a.path.localeCompare(b.path));
    // Prefer payload digest when only stable task contract(s) were hashed, matching contractPayload mode.
    if (
      contractEntries.length === 1 &&
      contractEntries[0].path === '(stable-contract-payload)'
    ) {
      contractFingerprint = contractEntries[0].sha256;
    } else {
      contractFingerprint = hashManifest(contractEntries);
    }
  }

  const git = includeGit ? gitMeta(repoRoot) : { head: null, dirty: true, tracked: new Set() };
  for (const e of implEntries) e.tracked = git.tracked.has(e.path);
  for (const e of verEntries) e.tracked = git.tracked.has(e.path);

  return {
    schemaVersion: 1,
    fingerprintPolicyVersion: FINGERPRINT_POLICY_VERSION,
    computedAt: new Date().toISOString(),
    git: {
      head: git.head,
      branch: git.branch || null,
      changes: git.changes || [],
      dirty: git.dirty,
      cleanGitCheckoutVerified: false,
      sourceIncludesUncommittedFiles: true,
    },
    implementation: {
      fingerprint: hashManifest(implEntries),
      fileCount: implEntries.length,
      entries: implEntries,
      roots: implRoots,
    },
    verification: {
      fingerprint: hashManifest(verEntries),
      fileCount: verEntries.length,
      entries: verEntries,
      roots: verRoots,
    },
    contract: {
      fingerprint: contractFingerprint,
      fileCount: contractEntries.length,
      entries: contractEntries,
    },
  };
}

/** Snapshot export roots = implementation ∪ verification (deduped). */
export function snapshotRootsFor(implementationRoots, verificationRoots) {
  return dedupeRoots([
    ...(implementationRoots || IMPLEMENTATION_ROOTS),
    ...(verificationRoots || VERIFICATION_ROOTS),
  ]);
}

export function classifyChange(oldFp, newFp) {
  const implChanged = oldFp?.implementation?.fingerprint !== newFp.implementation.fingerprint;
  const verChanged = oldFp?.verification?.fingerprint !== newFp.verification.fingerprint;
  const contractChanged = oldFp?.contract?.fingerprint !== newFp.contract.fingerprint;
  return {
    implementationChanged: !!implChanged,
    verificationChanged: !!verChanged,
    contractChanged: !!contractChanged,
    staleForCurrentCode: !!(implChanged || contractChanged),
    verificationBaselineChanged: !!verChanged,
    // Verification-only change: keep delivery identity, mark re-review on new baseline.
    needsReverification: !!(verChanged && !implChanged && !contractChanged),
  };
}
