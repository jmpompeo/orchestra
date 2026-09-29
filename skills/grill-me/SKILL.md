---
name: grill-me
description: Stress-test a non-trivial plan, design, or decision through a structured interview before implementation. Use when the user asks to be grilled, wants to pressure-test their thinking, or needs important design branches resolved; do not use for small, obvious, localized changes.
---

# Grill me

Use this skill when a non-trivial change has unresolved material decisions.
This workflow is adapted from Matt Pocock's `grilling` skill under the MIT
license; see `LICENSE`.

1. Read the repository instructions and relevant project context. Inspect the
   codebase for facts that can answer a question before asking the user.
2. Map the unresolved work as a decision tree. Separate facts to discover from
   decisions that require the user's intent. When invoked by another workflow,
   incorporate that workflow's required interview fields and ask each question
   only once. For `$prep`, cover the project and agent tool, outcome and task
   dependencies, acceptance criteria and non-goals, base and working branch,
   autonomous and parked decisions, permissions and commit scope, time and
   enforceable token budgets, stop conditions, and exact validation commands.
   Prep owns the later Git, environment, hook, and artifact checks.
3. Work in rounds. In each round, ask every independent decision whose
   prerequisites are settled. Number each question and give a recommended
   answer with a brief rationale. Do not ask a question whose answer depends on
   another open question in the same round.
4. Let each answer update the tree. Explore the repository or delegate focused
   read-only discovery for facts; do not make the user answer questions that
   the available context can resolve.
5. Do not edit code, files, configuration, or external systems during the
   grilling session. The user owns decisions; wait for their answers between
   rounds.
6. When no unresolved decision branches remain, return a decision summary:
   outcome, scope and non-goals, settled choices and rationale, constraints,
   risks, acceptance criteria, and remaining unknowns. Ask for confirmation
   only if a new material decision or scope ambiguity remains.
7. Return automatically to the originating workflow, whether debugging,
   feature delivery, refactoring, bootstrap, or another task. Briefly state the
   transition. Offer to record durable, project-specific decisions in
   `docs/agent-context.md` or recurring process lessons in
   `docs/harness-evolution.md`; never write either without authorization.

Grill-me is a temporary read-only detour, not a new primary workflow. Carry the
goal, existing authorization, evidence, decisions, constraints, and remaining
work back into the originating workflow in the same task. Do not reopen settled
questions or treat the interview as permission for edits outside the original
scope. Any later repository edit must satisfy the shared Git branch preflight.
