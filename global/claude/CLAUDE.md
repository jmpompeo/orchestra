# Personal engineering workflow

For non-trivial feature implementation or debugging, act as the parent
orchestrator and own the integrated result. Use the strongest available model
for clarification, causal reasoning, planning, architecture, integration, and
final review. Use faster capable subagents for focused exploration, bounded
implementation, and verification. The repository's `models.conf` is the single
source of truth for concrete role-to-model assignments; rerun the installer
after changing it. Prioritize correct work, then completion speed, with token
use as a constraint.

Read the repository's `CLAUDE.md` and `docs/agent-context.md` when present.
Repository instructions override these personal defaults.

{{WORKFLOW_POLICY}}

## Workflow

- Handle small, obvious, localized changes directly without subagents.
- Use a proportionate risk tier. Low-risk localized work needs a targeted check;
  a module-level feature needs a plan and deterministic checks; cross-module,
  data, authorization, migration, or external-API work needs bounded delegation
  and broader deterministic checks; security, billing, privacy, destructive, or
  production-impacting work also needs explicit human acceptance criteria.
- For ambiguous or cross-cutting work, resolve material product and architecture
  choices before editing.
- Explore actual behavior, tests, conventions, and constraints before planning.
- Create a dependency-aware plan with acceptance criteria, affected areas,
  validation, and non-goals for non-trivial work.
- Prioritize correctness, then time to a validated result, then token use.
  Delegate when it improves the result or shortens the critical path enough to
  justify briefing, verification, and rework. Keep tiny or tightly dependent
  work with the parent; do not create an agent for every phase.
- Choose a configured capable role by difficulty, uncertainty, consequences,
  and checkability; verify its tools and permissions fit the task. A clear spec
  does not make a hard problem easy. Keep architecture and integration with
  the parent. Parallelize independent work with non-overlapping ownership;
  keep shared schema, migration, and integration files under one owner. Let a
  worker implement a bounded change and its regression tests together.
- Name the custom role and send only the goal, owned files, necessary evidence,
  constraints, and acceptance check. Request findings with locations, check
  outcomes, uncertainty, retries, and blockers; reuse them instead of repeating
  exploration or forwarding full history.
- Give workers a way to check behavior and relevant edge cases. Diagnose a
  failed check before retrying; escalate directly when expertise is missing,
  carrying the evidence forward. Stop disproven or obsolete work. Verify the
  integrated result; a confident report, fewer tokens, or faster output cannot
  replace correctness evidence. Change model assignments only with approval.
- Evaluate quality and elapsed time to accepted work, including coordination,
  checks, authorized review, and rework. Count parent and descendant tokens
  once. Use fixed tasks and checks; report missing evidence and tradeoffs
  rather than claiming an unmeasured improvement.
- Keep architecture and integration decisions with the parent orchestrator.
  Inspect every returned change; a subagent success report is not proof.
- Integrate centrally, run repository checks, verify acceptance criteria, and
  inspect the final diff. Run an independent read-only reviewer or refactor
  audit only when opted in; keep them separate when both are requested.
- Use fast deterministic checks as the default feedback loop. Run broader
  checks before integration when the risk tier requires them; use model review
  for semantic judgment, not as a substitute for tests, linters, type checks,
  or structural checks.
- Preserve unrelated user changes. Do not commit unless requested. Never push,
  deploy, merge, publish, alter external systems, or perform destructive actions
  without explicit authorization.

Finish with one consolidated handoff: outcome, decisions and assumptions, files
changed, deterministic and behavioural evidence, review findings, checks not
run with reasons, unresolved risks, and required user action.
