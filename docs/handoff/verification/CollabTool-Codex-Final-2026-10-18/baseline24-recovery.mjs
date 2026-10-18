// N20 fixture improvement: observe actual test-command start before killing the worker.
// Original failure is preserved; success requires interrupted + reason, not merely != running.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync, spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const repo = process.cwd();
const reportRoot = path.join(repo, '.collab-local/acceptance-20261017', process.env.COLLAB_AUDIT_RUN_LABEL || 'current');
const subjectTools = process.env.COLLAB_AUDIT_TOOL_SOURCE ? path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE) : path.join(repo,'tools/collab');
const work = path.join(reportRoot, 'runs', new Date().toISOString().replace(/[:.]/g,'-'));
fs.mkdirSync(work, { recursive: true });
const imp = name => import(pathToFileURL(path.join(subjectTools, 'lib', name + '.mjs')));
const cmd = await imp('commands');
const fp = await imp('fingerprint');
const schema = await imp('schemas');
const state = await imp('state');
const snapshot = await imp('snapshot');
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const originalFiles = [
  'docs/collaboration/deliveries/DEL-20261017065745-c9b348.json',
  'docs/collaboration/reviews/REV-20261017065756-f1940a.json',
  'docs/Development Manual/Development Task Book/Task book/Airline_CEO_Dev_Task_2026-10-17.md'
];
const productionBefore = fp.hashManifest(fp.collectEntries(repo, ['Unity Project/Assets', 'Unity Project/Packages', 'Unity Project/ProjectSettings']));
const beforeHashes = Object.fromEntries(originalFiles.map(f => [f, sha(path.join(repo, f))]));
let serial = 0;
const results = [];
const put = (root, file, contents) => { const dest = path.join(root, file); fs.mkdirSync(path.dirname(dest), {recursive:true}); fs.writeFileSync(dest, typeof contents === 'string' ? contents : JSON.stringify(contents, null, 2)); };
function fixture({ demoRoots = ['src'], command = [process.execPath, '-e', 'process.stdout.write("no machine result")'] } = {}) {
  const root = path.join(work, 'fixtures', String(++serial).padStart(2, '0'));
  fs.mkdirSync(root, { recursive: true });
  fs.cpSync(subjectTools, path.join(root, 'tools/collab'), { recursive: true });
  put(root, 'docs/collaboration/PROTOCOL.md', '# Isolated acceptance fixture\n');
  put(root, 'src/model.cs', 'snapshot-A');
  put(root, 'tools/semcheck/run.js', '// fixture existence only, never executed');
  put(root, 'tools/regression/export-baseline.js', '// fixture existence only');
  put(root, 'tools/slotcheck/slotcheck.csproj', '<Project/>');
  const cfg = JSON.parse(fs.readFileSync(path.join(root, 'tools/collab/config.example.json'), 'utf8'));
  cfg._repoRoot = root;
  cfg._configPath = path.join(root, 'tools/collab/config.json');
  cfg.demoSnapshotRoots = demoRoots;
  cfg.profiles = { smoke: { kind: 'node', command } };
  put(root, 'tools/collab/config.json', cfg);
  cmd.cmdInit(cfg, {});
  return { root, cfg };
}
function deliver(f) { return cmd.cmdDeliver(f.cfg, {actor:'cursor', task:'C-DEMO', summary:'Independent isolated acceptance fixture', profiles:'smoke'}); }
function start(f, d = deliver(f)) { return cmd.cmdReviewStart(f.cfg, {actor:'codex', delivery:d.deliveryId, profile:'smoke'}); }
function run(f, r) { return cmd.cmdReviewRun(f.cfg, {actor:'codex', review:r.reviewId}); }
function evidence(f, value) { const file = path.join(f.root, 'evidence-' + crypto.randomBytes(4).toString('hex') + '.json'); fs.writeFileSync(file, JSON.stringify(value)); return file; }
function deliveryFp(f, reviewId) {
  const review = JSON.parse(fs.readFileSync(path.join(f.root, 'docs/collaboration/reviews', reviewId + '.json'), 'utf8'));
  const delivery = JSON.parse(fs.readFileSync(path.join(f.root, 'docs/collaboration/deliveries', review.deliveryId + '.json'), 'utf8'));
  return delivery.fingerprints.implementation.fingerprint;
}
function finish(f, r, value) {
  const body = { ...value };
  if (!Object.prototype.hasOwnProperty.call(value, 'subjectImplementationFingerprint')) {
    // Bind subject fingerprint from the review's delivery unless the case intentionally omits it (N03).
    if (value.assertionsTotal >= 3 && Array.isArray(value.coverage) && value.coverage.length >= 3) {
      body.subjectImplementationFingerprint = deliveryFp(f, r.reviewId);
    }
  }
  return cmd.cmdReviewFinish(f.cfg, {actor:'codex', review:r.reviewId, evidence:evidence(f, body)});
}
const full = () => ({conclusion:'passed', assertionsTotal:3, assertionsFailed:0, assertionsSkipped:0, coverage:['AC1','AC2','AC3'].map(id=>({id,result:'passed',evidence:'controlled fixture assertion'}))});
function outcome(fn) { try { return {accepted:true,value:fn()}; } catch (e) { return {accepted:false,exitCode:e.exitCode ?? null,error:e.message}; } }
async function test(id, requirement, check) {
  try {
    const observation = await check();
    results.push({id,requirement,...observation});
  } catch(e) {results.push({id,requirement,passed:false,testError:e.stack});}
  const last=results.at(-1); process.stdout.write(`${last.passed?'PASS':'FAIL'} ${id}: ${requirement}\n`);
}

await test('P01','Developer supplied tests run in disposable clone, not real project',()=>{
  const f=fixture();
  const r=spawnSync(process.execPath,[path.join(f.root,'tools/collab/tests/collab.test.mjs')],{cwd:f.root,encoding:'utf8',timeout:60000});
  put(work,'provided-tests.stdout.txt',r.stdout||''); put(work,'provided-tests.stderr.txt',r.stderr||'');
  return {passed:r.status===0, exitCode:r.status, output:r.stdout, error:r.error?.message};
});
await test('P02','Cursor cannot publish review-finish',()=>{
  const f=fixture(); const o=outcome(()=>cmd.cmdReviewFinish(f.cfg,{actor:'cursor',review:'none',evidence:'none'}));
  return {passed:!o.accepted&&o.exitCode===7,observation:o};
});
await test('P03','Repeated init preserves existing task and state bytes',()=>{
  const f=fixture();const files=['docs/collaboration/tasks/C-DEMO.json','docs/collaboration/state.json'];
  const a=files.map(p=>sha(path.join(f.root,p)));cmd.cmdInit(f.cfg,{});
  return {passed:files.every((p,i)=>sha(path.join(f.root,p))===a[i])};
});
await test('N01','Malformed types and empty acceptance objects must be rejected',()=>{
  const o=outcome(()=>schema.validateTask({schemaVersion:'bad',projectId:1,taskId:'../escape',title:4,revision:-9,devStatus:'todo',reviewStatus:'not_run',scope:42,acceptanceCriteria:[{}],createdAt:'not-a-date',updatedAt:'not-a-date'}));
  return {passed:!o.accepted&&o.exitCode===2,observation:o};
});
await test('N02','Review executes fixed snapshot A rather than edited workspace B',()=>{
  const f=fixture({command:[process.execPath,'-e','process.stdout.write(require("fs").readFileSync("src/model.cs","utf8"))']});
  const d=deliver(f);put(f.root,'src/model.cs','workspace-B');const r=start(f,d);const result=run(f,r);
  const actual=fs.readFileSync(path.join(f.root,result.stdoutPath),'utf8');
  return {passed:actual==='snapshot-A',actual,expected:'snapshot-A',snapshot:path.join(d.snapshotPath,'src/model.cs')};
});
await test('N03','Review must reject evidence missing subject fingerprint and real result',()=>{
  const f=fixture();const r=start(f);run(f,r);const o=outcome(()=>finish(f,r,{conclusion:'passed',assertionsTotal:1,coverage:[{id:'AC1',result:'passed'}]}));
  return {passed:!o.accepted&&o.exitCode===6,observation:o};
});
await test('N04','Required criterion not_run prevents passed',()=>{
  const f=fixture();const r=start(f);run(f,r);const e=full();e.coverage[2].result='not_run';
  const o=outcome(()=>finish(f,r,e));return {passed:!o.accepted&&o.exitCode===6,observation:o};
});
await test('N05','Only-skipped assertions prevent passed',()=>{
  const f=fixture();const r=start(f);run(f,r);const e=full();e.assertionsSkipped=3;
  const o=outcome(()=>finish(f,r,e));return {passed:!o.accepted&&o.exitCode===6,observation:o};
});
await test('N06','Finished review cannot be overwritten by another finish',()=>{
  const f=fixture();const r=start(f);run(f,r);finish(f,r,full());
  const file=path.join(f.root,'docs/collaboration/reviews',r.reviewId+'.json');const before=sha(file);
  const o=outcome(()=>finish(f,r,{...full(),conclusion:'failed'}));
  return {passed:!o.accepted&&o.exitCode===3&&sha(file)===before,hashChanged:sha(file)!==before,observation:o};
});
await test('N07','Finalization rechecks current code and does not mark changed workspace current',()=>{
  const f=fixture();const r=start(f);run(f,r);put(f.root,'src/model.cs','workspace-B');finish(f,r,full());
  const task=state.loadTask(f.cfg,'C-DEMO');return {passed:task.freshness!=='current',actual:{reviewStatus:task.reviewStatus,freshness:task.freshness}};
});
await test('N08','Operational state changes do not mutate acceptance-contract fingerprint',()=>{
  const f=fixture();const d=deliver(f);
  const stored=JSON.parse(fs.readFileSync(path.join(f.root,'docs/collaboration/deliveries',d.deliveryId+'.json')));
  const current=fp.computeFingerprints(f.root,{implementationRoots:['src'],verificationRoots:['src'],contractPaths:['docs/collaboration/tasks/C-DEMO.json']});
  return {passed:stored.fingerprints.contract.fingerprint===current.contract.fingerprint,before:stored.fingerprints.contract.fingerprint,after:current.contract.fingerprint};
});
await test('N09','Demo generated events and CURRENT do not affect implementation fingerprint',()=>{
  const f=fixture({demoRoots:['tools/collab','docs/collaboration']});
  const a=fp.computeFingerprints(f.root,{implementationRoots:f.cfg.demoSnapshotRoots}).implementation.fingerprint;
  cmd.cmdAck(f.cfg,{actor:'codex',revision:state.loadState(f.cfg).revision});
  const b=fp.computeFingerprints(f.root,{implementationRoots:f.cfg.demoSnapshotRoots}).implementation.fingerprint;
  return {passed:a===b,before:a,after:b};
});
await test('N10','Unity Assets/Tests changes belong to verification fingerprint',()=>{
  const f=fixture();put(f.root,'Unity Project/Assets/Tests/Case.cs','assert true');const a=fp.computeFingerprints(f.root);
  put(f.root,'Unity Project/Assets/Tests/Case.cs','assert changed');const b=fp.computeFingerprints(f.root);
  return {passed:a.verification.fingerprint!==b.verification.fingerprint,implementationChanged:a.implementation.fingerprint!==b.implementation.fingerprint,verificationChanged:a.verification.fingerprint!==b.verification.fingerprint};
});
await test('N11','Normal non-demo delivery creates snapshot without overlapping-root mismatch',()=>{
  const f=fixture();f.cfg.demoTaskId='OTHER';const o=outcome(()=>deliver(f));
  return {passed:o.accepted,observation:o};
});
await test('N12','Unread/missing ack prevents claim of current revision',()=>{
  const f=fixture();const o=outcome(()=>cmd.cmdClaim(f.cfg,{actor:'cursor',task:'C-DEMO','expected-revision':state.loadState(f.cfg).revision}));
  return {passed:!o.accepted&&o.exitCode===3,observation:o};
});
await test('N13','Snapshot content tampering is detected before review command',()=>{
  const f=fixture();const d=deliver(f);put(f.root,path.join(d.snapshotPath,'src/model.cs'),'TAMPERED');const r=start(f,d);
  const o=outcome(()=>run(f,r));return {passed:!o.accepted&&[5,6].includes(o.exitCode),observation:o};
});
await test('N14','Failed process plus forged success cannot overwrite earlier failed report',()=>{
  const f=fixture({command:[process.execPath,'-e','process.exit(9)']});const r=start(f);const rr=run(f,r);
  const file=path.join(f.root,'docs/collaboration/reviews',r.reviewId+'.json');const before=sha(file);
  const record=JSON.parse(fs.readFileSync(file,'utf8'));
  const o=outcome(()=>finish(f,r,full()));const terminalRefusal=o.exitCode===3&&record.status==='failed'&&!!record.finishedAt&&/immutable terminal status=failed/.test(o.error);
  return {passed:rr.exitCode===9&&!o.accepted&&(o.exitCode===6||terminalRefusal)&&sha(file)===before,externalExitCode:rr.exitCode,terminalRefusal,reportUnchanged:sha(file)===before,observation:o};
});
await test('N15','Repeated no-change sync leaves revision and contract stable',()=>{
  const f=fixture();cmd.cmdSync(f.cfg,{});const a=state.loadState(f.cfg).revision;
  cmd.cmdAck(f.cfg,{actor:'codex',revision:a});cmd.cmdSync(f.cfg,{});
  const b=state.loadState(f.cfg).revision;
  return {passed:a===b,before:a,after:b,lastAcknowledged:state.loadAck(f.cfg).codex.revision};
});
await test('N16','Failed review task status agrees with recorded failed run',()=>{
  const f=fixture({command:[process.execPath,'-e','process.exit(9)']});const r=start(f);run(f,r);
  const report=JSON.parse(fs.readFileSync(path.join(f.root,'docs/collaboration/reviews',r.reviewId+'.json')));
  const task=state.loadTask(f.cfg,'C-DEMO');return {passed:task.reviewStatus===report.status,reportStatus:report.status,taskStatus:task.reviewStatus};
});
await test('N17','Concurrent different-task claims do not lose shared state revision',async()=>{
  const f=fixture();const demo=state.loadTask(f.cfg,'C-DEMO');
  for(const id of ['A','B'])put(f.root,`docs/collaboration/tasks/${id}.json`,{...demo,taskId:id});
  const before=state.loadState(f.cfg).revision;
  cmd.cmdAck(f.cfg,{actor:'cursor',revision:before});
  const worker=path.join(f.root,'concurrent.mjs');
  // Race two claims under shared state.lock + CAS. Exactly one must commit; the other must conflict.
  // (Rename-barrier interleaving deadlocks once claim holds state.lock for the whole critical section.)
  fs.writeFileSync(worker,`import {cmdClaim} from './tools/collab/lib/commands.mjs';import {loadConfig} from './tools/collab/lib/paths.mjs';const gate=process.argv[3],id=process.argv[2];import fs from 'node:fs';while(!fs.existsSync(gate)){};try{console.log(JSON.stringify(cmdClaim(loadConfig(),{actor:'cursor',task:id,'expected-revision':${before}})));}catch(e){console.log(JSON.stringify({error:e.message,exitCode:e.exitCode}));process.exit(e.exitCode||1);}`);
  const gate=path.join(f.root,'start-gate');
  const children=['A','B'].map(id=>{const p=spawn(process.execPath,[worker,id,gate],{cwd:f.root,stdio:['ignore','pipe','pipe']});let out='';p.stdout.on('data',d=>out+=d);p.stderr.on('data',d=>out+=d);return new Promise(resolve=>p.on('close',code=>resolve({code,out})));});
  await new Promise(r=>setTimeout(r,400));fs.writeFileSync(gate,'go');const observations=await Promise.all(children);
  const holders=['A','B'].map(id=>({id,holder:state.loadTask(f.cfg,id).holder}));
  const after=state.loadState(f.cfg).revision;
  return {passed:observations.filter(r=>r.code===0).length===1&&observations.every(r=>[0,3,4].includes(r.code))&&holders.filter(t=>t.holder).length===1&&after===before+1,observations,holders,before,after,scheduler:'Shared state.lock serializes claim; CAS rejects the loser so revision advances exactly once.'};
});
await test('N18','Unity process inspection fails closed if PowerShell cannot be started',()=>{
  const old=process.env.PATH;process.env.PATH=path.join(work,'nonexistent-path');let observation;
  try{observation=cmd.unityRunning();}finally{process.env.PATH=old;}
  return {passed:observation.checked===false,observation};
});
await test('N19','Newly failed latest delivery is not paired with an old passed review',()=>{
  const f=fixture();const d1=deliver(f);const r1=start(f,d1);run(f,r1);finish(f,r1,full());
  const d2=deliver(f);const r2=start(f,d2);run(f,r2);finish(f,r2,{...full(),conclusion:'failed'});
  // Align operational contract to avoid hiding wrong review/delivery pairing behind N08.
  state.refreshFreshness(f.cfg);const task=state.loadTask(f.cfg,'C-DEMO');
  return {passed:!task.freshnessNote?.includes(`${r1.reviewId} passed delivery ${d2.deliveryId}`),oldReview:r1.reviewId,newDelivery:d2.deliveryId,note:task.freshnessNote};
});

await test('P04','Source mutation during snapshot copy is rejected without publishing mixed snapshot',()=>{
  const f=fixture();const dest=path.join(f.root,'out/drift-snapshot');
  const write=fs.writeFileSync;let changed=false;
  fs.writeFileSync=function(file,...args){const value=write.call(fs,file,...args);if(String(file)===path.join(dest,'src/model.cs')&&!changed){changed=true;write.call(fs,path.join(f.root,'src/model.cs'),'changed-during-copy');}return value;};
  let o;try{o=outcome(()=>snapshot.exportStableSnapshot(f.root,dest,{maxAttempts:1,roots:['src']}));}finally{fs.writeFileSync=write;}
  return {passed:!o.accepted&&o.exitCode===5&&!fs.existsSync(dest),observation:o,publishedSnapshot:fs.existsSync(dest)};
});
await test('N20','Interrupted review is recoverable rather than left running indefinitely',async()=>{
  const f=fixture({command:[process.execPath,'-e','const fs=require("fs");fs.mkdirSync("Logs",{recursive:true});fs.writeFileSync("Logs/worker-started","started");Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,1500)']});const r=start(f);
  const child=spawn(process.execPath,[path.join(f.root,'tools/collab/cli.mjs'),'review-run','--review',r.reviewId,'--actor','codex','--json'],{cwd:f.root,stdio:'ignore'});
  const file=path.join(f.root,'docs/collaboration/reviews',r.reviewId+'.json');let observed=false;
  for(let i=0;i<150;i++){await new Promise(resolve=>setTimeout(resolve,20));if(JSON.parse(fs.readFileSync(file)).status==='running' && fs.existsSync(path.join(f.root,JSON.parse(fs.readFileSync(file)).snapshotPath,'Logs/worker-started'))){observed=true;break;}}
  if(!observed)throw new Error('Fixture review never started');
  const closed=new Promise(resolve=>child.once('close',resolve));child.kill();await closed;
  await new Promise(resolve=>setTimeout(resolve,1700));cmd.cmdSync(f.cfg,{});
  const record=JSON.parse(fs.readFileSync(file));
  const helper=spawnSync(process.execPath,[path.join(f.root,'tools/collab/cli.mjs'),'help'],{cwd:f.root,encoding:'utf8'});
  return {passed:observed&&record.status==='interrupted'&&!!record.interruptReason,statusAfterWorkerExitAndSync:record.status,trackedWorkerPid:record.pid??null,recoveryCommandDocumented:/resume|recover|unlock|cancel/.test(helper.stdout)};
});

const productionAfter=fp.hashManifest(fp.collectEntries(repo,['Unity Project/Assets','Unity Project/Packages','Unity Project/ProjectSettings']));
const preservation={productionUnchanged:productionBefore===productionAfter,productionBefore,productionAfter,originalRecords:Object.fromEntries(originalFiles.map(f=>[f,{before:beforeHashes[f],after:sha(path.join(repo,f)),unchanged:beforeHashes[f]===sha(path.join(repo,f))}]))};
const source=fp.collectEntries(path.resolve(subjectTools,'../..'),['tools/collab']);
const report={date:new Date().toISOString(),fixtureRoot:work,subjectToolsPath:subjectTools,scope:'Independent acceptance of explicitly selected tool source; all behavioral fixtures are disposable; no Unity or production code execution',sourceFingerprint:fp.hashManifest(source),sourceEntries:source,referencedDelivery:'DEL-20261017065745-c9b348',referencedReview:'REV-20261017065756-f1940a',total:results.length,passed:results.filter(x=>x.passed).length,failed:results.filter(x=>!x.passed).length,results,preservation};
put(work,'results.json',report);
put(reportRoot,'results.json',report);
process.stdout.write(`SUMMARY ${report.passed}/${report.total} conformance checks passed; ${report.failed} failed\n`);
process.exit(report.failed?1:0);
