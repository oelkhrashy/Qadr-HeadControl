using Qadr.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class HandsFreeIntegration
{
 [DllImport("user32.dll",SetLastError=true)]static extern bool PostThreadMessageW(uint thread,uint message,nuint wParam,nint lParam);
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);checks++;}
 public static async Task<int> Run(string[] args){try{await Case(args,true);await Case(args,false);Console.WriteLine($"{checks} hands-free process checks passed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static async Task Case(string[] args,bool saved){
  string session="Qadr-handsfree-"+Guid.NewGuid().ToString("N"),data=Path.Combine(Path.GetTempPath(),session);
  var storage=new Storage(data);storage.Save("settings.json",new Settings{TrackingFps=60,ClickMethod="dwell",EyebrowDoubleClick=true,EyebrowLearned=false,RightClickMethod="either",EyesLearned=false});
  if(saved)storage.Save("profiles/"+Storage.DeviceKey("diagnostic-camera")+".json",new Profile{CoordinateVersion=3});
  var info=new ProcessStartInfo(Arguments.Get(args,"--guardian")!){UseShellExecute=false,CreateNoWindow=true};
  foreach(string value in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input","--test-source","--diagnostic-control"})info.ArgumentList.Add(value);
  using var process=Process.Start(info)!;SafetyGate? gate=null;SharedTransport? rpc=null;long sequence=0,frame=0;EngineStatus? latest=null;double? exploration=null;bool blockedLesson=false,missing=false;double posture=.28;
  async Task Tick(){
   rpc!.Send(new("heartbeat"));var status=rpc.Read(ref sequence);if(status is not null){latest=status;blockedLesson|=status.State=="LearnClick";}
   double yaw=latest?.State is "Calibrating" or "Positioning"?0:posture,pitch=0;
   if(latest?.State=="Calibrating"&&latest.Title.Contains("Explore")){
    exploration??=Arguments.Now;double t=Arguments.Now-exploration.Value;yaw=Math.Sin(t*2)*.08;pitch=Math.Cos(t*2)*.08;
   }
   var matrix=new double[16];matrix[0]=Math.Cos(yaw);matrix[8]=-Math.Sin(yaw);matrix[9]=-Math.Sin(pitch);matrix[10]=Math.Cos(pitch);matrix[15]=1;
   rpc.Send(new("diagnosticResult",DiagnosticResult:new(1,"result",Sequence:++frame,Captured:Arguments.Now,Matrix:matrix,Blendshapes:new(){{"eyeBlinkLeft",.62},{"eyeBlinkRight",.62},{"mouthSmileLeft",.6},{"mouthSmileRight",.6},{"browInnerUp",.5}},Geometry:missing?null:new(.5,.45,.2,.5,.5,.3,.2,.7,.8,0))));
   await Task.Delay(33);
  }
  async Task Wait(Func<bool> predicate){long began=Environment.TickCount64;while(Environment.TickCount64-began<30000){if(process.HasExited)throw new Exception("Guardian exited");await Tick();if(predicate())return;}throw new Exception("Timeout; last="+latest?.State+" "+latest?.Title);}
  async Task StayStopped(){for(int i=0;i<30;i++)await Tick();Check(gate!.Stopped&&latest?.State=="Stopped","continuous face input cannot undo an explicit stop");}
  void Voice(string text,int? epoch=null)=>rpc!.Send(new("diagnosticVoice",Name:text,UserStopEpoch:epoch??gate!.UserStops));
  try{
   long began=Environment.TickCount64;while(rpc is null){try{gate??=new SafetyGate(session,false);rpc=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-began>15000)throw;await Task.Delay(30);}}gate!.SharedRpc=1;
   await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
   Check(!blockedLesson,saved?"saved profile automatically starts without a Resume click or optional gesture lesson":"new calibration automatically starts without a Resume click or optional gesture lesson");
   Check(latest!.CurrentSettings?.SpokenGuidance==false,"silent launch skips narration and reaches control despite neutral high eye scores and a shifted posture");
   Voice("start motion test");await Wait(()=>latest?.MovementRecording?.Recording==true);Voice("mark rest");for(int i=0;i<20;i++)await Tick();
   Check(latest!.CurrentSettings?.TrackingFps==60,"the staged Engine preserves the 60 FPS performance request");
   var at=NativeInput.Cursor;var testZone=new MouseZone("A",at.X-50,at.Y-50,100,100,1);
   rpc.Send(new("assessmentTarget",Name:"A",Action:"double",Zones:[testZone],UserStopEpoch:gate.UserStops));await Wait(()=>latest?.Assessment?.Target.Id=="A");
   rpc.Send(new("assessmentClick",Name:"A",Action:"left",InputX:at.X,InputY:at.Y,UserStopEpoch:gate.UserStops));for(int i=0;i<6;i++)await Tick();
   Check(latest!.Assessment?.Target.Id=="A"&&latest.AssessmentResult is null,"a staged double-click target waits after the first tap");
   rpc.Send(new("assessmentClick",Name:"A",Action:"double",InputX:at.X,InputY:at.Y,UserStopEpoch:gate.UserStops));await Wait(()=>latest?.AssessmentResult is {Id:"A",Completed:true});
   Check(latest!.Assessment is null,"a valid double-click event completes the staged labelled target once");
   string[] requestedActions=["move","left","double","left","move","double","left","move","double","left"];
   for(int i=1;i<10;i++){
    string id=((char)('A'+i)).ToString(),action=requestedActions[i];
    rpc.Send(new("assessmentTarget",Name:id,Action:action,Zones:[testZone],UserStopEpoch:gate.UserStops));await Wait(()=>latest?.Assessment?.Target.Id==id);
    if(action!="move")rpc.Send(new("assessmentClick",Name:id,Action:action,InputX:at.X,InputY:at.Y,UserStopEpoch:gate.UserStops));
    await Wait(()=>latest?.AssessmentResult?.Id==id&&latest.AssessmentResult.Completed);
    Check(latest!.Assessment is null,"staged target "+id+" completes only its required "+action+" action");
   }
   missing=true;await Wait(()=>latest?.State=="Degraded"&&gate.Stopped==false);posture=-.24;missing=false;await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);
   Check(!blockedLesson,"tracking loss recovers at a new comfortable posture without range calibration or gesture lessons");
   Voice("save motion test");await Wait(()=>latest?.MovementRecording?.Report is not null);
   Check(File.Exists(latest!.MovementRecording!.Report),"voice save exports a numeric movement report without interrupting control");
   using(var archive=System.IO.Compression.ZipFile.OpenRead(latest.MovementRecording.Report!)){
    using var reader=new StreamReader(archive.GetEntry("movement.jsonl")!.Open());string trace=reader.ReadToEnd();
    Check(trace.Contains("\"kind\":\"control\"")&&trace.Contains("\"kind\":\"frame\"")&&trace.Contains("\"face unavailable\""),"real Engine report pairs facial observations with controller output and tracking loss");
    using var summaryReader=new StreamReader(archive.GetEntry("summary.json")!.Open());using var summary=System.Text.Json.JsonDocument.Parse(summaryReader.ReadToEnd());
    Check(summary.RootElement.GetProperty("synthetic").GetBoolean(),"diagnostic process reports cannot be confused with a live customer test");
    Check(summary.RootElement.GetProperty("pointingTest").GetProperty("trials").GetArrayLength()==10,"exported staged report contains all ten labelled target outcomes");
   }
   Check((gate.Hotkeys&1)!=0&&latest!.StopShortcut.Contains("Esc"),"Guardian registers the primary Escape stop and reports it to the UI");
   int before=gate.UserStops;Voice("stop the app");await Wait(()=>gate.Stopped&&latest?.State=="Stopped");Check(gate.UserStops==before+1,"recognized stop the app reaches the explicit emergency stop gate");await StayStopped();
   Voice("resume",before);await StayStopped();Check(gate.ResumeRequest==0,"pre-stop resume intent is rejected without mouse input");
   rpc.Send(new("speechActivity",Enabled:true));for(int i=0;i<5;i++)await Tick();Voice("resume");await StayStopped();Check(gate.ResumeRequest==0,"the app's narrated resume cannot rearm pointer control");
   rpc.Send(new("speechActivity",Enabled:false));for(int i=0;i<24;i++)await Tick();Voice("resume");await Wait(()=>latest?.State=="Tracking"&&!gate.Stopped);Check(true,"fresh plain resume restores control after the short rest without any mouse click");
   before=gate.UserStops;Check(PostThreadMessageW((uint)process.Threads[0].Id,0x312,1,0),"OS hotkey notification is delivered to the Guardian message loop");await Wait(()=>gate.Stopped&&latest?.State=="Stopped");Check(gate.UserStops==before+1,"Escape hotkey handler stops control and advances the stop epoch");
   rpc.Send(new("shutdown"));Check(process.WaitForExit(4000),"hands-free diagnostic processes close cleanly without injecting input");
  }finally{if(gate is not null){gate.Stop();gate.Closing=1;}if(!process.WaitForExit(3000))process.Kill(true);rpc?.Dispose();gate?.Dispose();}
 }
}
