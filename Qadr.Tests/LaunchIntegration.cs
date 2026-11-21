using Qadr.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

static class LaunchIntegration
{
    static int checks;
    static void Check(bool valid,string message){if(!valid)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);checks++;}
    public static async Task<int> Run(string[] args){
        try{
            string data=Path.Combine(Path.GetTempPath(),"Qadr-launch-"+Guid.NewGuid().ToString("N"));
            var store=new Storage(data);store.Save("settings.json",new Settings{Sensitivity=.5,RelativeTravelBoost=3,RelativeSpeedLimit=1000,Mapping="relative",RightClickMethod="none",MotionDiagnosticsOnStartup=true});
            store.Save("profiles/"+Storage.DeviceKey("diagnostic-camera")+".json",new Profile{CoordinateVersion=3});
            await Case(args,data,true);await Case(args,data,false);
            Console.WriteLine($"{checks} launch assessment process checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static async Task Case(string[] args,string data,bool full){
        string session="Qadr-launch-"+Guid.NewGuid().ToString("N");
        var info=new ProcessStartInfo(Arguments.Get(args,"--guardian")!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string s in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input","--test-source","--diagnostic-control","--launch-assessment-check"})info.ArgumentList.Add(s);
        using var process=Process.Start(info)!;SafetyGate? gate=null;SharedTransport? rpc=null;EngineStatus? latest=null;long sequence=0,frame=0;bool missingFace=false;double headYaw=.12;
        async Task Tick(){rpc!.Send(new("heartbeat"));latest=rpc.Read(ref sequence)??latest;
            double yaw=headYaw;double[] m=new double[16];m[0]=Math.Cos(yaw);m[8]=-Math.Sin(yaw);m[10]=m[15]=1;
            rpc.Send(new("diagnosticResult",DiagnosticResult:missingFace?new(1,"result",Sequence:++frame,Captured:Arguments.Now):new(1,"result",Sequence:++frame,Captured:Arguments.Now,Matrix:m,Geometry:new(.5,.45,.2,.5,.5,.3,.2,.7,.8,0))));await Task.Delay(33);
        }
        async Task Wait(Func<bool> condition){long began=Environment.TickCount64;while(Environment.TickCount64-began<30000){if(process.HasExited)throw new Exception("Guardian exited");await Tick();if(condition())return;}throw new Exception("Timeout: "+latest?.State+" phase "+latest?.LaunchCheck?.Phase);}
        void Send(string type,string? name=null,MouseZone[]? zones=null,string? action=null,int? epoch=null)=>rpc!.Send(new(type,Name:name,Zones:zones,Action:action,UserStopEpoch:epoch??gate!.UserStops));
        void Target(string id,bool inside=true){var at=NativeInput.Cursor;Send("assessmentTarget",id,[new(id,at.X+(inside?-40:250),at.Y-40,80,80,1)],"move");}
        try{
            long began=Environment.TickCount64;while(rpc is null){try{gate??=new SafetyGate(session,false);rpc=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-began>15000)throw;await Task.Delay(30);}}gate!.SharedRpc=1;
            await Wait(()=>latest?.LaunchCheck?.Phase=="ready"&&!gate.Stopped);
            Check(latest!.AssessmentRequest=="launch"&&latest.LaunchCheck!.Required,full?"startup automatically requires the quick check":"opening the same saved profile in a new process requires the check again");
            if(!full){Send("stop");await Wait(()=>gate.Stopped);Send("shutdown");Check(process.WaitForExit(4000),"second launch closes without injecting input");return;}
            Target("A");Send("launchAssessmentFinish");for(int i=0;i<5;i++)await Tick();
            Check(latest!.Assessment is null&&latest.LaunchCheck!.Required,"ordinary target and early completion cannot bypass mandatory setup");
            var cursor=NativeInput.Cursor;var canvas=new MouseZone("canvas",cursor.X-400,cursor.Y-300,1000,800,1);
            Send("launchAssessmentBegin",zones:[canvas],epoch:gate.UserStops-1);for(int i=0;i<5;i++)await Tick();
            Check(latest!.LaunchCheck!.Phase=="ready","a stale begin request cannot arm the launch check");
            Send("launchAssessmentBegin",zones:[canvas]);await Wait(()=>latest?.LaunchCheck?.Phase=="running");
            Check(latest!.CurrentSettings?.ClickMethod=="none"&&latest.CurrentSettings.RightClickMethod=="none"&&!latest.CurrentSettings.EyebrowDoubleClick,"click gestures are temporarily suspended for movement-only startup testing");
            Send("diagnosticVoice","resume");for(int i=0;i<8;i++)await Tick();Check(latest!.State=="Tracking"&&latest.LaunchCheck!.Phase=="running"&&latest.CurrentSettings!.ClickMethod=="none","resume while control is already running cannot interrupt a target or rebase movement");
            Target("A");await Wait(()=>latest?.AssessmentResult?.Id=="A");Check(latest!.AssessmentResult!.Completed,"a real Engine target accepts a reliable held cursor sample");
            Target("B",false);await Wait(()=>latest?.Assessment?.Target.Id=="B");missingFace=true;await Wait(()=>latest?.AssessmentResult?.Id=="B");missingFace=false;Check(latest!.AssessmentResult is {Completed:false,Reason:"time limit"},"an unreachable target advances after eight seconds without claiming a hit");
            Check(latest!.AssessmentResult!.DurationMs<9000&&latest.AssessmentResult.TrackingLosses>0,"eight-second wall-clock deadline survives continuous missing-face observations");
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Target("C");await Wait(()=>latest?.Assessment?.Target.Id=="C");Send("diagnosticVoice","skip target");await Wait(()=>latest?.AssessmentResult?.Id=="C");
            Check(latest!.LaunchCheck!.Attempted==3&&latest.LaunchCheck.Passed==1&&latest.LaunchCheck.Skipped==2,"voice skip and timeout remain distinct from success");
            int oldEpoch=gate.UserStops;Target("D");await Wait(()=>latest?.Assessment?.Target.Id=="D");gate.UserStop();
            await Wait(()=>latest?.State=="Stopped"&&latest.LaunchCheck?.Phase=="interrupted"&&latest.MovementRecording?.Report is not null);
            string first=latest!.MovementRecording!.Report!;
            using(var z=ZipFile.OpenRead(first)){using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();Check(trace.Split("\"kind\":\"startup-centre\"").Length==2,"startup plans exactly one monitor-centre movement in the synthetic report");}
            Check(latest.CurrentSettings!.ClickMethod=="dwell","Escape restores original gesture settings while keeping the test required");
            Send("launchAssessmentFinish",epoch:oldEpoch);Send("diagnosticVoice","resume",epoch:oldEpoch);for(int i=0;i<15;i++)await Tick();
            Check(gate.Stopped&&latest.LaunchCheck!.Required,"pre-stop completion and resume cannot undo Escape");
            Send("diagnosticVoice","resume");await Wait(()=>latest?.LaunchCheck?.Phase=="ready"&&!gate.Stopped);
            Send("launchAssessmentBegin",zones:[canvas]);await Wait(()=>latest?.LaunchCheck?.Phase=="running"&&latest.LaunchCheck.Attempted==0);
            Check(true,"fresh voice resume restarts the quick assessment without a mouse click");
            foreach(char id in "ABCDE"){Target(id.ToString());await Wait(()=>latest?.Assessment?.Target.Id==id.ToString());Send("assessmentSkip");await Wait(()=>latest?.AssessmentResult?.Id==id.ToString());}
            Send("launchAssessmentFinish");await Wait(()=>latest?.LaunchCheck?.Phase=="complete"&&latest.MovementRecording?.Report is not null);
            Check(latest!.LaunchCheck is {Passed:0,Skipped:5,Required:false},"five attempted targets permit normal use without fabricating successful outcomes");
            Check(latest.CurrentSettings is {ClickMethod:"dwell",Sensitivity:.5,RelativeTravelBoost:3},"completing setup preserves the owner's movement and click settings");
            using(var z=ZipFile.OpenRead(latest.MovementRecording!.Report!)){
                using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();Check(!trace.Contains("\"kind\":\"startup-centre\""),"resume after interruption does not repeat the startup pointer jump");
                using var sr=new StreamReader(z.GetEntry("summary.json")!.Open());using var summary=JsonDocument.Parse(sr.ReadToEnd());Check(summary.RootElement.GetProperty("pointingTest").GetProperty("trials").GetArrayLength()==5&&summary.RootElement.GetProperty("synthetic").GetBoolean(),"saved mandatory assessment contains five outcomes and is explicitly synthetic in diagnostics");
            }
            Check(!Directory.GetFiles(Path.Combine(data,"movement-reports"),"*.partial").Any(),"completed reports expose only finalized ZIP archives");
            await Wait(()=>latest?.MovementRecording?.Saving!=true);
            rpc!.Send(new("motionRecord",Enabled:true,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.MovementRecording?.Recording==true);
            rpc.Send(new("diagnosticResult",DiagnosticResult:new(1,"result",Sequence:++frame,Captured:Arguments.Now)));
            await Wait(()=>latest?.State is "Degraded" or "Lost" or "Ready");
            Check(latest!.State!="Tracking"&&!latest.Dragging&&!latest.HeadScrolling,"tracking loss pauses control and clears held mouse modes before recovery");
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Check(latest!.LaunchCheck is {Required:false},"stable recovery restores control without repeating the finished startup test");
            for(int i=0;i<12;i++)await Tick();
            rpc.Send(new("motionRecord",Enabled:false,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.MovementRecording is {Recording:false,Saving:false,Report:not null});
            using(var z=ZipFile.OpenRead(latest!.MovementRecording!.Report!)){
                using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();
                Check(trace.Split("\"kind\":\"startup-centre\"").Length==2,"stable recovery plans exactly one new monitor-centre movement outside assessments");
            }
            rpc.Send(new("settings",Settings:latest.CurrentSettings! with{CentreOnRecovery=false,PreviewFps=15}));
            await Wait(()=>latest?.CurrentSettings is {CentreOnRecovery:false,PreviewFps:15});
            Check(latest!.CurrentSettings is {Sensitivity:.5,RelativeTravelBoost:3,ClickMethod:"dwell"},"preview and recovery preferences preserve movement and click configuration");
            rpc.Send(new("motionRecord",Enabled:true,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.MovementRecording?.Recording==true);
            missingFace=true;await Wait(()=>latest?.State is "Degraded" or "Lost");missingFace=false;
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);for(int i=0;i<8;i++)await Tick();
            rpc.Send(new("motionRecord",Enabled:false));await Wait(()=>latest?.MovementRecording is {Recording:false,Saving:false,Report:not null});
            using(var z=ZipFile.OpenRead(latest!.MovementRecording!.Report!)){
                using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();
                Check(!trace.Contains("\"kind\":\"startup-centre\""),"keep-position recovery does not plan a cursor jump");
            }
            rpc.Send(new("motionRecord",Enabled:true,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.MovementRecording?.Recording==true);
            Send("diagnosticVoice","centre pointer");await Wait(()=>latest?.State=="Ready");await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);for(int i=0;i<8;i++)await Tick();
            rpc.Send(new("motionRecord",Enabled:false));await Wait(()=>latest?.MovementRecording is {Recording:false,Saving:false,Report:not null});
            using(var z=ZipFile.OpenRead(latest!.MovementRecording!.Report!)){
                using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();
                Check(trace.Split("\"kind\":\"startup-centre\"").Length==2,"explicit voice recentre still plans one centre when automatic centring is off");
            }
            // Exercise the staged Engine, not just the pure mapper. The diagnostic
            // transport supplies poses; --no-input prevents physical pointer events.
            rpc.Send(new("settings",Settings:latest.CurrentSettings! with{Mapping="position",Sensitivity=.45,ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false}));
            await Wait(()=>latest?.CurrentSettings?.Mapping=="position");
            Send("diagnosticVoice","centre pointer");await Wait(()=>latest?.State=="Ready");await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            rpc.Send(new("motionRecord",Enabled:true,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.MovementRecording?.Recording==true);
            for(int i=0;i<20;i++)await Tick();
            headYaw=.30;for(int i=0;i<35;i++)await Tick();
            missingFace=true;await Wait(()=>latest?.State is "Degraded" or "Lost");missingFace=false;
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            for(int i=0;i<20;i++)await Tick();headYaw=.12;for(int i=0;i<35;i++)await Tick();
            rpc.Send(new("motionRecord",Enabled:false));await Wait(()=>latest?.MovementRecording is {Recording:false,Saving:false,Report:not null});
            using(var z=ZipFile.OpenRead(latest!.MovementRecording!.Report!)){
                using var reader=new StreamReader(z.GetEntry("movement.jsonl")!.Open());
                var measurements=reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(Protocol.Decode<MovementMeasurement>).ToArray();
                var outputs=measurements.Where(m=>m.Kind=="control"&&m.Output?.PositionX is not null&&!m.Output.Frozen).Select(m=>m.Output!).ToArray();
                Check(outputs.Length>30,"position: staged Engine records absolute targets with native input disabled");
                Check(outputs.Any(o=>o.PositionX>.99),"position: staged rightward pose reaches the monitor edge");
                Check(outputs.TakeLast(8).All(o=>Math.Abs(o.PositionX!.Value)<.001),"position: neutral return after sideways tracking recovery retains the original screen centre");
                Check(outputs.All(o=>o.Dx==0&&o.Dy==0),"position: staged control does not accumulate relative movement");
                Check(!measurements.Any(m=>m.Kind=="startup-centre"),"position: ordinary tracking recovery does not silently redefine the comfortable origin");
            }
            Send("stop");await Wait(()=>gate.Stopped&&latest?.State=="Stopped");Check(gate.Stopped,"position: explicit stop closes the staged input gate");
            Send("shutdown");Check(process.WaitForExit(4000),"launch assessment processes shut down without injecting pointer input");
        }finally{if(gate is not null){gate.Stop();gate.Closing=1;}if(!process.WaitForExit(3000))process.Kill(true);rpc?.Dispose();gate?.Dispose();}
    }
}
