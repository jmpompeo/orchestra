using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentHarness;

/// <summary>Read-only comparisons; supplied reports never establish authenticated runtime identity.</summary>
public static class ModelDiagnostics
{
    private static readonly string[] Roles = ["orchestrator", "reviewer", "worker", "explorer", "verifier"];
    private sealed record Observation(string Tool, string SessionId, string Role, string Source, string Stage, string Model, string Effort);
    public static int Run(string[] args, AssetStore assets, string home)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("models inspect --tool codex|claude [--evidence PATH]\nmodels candidate --config PATH [--tool codex|claude] [--results PATH]\nRead-only; candidate never adopts assignments. --results accepts evaluate-efficiency v1 report JSON. --evidence JSON: {\"observations\":[{\"tool\":\"codex\",\"sessionId\":\"session-1\",\"role\":\"worker\",\"source\":\"runtime-log\",\"stage\":\"runtime-reported\",\"model\":\"model-id\",\"effort\":\"medium\"}]}\nStages: selected, requested, runtime-reported. Missing/null effort is unavailable. Supplied reports are unverified; token totals do not establish model identity.");
                return 0;
            }
            if (args[0] is not ("inspect" or "candidate")) throw new ArgumentException($"Unknown models command '{args[0]}'.");
            var inspect = args[0] == "inspect";
            var options = ParseOptions(args[1..], inspect ? ["--tool", "--evidence"] : ["--tool", "--config", "--results"]);
            var tool = options.GetValueOrDefault("--tool");
            if (tool is not null && tool is not ("codex" or "claude")) throw new ArgumentException("--tool must be codex or claude.");
            if (inspect && tool is null) throw new ArgumentException("models inspect requires --tool codex|claude.");
            var baseline = ParseConfig(assets.ReadText("models.conf"), assets);
            if (inspect)
            {
                var reports = ReadObservations(options.GetValueOrDefault("--evidence"));
                Console.WriteLine("Assignments source: bundled models.conf (rerun installer after assignment changes).\nInstalled definitions do not prove runtime selection. Native token totals are not model evidence.");
                Inspect(tool!, baseline, reports, assets, home);
            }
            else
            {
                if (!options.TryGetValue("--config", out var path)) throw new ArgumentException("models candidate requires --config PATH.");
                var candidate = ParseConfig(File.ReadAllText(path), assets);
                var resultsPath = options.GetValueOrDefault("--results");
                Console.WriteLine($"Candidate source: {Path.GetFullPath(path)}; no files changed, no adoption performed.");
                foreach (var target in tool is null ? new[] { "codex", "claude" } : new[] { tool })
                foreach (var role in Roles)
                {
                    var key = Key(target, role); var rendered = Render(target, role, candidate, assets);
                    Console.WriteLine($"{target}/{role}: {(baseline[key] == candidate[key] ? "unchanged" : "CHANGED")} configured {baseline[key]} -> {candidate[key]}; rendered {Value(rendered.Model)}; effort {Value(rendered.Effort)}.");
                }
                Console.WriteLine("Compatibility: static template substitution passed; runtime availability, supported effort, quality, latency, and cost are unverified. Static compatibility does not prove runtime compatibility.");
                PrintResults(resultsPath);
            }
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        { Console.Error.WriteLine($"Error: {ex.Message}"); return 2; }
    }

    private static Dictionary<string, string> ParseOptions(string[] args, string[] allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!allowed.Contains(args[i])) throw new ArgumentException($"Unknown models option '{args[i]}'.");
            var name = args[i];
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException($"{name} requires a value.");
            if (!result.TryAdd(name, args[i])) throw new ArgumentException($"Duplicate option '{name}'.");
        }
        return result;
    }
    private static string Key(string tool, string role) => $"{tool}_{role}_MODEL".ToUpperInvariant();
    private static string Value(string? value) => value ?? "unavailable";
    private static Dictionary<string, string> ParseConfig(string text, AssetStore assets)
    {
        var expected = new[] { "codex", "claude" }.SelectMany(t => Roles.Select(r => Key(t, r))).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !expected.Contains(pair[0]) || !Regex.IsMatch(pair[1], @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$")) throw new ArgumentException($"Invalid models.conf assignment: {line}");
            if (!result.TryAdd(pair[0], pair[1])) throw new ArgumentException($"Duplicate model assignment: {pair[0]}");
        }
        foreach (var key in expected) if (!result.ContainsKey(key)) throw new ArgumentException($"Missing model assignment: {key}");
        foreach (var tool in new[] { "codex", "claude" }) foreach (var role in Roles.Where(r => r != "orchestrator")) _ = Render(tool, role, result, assets);
        return result;
    }
    private static (string? Text, string? Model, string? Effort, string? FileName) Render(string tool, string role, Dictionary<string, string> config, AssetStore assets)
    {
        if (role == "orchestrator") return (null, null, null, null);
        var path = $"global/{tool}/agents/workflow{(tool == "codex" ? "_" : "-")}{role}{(tool == "codex" ? ".toml" : ".md")}.tmpl";
        var text = assets.ReadText(path);
        foreach (var (key, model) in config) text = text.Replace($"@@{key}@@", model, StringComparison.Ordinal);
        if (text.Contains("@@", StringComparison.Ordinal)) throw new ArgumentException($"Unresolved substitution in {path}.");
        var fields = ReadFields(text, tool);
        if (fields.Model != config[Key(tool, role)]) throw new ArgumentException($"Rendered model assignment mismatch in {path}.");
        return (text, fields.Model, fields.Effort, Path.GetFileName(path[..^5]));
    }
    private static (string? Model, string? Effort) ReadFields(string text, string tool)
    {
        if (tool == "claude")
        {
            text = text.Replace("\r\n", "\n");
            var end = text.StartsWith("---\n", StringComparison.Ordinal) ? text.IndexOf("\n---", 4, StringComparison.Ordinal) : -1;
            if (end < 0) return (null, null);
            text = text[4..end];
        }
        string? Field(string name)
        {
            var matches = Regex.Matches(text, $@"(?m)^\s*{name}\s*{(tool == "codex" ? "=" : ":")}\s*([^\r\n]+)$");
            if (matches.Count != 1) return null;
            var value = matches[0].Groups[1].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1];
            if (tool == "claude" && Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$")) return value;
            return null;
        }
        return (Field("model"), Field(tool == "codex" ? "model_reasoning_effort" : "effort"));
    }
    private static void Inspect(string tool, Dictionary<string, string> config, List<Observation> reports, AssetStore assets, string home)
    {
        foreach (var role in Roles)
        {
            var expected = Render(tool, role, config, assets);
            Console.WriteLine($"{tool}/{role}: configured={config[Key(tool, role)]}; configured effort=unavailable (fixed by template); rendered={Value(expected.Model)}; rendered effort={Value(expected.Effort)}");
            if (role == "orchestrator") Console.WriteLine("  advisory only: primary model is selected in tool UI/config; installed definition unavailable.");
            else
            {
                var path = Path.Combine(home, "." + tool, "agents", expected.FileName!);
                if (!File.Exists(path)) Console.WriteLine($"  installed=missing; installed effort=unavailable; path={path}");
                else
                {
                    var text = File.ReadAllText(path); var actual = ReadFields(text, tool);
                    var drift = text == expected.Text ? "matched" : "full-file modified";
                    var assignment = actual.Model != expected.Model || actual.Effort != expected.Effort ? "stale/overridden assignment" : "matched";
                    Console.WriteLine($"  installed={Value(actual.Model)}; installed effort={Value(actual.Effort)}; drift={drift}; assignment={assignment}; path={path}");
                }
            }
            foreach (var stage in new[] { "selected", "requested", "runtime-reported" })
            {
                var matches = reports.Where(o => o.Tool == tool && o.Role == role && o.Stage == stage).ToArray();
                if (matches.Length == 0) Console.WriteLine($"  {stage}=unavailable; effort=unavailable");
                foreach (var report in matches) PrintReport(report);
            }
        }
    }
    private static List<Observation> ReadObservations(string? path)
    {
        if (path is null) return [];
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("observations", out var observations) || observations.ValueKind != JsonValueKind.Array) throw new ArgumentException("Evidence/results must contain an observations array; token totals are not model evidence.");
        var result = new List<Observation>();
        foreach (var item in observations.EnumerateArray())
        {
            string Required(string name)
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString())) throw new ArgumentException($"Each observation requires explicit {name} identity/value.");
                var value = field.GetString()!;
                if (value.Any(char.IsControl)) throw new ArgumentException($"Observation {name} contains control characters.");
                return value;
            }
            var report = new Observation(Required("tool"), Required("sessionId"), Required("role"), Required("source"), Required("stage"), Required("model"), item.TryGetProperty("effort", out var effort) && effort.ValueKind != JsonValueKind.Null ? Required("effort") : "unavailable");
            if (report.Tool is not ("codex" or "claude") || !Roles.Contains(report.Role) || report.Stage is not ("selected" or "requested" or "runtime-reported")) throw new ArgumentException("Observation tool, role, or stage is invalid.");
            result.Add(report);
        }
        return result;
    }
    private static void PrintResults(string? path)
    {
        if (path is null) { Console.WriteLine("Experiment results: unavailable; runtime-reported actual model and effort unavailable."); return; }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        JsonElement Required(JsonElement value, string name, JsonValueKind kind)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var field) || field.ValueKind != kind)
                throw new ArgumentException($"Invalid evaluation report: requires {name} ({kind}).");
            return field;
        }
        var version = Required(root, "version", JsonValueKind.Number);
        if (!version.TryGetInt32(out var number) || number != 1) throw new ArgumentException("Evaluation report version must be 1.");
        var evidence = Required(root, "evidence", JsonValueKind.String).GetString();
        if (evidence is not ("synthetic-fixture-only" or "recorded-trials")) throw new ArgumentException("Unknown evaluation report evidence kind.");
        var combined = Required(root, "combined", JsonValueKind.Object);
        var batches = Required(root, "batches", JsonValueKind.Object);
        var heldOut = Required(root, "heldOut", JsonValueKind.Object);
        var pooled = Required(root, "pooled", JsonValueKind.Object);
        _ = Required(root, "unmatched", JsonValueKind.Array);
        // Validate the comparisons before printing any supplied results.
        foreach (var comparison in new[] { combined, heldOut, pooled }.Concat(batches.EnumerateObject().Select(p => p.Value)))
        {
            var matched = Required(comparison, "matchedTasks", JsonValueKind.Number);
            if (!matched.TryGetInt32(out var count) || count < 0) throw new ArgumentException("Invalid evaluation matchedTasks.");
            var variants = Required(comparison, "variants", JsonValueKind.Object);
            foreach (var variant in new[] { "baseline", "candidate" })
            {
                var values = Required(variants, variant, JsonValueKind.Object);
                foreach (var key in new[] { "attempts", "failed", "retried", "acceptedTasks", "deterministicPassedTasks" })
                    if (!Required(values, key, JsonValueKind.Number).TryGetInt32(out var countValue) || countValue < 0) throw new ArgumentException($"Invalid evaluation {key}.");
            }
            _ = Required(comparison, "metrics", JsonValueKind.Object);
            _ = Required(comparison, "targets", JsonValueKind.Object);
        }
        Console.WriteLine($"Evaluation report: {Path.GetFullPath(path)}; evidence={evidence}; reported/unverified (supplied file, authenticity not verified).");
        if (evidence == "synthetic-fixture-only") Console.WriteLine("Synthetic fixture only: no productivity or runtime compatibility claim.");
        Console.WriteLine("Recorded evaluation results are separate from static config compatibility and do not establish runtime model identity or readiness to adopt. Adoption requires explicit user approval.");
        void PrintComparison(string label, JsonElement comparison)
        {
            Console.WriteLine($"  {label}: matchedTasks={comparison.GetProperty("matchedTasks")}; variants={comparison.GetProperty("variants").GetRawText()}");
            Console.WriteLine($"  {label} metrics (reported): {comparison.GetProperty("metrics").GetRawText()}");
            Console.WriteLine($"  {label} targets (reported/unverified): {comparison.GetProperty("targets").GetRawText()}");
        }
        PrintComparison("pooled (descriptive)", pooled);
        PrintComparison("combined batch", combined);
        foreach (var batch in batches.EnumerateObject()) PrintComparison("batch " + batch.Name, batch.Value);
        PrintComparison("heldOut", heldOut);
    }
    private static void PrintReport(Observation report) => Console.WriteLine($"  {report.Stage}={report.Model}; effort={report.Effort}; session={report.SessionId}; role={report.Role}; source={report.Source}; reported/unverified (supplied file, authenticity not verified)");
}
