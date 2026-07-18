using OffsetClient = CS2Dumper.Offsets.ClientDll;
using Schemas = CS2Dumper.Schemas.ClientDll;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vectra.External;

public sealed class GameSnapshotReader
{
    public const int MaximumSkeletonLayouts = 128;
    private const int EntityStride = 0x70, EntityBlockOffset = 0x10, EntityBlockSize = 0x200;
    private const int GlobalVarsCurrentTimeOffset = 0x34;
    private const uint EntityIndexMask = 0x7FFF;
    private const int MinimumWorldDiscoverySlotsPerSnapshot = 8;
    private const int WorldDiscoverySlotsPerSnapshot = 64;
    private static readonly TimeSpan WorldDiscoveryBudget = TimeSpan.FromMilliseconds(2);
    private nint _clientBase;
    private nint _entitySystem;
    private readonly Dictionary<int, nint> _entityBlocks = new();
    private readonly Dictionary<int, CachedName> _names = new();
    private readonly Dictionary<nint, SkeletonLayout> _skeletonLayouts = new();
    private readonly Dictionary<int, TrackedWorldEntity> _worldEntities = new();
    private readonly WorldEntityDiscoveryCursor _worldDiscovery = new();
    private WorldEntityDiscoveryReport _worldDiscoveryReport = WorldEntityDiscoveryReport.Unavailable;
    private DateTimeOffset _lastCaptureCompleted;
    private double _captureHz;

    public WorldEntityDiscoveryReport WorldEntityDiscovery => Volatile.Read(ref _worldDiscoveryReport);

    public GameSnapshot Capture(GameProcessSession session, CaptureOptions options)
    {
        var memory = session.Memory;
        if (memory is null || !session.Ready) return GameSnapshot.Empty(session.GameBuild);
        if (_clientBase != session.ClientBase) ResetCaches(session.ClientBase);
        memory.BeginBatch();
        var sampledAt = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();
        if (!memory.ReadArray(GameProcessSession.Address(session.ClientBase, OffsetClient.dwViewMatrix), 16, out float[] matrix) ||
            !TryGetEntitySystem(memory, session, out var entitySystem) ||
            !memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwLocalPlayerPawn), out var localPawn)) {
            session.Report = new(ReaderState.MemoryUnavailable, "Unable to read required client data", session.DumpBuild, session.GameBuild);
            return GameSnapshot.Empty(session.GameBuild);
        }
        if (!memory.Read(GameProcessSession.Address(localPawn, Schemas.C_BaseEntity.m_iTeamNum), out byte localTeam)) {
            session.Report = new(ReaderState.WaitingForLocalPlayer, "Waiting for local player", session.DumpBuild, session.GameBuild);
            return GameSnapshot.Empty(session.GameBuild);
        }
        if (!memory.ReadPointer(GameProcessSession.Address(localPawn, Schemas.C_BaseEntity.m_pGameSceneNode), out var localNode) ||
            !memory.Read(GameProcessSession.Address(localNode, Schemas.CGameSceneNode.m_vecAbsOrigin), out Vec3 localOrigin)) {
            session.Report = new(ReaderState.WaitingForLocalPlayer, "Waiting for local player position", session.DumpBuild, session.GameBuild);
            return GameSnapshot.Empty(session.GameBuild);
        }
        var localViewOffset = new Vec3(0, 0, 64);
        if (options.ReadVisibility && memory.Read(GameProcessSession.Address(localPawn, Schemas.C_BaseModelEntity.m_vecViewOffset), out Vec3 capturedViewOffset) &&
            Vec3.IsFinite(capturedViewOffset) && MathF.Abs(capturedViewOffset.Z) <= 128) localViewOffset = capturedViewOffset;
        var localEyePosition = new Vec3(localOrigin.X + localViewOffset.X, localOrigin.Y + localViewOffset.Y, localOrigin.Z + localViewOffset.Z);
        var localViewAngles = default(Angles);
        var hasLocalViewAngles = (options.ReadViewYaw || options.ReadGrenadeThrow) && memory.Read(GameProcessSession.Address(session.ClientBase, OffsetClient.dwViewAngles), out localViewAngles) && ValidViewAngles(localViewAngles);
        var crosshair = -1;
        if (options.ReadCrosshair) memory.Read(GameProcessSession.Address(localPawn, Schemas.C_CSPlayerPawn.m_iIDEntIndex), out crosshair);
        var optionalWarnings = new List<string>();
        var skeletonCapture = new SkeletonCaptureAccumulator(options.ReadSkeleton);
        var recoil = RecoilSnapshot.Unavailable;
        if (options.ReadRecoil) try { recoil = ReadRecoil(memory, entitySystem, localPawn); } catch (Exception error) { optionalWarnings.Add($"recoil: {error.GetType().Name}"); }
        var players = new List<PlayerSnapshot>(64);
        if (options.ReadPlayers) {
            for (var index = 1; index <= 64; index++) {
                try {
                    var controller = EntityByIndex(memory, entitySystem, index);
                    if (controller == 0) continue;
                    var player = ReadPlayer(memory, entitySystem, controller, index, localOrigin, localPawn, session.GameBuild, optionalWarnings, options, skeletonCapture);
                    if (player is not null) players.Add(player);
                } catch (Exception error) { optionalWarnings.Add($"player {index}: {error.GetType().Name}"); }
            }
        }
        IReadOnlyList<WorldItemSnapshot> worldItems = Array.Empty<WorldItemSnapshot>();
        IReadOnlyList<DynamicCollisionSnapshot> dynamicCollisions = Array.Empty<DynamicCollisionSnapshot>();
        if (options.ReadWorldItems || options.ReadGrenadeThrow) try {
            var world = ReadWorldEntities(memory, entitySystem, localOrigin, options.ReadWorldItems, options.ReadGrenadeThrow);
            worldItems = world.Items; dynamicCollisions = world.DynamicCollisions;
        } catch (Exception error) { optionalWarnings.Add($"world entities: {error.GetType().Name}"); }
        var bomb = BombSnapshot.Unavailable;
        if (options.ReadBomb) try { bomb = ReadBomb(memory, entitySystem, session, localOrigin); } catch (Exception error) { optionalWarnings.Add($"bomb: {error.GetType().Name}"); }
        var grenadeThrow = GrenadeThrowState.Unavailable;
        if (options.ReadGrenadeThrow) try { grenadeThrow = ReadGrenadeThrow(memory, entitySystem, localPawn, localOrigin, localViewAngles, hasLocalViewAngles); } catch (Exception error) { optionalWarnings.Add($"grenade prediction: {error.GetType().Name}"); }
        var completed = DateTimeOffset.UtcNow;
        var duration = Stopwatch.GetElapsedTime(started);
        if (_lastCaptureCompleted != default) {
            var elapsed = (completed - _lastCaptureCompleted).TotalSeconds;
            if (elapsed > .0001) _captureHz = _captureHz > 0 ? _captureHz * .8 + (1d / elapsed) * .2 : 1d / elapsed;
        }
        _lastCaptureCompleted = completed;
        var skeletonReport = skeletonCapture.ToReport();
        if (skeletonReport.Requested && skeletonReport.AttemptedPlayers > 0 && skeletonReport.RenderablePlayers == 0)
            optionalWarnings.Add($"skeleton: 0/{skeletonReport.AttemptedPlayers} renderable ({skeletonReport.LastFailure})");
        var optionalWarning = string.Join("; ", optionalWarnings.Distinct().Take(4));
        var slow = SnapshotTiming.IsSlowCapture(duration);
        session.Report = new(slow ? ReaderState.SnapshotTooSlow : ReaderState.Ready, slow ? $"Slow but valid snapshot: {duration.TotalMilliseconds:F1} ms" : $"Snapshot: {players.Count} players", session.DumpBuild, session.GameBuild, optionalWarning);
        return new GameSnapshot(true, localTeam, localOrigin, hasLocalViewAngles ? localViewAngles.Yaw : 0, hasLocalViewAngles, crosshair, recoil, matrix, players, sampledAt, session.GameBuild, duration, _captureHz) { LocalEyePosition = localEyePosition, WorldItems = worldItems, DynamicCollisions = dynamicCollisions, Bomb = bomb, GrenadeThrow = grenadeThrow, Skeleton = skeletonReport };
    }

    private BombSnapshot ReadBomb(ProcessMemoryReader memory, nint entitySystem, GameProcessSession session, Vec3 localOrigin)
    {
        var rulesAvailable = memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwGameRules), out var gameRules);
        var bombPlanted = false;
        if (rulesAvailable) memory.Read(GameProcessSession.Address(gameRules, Schemas.C_CSGameRules.m_bBombPlanted), out bombPlanted);
        var hasCurrentTime = TryReadCurrentTime(memory, session, out var currentTime);

        if (memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwPlantedC4), out var plantedReference))
        {
            foreach (var candidate in BombPointerCandidates(memory, plantedReference))
                if (TryReadPlantedBomb(memory, candidate, localOrigin, hasCurrentTime ? currentTime : null, out var planted)) return planted;
        }

        if (!memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwWeaponC4), out var weaponReference)) return BombSnapshot.Unavailable;
        foreach (var candidate in BombPointerCandidates(memory, weaponReference))
            if (TryReadWeaponBomb(memory, entitySystem, candidate, localOrigin, bombPlanted, out var weapon)) return weapon;
        return BombSnapshot.Unavailable;
    }

    private static IEnumerable<nint> BombPointerCandidates(ProcessMemoryReader memory, nint first)
    {
        yield return first;
        if (memory.ReadPointer(first, out var second) && second != first) yield return second;
    }

    private static bool TryReadPlantedBomb(ProcessMemoryReader memory, nint entity, Vec3 localOrigin, float? currentTime, out BombSnapshot bomb)
    {
        bomb = BombSnapshot.Unavailable;
        if (!memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bC4Activated), out bool activated) ||
            !memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bBombTicking), out bool ticking)) return false;
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bHasExploded), out bool exploded);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bBombDefused), out bool defused);
        if (!TryReadEntityOrigin(memory, entity, out var origin) || BombEsp.ResolvePlantedState(activated, ticking, exploded, defused, true) != BombState.Planted) return false;

        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_nBombSite), out int site);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_flC4Blow), out float explosionTime);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_flTimerLength), out float timerLength);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bBeingDefused), out bool beingDefused);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_bCannotBeDefused), out bool cannotBeDefused);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_flDefuseCountDown), out float defuseTime);
        memory.Read(GameProcessSession.Address(entity, Schemas.C_PlantedC4.m_flDefuseLength), out float defuseLength);
        var explosionRemaining = currentTime is float current ? BombEsp.RemainingSeconds(explosionTime, current, timerLength) : null;
        var defuseRemaining = beingDefused && currentTime is float defuseCurrent ? BombEsp.RemainingSeconds(defuseTime, defuseCurrent, defuseLength) : null;
        bomb = new(BombState.Planted, origin, Distance(localOrigin, origin), 0, site is 0 or 1 ? site : -1,
            float.IsFinite(explosionTime) ? explosionTime : null, explosionRemaining, beingDefused, cannotBeDefused,
            beingDefused && float.IsFinite(defuseTime) ? defuseTime : null, defuseRemaining);
        return true;
    }

    private bool TryReadWeaponBomb(ProcessMemoryReader memory, nint entitySystem, nint entity, Vec3 localOrigin, bool bombPlanted, out BombSnapshot bomb)
    {
        bomb = BombSnapshot.Unavailable;
        if (!memory.Read(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_hOwnerEntity), out uint ownerHandle)) return false;
        var state = BombEsp.ResolveWeaponState(bombPlanted, ownerHandle, true);
        if (state == BombState.Unavailable) return false;
        var originEntity = entity;
        var carrierIndex = 0u;
        if (state == BombState.Carried)
        {
            originEntity = EntityByHandle(memory, entitySystem, ownerHandle);
            carrierIndex = ownerHandle & EntityIndexMask;
            if (originEntity == 0) return false;
        }
        if (!TryReadEntityOrigin(memory, originEntity, out var origin)) return false;
        bomb = new(state, origin, Distance(localOrigin, origin), carrierIndex, -1, null, null, false, false, null, null);
        return true;
    }

    private static bool TryReadEntityOrigin(ProcessMemoryReader memory, nint entity, out Vec3 origin)
    {
        origin = default;
        return memory.ReadPointer(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_pGameSceneNode), out var node) &&
            memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_vecAbsOrigin), out origin) && Vec3.IsFinite(origin);
    }

    private static bool TryReadCurrentTime(ProcessMemoryReader memory, GameProcessSession session, out float currentTime)
    {
        currentTime = default;
        return memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwGlobalVars), out var globalVars) &&
            memory.Read(GameProcessSession.Address(globalVars, GlobalVarsCurrentTimeOffset), out currentTime) &&
            float.IsFinite(currentTime) && currentTime >= 0 && currentTime < 100_000_000;
    }

    private PlayerSnapshot? ReadPlayer(ProcessMemoryReader memory, nint entitySystem, nint controller, int index, Vec3 localOrigin, nint localPawn, uint gameBuild, List<string> optionalWarnings, CaptureOptions options, SkeletonCaptureAccumulator skeletonCapture)
    {
        if (!memory.Read(GameProcessSession.Address(controller, Schemas.CCSPlayerController.m_hPlayerPawn), out uint pawnHandle)) return null;
        var pawn = EntityByHandle(memory, entitySystem, pawnHandle); if (pawn == 0) return null;
        if (!memory.Read(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_iHealth), out int health) ||
            !memory.Read(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_iTeamNum), out byte team) ||
            !memory.Read(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_lifeState), out byte lifeState) ||
            !memory.ReadPointer(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_pGameSceneNode), out var node) ||
            !memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_vecAbsOrigin), out Vec3 origin)) return null;
        memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_bDormant), out bool dormant);
        var isLocal = pawn == localPawn;
        var needsDetailedState = PlayerReadPlan.NeedsDetailedState(lifeState == 0 && health > 0, dormant, isLocal);
        var velocity = default(Vec3);
        var hasBounds = false; var mins = default(Vec3); var maxs = default(Vec3);
        if (needsDetailedState)
        {
            memory.Read(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_vecAbsVelocity), out velocity);
            if (!ValidVelocity(velocity)) velocity = default;
            if (memory.ReadPointer(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_pCollision), out var collision) &&
                memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMins), out mins) &&
                memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMaxs), out maxs)) hasBounds = ValidBounds(mins, maxs);
        }
        var name = needsDetailedState || isLocal ? ReadCachedName(memory, controller, pawnHandle, index) : ReadCachedNameIfPresent(controller, pawnHandle, index);
        var weapon = WeaponSnapshot.Unavailable;
        if (needsDetailedState && options.ReadWeapons) try { weapon = ReadWeapon(memory, entitySystem, pawn, out _, out _); } catch (Exception error) { optionalWarnings.Add($"weapon {index}: {error.GetType().Name}"); }
        var skeletonJoints = Array.Empty<Vec3>();
        var hasSkeletonJoint = Array.Empty<bool>();
        if (options.ReadSkeleton && needsDetailedState) {
            skeletonJoints = PlayerSnapshotBones.EmptyJoints; hasSkeletonJoint = PlayerSnapshotBones.EmptyValidity;
            try {
                var stage = ReadSkeleton(memory, pawn, node, origin, mins, maxs, hasBounds, skeletonJoints, hasSkeletonJoint, out var layoutResolved, out var boneCacheRead);
                skeletonCapture.Record(stage, layoutResolved, boneCacheRead);
            } catch (Exception error) {
                Array.Clear(skeletonJoints); Array.Clear(hasSkeletonJoint); skeletonCapture.Record(SkeletonReadStage.PoseRejected, false, false);
                optionalWarnings.Add($"skeleton {index}: {error.GetType().Name}");
            }
        }
        var botDifficulty = -1;
        if (options.ReadSkeleton && needsDetailedState) memory.Read(GameProcessSession.Address(controller, Schemas.CCSPlayerController.m_iPawnBotDifficulty), out botDifficulty);
        return new PlayerSnapshot(index, pawnHandle, pawnHandle & EntityIndexMask, team, Math.Clamp(health, 0, 100), lifeState == 0 && health > 0, dormant, name, Distance(localOrigin, origin), weapon, origin, velocity, mins, maxs, hasBounds, isLocal)
        {
            IsBot = botDifficulty is >= 0 and <= 5,
            SkeletonJoints = skeletonJoints,
            HasSkeletonJoint = hasSkeletonJoint
        };
    }

    private SkeletonReadStage ReadSkeleton(ProcessMemoryReader memory, nint pawn, nint sceneNode, Vec3 origin, Vec3 mins, Vec3 maxs, bool hasBounds, Vec3[] joints, bool[] valid, out bool layoutResolved, out bool boneCacheRead)
    {
        layoutResolved = false; boneCacheRead = false;
        if (!TryResolveSkeletonInstance(memory, pawn, sceneNode, out var modelState, out var boneArray, out var instanceFailure)) return instanceFailure;
        if (!memory.ReadPointer(GameProcessSession.Address(modelState, Schemas.CModelState.m_hModel), out var modelBinding)) return SkeletonReadStage.ModelUnavailable;

        if (!_skeletonLayouts.TryGetValue(modelBinding, out var layout))
        {
            if (!TryResolveSkeletonLayout(memory, modelBinding, out layout)) return SkeletonReadStage.LayoutUnavailable;
        }
        layoutResolved = true;
        if (!TryReadSkeletonLayout(memory, boneArray, layout, origin, mins, maxs, hasBounds, joints, valid))
        {
            _skeletonLayouts.Remove(modelBinding);
            return SkeletonReadStage.BoneReadFailed;
        }
        boneCacheRead = true;
        if (SkeletonPoseValidator.ValidatePose(joints, valid, origin, mins, maxs, hasBounds)) return SkeletonReadStage.Renderable;

        Array.Clear(joints); Array.Clear(valid);
        return SkeletonReadStage.PoseRejected;
    }

    private static bool TryResolveSkeletonInstance(ProcessMemoryReader memory, nint pawn, nint sceneNode, out nint modelState, out nint boneArray, out SkeletonReadStage failure)
    {
        modelState = 0; boneArray = 0; failure = SkeletonReadStage.SkeletonInstanceUnavailable;
        Span<nint> candidates = stackalloc nint[2];
        candidates[0] = sceneNode;
        if (memory.ReadPointer(GameProcessSession.Address(pawn, Schemas.C_BaseEntity.m_CBodyComponent), out var bodyComponent))
            candidates[1] = GameProcessSession.Address(bodyComponent, Schemas.CBodyComponentSkeletonInstance.m_skeletonInstance);

        foreach (var candidate in candidates)
        {
            if (candidate < 0x10000) continue;
            failure = SkeletonReadStage.BoneCacheUnavailable;
            var candidateModelState = GameProcessSession.Address(candidate, Schemas.CSkeletonInstance.m_modelState);
            if (!memory.ReadPointer(GameProcessSession.Address(candidateModelState, SkeletonMemoryLayout.BoneCacheOffsetInModelState), out var candidateBoneArray)) continue;
            modelState = candidateModelState; boneArray = candidateBoneArray;
            return true;
        }
        return false;
    }

    private static bool TryReadSkeletonLayout(ProcessMemoryReader memory, nint boneArray, SkeletonLayout layout, Vec3 origin, Vec3 mins, Vec3 maxs, bool hasBounds, Vec3[] joints, bool[] valid)
    {
        var requiredCount = layout.Indices.Where(index => index >= 0).DefaultIfEmpty(-1).Max() + 1;
        if (requiredCount <= 0 || requiredCount > SkeletonMemoryLayout.MaximumModelBones || !memory.ReadArray(boneArray, requiredCount, out BoneCacheEntry[] cache)) return false;
        for (var i = 0; i < layout.Indices.Length; i++)
        {
            var index = layout.Indices[i];
            if (index < 0 || index >= cache.Length) continue;
            var position = cache[index].Position;
            if (!Vec3.IsFinite(position)) continue;
            joints[i] = position; valid[i] = true;
        }
        return valid.Any(value => value);
    }

    private WorldEntityCapture ReadWorldEntities(ProcessMemoryReader memory, nint entitySystem, Vec3 localOrigin, bool readItems, bool readObstacles)
    {
        DiscoverWorldEntities(memory, entitySystem, readItems, readObstacles);
        var items = readItems ? new List<WorldItemSnapshot>() : null;
        var obstacles = readObstacles ? new List<DynamicCollisionSnapshot>() : null;
        foreach (var pair in _worldEntities.ToArray())
        {
            var index = pair.Key; var tracked = pair.Value;
            if ((tracked.Type == TrackedWorldType.Item && !readItems) || (tracked.Type == TrackedWorldType.Obstacle && !readObstacles)) continue;
            var entity = EntityByIndex(memory, entitySystem, index);
            if (entity == 0 || entity != tracked.Pointer)
            {
                if (++tracked.Misses >= 2) _worldEntities.Remove(index);
                continue;
            }
            if (!TryReadDesignerName(memory, entity, out var currentDesignerName) || !currentDesignerName.Equals(tracked.DesignerName, StringComparison.OrdinalIgnoreCase))
            {
                _worldEntities.Remove(index); TrackWorldEntity(index, entity, currentDesignerName, readItems, readObstacles); continue;
            }
            tracked.Misses = 0;
            if (tracked.Type == TrackedWorldType.Item)
            {
                if (readItems && TryReadWorldItem(memory, entity, index, tracked.DesignerName, localOrigin, out var item)) items!.Add(item);
                continue;
            }
            if (readObstacles && TryReadDynamicCollision(memory, entity, index, out var obstacle)) obstacles!.Add(obstacle);
        }
        return new(items is null ? Array.Empty<WorldItemSnapshot>() : items, obstacles is null ? Array.Empty<DynamicCollisionSnapshot>() : obstacles);
    }

    private void DiscoverWorldEntities(ProcessMemoryReader memory, nint entitySystem, bool readItems, bool readObstacles)
    {
        // Keep world discovery independent from the player/visibility capture cost.
        var started = Stopwatch.GetTimestamp();
        if (!memory.Read(GameProcessSession.Address(entitySystem, OffsetClient.dwGameEntitySystem_highestEntityIndex), out int highest)) return;
        highest = Math.Clamp(highest, 1, 0x7FFF);
        _worldDiscovery.UpdateHighest(highest);
        var scanned = 0;
        for (; ShouldContinueWorldDiscovery(scanned, Stopwatch.GetElapsedTime(started)); scanned++)
        {
            var index = _worldDiscovery.Next(highest);
            var entity = EntityByIndex(memory, entitySystem, index);
            if (entity == 0 || !TryReadDesignerName(memory, entity, out var designerName)) continue;
            TrackWorldEntity(index, entity, designerName, readItems, readObstacles);
        }
        Volatile.Write(ref _worldDiscoveryReport, new(true, _worldDiscovery.Cursor, highest, scanned, _worldDiscovery.CompletedPasses));
    }

    internal static bool ShouldContinueWorldDiscovery(int scannedSlots, TimeSpan elapsed)
        => scannedSlots < WorldDiscoverySlotsPerSnapshot &&
           (scannedSlots < MinimumWorldDiscoverySlotsPerSnapshot || elapsed < WorldDiscoveryBudget);

    private void TrackWorldEntity(int index, nint entity, string designerName, bool readItems, bool readObstacles)
    {
        if (readItems && IsPotentialItemName(designerName)) _worldEntities[index] = new(entity, TrackedWorldType.Item, designerName);
        else if (readObstacles && IsPotentialObstacleName(designerName)) _worldEntities[index] = new(entity, TrackedWorldType.Obstacle, designerName);
    }

    private static bool TryReadDesignerName(ProcessMemoryReader memory, nint entity, out string designerName)
    {
        designerName = string.Empty;
        if (!memory.ReadPointer(GameProcessSession.Address(entity, Schemas.CEntityInstance.m_pEntity), out var identity) ||
            !memory.ReadUtf8String(GameProcessSession.Address(identity, Schemas.CEntityIdentity.m_designerName), 64, out designerName)) return false;
        return true;
    }

    internal static bool IsPotentialItemName(string designerName)
        => !IsProjectileName(designerName) && !designerName.Contains("Planted", StringComparison.OrdinalIgnoreCase) &&
           (designerName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) || designerName.StartsWith("C_Weapon", StringComparison.OrdinalIgnoreCase) ||
            designerName.StartsWith("item_", StringComparison.OrdinalIgnoreCase) || designerName.StartsWith("C_Item", StringComparison.OrdinalIgnoreCase) ||
            designerName.Equals("C4", StringComparison.OrdinalIgnoreCase) || designerName.Contains("Grenade", StringComparison.OrdinalIgnoreCase) ||
            designerName.Contains("Healthshot", StringComparison.OrdinalIgnoreCase) || designerName.Contains("Defuser", StringComparison.OrdinalIgnoreCase));

    internal static bool IsProjectileName(string designerName)
        => designerName.Contains("Projectile", StringComparison.OrdinalIgnoreCase) ||
           designerName.Contains("Inferno", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPotentialObstacleName(string designerName)
        => designerName.Contains("door", StringComparison.OrdinalIgnoreCase) || designerName.Contains("movelinear", StringComparison.OrdinalIgnoreCase) ||
           designerName.Contains("dynamic_prop", StringComparison.OrdinalIgnoreCase) || designerName.Contains("breakable", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadDynamicCollision(ProcessMemoryReader memory, nint entity, int index, out DynamicCollisionSnapshot obstacle)
    {
        obstacle = default!;
        if (!memory.ReadPointer(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_pGameSceneNode), out var node) ||
            !memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_vecAbsOrigin), out Vec3 origin) ||
            !memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_bDormant), out bool dormant) || dormant ||
            !memory.ReadPointer(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_pCollision), out var collision) ||
            !memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMins), out Vec3 mins) ||
            !memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMaxs), out Vec3 maxs) || !ValidBounds(mins, maxs)) return false;
        var worldMins = new Vec3(origin.X + mins.X, origin.Y + mins.Y, origin.Z + mins.Z); var worldMaxs = new Vec3(origin.X + maxs.X, origin.Y + maxs.Y, origin.Z + maxs.Z);
        if (!Vec3.IsFinite(worldMins) || !Vec3.IsFinite(worldMaxs)) return false;
        obstacle = new((uint)index, worldMins, worldMaxs); return true;
    }

    private static bool TryReadWorldItem(ProcessMemoryReader memory, nint entity, int index, string designerName, Vec3 localOrigin, out WorldItemSnapshot item)
    {
        item = default!;
        if (!memory.Read(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_hOwnerEntity), out uint ownerHandle)) return false;
        if (BombEsp.IsValidHandle(ownerHandle)) return false;
        if (!TryReadEntityOrigin(memory, entity, out var origin) || !Vec3.IsFinite(origin)) return false;
        if (!memory.ReadPointer(GameProcessSession.Address(entity, Schemas.C_BaseEntity.m_pGameSceneNode), out var node)) return false;
        memory.Read(GameProcessSession.Address(node, Schemas.CGameSceneNode.m_bDormant), out bool dormant);
        if (dormant) return false;

        if (designerName.Contains("defuser", StringComparison.OrdinalIgnoreCase))
        {
            item = new((uint)index, WorldItemKind.DefuseKit, 0, "Defuse Kit", designerName, origin, Distance(localOrigin, origin), false); return true;
        }

        var econItem = GameProcessSession.Address(GameProcessSession.Address(entity, Schemas.C_EconEntity.m_AttributeManager), Schemas.C_AttributeContainer.m_Item);
        if (!memory.Read(GameProcessSession.Address(econItem, Schemas.C_EconItemView.m_iItemDefinitionIndex), out ushort definitionIndex) || !ItemIconCatalog.IsPickupDefinition(definitionIndex)) return false;
        var descriptor = ItemIconCatalog.Resolve(definitionIndex);
        var kind = definitionIndex switch
        {
            49 => WorldItemKind.C4,
            57 => WorldItemKind.Healthshot,
            >= 43 and <= 48 => WorldItemKind.Grenade,
            _ => WorldItemKind.Weapon
        };
        item = new((uint)index, kind, definitionIndex, descriptor.Name, designerName, origin, Distance(localOrigin, origin), false);
        return true;
    }

    private GrenadeThrowState ReadGrenadeThrow(ProcessMemoryReader memory, nint entitySystem, nint localPawn, Vec3 localOrigin, Angles viewAngles, bool hasViewAngles)
    {
        if (!hasViewAngles || !Vec3.IsFinite(localOrigin)) return GrenadeThrowState.Unavailable;
        var weapon = ReadWeapon(memory, entitySystem, localPawn, out _, out var activeWeapon);
        var kind = ItemIconCatalog.Grenade(weapon.DefinitionIndex);
        if (!weapon.Available || kind == GrenadeKind.None || activeWeapon == 0) return GrenadeThrowState.Unavailable;
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_bPinPulled), out bool pinPulled);
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_bThrowAnimating), out bool throwing);
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_bIsHeldByPlayer), out bool heldByPlayer);
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_bJustPulledPin), out bool justPulledPin);
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_fPinPullTime), out float pinPullTime);
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_fThrowTime), out float throwTime);
        if (!IsGrenadePrimed(heldByPlayer, pinPulled, throwing, justPulledPin, pinPullTime, throwTime)) return GrenadeThrowState.Unavailable;
        if (!memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BaseCSGrenade.m_flThrowStrength), out float strength) || !float.IsFinite(strength)) return GrenadeThrowState.Unavailable;
        memory.Read(GameProcessSession.Address(localPawn, Schemas.C_BaseEntity.m_vecAbsVelocity), out Vec3 velocity);
        if (!ValidVelocity(velocity)) velocity = default;
        memory.Read(GameProcessSession.Address(localPawn, Schemas.C_BaseModelEntity.m_vecViewOffset), out Vec3 viewOffset);
        if (!Vec3.IsFinite(viewOffset) || MathF.Abs(viewOffset.Z) > 128) viewOffset = new Vec3(0, 0, 64);
        var radius = 4f;
        if (memory.ReadPointer(GameProcessSession.Address(activeWeapon, Schemas.C_BaseEntity.m_pCollision), out var collision) &&
            memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMins), out Vec3 mins) &&
            memory.Read(GameProcessSession.Address(collision, Schemas.CCollisionProperty.m_vecMaxs), out Vec3 maxs) && ValidBounds(mins, maxs))
            radius = Math.Clamp(MathF.Max(MathF.Max(MathF.Abs(mins.X), MathF.Abs(maxs.X)), MathF.Max(MathF.Abs(mins.Y), MathF.Abs(maxs.Y))), 2, 8);
        var start = new Vec3(localOrigin.X + viewOffset.X, localOrigin.Y + viewOffset.Y, localOrigin.Z + viewOffset.Z);
        return new(true, kind, start, velocity, viewAngles, Math.Clamp(strength, 0, 1), radius);
    }

    internal static bool IsGrenadePrimed(bool heldByPlayer, bool pinPulled, bool throwing, bool justPulledPin, float pinPullTime, float throwTime)
        => heldByPlayer && (pinPulled || throwing || justPulledPin ||
            (float.IsFinite(pinPullTime) && pinPullTime > 0) || (float.IsFinite(throwTime) && throwTime > 0));

    private bool TryResolveSkeletonLayout(ProcessMemoryReader memory, nint modelBinding, out SkeletonLayout layout)
    {
        layout = default!;
        var candidates = new List<nint> { modelBinding };
        foreach (var offset in SkeletonMemoryLayout.ModelResourcePointerOffsets)
            if (memory.ReadPointer(GameProcessSession.Address(modelBinding, offset), out var candidate) && !candidates.Contains(candidate)) candidates.Add(candidate);
        foreach (var candidate in candidates)
        {
            var skeleton = GameProcessSession.Address(candidate, CS2Dumper.Schemas.AnimationsystemDll.PermModelData_t.m_modelSkeleton);
            var namesVector = GameProcessSession.Address(skeleton, CS2Dumper.Schemas.AnimationsystemDll.ModelSkeletonData_t.m_boneName);
            var parentsVector = GameProcessSession.Address(skeleton, CS2Dumper.Schemas.AnimationsystemDll.ModelSkeletonData_t.m_nParent);
            if (!memory.ReadPointer(GameProcessSession.Address(namesVector, SkeletonMemoryLayout.UtlVectorDataOffset), out var namesData) ||
                !memory.Read(GameProcessSession.Address(namesVector, SkeletonMemoryLayout.UtlVectorSizeOffset), out int namesCount) ||
                !memory.ReadPointer(GameProcessSession.Address(parentsVector, SkeletonMemoryLayout.UtlVectorDataOffset), out var parentsData) ||
                !memory.Read(GameProcessSession.Address(parentsVector, SkeletonMemoryLayout.UtlVectorSizeOffset), out int parentsCount) ||
                !SkeletonMemoryLayout.IsValidVectorHeader(namesData, namesCount) || !SkeletonMemoryLayout.IsValidVectorHeader(parentsData, parentsCount) || parentsCount != namesCount ||
                !memory.ReadArray(parentsData, parentsCount, out short[] parents)) continue;

            if (!SkeletonLayoutResolver.HasValidParentTable(parents)) continue;

            var names = new string[namesCount];
            for (var i = 0; i < namesCount; i++)
            {
                if (memory.ReadUtf8String(GameProcessSession.Address(namesData, i * IntPtr.Size), 64, out var boneName)) names[i] = boneName;
            }
            if (!SkeletonLayoutResolver.TryResolve(names, parents, out var indices)) continue;
            layout = new SkeletonLayout(indices, namesCount);
            if (_skeletonLayouts.Count >= MaximumSkeletonLayouts) _skeletonLayouts.Clear();
            _skeletonLayouts[modelBinding] = layout;
            return true;
        }
        return false;
    }

    private RecoilSnapshot ReadRecoil(ProcessMemoryReader memory, nint entitySystem, nint localPawn)
    {
        if (!memory.Read(GameProcessSession.Address(localPawn, Schemas.C_CSPlayerPawn.m_iShotsFired), out int shotsFired) ||
            !memory.ReadPointer(GameProcessSession.Address(localPawn, Schemas.C_BasePlayerPawn.m_pCameraServices), out var cameraServices) ||
            !memory.Read(GameProcessSession.Address(cameraServices, Schemas.CPlayer_CameraServices.m_vecCsViewPunchAngle), out Angles viewPunch) ||
            !memory.Read(GameProcessSession.Address(cameraServices, Schemas.CPlayer_CameraServices.m_nCsViewPunchAngleTick), out int punchTick) ||
            !Angles.IsFinite(viewPunch) || punchTick < -1 || MathF.Abs(viewPunch.Pitch) > 90 || MathF.Abs(viewPunch.Yaw) > 90) return RecoilSnapshot.Unavailable;
        _ = ReadWeapon(memory, entitySystem, localPawn, out var activeWeaponHandle, out var activeWeapon);
        if (activeWeaponHandle is 0 or 0xFFFFFFFF || activeWeapon == 0 || !memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_CSWeaponBase.m_bInReload), out bool reloading)) return RecoilSnapshot.Unavailable;
        return new RecoilSnapshot(true, Math.Clamp(shotsFired, 0, 1000), viewPunch, punchTick, punchTick >= 0, reloading, activeWeaponHandle);
    }

    private WeaponSnapshot ReadWeapon(ProcessMemoryReader memory, nint entitySystem, nint pawn, out uint activeWeaponHandle, out nint activeWeapon)
    {
        activeWeaponHandle = 0xFFFFFFFF; activeWeapon = 0;
        if (!memory.ReadPointer(GameProcessSession.Address(pawn, Schemas.C_BasePlayerPawn.m_pWeaponServices), out var weaponServices) ||
            !memory.Read(GameProcessSession.Address(weaponServices, Schemas.CPlayer_WeaponServices.m_hActiveWeapon), out activeWeaponHandle)) return WeaponSnapshot.Unavailable;
        activeWeapon = EntityByHandle(memory, entitySystem, activeWeaponHandle);
        if (activeWeapon == 0) return WeaponSnapshot.Unavailable;
        memory.Read(GameProcessSession.Address(activeWeapon, Schemas.C_BasePlayerWeapon.m_iClip1), out int clip);
        var item = GameProcessSession.Address(GameProcessSession.Address(activeWeapon, Schemas.C_EconEntity.m_AttributeManager), Schemas.C_AttributeContainer.m_Item);
        if (!memory.Read(GameProcessSession.Address(item, Schemas.C_EconItemView.m_iItemDefinitionIndex), out ushort definitionIndex)) return WeaponSnapshot.Unavailable;
        return WeaponCatalog.From(definitionIndex, clip);
    }

    private nint EntityByHandle(ProcessMemoryReader memory, nint entitySystem, uint handle) => handle is 0 or 0xFFFFFFFF ? 0 : EntityByIndex(memory, entitySystem, (int)(handle & EntityIndexMask));
    private nint EntityByIndex(ProcessMemoryReader memory, nint entitySystem, int index)
    {
        var slot = index / EntityBlockSize;
        if (!_entityBlocks.TryGetValue(slot, out var block)) {
            var blockAddress = GameProcessSession.Address(entitySystem, EntityBlockOffset + IntPtr.Size * slot);
            if (!memory.ReadPointer(blockAddress, out block)) return 0;
            _entityBlocks[slot] = block;
        }
        if (memory.ReadPointer(GameProcessSession.Address(block, EntityStride * (index % EntityBlockSize)), out var entity)) return entity;
        _entityBlocks.Remove(slot);
        return 0;
    }

    private bool TryGetEntitySystem(ProcessMemoryReader memory, GameProcessSession session, out nint entitySystem)
    {
        entitySystem = _entitySystem;
        if (entitySystem != 0) return true;
        if (!memory.ReadPointer(GameProcessSession.Address(session.ClientBase, OffsetClient.dwEntityList), out entitySystem)) return false;
        _entitySystem = entitySystem;
        return true;
    }

    private string ReadCachedName(ProcessMemoryReader memory, nint controller, uint pawnHandle, int index)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_names.TryGetValue(index, out var cached) || cached.Controller != controller || cached.PawnHandle != pawnHandle || now - cached.RefreshedAt >= TimeSpan.FromSeconds(2)) {
            memory.ReadUtf8String(GameProcessSession.Address(controller, Schemas.CCSPlayerController.m_sSanitizedPlayerName), 64, out var value);
            cached = new CachedName(controller, pawnHandle, value, now);
            _names[index] = cached;
        }
        return cached.Value;
    }

    private string ReadCachedNameIfPresent(nint controller, uint pawnHandle, int index)
        => _names.TryGetValue(index, out var cached) && cached.Controller == controller && cached.PawnHandle == pawnHandle ? cached.Value : string.Empty;

    private void ResetCaches(nint clientBase)
    {
        _clientBase = clientBase; _entitySystem = 0; _entityBlocks.Clear(); _names.Clear(); _skeletonLayouts.Clear(); _worldEntities.Clear(); _worldDiscovery.Reset();
        Volatile.Write(ref _worldDiscoveryReport, WorldEntityDiscoveryReport.Unavailable); _lastCaptureCompleted = default; _captureHz = 0;
    }

    private static bool ValidBounds(Vec3 mins, Vec3 maxs)
    {
        if (!Vec3.IsFinite(mins) || !Vec3.IsFinite(maxs)) return false;
        var x = maxs.X - mins.X; var y = maxs.Y - mins.Y; var z = maxs.Z - mins.Z;
        return MathF.Abs(mins.X) <= 256 && MathF.Abs(mins.Y) <= 256 && MathF.Abs(mins.Z) <= 256 && MathF.Abs(maxs.X) <= 256 && MathF.Abs(maxs.Y) <= 256 && MathF.Abs(maxs.Z) <= 256 && x is >= 1 and <= 128 && y is >= 1 and <= 128 && z is >= 16 and <= 128;
    }

    private static bool ValidVelocity(Vec3 velocity)
    {
        if (!Vec3.IsFinite(velocity)) return false;
        var speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
        return speed <= 4000;
    }

    private static bool ValidViewAngles(Angles angles) => Angles.IsFinite(angles) && MathF.Abs(angles.Pitch) <= 90 && MathF.Abs(angles.Yaw) <= 3600;

    private static float Distance(Vec3 first, Vec3 second)
    {
        var x = second.X - first.X; var y = second.Y - first.Y; var z = second.Z - first.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    private static float Distance(Vec3 first, Vec3 second, bool squared = false)
    {
        var value = Distance(first, second);
        return squared ? value * value : value;
    }

    private readonly record struct SkeletonLayout(int[] Indices, int BoneCount);
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct BoneCacheEntry
    {
        public readonly Vec3 Position;
        private readonly float Scale;
        private readonly float Rotation0, Rotation1, Rotation2, Rotation3;
    }

    private readonly record struct CachedName(nint Controller, uint PawnHandle, string Value, DateTimeOffset RefreshedAt);
    private readonly record struct WorldEntityCapture(IReadOnlyList<WorldItemSnapshot> Items, IReadOnlyList<DynamicCollisionSnapshot> DynamicCollisions);
    private enum TrackedWorldType : byte { Item, Obstacle }
    private sealed class TrackedWorldEntity(nint pointer, TrackedWorldType type, string designerName)
    {
        public nint Pointer { get; } = pointer;
        public TrackedWorldType Type { get; } = type;
        public string DesignerName { get; } = designerName;
        public int Misses { get; set; }
    }

    private sealed class SkeletonCaptureAccumulator(bool requested)
    {
        private int _attempted, _resolved, _boneReads, _renderable, _failures;
        private SkeletonReadStage _lastFailure = SkeletonReadStage.Unavailable;

        public void Record(SkeletonReadStage stage, bool layoutResolved, bool boneCacheRead)
        {
            if (!requested) return;
            _attempted++;
            if (layoutResolved) _resolved++;
            if (boneCacheRead) _boneReads++;
            if (stage == SkeletonReadStage.Renderable) _renderable++;
            else { _failures++; _lastFailure = stage; }
        }

        public SkeletonCaptureReport ToReport() => requested
            ? new(true, _attempted, _resolved, _boneReads, _renderable, _failures, _lastFailure)
            : SkeletonCaptureReport.Unavailable;
    }
}

internal sealed class WorldEntityDiscoveryCursor
{
    public int Cursor { get; private set; } = 1;
    public int CompletedPasses { get; private set; }
    public void UpdateHighest(int highest) { if (Cursor > Math.Max(1, highest)) Cursor = 1; }
    public int Next(int highest) { highest = Math.Max(1, highest); var value = Cursor++; if (Cursor > highest) { Cursor = 1; CompletedPasses++; } return value; }
    public void Reset() { Cursor = 1; CompletedPasses = 0; }
}

public sealed record WorldEntityDiscoveryReport(bool Available, int Cursor, int HighestEntityIndex, int LastScannedSlots, int CompletedPasses)
{
    public static WorldEntityDiscoveryReport Unavailable { get; } = new(false, 1, 0, 0, 0);
}
