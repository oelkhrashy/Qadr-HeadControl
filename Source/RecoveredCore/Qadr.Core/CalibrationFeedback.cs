namespace Qadr.Core;

public sealed record CalibrationFeedback(string Title, string Help, double Progress, double X = 0.0, double Y = 0.0, Profile? Profile = null);
