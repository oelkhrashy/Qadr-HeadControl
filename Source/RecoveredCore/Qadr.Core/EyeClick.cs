using System;

namespace Qadr.Core;

/// <summary>Intentional eye hold, then stable reopening, generates one right-click.</summary>
public sealed class EyeClick
{
    private double? previous, neutralSince, started, reopening;
    private double held, lastClick = double.NegativeInfinity;
    private bool armed, bilateral, canceled;
    public bool Active { get; private set; }
    public double Progress { get; private set; }
    public bool Qualified { get; private set; }

    private void Clear()
    {
        previous = neutralSince = started = reopening = null;
        held = 0;
        armed = bilateral = canceled = Active = Qualified = false;
        Progress = 0;
    }

    public bool Feed(double left, double right, Settings settings, double now)
    {
        double threshold = settings.BlinkThreshold;
        if (!double.IsFinite(left) || !double.IsFinite(right) || !double.IsFinite(now)
            || !double.IsFinite(threshold) || !double.IsFinite(settings.EyeRestLevel)
            || threshold <= settings.EyeRestLevel || threshold > 1
            || settings.RightClickMethod is not ("either" or "left" or "right" or "both"))
        { Clear(); return false; }
        if (previous.HasValue && now <= previous.Value) return false;
        if (previous.HasValue && now - previous.Value > TrackingTiming.MaximumGapSeconds) Clear();
        double dt = previous.HasValue ? now - previous.Value : 0;
        previous = now;
        double release = settings.EyeRestLevel + (threshold - settings.EyeRestLevel) * settings.EyeReleaseRatio;
        bool open = Math.Max(left, right) < release;
        bool both = Math.Min(left, right) >= threshold;
        bool closed = settings.RightClickMethod switch
        {
            "left" => left >= threshold && right < release,
            "right" => right >= threshold && left < release,
            "both" => both,
            _ => Math.Max(left, right) >= threshold
        };
        if (!armed)
        {
            if (open) { neutralSince ??= now; if (now - neutralSince >= .08) armed = true; }
            else neutralSince = null;
            return false;
        }
        if (!started.HasValue)
        {
            if (!closed) return false;
            started = now;
            Active = true;
            bilateral = both;
        }
        else if (closed)
        {
            if (reopening.HasValue) canceled = true;
            held += dt;
        }
        bilateral |= both;
        if (settings.RightClickMethod is "left" or "right" && both) canceled = true;
        if (now - started.Value > 2.5) canceled = true;
        double required = bilateral ? Math.Max(.4, settings.RightClickHold) : settings.RightClickHold;
        Progress = Math.Clamp(held / Math.Max(.25, required), 0, 1);
        Qualified = !canceled && Progress >= 1;
        Active = true;
        if (!open) { reopening = null; return false; }
        reopening ??= now;
        if (now - reopening < .06) return false;
        bool fire = Qualified && now - lastClick >= settings.RightClickCooldown;
        Clear();
        previous = now;
        neutralSince = now;
        if (fire) lastClick = now;
        return fire;
    }
}
