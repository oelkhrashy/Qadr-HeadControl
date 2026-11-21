using System.Diagnostics;
using System.Text.Json;
using Qadr.Core;

static class PointerBenchmark
{
    static double sink;
    const int Iterations = 60000;

    static MotionController Controller(string click,string algorithm) {
        var controller = new MotionController {Settings = new() {ClickMethod=click,RightClickMethod="none",MovementAlgorithm=algorithm}};
        controller.SetProfile(new() {CoordinateVersion=3});
        return controller;
    }

    static object MeasureMotion(string click,string algorithm) {
        var observations=Enumerable.Range(0,Iterations).Select(i=>new Observation(Math.Sin(i*.003)*.08,Math.Cos(i*.003)*.04,0,0,0,i/30.0)).ToArray();
        var warm=Controller(click,algorithm);
        for(int i=0;i<2000;i++)sink+=warm.Feed(observations[i],500,400).Dx;
        var timings=new List<double>();var allocations=new List<double>();
        for(int pass=0;pass<5;pass++) {
            var controller=Controller(click,algorithm);
            long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            foreach(var observation in observations)sink+=controller.Feed(observation,500,400).Dx;
            watch.Stop();
            timings.Add(watch.Elapsed.TotalMilliseconds);
            allocations.Add((double)(GC.GetAllocatedBytesForCurrentThread()-allocated)/Iterations);
        }
        return new{movementAlgorithm=algorithm,clickMethod=click,iterations=Iterations,medianTotalMs=timings.Order().ElementAt(2),medianBytesPerFeed=allocations.Order().ElementAt(2)};
    }

    public static int Run() {
        Console.WriteLine(Protocol.Encode(new{synthetic=true,motion=new[]{MeasureMotion("none","classic"),MeasureMotion("none","adaptive"),MeasureMotion("dwell","classic"),MeasureMotion("dwell","adaptive")},notes="Controller only; five passes after warmup. Excludes camera, inference, preview, UI and Windows input."}));
        return 0;
    }

    public static int ProtocolFixture(string path) {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        string full=document.RootElement.GetProperty("full").GetRawText(),compact=document.RootElement.GetProperty("compact").GetRawText();
        var expected=Features.Extract(Protocol.Decode<VisionResult>(full));
        var actual=Features.Extract(Protocol.Decode<VisionResult>(compact));
        if(expected is null || actual is null || Math.Abs(expected.Yaw-actual.Yaw)>1e-12 || Math.Abs(expected.Pitch-actual.Pitch)>1e-12 || Math.Abs(expected.Scale-actual.Scale)>1e-12 || expected.Pucker!=actual.Pucker || expected.Eyebrow!=actual.Eyebrow || expected.LeftEye!=actual.LeftEye || expected.FaceWidth!=actual.FaceWidth || expected.FaceHeight!=actual.FaceHeight)throw new Exception("Python-to-C# geometry differs");
        object Measure(string json) {
            const int iterations=5000;
            for(int i=0;i<1000;i++)sink+=Features.Extract(Protocol.Decode<VisionResult>(json))!.Yaw;
            var timings=new List<double>();var allocations=new List<double>();
            for(int pass=0;pass<5;pass++){
                long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                for(int i=0;i<iterations;i++)sink+=Features.Extract(Protocol.Decode<VisionResult>(json))!.Yaw;
                watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);allocations.Add((double)(GC.GetAllocatedBytesForCurrentThread()-allocated)/iterations);
            }
            return new{utf8Bytes=System.Text.Encoding.UTF8.GetByteCount(json),iterations,medianDecodeAndExtractMs=timings.Order().ElementAt(2),medianBytesPerResult=allocations.Order().ElementAt(2)};
        }
        Console.WriteLine(Protocol.Encode(new{synthetic=true,pythonToCSharpEquivalent=true,full=Measure(full),compact=Measure(compact),notes="478 synthetic landmarks, 52 blendshapes, 16 matrix entries; no preview. Compares full and compact formats in the updated Engine feature code, not camera latency."}));
        return 0;
    }
}
