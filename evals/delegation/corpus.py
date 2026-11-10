"""Private, stdlib-only coding corpus. Never copy this module into agent workspaces."""
from __future__ import annotations
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

CHECK = 'python3 -B -m unittest discover -s tests -v'
TOOLS = '''import re
SUPPORTED = ("codex", "claude", "cursor")
def parse_tools(value):
    if not isinstance(value, str):
        raise ValueError("tools must be text")
    parts = [p.strip().lower() for p in re.split(r"[,;]", value)]
    if not parts or any(not p for p in parts):
        raise ValueError("empty tool")
    if parts == ["all"]:
        return list(SUPPORTED)
    if "all" in parts or any(p not in SUPPORTED for p in parts):
        raise ValueError("invalid tool")
    return list(dict.fromkeys(parts))
'''
OWNERSHIP = '''from pathlib import Path
import hashlib
import stat
def plan_install(root, desired, owned, backup=False):
    root = Path(root)
    result = {}
    root_unsafe = root.is_symlink() or not root.is_dir()
    for name, content in desired.items():
        rel = Path(name)
        if rel.is_absolute() or not rel.parts or any(p in ("..", ".") for p in rel.parts):
            raise ValueError("unsafe relative path")
        path = root / rel
        unsafe = root_unsafe
        cursor = root
        for part in rel.parts[:-1]:
            cursor = cursor / part
            if cursor.is_symlink() or (cursor.exists() and not cursor.is_dir()):
                unsafe = True
        if unsafe or path.is_symlink():
            result[name] = "conflict"
        elif not path.exists():
            result[name] = "create"
        elif not stat.S_ISREG(path.lstat().st_mode):
            result[name] = "conflict"
        else:
            data = path.read_bytes()
            digest = hashlib.sha256(data).hexdigest()
            if data == content:
                result[name] = "unchanged"
            elif owned.get(name) == digest:
                result[name] = "update"
            else:
                result[name] = "backup-update" if backup else "conflict"
    return result
'''
DAG = '''import asyncio
async def execute(tasks, dependencies, limit=2):
    if not isinstance(limit, int) or isinstance(limit, bool) or limit < 1:
        raise ValueError("positive integer limit required")
    if set(dependencies) - set(tasks):
        raise ValueError("unknown task")
    deps = {k: set(dependencies.get(k, ())) for k in tasks}
    if any(d - set(tasks) for d in deps.values()):
        raise ValueError("missing dependency")
    pending = set(tasks)
    visited = set()
    while pending:
        ready = {k for k in pending if deps[k] <= visited}
        if not ready:
            raise ValueError("cycle")
        visited.update(ready)
        pending -= ready
    outcomes = {}
    running = {}
    pending = set(tasks)
    async def run(name):
        try:
            await tasks[name]()
            return "ok"
        except Exception:
            return "failed"
    while pending or running:
        for name in sorted(pending):
            if deps[name] <= outcomes.keys():
                if any(outcomes[d] != "ok" for d in deps[name]):
                    outcomes[name] = "blocked"
                    pending.remove(name)
                elif len(running) < limit:
                    running[asyncio.create_task(run(name))] = name
                    pending.remove(name)
        if running:
            done, _ = await asyncio.wait(running, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                outcomes[running.pop(task)] = task.result()
    return {k: outcomes[k] for k in sorted(tasks)}
'''
RENDERING = '''import re
def render(template, models):
    def replace(match):
        key = match.group(1)
        value = models.get(key)
        if not isinstance(value, str) or not value.strip():
            raise ValueError("missing model")
        return value
    result = re.sub(r"\\{\\{model:([A-Za-z0-9_-]+)\\}\\}", replace, template)
    if "{{model:" in result:
        raise ValueError("malformed model placeholder")
    return result
'''
SKILLS = '''def strip_frontmatter(text):
    lines = text.splitlines(keepends=True)
    if not lines or lines[0].rstrip("\\r\\n") != "---":
        return text
    for index in range(1, len(lines)):
        if lines[index].rstrip("\\r\\n") == "---":
            return "".join(lines[index + 1:])
    raise ValueError("unterminated frontmatter")
'''
BUNDLES = '''from rendering import render
from skills import strip_frontmatter
def build_bundle(template, skill, models):
    return {"agent": render(template, models), "skill": strip_frontmatter(skill)}
'''
REFERENCE = {
    'tools': {'tools.py': TOOLS},
    'ownership': {'installer.py': OWNERSHIP},
    'dag': {'executor.py': DAG},
    'bundle': {'rendering.py': RENDERING, 'skills.py': SKILLS, 'bundles.py': BUNDLES},
}
SEEDS = {
    'tools': {'tools.py': 'SUPPORTED = ("codex", "claude", "cursor")\ndef parse_tools(value):\n    return value.split(",")\n'},
    'ownership': {'installer.py': OWNERSHIP.replace('if unsafe or path.is_symlink():', 'if unsafe:').replace('elif owned.get(name) == digest:', 'elif name in owned:')},
    'dag': {'executor.py': 'async def execute(tasks, dependencies, limit=2):\n    outcomes = {}\n    for name, task in tasks.items():\n        try:\n            await task()\n            outcomes[name] = "ok"\n        except Exception:\n            outcomes[name] = "failed"\n    return outcomes\n'},
    'bundle': {'rendering.py': 'def render(template, models):\n    for key, value in models.items():\n        template = template.replace("{{model:" + key + "}}", value)\n    return template\n', 'skills.py': 'def strip_frontmatter(text):\n    return text\n', 'bundles.py': BUNDLES},
}
CONTRACTS = {
 'tools': '''Implement tools.parse_tools(value) -> list[str]. Accept comma and semicolon separators, trim whitespace, lowercase, and de-duplicate while preserving first-seen order. Supported names are codex, claude, cursor. A sole case-insensitive 'all' returns that order. Raise ValueError for non-string input, blank input, empty segments, unknown names, or all combined with another segment (even all). Preserve SUPPORTED. This is a tiny direct-control task; implement directly and add behavioral unittest coverage.''',
 'ownership': '''Repair installer.plan_install(root, desired, owned, backup=False) -> dict[str,str]. desired maps safe relative POSIX names to bytes; owned maps names to SHA256 hex digests of previously installed bytes. Return create for missing paths, unchanged for identical regular-file bytes, update only when current bytes match owned digest, otherwise conflict (or backup-update if backup=True). Symlinks (including dangling, file, directory ancestors, and root), directories/nonregular targets, and non-directory ancestors always conflict, even with backup. Reject absolute and parent-traversal names with ValueError. Never follow links, mutate filesystem, create backups, or alter dictionaries: this is a read-only plan. Absent intermediate directories are safe creates. Precondition: root already exists as a directory (or a symlink to one, which must conflict); callers must not pass a missing root. Use temporary fixtures only. Add regression tests for ownership and path safety.''',
 'dag': '''Implement executor.execute(tasks, dependencies, limit=2), an async function. tasks maps string names to zero-argument async callables. dependencies maps names to iterable prerequisite names; omitted entries mean none. Validate all dependencies, unknown dependency keys, cycles and positive integer limit (bool invalid) before invoking any task; invalid input raises ValueError. Run ready work with at most limit active tasks and allow independent ready tasks to overlap. A raised Exception yields failed; descendants yield blocked without invocation; independent branches complete. Await every scheduled task. Return a dict in sorted task-name insertion order with values ok/failed/blocked (not task results). Empty graph returns {}. Preserve API and add deterministic async behavioral tests (events/counters, bounded waits).''',
 'bundle': '''Implement rendering.render(template, models) -> str and skills.strip_frontmatter(text) -> str independently, then integrate via bundles.build_bundle(template, skill, models) -> {'agent': rendered, 'skill': stripped}. render replaces each {{model:KEY}} where KEY consists of ASCII letters, digits, underscore or hyphen, using nonblank string values from models; missing/blank/non-string values and malformed remaining {{model: sequences raise ValueError. Repeated keys work; unrelated braces and unused mappings are preserved/ignored. strip_frontmatter removes only an opening YAML block delimited by exact --- lines at the start of text, preserving body bytes/newlines; accept LF and CRLF, allow empty metadata/body. Raise ValueError on unclosed opening block; no opening delimiter means unchanged text (including later ---). Never mutate models. Add module and integration tests. No YAML library needed.''',
}
CASES = {key: {'prompt': value + '\nEdit only ' + ', '.join(REFERENCE[key]) + ' and tests/**/*.py. Public signatures must remain unchanged. Add tests; run ' + CHECK + '.', 'coding': True, 'allowed_files': list(REFERENCE[key])} for key, value in CONTRACTS.items()}
SMOKE = {
 'tools': 'from tools import parse_tools\nclass Smoke(unittest.TestCase):\n def test_one(self): self.assertEqual(parse_tools("codex"), ["codex"])\n',
 'ownership': 'from installer import plan_install\nclass Smoke(unittest.TestCase):\n def test_missing(self):\n  with tempfile.TemporaryDirectory() as d: self.assertEqual(plan_install(Path(d), {"a": b"x"}, {}), {"a":"create"})\n',
 'dag': 'from executor import execute\nclass Smoke(unittest.TestCase):\n def test_empty(self): self.assertEqual(asyncio.run(execute({}, {})), {})\n',
 'bundle': 'from bundles import build_bundle\nclass Smoke(unittest.TestCase):\n def test_plain(self): self.assertEqual(build_bundle("plain", "body", {}), {"agent":"plain", "skill":"body"})\n',
}
IMPORTS = 'import unittest, tempfile, asyncio, hashlib\nfrom pathlib import Path\n'

def prepare_case(case_id, dest):
    dest = Path(dest)
    dest.mkdir(parents=True, exist_ok=True)
    for name, content in SEEDS[case_id].items():
        (dest / name).write_text(content)
    (dest / 'tests').mkdir(exist_ok=True)
    (dest / 'tests/test_smoke.py').write_text(IMPORTS + SMOKE[case_id])
    (dest / 'docs').mkdir(exist_ok=True)
    (dest / 'docs/agent-context.md').write_text('# Case workspace\nStdlib Python only. No network, user config or external dependencies.\nFast check: `' + CHECK + '`\n')
    (dest / 'TASK.md').write_text(CASES[case_id]['prompt'])
    (dest / '.gitignore').write_text('__pycache__/\n*.py[cod]\n')

# Each group executes in its own subprocess, with fixtures outside the task workspace.
GROUPS = {
 'tools': {
  'normalization': r'''from tools import parse_tools, SUPPORTED
assert tuple(SUPPORTED) == ('codex','claude','cursor')
assert parse_tools(' Cursor ; CODEX,claude ;cursor ') == ['cursor','codex','claude']
assert parse_tools(' CoDeX ') == ['codex']
''',
  'all': r'''from tools import parse_tools
assert parse_tools(' ALL ') == ['codex','claude','cursor']
''',
  'invalid': r'''from tools import parse_tools
for value in (None, 1, [], '', ' ', 'codex,', ';codex', 'codex,,cursor', 'all,codex', 'all;all', 'unknown', 'codex cursor'):
 with raises(ValueError): parse_tools(value)
''',
  'interface': r'''import inspect
from tools import parse_tools
assert list(inspect.signature(parse_tools).parameters) == ['value']
assert parse_tools('claude,codex,claude,cursor') == ['claude','codex','cursor']
''',
 },
 'ownership': {
  'ownership': r'''from installer import plan_install
with tempfile.TemporaryDirectory() as d:
 root=Path(d); (root/'a').write_bytes(b'old')
 desired={'a':b'new'}; owned={'a':hashlib.sha256(b'old').hexdigest()}
 assert plan_install(root,desired,owned)=={'a':'update'}
 (root/'a').write_bytes(b'edited')
 assert plan_install(root,desired,owned)=={'a':'conflict'}
 assert plan_install(root,desired,{},backup=True)=={'a':'backup-update'}
 assert desired=={'a':b'new'} and owned=={'a':hashlib.sha256(b'old').hexdigest()}
 assert (root/'a').read_bytes()==b'edited'
''',
  'regular_paths': r'''from installer import plan_install
with tempfile.TemporaryDirectory() as d:
 root=Path(d); (root/'a').write_bytes(b'x'); (root/'dir').mkdir()
 assert plan_install(root,{'a':b'x','new/deep':b'y','dir':b'z','a/child':b'z'}, {})=={'a':'unchanged','new/deep':'create','dir':'conflict','a/child':'conflict'}
 assert sorted(p.relative_to(root).as_posix() for p in root.rglob('*'))==['a','dir']
''',
  'links': r'''from installer import plan_install
from unittest.mock import patch
with tempfile.TemporaryDirectory() as d:
 base=Path(d).resolve(); root=base/'root'; root.mkdir(); outside=base/'outside'; outside.mkdir()
 (outside/'file').write_bytes(b'secret'); (root/'source').write_bytes(b'owned')
 (root/'file').symlink_to(outside/'file'); (root/'dangling').symlink_to(outside/'absent'); (root/'dir').symlink_to(outside, target_is_directory=True)
 linked=base/'linked'; linked.symlink_to(root,target_is_directory=True)
 desired={'file':b'secret','dangling':b'new','dir/file':b'secret','dir/absent':b'new'}
 unsafe_reads=[]; read_bytes=Path.read_bytes
 def guarded_read(path):
  path=Path(path)
  linked_path=any(p.is_symlink() for p in (path,*path.parents) if p==base or base in p.parents)
  # Also catch resolving a linked filename before reading its outside target.
  if linked_path or path in (outside/'file',root/'source'):
   unsafe_reads.append(str(path))
   raise AssertionError('planner followed a symlink for content')
  return read_bytes(path)
 with patch.object(Path,'read_bytes',guarded_read):
  for backup in (False,True):
   assert plan_install(root,desired,{},backup)==dict.fromkeys(desired,'conflict')
   assert plan_install(linked,{'source':b'owned','missing':b'x'}, {},backup)=={'source':'conflict','missing':'conflict'}
  assert not unsafe_reads
 assert (outside/'file').read_bytes()==b'secret' and (root/'source').read_bytes()==b'owned' and not (outside/'absent').exists()
''',
  'invalid_interface': r'''import inspect
from installer import plan_install
assert list(inspect.signature(plan_install).parameters)==['root','desired','owned','backup']
assert inspect.signature(plan_install).parameters['backup'].default is False
with tempfile.TemporaryDirectory() as d:
 for name in ('/absolute','../outside','safe/../../outside'):
  with raises(ValueError): plan_install(Path(d),{name:b'x'}, {})
''',
 },
 'dag': {
  'dependencies': r'''from executor import execute
async def scenario():
 seen=[]
 async def a(): seen.append('a')
 async def b(): assert 'a' in seen; seen.append('b')
 async def c(): assert 'b' in seen; seen.append('c')
 result=await execute({'c':c,'b':b,'a':a},{'c':['b'],'b':['a']},2)
 assert result=={'a':'ok','b':'ok','c':'ok'} and list(result)==['a','b','c']
 assert await execute({}, {})=={}
asyncio.run(scenario())
''',
  'failure_isolation': r'''from executor import execute
async def scenario():
 seen=[]
 async def fail(): seen.append('bad'); raise RuntimeError('expected')
 async def forbidden(): seen.append('forbidden')
 async def independent(): await asyncio.sleep(0); seen.append('good')
 result=await execute({'z':independent,'a':fail,'b':forbidden,'c':forbidden},{'b':['a'],'c':['b']},2)
 assert result=={'a':'failed','b':'blocked','c':'blocked','z':'ok'}
 assert seen.count('good')==1 and seen.count('bad')==1 and 'forbidden' not in seen
asyncio.run(scenario())
''',
  'parallel_limit': r'''from executor import execute
async def scenario(limit):
 active=0; peak=0; calls=[]; two=asyncio.Event()
 async def work():
  nonlocal active,peak
  active+=1; peak=max(peak,active); calls.append(1)
  if active>=2: two.set()
  if limit==2: await asyncio.wait_for(two.wait(),.8)
  await asyncio.sleep(.01)
  active-=1
 result=await asyncio.wait_for(execute({str(i):work for i in range(5)}, {},limit),2)
 assert len(calls)==5 and active==0 and peak==limit and set(result.values())=={'ok'}
asyncio.run(scenario(2)); asyncio.run(scenario(1))
''',
  'upfront_validation': r'''import inspect
from executor import execute
assert list(inspect.signature(execute).parameters)==['tasks','dependencies','limit']
assert inspect.signature(execute).parameters['limit'].default==2
async def scenario():
 called=[]
 async def work(): called.append(1)
 for tasks,deps,limit in [({'a':work},{'a':['missing']},2),({'a':work},{'other':[]},2),({'a':work,'b':work},{'a':['b'],'b':['a']},2),({'a':work},{'a':['a']},2),({'a':work},{},0),({'a':work},{},True),({'a':work},{},1.5)]:
  with raises(ValueError): await execute(tasks,deps,limit)
  assert not called
asyncio.run(scenario())
''',
 },
 'bundle': {
  'render_values': r'''from rendering import render
models={'worker':'model-a','review_2':'model-b','unused':None}
assert render('{{model:worker}} / {{model:review_2}} / {{model:worker}} {other}', models)=='model-a / model-b / model-a {other}'
assert models=={'worker':'model-a','review_2':'model-b','unused':None}
assert render('unchanged',{})=='unchanged'
''',
  'render_errors': r'''from rendering import render
for template,models in [('{{model:x}}',{}),('{{model:x}}',{'x':''}),('{{model:x}}',{'x':'  '}),('{{model:x}}',{'x':42}),('{{model:}}',{}),('{{model:bad.key}}',{}),('{{model:x}',{'x':'a'})]:
 with raises(ValueError): render(template,models)
''',
  'frontmatter': r'''from skills import strip_frontmatter
for source,expected in [('---\nname: x\n---\n# Body\n','\x23 Body\n'),('---\r\nname: x\r\n---\r\nBody\r\n','Body\r\n'),('---\n---\n',''),('---\n---',''),('plain\n---\nbody','plain\n---\nbody'),(' ---\n',' ---\n'),('','')]:
 assert strip_frontmatter(source)==expected
for source in ('---\nname: x','---','---\n ---\nbody'):
 with raises(ValueError): strip_frontmatter(source)
''',
  'integration_interface': r'''import inspect
from bundles import build_bundle
from rendering import render
from skills import strip_frontmatter
assert list(inspect.signature(render).parameters)==['template','models']
assert list(inspect.signature(strip_frontmatter).parameters)==['text']
assert list(inspect.signature(build_bundle).parameters)==['template','skill','models']
assert build_bundle('{{model:a}}','---\ntitle: demo\n---\nbody\n',{'a':'sol'})=={'agent':'sol','skill':'body\n'}
with raises(ValueError): build_bundle('{{model:missing}}','plain',{})
''',
 },
}

# Wrappers are installed before test discovery; tests importing public symbols see them.
# Each fault is valid Python and changes a separate observable contract.
MUTANTS = {
 'tools': {
  'semicolon': "def fault(value):\n return original(value.replace(';', ',bad,'))\n",
  'case': "def fault(value):\n result=original(value)\n return [x.upper() for x in result] if value != value.lower() else result\n",
  'duplicates': "def fault(value):\n result=original(value)\n return result + result[:1] if len(value.replace(';',',').split(',')) > len(result) else result\n",
  'all': "def fault(value):\n return ['codex'] if isinstance(value,str) and value.strip().lower()=='all' else original(value)\n",
  'invalid': "def fault(value):\n try: return original(value)\n except ValueError: return []\n",
 },
 'ownership': {
  'overwrite_modified': "def fault(root, desired, owned, backup=False):\n result=original(root,desired,owned,backup)\n return {k:('update' if v=='conflict' and k in owned else v) for k,v in result.items()}\n",
  'links': "def fault(root, desired, owned, backup=False):\n result=original(root,desired,owned,backup)\n return {k:('create' if (Path(root)/k).is_symlink() else v) for k,v in result.items()}\n",
  'backup': "def fault(root, desired, owned, backup=False):\n result=original(root,desired,owned,backup)\n return {k:('conflict' if v=='backup-update' else v) for k,v in result.items()}\n",
  'unchanged': "def fault(root, desired, owned, backup=False):\n return {k:('update' if v=='unchanged' else v) for k,v in original(root,desired,owned,backup).items()}\n",
  'traversal': "def fault(root, desired, owned, backup=False):\n try: return original(root,desired,owned,backup)\n except ValueError: return dict.fromkeys(desired,'create')\n",
 },
 'dag': {
  'failure': "async def fault(tasks, dependencies, limit=2):\n return {k:('ok' if v=='failed' else v) for k,v in (await original(tasks,dependencies,limit)).items()}\n",
  'blocked': "async def fault(tasks, dependencies, limit=2):\n return {k:('failed' if v=='blocked' else v) for k,v in (await original(tasks,dependencies,limit)).items()}\n",
  'limit': "async def fault(tasks, dependencies, limit=2):\n return await original(tasks,dependencies,limit+1)\n",
  'serial': "async def fault(tasks, dependencies, limit=2):\n return await original(tasks,dependencies,1)\n",
  'order': "async def fault(tasks, dependencies, limit=2):\n return dict(reversed(list((await original(tasks,dependencies,limit)).items())))\n",
  'validation': "async def fault(tasks, dependencies, limit=2):\n try: return await original(tasks,dependencies,limit)\n except ValueError: return {}\n",
 },
 'bundle': {
  'missing_model': "def fault(template, models):\n try: return original(template,models)\n except ValueError: return template\n",
  'repeat': "def fault(template, models):\n result=original(template,models)\n return result+'!' if any(template.count('{{model:'+k+'}}')>1 for k in models) else result\n",
  'metadata': "def fault(text):\n return text\n",
  'crlf': "def fault(text):\n return original(text).replace('\\r\\n','\\n')\n",
  'unclosed': "def fault(text):\n try: return original(text)\n except ValueError: return text\n",
 },
}
TARGET = {'tools':('tools','parse_tools'), 'ownership':('installer','plan_install'), 'dag':('executor','execute'), 'bundle':('rendering','render')}

def _target(case_id, mutant):
    if case_id == 'bundle' and mutant in ('metadata','crlf','unclosed'):
        return 'skills', 'strip_frontmatter'
    return TARGET[case_id]


def _run(script, workspace, scratch, label, timeout=12):
    """Write trusted harness outside workspace; preserve stdout only as evidence."""
    script_path = scratch / (label + '.py')
    script_path.write_text(script)
    start = time.monotonic()
    try:
        result = subprocess.run([sys.executable, '-B', str(script_path)], cwd=workspace,
            env={**os.environ, 'PYTHONPATH': str(workspace), 'PYTHONDONTWRITEBYTECODE':'1'},
            capture_output=True, text=True, timeout=timeout)
        return {'command': [sys.executable, '-B', str(script_path)], 'reason':label,
                'seconds':time.monotonic()-start, 'returncode':result.returncode,
                'stdout':result.stdout[-6000:], 'stderr':result.stderr[-6000:]}
    except subprocess.TimeoutExpired:
        return {'command':[sys.executable,'-B',str(script_path)], 'reason':label,
                'seconds':time.monotonic()-start, 'returncode':124, 'stderr':'timeout', 'stdout':''}


def _tests_script(case_id, output, mutant=None):
    imports = '\n'.join('import '+Path(name).stem for name in REFERENCE[case_id])
    patch = ''
    if mutant:
        module, symbol = _target(case_id,mutant)
        patch = f'original = {module}.{symbol}\n' + MUTANTS[case_id][mutant] + f'\n{module}.{symbol}=fault\n'
        # Bundle integration imports symbols eagerly, so refresh their bindings too.
        if case_id == 'bundle':
            patch += f'bundles.{"render" if module=="rendering" else "strip_frontmatter"}=fault\n'
    return IMPORTS + 'import json, sys, re\n' + imports + '\n' + patch + f'''
Path({str(output)!r}).write_text(json.dumps({{"ready":True}}))
suite=unittest.defaultTestLoader.discover('tests')
result=unittest.TextTestRunner(verbosity=2).run(suite)
Path({str(output)!r}).write_text(json.dumps({{"ready":True,"tests":result.testsRun,"success":result.wasSuccessful(),"failures":len(result.failures),"errors":len(result.errors),"import_errors":sum(bool(re.search(r"(?:^|\\n)(?:SyntaxError|ImportError|ModuleNotFoundError):", trace)) for _,trace in result.errors)}}))
sys.exit(0 if result.wasSuccessful() else 1)
'''


def _git(workspace, *args):
    return subprocess.run(['git','-C',str(workspace),*args],capture_output=True,text=True,check=True).stdout


def grade_case(case_id, workspace, report_dir):
    started=time.monotonic(); workspace=Path(workspace).resolve(); report_dir=Path(report_dir).resolve()
    if report_dir == workspace or workspace in report_dir.parents:
        raise ValueError('reports/held-out fixtures must be outside task workspace')
    report_dir.mkdir(parents=True,exist_ok=True)
    checks=[]; failures=[]; groups={}; mutants={}; scope_ok=False; authored=False
    try:
        _git(workspace,'rev-parse','--verify','HEAD')
        changed=set(_git(workspace,'diff','--name-only','HEAD','-z').split('\0')) | set(_git(workspace,'ls-files','--others','--exclude-standard','-z').split('\0'))
        changed.discard('')
        def allowed(name):
            p=Path(name)
            return name in CASES[case_id]['allowed_files'] or (len(p.parts)>1 and p.parts[0]=='tests' and p.suffix=='.py')
        scope_ok=all(allowed(name) for name in changed)
        authored=any(name.startswith('tests/') and name.endswith('.py') and (workspace/name).is_file() for name in changed)
        # Deleted source files, symlink replacements, or test symlinks cannot be valid submissions.
        scope_ok=scope_ok and all((workspace/name).is_file() and not (workspace/name).is_symlink() for name in CASES[case_id]['allowed_files'])
        scope_ok=scope_ok and not (workspace/'tests').is_symlink() and not any(p.is_symlink() for p in (workspace/'tests').rglob('*'))
        if not scope_ok: failures.append('Changes outside allowed regular source/test files')
    except (subprocess.CalledProcessError,OSError) as exc:
        failures.append('Git scope validation failed: '+str(exc))
    with tempfile.TemporaryDirectory(prefix='corpus-',dir=report_dir) as temporary:
        scratch=Path(temporary)
        # Copy source and tests only; never mutate or import from the original submission.
        copy=scratch/'submission'; copy.mkdir()
        for name in REFERENCE[case_id]:
            path=workspace/name
            if path.is_file() and not path.is_symlink(): shutil.copyfile(path,copy/name)
        (copy/'tests').mkdir()
        if (workspace/'tests').is_dir() and not (workspace/'tests').is_symlink():
            for directory, dirs, files in os.walk(workspace/'tests', followlinks=False):
                dirs[:] = [name for name in dirs if not (Path(directory)/name).is_symlink()]
                relative=Path(directory).relative_to(workspace/'tests')
                target=copy/'tests'/relative
                target.mkdir(parents=True,exist_ok=True)
                for name in files:
                    path=Path(directory)/name
                    if path.suffix=='.py' and path.is_file() and not path.is_symlink():
                        shutil.copyfile(path,target/name)
        prefix=IMPORTS+'from contextlib import contextmanager\n@contextmanager\ndef raises(kind):\n try:\n  yield\n except kind:\n  pass\n else:\n  raise AssertionError("expected exception")\n'
        for name, body in GROUPS[case_id].items():
            result=_run(prefix+body,copy,scratch,'functional_'+name)
            checks.append(result); groups[name]=result['returncode']==0
            if not groups[name]: failures.append('Functional group failed: '+name)
        output=scratch/'authored.json'
        result=_run(_tests_script(case_id,output),copy,scratch,'authored_tests')
        checks.append(result)
        data=json.loads(output.read_text()) if output.exists() else {}
        authored_pass=bool(result['returncode']==0 and data.get('success') and data.get('tests',0)>0)
        if not authored_pass: failures.append('Authored test discovery failed or found no tests')
        if not authored: failures.append('Task requires an added or changed test file')
        validation_seconds=time.monotonic()-started
        grading_start=time.monotonic()
        for name in MUTANTS[case_id]:
            output=scratch/('mutant_'+name+'.json')
            result=_run(_tests_script(case_id,output,name),copy,scratch,'mutant_'+name)
            checks.append(result)
            data=json.loads(output.read_text()) if output.exists() else {}
            # Missing ready marker means import/patch failed, not a killed mutant.
            killed=bool(authored and authored_pass and data.get('ready') and data.get('tests',0)>0 and not data.get('success') and result['returncode']==1 and not data.get('import_errors') and (data.get('failures',0)+data.get('errors',0)>0))
            mutants[name]=killed
        grading_seconds=time.monotonic()-grading_start
    critical=scope_ok and all(groups.values())
    functional=70*sum(groups.values())/len(groups)
    regression=20*sum(mutants.values())/len(mutants) if authored and authored_pass else 0
    answer={'functional_score':round(functional,3),'regression_score':round(regression,3),
      'critical_pass':critical,'accepted':bool(critical and authored_pass and authored),
      'functional_groups':groups,'mutants':mutants,'validation_seconds':validation_seconds,
      'grading_seconds':grading_seconds,'checks':checks,'failures':failures,
      'scope_ok':scope_ok,'authored_tests_pass':authored_pass}
    (report_dir/'grade.json').write_text(json.dumps(answer,indent=2))
    return answer
