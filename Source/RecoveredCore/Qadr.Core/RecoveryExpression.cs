using System;

namespace Qadr.Core;

/// <summary>Use pre-loss thresholds; never learn a held click as a new rest.</summary>
public static class RecoveryExpression
{
    public static bool Neutral(Observation o,Settings settings)
    {
        if(!o.IsUsable)return false;
        static bool Raised(double value,double rest,double threshold)=>
            value>=rest+Math.Max(.02,threshold-rest);
        if(settings.ClickMethod=="smile"&&Raised(o.Smile,settings.SmileRestLevel,settings.SmileThreshold))return false;
        if(settings.ClickMethod=="mouth"&&Raised(o.Mouth,settings.MouthRestLevel,settings.MouthThreshold))return false;
        // Either closed eye can contaminate the expression-rest measurement.
        if(settings.RightClickMethod!="none"&&Raised(Math.Max(o.Blink,Math.Max(o.LeftEye,o.RightEye)),settings.EyeRestLevel,settings.BlinkThreshold))return false;
        if(settings.EyebrowDoubleClick&&settings.EyebrowLearned&&Raised(o.Eyebrow,settings.EyebrowRestLevel,settings.EyebrowThreshold))return false;
        if(settings.PuckerDrag&&settings.PuckerLearned&&Raised(o.Pucker,settings.PuckerRestLevel,settings.PuckerThreshold))return false;
        if(settings.MouthScroll&&settings.ScrollGestureLearned&&Raised(o.Mouth,settings.ScrollGestureRestLevel,settings.ScrollGestureThreshold))return false;
        return true;
    }
}
