namespace Qadr.Core;

public sealed record PointerOutput(double Dx, double Dy, string? Click, double ClickX, double ClickY, double Dwell, bool DistanceChanged = false, bool Frozen = false, bool IsRate = false, double VelocityX = 0.0, double VelocityY = 0.0, bool StopX = false, bool StopY = false, bool IsAbsolute = false, double AbsoluteX = 0.0, double AbsoluteY = 0.0);
