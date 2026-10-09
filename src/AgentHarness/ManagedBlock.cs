namespace AgentHarness;

internal enum BlockPresence { Missing, Present, Malformed }

internal static class ManagedBlock
{
    internal const string Begin = "<!-- orchestra:begin -->";
    internal const string End = "<!-- orchestra:end -->";
    private const string SeparatorPrefix = "<!-- orchestra:separator=";

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

    internal static string Wrap(string content) => Wrap(content, "\n", null);

    internal static string Append(string text, string content)
    {
        var eol = LineEnding(text);
        // A second newline separates an existing final line from the block.
        // Record exactly how many newlines we add so removal can restore the prefix.
        var separatorCount = text.Length == 0 ? 0 : text.EndsWith('\n') ? 1 : 2;
        return text + string.Concat(Enumerable.Repeat(eol, separatorCount))
            + Wrap(content, eol, separatorCount) + eol;
    }

    internal static string Replace(string text, int start, int length, string content)
    {
        var eol = BlockLineEnding(text, start);
        var separatorCount = SeparatorCount(text, start, length, eol);
        return text[..start] + Wrap(content, eol, separatorCount) + text[(start + length)..];
    }

    internal static string Remove(string text, int start, int length)
    {
        var eol = BlockLineEnding(text, start);
        var separatorCount = SeparatorCount(text, start, length, eol);
        var before = text[..start];
        if (separatorCount is int count && count > 0)
        {
            var separator = string.Concat(Enumerable.Repeat(eol, count));
            if (before.EndsWith(separator, StringComparison.Ordinal)) before = before[..^separator.Length];
        }
        var after = text[(start + length)..];
        after = after.StartsWith("\r\n", StringComparison.Ordinal) ? after[2..] : after.StartsWith('\n') ? after[1..] : after;
        return before + after;
    }

    internal static bool PreservesEmptyFile(string text, int start, int length) =>
        SeparatorCount(text, start, length, BlockLineEnding(text, start)) == 0;

    private static string Wrap(string content, string eol, int? separatorCount)
    {
        var metadata = separatorCount is int count ? $"{SeparatorPrefix}{count} -->{eol}" : "";
        var body = content.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);
        return $"{Begin}{eol}{metadata}{body}{eol}{End}";
    }

    private static string LineEnding(string text)
    {
        var last = text.LastIndexOf('\n');
        return last > 0 && text[last - 1] == '\r' ? "\r\n" : "\n";
    }

    private static string BlockLineEnding(string text, int start) =>
        text.AsSpan(start + Begin.Length).StartsWith("\r\n") ? "\r\n" : "\n";

    private static int? SeparatorCount(string text, int start, int length, string eol)
    {
        var firstLine = start + Begin.Length + eol.Length;
        var end = start + length;
        for (var count = 0; count <= 2; count++)
        {
            var marker = $"{SeparatorPrefix}{count} -->{eol}";
            if (firstLine + marker.Length <= end && text.AsSpan(firstLine, marker.Length).SequenceEqual(marker)) return count;
        }
        return null; // Legacy blocks do not identify which preceding whitespace is theirs.
    }
}
