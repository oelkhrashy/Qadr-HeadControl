namespace Qadr.Core;

public sealed record ClickLessonFeedback(string Phase, double Progress, double? Threshold = null, double RestLevel = 0.0);
