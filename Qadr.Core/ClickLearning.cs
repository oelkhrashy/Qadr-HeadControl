namespace Qadr.Core;

public sealed record ClickLessonFeedback(string Phase,double Progress,double? Threshold=null,double RestLevel=0);

/// <summary>Waits for a comfortable, held gesture and release; no action deadline.</summary>
public sealed class ClickLearning(bool eyes,Settings settings)
{
    readonly List<double> rest=[],action=[];
    double restNoise;
    double? started,actionStarted;
    double baseline,threshold;
    bool releasing;
    public void Lost(){started=actionStarted=null;rest.Clear();action.Clear();releasing=false;}
    public ClickLessonFeedback Feed(double value,double timestamp)
    {
        started??=timestamp;
        if(timestamp-started<settings.ClickLearningRestSeconds){rest.Add(value);return new("rest",Math.Clamp((timestamp-started.Value)/settings.ClickLearningRestSeconds,0,1));}
        baseline=Calibration.Median(rest);restNoise=1.4826*Calibration.Median(rest.Select(v=>Math.Abs(v-baseline)));
        double minimum=eyes?.2:settings.SubtleSmile?.02:.1;
        double separation=!eyes&&settings.SubtleSmile?Math.Max(.025,restNoise*4):settings.ClickLearningSeparation;
        if(releasing){if(value<baseline+(threshold-baseline)*settings.EyeReleaseRatio)return new("done",1,threshold,baseline);return new("release",1);}
        if(value>=Math.Max(minimum,baseline+separation)){
            actionStarted??=timestamp;action.Add(value);
            double progress=(timestamp-actionStarted.Value)/settings.ClickLearningHoldSeconds;
            if(progress>=1){threshold=Math.Clamp(baseline+Math.Max(separation*.8,(Calibration.Median(action)-baseline)*.5),minimum,.9);releasing=true;return new("release",1);}
            return new("gesture",Math.Clamp(progress,0,1));
        }
        actionStarted=null;action.Clear();return new("gesture",0);
    }
}

