using Qadr.Core;

static class FeedbackVerification
{
    public static void Run(Action<bool,string> check,string algorithm){
        foreach(int fps in new[]{16,30,60}){
            var intent=new RelativeTravelIntent();double dt=1d/fps,dx=1920*.7*.5*dt,dy=1080*.7*.5*dt;
            check(intent.Feed(dx,0,dt,1920,1080,.5)==0,$"extra reach starts conservatively at {fps} FPS");
            double confidence=0;for(int i=0;i<fps;i++)confidence=intent.Feed(dx,0,dt,1920,1080,.5);
            check(confidence==1,$"sustained travel regains full reach at {fps} FPS");
            check(intent.Feed(-dx,0,dt,1920,1080,.5)==0,$"a correction reversal immediately removes extra reach at {fps} FPS");
            check(intent.Feed(0,dy,dt,1920,1080,.5)==0,$"a new perpendicular movement starts without extra boost at {fps} FPS");
            intent.Reset();double alternating=0;for(int i=0;i<fps*2;i++)alternating+=intent.Feed(i%2==0?dx:-dx,0,dt,1920,1080,.5);
            check(alternating==0,$"alternating jitter cannot repeatedly enable travel boost at {fps} FPS");
            check(intent.Feed(0,0,dt,1920,1080,.5)==0&&intent.Feed(dx,0,dt,1920,1080,.5)==0,$"a stopped head clears boost history at {fps} FPS");
            check(intent.Feed(double.NaN,0,dt,1920,1080,.5)==0&&intent.Feed(dx,0,.5,1920,1080,.5)==0,$"nonfinite observations and gaps reset travel intent at {fps} FPS");
        }
        foreach(var size in new[]{(1280d,700d),(911d,360d),(731d,260d),(640d,224d),(600d,176d)}){
            var plan=QuickTargetLayout.Create(size.Item1,size.Item2);
            check(plan is not null&&QuickTargetLayout.Comparable(plan.Targets),$"five targets stay separated in a {size.Item1} by {size.Item2} test surface");
            check(plan!.Targets.All(t=>t.X>=8&&t.Y>=8&&t.X+t.Width<=size.Item1-8+.001&&t.Y+t.Height<=size.Item2-8+.001),"accessible test targets fit inside the available surface");
        }
        check(QuickTargetLayout.Create(678,88) is null&&QuickTargetLayout.Create(double.NaN,500) is null,"collapsed or invalid test surfaces cannot fabricate vertical targets");
        var recorded=new[]{new AssessmentTarget("A","move",761,633,154,154),new("B","move",310.32,633,154,154),new("C","move",1211.68,633,154,154),new("D","move",761,633,154,154),new("E","move",761,633,154,154)};
        check(!QuickTargetLayout.Comparable(recorded),"the customer's overlapping centre/top/bottom geometry is rejected as tuning evidence");
        var profile=new Profile{CoordinateVersion=3};var settings=new Settings{Mapping="relative",MovementAlgorithm=algorithm,RelativeTravelBoost=3,RelativeSpeedLimit=1000,ClickMethod="none",RightClickMethod="none"};
        var controller=new MotionController{Settings=settings};controller.SetProfile(profile);
        for(int i=0;i<60;i++)controller.Feed(new(.12*Math.Sin(i/20d),0,0,0,0,i/30d),100,100);
        controller.Reset();var first=controller.Feed(new(-.1,0,0,0,0,3),100,100);
        check(first.Dx==0&&first.Dy==0&&first.Click is null,"Escape reset discards travel-intent state without generating a restart jump");
    }
}
