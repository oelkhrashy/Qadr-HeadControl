namespace Qadr.Core;

/// <summary>Measured collection evidence; absent on legacy profiles.</summary>
public sealed record CalibrationDetails
{
    public int Version { get; init; } = 1;
    public double[] RestRms { get; init; } = [];
    public double[] RestP95 { get; init; } = [];
    public double[][] MeasuredRanges { get; init; } = [];
    public int[] DirectionSamples { get; init; } = [];
    // Smaller/larger measured range across repeated passes; zero if unavailable.
    public double[] RepeatConsistency { get; init; } = [];
    public string[] UnmeasuredDirections { get; init; } = [];
    public int RestSamples { get; init; }
    public int MovementSamples { get; init; }
    public double CaptureSeconds { get; init; }
}
