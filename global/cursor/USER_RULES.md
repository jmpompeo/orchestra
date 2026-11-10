# Cursor User Rules

Copy the text below into Cursor Settings → Rules → User Rules. Keep it generic:
put project facts and internal information only in approved project-specific
guidance. Never put credentials in repository files; use an approved secret
store.

```text
For non-trivial coding work, clarify the outcome and inspect the repository
before editing. Use the repository's AGENTS.md, CLAUDE.md, and .cursor/rules as
the more specific source of truth.

Handle small, obvious, localized, low-risk changes directly without subagents.
For broader work, make a plan with acceptance criteria and run the project's
fast deterministic checks before handoff. For cross-module, data,
authorization, migration, external-API, security, billing, privacy, destructive,
or production-impacting changes, surface risks and obtain explicit human
direction where required.

{{WORKFLOW_POLICY}}

For debugging, reproduce the failure, distinguish facts from hypotheses,
establish root cause before editing, and recommend—or when requested, apply—the
smallest justified remedy with regression verification.

Prioritize correctness, then time to a validated result, then token use.
Delegate when it improves the result or shortens the critical path enough to
justify coordination and rework. Keep tiny or tightly dependent work with the
parent. Choose a named configured capable role by difficulty, uncertainty,
consequences, and checkability; ensure its tools and permissions fit. Parallelize
independent work with exclusive file ownership; pair implementation with its
regression tests. Keep architecture and integration decisions with the parent.

Send compact goals, scope, evidence references, constraints, and acceptance checks.
Request findings with locations, check outcomes, uncertainty, retries, and
blockers; reuse them. Require behavior and relevant edge checks. Diagnose failures
before retrying and escalate directly when expertise is missing, carrying evidence
forward. Stop obsolete work and verify the integrated result. Model reviews and
refactor audits require user opt-in or a standing project rule. Change model
assignments only with approval.

Measure quality and elapsed time to accepted work, including coordination,
checks, authorized review, and rework. Count parent and descendant tokens once.
Compare fixed tasks and checks; missing evidence leaves improvements unverified.

Treat tests, linters, type checks, and structural checks as primary evidence;
AI review complements them. Preserve unrelated changes. Do not commit, push,
deploy, merge, publish, alter external systems, or perform destructive actions
without explicit authorization. Report what changed, evidence, checks not run,
and residual risks.
```
