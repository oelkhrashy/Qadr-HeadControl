using Qadr.Core;
using System.IO.Compression;
using System.Text.Json;

static class DiagnosticsVerification
{
 public static void Run(Action<bool,string> check){
  check(new Settings().TrackingFps==30&&!new Settings().MotionDiagnosticsOnStartup,"new customer settings keep balanced 30 FPS and diagnostic recording off");
  check(new Settings{TrackingFps=60}.Validate().TrackingFps==60&&new Settings{TrackingFps=144}.Validate().TrackingFps==30,"only supported 30 and 60 FPS requests survive validation");
  check(CaptureMode.Score(640,480,60,60)>CaptureMode.Score(640,480,30,60),"high-rate mode selects supported 60 FPS at equal face resolution");
  check(CaptureMode.Score(640,480,30,60)>CaptureMode.Score(640,480,15,60),"high-rate mode falls back to a real available 30 FPS format");
  check(CaptureMode.Score(640,480,30,60)>CaptureMode.Score(320,240,60,60),"60 FPS selection does not sacrifice most face detail for its FPS number");
  check(VoiceControl.MovementPhrases.All(VoiceControl.Phrases(true,false,[]).Contains),"movement test commands are available without routine voice recording");
  string directory=Path.Combine(Path.GetTempPath(),"Qadr-diagnostics-"+Guid.NewGuid().ToString("N"));
  using(var recorder=new MovementRecorder(directory)){
   recorder.Add(new("frame",1));check(!Directory.Exists(directory),"disabled diagnostics never create a recording file");
   check(recorder.Start(1,true)&&!recorder.Start(1,true),"only one recording runs at a time");
   recorder.Add(new("configuration",1,Profile:new(){CoordinateVersion=3},MovementSettings:new(){TrackingFps=60}));
   recorder.Add(new("label",1,Label:"rest"));
   for(int i=0;i<20;i++)recorder.Add(new("frame",1+i/60d,State:"Tracking",Face:new(0,0,0,0,0,1+i/60d),QualityAccepted:true,CursorX:100+i%2,CursorY:100,ProcessingMs:5,InferenceMs:2));
   recorder.Add(new("frame",1.4,State:"Degraded",QualityAccepted:false,Reason:"face unavailable"));
   recorder.Add(new("pointer",1.5,RequestedX:120,RequestedY:100,CursorX:119,CursorY:100,NativeAccepted:true));
   recorder.Add(new("gesture",1.6,Label:"left"));
   recorder.Add(new("target",2,TargetX:100,TargetY:100,TargetWidth:60,TargetHeight:60));
   recorder.Add(new("target-hit",3,State:"Tracking",TargetX:100,TargetY:100,TargetWidth:60,TargetHeight:60,CursorX:130,CursorY:130));
   recorder.Stop();check(recorder.Completion.Wait(8000),"stopping flushes the background writer and creates a report");
   check(recorder.Status.Report is not null&&!recorder.Recording&&!recorder.Status.Saving&&recorder.Status.Error is null,"saved status exposes the exact ZIP with recording stopped");
   using var zip=ZipFile.OpenRead(recorder.Status.Report!);check(zip.Entries.Count==3&&zip.Entries.All(e=>e.Name is "movement.jsonl" or "summary.json" or "READ_ME.txt"),"shareable ZIP contains only the numeric trace, summary and readme");
   using var sr=new StreamReader(zip.GetEntry("summary.json")!.Open());using var json=JsonDocument.Parse(sr.ReadToEnd());var summary=json.RootElement;
   check(summary.GetProperty("frames").GetInt64()==21&&summary.GetProperty("rejected").GetInt64()==1,"summary retains accepted and lost face observations");
   check(summary.GetProperty("labelledRestRmsPx").GetDouble()>0&&summary.GetProperty("captureToEngineP95Ms").GetDouble()==5,"rest jitter and measured processing age are computed from labelled samples");
   check(summary.GetProperty("targetHits").GetInt64()==1&&summary.GetProperty("targetCompletionP95Ms").GetDouble()==1000&&summary.GetProperty("targetCentreErrorP95Px").GetDouble()==0,"labelled target task measures successful reach time and endpoint error");
   check(summary.GetProperty("synthetic").GetBoolean(),"synthetic results remain explicitly marked instead of claiming live-user accuracy");
   using var trace=new StreamReader(zip.GetEntry("movement.jsonl")!.Open());string text=trace.ReadToEnd();check(!text.Contains("\"preview\":")&&!text.Contains("landmarks")&&!text.Contains("cameraId")&&!text.Contains("windowTitle"),"report schema excludes images, face meshes, camera identity and window content");
  }
  using(var limited=new MovementRecorder(Path.Combine(directory,"limit"),maximumSeconds:1)){
   limited.Start(0,true);limited.Tick(2);check(limited.Completion.Wait(8000)&&!limited.Recording,"wall-clock limit stops recording even without camera observations");
  }
  using(var overflow=new MovementRecorder(Path.Combine(directory,"overflow"),capacity:1)){
   overflow.Start(0,true);for(int i=0;i<10000;i++)overflow.Add(new("frame",i/100d));overflow.Stop();check(overflow.Completion.Wait(8000)&&overflow.Status.Dropped>0,"a full queue drops diagnostics instead of blocking the pointer loop");
  }
  using(var sizeLimited=new MovementRecorder(Path.Combine(directory,"size"),maximumBytes:2048)){
   sizeLimited.Start(0,true);for(int i=0;i<100;i++)sizeLimited.Add(new("frame",i/100d,State:"Tracking",Face:new(0,0,0,0,0,i/100d)));sizeLimited.Stop();
   check(sizeLimited.Completion.Wait(8000)&&new FileInfo(Directory.GetFiles(Path.Combine(directory,"size"),"*.jsonl")[0]).Length<=2048,"size limit bounds the actual numeric trace bytes and still exports a report");
  }
  string bad=Path.Combine(directory,"not-a-folder");File.WriteAllText(bad,"test");using(var broken=new MovementRecorder(bad)){broken.Start(0,true);check(broken.Completion.Wait(8000)&&broken.Status.Error is not null&&!broken.Recording,"a disk failure is reported without throwing into mouse control");}
 }
}
