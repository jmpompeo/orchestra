#!/usr/bin/env node
// Local manifests are trusted executable configuration. Planning never executes checks.
import { readFile, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { resolve, relative, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { performance } from 'node:perf_hooks';

const strings = value => Array.isArray(value) && value.every(x => typeof x === 'string' && x.length > 0 && !x.includes('\0'));
export function validateManifest(m) {
  if (!m || m.version !== 1 || !Array.isArray(m.projects) || !Array.isArray(m.checks) || !strings(m.alwaysRequired) || !strings(m.broadChecks)) throw Error('Invalid manifest v1');
  const checkIds = new Set(), projectIds = new Set();
  for (const c of m.checks) {
    if (!c || typeof c.id !== 'string' || !c.id || checkIds.has(c.id) || !strings(c.command) || !c.command.length || !strings(c.dependsOn ?? []) || (c.platforms !== undefined && (!strings(c.platforms) || !c.platforms.length))) throw Error('Invalid or duplicate check');
    checkIds.add(c.id);
  }
  for (const p of m.projects) {
    if (!p || typeof p.id !== 'string' || !p.id || projectIds.has(p.id) || !strings(p.paths) || !p.paths.length || !strings(p.dependsOn ?? []) || !strings(p.checks)) throw Error('Invalid or duplicate project');
    p.paths.forEach(pattern => { if (pattern.includes('..') || pattern.startsWith('/') || pattern.includes('\\') || pattern.includes(':')) throw Error('Unsafe path pattern'); });
    projectIds.add(p.id);
  }
  for (const c of m.checks) for (const id of c.dependsOn ?? []) if (!checkIds.has(id)) throw Error(`Unknown check dependency: ${id}`);
  for (const p of m.projects) {
    for (const id of p.dependsOn ?? []) if (!projectIds.has(id)) throw Error(`Unknown project dependency: ${id}`);
    for (const id of p.checks) if (!checkIds.has(id)) throw Error(`Unknown check: ${id}`);
  }
  for (const id of [...m.alwaysRequired, ...m.broadChecks]) if (!checkIds.has(id)) throw Error(`Unknown check: ${id}`);
  function cycles(items) {
    const done = new Set(), active = new Set(), map = new Map(items.map(x => [x.id,x]));
    function visit(id) { if (active.has(id)) throw Error(`Dependency cycle: ${id}`); if (done.has(id)) return; active.add(id); for (const dep of map.get(id).dependsOn ?? []) visit(dep); active.delete(id); done.add(id); }
    items.forEach(x => visit(x.id));
  }
  cycles(m.projects); cycles(m.checks); return m;
}
export function normalizeFile(file, cwd = process.cwd()) {
  if (typeof file !== 'string' || !file || file.includes('\0') || file.includes('\\')) throw Error('Invalid file path');
  const local = relative(cwd, resolve(cwd,file)).split('\\').join('/');
  if (!local || local === '..' || local.startsWith('../') || isAbsolute(local) || local.includes(':')) throw Error(`Path outside repository: ${file}`);
  return local;
}
function matches(path, pattern) {
  const escaped = pattern.split('**').map(part => part.split('*').map(x => x.replace(/[.+?^${}()|[\]\\]/g,'\\$&')).join('[^/]*')).join('.*');
  return new RegExp(`^${escaped}$`).test(path);
}
export function planValidation(manifest, files, { cwd = process.cwd() } = {}) {
  const m = validateManifest(manifest), normalized = [...new Set(files.map(x => normalizeFile(x,cwd)))];
  if (!normalized.length) throw Error('At least one changed file is required');
  const affected = new Map(), reasons = new Map();
  const add = (id, reason) => reasons.set(id,[...new Set([...(reasons.get(id) ?? []),reason])]);
  m.alwaysRequired.forEach(id => add(id,'always required'));
  for (const file of normalized) {
    const owners = m.projects.filter(p => p.paths.some(pattern => matches(file,pattern)));
    if (!owners.length) m.broadChecks.forEach(id => add(id,`unmapped path: ${file}`));
    for (const p of owners) affected.set(p.id,[...(affected.get(p.id) ?? []),`changed: ${file}`]);
  }
  let changed = true;
  while (changed) { changed = false; for (const p of m.projects) if (!affected.has(p.id)) { const deps = (p.dependsOn ?? []).filter(id => affected.has(id)); if (deps.length) { affected.set(p.id,deps.map(id => `depends on affected project: ${id}`)); changed = true; } } }
  for (const p of m.projects) if (affected.has(p.id)) p.checks.forEach(id => affected.get(p.id).forEach(reason => add(id,`${p.id}: ${reason}`)));
  const ordered = [], done = new Set(), checks = new Map(m.checks.map(x => [x.id,x]));
  function visit(id) { if (done.has(id)) return; for (const dep of checks.get(id).dependsOn ?? []) { add(dep,`prerequisite of: ${id}`); visit(dep); } done.add(id); ordered.push(id); }
  [...reasons.keys()].forEach(visit);
  return { version:1, files:normalized, affectedProjects:[...affected.keys()], checks:ordered.map(id => ({...checks.get(id),reasons:reasons.get(id)})) };
}
function execute(command, cwd, maxOutput = 65536) {
  return new Promise(resolveResult => {
    const start = performance.now(); let stdout = '', stderr = '', settled = false;
    const child = spawn(command[0],command.slice(1),{cwd,shell:false,stdio:['ignore','pipe','pipe']});
    child.stdout.on('data',x => { stdout = (stdout + x).slice(-maxOutput); }); child.stderr.on('data',x => { stderr = (stderr + x).slice(-maxOutput); });
    const finish = result => { if (!settled) { settled = true; resolveResult({...result,durationMs:Math.round(performance.now()-start),stdout,stderr}); } };
    child.on('error',error => finish({exitCode:null,error:error.message,status:'unavailable'}));
    child.on('close',(exitCode,signal) => finish({exitCode,signal,status:exitCode === 0 ? 'passed' : 'failed'}));
  });
}
export async function runValidation(plan, {cwd = process.cwd(), platform = process.platform} = {}) {
  const results = [];
  for (const check of plan.checks) {
    let result;
    if (check.platforms && !check.platforms.includes(platform)) result = {status:'unavailable',exitCode:null,durationMs:0,error:`Required check unavailable on platform ${platform}`};
    else if ((check.dependsOn ?? []).some(id => results.find(x => x.id === id)?.status !== 'passed')) result = {status:'blocked',exitCode:null,durationMs:0,error:'Prerequisite did not pass'};
    else result = await execute(check.command,cwd);
    results.push({...check,...result});
  }
  return {...plan,checks:results,passed:results.every(x => x.status === 'passed')};
}
export function parseArgs(args) {
  const options = {files:[],run:false};
  for (let i=0;i<args.length;i++) {
    const arg = args[i];
    if (arg === '--run') { if (options.run) throw Error('Duplicate --run'); options.run = true; }
    else if (arg === '--files') { const start = i+1; while (i+1 < args.length && !args[i+1].startsWith('--')) options.files.push(args[++i]); if (i+1 === start) throw Error('Missing --files value'); }
    else if (['--base','--manifest','--report'].includes(arg)) { const key = arg.slice(2); if (options[key] || !args[i+1] || args[i+1].startsWith('-')) throw Error(`Invalid ${arg}`); options[key] = args[++i]; }
    else throw Error(`Unknown argument: ${arg}`);
  }
  if (Boolean(options.base) === Boolean(options.files.length)) throw Error('Specify either --files paths or --base ref');
  return options;
}
export async function main(args = process.argv.slice(2)) {
  const options = parseArgs(args), cwd = process.cwd();
  const manifestPath = options.manifest ? resolve(cwd,options.manifest) : resolve(cwd,'scripts/validation-manifest.json');
  const manifest = JSON.parse(await readFile(manifestPath,'utf8'));
  let files = options.files;
  if (options.base) {
    const mergeBase = await execute(['git','merge-base',options.base,'HEAD'],cwd,Infinity);
    if (mergeBase.status !== 'passed') throw Error(`git merge-base failed: ${mergeBase.stderr || mergeBase.error}`);
    const diff = await execute(['git','diff','--name-only','-z','--no-renames',mergeBase.stdout.trim(),'--'],cwd,Infinity);
    if (diff.status !== 'passed') throw Error(`git diff failed: ${diff.stderr || diff.error}`);
    const untracked = await execute(['git','ls-files','--others','--exclude-standard','-z'],cwd,Infinity);
    if (untracked.status !== 'passed') throw Error(`git ls-files failed: ${untracked.stderr || untracked.error}`);
    files = [...new Set([...diff.stdout.split('\0'),...untracked.stdout.split('\0')].filter(Boolean))];
  }
  const plan = planValidation(manifest,files,{cwd});
  const report = options.run ? await runValidation(plan,{cwd}) : {...plan,mode:'plan'};
  if (options.report) await writeFile(resolve(cwd,options.report),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2));
  return options.run && !report.passed ? 1 : 0;
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main().then(code => {process.exitCode=code;}).catch(error => {console.error(error.message);process.exitCode=1;});
