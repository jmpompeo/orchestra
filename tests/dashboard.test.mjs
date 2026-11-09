import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const html = readFileSync(new URL('../skills/prep/templates/dashboard.html', import.meta.url), 'utf8');
const script = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)][0][1];
const context = vm.createContext({});
vm.runInContext(script, context);
const {viewModel, render, setupTheme} = context.OrchestraDashboard;
const now = Date.parse('2026-11-08T12:10:00Z');
const at = minute => `2026-11-08T12:${String(minute).padStart(2, '0')}:00Z`;
const state = overrides => ({version:1,runId:'fixture',totalTasks:2,timeBudgetMinutes:15,staleAfterSeconds:30,startedAt:null,hookState:'waiting',lastHookAt:null,tokensUsed:null,tokenBudget:100,usageSource:null,usageUpdatedAt:null,events:[],...overrides});
const event = (task,status,minute=0,reason='evidence') => ({task,status,at:at(minute),reason});
function documentMock(){
 const nodes = new Map();
 function node(){return {textContent:'',children:[],attributes:{},listeners:{},appendChild(child){this.children.push(child);},replaceChildren(){this.children=[];},setAttribute(key,value){this.attributes[key]=value;},removeAttribute(key){delete this.attributes[key];},addEventListener(key,fn){this.listeners[key]=fn;},set innerHTML(value){throw Error('HTML sink used');}};}
 return {getElementById(id){if(!nodes.has(id))nodes.set(id,node());return nodes.get(id);},createElement:node,documentElement:node()};
}
test('pending budgets do not include prep and zero usage is available',()=>{
 const model=viewModel(state({tokensUsed:0,usageSource:'cursor-sdk',usageUpdatedAt:at(9)}),now);
 assert.equal(model.pending,true);assert.equal(model.elapsed,0);assert.equal(model.remainingSeconds,900);assert.equal(model.timeOver,false);assert.equal(model.liveness,'Unavailable');
 const doc=documentMock();render(doc,model);assert.equal(doc.getElementById('tokens').textContent,'0');assert.match(doc.getElementById('usage').textContent,/cursor-sdk/);assert.equal(doc.getElementById('elapsed').textContent,'Pending');
 render(doc,viewModel(state(),now));assert.equal(doc.getElementById('tokens').textContent,'Unavailable');
});
test('latest task state follows append order, including retry after finish',()=>{
 const model=viewModel(state({startedAt:at(0),events:[event('a','finished',9),event('a','started',1),event('b','blocked',8,'DEC-1 approval'),event('c','failed',7)]}),now);
 assert.equal(model.done,0);assert.equal(model.remaining,2);assert.equal(model.active[0].task,'a');assert.equal(model.blocked[0].reason,'DEC-1 approval');assert.equal(model.failed[0].task,'c');assert.equal(model.events[0].task,'c');assert.equal(model.elapsed,600);
});
test('completion freezes elapsed but reopened work uses current time',()=>{
 const events=[event('a','finished',2),event('b','finished',4)];
 const model=viewModel(state({startedAt:at(0),events}),now);assert.equal(model.complete,true);assert.equal(model.state,'Completed');assert.equal(model.elapsed,240);assert.equal(model.remainingSeconds,660);
 assert.equal(viewModel(state({startedAt:at(0),events:[...events,event('a','started',5)]}),now).elapsed,600);
});
test('only active hooks become stale; absent hooks remain unavailable',()=>{
 for(const hookState of ['waiting','stopped'])assert.equal(viewModel(state({hookState,lastHookAt:at(0)}),now).stale,false);
 assert.equal(viewModel(state({hookState:'active'}),now).liveness,'Unavailable');
 assert.equal(viewModel(state({hookState:'active',lastHookAt:at(0)}),now).stale,true);
 assert.equal(viewModel(state({hookState:'active',lastHookAt:'2026-11-08T12:09:45Z'}),now).liveness,'Active');
});
test('blocked and failed state remain distinct and token/time limits are visible',()=>{
 assert.equal(viewModel(state({startedAt:at(0),events:[event('a','blocked')]}),now).state,'Blocked');
 assert.equal(viewModel(state({startedAt:at(0),events:[event('a','failed')]}),now).state,'Needs attention');
 const doc=documentMock();render(doc,viewModel(state({startedAt:at(0),timeBudgetMinutes:1,tokensUsed:100}),now));assert.match(doc.getElementById('time').textContent,/Budget reached/);assert.match(doc.getElementById('token-budget').textContent,/Budget reached/);
});
test('event and run text use text nodes, with no HTML execution sink',()=>{
 const unsafe='<img src=x onerror=alert(1)>';
 const doc=documentMock();render(doc,viewModel(state({runId:unsafe,events:[event(unsafe,'blocked',0,unsafe)]}),now));
 assert.equal(doc.getElementById('run').textContent,unsafe);assert.match(doc.getElementById('blocked').children[0].textContent,/<img/);assert.equal(doc.getElementById('blocked').children[0].children.length,0);
 assert.doesNotMatch(script,/innerHTML|fetch\(|XMLHttpRequest/);assert.match(html,/<script src="heartbeat.js"><\/script>/);assert.match(html,/<meta http-equiv="refresh" content="15">/);
});
test('theme persists all choices and system restores OS preference',()=>{
 const doc=documentMock();const saved=new Map([['orchestra-theme','dark']]);const storage={getItem:key=>saved.get(key),setItem:(key,value)=>saved.set(key,value)};
 setupTheme(doc,()=>storage);assert.equal(doc.documentElement.attributes['data-theme'],'dark');const select=doc.getElementById('theme');
 for(const choice of ['light','dark','system']){select.value=choice;select.listeners.change();assert.equal(saved.get('orchestra-theme'),choice);assert.equal(doc.documentElement.attributes['data-theme'],choice==='system'?undefined:choice);}
});
test('file URL storage getter and write failures still permit theme selection',()=>{
 for(const storage of [()=>{throw Error('denied');},()=>({getItem:()=>null,setItem:()=>{throw Error('denied');}})]){
 const doc=documentMock();setupTheme(doc,storage);const select=doc.getElementById('theme');assert.equal(select.value,'system');select.value='dark';assert.doesNotThrow(()=>select.listeners.change());assert.equal(doc.documentElement.attributes['data-theme'],'dark');}
});
test('missing or unsupported state gives a clear error and empty data boots safely',()=>{
 assert.throws(()=>viewModel(undefined),/unavailable/);assert.throws(()=>viewModel(state({version:2})),/unsupported/);
 const document=documentMock();vm.runInNewContext(script,{document,window:{}});assert.equal(document.getElementById('error').hidden,false);assert.match(document.getElementById('error').textContent,/heartbeat.js/);
});
