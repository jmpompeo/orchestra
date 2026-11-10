# Argument-boundary repair, version 2

This is an additive replacement for the `status-json` task only. The stopped
experiment, its frozen v1 corpus, reference, hashes and result records remain
unchanged. This repair changes the benchmark reference and checks; it does not
modify Orchestra's shipped CLI or delegation policies.

The reference now rejects a standalone `--json` token on non-status commands
before generic parsing, including help aliases. The shared `--tools` value reader
rejects missing, empty/whitespace, and option-shaped values in both syntaxes.
Leading whitespace is normalized for validation, while valid values are preserved.
This closes both `--tools --json` and `--tools=--json`, including whitespace forms.

[Reference.patch](Reference.patch) contains the C# remedy against the same frozen
source commit. [BoundaryChecks.cs](BoundaryChecks.cs) is the readable C# matrix.
It checks argument errors before output, state loading, fixture changes, HTTP or
replacement. A broken state sentinel makes incorrect validation order observable.
All HTTP/replacement paths use injected fakes; project working directories are
isolated. Compatibility controls accept valid spaced/equal tool syntax, normal
JSON status, and ordinary help.

`status-json.json` contains the explicit v2 task contract, original three status
groups plus the new boundary group, repaired reference patch, unchanged status
faults rebased onto it, and two boundary faults. A failure to compile never earns
fault-detection credit. This version changes the case's regression corpus, so do
not compare its scores directly with frozen v1 scores or pool old performance data.

Run the focused deterministic checker from the repository root:

```sh
python3 -B evals/delegation/repository-five/argument-boundary-v2/check_boundary.py --source /path/to/frozen-orchestra-source --output /tmp/fresh-boundary-check
```

Optionally add `--candidate /path/to/saved-candidate-workspace` to confirm the
matrix rejects the previously observed incomplete solution. The checker copies
inputs, builds each required C# variant once, and uses a fresh output directory.
It runs the repaired reference's four groups and existing .NET executable suite,
then checks only the newly relevant faults and optional saved candidate. No
benchmark model calls, authentication, real updates or performance measurements.
Scratch source/build directories are cleaned; concise logs/summary remain at the
requested output. The frozen source and saved candidate are never modified.

To use v2 in a future benchmark, replace only the status-json entry in a **new**
external harness's cases.json with this object, then freeze the new inputs and
protocol before inference. Do not resume or rewrite the stopped v1 experiment.
The remaining four task definitions and statistical code are unchanged.

## Focused validation

[Recorded checks](validation.json) pass: 90 malformed-argument cases and five
valid-syntax controls, all three existing status groups, and the existing .NET
executable suite. The old reference, permissive-value fault, and saved candidate
all compile and are rejected by runtime C# assertions. The remaining four tasks,
unchanged status-output faults, statistical tests and model benchmark were not
rerun because this change is confined to the status argument boundary. Zero new
benchmark model calls. Historical v1 input hashes and results remain unchanged.
