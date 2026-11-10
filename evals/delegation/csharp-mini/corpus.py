"""One tiny C# coding task; Python only orchestrates the existing native runner."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

CHECK = 'dotnet run --project MiniEval.csproj --configuration Release'
PROJECT = '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup>
  <ItemGroup><Compile Include="UpdatePlanner.cs"/><Compile Include="Checks.cs"/></ItemGroup>
</Project>
'''
HEADER = '''using System.Security.Cryptography;
public enum InstallAction { Create, Unchanged, Update, Conflict, BackupUpdate }
public static class UpdatePlanner
{
'''
REFERENCE = HEADER + '''    public static InstallAction Decide(byte[]? existing, byte[] desired, string? ownedSha256, bool backup = false)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (existing is null) return InstallAction.Create;
        if (existing.AsSpan().SequenceEqual(desired)) return InstallAction.Unchanged;
        var digest = Convert.ToHexString(SHA256.HashData(existing));
        if (string.Equals(digest, ownedSha256, StringComparison.OrdinalIgnoreCase)) return InstallAction.Update;
        return backup ? InstallAction.BackupUpdate : InstallAction.Conflict;
    }
}
'''
SEED = HEADER + '''    public static InstallAction Decide(byte[]? existing, byte[] desired, string? ownedSha256, bool backup = false)
        => existing is null ? InstallAction.Create : InstallAction.Update;
}
'''
SMOKE = '''if (UpdatePlanner.Decide(null, new byte[] { 1 }, null) != InstallAction.Create)
    throw new Exception("Missing file must be created");
Console.WriteLine("Smoke passed");
'''
PROMPT = '''Implement the pure C# UpdatePlanner.Decide method in UpdatePlanner.cs and add runnable behavior checks in Checks.cs. Keep the public signature and enum unchanged. Only edit those two files; no dependencies, project/config changes, filesystem operations, commits or pushes.
Rules in precedence order: null desired throws ArgumentNullException; null existing means Create; identical bytes mean Unchanged regardless of ownership or backup; otherwise SHA256(existing) matching ownedSha256 (case-insensitive hexadecimal) means Update; otherwise backup=true means BackupUpdate and backup=false means Conflict. A missing/invalid ownership hash must not authorize Update. Do not mutate either input array. This is a small localized task: use proportionate process.
Run: dotnet run --project MiniEval.csproj --configuration Release
'''
CASES = {'update-plan': {'coding': True, 'prompt': PROMPT, 'allowed_files': ['UpdatePlanner.cs', 'Checks.cs']}}
GROUPS = {
    'missing': 'Check(UpdatePlanner.Decide(null, desired, null) == InstallAction.Create); Check(UpdatePlanner.Decide(null, desired, "bad", true) == InstallAction.Create);',
    'identical': 'Check(UpdatePlanner.Decide(desired.ToArray(), desired, null) == InstallAction.Unchanged); Check(UpdatePlanner.Decide(desired.ToArray(), desired, "bad", true) == InstallAction.Unchanged); Check(UpdatePlanner.Decide(Array.Empty<byte>(), Array.Empty<byte>(), null) == InstallAction.Unchanged);',
    'owned': 'var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(old)); Check(UpdatePlanner.Decide(old, desired, hash.ToLowerInvariant()) == InstallAction.Update); Check(UpdatePlanner.Decide(old, desired, hash, true) == InstallAction.Update);',
    'conflicts': 'foreach (var hash in new string?[] { null, "", "bad", new string(\'0\',64) }) { Check(UpdatePlanner.Decide(old, desired, hash) == InstallAction.Conflict); Check(UpdatePlanner.Decide(old, desired, hash, true) == InstallAction.BackupUpdate); }',
    'invalid_and_immutable': 'try { UpdatePlanner.Decide(old, null!, null); throw new Exception("Expected ArgumentNullException"); } catch (ArgumentNullException) {} var oldCopy=old.ToArray(); var desiredCopy=desired.ToArray(); foreach(var backup in new[]{false,true}) UpdatePlanner.Decide(old,desired,null,backup); Check(old.SequenceEqual(oldCopy)); Check(desired.SequenceEqual(desiredCopy));',
}
TEST_PREFIX = '''byte[] old = { 1, 2, 3 }; byte[] desired = { 3, 2, 1 };
void Check(bool condition) { if (!condition) throw new Exception("Held-out behavior failed"); }
'''
MUTANTS = {
    'identical': REFERENCE.replace('return InstallAction.Unchanged;', 'return InstallAction.Update;'),
    'ownership': REFERENCE.replace('return InstallAction.Update;', 'return InstallAction.Conflict;'),
    'backup': REFERENCE.replace('backup ? InstallAction.BackupUpdate : InstallAction.Conflict', 'InstallAction.Conflict'),
    'modified': REFERENCE.replace('backup ? InstallAction.BackupUpdate : InstallAction.Conflict', 'backup ? InstallAction.BackupUpdate : InstallAction.Update'),
}


def prepare_case(case_id, dest):
    assert case_id in CASES
    dest = Path(dest); dest.mkdir(parents=True, exist_ok=True)
    for name, body in {'MiniEval.csproj': PROJECT, 'UpdatePlanner.cs': SEED, 'Checks.cs': SMOKE,
                       '.gitignore': 'bin/\nobj/\n', 'TASK.md': PROMPT,
                       'docs/agent-context.md': '# Mini C# fixture\n.NET 10, no packages. Pure in-memory planning.\nFast check: `' + CHECK + '`\n'}.items():
        path=dest/name; path.parent.mkdir(exist_ok=True,parents=True);path.write_text(body)


def run_check(work, log):
    result=subprocess.run(CHECK.split(),cwd=work,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=60)
    Path(log).write_text(result.stdout)
    return result.returncode


def grade_case(case_id, workspace, report_dir):
    started=time.monotonic(); workspace=Path(workspace).resolve(); report_dir=Path(report_dir).resolve()
    if workspace==report_dir or workspace in report_dir.parents: raise ValueError('Private grader must be outside workspace')
    report_dir.mkdir(parents=True,exist_ok=True)
    def git(*args): return subprocess.check_output(['git','-C',str(workspace),*args],text=True).split('\0')
    changed=set(git('diff','--name-only','HEAD','-z'))|set(git('ls-files','--others','--exclude-standard','-z'));changed.discard('')
    allowed={'UpdatePlanner.cs','Checks.cs'}
    scope=changed<=allowed and all((workspace/n).is_file() and not (workspace/n).is_symlink() for n in allowed)
    authored='Checks.cs' in changed
    groups={};mutants={}; authored_pass=False
    with tempfile.TemporaryDirectory(dir=report_dir) as temp:
        work=Path(temp);(work/'MiniEval.csproj').write_text(PROJECT)
        source=(workspace/'UpdatePlanner.cs').read_text() if scope else SEED
        checks=(workspace/'Checks.cs').read_text() if scope else SMOKE
        (work/'UpdatePlanner.cs').write_text(source)
        for name,body in GROUPS.items():
            (work/'Checks.cs').write_text(TEST_PREFIX+body)
            groups[name]=run_check(work,report_dir/(name+'.log'))==0
        (work/'Checks.cs').write_text(checks)
        authored_pass=run_check(work,report_dir/'authored.log')==0
        validation=time.monotonic()-started; grading_start=time.monotonic()
        # Build every fault first: a compiler failure never earns mutation credit.
        for name,body in MUTANTS.items():
            (work/'UpdatePlanner.cs').write_text(body)
            built=subprocess.run(['dotnet','build','MiniEval.csproj','-c','Release','--no-restore'],cwd=work,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=60).returncode==0
            killed=built and run_check(work,report_dir/('mutant-'+name+'.log'))!=0
            mutants[name]=bool(authored and authored_pass and killed)
        grading=time.monotonic()-grading_start
    critical=scope and all(groups.values())
    result={'functional_score':70*sum(groups.values())/len(groups),'regression_score':20*sum(mutants.values())/len(mutants),
            'critical_pass':critical,'accepted':critical and authored and authored_pass,'functional_groups':groups,'mutants':mutants,
            'validation_seconds':validation,'grading_seconds':grading,'scope_ok':scope,'authored_tests_pass':authored and authored_pass}
    (report_dir/'grade.json').write_text(json.dumps(result,indent=2));return result
