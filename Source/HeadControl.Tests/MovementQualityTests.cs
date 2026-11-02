using Qadr.Core;

static class MovementQualityTests
{
    public static void Run(Action<bool,string> check)
    {
        var line=Enumerable.Range(0,101).Select(i=>new MovementSample(i/100.0,i,0,"travel")).ToArray();
        var a=MovementQualityAnalysis.Measure(line,0,0,100,0);
        check(a.CrossTrackRmsPx==0&&Math.Abs(a.PathEfficiency-1)<1e-10&&a.EndpointErrorPx==0,"a straight reference path has zero cross-track error and full efficiency");
        var skew=line.Select(p=>p with{Y=p.X*.1}).ToArray();
        var b=MovementQualityAnalysis.Measure(skew,0,0,100,0);
        check(b.CrossTrackRmsPx>5&&b.CrossTrackMaximumPx==10,"path quality detects off-axis skew");
        var hold=Enumerable.Range(0,101).Select(i=>new MovementSample(2+i/100.0,100+(i%2==0?1:-1),0,"hold")).ToArray();
        b=MovementQualityAnalysis.Measure(line.Concat(hold).ToArray(),0,0,100,0);
        check(b.HoldJitterRmsPx is >.99 and <1.01&&b.CrossTrackRmsPx==0,"labelled hold vibration is measured separately from travel");
        check(a.HoldJitterRmsPx is null,"travel inside a target is not silently labelled resting jitter");
        var drifting=Enumerable.Range(0,101).Select(i=>new MovementSample(i/100.0,i*.2,i*.1,"hold")).ToArray();
        var driftQuality=MovementQualityAnalysis.Measure(drifting,0,0,0,0);
        check(driftQuality.HoldJitterRmsPx>6&&driftQuality.HoldDetrendedRmsPx<1e-8,"linear resting drift is separated from residual cursor vibration");
        var gap=line.Select((p,i)=>i>=40&&i<50?p with{Valid=false}:p).ToArray();
        b=MovementQualityAnalysis.Measure(gap,0,0,100,0);
        check(b.TrackingLosses==1&&b.InvalidSamples==10&&b.PathLengthPx<100,"invalid intervals do not fabricate a connecting cursor path");
        var diagonal=line.Select(p=>p with{Y=p.X}).ToArray();
        b=MovementQualityAnalysis.Measure(diagonal,0,0,100,100);
        check(b.CrossTrackRmsPx<1e-8,"an intentional diagonal path is correctly measured against its diagonal reference");
        double Oscillation(double precision)
        {
            var c=new MotionController{Settings=new Settings{ClickMethod="none",RightClickMethod="none",PrecisionNoiseMultiplier=precision}};
            c.SetProfile(new Profile{CoordinateVersion=3,Noise=[.001,.001]});
            double path=0;
            for(int i=0;i<600;i++){var o=new Observation(.004*Math.Sin(i/30.0*2),0,0,0,0,i/30.0,LeftEye:0,RightEye:0);path+=Math.Abs(c.Feed(o,900,500).Dx);}
            return path;
        }
        check(Oscillation(1.5)<Oscillation(.75),"stronger precision measurably reduces synthetic oscillation travel in the adaptive filter");
    }
}
