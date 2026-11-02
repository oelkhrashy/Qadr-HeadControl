using Qadr.Core;

static class RecoveryAlignmentTests
{
    static Observation O(double t,double yaw=0,double pitch=0,double x=.5,double y=.45)=>new(yaw,pitch,0,0,0,t,X:x,Y:y,Scale:.2,LeftEye:0,RightEye:0,FaceX:x,FaceY:.5,FaceWidth:.4,FaceHeight:.6);
    static FaceGeometry G(double noseX=.5)=>new(.5,.45,.2,noseX,.5,.3,.2,.7,.8,0);
    public static void Run(Action<bool,string> check)
    {
        var profile=new Profile{CoordinateVersion=3,Center=[.5,.45]};
        var settings=new Settings();var r=new RecoveryAlignment();r.Begin(profile,O(0),G());
        check(!r.Check(null,settings).Ready,"recovery cannot resume with no face");
        check(!r.Check(O(1,x:.6),settings,G()).Ready,"a stable off-centre face cannot satisfy recovery alignment");
        check(r.Check(O(1,x:.6),settings,G()).X==.5,"recovery guide stays at the learned position instead of chasing the returning face");
        check(!r.Check(O(1,yaw:.2),settings,G()).Ready,"a centred face turned away from its resting direction stays paused");
        check(!r.Check(O(1),settings,G(.60)).Ready,"nose misalignment is rejected when numeric nose geometry is available");
        check(!r.Check(O(1) with{FaceX=.1},settings,G()).Ready,"partly clipped faces cannot pass recovery");
        check(r.Check(O(1),settings,G()).Ready&&r.Pending,"aligned recovery remains guarded until the Engine completes its resting window");
        r.Reset();check(!r.Check(O(2),settings,G()).Ready,"a cancelled recovery cannot resume itself");
        var shifted=profile with{Neutral=[.2,.1],Center=[.58,.5]};
        r.Begin(shifted,O(2,.2,.1,.58,.5));
        check(r.Check(O(3,.2,.1,.58,.5),settings).Ready,"an explicitly learned comfortable posture can establish a different recovery reference");
        var rest=new ResumeRest();bool ready=false;
        for(int i=0;i<15;i++)ready=rest.Feed(O(i/16.0),profile);
        check(ready&&rest.Samples.Count>=10,"stable aligned observations complete the resting window at realistic tracking FPS");
        rest.Reset();ready=false;var noisy=profile with{Noise=[.015,.016]};
        for(int i=0;i<40;i++)ready|=rest.Feed(O(i/16.0,pitch:i%2==0?.025:-.025),noisy);
        check(!ready,"a noisy drifting pitch sequence cannot complete the previous overly wide recovery window");
        rest.Reset();for(int i=0;i<15;i++)rest.Feed(O(i/16.0) with{FaceX=i==14?.53:.5},profile);
        check(rest.Centre().FaceX==.5,"the face guide and neutral use the same robust median rest rather than the last bounding box");
        r.Begin(profile,O(0),G());bool premature=false;FacePosition? result=null;
        for(int i=0;i<64;i++){result=r.Check(O(i/16.0,yaw:.25),settings,G());premature|=result.Ready;}
        check(!premature&&result!.Hint.Contains("1s"),"turned recovery shows a countdown and never adopts before four capture-time seconds");
        result=r.Check(O(4,yaw:.25),settings,G());
        check(result.Ready&&result.Code=="adopt","a neutral stable four-second posture provides a hands-free recovery fallback");
        r.Begin(profile,O(0),G());
        for(int i=0;i<50;i++)r.Check(O(i/16.0,yaw:.25),settings,G());
        result=r.Check(O(3.2,yaw:.32),settings,G());
        check(!result.Ready&&result.Hint.Contains("4s"),"moving the head cancels the posture adoption countdown");
        for(int i=0;i<40;i++)r.Check(O(3.3+i/16.0,yaw:.32),settings,G());
        r.Check(null,settings);
        check(!r.Check(O(6,yaw:.32),settings,G()).Ready,"lost face resets the posture fallback rather than completing an old hold");
        r.Begin(profile,O(0),G());for(int i=0;i<50;i++)r.Check(O(i/16.0,yaw:.25),settings,G());
        check(!r.Check(O(4,yaw:.25),settings,G()).Ready,"a capture gap cannot complete the four-second fallback");
        r.Begin(profile,O(0),G());bool accepted=false;
        for(int i=0;i<100;i++)accepted|=r.Check(O(i/16.0,yaw:.25) with{FaceX=.1},settings,G()).Ready;
        check(!accepted,"four-second fallback never adopts a clipped face");
        r.Begin(profile,O(0),G());accepted=false;
        for(int i=0;i<100;i++)accepted|=r.Check(O(i/16.0,yaw:.25),settings,G(),expressionNeutral:false).Ready;
        check(!accepted,"held gestures cannot advance the hands-free posture timer");
        var gestureSettings=new Settings{ClickMethod="smile",RightClickMethod="either",SmileLearned=true};
        check(!RecoveryExpression.Neutral(O(0) with{Smile=.9},gestureSettings),"a held smile cannot be learnt as recovery rest");
        check(!RecoveryExpression.Neutral(O(0) with{LeftEye=.9},gestureSettings),"a held wink cannot be learnt as recovery rest");
        check(RecoveryExpression.Neutral(O(0),gestureSettings),"relaxed gestures allow recovery rest collection");
        check(RecoveryExpression.Neutral(O(0) with{LeftEye=.42},gestureSettings),"a natural eye-rest score below its configured activation threshold does not block startup");
        check(RecoveryExpression.Neutral(O(0) with{Smile=.9,LeftEye=.9},gestureSettings with{ClickMethod="none",RightClickMethod="none"}),"disabled gesture channels do not block movement-only measurement");
        r.Begin(profile,O(0),G());
        check(r.Check(O(0,x:.56),settings,G(.56)).Ready,"a modest image shift inside the reviewed 0.08 tolerance is accepted");
        r.Begin(profile,O(0),G());for(int i=0;i<64;i++)r.Check(O(i/16.0,yaw:.25),settings,G());
        r.BreakHold();
        check(!r.Check(O(4,yaw:.25),settings,G()).Ready,"a rejected-quality observation invalidates the fallback countdown without moving the saved reference");
        r.Begin(profile,O(0),G());for(int i=0;i<64;i++)r.Check(O(i/16.0,yaw:.25),settings,G());
        check(!r.Check(O(4,yaw:.25),settings,G(.53)).Ready,"nose motion cancels posture adoption even when the estimated head angle stays constant");
        // A mathematical rotation check validates extraction only, not real-camera accuracy.
        double maxPitch=0;
        foreach(double yaw in new[]{-.5,-.25,0,.25,.5})
        {
            double[] matrix=[Math.Cos(yaw),0,Math.Sin(yaw),0,0,1,0,0,-Math.Sin(yaw),0,Math.Cos(yaw),0,0,0,0,1];
            var extracted=Features.Extract(new VisionResult(1,"result",Captured:1,Matrix:matrix,Geometry:G()));
            maxPitch=Math.Max(maxPitch,Math.Abs(extracted!.Pitch));
        }
        check(maxPitch<1e-12,"synthetic pure yaw rotations add no pitch in extraction; real-image pose errors remain untested");
    }
}
