using System;

namespace Qadr.Core;

/// <summary>One click per deliberate expression; stable neutral is required to rearm.</summary>
public sealed class Gesture(double hold = .34)
{
    private double? previous, neutralSince, started, released;
    private double firedAt = double.NegativeInfinity;
    private bool armed, latched;
    public bool Active { get; private set; }
    public double Progress { get; private set; }

    private void Clear()
    {
        previous = neutralSince = started = released = null;
        armed = latched = Active = false;
        Progress = 0;
    }

    public bool Feed(double value, double threshold, double timestamp, double rest = 0,
        double releaseRatio = .7, double cooldown = .8)
    {
        if (!double.IsFinite(value) || !double.IsFinite(timestamp) || !double.IsFinite(threshold)
            || !double.IsFinite(rest) || !double.IsFinite(releaseRatio) || !double.IsFinite(cooldown)
            || threshold <= rest || threshold > 1 || hold <= 0)
        { Clear(); return false; }
        if (previous.HasValue && timestamp <= previous.Value) return false;
        if (previous.HasValue && timestamp - previous.Value > TrackingTiming.MaximumGapSeconds) Clear();
        previous = timestamp;
        double release = rest + (threshold - rest) * Math.Clamp(releaseRatio, .1, .95);
        if (!armed)
        {
            if (value < release)
            {
                neutralSince ??= timestamp;
                if (timestamp - neutralSince >= .08) armed = true;
            }
            else neutralSince = null;
            return false;
        }
        if (value < release)
        {
            released ??= timestamp;
            if (timestamp - released >= .08)
            {
                started = null;
                latched = Active = false;
                Progress = 0;
            }
            return false;
        }
        released = null;
        if (!started.HasValue)
        {
            if (value < threshold) return false;
            started = timestamp;
        }
        Active = true;
        if (latched) return false;
        Progress = Math.Clamp((timestamp - started.Value) / hold, 0, 1);
        if (Progress < 1 || timestamp - firedAt < cooldown) return false;
        latched = true;
        firedAt = timestamp;
        return true;
    }
}
