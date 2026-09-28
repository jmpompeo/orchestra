---
name: kickoff
description: Run one prepared long-running session from its local brief and ledger until done or a recorded stop condition. Use when invoked by /prep or /kickoff with a run directory.
---

# Kick off a prepared run

The text after `/kickoff` is the absolute run directory. If it is absent or
ambiguous, ask for the path; do not select the newest run by guesswork. This
skill is the complete Cursor command body as well as a Codex and Claude skill.
Read `brief.md`, `plan.md`, `progress.md`, `decisions.md`, and the heartbeat
status in that directory before doing anything else. Follow the nearest
project `AGENTS.md`, its context and applicable workflow skills. Treat the
brief as the specific authorization for this run; do not expand its scope.

## Resume and preflight

Verify that the run directory is inside the intended project's
`.orchestra/runs/`, is Git-ignored, and is not a symlink to an unexpected
location. Verify the project repository, confirmed base and working branch,
attached HEAD, and expected checkout. Preserve unrelated changes. The clean
tree gate was completed by prep; your own completed edits are part of this
run, but an unexpected branch, HEAD, base, or unrelated change requires a
decision before further edits. Do not stash, reset, or switch branches to
work around it. For Claude background work, verify that the current directory
is the dedicated linked worktree recorded in the brief, including after the
first file edit. Check time budget, any configured token budget, the selected
native usage collector, active tool permissions, and every stop condition
before starting a task. `orchestrate heartbeat status --dir <absolute-run-dir>`
reports `tokensUsed`, `usageSource`, `usageUpdatedAt`, and
`tokensOverBudget`. A null `tokensUsed` means no sample has arrived; it is
not zero usage.

For Codex and Claude, the heartbeat must be bound to this native session ID.
At first launch, prep may still be receiving the ID from the launcher. If
`boundSessionId` is null, wait up to 30 seconds, checking status every few
seconds, for prep to bind it. Proceed only when it equals this session's ID
and a matching hook event advances `lastHookAt`. If a different ID appears,
or the bounded wait expires, stop before unattended work and report the gap.
On a resumed native session with a new ID, confirm the previous session ended,
then run
`orchestrate heartbeat bind --dir <absolute-run-dir> --session-id <new-id> --replace`
and verify a matching hook event. Never use a different session's probe as
proof of this run's liveness. For a Cursor SDK run, verify the launcher has
bound its returned run ID; Cursor has no native hook in this version. A manual
Cursor IDE Agent chat has neither native binding nor measured token usage.
Do not start a manual chat under a brief that requires a token cap.
On an SDK resume, the launcher first verifies the prior run ended and uses a
conditional `bind --replace --expected-session-id` for the new run ID. Verify
the new binding and preserved aggregate usage before continuing the ledger.

For a Codex desktop run whose brief explicitly authorizes a capped native
Goal, create that goal in this fresh task with the agreed objective and token
budget, then call `get_goal` and confirm it returns `tokensUsed`. If the tools
or usage field are unavailable, park before unattended work. Use this task's
native session ID for its `codex-goal` usage snapshots.

If the run is already finished or stopped, report that state and do not start
it again. Reconstruct task state from the ledgers, not conversation memory.
Choose the next unblocked task whose dependencies are complete. Keep task IDs
stable across all files and events.

## Work loop inside this one native session

Use the project's appropriate implementation or debugging workflow for the
task. Do not spawn another long-running session or have Orchestra supervise
this one. Before each task, append its start time and expected evidence to
`progress.md`, then write:

```text
orchestrate heartbeat event --dir <absolute-run-dir> --task <task-id> --status started --reason <short-reason>
```

Work to the task's acceptance criteria. Record decisions, material evidence,
file changes, and check results in the ledgers as they happen so a resumed
session can continue accurately. Native Codex and Claude hooks supply
additional liveness stamps; use task events for progress in every tool,
including Cursor. If a reliable cumulative token count is available, record
its absolute value with
the separate `orchestrate heartbeat usage --dir <absolute-run-dir> --source
<source> --session-id <native-session-id> --tokens-used <cumulative-total>`
command. Task events never carry token counts. The prepared collectors do
this automatically for their native streams:

- **Codex app-server or CLI JSONL:** the bundled app-server launcher records
  `thread/tokenUsage/updated` notifications during work. `collect-codex` can
  read another app-server JSONL transport or `codex exec --json`
  `turn.completed` usage after the turn. A single long CLI kickoff turn only
  provides final accounting, so it cannot enforce a cap. On Codex desktop,
  if a native Goal is active for this task and `get_goal` returns cumulative
  `tokensUsed`, use source `codex-goal` and record that absolute value at safe
  boundaries. Do not claim desktop usage when no Goal exists.
- **Claude Code:** prep's local OpenTelemetry receiver records native token
  counter samples for the bound Claude session. Confirm it remains alive;
  a statusline context-window count is not cumulative run usage.
- **Cursor SDK:** the measured launcher records native SDK usage events for
  its run. A manual Cursor IDE Agent chat has no documented cumulative token
  feed, so leave usage unavailable there.

Never infer usage from elapsed time, output text, or context occupancy. At
each task boundary, check `usageSource` and `usageUpdatedAt` against the
brief's selected source and the recent model work. If an agreed cap is active
and the collector stops, its source changes, or its samples fail to advance
after model work, park the run before more unattended work and report the gap.
The preflight probe does not guarantee later availability. A token cap stops
further work at the next safe boundary after usage reaches
it; one model request or telemetry delay can take usage beyond the number.

Run the exact deterministic done check and relevant baseline/app smoke checks
at the points named in the brief, and before declaring completion. A done
check that failed at prep must pass at the end. A task is finished only when
its own criteria and checks pass. Do not mark unfinished work complete to
satisfy a budget.

If the brief explicitly authorizes per-task commits, after checks pass stage
only files in that task's approved scope. Inspect the staged diff for secrets,
run files, hook configuration, and unrelated changes; remove any such file
from the stage. Make at most one Conventional Commit for that completed task.
Record the commit SHA. If authorization is absent, leave the changes
uncommitted. Never commit `brief.md`, ledgers, dashboard, heartbeat files, or
run-specific hook config. No push, merge, deploy, publish, or external change
follows from a commit grant; each needs separate explicit authorization in
the brief.

After the optional commit step, append the task's evidence and completion
time to `progress.md`, update `plan.md`, then write a `finished` event.

## Blockers, failures, and stopping

For a human decision, append a stable decision ID, options, impact, and its
dependent task IDs to `decisions.md`; mark the task blocked in `progress.md`
and write a `blocked` heartbeat event with `decision-ID: short question` as
its reason, so the local dashboard can show the parked queue. Move to another
independent ready task. Do not guess an answer or repeatedly ask the same
question. If no ready task remains, stop and send the user one compact list
of the parked decisions.

For a failed command or implementation attempt, record the exact task,
operation, error signature, and attempted remedy in `progress.md`; write a
`failed` event whose `--reason` is a stable `operation|error-signature` string,
verbatim on a repeat, without timestamps or attempt numbers. Put the narrative
in the ledger. Diagnose once with new evidence. If the same operation fails
with the same signature twice, stop this run and report
it instead of retrying indefinitely. A different failure can receive its own
bounded diagnosis. Preserve partial work and show the next safe step.

Stop when all tasks and the final done check pass, the time or configured
token budget is reached, a named stop condition occurs, two matching failures
occur, or no independent task can proceed. Check the budgets before starting
each new task and after significant work; finish the current atomic action
safely when a limit is reached, then stop. Record the stop reason and final
status in `progress.md`, emit the appropriate `finished`, `blocked`, or
`failed` task event, and report the outcome, checks, remaining work, parked
decisions, dashboard path, and any required user action. On permanent
completion or abort, remove only the exact run-specific hook handlers that
prep recorded, preserving all other config and later user edits; if removal
is unsafe, leave them and give the user exact cleanup instructions. Keep
handlers when the run is parked for a decision or resumable after a budget
stop. Stop the run-scoped Claude telemetry receiver on permanent completion
or abort; leave it available while the run is parked for a resumable decision
only if it is needed for the same bound session. Do not launch a replacement
session automatically.
