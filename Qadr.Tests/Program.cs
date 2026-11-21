using Qadr.Core;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

static class Tests
{
    static int count;
    static string movementAlgorithm="adaptive";
    static void Check(bool valid,string name){if(!valid)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    static Observation O(double t,double yaw=0,double pitch=0,double scale=.2,double x=.5,double y=.5,double smile=0,double mouth=0,double blink=0)=>new(yaw,pitch,smile,mouth,blink,t,x,y,scale);
    static Profile Profile(string mode="turn")=>new(){CoordinateVersion=3,Modes=[mode,mode],Neutral=[0,0],Ranges=[[.08,.14],[.05,.10]],Noise=[.002,.002]};
    static MotionController Controller(Settings? settings=null,string mode="turn"){var c=new MotionController{Settings=(settings??new(){ClickMethod="none",Mapping="rate"}) with{MovementAlgorithm=movementAlgorithm}};c.SetProfile(Profile(mode));return c;}
    public static int Main(string[] args)
    {
        if(args.Contains("--handsfree-integration"))return HandsFreeIntegration.Run(args).GetAwaiter().GetResult();
        if(args.Contains("--autotune-integration"))return AutoTuneIntegration.Run(args).GetAwaiter().GetResult();
        if(args.Contains("--launch-integration"))return LaunchIntegration.Run(args).GetAwaiter().GetResult();
        if(args.Contains("--tutorial-integration"))return TutorialIntegration.Run(args).GetAwaiter().GetResult();
        if(args.Contains("--tutorial-grammar")){Console.WriteLine(Protocol.Encode(TutorialFlow.Phrases(true).Concat(TutorialFlow.Phrases(false)).Distinct().ToArray()));return 0;}
        if(args.Contains("--classic"))movementAlgorithm="classic";
        if(Arguments.Get(args,"--feedback-report") is string feedback)return FeedbackReplay.Run(feedback,Arguments.Get(args,"--baseline-core")!,args.Contains("--comfort-settings"));
        if(Arguments.Get(args,"--gesture-failure-report") is string failure)return GestureFailureReplay.Run(failure,Arguments.Get(args,"--baseline-core")!,args.Contains("--replay-relative"));
        if(Arguments.Get(args,"--recorded-rest-report") is string restReport)return RecordedRestVerification.Run(restReport,Arguments.Get(args,"--baseline-core")!);
        if(args.Contains("--realtime-report"))return RealtimeVerification.Report();
        if(args.Contains("--advanced-movement-report"))return AdvancedMovementVerification.Report();
        if(args.Contains("--benchmark-motion"))return PointerBenchmark.Run();
        if(Arguments.Get(args,"--protocol-fixture") is string fixture)return PointerBenchmark.ProtocolFixture(fixture);
        if(args.Contains("--replay"))return Replay.Run(args);
        if(Arguments.Get(args,"--customer-report") is string report)return FollowMovementVerification.Replay(report);
        if(Arguments.Get(args,"--reach-report") is string reachReport)return FollowMovementVerification.Replay(reachReport,true);
        if(args.Contains("--configure-owner")){var storage=new Storage(Arguments.DataDirectory(args));var settings=storage.Load<Settings>("settings.json")??new();storage.Save("settings.json",(settings with{BalanceHorizontal=true,RightClickHold=.30,RightClickCooldown=.45}).Validate());Console.WriteLine("Migrated preferences configured: balanced horizontal response, 0.30 s wink hold and 0.45 s right-click cooldown.");return 0;}
        return RunTests(args);
    }
    static int RunTests(string[] args)
    {
        if(args.Contains("--camera-grammar")){Console.WriteLine(Protocol.Encode(CameraVoice.Phrases(20)));return 0;}
        if(args.Contains("--integration"))return Integration(args).GetAwaiter().GetResult();
        if(args.Contains("--gate-probe")) {
            string session=Arguments.Get(args,"--session")!;using var gate=new SafetyGate(session,false);
            for(int i=0;i<300;i++){Console.WriteLine(Protocol.Encode(new{stopped=gate.Stopped,hotkeys=gate.Hotkeys,generation=gate.Generation,ack=gate.Acknowledged}));Thread.Sleep(100);}return 0;
        }
        if(args.Contains("--fake-engine") || Environment.GetEnvironmentVariable("QADR_FAKE_ENGINE")=="1") {
            using var gate=new SafetyGate(Arguments.Get(args,"--session")!,false);
            long start=Environment.TickCount64;while(Environment.TickCount64-start<800){gate.Heartbeat=Environment.TickCount64;gate.Acknowledged=gate.Generation;Thread.Sleep(10);}Thread.Sleep(10000);return 0;
        }
        try {
            if(args.Contains("--motion-regression")){MotionRegression.Run(Check);Console.WriteLine($"{count} motion regression checks passed.");return 0;}
            if(args.Contains("--advanced-movement")){AdvancedMovementVerification.Run(Check);
            FollowMovementVerification.Run(Check,movementAlgorithm);
            ReachVerification.Run(Check,movementAlgorithm);Console.WriteLine($"{count} advanced movement checks passed.");return 0;}
            Check(new TutorialFlow().Page==-1&&!new TutorialFlow().Completed,"tutorial waits for an explicit choice");
            Check(TutorialFlow.Resolve(" YES ",true)=="tutorialStart"&&TutorialFlow.Resolve("no",true)=="tutorialSkip","spoken yes and no resolve to tutorial or direct startup");
            Check(TutorialFlow.Resolve("next",true) is null&&TutorialFlow.Resolve("yes",false) is null,"tutorial grammar rejects commands from the wrong phase");
            Check(TutorialFlow.Resolve("next",false)=="tutorialNext"&&TutorialFlow.Resolve("back",false)=="tutorialBack"&&TutorialFlow.Resolve("start",false)=="tutorialSkip","spoken tutorial navigation resolves explicit actions");
            var tour=new TutorialFlow();Check(tour.Start()&&!tour.Start()&&tour.Page==0,"tutorial starts once at the safety lesson");tour.Back();Check(tour.Page==0,"back cannot go before the first tutorial page");
            tour.Next();tour.Back();Check(tour.Page==0,"tutorial can repeat its previous lesson");for(int i=0;i<TutorialFlow.Pages.Length-1;i++)tour.Next();Check(!tour.Completed&&tour.Page==TutorialFlow.Pages.Length-1,"tutorial waits on its last page until Start is chosen");
            Check(tour.Next()&&tour.Completed&&!tour.Start(),"finishing the guide completes startup exactly once");tour=new();tour.Complete();Check(!tour.Start()&&!tour.Next(),"skipped or stopped tutorial cannot restart from old navigation");
            Check(TutorialFlow.Phrases(true).Contains("computer stop")&&TutorialFlow.Phrases(false).Contains("computer stop"),"emergency voice stop remains in both tutorial grammars");
            Check(new[]{"tutorialStart","tutorialSkip","tutorialNext","tutorialBack"}.All(t=>!CommandIntent.IsCurrent(new(t,UserStopEpoch:3),4)),"pre-stop tutorial actions cannot start setup");
            var c=Controller();var still=Enumerable.Range(0,120).Select(i=>c.Feed(O(i/30.0),100,100)).ToArray();Check(still.All(p=>p.Dx==0&&p.Dy==0&&p.Click is null),"rest produces no movement or clicks");
            foreach(string mapping in new[]{"relative","rate"}){
                double Travel(bool balanced,double yaw){var control=Controller(new(){ClickMethod="none",RightClickMethod="none",Mapping=mapping,BalanceHorizontal=balanced});control.SetProfile(Profile() with{Ranges=[[.6,.43],[.1,.1]]});control.Feed(O(0),500,400);return Math.Abs(Enumerable.Range(1,60).Sum(i=>control.Feed(O(i/30.0,yaw:yaw),500,400).Dx));}
                Check(Travel(false,-.1)<Travel(false,.1),mapping+" reproduces slower left response with a larger calibrated left range");
                Check(Math.Abs(Travel(true,-.1)-Travel(true,.1))<.000001,mapping+" balanced response gives equal travel for equal left and right turns");
            }
            var quickEyes=new EyeClick();int quickWinks=0;for(int i=0;i<16;i++)if(quickEyes.Feed(i<11?.9:0,0,new Settings(),i/30.0))quickWinks++;
            Check(quickWinks==1,"default deliberate wink clicks on reopening after about 0.30 seconds");
            quickEyes=new();int shortBoth=0;for(int i=0;i<16;i++)if(quickEyes.Feed(i<11?.9:0,i<11?.9:0,new Settings(),i/30.0))shortBoth++;
            Check(shortBoth==0,"both-eye closures below 0.4 seconds remain rejected at the faster wink setting");
            foreach(double scale in new[]{1.0,1.25,1.5,2.0}){var collapsed=EdgeDockLayout.Calculate(-1920,0,1920,1040,scale,false);var expanded=EdgeDockLayout.Calculate(-1920,0,1920,1040,scale,true);Check(collapsed.X+collapsed.Width==expanded.X+expanded.Width&&collapsed.Y+collapsed.Height/2==expanded.Y+expanded.Height/2&&expanded.X>=-1920&&expanded.Y>=0&&expanded.Y+expanded.Height<=1040,"right-edge dock keeps its tab centred and stays in the monitor work area at scale "+scale);}
            c=Controller();c.Feed(O(0),0,0);double moved=Enumerable.Range(1,40).Sum(i=>c.Feed(O(i/30.0,.07),0,0).Dx);Check(moved>300 && moved<500,"half-range movement follows the configured acceleration curve");
            var near=Controller(mode:"shift");var far=Controller(mode:"shift");near.SetProfile(Profile("shift") with{Scale=.3});far.SetProfile(Profile("shift") with{Scale=.1});
            near.Feed(O(0,scale:.3),0,0);far.Feed(O(0,scale:.1),0,0);
            double a=0,b=0;for(int i=1;i<30;i++){a+=near.Feed(O(i/30.0,scale:.3,x:.53),0,0).Dx;b+=far.Feed(O(i/30.0,scale:.1,x:.51),0,0).Dx;}
            Check(Math.Abs(a-b)<.001,"translation is invariant to camera scale");
            c=Controller();c.Feed(O(0),0,0);var changed=c.Feed(O(.1,scale:.34,x:.62),0,0);Check(changed.DistanceChanged&&changed.Dx==0&&changed.Dy==0,"posture change freezes movement");
            for(int i=2;i<40;i++)c.Feed(O(i/30.0,scale:.34,x:.62),0,0);Check(Math.Abs(c.Profile!.Scale-.34)<1e-6,"stable posture rebases distance without a jump");
            c=Controller();c.Feed(O(0),100,100);for(int i=1;i<35;i++)c.Feed(O(i/30.0,yaw:.04,pitch:.05,scale:.34,x:.6),100,100);
            var recentered=c.Feed(O(1.2,yaw:.04,pitch:.05,scale:.34,x:.6),100,100);Check(Math.Abs(c.Profile!.Neutral[0]-.04)<1e-6&&Math.Abs(c.Profile.Neutral[1]-.05)<1e-6&&Math.Abs(recentered.Dx)+Math.Abs(recentered.Dy)<.001,"distance adaptation also learns the stable new angle without a cursor jump");
            c=Controller(new(){ClickMethod="smile"});var clicks=new List<string>();for(int i=0;i<100;i++){var p=c.Feed(O(i/30.0,smile:i<8?0:.8),200,300);if(p.Click is not null)clicks.Add(p.Click);CheckSilent(p.Dx==0&&p.Dy==0,"gesture moves cursor");}Check(clicks.Count==1,"held smile clicks exactly once");
            c=Controller(new(){ClickMethod="blink"});int blinks=0;for(int i=0;i<30;i++){var p=c.Feed(O(i/30.0,blink:i<5?.9:0),0,0);if(p.Click is not null)blinks++;}Check(blinks==0,"ordinary short blink does not click");
            c=Controller(new(){ClickMethod="dwell",DwellSeconds=1});int dwellClicks=0;for(int i=0;i<100;i++)if(c.Feed(O(i/30.0),100,100).Click is not null)dwellClicks++;Check(dwellClicks==1,"dwell requires leaving the target before rearming");
            c=Controller(new(){ClickMethod="dwell",DwellSeconds=1});dwellClicks=0;bool lockedTarget=false;
            for(int i=0;i<150;i++){var p=c.Feed(O(i/30.0,yaw:.035+Math.Sin(i)*.0004),500+Math.Sin(i*.5)*3,300);if(p.Click=="left")dwellClicks++;lockedTarget|=p.Frozen;}
            Check(dwellClicks==1&&lockedTarget,"dwell tolerates small jitter and locks the target before one left-click");
            foreach(bool both in new[]{false,true}){
                c=Controller(new(){ClickMethod="dwell"});int rightClicks=0,leftDuring=0;
                for(int i=0;i<35;i++){var o=O(i/30.0) with{LeftEye=i<22?.9:0,RightEye=both&&i<22?.9:0};var p=c.Feed(o,100,100);if(p.Click=="right")rightClicks++;if(i<22&&p.Click=="left")leftDuring++;}
                Check(rightClicks==1&&leftDuring==0,both?"deliberate both-eye closure produces one right-click on reopening":"deliberate single-eye wink produces one right-click on reopening");
            }
            var eyeClick=new EyeClick();int ordinary=0;for(int i=0;i<30;i++)if(eyeClick.Feed(i<5?.9:0,i<5?.9:0,new Settings(),i/30.0))ordinary++;
            Check(ordinary==0,"ordinary blinks cannot trigger right-click");
            Check(new Settings{ClickMethod="blink"}.Validate().ClickMethod=="dwell","earlier blink-left setting migrates to dwell with independent right-click");
            var points=Enumerable.Range(0,468).Select(_=>new[]{.5,.5,0.0}).ToArray();points[33]=[.4,.5,0];points[263]=[.6,.5,0];points[1]=[.55,.55,0];double[] matrix=new double[16];matrix[0]=Math.Cos(.2);matrix[8]=-Math.Sin(.2);matrix[9]=-Math.Sin(.1);matrix[10]=Math.Cos(.1);
            var extracted=Features.Extract(new(1,"result",Landmarks:points,Matrix:matrix,Blendshapes:new(){{"eyeBlinkLeft",.9},{"eyeBlinkRight",.1}}));
            Check(extracted is {Yaw:>0,Pitch:>0,LeftEye:>.8,RightEye:<.2} && Profile().Signals(extracted)[1]<0,"mirrored right/up pose produces right/up screen signals and retains each eye");
            foreach(string mapping in new[]{"relative","rate"})foreach(double pitch in new[]{-.04,.04}){
                c=Controller(new(){Mapping=mapping,ClickMethod="none",RightClickMethod="none"});c.Feed(O(0),500,500);
                double vertical=Enumerable.Range(1,40).Sum(i=>c.Feed(O(i/30.0,pitch:pitch),500,500).Dy);
                Check(Math.Sign(vertical)==-Math.Sign(pitch),mapping+" maps look up to screen up and look down to screen down");
            }
            c=Controller(new(){ClickMethod="none",RightClickMethod="none"},"shift");c.Feed(O(0),500,500);
            double translationStep=c.Feed(O(.033,y:.49),500,500).Dy;
            translationStep+=c.Feed(O(.066,y:.49),500,500).Dy;
            Check(translationStep<0,"accepted screen-space vertical translation is not double inverted");
            var old=(Profile() with{CoordinateVersion=2,Ranges=[[.08,.14],[.03,.09]]}).Upgrade();
            Check(old.IsValid&&old.Ranges[1].SequenceEqual(new[]{.09,.03})&&old.Neutral.SequenceEqual(Profile().Neutral),"saved asymmetric turn ranges migrate without losing the relaxed pose");
            old=(Profile("shift") with{CoordinateVersion=2,Ranges=[[.08,.14],[.03,.09]]}).Upgrade();
            Check(old.Ranges[1].SequenceEqual(new[]{.03,.09}),"saved translation ranges retain their screen direction");
            // Replay the measured noise/range proportions from the reported
            // profile, with bounded simulated rest jitter and natural blinks.
            c=Controller(new(){ClickMethod="dwell",RightClickMethod="either"});
            c.SetProfile(Profile() with{Ranges=[[.14558094299,.18946180396],[.04879216762,.04879216762]],Noise=[.01116149069,.01203571348]});
            var trajectory=new CursorTrajectory();trajectory.Reset(500,400);var point=(X:500.0,Y:400.0);int noisyLeft=0;
            for(int i=0;i<150;i++){
                double now=i/30.0;bool blinking=i%24<3;
                var output=c.Feed(O(now,yaw:.05+Math.Sin(i*.6)*.008,pitch:.025+Math.Cos(i*.7)*.009) with{LeftEye=blinking?.75:0,RightEye=blinking?.75:0},point.X,point.Y);
                if(output.Frozen)trajectory.Reset(point.X,point.Y);else trajectory.Add(output.Dx,output.Dy,point.X,point.Y,0,0,1920,1080);
                point=trajectory.Step(1.0/30);if(output.Click=="left")noisyLeft++;
            }
            Console.WriteLine($"Noise replay: {noisyLeft} left clicks, final progress {c.LeftProgress:F2}");
            Check(noisyLeft==1,"measured high-noise restricted range and frequent natural blinks still allow one dwell click");
            var lesson=new ClickLearning(true,new());ClickLessonFeedback learned=new("rest",0);
            for(int i=0;i<300;i++)learned=lesson.Feed(.34,i/30.0);
            Check(learned.Phase=="gesture"&&learned.Threshold is null,"gesture learning waits for the person without a countdown deadline");
            for(int i=300;i<321;i++)learned=lesson.Feed(.56,i/30.0);
            Check(learned.Phase=="release","gesture lesson requires a sustained comfortable closure");
            learned=lesson.Feed(.34,10.8);
            Check(learned.Threshold is >.34 and <.56&&learned.RestLevel==.34,"gesture learning finishes on release despite elevated open-eye scores");
            var calibrated=new Settings{EyeRestLevel=.34,BlinkThreshold=learned.Threshold!.Value,RightClickMethod="either"};
            var detector=new EyeClick();int elevatedRight=0;for(int i=0;i<50;i++)if(detector.Feed(i<25?.56:.34,.32,calibrated,i/30.0))elevatedRight++;
            Check(elevatedRight==1,"learned baseline allows a moderate wink to right-click once on reopening");
            var learnedSmile=new Gesture(.28);int elevatedSmile=0;
            for(int i=0;i<90;i++)if(learnedSmile.Feed(i%45>=8&&i%45<28?.48:.30,.39,i/30.0,.30))elevatedSmile++;
            Check(elevatedSmile==2,"smile re-arms at a person's resting baseline");
            detector=new();int partial=0;for(int i=0;i<50;i++)if(detector.Feed(i<4?.8:i<30?.4:0,0,new(),i/30.0))partial++;
            Check(partial==0,"partial closure time cannot turn a short blink into a deliberate right click");
            c=Controller(new(){ClickMethod="dwell",RightClickMethod="none",DwellSeconds=.6});for(int i=0;i<30;i++)c.Feed(O(i/30.0),100,100);
            var escaped=c.Feed(O(1.1,yaw:.13),100,100);
            if(movementAlgorithm=="adaptive")escaped=c.Feed(O(1.133,yaw:.13),100,100);
            Check(!escaped.Frozen&&escaped.Dx>0,"confirmed intentional motion leaves a dwell anchor");
            c=Controller(new(){ClickMethod="smile",RightClickMethod="none"});c.Feed(O(0),100,100);c.Feed(O(.033),100,100);c.Feed(O(.066),100,100);
            c.Feed(O(.1,smile:.8),125,100);PointerOutput? anchoredClick=null;for(int i=4;i<15;i++){var output=c.Feed(O(i/30.0,smile:.8),125,100);if(output.Click=="left")anchoredClick=output;}
            Check(anchoredClick?.ClickX==100,"gesture click holds the cursor position from before expression onset");
            c=Controller(new(){ClickMethod="smile",RightClickMethod="none"});
            for(int i=0;i<10;i++)c.Feed(O(i/30.0),100,100);
            var smileOnset=c.Feed(O(10/30.0,yaw:.025,smile:.25),100,100);
            Check(!smileOnset.Frozen,"subthreshold smile variation does not freeze head movement");
            PointerOutput? earlyAnchor=null;for(int i=11;i<30;i++){var output=c.Feed(O(i/30.0,yaw:.08,smile:.8),125,100);if(output.Click=="left")earlyAnchor=output;}
            Check(earlyAnchor?.ClickX==100,"gradual smile clicks the anchor before early onset, not the drifted threshold position");
            var smileRelease=c.Feed(O(1,yaw:.025),100,100);Check(smileRelease.Frozen,"smile release retains a short settling freeze");
            var modelStatus=new EngineStatus("Positioning","","",0,0,0,false,"Waiting for a face",0,0,"Esc",[],null,[],"Microphone off");
            Check(SetupPresentation.From(modelStatus).FaceState=="No face"&&SetupPresentation.From(modelStatus).Setup,"setup presentation consistently reports no face and hides control tools");
            var fittedStatus=modelStatus with{Face=true,Position=new("adapting","",false)};
            Check(SetupPresentation.From(fittedStatus).FaceState=="Fitting"&&!SetupPresentation.From(fittedStatus).Checks.Contains("No face"),"detected fitting face cannot also be described as missing");
            Check(SetupPresentation.From(fittedStatus with{Position=new("good","",true)}).FaceState=="Locked"&&SetupPresentation.From(modelStatus with{State="Tracking"}).Setup==false,"locked geometry and control mode have explicit presentation states");
            var guide=new FacePositioning();Check(guide.Check(O(0) with{Scale=.04}).Code=="closer"&&guide.Check(O(0) with{Scale=.4}).Code=="farther","face guide retains reliability limits for faces too small or too close");
            guide.UseComfortablePosition(O(0));Check(guide.Check(O(0) with{FaceX=.2}).Code=="adapting"&&!guide.Check(O(0) with{Yaw=.5}).Ready,"face guide refits a changed position and retains a relaxed-angle cue");
            guide.UseComfortablePosition(O(0) with{FaceX=.3,Yaw=.45});Check(guide.Check(O(0) with{FaceX=.3,Yaw=.45}).Ready,"a comfortable supported posture can become the face guide target");
            guide=new();FacePosition fitted=new("find","",false);
            for(int i=0;i<35;i++)fitted=guide.Check(O(i/30.0,scale:.07) with{FaceX=.63,FaceY=.42,FaceWidth=.18,FaceHeight=.3});
            Check(fitted.Ready&&Math.Abs(fitted.X-.63)<.001&&fitted.Width<.25&&fitted.Height<.4,"a farther fully visible face gets a fitted guide and can calibrate without moving closer");
            double farWidth=fitted.Width;bool heldForDistance=false;
            for(int i=35;i<75;i++){fitted=guide.Check(O(i/30.0,scale:.14) with{FaceX=.55,FaceY=.48,FaceWidth=.32,FaceHeight=.5},false);heldForDistance|=!fitted.Ready;}
            Check(!heldForDistance&&fitted.Ready&&fitted.Width>farWidth&&Math.Abs(fitted.X-.55)<.001,"visual guide resizes during distance changes without holding control");
            Check(!guide.Check(O(3) with{FaceX=.95,FaceWidth=.3}).Ready,"automatic fitting never accepts a clipped face");
            c=Controller();var beforeRanges=c.Profile!.Ranges;var sharedFit=guide.FittedObservation!;c.Rebase(sharedFit);
            Check(c.Profile!.Center.SequenceEqual(new[]{sharedFit.X,sharedFit.Y})&&c.Profile.Neutral.SequenceEqual(new[]{sharedFit.Yaw,sharedFit.Pitch})&&c.Profile.Scale==sharedFit.Scale&&ReferenceEquals(beforeRanges,c.Profile.Ranges),"guide snapshot rebases movement with the same pose and preserves learned directional ranges");
            var actions=new MouseActions();
            foreach(string id in new[]{"left","right","middle","back","forward"}){actions.Select(id);var plan=actions.Trigger("left");Check(plan.Steps.Length==2&&plan.Steps[0].Kind=="down"&&plan.Steps[1].Kind=="up"&&plan.Steps.All(s=>s.Button==id)&&actions.Mode=="left",id+" emits a matched press/release and restores left click");}
            actions.Select("double");var twice=actions.Trigger("left",doubleGap:.04);
            Check(twice.Steps.Select(s=>s.Kind).SequenceEqual(new[]{"down","up","down","up"})&&twice.Steps[2].Delay==.04&&actions.Mode=="left","double click schedules two matched clicks at one target and returns to left mode");
            actions.Select("drag");var startDrag=actions.Trigger("left");Check(actions.Dragging&&startDrag.Steps is [{Kind:"down"}],"drag holds the left button without an early release");
            var drop=actions.Trigger("left");Check(!actions.Dragging&&drop.Steps is [{Kind:"up"}]&&actions.Mode=="left","the next activation drops the held item and restores left click");
            actions.Select("drag");actions.Trigger("left");actions.Reset();Check(!actions.Dragging&&actions.Mode=="left","stop resets drag state");
            foreach(var (id,kind,amount) in new[]{("scroll-up","vertical",1),("scroll-down","vertical",-1),("scroll-left","horizontal",-1),("scroll-right","horizontal",1)}){actions.Select(id);var wheel=actions.Trigger("left");Check(wheel.Steps is [{ } step]&&step.Kind==kind&&step.Amount==amount&&actions.Mode==id&&actions.Scrolling,id+" uses the correct wheel axis and direction and stays selectable");}
            actions.Select("double");var overrideRight=actions.Trigger("right");Check(overrideRight.Steps.Length==2&&overrideRight.Steps.All(s=>s.Button=="right"),"deliberate right-eye activation overrides the selected next-click mode");
            Check(!actions.Select("unknown")&&actions.Mode=="left","unknown action cannot change the mouse mode");
            Check(CameraVoice.Resolve("camera one",2)==0&&CameraVoice.Resolve("choose camera two",2)==1,"spoken camera numbers select the corresponding device");
            Check(CameraVoice.Resolve(" CAMERA  2 ",2)==1&&CameraVoice.Resolve("camera twenty",20)==19,"camera parser accepts normalized text and the supported upper ordinal");
            Check(new[]{"camera zero","camera three","camera -1","camera one please","computer run camera one","camera"}.All(t=>CameraVoice.Resolve(t,2)==-1),"out-of-range and unrelated speech cannot select a camera");
            Check(CameraVoice.Phrases(2).SequenceEqual(new[]{"camera one","choose camera one","camera two","choose camera two","computer stop"}),"camera grammar contains only numbered choices and emergency stop");
            Check(CameraVoice.Phrases(100).Length==41&&CameraVoice.Resolve("camera 21",100)==-1,"excess devices require manual choice instead of an ambiguous voice number");
            var startup=new StartupFlow(true);Check(startup.Advance(),"normal launch permits the automatic calibration chain");startup.Cancel();Check(!startup.Advance(),"stop or failure cancels automatic startup for this session");
            startup=new(true);startup.Started();Check(!startup.Advance()&&!new StartupFlow(false).Advance(),"startup is one-shot and preview mode never automatically enables input");
            string[] intents=["resume","useDwell","confirm","record","camera","refresh","calibrate","comfortablePosition","learnSmile","learnEyes","restoreProfile"];
            Check(intents.All(type=>!CommandIntent.IsCurrent(new(type,UserStopEpoch:3),4)&&!CommandIntent.IsCurrent(new(type),4)),"Esc rejects queued and unstamped control intents before they alter setup or restart input");
            Check(intents.All(type=>CommandIntent.IsCurrent(new(type,UserStopEpoch:4),4)),"a fresh explicit intent after Esc is accepted");
            Check(!CommandIntent.IsCurrent(new("cameraVoice",Enabled:true,UserStopEpoch:3),4)&&!CommandIntent.IsCurrent(new("voice",Enabled:true,UserStopEpoch:3),4)&&CommandIntent.IsCurrent(new("cameraVoice",Enabled:false),4)&&CommandIntent.IsCurrent(new("stop"),4),"stop and microphone-off remain available while stale microphone-on is rejected");
            var path=new CursorTrajectory();path.Reset(100,100);path.Add(100,0,100,100,0,0,1920,1080);var interpolated=path.Step(.008);Check(interpolated.X>100&&interpolated.X<200,"cursor fills intervals between camera frames without jumping to its target");
            for(int i=0;i<50;i++)interpolated=path.Step(.008);Check(interpolated.X<=200&&interpolated.X>199.9,"cursor interpolation converges without overshoot");
            path.Reset(100,100);interpolated=(100,100);for(int i=0;i<10;i++){path.Add(.3,0,Math.Round(interpolated.X),100,0,0,1920,1080);interpolated=path.Step(.03);}for(int i=0;i<50;i++)interpolated=path.Step(.008);Check(Math.Abs(interpolated.X-103)<.01,"fractional movements accumulate instead of being rounded away");
            Check(!(Profile() with{CoordinateVersion=1}).IsValid,"profiles with the earlier direction convention require recalibration");
            var quality=new QualityGate();Check(!quality.Accept(O(0),.5),"stale frame rejected");quality.Accept(O(1),1);for(int i=1;i<=21;i++)quality.Accept(O(1+i/30.0),1+i/30.0);Check(quality.Accept(O(1.72),1.72),"tracking recovers after continuously observed stability hold");Check(!quality.Accept(O(1.73,1.2),1.73),"impossible pose jump rejected");
            var calibration=new Calibration();CalibrationFeedback? feedback=null;for(int i=0;i<1000;i++)feedback=calibration.Feed(O(i/30.0));Check(feedback?.Profile is null,"no-motion calibration cannot activate control");
            calibration=new();for(int i=0;i<160;i++)calibration.Feed(O(i/30.0));for(int i=160;i<700;i++){double t=i/30.0;feedback=calibration.Feed(O(t,Math.Sin(t*2)*.03,Math.Cos(t*2)*.02));if(feedback.Profile is not null)break;}Check(feedback?.Profile is {IsValid:true},"calibration learns a restricted comfortable range");
            calibration=new();for(int i=0;i<160;i++)calibration.Feed(O(i/30.0,yaw:Math.Sin(i)*.004,pitch:Math.Cos(i)*.004));feedback=null;for(int i=160;i<700;i++){double t=i/30.0;feedback=calibration.Feed(O(t,yaw:Math.Sin(t*2)*.06,pitch:Math.Cos(t*2)*.06,x:.5-Math.Sin(t*2)*.08,y:.5-Math.Cos(t*2)*.08));if(feedback.Profile is not null)break;}Check(feedback?.Profile is {Modes:["turn","turn"]},"clear head turns are preferred over opposite correlated eye-centre motion");
            string temp=Path.Combine(Environment.GetEnvironmentVariable("QADR_TEST_DATA")??Path.GetTempPath(),"Qadr-verify-"+Guid.NewGuid());var storage=new Storage(temp);storage.Save("settings.json",new Settings{Sensitivity=1.4});storage.Save("settings.json",new Settings{Sensitivity=1.5});File.WriteAllText(Path.Combine(temp,"settings.json"),"broken");Check(storage.Load<Settings>("settings.json")?.Sensitivity==1.4,"damaged settings recover the last good copy");
            try{storage.Save("../escape.json",new Settings());throw new Exception("Path escaped");}catch(ArgumentException){Check(true,"storage rejects path traversal");}
            using(var gate=new SafetyGate("Local\\Qadr-test-"+Guid.NewGuid(),true)){gate.Hotkeys=3;gate.Heartbeat=Environment.TickCount64;Check(!NativeInput.Move(gate,0,0),"stopped gate prevents injection");Check(!NativeInput.Button(gate,"back",true)&&!NativeInput.Button(gate,"middle",true)&&!NativeInput.ScrollHorizontal(gate,1)&&!NativeInput.Scroll(gate,-1)&&gate.MouseButtons==0,"stop blocks extra buttons and both scroll axes without claiming ownership");gate.ResumeRequest=1;long requested=gate.ResumeGeneration;gate.Stop();Check(gate.Generation!=requested&&gate.ResumeRequest==0,"stop invalidates a pending resume generation");int userStops=gate.UserStops;gate.UserStop();gate.Stop();Check(!gate.RequestResume(userStops),"a return-to-rest intent from before Esc cannot request automatic resume");Check(gate.UserStops==userStops+1,"explicit stop remains observable across internal calibration stops");gate.Acknowledged=gate.Generation;long generation=gate.Generation;gate.Stop();Check(gate.Generation==generation+1&&gate.Stopped,"stop invalidates the current generation");}
            if(args.Contains("--watchdog"))Watchdog(args);
            AssessmentVerification.Run(Check);
            DiagnosticsVerification.Run(Check);
            StartupVerification.Run(Check);
            RealtimeVerification.Run(Check);
            HandsFreeVerification.Run(Check);
            FacialModeVerification.Run(Check);
            ContinuityVerification.Run(Check,movementAlgorithm);
            EyebrowVerification.Run(Check);
            PersonalMovementVerification.Run(Check);
            MotionRegression.Run(Check);
            AdvancedMovementVerification.Run(Check);
            FollowMovementVerification.Run(Check,movementAlgorithm);
            ReachVerification.Run(Check,movementAlgorithm);
            LaunchVerification.Run(Check);
            ExperienceVerification.Run(Check);PositionVerification.Run(Check,movementAlgorithm);MovementTuningVerification.Run(Check);FeedbackVerification.Run(Check,movementAlgorithm);PrecisionStopVerification.Run(Check,movementAlgorithm);ComfortVerification.Run(Check,movementAlgorithm);ManualAdjustmentVerification.Run(Check);
            Console.WriteLine($"{count} verification checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void CheckSilent(bool valid,string message){if(!valid)throw new Exception(message);}
    static async Task<int> Integration(string[] args)
    {
        string guardian=Arguments.Get(args,"--guardian")!,root=Path.GetDirectoryName(Path.GetFullPath(guardian))!;
        string session="Qadr-integration-"+Guid.NewGuid().ToString("N"),data=Path.Combine(Environment.GetEnvironmentVariable("QADR_TEST_DATA")??Path.GetTempPath(),session);
        var info=new System.Diagnostics.ProcessStartInfo(guardian){UseShellExecute=false,CreateNoWindow=true};
        foreach(string s in new[]{"--session",session,"--data-dir",data,"--shell-pid",Environment.ProcessId.ToString(),"--no-input"})info.ArgumentList.Add(s);
        if(args.Contains("--test-source"))info.ArgumentList.Add("--test-source");
        using var process=System.Diagnostics.Process.Start(info)!;
        using var cancel=new CancellationTokenSource();
        try {
            SharedTransport? transport=null;SafetyGate? openedGate=null;long opened=Environment.TickCount64;
            while(transport is null){try{openedGate??=new SafetyGate(session,false);transport=new SharedTransport(session,false);}catch(FileNotFoundException){if(Environment.TickCount64-opened>30000)throw;await Task.Delay(50);}}
            using var rpc=transport;using var gate=openedGate!;gate.SharedRpc=1;
            EngineStatus? latest=null;int previews=0;var latency=new List<double>();
            var receive=Task.Run(async()=>{long seq=0;try{while(!cancel.IsCancellationRequested){var s=rpc.Read(ref seq);if(s is not null){Volatile.Write(ref latest,s);if(s.Preview is not null)previews++;if(s.Fps>0)latency.Add(s.LatencyMs);}await Task.Delay(20,cancel.Token);}}catch(OperationCanceledException){ }});
            bool choseCamera=false;for(int i=0;i<200;i++){rpc.Send(new Command("heartbeat"));if(!choseCamera&&Volatile.Read(ref latest) is {State:"ChooseCamera",Cameras.Length:>0} choice){rpc.Send(new Command("camera",CameraId:choice.Cameras[0].Id,UserStopEpoch:gate.UserStops));choseCamera=true;}await Task.Delay(100);if(previews>=20&&Volatile.Read(ref latest)?.Fps>0)break;}
            var status=Volatile.Read(ref latest);
            if(receive.IsFaulted)await receive;
            Console.WriteLine($"Guardian exited: {process.HasExited}; heartbeat age: {Environment.TickCount64-gate.Heartbeat}; RPC: {gate.SharedRpc}");
            Console.WriteLine(Protocol.Encode(new{state=status?.State,title=status?.Title,help=status?.Help,previews,fps=status?.Fps}));
            Check(status?.Cameras.Length>0,"native camera names discovered");
            Check(previews>=20 && status?.Fps>0,args.Contains("--test-source")?"synthetic frames traverse the real inference and preview pipeline":"camera-to-worker-to-Engine preview pipeline is live");
            Check(gate.Stopped,"preview calibration keeps input stopped");
            Console.WriteLine(Protocol.Encode(new{fps=status!.Fps,latencyP50Ms=Calibration.Percentile(latency,.5),latencyP95Ms=Calibration.Percentile(latency,.95),face=status.Face,previews,shortcut=status.StopShortcut}));
            rpc.Send(new Command("stop"));await Task.Delay(150);
            Check(gate.Stopped && gate.Acknowledged==gate.Generation,"Engine acknowledges an emergency stop");
            int beforeStop=gate.UserStops;gate.UserStop();
            rpc.Send(new Command("resume",UserStopEpoch:beforeStop));rpc.Send(new Command("useDwell",UserStopEpoch:beforeStop));
            for(int i=0;i<8;i++){rpc.Send(new Command("heartbeat"));await Task.Delay(100);}
            Check(Volatile.Read(ref latest)?.State=="Stopped"&&gate.Stopped&&gate.ResumeRequest==0,"a queued pre-Esc Resume cannot restart calibration or request control");
            Check(Volatile.Read(ref latest)?.CurrentSettings?.RightClickMethod=="either","a queued pre-Esc dwell-only request cannot change click settings");
            rpc.Send(new Command("resume",UserStopEpoch:gate.UserStops));
            for(int i=0;i<15;i++){rpc.Send(new Command("heartbeat"));await Task.Delay(100);if(Volatile.Read(ref latest)?.State=="Positioning")break;}
            Check(Volatile.Read(ref latest)?.State=="Positioning"&&gate.Stopped,"a new Resume after Esc can restart calibration while input stays stopped");
            rpc.Send(new Command("stop"));
            var vision=System.Diagnostics.Process.GetProcessesByName("Qadr.Vision").FirstOrDefault(p=>p.MainModule?.FileName?.StartsWith(root,StringComparison.OrdinalIgnoreCase)==true);
            Check(vision is not null,"vision is an independent process");vision!.Kill();vision.Dispose();
            for(int i=0;i<35;i++){rpc.Send(new Command("heartbeat"));await Task.Delay(100);}
            Check(!process.HasExited && gate.Stopped,"worker crash preserves stop and Guardian supervision");
            Check(System.Diagnostics.Process.GetProcessesByName("Qadr.Vision").Any(p=>p.MainModule?.FileName?.StartsWith(root,StringComparison.OrdinalIgnoreCase)==true),"vision worker restarts once");
            rpc.Send(new Command("shutdown"));cancel.Cancel();await receive;
            Check(process.WaitForExit(3000),"all supervised processes shut down after Shell disconnect");
            Console.WriteLine($"{count} integration checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{cancel.Cancel();if(!process.HasExited)process.Kill(true);}
    }
    static void Watchdog(string[] args)
    {
        string guardian=Arguments.Get(args,"--guardian")!;string session="Local\\Qadr-watchdog-"+Guid.NewGuid().ToString("N");string dir=Path.Combine(Environment.GetEnvironmentVariable("QADR_TEST_DATA")??Path.GetTempPath(),"Qadr-watchdog-"+Guid.NewGuid());
        var info=new System.Diagnostics.ProcessStartInfo(guardian){UseShellExecute=false};
        foreach(string s in new[]{"--session",session,"--data-dir",dir,"--engine",Path.Combine(AppContext.BaseDirectory,"Qadr.Tests.exe"),"--shell-pid","0"})info.ArgumentList.Add(s);
        // A copied test executable recognises Engine arguments through QADR_FAKE_ENGINE.
        info.Environment["QADR_FAKE_ENGINE"]="1";
        using var p=System.Diagnostics.Process.Start(info)!;Check(!p.WaitForExit(300),"Guardian supervises the live test Engine before its deliberate hang");Check(p.WaitForExit(5000),"Guardian ends an unresponsive Engine process");Check(p.ExitCode==0,"Guardian watchdog closes cleanly");Check(File.Exists(Path.Combine(dir,"stopped.flag")),"watchdog persists stopped state");
    }
}


