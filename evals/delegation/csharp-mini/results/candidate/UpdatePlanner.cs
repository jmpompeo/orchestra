using System.Security.Cryptography;
public enum InstallAction { Create, Unchanged, Update, Conflict, BackupUpdate }
public static class UpdatePlanner
{
    public static InstallAction Decide(byte[]? existing, byte[] desired, string? ownedSha256, bool backup = false)
    {
        ArgumentNullException.ThrowIfNull(desired);

        if (existing is null)
            return InstallAction.Create;

        if (existing.AsSpan().SequenceEqual(desired))
            return InstallAction.Unchanged;

        if (string.Equals(Convert.ToHexString(SHA256.HashData(existing)), ownedSha256,
                StringComparison.OrdinalIgnoreCase))
            return InstallAction.Update;

        return backup ? InstallAction.BackupUpdate : InstallAction.Conflict;
    }
}
