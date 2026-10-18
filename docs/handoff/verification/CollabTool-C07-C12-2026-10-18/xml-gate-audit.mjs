// Independent XML result-gate audit of a delivered snapshot (no workspace imports).
// Usage: node docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs
//        [COLLAB_AUDIT_TOOL_SOURCE=out/collab/<DEL>/tools/collab] [COLLAB_AUDIT_RUN_LABEL=label]
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';

const repo = process.cwd();
const subject = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || 'tools/collab');
const label = process.env.COLLAB_AUDIT_RUN_LABEL || 'c07-c12-xml';
const work = path.join(repo, '.collab-local', label, new Date().toISOString().replace(/[:.]/g, '-'));
fs.mkdirSync(work, { recursive: true });

const cmd = await import(pathToFileURL(path.join(subject, 'lib/commands.mjs')));
const fp = await import(pathToFileURL(path.join(subject, 'lib/fingerprint.mjs')));
const { parseXmlDocument } = await import(pathToFileURL(path.join(subject, 'lib/xml.mjs')));

const sha = (f) => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const put = (root, rel, body) => {
  const dest = path.join(root, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, typeof body === 'string' ? body : JSON.stringify(body, null, 2));
};
const read = (root, rel) => JSON.parse(fs.readFileSync(path.join(root, rel), 'utf8'));

let serial = 0;
function fixture({ xml, validJson = false }) {
  const root = path.join(work, 'fixtures', String(++serial).padStart(2, '0'));
  fs.mkdirSync(root, { recursive: true });
  fs.cpSync(subject, path.join(root, 'tools/collab'), { recursive: true });
  put(root, 'docs/collaboration/PROTOCOL.md', '# Isolated XML gate audit fixture');
  put(root, 'src/model.cs', 'A');
  const cfg = read(root, 'tools/collab/config.example.json');
  cfg._repoRoot = root;
  cfg._configPath = path.join(root, 'tools/collab/config.json');
  cfg.demoSnapshotRoots = ['src'];
  const script = validJson
    ? 'console.log(JSON.stringify({assertionsTotal:1,assertionsFailed:0,assertionsSkipped:0}))'
    : `require("fs").writeFileSync("result.xml", ${JSON.stringify(xml)});console.log("fixture complete")`;
  cfg.profiles = { smoke: { kind: 'node', command: [process.execPath, '-e', script] } };
  if (!validJson) {
    cfg.profiles.smoke.resultKind = 'nunit';
    cfg.profiles.smoke.resultFile = 'result.xml';
  }
  put(root, 'tools/collab/config.json', cfg);
  cmd.cmdInit(cfg, {});
  return { root, cfg };
}
const deliver = (f) => cmd.cmdDeliver(f.cfg, { actor: 'cursor', task: 'C-DEMO', summary: 'XML gate audit fixture', profiles: 'smoke' });
const start = (f, d = deliver(f)) => cmd.cmdReviewStart(f.cfg, { actor: 'codex', delivery: d.deliveryId, profile: 'smoke' });
const run = (f, r) => cmd.cmdReviewRun(f.cfg, { actor: 'codex', review: r.reviewId });
function finish(f, r, total = 1) {
  const d = read(f.root, `docs/collaboration/deliveries/${read(f.root, `docs/collaboration/reviews/${r.reviewId}.json`).deliveryId}.json`);
  const evidence = {
    conclusion: 'passed',
    subjectImplementationFingerprint: d.fingerprints.implementation.fingerprint,
    assertionsTotal: total,
    assertionsFailed: 0,
    assertionsSkipped: 0,
    coverage: ['AC1', 'AC2', 'AC3'].map((id) => ({ id, result: 'passed', evidence: 'caller declaration' })),
  };
  const file = path.join(f.root, `evidence-${crypto.randomBytes(4).toString('hex')}.json`);
  fs.writeFileSync(file, JSON.stringify(evidence));
  return cmd.cmdReviewFinish(f.cfg, { actor: 'codex', review: r.reviewId, evidence: file });
}
const outcome = (fn) => {
  try {
    return { accepted: true, value: fn() };
  } catch (e) {
    return { accepted: false, exitCode: e.exitCode ?? null, error: e.message };
  }
};

const attrs = 'total="1" passed="1" failed="0" skipped="0" result="Passed"';
const wrap = (body) => `<test-run ${attrs}>${body}</test-run>`;
const malformed = [
  ['adjacent attributes', '<test-run total="1"passed="1" failed="0" skipped="0" result="Passed"/>'],
  ['CDATA terminator in text', wrap(']]>')],
  ['double hyphen in comment', wrap('<!--bad--comment-->')],
  ['declaration after whitespace', ` <?xml version="1.0"?>${wrap('')}`],
  ['decimal control reference', wrap('&#1;')],
  ['unvalidated doctype', `<!DOCTYPE garbage>${wrap('')}`],
];

const results = [];
const record = (id, requirement, passed, detail) => {
  results.push({ id, requirement, passed, detail });
  console.log(`${passed ? 'PASS' : 'FAIL'} ${id}: ${requirement}`);
};

// 1) Parser level: every malformed document must be rejected, the valid one must parse.
for (const [name, xml] of malformed) {
  const parsed = parseXmlDocument(xml);
  const counters = cmd.parseNunitResult(xml).counters;
  record(`X-${name}`, `parseXmlDocument and NUnit counters refuse ${name}`, parsed.ok === false && !counters, {
    ok: parsed.ok,
    error: parsed.error || null,
    counters: counters || null,
  });
}
const validParsed = parseXmlDocument(wrap('航班 &amp; &#x1F600; <![CDATA[<details>]]>'));
record('X-valid', 'parseXmlDocument still accepts valid UTF-8 NUnit content', validParsed.ok === true, { ok: validParsed.ok, error: validParsed.error || null });

// 2) Gate level: a malformed result file cannot manufacture a passed review.
for (const [name, xml] of malformed.slice(0, 3)) {
  const f = fixture({ xml });
  const r = start(f);
  run(f, r);
  const o = outcome(() => finish(f, r));
  record(`G-${name}`, `finish refuses a bound result file with ${name}`, !o.accepted && [5, 6].includes(o.exitCode), o);
}

// 3) Gate level: a well-formed result file still passes (no over-blocking).
{
  const f = fixture({ xml: wrap('') });
  const r = start(f);
  const rr = run(f, r);
  const o = outcome(() => finish(f, r));
  record('G-valid-nunit', 'A well-formed NUnit result file still finishes passed', o.accepted && o.value.status === 'passed' && rr.exitCode === 0, { run: rr.exitCode, finish: o });
}

// 4) Gate level: plain JSON profiles are unaffected by the XML tightening.
{
  const f = fixture({ validJson: true });
  const r = start(f);
  run(f, r);
  const o = outcome(() => finish(f, r));
  record('G-valid-json', 'JSON machine output still finishes passed', o.accepted && o.value.status === 'passed', o);
}

const summary = {
  date: new Date().toISOString(),
  subject: path.relative(repo, subject).replace(/\\/g, '/'),
  subjectManifest: fp.hashManifest(fp.collectEntries(path.resolve(subject, '../..'), ['tools/collab'])),
  total: results.length,
  passed: results.filter((r) => r.passed).length,
  failed: results.filter((r) => !r.passed).length,
  results,
};
put(work, 'results.json', summary);
fs.writeFileSync(path.join(work, 'results.txt'), results.map((r) => `${r.passed ? 'PASS' : 'FAIL'} ${r.id}: ${r.requirement}`).join('\n') + '\n');
console.log(`SUMMARY ${summary.passed}/${summary.total} XML gate audits passed; ${summary.failed} failed; subject=${summary.subject}`);
console.log(`RESULT ${path.join(work, 'results.json')}`);
process.exitCode = summary.failed ? 1 : 0;
