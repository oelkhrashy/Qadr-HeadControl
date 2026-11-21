using Qadr.Core;

namespace Qadr.Engine;

internal sealed partial class Controller
{
    bool TuningEvidenceTest=>DiagnosticControl&&args.Contains("--tuning-test-evidence");
    MovementParameters configuredMovement=null!;
    void ApplyManualSettings(Settings requested){
        requested=requested.Validate();
        bool inputChanged=(requested with{PreviewFps=motion.Settings.PreviewFps,CentreOnRecovery=motion.Settings.CentreOnRecovery})!=motion.Settings;
        manualAdjustments.Observe(requested);
        if(MovementParameters.From(requested)!=MovementParameters.From(motion.Settings))configuredMovement=MovementParameters.From(requested);
        motion.Settings=requested;
        if(motion.Profile is {} profile){
            var desktop=NativeInput.Desktop;tuningContext=TuningContext.Create(profile,motion.Settings,desktop.Width,desktop.Height);
            if(cameraTuning?.Matches(tuningContext,configuredMovement.Apply(motion.Settings),TuningEvidenceTest)!=true){motion.Settings=configuredMovement.Apply(motion.Settings);cameraTuning=null;movementTuning=null;}
        }
        worker.ConfigureFps(motion.Settings.TrackingFps);var saved=configuredMovement.Apply(motion.Settings);
        if(!storageWrites.Writer.TryWrite(()=>storage.Save("settings.json",saved)))Fail("Qadr could not queue your settings for saving.");
        // Display/recovery preferences must not interrupt a held gesture or drag.
        if(inputChanged){ClearMouse();motion.Reset();trajectory.Clear();}
    }
    void DiagnosticAssessmentResult(string? json){
        if(!TuningEvidenceTest||!launchAssessmentRunning||assessment is null||json is null||gate.Stopped)return;
        var result=Protocol.Decode<AssessmentResult>(json);
        if(result.Id!=launchCheck.NextId||result.Id!=assessment.Progress.Target.Id||result.ExpectedAction!="move")return;
        RecordAssessment(result);
    }
    TunedMovementProfile? cameraTuning;
    TuningContext? tuningContext;
    MovementTuningStatus? movementTuning;
    readonly List<AssessmentResult> tuningBaseline=[],tuningValidation=[];
    readonly List<AssessmentTarget> tuningTargets=[];
    bool tuningGeometryValid,tuningQualityValid,tuningSavePending;
    sealed record TuningPersisted(TunedMovementProfile Profile,MovementTuningStatus Decision,int Epoch);
    int tuningEpoch;

    void LoadTunedMovement(Profile profile){
        motion.Settings=configuredMovement.Apply(motion.Settings);
        var desktop=NativeInput.Desktop;
        tuningContext=TuningContext.Create(profile,motion.Settings,desktop.Width,desktop.Height);
        cameraTuning=activeCamera is null?null:storage.Load<TunedMovementProfile>("movement-tuning/"+Storage.DeviceKey(activeCamera)+".json");
        if(cameraTuning?.Matches(tuningContext,motion.Settings,TuningEvidenceTest)==true){
            motion.Settings=cameraTuning.Current.Apply(motion.Settings);
            movementTuning=new("loaded","Your verified movement settings were restored for this camera and screen.",cameraTuning.Previous,Applied:cameraTuning.Current);
        }else{cameraTuning=null;movementTuning=null;}
    }
    void StartTuningEvidence(){
        tuningBaseline.Clear();tuningValidation.Clear();tuningTargets.Clear();tuningGeometryValid=tuningQualityValid=true;tuningEpoch=gate.UserStops;
        movementTuning=new("measuring","Measuring your movement. Any adjustment will be checked automatically.",MovementParameters.From(launchSettings!));
    }
    bool AcceptTuningTarget(AssessmentTarget target){
        if(launchCheck.Status.Round==1){tuningTargets.Add(target);return true;}
        int index=launchCheck.Status.Attempted;
        if(index>=tuningTargets.Count)return false;
        var original=tuningTargets[index];
        bool same=original.X==target.X&&original.Y==target.Y&&original.Width==target.Width&&original.Height==target.Height;
        if(!same){tuningGeometryValid=false;InterruptLaunch();gate.UserStop();}
        return same;
    }
    void RecordTuningResult(AssessmentResult result){
        (launchCheck.Status.Round==1?tuningBaseline:tuningValidation).Add(result);
    }
    void LogTuning(){if(movementTuning is {} decision)movementRecorder.Add(new("movement-tuning",Arguments.Now,Tuning:decision));}
    void FinishTunedLaunchAssessment(){
        if(tuningSavePending||!launchAssessmentRunning||launchCheck.Status.Attempted!=5||launchSettings is null||gate.Stopped||gate.UserStops!=tuningEpoch)return;
        if(tuningContext is {} expected&&motion.Profile is {} currentProfile){
            var desktop=NativeInput.Desktop;if(TuningContext.Create(currentProfile,launchSettings,desktop.Width,desktop.Height)!=expected)tuningQualityValid=false;
        }
        if(launchCheck.Status.Round==1){
            movementTuning=launchSettings.Mapping=="position"?new("kept","Position control checked. Comfortable angular ranges are preserved; relative-travel tuning does not apply to this mode.",MovementParameters.From(launchSettings),Applied:MovementParameters.From(launchSettings)):
                !QuickTargetLayout.Comparable(tuningTargets)?new("kept","Test targets were too close or misplaced; previous settings kept.",MovementParameters.From(launchSettings),Applied:MovementParameters.From(launchSettings)):
                !tuningQualityValid?new("kept","Tracking was interrupted; previous settings kept.",MovementParameters.From(launchSettings),Applied:MovementParameters.From(launchSettings)):
                MovementTuner.Propose(launchSettings,cameraTuning?.Anchor??configuredMovement,tuningBaseline,args.Contains("--no-input")&&!TuningEvidenceTest);
            if(movementTuning.Proposed is {} proposed&&movementTuning.Phase=="validating"){
                if(!launchCheck.BeginValidation())return;
                motion.Settings=proposed.Apply(motion.Settings);motion.Reset();trajectory.Clear();assessmentResult=null;LogTuning();
                title="Checking your automatic adjustment";help=movementTuning.Message;
                var canvas=launchCanvas!;
                if(!args.Contains("--no-input")&&!NativeInput.Move(gate,canvas.X+canvas.Width/2,canvas.Y+canvas.Height/2))Fail("Windows could not position the pointer for the repeat check.");
                return;
            }
        }else if(movementTuning is {} proposal){
            movementTuning=MovementTuner.Validate(proposal,tuningBaseline,tuningValidation,tuningGeometryValid&&tuningQualityValid);
            if(movementTuning.Phase=="applied"&&movementTuning.Applied is {} applied&&tuningContext is {} context&&activeCamera is {} camera){
                var saved=new TunedMovementProfile(1,context,cameraTuning?.Anchor??configuredMovement,MovementParameters.From(launchSettings),applied,movementTuning.Adjustment??"movement",DateTime.UtcNow,Synthetic:TuningEvidenceTest);
                // Only a completed, verified decision enters the persistence queue.
                // The background action never mutates or arms the controller.
                var decision=movementTuning;int epoch=tuningEpoch;
                if(storageWrites.Writer.TryWrite(()=>{storage.Save("movement-tuning/"+Storage.DeviceKey(camera)+".json",saved);Queue(new TuningPersisted(saved,decision,epoch));})){
                    tuningSavePending=true;movementTuning=decision with{Phase="saving",Message="The repeat check improved your movement. Saving verified settings."};title="Saving your movement settings";help=movementTuning.Message;return;
                }
                else movementTuning=proposal with{Phase="reverted",Message="Settings could not be queued for saving. Previous settings restored.",Applied=proposal.Before};
            }
        }
        CompleteTunedLaunch();
    }
    void TuningSaved(TuningPersisted saved){
        if(!tuningSavePending||!launchAssessmentRunning||launchSettings is null||gate.Stopped||gate.UserStops!=saved.Epoch)return;
        tuningSavePending=false;cameraTuning=saved.Profile;movementTuning=saved.Decision;launchSettings=saved.Profile.Current.Apply(launchSettings);CompleteTunedLaunch();
    }
    void CompleteTunedLaunch(){
        if(!launchCheck.Finish())return;
        LogTuning();EndAssessment("launch assessment completed",true);RestoreLaunchSettings();
        title=movementTuning?.Phase=="applied"?"Movement adjusted for you":"Movement check saved";
        help=movementTuning?.Message??"Previous settings kept.";
    }
    void UndoTunedMovement(){
        if(launchCheck.Required||launchAssessmentRunning||cameraTuning is not {} previous||activeCamera is not {} camera)return;
        var restored=previous with{Current=previous.Previous,Previous=previous.Current,Reason="User restored previous verified movement",SavedUtc=DateTime.UtcNow};
        if(!storageWrites.Writer.TryWrite(()=>storage.Save("movement-tuning/"+Storage.DeviceKey(camera)+".json",restored)))return;
        cameraTuning=restored;motion.Settings=restored.Current.Apply(motion.Settings);ClearMouse();motion.Reset();trajectory.Clear();
        movementTuning=new("undone","Your previous movement settings were restored.",previous.Current,Applied:restored.Current);title="Previous movement restored";help=movementTuning.Message;LogTuning();
    }
}
