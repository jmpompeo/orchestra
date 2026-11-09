#!/usr/bin/env node
// Reports recorded trials only. Fixture records are not productivity evidence.
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
const metrics = ['latencySeconds','tokens','costUsd','userOverheadSeconds','developerSeconds'];
const types = ['small-edit','feature','bug','refactor'];
export function summarize(values) {
  if (!values.length) return {n:0,mean:null,min:null,max:null,standardDeviation:null};
  const mean = values.reduce((a,b) => a+b,0)/values.length;
  return {n:values.length,mean,min:Math.min(...values),max:Math.max(...values),standardDeviation:values.length>1 ? Math.sqrt(values.reduce((s,x) => s+(x-mean)**2,0)/(values.length-1)) : null};
}
export function evaluateTrials(data) {
  if (!data || data.version !== 1 || !Array.isArray(data.trials)) throw Error('Invalid trial dataset v1');
  const ids = new Set();
  for (const t of data.trials) {
    if (!t || typeof t.taskId !== 'string' || !t.taskId || !types.includes(t.taskType) || !['baseline','candidate'].includes(t.variant) || typeof t.batch !== 'string' || !t.batch || typeof t.heldOut !== 'boolean' || !Array.isArray(t.attempts) || !t.attempts.length) throw Error('Invalid trial');
    if (t.repetition !== undefined && (typeof t.repetition !== 'string' || !t.repetition)) throw Error('Invalid repetition');
    const key = JSON.stringify([t.taskId,t.batch,t.heldOut,t.repetition ?? '1',t.variant]); if (ids.has(key)) throw Error('Duplicate trial'); ids.add(key);
    for (const a of t.attempts) {
      if (!a || ['accepted','deterministicPassed','failed','retried'].some(k => typeof a[k] !== 'boolean')) throw Error('Attempt outcomes must be explicit booleans');
      for (const metric of metrics) if (a[metric] !== undefined && (typeof a[metric] !== 'number' || !Number.isFinite(a[metric]) || a[metric] < 0)) throw Error(`Invalid metric: ${metric}`);
      for (const metric of ['userOverheadSeconds','developerSeconds']) if (a[metric] !== undefined && (!a.provenance || a.provenance.kind !== 'human-observation' || typeof a.provenance.source !== 'string' || !a.provenance.source.trim())) throw Error('Human time requires human-observation provenance and source');
    }
  }
  const groups = new Map();
  for (const t of data.trials) {const key=JSON.stringify([t.taskId,t.batch,t.heldOut,t.repetition ?? '1']); if (!groups.has(key)) groups.set(key,{}); groups.get(key)[t.variant]=t;}
  const pairs=[], unmatched=[];
  for (const group of groups.values()) {
    if (!group.baseline || !group.candidate) {unmatched.push((group.baseline ?? group.candidate).taskId); continue;}
    if (group.baseline.taskType !== group.candidate.taskType) throw Error('Matched task type differs');
    pairs.push(group);
  }
  const qualityValid = trial => { const a=trial.attempts.at(-1); return a.accepted && a.deterministicPassed && !a.failed; };
  function targets(selected) {
    const unavailable = reason => ({status:'unavailable',reason,overheadReductionTargetPercent:15,productivityIncreaseTargetPercent:10});
    if (data.synthetic === true) return unavailable('Synthetic fixture data cannot establish productivity targets');
    if (unmatched.length) return unavailable('Unmatched trials must be resolved before target claims; do not exclude incomplete attempts');
    if (data.observationKind !== 'human-observation') return unavailable('Dataset must declare observationKind human-observation');
    if (!selected.length || !selected.every(p=>['baseline','candidate'].every(v=>p[v].attempts.every(a=>a.userOverheadSeconds!==undefined && a.developerSeconds!==undefined)))) return unavailable('Complete matched human observations required, including failed and retried attempts');
    const variants = {};
    for (const variant of ['baseline','candidate']) {
      const accepted=selected.filter(p=>qualityValid(p[variant])).length;
      const attempts=selected.flatMap(p=>p[variant].attempts);
      const overhead=attempts.reduce((s,a)=>s+a.userOverheadSeconds,0), developer=attempts.reduce((s,a)=>s+a.developerSeconds,0);
      if (!accepted || !developer) return unavailable('Positive quality-valid accepted count and developer time required in both variants');
      variants[variant]={qualityValidAccepted:accepted,userOverheadSeconds:overhead,developerSeconds:developer,overheadSecondsPerAccepted:overhead/accepted,acceptedPerDeveloperHour:accepted/(developer/3600)};
    }
    if (!variants.baseline.overheadSecondsPerAccepted) return unavailable('Positive baseline overhead required');
    const overheadReductionPercent=(1-variants.candidate.overheadSecondsPerAccepted/variants.baseline.overheadSecondsPerAccepted)*100;
    const productivityIncreasePercent=(variants.candidate.acceptedPerDeveloperHour/variants.baseline.acceptedPerDeveloperHour-1)*100;
    const qualityNoRegression=selected.every(p=>(!qualityValid(p.baseline) || qualityValid(p.candidate)) && (!p.baseline.attempts.at(-1).deterministicPassed || p.candidate.attempts.at(-1).deterministicPassed));
    return {status:'observed',variants,qualityNoRegression,overheadReductionPercent,productivityIncreasePercent,overheadReductionTargetPercent:15,productivityIncreaseTargetPercent:10,targetsMet:qualityNoRegression && overheadReductionPercent>=15-1e-9 && productivityIncreasePercent>=10-1e-9,inference:'Descriptive observed comparison; no statistical significance claim'};
  }
  function compare(selected) {
    const result={matchedTasks:selected.length,taskTypes:[...new Set(selected.map(p=>p.baseline.taskType))],heldOutTasks:selected.filter(p=>p.baseline.heldOut).length,variants:{},metrics:{}};
    for (const variant of ['baseline','candidate']) {
      const attempts=selected.flatMap(p=>p[variant].attempts);
      result.variants[variant]={attempts:attempts.length,failed:attempts.filter(a=>a.failed).length,retried:attempts.filter(a=>a.retried).length,acceptedTasks:selected.filter(p=>p[variant].attempts.at(-1).accepted).length,qualityValidAcceptedTasks:selected.filter(p=>qualityValid(p[variant])).length,deterministicPassedTasks:selected.filter(p=>p[variant].attempts.at(-1).deterministicPassed).length};
    }
    for (const metric of metrics) {
      const measured=selected.filter(p=>['baseline','candidate'].every(v=>p[v].attempts.every(a=>a[metric]!==undefined)));
      const values=Object.fromEntries(['baseline','candidate'].map(v=>[v,measured.map(p=>p[v].attempts.reduce((s,a)=>s+a[metric],0))]));
      const reductions=values.baseline.map((value,i)=>value>0 ? (value-values.candidate[i])/value*100 : null).filter(x=>x!==null);
      result.metrics[metric]={status:measured.length ? 'observed' : 'unavailable',baseline:summarize(values.baseline),candidate:summarize(values.candidate),pairedReductionPercent:summarize(reductions)};
    }
    result.targets=targets(selected);
    return result;
  }
  const batches=Object.fromEntries([...new Set(pairs.map(p=>p.baseline.batch))].map(batch=>[batch,compare(pairs.filter(p=>p.baseline.batch===batch))]));
  return {version:1,evidence:data.synthetic === true ? 'synthetic-fixture-only' : 'recorded-trials',modelPolicy:'Report only; no model assignments changed',unmatched,pooled:compare(pairs),combined:compare(pairs.filter(p=>p.baseline.batch==='combined')),batches,heldOut:compare(pairs.filter(p=>p.baseline.heldOut)),aggregationNote:'Pooled data is descriptive only; combined contains only explicitly recorded batch combined trials. Batch interventions are evaluated separately.'};
}
export async function main(args=process.argv.slice(2)) {
  if (!args.length || args[0].startsWith('-') || (args.length!==1 && !(args.length===3 && args[1]==='--report' && !args[2].startsWith('-')))) throw Error('Usage: node scripts/evaluate-efficiency.mjs TRIALS.json [--report REPORT.json]');
  const report=evaluateTrials(JSON.parse(await readFile(resolve(args[0]),'utf8')));
  if (args[2]) await writeFile(resolve(args[2]),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2)); return report;
}
if (process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) main().catch(error=>{console.error(error.message);process.exitCode=1;});
