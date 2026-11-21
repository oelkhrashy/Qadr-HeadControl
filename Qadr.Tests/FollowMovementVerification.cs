using Qadr.Core;
using System.Text.Json;

/// <summary>No camera, microphone or Windows input. Regression tests and numeric replay.</summary>
static class FollowMovementVerification
{
    static Profile P()=>new(){CoordinateVersion=3,Modes=["shift","turn"],
        Ranges=[[.07535597269064569,.12975727960190478],[.30486902723386183,.05667157288454165]],
        Noise=[.009087092957464082,.012427753682838955],Scale=.11};
    static Settings S(string algorithm)=>new(){Mapping="relative",MovementAlgorithm=algorithm,
        Sensitivity=.5,RelativeSpeedLimit=550,BalanceHorizontal=true,BalanceVertical=true,ClickMethod="none",RightClickMethod="none"};
    static Observation O(double time,double x=.5,double pitch=0)=>new(0,pitch,0,0,0,time,X:x,Scale:.11);

    public static void Run(Action<bool,string> check,string algorithm)
    {
        foreach(int fps in new[]{16,30,60}){
            var c=new MotionController{Settings=S(algorithm)};c.SetProfile(P());
            double x=960,lateTravel=0;PointerOutput output=new(0,0,null,x,540,0);
            // Customer profile; extend a translation beyond the old 1.8-range ceiling.
            for(int i=0;i<fps*4;i++){
                double t=(double)i/fps;output=c.Feed(O(t,.5+.012*t),x,540);x+=output.Dx;
                if(t>=2.5)lateTravel+=output.Dx;
            }
            check(lateTravel>200,$"relative movement continues beyond old learned-range saturation at {fps} FPS");
            double tail=0;
            for(int i=0;i<fps*2;i++){
                output=c.Feed(O(4+(double)i/fps,.5+.012*(4-1d/fps)),x,540);x+=output.Dx;
                if(i>=fps)tail+=Math.Abs(output.Dx)+Math.Abs(output.Dy);
            }
            check(!output.IsRate&&tail<1,$"holding the head off centre leaves less than one pixel of settling travel rather than sustained velocity at {fps} FPS (tail {tail:F4} px)");
            var gap=c.Feed(O(8,.58),x,540);
            check(gap.Dx==0&&gap.Dy==0&&gap.Click is null,$"relative recovery after a gap seeds without jumping at {fps} FPS");
            c.Reset();check(c.Feed(O(8.1,.55),x,540).Dx==0,$"relative explicit stop clears pending movement at {fps} FPS");
        }
        foreach(int axis in new[]{0,1}){
            double Travel(int sign){
                var c=new MotionController{Settings=S(algorithm)};c.SetProfile(P());double travel=0;
                for(int i=0;i<90;i++){
                    double signal=sign*.06*Math.Clamp(i/30d,0,1);
                    var output=c.Feed(O(i/30d,axis==0?.5+signal*.11:.5,axis==1?-signal:0),960,540);
                    travel+=axis==0?output.Dx:output.Dy;
                }
                return travel;
            }
            check(Math.Abs(Travel(-1)+Travel(1))<.001&&Travel(1)>0,
                $"optional balanced axis {axis} gives symmetric movement for the real asymmetric profile");
        }
        check(!Protocol.Decode<Settings>("{}").BalanceVertical,"old settings retain separately learned vertical ranges unless balancing is chosen");
        check(Protocol.Decode<Settings>("{}").RelativeSpeedLimit==0&&new Settings{RelativeSpeedLimit=double.NaN}.Validate().RelativeSpeedLimit==0,"old profiles and invalid speed limits retain unrestricted movement unless chosen");
        var limited=new MotionController{Settings=S(algorithm)};limited.SetProfile(P());
        double previous=0;bool bounded=true;
        for(int i=0;i<180;i++){
            double now=i/30d;var output=limited.Feed(O(now,.5+.07*Math.Sin(now*2),.15*Math.Sin(now*2)),500,400);
            bounded&=Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy)<=550*(now-previous)+1e-6;previous=now;
        }
        check(bounded,"relative speed limit bounds diagonal travel without introducing a queued tail");
        var dwell=new MotionController{Settings=S(algorithm) with{ClickMethod="dwell",DwellSeconds=.6}};
        dwell.SetProfile(P());int clicks=0;
        for(int i=0;i<90;i++)if(dwell.Feed(O(i/30d,.52,.07),500,400).Click=="left")clicks++;
        check(clicks==1,"relative dwell can click once while the head rests away from neutral");
    }

    public static int Replay(string path,bool reach=false)
    {
        try{
            var c=new MotionController();Observation? last=null;var frames=new Dictionary<long,Observation>();
            long samples=0,oldSaturatedX=0,oldSaturatedY=0,frozen=0;
            double travel=0,maxDelta=0,x=960,y=540;
            foreach(string line in File.ReadLines(path)){
                using var row=JsonDocument.Parse(line);var r=row.RootElement;
                string? kind=r.GetProperty("kind").GetString();
                if(kind is "configuration" or "recentre"){
                    if(r.TryGetProperty("movementSettings",out var settings))
                        c.Settings=Protocol.Decode<Settings>(settings.GetRawText()) with{Mapping="relative",Sensitivity=.5,RelativeSpeedLimit=reach?1000:550,RelativeTravelBoost=reach?3:1,BalanceHorizontal=true,BalanceVertical=true};
                    if(r.TryGetProperty("profile",out var profile))c.SetProfile(Protocol.Decode<Profile>(profile.GetRawText()));
                }
                else if(kind=="state"&&r.GetProperty("state").GetString()!="Tracking")c.Reset();
                else if(kind=="frame"){
                    if(!r.GetProperty("qualityAccepted").GetBoolean()){c.Reset();continue;}
                    if(r.TryGetProperty("face",out var face))frames[r.GetProperty("sequence").GetInt64()]=Protocol.Decode<Observation>(face.GetRawText());
                }
                else if(kind=="control"&&c.Profile is {} p&&frames.Remove(r.GetProperty("sequence").GetInt64(),out var o)){
                    last=o;
                    double rx=c.Settings.BalanceHorizontal?p.Ranges[0].Min():p.Ranges[0][p.Signal(o,0)<0?0:1];
                    if(Math.Abs(p.Signal(o,0)/rx)>1.8)oldSaturatedX++;
                    if(Math.Abs(p.Signal(o,1)/p.Ranges[1][p.Signal(o,1)<0?0:1])>1.8)oldSaturatedY++;
                    var output=c.Feed(o,x,y);samples++;
                    if(!double.IsFinite(output.Dx)||!double.IsFinite(output.Dy)||output.IsRate)throw new Exception("Unsafe relative replay output");
                    if(output.Frozen){frozen++;if(output.Dx!=0||output.Dy!=0)throw new Exception("Frozen replay emitted movement");}
                    double distance=Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy);travel+=distance;maxDelta=Math.Max(maxDelta,distance);
                    x=Math.Clamp(x+output.Dx,0,1919);y=Math.Clamp(y+output.Dy,0,1079);
                }
            }
            if(last is null||samples<100)throw new Exception("Insufficient real report samples");
            // Repeat the final head position. This is a synthetic stop appended
            // to recorded input, not a measured improvement in user performance.
            double lateTravel=0;
            for(int i=1;i<=60;i++){
                var output=c.Feed(last with{Timestamp=last.Timestamp+i/30d},x,y);
                if(i>30)lateTravel+=Math.Abs(output.Dx)+Math.Abs(output.Dy);
            }
            if(lateTravel>.001)throw new Exception("Stationary head continued moving");
            Console.WriteLine(Protocol.Encode(new{inputIsRealRecording=true,outputIsCounterfactualSimulation=true,reachBoost=reach?3:1,speedLimit=reach?1000:550,samples,
                oldRangeSaturatedX=oldSaturatedX,oldRangeSaturatedY=oldSaturatedY,frozen,pathLengthPx=travel,maximumFrameDeltaPx=maxDelta,
                appendedStationarySecondTravelPx=lateTravel,nativeInputUsed=false,imagesRead=false,
                notes="Controller-only replay at recorded capture timestamps with proposed relative settings on a 1920x1080 simulated desktop. Historical effective expression settings retained. Personal noise model is not in the report and is not replayed. Does not predict target hits, latency or live-user accuracy."}));return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
