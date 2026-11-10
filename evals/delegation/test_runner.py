"""Deterministic runner checks: no model requests or real authentication access."""
import asyncio
import contextlib
import copy
import io
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch, AsyncMock

import runner


def row(arm='baseline', rep=1, elapsed=10, tokens=100, accepted=True, case='tools'):
    return {'trial_id': f'{case}-r{rep}-{arm}-a1', 'arm': arm, 'case': case, 'coding': True,
            'repetition': rep, 'attempt': 1, 'elapsed_seconds': elapsed, 'total_tokens': tokens,
            'accepted': accepted, 'complete': True, 'usage_complete': True, 'errors': [],
            'grade': {'critical_pass': True, 'functional_score': 70, 'regression_score': 20,
                      'functional_groups': {'heldout': True}}}


def reviews(rows, criteria=None):
    return {r['trial_id']: {'criteria': criteria or [2] * 5, 'reviewer': 'human',
                           'rationale': 'Each frozen criterion inspected.'} for r in rows}


def fixture(root):
    (root / 'global/codex/agents').mkdir(parents=True)
    (root / 'skills/local').mkdir(parents=True)
    (root / 'global/codex/AGENTS.md').write_text('Isolated workflow.\n')
    (root / 'models.conf').write_text('CODEX_ORCHESTRATOR_MODEL=test-root\nCODEX_WORKER_MODEL=test-worker\n')
    for role in runner.ROLES:
        (root / f'global/codex/agents/workflow_{role}.toml.tmpl').write_text('model = "@@CODEX_WORKER_MODEL@@"\nmodel_reasoning_effort = "medium"\n')
    (root / 'skills/local/SKILL.md').write_text('Local skill.\n')


class MetricsTests(unittest.TestCase):
    def test_throughput_ten_percent_means_dividing_time_by_one_point_one(self):
        rows = [row(), row('candidate', elapsed=10 / 1.1)]
        result = runner.summarize(rows, reviews(rows), screen=True)
        self.assertAlmostEqual(result['ratios']['throughput'], 1.1)
        self.assertTrue(result['gates']['throughput_target'])
        self.assertFalse(result['targets_passed'])
        self.assertIn('NONCONFIRMATORY', result['mode'])

    def test_ten_percent_time_reduction_is_different(self):
        rows = [row(), row('candidate', elapsed=9)]
        result = runner.summarize(rows, reviews(rows))
        self.assertAlmostEqual(result['ratios']['throughput'], 1 / .9)

    def test_zero_accepted_and_missing_usage_are_unevaluable(self):
        rows = [row(accepted=False), row('candidate', accepted=False)]
        rows[0]['usage_complete'] = False
        rows[0]['total_tokens'] = None
        result = runner.summarize(rows, reviews(rows))
        self.assertIsNone(result['ratios']['tokens'])
        self.assertIsNone(result['ratios']['throughput'])
        self.assertFalse(result['gates']['native_usage_complete'])
        self.assertFalse(result['targets_passed'])

    def test_failure_penalizes_all_denominators(self):
        rows = [row(), row('candidate', elapsed=5), row('candidate', elapsed=5, accepted=False)]
        rows[-1]['trial_id'] += '-retry'
        result = runner.summarize(rows, reviews(rows))
        candidate = result['arms']['candidate']
        self.assertEqual(candidate['acceptance_rate'], .5)
        self.assertEqual(candidate['quality'], 50)
        self.assertEqual(candidate['tokens_per_accepted_task'], 200)
        self.assertEqual(candidate['throughput'], .1)
        self.assertFalse(result['gates']['acceptance_not_lower'])

    def test_missing_manual_review_never_awards_full_points(self):
        rows = [row(), row('candidate')]
        result = runner.summarize(rows)
        self.assertIsNone(result['arms']['baseline']['quality'])
        self.assertFalse(result['gates']['quality_verified'])
        self.assertIsNone(result['ratios']['quality'])

    def test_manual_review_requires_five_integer_anchors_and_identity(self):
        rows = [row(), row('candidate')]
        review = reviews(rows)
        review[rows[0]['trial_id']]['criteria'] = [2, 2, 2, 2, 2.0]
        self.assertFalse(runner.summarize(rows, review)['gates']['quality_verified'])
        review = reviews(rows)
        review[rows[0]['trial_id']]['reviewer'] = ''
        self.assertFalse(runner.summarize(rows, review)['gates']['quality_verified'])

    def test_regressions_and_critical_failures_are_hard_gates(self):
        rows = [row(), row('candidate')]
        rows[1]['grade']['functional_groups']['heldout'] = False
        rows[1]['grade']['critical_pass'] = False
        result = runner.summarize(rows, reviews(rows))
        self.assertFalse(result['gates']['candidate_critical'])
        self.assertFalse(result['gates']['no_heldout_regression'])
        self.assertEqual(len(result['heldout_regressions']), 1)

    def test_paired_retries_cannot_hide_group_regression(self):
        rows = [row(), row('candidate'), row('candidate')]
        rows[-1]['trial_id'] += '-retry'
        rows[1]['grade']['functional_groups']['heldout'] = False
        self.assertFalse(runner.summarize(rows, reviews(rows))['gates']['no_heldout_regression'])

    def test_ceiling_reported_without_relaxing_target(self):
        rows = [row(), row('candidate')]
        result = runner.summarize(rows, reviews(rows))
        self.assertTrue(result['baseline_quality_ceiling'])
        self.assertFalse(result['gates']['quality_target'])

    def test_quality_ratio_is_multiplicative_and_macro_all_attempts(self):
        rows = [row(rep=n) for n in range(1, 4)] + [row('candidate', rep=n, elapsed=8) for n in range(1, 4)]
        for r in rows[:3]:
            r['grade']['functional_score'] = 50
        result = runner.summarize(rows, reviews(rows))
        self.assertEqual(result['arms']['baseline']['quality'], 80)
        self.assertEqual(result['ratios']['quality'], 1.25)
        self.assertTrue(result['targets_passed'])
        self.assertEqual(result['paired_time_ratio_variability']['n'], 3)

    def test_missing_expected_pair_blocks_confirmation(self):
        rows = [row(rep=n) for n in range(1, 4)] + [row('candidate', rep=n, elapsed=8) for n in range(1, 4)]
        result = runner.summarize(rows, reviews(rows), expected=runner.expected_trials(['tools', 'dag'], 3))
        self.assertFalse(result['gates']['paired_design_complete'])


class UsageTests(unittest.TestCase):
    def test_sum_each_thread_once_cached_and_reasoning_are_subsets(self):
        event = {'totalTokens': 110, 'inputTokens': 100, 'cachedInputTokens': 90,
                 'cacheWriteInputTokens': 0, 'outputTokens': 10, 'reasoningOutputTokens': 8}
        events = {'root': event, 'child': event}
        logs = {tid: {'usage': runner.normalize_usage(event), 'role': 'workflow_worker',
                      'runtime_contexts': [{'model': 'native-model', 'effort': 'medium'}]}
                for tid in events}
        records, complete, total, issues = runner.reconcile_usage({'root': {}, 'child': {}}, events, logs, 'root')
        self.assertTrue(complete, issues)
        self.assertEqual(total, 220)
        self.assertEqual(len(records), 2)

    def test_missing_rollout_or_runtime_or_mismatch_never_zero(self):
        event = {'totalTokens': 30}
        for logs in ({}, {'root': {'usage': {'total_tokens': 29}, 'runtime_contexts': [{'model': 'm', 'effort': 'medium'}]}}):
            _, complete, total, issues = runner.reconcile_usage({'root': {}}, {'root': event}, logs, 'root')
            self.assertFalse(complete)
            self.assertIsNone(total)
            self.assertTrue(issues)

    def test_rollout_only_descendant_is_discovered_but_incomplete(self):
        log = {'usage': {'total_tokens': 1}, 'role': 'workflow_worker',
               'runtime_contexts': [{'model': 'm', 'effort': 'medium'}]}
        records, complete, total, _ = runner.reconcile_usage({'root': {}}, {'root': {'totalTokens': 1}}, {'root': log, 'child': log}, 'root')
        self.assertIn('child', records)
        self.assertFalse(complete)
        self.assertIsNone(total)

    def test_inherited_metadata_and_contexts_do_not_replace_child_identity(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            home = root / 'home'
            home.mkdir()
            root_id, child_id = 'root', 'child'
            own_root = {'type': 'session_meta', 'payload': {'id': root_id}}
            own_child = {'type': 'session_meta', 'payload': {'id': child_id, 'agent_role': 'workflow_reviewer',
                'source': {'subagent': {'thread_spawn': {'parent_thread_id': root_id, 'agent_role': 'workflow_reviewer'}}}}}
            def context(turn, effort):
                return {'type': 'turn_context', 'payload': {'turn_id': turn, 'model': 'native-model', 'effort': effort}}
            def counter(total):
                return {'type': 'event_msg', 'payload': {'type': 'token_count',
                    'info': {'total_token_usage': {'total_tokens': total}}}}
            root_values = [own_root, context('root-turn', 'medium'), counter(743830)]
            # Child file starts with its own metadata, followed by inherited history.
            child_values = [own_child, own_root, context('root-turn', 'medium'), counter(743830),
                            context('child-turn1', 'high'), counter(300000), context('child-turn2', 'high'), counter(394751)]
            for name, values in [('root', root_values), ('child', child_values)]:
                (home / (name + '.jsonl')).write_text('\n'.join(json.dumps(v) for v in values))
            events = []
            for tid, turn in [(root_id, 'root-turn'), (child_id, 'child-turn1'), (child_id, 'child-turn2')]:
                for method in ('turn/started', 'turn/completed'):
                    events.append({'method': method, 'params': {'threadId': tid, 'turn': {'id': turn}}})
            events += [{'method': 'thread/tokenUsage/updated', 'params': {'threadId': tid,
                'tokenUsage': {'total': {'totalTokens': total}}}} for tid, total in [(root_id, 743830), (child_id, 394751)]]
            turn_ids = {}
            for event in events:
                runner.capture_turn_id(turn_ids, event)
            self.assertEqual(turn_ids[child_id], {'child-turn1', 'child-turn2'})
            logs = runner.native_logs(home, turn_ids)
            self.assertEqual(set(logs), {root_id, child_id})
            self.assertEqual(logs[child_id]['role'], 'workflow_reviewer')
            self.assertEqual(logs[child_id]['parent'], root_id)
            self.assertEqual([c['effort'] for c in logs[child_id]['runtime_contexts']], ['high', 'high'])
            self.assertEqual([c['turn_id'] for c in logs[child_id]['runtime_contexts']], ['child-turn1', 'child-turn2'])
            usage = {root_id: {'totalTokens': 743830}, child_id: {'totalTokens': 394751}}
            records, complete, total, issues = runner.reconcile_usage({root_id: {}, child_id: {}}, usage, logs, root_id)
            self.assertTrue(complete, issues)
            self.assertEqual(total, 1138581)
            self.assertEqual(len(records), 2)
            (root / 'result.json').write_text(json.dumps({'root': root_id}))
            (root / 'events.jsonl').write_text('\n'.join(json.dumps(e) for e in events))
            reanalysis = runner.reanalyse_native_usage(root)
            self.assertTrue(reanalysis['usage_complete'])
            self.assertEqual(reanalysis['total_tokens'], 1138581)
            # An authoritative map missing a child's own turns must not supply
            # inherited model/effort as if it were valid execution evidence.
            missing = runner.native_logs(home, {root_id: {'root-turn'}})
            self.assertEqual(missing[child_id]['runtime_contexts'], [])
            self.assertFalse(runner.reconcile_usage({}, usage, missing, root_id)[1])

    def test_log_duplicate_snapshots_are_not_summed(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            home = Path(tmp)
            for i, total in enumerate((20, 30)):
                values = [{'type': 'session_meta', 'payload': {'id': 'root'}},
                          {'type': 'turn_context', 'payload': {'model': 'm', 'effort': 'medium'}},
                          {'type': 'event_msg', 'payload': {'type': 'token_count', 'info': {'total_token_usage': {'total_tokens': total}}}}]
                (home / f'{i}.jsonl').write_text('\n'.join(json.dumps(x) for x in values))
            logs = runner.native_logs(home)
            self.assertEqual(logs['root']['usage']['total_tokens'], 30)
            self.assertEqual(len(logs['root']['log_paths']), 2)


class IsolationTests(unittest.TestCase):
    def test_manifest_freezes_and_refuses_drift(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            path = Path(tmp) / 'manifest.json'
            runner.freeze_manifest(path, {'a': 1})
            runner.freeze_manifest(path, {'a': 1})
            with self.assertRaisesRegex(ValueError, 'drift'):
                runner.freeze_manifest(path, {'a': 2})

    def test_manifest_records_actual_budgets_and_rejects_budget_drift(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            fixture(root / 'baseline')
            fixture(root / 'candidate')
            corpus_path = root / 'corpus.py'
            corpus_path.touch()
            corpus = types.SimpleNamespace(__file__=str(corpus_path), CASES={})
            args = (root / 'baseline', root / 'candidate', corpus, 1, True, 'nonexistent-selftest-codex')
            defaults = runner.make_manifest(*args)
            self.assertEqual(defaults['timeout_seconds'], 900)
            self.assertEqual(defaults['grading_timeout_seconds'], 300)
            frozen = runner.make_manifest(*args, timeout=600, grading_timeout=300)
            self.assertEqual(frozen['timeout_seconds'], 600)
            manifest_path = root / 'manifest.json'
            runner.freeze_manifest(manifest_path, frozen)
            for budgets in ({'timeout': 601, 'grading_timeout': 300}, {'timeout': 600, 'grading_timeout': 301}):
                with self.subTest(budgets=budgets), self.assertRaisesRegex(ValueError, 'drift'):
                    runner.freeze_manifest(manifest_path, runner.make_manifest(*args, **budgets))

    def test_plan_never_starts_process_or_reads_credentials(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            for name in ('baseline', 'candidate'):
                fixture(root / name)
            corpus_path = root / 'corpus.py'
            corpus_path.write_text('# deterministic corpus fixture\n')
            corpus = types.SimpleNamespace(__file__=str(corpus_path), CASES={'tools': {'prompt': 'task', 'coding': True, 'allowed_files': ['app.py']}})
            with patch.dict('sys.modules', corpus=corpus), patch('runner.asyncio.create_subprocess_exec', side_effect=AssertionError('model call')), patch('runner.subprocess.run', side_effect=AssertionError('process call')):
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    status = runner.main(['--baseline', str(root / 'baseline'), '--candidate', str(root / 'candidate'), '--output', str(root / 'output'), '--codex', 'nonexistent-selftest-codex'])
                self.assertEqual(status, 0)
                self.assertIn('plan only', output.getvalue())
                self.assertFalse((root / 'output').exists())

    def test_fixture_code_equal_and_auth_is_only_sibling_link(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            fixture(root / 'baseline')
            fixture(root / 'candidate')
            auth = root / 'auth-placeholder'
            auth.touch()
            def prepare(case, work):
                (work / 'app.py').write_text('pass\n')
            corpus = types.SimpleNamespace(prepare_case=prepare)
            homes = []
            for arm in ('baseline', 'candidate'):
                run = root / 'runs' / f'tools-r1-{arm}-a1'
                home, work, inventory = runner.prepare_trial(run, root / arm, root / 'baseline', 'tools', corpus, auth)
                homes.append(home)
                self.assertTrue((home / 'auth.json').is_symlink())
                self.assertNotIn('auth.json', runner.git(work, 'ls-files'))
                self.assertEqual(runner.git(work, 'branch', '--show-current').strip(), 'feat/delegation-eval')
                self.assertEqual(runner.git(work, 'status', '--porcelain'), '')
            for home in homes:
                (home / 'auth.json').unlink()

    def test_orphan_attempt_retained_and_unevaluable(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            (root / 'runs/tools-r1-baseline-a1').mkdir(parents=True)
            rows = runner.read_results(root)
            self.assertEqual(len(rows), 1)
            rows.append(row('candidate'))
            result = runner.summarize(rows, reviews(rows))
            self.assertEqual(result['arms']['baseline']['attempts'], 1)
            self.assertFalse(result['gates']['native_usage_complete'])
            self.assertFalse(result['targets_passed'])

    def test_canonical_fixture_trust_is_preseeded_without_weakening_inventory(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            fixture(root / 'source')
            (root / 'actual').mkdir()
            (root / 'alias').symlink_to(root / 'actual', target_is_directory=True)
            auth = root / 'source-auth-placeholder'
            auth.touch()
            def prepare(case, work):
                (work / 'app.py').write_text('pass\n')
            corpus = types.SimpleNamespace(prepare_case=prepare)
            home, work, inventory = runner.prepare_trial(root / 'alias/runs/tools-r1-baseline-a1',
                root / 'source', root / 'source', 'tools', corpus, auth)
            config = home / 'config.toml'
            original = config.read_text()
            expected_table = '[projects.' + json.dumps(str(work.resolve())) + ']'
            self.assertIn(expected_table + '\ntrust_level = "trusted"\n', original)
            self.assertNotIn('[projects.' + json.dumps(str(work)) + ']', original)
            # Simulate native ensure-trusted: only absent records are appended.
            if expected_table not in config.read_text():
                with config.open('a') as stream:
                    stream.write('\n' + expected_table + '\ntrust_level = "trusted"\n')
            self.assertEqual(config.read_text(), original)
            info = {'instructionSources': [str(home / 'AGENTS.md')]}
            self.assertEqual(runner.instruction_check(info, home, work, inventory)[1], [])
            for target in (config, home / 'roles/workflow_worker.toml'):
                with self.subTest(target=target.name):
                    before = target.read_text()
                    target.write_text(before.replace('test-root', 'changed-root').replace('test-worker', 'changed-worker'))
                    issues = runner.instruction_check(info, home, work, inventory)[1]
                    self.assertTrue(any('Installed instruction changed' in issue for issue in issues))
                    target.write_text(before)
            (home / 'auth.json').unlink()

    def test_failure_cleans_replaced_auth_file_and_persists_attempt(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            fixture(root / 'baseline')
            auth = root / 'source-auth-placeholder'
            auth.touch()
            def prepare(case, work):
                (work / 'app.py').write_text('pass\n')
            corpus = types.SimpleNamespace(prepare_case=prepare, CASES={'tools': {'coding': True, 'prompt': 'task'}})
            class FailingServer:
                def __init__(self, binary, home, work, run, timeout):
                    self.home, self.threads, self.usage, self.root, self.errors = home, {}, {}, None, []
                async def start(self):
                    (self.home / 'auth.json').unlink()
                    (self.home / 'auth.json').touch()  # Simulate refresh replacing link; no credentials.
                    raise RuntimeError('Deterministic startup failure')
                async def close(self):
                    pass
            args = types.SimpleNamespace(output=root / 'output', baseline=root / 'baseline',
                auth_source=auth, codex='unused', timeout=1, grading_timeout=1)
            grade = {'accepted': False, 'critical_pass': False, 'validation_seconds': 0, 'grading_seconds': 0}
            with patch('runner.AppServer', FailingServer), patch('runner.grade_external', AsyncMock(return_value=grade)):
                result = asyncio.run(runner.trial(args, corpus, 'baseline', 'tools', 1, 1, {}))
            run = root / 'output/runs/tools-r1-baseline-a1'
            self.assertFalse((run / 'home/auth.json').exists())
            self.assertTrue(auth.is_file())
            self.assertTrue((run / 'result.json').is_file())
            self.assertFalse(result['accepted'])
            self.assertFalse(result['usage_complete'])

    def test_assignment_template_drift_rejected(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            fixture(root / 'baseline')
            fixture(root / 'candidate')
            (root / 'candidate/models.conf').write_text('CODEX_ORCHESTRATOR_MODEL=changed\n')
            with self.assertRaisesRegex(ValueError, 'assignment/template differs'):
                runner.make_manifest(root / 'baseline', root / 'candidate', None, 3, False, 'codex')

    def test_foreign_instruction_sources_rejected(self):
        with tempfile.TemporaryDirectory(prefix='runner-selftest-') as tmp:
            root = Path(tmp)
            home, work = root / 'home', root / 'workspace'
            home.mkdir()
            work.mkdir()
            foreign = root / 'foreign.md'
            foreign.write_text('foreign instructions')
            _, issues = runner.instruction_check({'instructionSources': [str(foreign)]}, home, work, {'home': {}, 'workspace': {}})
            self.assertTrue(issues)


if __name__ == '__main__':
    unittest.main()
