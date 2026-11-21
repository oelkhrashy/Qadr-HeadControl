namespace Qadr.Core;

/// <summary>Extra reach builds only during a sustained direction. Reversals and
/// stops reset it immediately. No pointer movement is generated or queued.</summary>
public sealed class RelativeTravelIntent
{
    double previousX,previousY,seconds;
    bool seeded;
    public void Reset(){previousX=previousY=seconds=0;seeded=false;}
    public double Feed(double dx,double dy,double dt,double width,double height,double sensitivity){
        if(!double.IsFinite(dx)||!double.IsFinite(dy)||!double.IsFinite(dt)||!double.IsFinite(width)||!double.IsFinite(height)||!double.IsFinite(sensitivity)||dt<=0||dt>TrackingTiming.MaximumGapSeconds||width<=0||height<=0||sensitivity<=0){Reset();return 0;}
        double x=dx/(width*.7*sensitivity*dt),y=dy/(height*.7*sensitivity*dt),speed=Math.Sqrt(x*x+y*y);
        if(speed<=.15){Reset();return 0;}
        x/=speed;y/=speed;
        if(!seeded||x*previousX+y*previousY<.5)seconds=0;else seconds=Math.Min(.24,seconds+dt);
        previousX=x;previousY=y;seeded=true;
        double amount=Math.Clamp((seconds-.06)/.18,0,1);
        return amount*amount*(3-2*amount);
    }
}
