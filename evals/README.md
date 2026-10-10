# Maintainer evals

Four small baseline cases exercise the current Codex workflow for debugging and
refactoring; a fifth exercises a refactor-to-debugging-to-refactor handoff.
They are a local diagnostic baseline, not a benchmark or a measure
of statistical improvement. Each case runs once in a fresh temporary Git repo.

## Commands

From the repository root, with .NET 10 installed:

```sh
dotnet run --project evals/Orchestra.Evals.csproj -- list
dotnet run --project evals/Orchestra.Evals.csproj -- self-test
dotnet run --project evals/Orchestra.Evals.csproj -- run
dotnet run --project evals/Orchestra.Evals.csproj -- run debug-range
dotnet run --project evals/Orchestra.Evals.csproj -- run debug-range --source-ref HEAD
```

`list` and `self-test` never call a model. `self-test` verifies that debugging
and handoff cases fail before their reference fixes and pass afterward, and
that refactoring cases preserve behavior before and after their reference changes.
Add a case name to `run` to bound the paid work to one trial. `self-test` also
accepts a case name for focused offline verification. Only `run` invokes
`codex exec --json --full-auto`; it requires a working Codex CLI login and may
incur model charges. By default, the runner stages the current working-tree
`global/codex/AGENTS.md` and relevant `skills/*/SKILL.md` files into each
temporary repo as `AGENTS.md` and `.agents/skills/*/SKILL.md`. With
`--source-ref HEAD` (or another Git ref), it stages those files from the exact
commit instead. This allows a baseline against pre-change instructions even
when the working tree is dirty. It points the prompt at the staged skill.

## Required local container grader

`self-test` and `run` require Docker or Podman and the .NET 10 SDK image
`mcr.microsoft.com/dotnet/sdk:10.0` already present locally. `list` does not.
The runner inspects the local image, resolves its immutable image ID, checks
that it runs .NET 10, and uses that ID for all grading in the run. It never
pulls an image or runs graded code with the host .NET SDK. If the runtime,
image, or isolation flags are unavailable, it stops before any model call.

Each grader starts a disposable, non-root container with networking disabled,
all capabilities dropped, no new privileges, a read-only root filesystem, and
CPU, memory, and process limits. Only the temporary grading directory is
mounted, read-only. Build outputs and caches stay in a container tmpfs. The
runner force-removes the named grader container in a `finally` block, including
after a timeout, and reports when cleanup cannot be confirmed. The
agent prompt prohibits running candidate code or tests in its workspace; the
container grader performs all execution. Consequently these evals measure the
code outcome and handoff context, not whether the agent independently ran its
normal verification steps. As with any prompt instruction, this is not a
technical execution sandbox for the Codex trial itself.

The grader compiles the candidate `Library.cs` with trusted visible and hidden
tests outside the agent's repo. For debugging, the visible failure must exist
before the trial and both suites must pass afterward. For refactoring, both
suites must pass before and after; then a maintainer must inspect the diff and
trace to decide whether the structure actually improved. A passing automatic
grade is not a completed refactor verdict.
The grader rejects symlinked or oversized (`>256 KiB`) candidate source files.
The handoff case also needs a manual trace review: verify that the agent
records the failing baseline, carries evidence into debugging, fixes the bug,
then resumes the refactor. The runner rejects edits outside `Library.cs`.
Its prompt explicitly requests the transition, so it does not measure whether
the agent would choose that route unprompted.

The Codex JSONL and stderr traces are written under `evals/artifacts/`, which
is ignored by Git. They may contain prompts, source, model output, and local
paths. Keep them local, inspect them before sharing, and delete runs you no
longer need. The runner does not collect account usage or enforce a monetary
limit. It uses one trial per case and an eight-minute timeout per Codex call.
The sample cases are intentionally tiny; passing them does not establish
reliability on real repositories. Hidden checks only cover the listed behavior.
Each run also writes `summary.json` with the timestamp, source ref and resolved
revision, dirty state for working-tree instructions, SHA-256 of the staged
instruction files and each case's fixture inputs/graders, the pinned grader
image ID, Codex CLI version, per-case automatic checks, and whether manual
review is pending. The summary contains no prompt, source, or transcript
content. The actual selected model and private Codex configuration are not
recorded, so comparisons should also note any known CLI settings externally.
