using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Channels;
using Qadr.Core;

namespace Qadr.Shell;

internal sealed class EngineConnection : IDisposable
{
    readonly string session="Qadr-"+Guid.NewGuid().ToString("N");
    readonly Channel<Command> outgoing=Channel.CreateBounded<Command>(32);
    readonly CancellationTokenSource stop=new();
    Process? guardian;
    Task? connectionTask;
    SharedTransport? shared;
    SafetyGate? gate;
    public EngineStatus? Latest;
    public string? Error;
    public void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Qadr.Guardian.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
        foreach(string s in new[]{"--session",session,"--data-dir",Arguments.DataDirectory(args),"--shell-pid",Environment.ProcessId.ToString()})info.ArgumentList.Add(s);
        if(args.Contains("--offer-tutorial"))info.ArgumentList.Add("--offer-tutorial");
        foreach(string flag in new[]{"--no-input","--test-source"})if(args.Contains(flag))info.ArgumentList.Add(flag);
        string? python=Arguments.Get(args,"--python");if(python is not null){info.ArgumentList.Add("--python");info.ArgumentList.Add(python);}
        guardian=Process.Start(info)??throw new IOException("Qadr's stop controller could not start.");
        connectionTask=Task.Run(Connect);
    }
    async Task Connect()
    {
        try {
            using var pipe=new NamedPipeClientStream(".",session+".engine",PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(30000,stop.Token);gate=new SafetyGate(session,false);
            using var reader=new StreamReader(pipe);using var writer=new StreamWriter(pipe){AutoFlush=true};
            var send=Task.Run(async()=>{await foreach(var command in outgoing.Reader.ReadAllAsync(stop.Token))await writer.WriteLineAsync(Protocol.Encode(command));},stop.Token);
            while(!stop.IsCancellationRequested && await reader.ReadLineAsync(stop.Token) is string line) {
                if(line.Length>1500000)throw new InvalidDataException("Status message too large");
                var status=Protocol.Decode<EngineStatus>(line);if(status.Version!=Protocol.Version)throw new InvalidDataException("Status protocol version mismatch");Volatile.Write(ref Latest,status);
                if(send.IsFaulted)await send;
            }
            if(!stop.IsCancellationRequested)Error="Control stopped because the Engine disconnected. Close Qadr and reopen it.";
        }catch(UnauthorizedAccessException){await SharedFallback();}
        catch(Exception e) when(e is IOException or TimeoutException or OperationCanceledException or System.Text.Json.JsonException){if(!stop.IsCancellationRequested)Error="Qadr could not connect: "+e.Message;}
        finally{gate?.Stop();}
    }
    async Task SharedFallback()
    {
        try {
            long started=Environment.TickCount64;
            while(shared is null && !stop.IsCancellationRequested){try{gate??=new SafetyGate(session,false);shared=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-started>30000)throw;await Task.Delay(50,stop.Token);}}
            if(shared is null)return;gate!.SharedRpc=1;long sequence=0;
            while(!stop.IsCancellationRequested){while(outgoing.Reader.TryRead(out var cmd))if(!shared.Send(cmd))gate.Stop();var status=shared.Read(ref sequence);if(status is not null)Volatile.Write(ref Latest,status);await Task.Delay(15,stop.Token);}
        }catch(Exception e) when(e is IOException or OperationCanceledException){if(!stop.IsCancellationRequested)Error="Qadr could not connect: "+e.Message;}
    }
    public void Send(Command command){if(command.Type=="stop")gate?.UserStop();command=command with{UserStopEpoch=gate?.UserStops};if(!outgoing.Writer.TryWrite(command))gate?.Stop();}
    public void Dispose(){gate?.Stop();if(gate is not null)gate.Closing=1;stop.Cancel();if(guardian is {HasExited:false} && !guardian.WaitForExit(1000))guardian.Kill(true);guardian?.Dispose();
        try{if(connectionTask?.Wait(800)!=false){shared?.Dispose();gate?.Dispose();}}catch(AggregateException){ }
        stop.Dispose();}
}
