namespace Qadr.Core;

/// <summary>Online Gaussian model of labeled rest-signal increments. Stores sufficient
/// statistics only; no images, face identity, intended-target inference or cloud training.</summary>
public sealed record RestNoiseModel(int Version=1,long Samples=0,double VarianceX=0,double VarianceY=0)
{
    public bool Valid=>Version==1&&Samples>=0&&Samples<=10000000&&double.IsFinite(VarianceX)&&double.IsFinite(VarianceY)&&VarianceX is >=0 and <=1&&VarianceY is >=0 and <=1;
    public RestNoiseModel Observe(double dx,double dy)=>new(1,Math.Min(10000000,Samples+1),VarianceX+(dx*dx-VarianceX)/Math.Min(300,Samples+1),VarianceY+(dy*dy-VarianceY)/Math.Min(300,Samples+1));
    public double Noise(int axis,double baseline){if(Samples<60)return baseline;double sigma=Math.Sqrt((axis==0?VarianceX:VarianceY)/2);return Math.Clamp(.5*baseline+.5*sigma,Math.Max(.0001,baseline*.65),Math.Max(.0002,baseline*1.5));}
}
public sealed class RestNoiseLearner
{
    public RestNoiseModel Model {get;private set;}=new();
    Observation? previous;
    public void Load(RestNoiseModel? model){Model=model?.Valid==true?model:new();previous=null;}
    public void Break()=>previous=null;
    public bool Feed(Observation o,Profile profile,bool labeledRest){
        if(!labeledRest||!o.IsUsable){Break();return false;}
        var before=previous;previous=o;if(before is null)return false;
        double dt=o.Timestamp-before.Timestamp;if(dt<=0||dt>TrackingTiming.MaximumGapSeconds){Break();return false;}
        double dx=profile.Signal(o,0)-profile.Signal(before,0),dy=profile.Signal(o,1)-profile.Signal(before,1);
        // Reject purposeful turns, expressions and outliers rather than learning them as noise.
        if(Math.Abs(dx)>Math.Max(.008,profile.Noise[0]*4)||Math.Abs(dy)>Math.Max(.008,profile.Noise[1]*4)){Break();return false;}
        Model=Model.Observe(dx,dy);return true;
    }
}
/// <summary>Brief stable return after loss. Does not relearn ranges or alter saved calibration.</summary>
public sealed class RecoveryRest
{
    Observation? anchor;
    double since,last;
    public void Reset(){anchor=null;since=last=0;}
    public bool Feed(Observation o){
        if(!o.IsUsable){Reset();return false;}
        if(anchor is null||o.Timestamp<=last||o.Timestamp-last>TrackingTiming.MaximumGapSeconds||Math.Abs(o.Yaw-anchor.Yaw)>.025||Math.Abs(o.Pitch-anchor.Pitch)>.025||Math.Abs(o.Scale/anchor.Scale-1)>.08){anchor=o;since=o.Timestamp;}
        last=o.Timestamp;return o.Timestamp-since>=.35;
    }
}
