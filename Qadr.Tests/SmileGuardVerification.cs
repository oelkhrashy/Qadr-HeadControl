using Qadr.Core;

static class SmileGuardVerification
{
    public static void Run(Action<bool,string> check,string algorithm){
        foreach(string mapping in new[]{"relative","position"})foreach(int fps in new[]{16,30,60}){
            Settings S(string method)=>new(){Mapping=mapping,MovementAlgorithm=algorithm,ClickMethod=method,SmileLearned=true,SmileRestLevel=.1,SmileThreshold=.3,RightClickMethod="none",EyebrowDoubleClick=false};
            MotionController C(string method){var c=new MotionController{Settings=S(method)};c.SetProfile(new Profile{CoordinateVersion=3});return c;}
            var atStartup=C("smile");int freezes=0,clicks=0;
            for(int i=0;i<fps*5;i++){
                var o=atStartup.Feed(new(i*.0003,0,.85,0,0,i/(double)fps),100+i*8,100);
                if(o.Frozen)freezes++;if(o.Click is not null)clicks++;
            }
            check(freezes==0&&clicks==0,$"{mapping}/{fps}: learned elevated smile present at startup cannot freeze or click without neutral arming");
            foreach(string method in new[]{"smile","dwell"}){
                var c=C(method);int lateFreezes=0;bool onset=false;
                for(int i=0;i<fps*8;i++){
                    double score=i<fps/2?0:.85;
                    var o=c.Feed(new(i*.0003,0,score,0,0,i/(double)fps),100+i*8,100);
                    if(i>fps/2&&i<fps)onset|=o.Frozen;
                    if(i>fps*3&&o.Frozen)lateFreezes++;
                }
                check(onset&&lateFreezes==0,$"{mapping}/{fps}/{method}: genuine onset is protected but sustained high smile cannot indefinitely block moving control");
            }
        }
        var guard=new SmilePoseGuard();
        for(int i=0;i<10;i++)guard.Feed(0,.1,.3,i/30d,true);
        check(guard.Feed(.25,.1,.3,10/30d,true),"smile pose guard preserves pre-click learned onset protection");
        check(!guard.Feed(.8,.1,.3,2,true),"smile pose guard requires fresh neutral after a tracking gap");
        check(!guard.Feed(double.NaN,.1,.3,3,true),"invalid smile input cannot retain a pose freeze");
    }
}
