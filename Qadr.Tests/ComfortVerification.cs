using Qadr.Core;

static class ComfortVerification
{
    public static void Run(Action<bool,string> check,string algorithm){
        var rest=new RelativeRest();for(int i=1;i<=4;i++)rest.Feed(i*.0025,0,.1,i/30d,1/30d);
        bool settled=false;for(int i=5;i<=10;i++)settled|=rest.Feed(.01,0,.1,i/30d,1/30d);
        check(settled&&!rest.CanFinish,"a tiny coherent stroke ages out into rest without a catch-up burst");
        var profile=new Profile{CoordinateVersion=3,Modes=["shift","turn"],Scale=.12,Ranges=[[.07535597269,.12975727960],[.30486902723,.05667157288]],Noise=[.00908709296,.01242775368]};
        foreach(int fps in new[]{16,26,30,60})foreach(int axis in new[]{0,1})foreach(int sign in new[]{-1,1}){
            var c=new MotionController{Settings=new(){Mapping="relative",MovementAlgorithm=algorithm,Sensitivity=.45,RelativeTravelBoost=3.5,RelativeSpeedLimit=900,BalanceHorizontal=true,BalanceVertical=true,ClickMethod="none",RightClickMethod="none"}};c.SetProfile(profile);
            double x=959.5,y=539.5,peak=0;bool hit=false;
            double range=Math.Sqrt(profile.Ranges[axis][0]*profile.Ranges[axis][1]);
            for(int i=0;i<=fps*5;i++){
                double t=i/(double)fps,s=sign*range*1.5*Math.Clamp((t-.5)/2,0,1);
                var o=new Observation(0,axis==1?-s:0,0,0,0,t,X:axis==0?.5+s*.12:.5,Scale:.12);
                var output=c.Feed(o,x,y);peak=Math.Max(peak,Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy)*fps);
                x=Math.Clamp(x+output.Dx,0,1919);y=Math.Clamp(y+output.Dy,0,1079);hit|=axis==0?(sign<0?x==0:x==1919):(sign<0?y==0:y==1079);
            }
            check(hit&&peak<=900.001,$"slower comfort settings reach axis {axis} edge {sign} at {fps} FPS within 900px/s (end {x:F1},{y:F1})");
        }
        double dt=1/30d;
        double oldSlow=1920*.7*.5*.1*dt,newSlow=1920*.7*.45*.1*dt;
        var slow=RelativeReach.Apply(newSlow,0,dt,1920,1080,.45,3.5);
        check(Math.Abs(slow.X/oldSlow-.9)<1e-10,"comfort settings make tiny corrections ten percent slower without reach acceleration");
        check(RelativeReach.Apply(0,0,dt,1920,1080,.45,3.5)==(0d,0d),"increased screen reach never generates travel while resting");
        var original=new Settings{ClickMethod="smile",SmileLearned=true,SmileThreshold=.4,HandsFreeVoice=true,InvertY=true};
        var changed=(original with{Sensitivity=.45,RelativeTravelBoost=3.5,RelativeSpeedLimit=900}).Validate();
        check(changed.SmileThreshold==original.SmileThreshold&&changed.InvertY&&changed.ClickMethod=="smile"&&changed.HandsFreeVoice,"speed adjustment preserves gestures, orientation and voice preferences");
    }
}

