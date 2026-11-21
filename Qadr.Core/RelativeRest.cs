namespace Qadr.Core;

/// <summary>Stops only a confirmed, locally steady axis near its filtered pose.
/// Uses accepted observations, capture time and a bounded calibrated noise envelope.
/// Never changes movement gain, predicts motion or treats every zero delta as rest.</summary>
public sealed class RelativeRest
{
    bool seeded, held;
    double anchor,minimum,maximum,since,last,path,quietTime;
    public bool CanFinish {get;private set;}
    public void Reset(){seeded=held=CanFinish=false;}
    public bool Feed(double accepted,double filtered,double noise,double time,double dt){
        if(!double.IsFinite(accepted)||!double.IsFinite(filtered)||!double.IsFinite(noise)||!double.IsFinite(time)||dt<=0||dt>TrackingTiming.MaximumGapSeconds){
            Reset();return false;
        }
        double envelope=Math.Max(.00005,noise*.2),release=Math.Max(.0001,noise*.12);
        if(!seeded){Seed(accepted,time);return false;}
        if(held){
            if(Math.Abs(accepted-anchor)<=release)return true;
            Seed(accepted,time);return false;
        }
        double change=Math.Abs(accepted-last);
        quietTime=change<=Math.Max(1e-9,noise*.002)?quietTime+dt:0;
        path+=change;last=accepted;
        minimum=Math.Min(minimum,accepted);maximum=Math.Max(maximum,accepted);
        if(maximum-minimum>envelope){Seed(accepted,time);return false;}
        // Small coherent corrections are movement, even inside the noise
        // envelope. A stable window must not erase a slow monotonic stroke.
        // An unchanged accepted pose retires historical stroke coherence.
        // Keep the original noise-window test for naturally varying observations.
        bool historicalProgress=path>Math.Max(.00001,noise*.025)&&Math.Abs(accepted-anchor)>path*.8;
        bool progressing=quietTime<.075&&historicalProgress;
        if(time-since>=.075&&!progressing){
            // A small remainder may finish once. Large smoothing backlog must
            // be discarded at rest rather than burst or coast after a head stop.
            double finishLimit=Math.Clamp(noise*.6,.0002,.02);
            if(quietTime>=.075&&historicalProgress)finishLimit=Math.Min(finishLimit,.005);
            CanFinish=Math.Abs(accepted-filtered)<=finishLimit;
            held=true;anchor=(minimum+maximum)/2;return true;
        }
        return false;
    }
    void Seed(double input,double time){seeded=true;held=CanFinish=false;anchor=last=minimum=maximum=input;path=quietTime=0;since=time;}
}
