using Qadr.Core;
static class EyebrowVerification
{
 public static void Run(Action<bool,string> check,string mapping="relative"){
  var settings=new Settings{Mapping=mapping,EyebrowDoubleClick=true,EyebrowLearned=true,EyebrowThreshold=.3,EyebrowRestLevel=.02,ClickMethod="none",RightClickMethod="none"};
  var g=new EyebrowClick();int fired=0;for(int i=0;i<45;i++)if(g.Feed(i>=8&&i<28?.7:.02,settings,i/30.0))fired++;check(fired==1,"one held eyebrow raise produces one activation on relaxation");
  g=new();fired=0;for(int i=0;i<60;i++)if(g.Feed(.7,settings,i/30.0))fired++;check(fired==0,"held raised eyebrows never repeatedly double-click");
  g=new();for(int i=0;i<28;i++)g.Feed(i<8?.02:.7,settings,i/30.0);check(!g.Feed(.02,settings,2),"tracking gap cannot release an old eyebrow activation");
  g=new();fired=0;for(int i=0;i<20;i++)if(g.Feed(i<4?.7:.02,settings,i/30.0))fired++;check(fired==0,"a brief eyebrow twitch does not double-click");
  g=new();for(int i=0;i<28;i++)g.Feed(i<8?.02:.7,settings,i/30.0);g.Feed(.7,settings,1,true);check(!g.Feed(.02,settings,1.033),"a conflicting expression cancels the pending eyebrow click");
  g=new();fired=0;for(int i=0;i<40;i++)if(g.Feed(i>=8&&i<28?.7:.02,settings with{EyebrowLearned=false},i/30.0))fired++;check(fired==0,"unlearned eyebrow gestures cannot activate input");
  g=new();for(int i=0;i<28;i++)g.Feed(i<8?.02:.7,settings,i/30.0);g.Feed(.7,settings with{EyebrowDoubleClick=false},1);check(!g.Feed(.02,settings,1.033),"switching eyebrow binding off cancels a held gesture");
  var c=new MotionController{Settings=settings};c.SetProfile(new Profile{CoordinateVersion=3});int doubles=0;PointerOutput? click=null;for(int i=0;i<50;i++){var o=new Observation(0,0,0,0,0,i/30.0,Eyebrow:i>=8&&i<26?.7:.02);var result=c.Feed(o,100,100);if(result.Click=="double"){doubles++;click=result;}}check(doubles==1&&click!.ClickX==100&&click.ClickY==100,"eyebrow binding reaches the live controller with a stable anchored double-click");
  c=new(){Settings=settings with{ClickMethod="smile",SmileLearned=true,SmileThreshold=.3}};c.SetProfile(new Profile{CoordinateVersion=3});int overlaps=0;for(int i=0;i<50;i++){var result=c.Feed(new(0,0,.8,0,0,i/30.0,Eyebrow:.8),100,100);if(result.Click is not null)overlaps++;}check(overlaps==0,"simultaneous smile and eyebrow raise do not produce competing clicks");
  var mouse=new MouseActions();mouse.Select("scroll-up");var plan=mouse.Trigger("double");check(plan.Steps.Length==4&&plan.Steps.All(s=>s.Kind is "down" or "up")&&mouse.Mode=="left","direct eyebrow double-click overrides selected scroll mode and returns to left click");
  mouse.Select("drag");mouse.Trigger("left");plan=mouse.Trigger("double");check(plan.Steps.Length==1&&plan.Steps[0].Kind=="up"&&!mouse.Dragging,"eyebrow activation while dragging drops the item safely");
  check(!CommandIntent.IsCurrent(new("learnEyebrows",UserStopEpoch:1),2),"queued pre-Esc eyebrow learning cannot restart setup");
  var extracted=Features.Extract(new VisionResult(1,"result",Captured:1,Geometry:new FaceGeometry(.5,.4,.2,.5,.45,.3,.2,.7,.8,0),Blendshapes:new Dictionary<string,double>{{"browInnerUp",.6},{"browOuterUpLeft",.2},{"browOuterUpRight",.4}}));check(extracted?.Eyebrow==.6,"worker blendshape channels reach the eyebrow observation correctly");
  check(!(new Observation(0,0,0,0,0,0,Eyebrow:double.NaN)).IsUsable,"nonfinite eyebrow scores are rejected");
 }
}
