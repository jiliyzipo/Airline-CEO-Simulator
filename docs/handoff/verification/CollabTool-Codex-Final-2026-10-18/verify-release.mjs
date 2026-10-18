// Execute existing suites against an explicit subject; never import stale shell selection.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
const args=process.argv.slice(2);
const arg=(name,fallback)=>{const i=args.indexOf('--'+name);return i<0?fallback:args[i+1];};
const repo=process.cwd(), subject=path.resolve(arg('subject','tools/collab'));
const label=arg('label',new Date().toISOString().replace(/[:.]/g,'-'));
if(!/^[A-Za-z0-9_.-]+$/.test(label))throw Error('label must be a filename component');
const out=path.join(repo,'docs/handoff/verification/CollabTool-Codex-Final-2026-10-18',label);
fs.mkdirSync(out,{recursive:false});
const env={...process.env,COLLAB_AUDIT_TOOL_SOURCE:subject,COLLAB_AUDIT_RUN_LABEL:'codex-final-'+label};
const result=[];
for(const [id,script,extra] of [
  ['all',path.join(subject,'tests/run-all.mjs'),[]],
  ['external','docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/run-external.mjs',['--subject',subject,'--label','codex-final-'+label]],
  ['xml-gate','docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs',[]],
]){
  const r=spawnSync(process.execPath,[script,...extra],{cwd:repo,env,encoding:'utf8',timeout:240000,maxBuffer:12*1024*1024,windowsHide:true});
  for(const [kind,text] of [['stdout',r.stdout||''],['stderr',r.stderr||'']])fs.writeFileSync(path.join(out,`${id}.${kind}.txt`),text);
  result.push({id,exitCode:r.status??1,spawnError:r.error?.message||null,stdoutSha256:crypto.createHash('sha256').update(r.stdout||'').digest('hex')});
  if(id==='all'&&r.status===0){
    const summary=JSON.parse(r.stdout.trim());
    fs.writeFileSync(path.join(out,'all.summary.json'),JSON.stringify(summary,null,2));
    for(const name of fs.readdirSync(summary.evidenceDirectory)) if(/\.(?:txt|json)$/.test(name))fs.copyFileSync(path.join(summary.evidenceDirectory,name),path.join(out,name));
  }
  if(id==='external'){
    const evidence=path.join(repo,'docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/evidence','codex-final-'+label);
    if(fs.existsSync(evidence))fs.cpSync(evidence,path.join(out,'external-evidence'),{recursive:true});
  }
  console.log(`${r.status===0?'PASS':'FAIL'} ${id} exit=${r.status}`);
}
const files=[];
function walk(dir){for(const name of fs.readdirSync(dir).sort()){const p=path.join(dir,name),s=fs.statSync(p);if(s.isDirectory())walk(p);else files.push({path:path.relative(out,p).replace(/\\/g,'/'),bytes:s.size,sha256:crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')});}}
walk(out);
const summary={at:new Date().toISOString(),subject,label,result,files,allPassed:result.every(r=>r.exitCode===0)};
fs.writeFileSync(path.join(out,'release-verification.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify({out,allPassed:summary.allPassed}));
process.exitCode=summary.allPassed?0:1;
