"""Self-check graders using trusted complete tests, seeds, and unsafe submissions."""
import ast
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

import corpus

REPORT_PATH = os.environ.get('ORCHESTRA_CORPUS_SELFTEST_REPORT')


def initialize(path):
    # Local disposable fixture commits establish immutable scope baselines only.
    for args in (['init','-q'], ['add','.'],
                 ['-c','user.name=Corpus fixture','-c','user.email=fixture@example.invalid',
                  '-c','commit.gpgsign=false','commit','-qm','seed']):
        subprocess.run(['git','-C',str(path),*args],check=True,capture_output=True)


def complete_tests(case_id):
    prefix = corpus.IMPORTS + '''from contextlib import contextmanager
@contextmanager
def raises(kind):
 try:
  yield
 except kind:
  pass
 else:
  raise AssertionError('expected exception')

class Contract(unittest.TestCase):
'''
    for name, body in corpus.GROUPS[case_id].items():
        prefix += f' def test_{name}(self):\n  exec({body!r}, globals())\n'
    return prefix


class CorpusChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='orchestra-corpus-selftest-')
        cls.root = Path(cls.temp.name)
        cls.results = {}

    @classmethod
    def tearDownClass(cls):
        try:
            if REPORT_PATH:
                report=Path(REPORT_PATH)
                report.parent.mkdir(parents=True,exist_ok=True)
                report.write_text(json.dumps(cls.results,indent=2))
        finally:
            cls.temp.cleanup()

    def fixture(self, case_id, suffix):
        path=self.root/(case_id+'-'+suffix)
        corpus.prepare_case(case_id,path)
        initialize(path)
        return path

    def test_seeds_have_passing_smoke_but_fail_acceptance(self):
        for case_id in corpus.CASES:
            with self.subTest(case=case_id):
                path=self.fixture(case_id,'seed')
                smoke=subprocess.run(['python3','-B','-m','unittest','discover','-s','tests','-v'],cwd=path,capture_output=True,text=True)
                self.assertEqual(smoke.returncode,0,smoke.stderr)
                result=corpus.grade_case(case_id,path,self.root/(case_id+'-seed-report'))
                self.results[case_id+'-seed']=result
                self.assertFalse(result['accepted'])
                self.assertLess(result['functional_score'],70)
                self.assertEqual(result['regression_score'],0)
                self.assertTrue(result['scope_ok'])

    def test_references_and_complete_tests_kill_all_valid_faults(self):
        for case_id in corpus.CASES:
            with self.subTest(case=case_id):
                path=self.fixture(case_id,'reference')
                for name,source in corpus.REFERENCE[case_id].items():
                    (path/name).write_text(source)
                (path/'tests/test_contract.py').write_text(complete_tests(case_id))
                result=corpus.grade_case(case_id,path,self.root/(case_id+'-reference-report'))
                self.results[case_id+'-reference']=result
                self.assertTrue(result['critical_pass'],result['failures'])
                self.assertTrue(result['authored_tests_pass'],result['checks'])
                self.assertTrue(result['accepted'])
                self.assertEqual(result['functional_score'],70)
                self.assertEqual(result['regression_score'],20,result['mutants'])
                self.assertTrue(all(result['mutants'].values()))
                for check in result['checks']:
                    if check['reason'].startswith('mutant_'):
                        self.assertNotIn('SyntaxError',check.get('stderr',''))
                        self.assertNotIn('ImportError',check.get('stderr',''))
                        self.assertNotIn('ModuleNotFoundError',check.get('stderr',''))

    def test_scope_is_a_hard_gate_and_no_tests_score_zero(self):
        path=self.fixture('tools','scope')
        (path/'tools.py').write_text(corpus.TOOLS)
        (path/'tests/test_contract.py').write_text(complete_tests('tools'))
        (path/'TASK.md').write_text('unauthorized change')
        result=corpus.grade_case('tools',path,self.root/'scope-report')
        self.assertFalse(result['scope_ok'])
        self.assertFalse(result['critical_pass'])
        self.assertFalse(result['accepted'])
        (path/'TASK.md').write_text(corpus.CASES['tools']['prompt'])
        (path/'tests/test_smoke.py').unlink()
        (path/'tests/test_contract.py').unlink()
        result=corpus.grade_case('tools',path,self.root/'no-tests-report')
        self.assertFalse(result['authored_tests_pass'])
        self.assertEqual(result['regression_score'],0)
        self.assertFalse(result['accepted'])

    def test_broken_import_does_not_kill_mutants(self):
        path=self.fixture('tools','broken')
        (path/'tools.py').write_text('raise ImportError("intentional fixture")\n')
        (path/'tests/test_contract.py').write_text(complete_tests('tools'))
        result=corpus.grade_case('tools',path,self.root/'broken-report')
        self.assertEqual(result['regression_score'],0)
        self.assertFalse(any(result['mutants'].values()))
        self.assertFalse(result['accepted'])

    def test_runtime_import_failures_are_not_mutation_credit(self):
        path=self.fixture('tools','runtime-import')
        (path/'tools.py').write_text(corpus.TOOLS)
        (path/'tests/test_contract.py').write_text(corpus.IMPORTS + "from tools import parse_tools\nclass InvalidMutationSignal(unittest.TestCase):\n def test_import_signal(self):\n  if parse_tools('ALL') != ['codex','claude','cursor']: raise ImportError('not an assertion')\n")
        result=corpus.grade_case('tools',path,self.root/'runtime-import-report')
        self.assertTrue(result['authored_tests_pass'])
        self.assertEqual(result['regression_score'],0,result['mutants'])
        self.assertFalse(any(result['mutants'].values()))

    def test_unchanged_seed_tests_are_runnable_but_not_authored(self):
        path=self.fixture('tools','unchanged-tests')
        (path/'tools.py').write_text(corpus.TOOLS)
        result=corpus.grade_case('tools',path,self.root/'unchanged-tests-report')
        self.assertTrue(result['critical_pass'])
        self.assertTrue(result['authored_tests_pass'])
        self.assertEqual(result['regression_score'],0)
        self.assertFalse(result['accepted'])
        self.assertIn('Task requires an added or changed test file',result['failures'])

    def test_python_caches_are_ignored_but_gitignore_is_protected(self):
        path=self.fixture('tools','caches')
        (path/'tools.py').write_text(corpus.TOOLS)
        (path/'tests/test_contract.py').write_text(complete_tests('tools'))
        (path/'__pycache__').mkdir()
        (path/'__pycache__/tools.cpython-fixture.pyc').write_bytes(b'cache')
        (path/'tools.pyo').write_bytes(b'cache')
        result=corpus.grade_case('tools',path,self.root/'caches-report')
        self.assertTrue(result['scope_ok'])
        self.assertTrue(result['accepted'])
        (path/'.gitignore').write_text('__pycache__/\n*.py[cod]\n*\n')
        result=corpus.grade_case('tools',path,self.root/'gitignore-report')
        self.assertFalse(result['scope_ok'])
        self.assertFalse(result['accepted'])

    def test_symlink_content_read_is_rejected_even_if_outputs_are_correct(self):
        path=self.fixture('ownership','link-read')
        # This fault preserves correct output and disk bytes, but reads a linked file.
        fault = corpus.OWNERSHIP + '''
_safe_plan = plan_install
def plan_install(root, desired, owned, backup=False):
    for name in desired:
        target=Path(root)/name
        if target.is_symlink():
            try:
                target.read_bytes()
            except Exception:
                pass
    return _safe_plan(root,desired,owned,backup)
'''
        (path/'installer.py').write_text(fault)
        result=corpus.grade_case('ownership',path,self.root/'link-read-report')
        self.assertFalse(result['functional_groups']['links'])
        self.assertFalse(result['critical_pass'])
        self.assertFalse(result['accepted'])

    def test_prepared_sources_and_tests_have_no_private_harness_imports(self):
        for case_id in corpus.CASES:
            with self.subTest(case=case_id):
                path=self.fixture(case_id,'exposure')
                expected=set(corpus.CASES[case_id]['allowed_files']) | {'tests/test_smoke.py','docs/agent-context.md','TASK.md','.gitignore'}
                actual={p.relative_to(path).as_posix() for p in path.rglob('*') if p.is_file() and '.git' not in p.relative_to(path).parts}
                self.assertEqual(actual,expected)
                for source in path.rglob('*.py'):
                    tree=ast.parse(source.read_text())
                    for node in ast.walk(tree):
                        if isinstance(node,ast.Import):
                            imported=[alias.name for alias in node.names]
                        elif isinstance(node,ast.ImportFrom):
                            imported=[node.module or '']
                        else:
                            continue
                        self.assertFalse(any(name.split('.')[0] in ('corpus','test_corpus') for name in imported))
                self.assertEqual((path/'.gitignore').read_text(),'__pycache__/\n*.py[cod]\n')

    def test_private_reports_cannot_be_inside_workspace(self):
        path=self.fixture('tools','report-path')
        with self.assertRaises(ValueError):
            corpus.grade_case('tools',path,path/'reports')

if __name__ == '__main__':
    unittest.main(verbosity=2)
