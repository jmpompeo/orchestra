# Five-task benchmark stopped before confirmation

The frozen run stopped at its manual correctness gate. Four of 30 planned trials
completed (two pairs); a fifth trial was cancelled when the contract failure was
confirmed. Twenty-five trials never started. There is no repeated dataset,
confidence interval or verified general speed/token/quality improvement.
Nothing was committed or pushed, and none of the benchmark feature solutions
was integrated into Orchestra.

## What failed

The status task explicitly required rejecting `--json` on other commands before
work. For this malformed invocation:

```text
orchestrate update --tools --json
```

The candidate calls the existing parser first:

```csharp
var options = CliOptions.Parse(args[1..]);
if (options.Json && command != "status")
    throw new ArgumentException("--json is only supported by status.");
```

That parser consumes `--json` as the value of `--tools`. `options.Json` remains
false, so the guard is skipped and execution reaches the downloader. Returning
exit 2 after a download error does not satisfy rejection before work.

The baseline scans for the new flag on non-status commands before parsing and
rejects the invocation. With an injected fake HTTP handler:

| Implementation | Exit code | HTTP attempts |
| --- | ---: | ---: |
| Baseline output | 2 | 0 |
| Candidate output | 2 | 1 |
| Calibration reference | 2 | 1 |
| Unchanged starting source | 2 | 1 |

**This underlying malformed-argument behavior already exists in the starting
CLI. The candidate failed to fulfill the new rejection guarantee; it did not
introduce the underlying parser behavior.** This is a correctness gap relative
to the paired baseline output, not proof the revised instructions generally
reduce correctness. No real HTTP request or executable replacement occurred.

## The evaluator missed it too

All automated held-out groups, original suites and fault-detection checks passed
for both status outputs. Parent source inspection identified the consumed-flag
edge, and the separately reproduced manual contract check failed the candidate.
The calibration reference also fails it, demonstrating incomplete oracle coverage.
Passing the calibration was not proof that the reference covered the full task.

The source-inspection gate and early correctness stop were frozen before
inference. The reproduction checks that existing before-work requirement; no new
hidden group, reference patch, prompt, model assignment, threshold or timing was
substituted in this run. Raw automated acceptance records remain immutable.
Derived analysis records the candidate as unaccepted, with zero accepted-work
quality. The incomplete design and missing assessment/grade for the cancelled
trial also block confirmation; no aggregate performance claim is made.

## Completed trial observations

| Pair | Baseline seconds / tokens | Candidate seconds / tokens | Manual acceptance |
| --- | ---: | ---: | --- |
| Option validation, repetition 1 | 160.00 / 769,867 | 150.89 / 430,879 | Both pass; quality 100 / 100 |
| JSON status, repetition 1 | 149.90 / 1,301,881 | 409.33 / 247,657 | Baseline passes; candidate misses before-work guarantee |

The status candidate's native stderr reports one WebSocket idle timeout and
internal retry. All 409.33 seconds remain counted; the transport delay was not
subtracted, and the trial was not rerun to seek a better result. That timing
cannot be attributed solely to instruction changes. Its correctness gap is
separately reproducible without inference or network access.

The in-flight preview baseline was cancelled after the stop: 241.48 agent
seconds and 1,835,948 native tokens are retained as an interrupted planned
attempt, not a baseline implementation failure. All five started planned trials
consumed 4,586,232 reported native tokens in total, including cached input.
A separate earlier cancelled setup diagnostic consumed 243,986 tokens and
99.16 seconds before the explicit offline guard was added. All raw costs remain
visible; none are silently removed from a claimed successful comparison.

## Evidence and next experiment

- [C# reproduction](repro/Program.cs), using a counting fake HTTP handler.
- [Reproduction results](reproduction.json).
- [Baseline C# patch](status-json-baseline.patch) and [candidate C# patch](status-json-candidate.patch).
- [Sanitized run records, assessments and frozen input hashes](summary.json).

To run the reproduction against an isolated evaluated source tree:

```sh
dotnet run --project evals/delegation/repository-five/results/repro/Repro.csproj --configuration Release -p:SubjectRoot=/path/to/isolated/source
```

It succeeds only if the invocation returns exit 2 before HTTP attempts or output.
Its handler returns a fake 404 if called; no real request is sent. The checked
patches reconstruct the implementations from the recorded source commit.

Before another confirmation attempt, make the flag/value interpretation explicit
in the task, strengthen invalid-argument boundary checks, and validate the
reference against them. That must be a new version and fresh experiment. Preserve
this failed result. The workflow should require evidence for explicit
"before side effects" promises, rather than treating a passing happy-path suite
as complete acceptance. This finding does not by itself establish that mandatory
model review is necessary or that review removal caused the gap.

The retained corpus is useful evidence and scaffolding, **not a fully validated
confirmation corpus**. Real engineering productivity and monetary cost remain
unverified. The deliverable worktree, frozen source/instruction snapshots, native
logs, reference and reproduction evidence remain local for revisiting the result.
Temporary trial auth paths are removed and owned inference processes have ended.

## Final validation and retention

All 14 measurement tests pass from the published location. The standalone C#
reproduction was run against all four source variants and returned exactly the
outcomes above. Parent inspected the deliverable changes and `git diff --check`
passes. The application and its embedded instructions were not changed by this
benchmark slice, so the previously passing repository build/suite was not repeated;
the frozen case references each ran that suite during calibration. No independent
model review of the benchmark deliverable was requested.

Generated build/cache output for the published reproduction was removed. The
uncommitted deliverable worktree and failed-run source, native evidence,
calibration and reproduction records are retained for the next decision; this is
intentional retention of an interrupted experiment, not complete resource removal.
