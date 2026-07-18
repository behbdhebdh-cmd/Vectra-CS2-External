using System.Globalization;

namespace Vectra.External;

public static class BombEsp
{
    public static BombState ResolvePlantedState(bool activated, bool ticking, bool exploded, bool defused, bool validOrigin)
        => activated && ticking && !exploded && !defused && validOrigin ? BombState.Planted : BombState.Unavailable;

    public static BombState ResolveWeaponState(bool bombPlanted, uint ownerHandle, bool validOrigin)
    {
        if (bombPlanted || !validOrigin) return BombState.Unavailable;
        return IsValidHandle(ownerHandle) ? BombState.Carried : BombState.Dropped;
    }

    public static float? RemainingSeconds(float endTime, float currentTime, float maximum)
    {
        if (!float.IsFinite(endTime) || !float.IsFinite(currentTime) || !float.IsFinite(maximum) || maximum <= 0 || maximum > 90) return null;
        var remaining = endTime - currentTime;
        if (remaining < -.25f || remaining > maximum + 1) return null;
        return Math.Clamp(remaining, 0, maximum);
    }

    public static string SiteLabel(int site) => site switch { 0 => "A", 1 => "B", _ => "?" };

    public static string Label(BombSnapshot bomb, string? carrierName = null)
    {
        var distance = $"{bomb.Distance.ToString("F0", CultureInfo.InvariantCulture)}u";
        return bomb.State switch
        {
            BombState.Carried => string.IsNullOrWhiteSpace(carrierName) ? $"C4 CARRIED · {distance}" : $"C4 CARRIED · {TrimName(carrierName)} · {distance}",
            BombState.Dropped => $"C4 DROPPED · {distance}",
            BombState.Planted => $"C4 · SITE {SiteLabel(bomb.BombSite)}" + (bomb.ExplosionRemainingSeconds is float seconds ? $" · {seconds.ToString("F1", CultureInfo.InvariantCulture)}s" : string.Empty),
            _ => string.Empty
        };
    }

    public static string DefuseLabel(BombSnapshot bomb)
        => bomb.BeingDefused
            ? "DEFUSE" + (bomb.DefuseRemainingSeconds is float seconds ? $" · {seconds.ToString("F1", CultureInfo.InvariantCulture)}s" : string.Empty)
            : string.Empty;

    public static bool IsValidHandle(uint handle) => handle is not 0 and not 0xFFFFFFFF;

    private static string TrimName(string value) => value.Length > 18 ? value[..15] + "..." : value;
}
