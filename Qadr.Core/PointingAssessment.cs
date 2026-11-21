namespace Qadr.Core;

public sealed record AssessmentTarget(string Id,string Action,double X,double Y,double Width,double Height)
{
    public bool Valid=>Id.Length is >0 and <=8&&Action is "move" or "left" or "double" or "right"&&
        new[]{X,Y,Width,Height}.All(double.IsFinite)&&Width is >=20 and <=1000&&Height is >=20 and <=1000;
    public bool Contains(double x,double y)=>Math.Pow((x-X-Width/2)/(Width/2),2)+Math.Pow((y-Y-Height/2)/(Height/2),2)<=1;
}
public sealed record AssessmentResult(string Id,string ExpectedAction,bool Completed,string Reason,double DurationMs,
    double? FirstEntryMs,double PathLengthPx,double PathEfficiency,int Entries,int Overshoots,double MaximumOvershootPx,
    double? FinalCentreErrorPx,double? InsideJitterRmsPx,int ClickAttempts,int WrongActionAttempts,int TrackingLosses,long Samples);
public sealed record AssessmentProgress(AssessmentTarget Target,double InsideProgress,double ElapsedSeconds,bool ConfirmingSingleClick=false);

/// <summary>Measures labelled tasks from actual cursor samples; never injects
/// input or trains controller parameters. Targets are visible ellipses.</summary>
public sealed class PointingAssessment
{
    readonly AssessmentTarget target;
    readonly double started,startX,startY;
    readonly double doubleClickSeconds;
    AssessmentResult? pendingSingle;
    double singleAt;
    public PointingAssessment(AssessmentTarget target,double started,double startX,double startY,double doubleClickSeconds=.5){this.target=target;this.started=started;this.startX=startX;this.startY=startY;this.doubleClickSeconds=double.IsFinite(doubleClickSeconds)?Math.Clamp(doubleClickSeconds,.1,5):.5;lastTime=started;lastX=startX;lastY=startY;}
    double lastTime,lastX,lastY,path,firstEntry=double.NaN,insideSince=double.NaN,maximumOvershoot;
    double meanX,meanY,m2X,m2Y;long insideSamples,samples;
    bool inside,usableBefore=true,completed;
    int entries,overshoots,attempts,wrong,losses;
    // Wall-clock deadline must remain testable when tracking/input is paused.
    public bool HasTimedOut(double now,double seconds)=>!completed&&double.IsFinite(now)&&double.IsFinite(seconds)&&seconds>0&&now>=started&&now-started>=seconds;
    public AssessmentProgress Progress=>new(target,double.IsFinite(insideSince)?Math.Clamp((lastTime-insideSince)/.6,0,1):0,Math.Max(0,lastTime-started),pendingSingle is not null);
    public AssessmentResult? Feed(double now,double x,double y,bool usable){
        if(completed||!double.IsFinite(now)||now<lastTime||!double.IsFinite(x)||!double.IsFinite(y))return null;
        bool gap=now-lastTime>TrackingTiming.MaximumGapSeconds;
        lastTime=now;
        if(pendingSingle is {} pending){
            if(!usable||gap)pendingSingle=null;
            else if(now-singleAt>=doubleClickSeconds){completed=true;pendingSingle=null;return pending;}
            else return null;
        }
        if(!usable){if(usableBefore)losses++;usableBefore=false;inside=false;insideSince=double.NaN;insideSamples=0;return null;}
        if(gap&&usableBefore)losses++;
        if(usableBefore&&!gap)path+=Math.Sqrt(Math.Pow(x-lastX,2)+Math.Pow(y-lastY,2));
        lastX=x;lastY=y;usableBefore=true;samples++;
        bool hit=target.Contains(x,y);
        if(hit){
            if(!inside||gap){entries++;insideSince=now;insideSamples=0;meanX=meanY=m2X=m2Y=0;firstEntry=double.IsFinite(firstEntry)?firstEntry:now;}
            insideSamples++;double dx=x-meanX,dy=y-meanY;meanX+=dx/insideSamples;meanY+=dy/insideSamples;m2X+=dx*(x-meanX);m2Y+=dy*(y-meanY);
        }else{
            if(inside)overshoots++;
            if(double.IsFinite(firstEntry)){
                double distance=Math.Sqrt(Math.Pow(x-target.X-target.Width/2,2)+Math.Pow(y-target.Y-target.Height/2,2));
                maximumOvershoot=Math.Max(maximumOvershoot,Math.Max(0,distance-Math.Min(target.Width,target.Height)/2));
            }
            insideSince=double.NaN;insideSamples=0;
        }
        inside=hit;
        return target.Action=="move"&&insideSamples>=10&&now-insideSince>=.6?Finish(now,true,"held inside target"):null;
    }
    public AssessmentResult? Click(string action,double now,double x,double y){
        if(completed||!double.IsFinite(now)||now<lastTime||!double.IsFinite(x)||!double.IsFinite(y))return null;
        // Do not classify the first Tapped event as a single click until the
        // operating system's double-click interval has elapsed. Keep metrics
        // at the first click timestamp/position, not the confirmation time.
        pendingSingle=null;
        var automatic=Feed(now,x,y,usableBefore);if(automatic is not null)return automatic;
        attempts++;
        if(!usableBefore||!target.Contains(x,y))return null;
        // Windows raises Tapped first, then DoubleTapped for the second tap.
        if(target.Action=="double"&&action=="left")return null;
        if(action!=target.Action){wrong++;return null;}
        if(action=="left"){pendingSingle=Result(now,true,"single tap confirmed");singleAt=now;return null;}
        return Finish(now,true,"required action received");
    }
    public AssessmentResult Cancel(double now,string reason)=>Finish(Math.Max(lastTime,now),false,reason);
    AssessmentResult Finish(double now,bool success,string reason){
        completed=true;pendingSingle=null;return Result(now,success,reason);
    }
    AssessmentResult Result(double now,bool success,string reason){
        double direct=Math.Sqrt(Math.Pow(lastX-startX,2)+Math.Pow(lastY-startY,2));
        return new(target.Id,target.Action,success,reason,Math.Max(0,now-started)*1000,
            double.IsFinite(firstEntry)?(firstEntry-started)*1000:null,path,path>0?Math.Clamp(direct/path,0,1):0,entries,overshoots,maximumOvershoot,
            samples>0?Math.Sqrt(Math.Pow(lastX-target.X-target.Width/2,2)+Math.Pow(lastY-target.Y-target.Height/2,2)):null,
            insideSamples>1?Math.Sqrt((m2X+m2Y)/(insideSamples-1)):null,attempts,wrong,losses,samples);
    }
}
