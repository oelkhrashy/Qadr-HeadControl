using System.IO.MemoryMappedFiles;
using Qadr.Core;
namespace Qadr.Engine;
internal sealed class CameraCapture : IAsyncDisposable
{
    readonly MemoryMappedFile memory;
    readonly MemoryMappedViewAccessor view;
    readonly object sync=new();
    MediaFoundation? capture;
    long sequence;
    double lastFrame,measuredAt,actualFps;
    int measuredFrames;
    public double ActualFps=>Volatile.Read(ref actualFps);
    public double NominalFps=>capture?.NominalFps??0;
    string? error;
    CancellationTokenSource? testFrames;
    public double LastFrame=>Volatile.Read(ref lastFrame);
    public string? Error=>Volatile.Read(ref error);
    public CameraCapture(string session){memory=MemoryMappedFile.CreateNew(session+".frames",64+640*480*4);view=memory.CreateViewAccessor();}
    public static Task<CameraDevice[]> Discover()=>Task.Run(MediaFoundation.Discover);
    public async Task Start(string id,int targetFps=30)
    {
        await Task.Run(()=>capture?.Dispose());capture=null;Volatile.Write(ref error,null);
        lock(sync){view.Write(0,0L);sequence=0;measuredAt=0;measuredFrames=0;Volatile.Write(ref actualFps,0);}
        capture=new MediaFoundation(id,Write,message=>Volatile.Write(ref error,message),targetFps);
        await capture.Ready.WaitAsync(TimeSpan.FromSeconds(12));Volatile.Write(ref lastFrame,Arguments.Now);
    }
    public void StartTestFrames(int targetFps=30)
    {
        testFrames?.Cancel();testFrames=new();var token=testFrames.Token;byte[] blank=new byte[640*480*4];
        _=Task.Run(async()=>{while(!token.IsCancellationRequested){Write(blank,640,480);try{await Task.Delay(TimeSpan.FromSeconds(1d/(targetFps==60?60:30)),token);}catch(OperationCanceledException){break;}}});
    }
    void Write(byte[] pixels,int width,int height)
    {
        lock(sync){double now=Arguments.Now;if(measuredAt==0)measuredAt=now;measuredFrames++;if(now-measuredAt>=1){Volatile.Write(ref actualFps,measuredFrames/(now-measuredAt));measuredAt=now;measuredFrames=0;}
            view.Write(0,++sequence);Thread.MemoryBarrier();view.Write(8,width);view.Write(12,height);view.Write(16,width*4);view.Write(24,Arguments.Now);
            view.WriteArray(64,pixels,0,pixels.Length);Thread.MemoryBarrier();view.Write(0,++sequence);Volatile.Write(ref lastFrame,Arguments.Now);}
    }
    public async ValueTask DisposeAsync(){testFrames?.Cancel();await Task.Run(()=>capture?.Dispose());lock(sync){view.Dispose();memory.Dispose();}}
}
