---
name: refactor-code
description: Improve existing code structure, readability, and maintainability without changing observable behavior. Use for standalone requests to refactor, simplify, reorganize, clean up, or reduce duplication in code, and for an explicitly opted-in read-only audit during agentic feature delivery; do not use for feature development, behavior changes, broad rewrites, or bug fixes.
---

# Refactor code

Preserve observable behaviour while making the smallest structural improvement
that materially helps a reader or maintainer. Prefer clear code over clean-code
ceremony.

## Determine the invocation context

- When invoked standalone for a refactor request, execute the complete refactor
  workflow and edit only approved files.
- When explicitly opted in and invoked by agentic-feature-delivery as a
  subagent, perform only the read-only audit workflow below. Never expand the
  feature or edit code from this mode.

## Execute a refactor

1. Read repository instructions and inspect the working tree. Preserve
   unrelated changes.
2. Obtain an explicit writable-file allowlist before editing. Do not infer
   permission for source, tests, fixtures, configuration, or generated files
   merely because they are related. If another file must change, stop and ask
   for approval with a concise, concrete reason.
3. Read only the context needed to establish behaviour and dependencies. The
   allowlist restricts writes, not necessary read-only inspection. Delegate
   broader call-site, contract, test, or neighbouring-module discovery to the
   lowest-cost capable read-only explorer; keep tiny local reads with the
   parent when delegation would cost more tokens.
4. Reject work that requires changing observable behaviour, public contracts,
   or user-visible output; redirect it to the appropriate feature or debugging
   workflow. Otherwise, use the grill-me skill when the refactor crosses files or
   responsibilities, changes internal dependency direction, restructures state
   or data flow, affects concurrency or persistence, lacks meaningful tests, or
   has multiple plausible designs with material tradeoffs. Skip grilling for
   localized internal renames, representation or interpolation rewrites that
   preserve emitted text, and isolated few-line changes whose callers and
   dependencies are unaffected.
5. State the observable invariants. Identify and run the narrowest relevant
   tests before editing. If relevant tests fail, resolve whether the baseline
   is trustworthy before proceeding. Treat absent or inadequate coverage as a
   hard stop: do not edit, and do not substitute agent-authored ad hoc checks
   for established coverage. Use the grill-me skill and wait for the user to choose
   between adding approved characterization tests and explicitly accepting an
   unverified refactor. Test files must also appear in the writable-file
   allowlist. Never treat user acceptance of risk as proof of preserved
   behaviour.
6. Plan the smallest coherent transformation. Make non-goals explicit and
   avoid mixing feature work, bug fixes, broad formatting, or unrelated cleanup
   into the refactor.
7. Implement for readability and maintainability. Apply KISS and YAGNI first,
   emphasize single responsibility, and use DRY and other SOLID principles only
   where they reduce cognitive load. Inline needless indirection. Do not
   extract private helpers, introduce layers, or generalize code merely to
   satisfy a pattern or appearance of cleanliness.
8. Rerun the baseline tests and proportionate static checks. Inspect the final
   diff for allowlist violations, accidental API or output changes, noisy
   churn, and speculative abstraction. Compare before-and-after behavioural
   evidence rather than relying only on newly authored tests.
9. Return a concise handoff with changed files, invariants, baseline and final
   evidence, review findings, accepted gaps, and residual risk.

For a standalone refactor, this is the primary workflow. Necessary structural
edits within a feature or bug task belong to that task's primary workflow; they
do not trigger this skill. If a test exposes a defect, stop refactor edits and
use agentic-debugging as a temporary detour. Carry the goal, failing check,
observable invariants, authorization, decisions, constraints, file allowlist,
and remaining work; return automatically when the baseline and writable-file
scope are settled. A newly requested behavioral outcome can make
agentic-feature-delivery primary when the user's scope supports it. Announce
clear transitions briefly and ask only about new material decisions or scope.
Switching skills does not grant permission to edit additional files. Satisfy
the shared Git branch preflight before the task's first repository edit.

## Audit feature work

1. Accept an exact list of affected methods or functions from the parent
   workflow. For new code, wait until the relevant definitions exist.
2. Inspect only those definitions, their diff, and the minimum directly needed
   context. Do not read an entire file when targeted spans answer the question.
3. Look for concrete simplifications, unclear responsibility, needless
   indirection, harmful duplication, and maintainability risks. Avoid stylistic
   preferences and speculative abstractions.
4. Separate pre-existing debt from problems introduced by the feature.
   Report pre-existing opportunities without editing them or expanding feature
   scope. Return introduced regressions or maintainability problems as normal
   feature-review findings that may be corrected before handoff.
5. Report only actionable findings with location, impact, and a short direction;
   explicitly say when no meaningful smell was found. Keep the response compact.
