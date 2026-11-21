using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using Qadr.Core;
using System.IO;

namespace Qadr.Engine;

internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        NativeInput.EnablePhysicalPixels();
        string directory=Arguments.DataDirectory(args);
        try {using var engine=new Controller(args);engine.Run();return 0;}
        catch(Exception e){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"engine-error.txt"),e.ToString());return 1;}
    }
}

internal sealed partial class Controller : IDisposable
{
    readonly string[] args;
    readonly string session;
    readonly Process? shell;
    readonly long startedAt=Environment.TickCount64;
    long shellCheckAt;
    readonly SafetyGate gate;
    readonly Storage storage;
    readonly SharedTransport shared;
    readonly CameraCapture capture;
    VisionClient worker=new();
    readonly CancellationTokenSource cancellation=new();
    readonly Channel<object> commands=Channel.CreateBounded<object>(new BoundedChannelOptions(64){FullMode=BoundedChannelFullMode.Wait});
    readonly Channel<EngineStatus> statuses=Channel.CreateBounded<EngineStatus>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
    readonly Channel<Action> storageWrites=Channel.CreateBounded<Action>(32);
    readonly MotionController motion=new();
    readonly QualityGate quality=new();
    readonly TrackingHealth trackingHealth=new();
    readonly RestNoiseLearner personal=new();
    readonly MovementRecorder movementRecorder;
    string? recordedState;
    Profile? recordedProfile;
    Settings? recordedMovementSettings;
    PointingAssessment? assessment;
    AssessmentResult? assessmentResult;
    bool assessmentSession;
    long assessmentRequestSequence;
    string? assessmentRequest;
    readonly ResumeRest resumeRest=new();
    readonly StartupPointer startupPointer=new();
    bool centreOnReturn,explicitCentre;
    LaunchAssessmentFlow launchCheck;
    Settings? launchSettings;
    MouseZone? launchCanvas;
    bool launchAssessmentRunning;
    void RestoreLaunchSettings(){tuningSavePending=false;if(launchSettings is {} saved){motion.Settings=saved;launchSettings=null;motion.Reset();trajectory.Clear();}launchCanvas=null;launchAssessmentRunning=false;}
    void InterruptLaunch(){if(!launchAssessmentRunning&&launchCheck.Status.Phase!="ready")return;
        if(launchAssessmentRunning){movementTuning=new("interrupted","Check interrupted. Previous movement settings restored.",MovementParameters.From(launchSettings!));LogTuning();}
        EndAssessment("launch interrupted",true);RestoreLaunchSettings();launchCheck.Interrupt();}
    void PumpLaunchStartup(double now){
        // Test deadlines cannot depend on the face being visible or control active.
        if(launchAssessmentRunning&&assessment is {} timed&&timed.HasTimedOut(now,LaunchAssessmentFlow.TargetSeconds))EndAssessment("time limit",false);
        if(!active||returning||gate.Stopped||now-lastCapture>=TrackingTiming.MaximumAgeSeconds)return;
        if(startupPointer.Pending){
            long window=0;try{shell?.Refresh();window=shell?.MainWindowHandle.ToInt64()??0;}catch(InvalidOperationException){}
            var monitor=NativeInput.MonitorBounds(window);
            if(startupPointer.Plan(gate.UserStops,true,monitor.X,monitor.Y,monitor.Width,monitor.Height) is {} centre){
                bool previewOnly=args.Contains("--no-input");bool sent=previewOnly||NativeInput.Move(gate,centre.X,centre.Y);
                movementRecorder.Add(new("startup-centre",now,State:state,RequestedX:centre.X,RequestedY:centre.Y,NativeAccepted:previewOnly?null:sent));
                if(!sent){Fail("Windows could not centre the pointer. Control has stopped.");return;}
                startupPointer.Complete();motion.Reset();trajectory.Clear();
            }
        }
        if(startupPointer.Completed&&launchCheck.Ready()){assessmentRequest="launch";assessmentRequestSequence++;title="Quick movement check";help="Five large targets start automatically. Esc stops and saves.";}

    }
    void BeginLaunchAssessment(Command c){
        if(launchCheck.Status.Phase!="ready"||state!="Tracking"||gate.Stopped||c.Zones is not [var canvas]||
           !new[]{canvas.X,canvas.Y,canvas.Width,canvas.Height}.All(double.IsFinite)||canvas.Width<100||canvas.Height<100||canvas.Width>20000||canvas.Height>20000)return;
        if(movementRecorder.Status.Saving)return;
        StartMovementRecording();if(!movementRecorder.Recording||!launchCheck.Begin())return;
        ClearMouse();launchSettings=motion.Settings;motion.Settings=motion.Settings with{ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false,PuckerDrag=false,MouthScroll=false};motion.Reset();trajectory.Clear();
        launchCanvas=canvas;launchAssessmentRunning=true;assessmentResult=null;StartTuningEvidence();
        movementRecorder.Add(new("launch-assessment",Arguments.Now,Label:"required every launch; five movement-only targets; eight-second target limit; gestures temporarily suspended"));
        if(!args.Contains("--no-input")&&!NativeInput.Move(gate,canvas.X+canvas.Width/2,canvas.Y+canvas.Height/2))Fail("Windows could not position the pointer for the quick test.");
    }
    void FinishLaunchAssessment()=>FinishTunedLaunchAssessment();
    bool restLesson;
    RestNoiseModel? restBackup;
    long savedModelSamples,restLessonSamples;
    double restLessonStarted;
    readonly FacePositioning positioning=new();
    readonly CursorTrajectory trajectory=new();
    readonly MouseActions mouse=new();
    readonly HeadScrollController headScroll=new();
    readonly StartupFlow startup;
    bool cameraVoice,tutorialVoice,routineVoice;
    readonly SpeechCommandGate speechCommands=new();
    (int Epoch,bool Blocked,bool HandsFree,bool Routines,int RoutinesRevision,bool Tutorial,bool Camera,bool Choice,int Cameras)? voiceKey;
    long voiceRevision;
    volatile bool voiceSuppressed;
    int routineVoiceRevision;
    bool DiagnosticControl=>args.Contains("--no-input")&&args.Contains("--test-source")&&args.Contains("--diagnostic-control");
    readonly TutorialFlow tutorial=new();
    int userStops;
    int returnUserStops;
    readonly Queue<(double Due,MouseStep Step)> mouseEvents=new();
    MouseZone[] mouseZones=[];
    string? latchedZone;
    double mouseX,mouseY,lastWheel;
    bool scrollRepeating;
    string mouseFeedback="";
    FacePosition position=new("find","Bring your face into the oval",false);
    int fittedRevision;
    long dockToggleSequence;
    Observation? observation;
    string cue="position",clickFeedback="Hold still on a target to left-click";
    double positionedAt,guideX,guideY,lastPump=Arguments.Now,clickFeedbackUntil;
    string? lesson;
    ClickLearning? clickLearning;
    VisionResult? latest;
    string state="Starting",title="Finding your cameras",help="Qadr will start calibration after you choose a camera.",distance="Waiting for a face",voice="Microphone off";
    string? latestPreview;
    string? preview,activeCamera,pendingRoutine,lastFailure;
    string recordingDraft="new routine";
    bool recordingText;
    double pendingUntil,progress,lastResult,lastCapture,lastStatus,dwell;
    long lastSequence=-1,lastGeneration;
    bool face,active,returning,recording,cameraRunning,cameraStarting,cameraNeedsSetup,workerReady,disposed,workerFailed;
    int workerRestarts;
    double fps,latency,inferenceMs,frames,measuredAt=Arguments.Now;
    bool fpsStarted;
    CameraDevice[] cameras=[];
    Calibration? calibration;
    RoutineService routines;
    public Controller(string[] args)
    {
        this.args=args;session=Arguments.Get(args,"--session")??throw new ArgumentException("Session required");
        startup=new(!args.Contains("--no-input")||DiagnosticControl);
        launchCheck=new(!args.Contains("--no-input")||DiagnosticControl&&args.Contains("--launch-assessment-check"));
        if(int.TryParse(Arguments.Get(args,"--shell-pid"),out int shellId) && shellId>0)shell=Process.GetProcessById(shellId);
        gate=new SafetyGate(session,false);shared=new(session,true);storage=new(Arguments.DataDirectory(args));capture=new(session);
        userStops=gate.UserStops;
        motion.Settings=(storage.Load<Settings>("settings.json")??new()).Validate();
        configuredMovement=MovementParameters.From(motion.Settings);
        movementRecorder=new(Path.Combine(Arguments.DataDirectory(args),"movement-reports"));
        if(motion.Settings.MotionDiagnosticsOnStartup)StartMovementRecording();
        routines=new(storage,gate,text=>Queue(new RoutineDone(text)),Arguments.Get(args,"--shell-pid"));
        routines.ConfirmationRequired+=text=>Queue(new Notice(text));
        lastGeneration=gate.Generation;
        StartWorker();
        _=Task.Run(async()=>{try{await foreach(var save in storageWrites.Reader.ReadAllAsync(cancellation.Token)){try{save();}catch(Exception e){Queue(new Failure("Qadr could not save your settings: "+e.Message));}}}catch(OperationCanceledException){}});
        _=Serve();
        if(DiagnosticControl)Queue(new Devices([new("diagnostic-camera","Diagnostic camera")]));
        else _=Task.Run(async()=>{try{Queue(new Devices(await CameraCapture.Discover()));}catch(Exception e){Queue(new Failure(e.Message));}});
    }
    void StartWorker()
    {
        voiceKey=null;
        worker.Result+=r=>{
            if(r.Type=="result"){if(!DiagnosticControl)Volatile.Write(ref latest,r);}
            else if(r.Type=="preview")Volatile.Write(ref latestPreview,r.Preview);
            else if(r.Type=="voice"&&r.VoiceRevision==Interlocked.Read(ref voiceRevision)&&!voiceSuppressed){
                if(VoiceControl.IsStop(r.Text??""))gate.UserStop();
                else Queue(new VoiceIntent(r.Text??"",r.UserStopEpoch??-1));
            }
            else Queue(r);
        };
        worker.Start(session,Arguments.Get(args,"--python"));worker.ConfigureFps(motion.Settings.TrackingFps);lastResult=Arguments.Now;
    }
    void Queue(object command) {if(!disposed && !cancellation.IsCancellationRequested && !commands.Writer.TryWrite(command))gate.Stop();}
    async Task Serve()
    {
        try {
            using var pipe=new NamedPipeServerStream(session+".engine",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellation.Token);
            using var reader=new StreamReader(pipe);using var writer=new StreamWriter(pipe){AutoFlush=true};
            gate.ShellHeartbeat=Environment.TickCount64;
            var receive=Task.Run(async()=>{
                while(!cancellation.IsCancellationRequested && await reader.ReadLineAsync(cancellation.Token) is string line) {
                    if(line.Length>65536)throw new InvalidDataException("Command too large");
                    Command cmd=Protocol.Decode<Command>(line);
                    if(cmd.Version!=Protocol.Version)throw new InvalidDataException("Command protocol version mismatch");
                    if(cmd.Type=="heartbeat")gate.ShellHeartbeat=Environment.TickCount64;
                    else if(cmd.Type=="stop")gate.UserStop();
                    else if(cmd.Type=="shutdown"){gate.Stop();gate.Closing=1;cancellation.Cancel();break;}
                    else Queue(cmd);
                }
                if(!cancellation.IsCancellationRequested){gate.Stop();cancellation.Cancel();}
            },cancellation.Token);
            while(!cancellation.IsCancellationRequested){var s=await statuses.Reader.ReadAsync(cancellation.Token);await writer.WriteLineAsync(Protocol.Encode(s));if(receive.IsCompleted)await receive;}
        }catch(UnauthorizedAccessException){ /* Shared RPC remains available; no input is armed here. */ }
        catch(Exception e) when(e is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException){if(!cancellation.IsCancellationRequested && gate.SharedRpc==0){gate.Stop();cancellation.Cancel();}}
    }
    public void Run()
    {
        // Warm JSON metadata before announcing control-loop readiness. Input
        // remains stopped during this startup work; the 250 ms watchdog then
        // measures completed control iterations, not cold serializer/JIT work.
        Protocol.Decode<Command>(Protocol.Encode(new Command("heartbeat")));
        Protocol.Encode(new EngineStatus(state,title,help,0,0,0,false,distance,0,0,"",[],null,[],voice,CurrentSettings:motion.Settings,Position:position));
        while(!cancellation.IsCancellationRequested && gate.Closing==0) {
            if(Environment.TickCount64-shellCheckAt>500){shellCheckAt=Environment.TickCount64;if(shell is {HasExited:true} || gate.ShellHeartbeat==0 && shellCheckAt-startedAt>30000){gate.Stop();cancellation.Cancel();break;}}
            if(gate.SharedRpc!=0)for(int i=0;i<16;i++) {
                var cmd=shared.Receive();if(cmd is null)break;
                if(cmd.Type=="heartbeat")gate.ShellHeartbeat=Environment.TickCount64;
                else if(cmd.Type=="stop")gate.UserStop();
                else if(cmd.Type=="shutdown"){gate.Stop();gate.Closing=1;cancellation.Cancel();break;}
                else Queue(cmd);
            }
            if(gate.UserStops!=userStops){startupPointer.Cancel();explicitCentre=centreOnReturn=false;InterruptLaunch();if(assessmentSession){EndAssessment("user stop",true);assessmentRequest="finish";assessmentRequestSequence++;}userStops=gate.UserStops;startup.Cancel();tutorial.Complete();gate.Stop();}
            if(gate.Generation!=lastGeneration){startupPointer.Cancel();explicitCentre=centreOnReturn=false;InterruptLaunch();lastGeneration=gate.Generation;gate.ResumeRequest=0;startup.Cancel();tutorial.Complete();tutorialVoice=cameraVoice=false;voiceKey=null;active=returning=false;if(restLesson){personal.Load(restBackup);restBackup=null;}restLesson=false;personal.Break();calibration=null;lesson=null;ClearMouse();motion.Reset();trajectory.Clear();routines.Cancel();recording=false;pendingRoutine=null;pendingUntil=0;cue="stop";
                state="Stopped";if(lastFailure is null){title="Control is stopped";help="Say resume when you are ready, or choose Resume. Esc stops control; it never resumes.";}}
            if(gate.Stopped)AcknowledgeStop();
            if(gate.PauseRequest!=0){gate.PauseRequest=0;gate.UserStop();}
            if(gate.ShellHeartbeat!=0 && Environment.TickCount64-gate.ShellHeartbeat>1500){gate.Stop();cancellation.Cancel();break;}
            for(int i=0;i<12 && commands.Reader.TryRead(out object? command);i++)try{Handle(command);}catch(Exception e){Fail(e.Message);}
            var result=Volatile.Read(ref latest);
            if(result is not null && result.Sequence!=lastSequence){lastSequence=result.Sequence;ProcessObservation(result);}
            double now=Arguments.Now;movementRecorder.Tick(now);if(launchAssessmentRunning&&!movementRecorder.Recording){InterruptLaunch();gate.UserStop();}else if(assessmentSession&&!movementRecorder.Recording)EndAssessment("recording ended",false);
            if(recordedState!=state){recordedState=state;movementRecorder.Add(new("state",now,State:state,Reason:gate.Stopped?"input stopped":"input permitted"));}
            if(cameraRunning && (capture.Error is not null || now-capture.LastFrame>2)){cameraRunning=false;Fail(capture.Error??"The camera stopped providing frames. Choose another camera or reconnect it.");}
            if(!workerFailed && (cameraRunning && workerReady && now-lastResult>2 || worker.Exited)) {
                gate.Stop();active=false;
                if(workerRestarts++==0){workerReady=false;workerFailed=true;var old=worker;
                    _=Task.Run(()=>{old.Dispose();Queue(new WorkerRestarted());});title="Restarting the face tracker";help="Control stays stopped. Resume after your face returns.";}
                else{Fail("The face tracker stopped. Close Qadr and open it again.");cameraRunning=false;workerFailed=true;}
            }
            if(now-lastCapture>TrackingTiming.MaximumAgeSeconds&&(mouse.Dragging||mouseEvents.Count>0))ClearMouse();
            if(face && now-lastResult>.25){face=false;ClearMouse();motion.Reset();trajectory.Clear();resumeRest.Reset();calibration?.Lost();if(active){centreOnReturn=true;state="Lost";resumeRest.Reset();returning=true;help="Bring your face back into view and rest comfortably.";}}
            PumpLaunchStartup(now);
            if(active&&!returning&&!gate.Stopped&&now-lastCapture<TrackingTiming.MaximumAgeSeconds)PumpMouse();
            if(active && !returning && !gate.Stopped && mouseEvents.Count==0 && trajectory.Ready && now-lastCapture<TrackingTiming.MaximumAgeSeconds && !args.Contains("--no-input")){
                var point=trajectory.Step(now-lastPump,now,motion.Settings.DirectResponse&&motion.Settings.MovementAlgorithm=="adaptive");var cursor=NativeInput.Cursor;
                if((int)Math.Round(point.X)!=cursor.X || (int)Math.Round(point.Y)!=cursor.Y){
                    bool sent=NativeInput.Move(gate,point.X,point.Y);var actual=NativeInput.Cursor;
                    RecordAssessment(assessment?.Feed(Arguments.Now,actual.X,actual.Y,sent&&!gate.Stopped));
                    if(movementRecorder.Recording)movementRecorder.Add(new("pointer",Arguments.Now,Sequence:lastSequence,State:state,CursorX:actual.X,CursorY:actual.Y,RequestedX:point.X,RequestedY:point.Y,NativeAccepted:sent));
                    if(!sent)Fail("Windows could not move the pointer. Control has stopped.");
                }
            }
            lastPump=now;
            SyncVoice();
            if(pendingRoutine is not null && now>pendingUntil)pendingRoutine=null;
            if(now-lastStatus>=1d/motion.Settings.TrackingFps){lastStatus=now;Publish();}
            gate.Heartbeat=Environment.TickCount64;
            Thread.Sleep(8);
        }
        gate.Stop();routines.Cancel();AcknowledgeStop();
    }
    void AcknowledgeStop()
    {
        bool locked=false;
        try {try{locked=gate.InjectionMutex.WaitOne(5);}catch(AbandonedMutexException){locked=true;}
            if(locked){NativeInput.ReleaseOwned(gate);gate.Acknowledged=gate.Generation;}
        }finally{if(locked)gate.InjectionMutex.ReleaseMutex();}
    }
    void Handle(object command)
    {
        switch(command) {
            case Devices d:
                cameras=d.Cameras;
                if(args.Contains("--offer-tutorial")&&!tutorial.Completed){state=tutorial.Page<0?"TutorialChoice":"Tutorial";title="Would you like a tutorial?";help="Say yes for a guide, or no to start camera setup.";cue="tutorial:0";}
                else ChooseStartupCamera();
                break;
            case CameraStarted s:
                cameraStarting=false;cameraRunning=s.Success;
                if(s.UserStopEpoch!=gate.UserStops)break;
                if(s.Success)StartCameraSetup();else Fail(s.Error??"The camera could not start.");break;
            case Failure f:Fail(f.Error);break;
            case Notice n:title=n.Text;help="Review this step, then choose Confirm to continue, or Stop to cancel.";break;
            case WorkerRestarted:worker=new();workerFailed=false;StartWorker();break;
            case TuningPersisted savedTuning:TuningSaved(savedTuning);break;
            case RoutineDone done:state="Stopped";title=done.Text;help="Resume pointer control when you are ready.";gate.Stop();lastGeneration=gate.Generation;break;
            case VoiceIntent v:if(v.UserStopEpoch==gate.UserStops)Voice(v.Text,v.UserStopEpoch);break;
            case VisionResult r:
                if(r.Type=="ready"){workerReady=true;lastResult=Arguments.Now;}
                else if(r.Type=="error")Fail("Face tracker: "+r.Text);
                else if(r.Type=="voice_status" || r.Type=="voice_error")voice=r.Type=="voice_status"&&tutorialVoice&&r.Text=="Listening for English commands"?(state=="TutorialChoice"?"Listening for tutorial choice":"Listening for tutorial navigation"):r.Type=="voice_error"?(cameraVoice?"Microphone unavailable. Choose manually, or check Windows microphone permission and select Read choices and listen again.":"Microphone unavailable. Check Windows microphone permission and your input device, then switch Voice commands off and on to retry."):cameraVoice&&r.Text=="Listening for English commands"?"Listening for camera choice":r.Text??"Microphone off";
                break;
            case Command c:
                if(!CommandIntent.IsCurrent(c,gate.UserStops))break;
                if(state is "TutorialChoice" or "Tutorial" && c.Type is not ("tutorialStart" or "tutorialSkip" or "tutorialNext" or "tutorialBack" or "tutorialVoice" or "stop" or "heartbeat" or "mouseZones" or "settings" or "speechActivity" or "diagnosticVoice" or "shutdown"))break;
                if(launchCheck.Required&&c.Type is "record" or "confirm" or "reviewRoutine" or "mouseMode" or "learnRest" or "resetRest" or "restoreProfile" or "learnDrag" or "learnScroll" or "learnEyebrows" or "learnSmile" or "learnEyes" or "useDwell" or "comfortablePosition")break;
                switch(c.Type) {
                    case "launchAssessmentBegin":BeginLaunchAssessment(c);break;
                    case "launchAssessmentFinish":FinishLaunchAssessment();break;
                    case "movementTuningUndo":UndoTunedMovement();break;
                    case "diagnosticAssessmentResult":DiagnosticAssessmentResult(c.Name);break;
                    case "diagnosticRestartLaunch":if(TuningEvidenceTest&&!launchCheck.Required&&!movementRecorder.Status.Saving){launchCheck=new();movementTuning=null;}break;
                    case "launchAssessmentCancel":InterruptLaunch();gate.UserStop();break;
                    case "motionRecord":if(c.Enabled)StartMovementRecording();else if(launchAssessmentRunning){InterruptLaunch();gate.UserStop();}else{EndAssessment("recording saved",false);movementRecorder.Stop();}break;
                    case "assessmentTarget":
                        if(state=="Tracking"&&!gate.Stopped&&movementRecorder.Recording&&c.Zones is [var z]&&(!launchCheck.Required||launchAssessmentRunning&&c.Action=="move"&&c.Name==launchCheck.NextId)&&(!launchAssessmentRunning||assessment is null)){
                            var expectedTarget=new AssessmentTarget(c.Name??"",c.Action??"left",z.X,z.Y,z.Width,z.Height);
                            if(expectedTarget.Valid&&(!launchAssessmentRunning||AcceptTuningTarget(expectedTarget))){EndAssessment("target replaced",false);var point=NativeInput.Cursor;assessment=new(expectedTarget,Arguments.Now,point.X,point.Y,NativeInput.GetDoubleClickTime()/1000d+.06);assessmentResult=null;assessmentSession=true;
                                movementRecorder.Add(new("assessment-target",Arguments.Now,Label:expectedTarget.Id+":"+expectedTarget.Action,TargetX:expectedTarget.X,TargetY:expectedTarget.Y,TargetWidth:expectedTarget.Width,TargetHeight:expectedTarget.Height));}
                        }break;
                    case "assessmentClick":
                        if(assessment is {} activeTrial&&activeTrial.Progress.Target.Id==c.Name&&state=="Tracking"&&!gate.Stopped&&Arguments.Now-lastCapture<TrackingTiming.MaximumAgeSeconds&&c.Action is "left" or "double" or "right"&&c.InputX is {} inputX&&c.InputY is {} inputY&&double.IsFinite(inputX)&&double.IsFinite(inputY)){
                            movementRecorder.Add(new("assessment-click",Arguments.Now,State:state,Label:c.Action,CursorX:inputX,CursorY:inputY));
                            RecordAssessment(activeTrial.Click(c.Action,Arguments.Now,inputX,inputY));
                        }break;
                    case "assessmentSkip":EndAssessment("skipped",false);break;
                    case "assessmentEnd":if(launchAssessmentRunning){InterruptLaunch();gate.UserStop();}else EndAssessment("test ended",true);break;
                    case "motionLabel":if(c.Name is "rest" or "movement" or "clicks")movementRecorder.Add(new("label",Arguments.Now,Label:c.Name));break;
                    case "motionTarget":case "motionTargetHit":if(c.Zones is [var target]&&new[]{target.X,target.Y,target.Width,target.Height}.All(double.IsFinite)&&target.Width>0&&target.Height>0&&target.Width<10000&&target.Height<10000){var point=NativeInput.Cursor;movementRecorder.Add(new(c.Type=="motionTarget"?"target":"target-hit",Arguments.Now,State:state,Label:c.Name,CursorX:point.X,CursorY:point.Y,TargetX:target.X,TargetY:target.Y,TargetWidth:target.Width,TargetHeight:target.Height));}break;
                    case "speechActivity":speechCommands.Set(c.Enabled,Arguments.Now);voiceSuppressed=speechCommands.Blocked(Arguments.Now);voiceKey=null;break;
                    case "diagnosticResult":if(DiagnosticControl&&c.DiagnosticResult is not null)ProcessObservation(c.DiagnosticResult);break;
                    case "diagnosticVoice":if(DiagnosticControl)Voice(c.Name??"",c.UserStopEpoch??-1);break;
                    case "tutorialStart":if(state=="TutorialChoice"&&tutorial.Start())ShowTutorialPage();break;
                    case "tutorialSkip":if(state is "TutorialChoice" or "Tutorial")EndTutorial();break;
                    case "tutorialNext":if(state=="Tutorial"){if(tutorial.Next())EndTutorial();else ShowTutorialPage();}break;
                    case "tutorialBack":if(state=="Tutorial"){tutorial.Back();ShowTutorialPage();}break;
                    case "tutorialVoice":tutorialVoice=c.Enabled&&state is "TutorialChoice" or "Tutorial";voiceKey=null;break;
                    case "camera":if(c.CameraId is not null && cameras.Any(d=>d.Id==c.CameraId))OpenCamera(c.CameraId);break;
                    case "refresh":_=Task.Run(async()=>{try{Queue(new Devices(await CameraCapture.Discover()));}catch(Exception e){Queue(new Failure(e.Message));}});break;
                    case "recentre":movementRecorder.Add(new("control-intent",Arguments.Now,State:state,Label:"recentre",Reason:"explicit recentre requested"));InterruptLaunch();if(motion.Profile is not null&&face){explicitCentre=centreOnReturn=true;ClearMouse();motion.Reset();trajectory.Clear();active=false;returning=true;resumeRest.Reset();returnUserStops=gate.UserStops;state="Ready";title="Rest to recentre";help="Your learned movement range is preserved.";}break;
                    case "learnRest":if(motion.Profile is not null&&face){gate.Stop();lastGeneration=gate.Generation;ClearMouse();trajectory.Clear();motion.Reset();active=returning=false;restBackup=personal.Model;restLessonSamples=personal.Model.Samples;restLesson=true;restLessonStarted=0;personal.Break();state="LearnRest";cue="rest";title="Learn your resting steadiness";help="Rest naturally for four seconds. No mouse actions are sent. Esc cancels.";}break;
                    case "resetRest":personal.Load(null);motion.PersonalModel=personal.Model;SavePersonal(true);title="Personal noise learning reset";break;
                    case "calibrate":gate.Stop();lastGeneration=gate.Generation;routines.Cancel();pendingRoutine=null;if(cameraRunning)StartCalibration();break;
                    case "comfortablePosition":if(observation is not null){positioning.UseComfortablePosition(observation);if(motion.Profile is not null){motion.Rebase(observation);BeginReturn(expectedUserStops:c.UserStopEpoch);}else if(cameraRunning)StartCalibration();}break;
                    case "learnDrag":StartLesson("pucker");break;
                    case "learnScroll":if(motion.Settings.ClickMethod=="mouth"){title="Choose another left-click method first";help="Mouth opening is already assigned to left-click. Select dwell or smile before learning scrolling.";}else StartLesson("scroll");break;
                    case "learnEyebrows":StartLesson("brows");break;
                    case "learnSmile":StartLesson("smile");break;
                    case "learnEyes":StartLesson("eyes");break;
                    case "mouseMode":if(c.Name is not null)SelectMouse(c.Name);break;
                    case "mouseZones":if(c.Zones is {Length:<=48})mouseZones=c.Zones.Where(z=>new[]{z.X,z.Y,z.Width,z.Height}.All(double.IsFinite)&&z.Width>0&&z.Height>0&&z.Width<10000&&z.Height<10000&&z.Window!=0).ToArray();break;
                    case "resume":if(active&&!returning&&!gate.Stopped&&state=="Tracking"){movementRecorder.Add(new("control-intent",Arguments.Now,State:state,Label:"resume",Reason:"ignored: control already running"));break;}movementRecorder.Add(new("control-intent",Arguments.Now,State:state,Label:"resume",Reason:"resume requested"));startup.Begin();if(cameraRunning&&cameraNeedsSetup){StartCameraSetup();break;}if(!cameraRunning){if(activeCamera is not null)OpenCamera(activeCamera);else if(cameras.Length==1)OpenCamera(cameras[0].Id);else if(cameras.Length>1){state="ChooseCamera";title="Choose your camera";help="Choose a camera manually or by voice.";}else Fail("No camera was found. Connect a camera and choose Refresh cameras.");}else if(motion.Profile is null)StartCalibration();else BeginReturn(expectedUserStops:c.UserStopEpoch);break;
                    case "adjustMovement":AdjustMovement(c.Name);break;
                    case "undoAdjustment":UndoAdjustment();break;
                    case "settings":if(launchAssessmentRunning)break;if(c.Settings is not null)ApplyManualSettings(c.Settings);break;
                    case "restoreProfile":if(activeCamera is not null && storage.Load<Profile>("profiles/"+Storage.DeviceKey(activeCamera)+".json")?.Upgrade() is {IsValid:true} p){motion.SetProfile(p);gate.Stop();title="Saved movement restored";}else{title="Relearn your movement";help="This saved profile uses the earlier direction mapping. Choose Relearn my movement.";}break;
                    case "useDwell":motion.Settings=motion.Settings with{ClickMethod="dwell",RightClickMethod="none"};var dwellSettings=configuredMovement.Apply(motion.Settings);storageWrites.Writer.TryWrite(()=>storage.Save("settings.json",dwellSettings));lesson=null;clickLearning=null;motion.Reset();trajectory.Clear();if(motion.Profile is not null){calibration=null;BeginReturn(expectedUserStops:c.UserStopEpoch);}else if(cameraRunning)StartCalibration();break;
                    case "cameraVoice":cameraVoice=c.Enabled&&state=="ChooseCamera";voiceKey=null;break;
                    case "voice":routineVoice=c.Enabled;voiceKey=null;break;
                    case "record":if(c.Name is not null){routines.StartRecording(c.Name,c.Enabled);recording=true;title="Recording your routine";help="Perform the steps, then choose Save recording. Password fields are excluded.";}break;
                    case "routineDraft":recordingDraft=c.Name??"new routine";recordingText=c.Enabled;break;
                    case "saveRecording":if(recording){routines.SaveRecording();recording=false;title="Routine saved";routineVoiceRevision++;voiceKey=null;}break;
                    case "reviewRoutine":if(c.Name is not null){pendingRoutine=c.Name;pendingUntil=Arguments.Now+30;title="Review: "+c.Name;help=routines.Describe(c.Name)+" Choose Confirm to run.";}break;
                    case "confirm":if(routines.WaitingForConfirmation)routines.ConfirmStep();else if(pendingRoutine is not null && Arguments.Now<=pendingUntil){string n=pendingRoutine;pendingRoutine=null;routines.Run(n,args.Contains("--no-input"),c.UserStopEpoch!.Value);state="Routine";active=false;}break;
                }break;
        }
    }
    void SyncVoice(){
        if(!workerReady)return;
        bool blocked=speechCommands.Blocked(Arguments.Now);voiceSuppressed=blocked;
        var key=(gate.UserStops,blocked,motion.Settings.HandsFreeVoice,routineVoice,routineVoiceRevision,tutorialVoice,cameraVoice,state=="TutorialChoice",cameras.Length);
        if(voiceKey.HasValue&&voiceKey.Value==key)return;voiceKey=key;
        long revision=Interlocked.Increment(ref voiceRevision);
        string[]? phase=tutorialVoice?TutorialFlow.Phrases(state=="TutorialChoice"):cameraVoice?CameraVoice.Phrases(cameras.Length):null;
        string[] phrases=blocked?[]:VoiceControl.Phrases(motion.Settings.HandsFreeVoice,routineVoice,routines.Names,phase);
        if(args.Contains("--no-input"))phrases=[];
        // Keep a live microphone stream during narration, but accept no phrases.
        // Changing revision discards buffered audio from the preceding phase.
        bool listen=!args.Contains("--no-input")&&(motion.Settings.HandsFreeVoice||routineVoice||cameraVoice||tutorialVoice);
        worker.Voice(listen,[],phrases,gate.UserStops,revision);
        voice=listen?(blocked?"Speaking guidance; listening resumes after the prompt":tutorialVoice?(state=="TutorialChoice"?"Listening for tutorial choice":"Listening for tutorial navigation"):cameraVoice?"Listening for camera choice":"Listening: say resume or stop the app"):"Microphone off";
    }
    void OpenCamera(string id)
    {
        InterruptLaunch();
        if(cameraStarting)return;
        trackingHealth.Reset();fpsStarted=false;fps=frames=0;
        CancelRestLesson();
        cameraVoice=false;voiceKey=null;
        lastFailure=null;
        startup.Begin();
        if(cameraRunning){gate.Stop();lastGeneration=gate.Generation;}
        int selectionEpoch=gate.UserStops;
        cameraStarting=true;cameraNeedsSetup=true;cameraRunning=false;active=false;calibration=null;activeCamera=id;state="Starting";title="Opening your camera";
        _=Task.Run(async()=>{try{if(args.Contains("--test-source"))capture.StartTestFrames(motion.Settings.TrackingFps);else await capture.Start(id,motion.Settings.TrackingFps);Queue(new CameraStarted(true,UserStopEpoch:selectionEpoch));}catch(Exception e){Queue(new CameraStarted(false,e is UnauthorizedAccessException || e.HResult==unchecked((int)0x80070005)?"Windows denied camera access. Run Qadr normally outside the test sandbox, then check camera permissions if needed.":e.Message,selectionEpoch));}});
    }
    void StartCameraSetup(){
        cameraNeedsSetup=false;
        var key=activeCamera is null?null:Storage.DeviceKey(activeCamera);
        personal.Load(key is null?null:storage.Load<RestNoiseModel>("learning/"+key+".json"));motion.PersonalModel=personal.Model;savedModelSamples=personal.Model.Samples;
        var saved=key is null?null:storage.Load<Profile>("profiles/"+key+".json")?.Upgrade();
        if(saved?.IsValid!=true){StartCalibration();return;}
        motion.SetProfile(saved);LoadTunedMovement(saved);calibration=null;lesson=null;active=false;returning=true;resumeRest.Reset();returnUserStops=gate.UserStops;state="Ready";cue="rest";title="Welcome back â€” your movement is saved";help="Rest comfortably. Qadr will recentre without repeating movement calibration.";
    }
    void SavePersonal(bool force=false){if(activeCamera is null||personal.Model.Samples==savedModelSamples||!force&&personal.Model.Samples-savedModelSamples<30)return;var model=personal.Model;var path="learning/"+Storage.DeviceKey(activeCamera)+".json";if(storageWrites.Writer.TryWrite(()=>storage.Save(path,model)))savedModelSamples=model.Samples;}
    void CancelRestLesson(){if(!restLesson)return;personal.Load(restBackup);motion.PersonalModel=personal.Model;restBackup=null;restLesson=false;}
    void StartCalibration()
    {
        InterruptLaunch();
        CancelRestLesson();
        gate.Stop();lastGeneration=gate.Generation;routines.Cancel();pendingRoutine=null;
        ClearMouse();
        lastFailure=null;restLesson=false;resumeRest.Reset();personal.Break();
        lesson=null;active=false;returning=false;motion.Reset();trajectory.Clear();calibration=new();positionedAt=0;progress=0;state="Positioning";cue="position";title="Place your face inside the oval";help="Follow the arrows. You can also use your comfortable position.";
    }
    void BeginReturn(bool automatic=false,int? expectedUserStops=null){CancelRestLesson();int expected=automatic?userStops:expectedUserStops??gate.UserStops;if(gate.UserStops!=expected){startup.Cancel();return;}returnUserStops=expected;motion.PersonalModel=personal.Model;ClearMouse();if(gate.Hotkeys==0){Fail("No global stop shortcut is available. Close conflicting apps and restart Qadr before controlling the pointer.");return;}if(motion.Settings.ClickMethod=="smile"&&!motion.Settings.SmileLearned)motion.Settings=motion.Settings with{ClickMethod="dwell"};lesson=null;lastFailure=null;returning=true;active=false;resumeRest.Reset();progress=0;state="Ready";cue="rest";title="Return to your relaxed position";help="Settle in your current comfortable position briefly. Esc stops control everywhere.";motion.Reset();trajectory.Clear();}
    void StartLesson(string kind){CancelRestLesson();if(!face){title="Bring your face into view to learn clicks";return;}gate.Stop();lastGeneration=gate.Generation;ClearMouse();active=returning=false;calibration=null;trajectory.Clear();routines.Cancel();lesson=kind;clickLearning=new(kind=="eyes",kind=="smile"?motion.Settings:motion.Settings with{SubtleSmile=false});state="LearnClick";cue="rest";title="First, relax your face";help="Keep your eyes open and rest naturally for a moment.";}
    void LearnClick(Observation o){
        double value=lesson=="pucker"?o.Pucker:lesson=="scroll"?o.Mouth:lesson=="brows"?o.Eyebrow:lesson=="smile"?o.Smile:Math.Max(o.LeftEye,o.RightEye);
        var feedback=clickLearning!.Feed(value,o.Timestamp);progress=feedback.Progress;
        if(feedback.Phase=="rest"){cue="rest";title="First, relax your face";help="Keep your eyes open and rest naturally for a moment.";return;}
        cue=lesson is "pucker" or "scroll"?lesson:lesson=="brows"?"brows":lesson=="smile"?"smile":"eyes";
        title=lesson is "pucker" or "scroll"?(feedback.Phase=="release"?"Now relax your mouth":lesson=="pucker"?"Pucker your lips gently":"Open your mouth comfortably"):lesson=="brows"?(feedback.Phase=="release"?"Now relax your eyebrows":"Raise your eyebrows gently"):feedback.Phase=="release"?"Now relax and reopen your eyes":lesson=="smile"?"Make the smallest comfortable smile":"Close one eye, or both eyes";
        help="Hold gently until the bar fills, then relax. Take as much time as you need.";
        if(feedback.Threshold is not double threshold)return;
        motion.Settings=(lesson=="pucker"?motion.Settings with{PuckerThreshold=Math.Max(.05,threshold),PuckerRestLevel=feedback.RestLevel,PuckerLearned=true,PuckerDrag=true}:lesson=="scroll"?motion.Settings with{ScrollGestureThreshold=Math.Max(.05,threshold),ScrollGestureRestLevel=feedback.RestLevel,ScrollGestureLearned=true,MouthScroll=true}:lesson=="brows"?motion.Settings with{EyebrowThreshold=Math.Max(.05,threshold),EyebrowRestLevel=feedback.RestLevel,EyebrowLearned=true,EyebrowDoubleClick=true}:lesson=="smile"?motion.Settings with{SmileThreshold=threshold,SmileRestLevel=feedback.RestLevel,SmileLearned=true,ClickMethod="smile"}:motion.Settings with{BlinkThreshold=threshold,EyeRestLevel=feedback.RestLevel,EyesLearned=true,RightClickMethod=motion.Settings.RightClickMethod=="none"?"either":motion.Settings.RightClickMethod}).Validate();var saved=configuredMovement.Apply(motion.Settings);storageWrites.Writer.TryWrite(()=>storage.Save("settings.json",saved));
        bool moreEyes=lesson=="smile"&&motion.Settings.RightClickMethod!="none"&&!motion.Settings.EyesLearned;
        lesson=null;clickLearning=null;state="ClickSetup";cue="click";motion.Reset();title="Your click gesture is learned";help="Choose Resume, then try the large practice target.";
        if(moreEyes)StartLesson("eyes");
        else if(startup.Advance())BeginReturn(true);
    }
    void ProcessObservation(VisionResult result)
    {
        double now=Arguments.Now;lastResult=now;lastCapture=result.Captured;
        if(!fpsStarted){measuredAt=now;fpsStarted=true;}
        if(result.Preview is not null)preview=result.Preview;
        frames++;if(now-measuredAt>=1){fps=frames/(now-measuredAt);frames=0;measuredAt=now;}
        latency=(now-result.Captured)*1000;inferenceMs=Math.Max(0,result.InferenceMs);
        Observation? o=Features.Extract(result);observation=o;face=o is not null;positioning.Settings=motion.Settings;position=positioning.Check(o,calibration?.Resting==true || state=="Positioning");
        if(o is not null && (calibration?.Resting==true || state=="Positioning")){guideX=Math.Clamp((o.FaceX-position.X)/Math.Max(.05,position.Width/2),-1,1);guideY=Math.Clamp((o.FaceY-position.Y)/Math.Max(.05,position.Height/2),-1,1);}
        else if(o is not null && motion.Profile is { } shownProfile){double[] signals=shownProfile.Signals(o);guideX=Math.Clamp(signals[0]/shownProfile.Ranges[0][signals[0]<0?0:1],-1,1);guideY=Math.Clamp(signals[1]/shownProfile.Ranges[1][signals[1]<0?0:1],-1,1);}
        bool accepted=quality.Accept(o,now);trackingHealth.Feed(now,o is not null,accepted,latency,inferenceMs);if(launchAssessmentRunning&&!accepted)tuningQualityValid=false;var sampledCursor=NativeInput.Cursor;
        if(movementRecorder.Recording)movementRecorder.Add(new("frame",now,Sequence:result.Sequence,State:state,Face:o,QualityAccepted:accepted,Reason:accepted?null:o is null?"face unavailable":now-o.Timestamp>TrackingTiming.MaximumAgeSeconds?"stale frame":now<o.Timestamp?"future frame":"stabilizing or discontinuity",CursorX:sampledCursor.X,CursorY:sampledCursor.Y,ProcessingMs:Math.Max(0,latency),InferenceMs:inferenceMs,CaptureFps:capture.ActualFps,TrackingFps:fps));
        RecordAssessment(assessment?.Feed(now,sampledCursor.X,sampledCursor.Y,accepted&&active&&!returning&&!gate.Stopped&&state=="Tracking"));
        if(!accepted){dwell=0;ClearMouse();motion.Reset();trajectory.Clear();resumeRest.Reset();positionedAt=0;calibration?.Lost();clickLearning?.Lost();if(restLesson)restLessonStarted=0;if(o is null){distance="Bring your face into view";cue="position";}if(active){centreOnReturn=true;active=false;returning=true;resumeRest.Reset();state="Degraded";title="Tracking paused â€” your calibration is kept";help="Bring your whole face back into view and rest briefly.";}personal.Break();resumeRest.Reset();return;}
        if(o is null)return;
        if(positioning.Revision!=fittedRevision){fittedRevision=positioning.Revision;if(!active&&calibration is null&&motion.Profile is not null&&positioning.FittedObservation is {} fit){motion.Rebase(fit);ClearMouse();trajectory.Clear();resumeRest.Reset();if(active||returning){active=false;returning=true;state="Ready";cue="rest";title="Rest in your updated comfortable position";help="The face guide and movement now share the same tracked position.";}}}
        distance=position.Hint;
        if(restLesson&&motion.Profile is {} learningProfile){
            bool relaxed=o.Smile<motion.Settings.SmileThreshold&&o.Mouth<motion.Settings.MouthThreshold&&Math.Max(o.LeftEye,o.RightEye)<motion.Settings.BlinkThreshold&&(!motion.Settings.PuckerDrag||!motion.Settings.PuckerLearned||o.Pucker<motion.Settings.PuckerThreshold)&&(!motion.Settings.MouthScroll||!motion.Settings.ScrollGestureLearned||o.Mouth<motion.Settings.ScrollGestureThreshold)&&(!motion.Settings.EyebrowDoubleClick||!motion.Settings.EyebrowLearned||o.Eyebrow<motion.Settings.EyebrowThreshold);
            if(!relaxed){restLessonStarted=0;personal.Break();progress=0;return;}
            if(restLessonStarted==0)restLessonStarted=now;personal.Feed(o,learningProfile,true);progress=Math.Clamp((now-restLessonStarted)/4,0,1);
            if(progress>=1&&personal.Model.Samples-restLessonSamples>=60){restLesson=false;restBackup=null;motion.PersonalModel=personal.Model;SavePersonal(true);state="Stopped";title="Resting model saved on this PC";help="Choose Resume to try your personalized movement. Preferences can switch it off or reset it.";}return;
        }
        if(lesson is not null){LearnClick(o);return;}
        if(calibration is null && (active||returning) && !position.Ready){if(active)centreOnReturn=true;ClearMouse();active=false;returning=true;resumeRest.Reset();trajectory.Clear();motion.Reset();state="Degraded";cue="position";title=position.Hint;help="The guide adapts while you rest. Control waits until tracking is stable.";return;}
        if(calibration is not null) {
            if(!position.Ready){positionedAt=0;calibration.Lost();state="Positioning";cue="position";title=position.Hint;help="Use the oval and arrow, or choose Use my comfortable position.";return;}
            if(positionedAt==0)positionedAt=now;if(now-positionedAt<.65)return;
            state="Calibrating";
            var f=calibration.Feed(o);title=f.Title;help=f.Help;progress=f.Progress;
            cue=calibration.Resting?"rest":f.Help.Contains("up or down")?"vertical":"sides";guideX=f.X;guideY=f.Y;
            if(f.Profile is not null){motion.SetProfile(f.Profile);LoadTunedMovement(f.Profile);if(activeCamera is not null){string key="profiles/"+Storage.DeviceKey(activeCamera)+".json";var profile=f.Profile;storageWrites.Writer.TryWrite(()=>storage.Save(key,profile));}
                // Rest samples cannot establish how strongly this person can
                // wink or smile. Keep learned thresholds; learn actual gestures
                // on Resume instead of raising them after each calibration.
                motion.Settings=(motion.Settings with{MouthThreshold=Math.Max(.3,calibration.MouthRest+.2),MouthRestLevel=calibration.MouthRest}).Validate();
                var clickSettings=configuredMovement.Apply(motion.Settings);storageWrites.Writer.TryWrite(()=>storage.Save("settings.json",clickSettings));
                calibration=null;state="ClickSetup";cue="click";title="Your movement is learned";help="Choose your left- and right-click methods, then Resume and try the large practice target.";if(startup.Advance())BeginReturn(true);}
            return;
        }
        if(returning && motion.Profile is {} p) {
            // Learn today's supported centre rather than requiring the user to
            // find an old origin. Eye/smile scores are baselined, not entry gates.
            bool settled=resumeRest.Feed(o,p);progress=resumeRest.Progress;
            cue="rest";title="Settle comfortably to start";help="Keep a comfortable position briefly. You do not need to match the old centre or force your eyes open.";
            if(!settled)return;
            if(!gate.RequestResume(returnUserStops)){startup.Cancel();return;}
            // A recovered sideways pose must not silently become the new screen centre.
            // Startup and explicit Recentre establish the reference; ordinary loss keeps it.
            if(motion.Settings.Mapping!="position"||!startupPointer.Completed||explicitCentre)motion.Rebase(resumeRest.Centre());
            motion.SetExpressionRest(resumeRest.Samples);
            movementRecorder.Add(new("recentre",now,Profile:motion.Profile,MovementSettings:motion.EffectiveSettings));
            if(motion.Settings.PersonalizeMovement){foreach(var sample in resumeRest.Samples)personal.Feed(sample,motion.Profile!,true);SavePersonal();}
            startupPointer.Arm(returnUserStops,recentre:(explicitCentre||centreOnReturn&&motion.Settings.CentreOnRecovery&&motion.Settings.Mapping!="position")&&!assessmentSession&&!launchAssessmentRunning);
            explicitCentre=centreOnReturn=false;
            resumeRest.Reset();startup.Started();returning=false;active=true;state="Tracking";cue="control";
            title="Your movement. Your control.";help=motion.Settings.Mapping=="rate"?"Hold a small head turn to move; return to your relaxed centre to stop. Esc stops control.":"Move your head to guide the pointer, then settle to stop. Use Recentre if your resting position changes. Esc stops control.";
            motion.Reset();trajectory.Clear();return;
        }
        if(!active || gate.Stopped || state=="Routine"||startupPointer.Pending||launchCheck.Required&&!launchAssessmentRunning)return;
        // Position mode uses one physical screen frame during both assessment and
        // daily control. Scaling the same head pose into a smaller test canvas hid
        // the actual desktop gain and made the startup result misleading.
        var d=NativeInput.Desktop;var bounds=launchAssessmentRunning&&motion.Settings.Mapping!="position"&&launchCanvas is {} area?(X:area.X,Y:area.Y,Width:area.Width,Height:area.Height):(X:(double)d.X,Y:(double)d.Y,Width:(double)d.Width,Height:(double)d.Height);motion.Width=d.Width;motion.Height=d.Height;var cursor=NativeInput.Cursor;
        var output=motion.Feed(o,cursor.X,cursor.Y,suppressLeft:headScroll.Active);dwell=output.Dwell;
        if(movementRecorder.Recording){
            if(!ReferenceEquals(recordedProfile,motion.Profile)||!ReferenceEquals(recordedMovementSettings,motion.EffectiveSettings)){recordedProfile=motion.Profile;recordedMovementSettings=motion.EffectiveSettings;movementRecorder.Add(new("configuration",now,Profile:recordedProfile,MovementSettings:recordedMovementSettings));}
            movementRecorder.Add(new("control",now,Sequence:result.Sequence,State:state,Output:output,CursorX:cursor.X,CursorY:cursor.Y));
        }
        if(output.DistanceChanged){ClearMouse();trajectory.Clear();distance="Adjusting to your new distance â€” rest briefly";cue="rest";return;}
        cue="control";
        if(now>clickFeedbackUntil)clickFeedback=motion.RightProgress>0?"Keep closed, then reopen":motion.Settings.EyebrowDoubleClick&&motion.Settings.EyebrowLearned?"Raise eyebrows, hold briefly, then relax to double-click":motion.Settings.ClickMethod=="dwell"?"Rest on your target to left-click":"Use your learned left-click gesture";
        if(output.Click=="scroll-toggle"){
            if(mouse.Dragging){clickFeedback="Drop your item before starting scroll mode";clickFeedbackUntil=now+2;return;}
            bool wasScrolling=headScroll.Active;ClearMouse();trajectory.Reset(cursor.X,cursor.Y);
            if(!wasScrolling&&motion.Profile is {} scrollProfile)headScroll.Start(o,scrollProfile,cursor.X,cursor.Y);
            clickFeedback=wasScrolling?"Scroll mode off":"Scroll mode on â€” move head up/down; repeat mouth gesture to exit";clickFeedbackUntil=now+2;return;
        }
        if(headScroll.Active){
            trajectory.Reset(cursor.X,cursor.Y);
            if(Math.Abs(cursor.X-headScroll.AnchorX)>8||Math.Abs(cursor.Y-headScroll.AnchorY)>8){ClearMouse();return;}
            if(output.Click=="drag"){ClearMouse();StartMouse("drag",output.ClickX,output.ClickY);return;}
            if(motion.Profile is {} scrollProfile&&mouseEvents.Count==0){int notches=headScroll.Feed(o,scrollProfile,motion.Settings,output.Frozen);if(notches!=0){mouse.Select(notches>0?"scroll-up":"scroll-down");StartMouse("left",cursor.X,cursor.Y);}}
            return;
        }
        if(output.Frozen||mouseEvents.Count>0)trajectory.Reset(cursor.X,cursor.Y);
        else if(output.PositionX is {} nx&&output.PositionY is {} ny){
            // Absolute targets bypass relative accumulation and interpolation backlog.
            double px=bounds.X+(nx+1)/2*(bounds.Width-1),py=bounds.Y+(ny+1)/2*(bounds.Height-1);
            trajectory.Reset(px,py);
        }
        else if(output.IsRate)trajectory.SetRate(output.VelocityX,output.VelocityY,cursor.X,cursor.Y,bounds.X,bounds.Y,bounds.Width,bounds.Height,o.Timestamp);
        else trajectory.Add(output.Dx,output.Dy,cursor.X,cursor.Y,bounds.X,bounds.Y,bounds.Width,bounds.Height,output.StopX,output.StopY);
        var zones=mouseZones.Where(z=>cursor.X>=z.X&&cursor.X<z.X+z.Width&&cursor.Y>=z.Y&&cursor.Y<z.Y+z.Height).ToArray();
        var zone=zones.Length==0||shell is null?null:zones.FirstOrDefault(z=>z.Window==NativeInput.WindowAt(cursor)&&NativeInput.WindowOwner(z.Window)==shell.Id);
        if(zone is null)latchedZone=null;
        if(output.Click is not null&&zone is not null){scrollRepeating=false;if(latchedZone!=zone.Id){string action=zone.Id.Split(':').Last();if(action=="stop")gate.UserStop();else if(action=="toggle")dockToggleSequence++;else SelectMouse(action);latchedZone=zone.Id;}return;}
        if(output.Click is not null&&mouseEvents.Count==0)StartMouse(output.Click,output.ClickX,output.ClickY);
        else if(zone is null&&scrollRepeating&&mouse.Scrolling&&motion.HoldingClick&&motion.RightProgress==0&&now-lastWheel>=motion.Settings.ScrollRepeatSeconds)StartMouse("left",cursor.X,cursor.Y);
        if(!motion.HoldingClick||zone is not null)scrollRepeating=false;
    }
    void ClearMouse(){headScroll.Reset();bool owned=mouse.Dragging||mouseEvents.Count>0||state!="Routine"&&gate.MouseButtons!=0;mouseEvents.Clear();scrollRepeating=false;latchedZone=null;if(!args.Contains("--no-input")&&owned)NativeInput.ReleaseOwned(gate);mouse.Reset();}
    void SelectMouse(string mode){
        headScroll.Reset();
        if(mode!="release"&&!MouseActions.Modes.Any(m=>m.Id==mode))return;
        ClearMouse();if(mode!="release")mouse.Select(mode);
        clickFeedback=mode=="release"?"Released â€” left click":mouse.Help;clickFeedbackUntil=Arguments.Now+2;
    }
    void StartMouse(string source,double x,double y){
        movementRecorder.Add(new("gesture",Arguments.Now,Sequence:lastSequence,State:state,Label:source,CursorX:x,CursorY:y));
        var plan=mouse.Trigger(source,motion.Settings.ScrollNotches,Math.Min(.06,NativeInput.GetDoubleClickTime()/4000.0));
        scrollRepeating=mouse.Scrolling;lastWheel=Arguments.Now;
        mouseX=x;mouseY=y;mouseFeedback=plan.Feedback;trajectory.Reset(x,y);
        if(args.Contains("--no-input")){clickFeedback="Detected: "+plan.Feedback+" (preview only)";clickFeedbackUntil=Arguments.Now+1.3;return;}
        foreach(var step in plan.Steps)mouseEvents.Enqueue((Arguments.Now+step.Delay,step));
        PumpMouse();
    }
    void PumpMouse(){
        if(mouseEvents.Count==0)return;
        while(mouseEvents.TryPeek(out var next)&&next.Due<=Arguments.Now){
            if(gate.Stopped){ClearMouse();return;}mouseEvents.Dequeue();var step=next.Step;
            bool sent=NativeInput.Move(gate,mouseX,mouseY)&&step.Kind switch{
                "down"=>NativeInput.Button(gate,step.Button,true),"up"=>NativeInput.Button(gate,step.Button,false),
                "vertical"=>NativeInput.Scroll(gate,step.Amount),"horizontal"=>NativeInput.ScrollHorizontal(gate,step.Amount),_=>false};
            movementRecorder.Add(new("mouse-action",Arguments.Now,Sequence:lastSequence,State:state,Label:step.Kind+":"+step.Button,RequestedX:mouseX,RequestedY:mouseY,NativeAccepted:sent));
            if(!sent){ClearMouse();Fail("Windows could not send the mouse action. Control has stopped. Elevated apps can block input.");return;}
        }
        if(mouseEvents.Count==0){clickFeedback=mouseFeedback+" sent âœ“";clickFeedbackUntil=Arguments.Now+1.3;}
    }
    void Voice(string text,int epoch)
    {
        if(speechCommands.Blocked(Arguments.Now))return;
        text=VoiceControl.Normalize(text);
        if(text=="undo movement settings"){Handle(new Command("movementTuningUndo",UserStopEpoch:epoch));return;}
        if(text is "centre pointer" or "center pointer" or "recenter pointer"){Handle(new Command("recentre",UserStopEpoch:epoch));return;}
        if(text=="start motion test"){StartMovementRecording();return;}
        if(text=="save motion test"){if(launchAssessmentRunning){InterruptLaunch();gate.UserStop();return;}EndAssessment("recording saved",true);return;}
        if(text=="start pointing test"){assessmentRequest=launchCheck.Required?"launch":"start";assessmentRequestSequence++;return;}
        if(text=="skip target"){EndAssessment("skipped",false);return;}
        if(text=="save pointing test"){if(launchAssessmentRunning){InterruptLaunch();gate.UserStop();return;}EndAssessment("test ended",true);assessmentRequest="finish";assessmentRequestSequence++;return;}
        if(text is "mark rest" or "mark movement"){movementRecorder.Add(new("label",Arguments.Now,Label:text=="mark rest"?"rest":"movement"));return;}
        if(VoiceControl.IsStop(text)){gate.UserStop();return;}
        if(VoiceControl.IsResume(text)){
            if(state is "TutorialChoice" or "Tutorial")EndTutorial();
            else Handle(new Command("resume",UserStopEpoch:epoch));return;
        }
        if(tutorialVoice&&state is "TutorialChoice" or "Tutorial"){var action=TutorialFlow.Resolve(text,state=="TutorialChoice");if(action is not null)Handle(new Command(action,UserStopEpoch:epoch));return;}
        if(cameraVoice){int selected=CameraVoice.Resolve(text,cameras.Length);if(state=="ChooseCamera"&&selected>=0)OpenCamera(cameras[selected].Id);return;}
        if(text=="computer pause pointer")gate.Stop();
        else if(text=="computer resume pointer")Handle(new Command("resume",UserStopEpoch:epoch));
        else if(text=="computer confirm")Handle(new Command("confirm",UserStopEpoch:epoch));
        else if(text=="computer stop recording")Handle(new Command("saveRecording"));
        else if(text=="computer start recording")Handle(new Command("record",Name:recordingDraft,Enabled:recordingText,UserStopEpoch:epoch));
        else if(text.StartsWith("computer run ")){pendingRoutine=text[13..];pendingUntil=Arguments.Now+10;title="Confirm routine: "+pendingRoutine;help="Say computer confirm within ten seconds, or choose Confirm.";}
    }
    void ChooseStartupCamera(){if(cameras.Length==1)OpenCamera(cameras[0].Id);else if(cameras.Length==0)Fail("No camera was found. Connect a camera and choose Refresh cameras.");else{state="ChooseCamera";cue="position";title="Choose your camera";help="Select the camera that has the clearest view of your face.";}}
    void ShowTutorialPage(){state="Tutorial";cue="tutorial:"+tutorial.Page;var page=TutorialFlow.Pages[tutorial.Page];title=page.Title;help=page.Instruction;progress=(tutorial.Page+1.0)/TutorialFlow.Pages.Length;}
    void EndTutorial(){tutorial.Complete();tutorialVoice=false;voiceKey=null;ChooseStartupCamera();}
    void Fail(string error){startupPointer.Cancel();explicitCentre=centreOnReturn=false;InterruptLaunch();CancelRestLesson();startup.Cancel();tutorial.Complete();tutorialVoice=cameraVoice=false;voiceKey=null;lastFailure=error;gate.Stop();active=returning=false;calibration=null;state="Stopped";title="Qadr needs your attention";help=error;}
    void RecordAssessment(AssessmentResult? result){if(result is null)return;assessmentResult=result;assessment=null;if(launchAssessmentRunning){RecordTuningResult(result);launchCheck.Record(result);}movementRecorder.Add(new("assessment-result",Arguments.Now,AssessmentResult:result));}
    void EndAssessment(string reason,bool save){if(assessment is not null)RecordAssessment(assessment.Cancel(Arguments.Now,reason));assessment=null;if(reason!="skipped")assessmentSession=false;if(save)movementRecorder.Stop();}
    void StartMovementRecording(){
        if(movementRecorder.Start(Arguments.Now,args.Contains("--no-input")||args.Contains("--test-source"))){
            recordedState=null;recordedProfile=null;recordedMovementSettings=null;
            movementRecorder.Add(new("configuration",Arguments.Now,MovementSettings:motion.Settings));
        }
    }
    void Publish() {
        preview=Volatile.Read(ref latestPreview)??preview;
        var status=new EngineStatus(state,title,help,progress,guideX,guideY,face,distance,fps,latency,
        gate.Hotkeys==3?"Esc or Ctrl+Alt+Esc":gate.Hotkeys==1?"Esc":gate.Hotkeys==2?"Ctrl+Alt+Esc (Esc is unavailable)":"No stop shortcut is available â€” control is blocked",
        cameras,preview,routines.Names,voice,pendingRoutine,dwell,CurrentSettings:motion.Settings,Position:position,Cue:cue,RightClickProgress:motion.RightProgress,ClickFeedback:clickFeedback,LeftEye:Math.Max(0,observation?.LeftEye??0),RightEye:Math.Max(0,observation?.RightEye??0),MouseMode:mouse.Mode,Dragging:mouse.Dragging,MouseHelp:headScroll.Active?"Move head up/down to scroll; repeat the mouth gesture to exit":mouse.Help,DetectedFace:observation is {} detected?new("face","Detected face",true,detected.FaceX,detected.FaceY,detected.FaceWidth,detected.FaceHeight):null,DockToggleSequence:dockToggleSequence,HeadScrolling:headScroll.Active,RoutineVoiceEnabled:routineVoice,InferenceMs:inferenceMs,CaptureFps:capture.ActualFps,CameraNominalFps:capture.NominalFps,MovementRecording:movementRecorder.Status,Assessment:assessment?.Progress,AssessmentResult:assessmentResult,AssessmentRequestSequence:assessmentRequestSequence,AssessmentRequest:assessmentRequest,LaunchCheck:launchCheck.Status,MovementTuning:movementTuning is {} tuning?tuning with{UndoAvailable=cameraTuning is not null}:null,Adjustment:manualAdjustments.Status(motion.Settings),TrackingHealth:trackingHealth.Status(Arguments.Now));
        if(gate.SharedRpc!=0)shared.Publish(status);else statuses.Writer.TryWrite(status);
    }
    public void Dispose(){if(disposed)return;disposed=true;cancellation.Cancel();gate.Stop();movementRecorder.Dispose();routines.Dispose();worker.Dispose();capture.DisposeAsync().AsTask().Wait(2000);gate.Closing=1;shared.Dispose();gate.Dispose();shell?.Dispose();cancellation.Dispose();}
    sealed record Devices(CameraDevice[] Cameras);
    sealed record CameraStarted(bool Success,string? Error=null,int UserStopEpoch=0);
    sealed record VoiceIntent(string Text,int UserStopEpoch);
    sealed record Failure(string Error);
    sealed record Notice(string Text);
    sealed record WorkerRestarted;
    sealed record RoutineDone(string Text);
}


