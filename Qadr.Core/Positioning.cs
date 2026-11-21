namespace Qadr.Core;

public sealed record FacePosition(string Code,string Hint,bool Ready,double X=.5,double Y=.48,double Width=.34,double Height=.53);

public sealed class FacePositioning
{
    public Settings Settings {get;set;}=new();
    double targetX=.5,targetY=.48,targetWidth=.34,targetHeight=.53,neutralYaw,neutralPitch,scale;
    Observation? candidate;
    double since;
    bool personalized;
    public int Revision {get;private set;}
    public Observation? FittedObservation {get;private set;}
    public void UseComfortablePosition(Observation o){
        FittedObservation=o;Revision++;
        targetX=o.FaceX;targetY=o.FaceY;neutralYaw=o.Yaw;neutralPitch=o.Pitch;scale=o.Scale;personalized=true;candidate=null;
        targetWidth=Math.Clamp(o.FaceWidth*Settings.PositionGuidePadding,.10,Math.Max(.10,Math.Min(.95,2*Math.Min(targetX,1-targetX))));
        targetHeight=Math.Clamp(o.FaceHeight*Settings.PositionGuidePadding,.12,Math.Max(.12,Math.Min(.96,2*Math.Min(targetY,1-targetY))));
    }
    public FacePosition Check(Observation? o,bool placement=true)
    {
        FacePosition Result(string code,string hint,bool ready=false)=>new(code,hint,ready,targetX,targetY,targetWidth,targetHeight);
        if(o is null){candidate=null;return Result("find","Bring your face into the camera view");}
        if(o.Scale<Settings.PositionMinimumScale || o.FaceHeight<Settings.PositionMinimumHeight){candidate=null;return Result("closer","Your face is too small to track reliably. Bring the camera a little closer");}
        if(o.Scale>Settings.PositionMaximumScale || o.FaceHeight>Settings.PositionMaximumHeight){candidate=null;return Result("farther","Your face needs more room in the view. Move the camera a little farther away");}
        if(o.FaceX-o.FaceWidth/2<.015 || o.FaceX+o.FaceWidth/2>.985 || o.FaceY-o.FaceHeight/2<.01 || o.FaceY+o.FaceHeight/2>.99)
            {candidate=null;return Result("frame","Keep your whole face inside the camera view");}
        // During control the oval is visual guidance only. Resize it continuously;
        // never refit the movement origin or pause a usable observation for the oval.
        if(!placement && personalized){
            const double blend=.12;
            targetX+=(o.FaceX-targetX)*blend;targetY+=(o.FaceY-targetY)*blend;
            targetWidth+=(Math.Clamp(o.FaceWidth*Settings.PositionGuidePadding,.10,.95)-targetWidth)*blend;
            targetHeight+=(Math.Clamp(o.FaceHeight*Settings.PositionGuidePadding,.12,.96)-targetHeight)*blend;
            return Result("good","Tracking your face; the guide follows your distance",true);
        }
        // Accept the person's supported posture rather than requiring a fixed
        // camera distance. Fit after stability, never chase exploration turns.
        if(!personalized || Math.Abs(o.Scale/scale-1)>Settings.PositionScaleTolerance || placement&&(Math.Abs(o.FaceX-targetX)>.14||Math.Abs(o.FaceY-targetY)>.16)){
            if(candidate is null || o.Timestamp-candidate.Timestamp>.25 || Math.Abs(o.Scale/candidate.Scale-1)>Settings.PositionScaleTolerance ||
                Math.Abs(o.FaceX-candidate.FaceX)>Settings.PositionCentreTolerance || Math.Abs(o.FaceY-candidate.FaceY)>Settings.PositionCentreTolerance ||
                Math.Abs(o.Yaw-candidate.Yaw)>Settings.PositionAngleTolerance || Math.Abs(o.Pitch-candidate.Pitch)>Settings.PositionAngleTolerance){candidate=o;since=o.Timestamp;}
            else candidate=candidate with{Timestamp=o.Timestamp};
            if(o.Timestamp-since<Settings.PositionSettleSeconds)return Result("adapting","Hold comfortably while the oval fits your face and distance");
            UseComfortablePosition(o);
        }
        if(placement){
            if(Math.Abs(o.FaceX-targetX)>.14)return Result(o.FaceX<targetX?"right":"left",o.FaceX<targetX?"Move gently toward the right arrow":"Move gently toward the left arrow");
            if(Math.Abs(o.FaceY-targetY)>.16)return Result(o.FaceY<targetY?"down":"up",o.FaceY<targetY?"Lower the camera aim, or move toward the down arrow":"Raise the camera aim, or move toward the up arrow");
            if(Math.Abs(o.Yaw-neutralYaw)>.32 || Math.Abs(o.Pitch-neutralPitch)>.42)
                return Result("angle",personalized?"Return to your comfortable angle":"Look toward the camera, or choose Use my comfortable position");
        }
        return Result("good","Your face and distance are learned. Relax comfortably",true);
    }
}

/// <summary>Preserves fractional motion and fills the intervals between camera observations.</summary>
public sealed class CursorTrajectory
{
    double x,y,targetX,targetY,vx,vy,received,left,top,width,height;
    bool rate;
    public bool Ready {get;private set;}
    public void Reset(double px,double py){x=targetX=px;y=targetY=py;Ready=true;rate=false;vx=vy=0;}
    public void Clear(){Ready=rate=false;vx=vy=0;}
    public void SetRate(double velocityX,double velocityY,double px,double py,double desktopLeft,double desktopTop,double desktopWidth,double desktopHeight,double captured){
        if(!double.IsFinite(velocityX)||!double.IsFinite(velocityY)||!double.IsFinite(px)||!double.IsFinite(py)||!double.IsFinite(captured)||!double.IsFinite(desktopLeft)||!double.IsFinite(desktopTop)||!double.IsFinite(desktopWidth)||!double.IsFinite(desktopHeight)||desktopWidth<=0||desktopHeight<=0){Clear();return;}
        if(!Ready||!rate||Math.Abs(px-x)>8||Math.Abs(py-y)>8)Reset(px,py);
        vx=velocityX;vy=velocityY;left=desktopLeft;top=desktopTop;width=desktopWidth;height=desktopHeight;received=captured;rate=true;
    }
    public void Add(double dx,double dy,double px,double py,double left,double top,double width,double height,bool stopX=false,bool stopY=false){
        if(!Ready)Reset(px,py);
        // A helper's physical mouse takes over immediately.
        if(Math.Abs(px-x)>8 || Math.Abs(py-y)>8)Reset(px,py);
        if(stopX)x=targetX=px;else targetX=Math.Clamp(targetX+dx,left,left+width-1);
        if(stopY)y=targetY=py;else targetY=Math.Clamp(targetY+dy,top,top+height-1);
    }
    public (double X,double Y) Step(double dt,double now=double.NaN,bool direct=false){
        if(!Ready)return(x,y);
        if(!double.IsFinite(dt)||dt<0){Clear();return(x,y);}
        dt=Math.Min(dt,.05);
        if(rate){
            // A held rate command is integrated at the pointer pump cadence,
            // not delivered as camera-sized jumps. Stale data never coasts.
            if(!double.IsFinite(now)||now<received||now-received>TrackingTiming.MaximumAgeSeconds){vx=vy=0;return(x,y);}
            x=Math.Clamp(x+vx*dt,left,left+width-1);y=Math.Clamp(y+vy*dt,top,top+height-1);
            targetX=x;targetY=y;return(x,y);
        }
        double alpha=1-Math.Exp(-dt/(direct?.008:.018));x+=(targetX-x)*alpha;y+=(targetY-y)*alpha;return(x,y);
    }
}
