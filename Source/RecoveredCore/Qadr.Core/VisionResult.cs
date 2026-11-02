using System.Collections.Generic;

namespace Qadr.Core;

public sealed record VisionResult(int Version, string Type, long Sequence = 0L, double Captured = 0.0, double[][]? Landmarks = null, Dictionary<string, double>? Blendshapes = null, double[]? Matrix = null, string? Preview = null, double InferenceMs = 0.0, string? Text = null, FaceGeometry? Geometry = null, int? UserStopEpoch = null, long? VoiceRevision = null);
