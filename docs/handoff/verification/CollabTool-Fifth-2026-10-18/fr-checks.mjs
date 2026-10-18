// Developer-side checks for FR-01 / FR-02 / FR-03, binding the *new* delivery
// (the fourth-round DEL is polluted by history and can never be a clean input).
// Every case runs in an isolated fixture; the real delivery directory is only read.
//
//   node docs/handoff/verification/CollabTool-Fifth-2026-10-18/fr-checks.mjs
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const repo = process.cwd();
const subject = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || 'tools/collab');
const label = process.env.COLLAB_AUDIT_RUN_LABEL || 'fifth-fr';
const deliveryId = process.env.COLLAB_DELIVERY_ID || 'DEL-20261018021952-ba9a3f';
const work = path.join(
  repo,
  '.collab-local/reaccept-20261017',
  label,
  new Date().toISOString().replace(/[:.]/g, '-'),
);
fs.mkdirSync(work, { recursive: true });

const cmd = await import(pathToFileURL(path.join(subject, 'lib/commands.mjs')));
const sha = (file) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const put = (root, file, body) => {
  const dest = path.join(root, file);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, typeof body === 'string' ? body : JSON.stringify(body, null, 2));
};
const read = (root, file) => JSON.parse(fs.readFileSync(path.join(root, file), 'utf8'));

const delivery = read(repo, `docs/collaboration/deliveries/${deliveryId}.json`);
const results = [];
const outcome = (fn) => {
  try {
    return { accepted: true, value: fn() };
  } catch (e) {
    return { accepted: false, exitCode: e.exitCode ?? null, error: e.message };
  }
};
let serial = 0;

function fixture({ profileCommand = null, roots = ['src'] } = {}) {
  const root = path.join(work, 'fixtures', String(++serial).padStart(2, '0'));
  fs.mkdirSync(root, { recursive: true });
  fs.cpSync(subject, path.join(root, 'tools/collab'), { recursive: true });
  put(root, 'docs/collaboration/PROTOCOL.md', '# isolated fixture\n');
  put(root, 'src/model.cs', 'A');
  const cfg = read(root, 'tools/collab/config.example.json');
  cfg._repoRoot = root;
  cfg._configPath = path.join(root, 'tools/collab/config.json');
  cfg.demoSnapshotRoots = roots;
  cfg.demoSnapshotFiles = ['docs/collaboration/PROTOCOL.md'];
  cfg.profiles = {
    smoke: {
      kind: 'node',
      resultKind: 'json',
      command: profileCommand || [process.execPath, '-e', 'console.log("{}")'],
    },
  };
  put(root, 'tools/collab/config.json', cfg);
  cmd.cmdInit(cfg, {});
  return { root, cfg };
}

const deliver = (f) =>
  cmd.cmdDeliver(f.cfg, { actor: 'cursor', task: 'C-DEMO', summary: 'FR fixture', profiles: 'smoke' });
const start = (f, d) =>
  cmd.cmdReviewStart(f.cfg, {
    actor: 'codex',
    delivery: typeof d === 'string' ? d : d.deliveryId,
    profile: 'smoke',
  });
const run = (f, r) => cmd.cmdReviewRun(f.cfg, { actor: 'codex', review: r.reviewId });
const reviewOf = (f, r) => read(f.root, `docs/collaboration/reviews/${r.reviewId}.json`);
function finish(f, r, patch = {}) {
  const d = read(f.root, `docs/collaboration/deliveries/${reviewOf(f, r).deliveryId}.json`);
  const task = read(f.root, 'docs/collaboration/tasks/C-DEMO.json');
  const evidence = {
    conclusion: 'passed',
    subjectImplementationFingerprint: d.fingerprints.implementation.fingerprint,
    assertionsTotal: 3,
    assertionsFailed: 0,
    assertionsSkipped: 0,
    coverage: task.acceptanceCriteria.map((a) => ({ id: a.id, result: 'passed', evidence: 'fixture' })),
    ...patch,
  };
  const file = path.join(f.root, `evidence-${crypto.randomBytes(4).toString('hex')}.json`);
  fs.writeFileSync(file, JSON.stringify(evidence));
  return cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: file });
}

function copyDeliveryInto(root) {
  fs.cpSync(path.join(repo, delivery.snapshotPath), path.join(root, delivery.snapshotPath), {
    recursive: true,
  });
  put(root, `docs/collaboration/deliveries/${deliveryId}.json`, delivery);
  return read(root, path.join(delivery.snapshotPath, 'tools/collab/config.example.json')).profiles;
}

function test(id, requirement, fn) {
  let entry;
  try {
    entry = { id, requirement, ...fn() };
  } catch (e) {
    entry = { id, requirement, passed: false, testError: e.stack };
  }
  results.push(entry);
  console.log(`${entry.passed ? 'PASS' : 'FAIL'} ${id}: ${requirement}`);
}

// ---------------------------------------------------------------- FR-01
test('FR1A', 'The new delivery directory is clean at creation (no unlisted files)', () => {
  const snapAbs = path.join(repo, delivery.snapshotPath);
  const listed = new Set(delivery.snapshotManifest.entries.map((e) => e.path));
  const extra = [];
  const walk = (rel) => {
    for (const child of fs.readdirSync(path.join(snapAbs, rel))) {
      const childRel = rel ? `${rel}/${child}` : child;
      if (fs.statSync(path.join(snapAbs, childRel)).isDirectory()) walk(childRel);
      else if (!listed.has(childRel) && childRel !== 'collab-snapshot-manifest.json') extra.push(childRel);
    }
  };
  walk('');
  const hashesOk = delivery.snapshotManifest.entries.every(
    (e) => sha(path.join(snapAbs, e.path)) === (e.snapshotSha256 || e.sha256),
  );
  return {
    passed: extra.length === 0 && hashesOk && delivery.snapshotManifest.unlistedAtCreation === 0,
    extra,
    hashesOk,
    unlistedAtCreation: delivery.snapshotManifest.unlistedAtCreation,
    entryCount: delivery.snapshotManifest.entries.length,
  };
});

test('FR1B', 'Running the test entry inside a delivery copy writes nothing into it', () => {
  const target = path.join(work, 'direct-test-copy');
  fs.cpSync(path.join(repo, delivery.snapshotPath), target, { recursive: true });
  const before = [];
  const collect = (dir, rel = '') => {
    for (const child of fs.readdirSync(path.join(dir, rel))) {
      const childRel = rel ? `${rel}/${child}` : child;
      const abs = path.join(dir, childRel);
      if (fs.statSync(abs).isDirectory()) collect(dir, childRel);
      else before.push(childRel);
    }
  };
  collect(target);
  const r = spawnSync(process.execPath, [path.join(target, 'tools/collab/tests/collab.test.mjs')], {
    encoding: 'utf8',
    cwd: repo,
  });
  const after = [];
  const collect2 = (dir, rel = '') => {
    for (const child of fs.readdirSync(path.join(dir, rel))) {
      const childRel = rel ? `${rel}/${child}` : child;
      const abs = path.join(dir, childRel);
      if (fs.statSync(abs).isDirectory()) collect2(dir, childRel);
      else after.push(childRel);
    }
  };
  collect2(target);
  return {
    passed: r.status === 0 && before.length === after.length && !after.some((f) => f.endsWith('CURRENT.md')),
    testExit: r.status,
    filesBefore: before.length,
    filesAfter: after.length,
  };
});

test('FR1C', 'The new delivery runs its configured default smoke end to end', () => {
  const f = fixture();
  const profiles = copyDeliveryInto(f.root);
  f.cfg.profiles = profiles;
  const d = start(f, deliveryId);
  const rr = outcome(() => run(f, d));
  if (!rr.accepted) return { passed: false, observation: rr };
  const stdout = fs.readFileSync(path.join(f.root, rr.value.stdoutPath), 'utf8');
  const parsed = JSON.parse(stdout);
  return {
    passed: rr.value.exitCode === 0 && parsed.assertionsFailed === 0 && parsed.assertionsTotal === 2,
    exitCode: rr.value.exitCode,
    counters: { total: parsed.assertionsTotal, failed: parsed.assertionsFailed },
    checks: parsed.checks,
  };
});

test('FR1D', 'Removing the protocol input still fails the smoke (dependency is real)', () => {
  const f = fixture();
  const profiles = copyDeliveryInto(f.root);
  f.cfg.profiles = profiles;
  const snapRel = delivery.snapshotPath;
  const protocolRel = 'docs/collaboration/PROTOCOL.md';
  fs.rmSync(path.join(f.root, snapRel, protocolRel));
  // Keep the fixture's manifest self-consistent so the integrity gate passes and
  // the smoke runs: this isolates "missing dependency" from "tampered snapshot".
  const manifestPath = path.join(f.root, snapRel, 'collab-snapshot-manifest.json');
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  manifest.entries = manifest.entries.filter((e) => e.path !== protocolRel);
  manifest.copiedFileCount = manifest.entries.length;
  fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
  const dRec = JSON.parse(JSON.stringify(delivery));
  dRec.snapshotManifest.entries = dRec.snapshotManifest.entries.filter((e) => e.path !== protocolRel);
  put(f.root, `docs/collaboration/deliveries/${deliveryId}.json`, dRec);

  const d = start(f, deliveryId);
  const rr = run(f, d);
  const stdout = fs.readFileSync(path.join(f.root, rr.stdoutPath), 'utf8');
  const parsed = JSON.parse(stdout);
  const protocolCheck = parsed.checks.find((c) => c.name === 'protocol-exists');
  return {
    passed: rr.exitCode === 1 && protocolCheck && protocolCheck.ok === false,
    exitCode: rr.exitCode,
    checks: parsed.checks,
  };
});

test('FR1E', 'An unlisted source file added to the delivery copy is still rejected', () => {
  const f = fixture();
  const profiles = copyDeliveryInto(f.root);
  f.cfg.profiles = profiles;
  put(f.root, `${delivery.snapshotPath}/src/evil.cs`, 'class Evil {}');
  const d = start(f, deliveryId);
  const rr = outcome(() => run(f, d));
  return { passed: !rr.accepted && rr.exitCode === 5, exitCode: rr.exitCode, error: rr.error };
});

// ---------------------------------------------------------------- FR-02
test('FR2A', 'Tampered stdout cannot be finished as passed', () => {
  const f = fixture({
    profileCommand: [
      process.execPath,
      '-e',
      'console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:1,assertionsSkipped:0}))',
    ],
  });
  const d = deliver(f);
  const r = start(f, d);
  const rr = run(f, r);
  const file = path.join(f.root, rr.stdoutPath);
  fs.writeFileSync(file, JSON.stringify({ assertionsTotal: 3, assertionsFailed: 0, assertionsSkipped: 0 }));
  const o = outcome(() => finish(f, r));
  return { passed: !o.accepted && [5, 6].includes(o.exitCode), observation: o };
});

test('FR2B', 'Sequential runs keep separate evidence files and hashes', () => {
  const f = fixture({
    profileCommand: [
      process.execPath,
      '-e',
      'console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))',
    ],
  });
  const d = deliver(f);
  const r = start(f, d);
  const first = run(f, r);
  const firstAbs = path.join(f.root, first.stdoutPath);
  const firstBytes = fs.readFileSync(firstAbs);
  const firstSha = sha(firstAbs);
  const second = run(f, r);
  const secondAbs = path.join(f.root, second.stdoutPath);
  return {
    passed:
      first.runToken !== second.runToken &&
      first.stdoutPath !== second.stdoutPath &&
      sha(firstAbs) === firstSha &&
      fs.readFileSync(firstAbs).equals(firstBytes) &&
      first.resultFile === null &&
      second.resultFile === null &&
      fs.existsSync(secondAbs),
    firstToken: first.runToken,
    secondToken: second.runToken,
    firstPath: first.stdoutPath,
    secondPath: second.stdoutPath,
    firstUnchanged: sha(firstAbs) === firstSha,
  };
});

test('FR2C', 'An unbound (legacy-shaped) run cannot be finished as passed', () => {
  const f = fixture({
    profileCommand: [
      process.execPath,
      '-e',
      'console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))',
    ],
  });
  const d = deliver(f);
  const r = start(f, d);
  run(f, r);
  const reviewPath = path.join(f.root, `docs/collaboration/reviews/${r.reviewId}.json`);
  const review = JSON.parse(fs.readFileSync(reviewPath, 'utf8'));
  delete review.runs[0].stdout;
  delete review.runs[0].stderr;
  fs.writeFileSync(reviewPath, JSON.stringify(review, null, 2));
  const o = outcome(() => finish(f, r));
  return { passed: !o.accepted && [5, 6].includes(o.exitCode), observation: o };
});

// ---------------------------------------------------------------- FR-03
test('FR3A', 'A truncated NUnit result file is rejected', () => {
  const xml = '<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">';
  const f = fixture({
    profileCommand: [
      process.execPath,
      '-e',
      `require("fs").writeFileSync("result.xml",${JSON.stringify(xml)});console.log("done")`,
    ],
  });
  f.cfg.profiles.smoke.resultKind = 'nunit';
  f.cfg.profiles.smoke.resultFile = 'result.xml';
  put(f.root, 'tools/collab/config.json', f.cfg);
  const d = deliver(f);
  const r = start(f, d);
  run(f, r);
  const o = outcome(() => finish(f, r, { assertionsTotal: 1, assertionsFailed: 0, assertionsSkipped: 0 }));
  return { passed: !o.accepted && [5, 6].includes(o.exitCode), observation: o, parsed: cmd.parseNunitCounters(xml) };
});

test('FR3B', 'A complete passing NUnit result is accepted', () => {
  const xml = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<test-run id="2" total="2" passed="2" failed="0" skipped="0" result="Passed">',
    '<test-suite name="S"><test-case id="1" result="Passed"/><test-case id="2" result="Passed"/></test-suite>',
    '</test-run>',
  ].join('\n');
  const f = fixture({
    profileCommand: [
      process.execPath,
      '-e',
      `require("fs").writeFileSync("result.xml",${JSON.stringify(xml)});console.log("done")`,
    ],
  });
  f.cfg.profiles.smoke.resultKind = 'nunit';
  f.cfg.profiles.smoke.resultFile = 'result.xml';
  put(f.root, 'tools/collab/config.json', f.cfg);
  const d = deliver(f);
  const r = start(f, d);
  run(f, r);
  const o = outcome(() => finish(f, r, { assertionsTotal: 2, assertionsFailed: 0, assertionsSkipped: 0 }));
  return {
    passed: o.accepted && o.value.status === 'passed',
    observation: o,
    parsed: cmd.parseNunitCounters(xml),
  };
});

test('FR3C', 'Real Unity NUnit result files parse with their recorded counts', () => {
  const samples = [
    ['docs/handoff/verification/2026-10-17/EditMode.xml', { total: 25, failed: 0, skipped: 0, passed: 25 }],
    ['docs/handoff/verification/2026-10-17/PlayMode.xml', { total: 99, failed: 0, skipped: 0, passed: 99 }],
  ];
  const seen = [];
  let ok = true;
  for (const [rel, expected] of samples) {
    const abs = path.join(repo, rel);
    if (!fs.existsSync(abs)) {
      seen.push({ rel, skipped: 'sample not present' });
      continue;
    }
    const counters = cmd.parseNunitCounters(fs.readFileSync(abs, 'utf8'));
    seen.push({ rel, counters, expected });
    if (JSON.stringify(counters) !== JSON.stringify(expected)) ok = false;
  }
  return { passed: ok && seen.every((s) => !s.skipped), samples: seen };
});

const summary = {
  date: new Date().toISOString(),
  subjectToolsPath: subject,
  deliveryId,
  total: results.length,
  passed: results.filter((r) => r.passed).length,
  failed: results.filter((r) => !r.passed).length,
  results,
};
put(work, 'results.json', summary);
console.log(`SUMMARY ${summary.passed}/${summary.total} FR checks passed; ${summary.failed} failed`);
console.log(`RESULT ${path.join(work, 'results.json')}`);
process.exitCode = summary.failed ? 1 : 0;
