---
name: agentic-debugging
description: Diagnose and fix software bugs from reports, failing tests, stack traces, CI failures, and supplied application logs through evidence-driven triage, reproduction, root-cause isolation, bounded implementation, and regression verification. Use when the user asks to debug, diagnose, investigate, or fix an error or regression; do not use for feature design or autonomous live-incident remediation.
---

# Agentic debugging

Own the complete debugging result. Prioritize correctness, then time to a
validated remedy, with token use as a constraint.
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
4. Keep the causal model, remedy choice, and integration with the parent. Use
   the global quality-first delegation rules: named capable roles, appropriate
   tools and permissions, compact evidence, and falsifiable checks. Parallelize
   independent hypotheses only when useful; stop branches ruled out by evidence.
   Keep small bugs and tightly dependent probes with the parent. Pair a worker's
   bounded fix with its regression test. Diagnose failures before retrying and
   escalate directly when expertise is missing, carrying evidence forward.
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
   evidence packet and explicitly ask whether to run `$grill-me` or skip it and
   continue with the recommended remedy. Bypass this prompt for an obvious,
   localized fix.
9. Implement only when the user requested a fix, the evidence supports the
   remedy, and the required decisions are settled. Add regression coverage that
   fails for the original cause and passes with the remedy when practical.
   Avoid broad refactors, unrelated cleanup, and tests that merely encode the
   implementation.
10. Re-run the original reproduction, focused regression checks, and broader
    deterministic checks proportional to risk. Obtain an independent read-only
    review only when explicitly requested or opted in by a standing project rule;
    address material findings. Distinguish new failures from pre-existing or
    environmental ones.

Finish with one concise handoff: outcome, reproduction, root cause and evidence,
remedy, files changed, regression coverage, exact verification results, review
findings, unresolved uncertainty, checks not run with reasons, and required
user action.
