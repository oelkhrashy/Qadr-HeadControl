using Qadr.Core;

static class LaunchVerification
{
    static AssessmentResult Result(string id,bool passed=true)=>new(id,"move",passed,passed?"held inside target":"skipped",1000,100,50,.8,1,0,0,5,1,0,0,0,30);
    public static void Run(Action<bool,string> check){
        var flow=new LaunchAssessmentFlow();check(flow.Required&&flow.Status.Phase=="pending","every new app process starts with a required assessment");
        check(!flow.Begin()&&!flow.Finish(),"mandatory assessment cannot begin before tracking or finish without trials");
        check(flow.Ready()&&!flow.Ready()&&flow.Begin(),"tracking readiness offers one automatic launch check");
        check(!flow.Record(Result("B"))&&!flow.Record(Result("A") with{ExpectedAction="double"}),"out-of-order and wrong-action results cannot complete a launch check");
        check(flow.Record(Result("A"))&&!flow.Record(Result("A")),"a launch target outcome counts exactly once");
        flow.Interrupt();check(flow.Required&&!flow.Finish()&&flow.Status.Phase=="interrupted","stop preserves the mandatory requirement without claiming success");
        check(flow.Ready()&&flow.Begin()&&flow.Status.Attempted==0,"explicit resume restarts a comparable five-target run");
        foreach(char id in "ABCDE")flow.Record(Result(id.ToString(),id=='A'));
        check(flow.Status.Attempted==5&&flow.Status.Passed==1&&flow.Status.Skipped==4&&flow.Finish(),"all five attempts unlock use while skipped targets stay distinct from passes");
        check(!flow.Required&&!flow.Ready()&&!flow.Begin(),"tracking recovery cannot repeat a finished launch check");
        check(new LaunchAssessmentFlow().Required&&!new LaunchAssessmentFlow(false).Required,"a new launch requires testing again while no-input diagnostics can explicitly disable it");
        var start=new StartupPointer();start.Arm(3);
        check(start.Plan(3,false,0,0,1920,1080) is null&&start.Pending,"startup centring waits for the open input gate");
        check(start.Plan(3,true,-1920,0,1920,1080)==(-960.5,539.5),"startup centre uses monitor coordinates including negative monitor origins");
        start.Complete();start.Arm(3);check(start.Completed&&start.Plan(3,true,0,0,1920,1080) is null,"tracking loss and resume cannot repeat a completed startup centre");
        start.Arm(3,recentre:true);check(start.Pending&&!start.Completed&&start.Plan(3,true,0,0,1920,1080)==(959.5,539.5),"stable tracking recovery may explicitly centre again");
        start.Complete();start.Arm(3,recentre:true);check(start.Plan(4,true,0,0,1920,1080) is null&&!start.Pending,"Escape invalidates a recovery centring request");
        start.Arm(4,recentre:true);start.Cancel();check(!start.Pending&&start.Plan(4,true,0,0,1920,1080) is null,"stop cancels recovery centring without a delayed jump");
        start=new();start.Arm(3);check(start.Plan(4,true,0,0,1920,1080) is null&&!start.Pending,"an old centring request is invalidated by Escape");
        start.Arm(4);start.Cancel();check(start.Plan(4,true,0,0,1920,1080) is null,"explicit stop cancels pending startup centring");
        start.Arm(4);check(start.Plan(4,true,double.NaN,0,1920,1080) is null,"invalid monitor geometry cannot create a pointer request");
        check(CommandIntent.RequiresEpoch(new("launchAssessmentBegin"))&&CommandIntent.RequiresEpoch(new("launchAssessmentFinish"))&&!CommandIntent.IsCurrent(new("launchAssessmentFinish",UserStopEpoch:2),3),"mandatory-check start and completion commands reject stale stop epochs");
    }
}
