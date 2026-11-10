"""Frozen five-task paired protocol; stdlib only, no inference or runner mutation.

Intervals describe this small sampled benchmark, not general engineering work.
All supplied attempts remain in accounting, including retries and failures.
"""
import collections
import math
import random
import statistics

ARMS = ('baseline', 'candidate')
DEFAULT_SEED = 20261110
LIMITS = {'quality_delta_min': -0.25, 'throughput_ratio_min': 1.10,
          'tokens_per_accepted_ratio_max': 1.10}


def build_schedule(cases, repetitions=3, seed=DEFAULT_SEED):
    """Freeze this result before inference. Every pair stays adjacent.

    Randomize task order in each repetition and allocate first arms in balanced
    allocation across the full schedule (15 pairs => 8/7 first arms).
    """
    cases = list(cases)
    if not cases or len(set(cases)) != len(cases):
        raise ValueError('Cases must be nonempty and unique')
    if type(repetitions) is not int or repetitions < 1:
        raise ValueError('Repetitions must be a positive integer')
    rng = random.Random(seed)
    n = len(cases) * repetitions
    first = ['baseline'] * ((n + 1) // 2) + ['candidate'] * (n // 2)
    rng.shuffle(first)
    schedule = []
    for rep in range(1, repetitions + 1):
        order = cases[:]
        rng.shuffle(order)
        for case in order:
            arm = first[len(schedule)]
            schedule.append({'case': case, 'repetition': rep,
                             'order': [arm, ARMS[1] if arm == ARMS[0] else ARMS[0]]})
    return schedule


def _number(value, minimum=0):
    return (type(value) in (int, float) and math.isfinite(value)
            and value >= minimum)


def _ratio(a, b):
    return a / b if a is not None and b is not None and b > 0 else None


def _review(value):
    if not isinstance(value, dict):
        return None
    c = value.get('criteria')
    if (isinstance(c, list) and len(c) == 5
            and all(type(v) is int and 0 <= v <= 2 for v in c)
            and isinstance(value.get('reviewer'), str) and value['reviewer'].strip()
            and isinstance(value.get('rationale'), str) and value['rationale'].strip()):
        return sum(c)
    return None


def _grade(row):
    value = row.get('grade')
    return value if isinstance(value, dict) else {}


def _groups(row):
    value = _grade(row).get('functional_groups')
    return value if isinstance(value, dict) else {}


def _aggregate(rows):
    accepted = sum(r['accepted'] is True for r in rows)
    time_ok = bool(rows) and all(_number(r.get('elapsed_seconds')) and r['elapsed_seconds'] > 0 for r in rows)
    tokens_ok = bool(rows) and all(r.get('usage_complete') is True and type(r.get('total_tokens')) is int and r['total_tokens'] >= 0 for r in rows)
    quality_ok = bool(rows) and all(r['_score'] is not None for r in rows)
    seconds = sum(r['elapsed_seconds'] for r in rows) if time_ok else None
    tokens = sum(r['total_tokens'] for r in rows) if tokens_ok else None
    return {'attempts': len(rows), 'accepted': accepted,
            'acceptance_rate': _ratio(accepted, len(rows)),
            'total_seconds': seconds, 'total_tokens': tokens,
            'known_seconds': sum(r['elapsed_seconds'] for r in rows if _number(r.get('elapsed_seconds'))),
            'known_tokens': sum(r['total_tokens'] for r in rows if type(r.get('total_tokens')) is int and r['total_tokens'] >= 0),
            'throughput': _ratio(accepted, seconds),
            'tokens_per_accepted_task': _ratio(tokens, accepted),
            'quality': statistics.mean(r['_score'] for r in rows) if quality_ok else None,
            'time_complete': time_ok, 'usage_complete': tokens_ok, 'quality_complete': quality_ok}


def _metrics(base, candidate):
    return {'quality_delta': candidate['quality'] - base['quality'] if candidate['quality'] is not None and base['quality'] is not None else None,
            'throughput_ratio': _ratio(candidate['throughput'], base['throughput']),
            'tokens_per_accepted_ratio': _ratio(candidate['tokens_per_accepted_task'], base['tokens_per_accepted_task'])}


def _quantile(values, p):
    ordered = sorted(values)
    position = (len(ordered) - 1) * p
    lo = math.floor(position)
    hi = math.ceil(position)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (position - lo)


def _bootstrap(pairs, cases, repetitions, samples, seed):
    rng = random.Random(seed)
    draws = {key: [] for key in ('quality_delta', 'throughput_ratio', 'tokens_per_accepted_ratio')}
    undefined = {key: 0 for key in draws}
    for _ in range(samples):
        rows = {arm: [] for arm in ARMS}
        for case in rng.choices(cases, k=len(cases)):
            for rep in rng.choices(range(1, repetitions + 1), k=repetitions):
                for arm in ARMS:
                    rows[arm].append(pairs[(case, rep)][arm][0])
        metrics = _metrics(*[_aggregate(rows[arm]) for arm in ARMS])
        for key, value in metrics.items():
            if value is None:
                undefined[key] += 1
            else:
                draws[key].append(value)
    # Family-wise alpha=.05 across three metrics; each interval is 98 1/3%.
    tail = 0.05 / 6
    return {key: {'lower': _quantile(values, tail) if values and not undefined[key] else None,
                  'upper': _quantile(values, 1 - tail) if values and not undefined[key] else None,
                  'undefined_replicates': undefined[key], 'defined_replicates': len(values)}
            for key, values in draws.items()}


def evaluate(results, reviews, expected_cases, repetitions=3,
             bootstrap_samples=20000, seed=DEFAULT_SEED):
    """Assess exactly 5 x 3 pairs. Extra attempts stay visible, nonconfirmatory.

    Missing evidence blocks verification; failed attempts with complete evidence
    contribute zero quality and still charge all time and tokens. No undefined
    bootstrap samples are silently removed when constructing an interval.
    """
    cases = list(expected_cases)
    if len(cases) != len(set(cases)) or not cases:
        raise ValueError('Expected cases must be nonempty and unique')
    if type(repetitions) is not int or repetitions < 1:
        raise ValueError('Repetitions must be a positive integer')
    if type(bootstrap_samples) is not int or bootstrap_samples < 1:
        raise ValueError('Bootstrap sample count must be a positive integer')
    issues, design_issues, rows = [], [], []
    pairs = collections.defaultdict(lambda: collections.defaultdict(list))
    ids = collections.Counter()
    for index, original in enumerate(results):
        row = dict(original)
        label = row.get('trial_id', 'record-' + str(index))
        ids[label] += 1
        if not row.get('trial_id'):
            issues.append(f'{label}: missing trial_id')
        if row.get('arm') not in ARMS:
            design_issues.append(f'{label}: unexpected arm {row.get("arm")}')
        if row.get('case') not in cases or type(row.get('repetition')) is not int or row['repetition'] not in range(1, repetitions + 1):
            design_issues.append(f'{label}: unexpected case/repetition')
        if type(row.get('attempt')) is not int or row.get('attempt') != 1:
            design_issues.append(f'{label}: not the single planned first attempt')
        if type(row.get('accepted')) is not bool:
            issues.append(f'{label}: missing accepted decision')
        row.setdefault('accepted', False)
        if row.get('coding') is not True:
            issues.append(f'{label}: coding evidence missing or unexpected noncoding task')
        if row.get('complete') is not True or not isinstance(row.get('errors'), list) or row.get('errors'):
            issues.append(f'{label}: incomplete attempt or errors/missing error evidence')
        if not (_number(row.get('elapsed_seconds')) and row['elapsed_seconds'] > 0):
            issues.append(f'{label}: invalid/missing elapsed time')
        if row.get('usage_complete') is not True or type(row.get('total_tokens')) is not int or row['total_tokens'] < 0:
            issues.append(f'{label}: invalid/missing native token counters')
        manual = _review(reviews.get(label))
        grade = _grade(row)
        if manual is None:
            issues.append(f'{label}: invalid/missing anchored manual review')
        grade_ok = (isinstance(grade, dict)
                    and _number(grade.get('functional_score')) and grade['functional_score'] <= 70
                    and _number(grade.get('regression_score')) and grade['regression_score'] <= 20
                    and all(type(grade.get(key)) is bool for key in ('critical_pass', 'scope_ok', 'authored_tests_pass', 'accepted'))
                    and isinstance(grade.get('functional_groups'), dict) and bool(grade['functional_groups'])
                    and all(type(v) is bool for v in grade['functional_groups'].values()))
        if not grade_ok:
            issues.append(f'{label}: invalid/missing grading evidence')
        row['_score'] = (0 if row['accepted'] is False else grade['functional_score'] + grade['regression_score'] + manual) if manual is not None and grade_ok else None
        if row['accepted'] is True and grade_ok and not all(grade[key] for key in ('critical_pass', 'scope_ok', 'authored_tests_pass', 'accepted')):
            issues.append(f'{label}: accepted decision contradicts grading')
        rows.append(row)
        pairs[(row.get('case'), row.get('repetition'))][row.get('arm')].append(row)
    for label, count in ids.items():
        if count > 1:
            design_issues.append(f'{label}: duplicate trial_id ({count} records retained)')
    expected = {(case, rep) for case in cases for rep in range(1, repetitions + 1)}
    if set(pairs) != expected:
        design_issues.append('Missing or unexpected paired units')
    if any(len(pairs[key].get(arm, [])) != 1 for key in expected for arm in ARMS):
        design_issues.append('Every planned pair must contain exactly one record per arm')
    design = not design_issues and len(cases) == 5 and repetitions == 3 and len(rows) == 30
    if len(cases) != 5 or repetitions != 3:
        design_issues.append('Confirmatory protocol requires five cases and three repetitions')
    aggregates = {arm: _aggregate([r for r in rows if r.get('arm') == arm]) for arm in ARMS}
    point = _metrics(*[aggregates[arm] for arm in ARMS])
    per_case, paired, regressions = {}, [], []
    for case in cases:
        arms = {arm: _aggregate([r for r in rows if r.get('case') == case and r.get('arm') == arm]) for arm in ARMS}
        per_case[case] = {'arms': arms, 'metrics': _metrics(*[arms[arm] for arm in ARMS])}
        for rep in range(1, repetitions + 1):
            group = pairs[(case, rep)]
            b, c = group.get('baseline', []), group.get('candidate', [])
            passed = {key for r in b for key, value in _groups(r).items() if value is True}
            for row in c:
                for key in sorted(passed):
                    if _groups(row).get(key) is not True:
                        regressions.append({'case': case, 'repetition': rep, 'group': key, 'trial_id': row.get('trial_id')})
            ba, ca = _aggregate(b), _aggregate(c)
            paired.append({'case': case, 'repetition': rep, 'arms': {'baseline': ba, 'candidate': ca},
                           'metrics': _metrics(ba, ca), 'time_ratio': _ratio(ca['total_seconds'], ba['total_seconds']),
                           'token_ratio': _ratio(ca['total_tokens'], ba['total_tokens'])})
    candidates = [r for r in rows if r.get('arm') == 'candidate']
    gates = {'candidate_checks_pass': bool(candidates) and all(r['accepted'] is True and r.get('complete') is True and not r.get('errors')
                  and all(_grade(r).get(k) is True for k in ('accepted', 'critical_pass', 'scope_ok', 'authored_tests_pass'))
                  and bool(_groups(r))
                  and all(v is True for v in _groups(r).values()) for r in candidates),
             'no_baseline_passed_group_regression': not regressions,
             'acceptance_not_lower': aggregates['baseline']['acceptance_rate'] is not None and aggregates['candidate']['acceptance_rate'] is not None and aggregates['candidate']['acceptance_rate'] >= aggregates['baseline']['acceptance_rate'],
             'exact_design': design, 'evidence_complete': not issues}
    intervals = _bootstrap(pairs, cases, repetitions, bootstrap_samples, seed) if design and not issues else {}
    for key, threshold, direction in [('quality_delta', -0.25, 'lower'), ('throughput_ratio', 1.1, 'lower'), ('tokens_per_accepted_ratio', 1.1, 'upper')]:
        value = point[key]
        bound = intervals.get(key, {}).get(direction)
        gates[key + '_point'] = value is not None and (value >= threshold if direction == 'lower' else value <= threshold)
        gates[key + '_interval'] = bound is not None and (bound >= threshold if direction == 'lower' else bound <= threshold)
    undefined = any(v['undefined_replicates'] for v in intervals.values())
    if undefined or any(value is None for value in point.values()):
        issues.append('Undefined ratio: zero acceptance or zero baseline token rate; verification blocked')
    gates['evidence_complete'] = not issues
    worst = {}
    for metric in point:
        values = [(case, summary['metrics'][metric]) for case, summary in per_case.items() if summary['metrics'][metric] is not None]
        worst[metric] = None
        if values:
            item = (max if metric == 'tokens_per_accepted_ratio' else min)(values, key=lambda item: item[1])
            worst[metric] = {'case': item[0], 'value': item[1]}
    confirmed = all(gates.values())
    return {'status': 'blocked' if issues else 'confirmed' if confirmed else 'not_confirmed',
            'confirmed': confirmed, 'confirmatory_design': design, 'record_count': len(rows),
            'issues': issues, 'design_issues': design_issues, 'gates': gates,
            'aggregates': aggregates, 'all_records_accounting': _aggregate(rows),
            'point': point, 'per_case': per_case,
            'pairs': paired, 'worst_cases': worst, 'heldout_regressions': regressions,
            'intervals': intervals, 'thresholds': dict(LIMITS),
            'bootstrap': {'samples': bootstrap_samples, 'seed': seed, 'method': 'hierarchical paired task-cluster then paired repetition percentile bootstrap',
                          'family_wise_confidence': 0.95, 'individual_confidence': 1 - 0.05 / 3,
                          'quantiles': [0.05 / 6, 1 - 0.05 / 6]},
            'limitations': ['Only five task clusters and three paired repetitions; bootstrap tail estimates are fragile.',
                            'Results apply to these sampled tasks and conditions; no universal or general engineering claim.']}
