using Qadr.Core;
using System.Globalization;
using System.Text.Json;

/// <summary>Numeric observation replay only. Never opens a camera/microphone or
/// calls NativeInput. Target hit rate requires an actual labelled task trace.</summary>
static class Replay
{
    sealed record Trace(Profile Profile,Settings Settings,Observation[] Observations,bool Synthetic=false);
    public static int Run(string[] args){
        try{
            if(args.Contains("--recorded-metrics"))return RecordedMetrics(args);
            var input=Arguments.Get(args,"--replay")??throw new ArgumentException("Replay file required");
            var trace=Protocol.Decode<Trace>(File.ReadAllText(input));
            if(!trace.Profile.IsValid||trace.Observations.Length is <2 or >200000)throw new InvalidDataException("Invalid profile/trace size");
            var controller=new MotionController{Settings=trace.Settings.Validate(),Width=1920,Height=1080};controller.SetProfile(trace.Profile);
            var path=new CursorTrajectory();path.Reset(500,400);var cursor=(X:500.0,Y:400.0);
            var rest=new List<(double X,double Y)>();var clicks=new List<object>();var samples=new List<(double Input,double Output,double Time)>();
            double? previous=null,onset=null;double beforeX=500,beforeY=400,maxExpressionMotion=0,pathLength=0;
            bool restPeriodEnded=false;
            double trigger=trace.Settings.SmileRestLevel+(trace.Settings.SmileThreshold-trace.Settings.SmileRestLevel)*.4;
            foreach(var observation in trace.Observations){
                if(previous.HasValue&&observation.Timestamp<=previous.Value)throw new InvalidDataException("Capture timestamps must increase");
                if(new[]{observation.Timestamp,observation.Yaw,observation.Pitch,observation.Scale,observation.Smile}.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("Nonfinite observation");
                if(observation.Smile>=trigger){restPeriodEnded=true;if(!onset.HasValue){onset=observation.Timestamp;beforeX=cursor.X;beforeY=cursor.Y;}}
                var output=controller.Feed(observation,cursor.X,cursor.Y);
                if(onset.HasValue&&observation.Smile>=trigger)maxExpressionMotion=Math.Max(maxExpressionMotion,Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy));
                double dt=previous.HasValue?observation.Timestamp-previous.Value:0;previous=observation.Timestamp;
                var old=cursor;
                if(output.Frozen)path.Reset(cursor.X,cursor.Y);else path.Add(output.Dx,output.Dy,cursor.X,cursor.Y,0,0,1920,1080);
                // Include interpolation across the same simulated 8 ms pump.
                for(double remaining=dt;remaining>0;remaining-=.008)cursor=path.Step(Math.Min(.008,remaining));
                if(output.Click is not null){clicks.Add(new{at=observation.Timestamp,source=output.Click,x=output.ClickX,y=output.ClickY,displacementFromOnsetPx=Math.Sqrt(Math.Pow(output.ClickX-beforeX,2)+Math.Pow(output.ClickY-beforeY,2))});cursor=(output.ClickX,output.ClickY);path.Reset(cursor.X,cursor.Y);}
                pathLength+=Math.Sqrt(Math.Pow(cursor.X-old.X,2)+Math.Pow(cursor.Y-old.Y,2));
                if(!restPeriodEnded&&observation.Smile<trigger&&Math.Abs(observation.Yaw-trace.Profile.Neutral[0])<.01&&Math.Abs(observation.Pitch-trace.Profile.Neutral[1])<.01)rest.Add(cursor);
                samples.Add((observation.Yaw,cursor.X,observation.Timestamp));
                if(observation.Smile<trigger)onset=null;
            }
            static double Std(IEnumerable<double> data){var a=data.ToArray();double mean=a.Length==0?0:a.Average();return a.Length==0?0:Math.Sqrt(a.Average(v=>Math.Pow(v-mean,2)));}
            var report=new{trace.Synthetic,observations=trace.Observations.Length,durationSeconds=trace.Observations[^1].Timestamp-trace.Observations[0].Timestamp,restSamples=rest.Count,restSigmaXPx=Std(rest.Select(p=>p.X)),restSigmaYPx=Std(rest.Select(p=>p.Y)),maxExpressionOutputPerFramePx=maxExpressionMotion,clicks,pathLengthPx=pathLength,
                notes="1920x1080 simulated desktop; no native input. Rest sigma assumes the trace returns to the same target. Lag, overshoot and hit rate need labelled movement/target trials; they are not inferred from arbitrary observations."};
            string json=Protocol.Encode(report);string? destination=Arguments.Get(args,"--replay-output");if(destination is not null)File.WriteAllText(destination,json);Console.WriteLine(json);return 0;
        }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }

    // This branch reads the original JSONL in place. Overrides affect only the
    // in-memory replay; no Storage, camera, native pointer or publishing API is used.
    sealed record MetricFrame(double Time,double SignalX,double SignalY,double Yaw,double Pitch,
        double NoiseX,double NoiseY,double Dx,double Dy,double Dt,double SpeedLimit,bool Frozen);
    sealed class MetricTrial(string id,double x,double width){
        public string Id {get;}=id;
        public double TargetX {get;}=x+width/2;
        public double? StartCursorX;
        public readonly List<MetricFrame> Frames=[];
    }
    sealed record StopMetric(string Trial,string Channel,int StartFrame,int QuietFrames,
        double StartTime,int OutputFrames,double OutputPx,bool RightCensored,
        int OutputFramesIncludingOnset,double OutputPxIncludingOnset);
    sealed record StrokeMetric(string Trial,int StartFrame,int SignalEndFrame,int OutputEndFrame,
        double ControlledSignalExtent,double NetOutputPx,double GrossOutputPx,
        double PixelsPerUnitControlledSignal,bool OutputOpposesInput);
    static List<StrokeMetric> SegmentHorizontalStrokes(IEnumerable<MetricTrial> trials){
        var strokes=new List<StrokeMetric>();
        foreach(var trial in trials){
            var f=trial.Frames;if(f.Count<3)continue;
            // A reversal must exceed half the calibrated horizontal noise before
            // it creates a pivot. Report retained segment counts and thresholds;
            // this does not label sub-noise intent or smooth the actual controller.
            double noise=Quantile(f.Select(x=>x.NoiseX),.5),reversal=Math.Max(.0005,noise*.5),minimum=Math.Max(.002,noise);
            var pivots=new List<int>{0};int direction=0,extreme=0;
            for(int i=1;i<f.Count;i++){
                double signal=f[i].SignalX;
                if(direction==0){if(Math.Abs(signal-f[extreme].SignalX)>=reversal){direction=Math.Sign(signal-f[extreme].SignalX);extreme=i;}continue;}
                if(direction*(signal-f[extreme].SignalX)>=0){extreme=i;continue;}
                if(direction*(f[extreme].SignalX-signal)>=reversal){if(pivots[^1]!=extreme)pivots.Add(extreme);direction=-direction;extreme=i;}
            }
            if(pivots[^1]!=extreme)pivots.Add(extreme);
            for(int i=1;i<pivots.Count;i++){
                int start=pivots[i-1],end=pivots[i],outputEnd=i==pivots.Count-1?f.Count-1:end;
                double signal=f[end].SignalX-f[start].SignalX;if(Math.Abs(signal)<minimum)continue;
                var outputs=f.Skip(start+1).Take(outputEnd-start);double net=outputs.Sum(x=>x.Dx),gross=outputs.Sum(x=>Math.Abs(x.Dx));
                strokes.Add(new(trial.Id,start,end,outputEnd,Math.Abs(signal),net,gross,Math.Abs(net)/Math.Abs(signal),net*signal<0));
            }
        }
        return strokes;
    }
    static double Quantile(IEnumerable<double> values,double probability){
        var sorted=values.Order().ToArray();if(sorted.Length==0)return double.NaN;
        double at=(sorted.Length-1)*probability;int lo=(int)Math.Floor(at),hi=(int)Math.Ceiling(at);
        return sorted[lo]+(sorted[hi]-sorted[lo])*(at-lo);
    }
    static double? FiniteOrNull(double value)=>double.IsFinite(value)?value:null;
    static List<StopMetric> DetectStops(IEnumerable<MetricTrial> trials,bool yawProxy){
        var stops=new List<StopMetric>();
        foreach(var trial in trials){
            var frames=trial.Frames;
            for(int axis=0;axis<(yawProxy?1:2);axis++){
                double Signal(MetricFrame f)=>yawProxy?f.Yaw:axis==0?f.SignalX:f.SignalY;
                double Noise(MetricFrame f)=>yawProxy?.01:axis==0?f.NoiseX:f.NoiseY;
                double Output(MetricFrame f)=>yawProxy||axis==0?f.Dx:f.Dy;
                for(int i=1;i+2<frames.Count;i++){
                    // Retrospective *proxy*, never an intended-stop label. Require
                    // a clear preceding stroke and three raw observations in a
                    // narrow envelope. No controller output/rest flag selects stops.
                    double envelope=yawProxy?.002:Math.Max(.00005,Noise(frames[i])*.2);
                    if(Math.Abs(Signal(frames[i])-Signal(frames[i-1]))<=envelope)continue;
                    int start=Math.Max(0,i-6);
                    var before=frames.Skip(start).Take(i-start+1).Select(Signal).ToArray();
                    if(before.Max()-before.Min()<Math.Max(envelope*4,Noise(frames[i])*2))continue;
                    double min=Signal(frames[i]),max=min;int end=i;
                    while(end+1<frames.Count&&frames[end+1].Time-frames[end].Time<=TrackingTiming.MaximumGapSeconds){
                        double next=Signal(frames[end+1]),nextMin=Math.Min(min,next),nextMax=Math.Max(max,next);
                        if(nextMax-nextMin>envelope)break;min=nextMin;max=nextMax;end++;
                    }
                    if(end-i<2)continue;
                    double emitted=0;int lastOutput=0;
                    // Exclude the onset sample: its preceding capture interval
                    // can still contain the end of the actual movement.
                    for(int j=i+1;j<=end;j++){double output=Math.Abs(Output(frames[j]));emitted+=output;if(output>.1)lastOutput=j-i;}
                    int inclusiveFrames=lastOutput>0?lastOutput+1:Math.Abs(Output(frames[i]))>.1?1:0;
                    stops.Add(new(trial.Id,yawProxy?"yaw proxy -> horizontal":axis==0?"controlled horizontal":"controlled vertical",i,end-i+1,frames[i].Time,lastOutput,emitted,Math.Abs(Output(frames[end]))>.1,inclusiveFrames,emitted+Math.Abs(Output(frames[i]))));
                    i=end;
                }
            }
        }
        return stops;
    }
    static int MetricSelfChecks(){
        int checks=0;void Check(bool condition){checks++;if(!condition)throw new InvalidDataException("Replay metric self-check failed: "+checks);}
        Check(Math.Abs(Quantile([0,10],.9)-9)<1e-10);Check(double.IsNaN(Quantile([],.9)));
        var trial=new MetricTrial("known",0,1);double[] signal=[0,.04,.08,.12,.12,.12,.12];double[] dx=[0,80,80,80,20,5,0];
        for(int i=0;i<signal.Length;i++)trial.Frames.Add(new(i/16d,signal[i],0,signal[i],0,.001,.001,dx[i],0,1/16d,900,false));
        var stops=DetectStops([trial],false);
        Check(stops.Count==1);Check(stops[0].OutputFrames==2);Check(stops[0].OutputPx==25);Check(!stops[0].RightCensored);
        Check(stops[0].OutputFramesIncludingOnset==3);Check(stops[0].OutputPxIncludingOnset==105);
        var stroke=SegmentHorizontalStrokes([trial]);Check(stroke.Count==1&&Math.Abs(stroke[0].NetOutputPx-265)<1e-10);
        trial.Frames.RemoveAt(trial.Frames.Count-1);Check(DetectStops([trial],false).Single().RightCensored);
        var moving=new MetricTrial("moving",0,1);for(int i=0;i<20;i++)moving.Frames.Add(new(i/16d,i*.04,0,i*.04,0,.001,.001,10,0,1/16d,900,false));
        Check(DetectStops([moving],false).Count==0);return checks;
    }
    static int RecordedMetrics(string[] args){
        int selfChecks=MetricSelfChecks();string input=Arguments.Get(args,"--replay")??throw new ArgumentException("Replay file required");
        var overrides=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<args.Length;i++)if(args[i]=="--setting"){
            if(++i>=args.Length)throw new ArgumentException("Missing --setting name=value");
            var parts=args[i].Split('=',2);if(parts.Length!=2||!overrides.TryAdd(parts[0],parts[1]))throw new ArgumentException("Invalid or duplicate replay override");
        }
        foreach(var name in overrides.Keys)if(!name.Equals("RelativeTravelBoost",StringComparison.OrdinalIgnoreCase)&&!name.Equals("Mapping",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Unsupported replay override: "+name);
        double? boost=overrides.TryGetValue("RelativeTravelBoost",out var b)?double.Parse(b,CultureInfo.InvariantCulture):null;
        if(boost.HasValue&&(!double.IsFinite(boost.Value)||boost.Value is <1 or >4))throw new ArgumentException("Replay boost must be between 1 and 4");
        string? mapping=overrides.GetValueOrDefault("Mapping");if(mapping is not null&&mapping!="relative")throw new ArgumentException("Recorded metrics currently require Relative mapping");
        Settings Override(Settings saved)=>saved with{Mapping=mapping??saved.Mapping,RelativeTravelBoost=boost??saved.RelativeTravelBoost};
        double width=double.Parse(Arguments.Get(args,"--replay-width")??"1920",CultureInfo.InvariantCulture),height=double.Parse(Arguments.Get(args,"--replay-height")??"1080",CultureInfo.InvariantCulture);
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new ArgumentException("Invalid replay desktop");
        // Read the exact supplied envelope without Storage.Load: replay must never quarantine or repair user files.
        string? modelPath=Arguments.Get(args,"--replay-personal-model");string? modelHash=null;
        RestNoiseModel personalModel=new();
        if(modelPath is not null){
            using var envelope=JsonDocument.Parse(File.ReadAllText(modelPath));var root=envelope.RootElement;
            string payload=root.GetProperty("payload").GetString()??throw new InvalidDataException("Missing personal model payload");
            modelHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
            if(root.GetProperty("schema").GetInt32()!=1||!modelHash.Equals(root.GetProperty("hash").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid personal model envelope");
            personalModel=Protocol.Decode<RestNoiseModel>(payload);if(!personalModel.Valid)throw new InvalidDataException("Invalid personal model");
        }
        var controller=new MotionController{Width=width,Height=height,PersonalModel=personalModel};var accepted=new Dictionary<long,Observation>();var trials=new List<MetricTrial>();MetricTrial? trial=null;
        var all=new List<MetricFrame>();var sourceMappings=new HashSet<string>();var effectiveSettings=new List<Settings>();var profiles=new List<Profile>();
        Profile? profile=null;double? previous=null;int frozen=0,recordedPairs=0;double recordedErrorSquared=0,recordedMaxError=0;string? version=null;
        void Reset(){controller.Reset();previous=null;}
        foreach(string line in File.ReadLines(input)){
            if(string.IsNullOrWhiteSpace(line))continue;using var doc=JsonDocument.Parse(line);var row=doc.RootElement;string? kind=row.GetProperty("kind").GetString();
            if(kind=="session"){version=row.TryGetProperty("version",out var versionElement)?versionElement.GetString():null;continue;}
            if(kind is "configuration" or "recentre"){
                if(row.TryGetProperty("movementSettings",out var s)){
                    var saved=Protocol.Decode<Settings>(s.GetRawText());sourceMappings.Add(saved.Mapping);controller.Settings=Override(saved);
                    if(controller.Settings.Mapping!="relative")throw new InvalidDataException("Use --setting Mapping=relative for this non-Relative recording");
                    effectiveSettings.Add(controller.Settings);
                }
                if(row.TryGetProperty("profile",out var p)){profile=Protocol.Decode<Profile>(p.GetRawText()).Upgrade();if(!profile.IsValid)throw new InvalidDataException("Invalid recorded profile");controller.SetProfile(profile);profiles.Add(profile);previous=null;}
            }else if(kind=="state"&&row.GetProperty("state").GetString()!="Tracking")Reset();
            else if(kind=="frame"){
                if(!row.GetProperty("qualityAccepted").GetBoolean()){Reset();accepted.Clear();continue;}
                if(row.TryGetProperty("face",out var face))accepted[row.GetProperty("sequence").GetInt64()]=Protocol.Decode<Observation>(face.GetRawText());
            }else if(kind=="assessment-target"){
                string id=(row.GetProperty("label").GetString()??"?").Split(':')[0];trial=new(id,row.GetProperty("targetX").GetDouble(),row.GetProperty("targetWidth").GetDouble());trials.Add(trial);
            }else if(kind=="assessment-result")trial=null;
            else if(kind=="control"&&profile is not null&&accepted.Remove(row.GetProperty("sequence").GetInt64(),out var o)){
                if(all.Count>=200000)throw new InvalidDataException("Recorded replay sample limit exceeded");
                double x=row.GetProperty("cursorX").GetDouble(),y=row.GetProperty("cursorY").GetDouble();var output=controller.Feed(o,x,y);
                double dt=previous.HasValue?o.Timestamp-previous.Value:0;previous=o.Timestamp;
                if(!double.IsFinite(output.Dx)||!double.IsFinite(output.Dy))throw new InvalidDataException("Nonfinite replay output");
                var sample=new MetricFrame(o.Timestamp,profile.Signal(o,0),profile.Signal(o,1),o.Yaw,o.Pitch,profile.Noise[0],profile.Noise[1],output.Dx,output.Dy,dt,controller.Settings.RelativeSpeedLimit,output.Frozen);
                all.Add(sample);if(output.Frozen)frozen++;if(trial is not null){trial.StartCursorX??=x;trial.Frames.Add(sample);}
                if(row.TryGetProperty("output",out var recorded)){
                    double ex=output.Dx-recorded.GetProperty("dx").GetDouble(),ey=output.Dy-recorded.GetProperty("dy").GetDouble();double error=Math.Sqrt(ex*ex+ey*ey);recordedErrorSquared+=error*error;recordedMaxError=Math.Max(recordedMaxError,error);recordedPairs++;
                }
            }
        }
        if(all.Count<2)throw new InvalidDataException("Insufficient paired control observations");
        double Magnitude(MetricFrame f)=>Math.Sqrt(f.Dx*f.Dx+f.Dy*f.Dy);
        bool Moving(MetricFrame f)=>Magnitude(f)>.1;
        bool AtCap(MetricFrame f)=>f.SpeedLimit>0&&f.Dt>0&&Math.Abs(Magnitude(f)-f.SpeedLimit*f.Dt)<=Math.Max(1e-6,f.SpeedLimit*f.Dt*1e-6);
        var active=trials.SelectMany(t=>t.Frames).ToArray();var moving=active.Where(Moving).ToArray();var horizontal=trials.Where(t=>t.Id is "B" or "C" or "G" or "H").ToArray();
        var gains=new List<double>();var strokeReports=new List<object>();var netReach=new List<double>();
        foreach(var stroke in horizontal){
            double pathX=stroke.Frames.Sum(f=>Math.Abs(f.Dx)),netX=stroke.Frames.Sum(f=>f.Dx),signalPath=0;
            for(int i=1;i<stroke.Frames.Count;i++)signalPath+=Math.Abs(stroke.Frames[i].SignalX-stroke.Frames[i-1].SignalX);
            double? gain=signalPath>1e-9?pathX/signalPath:null;if(gain.HasValue)gains.Add(gain.Value);
            double direction=Math.Sign(stroke.TargetX-(stroke.StartCursorX??stroke.TargetX)),toward=direction*netX;netReach.Add(toward);
            strokeReports.Add(new{stroke.Id,samples=stroke.Frames.Count,grossHorizontalTravelPx=pathX,netHorizontalTravelPx=netX,netTowardTargetPx=toward,controlledSignalPath=signalPath,pixelsPerUnitControlledSignal=gain,emits537PxNetTowardTarget=toward>=537});
        }
        double trialSpread=gains.Count>=2?Quantile(gains,.9)/Quantile(gains,.1):double.NaN;
        var segments=SegmentHorizontalStrokes(horizontal);var segmentGains=segments.Select(s=>s.PixelsPerUnitControlledSignal).ToArray();
        double gainP10=Quantile(segmentGains,.1),gainP90=Quantile(segmentGains,.9),spread=segments.Count>=2?gainP90/gainP10:double.NaN;
        var stops=DetectStops(trials,false);var proxy=DetectStops(trials,true);double? stopP90=FiniteOrNull(Quantile(stops.Select(s=>(double)s.OutputFrames),.9));
        double? capPercent=moving.Length>0?100d*moving.Count(AtCap)/moving.Length:null;
        var report=new{inputFile=Path.GetFileName(input),sourceVersion=version,sourceMappings=sourceMappings.Order().ToArray(),mapping="relative",replayOnlyOverrides=overrides,
            replayDesktop=new{width,height,assumed=true,evidence="Session desktop dimensions not serialized; explicit/default dimensions are experimental assumptions, not recovered live values."},personalModelInput=new{provided=modelPath is not null,payloadSha256=modelHash,samples=personalModel.Samples,temporalFidelity="Saved snapshot only; session-time model evolution is not recorded."},metricSelfChecks=selfChecks,controlSamples=all.Count,trialControlSamples=active.Length,movingTrialFrames=moving.Length,atCapTrialFrames=moving.Count(AtCap),atCapMovingPercent=capPercent,
            allMovingFrames=all.Count(Moving),allAtCapFrames=all.Count(AtCap),frozenSamples=frozen,observedMedianIntervalSeconds=Quantile(all.Where(f=>f.Dt>0).Select(f=>f.Dt),.5),
            gainStrokeCount=segments.Count,gainP10=FiniteOrNull(gainP10),gainP90=FiniteOrNull(gainP90),gainSpreadP90OverP10=FiniteOrNull(spread),gainSpreadStatus=segments.Count<2?"insufficient segments":gainP10<=0?"unbounded (zero lower-decile gain)":"measured proxy",oppositeDirectionSegments=segments.Count(s=>s.OutputOpposesInput),directionalStrokes=segments,
            horizontalTrialGainSpreadP90OverP10=FiniteOrNull(trialSpread),horizontalStrokes=strokeReports,
            stopProxyCount=stops.Count,afterStopP90Frames=stopP90,afterStopP90Pixels=FiniteOrNull(Quantile(stops.Select(s=>s.OutputPx),.9)),rightCensoredStops=stops.Count(s=>s.RightCensored),controlledSignalStops=stops,yawProxyStops=proxy,
            afterStopP90FramesIncludingOnset=FiniteOrNull(Quantile(stops.Select(s=>(double)s.OutputFramesIncludingOnset),.9)),afterStopP90PixelsIncludingOnset=FiniteOrNull(Quantile(stops.Select(s=>s.OutputPxIncludingOnset),.9)),
            acceptance=new{atCapAtMost15Percent=capPercent.HasValue?(bool?)(capPercent<=15):null,gainSpreadAtMost2=double.IsFinite(spread)?(bool?)(spread<=2):null,
                afterStopP90AtMost2Frames=stopP90.HasValue&&stops.All(s=>!s.RightCensored)?(bool?)(stopP90<=2):null,
                everyAvailableHorizontalStrokeNetAtLeast537Px=netReach.Count>0?(bool?)netReach.All(x=>x>=537):null,allFourRequestedStrokesPresent=horizontal.Length==4},
            recordedOutputComparison=new{pairs=recordedPairs,rmsDifferencePx=recordedPairs>0?Math.Sqrt(recordedErrorSquared/recordedPairs):0,maxDifferencePx=recordedMaxError,comparable=sourceMappings.SetEquals(["relative"])},
            effectiveSettings=effectiveSettings.Select(s=>new{s.Sensitivity,s.RelativeTravelBoost,s.RelativeSpeedLimit,s.PrecisionNoiseMultiplier,s.Mapping,s.MovementAlgorithm,s.PersonalizeMovement}).Distinct().ToArray(),
            profileModes=profiles.Select(p=>p.Modes).ToArray(),nativeInputUsed=false,userDataWritten=false,liveAccuracyMeasured=false,
            methodology=new{moving="Emitted vector length >0.1 px; cap tolerance max(1e-6 px, one millionth of cap*dt); primary counts include labelled trials only.",
                gain="Primary: directional raw controlled-horizontal-signal segments within B/C/G/H. Confirm reversal at max(0.0005,0.5*calibrated noise); retain extent >=max(0.002,noise). Gain=abs(net Dx)/signal extent between pivots; final segment includes remaining trial output. Opposite output directions are separately counted. Secondary per-trial gain uses sum(abs(Dx))/sum(abs(delta signal)). Quantiles linearly interpolate; small sample, not population estimates.",
                stops="Retrospective raw controlled-signal proxy: preceding six-observation range >=2*calibrated noise, then >=3 observations spanning <=0.2*noise. Exclude onset output. Last >0.1 px output determines tail frames. Censored if output remains at the end of the quiet interval; no pass assigned when censored.",
                stopTimingUncertainty="Also report onset-inclusive totals: a true head-stop timestamp is unavailable, and excluding the onset observation changes timing by one frame. Neither variant is ground truth.",
                yawProxy="Separate horizontal comparison uses yaw range <=0.002 rad, preceding range >=0.02 rad. Yaw is NOT the actual controlled input for a shift-calibrated axis.",
                omissions="Session-time personal rest-noise evolution, private filter state and some explicit resets are not serialized in the report; no video/intended-stop labels. Desktop size is assumed. RMS against recorded baseline exposes replay mismatch; do not treat a mismatched baseline as exact reproduction.",
                interpretation="Open-loop replay cannot predict how the user responds to changed movement. Emitted travel is not actual edge reach, target success, or a live usability prediction. No saved setting, profile or binding is changed."}};
        string json=Protocol.Encode(report);string? destination=Arguments.Get(args,"--replay-output");if(destination is not null)File.WriteAllText(destination,json);Console.WriteLine(json);return 0;
    }
}
