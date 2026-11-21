namespace Qadr.Core;

/// <summary>
/// Protect pointer pose only during a newly armed, bounded expression interval.
/// An elevated score alone is not evidence of an intentional smile. In particular,
/// a sustained detector bias must not bypass Gesture's timeout and freeze forever.
/// </summary>
public sealed class SmilePoseGuard
{
    double? previous,neutralSince,started;
    bool armed;
    public void Reset(){previous=neutralSince=started=null;armed=false;}
    public bool Feed(double score,double rest,double threshold,double time,bool enabled){
        if(!enabled||!double.IsFinite(score)||!double.IsFinite(time)||!double.IsFinite(rest)||!double.IsFinite(threshold)||threshold<=rest){Reset();return false;}
        if(previous.HasValue&&time<=previous.Value)return false;
        if(previous.HasValue&&time-previous.Value>TrackingTiming.MaximumGapSeconds)Reset();
        previous=time;
        double onset=rest+(threshold-rest)*.7;
        if(score<onset){
            started=null;neutralSince??=time;
            if(time-neutralSince>=.06)armed=true;
            return false;
        }
        neutralSince=null;
        if(!armed)return false;
        started??=time;
        if(time-started.Value>1.5){armed=false;started=null;return false;}
        return true;
    }
}
