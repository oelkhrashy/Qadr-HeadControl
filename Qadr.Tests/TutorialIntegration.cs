using Qadr.Core;
using System.Diagnostics;
internal static class TutorialIntegration
{
    static int checks;
    static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);checks++;}
    public static async Task<int> Run(string[] args){try{foreach(string mode in new[]{"skip","complete","stop","saved"})await Case(args,mode);Console.WriteLine($"{checks} tutorial integration checks passed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static async Task Case(string[] args,string mode){
        string session="Qadr-tour-test-"+Guid.NewGuid().ToString("N");string data=Path.Combine(Path.GetTempPath(),session);
        var info=new ProcessStartInfo(Arguments.Get(args,"--guardian")!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string s in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input","--test-source","--offer-tutorial"})info.ArgumentList.Add(s);
        using var process=Process.Start(info)!;SafetyGate? gate=null;SharedTransport? rpc=null;long sequence=0;EngineStatus? last=null;
        async Task<EngineStatus> Wait(Func<EngineStatus,bool> predicate){long start=Environment.TickCount64;while(Environment.TickCount64-start<15000){if(process.HasExited)throw new Exception("Guardian exited in "+mode);rpc!.Send(new("heartbeat"));var status=rpc.Read(ref sequence);if(status is not null)last=status;if(last is not null&&predicate(last))return last;await Task.Delay(30);}throw new Exception("Timeout in "+mode+"; last state "+last?.State);}
        void Send(string type,int? epoch=null)=>rpc!.Send(new(type,UserStopEpoch:epoch??gate!.UserStops));
        try{
            long started=Environment.TickCount64;while(rpc is null){try{gate??=new SafetyGate(session,false);rpc=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-started>15000)throw;await Task.Delay(30);}}gate!.SharedRpc=1;
            var choice=await Wait(s=>s.State=="TutorialChoice");Check(gate.Stopped&&choice.Preview is null,mode+": startup waits before opening camera or enabling control");
            if(mode is "skip" or "saved")Send("tutorialSkip");
            else{
                Send("tutorialStart");var step=await Wait(s=>s.State=="Tutorial"&&s.Cue=="tutorial:0");Check(gate.Stopped&&step.Preview is null,mode+": Yes opens the first lesson with input stopped");
                if(mode=="stop"){
                    int epoch=gate.UserStops;gate.UserStop();await Wait(s=>s.State=="Stopped");Send("tutorialSkip",epoch);Send("tutorialNext",epoch);
                    for(int i=0;i<20;i++){rpc.Send(new("heartbeat"));var status=rpc.Read(ref sequence);if(status is not null)last=status;await Task.Delay(30);}
                    Check(last?.State=="Stopped"&&gate.Stopped&&last.Preview is null,"Esc cancels tutorial and queued old answers cannot open a camera");return;
                }
                Send("tutorialNext");await Wait(s=>s.Cue=="tutorial:1");Send("tutorialBack");await Wait(s=>s.Cue=="tutorial:0");Check(gate.Stopped,"manual Next/Back repeats a tutorial lesson without input");
                for(int i=1;i<TutorialFlow.Pages.Length;i++){Send("tutorialNext");await Wait(s=>s.State=="Tutorial"&&s.Cue=="tutorial:"+i);}
                Check(last?.State=="Tutorial"&&gate.Stopped,"final tutorial page still waits for explicit Start");Send("tutorialNext");
            }
            var setup=await Wait(s=>s.State is "ChooseCamera" or "Positioning" or "Calibrating" || s.State=="Stopped"&&s.Help.Contains("No camera"));
            Check(setup.State!="Tutorial"&&setup.State!="TutorialChoice",mode+": choice proceeds directly to real camera setup");
            if(mode=="saved"&&setup.Cameras.Length>0){
                var original=new Profile{CoordinateVersion=3,Ranges=[[.09,.13],[.05,.08]]};var storage=new Storage(data);storage.Save("profiles/"+Storage.DeviceKey(setup.Cameras[0].Id)+".json",original);
                rpc.Send(new("camera",CameraId:setup.Cameras[0].Id,UserStopEpoch:gate.UserStops));await Wait(s=>s.State=="Ready");Check(last!.Title.Contains("saved")&&gate.Stopped,"saved per-camera profile goes to brief recovery without full range calibration");
                Check(storage.Load<Profile>("profiles/"+Storage.DeviceKey(setup.Cameras[0].Id)+".json")!.Ranges[0].SequenceEqual(original.Ranges[0]),"camera reopening preserves the saved directional ranges");return;
            }
            if(setup.State=="ChooseCamera"&&setup.Cameras.Length>0)rpc.Send(new("camera",CameraId:setup.Cameras[0].Id,UserStopEpoch:gate.UserStops));
            if(setup.Cameras.Length>0){await Wait(s=>s.Preview is not null);Check(gate.Stopped,mode+": selected camera starts synthetic preview/calibration with input disabled");}
        }finally{if(gate is not null){gate.Stop();gate.Closing=1;}if(!process.WaitForExit(4000))process.Kill(true);rpc?.Dispose();gate?.Dispose();}
    }
}
