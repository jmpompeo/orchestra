namespace AgentHarness;

internal static class HeartbeatSummary
{
    internal static object Create(HeartbeatState state)
    {
        var now = DateTimeOffset.UtcNow;
        var latest = state.Events.GroupBy(x => x.Task, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last().Status, StringComparer.Ordinal);
        var done = latest.Values.Count(x => x == "finished");
        var blocked = latest.Where(x => x.Value == "blocked").Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
        var failureGroups = state.Events.Where(x => x.Status == "failed")
            .GroupBy(x => (x.Task, x.Reason)).ToArray();
        var failures = failureGroups.GroupBy(x => x.Key.Task, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(group => group.Count()), StringComparer.Ordinal);
        var stale = state.HookState == "active" && state.LastHookAt is not null && (now - state.LastHookAt.Value).TotalSeconds > state.StaleAfterSeconds;
        var timeOver = state.StartedAt is not null && (now - state.StartedAt.Value).TotalMinutes >= state.TimeBudgetMinutes;
        var tokensOver = state.TokenBudget is not null && state.TokensUsed is not null && state.TokensUsed >= state.TokenBudget;
        var repeatedFailure = failureGroups.Any(x => x.Count() >= 2);
        return new
        {
            state.RunId, state.TotalTasks, Done = done, Remaining = Math.Max(0, state.TotalTasks - done), BlockedTasks = blocked,
            ElapsedSeconds = state.StartedAt is null ? 0 : (long)Math.Max(0, (now - state.StartedAt.Value).TotalSeconds),
            state.CreatedAt, state.StartedAt, state.BoundSessionId, state.LastProbeAt, state.LastProbeSessionId,
            state.LastHookAt, state.HookState,
            state.StaleAfterSeconds, Stale = stale, TimeOverBudget = timeOver, TokensOverBudget = tokensOver,
            state.TokensUsed, state.TokenBudget, state.UsageSource, state.UsageUpdatedAt, FailureCounts = failures,
            StopRecommended = timeOver || tokensOver || repeatedFailure, state.Events.Count
        };
    }
}
