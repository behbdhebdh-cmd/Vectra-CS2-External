using System.Text.Json;
using System.IO;

namespace Vectra.External;

public sealed record ConfigOperationResult(bool Success, string Message);

public sealed class ClientConfigStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ClientConfigStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vectra External",
            "client-config.json");
    }

    public string Path { get; }

    public ConfigOperationResult Save(ClientSettings settings)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (string.IsNullOrWhiteSpace(directory)) return new(false, "Configuration path is invalid.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ConfigDocument.From(settings), JsonOptions));
            File.Move(temporaryPath, Path, true);
            return new(true, "Configuration saved locally.");
        }
        catch (Exception error)
        {
            return new(false, $"Could not save configuration ({error.GetType().Name}).");
        }
    }

    public ConfigOperationResult Load(ClientSettings settings)
    {
        if (!File.Exists(Path)) return new(false, "No saved configuration was found.");
        try
        {
            var document = JsonSerializer.Deserialize<ConfigDocument>(File.ReadAllText(Path), JsonOptions);
            if (document is null || document.SchemaVersion != SchemaVersion) return new(false, "Configuration format is unsupported.");
            document.ApplyTo(settings);
            // Private-match approval is intentionally session-only and must be confirmed again.
            settings.PrivateMatchAuthorized = false;
            return new(true, "Configuration loaded. Private-match approval was reset for this session.");
        }
        catch (Exception error)
        {
            return new(false, $"Could not load configuration ({error.GetType().Name}).");
        }
    }

    private sealed class ConfigDocument
    {
        public int SchemaVersion { get; init; } = ClientConfigStore.SchemaVersion;
        public bool MasterEnabled { get; init; }
        public bool EspEnabled { get; init; }
        public bool TeamCheckEnabled { get; init; }
        public bool TriggerEnabled { get; init; }
        public bool AimAssistEnabled { get; init; }
        public int AimAssistFovPixels { get; init; }
        public int AimAssistStrengthPercent { get; init; }
        public AimTargetPoint? AimTargetPoint { get; init; }
        public AimPriority? AimPriority { get; init; }
        public AimMovement? AimMovement { get; init; }
        public AimActivation? AimActivation { get; init; }
        public int? AimActivationKey { get; init; }
        public bool? AimVisibilityCheckEnabled { get; init; }
        public bool DrawAimFov { get; init; }
        public bool CornerBoxes { get; init; }
        public bool DrawNames { get; init; }
        public bool DrawHealth { get; init; }
        public bool DrawDistance { get; init; }
        public bool DrawWeapons { get; init; }
        public bool DrawBombEsp { get; init; }
        public bool DrawItemEsp { get; init; }
        public bool DrawGrenadePrediction { get; init; }
        public string? GrenadePredictionMap { get; init; }
        public bool DrawSnaplines { get; init; }
        public bool DrawOffscreenArrows { get; init; }
        public bool DrawRadar { get; init; }
        public bool DrawSkeleton { get; init; }
        public bool DrawHeadMarker { get; init; }
        public int EspTheme { get; init; }
        public bool HideOverlayFromCapture { get; init; }
        public double UiOpacity { get; init; }

        public static ConfigDocument From(ClientSettings settings) => new()
        {
            MasterEnabled = settings.MasterEnabled,
            EspEnabled = settings.EspEnabled,
            TeamCheckEnabled = settings.TeamCheckEnabled,
            TriggerEnabled = settings.TriggerEnabled,
            AimAssistEnabled = settings.AimAssistEnabled,
            AimAssistFovPixels = settings.AimAssistFovPixels,
            AimAssistStrengthPercent = settings.AimAssistStrengthPercent,
            AimTargetPoint = settings.AimTargetPoint,
            AimPriority = settings.AimPriority,
            AimMovement = settings.AimMovement,
            AimActivation = settings.AimActivation,
            AimActivationKey = settings.AimActivationKey,
            AimVisibilityCheckEnabled = settings.AimVisibilityCheckEnabled,
            DrawAimFov = settings.DrawAimFov,
            CornerBoxes = settings.CornerBoxes,
            DrawNames = settings.DrawNames,
            DrawHealth = settings.DrawHealth,
            DrawDistance = settings.DrawDistance,
            DrawWeapons = settings.DrawWeapons,
            DrawBombEsp = settings.DrawBombEsp,
            DrawItemEsp = settings.DrawItemEsp,
            DrawGrenadePrediction = settings.DrawGrenadePrediction,
            GrenadePredictionMap = settings.GrenadePredictionMap,
            DrawSnaplines = settings.DrawSnaplines,
            DrawOffscreenArrows = settings.DrawOffscreenArrows,
            DrawRadar = settings.DrawRadar,
            DrawSkeleton = false,
            DrawHeadMarker = settings.DrawHeadMarker,
            EspTheme = settings.EspTheme,
            HideOverlayFromCapture = settings.HideOverlayFromCapture,
            UiOpacity = settings.UiOpacity
        };

        public void ApplyTo(ClientSettings settings)
        {
            settings.MasterEnabled = MasterEnabled;
            settings.EspEnabled = EspEnabled;
            settings.TeamCheckEnabled = TeamCheckEnabled;
            settings.TriggerEnabled = TriggerEnabled;
            settings.AimAssistEnabled = AimAssistEnabled;
            settings.AimAssistFovPixels = Math.Clamp(AimAssistFovPixels, 30, 300);
            settings.AimAssistStrengthPercent = Math.Clamp(AimAssistStrengthPercent, 5, 100);
            var legacyAim = AimTargetPoint is null && AimPriority is null && AimMovement is null && AimActivation is null && AimActivationKey is null;
            if (legacyAim) {
                settings.AimTargetPoint = Vectra.External.AimTargetPoint.Chest;
                settings.AimPriority = Vectra.External.AimPriority.Crosshair;
                settings.AimMovement = Vectra.External.AimMovement.Smooth;
                settings.AimActivation = Vectra.External.AimActivation.Always;
                settings.AimActivationKey = 0x05;
            } else {
                settings.AimTargetPoint = Valid(AimTargetPoint, Vectra.External.AimTargetPoint.Head);
                settings.AimPriority = Valid(AimPriority, Vectra.External.AimPriority.Crosshair);
                settings.AimMovement = Valid(AimMovement, Vectra.External.AimMovement.Smooth);
                settings.AimActivation = Valid(AimActivation, Vectra.External.AimActivation.Hold);
                settings.AimActivationKey = AimActivationKey is >= 1 and <= 255 and not 0x1B ? AimActivationKey.Value : 0x05;
            }
            settings.AimVisibilityCheckEnabled = AimVisibilityCheckEnabled ?? true;
            settings.DrawAimFov = DrawAimFov;
            settings.CornerBoxes = CornerBoxes;
            settings.DrawNames = DrawNames;
            settings.DrawHealth = DrawHealth;
            settings.DrawDistance = DrawDistance;
            settings.DrawWeapons = DrawWeapons;
            settings.DrawBombEsp = DrawBombEsp;
            settings.DrawItemEsp = DrawItemEsp;
            settings.DrawGrenadePrediction = DrawGrenadePrediction;
            settings.GrenadePredictionMap = ValidMapName(GrenadePredictionMap);
            settings.DrawSnaplines = DrawSnaplines;
            settings.DrawOffscreenArrows = DrawOffscreenArrows;
            settings.DrawRadar = DrawRadar;
            // Skeleton ESP is temporarily disabled in the client; old profiles cannot re-enable it.
            settings.DrawSkeleton = false;
            settings.DrawHeadMarker = DrawHeadMarker;
            settings.EspTheme = Math.Clamp(EspTheme, 0, 2);
            settings.HideOverlayFromCapture = HideOverlayFromCapture;
            settings.UiOpacity = Math.Clamp(double.IsFinite(UiOpacity) ? UiOpacity : .96, .82, 1);
        }

        private static T Valid<T>(T? value, T fallback) where T : struct, Enum => value is T candidate && Enum.IsDefined(candidate) ? candidate : fallback;
        private static string ValidMapName(string? value) => MapCollisionProvider.SanitizeMap(value);
    }
}
