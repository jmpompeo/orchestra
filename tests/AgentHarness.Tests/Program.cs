using AgentHarness;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
}

static void CheckRenderedPolicy(string output, string policy, string destination)
{
    Check(output.Contains(policy, StringComparison.Ordinal), $"{destination} contains the complete shared workflow policy");
    Check(!output.Contains("{{WORKFLOW_POLICY}}", StringComparison.Ordinal), $"{destination} has no unresolved workflow marker");
}

static void CheckFeatureRouting(string output, string destination)
{
    var text = string.Join(' ', output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    Check(text.Contains("localized, low-risk feature work directly without subagents", StringComparison.Ordinal)
        && text.Contains("do not launch a refactor audit or independent reviewer for this tier", StringComparison.Ordinal),
        $"{destination} routes localized low-risk features directly");
    Check(text.Contains("For module-level, cross-cutting, or high-consequence feature work, once a reviewable draft or diff exists, launch", StringComparison.Ordinal),
        $"{destination} gates the refactor audit at module-level or higher");
    Check(text.Contains("For module-level, cross-cutting, or high-consequence feature work, obtain a separate independent read-only correctness review", StringComparison.Ordinal)
        && text.Contains("The refactor audit does not replace this review", StringComparison.Ordinal),
        $"{destination} retains a separate independent review at module-level or higher");
    Check(!text.Contains("Once a reviewable draft or diff exists, launch the lowest-cost capable", StringComparison.Ordinal),
        $"{destination} has no unconditional refactor audit");
}

static (int ExitCode, string Output, string Error) Capture(HarnessApp app, string[] args)
{
    var previousOutput = Console.Out;
    var previousError = Console.Error;
    using var output = new StringWriter();
    using var error = new StringWriter();
    try
    {
        Console.SetOut(output);
        Console.SetError(error);
        var exitCode = app.Run(args);
        return (exitCode, output.ToString(), error.ToString());
    }
    finally
    {
        Console.SetOut(previousOutput);
        Console.SetError(previousError);
    }
}

static byte[] ReleaseZip(string executableName, byte[] contents)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry(executableName);
        using var stream = entry.Open();
        stream.Write(contents);
    }
    return memory.ToArray();
}

var parsed = ToolSelection.Parse("codex, cursor");
Check(parsed == (Tool.Codex | Tool.Cursor), "tool selection parses comma-separated values");
Check(ToolSelection.Parse("all") == Tool.All, "all selects every tool");
var cursor = CursorCommandGenerator.FromSkill("demo", "---\nname: demo\n---\n\n# Workflow\n\nDo work.");
Check(!cursor.Contains("name: demo", StringComparison.Ordinal), "Cursor command excludes skill front matter");
Check(cursor.Contains("Do work.", StringComparison.Ordinal), "Cursor command retains skill body");
var sums = ChecksumParser.Parse(new string('a', 64) + "  orchestrate-osx-arm64.zip\n");
Check(sums["orchestrate-osx-arm64.zip"] == new string('a', 64), "checksum parser reads a valid manifest");
var workflowPolicy = new AssetStore().ReadText("global/shared/workflow-policy.md").Trim();
Check(workflowPolicy.Contains("clean working tree", StringComparison.Ordinal) && workflowPolicy.Contains("attached HEAD", StringComparison.Ordinal) && workflowPolicy.Contains("wait for confirmation", StringComparison.Ordinal) && workflowPolicy.Contains("before editing", StringComparison.Ordinal), "shared policy requires branch verification before edits");
Check(workflowPolicy.Contains("agentic-feature-delivery", StringComparison.Ordinal) && workflowPolicy.Contains("agentic-debugging", StringComparison.Ordinal) && workflowPolicy.Contains("refactor-code", StringComparison.Ordinal) && workflowPolicy.Contains("grill-me", StringComparison.Ordinal) && workflowPolicy.Contains("bootstrap-agent-harness", StringComparison.Ordinal), "shared policy routes all primary and supporting workflows");
Check(workflowPolicy.Contains("return to the primary workflow", StringComparison.Ordinal) && workflowPolicy.Contains("read-only", StringComparison.Ordinal), "shared policy preserves return routes and audit scope");
var flattenedPolicy = string.Join(' ', workflowPolicy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
Check(flattenedPolicy.Contains("localized, low-risk feature needs a targeted check, not a refactor audit or independent reviewer", StringComparison.Ordinal), "shared policy routes localized features directly");
Check(flattenedPolicy.Contains("module-level, cross-cutting, or high-consequence feature work, run a bounded read-only refactor audit and a separate independent correctness review", StringComparison.Ordinal), "shared policy retains both reviews at module-level or higher");
CheckFeatureRouting(new AssetStore().ReadText("skills/agentic-feature-delivery/SKILL.md"), "source feature skill");
try
{
    ChecksumParser.Parse(new string('a', 64) + "  duplicate.zip\n" + new string('b', 64) + "  duplicate.zip\n");
    throw new Exception("FAILED: checksum parser accepted duplicate entries");
}
catch (ArgumentException ex)
{
    Check(ex.Message.Contains("Duplicate SHA-256 manifest entry", StringComparison.Ordinal), "checksum parser rejects duplicate entries");
}

var root = Path.Combine(Path.GetTempPath(), "orchestra-tests-" + Guid.NewGuid().ToString("N"));
var home = Path.Combine(root, "home"); var state = Path.Combine(root, "state"); var project = Path.Combine(root, "project");
try
{
    Directory.CreateDirectory(root);
    var app = new HarnessApp(home, state);
    var help = Capture(app, new[] { "--help" });
    Check(help.ExitCode == 0 && help.Output.Contains("orchestrate", StringComparison.Ordinal), "help uses the orchestrate command name");
    Check(!help.Output.Contains("agent-harness", StringComparison.Ordinal), "help omits the retired command name");
    var heartbeatRoot = OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + root : root;
    var heartbeatDir = Path.Combine(heartbeatRoot, "run");
    var noRun = Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "started", "--reason", "begin" });
    Check(noRun.ExitCode == 2 && !Directory.Exists(heartbeatDir), "heartbeat event refuses an uninitialized run without creating files");
    var init = Capture(app, new[] { "heartbeat", "init", "--dir", heartbeatDir, "--run-id", "test-run", "--total-tasks", "3", "--time-budget-minutes", "10", "--stale-after-seconds", "30", "--token-budget", "100" });
    Check(init.ExitCode == 0, "heartbeat initializes a run");
    var heartbeatFile = Path.Combine(heartbeatDir, "heartbeat.js");
    var originalHeartbeat = File.ReadAllText(heartbeatFile);
    Check(originalHeartbeat.StartsWith("window.ORCHESTRA_HEARTBEAT = {", StringComparison.Ordinal), "heartbeat is browser-loadable JavaScript");
    var initialSummary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(initialSummary.GetProperty("createdAt").ValueKind == JsonValueKind.String && initialSummary.GetProperty("startedAt").ValueKind == JsonValueKind.Null && initialSummary.GetProperty("elapsedSeconds").GetInt64() == 0, "time budget starts on first started event");
    Check(initialSummary.GetProperty("tokensUsed").ValueKind == JsonValueKind.Null && !initialSummary.GetProperty("tokensOverBudget").GetBoolean(), "token usage is unavailable until reported");
    Check(!initialSummary.GetProperty("stale").GetBoolean() && initialSummary.GetProperty("hookState").GetString() == "waiting", "native liveness is unavailable before first hook");
    Check(Capture(app, new[] { "heartbeat", "init", "--dir", heartbeatDir, "--run-id", "other", "--total-tasks", "1", "--time-budget-minutes", "1", "--stale-after-seconds", "1" }).ExitCode == 2 && File.ReadAllText(heartbeatFile) == originalHeartbeat, "heartbeat init refuses to overwrite existing run");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "started", "--reason", "begin" }).ExitCode == 0, "heartbeat records task start");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "finished", "--reason", "done" }).ExitCode == 0, "heartbeat records task finish");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "two", "--status", "blocked", "--reason", "waiting" }).ExitCode == 0, "heartbeat records blocked task");
    var summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("done").GetInt32() == 1 && summary.GetProperty("remaining").GetInt32() == 2 && summary.GetProperty("blockedTasks")[0].GetString() == "two", "heartbeat status computes done, remaining, and blocked queue");
    Check(summary.GetProperty("tokensUsed").ValueKind == JsonValueKind.Null && summary.GetProperty("failureCounts").EnumerateObject().Count() == 0, "task events do not invent token usage");
    var priorInput = Console.In;
    try
    {
        Console.SetIn(new StringReader(JsonSerializer.Serialize(new { hook_event_name = "PostToolUse", session_id = "preflight", tool_response = new string('x', 1_100_000), tool_input = "secret" })));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 0, "heartbeat accepts large native hook payload for preflight probe");
        summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
        Check(summary.GetProperty("lastProbeSessionId").GetString() == "preflight" && summary.GetProperty("lastProbeAt").ValueKind == JsonValueKind.String && summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.Null && summary.GetProperty("hookState").GetString() == "waiting", "preflight hook records only probe fields");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"PostToolUse\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 2, "heartbeat requires hook session ID");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"PostToolUse\",\"session_id\":\"\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 2, "heartbeat rejects empty hook session ID");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"Unknown\",\"session_id\":\"preflight\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 2, "heartbeat rejects unknown hook event");
        Check(Capture(app, new[] { "heartbeat", "bind", "--dir", heartbeatDir, "--session-id", "kickoff" }).ExitCode == 0, "heartbeat binds kickoff session");
        Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "other", "--tokens-used", "9" }).ExitCode == 2, "usage cannot come from another session");
        Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "kickoff", "--tokens-used", "14" }).ExitCode == 0, "native usage sets absolute token count");
        Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "kickoff", "--tokens-used", "14" }).ExitCode == 0, "repeated usage snapshot is idempotent");
        Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "kickoff", "--tokens-used", "7" }).ExitCode == 0, "late lower snapshot is ignored");
        Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "claude-otel", "--session-id", "kickoff", "--tokens-used", "7" }).ExitCode == 2, "usage cannot mix native sources");
        Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "started", "--reason", "x", "--tokens-used", "4" }).ExitCode == 2, "task event does not accept guessed token deltas");
        summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
        Check(summary.GetProperty("boundSessionId").GetString() == "kickoff" && summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.Null && summary.GetProperty("hookState").GetString() == "waiting", "bind resets native liveness to waiting");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"PostToolUse\",\"session_id\":\"other\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 0, "unrelated PostToolUse is accepted and ignored");
        summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
        Check(summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.Null && summary.GetProperty("hookState").GetString() == "waiting", "unrelated hook cannot activate run");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"PostToolUse\",\"session_id\":\"kickoff\",\"tool_input\":\"secret\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 0, "bound PostToolUse activates run");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"Stop\",\"session_id\":\"other\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 0, "unrelated Stop is accepted and ignored");
        summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
        Check(summary.GetProperty("hookState").GetString() == "active" && summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.String, "unrelated Stop cannot stop bound run");
        Console.SetIn(new StringReader("{\"hook_event_name\":\"Stop\",\"session_id\":\"kickoff\",\"transcript_path\":\"secret\"}"));
        Check(Capture(app, new[] { "heartbeat", "hook", "--dir", heartbeatDir }).ExitCode == 0, "bound Stop marks run stopped");
    }
    finally { Console.SetIn(priorInput); }
    var script = File.ReadAllText(heartbeatFile);
    Check(script.Length < 100_000 && !script.Contains("secret", StringComparison.Ordinal), "heartbeat does not store large hook payloads or tool arguments");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("hookState").GetString() == "stopped" && summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.String, "bound Stop hook updates liveness state");
    Check(Capture(app, new[] { "heartbeat", "bind", "--dir", heartbeatDir, "--session-id", "other" }).ExitCode == 2, "bind refuses a different session by default");
    Check(Capture(app, new[] { "heartbeat", "bind", "--dir", heartbeatDir, "--session-id", "resume", "--replace", "--expected-session-id", "other" }).ExitCode == 2, "compare-and-swap bind rejects a changed prior session");
    Check(Capture(app, new[] { "heartbeat", "bind", "--dir", heartbeatDir, "--session-id", "resume", "--replace", "--expected-session-id", "kickoff" }).ExitCode == 0, "explicit compare-and-swap replace binds resumed session");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("boundSessionId").GetString() == "resume" && summary.GetProperty("lastHookAt").ValueKind == JsonValueKind.Null && summary.GetProperty("hookState").GetString() == "waiting" && summary.GetProperty("done").GetInt32() == 1 && summary.GetProperty("tokensUsed").GetInt64() == 14, "rebind resets liveness and preserves task progress and budget");
    Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "resume", "--tokens-used", "10" }).ExitCode == 0, "resumed native session reports its own absolute usage");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("tokensUsed").GetInt64() == 24 && !summary.GetProperty("tokensOverBudget").GetBoolean() && summary.GetProperty("usageSource").GetString() == "cursor-sdk", "usage totals aggregate resumed sessions");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "four", "--status", "failed", "--reason", "first" }).ExitCode == 0, "heartbeat records distinct failure signature");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "four", "--status", "failed", "--reason", "second" }).ExitCode == 0, "heartbeat records second distinct failure signature");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("failureCounts").GetProperty("four").GetInt32() == 2 && !summary.GetProperty("stopRecommended").GetBoolean(), "distinct failure reasons do not recommend stop");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "three", "--status", "failed", "--reason", "bad" }).ExitCode == 0, "heartbeat records failure");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "three", "--status", "failed", "--reason", "bad" }).ExitCode == 0, "heartbeat records repeated failure");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("failureCounts").GetProperty("three").GetInt32() == 2 && summary.GetProperty("stopRecommended").GetBoolean(), "two matching failures on one task recommend stop");
    Check(Capture(app, new[] { "heartbeat", "usage", "--dir", heartbeatDir, "--source", "cursor-sdk", "--session-id", "resume", "--tokens-used", "100" }).ExitCode == 0, "usage collector records budget crossing");
    summary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", heartbeatDir }).Output).RootElement;
    Check(summary.GetProperty("tokensUsed").GetInt64() == 114 && summary.GetProperty("tokensOverBudget").GetBoolean(), "native usage recommends budget stop");
    var heartbeatState = JsonDocument.Parse(File.ReadAllText(heartbeatFile)["window.ORCHESTRA_HEARTBEAT = ".Length..^1]).RootElement;
    Check(heartbeatState.GetProperty("events").GetArrayLength() >= 7, "heartbeat state retains event history");
    var malformed = Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "invalid", "--reason", "bad" });
    Check(malformed.ExitCode == 2, "heartbeat rejects invalid event status");
    Check(Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "one", "--status", "started", "--reason", "x", "--total-tasks", "2" }).ExitCode == 2, "heartbeat refuses task count decrease");
    var malicious = Capture(app, new[] { "heartbeat", "event", "--dir", heartbeatDir, "--task", "safe", "--status", "blocked", "--reason", "</script><script>alert(1)</script>" });
    Check(malicious.ExitCode == 0 && !File.ReadAllText(heartbeatFile).Contains("</script>", StringComparison.Ordinal), "heartbeat JSON safely escapes script-closing text");
    var codexRun = Path.Combine(heartbeatRoot, "codex-run");
    Check(Capture(app, new[] { "heartbeat", "init", "--dir", codexRun, "--run-id", "codex-run", "--total-tasks", "1", "--time-budget-minutes", "10", "--stale-after-seconds", "30" }).ExitCode == 0, "Codex collector fixture initializes");
    Check(Capture(app, new[] { "heartbeat", "bind", "--dir", codexRun, "--session-id", "codex-thread" }).ExitCode == 0, "Codex collector fixture binds thread");
    var oldInput = Console.In;
    try
    {
        Console.SetIn(new StringReader("{\"method\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"another-thread\",\"tokenUsage\":{\"total\":{\"totalTokens\":900}}}}\n" +
            "{\"method\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"codex-thread\",\"tokenUsage\":{\"total\":{\"totalTokens\":37}}}}\n" +
            "{\"method\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"codex-thread\",\"tokenUsage\":{\"total\":{\"totalTokens\":37}}}}\n"));
        Check(Capture(app, new[] { "heartbeat", "collect-codex", "--dir", codexRun, "--session-id", "codex-thread" }).ExitCode == 0, "Codex app-server collector consumes native JSONL");
    }
    finally { Console.SetIn(oldInput); }
    var codexSummary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", codexRun }).Output).RootElement;
    Check(codexSummary.GetProperty("tokensUsed").GetInt64() == 37 && codexSummary.GetProperty("usageSource").GetString() == "codex-app-server", "Codex collector keeps only bound cumulative thread usage");
    var codexExecRun = Path.Combine(heartbeatRoot, "codex-exec-run");
    Check(Capture(app, new[] { "heartbeat", "init", "--dir", codexExecRun, "--run-id", "codex-exec-run", "--total-tasks", "1", "--time-budget-minutes", "10", "--stale-after-seconds", "30" }).ExitCode == 0, "Codex exec collector fixture initializes");
    oldInput = Console.In;
    try
    {
        Console.SetIn(new StringReader("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":20,\"output_tokens\":7}}\n"));
        Check(Capture(app, new[] { "heartbeat", "collect-codex", "--dir", codexExecRun, "--session-id", "exec-thread" }).ExitCode == 2, "Codex exec usage requires a native thread.started identity");
        Console.SetIn(new StringReader("{\"type\":\"thread.started\",\"thread_id\":\"exec-thread\"}\n" +
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":20,\"cached_input_tokens\":5,\"output_tokens\":7}}\n"));
        Check(Capture(app, new[] { "heartbeat", "collect-codex", "--dir", codexExecRun, "--stream-id", "invocation-one" }).ExitCode == 0, "Codex exec collector consumes final native usage");
    }
    finally { Console.SetIn(oldInput); }
    codexSummary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", codexExecRun }).Output).RootElement;
    Check(codexSummary.GetProperty("boundSessionId").GetString() == "exec-thread" && codexSummary.GetProperty("tokensUsed").GetInt64() == 27 && codexSummary.GetProperty("usageSource").GetString() == "codex-exec", "Codex exec collector auto-binds and does not double-count cached input");
    oldInput = Console.In;
    try
    {
        Console.SetIn(new StringReader("{\"type\":\"thread.started\",\"thread_id\":\"exec-thread\"}\n" +
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":20,\"output_tokens\":7}}\n"));
        Check(Capture(app, new[] { "heartbeat", "collect-codex", "--dir", codexExecRun, "--stream-id", "invocation-one" }).ExitCode == 0, "Codex exec stream replay is accepted");
        Console.SetIn(new StringReader("{\"type\":\"thread.started\",\"thread_id\":\"exec-thread\"}\n" +
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":20,\"output_tokens\":7}}\n"));
        Check(Capture(app, new[] { "heartbeat", "collect-codex", "--dir", codexExecRun, "--stream-id", "invocation-two" }).ExitCode == 0, "Codex exec resumed invocation is accepted");
    }
    finally { Console.SetIn(oldInput); }
    codexSummary = JsonDocument.Parse(Capture(app, new[] { "heartbeat", "status", "--dir", codexExecRun }).Output).RootElement;
    Check(codexSummary.GetProperty("tokensUsed").GetInt64() == 54, "Codex exec usage deduplicates replay and counts a resumed invocation");
    var unknown = Capture(app, new[] { "unknown-command" });
    Check(unknown.ExitCode == 2 && unknown.Error.Contains("orchestrate --help", StringComparison.Ordinal), "unknown-command guidance uses orchestrate");
    var doctor = Capture(app, new[] { "doctor" });
    Check(doctor.ExitCode == 0 && doctor.Output.Contains("anonymous HTTPS", StringComparison.Ordinal) && !doctor.Output.Contains("authenticated", StringComparison.Ordinal), "doctor reports credential-free updates");
    var dryRunUpdate = Capture(new HarnessApp(home, state, runtimeIdentifier: "test-rid"), new[] { "update", "--dry-run" });
    Check(dryRunUpdate.ExitCode == 0 && dryRunUpdate.Output.Contains("latest stable public release", StringComparison.Ordinal), "update dry-run describes anonymous stable release download");

    var releaseBinary = "updated orchestra"u8.ToArray();
    var releaseZip = ReleaseZip(OperatingSystem.IsWindows() ? "orchestrate.exe" : "orchestrate", releaseBinary);
    var releaseHash = Convert.ToHexString(SHA256.HashData(releaseZip)).ToLowerInvariant();
    var releaseHandler = new StubHttpHandler(new Dictionary<string, byte[]>
    {
        ["orchestrate-test-rid.zip"] = releaseZip,
        ["SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes($"{releaseHash}  orchestrate-test-rid.zip\n")
    });
    var replacementSelf = Path.Combine(root, "orchestrate-current");
    File.WriteAllText(replacementSelf, "current orchestra");
    byte[]? replacementContents = null; string? replacementTarget = null;
    var updateApp = new HarnessApp(home, state, new HttpClient(releaseHandler), replacementSelf, "test-rid", (candidate, target) =>
    {
        replacementContents = File.ReadAllBytes(candidate);
        replacementTarget = target;
    });
    var update = Capture(updateApp, new[] { "update" });
    Check(update.ExitCode == 0 && replacementContents!.SequenceEqual(releaseBinary) && replacementTarget == replacementSelf, "update verifies and stages the public release executable");
    Check(releaseHandler.RequestedAssets.SequenceEqual(new[] { "orchestrate-test-rid.zip", "SHA256SUMS" }), "update requests only stable public release assets");
    Check(releaseHandler.RequestedUris.All(x => x.StartsWith("https://github.com/jmpompeo/orchestra/releases/latest/download/", StringComparison.OrdinalIgnoreCase)), "update uses stable anonymous release URLs");

    var mismatchHandler = new StubHttpHandler(new Dictionary<string, byte[]>
    {
        ["orchestrate-test-rid.zip"] = releaseZip,
        ["SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes($"{new string('0', 64)}  orchestrate-test-rid.zip\n")
    });
    var replacedMismatch = false;
    var mismatchApp = new HarnessApp(home, state, new HttpClient(mismatchHandler), replacementSelf, "test-rid", (_, _) => replacedMismatch = true);
    var mismatch = Capture(mismatchApp, new[] { "update" });
    Check(mismatch.ExitCode == 2 && mismatch.Error.Contains("checksum did not match", StringComparison.Ordinal) && !replacedMismatch, "checksum mismatch never replaces the executable");

    var absentEntryHandler = new StubHttpHandler(new Dictionary<string, byte[]>
    {
        ["orchestrate-test-rid.zip"] = releaseZip,
        ["SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes($"{releaseHash}  another-platform.zip\n")
    });
    var replacedAbsentEntry = false;
    var absentEntryApp = new HarnessApp(home, state, new HttpClient(absentEntryHandler), replacementSelf, "test-rid", (_, _) => replacedAbsentEntry = true);
    var absentEntry = Capture(absentEntryApp, new[] { "update" });
    Check(absentEntry.ExitCode == 2 && absentEntry.Error.Contains("does not contain orchestrate-test-rid.zip", StringComparison.Ordinal) && !replacedAbsentEntry, "missing checksum entry never replaces the executable");

    var duplicateEntryHandler = new StubHttpHandler(new Dictionary<string, byte[]>
    {
        ["orchestrate-test-rid.zip"] = releaseZip,
        ["SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes($"{releaseHash}  orchestrate-test-rid.zip\n{releaseHash}  orchestrate-test-rid.zip\n")
    });
    var replacedDuplicateEntry = false;
    var duplicateEntryApp = new HarnessApp(home, state, new HttpClient(duplicateEntryHandler), replacementSelf, "test-rid", (_, _) => replacedDuplicateEntry = true);
    var duplicateEntry = Capture(duplicateEntryApp, new[] { "update" });
    Check(duplicateEntry.ExitCode == 2 && duplicateEntry.Error.Contains("Duplicate SHA-256 manifest entry", StringComparison.Ordinal) && !replacedDuplicateEntry, "duplicate checksum entry never replaces the executable");

    var missingHandler = new StubHttpHandler(new Dictionary<string, byte[]>());
    var missingApp = new HarnessApp(home, state, new HttpClient(missingHandler), replacementSelf, "test-rid", (_, _) => throw new Exception("must not replace"));
    var missing = Capture(missingApp, new[] { "update" });
    Check(missing.ExitCode == 2 && missing.Error.Contains("Download the release manually", StringComparison.Ordinal), "download failure provides corrective instructions");
    var previousStateHome = Environment.GetEnvironmentVariable("AGENT_HARNESS_STATE_HOME");
    var compatibleStateHome = Path.Combine(root, "compatible-state");
    try
    {
        Environment.SetEnvironmentVariable("AGENT_HARNESS_STATE_HOME", compatibleStateHome);
        var compatibleStatus = Capture(new HarnessApp(home), new[] { "status" });
        Check(compatibleStatus.Output.Contains(Path.Combine(compatibleStateHome, "state.json"), StringComparison.Ordinal), "legacy state-home override remains supported");
    }
    finally { Environment.SetEnvironmentVariable("AGENT_HARNESS_STATE_HOME", previousStateHome); }
    if (!OperatingSystem.IsWindows())
    {
        var previousXdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var xdgStateHome = Path.Combine(root, "xdg-state");
        try
        {
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", xdgStateHome);
            var compatibleStatus = Capture(new HarnessApp(home), new[] { "status" });
            Check(compatibleStatus.Output.Contains(Path.Combine(xdgStateHome, "agent-harness", "state.json"), StringComparison.Ordinal), "legacy default state identity remains supported");
        }
        finally { Environment.SetEnvironmentVariable("XDG_STATE_HOME", previousXdgStateHome); }
    }
    Check(app.Run(new[] { "install", "--tools", "codex" }) == 0, "Codex install succeeds in isolated home");
    Check(File.Exists(Path.Combine(home, ".codex", "AGENTS.md")), "Codex instructions installed");
    var codexAgents = Directory.GetFiles(Path.Combine(home, ".codex", "agents"), "*.toml");
    Check(codexAgents.Length == 4 && codexAgents.All(path => !File.ReadAllText(path).Contains("@@", StringComparison.Ordinal)), "Codex agent models are rendered");
    Check(File.ReadAllText(Path.Combine(home, ".codex", "agents", "workflow_explorer.toml")).Contains("model = \"gpt-6-luna\"", StringComparison.Ordinal), "Codex explorer uses its configured model");
    CheckRenderedPolicy(File.ReadAllText(Path.Combine(home, ".codex", "AGENTS.md")), workflowPolicy, "installed Codex instructions");
    var renderedCodexInstructions = File.ReadAllBytes(Path.Combine(home, ".codex", "AGENTS.md"));
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "agentic-feature-delivery", "SKILL.md")), "Codex skill installed");
    CheckFeatureRouting(File.ReadAllText(Path.Combine(home, ".agents", "skills", "agentic-feature-delivery", "SKILL.md")), "installed Codex feature skill");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "agentic-debugging", "SKILL.md")), "agentic-debugging skill installed");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "grill-me", "SKILL.md")), "grill-me skill installed");
    Check(File.ReadAllText(Path.Combine(home, ".agents", "skills", "prep", "SKILL.md")).Contains("orchestrate heartbeat init", StringComparison.Ordinal), "Codex prep skill installs with heartbeat setup");
    Check(File.ReadAllText(Path.Combine(home, ".agents", "skills", "prep", "scripts", "codex-app-server-run.mjs")).Contains("thread/tokenUsage/updated", StringComparison.Ordinal), "Codex measured app-server launcher installs with prep");
    Check(File.ReadAllText(Path.Combine(home, ".agents", "skills", "kickoff", "SKILL.md")).Contains("orchestrate heartbeat event", StringComparison.Ordinal), "Codex kickoff skill installs with event writes");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "refactor-code", "SKILL.md")), "refactor-code skill installed");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "refactor-code", "agents", "openai.yaml")), "refactor-code Codex metadata installed");
    Check(app.Run(new[] { "install", "--tools", "codex" }) == 0, "repeat install is a no-op");
    var instructions = Path.Combine(home, ".codex", "AGENTS.md");
    File.WriteAllText(instructions, "personal change");
    Check(app.Run(new[] { "install", "--tools", "codex" }) == 0, "modified file conflict is non-destructive");
    Check(File.ReadAllText(instructions) == "personal change", "conflict preserves user configuration");
    Check(app.Run(new[] { "install", "--tools", "codex", "--backup" }) == 0, "explicit backup replacement succeeds");
    Check(File.ReadAllText(instructions).Contains("Personal engineering workflow", StringComparison.Ordinal), "backup replacement installs managed content");
    Check(Directory.GetFiles(Path.Combine(home, ".codex"), "AGENTS.md.backup.*").Length == 1, "backup is retained");
    var stale = Path.Combine(home, ".agents", "skills", "retired", "SKILL.md"); Directory.CreateDirectory(Path.GetDirectoryName(stale)!); File.WriteAllText(stale, "retired");
    var statePath = Path.Combine(state, "state.json"); var document = JsonSerializer.Deserialize<StateDocument>(File.ReadAllText(statePath))!;
    document.Files[Path.GetFullPath(stale)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stale))).ToLowerInvariant();
    File.WriteAllText(statePath, JsonSerializer.Serialize(document));
    Check(app.Run(new[] { "install", "--tools", "codex" }) == 0 && !File.Exists(stale), "stale unchanged skill is reconciled");
    var before = Directory.Exists(home) ? Directory.GetFiles(home, "*", SearchOption.AllDirectories).Length : 0;
    Check(app.Run(new[] { "install", "--tools", "claude", "--dry-run" }) == 0, "dry run succeeds");
    Check(Directory.GetFiles(home, "*", SearchOption.AllDirectories).Length == before, "dry run does not create Claude files");
    Check(app.Run(new[] { "install", "--tools", "claude" }) == 0, "Claude install succeeds in isolated home");
    var claudeAgents = Directory.GetFiles(Path.Combine(home, ".claude", "agents"), "*.md");
    Check(claudeAgents.Length == 4 && claudeAgents.All(path => !File.ReadAllText(path).Contains("@@", StringComparison.Ordinal)), "Claude agent models are rendered");
    Check(File.ReadAllText(Path.Combine(home, ".claude", "agents", "workflow-explorer.md")).Contains("model: \"haiku\"", StringComparison.Ordinal), "Claude explorer uses its configured model");
    CheckRenderedPolicy(File.ReadAllText(Path.Combine(home, ".claude", "CLAUDE.md")), workflowPolicy, "installed Claude instructions");
    Check(File.Exists(Path.Combine(home, ".claude", "skills", "agentic-debugging", "SKILL.md")), "Claude agentic-debugging skill installed");
    Check(File.ReadAllText(Path.Combine(home, ".claude", "skills", "prep", "SKILL.md")).Contains("dashboard.html", StringComparison.Ordinal), "Claude prep skill installs with dashboard handoff");
    Check(File.ReadAllText(Path.Combine(home, ".claude", "skills", "kickoff", "SKILL.md")).Contains("progress.md", StringComparison.Ordinal), "Claude kickoff skill installs with progress ledger");
    CheckFeatureRouting(File.ReadAllText(Path.Combine(home, ".claude", "skills", "agentic-feature-delivery", "SKILL.md")), "installed Claude feature skill");
    Check(File.Exists(Path.Combine(home, ".claude", "skills", "refactor-code", "SKILL.md")), "Claude refactor-code skill installed");
    Check(!File.Exists(Path.Combine(home, ".claude", "skills", "refactor-code", "agents", "openai.yaml")), "Claude excludes refactor-code Codex metadata");
    var printedCursorRules = Capture(app, new[] { "cursor-rules", "--print" });
    Check(printedCursorRules.ExitCode == 0, "Cursor user rules print succeeds");
    CheckRenderedPolicy(printedCursorRules.Output, workflowPolicy, "printed Cursor user rules");
    Directory.CreateDirectory(project); var previous = Directory.GetCurrentDirectory(); Directory.SetCurrentDirectory(project);
    try
    {
        Check(app.Run(new[] { "init-project", "--tools", "cursor" }) == 0, "project preview succeeds");
        Check(!Directory.Exists(Path.Combine(project, ".cursor")), "project preview does not write");
        Check(app.Run(new[] { "init-project", "--tools", "cursor", "--apply" }) == 0, "Cursor project apply succeeds");
        var cursorProjectRule = Path.Combine(project, ".cursor", "rules", "agentic-feature-workflow.mdc");
        CheckRenderedPolicy(File.ReadAllText(cursorProjectRule), workflowPolicy, "generated Cursor project rule");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")), "Cursor command generated");
        CheckFeatureRouting(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")), "generated Cursor feature command");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")).Contains("shared Git branch", StringComparison.Ordinal), "Cursor feature command refers to the shared Git gate");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")).Contains("reviewable draft or diff", StringComparison.Ordinal), "Cursor feature command retains refactor audit sequencing");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "agentic-debugging.md")), "Cursor agentic-debugging command generated");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-debugging.md")).Contains("compact evidence ledger", StringComparison.Ordinal), "Cursor agentic-debugging command retains workflow body");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "grill-me.md")), "Cursor grill-me command generated");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "prep.md")).Contains("heartbeat.js", StringComparison.Ordinal), "Cursor prep command includes heartbeat dashboard contract");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "kickoff.md")).Contains("orchestrate heartbeat event", StringComparison.Ordinal), "Cursor kickoff command includes task events");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "orchestra", "cursor-sdk-run.mjs")).Contains("cursor-sdk", StringComparison.Ordinal), "Cursor measured-run launcher installs with project commands");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "grill-me.md")).Contains("Return automatically to the originating workflow", StringComparison.Ordinal), "Cursor grill-me command returns to the originating workflow");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "refactor-code.md")), "Cursor refactor-code command generated");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "refactor-code.md")).Contains("explicit writable-file allowlist", StringComparison.Ordinal), "Cursor refactor-code command retains scope gate");
        var cursorFeatureCommand = Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md");
        File.AppendAllText(cursorProjectRule, "\nUser project rule\n");
        File.AppendAllText(cursorFeatureCommand, "\nUser command edit\n");
        Check(app.Run(new[] { "init-project", "--tools", "cursor", "--apply" }) == 0, "repeat Cursor project apply handles existing files");
        Check(File.ReadAllText(cursorProjectRule).EndsWith("\nUser project rule\n", StringComparison.Ordinal), "repeat apply preserves modified Cursor project rule");
        Check(File.ReadAllText(cursorFeatureCommand).EndsWith("\nUser command edit\n", StringComparison.Ordinal), "repeat apply preserves modified Cursor command");
    }
    finally { Directory.SetCurrentDirectory(previous); }
    Check(app.Run(new[] { "uninstall", "--tools", "codex" }) == 0, "uninstall succeeds");
    Check(!File.Exists(Path.Combine(home, ".codex", "AGENTS.md")), "uninstall removes unchanged owned file");
    var legacyHome = Path.Combine(root, "legacy-home"); var legacyState = Path.Combine(root, "legacy-state");
    var legacyInstructions = Path.Combine(legacyHome, ".codex", "AGENTS.md"); Directory.CreateDirectory(Path.GetDirectoryName(legacyInstructions)!);
    File.WriteAllBytes(legacyInstructions, renderedCodexInstructions);
    var legacy = new HarnessApp(legacyHome, legacyState);
    Check(legacy.Run(new[] { "install", "--tools", "codex" }) == 0, "legacy-shaped install is reported without taking ownership");
    Check(legacy.Run(new[] { "uninstall", "--tools", "codex" }) == 0, "legacy-shaped uninstall succeeds");
    Check(File.Exists(legacyInstructions), "matching unowned legacy configuration is preserved");
    Console.WriteLine("All Orchestra tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

sealed class StubHttpHandler(IReadOnlyDictionary<string, byte[]> assets) : HttpMessageHandler
{
    public List<string> RequestedAssets { get; } = new();
    public List<string> RequestedUris { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var asset = Path.GetFileName(request.RequestUri?.AbsolutePath) ?? "";
        RequestedAssets.Add(asset);
        RequestedUris.Add(request.RequestUri?.AbsoluteUri ?? "");
        if (!assets.TryGetValue(asset, out var contents))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(contents) });
    }
}
