using Qadr.Core;
using System.Text.Json;
using System.Runtime.Loader;
using System.Reflection;

static class FeedbackReplay
{
    public sealed class Historical(string path)
    {
        readonly Assembly assembly=new AssemblyLoadContext("Qadr-feedback-baseline",true).LoadFromAssemblyPath(Path.GetFullPath(path));
        object? controller;
        object Decode(string type,JsonElement value)=>JsonSerializer.Deserialize(value.GetRawText(),assembly.GetType("Qadr.Core."+type)!,Protocol.Json)!;
        public void Configure(JsonElement row){controller??=Activator.CreateInstance(assembly.GetType("Qadr.Core.MotionController")!)!;
            if(row.TryGetProperty("movementSettings",out var settings))controller.GetType().GetProperty("Settings")!.SetValue(controller,Decode("Settings",settings));
            if(row.TryGetProperty("profile",out var profile))controller.GetType().GetMethod("SetProfile")!.Invoke(controller,[Decode("Profile",profile)]);
        }
        public PointerOutput Feed(JsonElement face,double x,double y){var result=controller!.GetType().GetMethod("Feed")!.Invoke(controller,[Decode("Observation",face),x,y,false])!;return Protocol.Decode<PointerOutput>(JsonSerializer.Serialize(result,result.GetType(),Protocol.Json));}
        public void Reset()=>controller?.GetType().GetMethod("Reset")!.Invoke(controller,null);
    }
    public static int Run(string path,string baseline,bool comfort=false){
        try{
            var old=new Historical(baseline);var current=new MotionController();bool profileReady=false;var observations=new Dictionary<long,JsonElement>();
            int samples=0,oldCap=0,newCap=0,oldZeroY=0,newZeroY=0;double oldTravel=0,newTravel=0,oldPeak=0,newPeak=0,oldY=0,newY=0,oldX=0,newX=0;Observation? last=null;
            foreach(string line in File.ReadLines(path)){
                using var document=JsonDocument.Parse(line);var row=document.RootElement;string? kind=row.GetProperty("kind").GetString();
                if(kind is "configuration" or "recentre"){
                    old.Configure(row);
                    if(row.TryGetProperty("movementSettings",out var settings)){var recorded=Protocol.Decode<Settings>(settings.GetRawText());current.Settings=comfort?recorded with{Sensitivity=.45,RelativeTravelBoost=3.5,RelativeSpeedLimit=900}:recorded;}
                    if(row.TryGetProperty("profile",out var profile)){current.SetProfile(Protocol.Decode<Profile>(profile.GetRawText()));profileReady=true;}
                }else if(kind=="state"&&row.GetProperty("state").GetString()!="Tracking"){old.Reset();current.Reset();}
                else if(kind=="frame"){
                    if(!row.GetProperty("qualityAccepted").GetBoolean()){old.Reset();current.Reset();continue;}
                    if(row.TryGetProperty("face",out var face))observations[row.GetProperty("sequence").GetInt64()]=face.Clone();
                }else if(kind=="control"&&profileReady&&observations.Remove(row.GetProperty("sequence").GetInt64(),out var face)){
                    double x=row.GetProperty("cursorX").GetDouble(),y=row.GetProperty("cursorY").GetDouble();var before=old.Feed(face,x,y);last=Protocol.Decode<Observation>(face.GetRawText());var after=current.Feed(last,x,y);samples++;
                    if(!double.IsFinite(after.Dx)||!double.IsFinite(after.Dy))throw new Exception("Unsafe replay output");
                    oldY+=Math.Abs(before.Dy);newY+=Math.Abs(after.Dy);oldX+=Math.Abs(before.Dx);newX+=Math.Abs(after.Dx);if(before.Dy==0)oldZeroY++;if(after.Dy==0)newZeroY++;
                    double a=Math.Sqrt(before.Dx*before.Dx+before.Dy*before.Dy),b=Math.Sqrt(after.Dx*after.Dx+after.Dy*after.Dy);oldTravel+=a;newTravel+=b;
                    double av=Math.Sqrt(before.VelocityX*before.VelocityX+before.VelocityY*before.VelocityY),bv=Math.Sqrt(after.VelocityX*after.VelocityX+after.VelocityY*after.VelocityY);if(av>=950)oldCap++;if(bv>=950)newCap++;oldPeak=Math.Max(oldPeak,av);newPeak=Math.Max(newPeak,bv);
                }
            }
            if(samples<100||last is null)throw new Exception("Insufficient replay data");
            double late=0,oldStop=0,newStop=0;for(int i=1;i<=60;i++){var repeated=last with{Timestamp=last.Timestamp+i/30d};var output=current.Feed(repeated,100,100);var previous=old.Feed(JsonSerializer.SerializeToElement(repeated,Protocol.Json),100,100);if(i<=6){oldStop+=Math.Abs(previous.Dx)+Math.Abs(previous.Dy);newStop+=Math.Abs(output.Dx)+Math.Abs(output.Dy);}if(i>30)late+=Math.Abs(output.Dx)+Math.Abs(output.Dy);}
            if(late>.001)throw new Exception($"Stationary replay continued moving ({late:F9}px); last pose {last.Timestamp}, mapping {current.Settings.Mapping}");
            Console.WriteLine(Protocol.Encode(new{comfortSettings=comfort,samples,oldNearSpeedCapSamples=oldCap,newNearSpeedCapSamples=newCap,oldRequestedTravelPx=oldTravel,newRequestedTravelPx=newTravel,oldHorizontalTravelPx=oldX,newHorizontalTravelPx=newX,oldVerticalTravelPx=oldY,newVerticalTravelPx=newY,oldZeroVerticalSamples=oldZeroY,newZeroVerticalSamples=newZeroY,oldFirst200MsStationaryTravelPx=oldStop,newFirst200MsStationaryTravelPx=newStop,oldPeakSpeedPxS=oldPeak,newPeakSpeedPxS=newPeak,appendedStationarySecondTravelPx=late,nativeInputUsed=false,realInputRecording=true,counterfactualOutputs=true,accuracyImprovementMeasured=false,notes="Compares the supplied baseline DLL and updated controller on the same recorded pose, configuration, reset events and sampled positions. The final identical-pose stop interval is synthetic. Actual target hits/comfort cannot be predicted. Personal rest-noise model is not present in the report. No camera or native input."}));return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}


