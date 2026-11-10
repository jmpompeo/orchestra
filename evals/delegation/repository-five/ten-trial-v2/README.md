# Ten-trial v2 decision screen: stopped on a manual contract failure

The user authorized one baseline/candidate pair for each of the five tasks and
accepted this smaller decision screen in place of the original repeated design.
The pre-inference decision rule required all correctness, manual-contract,
evidence and design gates plus aggregate quality delta >= -0.25 points,
throughput ratio >= 1.10 and tokens per accepted task ratio <= 1.10. Bootstrap
intervals were to describe uncertainty, not gate delivery for this reduced run.
A passing screen would apply to these five tasks, not establish repeatability.

**Seven of ten trials completed. The eighth was interrupted after a manual
contract failure; two never started. No commit or push is justified.** Raw
automated records remain unchanged, with manual adjustments only in analysis.
Neither the failure nor the interrupted candidate proves that the revised
instructions regress correctness. The failed completed output was baseline.

## Observations

| Task | Baseline seconds / tokens | Candidate seconds / tokens | Outcome |
| --- | ---: | ---: | --- |
| Shared preview/execution plan | 270.76 / 3,141,325 | 186.08 / 1,061,958 | Both behavior contracts pass; quality 100 / 99 |
| Ownership-state validation | 216.67 / 803,557 | 138.97 / 548,300 | Both frozen contracts pass; quality 100 / 100 |
| Command option validation | 192.94 / 904,176 | 159.43 / 576,424 | Both contracts pass; quality 100 / 100 |
| Read-only installation verification | 314.94 / 1,612,594 | Interrupted: 50.60 agent seconds / 241,818 tokens | Baseline automated checks pass, manual no-follow contract fails; candidate incomplete |
| JSON status, boundary v2 | Not started | Not started | No result |

Use [summary.json](summary.json) for exact native measurements and assessments;
rounded display values are descriptive. All eight started attempts consumed
**8,890,152 native tokens**, including cached input and the interrupted attempt.
Offline scoring and parent assessment are evaluator overhead, separate from
trial timing/tokens. Account-wide used quota rose from 31% before the first trial
to 84% after stopping and reproduction; that includes parent/other account use
and is not an isolated benchmark quota measurement.

The preview candidate loses one maintainability point for retaining the old
`Apply` helper's global ownership/update/backup branches even though it now serves
only project initialization. Its shared typed plan and behavior checks pass.
The state baseline additionally repairs pre-existing case-sensitive ownership
comparisons; candidate validates uppercase hex format while preserving those
comparisons. The frozen validation/preservation contract and oracle do not add
ownership normalization as a new requirement after inference. This behavioral
difference is disclosed in both source assessments.

The three completed pairs favor the candidate on elapsed time and tokens, but
the sample is incomplete. Their partial mean quality is 99.667 versus 100,
which alone does not meet the -0.25-point threshold. No incomplete-task exclusion,
pooling with earlier runs, general improvement claim or delivery pass is made.

## The failed requirement and evidence

The verification task explicitly says: “Never follow links to read content.”
The baseline first inspects metadata and later calls `File.ReadAllBytes` (and
similarly checks the state path before `File.ReadAllText`). A path can change
between these operations. Its own final report identifies concurrent symlink
replacement as incomplete. All automated groups and both fault checks pass:
they cover links already present during inspection, not concurrent replacement.
This is another oracle coverage gap, not a passing calibration of every promise.

[The C# reproduction](repro/Program.cs) compiles the unmodified saved baseline
source. A regular destination always contains modified bytes; an outside target
contains the expected bundled bytes. Stable regular/link controls correctly
report `modified` and `unsafe`. A background macOS atomic exchange alternates
that regular entry and a symlink. On observed attempt 9, verification reports
`unchanged`, which is possible only by reading the symlink's matching target.
There were 274 exchanges, no exchange errors and zero HTTP requests. The exact
attempt count depends on scheduling; the observed violation is retained in
[reproduction.json](reproduction.json).

```sh
dotnet run --project repro/Repro.csproj --configuration Release --disable-build-servers -p:SubjectRoot=/path/to/saved-baseline-workspace
```

The probe is macOS-specific, uses only a disposable temporary tree, injects an
offline handler and cleans its fixture. Exit 0 means the violation was observed,
not that the implementation is safe. Exit 1 means no violation was observed in
that bounded attempt, not proof of safety. [The baseline patch](verify-installed-baseline.patch)
reconstructs its implementation against the frozen source commit. No real user
files, updates, network access or executable replacement are involved.

The frozen manual-stop rule stopped inference. The candidate was interrupted
before a final output and grade; it is incomplete, not a demonstrated candidate
implementation failure. Its partial counters remain included. Resolving the
no-follow implementation and adding a trusted concurrent-path check requires a
new calibrated experiment. Instructions and task contracts were not tuned here.

## Inputs, checks and retention

The source remains commit `3ad9d38943085290b1d5dcbaf2e7e49995a540ab`.
The instruction baseline is the previously frozen token-focused snapshot, not a
newly reconstructed main-branch policy; the candidate matches the current
deliverable instructions. Both instruction arms, model assignments, native runner, offline build fixtures,
four unchanged task definitions and grading rules match the preceding setup.
Only the status case uses the already-calibrated
[argument-boundary v2 replacement](../argument-boundary-v2/README.md).
A fresh seeded schedule balances first arms 3/2 and keeps each pair adjacent;
all model trials ran serially. Nothing is pooled into the stopped v1 results.
[Protocol](protocol.example.json) and frozen hashes in the summary describe it.

[Validation](validation.json): 14 measurement tests pass; integrated v2 status
reference plus external authored calibration tests earns all 70 behavior and
20 fault-detection points, including all four groups and four compiled faults.
An initial production-only reference calibration correctly had no authored-test
credit; supplying the missing external calibration tests resolved that setup
expectation without changing actor inputs. Frozen input hashes still match.
The C# race reproduction compiles and observes the contract violation. Parent
inspected the patches/evidence. No independent model review of this deliverable
was requested. Shipped assets/application code did not change in this slice, so
the earlier passing repository build/suite was not repeated.

For reuse, copy the preceding `harness/` plus the native `../../runner.py` into a
new external run directory, replace only the status entry with the v2 object,
use [experiment.py](experiment.py), supply source settings, freeze independent
arms and resolve the example protocol paths before inference. The runner plans
by default; `--run` is explicit. Keep historical outputs immutable. The inherited
measurement helper's three-repetition wording is historical; this coordinator
explicitly passes `repetitions=1`, reports intervals and never labels this screen
statistical confirmation. Its `decision_pass` still requires the complete design.

Owned inference/coordinator processes have ended and trial authentication links
are removed. Disposable reproduction/calibration source trees were cleaned.
Raw native logs, failed/interrupted trial workspaces, frozen inputs and the
uncommitted deliverable worktree remain local for investigation. Published
reproduction contains source only, without generated build output. No shipping
CLI feature solution, model assignment, installation, commit or push was applied.
