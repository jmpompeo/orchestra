using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentHarness;

internal sealed record ClaudeUsageSample(string SeriesId, string SessionId, long Tokens);

/// <summary>
/// Receives Claude Code's OTLP/HTTP JSON token counter on loopback. The caller
/// records per-series cumulative maxima so repeated exports cannot count twice.
/// </summary>
internal static class ClaudeUsageCollector
{
    private const int MaxRequestBytes = 4 * 1024 * 1024;
    private const string TokenMetric = "claude_code.token.usage";

    internal static async Task RunAsync(
        int port,
        string expectedSessionId,
        Func<IReadOnlyList<ClaudeUsageSample>, CancellationToken, Task> accept,
        CancellationToken cancellationToken,
        Action? onReady = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (!HeartbeatStore.ValidRunId(expectedSessionId)) throw new ArgumentException("A valid Claude session ID is required.", nameof(expectedSessionId));
        ArgumentNullException.ThrowIfNull(accept);

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        onReady?.Invoke();
        using var cancellation = cancellationToken.Register(listener.Stop);
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            await HandleAsync(context, expectedSessionId, accept, cancellationToken);
        }
    }

    private static async Task HandleAsync(
        HttpListenerContext context,
        string expectedSessionId,
        Func<IReadOnlyList<ClaudeUsageSample>, CancellationToken, Task> accept,
        CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.ContentType = "application/json";
        try
        {
            if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/v1/metrics")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            if (!string.Equals(context.Request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(context.Request.Headers["Content-Encoding"]))
            {
                response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                return;
            }
            if (context.Request.ContentLength64 > MaxRequestBytes)
            {
                response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return;
            }
            var payload = await ReadLimitedAsync(context.Request.InputStream, cancellationToken);
            await accept(ParseMetrics(payload, expectedSessionId), cancellationToken);
            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync("{}"u8.ToArray(), cancellationToken);
        }
        catch (InvalidDataException)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        catch (JsonException)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        catch (IOException)
        {
            response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        }
        catch (Exception error)
        {
            // A failed state write must make the exporter retry this batch.
            Console.Error.WriteLine($"Claude usage collector could not record a telemetry batch: {error.Message}");
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            try { response.Close(); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > MaxRequestBytes) throw new IOException("OTLP request is too large.");
            buffer.Write(chunk, 0, count);
        }
    }

    internal static IReadOnlyList<ClaudeUsageSample> ParseMetrics(ReadOnlyMemory<byte> payload, string expectedSessionId)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("OTLP request must be an object.");
        var samples = new List<ClaudeUsageSample>();
        if (!TryArray(root, "resourceMetrics", out var resources)) return samples;
        foreach (var resource in resources.EnumerateArray())
        {
            if (resource.ValueKind != JsonValueKind.Object) continue;
            var resourceAttributes = TryObject(resource, "resource", out var resourceInfo)
                ? AttributeKey(resourceInfo)
                : "";
            if (!TryArray(resource, "scopeMetrics", out var scopes)) continue;
            foreach (var scope in scopes.EnumerateArray())
            {
                if (scope.ValueKind != JsonValueKind.Object || !TryArray(scope, "metrics", out var metrics)) continue;
                foreach (var metric in metrics.EnumerateArray())
                {
                    if (metric.ValueKind != JsonValueKind.Object || !StringProperty(metric, "name", TokenMetric)) continue;
                    if (!TryObject(metric, "sum", out var sum) || !IsCumulative(sum))
                        throw new InvalidDataException("Claude token usage must use cumulative temporality.");
                    if (!TryArray(sum, "dataPoints", out var points)) continue;
                    foreach (var point in points.EnumerateArray())
                    {
                        if (point.ValueKind != JsonValueKind.Object) continue;
                        var attributes = ReadAttributes(point);
                        if (!attributes.TryGetValue("session.id", out var sessionId) ||
                            !string.Equals(sessionId, expectedSessionId, StringComparison.Ordinal)) continue;
                        if (!attributes.TryGetValue("type", out var type) || type is not ("input" or "output" or "cacheRead" or "cacheCreation"))
                            throw new InvalidDataException("Claude token sample has an unknown type.");
                        if (!TryTokenCount(point, out var tokens))
                            throw new InvalidDataException("Claude token sample requires a nonnegative integer count.");
                        if (!TryPositiveLong(point, "timeUnixNano", out var end))
                            throw new InvalidDataException("Claude token sample requires a valid end time.");
                        if (!TryPositiveLong(point, "startTimeUnixNano", out var start))
                            throw new InvalidDataException("Claude cumulative token sample requires a valid start time.");
                        if (start > end) throw new InvalidDataException("Claude token sample has an invalid time window.");
                        var identity = resourceAttributes + "|" + AttributeKey(attributes) + "|" + start.ToString(CultureInfo.InvariantCulture);
                        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
                        samples.Add(new ClaudeUsageSample(Convert.ToHexString(hash).ToLowerInvariant(), sessionId, tokens));
                    }
                }
            }
        }
        return samples;
    }

    private static bool IsCumulative(JsonElement sum) =>
        sum.TryGetProperty("aggregationTemporality", out var value) &&
        (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == 2 ||
         value.ValueKind == JsonValueKind.String && value.GetString() is "2" or "AGGREGATION_TEMPORALITY_CUMULATIVE");

    private static bool TryArray(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;

    private static bool TryObject(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool StringProperty(JsonElement parent, string name, string expected) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool TryPositiveLong(JsonElement parent, string name, out long value) =>
        TryNonnegativeLong(parent, name, out value) && value > 0;

    private static bool TryTokenCount(JsonElement point, out long value)
    {
        if (TryNonnegativeLong(point, "asInt", out value)) return true;
        value = 0;
        if (!point.TryGetProperty("asDouble", out var field) || field.ValueKind != JsonValueKind.Number ||
            !field.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0 ||
            number > 9_007_199_254_740_991d || Math.Truncate(number) != number) return false;
        value = (long)number;
        return true;
    }

    private static bool TryNonnegativeLong(JsonElement parent, string name, out long value)
    {
        value = 0;
        if (!parent.TryGetProperty(name, out var field)) return false;
        if (field.ValueKind == JsonValueKind.Number) return field.TryGetInt64(out value) && value >= 0;
        return field.ValueKind == JsonValueKind.String && long.TryParse(field.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static Dictionary<string, string> ReadAttributes(JsonElement parent)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryArray(parent, "attributes", out var items)) return result;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String ||
                !TryObject(item, "value", out var value)) continue;
            var name = key.GetString();
            if (string.IsNullOrEmpty(name) || result.ContainsKey(name)) continue;
            if (value.TryGetProperty("stringValue", out var text) && text.ValueKind == JsonValueKind.String)
                result.Add(name, text.GetString()!);
            else if (value.TryGetProperty("intValue", out var number) && number.ValueKind == JsonValueKind.String)
                result.Add(name, number.GetString()!);
            else if (value.TryGetProperty("boolValue", out var boolean) && boolean.ValueKind is JsonValueKind.True or JsonValueKind.False)
                result.Add(name, boolean.GetBoolean() ? "true" : "false");
        }
        return result;
    }

    private static string AttributeKey(JsonElement parent) => AttributeKey(ReadAttributes(parent));

    private static string AttributeKey(IReadOnlyDictionary<string, string> attributes)
    {
        var result = new StringBuilder();
        foreach (var pair in attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Append(pair.Key.Length).Append(':').Append(pair.Key);
            result.Append(pair.Value.Length).Append(':').Append(pair.Value);
        }
        return result.ToString();
    }
}
