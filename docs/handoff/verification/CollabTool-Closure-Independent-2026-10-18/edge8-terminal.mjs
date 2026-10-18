// Independent §14 checks beyond the original 24 cases. No real Unity invocation.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
const repo = process.cwd();
const subject = path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE || 'tools/collab');
const work = path.join(repo,'.collab-local/reaccept-20261017', process.env.COLLAB_AUDIT_RUN_LABEL || 'supplemental',new Date().toISOString().replace(/[:.]/g,'-'));
fs.mkdirSync(work,{recursive:true});
const cmd = await import(pathToFileURL(path.join(subject,'lib/commands.mjs')));
const fp = await import(pathToFileURL(path.join(subject,'lib/fingerprint.mjs')));
const state = await import(pathToFileURL(path.join(subject,'lib/state.mjs')));
const schema = await import(pathToFileURL(path.join(subject,'lib/schemas.mjs')));
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const protectedFiles = ['docs/collaboration/deliveries/DEL-20261017065745-c9b348.json','docs/collaboration/reviews/REV-20261017065756-f1940a.json','docs/collaboration/deliveries/DEL-20261017095048-cfd9dd.json','docs/Development Manual/Development Task Book/Task book/Airline_CEO_Dev_Task_2026-10-17.md'];
const before = Object.fromEntries(protectedFiles.map(f=>[f,sha(path.join(repo,f))]));
const prodHash = () => fp.hashManifest(fp.collectEntries(repo,['Unity Project/Assets','Unity Project/Packages','Unity Project/ProjectSettings']));
const productionBefore = prodHash();
const results=[]; let serial=0;
const put=(root,file,body)=>{const dest=path.join(root,file);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,typeof body==='string'?body:JSON.stringify(body,null,2));};
const read=(root,file)=>JSON.parse(fs.readFileSync(path.join(root,file),'utf8'));
function fixture({roots=['src'], command=[process.execPath,'-e','process.stdout.write("arbitrary non-result text")']}={}) {
  const root=path.join(work,'fixtures',String(++serial).padStart(2,'0'));
  fs.mkdirSync(root,{recursive:true});fs.cpSync(subject,path.join(root,'tools/collab'),{recursive:true});
  put(root,'docs/collaboration/PROTOCOL.md','# Isolated independent fixture');put(root,'src/model.cs','A');
  const cfg=read(root,'tools/collab/config.example.json');
  cfg._repoRoot=root;cfg._configPath=path.join(root,'tools/collab/config.json');cfg.demoSnapshotRoots=roots;
  cfg.profiles={smoke:{kind:'node',command}};put(root,'tools/collab/config.json',cfg);cmd.cmdInit(cfg,{});
  return {root,cfg};
}
const deliver=f=>cmd.cmdDeliver(f.cfg,{actor:'cursor',task:'C-DEMO',summary:'Independent supplemental fixture',profiles:'smoke'});
const start=(f,d=deliver(f))=>cmd.cmdReviewStart(f.cfg,{actor:'codex',delivery:d.deliveryId,profile:'smoke'});
const run=(f,r)=>cmd.cmdReviewRun(f.cfg,{actor:'codex',review:r.reviewId});
const rev=(f,r)=>read(f.root,`docs/collaboration/reviews/${r.reviewId}.json`);
function finish(f,r,patch={}) {
  const d=read(f.root,`docs/collaboration/deliveries/${rev(f,r).deliveryId}.json`);
  const e={conclusion:'passed',subjectImplementationFingerprint:d.fingerprints.implementation.fingerprint,assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0,coverage:['AC1','AC2','AC3'].map(id=>({id,result:'passed',evidence:'caller declaration'})),...patch};
  const file=path.join(f.root,`evidence-${crypto.randomBytes(4).toString('hex')}.json`);fs.writeFileSync(file,JSON.stringify(e));
  return cmd.cmdReviewFinish(f.cfg,{actor:'codex',review:r.reviewId,evidence:file});
}
const outcome=fn=>{try{return {accepted:true,value:fn()};}catch(e){return {accepted:false,exitCode:e.exitCode??null,error:e.message};}};
async function test(id,requirement,fn){try{results.push({id,requirement,...await fn()});}catch(e){results.push({id,requirement,passed:false,testError:e.stack});}console.log(`${results.at(-1).passed?'PASS':'FAIL'} ${id}: ${requirement}`);}
// Independent boundary checks for the newly added parser and run state machine.
await test('T01','Unstructured output from a declared JSON profile cannot support passed',()=>{
  const f=fixture();f.cfg.profiles.smoke.resultKind='json';const r=start(f);run(f,r);const o=outcome(()=>finish(f,r));
  return {passed:!o.accepted&&o.exitCode===6,observation:o,machineResult:rev(f,r).machineResult};
});
await test('T02','A malformed declared result file cannot fall back to passing stdout',()=>{
  const f=fixture({command:[process.execPath,'-e','require("fs").writeFileSync("result.xml","this is not XML");console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))']});
  f.cfg.profiles.smoke.resultKind='nunit';f.cfg.profiles.smoke.resultFile='result.xml';const r=start(f);run(f,r);const o=outcome(()=>finish(f,r));
  return {passed:!o.accepted&&o.exitCode===6,observation:o};
});
await test('T03','NUnit XML missing passed/failed/skipped attributes cannot pass',()=>{
  const f=fixture({command:[process.execPath,'-e','require("fs").writeFileSync("result.xml",\'<test-run total="1" result="Passed"/>\');console.log("fixture complete")']});
  f.cfg.profiles.smoke.resultKind='nunit';f.cfg.profiles.smoke.resultFile='result.xml';const r=start(f);run(f,r);const o=outcome(()=>finish(f,r,{assertionsTotal:1,assertionsFailed:0,assertionsSkipped:0}));
  return {passed:!o.accepted&&o.exitCode===6,parsed:cmd.parseNunitCounters('<test-run total="1" result="Passed"/>'),observation:o};
});
await test('T04','NUnit failure cases cannot be concealed by inconsistent root counters',()=>{
  const xml='<test-run total="1" passed="1" failed="0" skipped="0" result="Passed"><test-case result="Failed"/></test-run>';
  const f=fixture({command:[process.execPath,'-e',`require("fs").writeFileSync("result.xml",${JSON.stringify(xml)});console.log("fixture complete")`]});
  f.cfg.profiles.smoke.resultKind='nunit';f.cfg.profiles.smoke.resultFile='result.xml';const r=start(f);run(f,r);const o=outcome(()=>finish(f,r,{assertionsTotal:1,assertionsFailed:0,assertionsSkipped:0}));
  return {passed:!o.accepted&&o.exitCode===6,xml,observation:o};
});
await test('T05','A pre-existing result file unrelated to the run cannot support passed',()=>{
  const f=fixture({command:[process.execPath,'-e','console.log("fixture ran but produced no XML")']});
  put(f.root,'src/result.xml','<test-run total="1" passed="1" failed="0" skipped="0" result="Passed"/>');
  f.cfg.profiles.smoke.resultKind='nunit';f.cfg.profiles.smoke.resultFile='src/result.xml';const r=start(f);const rr=run(f,r);const o=outcome(()=>finish(f,r,{assertionsTotal:1,assertionsFailed:0,assertionsSkipped:0}));
  return {passed:!o.accepted&&o.exitCode===6,resultPredatedRun:true,run:rr,observation:o};
});
await test('T06','Run or finish detects execution-time modification of snapshot source',()=>{
  const f=fixture({command:[process.execPath,'-e','require("fs").writeFileSync("src/model.cs","mutated-during-run");console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))']});
  const r=start(f);const runObservation=outcome(()=>run(f,r));const record=rev(f,r);const file=path.join(f.root,'docs/collaboration/reviews',r.reviewId+'.json');const before=sha(file);const o=outcome(()=>finish(f,r));
  const terminalRefusal=o.exitCode===3&&record.status==='failed'&&!!record.finishedAt&&!runObservation.accepted&&runObservation.exitCode===5&&/snapshot/.test(runObservation.error)&&/immutable terminal status=failed/.test(o.error);
  return {passed:!o.accepted&&([5,6].includes(o.exitCode)||terminalRefusal)&&sha(file)===before,terminalRefusal,reportUnchanged:sha(file)===before,runObservation,observation:o,snapshotSource:fs.readFileSync(path.join(f.root,record.snapshotPath,'src/model.cs'),'utf8')};
});
await test('T07','Two processes running the same review execute its command at most once',async()=>{
  const f=fixture();const counter=path.join(f.root,'executions.txt');
  f.cfg.profiles.smoke.command=[process.execPath,'-e',`require("fs").appendFileSync(${JSON.stringify(counter)},"executed\\n");setTimeout(()=>console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0})),1500)`];
  put(f.root,'tools/collab/config.json',f.cfg);const r=start(f);
  function worker(){const child=spawn(process.execPath,[path.join(f.root,'tools/collab/cli.mjs'),'review-run','--actor','codex','--review',r.reviewId,'--json'],{cwd:f.root,stdio:['ignore','pipe','pipe']});let output='';child.stdout.on('data',d=>output+=d);child.stderr.on('data',d=>output+=d);return new Promise(resolve=>child.on('close',exitCode=>resolve({exitCode,output})));}
  const first=worker();const until=Date.now()+15000;while(!fs.existsSync(counter)){if(Date.now()>until)throw Error('bounded fixture start timeout');await new Promise(resolve=>setTimeout(resolve,25));}
  const second=worker();const workers=await Promise.all([first,second]);const executions=fs.readFileSync(counter,'utf8').trim().split(/\r?\n/).length;
  return {passed:executions===1,executions,workers,recordedRuns:rev(f,r).runs.length};
});
await test('T08','A second review can reuse immutable inputs after first run emits build artifacts',()=>{
  const f=fixture({command:[process.execPath,'-e','require("fs").mkdirSync("tools/semcheck/obj",{recursive:true});require("fs").writeFileSync("tools/semcheck/obj/build-output.txt","artifact");console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))']});
  const d=deliver(f);const r=start(f,d);run(f,r);finish(f,r);const second=start(f,d);const o=outcome(()=>run(f,second));
  return {passed:o.accepted,observation:o,note:'Uses only fixture Node output, not actual compiler or Unity.'};
});
const after=Object.fromEntries(protectedFiles.map(f=>[f,sha(path.join(repo,f))]));const productionAfter=prodHash();
const result={date:new Date().toISOString(),deliveryId:'DEL-20261017122743-589b23',subjectToolsPath:subject,sourceFingerprint:fp.hashManifest(fp.collectEntries(path.resolve(subject,'../..'),['tools/collab'])),fixtureRoot:work,total:results.length,passed:results.filter(r=>r.passed).length,failed:results.filter(r=>!r.passed).length,results,preservation:{productionBefore,productionAfter,productionUnchanged:productionBefore===productionAfter,records:protectedFiles.map(file=>({file,before:before[file],after:after[file],unchanged:before[file]===after[file]}))}};
put(work,'results.json',result);console.log(`SUMMARY ${result.passed}/${result.total} third-round boundary checks passed; ${result.failed} failed`);console.log(`RESULT ${path.join(work,'results.json')}`);process.exitCode=result.failed?1:0;
