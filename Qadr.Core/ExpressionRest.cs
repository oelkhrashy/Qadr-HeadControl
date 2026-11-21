namespace Qadr.Core;

/// <summary>Session-only neutral expression statistics from the explicitly prompted
/// resting interval. No images or free-movement samples are stored or trained.</summary>
public sealed record ExpressionRest(double Smile,double SmileNoise,double Brow,double BrowNoise,
    double Pucker,double PuckerNoise,double Mouth,double MouthNoise,double Eye=0,double EyeNoise=0)
{
    public static ExpressionRest? From(IEnumerable<Observation> observations){
        var samples=observations.Where(o=>o.IsUsable).ToArray();
        if(samples.Length<10||samples[^1].Timestamp-samples[0].Timestamp<.3)return null;
        (double Level,double Noise) Measure(Func<Observation,double> channel){
            double rest=Calibration.Median(samples.Select(channel));
            return(rest,1.4826*Calibration.Median(samples.Select(o=>Math.Abs(channel(o)-rest))));
        }
        var s=Measure(o=>o.Smile);var b=Measure(o=>o.Eyebrow);var p=Measure(o=>o.Pucker);var m=Measure(o=>o.Mouth);
        var e=Measure(o=>Math.Max(o.Blink,Math.Max(o.LeftEye,o.RightEye)));
        return new(s.Level,s.Noise,b.Level,b.Noise,p.Level,p.Noise,m.Level,m.Noise,e.Level,e.Noise);
    }
    public Settings Apply(Settings saved){
        // Preserve the demonstrated gesture amplitude, but require a rise above
        // today's neutral score and noise. An unreachable threshold disables
        // that gesture for this interval rather than accepting resting input.
        static double Threshold(double rest,double noise,double oldRest,double oldThreshold,double floor)
            =>rest+Math.Max(floor,Math.Max(oldThreshold-oldRest,noise*5));
        return saved with{
            // Fit the demonstrated eye rise into the score headroom. If noise
            // exceeds that headroom the threshold safely remains unreachable.
            EyeRestLevel=Eye,BlinkThreshold=Eye+Math.Max(.12,Math.Max(EyeNoise*5,Math.Min(saved.BlinkThreshold-saved.EyeRestLevel,(1-Eye)*.7))),
            SmileRestLevel=Smile,SmileThreshold=Threshold(Smile,SmileNoise,saved.SmileRestLevel,saved.SmileThreshold,.04),
            EyebrowRestLevel=Brow,EyebrowThreshold=Threshold(Brow,BrowNoise,saved.EyebrowRestLevel,saved.EyebrowThreshold,.08),
            PuckerRestLevel=Pucker,PuckerThreshold=Threshold(Pucker,PuckerNoise,saved.PuckerRestLevel,saved.PuckerThreshold,.06),
            ScrollGestureRestLevel=Mouth,ScrollGestureThreshold=Threshold(Mouth,MouthNoise,saved.ScrollGestureRestLevel,saved.ScrollGestureThreshold,.08),
            MouthRestLevel=Mouth,MouthThreshold=Threshold(Mouth,MouthNoise,saved.MouthRestLevel,saved.MouthThreshold,.08)
        };
    }
}
