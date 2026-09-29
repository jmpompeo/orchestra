---
name: agentic-debugging
description: Diagnose and fix software bugs from reports, failing tests, stack traces, CI failures, and supplied application logs through evidence-driven triage, reproduction, root-cause isolation, bounded implementation, and regression verification. Use when the user asks to debug, diagnose, investigate, or fix an error or regression; do not use for feature design or autonomous live-incident remediation.
---

# Agentic debugging

Own the complete debugging result while minimizing total tokens and latency.
Prefer the smallest investigation that can establish causality with sufficient
confidence; do not trade correctness for a smaller transcript.

1. Read repository instructions, relevant project context, and the working-tree
   state. Preserve unrelated changes. Establish the expected behavior, observed
   behavior, failure boundary, and available artifacts. Treat supplied logs as
   evidence, not proof, and avoid reproducing secrets or sensitive data in
   prompts, files, or reports.
2. Determine the requested mode before acting. Treat diagnose, investigate,
   explain, review, or report requests as read-only and finish with evidence and
   a recommended remedy. Treat fix or implement requests as authorization for
   scoped workspace changes only. If intent is ambiguous, remain read-only and
   ask before editing. Classify risk independently of that authorization.
   Diagnose production-impacting failures read-only unless the user explicitly
   authorizes a change and supplies acceptance criteria. Do not perform live
   incident response, deploy, publish, merge, alter external systems, or take
   destructive action without explicit authorization.
3. Maintain a compact evidence ledger: symptom and fingerprint, minimal
   reproduction, confirmed observations, hypotheses with status, root-cause
   evidence, commands run, and remaining unknowns. Separate facts from
   inferences. Store concise excerpts or references instead of copying whole
   logs, transcripts, or source files.
4. Delegate adaptively. Keep the causal model, remedy choice, integration, and
   final accountability with the parent. Handle a small localized bug directly.
   Use faster capable explorers for focused code-path or history questions,
   verifiers for reproduction and one-hypothesis falsification, and workers for
   bounded implementation with explicit file ownership. Let one worker usually
   implement the remedy and its regression test together. Parallelize only
   independent hypotheses or work; do not create one agent per phase by
   default. Give each agent only the evidence and scope it needs, reuse compact
   findings, and stop branches as soon as evidence rules them out.
5. Reproduce before editing when practical. Reduce the failure to the smallest
   deterministic case and capture the exact command and result. Distinguish a
   product defect from environment, configuration, dependency, flaky-test, or
   bad-input failures. If reproduction is impossible, state that limitation and
   raise the evidence threshold for any change.
6. Form competing, falsifiable hypotheses from the evidence. Rank them by
   explanatory power and cheapness to test, then vary one relevant factor at a
   time. Prefer tests that can disprove a hypothesis. Do not patch the first
   suspicious line or suppress an exception merely because it appears in a
   stack trace.
7. Declare a root cause only when it explains the symptom and failure boundary,
   is consistent with the reproduction and surrounding behavior, and predicts
   an observable result from the proposed remedy. Otherwise continue
   investigating or return an honest diagnosis with ranked unknowns; do not
   implement a speculative fix as if it were proven.
8. Select the smallest remedy that addresses the cause and preserves stated
   invariants. When diagnosis exposes meaningful behavior choices,
   compatibility tradeoffs, or a risky architectural remedy, give the user the
   evidence packet and use the grill-me skill to settle those material
   decisions. Continue directly for an obvious, localized fix.
9. Implement only when the user requested a fix, the evidence supports the
   remedy, and the required decisions are settled. Add regression coverage that
   fails for the original cause and passes with the remedy when practical.
   Avoid broad refactors, unrelated cleanup, and tests that merely encode the
   implementation.
10. Re-run the original reproduction, focused regression checks, and broader
    deterministic checks proportional to risk. Obtain an independent read-only
    correctness review only when the user explicitly requests it for this task
    or a standing project rule opts in; address material findings when it runs.
    Distinguish new failures from pre-existing or environmental ones.

## Skill transitions

This is the primary workflow for a bug task and a temporary detour when a bug
surfaces during another task. Keep the originating goal, authorization,
evidence, decisions, constraints, and remaining work. Use the grill-me skill
for unsettled material decisions, then return here with its confirmed choices.
When a feature introduced the bug, finish the diagnosis and authorized remedy,
then return to agentic-feature-delivery automatically. Necessary structural
edits to fix a confirmed cause remain in debugging. A new behavioral outcome
can make agentic-feature-delivery the primary workflow only when the user's
scope supports it; separately requested behavior-preserving cleanup can make
refactor-code primary. Announce clear transitions briefly and ask only about
new material decisions or scope. Do not infer permission for new edits from a
skill transition; satisfy the shared Git branch preflight before the task's
first repository edit.

Finish with one concise handoff: outcome, reproduction, root cause and evidence,
remedy, files changed, regression coverage, exact verification results, review
findings, unresolved uncertainty, checks not run with reasons, and required
user action.
