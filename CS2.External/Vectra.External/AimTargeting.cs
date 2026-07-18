namespace Vectra.External;

public static class AimTargeting
{
    public static bool IsEligible(PlayerSnapshot player, GameSnapshot snapshot, ClientSettings settings) =>
        !player.IsLocal && player.Alive && !player.Dormant &&
        (!settings.TeamCheckEnabled || player.Team != snapshot.LocalTeam) &&
        (!settings.AimVisibilityCheckEnabled || player.HasVisibilityData && player.IsVisible);

    internal static AimSelectionResult Select(
        GameSnapshot snapshot,
        int width,
        int height,
        ClientSettings settings,
        IReadOnlyDictionary<uint, DateTimeOffset> visibleSince,
        DateTimeOffset now,
        uint lockedTarget)
    {
        AimTargetCandidate? locked = null; AimTargetCandidate? best = null;
        var total = 0; var statusRejected = 0; var teamRejected = 0; var missingVisibility = 0; var occluded = 0; var projectionRejected = 0; var outsideFov = 0; var eligible = 0;
        var effectivePriority = !settings.AimVisibilityCheckEnabled && settings.AimPriority == AimPriority.MostVisible ? AimPriority.Crosshair : settings.AimPriority;
        foreach (var player in snapshot.Players) {
            total++;
            if (player.IsLocal || !player.Alive || player.Dormant) { statusRejected++; continue; }
            if (settings.TeamCheckEnabled && player.Team == snapshot.LocalTeam) { teamRejected++; continue; }
            if (settings.AimVisibilityCheckEnabled && !player.HasVisibilityData) { missingVisibility++; continue; }
            if (settings.AimVisibilityCheckEnabled && !player.IsVisible) { occluded++; continue; }
            var worldPoint = AimAssistMath.TargetPoint(player, settings.AimTargetPoint);
            if (!WorldProjection.TryProject(worldPoint, snapshot.ViewMatrix, width, height, out var point, true)) { projectionRejected++; continue; }
            var dx = point.X - width / 2f; var dy = point.Y - height / 2f; var screenDistance = MathF.Sqrt(dx * dx + dy * dy);
            if (screenDistance > settings.AimAssistFovPixels) { outsideFov++; continue; }
            eligible++;
            var visibleDuration = visibleSince.TryGetValue(player.PawnEntityIndex, out var since) ? now - since : TimeSpan.Zero;
            var candidate = new AimTargetCandidate(player.PawnEntityIndex, point, screenDistance, player.Distance, visibleDuration);
            if (candidate.EntityIndex == lockedTarget) locked = candidate;
            if (best is null || IsBetter(candidate, best.Value, effectivePriority)) best = candidate;
        }
        return new(locked ?? best, new(total, statusRejected, teamRejected, missingVisibility, occluded, projectionRejected, outsideFov, eligible));
    }

    private static bool IsBetter(AimTargetCandidate candidate, AimTargetCandidate current, AimPriority priority)
    {
        var comparison = priority switch {
            AimPriority.Closest => Compare(candidate.WorldDistance, current.WorldDistance),
            AimPriority.MostVisible => -Compare(candidate.VisibleDuration.TotalMilliseconds, current.VisibleDuration.TotalMilliseconds),
            _ => Compare(candidate.ScreenDistance, current.ScreenDistance)
        };
        if (comparison == 0 && priority != AimPriority.Crosshair) comparison = Compare(candidate.ScreenDistance, current.ScreenDistance);
        return comparison < 0 || (comparison == 0 && candidate.EntityIndex < current.EntityIndex);
    }

    private static int Compare(double left, double right) => Math.Abs(left - right) <= .001 ? 0 : left < right ? -1 : 1;
}

internal readonly record struct AimTargetCandidate(uint EntityIndex, ScreenPoint Point, float ScreenDistance, float WorldDistance, TimeSpan VisibleDuration);
internal readonly record struct AimSelectionResult(AimTargetCandidate? Candidate, AimCandidateDiagnostics Diagnostics);

internal sealed class AimVisibilityHistory
{
    private readonly Dictionary<uint, DateTimeOffset> _visibleSince = new();
    public IReadOnlyDictionary<uint, DateTimeOffset> VisibleSince => _visibleSince;

    public void Update(GameSnapshot snapshot, DateTimeOffset now)
    {
        var visible = snapshot.Players.Where(player => !player.IsLocal && player.Alive && !player.Dormant && player.HasVisibilityData && player.IsVisible).Select(player => player.PawnEntityIndex).ToHashSet();
        foreach (var entity in visible) _visibleSince.TryAdd(entity, now);
        foreach (var entity in _visibleSince.Keys.Where(entity => !visible.Contains(entity)).ToArray()) _visibleSince.Remove(entity);
    }

    public void Clear() => _visibleSince.Clear();
}
