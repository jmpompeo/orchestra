import copy
import unittest

from measurement import build_schedule, evaluate

CASES = ['a', 'b', 'c', 'd', 'e']


def fixture(speed=1.25, token=1.0):
    rows, reviews = [], {}
    for i, case in enumerate(CASES):
        for rep in range(1, 4):
            for arm in ('baseline', 'candidate'):
                tid = f'{case}-r{rep}-{arm}-a1'
                rows.append({'trial_id': tid, 'case': case, 'arm': arm, 'repetition': rep,
                             'attempt': 1, 'coding': True, 'complete': True, 'errors': [],
                             'accepted': True, 'usage_complete': True,
                             'elapsed_seconds': (10 + i + rep) / (speed if arm == 'candidate' else 1),
                             'total_tokens': int((1000 + 100 * i + 10 * rep) * (token if arm == 'candidate' else 1)),
                             'grade': {'functional_score': 70, 'regression_score': 20,
                                       'critical_pass': True, 'scope_ok': True, 'authored_tests_pass': True,
                                       'accepted': True, 'functional_groups': {'heldout': True}, 'mutants': {'one': True}}})
                reviews[tid] = {'criteria': [2] * 5, 'reviewer': 'anchored reviewer', 'rationale': 'All five criteria satisfied.'}
    return rows, reviews


class MeasurementTests(unittest.TestCase):
    def run_eval(self, rows=None, reviews=None, **kwargs):
        if rows is None:
            rows, reviews = fixture()
        return evaluate(rows, reviews, CASES, bootstrap_samples=500, **kwargs)

    def test_schedule_balanced_complete_deterministic(self):
        schedule = build_schedule(CASES)
        self.assertEqual(schedule, build_schedule(CASES))
        self.assertNotEqual(schedule, build_schedule(CASES, seed=42))
        self.assertEqual(15, len(schedule))
        self.assertEqual(8, sum(pair['order'][0] == 'baseline' for pair in schedule))
        for rep in range(1, 4):
            self.assertEqual(set(CASES), {p['case'] for p in schedule if p['repetition'] == rep})
        self.assertTrue(all(set(p['order']) == {'baseline', 'candidate'} for p in schedule))

    def test_confirmed_thresholds_and_determinism(self):
        result = self.run_eval(seed=8)
        self.assertEqual('confirmed', result['status'])
        self.assertEqual(result, self.run_eval(seed=8))
        self.assertEqual(15, len(result['pairs']))
        self.assertEqual(30, result['record_count'])
        self.assertAlmostEqual(1.25, result['point']['throughput_ratio'])
        self.assertAlmostEqual(1.0, result['point']['tokens_per_accepted_ratio'])
        self.assertEqual([.05 / 6, 1 - .05 / 6], result['bootstrap']['quantiles'])
        self.assertEqual(500, result['intervals']['quality_delta']['defined_replicates'])

    def test_identical_paired_units_have_degenerate_intervals(self):
        rows, reviews = fixture(speed=1)
        result = self.run_eval(rows, reviews)
        for metric, expected in [('quality_delta', 0), ('throughput_ratio', 1), ('tokens_per_accepted_ratio', 1)]:
            self.assertAlmostEqual(expected, result['intervals'][metric]['lower'])
            self.assertAlmostEqual(expected, result['intervals'][metric]['upper'])
        self.assertFalse(result['gates']['throughput_ratio_point'])

    def test_quality_uses_point_difference_not_historical_ratio(self):
        rows, reviews = fixture()
        for row in rows:
            if row['arm'] == 'candidate':
                row['grade']['regression_score'] = 19.75
        result = self.run_eval(rows, reviews)
        self.assertEqual('confirmed', result['status'])
        self.assertAlmostEqual(-.25, result['point']['quality_delta'])
        for row in rows:
            if row['arm'] == 'candidate':
                row['grade']['regression_score'] = 19.74
        self.assertFalse(self.run_eval(rows, reviews)['gates']['quality_delta_point'])

    def test_token_threshold_and_slow_candidate_reject(self):
        rows, reviews = fixture(speed=1.05, token=1.11)
        result = self.run_eval(rows, reviews)
        self.assertEqual('not_confirmed', result['status'])
        self.assertFalse(result['gates']['throughput_ratio_point'])
        self.assertFalse(result['gates']['tokens_per_accepted_ratio_point'])

    def test_missing_evidence_blocks_not_zero(self):
        for key, value in [('total_tokens', None), ('elapsed_seconds', None), ('errors', None), ('usage_complete', False)]:
            rows, reviews = fixture()
            rows[0][key] = value
            result = self.run_eval(rows, reviews)
            self.assertEqual('blocked', result['status'], key)
            self.assertFalse(result['confirmed'])
        rows, reviews = fixture()
        reviews.pop(rows[0]['trial_id'])
        result = self.run_eval(rows, reviews)
        self.assertEqual('blocked', result['status'])
        self.assertIsNone(result['aggregates']['baseline']['quality'])
        self.assertEqual(15, result['aggregates']['baseline']['accepted'])

    def test_malformed_grading_blocks_without_crashing(self):
        for grade in (None, [], {'functional_groups': None}):
            rows, reviews = fixture()
            rows[0]['grade'] = grade
            self.assertEqual('blocked', self.run_eval(rows, reviews)['status'])

    def test_failure_zero_and_all_costs_retained(self):
        rows, reviews = fixture()
        failed = rows[0]
        failed['accepted'] = False
        failed['grade']['accepted'] = False
        failed['grade']['critical_pass'] = False
        result = self.run_eval(rows, reviews)
        self.assertEqual('confirmed', result['status'])
        base = result['aggregates']['baseline']
        self.assertEqual(14, base['accepted'])
        self.assertAlmostEqual(1400 / 15, base['quality'])
        self.assertEqual(sum(r['total_tokens'] for r in rows if r['arm'] == 'baseline'), base['total_tokens'])
        self.assertEqual(sum(r['elapsed_seconds'] for r in rows if r['arm'] == 'baseline'), base['total_seconds'])

    def test_candidate_failure_and_heldout_regression_gate(self):
        rows, reviews = fixture()
        rows[1]['accepted'] = False
        rows[1]['grade']['accepted'] = False
        rows[1]['grade']['critical_pass'] = False
        rows[1]['grade']['functional_groups']['heldout'] = False
        result = self.run_eval(rows, reviews)
        self.assertFalse(result['gates']['candidate_checks_pass'])
        self.assertFalse(result['gates']['acceptance_not_lower'])
        self.assertEqual(1, len(result['heldout_regressions']))
        self.assertEqual(0, result['pairs'][0]['arms']['candidate']['quality'])

    def test_retry_duplicate_unexpected_are_nonconfirmatory_and_preserved(self):
        for mutate in ('retry', 'duplicate', 'unexpected'):
            rows, reviews = fixture()
            extra = copy.deepcopy(rows[0])
            if mutate == 'retry':
                extra['attempt'] = 2
                extra['trial_id'] += '-retry'
            if mutate == 'unexpected':
                extra['case'] = 'outside'
                extra['trial_id'] += '-outside'
            rows.append(extra)
            reviews[extra['trial_id']] = reviews[rows[0]['trial_id']]
            result = self.run_eval(rows, reviews)
            self.assertFalse(result['confirmatory_design'])
            self.assertFalse(result['confirmed'])
            self.assertEqual(16, result['aggregates']['baseline']['attempts'])
            self.assertEqual(31, result['record_count'])
            self.assertTrue(result['design_issues'])
            self.assertFalse(result['intervals'])

    def test_unknown_arm_accounting_is_preserved(self):
        rows, reviews = fixture()
        extra = copy.deepcopy(rows[0])
        extra['arm'] = 'outside'
        extra['trial_id'] += '-outside'
        rows.append(extra)
        reviews[extra['trial_id']] = reviews[rows[0]['trial_id']]
        result = self.run_eval(rows, reviews)
        self.assertFalse(result['confirmatory_design'])
        self.assertEqual(31, result['all_records_accounting']['attempts'])
        self.assertEqual(sum(r['total_tokens'] for r in rows), result['all_records_accounting']['total_tokens'])

    def test_zero_acceptance_and_zero_baseline_tokens_block(self):
        rows, reviews = fixture()
        for row in rows:
            row['total_tokens'] = 0
        self.assertEqual('blocked', self.run_eval(rows, reviews)['status'])
        rows, reviews = fixture()
        for row in rows:
            row['accepted'] = False
            row['grade']['accepted'] = False
        result = self.run_eval(rows, reviews)
        self.assertEqual('blocked', result['status'])
        self.assertIsNone(result['point']['throughput_ratio'])
        self.assertEqual(500, result['intervals']['throughput_ratio']['undefined_replicates'])

    def test_hierarchical_clusters_produce_nontrivial_bounds(self):
        rows, reviews = fixture(speed=1)
        for row in rows:
            if row['arm'] == 'candidate':
                # Task-specific effects make cluster resampling essential.
                row['elapsed_seconds'] /= [0.6, 0.8, 1, 2, 4][CASES.index(row['case'])]
        result = evaluate(rows, reviews, CASES, bootstrap_samples=20000, seed=77)
        bounds = result['intervals']['throughput_ratio']
        self.assertLess(bounds['lower'], result['point']['throughput_ratio'])
        self.assertGreater(bounds['upper'], result['point']['throughput_ratio'])
        self.assertLess(bounds['lower'], .9)
        self.assertGreater(bounds['upper'], 2)
        self.assertFalse(result['gates']['throughput_ratio_interval'])

    def test_validation_rejects_invalid_parameters(self):
        with self.assertRaises(ValueError):
            build_schedule(['a', 'a'])
        with self.assertRaises(ValueError):
            evaluate([], {}, CASES, bootstrap_samples=0)


if __name__ == '__main__':
    unittest.main()
