// 指纹缓存回归：缓存必须只省时间，不能改任何指纹值，也不能绕过凭据门禁。
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const source = process.env.COLLAB_AUDIT_TOOL_SOURCE
  ? path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE)
  : path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { computeFingerprints } = await import(pathToFileURL(path.join(source, 'lib/fingerprint.mjs')));
const { HashCache, hashCacheFor, resetSharedHashCacheForTests } = await import(
  pathToFileURL(path.join(source, 'lib/hashcache.mjs'))
);

const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'fp-cache-'));
const cacheFile = path.join(repo, '.cache/hash-cache.json');

function write(rel, content) {
  const abs = path.join(repo, rel);
  fs.mkdirSync(path.dirname(abs), { recursive: true });
  fs.writeFileSync(abs, content);
}

const roots = { implementationRoots: ['src'], verificationRoots: ['spec'] };
const fp = (extra = {}) => computeFingerprints(repo, { ...roots, includeGit: false, ...extra });

write('src/alpha.txt', 'alpha-content');
write('src/beta.bin', Buffer.from([0, 1, 2, 3, 4, 5, 6, 7]));
write('spec/spec.md', 'spec-content');

let failed = 0;
let total = 0;
function check(name, fn) {
  total++;
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (e) {
    failed++;
    console.log(`FAIL ${name}: ${e.message}`);
  }
}

// --- 1. 冷缓存与热缓存的指纹必须逐字节一致 --------------------------------
const cold = fp();
const seedCache = new HashCache(cacheFile);
fp({ hashCache: seedCache }); // 走一遍就是把条目写进缓存
const seedStats = seedCache.save();
const reloaded = HashCache.load(cacheFile);

check('冷缓存与热缓存指纹一致', () => {
  assert.equal(fp({ hashCache: reloaded }).implementation.fingerprint, cold.implementation.fingerprint);
  assert.equal(fp({ hashCache: reloaded }).verification.fingerprint, cold.verification.fingerprint);
});

check('缓存确实记录了条目（否则这条回归是空转）', () => {
  assert.ok(seedStats.written, 'seed cache was not written');
  assert.ok(reloaded.entries.size >= 3, `entries=${reloaded.entries.size}`);
});

check('命中时仍带回原始字节数', () => {
  const hit = fp({ hashCache: HashCache.load(cacheFile) });
  const alpha = hit.implementation.entries.find((e) => e.path === 'src/alpha.txt');
  assert.equal(alpha.bytes, 'alpha-content'.length);
});

// --- 2. 负向控制：缓存不能掩盖任何真实变化 --------------------------------
check('同长度内容改写会被发现', () => {
  const before = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  write('src/alpha.txt', 'alpha-CONTENT'); // 同样 13 字节
  const after = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  assert.notEqual(after, before);
});

check('改写后把 mtime 改回旧值仍然会被发现（ctime 兜底）', () => {
  const abs = path.join(repo, 'src/alpha.txt');
  const st = fs.statSync(abs);
  const before = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  write('src/alpha.txt', 'alpha-contnt'); // 同样 13 字节
  fs.utimesSync(abs, st.atime, st.mtime);
  const after = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  assert.notEqual(after, before);
});

check('新增文件会被发现', () => {
  const before = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  write('src/gamma.txt', 'gamma');
  const after = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  assert.notEqual(after, before);
});

check('删除文件会被发现', () => {
  const before = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  fs.rmSync(path.join(repo, 'src/gamma.txt'));
  const after = fp({ hashCache: HashCache.load(cacheFile) }).implementation.fingerprint;
  assert.notEqual(after, before);
});

check('缓存命中不会跳过凭据门禁', () => {
  // 构造一条「stat 完全命中、但上次没扫过凭据」的记录：如果代码信任命中，
  // 就会连扫描一起跳过，把带 apiKey 的文件算进指纹。必须抛 CREDENTIAL。
  const rel = 'src/secret.yml';
  write(rel, 'apiKey: sk-live-123456\n');
  const abs = path.join(repo, rel);
  const st = fs.statSync(abs);
  const forced = new HashCache(path.join(repo, '.cache/forced.json'));
  forced.record(abs, st, 'deadbeef'.repeat(8), false);
  assert.throws(() => fp({ hashCache: forced }), /credential/i);
});

check('扫描过的条目命中时正常复用（不能反过来把正常文件也拒掉）', () => {
  const rel = 'src/secret.yml';
  write(rel, 'note: clean\n');
  const abs = path.join(repo, rel);
  const st = fs.statSync(abs);
  const sha = crypto.createHash('sha256').update(fs.readFileSync(abs)).digest('hex');
  const scanned = new HashCache(path.join(repo, '.cache/scanned.json'));
  scanned.record(abs, st, sha, true);
  const out = fp({ hashCache: scanned });
  const entry = out.implementation.entries.find((e) => e.path === rel);
  assert.equal(entry.sha256, sha);
});

// --- 3. 清单记忆（同一事务内多任务共用根集）不改变结果 --------------------
check('清单记忆前后指纹一致', () => {
  const memo = new Map();
  const cache = HashCache.load(cacheFile);
  const a = fp({ hashCache: cache, entriesCache: memo });
  const b = fp({ hashCache: cache, entriesCache: memo });
  assert.equal(a.implementation.fingerprint, b.implementation.fingerprint);
  assert.equal(a.verification.fingerprint, b.verification.fingerprint);
});

check('记忆返回的是副本，下游改 entry 不会污染下一次', () => {
  const memo = new Map();
  const first = fp({ entriesCache: memo });
  first.implementation.entries[0].path = 'tampered';
  first.implementation.entries[0].sha256 = 'tampered';
  const second = fp({ entriesCache: memo });
  assert.notEqual(second.implementation.entries[0].path, 'tampered');
  assert.equal(second.implementation.fingerprint, fp().implementation.fingerprint);
});

// --- 4. 开关 ---------------------------------------------------------------
check('COLLAB_FINGERPRINT_CACHE=0 时返回 null（可做对照验证）', () => {
  resetSharedHashCacheForTests();
  const prev = process.env.COLLAB_FINGERPRINT_CACHE;
  process.env.COLLAB_FINGERPRINT_CACHE = '0';
  try {
    const cfg = { _repoRoot: repo, localRoot: '.collab-local' };
    assert.equal(hashCacheFor(cfg), null);
  } finally {
    if (prev === undefined) delete process.env.COLLAB_FINGERPRINT_CACHE;
    else process.env.COLLAB_FINGERPRINT_CACHE = prev;
    resetSharedHashCacheForTests();
  }
});

fs.rmSync(repo, { recursive: true, force: true });
console.log(`SUMMARY ${total - failed}/${total} fingerprint cache checks passed; ${failed} failed; source=${source}`);
process.exitCode = failed ? 1 : 0;
