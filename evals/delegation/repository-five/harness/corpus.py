"""Five isolated real-repository C# tasks. Private grader code stays outside actor workspaces."""
import fnmatch
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

HERE=Path(__file__).resolve().parent
SETTINGS=json.loads((HERE/'settings.json').read_text())
SOURCE=Path(SETTINGS['source'])
DEFINITIONS=json.loads((HERE/'cases.json').read_text())
CASES={name:{'coding':True,'prompt':case['prompt']} for name,case in DEFINITIONS.items()}
CHECK=['dotnet','run','--project','tests/AgentHarness.Tests/AgentHarness.Tests.csproj','--configuration','Release','--disable-build-servers','-p:UseSharedCompilation=false']
USINGS='''using AgentHarness;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.IO.Compression;
'''
HELPERS='''static void Check(bool condition, string message) { if (!condition) throw new Exception("CHECK: " + message); }
static (int ExitCode,string Output,string Error) Capture(HarnessApp app, string[] args)
{
    var oldOut=Console.Out; var oldErr=Console.Error;
    using var stdout=new StringWriter(); using var stderr=new StringWriter();
    try { Console.SetOut(stdout); Console.SetError(stderr); var code=app.Run(args); return(code,stdout.ToString(),stderr.ToString()); }
    finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
}
'''

OFFLINE_CLASS = """sealed class BenchmarkOfflineHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Requests++; throw new HttpRequestException("Benchmark forbids network requests"); }
}
"""


def group_block(body):
    return '''{
var root=Path.Combine(Path.GetTempPath(),"orchestra-heldout-"+Guid.NewGuid().ToString("N"));
var home=Path.Combine(root,"home"); var state=Path.Combine(root,"state"); var project=Path.Combine(root,"project");
var statePath=Path.Combine(state,"state.json"); Directory.CreateDirectory(root);
var network=new BenchmarkOfflineHandler();
var app=new HarnessApp(home,state,new HttpClient(network),Path.Combine(root,"fixture-executable"),"fixture-rid",(_,_)=>throw new Exception("Benchmark forbids executable replacement"));
try {
'''+body+'''
Check(network.Requests==0, "Command attempted network access");
} finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
}'''


def heldout_program(case):
    groups=DEFINITIONS[case]['functional_groups']
    return USINGS+'switch(args.Single()) {\n'+''.join('case '+json.dumps(name)+': '+group_block(body)+' break;\n' for name,body in groups.items())+'default: throw new Exception("Unknown group");\n}\n'+HELPERS+OFFLINE_CLASS


def apply_patch(work, patch):
    if patch:
        subprocess.run(['git','apply','--whitespace=nowarn','-'],input=patch,cwd=work,text=True,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=30)


def prepare_case(case, dest):
    dest=Path(dest);shutil.copytree(SOURCE,dest,dirs_exist_ok=True)
    (dest/'TASK.md').write_text(CASES[case]['prompt'])


def install_reference(case, work, mutant=None):
    apply_patch(work,DEFINITIONS[case]['reference_patch'])
    if mutant:apply_patch(work,DEFINITIONS[case]['mutants'][mutant])


def command(args, work, log, timeout=120):
    env=os.environ.copy();env['DOTNET_CLI_TELEMETRY_OPTOUT']='1';env['DOTNET_NOLOGO']='1'
    proc=subprocess.run(args,cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=timeout)
    Path(log).write_text(proc.stdout);return proc.returncode


def copy_submission(workspace, dest):
    def ignore(directory,names):return [n for n in names if n in ('.git','bin','obj','Bin','Obj','__pycache__')]
    shutil.copytree(workspace,dest,ignore=ignore)


def grade_case(case, workspace, report_dir):
    start=time.monotonic();workspace=Path(workspace).resolve();report_dir=Path(report_dir).resolve()
    if workspace==report_dir or workspace in report_dir.parents:raise ValueError('Private grading must be external')
    report_dir.mkdir(parents=True,exist_ok=True)
    def git(*args):return subprocess.check_output(['git','-C',str(workspace),*args],text=True).split('\0')
    changed=set(git('diff','--name-only','HEAD','-z'))|set(git('ls-files','--others','--exclude-standard','-z'));changed.discard('')
    allowed=DEFINITIONS[case]['allowed_paths']
    scope=all(any(fnmatch.fnmatchcase(p,pattern) and len(Path(p).parts)==len(Path(pattern).parts) for pattern in allowed) and (workspace/p).is_file() and not (workspace/p).is_symlink() for p in changed)
    scope=scope and all(not p.is_symlink() for folder in ['src','tests'] for p in (workspace/folder).rglob('*'))
    authored=any(p.startswith('tests/AgentHarness.Tests/') and p.endswith('.cs') for p in changed)
    groups={};mutants={};authored_pass=False
    with tempfile.TemporaryDirectory(dir=report_dir) as tmp:
        scratch=Path(tmp);work=scratch/'submission';copy_submission(workspace,work)
        held=work/'heldout';held.mkdir()
        (held/'Checks.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup><ProjectReference Include="../src/AgentHarness/AgentHarness.csproj"/></ItemGroup></Project>''')
        (held/'Program.cs').write_text(heldout_program(case))
        built=command(['dotnet','build','heldout/Checks.csproj','-c','Release','--disable-build-servers','-m:1','-p:UseSharedCompilation=false'],work,report_dir/'functional-build.log')==0
        for name in DEFINITIONS[case]['functional_groups']:
            groups[name]=built and command(['dotnet','heldout/bin/Release/net10.0/Checks.dll',name],work,report_dir/(name+'.log'))==0
        authored_pass=command(CHECK,work,report_dir/'authored.log')==0
        validation=time.monotonic()-start;scoring_start=time.monotonic()
        for name in DEFINITIONS[case]['mutants']:
            fault=scratch/name;shutil.copytree(SOURCE,fault)
            # Identical arm-specific embedded assets, authored tests, and trusted faulty production code.
            for rel in ['skills','global']:
                shutil.rmtree(fault/rel);shutil.copytree(workspace/rel,fault/rel)
            shutil.rmtree(fault/'tests');shutil.copytree(work/'tests',fault/'tests',ignore=shutil.ignore_patterns('bin','obj','Bin','Obj'))
            install_reference(case,fault,name)
            valid=command(['dotnet','build','tests/AgentHarness.Tests/AgentHarness.Tests.csproj','-c','Release','--disable-build-servers','-m:1','-p:UseSharedCompilation=false'],fault,report_dir/(name+'-build.log'))==0
            killed=valid and command(['dotnet','tests/AgentHarness.Tests/bin/Release/net10.0/AgentHarness.Tests.dll'],fault,report_dir/(name+'-run.log'))!=0
            # Assembly name for tests is project name; build validity prevents compiler errors earning credit.
            mutants[name]=bool(authored and authored_pass and killed)
        scoring=time.monotonic()-scoring_start
    critical=scope and all(groups.values())
    result={'functional_score':70*sum(groups.values())/len(groups),'regression_score':20*sum(mutants.values())/len(mutants),'critical_pass':critical,'accepted':critical and authored and authored_pass,'functional_groups':groups,'mutants':mutants,'validation_seconds':validation,'grading_seconds':scoring,'scope_ok':scope,'authored_tests_pass':authored and authored_pass}
    (report_dir/'grade.json').write_text(json.dumps(result,indent=2));return result
