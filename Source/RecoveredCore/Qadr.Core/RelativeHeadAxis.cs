using System;

namespace Qadr.Core;

/// <summary>
/// Relative displacement without a neutral-position dead zone or centre attraction.
/// Noise filtering precedes bounded hysteresis. Confirmed stops discard remainder;
/// rejected movement is never replayed. Timing uses camera capture timestamps.
/// </summary>
internal sealed class RelativeHeadAxis
{
    private readonly AdaptiveAxisFilter filter = new();
    private readonly RelativeRest rest = new();
    private bool initialized;
    private double held;
    public double Position { get; private set; }
    public bool Stopped { get; private set; }
    public double MotionConfidence => filter.MotionConfidence;

    public double Feed(double input, double dt, double timestamp, double noise,
        double pixelsPerUnit, double responsiveness, bool direct, double steadiness, double precisionMultiplier)
    {
        double filtered = filter.Feed(input, dt, noise, responsiveness, direct, relative: true);
        Position = filtered;
        if (!initialized || dt <= 0 || dt > TrackingTiming.MaximumGapSeconds)
        {
            initialized = true;
            held = filtered;
            rest.Reset();
            Stopped = true;
            return 0;
        }
        Stopped = rest.Feed(filter.AcceptedInput, filtered, noise, timestamp, dt);
        if (Stopped)
        {
            filter.Settle();
            held = Position = filter.AcceptedInput;
            return 0;
        }
        // Base hysteresis is at most two pixels, reduced during deliberate travel.
        // The band follows the last position: fine movement works at any angle.
        double gain = Math.Max(1, pixelsPerUnit);
        double band = Math.Min(noise * .4, 2 / gain) * Math.Clamp(steadiness, 0, 1);
        // Honour the precision control used by the existing measured startup tuner.
        // The established 0.75 setting retains its exact previous response.
        band *= Math.Clamp(precisionMultiplier, .1, 2) / .75;
        band = Math.Min(band, 6 / gain);
        band *= 1 - .85 * filter.MotionConfidence;
        double previous = held;
        if (Math.Abs(filtered - held) > band)
            held = filtered - Math.CopySign(band, filtered - held);
        Position = held;
        return (held - previous) * pixelsPerUnit;
    }
}
