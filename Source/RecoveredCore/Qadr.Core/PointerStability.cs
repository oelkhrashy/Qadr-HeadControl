using System;

namespace Qadr.Core;

/// <summary>
/// A short capture-timed vector low-pass and bounded per-coordinate noise bands.
/// Coordinates have independent noise evidence; neither axis is selected or snapped.
/// Confirmed stops discard pending motion rather than allowing a tail.
/// </summary>
internal sealed class PointerStability
{
    private double targetX, targetY, filteredX, filteredY, emittedX, emittedY;
    public bool Holding { get; private set; }

    public void Reset()
    {
        targetX = targetY = filteredX = filteredY = emittedX = emittedY = 0;
        Holding = true;
    }

    public (double X, double Y) Feed(double dx, double dy, double dt,
        bool stopX, bool stopY, double confidence, double confidenceX, double confidenceY,
        double projectedNoiseX, double projectedNoiseY,
        double steadiness, double precision)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dt)
            || dt <= 0 || dt > TrackingTiming.MaximumGapSeconds)
        {
            Reset(); return (0, 0);
        }
        double strength = Math.Clamp(steadiness, 0, 1);
        if (strength == 0)
        {
            Reset(); Holding = false;
            return (dx, dy);
        }
        if (stopX) targetX = filteredX = emittedX;
        else targetX += dx;
        if (stopY) targetY = filteredY = emittedY;
        else targetY += dy;

        // Strong deliberate changes need less lag so a confirmed stop does not
        // discard a large part of a short correction. Use one time constant
        // for the whole vector, including during a turn.
        double intent = Math.Clamp(confidence, 0, 1);
        double tau = (.045 - .025 * intent) * strength / .75;
        double alpha = tau > 0 ? 1 - Math.Exp(-dt / tau) : 1;
        filteredX += alpha * (targetX - filteredX);
        filteredY += alpha * (targetY - filteredY);
        double x = filteredX - emittedX, y = filteredY - emittedY;
        double Band(double noise, double axisConfidence)
        {
            double band = Math.Min(3, Math.Max(0, noise) * .2) * strength;
            band *= Math.Clamp(precision, .1, 2) / .75;
            return Math.Min(6, band) * (1 - .97 * Math.Clamp(axisConfidence, 0, 1));
        }
        // Horizontal intent must not open the vertical noise band (or vice
        // versa). Bands follow the last emitted coordinate, not a chosen axis
        // or a fixed straight line, and remain bounded during slow diagonals.
        double bandX = Band(projectedNoiseX, confidenceX);
        double bandY = Band(projectedNoiseY, confidenceY);
        // Retain the preceding radial envelope as well. Coordinate suppression
        // may remove additional cross-axis noise, but must not open a smaller
        // resting band than the previous stabilizer on either component.
        double radialBand = Band(Math.Max(projectedNoiseX, projectedNoiseY), intent);
        double distance = Math.Sqrt(x * x + y * y);
        double radialPortion = distance > radialBand && distance > 1e-12 ? (distance - radialBand) / distance : 0;
        x = Math.CopySign(Math.Min(Math.Max(0, Math.Abs(x) - bandX), Math.Abs(x) * radialPortion), x);
        y = Math.CopySign(Math.Min(Math.Max(0, Math.Abs(y) - bandY), Math.Abs(y) * radialPortion), y);
        emittedX += x; emittedY += y;
        Holding = x == 0 && y == 0 && confidence <= .1;
        return (x, y);
    }
}
