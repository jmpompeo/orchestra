---
name: agentic-feature-delivery
description: Plan, delegate, implement, verify, and review a non-trivial software feature end-to-end. Use for ambiguous or cross-cutting implementation work; skip for small localized edits or read-only advice.
---

# Agentic feature delivery

Own the complete integrated result in one orchestrated session. Prioritize
correctness, then time to a validated result, with token use as a constraint.

1. Read repository instructions and `docs/agent-context.md` when present.
   Read `docs/harness-evolution.md` when it exists and a recurring failure is
   relevant. Inspect the working tree and preserve unrelated changes.
2. Decide whether orchestration is justified. Handle small, obvious, low-risk
   localized edits directly even when this workflow is invoked. Use a targeted
   check; independent model review remains opt-in at every risk tier.
3. Resolve meaningful ambiguity before editing. Establish outcomes,
   constraints, non-goals, edge cases, and acceptance criteria. Ask only about
   decisions that materially change behavior, architecture, risk, cost, or
   destructive scope.
4. Before implementation, require a Git repository, a clean working tree, and
   an attached HEAD unless the user explicitly opts out for this task. Show the
   current branch and short HEAD, ask the user to confirm the intended base and
   supply the working branch name, then create or switch to that branch. If an
   existing branch diverges from the confirmed base, disclose that and ask
   before switching. Recheck the resulting branch and HEAD. Without an explicit
   opt-out, stop on dirty, non-Git, detached, or unexpected state. With one,
   record the exact exception and user-confirmed starting state. Never stash,
   discard, clean, or switch implicitly.
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
9. Apply the global quality-first delegation rules. Identify the critical path;
   parallelize independent bounded work only when it improves the result or
   completion time after coordination and rework. Name capable configured roles
   with suitable tools and permissions. Give compact evidence and acceptance
   checks, pair implementation with regression tests, reuse findings, and stop
   obsolete branches. Diagnose failures before retrying; escalate directly when
   expertise is missing. Verify the integrated behavior and relevant edge cases.
10. Once a reviewable draft or diff exists, run a bounded read-only refactor
    audit only when explicitly requested by the user or opted in by a standing
    project rule. Scope it to the changed functions and collect findings before
    handoff. Preserve unrelated work.
11. Keep architecture and integration decisions with the parent. Inspect every
   returned change and the final diff; subagent reports are not proof.
12. Run fast deterministic checks before handoff, then the broader checks the
   risk tier requires before integration. Use tests, linters, type checks, and
   structural checks as primary feedback; model review complements them.
   Separate new failures from pre-existing or environmental failures.
13. For behaviour-critical work, use approved fixtures or explicit manual
    acceptance steps. Do not treat agent-authored tests alone as sufficient
    evidence when trusted examples are available.
14. Obtain an independent read-only correctness review only when explicitly
    requested by the user or opted in by a standing project rule. Address
    material findings and rerun affected checks. An audit and a correctness
    review are separate opt-ins.
15. When a pattern has failed at least twice, propose the smallest durable
    control in `docs/harness-evolution.md`; never record sensitive data or raw
    transcripts. Stop only when acceptance criteria are met or a concrete
    blocker remains.

Do not commit unless the user requests it. Never push, deploy, merge, publish,
modify external systems, or perform destructive actions unless the user
explicitly authorizes the exact action.

Return one handoff containing the outcome, risk tier, decisions and assumptions,
files changed, deterministic and behavioural evidence, review findings, checks
not run with reasons, unresolved risks or blockers, and required user action.
