namespace Qadr.Core;

/// <summary>Learned hold-and-release action with cooldown, gap rejection and conflict cancellation.</summary>
public sealed class ReleaseGesture
{
    double? since,last;
    double firedAt=double.NegativeInfinity;
    bool qualified,armed;
    double? neutralSince;
    public bool Active {get;private set;}
    void Clear(){since=neutralSince=null;armed=qualified=Active=false;}
    public bool Feed(double score,double threshold,double rest,double hold,double time,bool enabled,bool conflict=false){
        if(!enabled||conflict||!double.IsFinite(score)||!double.IsFinite(time)){Clear();last=null;return false;}
        if(last.HasValue&&time<=last.Value)return false;
        if(last.HasValue&&time-last.Value>TrackingTiming.MaximumGapSeconds)Clear();last=time;
        double release=rest+(threshold-rest)*.7;
        if(score<release){neutralSince??=time;if(time-neutralSince>=.06)armed=true;}else neutralSince=null;
        if(!armed)return false;
        if(since.HasValue&&time-since.Value>1.5){Clear();return false;}
        if(score>=threshold){since??=time;Active=true;qualified|=time-since>=hold;return false;}
        if(since.HasValue&&score>=rest+(threshold-rest)*.7)return false;
        if(!since.HasValue)return false;
        bool fire=qualified&&time-firedAt>=.8;Clear();if(fire)firedAt=time;return fire;
    }
}
/// <summary>Vertical scrolling around the user's entry pose; no pointer motion or action selection.</summary>
public sealed class HeadScrollController
{
    public bool Active {get;private set;}
    public double AnchorX {get;private set;}
    public double AnchorY {get;private set;}
    double neutral,lastTime,lastWheel;
    public void Start(Observation o,Profile profile,double x,double y){Active=true;neutral=profile.Signal(o,1);lastTime=o.Timestamp;lastWheel=double.NegativeInfinity;AnchorX=x;AnchorY=y;}
    public void Reset()=>Active=false;
    public int Feed(Observation o,Profile profile,Settings settings,bool gestureActive=false){
        if(!Active)return 0;
        if(!o.IsUsable){Reset();return 0;}
        if(o.Timestamp<=lastTime)return 0;
        if(o.Timestamp-lastTime>TrackingTiming.MaximumGapSeconds){Reset();return 0;}lastTime=o.Timestamp;
        if(gestureActive)return 0;
        double range=Math.Min(profile.Ranges[1][0],profile.Ranges[1][1]);
        double offset=profile.Signal(o,1)-neutral,band=Math.Max(range*.18,profile.Noise[1]*4);
        if(Math.Abs(offset)<band){lastWheel=double.NegativeInfinity;return 0;}
        if(o.Timestamp-lastWheel<settings.ScrollRepeatSeconds)return 0;
        lastWheel=o.Timestamp;return offset<0?settings.ScrollNotches:-settings.ScrollNotches;
    }
}
