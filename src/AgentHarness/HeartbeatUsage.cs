using System.Net;
using System.Text.Json;

namespace AgentHarness;

public static partial class Heartbeat
{
    private static readonly string[] UsageSources = ["codex-app-server", "codex-exec", "codex-goal", "claude-otel", "cursor-sdk"];

    private static int Usage(string file, IReadOnlyDictionary<string, string> options)
    {
        var source = Require(options, "source");
        var sessionId = Require(options, "session-id");
        var tokens = NonnegativeLong(options, "tokens-used");
        RecordAbsoluteUsage(file, source, sessionId, tokens);
        return 0;
    }

    internal static void RecordAbsoluteUsage(string file, string source, string sessionId, long tokens) =>
        WriteUsage(file, source, sessionId, tokens, sampleId: null);

    internal static void RecordDeltaUsage(string file, string source, string sessionId, long tokens, string sampleId) =>
        WriteUsage(file, source, sessionId, tokens, sampleId);

    internal static void RecordClaudeSeriesBatch(string file, string sessionId, IReadOnlyList<ClaudeUsageSample> samples)
    {
        if (!HeartbeatStore.ValidRunId(sessionId)) throw new ArgumentException("Usage requires a valid session ID.");
        if (samples.Count == 0) return;
        foreach (var sample in samples)
            if (sample.SessionId != sessionId || !HeartbeatStore.ValidRunId(sample.SeriesId) || sample.Tokens < 0)
                throw new ArgumentException("Claude usage batch contains an invalid series.");
        using var guard = HeartbeatStore.Lock(file);
        var state = HeartbeatStore.Read(file);
        if (state.BoundSessionId != sessionId) throw new InvalidOperationException("Usage session does not match the bound run session.");
        if (state.UsageSource is not null && state.UsageSource != "claude-otel")
            throw new InvalidOperationException("Run usage is already measured by a different source.");
        state.UsageSource = "claude-otel";
        foreach (var sample in samples)
        {
            var key = sessionId + ":" + sample.SeriesId;
            state.UsageSeriesTotals[key] = Math.Max(state.UsageSeriesTotals.GetValueOrDefault(key), sample.Tokens);
        }
        state.TokensUsed = SumUsage(state.UsageSeriesTotals.Values);
        state.UsageUpdatedAt = DateTimeOffset.UtcNow;
        HeartbeatStore.Write(file, state, overwrite: true);
    }

    private static void WriteUsage(string file, string source, string sessionId, long tokens, string? sampleId)
    {
        if (!UsageSources.Contains(source, StringComparer.Ordinal)) throw new ArgumentException("Unknown usage source.");
        if (!HeartbeatStore.ValidRunId(sessionId)) throw new ArgumentException("Usage requires a valid session ID.");
        if (tokens < 0) throw new ArgumentException("Usage must be nonnegative.");
        if (sampleId is not null && !HeartbeatStore.ValidRunId(sampleId)) throw new ArgumentException("Usage requires a valid sample ID.");
        using var guard = HeartbeatStore.Lock(file);
        var state = HeartbeatStore.Read(file);
        if (!string.Equals(state.BoundSessionId, sessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Usage session does not match the bound run session.");
        if (state.UsageSource is not null && state.UsageSource != source)
            throw new InvalidOperationException("Run usage is already measured by a different source.");
        state.UsageSource = source;
        if (sampleId is null)
        {
            var prior = state.UsageTotals.GetValueOrDefault(sessionId);
            if (tokens < prior) return; // A delayed or repeated absolute snapshot cannot lower usage.
            state.UsageTotals[sessionId] = tokens;
        }
        else
        {
            var key = sessionId + ":" + sampleId;
            if (state.UsageSampleIds.Contains(key, StringComparer.Ordinal)) return;
            state.UsageSampleIds.Add(key);
            var prior = state.UsageTotals.GetValueOrDefault(sessionId);
            if (tokens > long.MaxValue - prior) throw new ArgumentException("Usage total would overflow.");
            state.UsageTotals[sessionId] = prior + tokens;
        }
        state.TokensUsed = SumUsage(state.UsageTotals.Values);
        state.UsageUpdatedAt = DateTimeOffset.UtcNow;
        HeartbeatStore.Write(file, state, overwrite: true);
    }

    private static long SumUsage(IEnumerable<long> values)
    {
        long sum = 0;
        foreach (var value in values)
        {
            if (value < 0 || value > long.MaxValue - sum) throw new ArgumentException("Usage total would overflow.");
            sum += value;
        }
        return sum;
    }

    private static int CollectCodex(string file, IReadOnlyDictionary<string, string> options)
    {
        options.TryGetValue("session-id", out var expectedSessionId);
        if (expectedSessionId is not null && !HeartbeatStore.ValidRunId(expectedSessionId))
            throw new ArgumentException("--session-id must be a valid ID.");
        options.TryGetValue("stream-id", out var streamId);
        if (streamId is not null && (!HeartbeatStore.ValidRunId(streamId) || streamId.Length > 100))
            throw new ArgumentException("--stream-id must be a valid ID of at most 100 characters.");
        string? observedSessionId = null;
        var turnNumber = 0;
        var turnPending = false;
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            if (line.Length > 1024 * 1024) throw new ArgumentException("Codex event exceeds 1 MiB.");
            using JsonDocument document = ParseCodexLine(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String &&
                method.GetString() == "thread/tokenUsage/updated" &&
                root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object &&
                parameters.TryGetProperty("threadId", out var threadId) && threadId.ValueKind == JsonValueKind.String &&
                parameters.TryGetProperty("tokenUsage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Object &&
                total.TryGetProperty("totalTokens", out var count) && count.ValueKind == JsonValueKind.Number &&
                count.TryGetInt64(out var totalTokens) && totalTokens >= 0)
            {
                var id = threadId.GetString()!;
                if (expectedSessionId is null || id == expectedSessionId)
                    RecordAbsoluteUsage(file, "codex-app-server", id, totalTokens);
                continue;
            }
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;
            if (type.GetString() == "thread.started" && root.TryGetProperty("thread_id", out var startedId) && startedId.ValueKind == JsonValueKind.String)
            {
                observedSessionId = startedId.GetString();
                if (expectedSessionId is not null && observedSessionId != expectedSessionId)
                    throw new InvalidOperationException("Codex stream belongs to a different session.");
                if (observedSessionId is not null) BindObservedSession(file, observedSessionId);
                turnPending = true;
                continue;
            }
            if (type.GetString() == "turn.started")
            {
                turnPending = true;
                continue;
            }
            if (type.GetString() == "turn.failed")
                throw new InvalidOperationException("Codex exec turn failed.");
            if (type.GetString() == "turn.completed" && root.TryGetProperty("usage", out var turnUsage) && turnUsage.ValueKind == JsonValueKind.Object)
            {
                var sessionId = observedSessionId ?? throw new InvalidOperationException("Codex usage arrived before thread.started.");
                if (streamId is null) throw new ArgumentException("Codex exec usage requires a unique --stream-id for this invocation.");
                var input = ReadNonnegativeCount(turnUsage, "input_tokens");
                var output = ReadNonnegativeCount(turnUsage, "output_tokens");
                if (output > long.MaxValue - input) throw new ArgumentException("Codex token usage would overflow.");
                RecordDeltaUsage(file, "codex-exec", sessionId, input + output, $"{streamId}-turn-{++turnNumber}");
                turnPending = false;
            }
        }
        if (turnPending || (streamId is not null && turnNumber == 0))
            throw new InvalidOperationException("Codex exec stream ended before a completed turn with usage.");
        return 0;
    }

    private static JsonDocument ParseCodexLine(string line)
    {
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { throw new ArgumentException("Codex stream contains invalid JSON."); }
    }

    private static long ReadNonnegativeCount(JsonElement usage, string property)
    {
        if (!usage.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count) || count < 0)
            throw new ArgumentException($"Codex usage is missing {property}.");
        return count;
    }

    private static void BindObservedSession(string file, string sessionId)
    {
        if (!HeartbeatStore.ValidRunId(sessionId)) throw new ArgumentException("Codex stream has an invalid session ID.");
        using var guard = HeartbeatStore.Lock(file);
        var state = HeartbeatStore.Read(file);
        if (state.BoundSessionId is not null && state.BoundSessionId != sessionId)
            throw new InvalidOperationException("Codex stream belongs to a different bound session.");
        if (state.BoundSessionId is null)
        {
            state.BoundSessionId = sessionId;
            HeartbeatStore.Write(file, state, overwrite: true);
        }
    }

    private static int CollectClaude(string file, IReadOnlyDictionary<string, string> options)
    {
        var sessionId = Require(options, "session-id");
        var port = PositiveInt(options, "port");
        if (port > 65535) throw new ArgumentException("--port must be at most 65535.");
        // The receiver is a local observer. The native Claude session remains independent.
        try
        {
            ClaudeUsageCollector.RunAsync(port, sessionId,
                (samples, _) => { RecordClaudeSeriesBatch(file, sessionId, samples); return Task.CompletedTask; },
                CancellationToken.None, () => { Console.WriteLine($"Claude usage collector ready on 127.0.0.1:{port}"); Console.Out.Flush(); }).GetAwaiter().GetResult();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException($"Claude usage collector could not bind loopback port {port}: {ex.Message}", ex);
        }
        return 0;
    }
}
