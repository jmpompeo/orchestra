using AgentHarness;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.IO.Compression;
{
var root=Path.Combine(Path.GetTempPath(),"orchestra-heldout-"+Guid.NewGuid().ToString("N"));
var home=Path.Combine(root,"home"); var state=Path.Combine(root,"state"); var project=Path.Combine(root,"project");
var statePath=Path.Combine(state,"state.json"); Directory.CreateDirectory(root);
var network=new BenchmarkOfflineHandler();
var app=new HarnessApp(home,state,new HttpClient(network),Path.Combine(root,"fixture-executable"),"fixture-rid",(_,_)=>throw new Exception("Benchmark forbids executable replacement"));
try {
// Invalid arguments must be rejected before state reads, writes, downloads or replacement.
Directory.CreateDirectory(state);
File.WriteAllText(statePath, "{invalid state sentinel");
Directory.CreateDirectory(home);
File.WriteAllText(Path.Combine(home, "sentinel.txt"), "preserve me");
Directory.CreateDirectory(project);
string Snapshot() => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.Ordinal)
    .Select(path => Directory.Exists(path) ? path + "/" : path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
var invalid = new List<string[]> { new[] { "update", "--tools", "--json" } };
foreach (var commandName in new[] { "install", "uninstall", "init-project", "cursor-rules", "doctor", "update", "help", "-h", "--help" })
{
    invalid.Add(new[] { commandName, "--json" });
    invalid.Add(new[] { commandName, "--tools", "codex", "--json" });
    invalid.Add(new[] { commandName, "--json", "--tools=codex" });
}
foreach (var commandName in new[] { "install", "uninstall", "init-project", "cursor-rules", "doctor", "update", "status" })
{
    invalid.Add(new[] { commandName, "--tools", "--json" });
    invalid.Add(new[] { commandName, "--tools=--json" });
    invalid.Add(new[] { commandName, "--tools", "--backup" });
    invalid.Add(new[] { commandName, "--tools=" });
    invalid.Add(new[] { commandName, "--tools", "" });
    invalid.Add(new[] { commandName, "--tools", " " });
    invalid.Add(new[] { commandName, "--tools", " --json" });
    invalid.Add(new[] { commandName, "--tools= --json" });
    invalid.Add(new[] { commandName, "--tools" });
}
var previousDirectory = Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory(project);
try
{
    var before = Snapshot();
    foreach (var commandArgs in invalid.DistinctBy(values => JsonSerializer.Serialize(values)))
    {
        var result = Capture(app, commandArgs);
        var label = JsonSerializer.Serialize(commandArgs);
        Check(result.ExitCode == 2 && result.Output == "", "usage error before output: " + label);
        Check(network.Requests == 0, "no HTTP attempt: " + label);
        Check(result.Error.Contains("--json", StringComparison.Ordinal) || result.Error.Contains("--tools", StringComparison.Ordinal), "argument diagnostic before state load: " + label);
        Check(Snapshot() == before, "no fixture mutation: " + label);
    }
    File.WriteAllText(statePath, JsonSerializer.Serialize(StateDocument.Empty()));
    var controlsBefore = Snapshot();
    foreach (var commandArgs in new[] {
        new[] { "install", "--tools", "codex", "--dry-run" },
        new[] { "install", "--tools=codex", "--dry-run" },
        new[] { "install", "--tools", " codex ", "--dry-run" },
        new[] { "status", "--json" },
        new[] { "help", "--ignored" }
    })
    {
        var result = Capture(app, commandArgs);
        Check(result.ExitCode == 0 && result.Error == "", "valid syntax remains accepted: " + JsonSerializer.Serialize(commandArgs));
        Check(network.Requests == 0 && Snapshot() == controlsBefore, "valid read-only controls have no side effects");
    }
    Console.WriteLine("Argument boundary matrix passed (plus 5 compatibility controls): " + invalid.DistinctBy(values => JsonSerializer.Serialize(values)).Count() + " cases");
}
finally { Directory.SetCurrentDirectory(previousDirectory); }

Check(network.Requests==0, "Command attempted network access");
} finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
}static void Check(bool condition, string message) { if (!condition) throw new Exception("CHECK: " + message); }
static (int ExitCode,string Output,string Error) Capture(HarnessApp app, string[] args)
{
    var oldOut=Console.Out; var oldErr=Console.Error;
    using var stdout=new StringWriter(); using var stderr=new StringWriter();
    try { Console.SetOut(stdout); Console.SetError(stderr); var code=app.Run(args); return(code,stdout.ToString(),stderr.ToString()); }
    finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
}
sealed class BenchmarkOfflineHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Requests++; throw new HttpRequestException("Benchmark forbids network requests"); }
}
