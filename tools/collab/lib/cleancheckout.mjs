import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { collabDirs, localDirs, snapshotRoot } from './paths.mjs';
import { EXIT, fail } from './exit.mjs';
import {
  appendJsonRecord,
  ensureDir,
  listJsonRecords,
  newId,
  nowIso,
} from './io.mjs';
import { gitMeta } from './fingerprint.mjs';

const sha256Bytes = (buf) => crypto.createHash('sha256').update(buf).digest('hex');

function git(cwd, argv, { maxBuffer = 64 * 1024 * 1024 } = {}) {
  return spawnSync('git', argv, { cwd, encoding: 'utf8', windowsHide: true, maxBuffer });
}

function sha256File(abs) {
  return sha256Bytes(fs.readFileSync(abs));
}

/**
 * Identity of the committed content, independent of the commit id: a commit
 * that touches any baseline byte changes it. The gate uses it to tell a stale
 * verification from a current one without re-cloning.
 *
 * `excludePrefixes` keeps the collaboration records out of the key. They are
 * bookkeeping *about* the baseline, not the baseline, and without the exemption
 * every new delivery/review would invalidate the verification that vouches for
 * the same source tree — including the verification record's own commit.
 */
export function gitTreeFingerprint(repoRoot, { excludePrefixes = [] } = {}) {
  const excluded = excludePrefixes
    .map((p) => String(p).replace(/\\/g, '/').replace(/\/+$/, ''))
    .filter(Boolean);
  const isExcluded = (p) => {
    const file = p.replace(/\\/g, '/');
    return excluded.some((prefix) => file === prefix || file.startsWith(`${prefix}/`));
  };
  const r = git(repoRoot, ['ls-tree', '-r', '--name-only', '-z', 'HEAD']);
  if (r.error || r.status !== 0) return null;
  const names = r.stdout.split('\0').filter(Boolean).filter((n) => !isExcluded(n)).sort();
  const blobs = git(repoRoot, ['ls-tree', '-r', '-z', 'HEAD']);
  if (blobs.error || blobs.status !== 0) return null;
  const byPath = new Map();
  for (const line of blobs.stdout.split('\0').filter(Boolean)) {
    const [meta, file] = line.split('\t');
    const [, , sha] = meta.split(' ');
    byPath.set(file, sha);
  }
  const rows = names.filter((n) => byPath.has(n)).map((n) => `${n}:${byPath.get(n)}`);
  return { fingerprint: sha256Bytes(Buffer.from(rows.join('\n'), 'utf8')), fileCount: rows.length };
}

/** The collaboration record roots of one config, as git path prefixes. */
export function recordPrefixes(cfg) {
  return [cfg.collaborationRoot, cfg.snapshotRoot || 'out/collab', cfg.localRoot || '.collab-local']
    .filter(Boolean)
    .map((p) => String(p).replace(/\\/g, '/'));
}

/** `gitTreeFingerprint` for a config, with its own record directories excluded. */
export function treeFingerprintFor(cfg) {
  return gitTreeFingerprint(cfg._repoRoot, { excludePrefixes: recordPrefixes(cfg) });
}

/**
 * Paths that are collaboration output, not source: writing a delivery, review
 * or verification record must not make the baseline look irreproducible.
 */
function dirtyOutsideRecords(cfg, changes) {
  const prefixes = [
    `${cfg.collaborationRoot.replace(/\\/g, '/')}/`,
    `${String(cfg.snapshotRoot || 'out/collab').replace(/\\/g, '/')}/`,
    `${String(cfg.localRoot || '.collab-local').replace(/\\/g, '/')}/`,
  ].map((p) => p.replace(/\/+$/, ''));
  return (changes || []).filter((row) => {
    const p = String(row.path || '').replace(/\\/g, '/');
    return !prefixes.some((prefix) => p === prefix || p.startsWith(`${prefix}/`));
  });
}

/**
 * Verify that `git clone` of the current commit reproduces the working tree
 * byte-for-byte — the only honest automated answer to "is the baseline
 * reproducible from version control?".
 *
 * Fail-closed by design:
 *  - not a git repository, or source changes outside the collaboration record
 *    directories, is refused outright: a clone of HEAD cannot contain them;
 *  - a clone whose HEAD differs from the recorded HEAD is refused;
 *  - failures list the offending paths instead of a bare boolean.
 *
 * It claims only byte reproduction. Compiling or launching from the checkout
 * needs its own recorded run; `doesNotClaim` says so inside the immutable
 * record so the claim cannot be stretched later.
 */
export function verifyCleanCheckout(cfg, args = {}) {
  const actor = args.actor || 'cursor';
  if (!['cursor', 'codex', 'user'].includes(actor)) {
    fail(EXIT.USAGE, 'clean-checkout-verify --actor must be cursor, codex or user');
  }
  const repoRoot = cfg._repoRoot;
  const meta = gitMeta(repoRoot);
  if (!meta.head) {
    fail(EXIT.CHECK_FAILED, 'not a git repository (or git is unavailable); a clean checkout cannot be verified');
  }
  const sourceChanges = dirtyOutsideRecords(cfg, meta.changes);
  if (sourceChanges.length > 0) {
    fail(
      EXIT.CONFLICT,
      'working tree has uncommitted source changes; a clone of HEAD cannot reproduce them. ' +
        'Commit or restore them first, then re-run.',
      { head: meta.head, changed: sourceChanges.slice(0, 20) },
    );
  }
  const tree = gitTreeFingerprint(repoRoot, { excludePrefixes: recordPrefixes(cfg) });
  if (!tree) fail(EXIT.CHECK_FAILED, 'cannot read the committed tree of HEAD');

  const ccvId = newId('CCV');
  const cloneRoot = path.join(os.tmpdir(), `collab-ccv-${ccvId}`);
  const keepClone = args.keep === true || args.keep === 'true';
  const clone = git(repoRoot, ['clone', '--no-hardlinks', '--quiet', repoRoot, cloneRoot]);
  if (clone.error || clone.status !== 0) {
    fail(
      EXIT.CHECK_FAILED,
      `git clone failed: ${(clone.stderr || clone.error?.message || '').trim().slice(0, 400)}`,
    );
  }

  try {
    const cloneHead = git(cloneRoot, ['rev-parse', 'HEAD']);
    if (cloneHead.error || cloneHead.status !== 0) {
      fail(EXIT.CHECK_FAILED, 'cannot read HEAD of the clean clone');
    }
    const cloned = cloneHead.stdout.trim();
    if (cloned !== meta.head) {
      fail(EXIT.CHECK_FAILED, `clean clone HEAD ${cloned.slice(0, 12)} differs from the recorded HEAD ${meta.head.slice(0, 12)}`);
    }

    const listed = git(cloneRoot, ['ls-tree', '-r', '--name-only', '-z', 'HEAD']);
    if (listed.error || listed.status !== 0) {
      fail(EXIT.CHECK_FAILED, 'cannot list tracked files in the clean clone');
    }
    const files = listed.stdout.split('\0').filter(Boolean);
    const different = [];
    const missing = [];
    for (const rel of files) {
      const inClone = path.join(cloneRoot, rel);
      const inWork = path.join(repoRoot, rel);
      if (!fs.existsSync(inClone)) { missing.push(`clone:${rel}`); continue; }
      if (!fs.existsSync(inWork)) { missing.push(`work:${rel}`); continue; }
      if (sha256File(inClone) !== sha256File(inWork)) different.push(rel);
    }
    const ok = files.length > 0 && different.length === 0 && missing.length === 0;

    // Optional evidence files: recorded by hash so a later claim is bound to the
    // exact bytes somebody actually inspected (Unity log, NUnit XML, screenshot).
    const evidenceRels = String(args.evidence || '')
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    const evidence = [];
    for (const rel of evidenceRels) {
      const abs = path.resolve(repoRoot, rel);
      if (abs !== path.resolve(repoRoot) && !abs.startsWith(path.resolve(repoRoot) + path.sep)) {
        fail(EXIT.USAGE, `--evidence must stay inside the repository: ${rel}`);
      }
      if (!fs.existsSync(abs)) fail(EXIT.USAGE, `--evidence file is missing: ${rel}`);
      const bytes = fs.readFileSync(abs);
      evidence.push({
        path: rel.replace(/\\/g, '/'),
        sha256: sha256Bytes(bytes),
        bytes: bytes.length,
      });
    }
    if (evidence.length > 0 && !String(args.statement || '').trim()) {
      fail(EXIT.USAGE, '--evidence requires --statement explaining what those files prove');
    }

    if (!keepClone) fs.rmSync(cloneRoot, { recursive: true, force: true });

    const record = {
      schemaVersion: 1,
      ccvId,
      kind: 'clean-checkout-byte-reproduction',
      actor,
      createdAt: nowIso(),
      gitHead: meta.head,
      gitBranch: meta.branch || null,
      treeFingerprint: tree.fingerprint,
      worktreeClean: true,
      ignoredForDirtyCheck: [
        cfg.collaborationRoot,
        String(cfg.snapshotRoot || 'out/collab'),
        String(cfg.localRoot || '.collab-local'),
      ],
      fileCount: files.length,
      byteIdentical: files.length - different.length - missing.length,
      different: different.slice(0, 200),
      differentCount: different.length,
      missing: missing.slice(0, 200),
      missingCount: missing.length,
      ok,
      claims:
        'Every file tracked at gitHead is reproduced byte-for-byte by "git clone" of this repository into an empty directory.',
      doesNotClaim:
        'Unity editor import, compilation or gameplay launch from the clone. Those need their own recorded run and evidence.',
      evidence,
      statement: String(args.statement || '').trim() || null,
      clonePath: keepClone ? cloneRoot : null,
    };
    ensureDir(collabDirs(cfg).verifications);
    appendJsonRecord(collabDirs(cfg).verifications, ccvId, record);

    return {
      ok,
      ccvId,
      gitHead: meta.head,
      branch: meta.branch || null,
      treeFingerprint: tree.fingerprint,
      fileCount: files.length,
      byteIdentical: record.byteIdentical,
      differentCount: different.length,
      missingCount: missing.length,
      different: different.slice(0, 10),
      missing: missing.slice(0, 10),
      evidenceCount: evidence.length,
      recordPath: `docs/collaboration/verifications/${ccvId}.json`,
      report: [
        `干净检出验证：${ccvId}`,
        `HEAD：${meta.head}（${meta.branch || 'detached'}）`,
        `跟踪文件：${files.length} · 逐字节相同：${record.byteIdentical}`,
        `差异：${different.length} · 缺失：${missing.length}`,
        evidence.length ? `附加证据：${evidence.length} 个文件（已记录 SHA256）` : '附加证据：无',
        '',
        ok
          ? '结论：git clone 能逐字节重建当前基线（不含 Unity 导入/启动）。'
          : '结论：无法逐字节重建当前基线，先修复差异再声称可复现。',
      ].join('\n'),
      exitCode: ok ? EXIT.OK : EXIT.CHECK_FAILED,
    };
  } finally {
    if (fs.existsSync(cloneRoot) && !keepClone) {
      try { fs.rmSync(cloneRoot, { recursive: true, force: true }); } catch { /* best effort */ }
    }
  }
}

export function listCleanCheckouts(cfg) {
  return listJsonRecords(collabDirs(cfg).verifications).filter(
    (r) => r && r.kind === 'clean-checkout-byte-reproduction',
  );
}

/** Newest passing verification for one committed tree, or null. */
export function latestCleanCheckoutFor(cfg, treeFingerprint) {
  if (!treeFingerprint) return null;
  const matches = listCleanCheckouts(cfg).filter(
    (r) =>
      r.treeFingerprint === treeFingerprint &&
      r.ok === true &&
      r.worktreeClean === true &&
      r.differentCount === 0 &&
      r.missingCount === 0,
  );
  return matches.at(-1) || null;
}

/**
 * Gate/CLI view: is the *current* committed tree covered by a passing
 * verification? Reading it from the repository rather than a caller-supplied id
 * keeps a stale record from vouching for later content.
 */
export function currentCheckoutStatus(cfg) {
  const meta = gitMeta(cfg._repoRoot);
  const tree = meta.head ? treeFingerprintFor(cfg) : null;
  const record = tree ? latestCleanCheckoutFor(cfg, tree.fingerprint) : null;
  const sourceChanges = dirtyOutsideRecords(cfg, meta.changes);
  return {
    head: meta.head || null,
    dirty: sourceChanges.length > 0,
    sourceChangeCount: sourceChanges.length,
    verified: !!record && sourceChanges.length === 0,
    record: record
      ? {
          ccvId: record.ccvId,
          createdAt: record.createdAt,
          fileCount: record.fileCount,
          evidenceCount: (record.evidence || []).length,
        }
      : null,
  };
}

/** CLI entry point: `clean-checkout-verify`. */
export function cmdCleanCheckoutVerify(cfg, args = {}) {
  return verifyCleanCheckout(cfg, args);
}
