using Qadr.Core;

static class RealtimeVerification
{
 public static void Run(Action<bool,string> check){
  check(new Settings().DirectResponse&&Protocol.Decode<Settings>("{}").DirectResponse,"direct response is enabled for existing and new settings");
  check(!(new Settings{DirectResponse=false}).Validate().DirectResponse,"balanced response remains selectable");
  check(CaptureMode.Score(640,480,30)>CaptureMode.Score(640,480,5),"camera selection prefers real-time frame rate over an identical slow format");
  check(CaptureMode.Score(640,480,30)>CaptureMode.Score(640,480,60),"camera selection avoids doubling capture work when 30 FPS is available");
  check(CaptureMode.Score(1280,720,30)>CaptureMode.Score(640,480,5),"camera selection can use a faster higher-resolution source rather than a five-FPS capture mode");
  check(CaptureMode.Score(640,480,25)>CaptureMode.Score(320,240,30),"adequate frame rates retain face detail instead of blindly choosing the smallest fastest image");
  check(double.IsNegativeInfinity(CaptureMode.Score(0,0,30))&&double.IsNegativeInfinity(CaptureMode.Score(640,480,double.NaN)),"invalid camera formats cannot win selection");
  check(double.IsFinite(CaptureMode.Score(640,480,0)),"unknown camera frame-rate metadata remains an explicitly unknown fallback");
  foreach(int fps in new[]{10,30,60}){
   var trajectory=new CursorTrajectory();trajectory.Reset(100,100);double next=0;var point=(X:100d,Y:100d);
   for(int i=0;i<125;i++){double now=i*.008;if(now>=next){trajectory.SetRate(200,100,Math.Round(point.X),Math.Round(point.Y),0,0,1920,1080,now);next=now+1d/fps;}point=trajectory.Step(.008,now);}
   check(Math.Abs(point.X-300)<1e-8&&Math.Abs(point.Y-200)<1e-8,"continuous pump preserves rate travel independently of camera updates at "+fps+" FPS");
  }
  var path=new CursorTrajectory();path.SetRate(100,0,100,100,0,0,1920,1080,1);var before=path.Step(.008,1.01);var stopped=path.Step(.008,1.3);
  check(stopped==before&&path.Step(.008,1.31)==stopped,"stale rate input stops without coasting or catch-up movement");
  path.SetRate(0,0,stopped.X,stopped.Y,0,0,1920,1080,1.4);check(path.Step(.008,1.41)==stopped,"returning to neutral stops rate movement immediately without a queued target tail");
  path.SetRate(300,0,stopped.X,stopped.Y,0,0,1920,1080,1.5);path.Clear();check(!path.Ready&&path.Step(.008,1.51)==stopped,"explicit clear prevents any further integrated rate movement");
  path=new();path.SetRate(10000,10000,98,98,0,0,100,100,0);check(path.Step(.008,.01)==(99d,99d),"rate integration stays within desktop bounds");
  path.Reset(500,400);path.SetRate(-100,0,500,400,-1920,0,3840,1080,1);check(path.Step(.008,1.01).X<500,"negative rate movement preserves left direction on multi-monitor desktops");
  path.SetRate(double.NaN,0,500,400,0,0,1920,1080,2);check(!path.Ready,"nonfinite velocities invalidate the trajectory safely");
  var direct=new CursorTrajectory();var balanced=new CursorTrajectory();direct.Reset(100,100);balanced.Reset(100,100);direct.Add(100,0,100,100,0,0,1920,1080);balanced.Add(100,0,100,100,0,0,1920,1080);
  var fast=direct.Step(.008,direct:true);var slow=balanced.Step(.008);check(fast.X>slow.X&&fast.X<=200,"relative direct response removes extra interpolation delay without overshoot");
  var f=new AdaptiveAxisFilter();f.Feed(0,.033,.01,.38,true);check(f.Feed(.8,.033,.01,.38,true)==0&&f.RejectedSpike,"direct filtering still rejects the first isolated large spike");
  check(f.Feed(.8,.033,.01,.38,true)>0&&!f.RejectedSpike,"direct filtering accepts the next consistent intended movement");
  var c=new MotionController{Settings=new(){Mapping="rate",ClickMethod="none",RightClickMethod="none"}};c.SetProfile(new(){CoordinateVersion=3});c.Feed(new(0,0,0,0,0,0),100,100);PointerOutput output=new(0,0,null,100,100,0);
  for(int i=1;i<40;i++)output=c.Feed(new(.08,0,0,0,0,i/30d),100,100);
  check(output.IsRate&&output.VelocityX>0&&Math.Abs(output.Dx-output.VelocityX/30)<1e-6,"controller supplies explicit rate velocity without deriving it from UI timing");
  c.Settings=c.Settings with{Mapping="relative"};check(!c.Feed(new(.08,0,0,0,0,1.34),100,100).IsRate,"relative motion remains distinct from integrated rate movement");
 }
 public static int Report(){
  double Response(bool direct){var f=new AdaptiveAxisFilter();double at=double.NaN;for(int i=0;i<480;i++){double t=i/120d;double value=f.Feed(t>=1?.6:0,1/120d,.02,.38,direct);if(t>=1&&value>=.54&&double.IsNaN(at))at=t-1;}return at;}
  Console.WriteLine(Protocol.Encode(new{synthetic=true,filterAt120Hz=true,balancedStep90Ms=Response(false)*1000,directStep90Ms=Response(true)*1000,notes="Causal filter only. Retains spike confirmation; excludes camera/inference/native output. Not live-user aiming latency."}));return 0;
 }
}
