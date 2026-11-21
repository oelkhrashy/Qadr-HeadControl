using Qadr.Core;
using System.Text.Json;

static class RecordedRestVerification
{
    public static int Run(string path,string baseline){
        try{
            Profile? profile=null;Settings? settings=null;
            foreach(string line in File.ReadLines(path)){
                using var doc=JsonDocument.Parse(line);var row=doc.RootElement;
                if(row.GetProperty("kind").GetString() is not ("configuration" or "recentre"))continue;
                if(row.TryGetProperty("profile",out var p))profile=Protocol.Decode<Profile>(p.GetRawText());
                if(row.TryGetProperty("movementSettings",out var s))settings=Protocol.Decode<Settings>(s.GetRawText());
            }
            if(profile is null||settings is null)throw new Exception("No recorded movement configuration");
            settings=settings with{ClickMethod="none",RightClickMethod="none",PuckerDrag=false,MouthScroll=false,EyebrowDoubleClick=false};
            var scenarios=new List<object>();
            foreach(string algorithm in new[]{"adaptive","classic"})foreach(int fps in new[]{16,26,30,60})foreach(int axis in new[]{0,1}){
                var configuration=settings with{MovementAlgorithm=algorithm};
                var current=new MotionController{Settings=configuration};current.SetProfile(profile);
                var old=new FeedbackReplay.Historical(baseline);old.Configure(JsonSerializer.SerializeToElement(new{profile,movementSettings=configuration},Protocol.Json));
                double oldTail=0,newTail=0,oldLate=0,newLate=0,stopTime=double.NaN,oldTravel=0,newTravel=0;bool stopped=false;
                for(int i=0;i<=fps*3;i++){
                    double t=i/(double)fps,range=Math.Sqrt(profile.Ranges[axis][0]*profile.Ranges[axis][1]);
                    double signal=range*.6*Math.Clamp(t-.5,0,1)+(t>1.5?profile.Noise[axis]*.06*Math.Sin(t*27):0);
                    var o=new Observation(profile.Neutral[0],profile.Neutral[1],0,0,0,t,profile.Center[0],profile.Center[1],profile.Scale);
                    if(profile.Modes[axis]=="turn")o=axis==0?o with{Yaw=o.Yaw+signal}:o with{Pitch=o.Pitch-signal};
                    else o=axis==0?o with{X=o.X+signal*o.Scale}:o with{Y=o.Y+signal*o.Scale};
                    var before=old.Feed(JsonSerializer.SerializeToElement(o,Protocol.Json),960,540);var after=current.Feed(o,960,540);
                    double a=Math.Abs(axis==0?before.Dx:before.Dy),b=Math.Abs(axis==0?after.Dx:after.Dy);oldTravel+=a;newTravel+=b;
                    if(t>1.5){oldTail+=a;newTail+=b;if(!stopped&&(axis==0?after.StopX:after.StopY)){stopped=true;stopTime=t-1.5;}}
                    if(t>1.75){oldLate+=a;newLate+=b;}
                }
                if(!stopped||stopTime>.25||newLate>.001||newTail>oldTail+2)throw new Exception($"Recorded-profile noisy stop regressed: {algorithm}/{fps}/{axis}: stop={stopTime}, late={newLate}, oldTail={oldTail}, newTail={newTail}");
                scenarios.Add(new{algorithm,fps,axis,stopConfirmationSeconds=stopTime,oldTailPx=oldTail,newTailPx=newTail,oldTravelAfter250MsPx=oldLate,newTravelAfter250MsPx=newLate,oldTravelPx=oldTravel,newTravelPx=newTravel});
            }
            var restNoise=new List<object>();
            foreach(int fps in new[]{16,26,60})foreach(int frequency in new[]{3,6,10})foreach(int axis in new[]{0,1}){
                var current=new MotionController{Settings=settings with{MovementAlgorithm="adaptive"}};current.SetProfile(profile);
                var old=new FeedbackReplay.Historical(baseline);old.Configure(JsonSerializer.SerializeToElement(new{profile,movementSettings=current.Settings},Protocol.Json));
                var a=new List<double>();var b=new List<double>();double oldPosition=0,newPosition=0;var random=new Random(19);
                for(int i=0;i<fps*6;i++){
                    double t=i/(double)fps,range=Math.Sqrt(profile.Ranges[axis][0]*profile.Ranges[axis][1]);
                    double signal=range*.5+profile.Noise[axis]*(1.5*Math.Sin(2*Math.PI*frequency*t)+.65*Math.Sin(2*Math.PI*1.617*frequency*t)+random.NextDouble()*2-1);
                    var o=new Observation(profile.Neutral[0],profile.Neutral[1],0,0,0,t,profile.Center[0],profile.Center[1],profile.Scale);
                    if(profile.Modes[axis]=="turn")o=axis==0?o with{Yaw=o.Yaw+signal}:o with{Pitch=o.Pitch-signal};
                    else o=axis==0?o with{X=o.X+signal*o.Scale}:o with{Y=o.Y+signal*o.Scale};
                    var before=old.Feed(JsonSerializer.SerializeToElement(o,Protocol.Json),960,540);var after=current.Feed(o,960,540);
                    oldPosition+=axis==0?before.Dx:before.Dy;newPosition+=axis==0?after.Dx:after.Dy;
                    if(t>=2){a.Add(oldPosition);b.Add(newPosition);}
                }
                double Sigma(List<double> values){double mean=values.Average();return Math.Sqrt(values.Average(x=>(x-mean)*(x-mean)));}
                double oldSigma=Sigma(a),newSigma=Sigma(b);
                if(newSigma>oldSigma*1.1+.5)throw new Exception($"Recorded-profile stationary jitter regressed: {fps}/{frequency}/{axis}: {oldSigma}->{newSigma}");
                restNoise.Add(new{fps,frequency,axis,oldSigmaPx=oldSigma,newSigmaPx=newSigma});
            }
            Console.WriteLine(Protocol.Encode(new{report=Path.GetFileName(path),synthetic=true,recordedProfile=true,gestureActionsDisabledForMovementIsolation=true,nativeInputUsed=false,scenarios,restNoise,notes="Generated ramp and noise use the recorded ranges, noise, settings and modes. The true user head-stop moment is not labelled in the recording. This is software evidence, not a live accuracy measurement."}));return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
