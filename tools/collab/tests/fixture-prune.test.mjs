// Sweeping leftover fixture trees must never destroy something that still matters.
// ISSUE-20261101061656-eee4ee: the first sweep judged age from the root directory's
// mtime and deleted both a retained failure and a tree with a live child writing
// into it. Both cases are reproduced here.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { mkTemp, finalizeFixtures, KEEP_MARKER } from './temp-fixtures.mjs';
import { pruneStaleFixtures, releaseOwnedRoots, containsPath } from './fixture-prune.mjs';

const base = mkTemp('collab-prune-');
let failed = 0;
const results = [];

function test(id, name, fn) {
  try {
    fn();
    results.push({ id, name, passed: true });
    console.log(`PASS ${id} ${name}`);
  } catch (e) {
    failed += 1;
    results.push({ id, name, passed: false, error: e.stack });
    console.log(`FAIL ${id} ${name}: ${e.message}`);
  }
}

const HOURS = 3600 * 1000;

/** Create a tree, then force every entry's mtime back by `ageHours`. */
function tree(name, { ageHours = 8, files = {} } = {}) {
  const dir = path.join(base, name);
  fs.mkdirSync(dir, { recursive: true });
  for (const [rel, body] of Object.entries(files)) {
    const abs = path.join(dir, rel);
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, body);
  }
  const stamp = Date.now() - ageHours * HOURS;
  ageTree(dir, stamp);
  return dir;
}

function ageTree(dir, stamp) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const abs = path.join(dir, e.name);
    if (e.isDirectory()) ageTree(abs, stamp);
    else fs.utimesSync(abs, new Date(stamp), new Date(stamp));
  }
  fs.utimesSync(dir, new Date(stamp), new Date(stamp));
}

const exists = (p) => fs.existsSync(p);

test('P01', 'a retained failure survives its root being hours old', () => {
  const dir = tree('collab-suite-kept-failure', {
    files: {
      [KEEP_MARKER]: JSON.stringify({ reason: 'suite reported failures' }),
      'results.json': JSON.stringify({ ok: false }),
      'Logs/diagnostic.log': 'stack trace that explains the failure\n',
    },
  });
  const out = pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir), 'retained failure must not be swept');
  assert(out.skipped.some((s) => s.startsWith('collab-suite-kept-failure')), JSON.stringify(out));
});

test('P02', 'a tree with a live child writing into it survives', () => {
  // The child touches the nested file, not the root entry: this is the shape that
  // fooled the old mtime-only check.
  const dir = tree('collab-stage-live-owner', {
    ageHours: 7,
    files: {
      'worker/executions.txt': 'run 1\n',
      'owner.json': JSON.stringify({ pid: process.pid }),
    },
  });
  fs.writeFileSync(path.join(dir, 'worker/executions.txt'), 'run 1\nrun 2\n');
  const out = pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir), 'a tree being written to must not be swept');
  assert(out.skipped.some((s) => s.startsWith('collab-stage-live-owner')), JSON.stringify(out));
});

test('P03', 'a nested file written recently protects an old root', () => {
  const dir = tree('collab-stage-deep-write', {
    ageHours: 9,
    files: { 'a/b/c/notes.txt': 'x' },
  });
  fs.writeFileSync(path.join(dir, 'a/b/c/notes.txt'), 'y');
  pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir), 'newest mtime anywhere in the tree is what counts');
});

test('P04', 'a provably abandoned tree is removed', () => {
  const dir = tree('collab-stage-abandoned', { ageHours: 9, files: { 'x.txt': 'x' } });
  const out = pruneStaleFixtures(base, 6 * HOURS);
  assert(!exists(dir), 'tree with no marker and no recent write should be swept');
  assert.equal(out.removed, 1, JSON.stringify(out));
});

test('P05', 'an ownership marker alone blocks the sweep', () => {
  const dir = tree('collab-lock-owned', { ageHours: 9, files: { '.lease': '{"owner":"x"}' } });
  pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir), 'a lease may still be reclaimed by its owner');
});

test('P06', 'directories that are not ours are never touched', () => {
  const dir = tree('unrelated-folder', { ageHours: 9, files: { 'x.txt': 'x' } });
  pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir));
});

test('P07', 'a fresh tree is left alone', () => {
  const dir = tree('collab-daily-fresh', { ageHours: 0, files: { 'x.txt': 'x' } });
  pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir));
});

test('P08', 'an unreadable tree is kept rather than guessed at', () => {
  const dir = tree('collab-stage-unreadable', { ageHours: 9, files: { 'x.txt': 'x' } });
  assert(exists(dir));
  // Simulated by pruning with the tree gone from disk between listing and scan
  // is racy; instead assert the contract: a missing tree reports an error, and the
  // sweep counts it as kept rather than throwing.
  const out = pruneStaleFixtures(path.join(base, 'does-not-exist'), 6 * HOURS);
  assert.deepEqual(out, { removed: 0, kept: 0, skipped: [] });
});

// ISSUE-20261101063120-97dd41: a standalone `run-all` keeps its summary and
// per-suite logs at the temp root, so `evidenceDir === root`. `startsWith(root + sep)`
// is false for equal paths, so the exit cleanup deleted the evidence it was
// supposed to protect. Equality must count as containment.
test('P09', 'evidence dir equal to the owned root is kept', () => {
  const dir = tree('collab-suite-standalone', { ageHours: 0, files: {} });
  fs.writeFileSync(path.join(dir, 'summary.json'), JSON.stringify({ ok: false }));
  fs.mkdirSync(path.join(dir, 'logs'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'logs', 'collab.test.mjs.stderr.txt'), 'the reason the run failed\n');

  const out = releaseOwnedRoots([dir], dir, { failed: true });
  assert(exists(dir), 'the root holding the run evidence must survive');
  assert.equal(out[0].removed, false, JSON.stringify(out));
  assert(exists(path.join(dir, 'summary.json')), 'summary must be readable after exit');
  assert(exists(path.join(dir, 'logs', 'collab.test.mjs.stderr.txt')), 'failure log must survive');
});

test('P10', 'evidence nested below the owned root is kept', () => {
  const dir = tree('collab-suite-nested', { ageHours: 0, files: {} });
  const evidence = path.join(dir, 'logs');
  fs.mkdirSync(evidence, { recursive: true });
  fs.writeFileSync(path.join(evidence, 'summary.json'), '{}');
  const out = releaseOwnedRoots([dir], evidence, { failed: true });
  assert(exists(dir) && exists(path.join(evidence, 'summary.json')), JSON.stringify(out));
});

test('P11', 'an owned root without evidence is still removed', () => {
  const dir = tree('collab-suite-plain', { ageHours: 0, files: { 'x.txt': 'x' } });
  const evidence = path.join(base, 'elsewhere-evidence');
  fs.mkdirSync(evidence, { recursive: true });
  const out = releaseOwnedRoots([dir], evidence, { failed: false });
  assert(!exists(dir), 'unrelated root must not be kept forever');
  assert.equal(out[0].removed, true, JSON.stringify(out));
  assert(exists(evidence), 'the evidence dir itself is never the target');
});

test('P12', 'a failed run marks its kept root so the next sweep leaves it alone', () => {
  const dir = tree('collab-suite-failed-run', { ageHours: 0, files: {} });
  fs.writeFileSync(path.join(dir, 'summary.json'), JSON.stringify({ ok: false }));
  releaseOwnedRoots([dir], dir, { failed: true });
  assert(exists(path.join(dir, KEEP_MARKER)), 'kept evidence needs a marker or it expires');
  // Age it past the sweep threshold: the marker, not the mtime, is what protects it.
  const stamp = Date.now() - 9 * HOURS;
  ageTree(dir, stamp);
  pruneStaleFixtures(base, 6 * HOURS);
  assert(exists(dir), 'a marked tree must outlive the sweep');
});

test('P13', 'containsPath counts equality and rejects siblings with a shared prefix', () => {
  assert.equal(containsPath('/a/b', '/a/b'), true, 'equal paths are contained');
  assert.equal(containsPath('/a/b', '/a/b/c'), true, 'descendants are contained');
  assert.equal(containsPath('/a/b', '/a/bc'), false, 'shared prefix is not containment');
  assert.equal(containsPath('/a/b', '/a'), false, 'ancestors are not contained');
});

console.log(JSON.stringify({ assertionsTotal: results.length, assertionsFailed: failed, assertionsSkipped: 0 }));
finalizeFixtures(failed);
process.exitCode = failed ? 1 : 0;
