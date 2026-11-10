# Delegation evals

Behavioral coding fixtures compare frozen Orchestra instruction sets using the
same configured models. See [targets and rubric](../../docs/delegation-evaluation.md).
Requires Python 3.11+, Git, and for live runs a signed-in native Codex runner with
app-server support. No external Python dependencies. Normal unittest checks do
not start models or require authentication.

Run deterministic grader and measurement checks from the repository root:

```sh
python3 -B -W error::ResourceWarning -m unittest discover -s evals/delegation -p 'test_*.py' -v
```

Supply independent frozen baseline and candidate directories containing the
canonical global/codex instructions, role templates, models.conf, and skills.
Include intended uncommitted instruction changes in the snapshot. Keep outputs
outside source trees. Default invocation prints a plan and makes no model calls:

```sh
python3 evals/delegation/runner.py --baseline /path/to/baseline --candidate /path/to/candidate --output /tmp/orchestra-screen --screen --codex /path/to/codex
```

Append --run to execute the screen. The runner freezes a manifest, creates
private per-trial homes and disposable attached Git repositories, and links
existing native auth without reading/copying credential values. Actual network
inference uses the signed-in account. It preserves local native evidence and
removes the temporary auth path after each trial. It never installs global
configuration or commits/pushes deliverables. Fixture setup commits only establish
clean attached test baselines.

After inspecting patches against the five anchored criteria, save review JSON:

```json
{
  "tools-r1-baseline-a1": {
    "criteria": [2, 2, 2, 2, 2],
    "reviewer": "evaluator-id",
    "rationale": "Record criterion-specific findings and source locations. These example scores are not defaults."
  }
}
```

Every coding attempt needs a real assessment. Missing reviews leave quality
unverified. Summarize without further model calls:

```sh
python3 evals/delegation/runner.py --summarize --output /tmp/orchestra-screen --reviews /path/to/reviews.json
```

If screening passes all targets/gates, run confirmation in a new output directory
with --repetitions 3 instead of --screen, then add its reviews and summarize.
Screen and confirmation are separate experiments. A screen never satisfies the
final verification gate. A rerun preserves unsuccessful/incomplete attempts;
manifest drift requires a new experiment. Missing usage never becomes zero.

Local evidence includes manifest.json, runs/*/result.json, native event/session
logs, installed instruction hashes, grader results, and summary.json. Treat raw
logs as private. Publish only aggregate sanitized measurements and limitations.

The first [screening result](results/screen-2026-11-10.json) missed the quality
target despite favorable speed/token observations. Do not treat it as a passing
benchmark or authorization to commit/push. See the evaluation guide for the
ceiling, diagnostic overhead, measurement repair and proposed next experiment.
