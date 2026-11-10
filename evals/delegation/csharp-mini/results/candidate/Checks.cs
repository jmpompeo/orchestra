using System.Security.Cryptography;

static void Check(string name, InstallAction expected, byte[]? existing, byte[] desired,
    string? ownedSha256, bool backup = false)
{
    byte[]? existingBefore = existing?.ToArray();
    byte[] desiredBefore = desired.ToArray();
    InstallAction actual = UpdatePlanner.Decide(existing, desired, ownedSha256, backup);

    if (actual != expected)
        throw new Exception($"{name}: expected {expected}, got {actual}");
    if (existing is not null && !existing.AsSpan().SequenceEqual(existingBefore))
        throw new Exception($"{name}: existing bytes changed");
    if (!desired.AsSpan().SequenceEqual(desiredBefore))
        throw new Exception($"{name}: desired bytes changed");
}

byte[] existing = [1, 2, 3];
byte[] desired = [3, 2, 1];
string ownedHash = Convert.ToHexString(SHA256.HashData(existing));
string otherHash = Convert.ToHexString(SHA256.HashData(desired));

try
{
    UpdatePlanner.Decide(null, null!, null);
    throw new Exception("Null desired must throw before Create");
}
catch (ArgumentNullException ex) when (ex.ParamName == "desired")
{
}

Check("Create", InstallAction.Create, null, desired, ownedHash, backup: true);
Check("Empty arrays are unchanged", InstallAction.Unchanged, [], [], null);
Check("Unchanged without ownership", InstallAction.Unchanged, existing, [1, 2, 3], null);
Check("Unchanged with backup", InstallAction.Unchanged, existing, [1, 2, 3], otherHash, backup: true);
Check("Update with uppercase hash", InstallAction.Update, existing, desired, ownedHash);
Check("Update with lowercase hash and backup", InstallAction.Update, existing, desired,
    ownedHash.ToLowerInvariant(), backup: true);
Check("Conflict without ownership", InstallAction.Conflict, existing, desired, null);
Check("Conflict with wrong hash", InstallAction.Conflict, existing, desired, otherHash);
Check("Conflict with invalid hash", InstallAction.Conflict, existing, desired, "not-a-sha256");
Check("Conflict with non-hex hash", InstallAction.Conflict, existing, desired, new string('g', 64));
Check("Backup update without ownership", InstallAction.BackupUpdate, existing, desired, null, backup: true);
Check("Backup update with invalid hash", InstallAction.BackupUpdate, existing, desired,
    "not-a-sha256", backup: true);

Console.WriteLine("All UpdatePlanner checks passed");
