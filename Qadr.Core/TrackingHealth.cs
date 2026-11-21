namespace Qadr.Core;

/// <summary>Bounded recent tracking evidence, not a face-identity or accuracy score.
/// This observer must never modify pointer gain, gesture thresholds or the stop gate.</summary>
public sealed record TrackingHealthStatus(string Code,string Message,int Samples,int AcceptedPercent,double? CaptureAgeP95Ms,double? InferenceP95Ms);
public sealed class TrackingHealth
{
    readonly Queue<(double Time,bool Face,bool Accepted,double Age,double Inference)> samples=new();
    double latestTime=double.NegativeInfinity,cachedAt=double.NegativeInfinity;
    TrackingHealthStatus? cached;
    public void Reset(){samples.Clear();latestTime=cachedAt=double.NegativeInfinity;cached=null;}
    public void Feed(double now,bool face,bool accepted,double ageMs,double inferenceMs){
        if(!double.IsFinite(now)||!double.IsFinite(ageMs)||!double.IsFinite(inferenceMs))return;
        if(now<latestTime)Reset();
        if(samples.Count==0){cached=null;cachedAt=double.NegativeInfinity;}
        latestTime=now;
        samples.Enqueue((now,face,accepted,Math.Clamp(ageMs,0,60000),Math.Clamp(inferenceMs,0,60000)));
        Trim(now);
    }
    void Trim(double now){while(samples.Count>180||samples.Count>0&&now-samples.Peek().Time>3)samples.Dequeue();}
    public TrackingHealthStatus Status(double now){
        if(!double.IsFinite(now))return new("waiting","Waiting for tracking measurements",0,0,null,null);
        // Status may publish frequently. Sort bounded timing samples at most 4x/s.
        if(cached is not null&&now>=cachedAt&&now-cachedAt<.25)return cached;
        cachedAt=now;
        Trim(now);
        if(samples.Count==0)return cached=new("waiting","Waiting for tracking measurements",0,0,null,null);
        int accepted=(int)Math.Round(samples.Count(x=>x.Accepted)*100d/samples.Count);
        double P95(Func<(double Time,bool Face,bool Accepted,double Age,double Inference),double> channel)=>samples.Select(channel).Order().ElementAt((int)Math.Ceiling(samples.Count*.95)-1);
        double age=P95(x=>x.Age),inference=P95(x=>x.Inference);
        (string Code,string Message) result=now-latestTime>.25?("stale","Tracking paused: waiting for fresh camera results"):
            samples.Count<10?("measuring","Checking your camera and tracking"):
            samples.Count(x=>!x.Face)*5>=samples.Count?("framing","Face tracking is interrupted. Keep your whole face visible and check camera placement"):
            accepted<80?("unstable","Tracking is settling. Rest comfortably; check framing if pauses continue"):
            age>=150?("delayed","Camera results are delayed. Try a lower preview rate or close other camera apps"):
            latestTime-samples.Peek().Time>=1&&(samples.Count-1)/(latestTime-samples.Peek().Time)<20?("low-rate","Tracking updates are infrequent. Check camera frame rate; try 30 FPS tracking if 60 is unreliable"):
            ("stable","Tracking is stable. Small comfortable movements are enough");
        return cached=new(result.Code,result.Message,samples.Count,accepted,age,inference);
    }
}

/// <summary>Throttle display decoding only; never queue or throttle pointer observations.</summary>
public sealed class PreviewSchedule
{
    double last=double.NegativeInfinity;
    public bool Due(double now,int fps){
        if(!double.IsFinite(now))return false;
        if(now<last)last=double.NegativeInfinity;
        int rate=fps is 15 or 60?fps:30;
        if(now-last<1d/rate)return false;
        last=now;return true;
    }
}
