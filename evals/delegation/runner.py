#!/usr/bin/env python3
"""Portable, paired native Codex benchmark. Plan only unless --run is explicit.

Raw native evidence stays in private output directories. result/summary JSON has
only counters, checks and source hashes; neither transcripts nor auth are copied.
Maintainability is deliberately manual: --reviews accepts {trial_id: {criteria:
[0..2,0..2,0..2,0..2,0..2], reviewer:"...", rationale:"..."}}. Freeze the rubric
before the first --run.
"""
import argparse
import asyncio
import hashlib
import importlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import statistics
import subprocess
import sys
import time

VERSION = 1
ROLES = ('explorer', 'worker', 'verifier', 'reviewer')
PREFIX = '''This is an isolated disposable evaluation repository. The user explicitly authorizes the fixture setup commit as the base and attached feat/delegation-eval as the working branch. Inspect Git status and HEAD before editing. Task changes, builds, tests and justified named configured-role subagents are authorized. Use local skills/<skill-name>/SKILL.md and their relative references as appropriate. The fixture setup commit already exists; do not create task commits, push, publish, or modify real user configuration. Keep changes within the requested task. Report exact validation results and blockers. Perform this task:\n\n'''
# Anchors are frozen in manifest.json before execution; manual scores need reasons.
RUBRIC = {
    'contracts_names_assumptions': 'Contracts/names/no hidden assumptions',
    'cohesive_abstraction': 'Cohesive abstraction',
    'errors_state_resources': 'Explicit errors/state/resources',
    'contained_change': 'Contained change/no duplication/needless complexity',
    'behavior_focused_tests': 'Readable behavior-focused tests',
    'anchors': {'0': 'Material issue or missing evidence', '1': 'Localized ambiguity', '2': 'No material issue'},
}
TOKEN_KEYS = {
    'total_tokens': ('totalTokens', 'total_tokens'),
    'input_tokens': ('inputTokens', 'input_tokens'),
    'cached_input_tokens': ('cachedInputTokens', 'cached_input_tokens'),
    'cache_write_input_tokens': ('cacheWriteInputTokens', 'cache_write_input_tokens'),
    'output_tokens': ('outputTokens', 'output_tokens'),
    'reasoning_output_tokens': ('reasoningOutputTokens', 'reasoning_output_tokens'),
}


def dump(path, value):
    path = Path(path)
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')
    temp.replace(path)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def file_map(root, paths=None):
    root = Path(root)
    files = paths if paths is not None else root.rglob('*')
    result = {}
    for path in sorted(files):
        path = Path(path)
        if path.is_symlink():
            raise ValueError('Instruction symlink refused: ' + str(path))
        if path.is_file():
            result[str(path.relative_to(root))] = sha(path)
    return result


def assets(root):
    root = Path(root)
    files = [root / 'global/codex/AGENTS.md', root / 'models.conf']
    files += list((root / 'global/codex/agents').glob('*.toml.tmpl'))
    files += list((root / 'skills').rglob('*'))
    result = file_map(root, files)
    if not (root / 'global/codex/AGENTS.md').is_file() or not (root / 'models.conf').is_file():
        raise ValueError('Missing canonical instructions or models.conf')
    for role in ROLES:
        if f'global/codex/agents/workflow_{role}.toml.tmpl' not in result:
            raise ValueError('Missing role template: ' + role)
    return result


def read_models(root):
    result = {}
    for line in (Path(root) / 'models.conf').read_text().splitlines():
        if line.strip() and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            result[key.strip()] = value.strip()
    if not result.get('CODEX_ORCHESTRATOR_MODEL'):
        raise ValueError('Missing root model assignment')
    return result


def make_manifest(baseline, candidate, corpus, repetitions, screen, codex, timeout=900, grading_timeout=300):
    maps = {arm: assets(path) for arm, path in [('baseline', baseline), ('candidate', candidate)]}
    shared = ['models.conf'] + [f'global/codex/agents/workflow_{r}.toml.tmpl' for r in ROLES]
    for name in shared:
        if maps['baseline'][name] != maps['candidate'][name]:
            raise ValueError('Role assignment/template differs between arms: ' + name)
    binary = shutil.which(codex)
    return {
        'version': VERSION, 'baseline': str(Path(baseline).resolve()),
        'candidate': str(Path(candidate).resolve()), 'source_assets': maps,
        'models': read_models(baseline), 'root_effort': 'medium', 'prefix': PREFIX,
        'corpus_sha256': sha(corpus.__file__), 'runner_sha256': sha(__file__),
        'cases': corpus.CASES, 'repetitions': repetitions, 'screen': screen,
        'timeout_seconds': timeout, 'grading_timeout_seconds': grading_timeout,
        'binary': str(Path(binary).resolve()) if binary else codex,
        'binary_sha256': sha(binary) if binary else None,
        'rubric': RUBRIC, 'targets': {'quality_ratio': 1.10, 'throughput_ratio': 1.10, 'token_ratio_max': 1.10},
    }


def freeze_manifest(path, manifest):
    path = Path(path)
    if path.exists():
        if json.loads(path.read_text()) != manifest:
            raise ValueError('Frozen manifest drift: refusing resume; use a new output directory')
    else:
        dump(path, manifest)


def git(work, *args):
    env = {k: v for k, v in os.environ.items() if not k.startswith('GIT_')}
    env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
    return subprocess.run(['git', '-C', str(work), *args], check=True, env=env,
                          text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30).stdout


def prepare_trial(run, source, shared_source, case, corpus, auth_source):
    run.mkdir(mode=0o700, parents=True)
    home, work = run / 'home', run / 'workspace'
    home.mkdir(mode=0o700)
    work.mkdir()
    corpus.prepare_case(case, work)
    if (work / '.git').exists():
        raise ValueError('Corpus must supply fresh files, without a Git repository')
    # Overlay instructions only. Both arms' code is constructed by the same corpus.
    shutil.copytree(source / 'skills', work / 'skills', dirs_exist_ok=True)
    shutil.copytree(source / 'skills', home / 'skills')
    shutil.copy2(source / 'global/codex/AGENTS.md', home / 'AGENTS.md')
    shutil.copy2(shared_source / 'models.conf', work / 'models.conf')
    roles = home / 'roles'
    roles.mkdir()
    models = read_models(shared_source)
    config = ['model = ' + json.dumps(models['CODEX_ORCHESTRATOR_MODEL']),
              'model_reasoning_effort = "medium"', 'approval_policy = "never"',
              'sandbox_mode = "workspace-write"', '[features]', 'multi_agent = true',
              '[agents]', 'max_threads = 4']
    for role in ROLES:
        name = 'workflow_' + role
        text = (shared_source / f'global/codex/agents/{name}.toml.tmpl').read_text()
        for key, value in models.items():
            text = text.replace('@@' + key + '@@', value)
        if '@@' in text:
            raise ValueError('Unrendered role placeholder')
        path = roles / (name + '.toml')
        path.write_text(text)
        config += [f'[agents.{name}]', f'description = "Configured {role} role for bounded assigned work."',
                   'config_file = ' + json.dumps(str(path))]
    # The native server records trust for its canonical disposable workspace.
    # Seed that exact owned record before hashing; later config edits still fail.
    config += ['', '[projects.' + json.dumps(str(work.resolve())) + ']', 'trust_level = "trusted"']
    (home / 'config.toml').write_text('\n'.join(config) + '\n')
    # Only the fixture workspace is staged, never its sibling home or auth link.
    git(work, 'init', '-q', '-b', 'feat/delegation-eval')
    git(work, 'add', '--all', '--', '.')
    git(work, '-c', 'user.name=Benchmark fixture', '-c', 'user.email=fixture@example.invalid',
        '-c', 'commit.gpgsign=false', 'commit', '-qm', 'Evaluation fixture setup')
    if git(work, 'status', '--porcelain') or git(work, 'branch', '--show-current').strip() != 'feat/delegation-eval':
        raise ValueError('Fixture Git preflight failed')
    inventory = {'home': file_map(home, [home / 'AGENTS.md', home / 'config.toml', *roles.glob('*'), *(home / 'skills').rglob('*')]),
                 'workspace': file_map(work, [work / 'models.conf', *(work / 'skills').rglob('*')]),
                 'setup_head': git(work, 'rev-parse', 'HEAD').strip()}
    code = file_map(work, [p for p in work.rglob('*') if not any(part in ('.git', 'skills') for part in p.relative_to(work).parts) and p.name != 'models.conf'])
    for prior in run.parent.glob(f'{case}-*/fixture-code.json'):
        if json.loads(prior.read_text()) != code:
            raise ValueError('Fixture code differs across paired attempts')
    dump(run / 'fixture-code.json', code)
    dump(run / 'installed-instructions.json', inventory)
    (home / 'auth.json').symlink_to(auth_source)
    return home, work, inventory


def normalize_usage(value):
    if not isinstance(value, dict):
        return None
    result = {}
    for target, aliases in TOKEN_KEYS.items():
        for key in aliases:
            if key in value and isinstance(value[key], int) and not isinstance(value[key], bool) and value[key] >= 0:
                result[target] = value[key]
                break
    return result if 'total_tokens' in result else None


def native_logs(home, turn_ids=None):
    """Keep each file's first identity and last cumulative counter.

    When supplied, native event turn IDs authoritatively select execution
    contexts; later inherited metadata and parent contexts cannot change identity.
    """
    found = {}
    for path in sorted(Path(home).rglob('*.jsonl')):
        tid, last, contexts, role, parent, catalog_roots = None, None, [], None, None, []
        with path.open(errors='replace') as stream:
            lines = list(stream)
        for line in lines:
            try:
                value = json.loads(line)
            except ValueError:
                continue
            payload = value.get('payload', {})
            if value.get('type') == 'session_meta' and tid is None:
                tid = payload.get('id') or payload.get('session_id')
                role = payload.get('agent_role') or payload.get('agentRole')
                source = payload.get('source')
                if isinstance(source, dict):
                    sub = source.get('subagent', source.get('subAgent', {}))
                    if isinstance(sub, dict):
                        spawn = sub.get('thread_spawn', sub.get('threadSpawn', sub))
                        if isinstance(spawn, dict):
                            parent = spawn.get('parent_thread_id') or spawn.get('parentThreadId')
                            role = role or spawn.get('agent_role') or spawn.get('agentRole')
            if value.get('type') == 'world_state':
                body = payload.get('state', {}).get('host_skills', {}).get('body', '')
                catalog_roots += re.findall(r'`(/[^`\n]+)`', body)
            if value.get('type') == 'turn_context':
                turn_id = payload.get('turn_id') or payload.get('turnId')
                if turn_ids is None or turn_id in turn_ids.get(tid, set()):
                    effort = payload.get('effort') or payload.get('reasoning_effort')
                    contexts.append({'model': payload.get('model'), 'effort': effort, 'turn_id': turn_id})
            if value.get('type') == 'event_msg' and payload.get('type') == 'token_count':
                info = payload.get('info') or {}
                count = normalize_usage(info.get('total_token_usage'))
                if count is not None:
                    last = count
        if tid:
            record = {'usage': last, 'runtime_contexts': contexts, 'role': role, 'parent': parent,
                      'log_paths': [str(path)], 'catalog_roots': catalog_roots}
            if tid in found:
                old = found[tid]
                # Duplicated files are snapshots, never additional token charges.
                record['log_paths'] += old['log_paths']
                if old['usage'] and (not last or old['usage']['total_tokens'] > last['total_tokens']):
                    record['usage'] = old['usage']
                record['runtime_contexts'] += old['runtime_contexts']
                record['catalog_roots'] += old['catalog_roots']
            found[tid] = record
    return found


def capture_turn_id(turn_ids, event):
    if event.get('method') not in ('turn/started', 'turn/completed'):
        return
    params = event.get('params', {})
    tid, turn_id = params.get('threadId'), params.get('turn', {}).get('id')
    if tid and turn_id:
        turn_ids.setdefault(tid, set()).add(turn_id)


def reanalyse_native_usage(run):
    """Read-only reconstruction from retained events and rollouts; no result writes."""
    run = Path(run)
    result = json.loads((run / 'result.json').read_text())
    threads, usage, turn_ids = {}, {}, {}
    with (run / 'events.jsonl').open() as stream:
        for line in stream:
            event = json.loads(line)
            capture_turn_id(turn_ids, event)
            params = event.get('params', {})
            if event.get('method') == 'thread/started':
                thread = params['thread']
                threads[thread['id']] = thread
            elif event.get('method') == 'thread/tokenUsage/updated':
                usage[params['threadId']] = params['tokenUsage']['total']
    logs = native_logs(run / 'home', turn_ids)
    records, complete, total, issues = reconcile_usage(threads, usage, logs, result['root'])
    return {'threads': records, 'usage_complete': complete, 'total_tokens': total,
            'native_usage_errors': issues, 'thread_turn_ids': {k: sorted(v) for k, v in turn_ids.items()}}


def reconcile_usage(threads, events, logs, root):
    ids = set(threads) | set(events) | set(logs)
    records, issues = {}, []
    if not root or root not in ids:
        issues.append('Missing root native usage')
    for tid in sorted(ids):
        event, log = normalize_usage(events.get(tid)), logs.get(tid, {})
        rollout = log.get('usage')
        if event is None or rollout is None:
            issues.append('Incomplete event/rollout usage: ' + tid)
        elif any(event.get(key) != rollout.get(key) for key in set(event) | set(rollout)):
            issues.append('Event/rollout usage mismatch: ' + tid)
        contexts = log.get('runtime_contexts', [])
        if not contexts or any(not c.get('model') or not c.get('effort') for c in contexts):
            issues.append('Missing runtime model/effort: ' + tid)
        thread = threads.get(tid, {})
        role = thread.get('agentRole') or thread.get('agent_role') or log.get('role')
        if tid != root and not role:
            issues.append('Missing descendant role: ' + tid)
        records[tid] = {'role': 'orchestrator' if tid == root else role, 'event': event,
                        'rollout': rollout, 'runtime_contexts': contexts, 'log_paths': log.get('log_paths', []),
                        'total_tokens': event.get('total_tokens') if event else None}
    complete = not issues and bool(records)
    total = sum(r['total_tokens'] for r in records.values()) if complete else None
    return records, complete, total, issues


def source_paths(value):
    result = []
    if isinstance(value, str):
        result.append(value)
    elif isinstance(value, list):
        for item in value:
            result += source_paths(item)
    elif isinstance(value, dict):
        for key in ('path', 'file', 'source', 'filePath'):
            if key in value:
                result += source_paths(value[key])
    return result


def instruction_check(info, home, work, inventory):
    sources = source_paths(info.get('instructionSources'))
    issues, recorded = [], []
    if not sources:
        issues.append('Native instruction source evidence missing')
    for source in sources:
        path = Path(source).resolve()
        allowed = path.is_relative_to(home.resolve()) or path.is_relative_to(work.resolve())
        if not allowed or not path.is_file():
            issues.append('Unexpected instruction source')
        else:
            recorded.append({'path': str(path), 'sha256': sha(path)})
    for location, root in [('home', home), ('workspace', work)]:
        for rel, expected in inventory[location].items():
            path = root / rel
            if path.is_symlink() or not path.is_file() or sha(path) != expected:
                issues.append('Installed instruction changed: ' + location + '/' + rel)
    return recorded, issues


def process_group_options():
    return {'creationflags': subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == 'nt' else {'start_new_session': True}


async def stop_process_group(proc, force=False):
    if os.name == 'nt':
        # taskkill is a Windows system utility; /T includes child processes.
        args = ['taskkill', '/PID', str(proc.pid), '/T'] + (['/F'] if force else [])
        killer = await asyncio.create_subprocess_exec(*args, stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
        await asyncio.wait_for(killer.wait(), 5)
    else:
        try:
            os.killpg(proc.pid, signal.SIGKILL if force else signal.SIGTERM)
        except ProcessLookupError:
            pass


class AppServer:
    def __init__(self, binary, home, work, run, timeout):
        self.binary, self.home, self.work, self.run, self.timeout = binary, home, work, run, timeout
        self.pending, self.threads, self.usage, self.turns, self.turn_ids = {}, {}, {}, {}, {}
        self.done = asyncio.Event()
        self.root, self.count, self.proc = None, 0, None
        self.errors, self.infos = [], []

    async def start(self):
        env = os.environ.copy()
        env.update(CODEX_HOME=str(self.home), HOME=str(self.home), USERPROFILE=str(self.home),
                   XDG_CONFIG_HOME=str(self.home / '.config'), XDG_DATA_HOME=str(self.home / '.local/share'),
                   PYTHONDONTWRITEBYTECODE='1')
        # Do not inherit config/skill environment overrides from the invoking app.
        for key in list(env):
            if key.startswith(('CODEX_', 'AGENT_HARNESS_', 'ORCHESTRA_')) and key != 'CODEX_HOME':
                del env[key]
        self.err = (self.run / 'stderr.log').open('w')
        self.events = (self.run / 'events.jsonl').open('w')
        self.proc = await asyncio.create_subprocess_exec(self.binary, 'app-server', env=env, cwd=self.work,
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=self.err,
            limit=32 * 1024 * 1024, **process_group_options())
        self.reader = asyncio.create_task(self.read())
        await self.rpc('initialize', {'clientInfo': {'name': 'orchestra_paired_eval', 'version': str(VERSION)},
                                    'capabilities': {'experimentalApi': True}})
        await self.send({'method': 'initialized'})

    async def send(self, value):
        self.proc.stdin.write((json.dumps(value) + '\n').encode())
        await self.proc.stdin.drain()

    async def rpc(self, method, params):
        self.count += 1
        key = self.count
        future = asyncio.get_running_loop().create_future()
        self.pending[key] = future
        await self.send({'id': key, 'method': method, 'params': params})
        try:
            result = await asyncio.wait_for(future, min(self.timeout, 30))
            if 'error' in result:
                raise RuntimeError('Native RPC error: ' + method)
            return result.get('result', {})
        finally:
            self.pending.pop(key, None)

    async def read(self):
        try:
            while True:
                line = await self.proc.stdout.readline()
                if not line:
                    break
                self.events.write(line.decode(errors='replace'))
                self.events.flush()
                try:
                    event = json.loads(line)
                except ValueError:
                    self.errors.append('Malformed native JSONL')
                    continue
                key = event.get('id')
                if key in self.pending and not self.pending[key].done():
                    self.pending[key].set_result(event)
                elif key is not None and event.get('method'):
                    # Approvals and dynamic tool calls cannot be granted by this harness.
                    await self.send({'id': key, 'error': {'code': -32601, 'message': 'Unsupported benchmark request'}})
                    self.errors.append('Unexpected server request')
                capture_turn_id(self.turn_ids, event)
                method, params = event.get('method'), event.get('params', {})
                tid = params.get('threadId')
                if method == 'thread/started':
                    thread = params['thread']
                    self.threads[thread['id']] = thread
                elif method == 'thread/tokenUsage/updated':
                    self.usage[tid] = params['tokenUsage']['total']
                elif method in ('turn/started', 'turn/completed'):
                    self.turns[tid] = params.get('turn', {})
                    if method == 'turn/completed' and tid == self.root:
                        if params['turn'].get('status') != 'completed':
                            self.errors.append('Root turn did not complete successfully')
                        self.done.set()
        except Exception:
            self.errors.append('Native reader failed')
        finally:
            for future in list(self.pending.values()):
                if not future.done():
                    future.set_exception(RuntimeError('Native server stream ended'))
            self.done.set()

    async def discover(self):
        cursor = None
        while True:
            params = {'ancestorThreadId': self.root, 'limit': 100}
            if cursor:
                params['cursor'] = cursor
            result = await self.rpc('thread/list', params)
            for thread in result.get('data', []):
                self.threads[thread['id']] = thread
            cursor = result.get('nextCursor')
            if not cursor:
                break
        for tid in list(self.threads):
            result = await self.rpc('thread/read', {'threadId': tid, 'includeTurns': True})
            thread = result.get('thread', {})
            self.threads[tid] = thread
            if thread.get('status', {}).get('type') == 'active':
                self.errors.append('Descendant active after root completion')

    async def close(self):
        if self.proc:
            # Interrupt every observed active child before closing the server.
            for tid, turn in list(self.turns.items()):
                if turn.get('status') == 'inProgress' and turn.get('id'):
                    try:
                        await asyncio.wait_for(self.rpc('turn/interrupt', {'threadId': tid, 'turnId': turn['id']}), 2)
                    except Exception:
                        pass
            await stop_process_group(self.proc)
            try:
                await asyncio.wait_for(self.proc.wait(), 5)
            except asyncio.TimeoutError:
                await stop_process_group(self.proc, force=True)
                await self.proc.wait()
            if hasattr(self, 'reader'):
                try:
                    await asyncio.wait_for(self.reader, 5)
                except asyncio.TimeoutError:
                    self.errors.append('Native reader did not close')
        for name in ('events', 'err'):
            file = getattr(self, name, None)
            if file:
                file.close()


async def grade_external(case, work, report_dir, timeout):
    """Bound the trusted grader in a separate process, including its subprocesses."""
    report_dir.mkdir(parents=True, exist_ok=True)
    code = ('import json,sys; from pathlib import Path; import corpus; '
            'r=corpus.grade_case(sys.argv[1],Path(sys.argv[2]),Path(sys.argv[3])); '
            'Path(sys.argv[4]).write_text(json.dumps(r))')
    raw = report_dir.parent / 'raw-grade.json'
    with (report_dir / 'grader.log').open('wb') as log:
        proc = await asyncio.create_subprocess_exec(sys.executable, '-c', code, case, str(work), str(report_dir), str(raw),
            cwd=Path(__file__).resolve().parent, stdout=log, stderr=log, **process_group_options())
        try:
            await asyncio.wait_for(proc.wait(), timeout)
            if proc.returncode:
                raise RuntimeError('Grader failed')
            return json.loads(raw.read_text())
        finally:
            if proc.returncode is None:
                await stop_process_group(proc)
                try:
                    await asyncio.wait_for(proc.wait(), 5)
                except asyncio.TimeoutError:
                    await stop_process_group(proc, force=True)
                    await proc.wait()


def sanitized_grade(grade):
    # Checks may contain command output: raw grade belongs only in local evidence.
    return {k: grade.get(k) for k in ('functional_score', 'regression_score', 'critical_pass', 'accepted',
        'functional_groups', 'mutants', 'validation_seconds', 'grading_seconds', 'scope_ok', 'authored_tests_pass')}


async def trial(args, corpus, arm, case, repetition, attempt, manifest):
    tid = f'{case}-r{repetition}-{arm}-a{attempt}'
    run = args.output / 'runs' / tid
    result = {'trial_id': tid, 'arm': arm, 'case': case, 'coding': corpus.CASES[case]['coding'],
              'repetition': repetition, 'attempt': attempt, 'accepted': False, 'complete': False,
              'usage_complete': False, 'total_tokens': None, 'errors': [], 'evidence_dir': str(run)}
    home = work = server = None
    started = time.monotonic()
    try:
        home, work, inventory = prepare_trial(run, getattr(args, arm), args.baseline, case, corpus, args.auth_source)
        server = AppServer(args.codex, home, work, run, args.timeout)
        await server.start()
        # Clock starts immediately before thread/start; includes checks/coordination.
        started = time.monotonic()
        info = await server.rpc('thread/start', {'model': manifest['models']['CODEX_ORCHESTRATOR_MODEL'],
            'cwd': str(work), 'approvalPolicy': 'never', 'sandbox': 'workspace-write', 'ephemeral': False})
        root = info['thread']['id']
        server.root = root
        server.threads[root] = info['thread']
        result.update(root=root, configured_model=manifest['models']['CODEX_ORCHESTRATOR_MODEL'],
                      selected_model=info.get('model'), selected_effort=info.get('reasoningEffort'), requested_effort='medium')
        await server.rpc('turn/start', {'threadId': root, 'input': [{'type': 'text',
            'text': PREFIX + corpus.CASES[case]['prompt']}], 'effort': 'medium'})
        await asyncio.wait_for(server.done.wait(), args.timeout)
        if not server.turns.get(root) or server.turns[root].get('status') != 'completed':
            raise RuntimeError('Root completion evidence missing')
        await asyncio.wait_for(server.discover(), args.timeout)
        result['complete'] = True
        result['instruction_sources'], issues = instruction_check(info, home, work, inventory)
        result['errors'] += issues + server.errors
    except asyncio.CancelledError:
        result['errors'].append('Cancelled; incomplete trial preserved')
        raise
    except Exception as exc:
        # Exception text can contain native transcript excerpts or credentials.
        result['errors'].append(type(exc).__name__ + ': trial incomplete; inspect private evidence')
    finally:
        if server:
            try:
                await server.close()
            except Exception:
                result['errors'].append('Native server cleanup failed')
            result['errors'] += [e for e in server.errors if e not in result['errors']]
        if home:
            auth = home / 'auth.json'
            if auth.is_symlink() or auth.is_file():
                auth.unlink()
            elif auth.exists():
                result['errors'].append('Owned auth path has unexpected type; cleanup incomplete')
        if server:
            try:
                turn_ids = getattr(server, 'turn_ids', {})
                result['thread_turn_ids'] = {k: sorted(v) for k, v in turn_ids.items()}
                logs = native_logs(home, turn_ids)
                records, complete, tokens, issues = reconcile_usage(server.threads, server.usage, logs, server.root)
                for log in logs.values():
                    for source in log.get('catalog_roots', []):
                        path = Path(source).resolve()
                        if not (path.is_relative_to(home.resolve()) or path.is_relative_to(work.resolve())):
                            issues.append('Unexpected native skill catalog root')
                result['skill_catalog_roots'] = sorted({p for log in logs.values() for p in log.get('catalog_roots', [])})
                result.update(threads=records, usage_complete=complete, total_tokens=tokens)
                result['errors'] += issues
            except Exception:
                result['errors'].append('Native evidence parsing failed; usage unevaluable')
        model_seconds = time.monotonic() - started
        result['agent_seconds'] = model_seconds
        grade = {}
        if work and (work / '.git').exists() and not any('Cancelled' in e for e in result['errors']):
            try:
                grade = await grade_external(case, work, run / 'grading', args.grading_timeout)
                result['grade'] = sanitized_grade(grade)
            except Exception as exc:
                result['errors'].append(type(exc).__name__ + ': grading incomplete')
        # Grading includes mutation work; validation alone is charged to throughput.
        validation = grade.get('validation_seconds')
        result['elapsed_seconds'] = model_seconds + (validation if isinstance(validation, (int, float)) and validation >= 0 else 0)
        result['grading_seconds'] = grade.get('grading_seconds')
        result['accepted'] = bool(result['complete'] and not result['errors'] and grade.get('accepted')
                                  and grade.get('critical_pass') and grade.get('scope_ok') and grade.get('authored_tests_pass'))
        if run.exists():
            dump(run / 'result.json', result)
    return result


def ratio(numerator, denominator):
    return numerator / denominator if (isinstance(numerator, (int, float)) and isinstance(denominator, (int, float))
        and math.isfinite(numerator) and math.isfinite(denominator) and denominator > 0) else None


def elapsed_sum(rows):
    values = [r.get('elapsed_seconds') for r in rows]
    return sum(values) if all(isinstance(v, (int, float)) and math.isfinite(v) and v > 0 for v in values) else None


def describe(values):
    return {'n': len(values), 'mean': statistics.mean(values) if values else None,
            'stdev': statistics.stdev(values) if len(values) > 1 else None,
            'min': min(values) if values else None, 'max': max(values) if values else None}


def summarize(results, reviews=None, screen=False, expected=None):
    reviews = reviews or {}
    arms, reasons = {}, []
    for arm in ('baseline', 'candidate'):
        rows = [r for r in results if r['arm'] == arm]
        accepted = sum(bool(r.get('accepted')) for r in rows)
        elapsed = [r.get('elapsed_seconds') for r in rows]
        time_ok = bool(rows) and all(isinstance(x, (int, float)) and math.isfinite(x) and x > 0 for x in elapsed)
        usage_ok = bool(rows) and all(r.get('usage_complete') and isinstance(r.get('total_tokens'), int) and r['total_tokens'] >= 0 for r in rows)
        coding = [r for r in rows if r.get('coding')]
        scores, manual_ok = [], bool(coding)
        for row in coding:
            review = reviews.get(row['trial_id'], {})
            criteria = review.get('criteria', [])
            valid = (isinstance(criteria, list) and len(criteria) == 5
                     and all(isinstance(s, int) and not isinstance(s, bool) and 0 <= s <= 2 for s in criteria)
                     and review.get('reviewer') and review.get('rationale'))
            score = sum(criteria) if valid else None
            manual_ok = manual_ok and bool(valid)
            grade = row.get('grade', {})
            # Every failed coding attempt contributes zero, even if partial checks pass.
            if not row.get('accepted'):
                scores.append(0)
            elif valid:
                f, g = grade.get('functional_score'), grade.get('regression_score')
                if not isinstance(f, (int, float)) or not 0 <= f <= 70 or not isinstance(g, (int, float)) or not 0 <= g <= 20:
                    manual_ok = False
                else:
                    scores.append(f + g + score)
        quality = statistics.mean(scores) if manual_ok and len(scores) == len(coding) else None
        arms[arm] = {'attempts': len(rows), 'accepted': accepted, 'acceptance_rate': ratio(accepted, len(rows)),
                     'total_seconds': sum(elapsed) if time_ok else None,
                     'throughput': ratio(accepted, sum(elapsed)) if time_ok else None,
                     'total_tokens': sum(r['total_tokens'] for r in rows) if usage_ok else None,
                     'tokens_per_accepted_task': ratio(sum(r['total_tokens'] for r in rows), accepted) if usage_ok else None,
                     'quality': quality, 'manual_reviews_complete': manual_ok,
                     'usage_complete': usage_ok, 'time_complete': time_ok,
                     'attempt_seconds': describe(elapsed) if time_ok else None,
                     'coding_attempts': len(coding),
                     'native_descendants': sum(max(0, len(r.get('threads', {})) - 1) for r in rows),
                     'attempts_with_delegation': sum(len(r.get('threads', {})) > 1 for r in rows),
                     'token_counters': {key: sum(t['event'][key] for r in rows for t in r.get('threads', {}).values())
                         if usage_ok and all(key in (t.get('event') or {}) for r in rows for t in r.get('threads', {}).values())
                         and all(r.get('threads') for r in rows) else None for key in TOKEN_KEYS}}
    base, candidate = arms['baseline'], arms['candidate']
    ratios = {'quality': ratio(candidate['quality'], base['quality']),
              'throughput': ratio(candidate['throughput'], base['throughput']),
              'tokens': ratio(candidate['tokens_per_accepted_task'], base['tokens_per_accepted_task'])}
    groups = {}
    for row in results:
        groups.setdefault((row['case'], row['repetition']), {}).setdefault(row['arm'], []).append(row)
    regressions, paired = [], []
    for (case, rep), pair in sorted(groups.items()):
        b, c = pair.get('baseline', []), pair.get('candidate', [])
        if not b or not c:
            reasons.append('Missing paired attempt: ' + case + '/' + str(rep))
            continue
        passed = set()
        for row in b:
            passed.update(k for k, v in row.get('grade', {}).get('functional_groups', {}).items() if v is True)
        for row in c:
            for group in passed:
                if row.get('grade', {}).get('functional_groups', {}).get(group) is not True:
                    regressions.append({'case': case, 'repetition': rep, 'group': group, 'trial_id': row['trial_id']})
        btime, ctime = elapsed_sum(b), elapsed_sum(c)
        paired.append({'case': case, 'repetition': rep, 'time_ratio': ratio(ctime, btime),
                       'throughput_ratio': ratio(ratio(sum(r.get('accepted', False) for r in c), ctime),
                                                 ratio(sum(r.get('accepted', False) for r in b), btime))})
    gates = {
        'candidate_critical': bool([r for r in results if r['arm'] == 'candidate']) and all(
            r.get('grade', {}).get('critical_pass') is True for r in results if r['arm'] == 'candidate'),
        'no_heldout_regression': not regressions,
        'acceptance_not_lower': base['acceptance_rate'] is not None and candidate['acceptance_rate'] is not None and candidate['acceptance_rate'] >= base['acceptance_rate'],
        'quality_target': ratios['quality'] is not None and ratios['quality'] >= 1.10,
        'throughput_target': ratios['throughput'] is not None and ratios['throughput'] >= 1.10,
        'token_target': ratios['tokens'] is not None and ratios['tokens'] <= 1.10,
        'quality_verified': all(a['manual_reviews_complete'] and a['quality'] is not None for a in arms.values()),
        'native_usage_complete': all(a['usage_complete'] for a in arms.values()),
        'all_attempts_complete': bool(results) and all(r.get('complete') and not r.get('errors') for r in results),
    }
    present = {(r['case'], r['repetition'], r['arm']) for r in results}
    gates['paired_design_complete'] = bool(groups) and not reasons and (expected is None or present == set(expected))
    reps = {r['repetition'] for r in results}
    confirmatory = not screen and len(reps) >= 3
    gates['confirmatory_design'] = confirmatory
    ceiling = base['quality'] is not None and base['quality'] > 100 / 1.1
    if ceiling:
        reasons.append('Baseline quality exceeds 90.909; a 10% gain is impossible on the 100-point scale. Target unchanged.')
    if screen:
        reasons.append('NONCONFIRMATORY screen: one paired repetition cannot authorize commit or push.')
    if not gates['quality_verified']:
        reasons.append('Quality unverified: anchored manual maintainability reviews required for every coding attempt.')
    delegation = any(a['native_descendants'] > 0 for a in arms.values())
    if not delegation:
        reasons.append('No native descendants observed; delegation-specific improvement is unverified. Overall workflow metrics remain reported.')
    return {'delegation_observed': delegation, 'delegation_claim_eligible': delegation and gates['native_usage_complete'],
            'version': VERSION, 'mode': 'NONCONFIRMATORY screen' if screen else 'repeated confirmation',
            'arms': arms, 'ratios': ratios, 'gates': gates, 'targets_passed': all(gates.values()),
            'screen_targets_passed': screen and all(v for k, v in gates.items() if k != 'confirmatory_design'),
            'baseline_quality_ceiling': ceiling, 'heldout_regressions': regressions,
            'critical_failures': [{'trial_id': r['trial_id'], 'case': r['case'], 'arm': r['arm']}
                                  for r in results if r.get('grade', {}).get('critical_pass') is not True],
            'paired_estimates': paired, 'paired_time_ratio_variability': describe([p['time_ratio'] for p in paired if p['time_ratio'] is not None]),
            'notes': reasons, 'automatic_commit_or_push': False,
            'monetary_cost': None, 'cost_status': 'Unverified; no observed billing/pricing evidence.',
            'human_productivity': 'Unverified; no observed user overhead or developer productivity measurements.'}


def parser():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run', action='store_true')
    p.add_argument('--baseline', type=Path)
    p.add_argument('--candidate', type=Path)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--codex', default='codex')
    p.add_argument('--repetitions', type=int, default=3)
    p.add_argument('--screen', action='store_true')
    p.add_argument('--summarize', action='store_true')
    p.add_argument('--reviews', type=Path)
    p.add_argument('--timeout', type=float, default=900)
    p.add_argument('--grading-timeout', type=float, default=300)
    return p


def read_results(output):
    results = []
    manifest_path = output / 'manifest.json'
    manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
    for directory in sorted((output / 'runs').glob('*')):
        if not directory.is_dir():
            continue
        path = directory / 'result.json'
        if path.exists():
            results.append(json.loads(path.read_text()))
            continue
        match = re.fullmatch(r'(.+)-r(\d+)-(baseline|candidate)-a(\d+)', directory.name)
        if not match:
            raise ValueError('Unrecognized attempt directory: ' + directory.name)
        case, rep, arm, attempt = match.groups()
        results.append({'trial_id': directory.name, 'case': case, 'repetition': int(rep), 'arm': arm,
            'attempt': int(attempt), 'coding': manifest.get('cases', {}).get(case, {}).get('coding', True),
            'accepted': False, 'complete': False, 'usage_complete': False, 'elapsed_seconds': None,
            'total_tokens': None, 'errors': ['Interrupted attempt lacks final result; evidence retained'],
            'evidence_dir': str(directory)})
    return results


def expected_trials(cases, reps):
    return [(case, rep, arm) for rep in range(1, reps + 1) for case in cases for arm in ('baseline', 'candidate')]


async def execute(args, corpus, manifest):
    for rep in range(1, manifest['repetitions'] + 1):
        for index, case in enumerate(corpus.CASES):
            order = ('baseline', 'candidate') if (rep + index) % 2 else ('candidate', 'baseline')
            for arm in order:
                # Completed attempts are immutable. Infrastructure failures remain in
                # aggregate penalties; a later --run creates an explicit new attempt.
                prefix = f'{case}-r{rep}-{arm}-a'
                old = [r for r in read_results(args.output) if r['case'] == case and r['repetition'] == rep and r['arm'] == arm]
                if any(r.get('complete') and not r.get('errors') for r in old):
                    continue
                # Drift check immediately before every model call, including resume.
                fresh = make_manifest(args.baseline, args.candidate, corpus, manifest['repetitions'], args.screen, args.codex,
                                      timeout=args.timeout, grading_timeout=args.grading_timeout)
                freeze_manifest(args.output / 'manifest.json', fresh)
                attempts = [int(p.name.removeprefix(prefix)) for p in (args.output / 'runs').glob(prefix + '*')]
                attempt = max(attempts, default=0) + 1
                result = await trial(args, corpus, arm, case, rep, attempt, manifest)
                print(json.dumps({k: result[k] for k in ('trial_id', 'accepted', 'complete', 'usage_complete', 'total_tokens', 'elapsed_seconds', 'errors')}), flush=True)
                if result['errors']:
                    return


def main(argv=None):
    args = parser().parse_args(argv)
    if args.run and args.summarize:
        raise SystemExit('Choose --run or --summarize')
    if args.summarize:
        manifest = json.loads((args.output / 'manifest.json').read_text())
        reviews = json.loads(args.reviews.read_text()) if args.reviews else {}
        summary = summarize(read_results(args.output), reviews, manifest['screen'],
                            expected_trials(manifest['cases'], manifest['repetitions']))
        dump(args.output / 'summary.json', summary)
        print(json.dumps(summary, indent=2))
        return 0
    if not args.baseline or not args.candidate:
        raise SystemExit('--baseline and --candidate required for planning/execution')
    if args.repetitions < 1 or args.timeout <= 0 or args.grading_timeout <= 0:
        raise SystemExit('Repetitions and timeouts must be positive')
    args.baseline, args.candidate, args.output = args.baseline.resolve(), args.candidate.resolve(), args.output.resolve()
    corpus = importlib.import_module('corpus')
    reps = 1 if args.screen else args.repetitions
    manifest = make_manifest(args.baseline, args.candidate, corpus, reps, args.screen, args.codex,
                             timeout=args.timeout, grading_timeout=args.grading_timeout)
    if not args.run:
        print(json.dumps({'action': 'plan only; no model calls', 'manifest': manifest,
                          'trials': expected_trials(corpus.CASES, reps)}, indent=2))
        return 0
    if manifest['binary_sha256'] is None:
        raise SystemExit('Codex binary not found')
    original_home = Path(os.environ.get('CODEX_HOME', str(Path.home() / '.codex')))
    args.auth_source = (original_home / 'auth.json').resolve()
    if not args.auth_source.is_file():
        raise SystemExit('Existing native auth.json required; no authentication values are read or copied')
    args.output.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(args.output, 0o700)
    freeze_manifest(args.output / 'manifest.json', manifest)
    try:
        asyncio.run(execute(args, corpus, manifest))
    except KeyboardInterrupt:
        print('Interrupted; incomplete attempt preserved. Resume uses a new attempt.', file=sys.stderr)
        return 130
    reviews = json.loads(args.reviews.read_text()) if args.reviews else {}
    summary = summarize(read_results(args.output), reviews, args.screen, expected_trials(corpus.CASES, reps))
    dump(args.output / 'summary.json', summary)
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
