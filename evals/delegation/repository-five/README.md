# Repeated five-task C# repository benchmark

This experiment compares frozen baseline and quality-first Orchestra instructions
on five independent changes to the same .NET CLI snapshot. It is separate from
the earlier Python and C# exploratory screens; those samples are not pooled into
confirmation. Benchmark feature solutions stay in disposable repositories.

| Task | Purpose |
| --- | --- |
| option-validation | Reject inappropriate flags before side effects; small-work control |
| state-validation | Diagnose and reject semantically invalid ownership state safely |
| status-json | Add deterministic machine-readable status without breaking text output |
| preview-execution | Share typed installation decisions between preview and execution |
| verify-installed | Add read-only rendered-asset verification with text/JSON reporting |

Exact contracts, held-out C# statement groups, reference patches and ten compiled
faults are in `harness/cases.json`. The grader constructs C# console programs in
private temporary directories. Neither hidden checks nor references are placed
in actor workspaces. Every task starts independently; no implementation carries
into another task or repetition. Source starts at commit
`3ad9d38943085290b1d5dcbaf2e7e49995a540ab`. Identical protected build fixtures
disable shared compilation and NuGet auditing and clear package sources; no
packages, SDKs, models or real user configuration are installed/changed.

## Frozen design

Five tasks × three repetitions × two instruction sets = **30 planned trials**.
A seeded schedule shuffles task order per repetition and balances which arm runs
first (eight baseline-first pairs, seven candidate-first pairs). Pairs are
adjacent and all inference runs serially to avoid benchmark-induced contention.
Models, role templates, prompts, code, grading and budgets are fixed before the
fresh run. Every call checks the frozen manifest again. There are no automatic
retries or discarded attempts. Missing evidence or a candidate acceptance failure
stops the run, preserving evidence and preventing a confirmation claim.

Targets: quality delta at least -0.25 **points**, throughput ratio at least 1.10,
and tokens per accepted task ratio at most 1.10. Correctness, authored-test,
scope, no-regression and complete-native-accounting gates remain mandatory.
Quality is 70 held-out behavior +20 authored-test fault detection +10 anchored
maintainability. All accepted attempts receive an actual parent assessment using
the same five 0/1/2 criteria; failed attempts score zero. A separate manual
contract gate covers requirements that need source inspection, including the
shared typed plan. Arm labels are obscured where practical; the parent knows
the experiment context, so this is not an independent blind quality study.

The analysis uses 20,000 deterministic hierarchical paired bootstrap samples:
resample five task clusters, then three paired repetitions within each drawn
task, with the same sampled units in both arms. Bonferroni-adjusted 98.333%
individual percentile intervals provide approximate simultaneous 95% protection
for the three metrics. Confirmation requires the entire relevant interval to
meet each target, not merely a favorable mean. Five task clusters are few;
tail estimates are fragile. Conclusions apply to these sampled Orchestra C#
tasks and conditions, not all engineering work or human productivity.

Elapsed time includes native task start, coordination, checks, authorized review,
rework and independent functional/scope/existing-suite validation. Offline fault
scoring and maintainability assessment are separate evaluation overhead. Native
parent/child counters are reconciled and summed once; cached input and reasoning
are subsets. Tokens are not monetary cost or unique prompt bytes.

## Validation and isolation

Before inference, all five references passed the existing .NET suites and all
15 hidden groups. All ten deliberately faulty implementations compiled and were
caught at runtime. The integrated grader then rejected every unfinished starter
and gave each complete reference full functional and fault-detection scores.
The measurement helper has 14 passing deterministic tests, including a full
20,000-sample heterogeneous bootstrap case.

Held-out fixtures inject a fake HTTP handler and a forbidden executable-replacement
callback; they assert zero HTTP attempts. All homes, state, project roots and
filesystem changes are disposable. Native auth is linked only into isolated
trial homes and removed afterward. Raw native logs remain private.

An initial trial was cancelled before grading when the missing explicit HTTP
guard was noticed. It consumed 243,986 native tokens and 99.16 agent seconds.
Its evidence remains diagnostic overhead; it is not silently dropped from a
claimed successful comparison. The offline guard was then added, every reference
and starter was revalidated, and a fresh output directory/protocol was frozen.
Task contracts, instructions, models and thresholds were unchanged.

## Reuse

`harness/experiment.py` reuses the existing `../runner.py` implementation by
copying it into an external run directory's `harness/` beside these five files.
Supply `harness/settings.json` with `{ "source": "/path/to/frozen-source" }`,
prepare the shared source snapshot/build fixtures and independent instruction
arm directories, and resolve the paths in `protocol.example.json` into a private
`protocol.json` one level above `harness/`. Freeze the exact reviewed schedule
and all inputs before inference. Keep the run directory outside source trees.

```sh
python3 -B /path/to/run/harness/experiment.py
python3 -B /path/to/run/harness/experiment.py --run
python3 -B /path/to/run/harness/experiment.py --summarize
```

The default only plans. Run uses the signed-in native account. Summarize makes no
model calls. Supply actual review records in the output's `reviews.json`:
`{ "trial-id": { "criteria": [2,2,2,2,2], "reviewer": "evaluator", "rationale": "criterion-specific findings", "contract_pass": true } }`.
Those scores are examples, never defaults. Preserve original native results;
manual contract failures are recorded as unaccepted work in derived analysis.
No script commits, pushes, publishes or integrates benchmark feature changes.

## Outcome

[The frozen run stopped on a manual correctness-contract failure](results/report.md)
after two completed pairs; the next planned trial was cancelled. The status
candidate, calibration reference and starting source all miss an argument-value
edge that the baseline output handles. Automated calibration therefore did not
establish complete contract coverage. This corpus needs strengthening in a new
version before another confirmation attempt; its frozen cases were not changed
after inference. No repeated/general performance improvement is established.

The [argument-boundary v2 repair](argument-boundary-v2/README.md) is additive and
passes focused offline calibration. It supplies explicit malformed-value rules,
a repaired reference and a readable C# matrix; frozen v1 inputs/results remain
unchanged. This is not a restarted performance benchmark.

## Authorized ten-trial v2 decision screen

The subsequent [ten-trial screen](ten-trial-v2/README.md) used the repaired status
case and one pair per task. It stopped after seven completed trials when the
baseline verification output violated the explicit no-follow requirement under
concurrent symlink replacement. The eighth attempt was interrupted and two never
started. A C# reproduction confirms the failure; all costs remain visible. This
incomplete screen does not authorize commit/push or establish general improvement.
