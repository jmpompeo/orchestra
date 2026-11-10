using AgentHarness;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net;

if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Reproduction uses macOS atomic path exchange.");
var root=Path.Combine(Path.GetTempPath(),"orchestra-no-follow-"+Guid.NewGuid().ToString("N"));
var home=Path.Combine(root,"home");var state=Path.Combine(root,"state");
var handler=new OfflineHandler();var app=new HarnessApp(home,state,new HttpClient(handler));
try {
 if(Capture(app,["install","--tools","codex"]).Code!=0)throw new Exception("fixture install failed");
 var target=Path.Combine(home,".codex","AGENTS.md");var alt=Path.Combine(home,".codex","swap-entry");var outside=Path.Combine(root,"outside.md");
 File.WriteAllBytes(outside,new AssetStore().ReadBytes("global/codex/AGENTS.md"));
 File.WriteAllText(target,"Regular file always differs from expected bundled bytes.");
 var regular=Capture(app,["verify-installed","--tools","codex","--json"]);
 if(Status(regular.Output,target)!="modified")throw new Exception("regular control failed");
 File.CreateSymbolicLink(alt,outside);
 if(Native.Exchange(target,alt)!=0)throw new Exception("swap control failed: "+Marshal.GetLastPInvokeError());
 var linked=Capture(app,["verify-installed","--tools","codex","--json"]);
 if(Status(linked.Output,target)!="unsafe")throw new Exception("linked control failed");
 if(Native.Exchange(target,alt)!=0)throw new Exception("reset failed");
 using var stop=new CancellationTokenSource();long exchanges=0;int swapError=0;
 var race=Task.Run(()=>{while(!stop.IsCancellationRequested){if(Native.Exchange(target,alt)!=0){swapError=Marshal.GetLastPInvokeError();break;}Interlocked.Increment(ref exchanges);}});
 var counts=new Dictionary<string,int>();var followed=false;int attempts=0;
 try {
  for(;attempts<3000;attempts++){
   var result=Capture(app,["verify-installed","--tools","codex","--json"]);
   var status=result.Code==2?"controlled-error":Status(result.Output,target);
   counts[status]=counts.GetValueOrDefault(status)+1;
   if(status=="unchanged"){followed=true;attempts++;break;}
  }
 } finally {stop.Cancel();race.GetAwaiter().GetResult();}
 Console.WriteLine(JsonSerializer.Serialize(new{regularControl="modified",linkedControl="unsafe",attempts,exchanges,swapError,statusCounts=counts,followedLink=followed,httpRequests=handler.Requests}));
 if(!followed)Environment.ExitCode=1;
} finally {if(Directory.Exists(root))Directory.Delete(root,true);}
static string Status(string text,string target){using var d=JsonDocument.Parse(text);return d.RootElement.GetProperty("files").EnumerateArray().Single(x=>x.GetProperty("path").GetString()==target).GetProperty("status").GetString()!;}
static (int Code,string Output) Capture(HarnessApp app,string[] args){var oldOut=Console.Out;var oldErr=Console.Error;using var output=new StringWriter();using var error=new StringWriter();try{Console.SetOut(output);Console.SetError(error);return(app.Run(args),output.ToString());}finally{Console.SetOut(oldOut);Console.SetError(oldErr);}}
static class Native {
 [DllImport("libSystem.B.dylib",EntryPoint="renameatx_np",SetLastError=true)]private static extern int Rename(int fromFd,string from,int toFd,string to,uint flags);
 public static int Exchange(string first,string second)=>Rename(-2,first,-2,second,2);
}
sealed class OfflineHandler:HttpMessageHandler {public int Requests;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;throw new Exception("No network allowed");}}
