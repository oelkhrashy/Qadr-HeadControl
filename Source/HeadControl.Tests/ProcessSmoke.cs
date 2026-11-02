using Qadr.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

static class ProcessSmoke
{
    public static async Task Run(string application,string data)
    {
        string session="Qadr-head-control-test-"+Guid.NewGuid().ToString("N");
        var storage=new Storage(data);
        storage.Save("settings.json",new Settings {ClickMethod="smile",SmileLearned=true,EyesLearned=true,HandsFreeVoice=false,EyebrowDoubleClick=false});
        storage.Save("profiles/"+Storage.DeviceKey("diagnostic-camera")+".json",new Profile{CoordinateVersion=3});
        var info=new ProcessStartInfo(Path.Combine(application,"Qadr.Guardian.exe")) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=application};
        foreach(string value in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input","--test-source","--diagnostic-control"})info.ArgumentList.Add(value);
        using var process=Process.Start(info)!;
        SafetyGate? gate=null;SharedTransport? rpc=null;EngineStatus? latest=null;long sequence=0,frame=0;
        double smile=0,eye=0,offset=0,yaw=0,noseOffset=0;bool missing=false,suspendResults=false;int checks=0;
        async Task Tick()
        {
            if(process.HasExited)throw new Exception("Diagnostic Guardian exited early; inspect "+data);
            rpc!.Send(new("heartbeat"));
            var status=rpc.Read(ref sequence);if(status is not null)latest=status;
            double[] matrix=[Math.Cos(yaw),0,Math.Sin(yaw),0,0,1,0,0,-Math.Sin(yaw),0,Math.Cos(yaw),0,0,0,0,1];
            if(!suspendResults)rpc.Send(new("diagnosticResult",DiagnosticResult:new(1,"result",Sequence:++frame,Captured:Arguments.Now,Matrix:matrix,
                Blendshapes:new(){{"mouthSmileLeft",smile},{"mouthSmileRight",smile},{"eyeBlinkLeft",eye},{"eyeBlinkRight",0}},
                Geometry:missing?null:new(.5+offset,.45,.2,.5+offset+noseOffset,.5,.3+offset,.2,.7+offset,.8,0))));
            await Task.Delay(33);
        }
        async Task Wait(Func<bool> predicate,int timeout=20000)
        {
            long start=Environment.TickCount64;
            do {await Tick();if(predicate())return;}while(Environment.TickCount64-start<timeout);
            throw new Exception("Diagnostic timeout: "+latest?.State+" / "+latest?.Title);
        }
        async Task Frames(int n) {for(int i=0;i<n;i++)await Tick();}
        void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);checks++;Console.WriteLine("PASS: "+text);}
        try
        {
            long start=Environment.TickCount64;
            while(rpc is null)
            {
                try {gate??=new SafetyGate(session,false);rpc=new SharedTransport(session,false);}
                catch(FileNotFoundException){if(Environment.TickCount64-start>15000)throw;await Task.Delay(30);}
            }
            gate!.SharedRpc=1;
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Check(latest!.CurrentSettings is {Mapping:"relative",MovementAlgorithm:"adaptive"},"supplied Guardian and Engine load the modified controller and reach Tracking with synthetic observations");
            Check(latest.CurrentSettings!.ClickMethod=="smile","the real Engine accepts smile left-click settings");
            rpc.Send(new("motionRecord",Enabled:true,UserStopEpoch:gate.UserStops));
            await Wait(()=>latest?.MovementRecording?.Recording==true);
            await Frames(12);smile=.9;await Frames(24);smile=0;await Frames(20);
            eye=.9;await Frames(20);eye=0;await Frames(20);
            missing=true;await Wait(()=>latest?.State=="Degraded");
            Check(true,"the real Engine pauses on lost tracking");
            missing=false;offset=.12;await Frames(55);
            Check(latest?.State!="Tracking","recovery waits for a stable off-centre face to align instead of rebasing to it");
            offset=0;yaw=.2;await Frames(40);
            Check(latest?.State!="Tracking","recovery waits for the resting head angle even when the face centre is aligned");
            yaw=0;noseOffset=.10;await Frames(40);
            Check(latest?.State!="Tracking","recovery waits for the nose reference when centre and angle alone look aligned");
            noseOffset=0;await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Check(true,"the real Engine recovers after stable neutral observations");
            rpc.Send(new("motionRecord",Enabled:false));await Wait(()=>latest?.MovementRecording?.Report is not null);
            string report=latest!.MovementRecording!.Report!;
            using(var zip=ZipFile.OpenRead(report))
            using(var reader=new StreamReader(zip.GetEntry("movement.jsonl")!.Open()))
            {
                var gestures=new List<string>();int centres=0;string? line;
                while((line=reader.ReadLine()) is not null)
                {
                    using var row=JsonDocument.Parse(line);var root=row.RootElement;
                    if(root.GetProperty("kind").GetString()=="gesture")gestures.Add(root.GetProperty("label").GetString()!);
                    if(root.GetProperty("kind").GetString()=="startup-centre")centres++;
                }
                Check(gestures.Count(v=>v=="left")==1,"the real Engine records exactly one left-click intent from one smile");
                Check(gestures.Count(v=>v=="right")==1,"the real Engine records exactly one right-click intent from one wink");
                Check(centres==1,"the real Engine plans exactly one pointer centre after aligned recovery in no-input mode");
            }
            suspendResults=true;await Wait(()=>latest?.State=="Lost");
            Check(true,"a missing-result interval pauses control without requiring an explicit no-face result");
            suspendResults=false;offset=.12;await Frames(45);
            Check(latest?.State!="Tracking","missing-result recovery also waits for face alignment");
            offset=0;await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Check(true,"aligned rest restores control after a missing-result interval");
            missing=true;await Wait(()=>latest?.State=="Degraded");
            missing=false;offset=.12;yaw=.25;
            long fallbackStarted=Environment.TickCount64;
            await Frames(65);
            Check(latest?.State!="Tracking"&&latest?.Title.Contains("adopt") is true,"turned recovery remains paused with hands-free adoption guidance before four seconds");
            await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped,8000);
            Check(Environment.TickCount64-fallbackStarted>=4000,"a stable new posture is adopted only after the four-second fallback and resting check");
            missing=true;await Wait(()=>latest?.State=="Degraded");missing=false;smile=.9;
            await Frames(155);
            Check(latest?.State!="Tracking","a smile held through recovery cannot become the new resting expression or complete the fallback");
            smile=0;eye=.9;await Frames(35);
            Check(latest?.State!="Tracking","a wink held through recovery cannot become the new resting expression");
            eye=0;await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
            Check(true,"gesture release allows recovery without restarting the application");
            missing=true;await Wait(()=>latest?.State=="Degraded");
            rpc.Send(new("stop"));await Wait(()=>gate.Stopped&&latest?.State=="Stopped");missing=false;await Frames(35);
            Check(gate.Stopped&&latest?.State=="Stopped","an explicit Stop during tracking loss cannot be undone by an aligned returning face");
            rpc.Send(new("shutdown"));Check(process.WaitForExit(5000),"supplied application processes shut down cleanly");
            Console.WriteLine($"{checks} process smoke checks passed. --no-input and synthetic diagnostic observations; no webcam, microphone or desktop clicks were used.");
        }
        finally
        {
            if(gate is not null){gate.Stop();gate.Closing=1;}
            if(!process.WaitForExit(3000))process.Kill(true);
            rpc?.Dispose();gate?.Dispose();
        }
    }
}
