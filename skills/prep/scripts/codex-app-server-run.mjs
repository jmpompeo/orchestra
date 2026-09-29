#!/usr/bin/env node

// One native Codex app-server session with run-scoped, cumulative usage.
// Protocol: https://learn.chatgpt.com/docs/app-server
// Usage schema: https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/json/v2/ThreadTokenUsageUpdatedNotification.json

import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { open, readFile, realpath, stat } from 'node:fs/promises';
import path from 'node:path';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const REQUEST_TIMEOUT_MS = 30_000;
const SHUTDOWN_TIMEOUT_MS = 2_000;

function usage() {
  return 'Usage: node codex-app-server-run.mjs --project ABSOLUTE_PATH --run-dir ABSOLUTE_PATH --model MODEL [--network-access true|false] [--probe | --resume] [--detach] [--codex-bin PATH] [--orchestrate-bin PATH]';
}

function parseArgs(argv) {
  const options = {};
  const allowed = new Set(['project', 'run-dir', 'model', 'network-access', 'codex-bin', 'orchestrate-bin']);
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--probe' || arg === '--resume' || arg === '--detach') {
      const key = arg.slice(2);
      if (options[key]) throw new Error(`Duplicate ${arg}`);
      options[key] = true;
      continue;
    }
    if (!arg.startsWith('--') || !allowed.has(arg.slice(2))) throw new Error(`Unknown argument ${arg}\n${usage()}`);
    const key = arg.slice(2);
    if (options[key] !== undefined || i + 1 >= argv.length || argv[i + 1].startsWith('--'))
      throw new Error(`Missing or repeated ${arg}`);
    options[key] = argv[++i];
  }
  if (!options.project || !options['run-dir'] || !options.model || (options.probe && (options.resume || options.detach)))
    throw new Error(usage());
  if (!path.isAbsolute(options.project) || !path.isAbsolute(options['run-dir']))
    throw new Error('--project and --run-dir must be absolute paths');
  if (options['network-access'] !== undefined && !['true', 'false'].includes(options['network-access']))
    throw new Error('--network-access must be true or false');
  return options;
}

function runCommand(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'], shell: false });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => child.kill('SIGTERM'), 10_000);
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('error', reject);
    child.on('close', code => {
      clearTimeout(timer);
      if (code !== 0) reject(new Error(`${command} ${args.slice(0, 2).join(' ')} failed (${code}): ${stderr.trim() || stdout.trim()}`));
      else resolve(stdout);
    });
  });
}

class AppServer {
  constructor(binary, cwd) {
    this.child = spawn(binary, ['app-server'], { cwd, stdio: ['pipe', 'pipe', 'inherit'], shell: false });
    this.pending = new Map();
    this.nextId = 1;
    this.closed = false;
    this.onNotification = () => {};
    this.onUnexpectedRequest = () => {};
    const lines = readline.createInterface({ input: this.child.stdout, crlfDelay: Infinity });
    lines.on('line', line => this.handleLine(line));
    this.child.stdin.on('error', error => this.close(error));
    this.child.on('error', error => this.close(error));
    this.child.on('close', code => this.close(new Error(`codex app-server exited (${code})`)));
  }

  handleLine(line) {
    let message;
    try { message = JSON.parse(line); }
    catch { this.close(new Error('Codex app-server emitted invalid JSON')); return; }
    if (!message || typeof message !== 'object') return;
    if (message.id !== undefined && !message.method) {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      clearTimeout(pending.timer);
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(`Codex ${pending.method} was rejected (code ${message.error.code ?? 'unknown'})`));
      else {
        pending.onAccepted?.();
        pending.resolve(message.result);
      }
      return;
    }
    if (message.method && message.id !== undefined) {
      this.onUnexpectedRequest(message);
      return;
    }
    if (message.method) this.onNotification(message);
  }

  send(message) {
    if (this.closed) throw new Error('Codex app-server is closed');
    this.child.stdin.write(`${JSON.stringify(message)}\n`);
  }

  request(method, params, timeoutMs = REQUEST_TIMEOUT_MS, onAccepted = null) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Codex ${method} timed out`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer, method, onAccepted });
      try { this.send({ id, method, params }); }
      catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }

  close(error) {
    if (this.closed) return;
    this.closed = true;
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pending.clear();
    this.onClosed?.(error);
  }

  async shutdown() {
    if (this.child.exitCode !== null || this.child.signalCode !== null) return;
    const closed = new Promise(resolve => this.child.once('close', resolve));
    this.child.stdin.end();
    this.child.kill('SIGTERM');
    let timer;
    await Promise.race([
      closed,
      new Promise(resolve => { timer = setTimeout(resolve, SHUTDOWN_TIMEOUT_MS); }),
    ]);
    clearTimeout(timer);
    if (this.child.exitCode === null && this.child.signalCode === null) this.child.kill('SIGKILL');
  }
}

function validId(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 128 && !/[\x00-\x1f\x7f]/.test(value);
}

function terminalTurn(turn) {
  return turn && ['completed', 'failed', 'interrupted'].includes(turn.status);
}

async function runPaths(options) {
  const project = await realpath(options.project);
  const runDir = await realpath(options['run-dir']);
  if (!(await stat(project)).isDirectory() || !(await stat(runDir)).isDirectory())
    throw new Error('Project and run directory must exist');
  const runsRoot = await realpath(path.join(project, '.orchestra', 'runs'));
  if (path.dirname(runDir) !== runsRoot)
    throw new Error('--run-dir must be an immediate child of <project>/.orchestra/runs');
  return { project, runDir };
}

async function launchDetached(options) {
  const { project, runDir } = await runPaths(options);
  const logPath = path.join(runDir, `codex-app-server-${randomUUID()}.log`);
  const log = await open(logPath, 'wx', 0o600);
  let child;
  try {
    const childArgs = process.argv.slice(2).filter(arg => arg !== '--detach');
    child = spawn(process.execPath, [fileURLToPath(import.meta.url), ...childArgs], {
      cwd: project, detached: true, stdio: ['ignore', log.fd, log.fd], shell: false,
    });
    await new Promise((resolve, reject) => {
      child.once('spawn', resolve);
      child.once('error', reject);
    });
    child.unref();
  } finally {
    await log.close();
  }
  console.log(`ORCHESTRA_CODEX_PID=${child.pid}`);
  console.log(`ORCHESTRA_CODEX_LOG=${logPath}`);
  for (let attempt = 0; attempt < 300; attempt++) {
    const output = await readFile(logPath, 'utf8');
    if (child.exitCode !== null || child.signalCode !== null) {
      if (child.exitCode === 0 && (output.includes('Codex kickoff completed;') || output.includes('Token cap reached;'))) {
        console.log('ORCHESTRA_CODEX_READY=complete');
        return;
      }
      throw new Error(`Detached Codex launcher exited before readiness; inspect ${logPath}`);
    }
    if (output.includes('Codex app-server turn started ')) {
      console.log('ORCHESTRA_CODEX_READY=ok');
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  console.log('ORCHESTRA_CODEX_READY=pending');
  console.log('Detached process is still starting; verify its PID, heartbeat binding, and log before ending prep. Do not launch a second process.');
}

async function main(options) {
  const { project, runDir } = await runPaths(options);
  const orchestrate = options['orchestrate-bin'] || 'orchestrate';
  const heartbeat = async (verb, ...args) => runCommand(orchestrate, ['heartbeat', verb, '--dir', runDir, ...args], project);
  const status = async () => JSON.parse(await heartbeat('status'));
  const initial = await status();
  if (initial.stopRecommended) throw new Error('Prepared run already has a stop condition');
  if (options.resume && !validId(initial.boundSessionId)) throw new Error('--resume requires an existing bound native session');
  if (!options.resume && initial.boundSessionId) throw new Error('Run is already bound; use --resume after the prior turn ends');
  const prompt = options.probe
    ? 'Reply with exactly OK. Do not call tools, inspect files, or change anything.'
    : await readFile(path.join(runDir, 'kickoff-prompt.md'), 'utf8');
  if (!prompt.trim()) throw new Error('Kickoff prompt is empty');

  const server = new AppServer(options['codex-bin'] || 'codex', project);
  let threadId = null;
  let sessionId = null;
  let turnId = null;
  let completed = null;
  let turnResolve;
  let turnReject;
  let receivedUsage = false;
  let usageResolve;
  const firstUsage = new Promise(resolve => { usageResolve = resolve; });
  let stoppedForCap = false;
  let interrupted = false;
  let capInterruptAwaitingAck = false;
  let capCompletionBeforeAck = false;
  let protocolError = null;
  let rawCompletedTurnId = null;
  let rawCompleteResolve;
  const rawCompletion = new Promise(resolve => { rawCompleteResolve = resolve; });
  const turnDone = new Promise((resolve, reject) => { turnResolve = resolve; turnReject = reject; });
  // A rejection may precede the caller reaching its await while setup fails.
  turnDone.catch(() => {});
  let eventQueue = Promise.resolve();
  let cleanupPromise = null;

  function cleanup() {
    if (!cleanupPromise) cleanupPromise = (async () => {
      if (turnId && !completed && !server.closed) {
        try { await server.request('turn/interrupt', { threadId, turnId }, 5_000); }
        catch { /* Closing the transport is the final fail-closed fallback. */ }
      }
      await server.shutdown();
    })();
    return cleanupPromise;
  }

  function fail(error) {
    if (protocolError) return;
    protocolError = error instanceof Error ? error : new Error(String(error));
    turnReject(protocolError);
  }

  const onSignal = signal => {
    process.exitCode = 1;
    fail(new Error(`Received ${signal}; stopping Codex turn`));
    void cleanup().catch(fail);
  };
  process.once('SIGTERM', onSignal);
  process.once('SIGINT', onSignal);

  server.onClosed = error => fail(error);
  server.onUnexpectedRequest = message => {
    fail(new Error(`Unexpected Codex server request ${message.method}; unattended launch cannot answer prompts`));
    server.child.kill('SIGTERM');
  };
  server.onNotification = message => {
    if (message.method === 'turn/completed') {
      if (capInterruptAwaitingAck && message.params?.turn?.id === turnId)
        capCompletionBeforeAck = true;
      rawCompletedTurnId = message.params?.turn?.id;
      rawCompleteResolve();
    }
    eventQueue = eventQueue.then(async () => {
      if (protocolError) return;
      const { method, params } = message;
      if (method === 'thread/tokenUsage/updated') {
        if (!threadId || !sessionId || params?.threadId !== threadId ||
            (params.turnId && turnId && params.turnId !== turnId))
          throw new Error('Codex usage arrived for a different or unknown thread/turn');
        const total = params?.tokenUsage?.total?.totalTokens;
        if (!Number.isSafeInteger(total) || total < 0) throw new Error('Codex usage has no valid cumulative totalTokens');
        await heartbeat('usage', '--source', 'codex-app-server', '--session-id', sessionId, '--tokens-used', String(total));
        receivedUsage = true;
        usageResolve();
        const current = await status();
        if (current.boundSessionId !== sessionId || current.usageSource !== 'codex-app-server')
          throw new Error('Heartbeat session or usage source changed during Codex run');
        if (current.tokensOverBudget && !interrupted && !completed && rawCompletedTurnId !== turnId) {
          interrupted = true;
          if (!turnId) throw new Error('Token cap reached before Codex identified the turn');
          capInterruptAwaitingAck = true;
          try {
            await server.request('turn/interrupt', { threadId, turnId }, REQUEST_TIMEOUT_MS,
              () => { capInterruptAwaitingAck = false; });
            // A terminal event before the ACK may be an unrelated interruption.
            stoppedForCap = !capCompletionBeforeAck;
          } catch (error) {
            // A completion can race the interrupt request. Its queued handler
            // will validate the thread and turn before this run succeeds.
            if (rawCompletedTurnId !== turnId) {
              let timer;
              await Promise.race([rawCompletion, new Promise(resolve => { timer = setTimeout(resolve, 2_000); })]);
              clearTimeout(timer);
              if (rawCompletedTurnId !== turnId) throw error;
            }
          } finally {
            capInterruptAwaitingAck = false;
          }
        }
      } else if (method === 'turn/started') {
        if (params?.threadId && params.threadId !== threadId) throw new Error('Codex started a turn in another thread');
        const id = params?.turn?.id;
        if (!validId(id) || (turnId && turnId !== id)) throw new Error('Codex returned an unexpected turn ID');
        turnId = id;
      } else if (method === 'turn/completed') {
        if (params?.threadId && params.threadId !== threadId) throw new Error('Codex completed another thread');
        const turn = params?.turn;
        if (!validId(turn?.id) || (turnId && turn.id !== turnId) || !terminalTurn(turn))
          throw new Error('Codex returned invalid turn completion');
        turnId = turn.id;
        completed = turn;
        turnResolve(turn);
      } else if (options.probe && method === 'item/started') {
        const type = params?.item?.type;
        if (type && !['agentMessage', 'userMessage', 'reasoning'].includes(type))
          throw new Error(`Probe unexpectedly started a ${type} item`);
      }
    }).catch(fail);
  };

  try {
    await server.request('initialize', {
      clientInfo: { name: 'orchestra', title: 'Orchestra measured session', version: '1.0.0' },
    });
    server.send({ method: 'initialized', params: {} });
    let thread;
    if (options.resume) {
      const old = initial.boundSessionId;
      const read = await server.request('thread/read', { threadId: old, includeTurns: true });
      const existing = read?.thread;
      if (existing?.id !== old || existing.status?.type !== 'notLoaded' && existing.status?.type !== 'idle')
        throw new Error('Bound Codex thread is missing, active, or not safely resumable');
      if (!Array.isArray(existing.turns) || existing.turns.length === 0 ||
          !terminalTurn(existing.turns.at(-1)))
        throw new Error('Prior Codex turn has no verified terminal status');
      const resumed = await server.request('thread/resume', {
        threadId: old, cwd: project, model: options.model, approvalPolicy: 'never', sandbox: 'workspaceWrite',
      });
      thread = resumed?.thread;
      if (thread?.id !== old || thread.sessionId !== old)
        throw new Error('Resumed Codex thread/session ID differs from the bound run');
    } else {
      const started = await server.request('thread/start', {
        cwd: project, model: options.model, approvalPolicy: 'never', sandbox: 'workspaceWrite',
        serviceName: 'orchestra',
      });
      thread = started?.thread;
      if (!validId(thread?.id) || thread.sessionId !== thread.id)
        throw new Error('Codex did not return a valid root thread and session ID');
    }
    threadId = thread.id;
    sessionId = thread.sessionId;
    if (!options.resume) await heartbeat('bind', '--session-id', sessionId);
    const afterBind = await status();
    if (afterBind.boundSessionId !== sessionId) throw new Error('Heartbeat did not bind the Codex native session');
    const startedTurn = await server.request('turn/start', {
      threadId, input: [{ type: 'text', text: prompt }], cwd: project,
      approvalPolicy: 'never',
      sandboxPolicy: options.probe
        ? { type: 'readOnly' }
        : { type: 'workspaceWrite', writableRoots: [project], networkAccess: options['network-access'] === 'true' },
      model: options.model,
    });
    const id = startedTurn?.turn?.id;
    if (!validId(id) || (turnId && turnId !== id)) throw new Error('Codex did not return a valid turn ID');
    turnId = id;
    console.log(`Codex app-server turn started ${turnId}; thread ${threadId}; session ${sessionId}; ${options.probe ? 'probe' : 'kickoff'}`);
    const finalTurn = await turnDone;
    if (!receivedUsage) {
      let graceTimer;
      await Promise.race([firstUsage, new Promise(resolve => { graceTimer = setTimeout(resolve, 3_000); })]);
      clearTimeout(graceTimer);
    }
    await eventQueue;
    if (protocolError) throw protocolError;
    if (!receivedUsage) throw new Error('No native thread/tokenUsage/updated sample arrived');
    const finalStatus = await status();
    if (finalStatus.boundSessionId !== sessionId || finalStatus.usageSource !== 'codex-app-server')
      throw new Error('Native Codex usage could not be confirmed in the run');
    if (options.probe && finalTurn.status !== 'completed') throw new Error(`Codex probe ${finalTurn.status}`);
    if (finalTurn.status !== 'completed' && !(finalTurn.status === 'interrupted' && stoppedForCap))
      throw new Error(`Codex turn ${finalTurn.status}`);
    if (stoppedForCap || finalStatus.tokensOverBudget) {
      console.log(`Token cap reached; Codex turn ${finalTurn.status}. Native usage: ${finalStatus.tokensUsed}`);
      return;
    }
    console.log(`${options.probe ? 'ORCHESTRA_CODEX_USAGE_PROBE=ok' : 'Codex kickoff completed'}; native tokens ${finalStatus.tokensUsed}`);
  } finally {
    await cleanup();
    process.removeListener('SIGTERM', onSignal);
    process.removeListener('SIGINT', onSignal);
  }
}

async function entry() {
  const options = parseArgs(process.argv.slice(2));
  return options.detach ? launchDetached(options) : main(options);
}

entry().catch(error => {
  console.error(`Codex measured run failed: ${error.message}`);
  process.exitCode = 1;
});
