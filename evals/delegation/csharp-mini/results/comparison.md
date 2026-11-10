# Tiny C# screen — 2026-11-10

One update-planning method, two model sessions, unchanged model assignments.
Both implementations passed every held-out check and their authored tests caught
all four compiled faults. Both received 100/100 under the existing quality rubric.

| Measurement | Baseline | Revised instructions | Outcome |
| --- | ---: | ---: | --- |
| Quality | 100 | 100 | Within the new 0.25-point tolerance |
| Time through independent validation | 69.93 s | 92.75 s | 32.6% longer; throughput down 24.6% |
| Native tokens, including cached input | 111,609 | 177,347 | 58.9% higher; guardrail missed |
| Subagents | 0 | 0 | Direct-work control |

**The mini-screen failed the speed and token requirements.** It is one pair,
with baseline first; it does not establish a repeatable performance difference.
The earlier favorable Python screen and this unfavorable C# screen both remain
recorded. No extra model trials were run to seek a favorable result, and nothing
was committed or pushed.

## Inspect the C#

- Baseline: [UpdatePlanner.cs](baseline/UpdatePlanner.cs), [Checks.cs](baseline/Checks.cs)
- Candidate: [UpdatePlanner.cs](candidate/UpdatePlanner.cs), [Checks.cs](candidate/Checks.cs)

Both use guard clauses and the same decision order. The baseline has a separate
`IsValidSha256` helper before comparing the calculated digest. The candidate
compares the digest directly, which also prevents malformed hashes from matching.
Both choices satisfy the contract; no material maintainability issue was found.
The separate validation can avoid calculating a digest for absent/invalid hashes.

The candidate added more C# checks after its first successful test run and reran
them successfully. Agent-phase time was 79.03 seconds versus 56.40 seconds;
independent validation was nearly identical at 13.72 versus 13.53 seconds.
The second test run accounts for roughly three seconds, not the entire gap.
Model generation and tool coordination are included in agent time. The evidence
does not identify how much the instruction change caused versus normal run
variation. No failed test or repair occurred in either arm.

The starter implementation was rejected in a grader self-check; a complete
reference solution passed all groups and killed all four faults. The tiny task,
authored checks and held-out programs use C#/.NET 10 with no NuGet dependencies.
The existing Python measurement engine was reused. The original historical
+10% quality gate remains in its raw output; [this mini-screen's protocol and
results](summary.json) apply the newly accepted 0.25-point tolerance separately.

Run either retained solution's authored checks with:

```sh
dotnet run --project evals/delegation/csharp-mini/results/baseline/MiniEval.csproj --configuration Release
dotnet run --project evals/delegation/csharp-mini/results/candidate/MiniEval.csproj --configuration Release
```

A parent assessment with opaque arm labels supplied the maintainability scores;
the experiment context was known. Human productivity and monetary cost remain
unverified. Native logs and disposable trial repositories are retained locally;
temporary auth links were removed. Neither arm used subagents, so this task adds
no evidence about parallel delegation on a larger C# change.
