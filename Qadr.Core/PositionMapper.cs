namespace Qadr.Core;

public sealed record PositionOptions(double Horizontal=.18,double Up=.12,double Down=.12,double Gain=1,bool Assist=true);
public sealed record PositionSample(double X,double Y,bool HeldX,bool HeldY);

/// <summary>Deterministic head-pose-to-screen mapping. No velocity integration,
/// prediction or accumulated edge error. A comfortable reference always maps to centre.
/// The simulator compiles this same source before application integration.</summary>
public sealed class PositionMapper
{
    readonly PositionAxis horizontal=new(),vertical=new();
    double yaw,pitch,noiseX=.0025,noiseY=.0025,last=double.NaN;
    bool frozen;
    PositionSample? output;
    public void Reset(){horizontal.Reset();vertical.Reset();last=double.NaN;frozen=false;output=null;}
    public void Rebase(double neutralYaw,double neutralPitch,double measuredNoiseX=.0025,double measuredNoiseY=.0025){
        if(!double.IsFinite(neutralYaw)||!double.IsFinite(neutralPitch))throw new ArgumentException("Invalid reference");
        yaw=neutralYaw;pitch=neutralPitch;
        noiseX=Math.Clamp(double.IsFinite(measuredNoiseX)?measuredNoiseX:.0025,.0005,.012);
        noiseY=Math.Clamp(double.IsFinite(measuredNoiseY)?measuredNoiseY:.0025,.0005,.012);Reset();
    }
    public PositionSample? Feed(double rawYaw,double rawPitch,double time,PositionOptions options,bool usable=true,bool freeze=false){
        if(!usable||!double.IsFinite(rawYaw)||!double.IsFinite(rawPitch)||!double.IsFinite(time)){
            Reset();return null;
        }
        if(double.IsFinite(last)&&time<=last)return null;
        double dt=double.IsFinite(last)?time-last:0;last=time;
        if(dt>.2){horizontal.Reset();vertical.Reset();output=null;dt=0;}
        if(freeze){frozen=true;return output;}
        // Discard expression-contaminated filter state on release. Pose is mapped
        // to the same origin; a gesture never invents a new head-to-screen reference.
        if(frozen){horizontal.Reset();vertical.Reset();frozen=false;dt=0;}
        var x=horizontal.Feed(rawYaw-yaw,time,dt,noiseX);
        var y=vertical.Feed(pitch-rawPitch,time,dt,noiseY);
        static double Map(double v,double range,double noise,double gain){
            range=double.IsFinite(range)?Math.Clamp(range,.025,.6):.18;
            gain=double.IsFinite(gain)?Math.Clamp(gain,.25,3):1;
            double centre=Math.Min(range*.15,noise*1.8);
            return Math.Clamp(Math.CopySign(Math.Max(0,Math.Abs(v)-centre)/(range-centre)*gain,v),-1,1);
        }
        double nx=Map(x.Value,options.Horizontal,noiseX,options.Gain);
        double ny=Map(y.Value,y.Value<0?options.Up:options.Down,noiseY,options.Gain);
        // Optional smooth assistance near the vertical centreline. It preserves
        // deliberate diagonals and never locks an axis or accumulates discarded motion.
        if(options.Assist&&Math.Abs(ny)>Math.Abs(nx)*3&&Math.Abs(nx)<.12)
            nx*=Math.Clamp((Math.Abs(nx)-.015)/.105,0,1);
        return output=new(nx,ny,x.Held,y.Held);
    }
}

sealed class PositionAxis
{
    readonly Queue<(double Time,double Value)> recent=new();
    double value,accepted,holdAnchor,candidate;
    bool seeded,held,pending;
    public void Reset(){seeded=held=pending=false;recent.Clear();}
    public (double Value,bool Held) Feed(double input,double time,double dt,double noise){
        if(!seeded||dt<=0){seeded=true;value=accepted=holdAnchor=input;held=pending=false;recent.Clear();recent.Enqueue((time,input));return(value,false);}
        double jump=Math.Abs(input-accepted),limit=Math.Max(.045,noise*10);
        if(jump>limit&&(!pending||Math.Abs(input-candidate)>Math.Max(noise*3,jump*.4))){candidate=input;pending=true;return(value,held);}
        pending=false;accepted=input;
        if(held){
            if(Math.Abs(input-holdAnchor)<=noise*2.5)return(value,true);
            held=false;recent.Clear();
        }
        recent.Enqueue((time,input));while(recent.Count>1&&time-recent.Peek().Time>.18)recent.Dequeue();
        double first=recent.Peek().Value,min=input,max=input,path=0,previous=first;
        foreach(var point in recent){min=Math.Min(min,point.Value);max=Math.Max(max,point.Value);path+=Math.Abs(point.Value-previous);previous=point.Value;}
        bool coherent=path>noise*.3&&Math.Abs(input-first)>path*.8;
        if(recent.Count>=3&&time-recent.Peek().Time>=.07&&max-min<=noise*3&&(!coherent||max-min<=noise*.05)){
            // Finish no backlog at confirmed rest. Keep the displayed position
            // rather than chasing the last noisy estimate after the head has stopped.
            if(max-min<=noise*.05)value=input;
            held=true;holdAnchor=(min+max)/2;return(value,true);
        }
        double change=input-value;
        // Convex causal response; no overshoot. Precision samples use a slightly
        // longer time constant than travel, with no derivative prediction.
        double tau=Math.Abs(change)>noise*4?.018:.035;
        value+=change*(1-Math.Exp(-dt/tau));return(value,false);
    }
}
