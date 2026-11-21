using Qadr.Core;

static class PrecisionStopVerification
{
    static MotionController Controller(string algorithm,double noise=.003)=>Create(algorithm,new Profile{CoordinateVersion=3,Noise=[noise,noise]});
    static MotionController Create(string algorithm,Profile p){var c=new MotionController{Settings=new(){MovementAlgorithm=algorithm,Mapping="relative",ClickMethod="none",RightClickMethod="none",Sensitivity=.5,RelativeTravelBoost=3,RelativeSpeedLimit=1000}};c.SetProfile(p);return c;}
    static Observation O(double time,double yaw=0,double pitch=0,double smile=0)=>new(yaw,pitch,smile,0,0,time);
    public static void Run(Action<bool,string> check,string algorithm){
        var restGate=new RelativeRest();bool slowStrokePreserved=true;
        for(int i=1;i<=60;i++)slowStrokePreserved&=!restGate.Feed(i*.0025,i*.0025,.1,i/30d,1/30d);
        check(slowStrokePreserved,"coherent sub-noise movement is never classified as a head stop");
        foreach(int fps in new[]{16,26,30,60}){
            foreach(int direction in new[]{-1,1}){
                var c=Controller(algorithm);double total=0,early=0;
                for(int i=0;i<=fps*2;i++){
                    double pitch=direction*.025*Math.Min(1,i/(double)fps);var o=c.Feed(O(i/(double)fps,0,pitch),960,540);total+=o.Dy;if(i<=fps/2)early+=o.Dy;
                }
                check(Math.Sign(total)==-direction&&Math.Abs(total)>25&&Math.Abs(early)>3,$"small vertical motion responds through centre with correct sign at {fps} FPS ({total:F1}px, early {early:F1}px)");
            }
            var motion=Controller(algorithm);double x=960,y=540,lastMovingY=0,tail=0;bool stopped=false,quiet=true;double stoppedAt=0;
            for(int i=0;i<=fps*4;i++){
                double t=i/(double)fps;
                // Sub-noise movement after a sustained ramp, not identical poses.
                double pitch=.08*Math.Min(t,1)+(t>1?.003*.06*Math.Sin(t*27):0);
                var o=motion.Feed(O(t,0,pitch),x,y);x+=o.Dx;y+=o.Dy;
                if(t<=1)lastMovingY=y;
                else {tail+=Math.Abs(o.Dy);if(o.StopY&&!stopped){stopped=true;stoppedAt=t;}}
                if(stopped)quiet&=o.Dy==0;
            }
            check(quiet,"confirmed local rest emits no vertical tail at "+fps+" FPS");
            check(stopped&&stoppedAt<=1.25,$"natural-noise head stop is recognized within 250ms at {fps} FPS ({stoppedAt:F3}s, tail {tail:F2}px)");
            check(tail<15,$"post-stop controller catch-up stays below 15px at {fps} FPS ({tail:F2}px)");
            double restart=0;for(int i=fps*4+1;i<=fps*5;i++)restart+=motion.Feed(O(i/(double)fps,0,.08+.025*(i-fps*4)/(double)fps),x,y).Dy;
            check(restart<-35,$"vertical fine movement restarts after rest without stuck latch at {fps} FPS");
        }
        var p=new Profile{CoordinateVersion=3};var smile=Create(algorithm,p);
        smile.Settings=smile.Settings with{ClickMethod="dwell",SmileLearned=true,SmileRestLevel=.1,SmileThreshold=.3,DwellSeconds=1.2};
        for(int i=0;i<10;i++)smile.Feed(O(i/30d),960,540);
        bool paused=true;
        for(int i=10;i<24;i++){var o=smile.Feed(O(i/30d,0,-.05,.5),960,540);paused&=o.Frozen&&o.Dy==0&&o.Click is null;}
        check(paused,"learned smile cannot pull cursor down or trigger dwell while pose is contaminated");
        bool jump=false;for(int i=24;i<32;i++){var o=smile.Feed(O(i/30d,0,-.05),960,540);jump|=Math.Abs(o.Dy)>.001;}
        check(!jump,"release after a smile seeds pose without downward rebound");
        double up=0;for(int i=32;i<62;i++)up+=smile.Feed(O(i/30d,0,-.05+.03*(i-31)/30d),960,540).Dy;
        check(up<-20,"lifting head after smile release moves upwards");
        var smileClick=Create(algorithm,p);smileClick.Settings=smile.Settings with{ClickMethod="smile"};
        for(int i=0;i<10;i++)smileClick.Feed(O(i/30d),960,540);
        var onset=smileClick.Feed(O(10/30d,0,-.02,.25),960,540);
        check(onset.Frozen&&onset.Dy==0&&onset.Click is null,"smile clicking freezes pose at learned onset before activation");
        int leftClicks=0;bool movesDuringSmile=false;
        for(int i=11;i<30;i++){var o=smileClick.Feed(O(i/30d,0,-.05,.5),960,540);if(o.Click=="left")leftClicks++;movesDuringSmile|=o.Dy!=0;}
        check(leftClicks==1&&!movesDuringSmile,"smile left click still fires once without downward movement");
        var trajectory=new CursorTrajectory();trajectory.Reset(960,540);trajectory.Add(20,20,960,540,0,0,1920,1080);
        var pos=trajectory.Step(.008);trajectory.Add(0,5,pos.X,pos.Y,0,0,1920,1080,true,false);var next=trajectory.Step(.016);
        check(next.X==pos.X&&next.Y>pos.Y,"only confirmed stopped axis cancels interpolation; other axis still moves");
        trajectory.Reset(960,540);trajectory.Add(20,0,960,540,0,0,1920,1080);pos=trajectory.Step(.008);trajectory.Add(0,0,pos.X,pos.Y,0,0,1920,1080);next=trajectory.Step(.016);
        check(next.X>pos.X,"an ordinary zero filtered delta does not cancel smooth interpolation");
        var slow=Controller(algorithm,.00015);double slowY=0;
        for(int i=0;i<=150;i++)slowY+=slow.Feed(O(i/30d,0,.04+.0008*Math.Clamp(i/30d-1,0,1)),960,540).Dy;
        check(slowY<-1.5,$"low-noise slow vertical corrections survive the rest latch ({slowY:F3}px)");
    }
}
