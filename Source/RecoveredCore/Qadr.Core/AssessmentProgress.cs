namespace Qadr.Core;

public sealed record AssessmentProgress(AssessmentTarget Target, double InsideProgress, double ElapsedSeconds, bool ConfirmingSingleClick = false);
