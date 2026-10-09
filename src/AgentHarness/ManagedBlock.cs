namespace AgentHarness;

internal enum BlockPresence { Missing, Present, Malformed }

internal static class ManagedBlock
{
    internal const string Begin = "<!-- orchestra:begin -->";
    internal const string End = "<!-- orchestra:end -->";

    internal static BlockPresence Locate(string text, out int start, out int length)
    {
        start = length = 0;
        var begin = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        if (begin < 0 && end < 0) return BlockPresence.Missing;
        var single = begin >= 0 && end > begin
            && text.IndexOf(Begin, begin + Begin.Length, StringComparison.Ordinal) < 0
            && text.IndexOf(End, end + End.Length, StringComparison.Ordinal) < 0;
        if (!single) return BlockPresence.Malformed;
        start = begin; length = end + End.Length - begin;
        return BlockPresence.Present;
    }

    internal static string Wrap(string content) => $"{Begin}\n{content.Trim()}\n{End}";

    internal static string Append(string text, string content) =>
        string.IsNullOrWhiteSpace(text) ? Wrap(content) + "\n" : text.TrimEnd() + "\n\n" + Wrap(content) + "\n";

    internal static string Replace(string text, int start, int length, string content) =>
        text[..start] + Wrap(content) + text[(start + length)..];

    internal static string Remove(string text, int start, int length)
    {
        var before = text[..start].TrimEnd();
        var after = text[(start + length)..].TrimStart();
        if (before.Length == 0) return after;
        return after.Length == 0 ? before + "\n" : before + "\n\n" + after;
    }
}
