using Qadr.Core;

static class MovementTuningVerification
{
    static AssessmentResult[] Trials(char start='A',int overshoots=0,double jitter=2,double duration=2000,double efficiency=.85)=>Enumerable.Range(0,5).Select(i=>new AssessmentResult(((char)(start+i)).ToString(),"move",true,"held inside target",duration,duration-650,300,efficiency,overshoots+1,overshoots,overshoots*20,8,jitter,0,0,0,40)).ToArray();
    public static void Run(Action<bool,string> check){
        var settings=new Settings{Sensitivity=.5,RelativeTravelBoost=3,Mapping="relative",MovementAlgorithm="adaptive",RightClickMethod="both",SmileLearned=true,SmileThreshold=.23,PuckerDrag=true,PuckerLearned=true,MouthScroll=true,ScrollGestureLearned=true};
        var anchor=MovementParameters.From(settings);
        var steady=Trials();
        check(MovementTuner.Propose(settings,anchor,steady).Phase=="kept","comfortable measured control does not trigger unnecessary tuning");
        var bad=Trials(overshoots:2,efficiency:.6);
        var proposal=MovementTuner.Propose(settings,anchor,bad);
        check(proposal is {Phase:"validating",Adjustment:"precision",Proposed.Sensitivity:.45},"overshooting proposes one bounded sensitivity reduction");
        check(proposal.Proposed!.TravelBoost==3&&proposal.Proposed.PrecisionNoise==settings.PrecisionNoiseMultiplier,"an experiment changes one movement control at a time");
        var candidate=proposal.Proposed.Apply(settings);
        check(candidate with{Sensitivity=settings.Sensitivity}==settings,"automatic movement changes preserve every other setting and gesture binding");
        var success=MovementTuner.Validate(proposal,bad,Trials('F',overshoots:1,efficiency:.75),true);
        check(success.Phase=="applied"&&success.Applied==proposal.Proposed,"matched targets with fewer overshoots accept the candidate");
        check(MovementTuner.Validate(proposal,bad,Trials('F',overshoots:2,efficiency:.6),true).Phase=="reverted","no measured improvement rolls back the candidate");
        check(MovementTuner.Validate(proposal,bad,Trials('F',overshoots:1,duration:4000),true).Phase=="reverted","precision improvements cannot mask much slower target completion");
        check(MovementTuner.Validate(proposal,bad,Trials('F',overshoots:1,jitter:20),true).Phase=="reverted","worse target stability rejects a candidate");
        check(MovementTuner.Validate(proposal,bad,Trials('F',overshoots:1),false).Phase=="reverted","changed target geometry cannot validate tuning");
        check(MovementTuner.Validate(proposal,bad,Trials('A',overshoots:1),true).Phase=="reverted","old baseline IDs cannot satisfy a validation round");
        var failed=Trials('F',overshoots:1);failed[2]=failed[2] with{Completed=false,Reason="time limit"};
        check(MovementTuner.Validate(proposal,bad,failed,true).Phase=="reverted","losing a previously reached target rejects tuning");
        failed=Trials('F',overshoots:1);failed[1]=failed[1] with{TrackingLosses=1};
        check(MovementTuner.Validate(proposal,bad,failed,true).Phase=="reverted","tracking gaps invalidate the comparison");
        var lost=Trials(overshoots:2);lost[0]=lost[0] with{TrackingLosses=1};
        check(MovementTuner.Propose(settings,anchor,lost).Phase=="kept","baseline tracking loss never suggests faster movement");
        var skipped=Trials().Select(r=>r with{Completed=false,Reason="skipped"}).ToArray();
        check(MovementTuner.Propose(settings,anchor,skipped).Phase=="kept","all skips keep previous settings");
        check(MovementTuner.Propose(settings,anchor,skipped.Select(r=>r with{Reason="time limit"}).ToArray()).Phase=="kept","timeouts alone are not evidence for higher gain");
        check(MovementTuner.Propose(settings,anchor,bad,true).Phase=="kept","synthetic diagnostic input cannot train a customer profile");
        check(MovementTuner.Propose(settings,anchor,bad.Take(4).ToArray()).Phase=="kept","partial runs cannot propose settings");
        var corrupt=Trials(overshoots:2);corrupt[1]=corrupt[1] with{InsideJitterRmsPx=double.NaN};corrupt[2]=corrupt[2] with{PathEfficiency=double.PositiveInfinity};
        check(MovementTuner.Propose(settings,anchor,corrupt).Phase=="kept","invalid metrics cannot contribute tuning evidence");
        var slow=Trials(duration:4000,efficiency:.9);proposal=MovementTuner.Propose(settings,anchor,slow);
        check(proposal is {Adjustment:"reach",Proposed.TravelBoost:>3 and <=3.31}&&proposal.Proposed.Sensitivity==.5,"slow coherent successful travel can propose a small reach boost");
        check(MovementTuner.Validate(proposal,slow,Trials('F',duration:3400,efficiency:.9),true).Phase=="applied","reach changes require faster matched target completion");
        check(MovementTuner.Propose(settings with{Mapping="rate"},anchor,slow).Phase=="kept","relative reach is not tuned in hold-to-move mode");
        var noisy=Trials(jitter:10);proposal=MovementTuner.Propose(settings,anchor,noisy);
        check(proposal.Adjustment=="steadiness"&&proposal.Proposed!.PrecisionNoise>anchor.PrecisionNoise,"target instability can propose a small Adaptive precision-band adjustment");
        check(MovementTuner.Validate(proposal,noisy,Trials('F',jitter:7),true).Phase=="applied","precision-band tuning requires improved measured stability");
        check(MovementTuner.Propose(settings with{MovementAlgorithm="classic"},anchor,noisy).Phase=="kept","Classic remains available without Adaptive-only precision tuning");
        var current=settings;
        for(int i=0;i<20;i++){var next=MovementTuner.Propose(current,anchor,bad);if(next.Proposed is {} value)current=value.Apply(current);}
        check(current.Sensitivity>=.35-1e-9&&MovementTuner.WithinEnvelope(MovementParameters.From(current),anchor),"repeated launches cannot drift beyond the original personal envelope");
        current=settings;
        for(int i=0;i<20;i++){var next=MovementTuner.Propose(current,anchor,slow);if(next.Proposed is {} value)current=value.Apply(current);}
        check(current.RelativeTravelBoost<=3.9+1e-9,"repeated reach experiments respect the original personal envelope");
        var profile=new Profile{CoordinateVersion=3};var context=TuningContext.Create(profile,settings,1920,1080);
        var saved=new TunedMovementProfile(1,context,anchor,anchor,success.Applied!,"precision",DateTime.UtcNow);
        check(saved.Matches(context,settings),"verified movement profile matches its camera calibration and configuration");
        check(!saved.Matches(context with{Width=2560},settings)&&!saved.Matches(context with{Algorithm="classic"},settings),"screen or algorithm changes ignore incompatible tuned settings");
        check(!saved.Matches(context,settings with{Sensitivity=.6}),"manual movement changes invalidate the old automatic overlay");
        check(TuningContext.Create(profile,settings with{Deadzone=.06},1920,1080)!=context,"motion configuration changes invalidate a saved comparison");
        check(TuningContext.Create(profile with{Neutral=[.3,.2],Center=[.6,.4]},settings,1920,1080)==context,"ordinary rest recentring does not discard verified settings");
        check(!(saved with{Current=anchor with{Sensitivity=double.NaN}}).Matches(context,settings),"malformed saved movement parameters are never applied");
        check(!(saved with{Anchor=null!}).Matches(context,settings)&&!(saved with{Current=null!}).Matches(context,settings),"null stored movement fields cannot load as verified parameters");
        check(VoiceControl.Phrases(true,false,[]).Contains("undo movement settings"),"hands-free grammar includes movement recovery without routine recording");
        var flow=new LaunchAssessmentFlow();flow.Ready();flow.Begin();foreach(var result in bad)flow.Record(result);
        check(flow.BeginValidation()&&flow.Status.Round==2&&flow.Required&&flow.NextId=="F","candidate verification remains mandatory and begins a distinct ordered round");
        check(!flow.Record(bad[0])&&!flow.Finish(),"stale baseline results and early finish cannot bypass validation");
        foreach(var result in Trials('F'))flow.Record(result);
        check(flow.Finish()&&!flow.Required,"a completed second round releases the launch requirement");
        flow=new();flow.Ready();flow.Begin();foreach(var result in bad)flow.Record(result);flow.BeginValidation();flow.Interrupt();flow.Ready();flow.Begin();
        check(flow.Status.Round==1&&flow.NextId=="A","interruption restarts baseline measurement instead of resuming provisional tuning");
        check(CommandIntent.RequiresEpoch(new("movementTuningUndo"))&&!CommandIntent.IsCurrent(new("movementTuningUndo",UserStopEpoch:0),1),"undo movement commands reject stale stop epochs");
    }
}
