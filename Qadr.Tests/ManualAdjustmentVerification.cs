using Qadr.Core;

static class ManualAdjustmentVerification
{
    public static void Run(Action<bool,string> check){
        var original=new Settings{Sensitivity=.45,RelativeTravelBoost=3.5,PrecisionNoiseMultiplier=.75,ClickMethod="smile",SmileThreshold=.42,SmileLearned=true,InvertY=true};
        foreach(string choice in new[]{"slower","reach","steady"}){
            var history=new ManualAdjustmentHistory();var adjusted=history.Adjust(original,choice)!;
            check(adjusted is not null&&history.Status(adjusted).CanUndo,"adjustment applies and offers Undo: "+choice);
            check(adjusted!.ClickMethod==original.ClickMethod&&adjusted.SmileThreshold==original.SmileThreshold&&adjusted.InvertY&&adjusted.Mapping==original.Mapping&&adjusted.MovementAlgorithm==original.MovementAlgorithm,"adjustment preserves gestures and mapping: "+choice);
            var later=adjusted with{BlinkThreshold=.7};var restored=history.Undo(later)!;
            check(MovementParameters.From(restored)==MovementParameters.From(original)&&restored.BlinkThreshold==.7&&!history.Status(restored).CanUndo,"Undo restores only movement and consumes its snapshot: "+choice);
        }
        var h=new ManualAdjustmentHistory();var slower=h.Adjust(original,"slower")!;var reach=h.Adjust(slower,"reach")!;
        check(h.Undo(reach)==slower,"repeated adjustments undo the latest step only");
        var other=new ManualAdjustmentHistory();var candidate=other.Adjust(original,"steady")!;
        check(other.Undo(candidate with{Sensitivity=.6}) is null,"Undo never overwrites a later external movement change");
        check(new ManualAdjustmentHistory().Adjust(original,"unknown") is null,"unknown adjustment leaves settings unchanged");
        check(new ManualAdjustmentHistory().Adjust(original with{RelativeTravelBoost=4},"reach") is null,"reach adjustment respects its existing bound");
        check(CommandIntent.RequiresEpoch(new("adjustMovement"))&&CommandIntent.RequiresEpoch(new("undoAdjustment"))&&!CommandIntent.IsCurrent(new("adjustMovement",UserStopEpoch:1),2),"stale adjustment and Undo intents fail closed after Escape");
    }
}
