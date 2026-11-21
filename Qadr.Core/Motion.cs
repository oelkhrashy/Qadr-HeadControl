namespace Qadr.Core;

public sealed record CalibrationFeedback(string Title, string Help, double Progress, double X = 0, double Y = 0, Profile? Profile = null);

public sealed class Calibration
{
    readonly List<Observation> samples = [];
    Profile? rest;
    double[] baseline = [.003, .003, .01, .01];
    double? started, previous;
    public bool Resting=>rest is null;
    public double SmileRest { get; private set; }
    public double MouthRest { get; private set; }
    public double BlinkRest { get; private set; }
    public void Lost() { samples.Clear(); started = previous = null; }
    public CalibrationFeedback Feed(Observation o)
    {
        if (previous.HasValue && o.Timestamp - previous > .5) Lost();
        previous = o.Timestamp; started ??= o.Timestamp;
        samples.Add(o); samples.RemoveAll(s => o.Timestamp - s.Timestamp > (rest is null ? 5.2 : 12));
        double elapsed = o.Timestamp - started.Value;
        if (rest is null) {
            bool still = samples.Count >= 50 && new[] {0, 1, 2, 3}.All(i => Spread(samples.Select(s => Channels(s, null)[i])) < (i < 2 ? .06 : .03));
            if (samples[^1].Timestamp - samples[0].Timestamp >= 5 && still) {
                rest = new() { CoordinateVersion=3, Neutral = [Median(samples.Select(s => s.Yaw)), Median(samples.Select(s => s.Pitch))],
                    Center = [Median(samples.Select(s => s.X)), Median(samples.Select(s => s.Y))], Scale = Median(samples.Select(s => s.Scale)) };
                baseline = Enumerable.Range(0,4).Select(i => Math.Max(Noise(samples.Select(s => Channels(s, rest)[i])), i < 2 ? .002 : .006)).ToArray();
                SmileRest = Percentile(samples.Select(s => s.Smile), .95);
                MouthRest = Percentile(samples.Select(s => s.Mouth), .95);
                BlinkRest = Percentile(samples.Select(s => s.Blink), .8);
                samples.Clear(); started = o.Timestamp;
                return new("Explore your comfortable movement", "Move gently in any direction. Small movements are enough. Stop if you feel tired.", .25);
            }
            return new("Find your relaxed position", still ? "Rest comfortably and look toward the screen for a few seconds." : "Qadr is measuring your natural movement. Relax without forcing yourself still.", Math.Min(.24, elapsed / 5 * .24));
        }
        double[][] channels = samples.Select(s => Channels(s, rest)).ToArray();
        var modes = new string[2]; var ranges = new double[2][]; var noises = new double[2]; var ready = new bool[2];
        for (int axis = 0; axis < 2; axis++) {
            double Strength(int c) => Math.Max(Math.Abs(Percentile(channels.Select(p => p[c]), .08)), Math.Abs(Percentile(channels.Select(p => p[c]), .92))) / baseline[c];
            double turnExtent=Math.Max(Math.Abs(Percentile(channels.Select(p=>p[axis]),.08)),Math.Abs(Percentile(channels.Select(p=>p[axis]),.92)));
            bool clearTurn=turnExtent>=Math.Max(.035,baseline[axis]*5);
            // Eye-centre translation can correlate in the opposite direction
            // during a head turn. Prefer a clear pose signal; retain shift for
            // users whose comfortable movement is primarily translation.
            int best = !clearTurn && Strength(axis + 2) > Strength(axis) * 1.8 ? axis + 2 : axis;
            double negative = Math.Abs(Math.Min(0, Percentile(channels.Select(p => p[best]), .08)));
            double positive = Math.Max(0, Percentile(channels.Select(p => p[best]), .92));
            double floor = Math.Max(baseline[best] * 4, best < 2 ? .010 : .025);
            ready[axis] = Math.Min(negative, positive) >= floor || elapsed >= 10 && Math.Max(negative, positive) >= floor;
            double largest = Math.Max(negative, positive);
            ranges[axis] = [Math.Max(floor, negative < floor ? largest : negative), Math.Max(floor, positive < floor ? largest : positive)];
            modes[axis] = best < 2 ? "turn" : "shift"; noises[axis] = baseline[best];
        }
        if (elapsed >= 4 && ready.All(v => v))
            return new("Your movement is learned", "Return to your relaxed position. Your pointer will stay still until you are ready.", 1,
                Profile: rest with { Modes = modes, Ranges = ranges, Noise = noises });
        double[] signal = Channels(o, rest);
        return new("Explore your comfortable movement", !ready[0] ? "Try a comfortable movement to either side." : !ready[1] ? "Try a comfortable movement up or down." : "A little more exploration will help Qadr learn your range.",
            .25 + .3 * ready.Count(v => v), Math.Clamp(signal[modes[0] == "turn" ? 0 : 2] / ranges[0].Max(), -1, 1), Math.Clamp(signal[modes[1] == "turn" ? 1 : 3] / ranges[1].Max(), -1, 1));
    }
    static double[] Channels(Observation o, Profile? p) => p is null ? [o.Yaw, -o.Pitch, o.X, o.Y] : [o.Yaw-p.Neutral[0],p.Neutral[1]-o.Pitch,(o.X-p.Center[0])/o.Scale,(o.Y-p.Center[1])/o.Scale];
    public static double Percentile(IEnumerable<double> values, double fraction) { double[] a = values.Order().ToArray(); return a.Length == 0 ? 0 : a[Math.Clamp((int)((a.Length - 1) * fraction), 0, a.Length - 1)]; }
    public static double Median(IEnumerable<double> v) => Percentile(v, .5);
    static double Spread(IEnumerable<double> v) => Percentile(v, .9) - Percentile(v, .1);
    static double Noise(IEnumerable<double> values) { double[] a = values.ToArray(); double m = Median(a); return Median(a.Select(x => Math.Abs(x - m))) * 1.4826; }
}

public sealed class OneEuro
{
    double? value, previous;
    double derivative;
    public void Settle(double input){value=previous=input;derivative=0;}
    static double Alpha(double cutoff, double dt) => 1 / (1 + 1 / (2 * Math.PI * cutoff * dt));
    public double Feed(double input, double dt, double responsiveness)
    {
        if (!value.HasValue) { value = previous = input; return input; }
        dt = Math.Max(.001, dt);
        derivative += Alpha(1, dt) * ((input - previous!.Value) / dt - derivative); previous = input;
        value += Alpha(.9 + responsiveness * 2 + 3.5 * Math.Abs(derivative), dt) * (input - value.Value);
        return value.Value;
    }
}

public sealed class Gesture(double hold = .34)
{
    double? start, previous;
    bool latched,armed;
    double? neutralSince;
    double firedAt = double.NegativeInfinity;
    public bool Active { get; private set; }
    public double Progress {get;private set;}
    public bool Feed(double value, double threshold, double timestamp,double rest=0,double releaseRatio=.7,double cooldown=.8)
    {
        if(!double.IsFinite(value)||!double.IsFinite(timestamp)){start=previous=neutralSince=null;armed=latched=Active=false;Progress=0;return false;}
        if(previous.HasValue && timestamp<=previous.Value)return false;
        if(previous.HasValue && timestamp-previous.Value>TrackingTiming.MaximumGapSeconds){start=neutralSince=null;armed=latched=Active=false;Progress=0;}
        previous=timestamp;
        double release=rest+(threshold-rest)*releaseRatio;
        if(value<release){neutralSince??=timestamp;if(timestamp-neutralSince>=.06)armed=true;}
        else neutralSince=null;
        if(!armed){Active=false;Progress=0;return false;}
        if(start.HasValue&&timestamp-start.Value>1.5){armed=latched=Active=false;start=null;Progress=0;return false;}
        Active = value >= threshold || latched && value >= release;
        if (latched) { if (value < rest+(threshold-rest)*releaseRatio) { latched = false; start = null;Progress=0; } return false; }
        if (!Active) { start = null; Progress=0; return false; }
        start ??= timestamp;
        Progress=Math.Clamp((timestamp-start.Value)/hold,0,1);
        if (timestamp - start >= hold && timestamp - firedAt >= cooldown) { latched = true; firedAt = timestamp; return true; }
        return false;
    }
}

/// <summary>Fires once on reopening after an intentional hold, never on an ordinary blink.</summary>
public sealed class EyeClick
{
    double? since;
    double lastClick=double.NegativeInfinity;
    double held;
    bool bilateralAttempt;
    double? previous;
    public bool Active {get;private set;}
    public double Progress {get;private set;}
    public bool Qualified {get;private set;}
    public bool Feed(double left,double right,Settings settings,double now){
        if(!double.IsFinite(left)||!double.IsFinite(right)||!double.IsFinite(now)){since=previous=null;held=0;bilateralAttempt=false;Progress=0;Active=Qualified=false;return false;}
        if(previous.HasValue && now<=previous.Value)return false;
        if(previous.HasValue && now-previous.Value>TrackingTiming.MaximumGapSeconds){since=null;held=0;bilateralAttempt=false;Progress=0;Active=Qualified=false;previous=null;}
        double threshold=settings.BlinkThreshold;
        double release=settings.EyeRestLevel+(threshold-settings.EyeRestLevel)*settings.EyeReleaseRatio;
        bool closed=settings.RightClickMethod switch {"left"=>left>=threshold&&right<release,"right"=>right>=threshold&&left<release,"both"=>Math.Min(left,right)>=threshold,"either"=>Math.Max(left,right)>=threshold,_=>false};
        bool open=Math.Max(left,right)<release;
        double dt=previous.HasValue?now-previous.Value:0;previous=now;
        if(settings.RightClickMethod=="none"){since=null;held=0;bilateralAttempt=false;Progress=0;Active=Qualified=false;return false;}
        if(Math.Min(left,right)>=threshold)bilateralAttempt=true;
        double requiredHold=bilateralAttempt?Math.Max(.4,settings.RightClickHold):settings.RightClickHold;
        if(closed){if(!since.HasValue){since=now;held=0;}else held+=dt;Active=true;Progress=Math.Clamp(held/requiredHold,0,1);Qualified=Progress>=1;return false;}
        if(!open && since.HasValue){Active=true;return false;}
        bool fire=open&&since.HasValue&&Qualified&&now-lastClick>=settings.RightClickCooldown;
        if(open){since=null;held=0;bilateralAttempt=false;Active=Qualified=false;Progress=0;}
        if(fire)lastClick=now;
        return fire;
    }
}

public sealed record PointerOutput(double Dx, double Dy, string? Click, double ClickX, double ClickY, double Dwell, bool DistanceChanged = false, bool Frozen = false,bool IsRate=false,double VelocityX=0,double VelocityY=0,bool StopX=false,bool StopY=false,double? PositionX=null,double? PositionY=null);
public sealed class MotionController
{
    public Settings Settings { get; set; } = new();
    public Profile? Profile { get; private set; }
    public double[] Normalized { get; private set; } = [0, 0];
    public RestNoiseModel PersonalModel {get;set;}=new();
    readonly PositionMapper positionMapper=new();
    readonly SmilePoseGuard smilePoseGuard=new();
    ExpressionRest? expressionRest;
    Settings? effectiveSource,effectiveSettings;
    public Settings EffectiveSettings{
        get{
            if(!ReferenceEquals(effectiveSource,Settings)||effectiveSettings is null){
                effectiveSource=Settings;effectiveSettings=expressionRest?.Apply(Settings)??Settings;
                if(Settings.EyeActionsUnavailable)effectiveSettings=effectiveSettings with{RightClickMethod="none",EyebrowDoubleClick=false};
            }
            return effectiveSettings;
        }
    }
    public void SetExpressionRest(IEnumerable<Observation> observations){
        var samples=observations.Where(o=>o.IsUsable).ToArray();expressionRest=ExpressionRest.From(samples);effectiveSettings=null;
        if(Profile is {} p){
            double Noise(Func<Observation,double> channel){if(samples.Length<10)return .0025;double median=Calibration.Median(samples.Select(channel));return Math.Max(.0005,1.4826*Calibration.Median(samples.Select(o=>Math.Abs(channel(o)-median))));}
            positionMapper.Rebase(p.Neutral[0],p.Neutral[1],Noise(o=>o.Yaw),Noise(o=>o.Pitch));
        }
    }
    public double Width { get; set; } = 1920;
    public double Height { get; set; } = 1080;
    OneEuro[] filters = [new(), new()];
    AdaptiveAxisFilter[] adaptiveFilters = [new(), new()];
    Gesture smile = new(.28), mouth = new(.34);
    EyeClick eyes=new();
    EyebrowClick eyebrows=new();
    ReleaseGesture dragGesture=new(),scrollGesture=new();
    public double RightProgress=>eyes.Progress;
    public double LeftProgress {get;private set;}
    public bool HoldingClick=>Settings.ClickMethod=="dwell"?dwellFired:Settings.ClickMethod=="smile"?smile.Active:Settings.ClickMethod=="mouth"&&mouth.Active;
    double? lastTime, distanceSince, dwellSince;
    double trackingScale,distanceScale, dwellX, dwellY, gestureX, gestureY, clicksUntil;
    Observation? distanceRest;
    bool dwellFired, wasGesture;
    bool expressionHeld;
    double expressionReleased=double.NegativeInfinity;
    double eyeReleased=double.NegativeInfinity;
    double eyeSettleSeconds;
    double[] lastMapped = [0, 0];
    double[] heldSignal=[0,0],dwellMapped=[0,0];
    readonly Queue<(double Time,double X,double Y)> history=new();
    public void SetProfile(Profile p) { p=p.Upgrade();if (!p.IsValid) throw new InvalidDataException("Invalid movement profile"); Profile = p; trackingScale=p.Scale;expressionRest=null;effectiveSettings=null; distanceSince = null; positionMapper.Rebase(p.Neutral[0],p.Neutral[1]);Reset(); }
    public void Rebase(Observation o){if(Profile is not {} p)return;Profile=p with{Center=[o.X,o.Y],Neutral=[o.Yaw,o.Pitch],Scale=o.Scale};trackingScale=o.Scale;distanceSince=null;distanceRest=null;positionMapper.Rebase(o.Yaw,o.Pitch);Reset();}
    readonly RelativeTravelIntent travelIntent=new();
    readonly RelativeRest[] relativeRest=[new(),new()];
    readonly bool[] relativeHeld=[false,false];
    void ResetFilters(){filters=[new(),new()];adaptiveFilters=[new(),new()];travelIntent.Reset();foreach(var axis in relativeRest)axis.Reset();Array.Clear(relativeHeld);}
    public void Reset() { smilePoseGuard.Reset();positionMapper.Reset();ResetFilters(); smile = new(.28); mouth = new(.34); eyes=new();eyebrows=new();dragGesture=new();scrollGesture=new(); lastTime = dwellSince = null; dwellFired = wasGesture = false; lastMapped = [0, 0];heldSignal=[0,0];LeftProgress=0;Normalized=[0,0];history.Clear();clicksUntil=0;expressionHeld=false;expressionReleased=eyeReleased=double.NegativeInfinity; }
    public bool AdaptDistance(Observation o)
    {
        var p=Profile;if(p is null)return false;
        double ratio = o.Scale / (trackingScale>0?trackingScale:p.Scale);
        // Translation is already divided by the live eye scale; angular motion is
        // distance-independent. Ordinary distance changes need no neutral refit.
        if(ratio>.65 && ratio<1.5){
            trackingScale+=(o.Scale-trackingScale)*.03;
            distanceSince=null;distanceRest=null;return false;
        }
        if ((ratio <= .65 || ratio >= 1.5) && (ratio<.65 || ratio>1.5 || Math.Abs(o.Yaw-p.Neutral[0]) < .14 && Math.Abs(o.Pitch-p.Neutral[1]) < .14)) {
            if (!distanceSince.HasValue || distanceRest is null || Math.Abs(o.Scale-distanceScale)/o.Scale > .05 || Math.Abs(o.Yaw-distanceRest.Yaw)>Math.Max(.03,p.Noise[0]*5) || Math.Abs(o.Pitch-distanceRest.Pitch)>Math.Max(.03,p.Noise[1]*5) || Math.Abs(o.X-distanceRest.X)>.02 || Math.Abs(o.Y-distanceRest.Y)>.02) { distanceSince=o.Timestamp; distanceScale=o.Scale;distanceRest=o; }
            else if (o.Timestamp-distanceSince >= .85) { Profile=p with { Center=[o.X,o.Y], Neutral=[o.Yaw,o.Pitch], Scale=o.Scale };trackingScale=o.Scale; distanceSince=null;distanceRest=null; }
            Reset(); return true;
        }
        distanceSince=null;
        return false;
    }
    bool NeedsDistanceRefit(Observation o)=>Settings.Mapping!="position"&&AdaptDistance(o);
    public PointerOutput Feed(Observation o, double x, double y,bool suppressLeft=false)
    {
        if(!o.IsUsable){Reset();return new(0,0,null,x,y,0,Frozen:true);}
        if(lastTime.HasValue && o.Timestamp<=lastTime.Value)return new(0,0,null,x,y,LeftProgress,Frozen:true);
        if(lastTime.HasValue && o.Timestamp-lastTime.Value>TrackingTiming.MaximumGapSeconds)Reset();
        var p = Profile; if (p is null) return new(0,0,null,x,y,0);
        if(NeedsDistanceRefit(o))return new(0,0,null,x,y,0,true,true);
        double dt=lastTime.HasValue ? o.Timestamp-lastTime.Value : 0; lastTime=o.Timestamp;
        var settings=EffectiveSettings;
        history.Enqueue((o.Timestamp,x,y));while(history.Count>1&&o.Timestamp-history.Peek().Time>Math.Max(.3,settings.GestureAnchorSeconds+.1))history.Dequeue();
        bool eyesWereActive=eyes.Active,eyesWereQualified=eyes.Qualified;
        bool right=eyes.Feed(o.LeftEye<0?o.Blink:o.LeftEye,o.RightEye<0?o.Blink:o.RightEye,settings,o.Timestamp);
        if(eyesWereActive&&!eyes.Active){eyeReleased=o.Timestamp;eyeSettleSeconds=right||eyesWereQualified?settings.GestureSettleSeconds:0;}
        bool dragIntent=settings.PuckerDrag&&settings.PuckerLearned&&o.Pucker>=settings.PuckerThreshold;
        bool scrollIntent=settings.MouthScroll&&settings.ScrollGestureLearned&&settings.ClickMethod!="mouth"&&o.Mouth>=settings.ScrollGestureThreshold;
        bool anyBrow=settings.EyebrowDoubleClick&&settings.EyebrowLearned&&o.Eyebrow>=settings.EyebrowThreshold;
        bool leftConflict=(settings.ClickMethod=="smile"&&o.Smile>=settings.SmileThreshold)||(settings.ClickMethod=="mouth"&&o.Mouth>=settings.MouthThreshold);
        bool modeWasActive=dragGesture.Active||scrollGesture.Active;
        bool dragToggle=dragGesture.Feed(o.Pucker,settings.PuckerThreshold,settings.PuckerRestLevel,settings.ToggleGestureHold,o.Timestamp,settings.PuckerDrag&&settings.PuckerLearned,eyes.Active||right||scrollIntent||anyBrow||leftConflict);
        bool scrollToggle=scrollGesture.Feed(o.Mouth,settings.ScrollGestureThreshold,settings.ScrollGestureRestLevel,settings.ToggleGestureHold,o.Timestamp,settings.MouthScroll&&settings.ScrollGestureLearned&&settings.ClickMethod!="mouth",eyes.Active||right||dragIntent||anyBrow||leftConflict);
        if(modeWasActive&&!dragGesture.Active&&!scrollGesture.Active){eyeReleased=o.Timestamp;eyeSettleSeconds=settings.GestureSettleSeconds;}
        bool modeIntent=dragGesture.Active||scrollGesture.Active;
        if(modeIntent){smile=new(.28);mouth=new(.34);}
        bool browWasActive=eyebrows.Active;
        bool browIntent=settings.EyebrowDoubleClick&&settings.EyebrowLearned&&o.Eyebrow>=settings.EyebrowThreshold;
        bool browConflict=modeIntent||eyes.Active||right||(settings.ClickMethod=="smile"&&o.Smile>=settings.SmileThreshold)||(settings.ClickMethod=="mouth"&&o.Mouth>=settings.MouthThreshold);
        bool doubleClick=eyebrows.Feed(o.Eyebrow,settings,o.Timestamp,browConflict);
        browIntent=eyebrows.Active;
        if(browWasActive&&!eyebrows.Active){eyeReleased=o.Timestamp;eyeSettleSeconds=settings.GestureSettleSeconds;}
        if(browIntent){smile=new(.28);mouth=new(.34);}
        bool clicked = !suppressLeft&&!modeIntent&&!browIntent && (settings.ClickMethod switch { "smile" => smile.Feed(o.Smile,settings.SmileThreshold,o.Timestamp,settings.SmileRestLevel,settings.EyeReleaseRatio,settings.ClickCooldown), "mouth" => mouth.Feed(o.Mouth,settings.MouthThreshold,o.Timestamp,settings.MouthRestLevel,settings.EyeReleaseRatio,settings.ClickCooldown), _=>false });
        bool leftGesture=settings.ClickMethod switch {"smile"=>smile.Active,"mouth"=>mouth.Active,_=>false};
        // A learned smile can contaminate head pose even when dwell is selected.
        // Pause it before its click threshold; never turn it into a dwell click.
        bool smileGuardEnabled=(settings.Mapping is "relative" or "position")&&(settings.ClickMethod is "dwell" or "smile")&&settings.SmileLearned;
        bool smileGuard=smilePoseGuard.Feed(o.Smile,settings.SmileRestLevel,settings.SmileThreshold,o.Timestamp,smileGuardEnabled);
        leftGesture|=smileGuard;
        bool expressionMethod=settings.ClickMethod is "smile" or "mouth"||smileGuardEnabled;
        if(expressionMethod&&leftGesture){expressionHeld=true;expressionReleased=double.NegativeInfinity;}
        else if(expressionHeld&&!leftGesture){expressionHeld=false;expressionReleased=o.Timestamp;}
        bool settling=expressionMethod&&!expressionHeld&&o.Timestamp-expressionReleased<settings.GestureSettleSeconds;
        bool eyeSettling=o.Timestamp-eyeReleased<eyeSettleSeconds;
        bool gesture = dragGesture.Active||scrollGesture.Active||modeIntent||dragToggle||scrollToggle||eyebrows.Active || browIntent || doubleClick || eyes.Active || leftGesture || expressionHeld || settling || eyeSettling;
        if (gesture && !wasGesture) {
            var before=history.Peek();double anchorTime=o.Timestamp-settings.GestureAnchorSeconds;
            foreach(var sample in history){if(sample.Time>anchorTime)break;before=sample;}
            gestureX=before.X;gestureY=before.Y;
        }
        wasGesture=gesture;
        // Discard expression-contaminated filter history on stable release.
        // The first post-settle sample seeds mapping and emits no jump.
        if(!gesture&&(double.IsFinite(expressionReleased)||double.IsFinite(eyeReleased))){ResetFilters();lastMapped=[0,0];heldSignal=[0,0];dt=0;expressionReleased=eyeReleased=double.NegativeInfinity;}
        Span<double> delta=stackalloc double[2], mapped=stackalloc double[2];
        Span<bool> stopped=stackalloc bool[2];
        for(int i=0;i<2;i++) {
            double signal=p.Signal(o,i);
            double range=p.Ranges[i][signal<0?0:1];
            // Balancing uses both learned directions. Using the smaller range made
            // a narrow calibration amplify every movement and saturate early.
            if(i==0?settings.BalanceHorizontal:settings.BalanceVertical)
                range=Math.Sqrt(p.Ranges[i][0]*p.Ranges[i][1]);
            // Relative control measures changes, so the joystick's saturation
            // limit must not discard movement beyond the learned exploration.
            // Keep a finite guard; tracking and spike gates still apply.
            double limit=settings.Mapping=="rate"?1.8:settings.RelativeTravelBoost>1?32:8;
            double raw=Math.Clamp(signal/range,-limit,limit)*((i==0?settings.InvertX:settings.InvertY)?-1:1);
            double learnedNoise=settings.PersonalizeMovement&&PersonalModel.Valid?PersonalModel.Noise(i,p.Noise[i]):p.Noise[i];
            double zone=Math.Clamp(learnedNoise*3/range+settings.Deadzone,.065,.32);
            bool adaptive=settings.MovementAlgorithm=="adaptive";
            double noise=learnedNoise/range;
            double smooth=adaptive?adaptiveFilters[i].Feed(raw,dt,noise,settings.Responsiveness,settings.DirectResponse,settings.Mapping=="relative"):filters[i].Feed(raw,dt==0?.033:dt,settings.Responsiveness);
            Normalized[i]=smooth;
            double filtered=smooth;
            double accepted=adaptive?adaptiveFilters[i].AcceptedInput:raw;
            bool resting=settings.Mapping=="relative"&&relativeRest[i].Feed(accepted,filtered,noise,o.Timestamp,dt);
            bool enteringRest=resting&&!relativeHeld[i];
            stopped[i]=resting&&(!enteringRest||!relativeRest[i].CanFinish);relativeHeld[i]=resting;
            // Finish only the tightly bounded remaining filter displacement on
            // the transition, then settle. No raw-motion acceleration or sign gate.
            if(resting){smooth=accepted;if(adaptive)adaptiveFilters[i].Settle();else filters[i].Settle(accepted);}
            // A small hysteresis band removes drift at any head angle. It is
            // measured from the user's rest noise, not a fixed pixel step.
            double band=Math.Clamp(learnedNoise/range*settings.PrecisionNoiseMultiplier,.0003,settings.PrecisionMaximumBand);
            // Retain a wide stabilizing band at rest, but reduce stiction when
            // coherent movement is distinguishable from calibrated noise.
            if(adaptive)band*=1-.85*adaptiveFilters[i].MotionConfidence;
            if(dt==0)heldSignal[i]=smooth;
            else if(Math.Abs(smooth-heldSignal[i])>band)heldSignal[i]=smooth-Math.CopySign(band,smooth-heldSignal[i]);
            smooth=heldSignal[i];
            double amount=Math.Max(0,Math.Abs(smooth)-zone)/(1-zone);
            // Only the vertical relative axis loses the joystick centre zone.
            // Keep its existing slope, full precision band and filtered input.
            mapped[i]=settings.Mapping=="relative"&&i==1?Math.CopySign((Math.Max(0,Math.Abs(smooth)-zone)+.35*Math.Min(Math.Abs(smooth),zone))/(1-zone),smooth):Math.CopySign(amount,smooth);
            if(settings.Mapping=="rate") delta[i]=Math.Abs(accepted)<=zone?0:Math.CopySign(Math.Min(1.35,Math.Pow(amount,1.65))*(i==0?Width:Height)*.68*settings.Sensitivity*dt,smooth);
            else {
                delta[i]=dt>0&&!stopped[i]?(mapped[i]-lastMapped[i])*(i==0?Width:Height)*.7*settings.Sensitivity:0;
            }
            lastMapped[i]=mapped[i];
        }
        if(settings.Mapping=="relative"&&settings.RelativeTravelBoost>1&&dt>0){
            double confidence=settings.MovementAlgorithm=="adaptive"?Math.Max(adaptiveFilters[0].MotionConfidence,adaptiveFilters[1].MotionConfidence):1;
            confidence*=travelIntent.Feed(delta[0],delta[1],dt,Width,Height,settings.Sensitivity);
            var travel=RelativeReach.Apply(delta[0],delta[1],dt,Width,Height,settings.Sensitivity,settings.RelativeTravelBoost,confidence);
            delta[0]=travel.X;delta[1]=travel.Y;
        }
        if(settings.Mapping=="relative"&&settings.RelativeSpeedLimit>0&&dt>0){
            double length=Math.Sqrt(delta[0]*delta[0]+delta[1]*delta[1]);
            double maximum=settings.RelativeSpeedLimit*dt;
            // Discard excess travel, never queue it for after the user stops.
            // Scale the vector together so diagonal direction is preserved.
            if(length>maximum){delta[0]*=maximum/length;delta[1]*=maximum/length;}
        }
        if(gesture || right || dt==0)delta.Clear();
        string? click=right?"right":dragToggle?"drag":scrollToggle?"scroll-toggle":doubleClick?"double":clicked&&!eyes.Active?"left":null;
        bool frozen=gesture||right;
        double progress=0;
        bool rateMoving=settings.Mapping=="rate"&&(Math.Abs(delta[0])+Math.Abs(delta[1])>5*dt);
        if(rateMoving){dwellSince=null;dwellFired=false;}
        // A natural blink pauses dwell; it must not restart the entire timer.
        // Deliberate eye holds and left gestures cancel it to avoid two clicks.
        if(dragGesture.Active||scrollGesture.Active||modeIntent||dragToggle||scrollToggle||eyebrows.Active||browIntent||doubleClick||leftGesture||eyes.Qualified||right){dwellSince=null;dwellFired=false;clicksUntil=o.Timestamp+settings.ClickCooldown;}
        else if((eyes.Active||eyeSettling)&&dwellSince.HasValue)dwellSince+=dt;
        if(!suppressLeft&&!rateMoving&&settings.ClickMethod=="dwell" && !gesture && !right && o.Timestamp>=clicksUntil) {
            // A tiny fixed pixel radius made dwell impossible for people with
            // measured natural movement. Bound the personal capture radius;
            // it never grows without limit as a range becomes smaller.
            double projectedNoise=Math.Max(Width*p.Noise[0]/p.Ranges[0].Min(),Height*p.Noise[1]/p.Ranges[1].Min())*.7*settings.Sensitivity*settings.DwellNoiseProjection;
            double radius=Math.Max(settings.DwellRadius,Math.Min(settings.DwellMaximumRadius,projectedNoise*settings.DwellNoiseMultiplier));
            bool escaped=false;
            for(int i=0;i<2;i++)escaped|=Math.Abs(mapped[i]-dwellMapped[i])>Math.Max(radius/((i==0?Width:Height)*.7*settings.Sensitivity),settings.DwellNoiseMultiplier*p.Noise[i]/Math.Min(p.Ranges[i][0],p.Ranges[i][1]));
            double offsetX=x-dwellX,offsetY=y-dwellY;
            if(!dwellSince.HasValue || offsetX*offsetX+offsetY*offsetY>radius*radius || escaped) {
                dwellX=x; dwellY=y; mapped.CopyTo(dwellMapped);dwellSince=o.Timestamp; dwellFired=false;
            } else if(!dwellFired) {
                progress=Math.Clamp((o.Timestamp-dwellSince.Value)/settings.DwellSeconds,0,1);
                if(progress>=1) { click="left"; dwellFired=true;gestureX=dwellX;gestureY=dwellY; }
            }
            if(dwellFired || o.Timestamp-dwellSince!.Value>=settings.DwellLockSeconds){delta.Clear();frozen=true;}
        }
        LeftProgress=settings.ClickMethod switch{"smile"=>smile.Progress,"mouth"=>mouth.Progress,_=>progress};
        if(settings.Mapping=="position"){
            var point=positionMapper.Feed(o.Yaw,o.Pitch,o.Timestamp,new PositionOptions(settings.PositionHorizontalRange,settings.PositionUpRange,settings.PositionDownRange,settings.Sensitivity/.45,settings.PositionAxisAssist),freeze:frozen);
            if(point is not null)point=point with{X=settings.InvertX?-point.X:point.X,Y=settings.InvertY?-point.Y:point.Y};
            return new(0,0,click,click=="left"&&settings.ClickMethod=="dwell"?dwellX:gesture||right?gestureX:x,click=="left"&&settings.ClickMethod=="dwell"?dwellY:gesture||right?gestureY:y,LeftProgress,Frozen:frozen||point is null,StopX:point?.HeldX??true,StopY:point?.HeldY??true,PositionX:point?.X,PositionY:point?.Y);
        }
        return new(delta[0],delta[1],click,click=="left"&&settings.ClickMethod=="dwell"?dwellX:gesture||right?gestureX:x,click=="left"&&settings.ClickMethod=="dwell"?dwellY:gesture||right?gestureY:y,LeftProgress,Frozen:frozen,IsRate:settings.Mapping=="rate",VelocityX:!frozen&&dt>0?delta[0]/dt:0,VelocityY:!frozen&&dt>0?delta[1]/dt:0,StopX:stopped[0],StopY:stopped[1]);
    }
}

public sealed class QualityGate
{
    Observation? previous;
    double stableSince;
    public bool Accept(Observation? o, double now)
    {
        if(o is null || !o.IsUsable || !double.IsFinite(now) || now-o.Timestamp > TrackingTiming.MaximumAgeSeconds || now < o.Timestamp) { previous=null; stableSince=now; return false; }
        if(previous is not null) {
            double dt=o.Timestamp-previous.Timestamp;
            if(dt<=0){previous=null;stableSince=o.Timestamp;return false;}
            if(dt>TrackingTiming.MaximumGapSeconds || dt<.15 && (Math.Abs(o.Yaw-previous.Yaw)>.45 || Math.Abs(o.Pitch-previous.Pitch)>.4 || Math.Abs(o.Scale-previous.Scale)/previous.Scale>.35 || Math.Abs(o.X-previous.X)>.15 || Math.Abs(o.Y-previous.Y)>.15)) { previous=o; stableSince=o.Timestamp; return false; }
        } else stableSince=o.Timestamp;
        previous=o; return o.Timestamp-stableSince>=TrackingTiming.RecoverySeconds;
    }
}
