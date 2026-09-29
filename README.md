# Orchestra

Orchestra is a self-contained .NET 10 CLI, `orchestrate`, for installing
portable personal defaults, project adapters, subagent definitions, and skills
for evidence-driven debugging, feature delivery, project-harness bootstrapping,
and long-running sessions across Codex, Claude Code, and Cursor. The public
repository is available for developing or auditing the harness.

## Long-running sessions

Use `$prep` (or `/prep` in Claude Code or Cursor) to define a bounded run,
verify its checks and permissions, and create a local progress dashboard. Prep
starts a fresh `$kickoff` session when the tool supports it; Cursor users get
the choice of a measured SDK run or the exact command for a new Agent chat.
Kickoff follows the saved task plan and stop conditions. Run files stay in the
project under Git-ignored `.orchestra/runs/`.

The dashboard records native token usage when the selected launch path can
report it: the bundled Codex app-server launcher or Goal checkpoints, Claude Code
telemetry, and Cursor SDK usage events. Codex CLI can report final usage from
JSONL. Manual Cursor Agent chat has no documented cumulative session token
feed, so the dashboard marks usage unavailable there. A token cap is accepted
only after prep proves a live source; it stops further work at the next safe
boundary, which may be after one request exceeds the cap.

See [the long-running session guide](INSTALL.md#prepare-a-long-running-session)
for setup, monitoring, budgets, and tool-specific behavior.

See [INSTALL.md](INSTALL.md) for setup, updates, project adoption, model
assignments, work/personal boundaries, and safe uninstall instructions.
