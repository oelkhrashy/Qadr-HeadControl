namespace Qadr.Core;

public sealed record ManualAdjustmentStatus(string Message,bool CanUndo);

/// <summary>One-step movement-only undo. Never restores whole settings or gestures.</summary>
public sealed class ManualAdjustmentHistory
{
    MovementParameters? previous,expected;
    string message="Choose one small change. Undo restores your previous movement settings.";
    public void Observe(Settings settings){
        if(expected is not null&&MovementParameters.From(settings)!=expected){previous=expected=null;message="Movement settings changed elsewhere. Choose a new adjustment to enable Undo.";}
    }
    public ManualAdjustmentStatus Status(Settings settings){Observe(settings);return new(message,previous is not null);}
    public Settings? Adjust(Settings settings,string? choice){
        Observe(settings);if(settings.Mapping=="position"&&choice is "reach" or "steady"){message="Position control uses comfortable angular ranges and its own noise filter. Adjust those ranges in Preferences; relative travel/steadiness changes do not apply.";return null;}var before=MovementParameters.From(settings);
        var after=choice switch{
            "slower"=>before with{Sensitivity=Math.Max(.3,Math.Round(before.Sensitivity*.9,3))},
            "reach"=>before with{TravelBoost=Math.Min(4,Math.Round(before.TravelBoost+.25,2))},
            "steady"=>before with{PrecisionNoise=Math.Min(2,Math.Round(before.PrecisionNoise*1.1,3))},
            _=>null
        };
        if(after is null||!after.Valid)return null;
        if(after==before){message="This setting is already at its limit. Your settings are unchanged.";return null;}
        previous=before;expected=after;
        message=choice switch{"slower"=>"Slower movement applied. Try pointing, or choose Undo.","reach"=>"More screen reach applied. Try a longer stroke, or choose Undo.",_=>"More steadiness applied. Small corrections may need more movement. Try it, or choose Undo."};
        return after.Apply(settings);
    }
    public Settings? Undo(Settings settings){
        Observe(settings);if(previous is null)return null;
        var restored=previous.Apply(settings);previous=expected=null;message="Previous movement settings restored.";return restored;
    }
    public void Explain(string reason)=>message=reason;
}
