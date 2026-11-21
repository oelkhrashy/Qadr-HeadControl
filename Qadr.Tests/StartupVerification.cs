using Qadr.Core;
static class StartupVerification
{
 public static void Run(Action<bool,string> check){
  check(!new Settings().SpokenGuidance&&!Protocol.Decode<Settings>("{}").SpokenGuidance,"new and legacy missing narration settings start silently");
  check(new Settings().HandsFreeVoice,"silent startup retains voice stop and resume");
  var profile=new Profile{CoordinateVersion=3,Neutral=[-.4,.3],Center=[.3,.7],Ranges=[[.02,.025],[.02,.025]]};
  Observation O(double t,double yaw=.2,double pitch=-.15)=>new(yaw,pitch,.6,0,.62,t,LeftEye:.62,RightEye:.62,Eyebrow:.5);
  var rest=new ResumeRest();bool ready=false;
  for(int i=0;i<22;i++)ready=rest.Feed(O(i/30d),profile);
  check(ready,"stable current posture can start far from an old narrow calibrated centre");
  check(rest.Progress==1&&rest.Samples.Count>=10,"resume requires a real short sampled hold");
  check(Math.Abs(rest.Centre().Yaw-.2)<1e-9&&Math.Abs(rest.Centre().Pitch+.15)<1e-9,"recentre uses measured session medians");
  var baseline=ExpressionRest.From(rest.Samples)!;var settings=baseline.Apply(new Settings{RightClickMethod="either"});
  check(settings.BlinkThreshold>.62&&settings.EyeRestLevel==.62,"neutral eye scores are baselined rather than blocking startup or freezing the cursor");
  var eye=new EyeClick();int rights=0;for(int i=0;i<30;i++)if(eye.Feed(i>=5&&i<20?.97:.62,.62,settings,i/30d))rights++;
  check(rights==1,"an intentional wink remains usable above an elevated neutral eye score");
  var c=new MotionController{Settings=new(){Mapping="rate",ClickMethod="none",EyebrowDoubleClick=false,RightClickMethod="either"}};c.SetProfile(profile);c.Rebase(rest.Centre());c.SetExpressionRest(rest.Samples);double travel=0;int clicks=0,freezes=0;
  for(int i=0;i<50;i++){var output=c.Feed(O(1+i/30d,yaw:.21),500,400);travel+=output.Dx;if(output.Click is not null)clicks++;if(output.Frozen)freezes++;}
  check(travel>0&&clicks==0&&freezes==0,"movement works after recentre with elevated resting eye scores and no false clicks");
  check(c.Profile!.Ranges[0].SequenceEqual(profile.Ranges[0]),"session recentre preserves learned movement capability");
  rest.Reset();for(int i=0;i<16;i++)rest.Feed(O(i/30d),profile);check(!rest.Feed(O(1),profile)&&rest.Progress==0,"a tracking gap cannot finish an old resume hold");
  rest.Reset();for(int i=0;i<16;i++)rest.Feed(O(i/30d),profile);check(!rest.Feed(O(.55,yaw:.4),profile)&&rest.Progress==0,"large movement restarts the current-position hold");
  rest.Reset();for(int i=0;i<22;i++)ready=rest.Feed(O(i/30d,yaw:.2+Math.Sin(i)*.007,pitch:-.15+Math.Cos(i)*.006),profile);
  check(ready,"small natural pose variation can complete resume without perfect stillness");
  rest.Reset();for(int i=0;i<22;i++)ready=rest.Feed(O(i/30d) with{LeftEye=i is >=8 and <=11?.95:.62,RightEye=i is >=8 and <=11?.95:.62},profile);
  check(ready,"natural blinking does not reset the posture hold");
  check(!rest.Feed(O(1) with{Yaw=double.NaN},profile)&&rest.Samples.Count==0,"invalid observations clear pending resume");
  rest.Feed(O(2),profile);check(!rest.Feed(O(2),profile)&&rest.Progress==0,"duplicate timestamps cannot accumulate resume progress");
  rest.Reset();for(int i=0;i<8;i++)ready=rest.Feed(O(i*.1),profile);check(!ready,"too few samples never enable control even after the time threshold");
 }
}
