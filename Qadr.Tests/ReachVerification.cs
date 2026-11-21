using Qadr.Core;

static class ReachVerification
{
    static Settings S(string algorithm,double boost)=>new(){Mapping="relative",MovementAlgorithm=algorithm,
        Sensitivity=.5,RelativeSpeedLimit=1000,RelativeTravelBoost=boost,BalanceHorizontal=true,BalanceVertical=true,
        ClickMethod="none",RightClickMethod="none"};
    static Profile P()=>new(){CoordinateVersion=3,Modes=["shift","turn"],Scale=.12,
        Ranges=[[.07535597269064569,.12975727960190478],[.30486902723386183,.05667157288454165]],
        Noise=[.009087092957464082,.012427753682838955]};
    public static void Run(Action<bool,string> check,string algorithm)
    {
        check(new Settings().RelativeTravelBoost==1&&Protocol.Decode<Settings>("{}").RelativeTravelBoost==1,
            "reach boost is opt-in for old and new profiles");
        check(new Settings{RelativeTravelBoost=double.NaN}.Validate().RelativeTravelBoost==1&&new Settings{RelativeTravelBoost=100}.Validate().RelativeTravelBoost==4,
            "invalid reach boost falls back safely and extreme gain is bounded");
        foreach(int fps in new[]{16,30,60}){
            double dt=1d/fps;
            double slow=1920*.7*.5*.1*dt;
            check(RelativeReach.Apply(slow,0,dt,1920,1080,.5,3)==(slow,0d),$"slow precision corrections remain byte-for-byte unchanged by reach boost at {fps} FPS");
            double move=1920*.7*.5*dt;
            var boosted=RelativeReach.Apply(move,0,dt,1920,1080,.5,3);
            check(Math.Abs(boosted.X-move*3)<1e-8,$"deliberate travel uses the same normalized reach gain at {fps} FPS");
            var extended=new MotionController{Settings=S(algorithm,3)};extended.SetProfile(P());double beyond=0;
            for(int i=0;i<fps*7;i++){
                double t=(double)i/fps;var output=extended.Feed(new(0,0,0,0,0,t,X:.5+.024*t,Scale:.12),960,540);
                if(t>=6)beyond+=output.Dx;
            }
            check(beyond>200,$"boosted relative travel continues beyond the former eight-range translation guard at {fps} FPS");
        }
        var diagonal=RelativeReach.Apply(20,-10,.02,1920,1080,.5,3);
        check(Math.Abs(diagonal.X/diagonal.Y+2)<1e-12&&diagonal.X>20,"reach gain preserves diagonal direction");
        check(RelativeReach.Apply(20,10,.02,1920,1080,.5,3,0)==(20d,10d),"low adaptive motion confidence cannot amplify displacement");
        check(RelativeReach.Apply(0,0,.02,1920,1080,.5,4)==(0d,0d),"reach gain cannot generate movement at rest");
        check(RelativeReach.Apply(double.NaN,0,.02,1920,1080,.5,3)==(0d,0d)&&RelativeReach.Apply(20,0,.5,1920,1080,.5,3)==(0d,0d),"nonfinite or interrupted reach input fails closed");
        foreach(int fps in new[]{16,30,60})foreach(int axis in new[]{0,1})foreach(int sign in new[]{-1,1}){
            var c=new MotionController{Settings=S(algorithm,3)};var p=P();c.SetProfile(p);
            double x=959.5,y=539.5,peak=0;bool hit=false;
            double range=Math.Sqrt(p.Ranges[axis][0]*p.Ranges[axis][1]);
            // 1.5 learned ranges over two seconds: ~.0188 image-width
            // translation or .197 radians vertically for this real profile.
            // First hold seeds filters, then one sweep, then hold to settle.
            for(int i=0;i<=fps*5;i++){
                double t=(double)i/fps,s=sign*range*1.5*Math.Clamp((t-.5)/2,0,1);
                var o=new Observation(0,axis==1?-s:0,0,0,0,t,X:axis==0?.5+s*.12:.5,Scale:.12);
                var output=c.Feed(o,x,y);peak=Math.Max(peak,Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy)*fps);
                x=Math.Clamp(x+output.Dx,0,1919);y=Math.Clamp(y+output.Dy,0,1079);
                hit|=axis==0?(sign<0?x==0:x==1919):(sign<0?y==0:y==1079);
            }
            check(hit,$"one deliberate relative sweep reaches axis {axis}, edge {sign}, at {fps} FPS using the measured asymmetric profile");
            check(peak<=1000.0001,$"reach stays within the configured travel speed on axis {axis}, edge {sign}, at {fps} FPS");
            double late=0;
            for(int i=1;i<=fps;i++){
                double s=sign*range*1.5;
                var output=c.Feed(new(0,axis==1?-s:0,0,0,0,5+(double)i/fps,X:axis==0?.5+s*.12:.5,Scale:.12),x,y);
                late+=Math.Abs(output.Dx)+Math.Abs(output.Dy);
            }
            check(late<1,$"no sustained edge motion or queued travel after head stops on axis {axis}, edge {sign}, at {fps} FPS");
        }
        var stopped=new MotionController{Settings=S(algorithm,3)};stopped.SetProfile(P());
        for(int i=0;i<30;i++)stopped.Feed(new(0,0,0,0,0,i/30d,X:.5+i*.001,Scale:.12),960,540);
        stopped.Reset();var recovered=stopped.Feed(new(0,.1,0,0,0,2,X:.55,Scale:.12),960,540);
        check(recovered.Dx==0&&recovered.Dy==0&&recovered.Click is null,"stop clears boosted relative travel without a restart jump");
    }
}
