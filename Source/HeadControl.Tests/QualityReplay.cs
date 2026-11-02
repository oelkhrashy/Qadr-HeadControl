using Qadr.Core;
using System.Text.Json;

// Offline only: no native input, camera, or user settings writes.
internal static class QualityReplay
{
    internal static void Run(string tracePath,string destination)
    {
        var records=File.ReadLines(tracePath).Select(s=>JsonDocument.Parse(s)).ToArray();
        var variants=new[]{("baseline",.75,.38,true),("median3",.75,.38,true),("precision-1.5",1.5,.38,true),("precision-2",2.0,.38,true),("response-.2",.75,.2,true),("indirect",.75,.38,false)};
        var outputs=new List<object>();
        foreach(var (name,precision,response,direct) in variants)
        {
            var c=new MotionController();var rows=new List<object>();
            Observation? previous=null,older=null;
            foreach(var record in records)
            {
                var e=record.RootElement;
                if(e.GetProperty("kind").GetString()=="configuration")
                {
                    c.SetProfile(e.GetProperty("profile").Deserialize<Profile>(Protocol.Json)!);
                    if(e.TryGetProperty("personalModel",out var model))c.PersonalModel=model.Deserialize<RestNoiseModel>(Protocol.Json)!;
                    if(e.TryGetProperty("width",out var width))c.Width=width.GetDouble();
                    if(e.TryGetProperty("height",out var height))c.Height=height.GetDouble();
                    c.Settings=e.GetProperty("settings").Deserialize<Settings>(Protocol.Json)! with {PrecisionNoiseMultiplier=precision,Responsiveness=response,DirectResponse=direct,ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false,PuckerDrag=false,MouthScroll=false};
                }
                if(e.GetProperty("kind").GetString()!="sample")continue;
                var face=e.GetProperty("face").Deserialize<Observation>(Protocol.Json)!;
                var actual=e.GetProperty("output").Deserialize<PointerOutput>(Protocol.Json)!;
                var original=face;
                if(name=="median3"&&previous is not null&&older is not null&&face.Timestamp-previous.Timestamp<=.2)
                {
                    double Median(double a,double b,double d)=>a+b+d-Math.Min(a,Math.Min(b,d))-Math.Max(a,Math.Max(b,d));
                    face=face with {Yaw=Median(face.Yaw,previous.Yaw,older.Yaw),Pitch=Median(face.Pitch,previous.Pitch,older.Pitch)};
                }
                older=previous;previous=original;
                var p=c.Feed(face,actual.ClickX,actual.ClickY);
                rows.Add(new {time=face.Timestamp,dx=p.Dx,dy=p.Dy,actualDx=actual.Dx,actualDy=actual.Dy});
            }
            outputs.Add(new {name,precision,response,direct,rows});
        }
        File.WriteAllText(destination,JsonSerializer.Serialize(outputs,Protocol.Json));
        foreach(var d in records)d.Dispose();
        Console.WriteLine("Offline replay complete; no native input or settings changes.");
    }
}
