namespace Qadr.Engine;

internal sealed record RoutineStep(string Kind, Target Target, double Delay, string? Value = null, string Button = "left");
