using Qadr.Core;

static class ContinuityVerification
{
 public static void Run(Action<bool,string> check,string algorithm){
  Observation O(double t,double yaw=0,double scale=.2,double smile=.55,double brow=.3)=>new(yaw,0,smile,0,0,t,Scale:scale,Eyebrow:brow);
  var profile=new Profile{CoordinateVersion=3};
  var settings=new Settings{ClickMethod="smile",SmileLearned=true,SmileRestLevel=.1,SmileThreshold=.23,
   EyebrowDoubleClick=true,EyebrowLearned=true,EyebrowRestLevel=.02,EyebrowThreshold=.18,
   RightClickMethod="none",Mapping="rate",Sensitivity=.42,MovementAlgorithm=algorithm};
  MotionController C(){var c=new MotionController{Settings=settings};c.SetProfile(profile);return c;}
  var guide=new FacePositioning();guide.UseComfortablePosition(O(0));int revision=guide.Revision;bool ready=true;
  for(int i=1;i<90;i++)ready&=guide.Check(O(i/30.0,scale:.2+.04*Math.Sin(i*.1)) with{FaceX=.65,FaceWidth=.35},false).Ready;
  check(ready&&guide.Revision==revision,"normal distance and face-position changes update only the visual guide without refitting control");
  check(!guide.Check(O(3) with{FaceX=.98,FaceWidth=.3},false).Ready,"continuous guide fitting still blocks genuinely clipped faces");
  var c=C();c.Settings=settings with{ClickMethod="none",EyebrowDoubleClick=false};bool uninterrupted=true;double travel=0;
  for(int i=0;i<150;i++){var result=c.Feed(O(i/30.0,.1,.2+.04*Math.Sin(i*.08)),100,100);uninterrupted&=!result.DistanceChanged&&!result.Frozen;travel+=result.Dx;}
  check(uninterrupted&&travel>500,"usable moving face continues driving the pointer across moderate camera-distance changes");
  c=C();c.Settings=settings with{ClickMethod="dwell",EyebrowDoubleClick=false};travel=0;int dwellMoving=0;
  for(int i=0;i<600;i++){var output=c.Feed(O(i/30.0,.08,smile:0,brow:0),100,100);travel+=output.Dx;if(output.Click is not null)dwellMoving++;}
  check(travel>1920&&dwellMoving==0,"slow continuous mode can cross the whole screen without a dwell lock stopping an intentional turn");
  c=C();c.Settings=settings with{EyebrowDoubleClick=false};int falseSmiles=0,freezes=0;
  for(int i=0;i<120;i++){var result=c.Feed(O(i/30.0,.1),100,100);if(result.Click is not null)falseSmiles++;if(i>10&&result.Frozen)freezes++;}
  check(falseSmiles==0&&freezes==0,"an elevated smile score present at startup neither clicks nor freezes movement");
  c=C();c.Settings=settings with{ClickMethod="none"};int falseBrows=0;freezes=0;
  for(int i=0;i<120;i++){var result=c.Feed(O(i/30.0,.1),100,100);if(result.Click is not null)falseBrows++;if(i>10&&result.Frozen)freezes++;}
  check(falseBrows==0&&freezes==0,"an elevated eyebrow score present at startup neither double-clicks nor freezes movement");
  c=C();c.SetExpressionRest(Enumerable.Range(0,25).Select(i=>O(i/30.0)));
  check(Math.Abs(c.EffectiveSettings.SmileRestLevel-.55)<1e-9&&Math.Abs(c.EffectiveSettings.EyebrowRestLevel-.3)<1e-9,"relaxed session baseline compensates for elevated neutral smile and eyebrow scores");
  check(c.Settings==settings,"session baseline never overwrites the saved personal gesture settings");
  int left=0,doubles=0;freezes=0;
  for(int i=0;i<160;i++){
   var result=c.Feed(O(i/30.0,.06,smile:i>=30&&i<45?.8:.55,brow:i>=85&&i<102?.6:.3),100,100);
   if(result.Click=="left")left++;if(result.Click=="double")doubles++;if(i<25&&result.Frozen)freezes++;
  }
  check(left==1&&doubles==1&&freezes==0,"neutral-adjusted controller allows one deliberate smile and one eyebrow cycle without resting false activations");
  c=C();c.SetExpressionRest(Enumerable.Range(0,25).Select(i=>O(i/30.0)));int jitterClicks=0;
  for(int i=0;i<180;i++)if(c.Feed(O(i/30.0,.06,smile:.55+Math.Sin(i)*.02,brow:.3+Math.Cos(i)*.03),100,100).Click is not null)jitterClicks++;
  check(jitterClicks==0,"small neutral expression fluctuations cannot trigger learned smile or eyebrow bindings");
  var g=new EyebrowClick();int prolonged=0;bool stuck=false;
  for(int i=0;i<150;i++){double score=i<8?0:i<130?.8:0;if(g.Feed(score,settings,i/30.0))prolonged++;if(i>70&&g.Active)stuck=true;}
  check(prolonged==0&&!stuck,"prolonged eyebrow elevation expires instead of holding the pointer indefinitely or clicking on late release");
  var smile=new Gesture();int afterGap=0;for(int i=0;i<15;i++)smile.Feed(0,.3,i/30.0);
  for(int i=0;i<20;i++)if(smile.Feed(.8,.3,2+i/30.0))afterGap++;
  check(afterGap==0,"a tracking gap disarms smile until a new neutral interval is observed");
  check(ExpressionRest.From([O(0),O(.01)]) is null,"insufficient resting samples cannot establish an expression baseline");
 }
}
