---
name: agentic-feature-delivery
description: Plan, delegate, implement, verify, and review a non-trivial software feature end-to-end. Use for ambiguous or cross-cutting implementation work; skip for small localized edits or read-only advice.
---

# Agentic feature delivery

Own the complete integrated result in one orchestrated session. Minimize total
tokens and latency without weakening correctness or evidence.

1. Read repository instructions and `docs/agent-context.md` when present.
   Read `docs/harness-evolution.md` when it exists and a recurring failure is
   relevant. Inspect the working tree and preserve unrelated changes.
2. Decide whether orchestration is justified. Handle small, obvious,
   localized, low-risk feature work directly without subagents, even when this
   skill was explicitly invoked. Use a targeted check. Model reviews are opt-in
   at every risk tier.
3. Resolve meaningful ambiguity before editing. Establish outcomes,
   constraints, non-goals, edge cases, and acceptance criteria. Ask only about
   decisions that materially change behavior, architecture, risk, cost, or
   destructive scope.
4. Before the task's first repository edit, satisfy the shared Git branch
   preflight in the always-on workflow policy. Carry the verified branch
   through skill transitions.
5. Assign a risk tier: localized, module-level, cross-cutting, or
   high-consequence. For high-consequence work, obtain explicit human
   acceptance criteria before changing security, billing, privacy, destructive,
   or production-impacting behaviour.
6. Explore actual code paths and tests. Delegate focused read-only questions
   when this protects the parent context or materially reduces total tokens or
   latency.
7. Build a dependency-aware task graph. Every delegated task must state its
   goal, acceptance criteria, owned scope or files, relevant context,
   constraints, validation command, and return contract.
8. Parallelize only independent work. Parallel writers must have explicit,
   non-overlapping file ownership. Never overlap shared migration, schema, or
   integration surfaces.
9. Delegate adaptively rather than assigning one agent to every phase. Keep a
   small localized change with the parent when the handoff would cost more than
   the work. Give faster capable agents only the context required for a bounded
   task, reuse compact findings across phases, combine implementation with its
   focused tests when ownership aligns, and stop obsolete branches early.
10. Only when the user explicitly requests a refactor audit for this task or a
    standing project rule opts in, wait for a reviewable draft or diff, then
    launch the lowest-cost capable read-only subagent using the refactor-code
    skill in audit mode. Scope it to the affected methods or functions and
    minimum context, never the whole file by default. Collect its result before
    handoff. Report pre-existing smells without fixing them; address problems
    introduced by the feature within its approved scope.
11. Keep architecture and integration decisions with the parent. Inspect every
   returned change and the final diff; subagent reports are not proof.
12. Run fast deterministic checks before handoff, then the broader checks the
   risk tier requires before integration. Use tests, linters, type checks, and
   structural checks as primary feedback; model review complements them.
   Separate new failures from pre-existing or environmental failures.
13. For behaviour-critical work, use approved fixtures or explicit manual
    acceptance steps. Do not treat agent-authored tests alone as sufficient
    evidence when trusted examples are available.
14. Only when the user explicitly requests an independent correctness review
    for this task or a standing project rule opts in, obtain a separate
    read-only review, address material findings, and rerun affected checks.
    A refactor audit does not opt in to a correctness review, or vice versa.
15. When a pattern has failed at least twice, propose the smallest durable
    control in `docs/harness-evolution.md`; never record sensitive data or raw
    transcripts. Stop only when acceptance criteria are met or a concrete
    blocker remains.

## Skill transitions

This is the primary workflow for a feature task. Keep the feature goal and plan
when a temporary detour is needed. If a bug surfaces during implementation,
use the agentic-debugging skill to diagnose and resolve it within the existing
authorization, then return here. Use the grill-me skill for unsettled material
decisions and return with the confirmed choices. Necessary structural edits
remain part of this feature; a later, separately requested cleanup can make
refactor-code the primary workflow. The read-only refactor audit above is a
subagent review, not a workflow switch. Carry the goal, authorization,
evidence, decisions, constraints, and remaining work through each detour.
Announce a clear transition briefly, return automatically when its purpose is
met, and ask only about new material decisions or scope. Do not repeat settled
discovery or expand edit permission during a transition.

Do not commit unless the user requests it. Never push, deploy, merge, publish,
modify external systems, or perform destructive actions unless the user
explicitly authorizes the exact action.

Return one handoff containing the outcome, risk tier, decisions and assumptions,
files changed, deterministic and behavioural evidence, review findings, checks
not run with reasons, unresolved risks or blockers, and required user action.
