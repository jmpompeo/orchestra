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
  feature needs a targeted check, not a refactor audit or independent reviewer.
  For module-level, cross-cutting, or high-consequence feature work, run a
  bounded read-only refactor audit and a separate independent correctness review.
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
