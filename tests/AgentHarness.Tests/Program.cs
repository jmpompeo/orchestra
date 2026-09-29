using AgentHarness;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
}

static string Flatten(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

static void CheckRenderedPolicy(string output, string policy, string destination)
{
    Check(output.Contains(policy, StringComparison.Ordinal), $"{destination} contains the complete shared workflow policy");
    Check(!output.Contains("{{WORKFLOW_POLICY}}", StringComparison.Ordinal), $"{destination} has no unresolved workflow marker");
}

static void CheckFeatureRouting(string output, string destination)
{
    var text = Flatten(output);
    Check(text.Contains("localized, low-risk feature work directly without subagents", StringComparison.Ordinal)
        && text.Contains("Model reviews are opt-in at every risk tier", StringComparison.Ordinal),
        $"{destination} routes localized low-risk features directly");
    Check(text.Contains("Only when the user explicitly requests a refactor audit for this task or a standing project rule opts in", StringComparison.Ordinal)
        && text.Contains("reviewable draft or diff", StringComparison.Ordinal),
        $"{destination} requires opt-in before a refactor audit");
    Check(text.Contains("Only when the user explicitly requests an independent correctness review for this task or a standing project rule opts in", StringComparison.Ordinal)
        && text.Contains("A refactor audit does not opt in to a correctness review, or vice versa", StringComparison.Ordinal),
        $"{destination} requires separate opt-in for a correctness review");
    Check(!text.Contains("For module-level, cross-cutting, or high-consequence feature work, obtain", StringComparison.Ordinal),
        $"{destination} has no mandatory correctness review by risk tier");
}

static void CheckDebugReviewRouting(string output, string destination)
{
    var text = Flatten(output);
    Check(text.Contains("correctness review only when the user explicitly requests it for this task or a standing project rule opts in", StringComparison.Ordinal),
        $"{destination} requires opt-in for a correctness review");
    Check(!text.Contains("For non-trivial fixes, obtain an independent read-only review", StringComparison.Ordinal),
        $"{destination} has no mandatory correctness review for non-trivial fixes");
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
var flattenedPolicy = Flatten(workflowPolicy);
Check(flattenedPolicy.Contains("Run a read-only refactor audit or an independent model correctness review only when the user explicitly requests it for this task or a standing project rule opts in", StringComparison.Ordinal), "shared policy makes both model reviews opt-in");
Check(flattenedPolicy.Contains("Keep deterministic checks, risk-based acceptance criteria, and the parent's own final diff inspection regardless of review opt-in", StringComparison.Ordinal), "shared policy preserves checks and parent inspection");
Check(flattenedPolicy.Contains("When context grows or work changes phases", StringComparison.Ordinal)
    && flattenedPolicy.Contains("Do not impose a fixed tool-turn ceiling", StringComparison.Ordinal)
    && flattenedPolicy.Contains("handoff saves total tokens or latency", StringComparison.Ordinal), "shared policy covers efficient context, guardrails, and delegation");
CheckFeatureRouting(new AssetStore().ReadText("skills/agentic-feature-delivery/SKILL.md"), "source feature skill");
CheckDebugReviewRouting(new AssetStore().ReadText("skills/agentic-debugging/SKILL.md"), "source debugging skill");
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
    Check(Flatten(File.ReadAllText(Path.Combine(home, ".codex", "AGENTS.md"))).Contains("Run an independent read-only reviewer or refactor audit only when opted in", StringComparison.Ordinal), "installed Codex instructions require review opt-in");
    var renderedCodexInstructions = File.ReadAllBytes(Path.Combine(home, ".codex", "AGENTS.md"));
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "agentic-feature-delivery", "SKILL.md")), "Codex skill installed");
    CheckFeatureRouting(File.ReadAllText(Path.Combine(home, ".agents", "skills", "agentic-feature-delivery", "SKILL.md")), "installed Codex feature skill");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "agentic-debugging", "SKILL.md")), "agentic-debugging skill installed");
    CheckDebugReviewRouting(File.ReadAllText(Path.Combine(home, ".agents", "skills", "agentic-debugging", "SKILL.md")), "installed Codex debugging skill");
    Check(File.Exists(Path.Combine(home, ".agents", "skills", "grill-me", "SKILL.md")), "grill-me skill installed");
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
    Check(Flatten(File.ReadAllText(Path.Combine(home, ".claude", "CLAUDE.md"))).Contains("Run an independent read-only reviewer or refactor audit only when opted in", StringComparison.Ordinal), "installed Claude instructions require review opt-in");
    Check(File.Exists(Path.Combine(home, ".claude", "skills", "agentic-debugging", "SKILL.md")), "Claude agentic-debugging skill installed");
    CheckDebugReviewRouting(File.ReadAllText(Path.Combine(home, ".claude", "skills", "agentic-debugging", "SKILL.md")), "installed Claude debugging skill");
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
        Check(Flatten(File.ReadAllText(cursorProjectRule)).Contains("Run model reviews only when opted in", StringComparison.Ordinal), "generated Cursor project rule requires review opt-in");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")), "Cursor command generated");
        CheckFeatureRouting(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")), "generated Cursor feature command");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")).Contains("shared Git branch", StringComparison.Ordinal), "Cursor feature command refers to the shared Git gate");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-feature-delivery.md")).Contains("reviewable draft or diff", StringComparison.Ordinal), "Cursor feature command retains refactor audit sequencing");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "agentic-debugging.md")), "Cursor agentic-debugging command generated");
        Check(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-debugging.md")).Contains("compact evidence ledger", StringComparison.Ordinal), "Cursor agentic-debugging command retains workflow body");
        CheckDebugReviewRouting(File.ReadAllText(Path.Combine(project, ".cursor", "commands", "agentic-debugging.md")), "generated Cursor debugging command");
        Check(File.Exists(Path.Combine(project, ".cursor", "commands", "grill-me.md")), "Cursor grill-me command generated");
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
