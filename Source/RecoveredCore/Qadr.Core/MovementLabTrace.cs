using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Qadr.Core;

/// <summary>Opt-in numeric diagnostics for the guided test; never blocks pointing.</summary>
internal static class MovementLabTrace
{
    private static volatile bool stopped;
    private static readonly Channel<object>? queue=Create();
    private static Profile? previousProfile;
    private static Settings? previousSettings;
    private static RestNoiseModel? previousModel;
    private static double previousWidth,previousHeight;
    private static double checkedAt=double.NegativeInfinity;
    private static bool testing;
    public static bool MovementOnly
    {
        get
        {
            if(queue is null||stopped)return false;
            double now=Arguments.Now;
            if(now-checkedAt>=.1)
            {
                checkedAt=now;
                string? marker=Environment.GetEnvironmentVariable("QADR_MOVEMENT_LAB_ACTIVE");
                testing=!string.IsNullOrEmpty(marker)&&File.Exists(marker);
            }
            return testing;
        }
    }
    private static Channel<object>? Create()
    {
        string? path=Environment.GetEnvironmentVariable("QADR_MOVEMENT_TRACE");
        if(string.IsNullOrWhiteSpace(path))return null;
        try
        {
            path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var channel=Channel.CreateBounded<object>(new BoundedChannelOptions(2048){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
            _=Task.Run(async()=>
            {
                try
                {
                    using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite);
                    using var writer=new StreamWriter(stream){AutoFlush=true};
                    string controllerHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MotionController).Assembly.Location)));
                    await writer.WriteLineAsync(Protocol.Encode(new{kind="session",schema=2,numericOnly=true,clock="QPC seconds",createdUtc=DateTime.UtcNow,
                        controllerRevision="analog-wave-3",controllerSha256=controllerHash,movementModel="capture-timed relative vector with independent axis noise bands"}));
                    await foreach(var item in channel.Reader.ReadAllAsync())
                    {
                        if(stream.Length>64*1024*1024||File.Exists(path+".stop"))break;
                        await writer.WriteLineAsync(JsonSerializer.Serialize(item,Protocol.Json));
                    }
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException) { }
                finally {stopped=true;channel.Writer.TryComplete();}
            });
            return channel;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return null;}
    }
    public static void Record(Observation observation,PointerOutput output,Profile? profile,Settings settings,RestNoiseModel personalModel,double width,double height)
    {
        if(queue is null||stopped)return;
        if(!ReferenceEquals(previousProfile,profile)||!ReferenceEquals(previousSettings,settings)||!ReferenceEquals(previousModel,personalModel)||previousWidth!=width||previousHeight!=height)
        {
            queue.Writer.TryWrite(new{kind="configuration",time=observation.Timestamp,profile,settings,personalModel,width,height});
            previousProfile=profile;previousSettings=settings;
            previousModel=personalModel;previousWidth=width;previousHeight=height;
        }
        queue.Writer.TryWrite(new{kind="sample",time=observation.Timestamp,received=Arguments.Now,face=observation,output});
    }
    public static void RecordPose(VisionResult result,FaceGeometry geometry)
    {
        if(queue is null||stopped)return;
        queue.Writer.TryWrite(new{kind="pose",time=result.Captured,received=Arguments.Now,sequence=result.Sequence,
            inferenceMs=double.IsFinite(result.InferenceMs)?(double?)result.InferenceMs:null,matrix=result.Matrix,geometry});
    }
}
