using AgentHarness;
using System.Text.Json;

public static class ModelDiagnosticsTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "orchestra-model-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assets = new AssetStore();
        void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED models: " + message); }
        (int Code, string Output, string Error) Invoke(params string[] args)
        {
            var originalOut = Console.Out; var originalError = Console.Error;
            using var output = new StringWriter(); using var error = new StringWriter();
            try { Console.SetOut(output); Console.SetError(error); return (ModelDiagnostics.Run(args, assets, root), output.ToString(), error.ToString()); }
            finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        }
        try
        {
            var missing = Invoke("inspect", "--tool", "codex");
            Check(missing.Code == 0 && missing.Output.Contains("installed=missing") && missing.Output.Contains("runtime-reported=unavailable") && missing.Output.Contains("advisory only"), "missing installed and unavailable evidence");
            Check(!Directory.Exists(Path.Combine(root, ".codex")), "inspection never creates installation");
            var config = assets.ReadText("models.conf");
            string Render(string path)
            {
                var text = assets.ReadText(path);
                foreach (var line in config.Split('\n'))
                { var pair = line.Split('=', 2, StringSplitOptions.TrimEntries); if (pair.Length == 2 && !pair[0].StartsWith('#')) text = text.Replace("@@" + pair[0] + "@@", pair[1]); }
                return text;
            }
            var agentRoot = Path.Combine(root, ".codex", "agents"); Directory.CreateDirectory(agentRoot);
            var installed = Path.Combine(agentRoot, "workflow_worker.toml");
            var expected = Render("global/codex/agents/workflow_worker.toml.tmpl");
            File.WriteAllText(installed, expected);
            Check(Invoke("inspect", "--tool", "codex").Output.Contains("drift=matched; assignment=matched"), "rendered matches installed");
            File.WriteAllText(installed, expected + "\n# local modification\n");
            var modified = Invoke("inspect", "--tool", "codex");
            Check(modified.Output.Contains("drift=full-file modified; assignment=matched"), "full file drift independent from assignment");
            File.WriteAllText(installed, expected.Replace("model_reasoning_effort = \"medium\"", "model_reasoning_effort = \"high\""));
            Check(Invoke("inspect", "--tool", "codex").Output.Contains("installed effort=high; drift=full-file modified; assignment=stale/overridden assignment"), "effort drift");
            File.WriteAllText(installed, expected.Replace("model = ", "model = \"other\" # "));
            Check(Invoke("inspect", "--tool", "codex").Output.Contains("stale/overridden assignment"), "model override drift");
            var evidence = Path.Combine(root, "evidence.json");
            var observations = new[] { new { tool = "codex", sessionId = "session-a", role = "worker", source = "user-log", stage = "runtime-reported", model = "reported-model", effort = "low" }, new { tool = "codex", sessionId = "session-a", role = "worker", source = "request", stage = "requested", model = "requested-model", effort = "medium" } };
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { observations }));
            var reported = Invoke("inspect", "--tool", "codex", "--evidence", evidence);
            Check(reported.Code == 0 && reported.Output.Contains("runtime-reported=reported-model; effort=low; session=session-a; role=worker; source=user-log; reported/unverified") && reported.Output.Contains("requested=requested-model"), "explicit reports preserve identity, stage, provenance");
            foreach (var field in new[] { "tool", "sessionId", "role", "source", "stage", "model" })
            {
                var fields = new Dictionary<string, string> { ["tool"] = "codex", ["sessionId"] = "s", ["role"] = "worker", ["source"] = "log", ["stage"] = "runtime-reported", ["model"] = "m", ["effort"] = "medium" }; fields.Remove(field);
                File.WriteAllText(evidence, JsonSerializer.Serialize(new { observations = new[] { fields } }));
                Check(Invoke("inspect", "--tool", "codex", "--evidence", evidence).Code == 2, "missing evidence " + field + " rejected");
            }
            File.WriteAllText(evidence, "{\"observations\":[{\"tool\":\"codex\",\"sessionId\":\"s\",\"role\":\"orchestrator\",\"source\":\"turn-request\",\"stage\":\"requested\",\"model\":\"m\",\"effort\":null}]}");
            Check(Invoke("inspect", "--tool", "codex", "--evidence", evidence).Output.Contains("requested=m; effort=unavailable"), "missing runtime effort is unavailable");
            File.WriteAllText(evidence, "{\"totalTokens\":999}");
            Check(Invoke("inspect", "--tool", "codex", "--evidence", evidence).Code == 2, "tokens alone are not identity evidence");
            File.WriteAllText(evidence, "{");
            Check(Invoke("inspect", "--tool", "codex", "--evidence", evidence).Code == 2, "malformed evidence rejected");
            var candidate = Path.Combine(root, "candidate.conf");
            File.WriteAllText(candidate, config.Replace("CODEX_WORKER_MODEL=", "CODEX_WORKER_MODEL=candidate-"));
            var before = File.ReadAllText(installed);
            var proposed = Invoke("candidate", "--config", candidate);
            Check(proposed.Code == 0 && proposed.Output.Contains("codex/worker: CHANGED") && proposed.Output.Contains("Static compatibility does not prove runtime compatibility") && proposed.Output.Contains("Experiment results: unavailable"), "candidate checks remain static");
            Check(File.ReadAllText(installed) == before, "candidate does not mutate installed files");
            var comparison = new { matchedTasks = 1, variants = new { baseline = new { attempts = 1, failed = 0, retried = 0, acceptedTasks = 1, deterministicPassedTasks = 1 }, candidate = new { attempts = 2, failed = 1, retried = 1, acceptedTasks = 0, deterministicPassedTasks = 0 } }, metrics = new { }, targets = new { status = "unavailable" } };
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { version = 1, evidence = "recorded-trials", pooled = comparison, combined = comparison, batches = new { model = comparison }, heldOut = comparison, unmatched = Array.Empty<string>() }));
            var evaluated = Invoke("candidate", "--config", candidate, "--results", evidence);
            Check(evaluated.Code == 0 && evaluated.Output.Contains("reported/unverified") && evaluated.Output.Contains("readiness to adopt") && evaluated.Output.Contains("batch model"), "evaluation reports retain failure outcomes and unverified provenance");
            File.WriteAllText(evidence, File.ReadAllText(evidence).Replace("recorded-trials", "synthetic-fixture-only"));
            Check(Invoke("candidate", "--config", candidate, "--results", evidence).Output.Contains("Synthetic fixture only"), "synthetic evaluations cannot establish productivity");
            File.WriteAllText(evidence, "{\"version\":1}");
            Check(Invoke("candidate", "--config", candidate, "--results", evidence).Code == 2, "invalid evaluation report rejected");
            foreach (var invalid in new[] { config + "\nCODEX_WORKER_MODEL=duplicate", config.Replace("CODEX_WORKER_MODEL=", "UNKNOWN_MODEL="), config.Replace("CODEX_WORKER_MODEL=", "CODEX_WORKER_MODEL=bad value ") })
            { File.WriteAllText(candidate, invalid); Check(Invoke("candidate", "--config", candidate).Code == 2, "malformed candidate rejected"); }
            foreach (var invalid in new[] { new[] { "inspect" }, new[] { "inspect", "--tool", "cursor" }, new[] { "inspect", "--tool" }, new[] { "inspect", "--tool", "codex", "--config", candidate }, new[] { "candidate" }, new[] { "candidate", "--apply" }, new[] { "inspect", "--tool", "codex", "--tool", "claude" } })
                Check(Invoke(invalid).Code == 2, "invalid flags rejected");
            var claude = Invoke("inspect", "--tool", "claude");
            Check(claude.Code == 0 && claude.Output.Contains("rendered effort=unavailable"), "Claude does not invent template effort");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Model diagnostics tests passed.");
    }
}
