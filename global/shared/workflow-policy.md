## Workflow routing and edit preflight

- Before the first repository edit in any task, inspect Git status and HEAD.
  Require a Git repository, a clean working tree, and an attached HEAD. Identify
  the intended base and working branch. If the user supplied both, verify them;
  otherwise propose the missing base or branch and wait for confirmation.
  If an existing branch diverges from the confirmed base, disclose it and wait
  for a decision before switching. Create or switch to the working branch,
  then verify the resulting branch and HEAD before editing. Never bypass this
  gate, silently stash or discard work, or edit from an unclear or unsafe Git
  state. Read-only investigation may proceed while the gate is unresolved.
  Carry the verified branch and this task's edits through skill transitions.
  Do not repeat the clean-tree gate for those edits. Stop and re-establish the
  branch decision only if the branch, base, or HEAD changes unexpectedly, or
  unrelated changes appear.
- Choose one primary workflow for the requested outcome: agentic-feature-delivery
  for a feature or behavior change, agentic-debugging for a defect, and
  refactor-code for separately requested behavior-preserving cleanup. Use
  grill-me when material choices need an interview. Use bootstrap-agent-harness
  as read-only preparation when project context is incomplete.
- Handle small, obvious, localized, low-risk work directly without subagents,
  even when a feature workflow is explicitly invoked. A localized, low-risk
  feature needs a targeted check. Run a read-only refactor audit or an
  independent model correctness review only when the user explicitly requests
  it for this task or a standing project rule opts in. The agent may suggest a
  review when evidence is weak, but must not launch it without opt-in. Keep
  deterministic checks, risk-based acceptance criteria, and the parent's own
  final diff inspection regardless of review opt-in.
- Keep the primary workflow and task plan in charge when another skill is
  needed. Announce a clear transition briefly, carry the goal, existing
  authorization, evidence and checks, settled decisions and constraints, and
  remaining work, then return to the primary workflow. Do not repeat settled
  discovery or ask again for authorization that still applies. Ask the user
  when the outcome, edit scope, or a material safety decision changes; a skill
  transition never grants broader permission by itself.
- Diagnose a failure discovered during feature work with agentic-debugging,
  then return to feature delivery with the result. Keep structural edits needed
  for an approved feature or fix inside its primary workflow. The read-only
  refactor-code audit during feature delivery is a bounded subagent review,
  not a change of primary workflow. A standalone refactor that reveals a
  defect or needed behavior change pauses until the appropriate workflow
  settles behavior and scope. Bootstrap returns its findings to the workflow
  that requested project context; writing project context still needs specific
  authorization.

## Token efficiency

- Keep reusable prompt guidance stable. Put volatile task values in task
  context, not persistent instructions; do not add session metadata to shared
  policy text.
- When context grows or work changes phases, retain a concise checkpoint of
  the goal, settled decisions, key evidence, and next action. Trim repetitive
  tool output before carrying it forward; keep details needed for verification.
- Before repeating a tool call, check whether its objective and arguments
  duplicate an unproductive recent attempt. Change the approach or report a
  concrete blocker instead of looping. Do not impose a fixed tool-turn ceiling.
- Route bounded tasks to cheaper capable agents when the handoff saves total
  tokens or latency. Give them only needed context; keep tiny tasks with the
  parent when delegation would cost more.


## Validation and delegation evidence

- Choose checks from the affected behavior and its transitive dependents, including
  embedded assets, shared configuration, generated output and installation paths.
  Keep repository-required checks authoritative. Use the portable validate.mjs
  runner with the project's reviewed manifest when available; it plans by default,
  runs only with --run, and records command, reason, duration and result. Unknown
  paths or dependency uncertainty require broader checks. Do not ask about builds
  routinely or rerun passing checks without a relevant change or unresolved risk.
- Name the installed custom role explicitly when delegating; generic agents may
  inherit the parent model. Send only the goal, owned files, relevant symbols and
  constraints, acceptance checks and return contract. Avoid full-history handoffs
  when a compact brief is sufficient. Require reports of retries and blockers.
- Distinguish configured, rendered/installed, selected/requested and runtime-reported
  model and reasoning effort. Use orchestrate models inspect for installed drift;
  neither a template nor token totals prove the executed model. Label unavailable
  runtime evidence. Use models candidate and representative evaluations before
  requesting approval to change models.conf; never adopt a candidate automatically.
- Measure accepted work including retries, review and rework. Report agent latency,
  tokens and cost separately from observed user overhead and developer productivity.
  Missing human observations mean the productivity outcome is unverified.

## Successful-run cleanup

- Track run-owned temporary worktrees, scratch files, dependency installs,
  processes and configuration as they are created. Reuse suitable resources
  instead of creating a worktree per task. Creation of disposable run resources
  includes permission for their safe cleanup under these rules.
- After all tasks and final checks pass, keep the final deliverable and concise
  run/validation evidence, then retire run-owned resources no longer needed.
  Preserve the checkout containing uncommitted deliverables. Before removing a
  worktree, check tracked, untracked and ignored files, unique commits, and
  active sessions; verify useful work and required evidence survive elsewhere.
  Use the native managed-worktree archive tool when applicable, otherwise normal
  Git worktree removal. Never force removal, delete needed branches or commits,
  or sweep pre-existing/user-owned resources.
- If ownership, activity, preservation or removal safety is uncertain, retain
  the resource and ask the user with its path and the specific uncertainty.
  Complete independent safe cleanup while awaiting an answer. Preserve resources
  needed by failed, blocked or resumable runs. Report what was removed and what
  remains, with reasons; do not claim cleanup is complete while decisions remain.
