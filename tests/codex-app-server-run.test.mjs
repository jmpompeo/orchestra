#!/usr/bin/env node

// Protocol-level smoke test for the bundled measured Codex launcher.
// Run after building the Release AgentHarness binary.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { access, mkdtemp, mkdir, readFile, writeFile, chmod, rm, realpath } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, '..');
const dll = path.join(repository, 'src/AgentHarness/bin/Release/net10.0/orchestrate.dll');
const launcher = path.join(repository, 'skills/prep/scripts/codex-app-server-run.mjs');
const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'orchestra-codex-app-server-')));

function command(bin, args, environment = {}) {
  const result = spawnSync(bin, args, {
    encoding: 'utf8', timeout: 15_000, env: { ...process.env, ...environment },
  });
  if (result.error) throw result.error;
  return result;
}

function heartbeat(runDir, ...args) {
  const result = command('dotnet', [dll, 'heartbeat', ...args, '--dir', runDir]);
  assert.equal(result.status, 0, result.stderr);
  return result.stdout;
}

try {
  const runsRoot = path.join(root, '.orchestra', 'runs');
  await mkdir(runsRoot, { recursive: true });
  const bin = path.join(root, 'orchestrate-mock');
  await writeFile(bin, `#!/usr/bin/env node\nconst { spawnSync } = require('node:child_process');\nconst r = spawnSync('dotnet', [${JSON.stringify(dll)}, ...process.argv.slice(2)], { stdio: 'inherit' });\nprocess.exit(r.status ?? 1);\n`);
  await chmod(bin, 0o755);
  const codex = path.join(root, 'codex-mock');
  await writeFile(codex, `#!/usr/bin/env node
const readline = require('node:readline');
const fs = require('node:fs');
process.on('exit', () => { if (process.env.MOCK_EXIT_FILE) fs.writeFileSync(process.env.MOCK_EXIT_FILE, 'exited'); });
process.on('SIGTERM', () => process.exit(0));
let resumed = false;
let turn = null;
const send = value => process.stdout.write(JSON.stringify(value) + '\\n');
readline.createInterface({ input: process.stdin }).on('line', line => {
  const request = JSON.parse(line);
  const { id, method, params } = request;
  if (method === 'initialize') send({ id, result: {} });
  else if (method === 'thread/read') send({ id, result: { thread: { id: 'thread-1', status: { type: 'notLoaded' }, turns: [{ id: 'turn-1', status: 'completed' }] } } });
  else if (method === 'thread/start' || method === 'thread/resume') {
    resumed = method === 'thread/resume';
    send({ id, result: { thread: { id: 'thread-1', sessionId: 'thread-1' } } });
  } else if (method === 'turn/start') {
    if (process.env.MOCK_MODE === 'reject-turn') { send({ id, error: { code: -32000, message: 'rejected' } }); return; }
    if (process.env.MOCK_MODE === 'probe') {
      if (params.sandboxPolicy?.type !== 'readOnly') process.exit(3);
    } else if (params.sandboxPolicy?.type !== 'workspaceWrite') process.exit(4);
    turn = resumed ? 'turn-2' : 'turn-1';
    send({ id, result: { turn: { id: turn, status: 'inProgress' } } });
    send({ method: 'turn/started', params: { threadId: 'thread-1', turn: { id: turn } } });
    if (process.env.MOCK_MODE !== 'no-usage') send({ method: 'thread/tokenUsage/updated', params: { threadId: 'thread-1', turnId: turn, tokenUsage: { total: { totalTokens: resumed ? 14 : 7 } } } });
    if (!['cap', 'hold'].includes(process.env.MOCK_MODE)) send({ method: 'turn/completed', params: { threadId: 'thread-1', turn: { id: turn, status: 'completed' } } });
  } else if (method === 'turn/interrupt') {
    send({ id, result: {} });
    send({ method: 'turn/completed', params: { threadId: 'thread-1', turn: { id: turn, status: 'interrupted' } } });
  }
});
`);
  await chmod(codex, 0o755);

  async function createRun(name, cap) {
    const dir = path.join(runsRoot, name);
    await mkdir(dir);
    heartbeat(dir, 'init', '--run-id', name, '--total-tasks', '1', '--time-budget-minutes', '10', '--stale-after-seconds', '30', ...(cap ? ['--token-budget', String(cap)] : []));
    await writeFile(path.join(dir, 'kickoff-prompt.md'), 'Run the task.');
    return dir;
  }

  const probe = await createRun('probe', null);
  let result = command('node', [launcher, '--project', root, '--run-dir', probe, '--model', 'mock', '--probe', '--codex-bin', codex, '--orchestrate-bin', bin], { MOCK_MODE: 'probe' });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /ORCHESTRA_CODEX_USAGE_PROBE=ok/);
  let state = JSON.parse(heartbeat(probe, 'status'));
  assert.equal(state.tokensUsed, 7);
  assert.equal(state.usageSource, 'codex-app-server');

  const capped = await createRun('capped', 5);
  result = command('node', [launcher, '--project', root, '--run-dir', capped, '--model', 'mock', '--codex-bin', codex, '--orchestrate-bin', bin], { MOCK_MODE: 'cap' });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Token cap reached; Codex turn interrupted/);
  state = JSON.parse(heartbeat(capped, 'status'));
  assert.equal(state.tokensUsed, 7);
  assert.equal(state.tokensOverBudget, true);

  const withoutUsage = await createRun('without-usage', 5);
  result = command('node', [launcher, '--project', root, '--run-dir', withoutUsage, '--model', 'mock', '--codex-bin', codex, '--orchestrate-bin', bin], { MOCK_MODE: 'no-usage' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /No native thread\/tokenUsage\/updated sample arrived/);

  const resumable = await createRun('resumable', null);
  result = command('node', [launcher, '--project', root, '--run-dir', resumable, '--model', 'mock', '--codex-bin', codex, '--orchestrate-bin', bin]);
  assert.equal(result.status, 0, result.stderr);
  result = command('node', [launcher, '--project', root, '--run-dir', resumable, '--model', 'mock', '--resume', '--codex-bin', codex, '--orchestrate-bin', bin]);
  assert.equal(result.status, 0, result.stderr);
  state = JSON.parse(heartbeat(resumable, 'status'));
  assert.equal(state.tokensUsed, 14);
  assert.equal(state.boundSessionId, 'thread-1');

  const durable = await createRun('durable', null);
  const mockExitFile = path.join(root, 'mock-exited');
  result = command('node', [launcher, '--project', root, '--run-dir', durable, '--model', 'mock', '--detach', '--codex-bin', codex, '--orchestrate-bin', bin], { MOCK_MODE: 'hold', MOCK_EXIT_FILE: mockExitFile });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /ORCHESTRA_CODEX_READY=ok/);
  const pid = Number(result.stdout.match(/ORCHESTRA_CODEX_PID=(\d+)/)?.[1]);
  assert.ok(Number.isInteger(pid) && pid > 0);
  try {
    process.kill(pid, 0);
    state = JSON.parse(heartbeat(durable, 'status'));
    assert.equal(state.boundSessionId, 'thread-1');
  } finally {
    process.kill(pid, 'SIGTERM');
  }
  let childExited = false;
  for (let attempt = 0; attempt < 50; attempt++) {
    try { await access(mockExitFile); childExited = true; break; }
    catch { await new Promise(resolve => setTimeout(resolve, 100)); }
  }
  if (!childExited) {
    const logPath = result.stdout.match(/ORCHESTRA_CODEX_LOG=([^\n]+)/)?.[1];
    if (logPath) console.error(await readFile(logPath, 'utf8'));
  }
  assert.equal(childExited, true, 'signal cleanup must close the mock app-server child');

  const rejected = await createRun('rejected', null);
  result = command('node', [launcher, '--project', root, '--run-dir', rejected, '--model', 'mock', '--detach', '--codex-bin', codex, '--orchestrate-bin', bin], { MOCK_MODE: 'reject-turn' });
  assert.equal(result.status, 1, 'a rejected turn must fail detached readiness');
  assert.doesNotMatch(result.stdout, /ORCHESTRA_CODEX_READY=ok/);

  const fast = await createRun('fast', null);
  result = command('node', [launcher, '--project', root, '--run-dir', fast, '--model', 'mock', '--detach', '--codex-bin', codex, '--orchestrate-bin', bin]);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /ORCHESTRA_CODEX_READY=(ok|complete)/);
  let fastTokens = null;
  for (let attempt = 0; attempt < 50; attempt++) {
    fastTokens = JSON.parse(heartbeat(fast, 'status')).tokensUsed;
    if (fastTokens === 7) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.equal(fastTokens, 7, 'fast detached completion must retain native usage');
  console.log('Codex app-server launcher mock tests passed.');
} finally {
  await rm(root, { recursive: true, force: true });
}
