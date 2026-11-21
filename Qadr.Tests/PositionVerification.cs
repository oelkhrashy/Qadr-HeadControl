using Qadr.Core;

static class PositionVerification
{
    public static void Run(Action<bool,string> check,string algorithm){
        SmileGuardVerification.Run(check,algorithm);
        var mapper=new PositionMapper();mapper.Rebase(.2,-.15);
        var options=new PositionOptions();double t=0;
        PositionSample Feed(double yaw,double pitch,int frames=30){PositionSample? last=null;for(int i=0;i<frames;i++)last=mapper.Feed(yaw,pitch,t+=1/30d,options);return last!;}
        check(Feed(.2,-.15) is {X:0,Y:0},"position: comfortable off-centre reference maps to screen centre");
        check(Feed(.8,-.15).X==1,"position: large right turn reaches the edge without accumulating excess travel");
        check(Feed(.2,-.15) is {X:0,Y:0},"position: returning from saturated edge returns to screen centre");
        check(Feed(-.4,-.15).X==-1&&Feed(.2,-.15).X==0,"position: left edge and repeated neutral return retain the same reference");
        check(Feed(.2,-.03).Y<-.99,"position: lifting head moves upward with the normal pitch convention");
        check(Feed(.2,-.27).Y>.99,"position: lowering head moves downward");
        Feed(.2,-.15);var diagonal=Feed(.29,-.21);
        check(diagonal.X>.4&&diagonal.Y>.4,"position: deliberate diagonals remain available with assistance enabled");
        mapper.Rebase(0,0);var vertical=Feed(.008,.1);
        check(Math.Abs(vertical.X)<.01&&vertical.Y<-.7,"position: small cross-axis noise is reduced during deliberate vertical travel");
        var before=Feed(.09,-.06);double tail=0;
        for(int i=0;i<20;i++){var next=mapper.Feed(.09,-.06,t+=1/30d,options)!;tail+=Math.Abs(next.X-before.X)+Math.Abs(next.Y-before.Y);before=next;}
        check(tail<1e-8,"position: fixed head pose has no continuing stationary interpolation tail");
        check(mapper.Feed(.8,.8,t+=.033,options,usable:false) is null,"position: lost tracking emits no target");
        check(mapper.Feed(double.NaN,0,t+=.033,options) is null,"position: invalid pose cannot generate a pointer target");
        var safe=mapper.Feed(0,0,t+=.033,options)!;
        check(mapper.Feed(.5,.5,t+=.033,options,freeze:true)==safe,"position: facial gesture freezes the last uncontaminated target");
        mapper.Rebase(0,0);options=options with{Up=.04};
        check(Feed(0,.04).Y<-.99,"position: smaller configured upward range reaches the upper edge");
        check(new Settings{Mapping="position",PositionUpRange=.001}.Validate() is {Mapping:"position",PositionUpRange:.025},"position: settings preserve the mode and bound comfortable angular ranges");
        var settings=new Settings{Mapping="position",ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false,MovementAlgorithm=algorithm};
        var motion=new MotionController{Settings=settings};motion.SetProfile(new Profile{CoordinateVersion=3});
        PointerOutput? output=null;
        for(int i=0;i<40;i++)output=motion.Feed(new(0,0,0,0,0,i/30d),200,100);
        check(output is {PositionX:0,PositionY:0,Dx:0,Dy:0,Frozen:false},"position: integrated controller emits an absolute centred target without relative movement");
        motion.Settings=motion.Settings with{EyeActionsUnavailable=true,RightClickMethod="either",EyebrowDoubleClick=true,SmileLearned=true};
        check(motion.EffectiveSettings is {RightClickMethod:"none",EyebrowDoubleClick:false,SmileLearned:true},"position: obscured-eye mode suppresses eye/brow actions without suppressing the learned smile");
        check(motion.Settings is {RightClickMethod:"either",EyebrowDoubleClick:true},"position: obscured-eye preference preserves configured gesture bindings for later restoration");
        var manual=new ManualAdjustmentHistory();
        check(manual.Adjust(settings,"reach") is null&&manual.Adjust(settings,"steady") is null,"position: relative reach and precision changes cannot silently affect position control");
        var slower=manual.Adjust(settings,"slower");check(slower is not null&&slower.Sensitivity<settings.Sensitivity&&manual.Undo(slower)==settings,"position: sensitivity adjustment remains reversible");
        var wire=Protocol.Decode<PointerOutput>(Protocol.Encode(output));check(wire==output,"position: optional absolute target survives the shared worker-independent contract");
        check(Protocol.Decode<PointerOutput>("{\"dx\":1,\"dy\":2,\"clickX\":0,\"clickY\":0,\"dwell\":0}").PositionX is null,"position: old relative messages do not become absolute targets");
        FacialModeVerification.Run(check,"position");
        EyebrowVerification.Run(check,"position");
        foreach(string click in new[]{"smile","dwell","right"}){
            var controller=new MotionController{Settings=settings with{ClickMethod=click=="right"?"none":click,RightClickMethod=click=="right"?"either":"none",DwellSeconds=1}};
            controller.SetProfile(new Profile{CoordinateVersion=3});int count=0;
            for(int i=0;i<100;i++){
                var o=new Observation(0,0,click=="smile"&&i>=8?.8:0,0,0,i/30d,LeftEye:click=="right"&&i<22?.9:0,RightEye:0);
                if(controller.Feed(o,200,300).Click==(click=="right"?"right":"left"))count++;
            }
            check(count==1,"position: existing "+click+" click fires once without competing actions");
        }
        motion=new MotionController{Settings=settings with{InvertX=true,InvertY=true}};motion.SetProfile(new Profile{CoordinateVersion=3});
        for(int i=0;i<30;i++)output=motion.Feed(new(.1,.08,0,0,0,i/30d),200,100);
        check(output is {PositionX:<0,PositionY:>0},"position: existing axis reversal preferences are honoured");
    }
}
