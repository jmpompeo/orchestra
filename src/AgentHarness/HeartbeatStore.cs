using System.Diagnostics;
using System.Text.Json;

namespace AgentHarness;

internal static class HeartbeatStore
{
    private const string Prefix = "window.ORCHESTRA_HEARTBEAT = ";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string FileFor(string directory, bool createDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("--dir must name a directory.");
        var full = Path.GetFullPath(directory);
        var ancestor = full;
        while (!Directory.Exists(ancestor))
        {
            if (File.Exists(ancestor)) throw new InvalidOperationException($"A file blocks heartbeat directory {ancestor}.");
            var parent = Path.GetDirectoryName(ancestor);
            if (parent is null || parent == ancestor) throw new InvalidOperationException("Heartbeat directory has no existing ancestor.");
            ancestor = parent;
        }
        for (var current = full; current != ancestor; current = Path.GetDirectoryName(current)!)
            RefuseLink(current);
        for (var current = ancestor; current is not null; current = Path.GetDirectoryName(current))
        {
            RefuseLink(current);
            if (Path.GetPathRoot(current) == current) break;
        }
        if (createDirectory) Directory.CreateDirectory(full);
        else if (!Directory.Exists(full)) throw new InvalidOperationException("Heartbeat directory does not exist.");
        return Path.Combine(full, "heartbeat.js");
    }

    private static void RefuseLink(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException($"Linked heartbeat path is not allowed: {path}");
    }

    internal static void RefuseLinkedFile(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException($"Linked heartbeat file is not allowed: {path}");
    }

    internal static FileStream Lock(string file)
    {
        var path = Path.Combine(Path.GetDirectoryName(file)!, ".heartbeat.lock");
        RefuseLinkedFile(path);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(25); }
        }
    }

    internal static HeartbeatState Read(string file)
    {
        RefuseLinkedFile(file);
        var script = File.ReadAllText(file);
        if (!script.StartsWith(Prefix, StringComparison.Ordinal) || !script.EndsWith(';')) throw new InvalidOperationException("Heartbeat file has an invalid format.");
        HeartbeatState? state;
        try { state = JsonSerializer.Deserialize<HeartbeatState>(script[Prefix.Length..^1], JsonOptions); }
        catch (JsonException) { throw new InvalidOperationException("Heartbeat file contains invalid JSON."); }
        if (state is null || !ValidState(state))
            throw new InvalidOperationException("Heartbeat file has invalid run metadata.");
        return state;
    }

    internal static bool ValidRunId(string? runId) =>
        !string.IsNullOrWhiteSpace(runId) && runId.Length <= 128 && !runId.Any(char.IsControl);

    internal static bool ValidState(HeartbeatState state) =>
        state.Version == 1 && ValidRunId(state.RunId) && state.CreatedAt != default &&
        state.TotalTasks > 0 && state.TimeBudgetMinutes > 0 && state.StaleAfterSeconds > 0 &&
        (state.TokenBudget is null || state.TokenBudget > 0) &&
        (state.TokensUsed is null || state.TokensUsed >= 0) &&
        state.UsageTotals is not null && state.UsageTotals.Values.All(x => x >= 0) &&
        state.UsageSampleIds is not null &&
        state.UsageSeriesTotals is not null && state.UsageSeriesTotals.Values.All(x => x >= 0) &&
        (state.StartedAt is null || state.StartedAt >= state.CreatedAt) &&
        (state.BoundSessionId is null || ValidRunId(state.BoundSessionId)) &&
        (state.LastProbeSessionId is null || ValidRunId(state.LastProbeSessionId)) &&
        (state.HookState is "waiting" or "active" or "stopped") && state.Events is not null;

    internal static void Write(string file, HeartbeatState state, bool overwrite)
    {
        RefuseLinkedFile(file);
        var temporary = Path.Combine(Path.GetDirectoryName(file)!, $".heartbeat.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) writer.Write(Prefix + JsonSerializer.Serialize(state, JsonOptions) + ";");
            RefuseLinkedFile(file);
            File.Move(temporary, file, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
