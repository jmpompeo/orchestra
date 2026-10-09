namespace AgentHarness;

internal enum BlockPresence { Missing, Present, Malformed }

internal static class ManagedBlock
{
    internal const string Begin = "<!-- orchestra:begin -->";
    internal const string End = "<!-- orchestra:end -->";

    internal static BlockPresence Locate(string text, out int start, out int length)
    {
        start = length = 0;
        var begins = MarkerLines(text, Begin);
        var ends = MarkerLines(text, End);
        if (begins.Count == 0 && ends.Count == 0) return BlockPresence.Missing;
        if (begins.Count != 1 || ends.Count != 1 || ends[0] < begins[0]) return BlockPresence.Malformed;
        start = begins[0]; length = ends[0] + End.Length - start;
        return BlockPresence.Present;
    }

    // only a line that is exactly the marker counts, so inline mentions in user text are ignored
    private static List<int> MarkerLines(string text, string marker)
    {
        var hits = new List<int>();
        for (var pos = 0; pos <= text.Length;)
        {
            var eol = text.IndexOf('\n', pos);
            var lineEnd = eol < 0 ? text.Length : eol;
            if (text.AsSpan(pos, lineEnd - pos).TrimEnd('\r').SequenceEqual(marker)) hits.Add(pos);
            if (eol < 0) break;
            pos = eol + 1;
        }
        return hits;
    }

    internal static string Wrap(string content) => $"{Begin}\n{content.Trim()}\n{End}";

    internal static string Append(string text, string content) =>
        string.IsNullOrWhiteSpace(text) ? Wrap(content) + "\n" : text.TrimEnd('\r', '\n') + "\n\n" + Wrap(content) + "\n";

    internal static string Replace(string text, int start, int length, string content) =>
        text[..start] + Wrap(content) + text[(start + length)..];

    internal static string Remove(string text, int start, int length)
    {
        var before = text[..start].TrimEnd('\r', '\n');
        var after = text[(start + length)..];
        after = after.StartsWith("\r\n", StringComparison.Ordinal) ? after[2..] : after.StartsWith('\n') ? after[1..] : after;
        if (before.Length == 0) return after;
        return after.Length == 0 ? before + "\n" : before + "\n\n" + after;
    }
}
