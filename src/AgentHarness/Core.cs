using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentHarness;

[Flags]
public enum Tool { None = 0, Codex = 1, Claude = 2, Cursor = 4, All = Codex | Claude | Cursor }

public static class ToolSelection
{
    public static Tool Parse(string value)
    {
        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)) return Tool.All;
        Tool result = Tool.None;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= item.ToLowerInvariant() switch
            {
                "codex" => Tool.Codex,
                "claude" => Tool.Claude,
                "cursor" => Tool.Cursor,
                _ => throw new ArgumentException($"Unknown tool '{item}'. Use codex, claude, cursor, or all.")
            };
        }
        if (result == Tool.None) throw new ArgumentException("At least one tool is required.");
        return result;
    }

    public static Tool Prompt()
    {
        if (Console.IsInputRedirected) throw new ArgumentException("Automated use requires --tools codex,claude,cursor (or all).");
        Console.Write("Install for [c]odex, [l]aude, curs[o]r, or [a]ll (comma-separated): ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
        var expanded = string.Join(',', answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x switch
        {
            "c" or "codex" => "codex", "l" or "claude" => "claude", "o" or "cursor" => "cursor", "a" or "all" => "all", _ => x
        }));
        return Parse(expanded);
    }
}

public sealed class AssetStore
{
    private const string Prefix = "AgentHarness.Assets/";
    private readonly Assembly _assembly;
    private readonly IReadOnlyDictionary<string, string> _resourceNames;
    public AssetStore()
    {
        _assembly = typeof(AssetStore).Assembly;
        _resourceNames = _assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Path: name.Replace('\\', '/')))
            .Where(resource => resource.Path.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(resource => resource.Path[Prefix.Length..], resource => resource.Name, StringComparer.Ordinal);
    }
    public IReadOnlyList<string> Paths => _resourceNames.Keys.Order(StringComparer.Ordinal).ToArray();

    public byte[] ReadBytes(string path)
    {
        if (!_resourceNames.TryGetValue(path.Replace('\\', '/'), out var resourceName))
            throw new InvalidOperationException($"Embedded asset not found: {path}");
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded asset not found: {path}");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    public string ReadText(string path) => Encoding.UTF8.GetString(ReadBytes(path));
    public IReadOnlyList<string> SkillNames => Paths.Where(x => x.StartsWith("skills/", StringComparison.Ordinal) && x.EndsWith("/SKILL.md", StringComparison.Ordinal))
        .Select(x => x.Split('/')[1]).Distinct(StringComparer.Ordinal).Order().ToArray();
}

public static class CursorCommandGenerator
{
    public static string FromSkill(string name, string skillMarkdown)
    {
        var body = skillMarkdown.Replace("\r\n", "\n");
        if (body.StartsWith("---\n", StringComparison.Ordinal))
        {
            var close = body.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (close >= 0) body = body[(close + 5)..];
        }
        return $"---\ndescription: {name} workflow\n---\n\n# {name}\n\n{body.Trim()}\n";
    }
}

public sealed record StateDocument(int Version, Dictionary<string, string> Files)
{
    public static StateDocument Empty() => new(1, new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed class HarnessApp
{
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly AssetStore _assets = new();
    private readonly string _home;
    private readonly string _statePath;
    private readonly HttpClient _httpClient;
    private readonly string? _processPath;
    private readonly string? _runtimeIdentifier;
    private readonly Action<string, string> _replaceSelf;
    private const string Repository = "jmpompeo/orchestra";
    private const string LatestReleaseBaseUrl = $"https://github.com/{Repository}/releases/latest/download/";

    public HarnessApp(
        string? home = null,
        string? stateDirectory = null,
        HttpClient? httpClient = null,
        string? processPath = null,
        string? runtimeIdentifier = null,
        Action<string, string>? replaceSelf = null)
    {
        _home = home ?? Environment.GetEnvironmentVariable("AGENT_HARNESS_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var stateRoot = stateDirectory ?? Environment.GetEnvironmentVariable("AGENT_HARNESS_STATE_HOME") ??
            (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agent-harness")
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? Path.Combine(_home, ".local", "state"), "agent-harness"));
        _statePath = Path.Combine(stateRoot, "state.json");
        _httpClient = httpClient ?? DefaultHttpClient;
        _processPath = processPath;
        _runtimeIdentifier = runtimeIdentifier;
        _replaceSelf = replaceSelf ?? ReplaceSelf;
    }

    public int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") return Help();
            var command = args[0].ToLowerInvariant();
            if (command == "heartbeat") return Heartbeat.Run(args[1..]);
            var options = CliOptions.Parse(args[1..]);
            return command switch
            {
                "install" => Install(RequireTools(options, true), options),
                "status" => Status(),
                "doctor" => Doctor(),
                "uninstall" => Uninstall(RequireTools(options, false), options),
                "init-project" => InitProject(RequireTools(options, true), options),
                "cursor-rules" => CursorRules(options),
                "update" => Update(options),
                _ => throw new ArgumentException($"Unknown command '{command}'. Run orchestrate --help.")
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}"); return 2;
        }
    }

    private static int Help()
    {
        Console.WriteLine("orchestrate — Orchestra's portable AI engineering workflow CLI\n\n" +
            "Commands:\n  install [--tools codex,claude,cursor|all] [--dry-run] [--backup]\n  status\n  doctor\n  update\n  uninstall --tools codex,claude [--dry-run]\n  init-project [--tools codex,claude,cursor|all] [--apply]\n  cursor-rules --print\n  heartbeat init|event|usage|collect-codex|collect-claude|bind|hook|status --dir PATH [options]\n\n" +
            "Without --tools, install and init-project ask interactively. Use --tools in scripts or CI.");
        return 0;
    }

    private static Tool RequireTools(CliOptions options, bool interactive)
    {
        if (options.Tools is not null) return ToolSelection.Parse(options.Tools);
        if (interactive) return ToolSelection.Prompt();
        throw new ArgumentException("uninstall requires --tools codex,claude.");
    }

    public int Install(Tool tools, CliOptions options)
    {
        var plan = GlobalPlan(tools);
        var state = LoadState();
        var changes = Apply(plan, state, options.DryRun, options.Backup, onlyMissing: false, operation: "install", trustedRoot: _home);
        ReconcileStaleSkills(tools, plan, state, options.DryRun, uninstall: false);
        if (!options.DryRun) SaveState(state);
        if (tools.HasFlag(Tool.Cursor)) Console.WriteLine("Cursor has no portable global skills directory. Run 'orchestrate cursor-rules --print' or 'orchestrate init-project --tools cursor --apply'.");
        Console.WriteLine($"Install complete: {changes} file change(s){(options.DryRun ? " planned" : "")}. ");
        return 0;
    }

    public int Uninstall(Tool tools, CliOptions options)
    {
        if (tools.HasFlag(Tool.Cursor)) throw new ArgumentException("Cursor has no global installation to uninstall. Project files are never removed automatically.");
        var state = LoadState();
        var expected = GlobalPlan(tools).Select(x => x.Destination).ToHashSet(PathComparer());
        var roots = SkillRoots(tools);
        var candidates = state.Files.Keys.Where(path => expected.Contains(path) || roots.Any(root => IsUnder(path, root))).ToArray();
        var removed = 0;
        foreach (var path in candidates)
        {
            var recorded = state.Files[path];
            if (!TryExistingFile(path, out var issue)) { state.Files.Remove(path); continue; }
            if (issue is not null) { Console.WriteLine($"PRESERVE {path}: {issue}"); state.Files.Remove(path); continue; }
            if (HashFile(path) != recorded) { Console.WriteLine($"PRESERVE {path}: changed since Orchestra installed it."); state.Files.Remove(path); continue; }
            Console.WriteLine($"{(options.DryRun ? "REMOVE" : "REMOVE")} {path}");
            if (!options.DryRun) File.Delete(path);
            state.Files.Remove(path); removed++;
        }
        foreach (var file in GlobalPlan(tools).Where(x => x.Mergeable)) RemoveBlock(file.Destination, options.DryRun);
        if (!options.DryRun) SaveState(state);
        Console.WriteLine($"Uninstall complete: {removed} unchanged CLI-owned file(s) {(options.DryRun ? "would be " : "")}removed. Project files were not touched.");
        return 0;
    }

    public int InitProject(Tool tools, CliOptions options)
    {
        var plan = ProjectPlan(tools, Directory.GetCurrentDirectory());
        if (!options.Apply)
        {
            Console.WriteLine("Preview only. Re-run with --apply to create missing files.");
            foreach (var file in plan) Console.WriteLine(File.Exists(file.Destination) ? $"CONFLICT {file.Destination} (manual merge required)" : $"CREATE {file.Destination}");
            return 0;
        }
        var changed = Apply(plan, StateDocument.Empty(), dryRun: false, backup: false, onlyMissing: true, operation: "project initialization", trustedRoot: Directory.GetCurrentDirectory());
        Console.WriteLine($"Project initialization complete: {changed} file(s) created.");
        return 0;
    }

    private int CursorRules(CliOptions options)
    {
        if (!options.Print) throw new ArgumentException("cursor-rules requires --print.");
        var text = RenderWorkflowPolicy(_assets.ReadText("global/cursor/USER_RULES.md"));
        var start = text.IndexOf("```", StringComparison.Ordinal);
        var end = start >= 0 ? text.IndexOf("```", start + 3, StringComparison.Ordinal) : -1;
        Console.WriteLine(start >= 0 && end > start ? text[(text.IndexOf('\n', start) + 1)..end].Trim() : text.Trim());
        return 0;
    }

    private int Status()
    {
        var state = LoadState();
        Console.WriteLine($"State file: {_statePath}");
        foreach (var tool in new[] { Tool.Codex, Tool.Claude })
        {
            var count = state.Files.Keys.Count(x => IsUnder(x, ToolRoot(tool)));
            Console.WriteLine($"{tool}: {count} CLI-owned file(s) tracked at {ToolRoot(tool)}");
        }
        Console.WriteLine("Cursor: project-local only; run init-project in each project.");
        return 0;
    }

    private int Doctor()
    {
        Console.WriteLine($"Home: {_home}");
        Console.WriteLine($"State: {_statePath}");
        Console.WriteLine($"Bundled skills: {string.Join(", ", _assets.SkillNames)}");
        Console.WriteLine("Release updates: anonymous HTTPS; GitHub CLI authentication is not required.");
        return 0;
    }

    private int Update(CliOptions options)
    {
        var rid = _runtimeIdentifier ?? CurrentRid();
        if (options.DryRun) { Console.WriteLine($"DRY-RUN: would anonymously download the latest stable public release for {rid}, verify SHA256SUMS, and replace this executable."); return 0; }
        var self = _processPath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the running executable path.");
        var zipName = $"orchestrate-{rid}.zip";
        var temp = Path.Combine(Path.GetTempPath(), "orchestrate-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var zip = Path.Combine(temp, zipName);
            var manifest = Path.Combine(temp, "SHA256SUMS");
            DownloadReleaseAsset(zipName, zip);
            DownloadReleaseAsset("SHA256SUMS", manifest);
            var checksums = ChecksumParser.Parse(File.ReadAllText(manifest));
            if (!checksums.TryGetValue(zipName, out var expected)) throw new InvalidOperationException($"SHA256SUMS does not contain {zipName}.");
            if (!string.Equals(HashFile(zip), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Downloaded release checksum did not match SHA256SUMS; executable was not replaced.");
            ZipFile.ExtractToDirectory(zip, temp, overwriteFiles: true);
            var candidate = Path.Combine(temp, OperatingSystem.IsWindows() ? "orchestrate.exe" : "orchestrate");
            if (!File.Exists(candidate)) throw new InvalidOperationException("Release archive did not contain the expected executable.");
            _replaceSelf(candidate, self);
            Console.WriteLine("Updated the CLI binary. Run 'orchestrate install --tools ... --dry-run' to preview configuration changes; update never changes configuration automatically.");
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
        return 0;
    }

    private void DownloadReleaseAsset(string assetName, string destination)
    {
        var uri = new Uri(LatestReleaseBaseUrl + Uri.EscapeDataString(assetName));
        try
        {
            using var response = _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Could not download {assetName} from {uri} (HTTP {(int)response.StatusCode}). Download the release manually from https://github.com/{Repository}/releases/latest and retry.");
            using var source = response.Content.ReadAsStream();
            using var output = File.Create(destination);
            source.CopyTo(output);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new InvalidOperationException($"Could not download {assetName} from {uri}: {ex.Message} Download the release manually from https://github.com/{Repository}/releases/latest and retry.");
        }
    }

    private void ReplaceSelf(string candidate, string self)
    {
        if (OperatingSystem.IsWindows())
        {
            var staged = self + ".new.exe"; File.Copy(candidate, staged, true);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 2 > nul & move /y \"{staged}\" \"{self}\" > nul") { CreateNoWindow = true, UseShellExecute = false });
            return;
        }
        var stagedUnix = self + ".new"; File.Copy(candidate, stagedUnix, true); File.Move(stagedUnix, self, true);
    }

    private IReadOnlyList<PlannedFile> GlobalPlan(Tool tools)
    {
        var list = new List<PlannedFile>();
        if (tools.HasFlag(Tool.Codex))
        {
            AddWorkflowAsset(list, "global/codex/AGENTS.md", Path.Combine(_home, ".codex", "AGENTS.md"), mergeable: true);
            AddTemplates(list, "global/codex/agents/", Path.Combine(_home, ".codex", "agents"), ".toml.tmpl");
            AddSkills(list, Path.Combine(_home, ".agents", "skills"), claude: false);
        }
        if (tools.HasFlag(Tool.Claude))
        {
            AddWorkflowAsset(list, "global/claude/CLAUDE.md", Path.Combine(_home, ".claude", "CLAUDE.md"), mergeable: true);
            AddTemplates(list, "global/claude/agents/", Path.Combine(_home, ".claude", "agents"), ".md.tmpl");
            AddSkills(list, Path.Combine(_home, ".claude", "skills"), claude: true);
        }
        return list;
    }

    private IReadOnlyList<PlannedFile> ProjectPlan(Tool tools, string project)
    {
        var list = new List<PlannedFile>();
        void Add(string source, string destination) { if (!list.Any(x => PathComparer().Equals(x.Destination, destination))) AddAsset(list, source, destination); }
        Add("project-template/docs/agent-context.md", Path.Combine(project, "docs", "agent-context.md"));
        Add("project-template/docs/harness-evolution.md", Path.Combine(project, "docs", "harness-evolution.md"));
        Add("project-template/docs/approved-fixtures/README.md", Path.Combine(project, "docs", "approved-fixtures", "README.md"));
        if (tools.HasFlag(Tool.Codex)) Add("project-template/AGENTS.md", Path.Combine(project, "AGENTS.md"));
        if (tools.HasFlag(Tool.Claude)) Add("project-template/CLAUDE.md", Path.Combine(project, "CLAUDE.md"));
        if (tools.HasFlag(Tool.Cursor))
        {
            AddWorkflowAsset(list, "project-template/.cursor/rules/agentic-feature-workflow.mdc", Path.Combine(project, ".cursor", "rules", "agentic-feature-workflow.mdc"));
            foreach (var skill in _assets.SkillNames)
            {
                var output = CursorCommandGenerator.FromSkill(skill, _assets.ReadText($"skills/{skill}/SKILL.md"));
                list.Add(new PlannedFile($"generated Cursor command for {skill}", Path.Combine(project, ".cursor", "commands", skill + ".md"), Encoding.UTF8.GetBytes(output)));
            }
            Add("skills/prep/scripts/cursor-sdk-run.mjs", Path.Combine(project, ".cursor", "orchestra", "cursor-sdk-run.mjs"));
        }
        return list;
    }

    private void AddSkills(List<PlannedFile> list, string root, bool claude)
    {
        foreach (var path in _assets.Paths.Where(x => x.StartsWith("skills/", StringComparison.Ordinal)))
        {
            var relative = path["skills/".Length..];
            if (claude && relative.Contains("/agents/", StringComparison.Ordinal)) continue;
            AddAsset(list, path, Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
    private void AddTemplates(List<PlannedFile> list, string prefix, string root, string suffix)
    {
        foreach (var path in _assets.Paths.Where(x => x.StartsWith(prefix, StringComparison.Ordinal) && x.EndsWith(suffix, StringComparison.Ordinal)))
        {
            var output = Path.GetFileName(path[..^suffix.Length]) + suffix.Replace(".tmpl", "");
            var rendered = RenderModels(_assets.ReadText(path));
            list.Add(new PlannedFile(path, Path.Combine(root, output), Encoding.UTF8.GetBytes(rendered)));
        }
    }
    private void AddAsset(List<PlannedFile> list, string source, string destination) => list.Add(new PlannedFile(source, destination, _assets.ReadBytes(source)));
    private void AddWorkflowAsset(List<PlannedFile> list, string source, string destination, bool mergeable = false) =>
        list.Add(new PlannedFile(source, destination, Encoding.UTF8.GetBytes(RenderWorkflowPolicy(_assets.ReadText(source))), mergeable));
    private string RenderWorkflowPolicy(string text)
    {
        const string marker = "{{WORKFLOW_POLICY}}";
        if (!text.Contains(marker, StringComparison.Ordinal)) throw new InvalidOperationException("Workflow policy marker missing from target asset.");
        return text.Replace(marker, _assets.ReadText("global/shared/workflow-policy.md").Trim(), StringComparison.Ordinal);
    }
    private string RenderModels(string text)
    {
        foreach (var line in _assets.ReadText("models.conf").Split('\n'))
        {
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && !pair[0].StartsWith('#')) text = text.Replace("@@" + pair[0] + "@@", pair[1], StringComparison.Ordinal);
        }
        return text;
    }

    private int Apply(IReadOnlyList<PlannedFile> plan, StateDocument state, bool dryRun, bool backup, bool onlyMissing, string operation, string trustedRoot)
    {
        var changed = 0;
        foreach (var file in plan)
        {
            var destination = Path.GetFullPath(file.Destination); var wanted = Hash(file.Content);
            if (LinkedParent(destination, trustedRoot)) { Console.WriteLine($"CONFLICT {destination}: a parent directory is a symbolic link or reparse point"); continue; }
            if (TryExistingFile(destination, out var unsafeIssue))
            {
                if (unsafeIssue is not null) { Console.WriteLine($"CONFLICT {destination}: {unsafeIssue}"); continue; }
                var actual = HashFile(destination);
                if (actual == wanted)
                {
                    Console.WriteLine($"UNCHANGED {destination}");
                    // Matching content proves no ownership. In particular, a
                    // legacy-installer result stays unowned until the user
                    // explicitly backs it up and replaces it with this CLI.
                    if (!onlyMissing && state.Files.ContainsKey(destination)) state.Files[destination] = wanted;
                    continue;
                }
                if (onlyMissing) { Console.WriteLine($"CONFLICT {destination}: exists; merge manually, then keep your version."); continue; }
                if (state.Files.TryGetValue(destination, out var owned) && actual == owned)
                {
                    Console.WriteLine($"UPDATE {destination}"); if (!dryRun) Write(destination, file.Content); state.Files[destination] = wanted; changed++; continue;
                }
                if (file.Mergeable && !backup && !state.Files.ContainsKey(destination)) { changed += MergeBlock(destination, Encoding.UTF8.GetString(file.Content), dryRun); continue; }
                if (!backup) { Console.WriteLine($"CONFLICT {destination}: existing file is not unchanged CLI-owned. Re-run with --backup or merge manually."); continue; }
                var backupPath = NextBackup(destination);
                Console.WriteLine($"BACKUP {destination} -> {backupPath}\nREPLACE {destination}");
                if (!dryRun) { File.Move(destination, backupPath); Write(destination, file.Content); }
                state.Files[destination] = wanted; changed++; continue;
            }
            if (onlyMissing || !onlyMissing) { Console.WriteLine($"CREATE {destination}"); if (!dryRun) Write(destination, file.Content); if (!onlyMissing) state.Files[destination] = wanted; changed++; }
        }
        return changed;
    }

    private static int MergeBlock(string destination, string content, bool dryRun)
    {
        var text = File.ReadAllText(destination);
        switch (ManagedBlock.Locate(text, out var start, out var length))
        {
            case BlockPresence.Malformed:
                Console.WriteLine($"CONFLICT {destination}: unmatched Orchestra block markers; fix or remove them, then re-run.");
                return 0;
            case BlockPresence.Present:
                var updated = ManagedBlock.Replace(text, start, length, content);
                if (updated == text) { Console.WriteLine($"UNCHANGED {destination} (Orchestra block)"); return 0; }
                Console.WriteLine($"UPDATE {destination} (Orchestra block only)");
                if (!dryRun) Write(destination, Encoding.UTF8.GetBytes(updated));
                return 1;
            default:
                Console.WriteLine($"APPEND {destination} (Orchestra block; your content is kept)");
                if (!dryRun) Write(destination, Encoding.UTF8.GetBytes(ManagedBlock.Append(text, content)));
                return 1;
        }
    }

    private void RemoveBlock(string destination, bool dryRun)
    {
        if (LinkedParent(destination, _home) || !TryExistingFile(destination, out var issue) || issue is not null) return;
        var text = File.ReadAllText(destination);
        if (ManagedBlock.Locate(text, out var start, out var length) != BlockPresence.Present) return;
        var remaining = ManagedBlock.Remove(text, start, length);
        if (remaining.Length == 0) { Console.WriteLine($"REMOVE {destination} (only held the Orchestra block)"); if (!dryRun) File.Delete(destination); return; }
        Console.WriteLine($"REMOVE Orchestra block from {destination}");
        if (!dryRun) Write(destination, Encoding.UTF8.GetBytes(remaining));
    }

    private void ReconcileStaleSkills(Tool tools, IReadOnlyList<PlannedFile> plan, StateDocument state, bool dryRun, bool uninstall)
    {
        var expected = plan.Select(x => Path.GetFullPath(x.Destination)).ToHashSet(PathComparer());
        foreach (var root in SkillRoots(tools))
        foreach (var path in state.Files.Keys.Where(x => IsUnder(x, root) && !expected.Contains(x)).ToArray())
        {
            if (TryExistingFile(path, out var issue) && issue is null && HashFile(path) == state.Files[path])
            {
                Console.WriteLine($"{(dryRun ? "REMOVE" : "REMOVE")} stale CLI-owned skill file {path}"); if (!dryRun) File.Delete(path);
            }
            else Console.WriteLine($"PRESERVE stale skill file {path}: changed, missing, or unsafe.");
            state.Files.Remove(path);
        }
    }

    private StateDocument LoadState()
    {
        if (!File.Exists(_statePath)) return StateDocument.Empty();
        try { return JsonSerializer.Deserialize<StateDocument>(File.ReadAllText(_statePath)) ?? StateDocument.Empty(); }
        catch (JsonException) { throw new InvalidOperationException($"State file is invalid JSON: {_statePath}. Move it aside after reviewing it; configuration was not changed."); }
    }
    private void SaveState(StateDocument state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static bool TryExistingFile(string path, out string? issue)
    {
        issue = null; var entry = GetEntry(path); if (entry is null) return false;
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) issue = "refuses to follow symbolic links or reparse points";
        else if (entry is DirectoryInfo) issue = "destination is a directory";
        return true;
    }
    private static FileSystemInfo? GetEntry(string path)
    {
        var parent = Path.GetDirectoryName(path); if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return null;
        return new DirectoryInfo(parent).EnumerateFileSystemInfos().FirstOrDefault(x => PathComparer().Equals(x.Name, Path.GetFileName(path)));
    }
    private static bool LinkedParent(string path, string trustedRoot)
    {
        var root = Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (var current = new DirectoryInfo(Path.GetDirectoryName(path) ?? path); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (PathComparer().Equals(Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar), root)) break;
        }
        return false;
    }
    private static void Write(string path, byte[] contents) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, contents); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string NextBackup(string path) { var basePath = path + ".backup." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"); var n = 0; while (File.Exists(n == 0 ? basePath : basePath + "." + n)) n++; return n == 0 ? basePath : basePath + "." + n; }
    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool IsUnder(string path, string root) => path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private string ToolRoot(Tool tool) => tool == Tool.Codex ? Path.Combine(_home, ".codex") : Path.Combine(_home, ".claude");
    private IReadOnlyList<string> SkillRoots(Tool tools) => new[] { tools.HasFlag(Tool.Codex) ? Path.Combine(_home, ".agents", "skills") : null, tools.HasFlag(Tool.Claude) ? Path.Combine(_home, ".claude", "skills") : null }.Where(x => x is not null).Cast<string>().ToArray();
    private static string CurrentRid()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64) return "osx-arm64";
        if (OperatingSystem.IsMacOS() && architecture == Architecture.X64) return "osx-x64";
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64) return "linux-x64";
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64) return "win-x64";
        throw new InvalidOperationException($"No self-contained release asset is available for {RuntimeInformation.OSDescription} / {architecture}.");
    }
}

public sealed record PlannedFile(string Source, string Destination, byte[] Content, bool Mergeable = false);

public sealed class CliOptions
{
    public string? Tools { get; private set; } public bool DryRun { get; private set; } public bool Backup { get; private set; } public bool Apply { get; private set; } public bool Print { get; private set; }
    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--tools") { if (++i >= args.Length) throw new ArgumentException("--tools requires a value."); result.Tools = args[i]; }
            else if (arg.StartsWith("--tools=", StringComparison.Ordinal)) result.Tools = arg[8..];
            else if (arg == "--dry-run") result.DryRun = true;
            else if (arg == "--backup") result.Backup = true;
            else if (arg == "--apply") result.Apply = true;
            else if (arg == "--print") result.Print = true;
            else throw new ArgumentException($"Unknown option '{arg}'.");
        }
        return result;
    }
}

public static class ChecksumParser
{
    public static IReadOnlyDictionary<string, string> Parse(string manifest)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit)) throw new ArgumentException($"Invalid SHA-256 manifest entry: {line}");
            var fileName = parts[1].TrimStart('*');
            if (!values.TryAdd(fileName, parts[0].ToLowerInvariant())) throw new ArgumentException($"Duplicate SHA-256 manifest entry: {fileName}");
        }
        return values;
    }
}
