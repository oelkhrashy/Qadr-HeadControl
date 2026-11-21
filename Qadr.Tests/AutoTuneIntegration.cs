using Qadr.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

static class AutoTuneIntegration
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);checks++;Console.WriteLine("PASS: "+message);}
    public static async Task<int> Run(string[] args){
        try{
            string data=Path.Combine(Path.GetTempPath(),"Qadr-tuning-"+Guid.NewGuid().ToString("N"));
            var storage=new Storage(data);storage.Save("settings.json",new Settings{Sensitivity=.5,RelativeTravelBoost=3,RelativeSpeedLimit=1000,Mapping="relative",RightClickMethod="none",SpokenGuidance=false});
            storage.Save("profiles/"+Storage.DeviceKey("diagnostic-camera")+".json",new Profile{CoordinateVersion=3});
            await Case(args,data,false);await Case(args,data,true);
            Console.WriteLine($"{checks} automatic tuning process checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static async Task Case(string[] args,string data,bool reload){
        string session="Qadr-tuning-"+Guid.NewGuid().ToString("N");
        var info=new ProcessStartInfo(Arguments.Get(args,"--guardian")!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string value in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input","--test-source","--diagnostic-control","--launch-assessment-check","--tuning-test-evidence"})info.ArgumentList.Add(value);
        using var process=Process.Start(info)!;SafetyGate? gate=null;SharedTransport? rpc=null;EngineStatus? latest=null;long sequence=0,frame=0;
        async Task Tick(){rpc!.Send(new("heartbeat"));latest=rpc.Read(ref sequence)??latest;
            double[] matrix=new double[16];matrix[0]=matrix[10]=matrix[15]=1;
            rpc.Send(new("diagnosticResult",DiagnosticResult:new(1,"result",Sequence:++frame,Captured:Arguments.Now,Matrix:matrix,Geometry:new(.5,.45,.2,.5,.5,.3,.2,.7,.8,0))));await Task.Delay(33);
        }
        async Task Wait(Func<bool> predicate){long began=Environment.TickCount64;while(Environment.TickCount64-began<30000){if(process.HasExited)throw new Exception("Guardian exited");await Tick();if(predicate())return;}throw new Exception("Timeout: "+latest?.State+" / "+latest?.LaunchCheck?.Phase+" / "+latest?.Help);}
        void Send(string type,string? name=null,MouseZone[]? zones=null,string? action=null,int? epoch=null)=>rpc!.Send(new(type,Name:name,Zones:zones,Action:action,UserStopEpoch:epoch??gate!.UserStops));
        var point=NativeInput.Cursor;
        var canvas=new MouseZone("canvas",point.X-400,point.Y-300,1000,800,1);
        MouseZone Target(int i,bool changed=false){(double X,double Y)[] offsets=[(100,100),(-250,100),(450,100),(100,-200),(100,400)];var at=offsets[i];return new("target",point.X+at.X-40+(changed?1:0),point.Y+at.Y-40,80,80,1);}
        AssessmentResult Result(string id,int overshoots=2)=>new(id,"move",true,"synthetic test fixture",2000,1350,300,.6,overshoots+1,overshoots,20,8,2,0,0,0,40);
        async Task Trial(char id,int index,int overshoots=2){Send("assessmentTarget",id.ToString(),[Target(index)],"move");await Wait(()=>latest?.Assessment?.Target.Id==id.ToString());Send("diagnosticAssessmentResult",Protocol.Encode(Result(id.ToString(),overshoots)));await Wait(()=>latest?.AssessmentResult?.Id==id.ToString());}
        async Task Begin(){Send("launchAssessmentBegin",zones:[canvas]);await Wait(()=>latest?.LaunchCheck is {Phase:"running",Attempted:0});}
        async Task Baseline(){foreach(int i in Enumerable.Range(0,5))await Trial((char)('A'+i),i);Send("launchAssessmentFinish");await Wait(()=>latest?.LaunchCheck?.Phase=="validating");}
        string savedPath="movement-tuning/"+Storage.DeviceKey("diagnostic-camera")+".json";var storage=new Storage(data);
        try{
            long began=Environment.TickCount64;while(rpc is null){try{gate??=new SafetyGate(session,false);rpc=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-began>15000)throw;await Task.Delay(30);}}gate!.SharedRpc=1;
            await Wait(()=>latest?.LaunchCheck?.Phase=="ready"&&!gate.Stopped);
            Check(latest!.LaunchCheck!.Required,"automatic tuning retains the mandatory check on each process launch");
            if(reload){
                Check(latest.CurrentSettings is {Sensitivity:.45,RelativeTravelBoost:3}&&latest.MovementTuning?.Phase=="loaded","verified camera movement reloads without changing global settings");
                await Begin();foreach(char id in "ABCDE"){Send("assessmentTarget",id.ToString(),[Target(id-'A')],"move");await Wait(()=>latest?.Assessment?.Target.Id==id.ToString());Send("assessmentSkip");await Wait(()=>latest?.AssessmentResult?.Id==id.ToString());}
                Send("launchAssessmentFinish");await Wait(()=>latest?.LaunchCheck?.Phase=="complete");
                Send("diagnosticVoice","undo movement settings");await Wait(()=>latest?.MovementTuning?.Phase=="undone");
                Check(latest!.CurrentSettings is {Sensitivity:.5,ClickMethod:"dwell",SpokenGuidance:false},"hands-free undo restores prior movement and preserves gestures and silent startup");
                await Wait(()=>storage.Load<TunedMovementProfile>(savedPath)?.Current.Sensitivity==.5);
                Check(true,"undo is persisted to the camera movement profile");
            }else{
                await Begin();await Baseline();
                Check(latest!.CurrentSettings is {Sensitivity:.45,ClickMethod:"none"}&&latest.LaunchCheck!.Required,"candidate settings are applied provisionally while click gestures remain suspended");
                Check(storage.Load<TunedMovementProfile>(savedPath) is null,"an unverified candidate is not persisted");
                Send("assessmentTarget","A",[Target(0)],"move");for(int i=0;i<5;i++)await Tick();
                Check(latest!.Assessment is null,"stale first-round targets cannot enter the validation round");
                gate.UserStop();await Wait(()=>latest?.State=="Stopped"&&latest.LaunchCheck?.Phase=="interrupted");
                Check(latest!.CurrentSettings is {Sensitivity:.5,ClickMethod:"dwell"}&&storage.Load<TunedMovementProfile>(savedPath) is null,"Escape restores baseline settings and leaves no provisional profile");
                Send("diagnosticVoice","resume");await Wait(()=>latest?.LaunchCheck?.Phase=="ready"&&!gate.Stopped);await Begin();await Baseline();
                Send("assessmentTarget","F",[Target(0,true)],"move");await Wait(()=>latest?.State=="Stopped"&&latest.LaunchCheck?.Phase=="interrupted");
                Check(latest!.CurrentSettings!.Sensitivity==.5&&storage.Load<TunedMovementProfile>(savedPath) is null,"changed validation geometry stops and restores the prior settings");
                Send("diagnosticVoice","resume");await Wait(()=>latest?.LaunchCheck?.Phase=="ready"&&!gate.Stopped);await Begin();await Baseline();
                foreach(int i in Enumerable.Range(0,5))await Trial((char)('F'+i),i,2);
                Send("launchAssessmentFinish");await Wait(()=>latest?.LaunchCheck?.Phase=="complete"&&latest.MovementRecording?.Report is not null);
                Check(latest!.MovementTuning?.Phase=="reverted"&&latest.CurrentSettings!.Sensitivity==.5,"a full repeat with no improvement rolls back automatically");
                Check(storage.Load<TunedMovementProfile>(savedPath) is null,"a rejected candidate creates no verified profile");
                // Start a fresh launch process for the accepted case by explicitly
                // restarting only the launch flow in diagnostic-only test mode.
                Send("diagnosticRestartLaunch");await Wait(()=>latest?.LaunchCheck?.Phase=="ready");await Begin();await Baseline();
                foreach(int i in Enumerable.Range(0,5))await Trial((char)('F'+i),i,1);
                Send("launchAssessmentFinish");await Wait(()=>latest?.LaunchCheck?.Phase=="complete"&&latest.MovementTuning?.Phase=="applied"&&latest.MovementRecording?.Report is not null);
                Check(latest!.CurrentSettings is {Sensitivity:.45,ClickMethod:"dwell",RelativeTravelBoost:3},"measured improvement commits movement while restoring existing click control");
                await Wait(()=>storage.Load<TunedMovementProfile>(savedPath) is not null);
                var saved=storage.Load<TunedMovementProfile>(savedPath)!;
                Check(saved.Synthetic&&saved.Previous.Sensitivity==.5&&saved.Current.Sensitivity==.45,"saved experimental profiles include rollback values and a synthetic marker");
                Check(!saved.Matches(saved.Context,new Settings{Sensitivity=.5,RelativeTravelBoost=3}),"synthetic test profiles cannot load in real camera operation");
                using(var zip=ZipFile.OpenRead(latest.MovementRecording!.Report!)){
                    using var reader=new StreamReader(zip.GetEntry("summary.json")!.Open());using var summary=JsonDocument.Parse(reader.ReadToEnd());
                    Check(summary.RootElement.GetProperty("synthetic").GetBoolean()&&summary.RootElement.GetProperty("pointingTest").GetProperty("trials").GetArrayLength()==10,"the saved experiment contains both rounds and is explicitly synthetic");
                    Check(summary.RootElement.GetProperty("movementTuning").EnumerateArray().Any(d=>d.GetProperty("phase").GetString()=="applied"),"numeric reports include the applied movement decision");
                }
                Check(storage.Load<Settings>("settings.json") is {Sensitivity:.5,RelativeTravelBoost:3},"per-camera automatic tuning preserves original global settings");
                rpc.Send(new("settings",Settings:latest.CurrentSettings! with{ScrollNotches=2}));await Wait(()=>latest?.CurrentSettings?.ScrollNotches==2);
                await Wait(()=>storage.Load<Settings>("settings.json")?.ScrollNotches==2);
                Check(latest!.CurrentSettings!.Sensitivity==.45&&storage.Load<Settings>("settings.json")!.Sensitivity==.5,"editing a non-movement preference keeps the verified overlay and original anchor");
            }
            if(reload){
                var before=latest!.CurrentSettings!;
                Send("adjustMovement","slower");await Wait(()=>latest?.Adjustment?.CanUndo==true&&Math.Abs(latest.CurrentSettings!.Sensitivity-before.Sensitivity*.9)<.0001);
                Check(latest!.CurrentSettings!.ClickMethod==before.ClickMethod,"Help me adjust changes speed without changing the click binding");
                await Wait(()=>Math.Abs((storage.Load<Settings>("settings.json")?.Sensitivity??0)-before.Sensitivity*.9)<.0001);
                Check(true,"manual adjustment is persisted by Engine");
                rpc!.Send(new("settings",Settings:latest.CurrentSettings! with{BlinkThreshold=.7}));await Wait(()=>latest?.CurrentSettings?.BlinkThreshold==.7);
                Send("undoAdjustment");await Wait(()=>latest?.Adjustment?.CanUndo==false&&latest.CurrentSettings?.Sensitivity==before.Sensitivity);
                Check(latest!.CurrentSettings!.BlinkThreshold==.7,"Engine Undo preserves a later gesture preference");
                await Wait(()=>storage.Load<Settings>("settings.json") is {BlinkThreshold:.7} saved&&saved.Sensitivity==before.Sensitivity);
                Check(true,"Undo is persisted without restoring stale gesture preferences");
                int epoch=gate!.UserStops;gate.UserStop();await Wait(()=>latest?.State=="Stopped");Send("adjustMovement","slower",epoch:epoch);for(int i=0;i<10;i++)await Tick();
                Check(latest!.CurrentSettings!.Sensitivity==before.Sensitivity&&gate.Stopped,"Escape rejects a queued stale adjustment and never resumes control");
            }
            Send("shutdown");Check(process.WaitForExit(4000),"automatic tuning test exits without native pointer input");
        }finally{if(gate is not null){gate.Stop();gate.Closing=1;}if(!process.WaitForExit(3000))process.Kill(true);rpc?.Dispose();gate?.Dispose();}
    }
}
