# Workflow efficiency

Orchestra provides a small feedback loop: select relevant checks, record their
results, compare model configuration with evidence, and reuse the run dashboard.
Required repository checks and normal permissions remain authoritative. Model
assignments and global installations are never changed by these diagnostic tools.

## Select and run local checks

The portable runner uses Node.js 22+. In the Orchestra repository:

```sh
node skills/agentic-feature-delivery/scripts/validate.mjs --files README.md
node skills/agentic-feature-delivery/scripts/validate.mjs --base origin/main
node skills/agentic-feature-delivery/scripts/validate.mjs --files README.md --run --report /tmp/orchestra-validation.json
```

Planning is the default; only `--run` executes commands. `--base` includes changes
from the merge base through HEAD, tracked working changes, and untracked files.
For another project, provide its reviewed manifest with `--manifest PATH`.
Without that option, the runner reads `scripts/validation-manifest.json` in the
current project; Orchestra's .NET commands are not universal defaults.

Installed Codex and Claude skills include `agentic-feature-delivery/scripts/validate.mjs`.
Cursor project initialization copies the runner into `.cursor/orchestra/validate.mjs`.
These tools do not install Node or overwrite project manifests. The runner uses
argument arrays with no shell, but a manifest is executable project configuration:
review it and run only commands authorized for the project.

Manifest v1 declares projects (`id`, `paths`, `dependsOn`, `checks`), checks (`id`,
argument-array `command`, check `dependsOn`, optional `platforms`), `alwaysRequired`
and `broadChecks`. Projects depending on a changed project are included transitively;
check prerequisites run first. Multiple path owners are combined. Unknown paths
select broad checks. Cycles, missing dependencies and paths outside the project
are rejected. A missing executable, unsupported required platform, failed check
or blocked prerequisite produces an unsuccessful report, never a silent pass.
The report records commands, selection reasons, duration, status and exit codes.
A broad plan can require Windows checks on macOS: run those checks in Windows CI.

Orchestra's manifest keeps its .NET suite mandatory; embedded skills/templates,
model assignments and global/project assets require a build. The source-local
feedback loop may omit unrelated installer or launcher checks. Existing full CI
checks remain in place, with efficiency/dashboard and protocol regressions added.
Do not use a passing narrow loop as proof that unavailable cross-platform checks ran.

The fixture `tests/fixtures/efficiency/transitive-manifest.json` demonstrates a
changed library whose consumer fails. Testing only the library would miss it.
Fixture trial results are synthetic and cannot establish productivity targets.

## Inspect model assignments and candidates

```sh
orchestrate models inspect --tool codex
orchestrate models inspect --tool claude --evidence /absolute/model-evidence.json
orchestrate models candidate --config /absolute/candidate-models.conf
orchestrate models candidate --config /absolute/candidate-models.conf --results /absolute/evaluation-report.json
```

`models.conf` bundled into the CLI is the configured assignment source. Role
models are rendered into installed definitions; orchestrator assignments are
advisory because the primary tool UI/config selects them. Inspector comparisons
show missing definitions, whole-file drift and model/effort drift separately.
A matched file does not establish that the host loaded that custom role.

Call custom roles explicitly. A generic/default subagent can inherit its parent.
Use a compact handoff: goal, owned files, relevant contracts, constraints,
acceptance command and return contract. Count retries and parent integration time
before concluding that a cheaper model is more efficient. Keep tiny tasks direct.

Evidence JSON contains `observations`, each with `tool` (codex or claude),
`sessionId`, `role`, `source`, `stage` (selected, requested, runtime-reported),
`model`, and optional/null `effort`. Missing effort is unavailable. The bundled
Codex app-server launcher records a unique `model-evidence-*.json` per accepted
turn request in its run directory. That records the requested root model and
session, not proof of the model/effort that executed a response. Child role
execution still needs host-provided evidence. Native token totals do not supply it.

Supplied files retain their source/session and are labeled reported/unverified;
Orchestra cannot authenticate arbitrary logs. Diagnostics are read-only. Candidate
validation verifies assignment shape and template substitution, not provider
availability or model quality. Evaluate representative tasks, including held-out
cases, then obtain approval before editing models.conf and rerunning installation.
An evaluation report does not authorize adoption. No automatic promotion occurs.

## Reusable dashboard

`orchestrate heartbeat init` creates both `heartbeat.js` and the embedded
`dashboard.html` in the run directory. It refuses an existing or linked dashboard
and preserves it. No dashboard generation agent, server or network is needed.
The page opens locally and refreshes every 15 seconds. System, Light and Dark
appearance are available; saved selection survives refresh where local-file
storage is supported, and storage denial does not break rendering.

Counts use the latest appended event per task. Blocked reasons show parked
choices. Usage distinguishes zero from unavailable and includes source/sample
metadata. Missing hooks mean unavailable liveness; only an active old hook can
be stale. Time is pending before the first task starts. At full completion, the
page freezes elapsed time at the final finished event; CLI status retains its
existing wall-clock behavior. Event text is rendered as text, never HTML.

Native hook trust and fresh-session launch requirements otherwise remain as
defined by prep. An explicit user override can keep work in the current chat
with native monitoring unavailable; a dashboard alone never proves monitoring.

## Measure outcomes

The targets are at least 15% less active user overhead per quality-valid accepted
task, and at least 10% more quality-valid accepted work per developer-hour.
Developer time includes setup, review, retries and rework. Resource savings are
supporting metrics; neither fewer tokens nor shorter agent time proves those targets.

Use the trial schema in `tests/fixtures/efficiency/README.md` and run:

```sh
node scripts/evaluate-efficiency.mjs /absolute/observed-trials.json --report /absolute/evaluation-report.json
```

The tool matches task, intervention batch, repetition and held-out status.
Every attempt records acceptance, deterministic pass, failure and retry flags.
It counts accepted work only when the final attempt passes checks and is accepted
without failure. It includes all recorded attempt times. Incomplete unmatched
trials block target claims, rather than disappearing from the denominator.
Tokens, latency and cost remain separate optional measurements. Unknown values
are omitted rather than set to zero. Reports include counts and variation;
synthetic fixtures can never establish the targets. `pooled` is descriptive:
only explicit `batch: "combined"` trials test the all-three intervention.

A concrete observation procedure:

1. Freeze comparable repositories, inputs, environments, permissions, models,
   acceptance tests and task mix. Record their revisions. Use small-edit, feature,
   bug and refactor tasks; reserve cases that are not used to tune the changes.
2. Run matched baseline/candidate task variants for each batch and for all three
   together. Counterbalance their order and use multiple repetitions. Start with
   at least three repetitions across the task categories; expand if variation is
   large. Do not claim statistical certainty from that starting sample.
3. An observer times active user setup, routing, clarification and interpretation.
   Separately record developer time through review and rework. Record unsuccessful
   attempts, corrections and final acceptance, not only successful completions.
4. Store actual human measurements with `observationKind: "human-observation"`
   and each attempt's `provenance: {"kind":"human-observation","source":"observer record"}`.
   Keep sensitive raw records outside the public repository. Run the evaluator;
   examine held-out outcomes and deterministic regressions as well as averages.
5. Confirm both targets on observed combined trials with preserved quality.
   Otherwise report the measured miss or `implemented; outcome not yet verified`.

These are descriptive comparisons from supplied records, not independently
authenticated observations or automatic model recommendations.
