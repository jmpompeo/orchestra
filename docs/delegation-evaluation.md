# Quality-first delegation evaluation

Improve correct code and time to an independently validated result. Token use
is a guardrail, not the primary objective. Requirements are relative to a frozen
baseline of the current working instructions, including task-owned uncommitted
changes; this is distinct from comparing only with the Git base commit.

| Requirement | Measurement | Passing condition |
| --- | --- | --- |
| Code quality improves by 10% | Mean coding-task score: 70 functional + 20 regression strength + 10 anchored maintainability; failed attempts score zero | Candidate / baseline >= 1.10 |
| Completion speed improves by 10% | Accepted coding tasks / total elapsed seconds through independent functional validation, including failures and rework | Candidate / baseline >= 1.10 |
| Tokens remain controlled | Total parent and descendant tokens / accepted coding tasks, including failed attempts | Candidate / baseline <= 1.10 |

The token condition uses the agreed upper guardrail: greater savings are welcome.
A 10% throughput increase means dividing equivalent completion time by 1.10,
not subtracting 10% from time. Quality is a defined code-quality proxy, not a
claim to measure every aspect of engineering quality or developer productivity.

## Hard gates

Every candidate must pass critical behavior, interface, scope, and authored-test
checks. No functional group passed by a paired baseline may regress; acceptance
rate must not decrease. Faster incorrect output never passes. Complete native
usage, timing, instruction-isolation evidence, and manual quality assessments
are required. Missing information is unverified, not zero or a perfect score.

If baseline quality exceeds 90.909/100, a relative 10% increase is impossible on
this scale. Report that bound and the next decision without weakening the target
or substituting a different metric after inspecting results.

## Executable coding cases

The suite lives in `evals/delegation/`. All cases use Python's standard library
and disposable repositories; no real installation, credentials, external service
mutation, or production data is part of a coding task. Contracts are explicit in
TASK.md. Hidden functional fixtures remain outside the task workspace.

| Case | Behaviors checked | Delegation evidence |
| --- | --- | --- |
| tools | Commas/semicolons, normalization, ordering, duplicates, all, invalid input, stable interface | Small direct-work control; report any unnecessary child agents |
| ownership | Recorded hash matching, modified/unowned conflicts, explicit backup planning, regular files, traversal, links and linked ancestors; no link-target reads or filesystem mutation | Causal investigation and verification under strict safety constraints |
| dag | Upfront graph validation, dependency ordering, bounded overlap, failed descendants, independent progress, deterministic output | Task complexity and safe concurrency; no credit for merely spawning agents |
| bundle | Model replacement and validation, exact LF/CRLF frontmatter removal, integrated output and unchanged inputs/interfaces | Independent modules permit distinct worker ownership and central integration |

All four behavior groups per case are critical. An added or changed runnable
regression test is required. Routine ignored Python bytecode is permitted;
changes outside named source files and tests are rejected. Agent-written tests
alone are insufficient: the evaluator runs separate functional checks.

Regression strength is the fraction of 21 predefined valid runtime faults that
the authored tests detect, weighted equally within each case. Faults include
lost semicolon support, overwritten modified content, accepted links, serialized
execution, wrong failure propagation, unresolved model names, lost CRLF bytes,
and accepted unterminated metadata. Import/syntax failures earn no mutation
credit. Trusted complete solutions must pass every group and kill every mutant;
seeded faulty solutions must fail despite passing their weak smoke tests.

## Maintainability rubric

Assess patches with arm labels hidden where practical. Score each criterion
0, 1, or 2 and record the rationale and source locations:

1. Clear contracts, names, and absence of hidden assumptions.
2. Cohesive functions and appropriate abstraction boundaries.
3. Explicit errors, state, and resource ownership.
4. Contained changes without duplicate logic or needless complexity.
5. Readable behavior-focused regression tests.

Zero means a material issue or missing evidence; one means localized ambiguity
or avoidable complexity; two means no material issue found. The sum contributes
at most ten points. This is an evaluator assessment, never the task agent's
self-rating. Missing assessments prevent a quality claim. This evaluation does
not add mandatory model reviews to normal user tasks; review and audit opt-ins
remain separate from deterministic checks and the parent's diff inspection.

## Experiment protocol

Freeze baseline/candidate asset hashes, corpus, grader, rubric, prompts, model
assignments, effort, and budgets before inference. Keep fixture code identical
between arms and use fresh isolated sessions with alternating arm order. Use the
same repository models.conf and rendered role templates; record configured,
requested/selected, and native runtime-reported models and effort separately.
The runner checks actual instruction sources and rejects configuration drift.

Start with one paired screening repetition per case. A screen cannot authorize
commit or push. If targets miss, correctness regresses, or the score ceiling
prevents the target, preserve evidence and revisit. If screening passes, use a
separate confirmation output directory with at least three paired repetitions.
Report per-case results, paired variation, worst cases, and uncertainty. Do not
tune hidden cases or discard difficult tasks to manufacture a pass.

Measure elapsed time through independent functional/scope validation, including
the agent's own checks, coordination, authorized review, and repairs. Mutation
scoring and offline maintainability assessment are evaluation overhead, reported
separately. Do not sum parallel-agent durations as user wait time. Human overhead
and developer productivity remain unverified without human observations.

Reconcile native events with per-thread session counters, then sum each parent
and descendant once. Cached input and reasoning are subsets, not extra charges
to add again. Preserve failed and infrastructure attempts in evidence and
aggregate penalties; missing native counters block verification. Keep native
transcripts and auth outside this public repository. Temporary auth links must
be removed after each run, without altering original credentials.

The portable runner plans by default; inference requires --run. See
[execution instructions](../evals/delegation/README.md) for commands, review
format, and local evidence layout. It never commits or pushes automatically.

## Implementation and evidence

The quality-first revision prioritizes validated work, capable roles with the
needed permissions, critical-path parallelism, compact evidence, regression
checks, and direct escalation when expertise is missing. It aligns generated
review/audit rules with the user's standing opt-in policy. Model assignments and
application runtime code are unchanged.

The original token-only pilot was stopped when the objective changed. Its
native data is retained locally but does not establish the new quality/speed
requirements. Earlier keyword-based prose grading is replaced by executable
behavioral checks; no success claim is carried forward from that pilot.

### Screening result: 2026-11-10

The quality-first candidate did **not** meet all targets. One paired repetition
per case accepted all eight task outputs, with no held-out functional regression.
These are screening observations, not repeatable performance claims:

| Metric | Baseline | Candidate | Observed change | Target |
| --- | ---: | ---: | ---: | --- |
| Mean quality / 100 | 100.00 | 99.75 | -0.25% | Failed: +10% required |
| Accepted tasks / minute | 0.352 | 0.522 | +48.1% | Screening threshold met |
| Tokens / accepted task | 708,551 | 313,619 | -55.7% | Screening guardrail met |
| Accepted tasks | 4 / 4 | 4 / 4 | No decline | Met |

| Case | Baseline seconds | Candidate seconds | Quality baseline / candidate | Native children baseline / candidate |
| --- | ---: | ---: | ---: | ---: |
| tools | 37.30 | 38.97 | 100 / 100 | 0 / 0 |
| ownership | 302.87 | 171.68 | 100 / 99 | 1 / 0 |
| dag | 204.46 | 137.85 | 100 / 100 | 1 / 0 |
| bundle | 136.47 | 111.38 | 100 / 100 | 3 / 2 |

Both arms earned full functional and regression-strength scores. The candidate
ownership implementation lost one maintainability point for combining path
validation, resource lifetimes, nested traversal and ownership decisions in one
deeply nested function. Assessments used opaque arm labels and the frozen
anchors; the parent evaluator still knew the experiment context. This is not
an independent blind engineering-quality study.

The small direct-work control was 4.5% slower. Other cases were faster, but
one pair per case provides no repeatability or statistical confidence claim.
The bundle case exercised worker delegation in both arms. Review policy also
changed from mandatory to opt-in, so reduced process overhead contributes to
the whole-workflow comparison; this does not isolate parallel-routing effects.
Models and role templates were identical between arms. Native per-thread model,
effort and usage evidence reconciled for all trials. Total tokens include cached
input; they are not dollars or unique prompt content.

The baseline scored 100, making +10% impossible on this bounded rubric. The
quality target remains unchanged. Confirmation was not run, and no commit or
push was performed. A follow-up needs a separately frozen, harder corpus of
representative repository tasks with measurable baseline defects or quality
headroom, followed by repeated paired runs. Do not replace this failed screen
with easier scoring or tune its hidden checks after seeing the outputs. Changing
the quality outcome to defect reduction would be a separate user decision.

Deterministic validation passed: all 35 evaluator self-tests, the .NET executable
suite (including full instruction installation and Cursor generation), and the
Release build with zero warnings/errors. Parent diff inspection found no change
to models.conf or application runtime code. No independent model review was
requested. Native performance was exercised on Codex/macOS; Claude/Cursor were
checked for distribution only. Windows process cleanup and human productivity
remain unverified.

A preceding diagnostic trial consumed 87,357 tokens and 36.19 seconds before
an isolated-config trust-record issue stopped that experiment. It is reported
as evaluation overhead, not silently removed from an alleged successful run.
During the retained screen, a native fork-metadata parser correction required
read-only reanalysis of four completed trials. Original results, manifests and
raw evidence hashes remain in a local migration audit; no inference, task code,
score or elapsed time was replaced. Confirmation would require a fresh run.
The prior token-only pilot remains separate because its objective changed.

[Sanitized results](../evals/delegation/results/screen-2026-11-10.json) retain
per-case metrics, anchored assessments, input hashes, model evidence and limits.
Raw native logs, diagnostic evidence and the uncommitted deliverable worktree
are retained locally to support revisiting the missed target. Temporary auth
paths are removed after trials; credentials and transcripts are not published.

The routing principles were informed by [Which Claude model should you build
with?](https://claude.dev/blog/which-claude-model-should-you-build-with/), credited
to Addy Osmani on claude.dev. Its published results are not Orchestra evidence;
its model names and prices are not routing requirements.

## Tiny C# follow-up

The user subsequently accepted up to a 0.25-point quality decrease if speed and
token requirements held. A separately frozen, one-task C# screen used that new
tolerance, preserving the original experiment and target miss. Both C# solutions
scored 100 and passed all critical checks; the candidate took 92.75 seconds versus
69.93 and used 58.9% more tokens. It failed the new screen's speed/token gates.
Neither arm delegated. This single pair does not confirm or invalidate a general
performance effect. See the [readable C# comparison](../evals/delegation/csharp-mini/results/comparison.md)
for the retained C# source, executable checks, measurements and limitations.

## Repeated five-task C# attempt

A fresh five-task, three-repetition protocol used balanced order, frozen inputs,
strict correctness gates and hierarchical paired bootstrap analysis with adjusted
intervals. It stopped under its manual correctness rule after two completed
pairs; the in-flight third pair was cancelled. In the JSON-status candidate,
`update --tools --json` consumes the new flag as an option value and reaches the
downloader instead of rejecting before work. Offline reproduction confirms the
baseline rejects without requests, while the candidate, reference and starting
source each attempt one fake HTTP request. This is an unmet new-task guarantee,
not a newly introduced underlying parser defect. The reference/checks also missed
this edge, so passing their automated calibration was insufficient. No repeated
performance claim is possible, and the frozen corpus was not tuned afterward.
See the [C# failure report](../evals/delegation/repository-five/results/report.md)
for exact code, all started costs, transport-retry context and the proposed next
experiment. Nothing was committed or pushed.

The subsequent [argument-boundary v2 repair](../evals/delegation/repository-five/argument-boundary-v2/README.md)
passes focused deterministic calibration: 90 invalid argument cases, five valid
controls, the three existing status groups and the .NET executable suite. Runtime
checks reject the old reference, a permissive-value fault and the saved candidate
after successful compilation. This repairs the benchmark reference/checks only;
no new model trials or performance claim, and no changes to frozen v1 evidence.

## Ten-trial v2 decision screen

The user accepted a fresh ten-trial decision screen using the repaired status
boundary. [Its report](../evals/delegation/repository-five/ten-trial-v2/README.md)
records seven completed trials, one interrupted attempt and two unstarted trials.
The baseline verification implementation passed automated checks but failed the
manual no-follow contract: a C# atomic-swap reproduction read matching bytes
through a symlink. The oracle lacked a concurrent-path check. This is a baseline
implementation/coverage failure, not evidence that candidate instructions caused
a correctness regression. No complete decision pass, commit, push or general
improvement claim is supported. Frozen inputs and previous results are unchanged.
