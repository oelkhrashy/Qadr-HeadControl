namespace Qadr.Core;

/// <summary>Optional velocity gain for relative travel. No target, queue, prediction
/// or timer. Small adjustments retain the existing displacement exactly.</summary>
public static class RelativeReach
{
    public static (double X,double Y) Apply(double dx,double dy,double dt,double width,double height,
        double sensitivity,double boost,double confidence=1)
    {
        if(!double.IsFinite(dx)||!double.IsFinite(dy)||!double.IsFinite(dt)||dt<=0||dt>TrackingTiming.MaximumGapSeconds||
           !double.IsFinite(width)||width<=0||!double.IsFinite(height)||height<=0||
           !double.IsFinite(sensitivity)||sensitivity<=0||!double.IsFinite(boost)||!double.IsFinite(confidence))return(0,0);
        boost=Math.Clamp(boost,1,4);
        // Speed is in normalized learned ranges/second, independent of desktop
        // dimensions, pointer sensitivity and tracking frame rate. Both axes
        // receive one gain so diagonal direction is unchanged.
        double vx=dx/(width*.7*sensitivity*dt),vy=dy/(height*.7*sensitivity*dt);
        double speed=Math.Sqrt(vx*vx+vy*vy);
        double intent=Math.Clamp((speed-.15)/.75,0,1);
        intent=intent*intent*(3-2*intent);
        // Adaptive confidence retains the noise/coherence gate. Classic keeps
        // its existing filter and uses the same gradual speed ramp.
        double gain=1+(boost-1)*intent*Math.Clamp(confidence,0,1);
        return(dx*gain,dy*gain);
    }
}
