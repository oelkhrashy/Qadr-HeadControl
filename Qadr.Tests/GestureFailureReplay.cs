using Qadr.Core;
using System.Text.Json;

static class GestureFailureReplay
{
    public static int Run(string path,string baseline,bool relative){
        try{
            var old=new FeedbackReplay.Historical(baseline);var current=new MotionController();bool ready=false;
            var faces=new Dictionary<long,JsonElement>();int samples=0,oldFrozen=0,newFrozen=0,oldClicks=0,newClicks=0;
            double oldRun=0,newRun=0,oldLongest=0,newLongest=0,lastTime=double.NaN,maxStep=0;
            foreach(string line in File.ReadLines(path)){
                using var document=JsonDocument.Parse(line);var row=document.RootElement;string? kind=row.GetProperty("kind").GetString();
                if(kind is "configuration" or "recentre"){
                    Profile? profile=row.TryGetProperty("profile",out var p)?Protocol.Decode<Profile>(p.GetRawText()):null;
                    Settings? settings=row.TryGetProperty("movementSettings",out var s)?Protocol.Decode<Settings>(s.GetRawText()):null;
                    if(settings is not null){if(relative)settings=settings with{Mapping="relative"};current.Settings=settings;}
                    if(profile is not null){current.SetProfile(profile);ready=true;}
                    old.Configure(JsonSerializer.SerializeToElement(new{profile,movementSettings=settings},new JsonSerializerOptions(Protocol.Json){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull}));
                }else if(kind=="state"&&row.GetProperty("state").GetString()!="Tracking"){old.Reset();current.Reset();oldRun=newRun=0;}
                else if(kind=="frame"){
                    if(!row.GetProperty("qualityAccepted").GetBoolean()){old.Reset();current.Reset();oldRun=newRun=0;continue;}
                    if(row.TryGetProperty("face",out var face))faces[row.GetProperty("sequence").GetInt64()]=face.Clone();
                }else if(kind=="control"&&ready&&faces.Remove(row.GetProperty("sequence").GetInt64(),out var face)){
                    var observation=Protocol.Decode<Observation>(face.GetRawText());double x=row.GetProperty("cursorX").GetDouble(),y=row.GetProperty("cursorY").GetDouble();
                    var before=old.Feed(face,x,y);var after=current.Feed(observation,x,y);samples++;
                    if(before.Click is not null)oldClicks++;if(after.Click is not null)newClicks++;
                    double dt=double.IsFinite(lastTime)?Math.Clamp(observation.Timestamp-lastTime,0,.2):0;lastTime=observation.Timestamp;
                    if(before.Frozen){oldFrozen++;oldRun+=dt;oldLongest=Math.Max(oldLongest,oldRun);}else oldRun=0;
                    if(after.Frozen){newFrozen++;newRun+=dt;newLongest=Math.Max(newLongest,newRun);}else newRun=0;
                    double step=Math.Sqrt(after.Dx*after.Dx+after.Dy*after.Dy);maxStep=Math.Max(maxStep,step);
                    if(!double.IsFinite(step))throw new Exception("Nonfinite movement");
                }
            }
            if(samples<100)throw new Exception("Insufficient real replay samples");
            if(oldClicks!=newClicks)throw new Exception("Recorded gesture actions changed during a pose-guard-only fix");
            if(path.Contains("131328")&&(oldLongest<7||newLongest>=2||newFrozen>=oldFrozen))throw new Exception($"Recorded long-freeze regression not resolved: old {oldLongest:F2}s/{oldFrozen}, new {newLongest:F2}s/{newFrozen}");
            Console.WriteLine(Protocol.Encode(new{report=Path.GetFileName(path),samples,mapping=relative?"relative":"recorded",oldFrozen,newFrozen,oldClicks,newClicks,oldLongestFreezeSeconds=oldLongest,newLongestFreezeSeconds=newLongest,maxControllerRelativeStepPx=maxStep,nativeInputUsed=false,intendedGestureLabelsAvailable=false,liveAccuracyImprovementMeasured=false,notes="Replays actual observations/configuration against the previous DLL. Measures controller freezes, not whether a smile was intended. Target success and comfort still require live confirmation."}));return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
