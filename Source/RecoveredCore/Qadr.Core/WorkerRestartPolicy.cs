namespace Qadr.Core;

/// <summary>
/// How often the face-tracking worker may be restarted automatically. It was
/// once per session: the second stall said "close Qadr and open it again",
/// which a hands-free user cannot do. Now up to <see cref="MaxRestarts"/>
/// within <see cref="WindowSeconds"/>, at least <see cref="MinGapSeconds"/>
/// apart; a worker that keeps failing still ends in the stopped state.
/// </summary>
public sealed class WorkerRestartPolicy
{
    public int MaxRestarts { get; init; } = 3;
    public double WindowSeconds { get; init; } = 600;
    public double MinGapSeconds { get; init; } = 5;
    private readonly Queue<double> recent = new();

    public bool Allow(double now)
    {
        while (recent.Count > 0 && now - recent.Peek() > WindowSeconds) recent.Dequeue();
        if (recent.Count >= MaxRestarts) return false;
        if (recent.Count > 0 && now - recent.Last() < MinGapSeconds) return false;
        recent.Enqueue(now);
        return true;
    }
}
