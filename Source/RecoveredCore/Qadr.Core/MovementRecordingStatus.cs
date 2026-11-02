namespace Qadr.Core;

public sealed record MovementRecordingStatus(bool Recording = false, bool Saving = false, string? Report = null, string? Error = null, long Dropped = 0L);
