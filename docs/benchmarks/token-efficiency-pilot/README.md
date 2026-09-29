# Token-efficiency pilot

The adjacent JSONL files are sanitized event logs from one read-only paired
run on a disposable Python range-merging feature. `old.jsonl` used the policy
at `origin/main` before the change; `new.jsonl` used this branch's policy. Both
used the same task prompt, fixture diff, bundled Codex CLI, `gpt-6-sol` at low
reasoning effort, and a 120-second cap. Both ran `python3 -m unittest -q` and
passed two tests. Thread IDs were removed, temporary fixture paths were
replaced with `<fixture-old>` or `<fixture-new>`, and temporary tool-cache
paths were normalized before these logs were saved.

| Measure | Old policy | New policy |
| --- | ---: | ---: |
| Input tokens | 186,906 | 90,018 |
| Cached input tokens | 150,912 | 64,896 |
| Input tokens excluding cached | 35,994 | 25,122 |
| Output tokens | 776 | 470 |
| Elapsed seconds | 42.7 | 17.4 |

This pair used 51.8% fewer input tokens with the new policy. It is a pilot,
not a reliable savings estimate. The old run claimed that it completed a
refactor audit and a correctness review, but the captured events show two
collaboration waits and no reviewer-launch events. We therefore cannot verify
that either review ran or attribute the difference to review token costs.
The new rendered instructions are also 170 words longer, which may increase
the fixed cost of tasks that never trigger reviews.

## Proposed reliability test (not run)

1. Prepare a small, preregistered set of real tasks with fixed starting commits,
   prompts, acceptance checks, and trusted expected behavior. Include tasks
   that previously triggered both reviews and localized tasks as controls.
2. Use the same model, reasoning effort, tool set, and CLI version in each arm.
   Run each task from fresh isolated checkouts. Alternate policy order and
   compare warm-cache and cold-cache runs separately. Strip unused plugins and
   tools from both arms before starting so fixed tool schemas do not dominate.
3. Capture parent and child usage separately: total and cached input, output,
   retries, elapsed time, and review-launch events. Treat a run as invalid if
   the required old-policy reviewers do not actually launch or their usage is
   missing. Include all rework in the task total.
4. Start with four tasks run twice per arm as a budgeted screening stage. If
   results are promising but uncertain, expand only to the preregistered sample
   size needed for a useful paired confidence interval. Stop if a quota cap is
   reached; do not interpret incomplete pairs as savings.
5. Compare paired tokens per accepted task, including child agents and rework.
   Report cached and uncached tokens separately, plus task pass rate and blind
   quality assessment. Call the change reliable only if the confidence interval
   shows a positive saving and quality does not fall below the agreed bar.
