using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public sealed record MovementSample(double Time, double X, double Y, string Phase, bool Valid=true);
public sealed record MovementQuality(int ValidSamples, int InvalidSamples, int TrackingLosses,
    double TravelSeconds, double PathLengthPx, double PathEfficiency, double EndpointErrorPx,
    double CrossTrackRmsPx, double CrossTrackP95Px, double CrossTrackMaximumPx,
    double BacktrackingPx, double PeakSpeedPxPerSecond, int HoldSamples,
    double? HoldJitterRmsPx, double? HoldP95RadiusPx, double? HoldMaximumRadiusPx, double? HoldDriftPx)
{
    // Linear drift removed for comparison; this is not a pure sensor-noise estimate.
    public double? HoldDetrendedRmsPx { get; init; }
}

/// <summary>Measures labelled travel separately from explicitly instructed still holds.</summary>
public static class MovementQualityAnalysis
{
    public static MovementQuality Measure(IReadOnlyList<MovementSample> samples,
        double startX,double startY,double targetX,double targetY)
    {
        bool Usable(MovementSample p)=>p.Valid&&double.IsFinite(p.Time)&&double.IsFinite(p.X)&&double.IsFinite(p.Y);
        var travel=samples.Where(p=>p.Phase=="travel"&&Usable(p)).ToArray();
        var hold=samples.Where(p=>p.Phase=="hold"&&Usable(p)).ToArray();
        double length=0,back=0,peak=0;int losses=0;
        double ax=targetX-startX,ay=targetY-startY,distance=Math.Sqrt(ax*ax+ay*ay);
        double ux=distance>0?ax/distance:0,uy=distance>0?ay/distance:0;
        var cross=new List<double>();MovementSample? previous=null;bool invalidRun=false;
        foreach(var p in samples)
        {
            if(!Usable(p)) {if(!invalidRun)losses++;invalidRun=true;previous=null;continue;}
            if(previous is not null&&p.Time-previous.Time>.2)losses++;
            invalidRun=false;
            if(p.Phase=="travel")
            {
                cross.Add(Math.Abs((p.X-startX)*uy-(p.Y-startY)*ux));
                if(previous is not null&&previous.Phase=="travel")
                {
                    double dt=p.Time-previous.Time;
                    if(dt>0&&dt<=.2)
                    {
                        double dx=p.X-previous.X,dy=p.Y-previous.Y,step=Math.Sqrt(dx*dx+dy*dy);
                        length+=step;back+=Math.Max(0,-dx*ux-dy*uy);peak=Math.Max(peak,step/dt);
                    }
                }
            }
            previous=p;
        }
        var endpoint=travel.LastOrDefault()??hold.LastOrDefault();
        double actual=endpoint is null?0:Math.Sqrt(Math.Pow(endpoint.X-startX,2)+Math.Pow(endpoint.Y-startY,2));
        double error=endpoint is null?distance:Math.Sqrt(Math.Pow(endpoint.X-targetX,2)+Math.Pow(endpoint.Y-targetY,2));
        double? jitter=null,p95=null,max=null,drift=null,detrended=null;
        // At least 0.5 seconds of labelled holding, never ordinary travel inside a target.
        if(hold.Length>=10&&hold[^1].Time-hold[0].Time>=.5)
        {
            double mx=hold.Average(p=>p.X),my=hold.Average(p=>p.Y);
            var radii=hold.Select(p=>Math.Sqrt(Math.Pow(p.X-mx,2)+Math.Pow(p.Y-my,2))).ToArray();
            jitter=Math.Sqrt(radii.Select(v=>v*v).Average());p95=Percentile(radii,.95);max=radii.Max();
            double mt=hold.Average(p=>p.Time-hold[0].Time);
            double tt=hold.Sum(p=>Math.Pow(p.Time-hold[0].Time-mt,2));
            double bx=hold.Sum(p=>(p.Time-hold[0].Time-mt)*(p.X-mx))/tt;
            double by=hold.Sum(p=>(p.Time-hold[0].Time-mt)*(p.Y-my))/tt;
            detrended=Math.Sqrt(hold.Average(p=>Math.Pow(p.X-mx-bx*(p.Time-hold[0].Time-mt),2)+Math.Pow(p.Y-my-by*(p.Time-hold[0].Time-mt),2)));
            var first=hold.Where(p=>p.Time<=hold[0].Time+.25).ToArray();
            var last=hold.Where(p=>p.Time>=hold[^1].Time-.25).ToArray();
            drift=Math.Sqrt(Math.Pow(first.Average(p=>p.X)-last.Average(p=>p.X),2)+Math.Pow(first.Average(p=>p.Y)-last.Average(p=>p.Y),2));
        }
        return new(samples.Count(Usable),samples.Count(p=>!Usable(p)),losses,
            travel.Length>1?travel[^1].Time-travel[0].Time:0,length,
            length>0?Math.Clamp(actual/length,0,1):0,error,
            cross.Count>0?Math.Sqrt(cross.Select(v=>v*v).Average()):0,Percentile(cross,.95),cross.Count>0?cross.Max():0,
            back,peak,hold.Length,jitter,p95,max,drift){HoldDetrendedRmsPx=detrended};
    }
    static double Percentile(IEnumerable<double> source,double q)
    {var a=source.OrderBy(x=>x).ToArray();return a.Length==0?0:a[Math.Clamp((int)Math.Ceiling((a.Length-1)*q),0,a.Length-1)];}
}
