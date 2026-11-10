using System.Text;

namespace AgentHarness;

internal static class ManagedInstructionFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Utf8Bom = [0xef, 0xbb, 0xbf];

    internal static bool TryRead(string path, out string text, out bool hasBom)
    {
        var bytes = File.ReadAllBytes(path);
        hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        try
        {
            text = StrictUtf8.GetString(bytes.AsSpan(hasBom ? Utf8Bom.Length : 0));
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            Console.WriteLine($"CONFLICT {path}: instruction file is not valid UTF-8; convert it to UTF-8 before re-running.");
            return false;
        }
    }

    internal static byte[] Encode(string text, bool hasBom) =>
        hasBom ? [.. Utf8Bom, .. StrictUtf8.GetBytes(text)] : StrictUtf8.GetBytes(text);

    // Never truncate user instructions: persist a sibling first, then rename it over the destination.
    internal static void WriteAtomic(string path, byte[] contents, Action<Stream, byte[]>? stageWrite = null)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".orchestra-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                if (stageWrite is null) stream.Write(contents);
                else stageWrite(stream, contents);
                stream.Flush(flushToDisk: true);
            }
            File.Replace(temporary, path, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
