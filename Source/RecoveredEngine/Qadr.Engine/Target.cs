namespace Qadr.Engine;

internal sealed record Target(string Process, string Window, ElementKey[] Path, double X, double Y);
