namespace Qadr.Core;
/// <summary>Deliberate eyebrow raise followed by relaxation; one activation per cycle.</summary>
public sealed class EyebrowClick
{
    double? start,last;
    double firedAt=double.NegativeInfinity;
    bool qualified,armed;
    double? neutralSince;
    public bool Active {get;private set;}
    public double Progress {get;private set;}
    void Clear(){start=neutralSince=null;armed=qualified=Active=false;Progress=0;}
    public bool Feed(double score,Settings settings,double time,bool conflict=false){
        if(!double.IsFinite(score)||!double.IsFinite(time)||conflict||!settings.EyebrowDoubleClick||!settings.EyebrowLearned){Clear();last=null;return false;}
        if(last.HasValue&&time<=last.Value)return false;
        if(last.HasValue&&time-last.Value>TrackingTiming.MaximumGapSeconds)Clear();last=time;
        double release=settings.EyebrowRestLevel+(settings.EyebrowThreshold-settings.EyebrowRestLevel)*.7;
        if(score<release){neutralSince??=time;if(time-neutralSince>=.06)armed=true;}else neutralSince=null;
        if(!armed)return false;
        if(start.HasValue&&time-start.Value>1.5){Clear();return false;}
        if(score>=settings.EyebrowThreshold){start??=time;Active=true;Progress=Math.Clamp((time-start.Value)/settings.EyebrowHold,0,1);qualified|=Progress>=1;return false;}
        if(score>=settings.EyebrowRestLevel+(settings.EyebrowThreshold-settings.EyebrowRestLevel)*.7&&start.HasValue)return false;
        if(!start.HasValue)return false;
        bool fire=qualified&&time-firedAt>=settings.ClickCooldown;Clear();if(fire)firedAt=time;return fire;
    }
}
