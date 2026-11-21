namespace Qadr.Core;

public sealed record LaunchAssessmentStatus(string Phase,int Attempted,int Passed,int Skipped,int Total=5,int Round=1)
{
    public bool Required=>Phase!="complete"&&Phase!="disabled";
}

/// <summary>One five-target attempt per launch. Skips are evidence, not hits.</summary>
public sealed class LaunchAssessmentFlow(bool enabled=true)
{
    public const int TargetCount=5;
    public const double TargetSeconds=8;
    string phase=enabled?"pending":"disabled";
    int attempted,passed,skipped,round=1;
    public string NextId=>((char)((round==1?'A':'F')+attempted)).ToString();
    public LaunchAssessmentStatus Status=>new(phase,attempted,passed,skipped,Round:round);
    public bool Required=>Status.Required;
    public bool Ready(){if(phase is not ("pending" or "interrupted"))return false;phase="ready";return true;}
    public bool Begin(){if(phase!="ready")return false;round=1;attempted=passed=skipped=0;phase="running";return true;}
    public bool BeginValidation(){if(phase!="running"||attempted!=TargetCount)return false;round=2;attempted=passed=skipped=0;phase="validating";return true;}
    public bool Record(AssessmentResult result){
        if(phase is not ("running" or "validating")||attempted>=TargetCount||result.Id!=NextId||result.ExpectedAction!="move")return false;
        attempted++;if(result.Completed)passed++;else skipped++;return true;
    }
    public bool Finish(){if(phase is not ("running" or "validating")||attempted!=TargetCount)return false;phase="complete";return true;}
    public void Interrupt(){if(Required)phase="interrupted";}
}

/// <summary>One-shot centring bound to the user's current stop epoch.</summary>
public sealed class StartupPointer
{
    int? pending;
    public bool Completed {get;private set;}
    public bool Pending=>pending.HasValue;
    public void Arm(int epoch,bool recentre=false){if(recentre)Completed=false;if(!Completed)pending=epoch;}
    public void Cancel()=>pending=null;
    public (double X,double Y)? Plan(int epoch,bool inputReady,double left,double top,double width,double height){
        if(pending.HasValue&&pending!=epoch){pending=null;return null;}
        if(Completed||!pending.HasValue||!inputReady||!new[]{left,top,width,height}.All(double.IsFinite)||width<1||height<1)return null;
        return(left+(width-1)/2,top+(height-1)/2);
    }
    public void Complete(){if(pending.HasValue){Completed=true;pending=null;}}
}
