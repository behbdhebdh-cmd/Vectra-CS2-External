namespace Vectra.External;

public readonly record struct CaptureOptions(
    bool ReadPlayers,
    bool ReadCrosshair,
    bool ReadRecoil,
    bool ReadVisibility,
    bool ReadWeapons,
    bool ReadSkeleton,
    bool ReadBomb,
    bool ReadWorldItems,
    bool ReadGrenadeThrow,
    bool ReadViewYaw)
{
    public static CaptureOptions From(ClientSettings settings)
    {
        var esp = settings.EspEnabled;
        return new CaptureOptions(
            esp || settings.AimAssistEnabled || settings.TriggerEnabled,
            settings.TriggerEnabled,
            settings.TriggerEnabled,
            settings.AimAssistEnabled && settings.AimVisibilityCheckEnabled,
            esp && settings.DrawWeapons,
            // Skeleton ESP is intentionally disabled while its Source 2 model path is under repair.
            false,
            esp && settings.DrawBombEsp,
            esp && settings.DrawItemEsp,
            settings.DrawGrenadePrediction,
            esp && settings.DrawRadar);
    }

    public bool HasActiveFeature => ReadPlayers || ReadBomb || ReadWorldItems || ReadGrenadeThrow || ReadViewYaw;
}

public static class RuntimeCadence
{
    public static TimeSpan Select(bool attached, bool foreground, ClientSettings settings)
    {
        if (!attached) return TimeSpan.FromMilliseconds(500);
        if (!foreground) return TimeSpan.FromMilliseconds(1000d / 15d);
        if (settings.AimAssistEnabled || settings.TriggerEnabled) return TimeSpan.FromMilliseconds(1000d / 120d);
        if (settings.EspEnabled) return TimeSpan.FromMilliseconds(1000d / 60d);
        return TimeSpan.FromMilliseconds(50);
    }
}

public static class OverlayCadence
{
    public static TimeSpan Select(bool ready, bool foreground)
        => !ready ? TimeSpan.FromMilliseconds(250) : foreground ? TimeSpan.FromMilliseconds(1000d / 30d) : TimeSpan.FromMilliseconds(1000d / 15d);
}

public static class OverlayUpdatePolicy
{
    public static bool ShouldUpdate(bool visualChange, long previousTick, long currentTick, TimeSpan maintenanceInterval)
        => visualChange || previousTick == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(previousTick, currentTick) >= maintenanceInterval;
}

public static class CaptureVisibilityPresenter
{
    public static string Status(bool requested, bool applied, bool affinityAvailable)
        => requested ? applied ? "Streamproof active" : "Streamproof unavailable"
            : affinityAvailable ? "Visible to screen capture" : "Screen-capture visibility pending";
}

public static class PlayerReadPlan
{
    public static bool NeedsDetailedState(bool alive, bool dormant, bool isLocal) => alive && !dormant && !isLocal;
}
