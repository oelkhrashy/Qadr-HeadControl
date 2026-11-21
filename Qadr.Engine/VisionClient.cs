using System.Diagnostics;
using Qadr.Core;
using System.IO;

namespace Qadr.Engine;

internal sealed class VisionClient : IDisposable
{
    Process? process;
    readonly object writeLock=new();
    volatile bool stopping;
    public bool Exited => process is {HasExited:true};
    public event Action<VisionResult>? Result;
    public void Start(string session,string? python)
    {
        string root=AppContext.BaseDirectory;
        var info=new ProcessStartInfo(python??Path.Combine(root,"vision","Qadr.Vision.exe")) {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardInput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=root};
        info.Environment["OPENBLAS_NUM_THREADS"]="1";info.Environment["OMP_NUM_THREADS"]="2";info.Environment["MKL_NUM_THREADS"]="1";
        if(python is not null)info.ArgumentList.Add(Path.Combine(root,"vision","worker.py"));
        info.ArgumentList.Add("--compact-results");
        foreach(string s in new[]{"--session",session,"--model",Path.Combine(root,"models","face_landmarker.task"),"--speech-model",Path.Combine(root,"models","vosk-model-small-en-us-0.15")}) info.ArgumentList.Add(s);
        var child=Process.Start(info)??throw new IOException("Vision worker could not start");process=child;
        _=Task.Run(async()=>{
            try {while(!stopping && await child.StandardOutput.ReadLineAsync() is string line){
                if(line.Length>1500000)throw new InvalidDataException("Worker message too large");
                var result=Protocol.Decode<VisionResult>(line);if(result.Version!=Protocol.Version)throw new InvalidDataException("Worker protocol version mismatch");Result?.Invoke(result);
            }}catch(Exception e){if(!stopping)Result?.Invoke(new(1,"error",Text:e.Message));}
        });
        // Drain diagnostic stderr without retaining camera data or flooding logs.
        _=Task.Run(async()=>{try{while(!stopping && await child.StandardError.ReadLineAsync() is not null){}}catch(Exception e) when(e is IOException or ObjectDisposedException){}});
    }
    public void Voice(bool enabled,string[] commands,string[]? phrases=null,int userStopEpoch=0,long voiceRevision=0) {lock(writeLock){if(process is not null && !process.HasExited){process.StandardInput.WriteLine(Protocol.Encode(new {version=1,type="voice",enabled,commands,phrases,userStopEpoch,voiceRevision}));process.StandardInput.Flush();}}}
    public void ConfigureFps(int fps){lock(writeLock){if(process is not null&&!process.HasExited){process.StandardInput.WriteLine(Protocol.Encode(new{version=1,type="performance",fps=fps==60?60:30}));process.StandardInput.Flush();}}}
    public void Dispose() {
        stopping=true;
        lock(writeLock){if(process is {HasExited:false}){try{process.StandardInput.WriteLine(Protocol.Encode(new{version=1,type="shutdown"}));process.StandardInput.Close();}catch(IOException){}}}
        if(process is {HasExited:false} && !process.WaitForExit(800))process.Kill(true);process?.Dispose();process=null;
    }
}
