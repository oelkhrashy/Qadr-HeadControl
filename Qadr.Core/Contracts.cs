using System.Text.Json;

namespace Qadr.Core;

public static class Protocol
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string Encode<T>(T message) => JsonSerializer.Serialize(message, Json);
    public static T Decode<T>(string message) => JsonSerializer.Deserialize<T>(message, Json) ?? throw new InvalidDataException("Empty message");
}

public sealed record CameraDevice(string Id, string Name);
public sealed record MouseZone(string Id,double X,double Y,double Width,double Height,long Window);
public sealed record Command(string Type, string? CameraId = null, Settings? Settings = null, string? Name = null, bool Enabled = false, int Version = 1,MouseZone[]? Zones=null,int? UserStopEpoch=null,VisionResult? DiagnosticResult=null,string? Action=null,double? InputX=null,double? InputY=null);

/// <summary>Capture the stop counter when an intent originates, before any IPC
/// or controller queue. Old and unstamped control intents fail closed.</summary>
public static class CommandIntent
{
    public static bool RequiresEpoch(Command command)=>command.Type is "adjustMovement" or "undoAdjustment" or "movementTuningUndo" or "launchAssessmentBegin" or "launchAssessmentFinish" or "assessmentTarget" or "assessmentClick" or "assessmentSkip" || command.Type=="motionRecord"&&command.Enabled || command.Type is "resume" or "useDwell" or "confirm" or "record" or "camera" or "refresh" or "calibrate" or "comfortablePosition" or "learnDrag" or "learnScroll" or "learnEyebrows" or "learnSmile" or "learnEyes" or "recentre" or "learnRest" or "resetRest" or "restoreProfile" or "tutorialStart" or "tutorialSkip" or "tutorialNext" or "tutorialBack" or "diagnosticVoice" || (command.Type is "voice" or "cameraVoice" or "tutorialVoice") && command.Enabled;
    public static bool IsCurrent(Command command,int epoch)=>!RequiresEpoch(command)||command.UserStopEpoch==epoch;
}
public sealed record EngineStatus(string State, string Title, string Help, double Progress, double MotionX, double MotionY,
    bool Face, string Distance, double Fps, double LatencyMs, string StopShortcut, CameraDevice[] Cameras,
    string? Preview, string[] Routines, string Voice, string? Pending = null, double Dwell = 0, int Version = 1, Settings? CurrentSettings = null,
    FacePosition? Position = null, string Cue = "position", double RightClickProgress = 0, string ClickFeedback = "", double LeftEye = 0, double RightEye = 0,
    string MouseMode="left",bool Dragging=false,string MouseHelp="Left click",FacePosition? DetectedFace=null,long DockToggleSequence=0,bool HeadScrolling=false,bool RoutineVoiceEnabled=false,double InferenceMs=0,double CaptureFps=0,double CameraNominalFps=0,MovementRecordingStatus? MovementRecording=null,AssessmentProgress? Assessment=null,AssessmentResult? AssessmentResult=null,long AssessmentRequestSequence=0,string? AssessmentRequest=null,LaunchAssessmentStatus? LaunchCheck=null,MovementTuningStatus? MovementTuning=null,ManualAdjustmentStatus? Adjustment=null,TrackingHealthStatus? TrackingHealth=null);
public sealed record VisionResult(int Version, string Type, long Sequence = 0, double Captured = 0,
    double[][]? Landmarks = null, Dictionary<string, double>? Blendshapes = null, double[]? Matrix = null,
    string? Preview = null, double InferenceMs = 0, string? Text = null, FaceGeometry? Geometry = null,int? UserStopEpoch=null,long? VoiceRevision=null);

/// <summary>Lossless summary of the landmark geometry used by pointer control.
/// Old workers can still send full landmarks; new Engines request this summary.</summary>
public sealed record FaceGeometry(double EyeX, double EyeY, double EyeScale, double NoseX, double NoseY,
    double Left, double Top, double Right, double Bottom, double OutsideFraction)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => double.IsFinite(EyeX) && double.IsFinite(EyeY) && double.IsFinite(EyeScale) &&
        double.IsFinite(NoseX) && double.IsFinite(NoseY) && double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Right) && double.IsFinite(Bottom) && double.IsFinite(OutsideFraction) &&
        EyeScale is >= .045 and <= .65 && Right > Left && Bottom > Top && OutsideFraction is >= 0 and <= .08;
}
public sealed record Observation(double Yaw, double Pitch, double Smile, double Mouth, double Blink,
    double Timestamp, double X = .5, double Y = .5, double Scale = .2, double LeftEye = -1, double RightEye = -1,
    double FaceX = .5, double FaceY = .5, double FaceWidth = .32, double FaceHeight = .5,double Eyebrow = 0,double Pucker = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable => double.IsFinite(Pucker)&&Pucker is >=0 and <=1 && double.IsFinite(Eyebrow)&&Eyebrow is >=0 and <=1 && double.IsFinite(Timestamp) && double.IsFinite(Yaw) && double.IsFinite(Pitch) &&
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Scale) && Scale is > 0 and <= 1 &&
        double.IsFinite(Smile) && Smile is >= 0 and <= 1 && double.IsFinite(Mouth) && Mouth is >= 0 and <= 1 &&
        double.IsFinite(Blink) && Blink is >= 0 and <= 1 && double.IsFinite(LeftEye) && (LeftEye == -1 || LeftEye is >= 0 and <= 1) &&
        double.IsFinite(RightEye) && (RightEye == -1 || RightEye is >= 0 and <= 1) &&
        double.IsFinite(FaceX) && double.IsFinite(FaceY) && double.IsFinite(FaceWidth) && double.IsFinite(FaceHeight) && FaceWidth > 0 && FaceHeight > 0;
}

public static class TrackingTiming
{
    public const double MaximumGapSeconds = .2;
    public const double MaximumAgeSeconds = .2;
    public const double RecoverySeconds = .6;
}

public sealed record Settings
{
    public double PositionHorizontalRange {get;init;}=.18;
    public double PositionUpRange {get;init;}=.12;
    public double PositionDownRange {get;init;}=.12;
    public bool PositionAxisAssist {get;init;}=true;
    public bool EyeActionsUnavailable {get;init;}
    public int TrackingFps {get;init;}=30;
    // Preview is display work; lowering this never lowers requested inference FPS.
    public int PreviewFps {get;init;}=30;
    public bool CentreOnRecovery {get;init;}=true;
    public bool MotionDiagnosticsOnStartup {get;init;}
    public bool DirectResponse {get;init;}=true;
    public bool HandsFreeVoice {get;init;}=true;
    public bool SpokenGuidance {get;init;}=false;
    public double Sensitivity { get; init; } = 1;
    public double RelativeSpeedLimit {get;init;}
    public double RelativeTravelBoost {get;init;}=1;
    public double Responsiveness { get; init; } = .38;
    public double Deadzone { get; init; } = .035;
    public bool PuckerDrag {get;init;}
    public bool PuckerLearned {get;init;}
    public double PuckerThreshold {get;init;}=.35;
    public double PuckerRestLevel {get;init;}
    public bool MouthScroll {get;init;}
    public bool ScrollGestureLearned {get;init;}
    public double ScrollGestureThreshold {get;init;}=.35;
    public double ScrollGestureRestLevel {get;init;}
    public double ToggleGestureHold {get;init;}=.35;
    public bool EyebrowDoubleClick {get;init;}=true;
    public bool EyebrowLearned {get;init;}
    public double EyebrowThreshold {get;init;}=.35;
    public double EyebrowRestLevel {get;init;}
    public double EyebrowHold {get;init;}=.30;
    public bool PersonalizeMovement {get;init;}=true;
    public bool SubtleSmile {get;init;}=true;
    public double SmileThreshold { get; init; } = .48;
    public bool SmileLearned { get; init; }
    public bool EyesLearned { get; init; }
    public double SmileRestLevel {get;init;}
    public double EyeRestLevel {get;init;}
    public double MouthRestLevel {get;init;}
    public double MouthThreshold { get; init; } = .5;
    public double BlinkThreshold { get; init; } = .45;
    public string ClickMethod { get; init; } = "dwell";
    public string RightClickMethod { get; init; } = "either";
    public double RightClickHold { get; init; } = .30;
    public double RightClickCooldown {get;init;}=.45;
    public bool BalanceHorizontal {get;init;}
    public bool BalanceVertical {get;init;}
    public double DwellSeconds { get; init; } = 1.2;
    public double DwellRadius { get; init; } = 24;
    public double DwellMaximumRadius {get;init;}=96;
    public double PrecisionNoiseMultiplier {get;init;}=.75;
    public double PrecisionMaximumBand {get;init;}=.3;
    public double DwellNoiseProjection {get;init;}=.25;
    public double DwellNoiseMultiplier { get; init; } = 3;
    public double DwellLockSeconds { get; init; } = .22;
    public double EyeReleaseRatio { get; init; } = .7;
    public double ClickCooldown { get; init; } = .8;
    public double GestureAnchorSeconds { get; init; } = .08;
    public double GestureOnsetRatio {get;init;}=.4;
    public double GestureSettleSeconds {get;init;}=.12;
    public double ClickLearningRestSeconds { get; init; } = 1.5;
    public double ClickLearningHoldSeconds { get; init; } = .4;
    public double ClickLearningSeparation { get; init; } = .08;
    public double ScrollRepeatSeconds {get;init;}=.25;
    public int ScrollNotches {get;init;}=1;
    public double PositionMinimumScale {get;init;}=.055;
    public double PositionMaximumScale {get;init;}=.34;
    public double PositionMinimumHeight {get;init;}=.17;
    public double PositionMaximumHeight {get;init;}=.88;
    public double PositionSettleSeconds {get;init;}=.8;
    public double PositionScaleTolerance {get;init;}=.12;
    public double PositionCentreTolerance {get;init;}=.045;
    public double PositionAngleTolerance {get;init;}=.10;
    public double PositionGuidePadding {get;init;}=1.18;
    public bool InvertX { get; init; }
    public bool InvertY { get; init; }
    public string Mapping { get; init; } = "relative";
    public string MovementAlgorithm { get; init; } = "adaptive";
    public Settings Validate()
    {
        static double Clamp(double n, double low, double high, double fallback) => double.IsFinite(n) ? Math.Clamp(n, low, high) : fallback;
        return this with {
            TrackingFps=TrackingFps==60?60:30,PreviewFps=PreviewFps is 15 or 60?PreviewFps:30,
            PuckerThreshold=Clamp(PuckerThreshold,.05,.95,.35),PuckerRestLevel=Clamp(PuckerRestLevel,0,.9,0),ScrollGestureThreshold=Clamp(ScrollGestureThreshold,.05,.95,.35),ScrollGestureRestLevel=Clamp(ScrollGestureRestLevel,0,.9,0),ToggleGestureHold=Clamp(ToggleGestureHold,.2,.8,.35),
            MouthScroll=MouthScroll&&ClickMethod!="mouth",
            EyebrowThreshold=Clamp(EyebrowThreshold,.05,.95,.35),EyebrowRestLevel=Clamp(EyebrowRestLevel,0,.9,0),EyebrowHold=Clamp(EyebrowHold,.2,.8,.3),
            Sensitivity = Clamp(Sensitivity, .3, 2.5, 1), Responsiveness = Clamp(Responsiveness, .05, 1, .38),
            RelativeSpeedLimit=Clamp(RelativeSpeedLimit,0,3000,0),RelativeTravelBoost=Clamp(RelativeTravelBoost,1,4,1),
            Deadzone = Clamp(Deadzone, .01, .12, .035), SmileThreshold = Clamp(SmileThreshold, SubtleSmile&&SmileLearned?.02:.1, .9, .48),
            MouthThreshold = Clamp(MouthThreshold, .2, .9, .5), BlinkThreshold = Clamp(BlinkThreshold, .2, .95, .45),
            SmileRestLevel=Clamp(SmileRestLevel,0,.9,0),EyeRestLevel=Clamp(EyeRestLevel,0,.9,0),MouthRestLevel=Clamp(MouthRestLevel,0,.9,0),
            DwellSeconds = Clamp(DwellSeconds, .6, 3, 1.2),
            DwellRadius=Clamp(DwellRadius,12,48,24),DwellMaximumRadius=Clamp(DwellMaximumRadius,24,128,96),DwellNoiseMultiplier=Clamp(DwellNoiseMultiplier,1,5,3),DwellLockSeconds=Clamp(DwellLockSeconds,.1,.5,.22),
            PrecisionNoiseMultiplier=Clamp(PrecisionNoiseMultiplier,.1,2,.75),PrecisionMaximumBand=Clamp(PrecisionMaximumBand,.05,.5,.3),DwellNoiseProjection=Clamp(DwellNoiseProjection,.1,1,.25),
            EyeReleaseRatio=Clamp(EyeReleaseRatio,.4,.9,.7),ClickCooldown=Clamp(ClickCooldown,.4,2,.8),GestureAnchorSeconds=Clamp(GestureAnchorSeconds,0,.2,.08),
            GestureOnsetRatio=Clamp(GestureOnsetRatio,.2,.8,.4),GestureSettleSeconds=Clamp(GestureSettleSeconds,.05,.4,.12),
            ClickLearningRestSeconds=Clamp(ClickLearningRestSeconds,1,5,1.5),ClickLearningHoldSeconds=Clamp(ClickLearningHoldSeconds,.3,1,.4),ClickLearningSeparation=Clamp(ClickLearningSeparation,.05,.3,.08),
            ScrollRepeatSeconds=Clamp(ScrollRepeatSeconds,.12,1,.25),ScrollNotches=Math.Clamp(ScrollNotches,1,5),
            PositionMinimumScale=Clamp(PositionMinimumScale,.045,.1,.055),PositionMaximumScale=Clamp(PositionMaximumScale,.25,.5,.34),
            PositionMinimumHeight=Clamp(PositionMinimumHeight,.12,.3,.17),PositionMaximumHeight=Clamp(PositionMaximumHeight,.7,.94,.88),
            PositionSettleSeconds=Clamp(PositionSettleSeconds,.4,3,.8),PositionScaleTolerance=Clamp(PositionScaleTolerance,.05,.25,.12),
            PositionCentreTolerance=Clamp(PositionCentreTolerance,.02,.1,.045),PositionAngleTolerance=Clamp(PositionAngleTolerance,.04,.2,.10),PositionGuidePadding=Clamp(PositionGuidePadding,1.05,1.4,1.18),
            ClickMethod = new[] {"dwell", "smile", "mouth", "none"}.Contains(ClickMethod) ? ClickMethod : "dwell",
            RightClickMethod = new[] {"either", "left", "right", "both", "none"}.Contains(RightClickMethod) ? RightClickMethod : "either",
            RightClickHold = Clamp(RightClickHold, .25, 1.5, .30),RightClickCooldown=Clamp(RightClickCooldown,.25,2,.45),
            PositionHorizontalRange=Clamp(PositionHorizontalRange,.025,.6,.18),PositionUpRange=Clamp(PositionUpRange,.025,.6,.12),PositionDownRange=Clamp(PositionDownRange,.025,.6,.12),
            Mapping = Mapping is "rate" or "position" ? Mapping : "relative",
            MovementAlgorithm = MovementAlgorithm == "classic" ? "classic" : "adaptive"
        };
    }
}

public sealed record Profile
{
    public int CoordinateVersion { get; init; } = 1;
    public double[] Neutral { get; init; } = [0, 0];
    public double[] Center { get; init; } = [.5, .5];
    public double Scale { get; init; } = .2;
    public string[] Modes { get; init; } = ["turn", "turn"];
    public double[][] Ranges { get; init; } = [[.16, .16], [.16, .16]];
    public double[] Noise { get; init; } = [.003, .003];
    public bool IsValid => CoordinateVersion == 3 && StructurallyValid;
    bool StructurallyValid => Neutral is {Length:2} && Center is {Length:2} && Modes is {Length:2} && Ranges is {Length:2} &&
        Noise is {Length:2} && Ranges.All(r => r is {Length:2}) && double.IsFinite(Scale) && Scale is >= .04 and <= 1 &&
        Neutral.Concat(Center).Concat(Noise).Concat(Ranges.SelectMany(r => r)).All(double.IsFinite) &&
        Noise.All(n => n is >= 0 and <= 1) && Ranges.All(r => r.Length == 2 && r.All(v => v is > 0 and <= 5)) &&
        Modes.All(m => m is "turn" or "shift");
    public double Signal(Observation o, int axis) => Modes[axis] == "turn"
        ? axis == 0 ? o.Yaw - Neutral[0] : Neutral[1] - o.Pitch
        : ((axis == 0 ? o.X : o.Y) - Center[axis]) / o.Scale;
    public double[] Signals(Observation o) => [Signal(o, 0), Signal(o, 1)];
    // Preserve asymmetric ranges when correcting the old vertical convention.
    public Profile Upgrade() => CoordinateVersion==2 && StructurallyValid
        ? this with {CoordinateVersion=3,Ranges=[[..Ranges[0]],Modes[1]=="turn"?[Ranges[1][1],Ranges[1][0]]:[..Ranges[1]]]}
        : this;
}

public static class Features
{
    public static Observation? Extract(VisionResult r)
    {
        if(!double.IsFinite(r.Captured))return null;
        var geometry=r.Geometry;
        if(geometry is null){
            var p=r.Landmarks;
            if(p is null || p.Length<264)return null;
            double left=double.PositiveInfinity,top=left,right=double.NegativeInfinity,bottom=right;
            int outside=0;
            foreach(var point in p){
                if(point is null || point.Length<3)return null;
                foreach(double coordinate in point)if(!double.IsFinite(coordinate))return null;
                left=Math.Min(left,point[0]);right=Math.Max(right,point[0]);top=Math.Min(top,point[1]);bottom=Math.Max(bottom,point[1]);
                if(point[0]<0||point[0]>1||point[1]<0||point[1]>1)outside++;
            }
            double dx=p[33][0]-p[263][0],dy=p[33][1]-p[263][1];
            geometry=new((p[33][0]+p[263][0])/2,(p[33][1]+p[263][1])/2,Math.Sqrt(dx*dx+dy*dy),p[1][0],p[1][1],left,top,right,bottom,(double)outside/p.Length);
        }
        if(!geometry.IsValid || r.Blendshapes?.Values.Any(v=>!double.IsFinite(v))==true)return null;
        double x=geometry.EyeX,y=geometry.EyeY,scale=geometry.EyeScale;
        double yaw=(geometry.NoseX-x)/scale,pitch=(y-geometry.NoseY)/scale;
        if(r.Matrix is {Length:>0} && (r.Matrix.Length!=16 || !r.Matrix.All(double.IsFinite)))return null;
        if (r.Matrix is { Length: 16 } m && m.All(double.IsFinite)) {
            // Euler rotation from MediaPipe's row-major canonical-face transform.
            // The worker mirrors pixels before inference. Matrix yaw has the
            // opposite sign to screen-space nose displacement in that image.
            yaw = -Math.Atan2(m[8], Math.Sqrt(m[0] * m[0] + m[4] * m[4]));
            pitch = Math.Atan2(-m[9], m[10]);
        }
        double Score(string key) => Math.Clamp(r.Blendshapes?.GetValueOrDefault(key) ?? 0, 0, 1);
        scale /= Math.Clamp(Math.Cos(yaw), .6, 1); // A turn must not look like moving farther away.
        var observation=new Observation(yaw, pitch, (Score("mouthSmileLeft") + Score("mouthSmileRight")) / 2,
            Score("jawOpen"), Math.Min(Score("eyeBlinkLeft"), Score("eyeBlinkRight")), r.Captured, x, y, scale,
            Score("eyeBlinkLeft"),Score("eyeBlinkRight"),(geometry.Left+geometry.Right)/2,(geometry.Top+geometry.Bottom)/2,geometry.Right-geometry.Left,geometry.Bottom-geometry.Top,Math.Max(Score("browInnerUp"),(Score("browOuterUpLeft")+Score("browOuterUpRight"))/2),Score("mouthPucker"));
        return observation.IsUsable?observation:null;
    }
}
