# Tiny C# comparison

One pure `UpdatePlanner.Decide` method, one baseline/candidate pair. The task,
authored tests, and held-out checks are C# console programs targeting .NET 10,
with no NuGet packages. Python only reuses the existing native-session and
measurement orchestration; this does not introduce another model harness.

The revised mini-screen requirements are frozen before inference: no critical
functional regression, at most **0.25 points** lower mean quality on the existing
100-point rubric, at least 10% higher throughput, and no more than 10% higher
tokens per accepted task. Greater token savings are welcome. This replaces the
previous +10% quality objective for this mini-screen, not the historical result.
One pair is exploratory and cannot establish repeatability or authorize push.

Five held-out groups cover missing content, identical bytes, owned hashes,
modified/unowned content and backup, and null handling/input immutability.
Authored C# tests are also run against four independently compiled faults:
wrong identical-content decision, rejected ownership, ignored backup, and
unauthorized update. Compiler failures earn no fault-detection credit.

The fixture is intentionally small enough for direct parent work. It is useful
for inspecting C# output and process overhead, not measuring parallel work on a
complex repository task. Both arms use the same frozen models and role templates.

To reuse the existing runner with this corpus, copy the unchanged runner and
this module into an external harness directory, then run a single screen:

```sh
mkdir -p /tmp/csharp-mini-harness
cp evals/delegation/runner.py /tmp/csharp-mini-harness/runner.py
cp evals/delegation/csharp-mini/corpus.py /tmp/csharp-mini-harness/corpus.py
python3 -B /tmp/csharp-mini-harness/runner.py --baseline /path/to/frozen-baseline --candidate /path/to/candidate --output /tmp/csharp-mini-results --screen --codex /path/to/native/codex --timeout 300 --grading-timeout 180 --run
```

The reused runner retains its historical +10% quality gate in its raw summary.
Assess this mini-screen using the explicitly revised tolerance above, preserve
both decisions, and never relabel historical results. Maintainability still
requires the same five anchored criteria with a recorded human/evaluator
assessment; missing evidence blocks a pass.

[The first tiny C# comparison](results/comparison.md) passed correctness and
quality tolerance, but missed speed and token targets. It includes both C#
implementations and executable authored checks for direct inspection.
