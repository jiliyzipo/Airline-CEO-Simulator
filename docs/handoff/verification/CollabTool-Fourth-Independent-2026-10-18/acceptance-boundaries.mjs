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
const protectedFiles = ['docs/collaboration/deliveries/DEL-20261017065745-c9b348.json','docs/collaboration/reviews/REV-20261017065756-f1940a.json','docs/collaboration/deliveries/DEL-20261017095048-cfd9dd.json','docs/collaboration/deliveries/DEL-20261017122743-589b23.json','docs/collaboration/deliveries/DEL-20261017150657-dd9483.json','docs/Development Manual/Development Task Book/Task book/Airline_CEO_Dev_Task_2026-10-17.md'];
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
const correctJsonCommand=[process.execPath,'-e','console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}))'];
await test('F01','Valid machine JSON and complete evidence explicitly finish passed',()=>{
  const f=fixture({command:correctJsonCommand});const r=start(f);run(f,r);const result=finish(f,r);const record=rev(f,r);
  return {passed:result.status==='passed'&&record.status==='passed'&&record.machineResult.unverified===false,actual:result};
});
await test('F02','Uncontracted unknown output stays terminal blocked with exit 6 and unknown freshness',()=>{
  const f=fixture();const r=start(f);run(f,r);const result=finish(f,r);const first=rev(f,r);const hash=sha(path.join(f.root,`docs/collaboration/reviews/${r.reviewId}.json`));const repeat=outcome(()=>finish(f,r));cmd.cmdSync(f.cfg,{});const t=state.loadTask(f.cfg,'C-DEMO');
  return {passed:result.status==='blocked'&&result.exitCode===6&&first.status==='blocked'&&!!first.finishedAt&&!repeat.accepted&&repeat.exitCode===3&&t.freshness==='unknown'&&hash===sha(path.join(f.root,`docs/collaboration/reviews/${r.reviewId}.json`)),result,repeat,task:{reviewStatus:t.reviewStatus,freshness:t.freshness}};
});
await test('F03','The actual fourth DEL can execute its configured default smoke successfully',()=>{
  const f=fixture();const deliveryId='DEL-20261017150657-dd9483';const d=read(repo,`docs/collaboration/deliveries/${deliveryId}.json`);
  // Copy real delivered bytes and record into a fixture; never run against or write the original snapshot.
  fs.cpSync(path.join(repo,d.snapshotPath),path.join(f.root,d.snapshotPath),{recursive:true});put(f.root,`docs/collaboration/deliveries/${deliveryId}.json`,d);
  f.cfg.profiles=read(f.root,path.join(d.snapshotPath,'tools/collab/config.example.json')).profiles;
  const r=start(f,d);const observation=outcome(()=>run(f,r));const rr=observation.value;
  return {passed:observation.accepted&&rr.exitCode===0,deliveryId,observation,stdout:rr?fs.readFileSync(path.join(f.root,rr.stdoutPath),'utf8'):null,stderr:rr?fs.readFileSync(path.join(f.root,rr.stderrPath),'utf8'):null};
});
await test('F04','Failed stdout recorded by run cannot be replaced after execution to produce passed',()=>{
  const f=fixture({command:[process.execPath,'-e','console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:1,assertionsSkipped:0}))']});const r=start(f);const rr=run(f,r);const file=path.join(f.root,rr.stdoutPath);const before=sha(file);
  fs.writeFileSync(file,JSON.stringify({assertionsTotal:3,assertionsFailed:0,assertionsSkipped:0}));const o=outcome(()=>finish(f,r));
  return {passed:!o.accepted&&[5,6].includes(o.exitCode),stdoutHashChanged:before!==sha(file),observation:o};
});
await test('F05','Truncated declared NUnit XML is rejected rather than accepted as a complete result',()=>{
  const xml='<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">';
  const f=fixture({command:[process.execPath,'-e',`require("fs").writeFileSync("result.xml",${JSON.stringify(xml)});console.log("fixture complete")`]});f.cfg.profiles.smoke.resultKind='nunit';f.cfg.profiles.smoke.resultFile='result.xml';const r=start(f);run(f,r);const o=outcome(()=>finish(f,r,{assertionsTotal:1,assertionsFailed:0,assertionsSkipped:0}));
  return {passed:!o.accepted&&[5,6].includes(o.exitCode),xml,parsed:cmd.parseNunitCounters(xml),observation:o};
});
await test('F06','A reconstruction from exactly the real delivery manifest can pass configured smoke',()=>{
  const f=fixture();const deliveryId='DEL-20261017150657-dd9483';const d=read(repo,`docs/collaboration/deliveries/${deliveryId}.json`);
  // Do not remove or clean the original. Reconstruct only listed input bytes into a fresh fixture.
  for(const e of d.snapshotManifest.entries){const dest=path.join(f.root,d.snapshotPath,e.path);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.copyFileSync(path.join(repo,d.snapshotPath,e.path),dest);}
  fs.copyFileSync(path.join(repo,d.snapshotPath,'collab-snapshot-manifest.json'),path.join(f.root,d.snapshotPath,'collab-snapshot-manifest.json'));
  put(f.root,`docs/collaboration/deliveries/${deliveryId}.json`,d);f.cfg.profiles=read(f.root,path.join(d.snapshotPath,'tools/collab/config.example.json')).profiles;
  const r=start(f,d);const observation=outcome(()=>run(f,r));const rr=observation.value;
  return {passed:observation.accepted&&rr.exitCode===0,deliveryId,observation,stdout:rr?fs.readFileSync(path.join(f.root,rr.stdoutPath),'utf8'):null};
});
const after=Object.fromEntries(protectedFiles.map(f=>[f,sha(path.join(repo,f))]));const productionAfter=prodHash();
const result={date:new Date().toISOString(),deliveryId:'DEL-20261017150657-dd9483',subjectToolsPath:subject,sourceFingerprint:fp.hashManifest(fp.collectEntries(path.resolve(subject,'../..'),['tools/collab'])),fixtureRoot:work,total:results.length,passed:results.filter(r=>r.passed).length,failed:results.filter(r=>!r.passed).length,results,preservation:{productionBefore,productionAfter,productionUnchanged:productionBefore===productionAfter,records:protectedFiles.map(file=>({file,before:before[file],after:after[file],unchanged:before[file]===after[file]}))}};
put(work,'results.json',result);console.log(`SUMMARY ${result.passed}/${result.total} fourth-round acceptance boundaries passed; ${result.failed} failed`);console.log(`RESULT ${path.join(work,'results.json')}`);process.exitCode=result.failed?1:0;
