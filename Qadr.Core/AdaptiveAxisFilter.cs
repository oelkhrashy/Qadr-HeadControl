namespace Qadr.Core;

/// <summary>
/// Range-normalized, noise-aware extension of the speed-adaptive 1 Euro filter.
/// Large innovations need a second consistent observation. Calibrated noise is
/// removed from the speed estimate so tremor does not raise the filter cutoff.
/// Filtering is causal and convex: no prediction, overshoot or frame queue.
/// </summary>
public sealed class AdaptiveAxisFilter
{
    bool initialized, pending;
    double value, accepted, velocity, variation, candidate;
    double relativeTravel,relativeTravelTime;
    public double AcceptedInput => accepted;
    public double MotionConfidence { get; private set; }
    public bool RejectedSpike { get; private set; }
    public void Settle(){value=accepted;velocity=variation=relativeTravel=relativeTravelTime=0;MotionConfidence=0;}

    static double Alpha(double cutoff, double dt) => 1 / (1 + 1 / (2 * Math.PI * cutoff * dt));

    public double Feed(double input, double dt, double noise, double responsiveness,bool direct=false,bool relative=false)
    {
        if(!double.IsFinite(input) || !double.IsFinite(dt) || !double.IsFinite(noise) || !double.IsFinite(responsiveness)) {
            initialized=pending=false;MotionConfidence=0;RejectedSpike=true;return 0;
        }
        noise=Math.Clamp(noise,.0001,.3);
        responsiveness=Math.Clamp(responsiveness,.05,1);
        if(!initialized || dt<=0 || dt>TrackingTiming.MaximumGapSeconds) {
            value=accepted=input;velocity=variation=relativeTravel=relativeTravelTime=0;initialized=true;pending=false;
            MotionConfidence=0;RejectedSpike=false;return value;
        }

        // A camera spike must not become pointer acceleration. A genuine step
        // is accepted on its next consistent sample (one tracking interval).
        double innovation=input-accepted;
        double limit=Math.Max(.12,Math.Max(noise*6,Math.Abs(velocity)*dt*3));
        bool unusual=Math.Abs(innovation)>limit;
        bool confirmed=pending &&
            Math.Sign(input-accepted)==Math.Sign(candidate-accepted) &&
            Math.Abs(input-candidate)<=Math.Max(noise*3,Math.Abs(candidate-accepted)*.5);
        RejectedSpike=unusual&&!confirmed;
        double clean=input;
        if(RejectedSpike) { candidate=input;pending=true;clean=accepted; }
        else pending=false;

        // This filter consumes capture time, never UI/pump timing. Noise is in
        // learned-range units, so small and asymmetric ranges remain usable.
        double change=clean-accepted;
        if(relative&&Math.Abs(change)>noise*.025){
            if(relativeTravel!=0&&Math.Sign(change)!=Math.Sign(relativeTravel)&&Math.Abs(change)>noise*.2){relativeTravel=relativeTravelTime=0;}
            relativeTravel+=change;relativeTravelTime+=dt;
        }
        double derivative=change/dt,derivativeAlpha=Alpha(1,dt);
        velocity+=derivativeAlpha*(derivative-velocity);
        variation+=derivativeAlpha*(Math.Abs(derivative)-variation);
        accepted=clean;
        double noiseSpeed=2*Math.PI*noise;
        double deliberateSpeed=Math.Max(0,Math.Abs(velocity)-noiseSpeed);
        // Back-and-forth jitter has substantial path length but little net
        // direction. It must not open the precision band like a deliberate move.
        double coherence=variation>1e-6?Math.Abs(velocity)/variation:0;
        double coherentIntent=Math.Clamp((coherence-.35)/.5,0,1);
        // Sustained displacement beyond the noise envelope also lowers lag;
        // a slow intentional movement need not reach a large velocity.
        // A large calibrated noise estimate must not delay a coherent relative
        // stroke by several noise envelopes. Oscillating input retains the full
        // envelope; rate mode retains its existing response. Filtering stays convex.
        double catchUpNoise=relative&&direct&&coherence>=.85&&relativeTravelTime>=.22&&Math.Abs(relativeTravel)>=noise*4?noise*.5:noise*4;
        double catchUpSpeed=Math.Max(0,Math.Abs(clean-value)-catchUpNoise)/(direct?.055:.12);
        double speed=Math.Max(deliberateSpeed*coherentIntent,catchUpSpeed);
        // Lower filter delay must not itself open the precision band or add
        // screen-reach acceleration. Retain the original confidence envelope.
        double confidenceSpeed=Math.Max(deliberateSpeed*coherentIntent,Math.Max(0,Math.Abs(clean-value)-noise*4)/(direct?.055:.12));
        MotionConfidence=Math.Clamp(confidenceSpeed/Math.Max(.08,noise*6),0,1);
        double minimumCutoff=(.45+responsiveness*1.8)/(1+noise*8);
        double cutoff=Math.Clamp(minimumCutoff+(3.5+responsiveness*4)*speed,.35,30);
        value+=Alpha(cutoff,dt)*(clean-value);
        return value;
    }
}
