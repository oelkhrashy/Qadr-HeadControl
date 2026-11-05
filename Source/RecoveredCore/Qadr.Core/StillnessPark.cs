namespace Qadr.Core;

/// <summary>
/// Holds the relative pointer still while the head is "still", and only
/// while it is still. Ported from HeadMouse (headmouse/stabilizer.py).
///
/// Why: measured on a held-still head, most of what moves is not tracking
/// noise but the person: slow posture sway and breathing, 0.5-1.1 deg over
/// 30 s, 9-15x the face model's own noise. Relative mapping integrates it,
/// so the cursor drifts (63-81 px/s in still moments on a live Qadr run).
/// Sway is slow and a deliberate move is not, so the decision is by SPEED,
/// with thresholds from this person's own calibration.
///
/// Parks after <see cref="ParkSeconds"/> slower than half the release speed.
/// Releases on movement above <see cref="ReleaseSpeed"/> sustained for
/// <see cref="SustainSeconds"/>, on one clearly fast frame (twice the release
/// speed), or on drift beyond <see cref="DriftLimit"/> from where it parked.
/// While parked the output is frozen; on release it continues from where it
/// was frozen, so the ignored sway is dropped, never jumped to.
///
/// Units are the controller's normalized signals (signal / range per axis),
/// so turn and shift modes behave alike.
/// </summary>
public sealed class StillnessPark
{
    public double[] ReleaseSpeed { get; private set; } = [.08, .08];
    public double[] DriftLimit { get; private set; } = [.04, .04];
    public double ParkSeconds { get; init; } = .3;
    public double SustainSeconds { get; init; } = .12;
    public bool Parked { get; private set; }

    private readonly Queue<(double T, double X, double Y)> history = new();
    private (double T, double X, double Y)? previous;
    private double? slowSince, overSince;
    private double anchorX, anchorY, offsetX, offsetY, outX, outY;
    private bool started;

    /// <summary>Thresholds from calibration, with safe defaults for
    /// profiles calibrated before these measurements existed.</summary>
    public void Configure(Profile p)
    {
        var d = p.CalibrationDetails;
        for (int i = 0; i < 2; i++)
        {
            double range = Math.Max(1e-6, (p.Ranges[i][0] + p.Ranges[i][1]) / 2);
            double speed = d is { RestSpeedP95.Length: 2 } ? d.RestSpeedP95[i] : 0;
            double sway = d is { RestSway.Length: 2 } ? d.RestSway[i] : 3 * p.Noise[i] / range;
            ReleaseSpeed[i] = Math.Max(.06, 1.5 * speed);
            DriftLimit[i] = Math.Max(.03, 2 * sway);
        }
    }

    public void Reset()
    {
        history.Clear();
        previous = null;
        slowSince = overSince = null;
        Parked = started = false;
        offsetX = offsetY = 0;
    }

    public (double X, double Y) Feed(double t, double x, double y)
    {
        if (!double.IsFinite(t) || !double.IsFinite(x) || !double.IsFinite(y)) return (x, y);
        if (!started || (previous.HasValue && (t <= previous.Value.T || t - previous.Value.T > TrackingTiming.MaximumGapSeconds)))
        {
            Reset();
            started = true;
            previous = (t, x, y);
            history.Enqueue((t, x, y));
            outX = x; outY = y;
            return (x, y);
        }
        var prev = previous!.Value;
        double fastX = Math.Abs(x - prev.X) / (t - prev.T), fastY = Math.Abs(y - prev.Y) / (t - prev.T);
        previous = (t, x, y);
        history.Enqueue((t, x, y));
        while (history.Count > 2 && t - history.Peek().T > .21) history.Dequeue();
        var first = history.Peek();
        double span = Math.Max(t - first.T, 1e-6);
        double vx = Math.Abs(x - first.X) / span, vy = Math.Abs(y - first.Y) / span;
        bool over = vx > ReleaseSpeed[0] || vy > ReleaseSpeed[1];
        bool fast = fastX > 2 * ReleaseSpeed[0] || fastY > 2 * ReleaseSpeed[1];

        if (Parked)
        {
            overSince = over ? overSince ?? t : null;
            bool sustained = fast || (overSince.HasValue && t - overSince.Value >= SustainSeconds);
            bool drifted = Math.Abs(x - anchorX) > DriftLimit[0] || Math.Abs(y - anchorY) > DriftLimit[1];
            if (!sustained && !drifted) return (outX, outY);
            Parked = false;
            overSince = slowSince = null;
            // Measured from the frame BEFORE the release: the sway ignored
            // while parked is dropped, but this frame's own movement (the
            // start of a real move, or a one-frame glitch for the spike
            // filter downstream to reject) passes through and leaves no
            // lasting offset behind.
            offsetX = outX - prev.X;
            offsetY = outY - prev.Y;
        }
        outX = x + offsetX;
        outY = y + offsetY;
        bool slow = vx < ReleaseSpeed[0] / 2 && vy < ReleaseSpeed[1] / 2;
        if (slow)
        {
            slowSince ??= t;
            if (t - slowSince.Value >= ParkSeconds)
            {
                Parked = true;
                anchorX = x;
                anchorY = y;
                overSince = null;
            }
        }
        else slowSince = null;
        return (outX, outY);
    }
}
