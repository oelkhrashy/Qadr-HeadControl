using System.IO;
using System.Windows;
using Qadr.Core;

namespace Qadr.MovementLab;
internal static class LabSimulation
{
    public static void Run(string report)
    {
        double now=1000;
        var window=new LabWindow(true,()=>now);
        var root=(FrameworkElement)window.Content;root.Measure(new Size(1280,900));root.Arrange(new Rect(0,0,1280,900));root.UpdateLayout();
        window.StartSimulation();
        string previous="";int previousIndex=-1;double since=now;bool interrupted=false;
        for(int step=0;step<20000&&window.TestRunning;step++)
        {
            string phase=window.TestPhase;int index=window.TestIndex;
            if(phase!=previous||index!=previousIndex){since=now;previous=phase;previousIndex=index;}
            Point start=window.TestStart,target=window.TestTarget,p;
            if(phase=="origin")p=start;
            else if(phase=="travel") {double t=Math.Min(1,(now-since)/1.0);p=new(start.X+(target.X-start.X)*t,start.Y+(target.Y-start.Y)*t);}
            else p=target;
            bool valid=true;
            if(index==1&&phase=="hold"&&!interrupted&&now-since>.3){valid=false;interrupted=true;}
            window.Advance(now,(int)Math.Round(p.X),(int)Math.Round(p.Y),valid);now+=.01;
        }
        var results=window.TestResults;
        if(window.TestRunning||results.Length!=18||results.Any(p=>!p.Completed))throw new Exception("Guided protocol did not complete all 18 simulated tasks.");
        if(results.Any(p=>p.Quality.HoldJitterRmsPx is null||p.Quality.HoldJitterRmsPx>1e-6))throw new Exception("A stationary hold was incorrectly measured.");
        if(!interrupted||results[1].Quality.TrackingLosses!=1)throw new Exception("Interrupted hold was not tracked separately.");
        if(results.Where(p=>p.Direction!="Rest at centre").Any(p=>p.Quality.CrossTrackRmsPx>1))throw new Exception("Straight/diagonal path measurement exceeded rounding tolerance.");
        File.WriteAllText(report,Protocol.Encode(new{synthetic=true,taskCount=results.Length,allCompleted=true,invalidHoldExcluded=true,trials=results}));
        File.WriteAllText(System.IO.Path.ChangeExtension(report,".html"),ReportHtml.Create(results));
        // A resting cursor that drifts beyond the circle must still be measured.
        var restWindow=new LabWindow(true,()=>now);
        var restRoot=(FrameworkElement)restWindow.Content;restRoot.Measure(new Size(1280,900));restRoot.Arrange(new Rect(0,0,1280,900));restRoot.UpdateLayout();
        restWindow.StartSimulation(true);double holdSince=double.NaN;
        for(int step=0;step<3000&&restWindow.TestRunning;step++)
        {
            var anchor=restWindow.TestStart;
            if(restWindow.TestPhase=="hold"&&double.IsNaN(holdSince))holdSince=now;
            double drift=double.IsNaN(holdSince)?0:(now-holdSince)*12;
            restWindow.Advance(now,(int)Math.Round(anchor.X+drift),(int)Math.Round(anchor.Y),true);now+=.01;
        }
        var resting=restWindow.TestResults.Single();
        if(!resting.Completed||resting.Quality.HoldDriftPx<170||resting.Quality.HoldSamples<1400)
            throw new Exception("The 15s still-head check discarded spatial drift or timed out.");
        File.WriteAllText(System.IO.Path.ChangeExtension(report,".rest-check.json"),Protocol.Encode(new{synthetic=true,spatialDriftRetained=true,trial=resting}));
    }
}
