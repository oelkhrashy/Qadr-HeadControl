namespace Qadr.Core;

/// <summary>Accept a brief supported posture at the current position, independent
/// of yesterday's centre and expression scores. Gaps and movement restart the hold.</summary>
public sealed class ResumeRest
{
    readonly List<Observation> samples=[];
    Observation? anchor,previous;
    public IReadOnlyList<Observation> Samples=>samples;
    public double Progress {get;private set;}
    public void Reset(){samples.Clear();anchor=previous=null;Progress=0;}
    public bool Feed(Observation o,Profile profile){
        if(!o.IsUsable){Reset();return false;}
        double turnX=Math.Clamp(profile.Noise[0]*6,.035,.08),turnY=Math.Clamp(profile.Noise[1]*6,.035,.08);
        bool interrupted=previous is null||o.Timestamp<=previous.Timestamp||o.Timestamp-previous.Timestamp>TrackingTiming.MaximumGapSeconds;
        bool moved=anchor is not null&&(Math.Abs(o.Yaw-anchor.Yaw)>turnX||Math.Abs(o.Pitch-anchor.Pitch)>turnY||
            Math.Abs(o.X-anchor.X)/o.Scale>.10||Math.Abs(o.Y-anchor.Y)/o.Scale>.10||Math.Abs(o.Scale/anchor.Scale-1)>.12);
        if(interrupted||moved){Reset();anchor=o;}
        previous=o;samples.Add(o);
        Progress=Math.Clamp((o.Timestamp-samples[0].Timestamp)/.6,0,1);
        return Progress>=1&&samples.Count>=10;
    }
    public Observation Centre(){
        if(samples.Count==0)throw new InvalidOperationException("No resting observations");
        return samples[^1] with{Yaw=Calibration.Median(samples.Select(o=>o.Yaw)),Pitch=Calibration.Median(samples.Select(o=>o.Pitch)),
            X=Calibration.Median(samples.Select(o=>o.X)),Y=Calibration.Median(samples.Select(o=>o.Y)),Scale=Calibration.Median(samples.Select(o=>o.Scale))};
    }
}
