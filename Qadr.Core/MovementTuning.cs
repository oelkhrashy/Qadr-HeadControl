namespace Qadr.Core;

/// <summary>Only these movement controls may be changed by the target test.
/// Gesture thresholds, calibration, mapping, algorithm and speech are untouched.</summary>
public sealed record MovementParameters(double Sensitivity,double TravelBoost,double PrecisionNoise)
{
    public static MovementParameters From(Settings s)=>new(s.Sensitivity,s.RelativeTravelBoost,s.PrecisionNoiseMultiplier);
    public bool Valid=>double.IsFinite(Sensitivity)&&Sensitivity is >=.3 and <=2.5&&double.IsFinite(TravelBoost)&&TravelBoost is >=1 and <=4&&double.IsFinite(PrecisionNoise)&&PrecisionNoise is >=.1 and <=2;
    public Settings Apply(Settings s)=>Valid?s with{Sensitivity=Sensitivity,RelativeTravelBoost=TravelBoost,PrecisionNoiseMultiplier=PrecisionNoise}:s;
}

public sealed record TuningContext(string Calibration,string Mapping,string Algorithm,int Width,int Height)
{
    public static TuningContext Create(Profile p,Settings s,int width,int height)=>new(Storage.DeviceKey(Protocol.Encode(new{RelativeMappingRevision=3,p.CoordinateVersion,p.Modes,p.Ranges,p.Noise,s.Responsiveness,s.Deadzone,s.RelativeSpeedLimit,s.BalanceHorizontal,s.BalanceVertical,s.DirectResponse,s.PrecisionMaximumBand,s.InvertX,s.InvertY,s.PersonalizeMovement})),s.Mapping,s.MovementAlgorithm,width,height);
}
public sealed record TunedMovementProfile(int Schema,TuningContext Context,MovementParameters Anchor,MovementParameters Previous,MovementParameters Current,string Reason,DateTime SavedUtc,bool Synthetic=false)
{
    public bool Matches(TuningContext context,Settings configured,bool synthetic=false)=>Synthetic==synthetic&&Schema==1&&Context==context&&Anchor is {Valid:true}&&Previous is {Valid:true}&&Current is {Valid:true}&&Anchor==MovementParameters.From(configured)&&MovementTuner.WithinEnvelope(Current,Anchor)&&MovementTuner.WithinEnvelope(Previous,Anchor);
}
public sealed record MovementTuningStatus(string Phase,string Message,MovementParameters Before,MovementParameters? Proposed=null,MovementParameters? Applied=null,string? Adjustment=null,bool UndoAvailable=false);

/// <summary>A small deterministic local experiment, not an ML model or an
/// estimate of intent. Only successful, uninterrupted labelled movement trials
/// can propose a change. A second run on the same geometry must validate it.</summary>
public static class MovementTuner
{
    static bool Finite(double? n)=>n is {} v&&double.IsFinite(v)&&v>=0;
    static bool Usable(AssessmentResult r)=>r.Completed&&r.ExpectedAction=="move"&&r.TrackingLosses==0&&r.Samples>=10&&r.DurationMs is >=600 and <=8500&&
        double.IsFinite(r.PathLengthPx)&&r.PathLengthPx>=40&&double.IsFinite(r.PathEfficiency)&&r.PathEfficiency is >=0 and <=1&&r.Overshoots>=0&&r.Entries>=1&&
        Finite(r.FirstEntryMs)&&r.FirstEntryMs<=r.DurationMs&&Finite(r.FinalCentreErrorPx)&&Finite(r.InsideJitterRmsPx);
    static AssessmentResult[] Directional(IReadOnlyList<AssessmentResult> results)=>results.Skip(1).Where(Usable).ToArray();
    public static bool WithinEnvelope(MovementParameters value,MovementParameters anchor)=>value.Valid&&anchor.Valid&&
        value.Sensitivity>=Math.Max(.3,anchor.Sensitivity*.7)-1e-9&&value.Sensitivity<=Math.Min(2.5,anchor.Sensitivity*1.3)+1e-9&&
        value.TravelBoost>=Math.Max(1,anchor.TravelBoost*.7)-1e-9&&value.TravelBoost<=Math.Min(4,anchor.TravelBoost*1.3)+1e-9&&
        value.PrecisionNoise>=Math.Max(.1,anchor.PrecisionNoise*.7)-1e-9&&value.PrecisionNoise<=Math.Min(2,anchor.PrecisionNoise*1.3)+1e-9;
    static MovementParameters Bound(MovementParameters v,MovementParameters a)=>new(
        Math.Clamp(v.Sensitivity,Math.Max(.3,a.Sensitivity*.7),Math.Min(2.5,a.Sensitivity*1.3)),
        Math.Clamp(v.TravelBoost,Math.Max(1,a.TravelBoost*.7),Math.Min(4,a.TravelBoost*1.3)),
        Math.Clamp(v.PrecisionNoise,Math.Max(.1,a.PrecisionNoise*.7),Math.Min(2,a.PrecisionNoise*1.3)));
    public static MovementTuningStatus Propose(Settings settings,MovementParameters anchor,IReadOnlyList<AssessmentResult> baseline,bool synthetic=false){
        var before=MovementParameters.From(settings);
        MovementTuningStatus Keep(string message)=>new("kept",message,before,Applied:before);
        if(synthetic)return Keep("Diagnostic input cannot train a personal movement profile.");
        if(!before.Valid||!anchor.Valid||baseline.Count!=5||baseline.Where((r,i)=>r.Id!=((char)('A'+i)).ToString()||r.ExpectedAction!="move").Any())return Keep("Incomplete movement evidence; previous settings kept.");
        if(baseline.Any(r=>r.TrackingLosses!=0))return Keep("Tracking was interrupted; previous settings kept.");
        var good=Directional(baseline);
        if(good.Length<3)return Keep("Not enough reliably reached targets; previous settings kept.");
        double overshoots=good.Average(r=>r.Overshoots),jitter=good.Average(r=>r.InsideJitterRmsPx!.Value),efficiency=good.Average(r=>r.PathEfficiency);
        MovementParameters candidate;string adjustment,message;
        if(overshoots>=.75&&efficiency<.9){candidate=before with{Sensitivity=before.Sensitivity*.9};adjustment="precision";message="Trying slightly slower movement to reduce overshoots. Repeat the same five targets.";}
        else if(jitter>=8&&settings.MovementAlgorithm=="adaptive"){candidate=before with{PrecisionNoise=before.PrecisionNoise*1.1};adjustment="steadiness";message="Trying a slightly stronger precision band. Repeat the same five targets.";}
        else if(settings.Mapping=="relative"&&overshoots<=.25&&efficiency>=.8&&good.Average(r=>r.FirstEntryMs!.Value)>=2000){candidate=before with{TravelBoost=before.TravelBoost*1.1};adjustment="reach";message="Trying slightly easier travel while keeping slow corrections unchanged. Repeat the same five targets.";}
        else return Keep("Your measured movement does not support a useful adjustment. Previous settings kept.");
        candidate=Bound(candidate,anchor);
        if(candidate==before)return Keep("Your personal adjustment limit was reached; previous settings kept.");
        return new("validating",message,before,candidate,Adjustment:adjustment);
    }
    public static MovementTuningStatus Validate(MovementTuningStatus proposal,IReadOnlyList<AssessmentResult> baseline,IReadOnlyList<AssessmentResult> validation,bool sameGeometry){
        MovementTuningStatus Reject(string message)=>proposal with{Phase="reverted",Message=message,Applied=proposal.Before};
        if(proposal.Phase!="validating"||proposal.Proposed is not {Valid:true}||baseline.Count!=5||validation.Count!=5||!sameGeometry)return Reject("The comparison was incomplete or changed; previous settings restored.");
        if(validation.Where((r,i)=>r.Id!=((char)('F'+i)).ToString()||r.ExpectedAction!="move"||r.TrackingLosses!=0).Any()||validation.Count(r=>r.Completed)<baseline.Count(r=>r.Completed))return Reject("Tracking or target completion became worse; previous settings restored.");
        var pairs=baseline.Skip(1).Select((r,i)=>(Before:r,After:validation[i+1])).Where(p=>Usable(p.Before)).ToArray();
        if(pairs.Length<3||pairs.Any(p=>!Usable(p.After)))return Reject("Too few comparable targets; previous settings restored.");
        double Mean(Func<AssessmentResult,double> metric,bool after)=>pairs.Average(p=>metric(after?p.After:p.Before));
        double bt=Mean(r=>r.DurationMs,false),at=Mean(r=>r.DurationMs,true),bo=Mean(r=>r.Overshoots,false),ao=Mean(r=>r.Overshoots,true),bj=Mean(r=>r.InsideJitterRmsPx!.Value,false),aj=Mean(r=>r.InsideJitterRmsPx!.Value,true);
        bool noRegression=at<=bt*1.15+100&&aj<=bj*1.15+2&&ao<=bo&&Mean(r=>r.FinalCentreErrorPx!.Value,true)<=Mean(r=>r.FinalCentreErrorPx!.Value,false)+5&&Mean(r=>r.PathEfficiency,true)>=Mean(r=>r.PathEfficiency,false)-.08;
        bool improvement=proposal.Adjustment switch{"precision"=>ao<=bo*.75&&ao<bo,"steadiness"=>aj<=bj*.8,"reach"=>at<=bt*.9,_=>false};
        return noRegression&&improvement?proposal with{Phase="applied",Message="The repeat check improved the measured result. Your movement settings were saved automatically.",Applied=proposal.Proposed}:Reject("The repeat check did not show a reliable improvement. Previous settings restored.");
    }
}
