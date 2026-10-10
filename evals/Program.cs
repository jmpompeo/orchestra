using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = FindRoot();
var cases = new[]
{
    new EvalCase("debug-range", "debugging", "agentic-debugging",
        "Fix Range.Contains so a range includes its start and excludes its end. The visible tests reproduce a failing end-boundary case."),
    new EvalCase("debug-slug", "debugging", "agentic-debugging",
        "Fix Slug.Normalize so leading and trailing whitespace is removed before making a lower-case slug. The visible tests reproduce the failure."),
    new EvalCase("refactor-discount", "refactoring", "refactor-code",
        "Reduce the duplicated discount calculation in Pricing without changing any observable result. A small private helper is reasonable if it makes the two public methods easier to read."),
    new EvalCase("refactor-format", "refactoring", "refactor-code",
        "Reduce the duplicated currency formatting in MoneyFormatter without changing its output. Keep the two public methods and their behavior."),
    new EvalCase("handoff-tax", "handoff", "refactor-code",
        "Refactor TaxTotal to reduce duplicated tax calculation. The visible tests currently fail because PriorityTotal applies the wrong rate; you are explicitly authorized to fix that defect in Library.cs as part of this task. Establish the failing baseline, hand off to agentic-debugging with the evidence, fix the bug, then return to refactor-code for the structural improvement. Preserve all other behavior."),
};

if (args.Length < 1 || !new[] { "list", "self-test", "run" }.Contains(args[0], StringComparer.Ordinal))
{
    PrintUsage();
    return 2;
}

string? caseName = null;
string? sourceRef = null;
for (var index = 1; index < args.Length; index++)
{
    if (args[index] == "--source-ref" && args[0] == "run" && sourceRef == null && index + 1 < args.Length)
    {
        sourceRef = args[++index];
    }
    else if (!args[index].StartsWith("-", StringComparison.Ordinal) && caseName == null && args[0] != "list")
    {
        caseName = args[index];
    }
    else
    {
        PrintUsage();
        return 2;
    }
}

var selectedCases = caseName == null ? cases : cases.Where(item => item.Name == caseName).ToArray();
if (selectedCases.Length == 0)
{
    Console.Error.WriteLine($"Unknown case: {caseName}. Use 'list' to see available names.");
    return 2;
}

if (args[0] == "list")
{
    foreach (var item in cases) Console.WriteLine($"{item.Name} ({item.Kind})");
    return 0;
}

ContainerRuntime container;
try { container = await RequireContainerAsync(root); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Container grader unavailable: {ex.Message}");
    return 2;
}

var allGood = true;
if (args[0] == "self-test")
{
    foreach (var item in selectedCases)
    {
        var result = await SelfTestAsync(root, item, container);
        Console.WriteLine($"{item.Name}: baseline visible={Word(result.Baseline.Visible)} hidden={Word(result.Baseline.Hidden)}, reference visible={Word(result.Reference.Visible)} hidden={Word(result.Reference.Hidden)} => {(result.Valid ? "PASS" : "FAIL")}");
        if (!result.Valid) Console.Error.WriteLine(result.Details);
        allGood &= result.Valid;
    }
    return allGood ? 0 : 1;
}

InstructionSource instructions;
try { instructions = await LoadInstructionsAsync(root, selectedCases, sourceRef); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Instruction source unavailable: {ex.Message}");
    return 2;
}

var artifactRoot = Path.Combine(root, "evals", "artifacts", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24]);
Directory.CreateDirectory(artifactRoot);
var startedAtUtc = DateTimeOffset.UtcNow;
CommandResult codexVersionResult;
try { codexVersionResult = await RunProcessAsync("codex", ["--version"], root, TimeSpan.FromSeconds(30)); }
catch (System.ComponentModel.Win32Exception ex)
{
    Console.Error.WriteLine($"Codex CLI unavailable: {ex.Message}");
    return 2;
}
var codexVersion = codexVersionResult.ExitCode == 0 ? codexVersionResult.Output.Trim() : "unknown";
var summaries = new List<RunCaseSummary>();
Console.WriteLine($"Local traces: {artifactRoot}");
foreach (var item in selectedCases)
{
    var preflight = await SelfTestAsync(root, item, container);
    if (!preflight.Valid)
    {
        Console.Error.WriteLine($"{item.Name}: fixture preflight failed; skipping model call. {preflight.Details}");
        summaries.Add(new RunCaseSummary(item.Name, item.Kind, FixtureHash(root, item), null, false, null, null, false, 0, false, true, "preflight failed"));
        allGood = false;
        continue;
    }

    using var workspace = new TempDirectory($"orchestra-eval-{item.Name}-");
    var caseRoot = Path.Combine(root, "evals", "cases", item.Name);
    CopyTree(Path.Combine(caseRoot, "seed"), workspace.Path);
    foreach (var staged in instructions.Files.Where(pair => pair.Key == "global/codex/AGENTS.md" || pair.Key == $"skills/{item.Skill}/SKILL.md" || (item.Kind == "handoff" && pair.Key == "skills/agentic-debugging/SKILL.md")))
    {
        var destination = staged.Key == "global/codex/AGENTS.md" ? "AGENTS.md" : $".agents/{staged.Key}";
        var fullPath = Path.Combine(workspace.Path, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, staged.Value);
    }
    var git = await RunProcessAsync("git", ["init", "-q", workspace.Path], root, TimeSpan.FromSeconds(30));
    if (git.ExitCode != 0) throw new InvalidOperationException($"git init failed: {git.Output} {git.Error}");
    git = await RunProcessAsync("git", ["add", "."], workspace.Path, TimeSpan.FromSeconds(30));
    if (git.ExitCode != 0) throw new InvalidOperationException($"git add failed: {git.Output} {git.Error}");
    var before = Snapshot(workspace.Path);

    var baselineEvidence = item.Kind is "debugging" or "handoff" ? "The runner verified that the unchanged visible tests fail on the seed code." : "The runner verified that the unchanged visible and hidden tests pass on the seed code.";
    var prompt = $"{item.Task}\n\nThis is an Orchestra maintainer evaluation. {baselineEvidence} Read and follow .agents/skills/{item.Skill}/SKILL.md and AGENTS.md.{(item.Kind == "handoff" ? " Also read .agents/skills/agentic-debugging/SKILL.md for the authorized bug fix." : "")} Your writable-file allowlist is Library.cs only. You may inspect the visible tests, but do not run code or tests in this workspace; the runner grades your change in an isolated container. Do not modify tests, instructions, or configuration. Finish with a concise summary of the change and the evidence you used.";
    var trace = Path.Combine(artifactRoot, $"{item.Name}.jsonl");
    var stderr = Path.Combine(artifactRoot, $"{item.Name}.stderr.txt");
    Console.WriteLine($"{item.Name}: running Codex (one trial; eight-minute timeout)...");
    CommandResult trial;
    try
    {
        trial = await RunProcessToFilesAsync("codex", ["exec", "--json", "--full-auto", "-C", workspace.Path, prompt], workspace.Path, TimeSpan.FromMinutes(8), trace, stderr);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{item.Name}: could not start Codex: {ex.Message}");
        summaries.Add(new RunCaseSummary(item.Name, item.Kind, FixtureHash(root, item), null, false, null, null, false, 0, false, true, "Codex did not start"));
        allGood = false;
        continue;
    }

    var grade = await GradeAsync(root, item, Path.Combine(workspace.Path, "Library.cs"), container);
    var after = Snapshot(workspace.Path);
    var unauthorized = before.Keys.Union(after.Keys, StringComparer.Ordinal)
        .Where(path => path != "Library.cs" && (!before.TryGetValue(path, out var oldHash) || !after.TryGetValue(path, out var newHash) || oldHash != newHash))
        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    var changed = before.GetValueOrDefault("Library.cs") != after.GetValueOrDefault("Library.cs");
    var candidateLibrary = Path.Combine(workspace.Path, "Library.cs");
    if (File.Exists(candidateLibrary) && new FileInfo(candidateLibrary) is { LinkTarget: null, Length: <= 262_144 })
    {
        var diff = await RunProcessAsync("git", ["diff", "--no-index", "--", Path.Combine(caseRoot, "seed", "Library.cs"), candidateLibrary], workspace.Path, TimeSpan.FromSeconds(30));
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, $"{item.Name}.diff"), diff.Output);
    }
    var passed = trial.ExitCode == 0 && !trial.TimedOut && grade.Visible && grade.Hidden && changed && unauthorized.Length == 0;
    Console.WriteLine($"{item.Name}: Codex exit={trial.ExitCode}{(trial.TimedOut ? " timeout" : "")}; visible={Word(grade.Visible)} hidden={Word(grade.Hidden)} changed={(changed ? "yes" : "no")} unauthorized={unauthorized.Length} => {(passed ? "AUTO PASS" : "FAIL")}{(item.Kind == "refactoring" && passed ? " (manual structure review pending)" : item.Kind == "handoff" && passed ? " (manual structure and handoff review pending)" : "")}");
    if (!grade.Visible || !grade.Hidden) Console.Error.WriteLine(grade.Details);
    if (unauthorized.Length > 0) Console.Error.WriteLine($"{item.Name}: unauthorized changes: {string.Join(", ", unauthorized)}");
    summaries.Add(new RunCaseSummary(item.Name, item.Kind, FixtureHash(root, item), trial.ExitCode, trial.TimedOut, grade.Visible, grade.Hidden, changed, unauthorized.Length, passed, !passed || (item.Kind is "refactoring" or "handoff"), passed ? null : "automatic checks failed"));
    allGood &= passed;
}

var summary = new RunSummary(startedAtUtc, instructions.SourceRef, instructions.SourceRevision, instructions.SourceDirty, instructions.Sha256, container.ImageId, codexVersion, summaries);
await File.WriteAllTextAsync(Path.Combine(artifactRoot, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
return allGood ? 0 : 1;

static string Word(bool value) => value ? "pass" : "fail";

static void PrintUsage() => Console.Error.WriteLine("Usage: dotnet run --project evals/Orchestra.Evals.csproj -- list | self-test [case-name] | run [case-name] [--source-ref GIT_REF]");

static string FindRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global", "codex", "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "evals", "cases"))) return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException("Run from the Orchestra source checkout; evals/cases and global/codex/AGENTS.md were not found.");
}

static void CopyTree(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    foreach (var directory in Directory.EnumerateDirectories(source)) CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
}

static string HashFiles(IEnumerable<KeyValuePair<string, byte[]>> files)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var file in files.OrderBy(file => file.Key, StringComparer.Ordinal))
    {
        hash.AppendData(Encoding.UTF8.GetBytes(file.Key));
        hash.AppendData([0]);
        hash.AppendData(file.Value);
        hash.AppendData([0]);
    }
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

static string FixtureHash(string root, EvalCase item)
{
    var basePath = Path.Combine(root, "evals", "cases", item.Name);
    var files = Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories)
        .Where(file => !Path.GetRelativePath(basePath, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        .Select(file => new KeyValuePair<string, byte[]>(Path.GetRelativePath(basePath, file).Replace('\\', '/'), File.ReadAllBytes(file)));
    return HashFiles(files);
}

static async Task<InstructionSource> LoadInstructionsAsync(string root, EvalCase[] selectedCases, string? sourceRef)
{
    string revision;
    bool dirty;
    if (sourceRef == null)
    {
        var head = await RunProcessAsync("git", ["rev-parse", "HEAD"], root, TimeSpan.FromSeconds(30));
        var status = await RunProcessAsync("git", ["status", "--porcelain"], root, TimeSpan.FromSeconds(30));
        if (head.ExitCode != 0 || status.ExitCode != 0) throw new InvalidOperationException("Cannot identify the current Git checkout.");
        revision = head.Output.Trim();
        dirty = !string.IsNullOrWhiteSpace(status.Output);
    }
    else
    {
        var resolved = await RunProcessAsync("git", ["rev-parse", "--verify", "--end-of-options", sourceRef + "^{commit}"], root, TimeSpan.FromSeconds(30));
        if (resolved.ExitCode != 0) throw new InvalidOperationException($"Git ref '{sourceRef}' does not resolve to a commit.");
        revision = resolved.Output.Trim();
        dirty = false;
    }

    var paths = selectedCases.Select(item => $"skills/{item.Skill}/SKILL.md")
        .Concat(selectedCases.Any(item => item.Kind == "handoff") ? ["skills/agentic-debugging/SKILL.md"] : [])
        .Append("global/codex/AGENTS.md")
        .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal);
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var path in paths)
    {
        if (sourceRef == null)
        {
            files[path] = await File.ReadAllBytesAsync(Path.Combine(root, path));
        }
        else
        {
            var content = await RunProcessAsync("git", ["show", $"{revision}:{path}"], root, TimeSpan.FromSeconds(30));
            if (content.ExitCode != 0) throw new InvalidOperationException($"{path} is missing from Git ref '{sourceRef}'.");
            files[path] = Encoding.UTF8.GetBytes(content.Output);
        }
    }
    return new InstructionSource(sourceRef ?? "working-tree", revision, dirty, HashFiles(files), files);
}

static async Task<ContainerRuntime> RequireContainerAsync(string root)
{
    const string imageTag = "mcr.microsoft.com/dotnet/sdk:10.0";
    var failures = new List<string>();
    foreach (var tool in new[] { "docker", "podman" })
    {
        try
        {
            var inspect = await RunProcessAsync(tool, ["image", "inspect", "--format", "{{.Id}}", imageTag], root, TimeSpan.FromSeconds(20));
            var imageId = inspect.Output.Trim();
            if (inspect.ExitCode != 0 || !imageId.StartsWith("sha256:", StringComparison.Ordinal) || imageId.Length != 71 || !imageId[7..].All(Uri.IsHexDigit))
            {
                failures.Add($"{tool}: preloaded {imageTag} image not found");
                continue;
            }
            var runtime = new ContainerRuntime(tool, imageId);
            using var temp = new TempDirectory("orchestra-grader-check-");
            MakeGraderReadable(temp.Path);
            var version = await RunContainerAsync(runtime, temp.Path, ["dotnet", "--version"], TimeSpan.FromSeconds(60));
            if (version.ExitCode == 0 && !version.TimedOut && version.Output.Trim().StartsWith("10.", StringComparison.Ordinal)) return runtime;
            failures.Add($"{tool}: local image {imageId} could not run .NET 10 with grader isolation flags ({Short(version)})");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            failures.Add($"{tool}: executable not installed");
        }
        catch (Exception ex)
        {
            failures.Add($"{tool}: {ex.Message}");
        }
    }
    throw new InvalidOperationException($"Docker or Podman and a locally preloaded {imageTag} image are required; the runner never pulls images or grades on the host. {string.Join("; ", failures)}");
}

static async Task<CommandResult> RunContainerAsync(ContainerRuntime runtime, string gradingDirectory, string[] command, TimeSpan timeout)
{
    var containerName = "orchestra-eval-grade-" + Guid.NewGuid().ToString("N");
    var arguments = new List<string>
    {
        "run", "--name", containerName, "--pull=never", "--network=none", "--user=65534:65534",
        "--cap-drop=ALL", "--security-opt=no-new-privileges", "--memory=1g",
        "--memory-swap=1g", "--cpus=1", "--pids-limit=128", "--read-only",
        "--tmpfs=/tmp:rw,nosuid,nodev,size=512m", "--volume", $"{gradingDirectory}:/work:ro",
        "--workdir=/work", "--env", "HOME=/tmp", "--env", "DOTNET_CLI_HOME=/tmp",
        "--env", "NUGET_PACKAGES=/tmp/nuget", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
        "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1", runtime.ImageId,
    };
    arguments.AddRange(command);
    try
    {
        return await RunProcessAsync(runtime.Tool, arguments.ToArray(), gradingDirectory, timeout);
    }
    finally
    {
        try
        {
            var removal = await RunProcessAsync(runtime.Tool, ["rm", "--force", "--volumes", containerName], gradingDirectory, TimeSpan.FromSeconds(20));
            if (removal.ExitCode != 0 || removal.TimedOut)
            {
                throw new InvalidOperationException($"Could not confirm cleanup of container {containerName}. Remove it with '{runtime.Tool} rm --force {containerName}' before another run.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not confirm cleanup of container {containerName}. Check '{runtime.Tool} ps -a' and force-remove it if present.", ex);
        }
    }
}

static void MakeGraderReadable(string directory)
{
    if (OperatingSystem.IsWindows()) return;
    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    foreach (var file in Directory.EnumerateFiles(directory))
    {
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
}

static Dictionary<string, string> Snapshot(string root)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    Visit(root);
    return result;

    void Visit(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry.Name is ".git" or "bin" or "obj") continue;
            var relative = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
            if (entry.LinkTarget != null)
            {
                result[relative] = "SYMLINK:" + entry.LinkTarget;
            }
            else if (entry is DirectoryInfo)
            {
                Visit(entry.FullName);
            }
            else
            {
                using var stream = File.OpenRead(entry.FullName);
                result[relative] = Convert.ToHexString(SHA256.HashData(stream));
            }
        }
    }
}

static async Task<SelfTestResult> SelfTestAsync(string root, EvalCase item, ContainerRuntime runtime)
{
    var caseRoot = Path.Combine(root, "evals", "cases", item.Name);
    var baseline = await GradeAsync(root, item, Path.Combine(caseRoot, "seed", "Library.cs"), runtime);
    var reference = await GradeAsync(root, item, Path.Combine(caseRoot, "reference", "Library.cs"), runtime);
    var baselineExpected = item.Kind is "debugging" or "handoff" ? !baseline.Visible : baseline.Visible && baseline.Hidden;
    var valid = baselineExpected && reference.Visible && reference.Hidden;
    return new SelfTestResult(baseline, reference, valid,
        valid ? "" : $"Expected debugging visible tests to fail or refactoring tests to pass at baseline, and reference tests to pass.\nBaseline: {baseline.Details}\nReference: {reference.Details}");
}

static async Task<GradeResult> GradeAsync(string root, EvalCase item, string libraryFile, ContainerRuntime runtime)
{
    if (!File.Exists(libraryFile)) return new GradeResult(false, false, $"Missing {libraryFile}");
    var library = new FileInfo(libraryFile);
    if (library.LinkTarget != null) return new GradeResult(false, false, "Library.cs must be a regular file, not a symlink.");
    if (library.Length > 262_144) return new GradeResult(false, false, "Library.cs exceeds the 256 KiB grading limit.");
    var caseRoot = Path.Combine(root, "evals", "cases", item.Name);
    var visible = await RunTrustedTestsAsync(Path.Combine(caseRoot, "seed", "Program.cs"), libraryFile, runtime);
    var hidden = await RunTrustedTestsAsync(Path.Combine(caseRoot, "hidden", "Program.cs"), libraryFile, runtime);
    return new GradeResult(visible.ExitCode == 0 && !visible.TimedOut, hidden.ExitCode == 0 && !hidden.TimedOut,
        $"Visible: {Short(visible)}\nHidden: {Short(hidden)}");
}

static string Short(CommandResult result)
{
    var message = (result.Output + " " + result.Error).Replace('\r', ' ').Replace('\n', ' ').Trim();
    return $"exit {result.ExitCode}{(result.TimedOut ? " (timeout)" : "")}: {message[..Math.Min(message.Length, 350)]}";
}

static async Task<CommandResult> RunTrustedTestsAsync(string trustedProgram, string libraryFile, ContainerRuntime runtime)
{
    using var temp = new TempDirectory("orchestra-grader-");
    File.Copy(trustedProgram, Path.Combine(temp.Path, "Program.cs"));
    File.Copy(libraryFile, Path.Combine(temp.Path, "Library.cs"));
    await File.WriteAllTextAsync(Path.Combine(temp.Path, "Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
    await File.WriteAllTextAsync(Path.Combine(temp.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
    MakeGraderReadable(temp.Path);
    return await RunContainerAsync(runtime, temp.Path, ["dotnet", "run", "--project", "/work/Tests.csproj", "--configuration", "Release", "--artifacts-path", "/tmp/artifacts"], TimeSpan.FromSeconds(120));
}

static async Task<CommandResult> RunProcessAsync(string file, string[] arguments, string directory, TimeSpan timeout)
{
    using var process = StartProcess(file, arguments, directory);
    var output = ReadLimitedAsync(process.StandardOutput);
    var error = ReadLimitedAsync(process.StandardError);
    var timedOut = await WaitAsync(process, timeout);
    return new CommandResult(process.ExitCode, timedOut, await output, await error);
}

static async Task<string> ReadLimitedAsync(StreamReader reader)
{
    const int maxCharacters = 65_536;
    var result = new StringBuilder();
    var buffer = new char[4_096];
    int count;
    while ((count = await reader.ReadAsync(buffer)) > 0)
    {
        if (result.Length < maxCharacters)
            result.Append(buffer, 0, Math.Min(count, maxCharacters - result.Length));
    }
    if (result.Length == maxCharacters) result.Append("\n[output truncated]");
    return result.ToString();
}

static async Task<CommandResult> RunProcessToFilesAsync(string file, string[] arguments, string directory, TimeSpan timeout, string outputFile, string errorFile)
{
    using var process = StartProcess(file, arguments, directory);
    await using var outputStream = File.Create(outputFile);
    await using var errorStream = File.Create(errorFile);
    var output = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
    var error = process.StandardError.BaseStream.CopyToAsync(errorStream);
    var timedOut = await WaitAsync(process, timeout);
    await Task.WhenAll(output, error);
    return new CommandResult(process.ExitCode, timedOut, "", "");
}

static Process StartProcess(string file, string[] arguments, string directory)
{
    var info = new ProcessStartInfo(file)
    {
        WorkingDirectory = directory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {file}");
}

static async Task<bool> WaitAsync(Process process, TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    try
    {
        await process.WaitForExitAsync(cancellation.Token);
        return false;
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        return true;
    }
}

record EvalCase(string Name, string Kind, string Skill, string Task);
record CommandResult(int ExitCode, bool TimedOut, string Output, string Error);
record GradeResult(bool Visible, bool Hidden, string Details);
record SelfTestResult(GradeResult Baseline, GradeResult Reference, bool Valid, string Details);
record ContainerRuntime(string Tool, string ImageId);
record InstructionSource(string SourceRef, string SourceRevision, bool SourceDirty, string Sha256, Dictionary<string, byte[]> Files);
record RunCaseSummary(string Name, string Kind, string FixtureSha256, int? CodexExitCode, bool TimedOut, bool? VisiblePassed, bool? HiddenPassed, bool Changed, int UnauthorizedChangeCount, bool AutoPassed, bool ManualReviewPending, string? FailureReason);
record RunSummary(DateTimeOffset StartedAtUtc, string SourceRef, string SourceRevision, bool SourceDirty, string InstructionSha256, string GraderImageId, string CodexVersion, List<RunCaseSummary> Cases);

sealed class TempDirectory : IDisposable
{
    public string Path { get; }
    public TempDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
