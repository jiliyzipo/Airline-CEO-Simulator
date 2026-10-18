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
await test('S01','Exit-zero machine JSON with failed assertions cannot be overridden by caller success',()=>{
  const f=fixture({command:[process.execPath,'-e','console.log(JSON.stringify({assertionsTotal:3,assertionsFailed:1,assertionsSkipped:0}))']});const r=start(f);const rr=run(f,r);const o=outcome(()=>finish(f,r));
  return {passed:!o.accepted&&o.exitCode===6,runExitCode:rr.exitCode,stdout:fs.readFileSync(path.join(f.root,rr.stdoutPath),'utf8'),observation:o};
});
await test('S02','Empty stdout and no result file cannot produce passed',()=>{
  const f=fixture({command:[process.execPath,'-e','process.exit(0)']});const r=start(f);const rr=run(f,r);const o=outcome(()=>finish(f,r));
  return {passed:!o.accepted&&o.exitCode===6,stdoutBytes:fs.statSync(path.join(f.root,rr.stdoutPath)).size,observation:o};
});
await test('S03','Non-numeric assertion counters are rejected',()=>{
  const f=fixture();const r=start(f);run(f,r);const o=outcome(()=>finish(f,r,{assertionsTotal:'not-a-number',assertionsFailed:'not-a-number',assertionsSkipped:'not-a-number'}));
  return {passed:!o.accepted&&[2,6].includes(o.exitCode),observation:o,storedAssertions:rev(f,r).assertions};
});
await test('S04','Demo deliver does not invalidate its own implementation fingerprint',()=>{
  const f=fixture({roots:['tools/collab','docs/collaboration']});const d=deliver(f);const stored=read(f.root,`docs/collaboration/deliveries/${d.deliveryId}.json`);const sync=cmd.cmdSync(f.cfg,{});
  const current=sync.freshness.find(t=>t.taskId==='C-DEMO');return {passed:current.implementationFingerprint===stored.fingerprints.implementation.fingerprint,deliveryFingerprint:stored.fingerprints.implementation.fingerprint,current};
});
await test('S05','Snapshot ProjectSettings identity and executable settings are validated',()=>{
  const f=fixture({roots:['src','Unity Project/ProjectSettings']});put(f.root,'Unity Project/ProjectSettings/ProjectSettings.asset','PlayerSettings:\n  companyName: Original\n  productName: Game\n  scriptingDefineSymbols: SAFE\n');
  const d=deliver(f);put(f.root,path.join(d.snapshotPath,'Unity Project/ProjectSettings/ProjectSettings.asset'),'PlayerSettings:\n  companyName: Original\n  productName: Game\n  scriptingDefineSymbols: TAMPERED\n');const r=start(f,d);const o=outcome(()=>run(f,r));
  return {passed:!o.accepted&&[5,6].includes(o.exitCode),observation:o};
});
await test('S06','Unlisted executable source added to snapshot is detected',()=>{
  const f=fixture();const d=deliver(f);put(f.root,path.join(d.snapshotPath,'src/Extra.cs'),'extra executable input');const r=start(f,d);const o=outcome(()=>run(f,r));
  return {passed:!o.accepted&&[5,6].includes(o.exitCode),observation:o};
});
await test('S07','Finishing smoke does not delete another review resource lock',()=>{
  const f=fixture();const r=start(f);run(f,r);const lock='.collab-local/locks/unity-review.lock';put(f.root,lock,{owner:'different-live-review',reviewId:'OTHER',pid:process.pid,machine:'fixture',heartbeatAt:new Date().toISOString()});
  const hash=sha(path.join(f.root,lock));const o=outcome(()=>finish(f,r));const exists=fs.existsSync(path.join(f.root,lock));return {passed:exists&&sha(path.join(f.root,lock))===hash,lockExistsAfter:exists,observation:o};
});
await test('S08','Cancelling a running worker remains cancelled after the worker completes',async()=>{
  const f=fixture({command:[process.execPath,'-e','setTimeout(()=>console.log("fixture finished"),1600)']});const r=start(f);
  const child=spawn(process.execPath,[path.join(f.root,'tools/collab/cli.mjs'),'review-run','--actor','codex','--review',r.reviewId,'--json'],{cwd:f.root,stdio:['ignore','pipe','pipe']});
  let output='';child.stdout.on('data',d=>output+=d);child.stderr.on('data',d=>output+=d);const done=new Promise(resolve=>child.on('close',code=>resolve(code)));
  const deadline=Date.now()+15000;while(!rev(f,r).workerPid){if(Date.now()>deadline)throw Error('worker start timeout');await new Promise(resolve=>setTimeout(resolve,30));}
  // The worker has recorded running and entered its test-owned bounded command.
  await new Promise(resolve=>setTimeout(resolve,200));const cancelled=cmd.cmdReviewCancel(f.cfg,{actor:'codex',review:r.reviewId});const code=await done;const final=rev(f,r);
  return {passed:final.status==='cancelled'&&!!final.finishedAt,cancelled,workerExitCode:code,finalStatus:final.status,finishedAt:final.finishedAt??null,output};
});
await test('S09','Sync cannot overwrite a claim committed after sync read the task',()=>{
  const f=fixture();cmd.cmdSync(f.cfg,{});const revision=state.loadState(f.cfg).revision;cmd.cmdAck(f.cfg,{actor:'cursor',revision});
  const rename=fs.renameSync;let triggered=false,claim;
  fs.renameSync=function(src,dst){if(!triggered&&dst===path.join(f.root,'docs/collaboration/tasks/C-DEMO.json')){triggered=true;claim=cmd.cmdClaim(f.cfg,{actor:'cursor',task:'C-DEMO','expected-revision':revision});}return rename.call(fs,src,dst);};
  let sync;try{sync=outcome(()=>cmd.cmdSync(f.cfg,{}));}finally{fs.renameSync=rename;}
  const task=state.loadTask(f.cfg,'C-DEMO');const index=state.loadState(f.cfg).tasksIndex['C-DEMO'];return {passed:triggered&&task.holder==='cursor'&&index.holder==='cursor',scheduler:'Run valid claim between sync task read and task replacement; no production code changed.',claim,sync,taskHolder:task.holder,indexHolder:index.holder};
});
await test('S10','PowerShell ack wrapper forwards its actor/revision arguments',()=>{
  const f=fixture();const r=spawnSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(f.root,'tools/collab/Collab.ps1'),'ack','codex'],{cwd:f.root,encoding:'utf8',timeout:20000});
  return {passed:r.status===0&&state.loadAck(f.cfg).codex?.revision===state.loadState(f.cfg).revision,exitCode:r.status,stdout:r.stdout,stderr:r.stderr,error:r.error?.message};
});
await test('S11','Delivery/review/issue/event malformed field types are rejected individually',()=>{
  const f=fixture();const r=start(f);const review=rev(f,r);const delivery=read(f.root,`docs/collaboration/deliveries/${review.deliveryId}.json`);
  const probes=[['delivery',()=>schema.validateDelivery({...delivery,files:42,fingerprints:42,taskRevision:-5,snapshotPath:42})],['review',()=>schema.validateReview({...review,taskRevision:-5,reviewId:42,createdAt:42})],['issue',()=>schema.validateIssue({schemaVersion:1,issueId:'I',taskId:42,deliveryId:42,title:42,expected:42,actual:42,severity:'invalid',status:'invalid',createdAt:42})],['event',()=>schema.validateEvent({schemaVersion:1,eventId:42,projectId:42,revision:-5,at:42,actor:'cursor',action:42})]];
  const observations=probes.map(([type,fn])=>({type,...outcome(fn)}));return {passed:observations.every(o=>!o.accepted&&o.exitCode===2),observations};
});
await test('S12','Verification tool test change preserves implementation identity',()=>{
  const f=fixture();const a=fp.computeFingerprints(f.root);put(f.root,'tools/collab/tests/OnlyVerification.mjs','export const newAssertion = true;');const b=fp.computeFingerprints(f.root);
  return {passed:a.implementation.fingerprint===b.implementation.fingerprint&&a.verification.fingerprint!==b.verification.fingerprint,implementationChanged:a.implementation.fingerprint!==b.implementation.fingerprint,verificationChanged:a.verification.fingerprint!==b.verification.fingerprint};
});
const after=Object.fromEntries(protectedFiles.map(f=>[f,sha(path.join(repo,f))]));
const productionAfter=prodHash();
const result={date:new Date().toISOString(),subjectToolsPath:subject,sourceFingerprint:fp.hashManifest(fp.collectEntries(path.resolve(subject,'../..'),['tools/collab'])),fixtureRoot:work,total:results.length,passed:results.filter(r=>r.passed).length,failed:results.filter(r=>!r.passed).length,results,preservation:{productionBefore,productionAfter,productionUnchanged:productionBefore===productionAfter,records:protectedFiles.map(file=>({file,before:before[file],after:after[file],unchanged:before[file]===after[file]}))}};
put(work,'results.json',result);console.log(`SUMMARY ${result.passed}/${result.total} supplemental checks passed; ${result.failed} failed`);console.log(`RESULT ${path.join(work,'results.json')}`);process.exitCode=result.failed?1:0;
