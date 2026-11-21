using Qadr.Core;

static class ExperienceVerification
{
    public static void Run(Action<bool,string> check){
        var health=new TrackingHealth();
        check(health.Status(0) is {Code:"waiting",Samples:0},"health starts without inventing tracking confidence");
        health.Feed(double.NaN,true,true,30,20);health.Feed(0,true,true,double.NaN,20);
        check(health.Status(0).Samples==0,"invalid timing samples cannot poison health measurements");
        for(int i=0;i<5;i++)health.Feed(i/30d,true,true,30,20);
        check(health.Status(.15).Code=="measuring","health waits for evidence before describing stability");
        for(int i=5;i<60;i++)health.Feed(i/30d,true,true,30,20);
        check(health.Status(2) is {Code:"stable",AcceptedPercent:100,CaptureAgeP95Ms:30,InferenceP95Ms:20},"recent accepted tracking has measured timing and stable feedback");
        check(health.Status(2.3).Code=="stale","missing fresh observations show paused tracking rather than stale success");
        check(health.Status(6) is {Code:"waiting",Samples:0},"old tracking evidence expires from the bounded window");
        check(health.Status(6.1) is {Code:"waiting",Samples:0},"expired health cache cannot restore old stability feedback");
        health.Reset();for(int i=0;i<30;i++)health.Feed(i/15d,true,true,30,20);
        check(health.Status(2).Code=="low-rate","low measured update rate is distinguished from face loss and processing age");
        health.Reset();for(int i=0;i<30;i++)health.Feed(i/30d,i>=10,i>=10,30,20);
        check(health.Status(1).Code=="framing","face loss gives framing guidance without inventing a lighting score");
        health.Reset();for(int i=0;i<30;i++)health.Feed(i/30d,true,i%2==0,30,20);
        check(health.Status(1) is {Code:"unstable",AcceptedPercent:50},"discontinuous tracking gives settling guidance");
        health.Reset();for(int i=0;i<30;i++)health.Feed(i/30d,true,true,170,40);
        check(health.Status(1).Code=="delayed","measured high capture age gives latency guidance");
        health.Reset();for(int i=0;i<1000;i++)health.Feed(i/1000d,true,true,20,10);
        check(health.Status(1).Samples==180,"health memory remains bounded even at excessive result rates");
        health.Feed(.1,true,true,20,10);
        check(health.Status(.1).Samples==1,"clock reversal discards incompatible health history");
        check(health.Status(double.NaN).Code=="waiting","invalid query time fails safely");
        var schedule=new PreviewSchedule();
        check(schedule.Due(0,30)&&!schedule.Due(.01,30)&&schedule.Due(.034,30),"preview throttling accepts newest display work without a frame backlog");
        check(!schedule.Due(double.NaN,60)&&schedule.Due(0,60),"invalid preview time is ignored and reversed time resets the schedule");
        foreach(int rate in new[]{15,30,60}){
            var limiter=new PreviewSchedule();int decoded=0;
            for(int i=0;i<10000;i++)if(limiter.Due(i/1000d,rate))decoded++;
            check(decoded<=rate*10+1&&decoded>rate*9,"preview work is bounded at selected "+rate+" FPS independently of tracking");
        }
        check(new Settings().Validate() is {CentreOnRecovery:true,PreviewFps:30},"recovery keeps the existing centre default and preview defaults to 30 FPS");
        check((new Settings{PreviewFps=999}).Validate().PreviewFps==30&&(new Settings{PreviewFps=15}).Validate().PreviewFps==15,"preview rate validates supported choices");
        var saved=Protocol.Decode<Settings>(Protocol.Encode(new Settings{CentreOnRecovery=false,PreviewFps=15,Sensitivity=.45}));
        check(!saved.CentreOnRecovery&&saved.PreviewFps==15&&saved.Sensitivity==.45,"new comfort preferences survive serialization without changing speed");
        check(Protocol.Decode<Settings>("{\"sensitivity\":0.45}") is {CentreOnRecovery:true,PreviewFps:30,Sensitivity:.45},"old saved settings receive compatible recovery and preview defaults");
        check(VoiceControl.Phrases(true,false,[]).Contains("centre pointer")&&VoiceControl.Phrases(true,false,[]).Contains("center pointer"),"hands-free vocabulary includes explicit recentre variants");
        check(!VoiceControl.Phrases(false,false,[]).Contains("centre pointer"),"recentre speech is not enabled with hands-free listening off");
        check(!CommandIntent.IsCurrent(new("recentre",UserStopEpoch:2),3),"stale recentre commands cannot undo Escape");
        var trial=new PointingAssessment(new("A","move",0,0,80,80),0,200,200);
        trial.Feed(.1,200,200,false);
        check(trial.HasTimedOut(8,8)&&!trial.HasTimedOut(7.99,8),"assessment wall deadline advances while face/input observations are unusable");
        check(!trial.HasTimedOut(double.NaN,8)&&!trial.HasTimedOut(8,double.NaN)&&!trial.HasTimedOut(8,0),"assessment rejects invalid deadlines");
        var outcome=trial.Cancel(8,"time limit");
        check(!outcome.Completed&&outcome.TrackingLosses==1&&outcome.DurationMs==8000&&!trial.HasTimedOut(9,8),"tracking timeout saves a failed outcome exactly once rather than inventing a hit");
        var status=new EngineStatus("Tracking","","",0,0,0,true,"",30,30,"Esc",[],null,[],"",TrackingHealth:health.Status(.1));
        check(Protocol.Decode<EngineStatus>(Protocol.Encode(status)).TrackingHealth==status.TrackingHealth,"tracking feedback survives cross-process status transport");
    }
}
