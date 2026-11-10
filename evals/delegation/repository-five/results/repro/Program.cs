using AgentHarness;
using System.Net;
using System.Text.Json;
var root=Path.Combine(Path.GetTempPath(),"orchestra-manual-contract-"+Guid.NewGuid().ToString("N"));
var handler=new CountingHandler();
var app=new HarnessApp(Path.Combine(root,"home"),Path.Combine(root,"state"),new HttpClient(handler),Path.Combine(root,"fixture-executable"),"fixture-rid",(_,_)=>throw new Exception("Replacement forbidden"));
var stdout=new StringWriter();var stderr=new StringWriter();var oldOut=Console.Out;var oldErr=Console.Error;int exit;
try { Console.SetOut(stdout);Console.SetError(stderr);exit=app.Run(new[]{"update","--tools","--json"}); }
finally {Console.SetOut(oldOut);Console.SetError(oldErr);if(Directory.Exists(root))Directory.Delete(root,true);}
Console.WriteLine(JsonSerializer.Serialize(new{exit,requests=handler.Requests,output=stdout.ToString(),error=stderr.ToString()}));
if(exit!=2 || handler.Requests!=0 || stdout.ToString()!="") Environment.ExitCode=1;
sealed class CountingHandler:HttpMessageHandler {
 public int Requests {get;private set;}
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {Requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));}
}
