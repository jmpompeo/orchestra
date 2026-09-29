# Orchestra

`orchestrate` is the supported installation path for this cross-tool
configuration. It is released as a self-contained single binary, so
users need neither a source clone nor a .NET runtime. This repository is for
auditing and maintaining the harness.

## First-time setup

No GitHub account, token, GitHub CLI, source clone, or .NET runtime is required.
Download the installer before running it so you can inspect the script locally.

On macOS or Linux:

```sh
curl -fL -o orchestra-install.sh \
  https://github.com/jmpompeo/orchestra/releases/latest/download/install.sh
sh orchestra-install.sh
```

On Windows PowerShell:

```powershell
Invoke-WebRequest `
  -Uri "https://github.com/jmpompeo/orchestra/releases/latest/download/install.ps1" `
  -OutFile "orchestra-install.ps1"
& ".\orchestra-install.ps1"
```

If local PowerShell policy blocks the downloaded script, run it with a policy
override scoped only to that process; this does not change the permanent policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\orchestra-install.ps1"
```

The scripts detect `osx-arm64`, `osx-x64`, `linux-x64`, or `win-x64`, download
the latest stable public release, and verify its archive against `SHA256SUMS`
before replacing anything. They install only the executable. Missing tools,
unsupported platforms, unsafe existing paths, checksum failures, and PATH
issues stop with corrective instructions.

By default, a new installation goes to `~/.local/bin/orchestrate` on macOS or
Linux and `%LOCALAPPDATA%\Programs\Orchestra\bin\orchestrate.exe` on Windows.
The scripts do not modify PATH or shell profiles. Override the destination or
select a specific stable release when needed:

```sh
sh orchestra-install.sh --install-dir "$HOME/bin" --version v2.1.0
```

```powershell
& ".\orchestra-install.ps1" -InstallDir "$HOME\bin" -Version "v2.1.0"
```

After the script succeeds, preview and apply the desired configuration:

```sh
orchestrate install --tools codex --dry-run
orchestrate install --tools codex
```

Without `--tools`, `install` presents an interactive selector. Automation must
use `--tools codex,claude,cursor` or `--tools all`.

## What the CLI installs

| Tool | Personal installation |
| --- | --- |
| Codex | `~/.codex/AGENTS.md`, `~/.codex/agents/`, and full skills under `~/.agents/skills/` |
| Claude Code | `~/.claude/CLAUDE.md`, `~/.claude/agents/`, and portable skills under `~/.claude/skills/` |
| Cursor | No global files; Cursor rules and generated commands are project-local |

The CLI never installs `project-template/` globally. Cursor commands are
generated from each canonical `SKILL.md`, excluding Codex-only agent metadata.
For a personal Cursor baseline, run `orchestrate cursor-rules --print` and
paste the result into Cursor Settings → Rules → User Rules.

The shared workflow policy requires a Git repository, clean working tree,
attached HEAD, and an agreed base and working branch before any repository
edit. If a base or branch was not supplied, the agent proposes one and waits
for confirmation. Once verified, the branch carries through skill detours.
Feature work uses `agentic-feature-delivery`, defects use `agentic-debugging`,
and standalone behaviour-preserving cleanup uses `refactor-code`. `grill-me`
and `bootstrap-agent-harness` support the active workflow, which resumes
afterward; a read-only refactor audit during feature work does not switch the
primary workflow.

Bundled workflows include `$agentic-debugging` for evidence-driven diagnosis,
root-cause fixes, and regression verification; `$grill-me` for resolving
non-trivial choices after investigation or before feature implementation;
`$agentic-feature-delivery` for executing an approved feature; `$refactor-code`
for behaviour-preserving structural improvements and scoped smell audits; and
`$bootstrap-agent-harness` for adopting the framework in an existing
repository. `$prep` and `$kickoff` structure long, unattended sessions.

### Prepare a long-running session

Invoke `$prep` (or `/prep` in Claude Code or Cursor) in the target project.
Prep asks for the outcome, allowed scope and decisions, a branch, time budget,
stop conditions, and explicit commands for the done check, baseline tests, and
an app smoke check when applicable. It verifies the done check is runnable,
then requires the baseline tests and app smoke check to pass. It also verifies
that required permissions and heartbeat hooks work before starting a fresh
native session where the host supports it. Cursor can launch through the
measured SDK runner or receive instructions for `/kickoff` in a new IDE Agent
chat. Orchestra does not supervise the native loop.

Prep keeps the brief, plan, progress ledger, decisions, heartbeat data, and
local dashboard under `.orchestra/runs/<run-id>/`. These files are ignored by
Git for that project. The dashboard is an HTML file generated once during prep;
it reloads `heartbeat.js` from the same directory and works over `file://`
without a server. It shows task progress, parked questions, liveness, and
available budget data, including the usage source and last sample time. Run
notes can contain sensitive context, so inspect them before sharing or
copying them elsewhere.

Kickoff reads the brief and resumes from the local ledger after compaction or a
restart. The time budget begins with its first task. A question needing human
judgment is recorded in the decision queue; independent tasks can continue.
The run stops at its time limit, an unmet permission or environment gate, or
the second occurrence of the same failure on a task.
Per-task commits require explicit authorization in that run's brief and stay
on its named branch. Token counts come from the selected tool's native usage
feed and are bound to that run's session ID. The agent or collector writes an
absolute native total through `orchestrate heartbeat usage`; task events do
not accept token deltas. If no native sample has arrived, the dashboard shows
usage as unavailable rather than zero.

| Tool and launch path | Token source | When a token cap is available |
| --- | --- | --- |
| Codex app-server | Bundled one-turn launcher reads live `thread/tokenUsage/updated` notifications | When its read-only probe receives a native usage sample |
| Codex desktop | Native Goal `get_goal` usage written at kickoff checkpoints | When the user authorizes a capped Goal and the fresh task can create and read it |
| Codex CLI `exec --json` | `turn.completed` usage forwarded to `heartbeat collect-codex` | Final accounting only for a single long kickoff turn; no token cap |
| Claude Code background | [OpenTelemetry token counter](https://code.claude.com/docs/en/monitoring-usage) sent to a run-scoped loopback receiver | When prep proves telemetry with a native sample and starts the receiver before kickoff |
| Cursor SDK | [SDK usage events](https://cursor.com/docs/sdk/typescript) recorded by the bundled project launcher | When its read-only native usage probe succeeds |
| Cursor IDE Agent chat | No documented cumulative per-session token feed | Unavailable; use a time limit and failure stop |

Prep offers both the Cursor SDK measured run and manual IDE chat. The SDK
launcher is installed in the project at `.cursor/orchestra/cursor-sdk-run.mjs`;
it needs Node.js, `@cursor/sdk` installed in the ignored run directory, a
selected model, and Cursor authentication. Run its read-only usage probe
before starting a capped run. Manual chat remains available through a fresh
Agent chat and `/kickoff <absolute-run-dir>`, with token usage labeled
unavailable. The SDK launcher runs one native Cursor agent and records its
usage; it does not supervise it. An explicit `--resume` checks that the prior
SDK run ended, reuses its saved agent state, and adds the new run's usage to
the existing total.

For Codex, the bundled prep script can start a measured app-server run and
record live thread usage. Its read-only probe must succeed with the installed
Codex version before prep accepts a cap. A parked measured run can continue
with `--resume` after the prior turn ends. Prep uses `--detach` for the measured
run, retains its local PID and log, and verifies the native session binding
before ending. The separate `collect-codex` command
also parses an existing app-server or CLI JSONL stream.

The Claude receiver starts as a local process for that run and listens only
on `127.0.0.1`. Prep launches Claude with session telemetry directed to the
receiver and verifies that a sample reaches the right session. It counts
native input, output, cache-read, and cache-creation token counters. The
receiver records cumulative series snapshots in batches, keeping the local
dashboard file compact across long runs. It stops the
receiver when the run completes or aborts. Token caps require Claude Code
2.1.214 or newer because earlier versions can inflate streamed usage metrics.
Codex hooks and Claude hooks
report liveness, not tokens. Codex desktop Goal snapshots are written by the
kickoff agent at safe boundaries; they are not a background feed. The Codex
CLI final event arrives too late to stop a single long kickoff turn at a cap.

A token cap stops additional work at the next safe boundary after usage
reaches it. One model request or a delayed telemetry export can exceed the
number. Prep declines a cap when the selected path cannot prove a working
usage feed, and kickoff parks a capped run if that feed later stops.

`orchestrate heartbeat` records task events, hook timestamps, and native usage
samples in the dashboard's script file. `heartbeat collect-codex` reads a
Codex JSONL stream, while `heartbeat collect-claude` listens for local Claude
telemetry. Neither starts or supervises an agent. Hooks are configured for
the selected project and run during prep, not enabled globally by
`orchestrate install`. [Codex requires review and
trust](https://learn.chatgpt.com/docs/hooks) of a new hook definition before
it runs. Prep verifies an actual hook
probe, then binds future stamps to the launched session ID so another session
in the same project cannot make this run appear live. Claude background
sessions [normally move into a new worktree](https://code.claude.com/docs/en/agent-view#how-file-edits-are-isolated)
on their first edit; prep uses a
verified dedicated linked worktree for an automatic Claude launch.

Refactor audits and independent model correctness reviews run only when you
request them for a task or a standing project rule opts in. The agent may
suggest a review when evidence is weak, but deterministic checks, risk-based
acceptance criteria, and its own final diff inspection still apply without one.
The installed policy also keeps persistent guidance stable, uses concise state
checkpoints when context grows or work changes phases, avoids repeated
unproductive tool calls, and delegates narrow tasks when the handoff saves
total tokens or latency. These are agent instructions, not runtime enforcement
of prompt caching, tool-call interception, or a fixed turn limit.

## Conflicts, backups, and safety

Preview changes first:

```sh
orchestrate install --tools codex,claude --dry-run
```

The CLI creates missing files, updates only unchanged CLI-owned files, and
never silently overwrites a different file. It refuses links/reparse points and
records SHA-256 ownership data in its per-user state directory. To explicitly
replace a conflicting file while retaining a timestamped sibling backup:

```sh
orchestrate install --tools codex --dry-run --backup
orchestrate install --tools codex --backup
```

Existing non-CLI configuration—including configuration previously installed by
an older source-based installer—is treated as unowned. It is preserved even if
its content matches the harness; review the preview and use `--backup` only
when you intentionally want the CLI to take over.

## Migrating from agent-harness

Existing `agent-harness` users must manually download and install
`orchestrate`, verify its archive with `SHA256SUMS`, then remove the old
executable from `PATH`. Existing managed-file state is retained: Orchestra
keeps the established internal state identity, so already managed files remain
tracked.

## Upgrading an existing Orchestra installation

The bootstrap scripts detect an existing `orchestrate` on PATH and replace that
exact executable when its location is unambiguous, writable, regular, and
user-owned. They never run `orchestrate install`, so existing configuration and
managed-file state are untouched. Unsafe, linked, system-owned, or ambiguous
installations are preserved and reported with corrective instructions.

Users of an older release can either run its authenticated `orchestrate update`
once when GitHub CLI is already configured, or run the new bootstrap script to
upgrade in place. Releases containing the anonymous updater no longer require
GitHub CLI or authentication.

## Updating

```sh
orchestrate update
orchestrate install --tools codex,claude --dry-run
```

`update` anonymously downloads the matching asset from the latest stable public
GitHub Release, verifies the ZIP against `SHA256SUMS`, and replaces only the CLI
binary. It never changes configuration automatically. Review the dry run, then
run `install` if you accept the configuration updates.

## Add the harness to a project

From the target project root:

```sh
orchestrate init-project --tools codex,claude,cursor
orchestrate init-project --tools codex,claude,cursor --apply
```

The first command is preview-only. `--apply` creates missing selected-tool
files only; existing instructions, rules, and docs are conflicts requiring a
manual merge. It does not remove project-local files.

### Update an existing Cursor project

After updating the CLI, refresh the personal Cursor rule separately:

```sh
orchestrate cursor-rules --print
```

Compare the output with your current Cursor Settings → Rules → User Rules,
then paste or merge it there. The CLI does not edit Cursor User Rules for you.
From each existing project root, preview the current project files:

```sh
orchestrate init-project --tools cursor
```

To obtain the current rule and commands for comparison, run
`orchestrate init-project --tools cursor --apply` in a new empty temporary
directory. On the verified working branch of your existing project, compare
those generated `.cursor/rules/` and `.cursor/commands/` files with your
project's versions and merge the changes you want. Review the reported
conflicts before merging.
`orchestrate init-project --tools cursor --apply` can create missing files, but
it preserves every existing project file, including files you modified; it
does not update or silently overwrite them. Keep a copy of your custom rules
and commands while merging.

## Work-versus-personal boundaries

- Keep this repository generic: no credentials, customer data, internal
  hostnames, tickets, logs, transcripts, or proprietary procedures.
- Put work-only facts in the relevant work repository only when policy allows.
- Treat repository access control as distinct from secret storage.
- Review staged content before committing; `.gitignore` is a guardrail, not a
  secret scanner.

## Change model assignments

Maintainers edit only `models.conf`, then build and release a new binary.
Users run `orchestrate update` and preview their selected installation. The
orchestrator assignment is advisory; explorer, worker, verifier, and reviewer
definitions are rendered from this one file.

## Safe uninstall

```sh
orchestrate uninstall --tools codex,claude --dry-run
orchestrate uninstall --tools codex,claude
```

Only unchanged, CLI-owned global files are removed. Retired skill files are
removed only when their ownership hash still matches. Modified, unknown, or
unsafe files and all project-local harness files are preserved.

## Developing and releasing

Maintainers need the .NET 10 SDK:

```sh
dotnet build AgentHarness.sln --configuration Release
dotnet run --project tests/AgentHarness.Tests/AgentHarness.Tests.csproj --configuration Release
```

Use Conventional Commit messages for changes that should release: `feat:` for a
minor version, `fix:` for a patch, and `feat!:` or `fix!:` for a major version.
After merged changes reach `main`, Release Please opens or updates a release
PR. Merging that PR creates the version tag and GitHub Release; the
workflow then builds self-contained single-file binaries on the matching macOS,
Linux, and Windows runners and uploads the platform archives, `SHA256SUMS`, and
both bootstrap installers. No manual tag creation is required. Checksums provide
download-integrity verification but do not protect against compromise of the
GitHub repository or release account. Apple notarization and Windows code
signing remain deferred until protected signing identities are available.

If an existing release is missing or has corrupt assets, open **Actions →
Release Orchestra → Run workflow**, enter its existing tag (for example
`v1.0.0`), and run it. The recovery path validates that the release exists,
rebuilds all platform assets from that tag, and replaces only the release
archives, checksum manifest, and bootstrap installers when that tag contains
them. It does not create a new version or tag.
