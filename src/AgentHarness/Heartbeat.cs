using System.Text.Json;

namespace AgentHarness;

public sealed class HeartbeatState
{
    public int Version { get; set; } = 1;
    public string RunId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public int TotalTasks { get; set; }
    public int TimeBudgetMinutes { get; set; }
    public int StaleAfterSeconds { get; set; }
    public long? TokenBudget { get; set; }
    public long? TokensUsed { get; set; }
    public string? UsageSource { get; set; }
    public DateTimeOffset? UsageUpdatedAt { get; set; }
    public Dictionary<string, long> UsageTotals { get; set; } = new(StringComparer.Ordinal);
    public List<string> UsageSampleIds { get; set; } = new();
    public Dictionary<string, long> UsageSeriesTotals { get; set; } = new(StringComparer.Ordinal);
    public string? BoundSessionId { get; set; }
    public DateTimeOffset? LastProbeAt { get; set; }
    public string? LastProbeSessionId { get; set; }
    public DateTimeOffset? LastHookAt { get; set; }
    public string HookState { get; set; } = "waiting";
    public List<HeartbeatEvent> Events { get; set; } = new();
}

public sealed class HeartbeatEvent
{
    public string Task { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public static partial class Heartbeat
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("heartbeat init --dir PATH --run-id ID --total-tasks N --time-budget-minutes N --stale-after-seconds N [--token-budget N]\n" +
                "heartbeat event --dir PATH --task ID --status started|finished|blocked|failed --reason TEXT [--total-tasks N]\n" +
                "heartbeat usage --dir PATH --source SOURCE --session-id ID --tokens-used N (absolute native total)\n" +
                "heartbeat collect-codex --dir PATH [--session-id ID] [--stream-id ID] (reads Codex JSONL on stdin)\n" +
                "heartbeat collect-claude --dir PATH --session-id ID --port N\n" +
                "heartbeat bind --dir PATH --session-id ID [--replace --expected-session-id OLD]\n" +
                "heartbeat hook --dir PATH (reads hook JSON on stdin)\nheartbeat status --dir PATH");
            return 0;
        }
        var verb = args[0];
        if (verb is not ("init" or "event" or "usage" or "collect-codex" or "collect-claude" or "bind" or "hook" or "status")) throw new ArgumentException($"Unknown heartbeat action '{verb}'.");
        var options = ParseOptions(args[1..]);
        var allowed = verb switch
        {
            "init" => new[] { "dir", "run-id", "total-tasks", "time-budget-minutes", "stale-after-seconds", "token-budget" },
            "event" => new[] { "dir", "task", "status", "reason", "total-tasks" },
            "usage" => new[] { "dir", "source", "session-id", "tokens-used" },
            "collect-codex" => new[] { "dir", "session-id", "stream-id" },
            "collect-claude" => new[] { "dir", "session-id", "port" },
            "bind" => new[] { "dir", "session-id", "replace", "expected-session-id" },
            _ => new[] { "dir" }
        };
        foreach (var key in options.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal)) throw new ArgumentException($"--{key} is not valid for heartbeat {verb}.");
        var directory = Require(options, "dir");
        var file = HeartbeatStore.FileFor(directory, createDirectory: verb == "init");
        if (verb == "init") return Init(file, options);
        if (!File.Exists(file)) throw new InvalidOperationException("Heartbeat run has not been initialized.");
        return verb switch
        {
            "status" => Status(file),
            "bind" => Bind(file, options),
            "event" => Event(file, options),
            "usage" => Usage(file, options),
            "collect-codex" => CollectCodex(file, options),
            "collect-claude" => CollectClaude(file, options),
            _ => Hook(file)
        };
    }

    private static int Init(string file, IReadOnlyDictionary<string, string> options)
    {
        var runId = Require(options, "run-id");
        if (!HeartbeatStore.ValidRunId(runId)) throw new ArgumentException("--run-id must be a nonempty ID of at most 128 characters.");
        var state = new HeartbeatState
        {
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            TotalTasks = PositiveInt(options, "total-tasks"),
            TimeBudgetMinutes = PositiveInt(options, "time-budget-minutes"),
            StaleAfterSeconds = PositiveInt(options, "stale-after-seconds"),
            TokenBudget = options.ContainsKey("token-budget") ? PositiveLong(options, "token-budget") : null
        };
        if (!HeartbeatStore.ValidState(state)) throw new ArgumentException("Invalid heartbeat run metadata.");
        using var guard = HeartbeatStore.Lock(file);
        HeartbeatStore.RefuseLinkedFile(file);
        if (File.Exists(file)) throw new InvalidOperationException("Heartbeat run already exists; refusing to overwrite it.");
        HeartbeatStore.Write(file, state, overwrite: false);
        Console.WriteLine(file);
        return 0;
    }

    private static int Status(string file)
    {
        using var guard = HeartbeatStore.Lock(file);
        Console.WriteLine(JsonSerializer.Serialize(HeartbeatSummary.Create(HeartbeatStore.Read(file)), HeartbeatStore.JsonOptions));
        return 0;
    }

    private static int Bind(string file, IReadOnlyDictionary<string, string> options)
    {
        var bindSessionId = Require(options, "session-id");
        if (!HeartbeatStore.ValidRunId(bindSessionId)) throw new ArgumentException("--session-id must be a nonempty ID of at most 128 characters.");
        using var guard = HeartbeatStore.Lock(file);
        var state = HeartbeatStore.Read(file);
        var replace = options.ContainsKey("replace");
        if (options.TryGetValue("expected-session-id", out var expectedSessionId))
        {
            if (!replace || !HeartbeatStore.ValidRunId(expectedSessionId))
                throw new ArgumentException("--expected-session-id requires --replace and a valid ID.");
            if (!string.Equals(state.BoundSessionId, expectedSessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Heartbeat binding changed since the previous session check.");
        }
        if (state.BoundSessionId is not null && !string.Equals(state.BoundSessionId, bindSessionId, StringComparison.Ordinal) && !replace)
            throw new InvalidOperationException("Heartbeat run is already bound to a different session.");
        if (state.BoundSessionId is null || replace)
        {
            state.BoundSessionId = bindSessionId;
            state.LastHookAt = null;
            state.HookState = "waiting";
            HeartbeatStore.Write(file, state, overwrite: true);
        }
        return 0;
    }

    private static int Event(string file, IReadOnlyDictionary<string, string> options)
    {
        var task = Require(options, "task");
        if (string.IsNullOrWhiteSpace(task) || task.Length > 128 || task.Any(char.IsControl)) throw new ArgumentException("--task must be a nonempty ID of at most 128 characters.");
        var status = Require(options, "status");
        if (status is not ("started" or "finished" or "blocked" or "failed")) throw new ArgumentException("--status must be started, finished, blocked, or failed.");
        var reason = Require(options, "reason");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4096) throw new ArgumentException("--reason must be nonempty and at most 4096 characters.");
        var total = options.ContainsKey("total-tasks") ? PositiveInt(options, "total-tasks") : (int?)null;
        using var guard = HeartbeatStore.Lock(file);
        var state = HeartbeatStore.Read(file);
        if (total is not null && total < state.TotalTasks) throw new ArgumentException("--total-tasks cannot decrease the task count.");
        if (total is not null) state.TotalTasks = total.Value;
        var at = DateTimeOffset.UtcNow;
        if (status == "started" && state.StartedAt is null) state.StartedAt = at;
        state.Events.Add(new HeartbeatEvent { Task = task, Status = status, Reason = reason, At = at });
        HeartbeatStore.Write(file, state, overwrite: true);
        return 0;
    }

    private static int Hook(string file)
    {
        var (hookEvent, sessionId) = ReadHookEvent();
        using (var guard = HeartbeatStore.Lock(file))
        {
            var state = HeartbeatStore.Read(file);
            if (state.BoundSessionId is null)
            {
                state.LastProbeAt = DateTimeOffset.UtcNow;
                state.LastProbeSessionId = sessionId;
            }
            else if (string.Equals(state.BoundSessionId, sessionId, StringComparison.Ordinal))
            {
                state.LastHookAt = DateTimeOffset.UtcNow;
                state.HookState = hookEvent == "Stop" ? "stopped" : "active";
            }
            else return 0;
            HeartbeatStore.Write(file, state, overwrite: true);
        }
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (!option.StartsWith("--", StringComparison.Ordinal) || option.Length == 2) throw new ArgumentException($"Unexpected argument '{option}'.");
            if (option == "--replace")
            {
                if (!result.TryAdd("replace", "true")) throw new ArgumentException("Duplicate --replace option.");
                continue;
            }
            var equals = option.IndexOf('=');
            var key = equals >= 0 ? option[2..equals] : option[2..];
            if (key == "replace") throw new ArgumentException("--replace takes no value.");
            var value = equals >= 0 ? option[(equals + 1)..] : ++i < args.Length ? args[i] : throw new ArgumentException($"--{key} requires a value.");
            if (value.Length == 0 || (equals < 0 && value.StartsWith("--", StringComparison.Ordinal))) throw new ArgumentException($"--{key} requires a value.");
            if (!result.TryAdd(key, value)) throw new ArgumentException($"Duplicate --{key} option.");
        }
        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) ? value : throw new ArgumentException($"--{key} is required.");

    private static int PositiveInt(IReadOnlyDictionary<string, string> options, string key) =>
        int.TryParse(Require(options, key), out var value) && value > 0 ? value : throw new ArgumentException($"--{key} must be a positive integer.");

    private static long PositiveLong(IReadOnlyDictionary<string, string> options, string key) =>
        long.TryParse(Require(options, key), out var value) && value > 0 ? value : throw new ArgumentException($"--{key} must be a positive integer.");

    private static long NonnegativeLong(IReadOnlyDictionary<string, string> options, string key) =>
        long.TryParse(Require(options, key), out var value) && value >= 0 ? value : throw new ArgumentException($"--{key} must be a nonnegative integer.");

    private static (string Event, string SessionId) ReadHookEvent()
    {
        var input = Console.In.ReadToEnd();
        try
        {
            using var document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Hook input must be a JSON object.");
            if (!document.RootElement.TryGetProperty("session_id", out var sessionValue) || sessionValue.ValueKind != JsonValueKind.String || !HeartbeatStore.ValidRunId(sessionValue.GetString()))
                throw new ArgumentException("Hook input requires a valid session_id.");
            var sessionId = sessionValue.GetString()!;
            foreach (var name in new[] { "hook_event_name", "hookEventName", "event" })
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var hookEvent = value.GetString();
                    if (hookEvent is "PostToolUse" or "Stop") return (hookEvent, sessionId);
                }
        }
        catch (JsonException) { throw new ArgumentException("Hook input must be valid JSON."); }
        throw new ArgumentException("Hook event must be PostToolUse or Stop.");
    }

}
