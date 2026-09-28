---
name: prep
description: Prepare a bounded, observable long-running agent session, then launch one fresh native kickoff session where supported. Use when a user asks for an autonomous multi-task run or invokes /prep.
---

# Prep a long-running session

Prepare one run in a Git project. The native agent session performs the work;
Orchestra only writes local run events and serves no process or dashboard. Do
not begin implementation in this prep session. Do not launch until every gate
below passes. Carry the user's existing authorization and project instructions;
ask only for missing material decisions.

## 1. Interview and branch gate

Read the nearest `AGENTS.md`, project context, and applicable workflow skills.
Use `$grill-me` once for the interview before creating run files; its prep
checklist covers the decisions below. Carry its answers forward without asking
again. Record and verify:

- Concrete outcome, acceptance criteria, tasks and dependencies, scope and
  non-goals, project path, selected agent (Codex, Claude Code, or Cursor).
- Confirmed base ref and working branch. Inspect `git status`, HEAD, and the
  branch graph. Require a Git repository, clean tree, attached HEAD, and an
  unambiguous base and branch decision before the first project edit. Disclose
  divergence or unrelated changes and resolve them before proceeding. Never
  silently stash, discard, reset, or move an active checkout. For Codex and
  Cursor, create or switch to the chosen branch and verify its HEAD. For a
  Claude background launch, agree on a dedicated linked worktree path first,
  then create or check out the branch in that worktree and verify its HEAD;
  do not check out the branch in the original checkout first. If the branch is
  already checked out elsewhere, use that checkout only if it is the agreed
  dedicated linked worktree. Otherwise resolve the occupied branch with the
  user before moving it, or give a manual foreground kickoff command from
  the verified checkout. Use the dedicated worktree as the actual project
  directory for all later checks and artifacts. Claude background sessions
  otherwise move into a new worktree before editing.
- Decisions the kickoff agent may make alone; decisions to park for the human;
  dependencies or external actions that need explicit approval. Record whether
  this run authorizes one commit per completed task. The default is no commit.
  Push, merge, deploy, publish, and external mutation require their own explicit
  authorization; a commit grant does not authorize them.
- A positive time budget in minutes, optional token budget, stale threshold,
  repeated-failure and other stop conditions, and the applicable permissions
  for the chosen tool. Choose and record a native usage source for the selected
  launch path. A token cap requires a verified, session-bound collector that
  reports usage while work is in progress. Refuse the cap if that feed cannot
  be activated; keep time and failure limits and label usage unavailable until
  a native sample arrives. Never estimate tokens from text or context size.
- Explicit commands for the deterministic done check, baseline tests, and, for
  an app, an app smoke check. Also record their working directory and expected
  starting result. Do not invent a check from a package manager or mark a
  missing required command as passed.

If a decision changes safety, scope, cost, or observable behavior, settle it
before launch. An answer to the interview is authorization only for the scope
it explicitly covers.

## 2. Validate the starting point

Run the baseline tests and applicable app smoke command in the named directory;
both must pass. Bound the smoke check with a timeout and prove app startup,
a useful response, and clean teardown. A server left running or a hung smoke
command fails prep. Run the deterministic done check once to prove it is executable
and interpretable. It may fail because the feature is unfinished; record that
starting result. A missing executable, malformed command, or ambiguous result
is a failed preflight. Resolve any failures before launching. Check that the
selected tool, `orchestrate` executable, needed native hooks, and permissions
are available. A command may be authorized for unattended execution only if
the tool's actual permission mode permits it; do not select an unsafe bypass
mode merely to avoid a prompt.

### Prove the native usage path

Record the chosen path, native session ID strategy, collector command,
readiness check, and cap behavior in `brief.md`. `orchestrate heartbeat usage`
accepts a session-bound **absolute** total; task events no longer accept token
deltas. For a collector that needs an initialized run directory, create a
disposable heartbeat in a temporary directory, bind only its disposable native
session, prove a real sample, and remove that temporary directory afterward.
Do not use the actual run directory before its branch and baseline gates pass.
For the bundled Codex app-server probe, the disposable directory must be a
unique Git-ignored child of `<project>/.orchestra/runs/`; verify it is ignored,
then remove it after the probe. This is separate from the eventual run ID.
Native samples come from these sources:

- **Codex app-server:** use the bundled
  `scripts/codex-app-server-run.mjs` next to this skill. It starts one native
  app-server thread, observes `thread/tokenUsage/updated`, and writes
  session-bound absolute totals. First run its `--probe` mode against a
  disposable heartbeat and require `ORCHESTRA_CODEX_USAGE_PROBE=ok` with a
  real token sample. The probe uses a read-only turn. If the installed Codex
  version cannot complete the current app-server protocol or emit usage,
  decline a cap on this path. `heartbeat collect-codex` also accepts an
  existing JSONL notification transport, but the bundled launcher needs no
  separate forwarder.
- **Codex CLI:** pipe `codex exec --json` events to `collect-codex` for
  session-bound `turn.completed` accounting. Pass a unique `--stream-id` for
  each native `codex exec` invocation; reuse that ID only when replaying the
  same stream, so resumed invocations add usage without counting retries twice.
  A single long kickoff turn may
  provide usage only after it ends; do not accept a cap on that basis.
- **Codex desktop Goal:** when the user explicitly requests a capped
  long-running goal, verify the native Goal tools and usage field in a
  separate preflight task. The fresh kickoff task must establish its own
  goal, read `get_goal` at safe boundaries, and write absolute snapshots with
  source `codex-goal`. This is agent-driven checkpoint reporting, not
  automatic background telemetry. If the fresh task cannot establish and
  read its goal, stop before unattended work.
- **Claude Code:** a local `collect-claude` process receives its documented
  OpenTelemetry token counter for the exact `session.id`. Verify a native
  token sample from a short disposable Claude session with the same exporter
  settings and disposable heartbeat before accepting a cap. Require Claude Code
  2.1.214 or newer for token caps, since earlier versions can inflate streamed
  token metrics. The collector listens only on loopback,
  observes one run, and never controls the Claude agent.
- **Cursor SDK:** use the installed project
  `.cursor/orchestra/cursor-sdk-run.mjs` launcher. Install `@cursor/sdk` in
  a disposable temporary SDK directory for the preflight, then install the
  verified version in the ignored run directory after creating it. Run
  `node <script> --probe --project
  <absolute-project> --model <model-id> --review-mode sandbox|auto-review
  --sdk-dir <temporary-sdk-dir>` with the selected model and authentication
  before accepting a cap; require `ORCHESTRA_CURSOR_USAGE_PROBE=ok`. The probe
  gives the SDK no tools and removes its temporary store afterward. The
  measured SDK run exposes native usage events; a manual Cursor IDE Agent chat does not
  expose a documented cumulative session count, so offer that chat without
  a cap and show usage as unavailable.

If the selected collector or native feed cannot be proved, choose a launch
path without a token cap or stop for a different tool/path decision. A native
counter may omit a request until it finishes, so describe the cap as a
best-effort boundary stop, not a hard pre-request quota.

## 3. Create local run artifacts

Choose a unique, filesystem-safe run ID. Create only
`<project>/.orchestra/runs/<run-id>/` and never overwrite an existing run.
Keep `.orchestra/runs/` Git-ignored locally (for example, add it to
`.git/info/exclude` if needed), then verify `git check-ignore` on a run file.
Do not add run data to a tracked `.gitignore` or commit it. The run directory
contains:

- `brief.md`: the full interview result, confirmed branch and base, task IDs and
  dependencies, accepted permissions, exact commands and working directories,
  budgets, stop conditions, allowed autonomous decisions, parked decisions,
  the explicit per-task commit choice, and the selected usage source and
  collector process details when applicable.
- `plan.md`: ordered task IDs, acceptance checks, and dependencies.
- `progress.md`: timestamped task state and evidence; initialize all tasks as
  pending. Keep this readable after context compaction.
- `decisions.md`: resolved decisions and parked human choices, each with a
  stable ID, impact, and dependent task IDs.
- `kickoff-prompt.md`: a short, self-contained instruction to invoke `/kickoff`
  for this absolute run path, read these artifacts, and continue until the
  stated stop condition. Include the selected tool and branch. Do not put
  secrets in this file.

Initialize the event writer with the agreed numeric values:

```text
orchestrate heartbeat init --dir <absolute-run-dir> --run-id <run-id> --total-tasks <N> --time-budget-minutes <N> --stale-after-seconds <N> [--token-budget <N>]
```

Use the optional token argument only after proving the selected collector can
report native usage during the run. Token caps stop further work at the next
safe boundary after a sample arrives; one model request or exporter delay can
take usage beyond the cap. Task events do not carry token deltas. The writer
produces `heartbeat.js`, which assigns
`window.ORCHESTRA_HEARTBEAT` to version 1 JSON. The state has `runId`,
`createdAt`, nullable `startedAt` (set by kickoff's first `started` event),
`totalTasks`, `timeBudgetMinutes`, `staleAfterSeconds`, nullable
`tokenBudget`, nullable `tokensUsed`, nullable `usageSource`, nullable
`usageUpdatedAt`, nullable `lastHookAt`, `hookState`,
nullable `boundSessionId`, `lastProbeAt`, `lastProbeSessionId`, and `events`.
Each task event has `task`, `status`, `reason`, and `at`. Collector bookkeeping
may also appear in the state. The dashboard should show the source and last
sample time when available, and clearly distinguish unavailable from zero.
Use `orchestrate heartbeat status --dir <absolute-run-dir>` to inspect its
summary. The writer is for the agents and native hooks, not a command the
user must run during normal work.

## 4. Build the dashboard once

Spawn one bounded subagent to create only `dashboard.html` in the run directory.
Give it the version 1 `window.ORCHESTRA_HEARTBEAT` schema, the selected tool,
a sample status result, read-only access to other run files, and no permission
to alter source or launch another agent. The page must work by opening the
local file: load `heartbeat.js` through
`<script src="heartbeat.js"></script>`, include a meta
refresh, and require no local server, remote library, or network call. Show the
current task, completed and remaining counts, parked decisions, liveness and
stale state, elapsed and remaining time, and token usage or an explicit
unavailable marker. Render event and decision text as text, never as HTML.
Before the first task starts, show the time budget as
pending rather than counting prep time. Never display a stale warning for
Cursor solely because Cursor has no native hook; label native liveness
unavailable. Inspect the generated HTML and open it locally to verify the
core fields and refresh.
If this environment has no usable subagent, report that the required dashboard
build could not run and stop before kickoff.

## 5. Activate run-scoped liveness

`orchestrate heartbeat hook --dir <absolute-run-dir>` consumes native hook JSON
on stdin and stamps `PostToolUse` or `Stop`. Make the hook command use the
absolute run directory and correctly quote it for the platform. Configure
only the selected project and run. Inspect existing config first; make a safe
merge that preserves existing hooks and settings. Record the exact inserted
handler definitions and whether the config file existed in `brief.md`, so
kickoff can remove only this run's handlers on permanent completion or abort.
Do not replace a user file, follow a symlink into an unexpected path, or
modify global hook settings. If an existing tracked config must be edited,
record that as a temporary authorized run change; preserve and reconcile any
later user edits, and keep it out of task commits.

- **Codex:** put the command handler in project `.codex/hooks.json` for
  `PostToolUse` and `Stop`, preserving other entries. Confirm the project
  config is trusted, hooks are enabled, and the *current definition* is
  reviewed and trusted through Codex's `/hooks` flow. A config file's presence
  does not prove activation. Do not use a trust-bypass flag as a substitute.
- **Claude Code:** merge `PostToolUse` and `Stop` command handlers into project
  `.claude/settings.local.json`, preserving permissions and existing hooks.
  Review the effective settings and command permission; do not replace
  `.claude/settings.json` or global settings.
- **Cursor:** do not install a hook in this version. Prompt-written task events
  still update progress; native liveness remains unavailable.

For Codex or Claude, start a short native preflight session or use the tool's
hook inspection and an actual matching event. Compare
`orchestrate heartbeat status --dir <absolute-run-dir>` before and after and
require a newer `lastProbeAt` with the preflight session's known `session_id`.
If the ID cannot be obtained, native hook activation is unverified. Probe
stamps never mark this run active. A direct call to `heartbeat hook` only
tests the writer, not hook activation. If a hook cannot be trusted or made
active, do not claim live monitoring and do not auto-launch; report the step to
complete and leave the prepared run available for a later kickoff. Use a new
session after changing hook configuration because an existing session may not
reload it. Do not commit project hook configuration or run files.

## 6. Launch one fresh session

After the checks and dashboard pass, launch exactly one fresh native session
with `kickoff-prompt.md`. Obtain a native session ID from the launcher or set
it through a documented native session option, then promptly run
`orchestrate heartbeat bind --dir <absolute-run-dir> --session-id <native-session-id>`.
The Codex JSONL collector may bind the ID from `thread.started`; the Cursor
SDK launcher binds the ID it receives from the SDK. Kickoff waits up to 30
seconds for binding before its first task. For Codex or Claude, verify a
matching hook event advances `lastHookAt` during a bounded check. If the
launcher cannot report or set an ID, or a
matching stamp never arrives, stop or park the launched session where
supported and give the user the recovery step; do not label native liveness
verified. For Cursor, verify the chosen SDK or manual chat handoff instead;
native hook liveness is unavailable.
Never launch an agent from a heartbeat event, hook, timer, or dashboard; there
is no supervisor or recursive polling loop.

- **Codex desktop:** when the native task creation tool is available for this
  saved project, create a separate local project task on the verified checkout
  and send it the kickoff prompt. Verify creation and give the user its task
  link. A requested token cap on this path requires the separately verified
  Goal path: instruct kickoff to create the user-authorized goal for its own
  task, check `get_goal`, and write source `codex-goal` snapshots. Ordinary
  desktop hooks provide liveness, not token usage. If this checkout cannot be
  selected without changing Git state, use the manual fallback. Do not open
  a second worktree silently.
- **Codex app-server measured run:** when this launch path was selected and
  its probe passed, start `node <installed-prep-skill>/scripts/codex-app-server-run.mjs
  --project <absolute-project> --run-dir <absolute-run-dir> --model <model-id>
  --detach`. The launcher starts a detached child, keeps its stdout and stderr
  in a unique log under the ignored run directory, and prints its PID, log
  path, and readiness state. Require `ORCHESTRA_CODEX_READY=ok` for an active
  process; `complete` means it finished during the check and its log and run
  status must be inspected. If readiness is pending, inspect the same PID and
  log until it is ready or has failed. Verify an active process is still alive
  and heartbeat has bound its native session before ending prep. Do not start
  another launcher while the first process remains alive or its native turn
  may still be active.
  Pass `--network-access true` only if the brief expressly authorizes it;
  otherwise the launcher uses workspace write access without network access.
  It binds the native root session and records live cumulative tokens. The
  launcher starts one native turn and stops it when a measured cap is reached.
  For a parked run, use `--resume --detach` only after the prior turn has
  ended; the launcher verifies the stored thread and its terminal turn before
  continuing with the same native session ID. Do not run the script twice
  concurrently.
- **Codex CLI:** use `codex exec --json -C <project> --full-auto -` with
  `kickoff-prompt.md` on stdin, using the recorded sandbox and approvals.
  Feed its JSONL into `orchestrate heartbeat collect-codex --dir
  <absolute-run-dir> --stream-id <unique-invocation-id>` and retain a local copy of the stream under the ignored
  run directory for diagnosis. `thread.started` supplies the session ID;
  `turn.completed` supplies final usage. Start it as one durable process and
  retain its session/log location. Never accept a token cap from this
  final-only stream. Never pass
  `--dangerously-bypass-approvals-and-sandbox`. If the CLI or a safe
  durable launcher is unavailable, give the exact command for manual start.
- **Claude Code:** from the verified dedicated linked worktree, use
  `claude --bg --session-id <UUID> "<short prompt naming absolute run dir and
  /kickoff>"`. Before launching Claude, bind that UUID and start
  `orchestrate heartbeat collect-claude --dir <absolute-run-dir> --session-id
  <UUID> --port <loopback-port>` as one local, run-scoped observer. Confirm
  its `Claude usage collector ready on 127.0.0.1:<loopback-port>` line, keep
  its process ID and log in the run directory, and
  launch Claude with these environment variables:

  ```text
  CLAUDE_CODE_ENABLE_TELEMETRY=1
  OTEL_METRICS_EXPORTER=otlp
  OTEL_EXPORTER_OTLP_METRICS_PROTOCOL=http/json
  OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://127.0.0.1:<loopback-port>/v1/metrics
  OTEL_METRIC_EXPORT_INTERVAL=1000
  OTEL_METRICS_INCLUDE_SESSION_ID=true
  OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=cumulative
  ```

  Verify the launched session remains in that worktree on the named branch
  and that a native usage sample reaches the bound run after its first model
  request. Stop the observer on permanent completion or abort. Do not
  combine `--bg` with `-p` or silently disable its worktree isolation. If the
  worktree, background mode, telemetry, or permissions are unavailable, give
  a manual foreground invocation from the verified checkout and label usage
  unavailable unless that foreground path has its own verified collector.
- **Cursor:** offer the measured SDK run and manual IDE Agent chat. For the
  SDK path, use the installed project `.cursor/orchestra/cursor-sdk-run.mjs`
  after its read-only probe succeeds. Launch exactly once with `node
  <script> --project <absolute-project> --run-dir <absolute-run-dir> --model
  <model-id> --review-mode sandbox|auto-review`; add `--sdk-dir` if the SDK
  is installed outside the run directory. The launcher binds its native run
  ID and writes source `cursor-sdk` usage while streaming. It is the native
  agent session, not an Orchestra supervisor. For manual chat, tell the user
  to configure **Settings > Agents > Approvals & Execution** with Auto-review
  or an Allowlist for only the approved commands and verify no approval is
  pending. Open a **new** Agent chat in the project and enter `/kickoff
  <absolute-run-dir>` using the installed command. Print the exact path and
  command. Explain that native liveness and cumulative token usage are
  unavailable in manual Cursor chat.

For a parked Cursor SDK run, use the same installed launcher and run directory
with `--resume`. It checks that the previously bound SDK run has ended,
resumes its saved local agent state, and conditionally binds the new native
run ID. If the prior run is still active or its state cannot be verified, do
not start a replacement. The dashboard keeps usage from both run IDs.

Manual fallback always states the project directory, selected branch, run
directory, exact native invocation, dashboard path, and any trust or permission
step still needed. Never say a run has started until the native tool confirms
it. Hand off the fresh session or the concrete manual start instructions,
then end this prep session.
