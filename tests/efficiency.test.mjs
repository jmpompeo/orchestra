import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
import {planValidation,runValidation,validateManifest,parseArgs,normalizeFile} from '../skills/agentic-feature-delivery/scripts/validate.mjs';
import {evaluateTrials} from '../scripts/evaluate-efficiency.mjs';
const cwd=fileURLToPath(new URL('..',import.meta.url));
const fixture=JSON.parse(await readFile(new URL('./fixtures/efficiency/transitive-manifest.json',import.meta.url)));
const repository=JSON.parse(await readFile(new URL('../scripts/validation-manifest.json',import.meta.url)));
test('transitive affected consumer catches a failure edited-project-only misses',async()=>{
  const plan=planValidation(fixture,['library/api.js'],{cwd});
  assert.deepEqual(plan.affectedProjects,['library','consumer']);
  assert.deepEqual(plan.checks.map(x=>x.id),['build','library-test','consumer-test']);
  assert.match(plan.checks.at(-1).reasons.join(),/depends on affected project: library/);
  const report=await runValidation(plan,{cwd});
  assert.equal(report.passed,false); assert.equal(report.checks.at(-1).exitCode,1);
  assert.equal(report.checks[1].status,'passed'); assert.ok(report.checks.at(-1).durationMs>=0);
});
test('documentation stays narrow while mandatory repository checks remain',()=>{
  const plan=planValidation(repository,['README.md'],{cwd});
  assert.deepEqual(plan.checks.map(x=>x.id),['build','dotnet-tests']);
});
test('embedded content and launcher select required dependencies; unknown paths fall broad',()=>{
  assert.deepEqual(planValidation(repository,['skills/prep/scripts/codex-app-server-run.mjs'],{cwd}).checks.map(x=>x.id),['build','dotnet-tests','launcher-tests','dashboard-tests']);
  const broad=planValidation(repository,['unknown.config'],{cwd});
  assert.ok(broad.checks.some(x=>x.id==='installer-windows'));
});
test('required unavailable platforms and prerequisites cannot silently pass',async()=>{
  const plan=planValidation(fixture,['library/api.js'],{cwd});
  plan.checks[0].platforms=['impossible'];
  const result=await runValidation(plan,{cwd});
  assert.equal(result.passed,false); assert.equal(result.checks[0].status,'unavailable');
  assert.equal(result.checks.at(-1).status,'blocked');
});
test('missing command is unavailable with duration and reason retained',async()=>{
  const plan=planValidation(fixture,['library/api.js'],{cwd});
  plan.checks[0].command=['certainly-not-a-real-efficiency-command'];
  const result=await runValidation(plan,{cwd}); assert.equal(result.checks[0].status,'unavailable'); assert.equal(result.passed,false);
});
test('manifest cycles, bad references, malformed paths and unknown arguments fail closed',()=>{
  const m=structuredClone(fixture);m.projects[0].dependsOn=['consumer']; assert.throws(()=>validateManifest(m),/cycle/);
  const n=structuredClone(fixture);n.checks[0].dependsOn=['missing'];assert.throws(()=>validateManifest(n),/Unknown/);
  assert.throws(()=>normalizeFile('../outside',cwd),/outside/);
  assert.throws(()=>parseArgs(['--files','README.md','--wat']),/Unknown/);
  assert.throws(()=>parseArgs(['--base','main','--files','a']),/either/);
  assert.throws(()=>parseArgs(['--files']),/Missing/);
  assert.deepEqual(parseArgs(['--files','a','b','--files','c']).files,['a','b','c']);
});
test('CLI defaults to plan and loads repository manifest from target cwd',()=>{
  const run=spawnSync(process.execPath,['skills/agentic-feature-delivery/scripts/validate.mjs','--files','README.md'],{cwd,encoding:'utf8'});
  assert.equal(run.status,0,run.stderr);const report=JSON.parse(run.stdout);assert.equal(report.mode,'plan');assert.equal(report.checks[0].status,undefined);
});
const attempt=(overrides={})=>({accepted:true,deterministicPassed:true,failed:false,retried:false,latencySeconds:10,tokens:100,costUsd:0.01,...overrides});
const trial=(variant,overrides={})=>({taskId:'small',taskType:'small-edit',variant,batch:'batch1',heldOut:true,attempts:[attempt()],...overrides});
test('evaluation separates resources and observed human time; retains all attempts and heldout samples',()=>{
  const data={version:1,synthetic:true,trials:[trial('baseline',{attempts:[attempt({failed:true,retried:true,accepted:false}),attempt()]}),trial('candidate')]};
  const report=evaluateTrials(data);assert.equal(report.evidence,'synthetic-fixture-only');
  assert.equal(report.pooled.variants.baseline.failed,1);assert.equal(report.pooled.variants.baseline.retried,1);
  assert.equal(report.pooled.metrics.latencySeconds.baseline.mean,20);
  assert.equal(report.pooled.metrics.latencySeconds.pairedReductionPercent.mean,50);
  assert.equal(report.pooled.metrics.tokens.baseline.mean,200);
  assert.equal(report.pooled.metrics.userOverheadSeconds.status,'unavailable');
  assert.equal(report.pooled.metrics.latencySeconds.baseline.standardDeviation,null);
  assert.equal(report.heldOut.matchedTasks,1); assert.equal(report.batches.batch1.matchedTasks,1);
});
test('actual observed provenance is required for human time and comparisons require matching tasks',()=>{
  assert.throws(()=>evaluateTrials({version:1,trials:[trial('baseline',{attempts:[attempt({developerSeconds:20})]})]}),/provenance/);
  const observed={developerSeconds:20,userOverheadSeconds:3,provenance:{kind:'human-observation',source:'observer time log'}};
  const report=evaluateTrials({version:1,trials:[trial('baseline',{attempts:[attempt(observed)]}),trial('candidate',{attempts:[attempt({...observed,developerSeconds:10})]}),trial('baseline',{taskId:'unmatched'})]});
  assert.equal(report.pooled.metrics.developerSeconds.pairedReductionPercent.mean,50);
  assert.deepEqual(report.unmatched,['unmatched']);
});
test('all task categories, variability and batch reporting use paired observations only',()=>{
  const trials=['small-edit','feature','bug','refactor'].flatMap((type,i)=>['baseline','candidate'].map(variant=>trial(variant,{taskId:type,taskType:type,batch:i<2?'batch1':'batch2',heldOut:i%2===0,attempts:[attempt({latencySeconds:10+i})]})));
  const report=evaluateTrials({version:1,trials}); assert.equal(report.pooled.matchedTasks,4);assert.equal(report.pooled.taskTypes.length,4);assert.equal(report.batches.batch2.matchedTasks,2);assert.ok(report.pooled.metrics.latencySeconds.baseline.standardDeviation>0);
});
test('observed targets use all attempt time, exact thresholds, and require quality preservation',()=>{
  const time=(userOverheadSeconds,developerSeconds,extra={})=>attempt({userOverheadSeconds,developerSeconds,provenance:{kind:'human-observation',source:'test-only observation fixture'},...extra});
  const data={version:1,observationKind:'human-observation',trials:[trial('baseline',{batch:'combined',attempts:[time(3,10,{failed:true,retried:true,accepted:false}),time(17,90)]}),trial('candidate',{batch:'combined',attempts:[time(17,90)]})]};
  const report=evaluateTrials(data);
  assert.equal(report.combined.targets.status,'observed');assert.equal(report.combined.targets.targetsMet,true);
  assert.equal(report.combined.targets.variants.baseline.developerSeconds,100);
  assert.ok(Math.abs(report.combined.targets.overheadReductionPercent-15)<1e-8);
  assert.ok(report.combined.targets.productivityIncreasePercent>=10);
  assert.equal(evaluateTrials({...data,synthetic:true}).combined.targets.status,'unavailable');
  const regression=structuredClone(data);regression.trials.push(trial('baseline',{taskId:'second',batch:'combined',attempts:[time(20,100)]}),trial('candidate',{taskId:'second',batch:'combined',attempts:[time(1,1,{accepted:false,deterministicPassed:false,failed:true})]}));
  const targets=evaluateTrials(regression).combined.targets;assert.equal(targets.qualityNoRegression,false);assert.equal(targets.targetsMet,false);
  assert.equal(evaluateTrials({...data,observationKind:undefined}).combined.targets.status,'unavailable');
});
test('repetitions are matched separately and batches never imply combined intervention results',()=>{
  const data={version:1,trials:[trial('baseline'),trial('candidate'),trial('baseline',{repetition:'2'}),trial('candidate',{repetition:'2'})]};
  const report=evaluateTrials(data);assert.equal(report.pooled.matchedTasks,2);assert.equal(report.combined.matchedTasks,0);
  assert.throws(()=>evaluateTrials({...data,trials:[trial('baseline'),trial('baseline')]}),/Duplicate/);
});
test('base selection includes tracked working changes and untracked files',async()=>{
  const {mkdtemp,writeFile,rm}=await import('node:fs/promises'); const {tmpdir}=await import('node:os');
  const dir=await mkdtemp(resolve(tmpdir(),'efficiency-git-'));
  const git=(...args)=>{const r=spawnSync('git',args,{cwd:dir,encoding:'utf8'});assert.equal(r.status,0,r.stderr);return r.stdout;};
  try {
    git('init','-q');git('config','user.email','fixture@example.invalid');git('config','user.name','Fixture');
    await writeFile(resolve(dir,'tracked.txt'),'before');git('add','tracked.txt');git('commit','-qm','fixture');
    await writeFile(resolve(dir,'tracked.txt'),'after');await writeFile(resolve(dir,'new.txt'),'new');
    const manifestPath=resolve(dir,'manifest.json');await writeFile(manifestPath,JSON.stringify(fixture));
    const run=spawnSync(process.execPath,[resolve(cwd,'skills/agentic-feature-delivery/scripts/validate.mjs'),'--base','HEAD','--manifest',manifestPath],{cwd:dir,encoding:'utf8'});
    assert.equal(run.status,0,run.stderr);const report=JSON.parse(run.stdout);assert.ok(report.files.includes('tracked.txt'));assert.ok(report.files.includes('new.txt'));
    const missing=spawnSync(process.execPath,[resolve(cwd,'skills/agentic-feature-delivery/scripts/validate.mjs'),'--files','new.txt'],{cwd:dir,encoding:'utf8'});assert.notEqual(missing.status,0);
  } finally {await rm(dir,{recursive:true,force:true});}
});
test('synthetic trial fixture cannot establish productivity targets',async()=>{
  const data=JSON.parse(await readFile(new URL('./fixtures/efficiency/synthetic-trials.json',import.meta.url)));
  const report=evaluateTrials(data);assert.equal(report.evidence,'synthetic-fixture-only');assert.equal(report.pooled.targets.status,'unavailable');assert.equal(report.pooled.taskTypes.length,4);
});


test('unmatched observed trials cannot be excluded to claim target achievement',()=>{
 const observed={userOverheadSeconds:10,developerSeconds:100,provenance:{kind:'human-observation',source:'test fixture'}};
 const data={version:1,observationKind:'human-observation',trials:[trial('baseline',{batch:'combined',attempts:[attempt(observed)]}),trial('candidate',{batch:'combined',attempts:[attempt({...observed,userOverheadSeconds:5,developerSeconds:50})]}),trial('baseline',{taskId:'incomplete',batch:'combined',attempts:[attempt({...observed,accepted:false,failed:true})]})]};
 assert.equal(evaluateTrials(data).combined.targets.status,'unavailable');
 assert.match(evaluateTrials(data).combined.targets.reason,/Unmatched/);
});


test('embedded assignment changes reach CLI consumers and dashboard through dependencies',()=>{
 const plan=planValidation(repository,['models.conf'],{cwd});
 assert.ok(plan.affectedProjects.includes('core'));assert.ok(plan.affectedProjects.includes('launcher'));assert.ok(plan.affectedProjects.includes('dashboard'));
 assert.ok(plan.checks.find(c=>c.id==='launcher-tests').reasons.some(r=>r.includes('depends on affected project: core')));
});
