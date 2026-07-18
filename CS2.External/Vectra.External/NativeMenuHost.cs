using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Vectra.External;

internal enum NativeBoolSetting
{
    MasterEnabled, PrivateMatchAuthorized, EspEnabled, TeamCheckEnabled,
    TriggerEnabled, AimAssistEnabled, AimVisibilityCheckEnabled, DrawAimFov,
    CornerBoxes, DrawNames, DrawHealth, DrawDistance, DrawWeapons,
    DrawBombEsp, DrawItemEsp, DrawGrenadePrediction, DrawSnaplines,
    DrawOffscreenArrows, DrawRadar, DrawHeadMarker, HideOverlayFromCapture
}

internal enum NativeIntSetting
{
    AimAssistFovPixels, AimAssistStrengthPercent, AimTargetPoint, AimPriority,
    AimMovement, AimActivation, AimActivationKey, EspTheme
}

internal enum NativeDoubleSetting { UiOpacity }
internal enum NativeStringSetting { GrenadePredictionMap }
internal enum NativeMenuCommand { SaveConfiguration = 1, LoadConfiguration = 2, Shutdown = 3 }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct NativeMenuState
{
    public int ApiVersion;
    public int MasterEnabled, PrivateMatchAuthorized, EspEnabled, TeamCheckEnabled;
    public int TriggerEnabled, AimAssistEnabled, AimVisibilityCheckEnabled, DrawAimFov;
    public int CornerBoxes, DrawNames, DrawHealth, DrawDistance, DrawWeapons;
    public int DrawBombEsp, DrawItemEsp, DrawGrenadePrediction, DrawSnaplines;
    public int DrawOffscreenArrows, DrawRadar, DrawHeadMarker, HideOverlayFromCapture;
    public int AimAssistFovPixels, AimAssistStrengthPercent, AimTargetPoint, AimPriority;
    public int AimMovement, AimActivation, AimActivationKey, EspTheme;
    public double UiOpacity;
    public int MapCount;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Build;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string StatusTitle;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string StatusDetail;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string GrenadeStatus;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string Diagnostics;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ConfigMessage;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string CurrentMap;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24 * 32, ArraySubType = UnmanagedType.I1)] public byte[] Maps;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct NativeCommandResult
{
    public int Success;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Message;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeGetState(ref NativeMenuState state);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeSetBool(int setting, int value);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeSetInt(int setting, int value);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeSetDouble(int setting, double value);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeSetString(int setting, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NativeExecuteCommand(int command, ref NativeCommandResult result);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMenuApi
{
    public int ApiVersion;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeGetState GetState;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeSetBool SetBool;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeSetInt SetInt;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeSetDouble SetDouble;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeSetString SetString;
    [MarshalAs(UnmanagedType.FunctionPtr)] public NativeExecuteCommand ExecuteCommand;
}

internal sealed class NativeMenuHost
{
    internal const int ApiVersion = 2;
    private const int MaximumMaps = 24;
    private const int MapNameBytes = 32;
    private readonly ExternalRuntime _runtime;
    private readonly OverlayWindow _overlay;
    private readonly ClientConfigStore _configStore;
    private readonly string _version;
    private readonly OffsetBuildInfo _offsetInfo;
    private string _configMessage = "No configuration loaded this session.";
    private readonly NativeGetState _getState;
    private readonly NativeSetBool _setBool;
    private readonly NativeSetInt _setInt;
    private readonly NativeSetDouble _setDouble;
    private readonly NativeSetString _setString;
    private readonly NativeExecuteCommand _executeCommand;

    public NativeMenuHost(ExternalRuntime runtime, OverlayWindow overlay, string version, OffsetBuildInfo offsetInfo, ClientConfigStore? configStore = null)
    {
        _runtime = runtime;
        _overlay = overlay;
        _version = version;
        _offsetInfo = offsetInfo;
        _configStore = configStore ?? new ClientConfigStore();
        _getState = FillState;
        _setBool = SetBool;
        _setInt = SetInt;
        _setDouble = SetDouble;
        _setString = SetString;
        _executeCommand = ExecuteCommand;
    }

    public int Run()
    {
        var api = new NativeMenuApi
        {
            ApiVersion = ApiVersion,
            GetState = _getState,
            SetBool = _setBool,
            SetInt = _setInt,
            SetDouble = _setDouble,
            SetString = _setString,
            ExecuteCommand = _executeCommand
        };
        return RunVectraMenu(ref api);
    }

    internal void FillState(ref NativeMenuState destination)
    {
        try
        {
            var settings = _runtime.Settings;
            var snapshot = _runtime.Latest;
            var report = _runtime.Session.Report;
            var overlayReport = _overlay.Report;
            var status = ClientStatusPresenter.Create(report, overlayReport);
            var grenade = _runtime.GrenadePredictionReport;
            var maps = new[] { "Auto" }.Concat(_runtime.AvailableGrenadeMaps).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumMaps).ToArray();
            destination = new NativeMenuState
            {
                ApiVersion = ApiVersion,
                MasterEnabled = Flag(settings.MasterEnabled), PrivateMatchAuthorized = Flag(settings.PrivateMatchAuthorized),
                EspEnabled = Flag(settings.EspEnabled), TeamCheckEnabled = Flag(settings.TeamCheckEnabled),
                TriggerEnabled = Flag(settings.TriggerEnabled), AimAssistEnabled = Flag(settings.AimAssistEnabled),
                AimVisibilityCheckEnabled = Flag(settings.AimVisibilityCheckEnabled), DrawAimFov = Flag(settings.DrawAimFov),
                CornerBoxes = Flag(settings.CornerBoxes), DrawNames = Flag(settings.DrawNames), DrawHealth = Flag(settings.DrawHealth), DrawDistance = Flag(settings.DrawDistance),
                DrawWeapons = Flag(settings.DrawWeapons), DrawBombEsp = Flag(settings.DrawBombEsp), DrawItemEsp = Flag(settings.DrawItemEsp),
                DrawGrenadePrediction = Flag(settings.DrawGrenadePrediction), DrawSnaplines = Flag(settings.DrawSnaplines), DrawOffscreenArrows = Flag(settings.DrawOffscreenArrows),
                DrawRadar = Flag(settings.DrawRadar), DrawHeadMarker = Flag(settings.DrawHeadMarker), HideOverlayFromCapture = Flag(settings.HideOverlayFromCapture),
                AimAssistFovPixels = settings.AimAssistFovPixels, AimAssistStrengthPercent = settings.AimAssistStrengthPercent,
                AimTargetPoint = (int)settings.AimTargetPoint, AimPriority = (int)settings.AimPriority, AimMovement = (int)settings.AimMovement,
                AimActivation = (int)settings.AimActivation, AimActivationKey = settings.AimActivationKey, EspTheme = settings.EspTheme,
                UiOpacity = settings.UiOpacity, MapCount = maps.Length, Version = _version, Build = _offsetInfo.DisplayBuild,
                StatusTitle = status.Title, StatusDetail = status.Detail,
                GrenadeStatus = $"{GrenadeStatus(grenade.Status)} · {grenade.MapName}. {grenade.Message}",
                Diagnostics = Diagnostics(report, overlayReport, snapshot), ConfigMessage = _configMessage,
                CurrentMap = settings.GrenadePredictionMap, Maps = EncodeMaps(maps)
            };
        }
        catch (Exception error)
        {
            destination.ApiVersion = ApiVersion;
            destination.StatusTitle = "MENU HOST ERROR";
            destination.StatusDetail = error.GetType().Name;
            destination.Maps ??= new byte[MaximumMaps * MapNameBytes];
        }
    }

    internal void SetBool(int setting, int value)
    {
        if (!Enum.IsDefined(typeof(NativeBoolSetting), setting)) return;
        var enabled = value != 0;
        var settings = _runtime.Settings;
        switch ((NativeBoolSetting)setting)
        {
            case NativeBoolSetting.MasterEnabled: settings.MasterEnabled = enabled; break;
            case NativeBoolSetting.PrivateMatchAuthorized: settings.PrivateMatchAuthorized = enabled; break;
            case NativeBoolSetting.EspEnabled: settings.EspEnabled = enabled; break;
            case NativeBoolSetting.TeamCheckEnabled: settings.TeamCheckEnabled = enabled; break;
            case NativeBoolSetting.TriggerEnabled: settings.TriggerEnabled = enabled; break;
            case NativeBoolSetting.AimAssistEnabled: settings.AimAssistEnabled = enabled; break;
            case NativeBoolSetting.AimVisibilityCheckEnabled: settings.AimVisibilityCheckEnabled = enabled; break;
            case NativeBoolSetting.DrawAimFov: settings.DrawAimFov = enabled; break;
            case NativeBoolSetting.CornerBoxes: settings.CornerBoxes = enabled; break;
            case NativeBoolSetting.DrawNames: settings.DrawNames = enabled; break;
            case NativeBoolSetting.DrawHealth: settings.DrawHealth = enabled; break;
            case NativeBoolSetting.DrawDistance: settings.DrawDistance = enabled; break;
            case NativeBoolSetting.DrawWeapons: settings.DrawWeapons = enabled; break;
            case NativeBoolSetting.DrawBombEsp: settings.DrawBombEsp = enabled; break;
            case NativeBoolSetting.DrawItemEsp: settings.DrawItemEsp = enabled; break;
            case NativeBoolSetting.DrawGrenadePrediction: settings.DrawGrenadePrediction = enabled; break;
            case NativeBoolSetting.DrawSnaplines: settings.DrawSnaplines = enabled; break;
            case NativeBoolSetting.DrawOffscreenArrows: settings.DrawOffscreenArrows = enabled; break;
            case NativeBoolSetting.DrawRadar: settings.DrawRadar = enabled; break;
            case NativeBoolSetting.DrawHeadMarker: settings.DrawHeadMarker = enabled; break;
            case NativeBoolSetting.HideOverlayFromCapture: settings.HideOverlayFromCapture = enabled; break;
        }
    }

    internal void SetInt(int setting, int value)
    {
        if (!Enum.IsDefined(typeof(NativeIntSetting), setting)) return;
        var settings = _runtime.Settings;
        switch ((NativeIntSetting)setting)
        {
            case NativeIntSetting.AimAssistFovPixels: settings.AimAssistFovPixels = Math.Clamp(value, 30, 300); break;
            case NativeIntSetting.AimAssistStrengthPercent: settings.AimAssistStrengthPercent = Math.Clamp(value, 5, 100); break;
            case NativeIntSetting.AimTargetPoint: settings.AimTargetPoint = ValidEnum<AimTargetPoint>(value, AimTargetPoint.Head); break;
            case NativeIntSetting.AimPriority: settings.AimPriority = ValidEnum<AimPriority>(value, AimPriority.Crosshair); break;
            case NativeIntSetting.AimMovement: settings.AimMovement = ValidEnum<AimMovement>(value, AimMovement.Smooth); break;
            case NativeIntSetting.AimActivation: settings.AimActivation = ValidEnum<AimActivation>(value, AimActivation.Hold); break;
            case NativeIntSetting.AimActivationKey: if (AimActivationGate.IsValidKey(value)) settings.AimActivationKey = value; break;
            case NativeIntSetting.EspTheme: settings.EspTheme = Math.Clamp(value, 0, 2); break;
        }
    }

    internal void SetDouble(int setting, double value)
    {
        if (setting == (int)NativeDoubleSetting.UiOpacity && double.IsFinite(value)) _runtime.Settings.UiOpacity = Math.Clamp(value, .82, 1);
    }

    internal void SetString(int setting, string value)
    {
        if (setting == (int)NativeStringSetting.GrenadePredictionMap) _runtime.Settings.GrenadePredictionMap = MapCollisionProvider.SanitizeMap(value);
    }

    internal void ExecuteCommand(int command, ref NativeCommandResult result)
    {
        try
        {
            switch ((NativeMenuCommand)command)
            {
                case NativeMenuCommand.SaveConfiguration:
                {
                    var operation = _configStore.Save(_runtime.Settings); _configMessage = operation.Message;
                    result.Success = Flag(operation.Success); result.Message = operation.Message; return;
                }
                case NativeMenuCommand.LoadConfiguration:
                {
                    var operation = _configStore.Load(_runtime.Settings); _configMessage = operation.Message;
                    result.Success = Flag(operation.Success); result.Message = operation.Message; return;
                }
                case NativeMenuCommand.Shutdown:
                    result.Success = 1; result.Message = "Closing Vectra External.";
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown())); return;
                default:
                    result.Success = 0; result.Message = "Unsupported menu command."; return;
            }
        }
        catch (Exception error)
        {
            result.Success = 0; result.Message = $"Menu command failed ({error.GetType().Name}).";
        }
    }

    private string Diagnostics(ReaderReport report, OverlayReport overlay, GameSnapshot snapshot)
    {
        var age = (DateTimeOffset.UtcNow - snapshot.CapturedAt).TotalMilliseconds;
        var trigger = _runtime.TriggerDiagnostics;
        var aim = _runtime.AimAssistReport;
        var discovery = _runtime.WorldEntityDiscovery;
        var grenade = _runtime.GrenadePredictionReport;
        return $"Reader: {report.Message}\nOverlay: {overlay.State} · {overlay.Message} · {CaptureVisibilityPresenter.Status(_overlay.StreamproofRequested, _overlay.CaptureExclusionApplied, _overlay.CaptureAffinityAvailable)}\n" +
               $"Snapshot: {(snapshot.IsFresh ? "fresh" : "stale")} · players {snapshot.Players.Count} · {snapshot.CaptureDuration.TotalMilliseconds:F2} ms · {snapshot.CaptureHz:F1} Hz · age {age:F1} ms\n" +
               $"World: items {snapshot.WorldItems.Count} · scan {discovery.Cursor}/{discovery.HighestEntityIndex} · passes {discovery.CompletedPasses}\n" +
               $"Visibility: {(snapshot.Visibility.CollisionReady ? "raycast ready" : "waiting for map collision")} · tested {snapshot.Visibility.TestedPlayers} · visible {snapshot.Visibility.VisiblePlayers} · occluded {snapshot.Visibility.OccludedPlayers}\n" +
               $"Map collision: {grenade.Status} · {grenade.MapName} · resources {grenade.PhysicsResources} · meshes {grenade.PhysicsMeshes} · triangles {grenade.Triangles} · errors {grenade.ParserErrors}\n" +
               $"Recoil: {(snapshot.Recoil.Available ? $"shots {snapshot.Recoil.ShotsFired} · punch {snapshot.Recoil.ViewPunch.Magnitude:F3}° · {(snapshot.Recoil.Reloading ? "reloading" : "ready")}" : "unavailable")}\n" +
               $"Trigger: {trigger.State} · {trigger.Gate} · target {trigger.TargetSnapshots}/2\n" +
               $"Aim: {aim.State} · {aim.Gate} · target {(aim.HasTarget ? aim.TargetEntityIndex : 0)} · move {aim.SentMoveX},{aim.SentMoveY}";
    }

    private static byte[] EncodeMaps(IReadOnlyList<string> maps)
    {
        var output = new byte[MaximumMaps * MapNameBytes];
        for (var index = 0; index < Math.Min(maps.Count, MaximumMaps); index++)
        {
            var bytes = Encoding.UTF8.GetBytes(maps[index]);
            Array.Copy(bytes, 0, output, index * MapNameBytes, Math.Min(bytes.Length, MapNameBytes - 1));
        }
        return output;
    }

    private static string GrenadeStatus(MapCollisionStatus status) => status switch
    {
        MapCollisionStatus.Loading => "Loading", MapCollisionStatus.Ready => "Collision ready",
        MapCollisionStatus.Approximate or MapCollisionStatus.Failed => "Approximate", _ => "Unavailable"
    };
    private static T ValidEnum<T>(int value, T fallback) where T : struct, Enum => Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : fallback;
    private static int Flag(bool value) => value ? 1 : 0;

    [DllImport("Vectra.Menu.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int RunVectraMenu(ref NativeMenuApi api);
}
