using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Winicode;

internal sealed class InstanceChannel : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private Task? server;
    internal static string UserIdentity
    {
        get
        {
            var sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..20];
        }
    }
    internal static string MutexName=>"Local\\Winicode.Input."+UserIdentity;
    private static string PipeName=>"Winicode.Command."+UserIdentity+"."+System.Diagnostics.Process.GetCurrentProcess().SessionId;
    internal void Start(string baseDirectory,Func<string,Task> handler)
    {
        server=Task.Run(async()=>
        {
            while(!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,256,true);
                    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),256,true){AutoFlush=true};
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var command=await ReadCommand(reader,timeout.Token);
                    var parts=command?.Split('\t',2);
                    if(parts?.Length==2&&parts[0] is "settings" or "pause" or "resume" or "exit")
                    {
                        if(parts[0]!="settings"&&!Path.GetFullPath(parts[1]).TrimEnd('\\').Equals(Path.GetFullPath(baseDirectory).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
                        {await writer.WriteLineAsync("OTHER_INSTALL");continue;}
                        await handler(parts[0]);await writer.WriteLineAsync("OK");
                    }
                    else await writer.WriteLineAsync("INVALID");
                }
                catch(OperationCanceledException){}
                catch(IOException){await Task.Delay(100);}
                catch(UnauthorizedAccessException){break;}
                catch(ArgumentException){}
            }
        });
    }
    private static async Task<string?> ReadCommand(StreamReader reader,CancellationToken token)
    {
        var chars=new char[1];var value=new StringBuilder();
        while(value.Length<1024)
        {
            var count=await reader.ReadAsync(chars.AsMemory(),token);
            if(count==0)return null;
            if(chars[0]=='\n')return value.ToString().TrimEnd('\r');
            value.Append(chars[0]);
        }
        return null;
    }
    internal static async Task<bool> SendAsync(string command,TimeSpan? timeout=null)
    {
        try
        {
            using var cts=new CancellationTokenSource(timeout??TimeSpan.FromSeconds(3));
            using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cts.Token);
            using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,256,true);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),256,true){AutoFlush=true};
            await writer.WriteLineAsync((command+"\t"+Path.GetFullPath(AppContext.BaseDirectory)).AsMemory(),cts.Token);
            return await reader.ReadLineAsync(cts.Token)=="OK";
        }
        catch(OperationCanceledException){return false;}
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }
    public void Dispose(){stop.Cancel();}
}
