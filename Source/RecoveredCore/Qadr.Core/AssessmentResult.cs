namespace Qadr.Core;

public sealed record AssessmentResult(string Id, string ExpectedAction, bool Completed, string Reason, double DurationMs, double? FirstEntryMs, double PathLengthPx, double PathEfficiency, int Entries, int Overshoots, double MaximumOvershootPx, double? FinalCentreErrorPx, double? InsideJitterRmsPx, int ClickAttempts, int WrongActionAttempts, int TrackingLosses, long Samples);
