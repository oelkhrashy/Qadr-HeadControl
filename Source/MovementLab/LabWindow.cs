using Qadr.Core;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Qadr.MovementLab;

internal sealed record TrialPlan(string Id,string Direction,double X,double Y,bool Rest=false);
internal sealed record TrialReport(string Id,string Direction,bool Completed,string Reason,double StartX,double StartY,double TargetX,double TargetY,MovementQuality Quality,MovementSample[] Samples);

internal sealed class LabWindow:Window
{
    static readonly Brush Navy=new SolidColorBrush(Color.FromRgb(17,35,54));
    static readonly Brush Green=new SolidColorBrush(Color.FromRgb(0,130,110));
    static readonly Brush Amber=new SolidColorBrush(Color.FromRgb(194,109,12));
    readonly TextBlock instruction=new(){FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=Navy,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock connection=new(){FontSize=14,Foreground=Navy,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock detail=new(){FontSize=15,Foreground=Navy,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock progress=new(){FontSize=16,Foreground=Navy,TextWrapping=TextWrapping.Wrap};
    readonly Canvas canvas=new(){Background=new SolidColorBrush(Color.FromRgb(243,247,248)),ClipToBounds=true,MinHeight=300};
    readonly Button start=new(){Content="Start guided test",MinHeight=46,MinWidth=180,FontSize=17,Padding=new Thickness(16,6,16,6)};
    readonly Button finish=new(){Content="Finish + save",MinHeight=46,MinWidth=140,FontSize=17,Padding=new Thickness(16,6,16,6),IsEnabled=false};
    readonly Button restCheck=new(){Content="15s still-head check",MinHeight=46,FontSize=16,Padding=new Thickness(14,6,14,6),Margin=new Thickness(12,0,0,0)};
    readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(10)};
    readonly Ellipse origin=new(){Width=70,Height=70,Stroke=Navy,StrokeThickness=3,Fill=Brushes.White};
    readonly Ellipse target=new(){Width=70,Height=70,Stroke=Green,StrokeThickness=4,Fill=new SolidColorBrush(Color.FromRgb(221,244,237))};
    readonly Line reference=new(){Stroke=new SolidColorBrush(Color.FromRgb(160,175,185)),StrokeThickness=2,StrokeDashArray=[5,5]};
    readonly Polyline trail=new(){Stroke=Amber,StrokeThickness=2};
    readonly List<TrialReport> results=[];
    readonly List<MovementSample> samples=[];
    readonly List<(double Time,double X,double Y,bool Valid,string Phase,string Trial)> cursorRows=[];
    readonly string folder=System.IO.Path.Combine(AppContext.BaseDirectory,"Data","movement-tests");
    readonly string session="Qadr-Quality-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6];
    readonly bool preview;
    readonly Func<double> clock;
    TrialPlan[] plans=[];
    int index;
    string phase="ready",pendingText="";
    double phaseSince,entered=double.NaN,lastDraw;
    double sx,sy,tx,ty,originX,originY;
    bool running,saved,traceExpected;
    double restDuration=3;
    string? tracePath,lastReport;
    string? labMarker;
    FileStream? traceStream;
    string partial="";
    double lastPose=double.NegativeInfinity;
    bool poseFrozen;

    public LabWindow(bool preview,Func<double>? clock=null)
    {
        this.preview=preview;
        this.clock=clock??(()=>Arguments.Now);
        Title="Qadr · movement quality test";Width=1280;Height=900;MinWidth=860;MinHeight=650;
        WindowState=WindowState.Maximized;Background=Brushes.White;FontFamily=new FontFamily("Segoe UI");
        var grid=new Grid{Margin=new Thickness(24),Background=Brushes.White};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var top=new StackPanel{Margin=new Thickness(0,0,0,16)};
        top.Children.Add(new TextBlock{Text="QADR  /  MOVEMENT QUALITY",FontSize=15,FontWeight=FontWeights.Bold,Foreground=Green,Margin=new Thickness(0,0,0,8)});
        instruction.Text="Finish Qadr’s startup check, then start this test.";
        detail.Text="About 3–4 minutes · head control only · no clicks needed during the tasks. We measure straight paths, diagonals and still holds separately.";
        top.Children.Add(instruction);top.Children.Add(detail);top.Children.Add(connection);grid.Children.Add(top);
        Grid.SetRow(canvas,1);grid.Children.Add(canvas);
        var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};
        var buttons=new WrapPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(start);buttons.Children.Add(restCheck);finish.Margin=new Thickness(12,0,0,0);buttons.Children.Add(finish);
        var open=new Button{Content="Open saved reports",MinHeight=46,FontSize=16,Margin=new Thickness(12,0,0,0),Padding=new Thickness(14,6,14,6)};buttons.Children.Add(open);
        footer.Children.Add(buttons);footer.Children.Add(progress);footer.Children.Add(new TextBlock{Text="Escape stops Qadr and saves a partial test. Reports contain numeric movement data; no video or audio.",FontSize=13,Foreground=Navy,Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap});
        Grid.SetRow(footer,2);grid.Children.Add(footer);Content=grid;
        canvas.Children.Add(reference);canvas.Children.Add(origin);canvas.Children.Add(target);canvas.Children.Add(trail);
        start.Click+=(_,_)=>Begin();restCheck.Click+=(_,_)=>Begin(true);finish.Click+=(_,_)=>End("ended by user");
        open.Click+=(_,_)=>{Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo("explorer.exe",folder){UseShellExecute=true});};
        KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){End("Escape");e.Handled=true;}};
        SizeChanged+=(_,_)=>{if(running)End("window resized; restart to keep coordinates accurate");};
        Deactivated+=(_,_)=>{if(running)End("test lost focus; restart when visible");};
        Closing+=(_,_)=>{End("test window closed");timer.Stop();StopTrace();traceStream?.Dispose();};
        timer.Tick+=(_,_)=>Tick();
        if(!preview)Loaded+=(_,_)=>{PrepareQadr();timer.Start();};
    }

    void PrepareQadr()
    {
        Directory.CreateDirectory(folder);
        string qadr=System.IO.Path.Combine(AppContext.BaseDirectory,"Qadr.exe");
        if(Process.GetProcessesByName("Qadr").Length>0)
        {
            pendingText="Qadr is already running: cursor-only test. For raw head measurements, close Qadr and reopen this test.";
            return;
        }
        tracePath=System.IO.Path.Combine(folder,session+"-head.jsonl");
        var info=new ProcessStartInfo(qadr){UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory};
        info.ArgumentList.Add("--data-dir");info.ArgumentList.Add(System.IO.Path.Combine(AppContext.BaseDirectory,"Data"));
        info.Environment["QADR_MOVEMENT_TRACE"]=tracePath;
        labMarker=tracePath+".active";
        info.Environment["QADR_MOVEMENT_LAB_ACTIVE"]=labMarker;
        try {Process.Start(info);traceExpected=true;pendingText="Complete Qadr’s startup movement check, then return here. This test records cursor and head measurements.";}
        catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException){pendingText="Could not open Qadr: "+e.Message;}
    }

    static TrialPlan[] Plan()
    {
        var p=new List<TrialPlan>{new("rest-start","Rest at centre",0,0,true)};
        foreach(double distance in new[]{.55,1.0})for(int repeat=1;repeat<=2;repeat++)
        {p.Add(new($"right-{distance}-{repeat}","Right",distance,0));p.Add(new($"left-{distance}-{repeat}","Left",-distance,0));}
        foreach(double distance in new[]{.55,1.0}) {p.Add(new($"up-{distance}","Up",0,-distance));p.Add(new($"down-{distance}","Down",0,distance));}
        p.AddRange([new("diagonal-ne","Up + right",.75,-.75),new("diagonal-sw","Down + left",-.75,.75),new("diagonal-nw","Up + left",-.75,-.75),new("diagonal-se","Down + right",.75,.75)]);
        p.Add(new("rest-end","Rest at centre",0,0,true));return p.ToArray();
    }

    void Begin(bool restOnly=false)
    {
        if(running||saved)return;
        ReadPose();if(traceExpected&&clock()-lastPose>.25){instruction.Text="Waiting for face control. Finish Qadr’s startup check and return here.";return;}
        restDuration=restOnly?15:3;
        plans=restOnly?[new("rest-15s","Rest at centre",0,0,true)]:Plan();
        index=0;results.Clear();cursorRows.Clear();start.IsEnabled=false;restCheck.IsEnabled=false;finish.IsEnabled=true;running=true;Next();
        if(labMarker is not null)File.WriteAllText(labMarker,"Movement-only trial: clicks temporarily suspended; saved preferences unchanged.");
    }
    void Next()
    {
        if(index>=plans.Length){End("completed");return;}
        samples.Clear();trail.Points.Clear();phase="origin";phaseSince=clock();entered=double.NaN;
        var plan=plans[index];originX=canvas.ActualWidth/2;originY=canvas.ActualHeight/2;
        Point a=Screen(new Point(originX,originY));sx=a.X;sy=a.Y;
        Point b=Screen(new Point(originX+plan.X*Math.Max(60,canvas.ActualWidth*.38),originY+plan.Y*Math.Max(60,canvas.ActualHeight*.36)));tx=b.X;ty=b.Y;
        Canvas.SetLeft(origin,originX-35);Canvas.SetTop(origin,originY-35);
        double lx=originX+plan.X*Math.Max(60,canvas.ActualWidth*.38),ly=originY+plan.Y*Math.Max(60,canvas.ActualHeight*.36);
        Canvas.SetLeft(target,lx-35);Canvas.SetTop(target,ly-35);
        reference.X1=originX;reference.Y1=originY;reference.X2=lx;reference.Y2=ly;
        origin.Visibility=Visibility.Visible;target.Visibility=plan.Rest?Visibility.Hidden:Visibility.Visible;
        instruction.Text=$"{index+1}/{plans.Length} · Move to the dark starting circle and settle.";
        detail.Text="The return to the starting circle is not scored. Keep your head comfortable; relax your click gestures.";
    }

    void ReadPose()
    {
        if(tracePath is null)return;
        try
        {
            traceStream??=new FileStream(tracePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            int available=(int)Math.Min(65536,traceStream.Length-traceStream.Position);if(available<=0)return;
            byte[] bytes=new byte[available];int read=traceStream.Read(bytes,0,bytes.Length);
            partial+=Encoding.UTF8.GetString(bytes,0,read);
            int end;
            while((end=partial.IndexOf('\n'))>=0)
            {
                string line=partial[..end];partial=partial[(end+1)..];
                try {using var doc=JsonDocument.Parse(line);var r=doc.RootElement;if(r.GetProperty("kind").GetString()!="sample")continue;lastPose=r.GetProperty("time").GetDouble();poseFrozen=r.GetProperty("output").GetProperty("frozen").GetBoolean();}
                catch(JsonException) { }
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }

    void Tick()
    {
        ReadPose();double now=clock();
        bool tracking=!traceExpected||now>=lastPose&&now-lastPose<=.25;
        connection.Text=traceExpected?(tracking?"Head tracking connected · numeric head and cursor recording":"Waiting for fresh head tracking · finish setup or bring your face back into view"):pendingText;
        if(!running)return;
        var pos=NativeInput.Cursor;bool valid=tracking&&(!traceExpected||!poseFrozen)&&IsActive;
        Advance(now,pos.X,pos.Y,valid);
    }
    internal void Advance(double now,int x,int y,bool valid)
    {
        if(!running)return;
        var pos=new NativeInput.Point{X=x,Y=y};
        cursorRows.Add((now,pos.X,pos.Y,valid,phase,plans[index].Id));
        if(phase is "travel" or "settle" or "hold")samples.Add(new(now,pos.X,pos.Y,phase=="settle"?"settle":phase,valid));
        if(!valid){entered=double.NaN;if(phase=="hold")InterruptHold();else if(phase=="settle")phaseSince=now;return;}
        var plan=plans[index];
        double RadiusAt(double px,double py,double cx,double cy)=>Math.Sqrt(Math.Pow(px-cx,2)+Math.Pow(py-cy,2));
        // Circles are WPF units; transform their radius into physical screen pixels.
        double radius=Screen(new Point(35,0)).X-Screen(new Point(0,0)).X;
        if(phase=="origin")
        {
            if(RadiusAt(pos.X,pos.Y,sx,sy)>radius){entered=double.NaN;return;}
            if(double.IsNaN(entered))entered=now;
            if(now-entered<.7)return;
            phase=plan.Rest?"settle":"travel";phaseSince=now;entered=double.NaN;
            // Use the actual start of the stroke for path measurement.
            sx=pos.X;sy=pos.Y;
            Point actualStart=Local(new Point(sx,sy));reference.X1=actualStart.X;reference.Y1=actualStart.Y;
            samples.Add(new(now,pos.X,pos.Y,"travel"));origin.Visibility=Visibility.Hidden;
            instruction.Text=plan.Rest?"Keep your head still. We will measure your resting steadiness.":$"{index+1}/{plans.Length} · Move {plan.Direction.ToLowerInvariant()} along the dashed line to the green circle.";
            detail.Text=plan.Rest?"Stay relaxed. Do not smile or deliberately click.":"Use your head only. Diagonal tasks should remain diagonal; no straight-line snapping is applied.";
        }
        else if(phase=="travel")
        {
            if(RadiusAt(pos.X,pos.Y,tx,ty)<=radius){phase="settle";phaseSince=now;}
            else if(now-phaseSince>20)CompleteTrial(false,"travel timed out");
        }
        else if(phase=="settle")
        {
            double cx=plan.Rest?sx:tx,cy=plan.Rest?sy:ty;
            if(!plan.Rest&&RadiusAt(pos.X,pos.Y,cx,cy)>radius){phase="travel";phaseSince=now;return;}
            if(now-phaseSince>=.4)
            {
                phase="hold";phaseSince=now;instruction.Text="Keep your head still now. Measuring vibration…";
                detail.Text="This hold is analysed separately from the movement into the target.";
            }
        }
        else if(phase=="hold")
        {
            // Once holding is instructed, record drift even beyond the circle.
            // Restarting on spatial drift would keep only the calmest segment.
            if(now-phaseSince>=(plan.Rest?restDuration:1.5)){CompleteTrial(true,"hold recorded after settling");return;}
        }
        if(now-lastDraw>.03)
        {
            lastDraw=now;var local=Local(new Point(pos.X,pos.Y));
            if(phase=="travel"&&trail.Points.Count<3000)trail.Points.Add(local);
            progress.Text=$"{results.Count}/{plans.Length} completed · {plan.Direction} · "+(phase=="hold"?$"still hold {now-phaseSince:F1}s":phase);
        }
    }
    void InterruptHold()
    {
        for(int i=0;i<samples.Count;i++)if(samples[i].Phase=="hold")samples[i]=samples[i] with{Phase="interrupted-hold"};
        phase="settle";phaseSince=clock();
        instruction.Text="Tracking was interrupted. Wait for it to return, then hold still again.";
    }
    void CompleteTrial(bool completed,string reason)
    {
        var plan=plans[index];results.Add(new(plan.Id,plan.Direction,completed,reason,sx,sy,tx,ty,MovementQualityAnalysis.Measure(samples,sx,sy,tx,ty),samples.ToArray()));index++;Next();
    }
    void StopTrace()
    {
        if(tracePath is null)return;
        try{if(labMarker is not null&&File.Exists(labMarker))File.Delete(labMarker);File.WriteAllText(tracePath+".stop","Test finished");}catch(IOException){ }
    }
    void End(string reason)
    {
        if(saved||!running)return;
        running=false;saved=true;finish.IsEnabled=false;StopTrace();
        if(index<plans.Length&&samples.Count>0)
        {var p=plans[index];results.Add(new(p.Id,p.Direction,false,reason,sx,sy,tx,ty,MovementQualityAnalysis.Measure(samples,sx,sy,tx,ty),samples.ToArray()));}
        if(preview)return;
        try
        {
            Directory.CreateDirectory(folder);string stem=System.IO.Path.Combine(folder,session);
            var summary=new{schema=2,holdBoundaryPolicy="fixed duration after settling; spatial drift is retained",createdUtc=DateTime.UtcNow,reason,synthetic=false,mode=traceExpected?"cursor-and-head":"cursor-only",headTrackingVerified=traceExpected,source="Physical cursor samples; use head control only. Physical helper input cannot be distinguished.",trials=results};
            string json=Protocol.Encode(summary);File.WriteAllText(stem+".json",json);
            using(var csv=new StreamWriter(stem+"-cursor.csv"))
            {csv.WriteLine("time_qpc,x_px,y_px,valid,phase,trial");foreach(var p in cursorRows)csv.WriteLine(FormattableString.Invariant($"{p.Time:R},{p.X:R},{p.Y:R},{p.Valid},{p.Phase},{p.Trial}"));}
            File.WriteAllText(stem+".html",ReportHtml.Create(results));
            lastReport=stem+".zip";
            using(var zip=ZipFile.Open(lastReport,ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(stem+".json","summary.json");zip.CreateEntryFromFile(stem+"-cursor.csv","cursor.csv");zip.CreateEntryFromFile(stem+".html","report.html");
                if(tracePath is not null&&File.Exists(tracePath))
                {using var source=new FileStream(tracePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var destination=zip.CreateEntry("head.jsonl").Open();source.CopyTo(destination);}
            }
            instruction.Text=$"Saved {results.Count(p=>p.Completed)}/{plans.Length} completed tasks.";
            detail.Text="Share the saved ZIP path for review. Qadr remains open; no movement settings were changed by this test.";
            progress.Text=lastReport;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){instruction.Text="Could not save the report";detail.Text=e.Message;}
    }

    public void DrawPreview()
    {
        instruction.Text="5/18 · Move right along the dashed line to the green circle.";
        detail.Text="Use your head only. After you arrive, hold still so travel and vibration can be measured separately.";
        connection.Text="Head tracking connected · numeric head and cursor recording";
        double w=canvas.ActualWidth,h=canvas.ActualHeight,y=h/2;
        Canvas.SetLeft(origin,w*.18-35);Canvas.SetTop(origin,y-35);Canvas.SetLeft(target,w*.82-35);Canvas.SetTop(target,y-35);
        reference.X1=w*.18;reference.Y1=y;reference.X2=w*.82;reference.Y2=y;
        for(int i=0;i<=80;i++)trail.Points.Add(new Point(w*.18+w*.48*i/80,y+Math.Sin(i*.2)*4));
        progress.Text="4/18 completed · Right · travel";finish.IsEnabled=true;
    }
    Point Screen(Point p)=>preview?p:canvas.PointToScreen(p);
    Point Local(Point p)=>preview?p:canvas.PointFromScreen(p);
    internal void StartSimulation(bool restOnly=false)=>Begin(restOnly);
    internal bool TestRunning=>running;
    internal string TestPhase=>phase;
    internal int TestIndex=>index;
    internal Point TestStart=>new(sx,sy);
    internal Point TestTarget=>new(tx,ty);
    internal TrialReport[] TestResults=>results.ToArray();
}
