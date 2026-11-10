using System.Security.Cryptography;

static void Check(string name, InstallAction expected, byte[]? existing, byte[] desired,
    string? ownedSha256, bool backup = false)
{
    byte[]? existingBefore = existing?.ToArray();
    byte[] desiredBefore = desired.ToArray();
    InstallAction actual = UpdatePlanner.Decide(existing, desired, ownedSha256, backup);
    if (actual != expected)
        throw new Exception($"{name}: expected {expected}, got {actual}");
    if (existingBefore is not null && !existingBefore.AsSpan().SequenceEqual(existing))
        throw new Exception($"{name}: existing array was mutated");
    if (!desiredBefore.AsSpan().SequenceEqual(desired))
        throw new Exception($"{name}: desired array was mutated");
}

byte[] existing = { 1, 2, 3 };
byte[] desired = { 4, 5, 6 };
string owned = Convert.ToHexString(SHA256.HashData(existing));

try
{
    UpdatePlanner.Decide(null, null!, null);
    throw new Exception("Null desired must throw ArgumentNullException");
}
catch (ArgumentNullException exception) when (exception.ParamName == "desired")
{
}

Check("missing existing", InstallAction.Create, null, desired, owned, backup: true);
Check("identical despite wrong owner and backup", InstallAction.Unchanged, existing,
    existing.ToArray(), "invalid", backup: true);
Check("owned differing content", InstallAction.Update, existing, desired, owned);
Check("lowercase owner", InstallAction.Update, existing, desired, owned.ToLowerInvariant(), backup: true);
Check("missing owner", InstallAction.Conflict, existing, desired, null);
Check("invalid owner", InstallAction.Conflict, existing, desired, owned[..63]);
Check("non-hex owner", InstallAction.Conflict, existing, desired, new string('g', 64));
Check("wrong owner", InstallAction.Conflict, existing, desired, new string('0', 64));
Check("backup without owner", InstallAction.BackupUpdate, existing, desired, null, backup: true);
Check("backup with invalid owner", InstallAction.BackupUpdate, existing, desired,
    new string('g', 64), backup: true);
Check("backup with wrong owner", InstallAction.BackupUpdate, existing, desired,
    new string('0', 64), backup: true);

Console.WriteLine("All UpdatePlanner checks passed");
