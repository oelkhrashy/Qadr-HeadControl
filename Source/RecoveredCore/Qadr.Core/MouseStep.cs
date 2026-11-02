namespace Qadr.Core;

public sealed record MouseStep(string Kind, string Button = "left", int Amount = 0, double Delay = 0.0);
