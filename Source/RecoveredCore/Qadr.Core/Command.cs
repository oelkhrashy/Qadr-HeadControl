namespace Qadr.Core;

public sealed record Command(string Type, string? CameraId = null, Settings? Settings = null, string? Name = null, bool Enabled = false, int Version = 1, MouseZone[]? Zones = null, int? UserStopEpoch = null, VisionResult? DiagnosticResult = null, string? Action = null, double? InputX = null, double? InputY = null);
