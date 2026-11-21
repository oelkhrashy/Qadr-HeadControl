using Qadr.Core;
static class FacialModeVerification
{
 public static void Run(Action<bool,string> check,string mapping="relative"){
  Observation O(double t,double pitch=0,double mouth=0,double pucker=0)=>new(0,pitch,0,mouth,0,t,Pucker:pucker);
  var settings=new Settings{Mapping=mapping,PuckerDrag=true,PuckerLearned=true,PuckerThreshold=.3,MouthScroll=true,ScrollGestureLearned=true,ScrollGestureThreshold=.3,ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false};
  var p=new Profile{CoordinateVersion=3};MotionController C(){var c=new MotionController{Settings=settings};c.SetProfile(p);return c;}
  var c=C();int drags=0;for(int i=0;i<50;i++){var output=c.Feed(O(i/30.0,pucker:i>=8&&i<30?.8:0),100,100);if(output.Click=="drag")drags++;}check(drags==1,"one learned lip pucker emits one direct drag toggle on relaxation");
  c=C();int scrolls=0;for(int i=0;i<50;i++){var output=c.Feed(O(i/30.0,mouth:i>=8&&i<30?.8:0),100,100);if(output.Click=="scroll-toggle")scrolls++;}check(scrolls==1,"one learned mouth opening emits one scroll-mode toggle on relaxation");
  c=C();int ambiguous=0;for(int i=0;i<50;i++)if(c.Feed(O(i/30.0,mouth:.8,pucker:.8),100,100).Click is not null)ambiguous++;check(ambiguous==0,"conflicting drag and scroll expressions cannot emit competing actions");
  c=C();for(int i=0;i<28;i++)c.Feed(O(i/30.0,pucker:i<8?0:.8),100,100);check(c.Feed(O(2),100,100).Click is null,"missing tracking cannot complete a pending drag gesture");
  c=C();int held=0;for(int i=0;i<90;i++)if(c.Feed(O(i/30.0,pucker:.8),100,100).Click is not null)held++;check(held==0,"holding a pucker never repeatedly toggles drag");
  c=C();c.Settings=settings with{PuckerLearned=false,ScrollGestureLearned=false};int unlearned=0;for(int i=0;i<50;i++)if(c.Feed(O(i/30.0,mouth:i<25?.8:0),100,100).Click is not null)unlearned++;check(unlearned==0,"unlearned optional facial modes never activate");
  var mouse=new MouseActions();mouse.Select("right");var plan=mouse.Trigger("drag");check(mouse.Dragging&&plan.Steps is [{Kind:"down",Button:"left"}],"direct drag gesture picks up without selecting a mouse tool");plan=mouse.Trigger("drag");check(!mouse.Dragging&&plan.Steps is [{Kind:"up",Button:"left"}]&&mouse.Mode=="left","repeating the drag gesture drops and restores left click");
  var scroll=new HeadScrollController();scroll.Start(O(0),p,100,200);check(scroll.Active&&scroll.AnchorX==100&&scroll.AnchorY==200,"scroll mode retains its entry pose and screen target");
  check(scroll.Feed(O(.033),p,settings)==0,"relaxed neutral head pose does not scroll");
  check(scroll.Feed(O(.066,pitch:.08),p,settings)>0,"head up emits a positive vertical wheel notch");
  check(scroll.Feed(O(.1,pitch:.08),p,settings)==0,"scroll cadence prevents frame-rate-dependent wheel flooding");
  scroll=new();scroll.Start(O(0),p,100,200);check(scroll.Feed(O(.033,pitch:-.08),p,settings)<0,"head down emits a negative vertical wheel notch");
  scroll=new();scroll.Start(O(0),p,100,200);check(scroll.Feed(O(.033,pitch:.08),p,settings,true)==0,"scroll pauses during another facial gesture");
  scroll.Feed(O(1,pitch:.08),p,settings);check(!scroll.Active,"a tracking gap exits head-scroll mode");
  scroll.Start(O(2),p,100,200);scroll.Reset();check(!scroll.Active&&scroll.Feed(O(2.033,pitch:.08),p,settings)==0,"reset and stop prevent additional wheel events");
  check(!(settings with{ClickMethod="mouth"}).Validate().MouthScroll,"mouth opening cannot simultaneously mean left click and scrolling");
  check(new[]{"learnDrag","learnScroll"}.All(t=>!CommandIntent.IsCurrent(new(t,UserStopEpoch:2),3)),"pre-Esc facial-mode learning commands are rejected");
  c=C();c.Settings=settings with{ClickMethod="dwell"};int dwells=0;for(int i=0;i<90;i++)if(c.Feed(O(i/30.0),100,100,suppressLeft:true).Click=="left")dwells++;check(dwells==0,"head-scroll operation suppresses dwell clicks over the page");
  var extracted=Features.Extract(new VisionResult(1,"result",Captured:1,Geometry:new FaceGeometry(.5,.4,.2,.5,.45,.3,.2,.7,.8,0),Blendshapes:new Dictionary<string,double>{{"mouthPucker",.6}}));check(extracted?.Pucker==.6,"compact worker blendshapes reach the pucker detector");
 }
}
