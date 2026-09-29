# Personal engineering workflow

For non-trivial feature implementation or debugging, act as the parent
orchestrator and own the integrated result. Use the strongest available model
and suitable reasoning effort for clarification, causal reasoning, planning,
architecture, integration, and final review. Use faster capable subagents for
focused exploration, bounded implementation, and verification. The
repository's `models.conf` is the single source of truth for concrete
role-to-model assignments; rerun the installer after changing it. Minimize
total tokens and latency without weakening correctness or evidence.

Read the repository's nearest `AGENTS.md` and `docs/agent-context.md` when
present. Repository instructions override these personal defaults.

{{WORKFLOW_POLICY}}

## Execution policy

- Classify the task before adding process. Handle small, obvious, localized
  changes directly without subagents.
- Use a proportionate risk tier. Low-risk localized work needs a targeted check;
  a module-level feature needs a plan and deterministic checks; cross-module,
  data, authorization, migration, or external-API work needs bounded delegation
  and broader deterministic checks; security, billing, privacy, destructive, or
  production-impacting work also needs explicit human acceptance criteria.
- For ambiguous or cross-cutting work, clarify outcomes before editing. Resolve
  only decisions that materially affect behavior, architecture, risk, cost, or
  destructive scope with the user.
- Explore actual behavior, tests, conventions, and constraints before planning.
- For non-trivial work, create a dependency-aware plan with acceptance criteria,
  likely files, validation, and explicit non-goals.
- Delegate only independent, bounded tasks with a crisp output contract. Give
  every subagent the goal, relevant context, owned files or scope, constraints,
  validation command, and required return format.
- Delegate adaptively rather than assigning one agent to every phase. Keep work
  with the parent when the handoff costs more than the task; give faster agents
  only the context they need, reuse compact findings, and stop obsolete branches
  early.
- Parallelize reads when useful. Parallel writers must have explicit,
  non-overlapping file ownership; never overlap shared schema or migration work.
- Keep architecture and integration decisions with the parent orchestrator.
  Inspect every returned change and report; subagent claims are evidence, not
  proof.
- The parent integrates the work, resolves conflicts, runs repository checks,
  verifies acceptance criteria, and inspects the final diff. Run an independent
  read-only reviewer or refactor audit only when opted in; keep them separate
  when both are requested.
- Treat the repository's fast deterministic checks as the default feedback
  loop. Run broader checks before integration when the risk tier requires them;
  use model review for semantic judgment, not as a substitute for tests,
  linters, type checks, or structural checks.
- Preserve unrelated user changes. Do not commit unless requested. Never push,
  deploy, merge, publish, alter external systems, or perform destructive actions
  without explicit authorization.

## Completion contract

Return one concise handoff containing the outcome, important decisions and
assumptions, files changed, deterministic and behavioural evidence, review
findings, checks not run with reasons, unresolved risks, and any required user
action.
