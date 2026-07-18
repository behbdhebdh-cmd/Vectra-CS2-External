namespace Vectra.External;

public static class SnapshotTiming
{
    public static readonly TimeSpan OverlayGrace = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan SlowCaptureThreshold = TimeSpan.FromMilliseconds(50);
    public static bool IsSlowCapture(TimeSpan duration) => duration > SlowCaptureThreshold;

    public static bool IsWithinGrace(DateTimeOffset timestamp, DateTimeOffset now, TimeSpan grace)
    {
        if (timestamp > now) return true;
        return now - timestamp <= grace;
    }

    public static GameSnapshot KeepLastValid(GameSnapshot current, GameSnapshot candidate) => candidate.Valid || !current.Valid ? candidate : current;
}

public sealed class SnapshotGapTracker
{
    public DateTimeOffset? StartedAt { get; private set; }
    public bool IsActive => StartedAt.HasValue;

    public bool BeginOrContinue(DateTimeOffset now)
    {
        StartedAt ??= now;
        return SnapshotTiming.IsWithinGrace(StartedAt.Value, now, SnapshotTiming.OverlayGrace);
    }

    public void Clear() => StartedAt = null;
}
