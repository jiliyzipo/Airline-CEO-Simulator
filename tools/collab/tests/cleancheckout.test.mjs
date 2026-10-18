// Clean-checkout verification (T02): git clone must reproduce the baseline bytes.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const source = path.resolve(
  process.env.COLLAB_AUDIT_TOOL_SOURCE || path.join(path.dirname(fileURLToPath(import.meta.url)), '..'),
);
const ccv = await import(pathToFileURL(path.join(source, 'lib/cleancheckout.mjs')));
const book = await import(pathToFileURL(path.join(source, 'lib/taskbook.mjs')));

let failed = 0;
const results = [];

async function test(id, name, fn) {
  try {
    await fn();
    results.push({ id, name, passed: true });
    console.log(`PASS ${id} ${name}`);
  } catch (e) {
    failed += 1;
    results.push({ id, name, passed: false, error: e.stack });
    console.log(`FAIL ${id} ${name}: ${e.message}`);
  }
}

const EXIT = { CONFLICT: 3, CHECK_FAILED: 5, USAGE: 2 };

function git(cwd, argv) {
  return spawnSync('git', argv, { cwd, encoding: 'utf8', windowsHide: true });
}

function commit(root, message) {
  const add = git(root, ['add', '-A']);
  assert.equal(add.status, 0, add.stderr);
  const c = git(root, ['commit', '-q', '-m', message]);
  assert.equal(c.status, 0, c.stderr);
  return git(root, ['rev-parse', 'HEAD']).stdout.trim();
}

function repoFixture(extra = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-ccv-'));
  fs.mkdirSync(path.join(root, 'docs/collaboration/tasks'), { recursive: true });
  fs.mkdirSync(path.join(root, 'src'), { recursive: true });
  // Same rule as the real repository: store bytes as-is, so a clone on a
  // core.autocrlf=true machine reproduces the working tree exactly.
  fs.writeFileSync(path.join(root, '.gitattributes'), '* -text\n');
  fs.writeFileSync(path.join(root, 'a.txt'), 'alpha\n');
  fs.writeFileSync(path.join(root, 'src/b.cs'), 'class B { }\n');
  const init = git(root, ['init', '-q', '-b', 'main']);
  assert.equal(init.status, 0, init.stderr);
  git(root, ['config', 'user.name', 'fixture']);
  git(root, ['config', 'user.email', 'fixture@example.invalid']);
  commit(root, 'init');
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
    ...extra,
  };
  return { root, cfg };
}

await test('CCV01', 'clone reproduces every tracked file byte-for-byte and records it', () => {
  const { cfg } = repoFixture();
  const result = ccv.verifyCleanCheckout(cfg, { actor: 'cursor' });
  assert.equal(result.ok, true, JSON.stringify(result));
  assert.equal(result.differentCount, 0);
  assert.equal(result.missingCount, 0);
  assert.equal(result.fileCount, 3); // .gitattributes, a.txt, src/b.cs
  assert.equal(result.byteIdentical, 3);

  const records = ccv.listCleanCheckouts(cfg);
  assert.equal(records.length, 1);
  assert.equal(records[0].ccvId, result.ccvId);
  assert.equal(records[0].actor, 'cursor');
  assert.equal(records[0].worktreeClean, true);
  assert.match(records[0].doesNotClaim, /Unity/);

  const status = ccv.currentCheckoutStatus(cfg);
  assert.equal(status.verified, true);
  assert.equal(status.record.ccvId, result.ccvId);
});

await test('CCV02', 'a second run appends instead of overwriting the first record', () => {
  const { cfg } = repoFixture();
  const first = ccv.verifyCleanCheckout(cfg, { actor: 'cursor' });
  const second = ccv.verifyCleanCheckout(cfg, { actor: 'cursor' });
  assert.notEqual(first.ccvId, second.ccvId);
  assert.equal(ccv.listCleanCheckouts(cfg).length, 2);
});

await test('CCV03', 'uncommitted source changes are refused, not silently ignored', () => {
  const { root, cfg } = repoFixture();
  fs.appendFileSync(path.join(root, 'src/b.cs'), 'class C { }\n');
  assert.throws(
    () => ccv.verifyCleanCheckout(cfg, { actor: 'cursor' }),
    (e) => e.exitCode === EXIT.CONFLICT && /uncommitted source changes/.test(e.message),
  );
  assert.equal(ccv.listCleanCheckouts(cfg).length, 0);
});

await test('CCV04', 'untracked source is refused too: a clone cannot contain it', () => {
  const { root, cfg } = repoFixture();
  fs.writeFileSync(path.join(root, 'src/new.cs'), 'class N { }\n');
  assert.throws(
    () => ccv.verifyCleanCheckout(cfg, { actor: 'cursor' }),
    (e) => e.exitCode === EXIT.CONFLICT,
  );
});

await test('CCV05', 'collaboration output written by the tool does not block the check', () => {
  const { root, cfg } = repoFixture();
  fs.writeFileSync(path.join(root, 'docs/collaboration/deliveries.json'), '{}\n');
  fs.mkdirSync(path.join(root, 'out/collab'), { recursive: true });
  fs.writeFileSync(path.join(root, 'out/collab/scratch.json'), '{}\n');
  fs.mkdirSync(path.join(root, '.collab-local'), { recursive: true });
  fs.writeFileSync(path.join(root, '.collab-local/ack.json'), '{}\n');
  const result = ccv.verifyCleanCheckout(cfg, { actor: 'codex' });
  assert.equal(result.ok, true);
});

await test('CCV06', 'a directory that is not a git repository fails closed', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-nogit-'));
  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
  };
  assert.throws(
    () => ccv.verifyCleanCheckout(cfg, { actor: 'cursor' }),
    (e) => e.exitCode === EXIT.CHECK_FAILED && /not a git repository/.test(e.message),
  );
});

await test('CCV07', 'a later commit invalidates the record instead of vouching for new content', () => {
  const { root, cfg } = repoFixture();
  const result = ccv.verifyCleanCheckout(cfg, { actor: 'cursor' });
  assert.equal(ccv.currentCheckoutStatus(cfg).verified, true);

  fs.appendFileSync(path.join(root, 'src/b.cs'), 'class C { }\n');
  commit(root, 'change source');
  const tree = ccv.gitTreeFingerprint(root);
  assert.equal(ccv.latestCleanCheckoutFor(cfg, tree.fingerprint), null);
  assert.equal(ccv.currentCheckoutStatus(cfg).verified, false);
  // The old record still exists: staleness is reported, history is not rewritten.
  assert.equal(ccv.listCleanCheckouts(cfg).filter((r) => r.ccvId === result.ccvId).length, 1);
});

await test('CCV08', 'evidence files are bound by hash and require a statement', () => {
  const { root, cfg } = repoFixture();
  // Evidence stays under the collaboration output root: it must not itself make
  // the baseline look dirty, but it still has to exist and be hashed.
  fs.mkdirSync(path.join(root, 'out/collab'), { recursive: true });
  const logPath = path.join(root, 'out/collab/run.log');
  fs.writeFileSync(logPath, 'unity log\n');

  assert.throws(
    () => ccv.verifyCleanCheckout(cfg, { actor: 'cursor', evidence: 'missing.log', statement: 'x' }),
    (e) => e.exitCode === EXIT.USAGE,
  );
  assert.throws(
    () => ccv.verifyCleanCheckout(cfg, { actor: 'cursor', evidence: 'out/collab/run.log' }),
    (e) => e.exitCode === EXIT.USAGE && /statement/.test(e.message),
  );

  const ok = ccv.verifyCleanCheckout(cfg, {
    actor: 'cursor',
    evidence: 'out/collab/run.log',
    statement: 'Unity imported the clone and entered play mode',
  });
  assert.equal(ok.evidenceCount, 1);
  const record = ccv.listCleanCheckouts(cfg).at(-1);
  const expected = crypto.createHash('sha256').update(fs.readFileSync(logPath)).digest('hex');
  assert.equal(record.evidence[0].sha256, expected);
  assert.equal(record.statement, 'Unity imported the clone and entered play mode');
});

await test('CCV09', 'the gate accepts a matching record for a task that cannot be delivered', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'collab-ccv-gate-'));
  fs.mkdirSync(path.join(root, 'docs/collaboration/tasks'), { recursive: true });
  fs.writeFileSync(path.join(root, '.gitattributes'), '* -text\n');
  fs.writeFileSync(path.join(root, 'a.txt'), 'alpha\n');
  fs.writeFileSync(
    path.join(root, 'docs/book.md'),
    [
      '## 3. 主任务总清单',
      '',
      '- [ ] **T9：总任务。** 状态：未完成。',
      '',
      '## 4. T9 · 分项检查',
      '',
      '- [x] T9.1 第一项。',
      '',
    ].join('\n'),
    'utf8',
  );
  git(root, ['init', '-q', '-b', 'main']);
  git(root, ['config', 'user.name', 'fixture']);
  git(root, ['config', 'user.email', 'fixture@example.invalid']);
  commit(root, 'init');

  const cfg = {
    _repoRoot: root,
    projectId: 'fixture',
    collaborationRoot: 'docs/collaboration',
    localRoot: '.collab-local',
    snapshotRoot: 'out/collab',
    taskBook: {
      path: 'docs/book.md',
      tasks: {
        T9: { sectionHeading: '## 4. T9 · 分项检查', itemPrefix: 'T9', requiresCleanCheckout: true },
      },
      completionTasks: ['T9'],
    },
  };
  fs.writeFileSync(
    path.join(root, 'docs/collaboration/tasks/T9.json'),
    JSON.stringify({
      schemaVersion: 1,
      projectId: 'fixture',
      taskId: 'T9',
      title: 'T9',
      revision: 1,
      devStatus: 'todo',
      reviewStatus: 'not_run',
      freshness: 'unknown',
      holder: null,
      scope: ['fixture'],
      acceptanceCriteria: [{ id: 'AC1', text: 'fixture' }],
      dependencies: [],
      readOnlyReference: true,
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
    }, null, 2),
  );
  book.recordBaseline(cfg, 'T9');

  const before = book.computeTaskGate(cfg, 'T9');
  assert.equal(before.ok, false);
  assert.ok(before.blockers.some((b) => b.id === 'clean-git-checkout'));

  ccv.verifyCleanCheckout(cfg, { actor: 'cursor' });
  const after = book.computeTaskGate(cfg, 'T9');
  assert.ok(
    after.checks.some((c) => c.id === 'clean-git-checkout' && c.ok),
    JSON.stringify(after),
  );
  assert.equal(after.ok, true, JSON.stringify(after.blockers));
});

console.log(JSON.stringify({ assertionsTotal: results.length, assertionsFailed: failed, assertionsSkipped: 0 }));
process.exitCode = failed > 0 ? 1 : 0;
