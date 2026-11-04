using System;

namespace Qadr.Core;

/// <summary>
/// Continuous relative pointing: the established precision/rest path opens
/// smoothly into a shared vector response as deliberate motion becomes clear.
/// No neutral-angle dead zone, minimum cursor speed or axis selection.
/// </summary>
internal sealed class AnalogHeadMotion
{
    private readonly AdaptiveAxisFilter x = new(), y = new();
    private readonly RelativeRest restX = new(), restY = new();
    private readonly RelativeHeadAxis precisionX = new(), precisionY = new();
    private readonly PointerStability stability = new();
    private bool initialized;
    private double filteredX, filteredY, heldX, heldY;
    public double PositionX => heldX;
    public double PositionY => heldY;
    public bool Stopped { get; private set; }
    public bool StoppedX { get; private set; }
    public bool StoppedY { get; private set; }
    public double MotionConfidence => Math.Max(x.MotionConfidence, y.MotionConfidence);

    public (double X, double Y) Feed(double inputX, double inputY, double dt,
        double timestamp, double noiseX, double noiseY, double gainX, double gainY,
        double responsiveness, bool direct, double steadiness, double precision)
    {
        if (!double.IsFinite(gainX) || !double.IsFinite(gainY) || gainX <= 0 || gainY <= 0)
        {
            initialized = false; Stopped = StoppedX = StoppedY = true;
            restX.Reset(); restY.Reset();
            stability.Reset();
            return (0, 0);
        }
        x.Feed(inputX, dt, noiseX, responsiveness, direct, relative: true);
        y.Feed(inputY, dt, noiseY, responsiveness, direct, relative: true);
        double precisionDx = precisionX.Feed(inputX, dt, timestamp, noiseX, gainX, responsiveness, direct, steadiness, precision);
        double precisionDy = precisionY.Feed(inputY, dt, timestamp, noiseY, gainY, responsiveness, direct, steadiness, precision);
        if (!initialized || dt <= 0 || dt > TrackingTiming.MaximumGapSeconds)
        {
            initialized = true;
            heldX = filteredX = x.AcceptedInput;
            heldY = filteredY = y.AcceptedInput;
            restX.Reset(); restY.Reset();
            stability.Reset();
            Stopped = StoppedX = StoppedY = true;
            return (0, 0);
        }

        double cutoff = Math.Max(x.Cutoff, y.Cutoff);
        double alpha = 1 / (1 + 1 / (2 * Math.PI * cutoff * dt));
        filteredX += alpha * (x.AcceptedInput - filteredX);
        filteredY += alpha * (y.AcceptedInput - filteredY);
        x.Synchronize(filteredX); y.Synchronize(filteredY);

        bool quietX = restX.Feed(x.AcceptedInput, filteredX, noiseX, timestamp, dt);
        bool quietY = restY.Feed(y.AcceptedInput, filteredY, noiseY, timestamp, dt);
        Stopped = quietX && quietY;
        if (Stopped)
        {
            StoppedX = StoppedY = true;
            x.Settle(); y.Settle();
            heldX = filteredX = x.AcceptedInput;
            heldY = filteredY = y.AcceptedInput;
            stability.Reset();
            return (0, 0); // discard filter remainder, never replay it later
        }

        double dx = (filteredX - heldX) * gainX;
        double dy = (filteredY - heldY) * gainY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double projectedNoise = Math.Max(noiseX * gainX, noiseY * gainY);
        double band = Math.Min(2, projectedNoise * .4) * Math.Clamp(steadiness, 0, 1);
        band = Math.Min(6, band * Math.Clamp(precision, .1, 2) / .75);
        band *= 1 - .85 * MotionConfidence;
        double portion = distance > band && distance > 1e-12 ? (distance - band) / distance : 0;
        dx *= portion; dy *= portion;
        heldX += dx / gainX; heldY += dy / gainY;
        // Keep the common vector response for both components, including a
        // shallow diagonal; per-axis noise evidence is applied by stability.
        static double Blend(double confidence)
        {
            double u = Math.Clamp((confidence - .1) / .15, 0, 1);
            return u * u * (3 - 2 * u);
        }
        double blend = Blend(MotionConfidence);
        StoppedX = blend <= 1e-12 && precisionX.Stopped;
        StoppedY = blend <= 1e-12 && precisionY.Stopped;
        var output = stability.Feed(precisionDx + (dx - precisionDx) * blend,
            precisionDy + (dy - precisionDy) * blend, dt, StoppedX, StoppedY,
            MotionConfidence, x.MotionConfidence, y.MotionConfidence,
            noiseX * gainX, noiseY * gainY, steadiness, precision);
        if (stability.Holding) StoppedX = StoppedY = true;
        return output;
    }
}
