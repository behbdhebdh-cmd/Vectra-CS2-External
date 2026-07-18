using System.Runtime.InteropServices;

namespace Vectra.External;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vec3(float x, float y, float z)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Z = z;

    public static bool IsFinite(Vec3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Angles(float pitch, float yaw)
{
    public readonly float Pitch = pitch;
    public readonly float Yaw = yaw;
    public float Magnitude => MathF.Sqrt(Pitch * Pitch + Yaw * Yaw);
    public static bool IsFinite(Angles value) => float.IsFinite(value.Pitch) && float.IsFinite(value.Yaw);
}

public sealed record WeaponSnapshot(bool Available, ushort DefinitionIndex, string Name, int Clip, int MaxClip)
{
    public static WeaponSnapshot Unavailable { get; } = new(false, 0, string.Empty, 0, 0);
}

public enum SkeletonJoint : byte
{
    Head, Neck, SpineUpper, SpineLower, Pelvis,
    LeftUpperArm, LeftLowerArm, LeftHand,
    RightUpperArm, RightLowerArm, RightHand,
    LeftUpperLeg, LeftLowerLeg, LeftAnkle,
    RightUpperLeg, RightLowerLeg, RightAnkle,
    Count
}

public sealed record RecoilSnapshot(bool Available, int ShotsFired, Angles ViewPunch, int PunchTick, bool HasPunchTick, bool Reloading, uint ActiveWeaponHandle)
{
    public static RecoilSnapshot Unavailable { get; } = new(false, 0, default, -1, false, false, 0xFFFFFFFF);
    public bool IsNeutral => Available && ShotsFired == 0 && !Reloading && ViewPunch.Magnitude <= .10f;
}

public sealed record TriggerDiagnostics(
    TriggerState State,
    string Gate,
    bool FocusOwned,
    bool ShotPending,
    bool ShotObserved,
    int TargetSnapshots,
    int QuietSnapshots,
    float PunchDelta)
{
    public static TriggerDiagnostics Empty { get; } = new(TriggerState.Disabled, "trigger disabled", false, false, false, 0, 0, 0);
}

public sealed record PlayerSnapshot(
    int Index,
    uint PawnHandle,
    uint PawnEntityIndex,
    int Team,
    int Health,
    bool Alive,
    bool Dormant,
    string Name,
    float Distance,
    WeaponSnapshot Weapon,
    Vec3 Origin,
    Vec3 Velocity,
    Vec3 CollisionMins,
    Vec3 CollisionMaxs,
    bool HasCollisionBounds,
    bool IsLocal)
{
    public bool IsBot { get; init; }
    public bool HasVisibilityData { get; init; }
    public bool IsVisible { get; init; }
    public Vec3[] SkeletonJoints { get; init; } = PlayerSnapshotBones.EmptyJoints;
    public bool[] HasSkeletonJoint { get; init; } = PlayerSnapshotBones.EmptyValidity;
}

public enum WorldItemKind { Weapon, Grenade, DefuseKit, Healthshot, C4 }

public sealed record WorldItemSnapshot(
    uint EntityIndex,
    WorldItemKind Kind,
    ushort DefinitionIndex,
    string Name,
    string DesignerName,
    Vec3 Origin,
    float Distance,
    bool Dormant);

public sealed record DynamicCollisionSnapshot(uint EntityIndex, Vec3 Mins, Vec3 Maxs);

public enum GrenadeKind { None, Flashbang, HighExplosive, Smoke, Molotov, Decoy, Incendiary }
public enum TrajectoryQuality { Unavailable, Approximate, CollisionAware }
public enum MapCollisionStatus { Unavailable, Approximate, Loading, Ready, Failed }

public sealed record GrenadeThrowState(
    bool Available,
    GrenadeKind Kind,
    Vec3 StartPosition,
    Vec3 PlayerVelocity,
    Angles ViewAngles,
    float ThrowStrength,
    float ProjectileRadius)
{
    public static GrenadeThrowState Unavailable { get; } = new(false, GrenadeKind.None, default, default, default, 0, 4);
}

public sealed record GrenadeTrajectory(
    IReadOnlyList<Vec3> Points,
    IReadOnlyList<int> BouncePointIndices,
    GrenadeKind Kind,
    TrajectoryQuality Quality,
    DateTimeOffset CreatedAt)
{
    public static GrenadeTrajectory Unavailable { get; } = new(Array.Empty<Vec3>(), Array.Empty<int>(), GrenadeKind.None, TrajectoryQuality.Unavailable, DateTimeOffset.MinValue);
    public bool Available => Points.Count > 1;
}

public sealed record GrenadePredictionReport(
    MapCollisionStatus Status,
    string MapName,
    string Message,
    int PhysicsResources = 0,
    int PhysicsMeshes = 0,
    int Triangles = 0,
    int ParserErrors = 0)
{
    public static GrenadePredictionReport Unavailable { get; } = new(MapCollisionStatus.Unavailable, string.Empty, "Grenade prediction disabled");
}

public enum BombState { Unavailable, Carried, Dropped, Planted }

public sealed record BombSnapshot(
    BombState State,
    Vec3 Origin,
    float Distance,
    uint CarrierEntityIndex,
    int BombSite,
    float? ExplosionTime,
    float? ExplosionRemainingSeconds,
    bool BeingDefused,
    bool CannotBeDefused,
    float? DefuseTime,
    float? DefuseRemainingSeconds)
{
    public static BombSnapshot Unavailable { get; } = new(BombState.Unavailable, default, 0, 0, -1, null, null, false, false, null, null);
    public bool Available => State != BombState.Unavailable;
}

public static class PlayerSnapshotBones
{
    public static Vec3[] EmptyJoints => new Vec3[(int)SkeletonJoint.Count];
    public static bool[] EmptyValidity => new bool[(int)SkeletonJoint.Count];
}

public enum SkeletonReadStage
{
    Unavailable,
    SkeletonInstanceUnavailable,
    BoneCacheUnavailable,
    ModelUnavailable,
    LayoutUnavailable,
    BoneReadFailed,
    PoseRejected,
    Renderable
}

public sealed record SkeletonCaptureReport(
    bool Requested,
    int AttemptedPlayers,
    int ResolvedLayouts,
    int BoneCacheReads,
    int RenderablePlayers,
    int Failures,
    SkeletonReadStage LastFailure)
{
    public static SkeletonCaptureReport Unavailable { get; } = new(false, 0, 0, 0, 0, 0, SkeletonReadStage.Unavailable);
    public bool HasRenderableData => RenderablePlayers > 0;
}

public sealed record GameSnapshot(
    bool Valid,
    int LocalTeam,
    Vec3 LocalOrigin,
    float LocalViewYaw,
    bool HasLocalViewYaw,
    int CrosshairEntityIndex,
    RecoilSnapshot Recoil,
    float[] ViewMatrix,
    IReadOnlyList<PlayerSnapshot> Players,
    DateTimeOffset CapturedAt,
    uint GameBuild,
    TimeSpan CaptureDuration,
    double CaptureHz)
{
    public static GameSnapshot Empty(uint build = 0) => new(false, 0, default, 0, false, -1, RecoilSnapshot.Unavailable, Array.Empty<float>(), Array.Empty<PlayerSnapshot>(), DateTimeOffset.UtcNow, build, TimeSpan.Zero, 0);
    public Vec3 LocalEyePosition { get; init; }
    public IReadOnlyList<WorldItemSnapshot> WorldItems { get; init; } = Array.Empty<WorldItemSnapshot>();
    public IReadOnlyList<DynamicCollisionSnapshot> DynamicCollisions { get; init; } = Array.Empty<DynamicCollisionSnapshot>();
    public BombSnapshot Bomb { get; init; } = BombSnapshot.Unavailable;
    public GrenadeThrowState GrenadeThrow { get; init; } = GrenadeThrowState.Unavailable;
    public VisibilityCaptureReport Visibility { get; init; } = VisibilityCaptureReport.Unavailable;
    public SkeletonCaptureReport Skeleton { get; init; } = SkeletonCaptureReport.Unavailable;
    public bool IsFresh => Valid && DateTimeOffset.UtcNow - CapturedAt < TimeSpan.FromMilliseconds(150);
    public bool IsRenderable => Valid && SnapshotTiming.IsWithinGrace(CapturedAt, DateTimeOffset.UtcNow, SnapshotTiming.OverlayGrace);
}

public sealed record VisibilityCaptureReport(bool CollisionReady, int TestedPlayers, int VisiblePlayers, int OccludedPlayers)
{
    public static VisibilityCaptureReport Unavailable { get; } = new(false, 0, 0, 0);
    public bool HasData => CollisionReady;
}

public enum ReaderState { WaitingForProcess, BuildMismatch, MemoryUnavailable, WaitingForLocalPlayer, SnapshotTooSlow, Ready }
public sealed record ReaderReport(ReaderState State, string Message, uint DumpBuild = 0, uint GameBuild = 0, string OptionalWarning = "");
public enum OverlayState { WaitingForProcess, BuildMismatch, WaitingForBounds, WaitingForSnapshot, Active }
public sealed record OverlayReport(OverlayState State, string Message)
{
    public static OverlayReport Waiting { get; } = new(OverlayState.WaitingForProcess, "Waiting for CS2 process");
}

public enum ClientStatusTone { Healthy, Warning, Error }
public sealed record ClientStatusSummary(string Title, string Detail, ClientStatusTone Tone);

public static class ClientStatusPresenter
{
    public static ClientStatusSummary Create(ReaderReport reader, OverlayReport overlay)
    {
        var builds = $"offsets {Build(reader.DumpBuild)} / game {Build(reader.GameBuild)}";
        if (reader.State == ReaderState.BuildMismatch)
            return new("BUILD MISMATCH", $"{builds} — rebuild the external release with current offsets", ClientStatusTone.Error);
        if (reader.State == ReaderState.MemoryUnavailable)
            return new("OVERLAY BLOCKED", $"{reader.Message} — {builds}", ClientStatusTone.Error);
        if (reader.State is ReaderState.Ready or ReaderState.SnapshotTooSlow && overlay.State == OverlayState.Active)
            return new("OVERLAY ACTIVE", $"CS2 build {Build(reader.GameBuild)} — process, snapshot and client bounds are ready", ClientStatusTone.Healthy);
        if (overlay.State is OverlayState.WaitingForBounds or OverlayState.WaitingForSnapshot)
            return new("OVERLAY WAITING", $"{overlay.Message} — {builds}", ClientStatusTone.Warning);
        return new("WAITING FOR CS2", $"{reader.Message} — offset build {Build(reader.DumpBuild)}", ClientStatusTone.Warning);
    }

    private static string Build(uint value) => value == 0 ? "unknown" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
public enum TriggerState { Disabled, MasterDisabled, SessionUnauthorized, WindowUnfocused, SnapshotStale, NoCrosshairTarget, FriendlyTarget, IneligibleTarget, TargetStabilizing, RecoilDataUnavailable, Reloading, WaitingForShotObservation, WaitingForRecoil, InputFailed, Fired }

public enum AimTargetPoint { Chest, Head }
public enum AimPriority { Crosshair, Closest, MostVisible }
public enum AimMovement { Smooth, Snap }
public enum AimActivation { Hold, Always }
public enum AimAssistState { Disabled, MasterDisabled, SessionUnauthorized, SnapshotStale, WindowUnfocused, ActivationReleased, WaitingForBounds, NoTarget, Locked, Snapped, InputFailed }

public sealed record AimCandidateDiagnostics(int TotalPlayers, int StatusRejected, int TeamRejected, int MissingVisibility, int Occluded, int ProjectionRejected, int OutsideFov, int Eligible)
{
    public static AimCandidateDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record AimAssistReport(
    AimAssistState State,
    string Gate,
    bool ActivationActive,
    uint TargetEntityIndex,
    AimTargetPoint TargetPoint,
    AimCandidateDiagnostics Candidates,
    int PlannedMoveX,
    int PlannedMoveY,
    int SentMoveX,
    int SentMoveY)
{
    public AimAssistReport(AimAssistState state, string gate, bool activationActive, uint targetEntityIndex, AimTargetPoint targetPoint)
        : this(state, gate, activationActive, targetEntityIndex, targetPoint, AimCandidateDiagnostics.Empty, 0, 0, 0, 0) { }
    public static AimAssistReport Disabled { get; } = new(AimAssistState.Disabled, "aim assist disabled", false, 0, AimTargetPoint.Head);
    public bool HasTarget => TargetEntityIndex != 0;
}

public sealed class ClientSettings
{
    public volatile bool MasterEnabled;
    public volatile bool PrivateMatchAuthorized;
    public volatile bool EspEnabled = true;
    public volatile bool TeamCheckEnabled = true;
    public volatile bool TriggerEnabled;
    public volatile bool AimAssistEnabled;
    public volatile int AimAssistFovPixels = 110;
    public volatile int AimAssistStrengthPercent = 35;
    public volatile AimTargetPoint AimTargetPoint = AimTargetPoint.Head;
    public volatile AimPriority AimPriority = AimPriority.Crosshair;
    public volatile AimMovement AimMovement = AimMovement.Smooth;
    public volatile AimActivation AimActivation = AimActivation.Hold;
    public volatile int AimActivationKey = 0x05;
    public volatile bool AimVisibilityCheckEnabled = true;
    public volatile bool DrawAimFov = true;
    public volatile bool CornerBoxes;
    public volatile bool DrawNames = true;
    public volatile bool DrawHealth = true;
    public volatile bool DrawDistance = true;
    public volatile bool DrawWeapons;
    public volatile bool DrawBombEsp;
    public volatile bool DrawItemEsp;
    public volatile bool DrawGrenadePrediction;
    public volatile bool DrawSnaplines;
    public volatile bool DrawOffscreenArrows;
    public volatile bool DrawRadar;
    public volatile bool DrawSkeleton;
    public volatile bool DrawHeadMarker;
    public volatile int EspTheme;
    public volatile bool HideOverlayFromCapture;
    public double UiOpacity = .96;
    public string GrenadePredictionMap = "Auto";
}
