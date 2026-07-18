using Vectra.External;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Text;
using AutomationProperties = System.Windows.Automation.AutomationProperties;

static class Program
{
    private const float Tolerance = .001f;

    private static int Main(string[] args)
    {
        try {
            if (args.Length == 2 && args[0].Equals("--map-smoke", StringComparison.OrdinalIgnoreCase))
            {
                var inspection = MapCollisionProvider.InspectMap(args[1]);
                Console.WriteLine($"resources={inspection.PhysicsResources}; meshes={inspection.PhysicsMeshes}; triangles={inspection.Triangles}; errors={inspection.ParserErrors}; firstError={inspection.FirstError}");
                return inspection.Triangles > 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0].Equals("--map-list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in MapCollisionProvider.InspectPackageEntries(args[1])) Console.WriteLine(entry);
                return 0;
            }
            if (args.Length == 3 && args[0].Equals("--active-map", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[1], out var processId))
            {
                var activeMap = RestartManagerMapResolver.FindActiveMap(args[2], processId, CancellationToken.None);
                Console.WriteLine(activeMap ?? "unresolved");
                return activeMap is null ? 1 : 0;
            }
            ProjectsForwardAtZeroYaw();
            RotatesWithLocalYaw();
            ClampsTargetsOutsideTheRadarRange();
            RejectsInvalidInput();
            DecodesProtectedText();
            ValidatesOffsetMetadataAndStatus();
            ValidatesAdaptivePerformancePolicy();
            ValidatesConfigurationRoundTrip();
            ValidatesSnapshotGraceAndPublication();
            ValidatesAimAndEspGeometry();
            ValidatesAimTargetingModes();
            ValidatesIconCatalogAndGrenadeMath();
            ValidatesSkeletonGeometryAndLayout();
            ValidatesBotSkeletonFallback();
            ValidatesNativeMenuHost();
            Console.WriteLine("Adaptive performance, native menu host, visibility raycasts, icons, Item/Bomb ESP, grenade prediction, cache, and ESP geometry tests passed.");
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void ProjectsForwardAtZeroYaw()
    {
        Assert(RadarGeometry.TryProject(default, new Vec3(100, 0, 0), 0, 100, 40, out var point), "Expected a valid projection.");
        AssertClose(0, point.X); AssertClose(-40, point.Y); Assert(!point.IsClamped, "In-range marker was clamped.");
    }

    private static void RotatesWithLocalYaw()
    {
        Assert(RadarGeometry.TryProject(default, new Vec3(0, 100, 0), 90, 100, 40, out var forward), "Expected a valid forward projection.");
        AssertClose(0, forward.X); AssertClose(-40, forward.Y);
        Assert(RadarGeometry.TryProject(default, new Vec3(100, 0, 0), 90, 100, 40, out var left), "Expected a valid side projection.");
        AssertClose(-40, left.X); AssertClose(0, left.Y);
    }

    private static void ClampsTargetsOutsideTheRadarRange()
    {
        Assert(RadarGeometry.TryProject(default, new Vec3(0, 400, 0), 90, 100, 40, out var point), "Expected a valid clamped projection.");
        AssertClose(0, point.X); AssertClose(-40, point.Y); Assert(point.IsClamped, "Out-of-range marker was not clamped.");
    }

    private static void RejectsInvalidInput() => Assert(!RadarGeometry.TryProject(default, new Vec3(float.NaN, 0, 0), 0, 100, 40, out _), "Invalid coordinates were accepted.");

    private static void DecodesProtectedText()
    {
        var productName = ObfuscatedText.Get(ProtectedText.ProductName);
        var radarLabel = ObfuscatedText.Get(ProtectedText.RotatingRadar);
        Assert(productName == "Vectra External", $"Product text did not decode correctly: '{productName}'.");
        Assert(radarLabel == "Show rotating radar", $"UI text did not decode correctly: '{radarLabel}'.");
    }

    private static void ValidatesOffsetMetadataAndStatus()
    {
        var offsets = OffsetBuildInfo.Current;
        Assert(offsets.BuildNumber == 14171, $"Expected offset build 14171, received {offsets.BuildNumber}.");
        Assert(offsets.GeneratedAt.HasValue, "Offset generation timestamp was not loaded.");
        Assert(!OffsetBuildInfo.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Available, "Missing offset metadata was accepted.");

        var mismatch = new ReaderReport(ReaderState.BuildMismatch, "Dump build 14171, game build 14172", 14171, 14172);
        var mismatchStatus = ClientStatusPresenter.Create(mismatch, new OverlayReport(OverlayState.BuildMismatch, mismatch.Message));
        Assert(mismatchStatus.Title == "BUILD MISMATCH" && mismatchStatus.Tone == ClientStatusTone.Error, "Build mismatch did not produce a blocking overlay status.");
        Assert(mismatchStatus.Detail.Contains("14171", StringComparison.Ordinal) && mismatchStatus.Detail.Contains("14172", StringComparison.Ordinal), "Build mismatch status omitted build numbers.");

        var ready = new ReaderReport(ReaderState.Ready, "Snapshot: 10 players", 14171, 14171);
        var readyStatus = ClientStatusPresenter.Create(ready, new OverlayReport(OverlayState.Active, "Overlay active"));
        Assert(readyStatus.Title == "OVERLAY ACTIVE" && readyStatus.Tone == ClientStatusTone.Healthy, "Ready reader did not produce an active overlay status.");
    }

    private static void ValidatesAdaptivePerformancePolicy()
    {
        var settings = new ClientSettings();
        AssertClose(500, (float)RuntimeCadence.Select(false, false, settings).TotalMilliseconds);
        AssertClose((float)(1000d / 15d), (float)RuntimeCadence.Select(true, false, settings).TotalMilliseconds);
        AssertClose((float)(1000d / 60d), (float)RuntimeCadence.Select(true, true, settings).TotalMilliseconds);
        settings.AimAssistEnabled = true;
        AssertClose((float)(1000d / 120d), (float)RuntimeCadence.Select(true, true, settings).TotalMilliseconds);
        settings.AimAssistEnabled = false; settings.TriggerEnabled = true;
        AssertClose((float)(1000d / 120d), (float)RuntimeCadence.Select(true, true, settings).TotalMilliseconds);
        settings.TriggerEnabled = false; settings.EspEnabled = false;
        AssertClose(50, (float)RuntimeCadence.Select(true, true, settings).TotalMilliseconds);

        var disabled = CaptureOptions.From(settings);
        Assert(!disabled.ReadPlayers && !disabled.ReadRecoil && !disabled.ReadVisibility && !disabled.ReadWeapons && !disabled.ReadSkeleton && !disabled.ReadBomb && !disabled.ReadWorldItems && !disabled.ReadGrenadeThrow && !disabled.ReadViewYaw, "Disabled features still requested optional capture data.");
        settings.EspEnabled = true; settings.DrawWeapons = true; settings.DrawSkeleton = true; settings.DrawBombEsp = true; settings.DrawItemEsp = true; settings.DrawGrenadePrediction = true; settings.DrawRadar = true;
        var visuals = CaptureOptions.From(settings);
        Assert(visuals.ReadPlayers && visuals.ReadWeapons && !visuals.ReadSkeleton && visuals.ReadBomb && visuals.ReadWorldItems && visuals.ReadGrenadeThrow && visuals.ReadViewYaw && !visuals.ReadRecoil && !visuals.ReadVisibility, "Visual capture options did not include Item ESP and prediction or requested disabled Skeleton reads.");
        settings.AimAssistEnabled = true; settings.TriggerEnabled = true;
        var input = CaptureOptions.From(settings);
        Assert(input.ReadCrosshair && input.ReadRecoil && input.ReadVisibility, "Active input features did not request their required capture data.");

        AssertClose(250, (float)OverlayCadence.Select(false, false).TotalMilliseconds);
        AssertClose((float)(1000d / 15d), (float)OverlayCadence.Select(true, false).TotalMilliseconds);
        AssertClose((float)(1000d / 30d), (float)OverlayCadence.Select(true, true).TotalMilliseconds);
        var nowTick = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert(OverlayUpdatePolicy.ShouldUpdate(true, nowTick, nowTick, TimeSpan.FromSeconds(1)), "A newly published snapshot did not bypass overlay maintenance cadence.");
        Assert(!OverlayUpdatePolicy.ShouldUpdate(false, nowTick, nowTick, TimeSpan.FromSeconds(1)), "Unchanged overlay state triggered an unnecessary update.");
        Assert(OverlayUpdatePolicy.ShouldUpdate(false, nowTick - System.Diagnostics.Stopwatch.Frequency * 2, nowTick, TimeSpan.FromSeconds(1)), "Expired overlay maintenance was not scheduled.");
        Assert(CaptureVisibilityPresenter.Status(false, false, true) == "Visible to screen capture" && CaptureVisibilityPresenter.Status(true, true, true) == "Streamproof active" && CaptureVisibilityPresenter.Status(true, false, false) == "Streamproof unavailable", "Streamproof diagnostics were ambiguous.");
        Assert(PlayerReadPlan.NeedsDetailedState(true, false, false) && !PlayerReadPlan.NeedsDetailedState(true, false, true) && !PlayerReadPlan.NeedsDetailedState(false, false, false) && !PlayerReadPlan.NeedsDetailedState(true, true, false), "Player read planning did not preserve active enemies or skip local/dead/dormant details.");
        Assert(ProcessMemoryReader.MaximumCachedRegions == 128 && GameSnapshotReader.MaximumSkeletonLayouts == 128, "Long-session cache limits changed unexpectedly.");

        using var runtime = new ExternalRuntime();
        Assert(!runtime.IsStarted, "External runtime started before the UI requested it.");
        Assert(runtime.Start() && !runtime.Start() && runtime.IsStarted, "External runtime start was not idempotent.");
    }

    private static void ValidatesConfigurationRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vectra-config-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientConfigStore(Path.Combine(directory, "client-config.json"));
            var source = new ClientSettings {
                MasterEnabled = true,
                PrivateMatchAuthorized = true,
                EspEnabled = false,
                TeamCheckEnabled = false,
                TriggerEnabled = true,
                AimAssistEnabled = true,
                AimAssistFovPixels = 246,
                AimAssistStrengthPercent = 73,
                AimTargetPoint = AimTargetPoint.Head,
                AimPriority = AimPriority.MostVisible,
                AimMovement = AimMovement.Snap,
                AimActivation = AimActivation.Hold,
                AimActivationKey = 0x58,
                AimVisibilityCheckEnabled = false,
                DrawAimFov = false,
                CornerBoxes = false,
                DrawNames = false,
                DrawHealth = false,
                DrawDistance = false,
                DrawWeapons = false,
                DrawBombEsp = true,
                DrawItemEsp = true,
                DrawGrenadePrediction = true,
                GrenadePredictionMap = "de_mirage",
                DrawSnaplines = true,
                DrawOffscreenArrows = false,
                DrawRadar = false,
                DrawSkeleton = true,
                DrawHeadMarker = false,
                EspTheme = 2,
                HideOverlayFromCapture = true,
                UiOpacity = .84
            };
            Assert(store.Save(source).Success, "Configuration was not saved.");
            var loaded = new ClientSettings();
            var result = store.Load(loaded);
            Assert(result.Success, "Configuration was not loaded.");
            Assert(loaded.MasterEnabled && loaded.TriggerEnabled && loaded.AimAssistEnabled, "Saved input settings were not restored.");
            Assert(!loaded.EspEnabled && !loaded.TeamCheckEnabled && !loaded.DrawRadar, "Saved visual settings were not restored.");
            Assert(loaded.DrawBombEsp, "Saved Bomb ESP setting was not restored.");
            Assert(loaded.DrawItemEsp && loaded.DrawGrenadePrediction && loaded.GrenadePredictionMap == "de_mirage", "Saved Item ESP or grenade predictor settings were not restored.");
            Assert(!loaded.DrawSkeleton, "Disabled Skeleton ESP was restored from an old enabled profile.");
            Assert(loaded.AimAssistFovPixels == 246 && loaded.AimAssistStrengthPercent == 73 && loaded.EspTheme == 2, "Saved numeric settings were not restored.");
            Assert(loaded.AimTargetPoint == AimTargetPoint.Head && loaded.AimPriority == AimPriority.MostVisible && loaded.AimMovement == AimMovement.Snap && loaded.AimActivation == AimActivation.Hold && loaded.AimActivationKey == 0x58 && !loaded.AimVisibilityCheckEnabled, "Saved aim-assist settings were not restored.");
            Assert(Math.Abs(loaded.UiOpacity - .84) < .001, "Saved native-menu opacity was not restored.");
            Assert(loaded.HideOverlayFromCapture, "Saved Streamproof setting was not restored.");
            Assert(!loaded.PrivateMatchAuthorized, "Private-match authorization was restored from disk.");

            File.WriteAllText(store.Path, "{\"SchemaVersion\":1,\"AimAssistEnabled\":true,\"AimAssistFovPixels\":110,\"AimAssistStrengthPercent\":35}");
            var legacy = new ClientSettings(); Assert(store.Load(legacy).Success, "Legacy configuration was not loaded.");
            Assert(legacy.AimActivation == AimActivation.Always && legacy.AimTargetPoint == AimTargetPoint.Chest && legacy.AimPriority == AimPriority.Crosshair && legacy.AimMovement == AimMovement.Smooth && legacy.AimVisibilityCheckEnabled, "Legacy aim behavior was not preserved.");
            Assert(!legacy.DrawBombEsp, "Legacy configuration unexpectedly enabled Bomb ESP.");
            Assert(!legacy.DrawItemEsp && !legacy.DrawGrenadePrediction && legacy.GrenadePredictionMap == "Auto", "Legacy configuration unexpectedly enabled new visual features.");
            Assert(!legacy.HideOverlayFromCapture, "Legacy configuration without a Streamproof field did not default to capture-visible.");

            File.WriteAllText(store.Path, "{\"SchemaVersion\":1,\"AimTargetPoint\":99,\"AimPriority\":99,\"AimMovement\":99,\"AimActivation\":99,\"AimActivationKey\":27}");
            var invalid = new ClientSettings { AimTargetPoint = AimTargetPoint.Chest, AimActivation = AimActivation.Always };
            Assert(store.Load(invalid).Success, "Configuration with invalid aim values could not be normalized.");
            Assert(invalid.AimTargetPoint == AimTargetPoint.Head && invalid.AimPriority == AimPriority.Crosshair && invalid.AimMovement == AimMovement.Smooth && invalid.AimActivation == AimActivation.Hold && invalid.AimActivationKey == 0x05 && invalid.AimVisibilityCheckEnabled, "Invalid aim values did not fall back to fresh defaults.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void ValidatesSnapshotGraceAndPublication()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        Assert(SnapshotTiming.IsWithinGrace(now.AddMilliseconds(-250), now, SnapshotTiming.OverlayGrace), "250 ms grace boundary was rejected.");
        Assert(!SnapshotTiming.IsWithinGrace(now.AddMilliseconds(-251), now, SnapshotTiming.OverlayGrace), "Expired grace window was retained.");

        var gap = new SnapshotGapTracker();
        Assert(gap.BeginOrContinue(now), "Gap did not start.");
        Assert(gap.BeginOrContinue(now.AddMilliseconds(249)), "Short gap was not preserved.");
        Assert(!gap.BeginOrContinue(now.AddMilliseconds(251)), "Long gap was preserved past its limit.");
        gap.Clear(); Assert(!gap.IsActive, "Gap tracker did not clear.");

        var valid = new GameSnapshot(true, 0, default, 0, true, -1, RecoilSnapshot.Unavailable, new float[16], Array.Empty<PlayerSnapshot>(), now, 14171, TimeSpan.Zero, 120);
        Assert(ReferenceEquals(valid, SnapshotTiming.KeepLastValid(valid, GameSnapshot.Empty(14171))), "Invalid candidate replaced the last valid snapshot.");
        Assert(SnapshotTiming.IsSlowCapture(TimeSpan.FromMilliseconds(51)), "A capture above 50 ms was not classified as slow.");
        Assert(!SnapshotTiming.IsSlowCapture(TimeSpan.FromMilliseconds(50)), "The 50 ms boundary was classified as slow.");
        var slowButValid = valid with { CapturedAt = DateTimeOffset.UtcNow, CaptureDuration = TimeSpan.FromMilliseconds(75) };
        Assert(slowButValid.Valid && slowButValid.IsRenderable, "A slow valid snapshot became non-renderable.");
        var waiting = new OverlayReport(OverlayState.WaitingForSnapshot, "FOV active");
        Assert(waiting.State == OverlayState.WaitingForSnapshot, "Overlay report did not preserve its explicit state.");
    }

    private static void ValidatesAimAndEspGeometry()
    {
        var player = new PlayerSnapshot(1, 1, 1, 2, 100, true, false, "target", 100, WeaponSnapshot.Unavailable, default, default, default, default, false, false);
        var upperBody = AimAssistMath.UpperBodyPoint(player);
        AssertClose(50.4f, upperBody.Z);
        Assert(AimAssistMath.TryGetCorrection(new ScreenPoint(1000, 540), 1920, 1080, 80, .12f, 2.5f, out var correction), "Aim correction inside FOV was rejected.");
        Assert(Math.Abs(correction.X) <= 8 && correction.Y == 0, "Aim correction exceeded its max step.");
        Assert(AimAssistMath.TryGetCorrection(new ScreenPoint(962, 540), 1920, 1080, 80, .35f, 8, out var subpixel) && subpixel.X is > 0 and < 1, "Subpixel aim correction was rounded away.");
        Assert(!AimAssistMath.TryGetCorrection(new ScreenPoint(1100, 540), 1920, 1080, 80, .12f, 2.5f, out _), "Aim correction outside FOV was accepted.");
        Assert(AimAssistMath.TryGetSnapCorrection(new ScreenPoint(1000, 560), 1920, 1080, 80, out var snap) && snap.X == 40 && snap.Y == 20, "Snap correction did not retain the complete target delta.");

        var boundsPlayer = player with { Origin = new Vec3(0, 0, 10), CollisionMins = new Vec3(-16, -16, 0), CollisionMaxs = new Vec3(16, 16, 100), HasCollisionBounds = true };
        AssertClose(98, AimAssistMath.HeadPoint(boundsPlayer).Z); AssertClose(80, AimAssistMath.ChestPoint(boundsPlayer).Z);
        var joints = PlayerSnapshotBones.EmptyJoints; var validJoints = PlayerSnapshotBones.EmptyValidity;
        joints[(int)SkeletonJoint.Head] = new Vec3(1, 2, 91); joints[(int)SkeletonJoint.Neck] = new Vec3(1, 2, 75); joints[(int)SkeletonJoint.SpineUpper] = new Vec3(1, 2, 65);
        validJoints[(int)SkeletonJoint.Head] = validJoints[(int)SkeletonJoint.Neck] = validJoints[(int)SkeletonJoint.SpineUpper] = true;
        var bonePlayer = boundsPlayer with { SkeletonJoints = joints, HasSkeletonJoint = validJoints };
        AssertClose(91, AimAssistMath.HeadPoint(bonePlayer).Z); AssertClose(70, AimAssistMath.ChestPoint(bonePlayer).Z);

        var matrix = new float[16]; matrix[0] = .03f; matrix[5] = .03f; matrix[15] = 1;
        var visibilitySettings = new ClientSettings { TeamCheckEnabled = true };
        var visibleEnemy = player with { HasVisibilityData = true, IsVisible = true };
        var hiddenEnemy = player with { HasVisibilityData = true, IsVisible = false };
        var snapshot = new GameSnapshot(true, 1, default, 0, true, -1, RecoilSnapshot.Unavailable, matrix, new[] { visibleEnemy }, DateTimeOffset.UtcNow, 14171, TimeSpan.Zero, 120);
        Assert(AimTargeting.IsEligible(visibleEnemy, snapshot, visibilitySettings), "Visible enemy was rejected as an aim target.");
        Assert(!AimTargeting.IsEligible(hiddenEnemy, snapshot, visibilitySettings), "Hidden enemy was accepted as an aim target.");
        Assert(!AimTargeting.IsEligible(player, snapshot, visibilitySettings), "Enemy without visibility data was accepted as an aim target.");

        Assert(EspGeometry.TryGetBounds(player, default, matrix, 1920, 1080, out var bounds), "Fallback collision bounds were rejected.");
        Assert(bounds.Width > 2 && bounds.Height > 8, "Fallback ESP box was too small.");
        matrix[3] = 1;
        Assert(EspGeometry.TryGetBounds(player, default, matrix, 1920, 1080, out var clipped), "Partially off-screen box was rejected.");
        Assert(clipped.Right <= 1920 && clipped.Bottom <= 1080, "ESP box was not clipped to the viewport.");
    }

    private static void ValidatesAimTargetingModes()
    {
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var matrix = new float[16]; matrix[0] = .001f; matrix[5] = .001f; matrix[15] = 1;
        var first = new PlayerSnapshot(1, 1, 11, 2, 100, true, false, "crosshair", 500, WeaponSnapshot.Unavailable, new Vec3(20, 0, 0), default, default, default, false, false) { HasVisibilityData = true, IsVisible = true };
        var second = new PlayerSnapshot(2, 2, 22, 2, 100, true, false, "closest-visible", 100, WeaponSnapshot.Unavailable, new Vec3(120, 0, 0), default, default, default, false, false) { HasVisibilityData = true, IsVisible = true };
        var snapshot = new GameSnapshot(true, 1, default, 0, true, -1, RecoilSnapshot.Unavailable, matrix, new[] { first, second }, now, 14171, TimeSpan.Zero, 120);
        var visibleSince = new Dictionary<uint, DateTimeOffset> { [11] = now.AddSeconds(-1), [22] = now.AddSeconds(-5) };
        var settings = new ClientSettings { AimAssistFovPixels = 200, AimTargetPoint = AimTargetPoint.Chest };

        settings.AimPriority = AimPriority.Crosshair;
        Assert(AimTargeting.Select(snapshot, 1000, 800, settings, visibleSince, now, 0).Candidate!.Value.EntityIndex == 11, "Crosshair priority selected the wrong target.");
        settings.AimPriority = AimPriority.Closest;
        Assert(AimTargeting.Select(snapshot, 1000, 800, settings, visibleSince, now, 0).Candidate!.Value.EntityIndex == 22, "Closest priority selected the wrong target.");
        settings.AimPriority = AimPriority.MostVisible;
        Assert(AimTargeting.Select(snapshot, 1000, 800, settings, visibleSince, now, 0).Candidate!.Value.EntityIndex == 22, "Most-visible priority selected the wrong target.");
        Assert(AimTargeting.Select(snapshot, 1000, 800, settings, visibleSince, now, 11).Candidate!.Value.EntityIndex == 11, "Valid target lock was replaced by priority scoring.");
        var hiddenFirst = first with { IsVisible = false };
        var changed = snapshot with { Players = new[] { hiddenFirst, second } };
        Assert(AimTargeting.Select(changed, 1000, 800, settings, visibleSince, now, 11).Candidate!.Value.EntityIndex == 22, "Invalid lock did not transfer to the best eligible target.");

        var missing = first with { HasVisibilityData = false, IsVisible = false }; var occluded = second with { HasVisibilityData = true, IsVisible = false };
        var visibilityFailures = snapshot with { Players = new[] { missing, occluded } };
        settings.AimVisibilityCheckEnabled = true; var checkedSelection = AimTargeting.Select(visibilityFailures, 1000, 800, settings, visibleSince, now, 0);
        Assert(checkedSelection.Candidate is null && checkedSelection.Diagnostics.MissingVisibility == 1 && checkedSelection.Diagnostics.Occluded == 1 && checkedSelection.Diagnostics.Eligible == 0, "Visibility rejections were not diagnosed separately.");
        settings.AimVisibilityCheckEnabled = false; settings.AimPriority = AimPriority.MostVisible;
        var uncheckedSelection = AimTargeting.Select(visibilityFailures, 1000, 800, settings, visibleSince, now, 0);
        Assert(uncheckedSelection.Candidate!.Value.EntityIndex == 11 && uncheckedSelection.Diagnostics.Eligible == 2 && uncheckedSelection.Diagnostics.MissingVisibility == 0 && uncheckedSelection.Diagnostics.Occluded == 0, "Disabled visibility check did not allow targets or fall back to crosshair priority.");
        settings.AimVisibilityCheckEnabled = true;

        var statusAndTeam = snapshot with { Players = new[] { first with { IsLocal = true }, second with { Team = snapshot.LocalTeam } } };
        var rejectedSelection = AimTargeting.Select(statusAndTeam, 1000, 800, settings, visibleSince, now, 0);
        Assert(rejectedSelection.Diagnostics.StatusRejected == 1 && rejectedSelection.Diagnostics.TeamRejected == 1, "Status and team rejection gates were not diagnosed independently.");
        var failedProjection = AimTargeting.Select(snapshot with { ViewMatrix = new float[16], Players = new[] { first } }, 1000, 800, settings, visibleSince, now, 0);
        Assert(failedProjection.Diagnostics.ProjectionRejected == 1, "Projection rejection was not diagnosed.");
        settings.AimAssistFovPixels = 1;
        var outsideFovSelection = AimTargeting.Select(snapshot with { Players = new[] { first } }, 1000, 800, settings, visibleSince, now, 0);
        Assert(outsideFovSelection.Diagnostics.OutsideFov == 1, "FOV rejection was not diagnosed.");
        settings.AimAssistFovPixels = 200;

        var candidateReport = new AimCandidateDiagnostics(2, 0, 0, 1, 1, 0, 0, 0);
        var aimReport = new AimAssistReport(AimAssistState.NoTarget, "visibility rejected", true, 0, AimTargetPoint.Head, candidateReport, 4, -2, 0, 0);
        Assert(aimReport.Candidates.MissingVisibility == 1 && aimReport.PlannedMoveX == 4 && aimReport.SentMoveX == 0, "Aim diagnostics did not retain candidate or movement details.");

        var history = new AimVisibilityHistory(); history.Update(snapshot, now); var firstSeen = history.VisibleSince[11]; history.Update(snapshot, now.AddSeconds(2));
        Assert(history.VisibleSince[11] == firstSeen, "Continuous visibility duration was reset between fresh snapshots."); history.Update(changed, now.AddSeconds(3));
        Assert(!history.VisibleSince.ContainsKey(11) && history.VisibleSince.ContainsKey(22), "Visibility history did not reset an obscured target.");

        var gate = new ClientSettings { AimActivation = AimActivation.Hold, AimActivationKey = 0x05 };
        Assert(!AimActivationGate.IsActive(gate, _ => false) && AimActivationGate.IsActive(gate, key => key == 0x05), "Hold-key activation gate returned the wrong state.");
        gate.AimActivation = AimActivation.Always; Assert(AimActivationGate.IsActive(gate, _ => false), "Always activation still required a key.");
        Assert(!AimActivationGate.IsValidKey(0x1B) && AimActivationGate.IsValidKey(0x01) && AimActivationGate.IsValidKey(0x58), "Aim key validation rejected supported inputs or accepted Escape.");

        var movement = new AimMovementState(); movement.Acquire(11); Assert(movement.ShouldSnap(11), "New lock was not armed for snap."); movement.MarkSnapped(11); Assert(!movement.ShouldSnap(11), "Same lock snapped more than once."); movement.Acquire(22); Assert(movement.ShouldSnap(22), "New target did not re-arm snap."); movement.Clear(); Assert(movement.ShouldSnap(22), "Clearing the lock did not re-arm snap.");
        Assert(AimKeyNames.Display(0x05) == "MOUSE 4" && AimKeyNames.Display(0x58) == "X", "Aim key labels were not formatted correctly.");
    }

    private static void ValidatesIconCatalogAndGrenadeMath()
    {
        Assert(ItemIconCatalog.Resolve(49).Glyph == "\uE031", "C4 did not use the current U+E031 icon.");
        Assert(ItemIconCatalog.Resolve(23).SvgName == "mp5sd.svg", "MP5-SD SVG fallback was not registered.");
        Assert(ItemIconCatalog.Resolve(57).Key == "healthshot" && ItemIconCatalog.Resolve(57).HasIcon, "Healthshot code-native icon was not registered.");
        Assert(!ItemIconCatalog.Resolve(65000).HasIcon && ItemIconCatalog.Resolve(65000).Name.Contains("65000", StringComparison.Ordinal), "Unknown item fallback was not stable.");
        foreach (var definition in new ushort[] { 1, 7, 9, 16, 43, 44, 45, 46, 47, 48, 49, 500, 515, 525 }) Assert(ItemIconCatalog.Resolve(definition).HasIcon, $"Known item {definition} has no icon.");
        Assert(GameSnapshotReader.IsPotentialItemName("weapon_ak47") && GameSnapshotReader.IsPotentialItemName("C_Item_Healthshot") && GameSnapshotReader.IsPotentialItemName("item_defuser") && GameSnapshotReader.IsPotentialItemName("C_WeaponTaser") && GameSnapshotReader.IsPotentialObstacleName("prop_door_rotating"), "World registry classification omitted an item or moving obstacle.");
        Assert(!GameSnapshotReader.IsPotentialItemName("C_HEGrenadeProjectile") && !GameSnapshotReader.IsPotentialItemName("C_PlantedC4") && GameSnapshotReader.IsProjectileName("C_SmokeGrenadeProjectile"), "Active projectile or planted-bomb entities reached Item ESP.");
        Assert(ItemIconCatalog.IsPickupDefinition(1) && ItemIconCatalog.IsPickupDefinition(31) && ItemIconCatalog.IsPickupDefinition(43) && ItemIconCatalog.IsPickupDefinition(49) && ItemIconCatalog.IsPickupDefinition(57) && ItemIconCatalog.IsPickupDefinition(525) && !ItemIconCatalog.IsPickupDefinition(65000), "Pickup definition filtering omitted supported equipment or accepted an unknown entity.");
        Assert(GameSnapshotReader.ShouldContinueWorldDiscovery(0, TimeSpan.FromSeconds(1)) && GameSnapshotReader.ShouldContinueWorldDiscovery(7, TimeSpan.FromSeconds(1)) && !GameSnapshotReader.ShouldContinueWorldDiscovery(8, TimeSpan.FromSeconds(1)) && !GameSnapshotReader.ShouldContinueWorldDiscovery(64, TimeSpan.Zero), "World discovery did not guarantee progress independently of the full snapshot duration.");
        var cursor = new WorldEntityDiscoveryCursor();
        Assert(cursor.Next(100) == 1 && cursor.Next(100) == 2, "World registry cursor did not advance incrementally.");
        cursor.UpdateHighest(200); Assert(cursor.Next(200) == 3, "Changing highest entity index reset the discovery cursor.");
        for (var i = 0; i < 197; i++) cursor.Next(200);
        Assert(cursor.Next(200) == 1, "World registry cursor did not wrap after the highest entity.");
        cursor.UpdateHighest(1); Assert(cursor.Cursor == 1, "World registry cursor was not clamped after entity removal.");
        Assert(cursor.CompletedPasses > 0, "World registry did not record completed rescans.");

        Assert(GameSnapshotReader.IsGrenadePrimed(true, true, false, false, 0, 0), "Pin-pulled grenade was not considered primed.");
        Assert(GameSnapshotReader.IsGrenadePrimed(true, false, true, false, 0, 0), "Throw animation was not considered primed.");
        Assert(GameSnapshotReader.IsGrenadePrimed(true, false, false, true, 0, 0), "Initial pin pull was not considered primed.");
        Assert(GameSnapshotReader.IsGrenadePrimed(true, false, false, false, 1, 0), "Pin-pull time was not considered primed.");
        Assert(!GameSnapshotReader.IsGrenadePrimed(true, false, false, false, 0, 0) && !GameSnapshotReader.IsGrenadePrimed(false, true, true, true, 1, 1), "Unprimed or no-longer-held grenade produced a trajectory.");

        var weak = new GrenadeThrowState(true, GrenadeKind.HighExplosive, new Vec3(0, 0, 64), default, new Angles(0, 0), 0, 4);
        var strong = weak with { ThrowStrength = 1 };
        var weakVelocity = GrenadeTrajectoryMath.InitialVelocity(weak); var strongVelocity = GrenadeTrajectoryMath.InitialVelocity(strong);
        static float Length(Vec3 value) => MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        Assert(Length(strongVelocity) > Length(weakVelocity) * 2, "Throw strength did not affect launch velocity.");
        var inherited = GrenadeTrajectoryMath.InitialVelocity(strong with { PlayerVelocity = new Vec3(100, 0, 0) });
        AssertClose(strongVelocity.X + 125, inherited.X);
        Assert(GrenadeTrajectoryMath.Direction(new Angles(0, 0)).Z > 0, "Source-2 pitch correction was not applied.");
        Assert(!GrenadeTrajectoryMath.Simulate(strong with { PlayerVelocity = new Vec3(float.NaN, 0, 0) }).Available, "NaN player velocity reached the simulation.");

        var plane = new TriangleCollisionWorld(new[] { new CollisionTriangle(new Vec3(-100, -100, 0), new Vec3(100, -100, 0), new Vec3(0, 100, 0)) });
        Assert(plane.SweepSphere(new Vec3(0, 0, 10), new Vec3(0, 0, -10), 1, out var hit) && hit.Normal.Z > .9f, "Synthetic BVH plane hit or normal failed.");
        Assert(!plane.SweepSphere(new Vec3(200, 0, 10), new Vec3(200, 0, -10), 1, out _), "Synthetic BVH reported a miss as a hit.");
        Assert(plane.IntersectsSegment(new Vec3(0, 0, 10), new Vec3(0, 0, -10), out var rayHit) && rayHit.Fraction > .49f && rayHit.Fraction < .51f, "Exact visibility ray missed the synthetic wall.");
        Assert(!plane.IntersectsSegment(new Vec3(200, 0, 10), new Vec3(200, 0, -10), out _), "Exact visibility ray reported a false wall hit.");
        var rayPlayer = new PlayerSnapshot(2, 2, 22, 2, 100, true, false, "ray-target", 20, WeaponSnapshot.Unavailable, new Vec3(0, 0, -73), default, default, default, false, false);
        var raySnapshot = new GameSnapshot(true, 1, default, 0, false, -1, RecoilSnapshot.Unavailable, Array.Empty<float>(), new[] { rayPlayer }, DateTimeOffset.UtcNow, 14171, TimeSpan.Zero, 120) { LocalEyePosition = new Vec3(0, 0, 10) };
        var rayResult = VisibilityRaycaster.Apply(raySnapshot, plane, AimTargetPoint.Head);
        Assert(rayResult.Visibility.CollisionReady && rayResult.Visibility.TestedPlayers == 1 && rayResult.Visibility.OccludedPlayers == 1 && rayResult.Players[0].HasVisibilityData && !rayResult.Players[0].IsVisible, "Map visibility did not mark the wall-occluded target.");
        var bounds = new DynamicBoundsCollisionWorld(new[] { new DynamicCollisionSnapshot(1, new Vec3(5, -2, -2), new Vec3(7, 2, 2)) });
        Assert(bounds.SweepSphere(default, new Vec3(10, 0, 0), 1, out var boundsHit) && boundsHit.Normal.X < 0, "Dynamic collision bounds were not swept.");
        Assert(bounds.IntersectsSegment(default, new Vec3(10, 0, 0), out _), "Dynamic collision bounds did not block a visibility segment.");
        var bounced = GrenadeTrajectoryMath.Simulate(new GrenadeThrowState(true, GrenadeKind.Smoke, new Vec3(0, 0, 20), default, new Angles(80, 0), .3f, 4), plane);
        Assert(bounced.Quality == TrajectoryQuality.CollisionAware && bounced.BouncePointIndices.Count > 0 && bounced.BouncePointIndices.Count <= GrenadeTrajectoryMath.MaximumBounces, "Collision-aware trajectory did not bounce or exceeded its limit.");
        Assert(GrenadeTrajectoryMath.Simulate(strong).Points.Count <= GrenadeTrajectoryMath.MaximumSteps + 2, "Approximate trajectory exceeded the five-second step limit.");

        var temp = Path.Combine(Path.GetTempPath(), "vectra-map-cache-" + Guid.NewGuid().ToString("N") + ".vpk");
        try
        {
            File.WriteAllText(temp, "a"); var first = MapCollisionProvider.CachePath("de_test", new FileInfo(temp));
            File.AppendAllText(temp, "b"); var second = MapCollisionProvider.CachePath("de_test", new FileInfo(temp));
            Assert(first != second && first.Contains("map-cache", StringComparison.OrdinalIgnoreCase), "Map cache fingerprint did not invalidate after the VPK changed.");
            Assert(MapCollisionProvider.SanitizeMap("../bad") == "Auto" && MapCollisionProvider.SanitizeMap("de_mirage") == "de_mirage", "Map selection sanitization failed.");
            Assert(MapCollisionProvider.IsPlayableMapArchive("de_mirage.vpk") && MapCollisionProvider.IsPlayableMapArchive("cs_office.vpk") && !MapCollisionProvider.IsPlayableMapArchive("de_mirage_vanity.vpk") && !MapCollisionProvider.IsPlayableMapArchive("graphics_settings.vpk"), "Playable map archive filtering included vanity/non-map data or excluded a real map.");
            Assert(MapCollisionProvider.IsPhysicsResourceEntry("vmdl_c", "maps/de_test/world_physics") && MapCollisionProvider.IsPhysicsResourceEntry("vphys_c", "maps/de_test/world_collision") && !MapCollisionProvider.IsPhysicsResourceEntry("vmdl_c", "maps/de_test/worldnodes/prop"), "Current CS2 world-physics resource classification failed.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }



    private static void ValidatesNativeMenuHost()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var configPath = Path.Combine(Path.GetTempPath(), "vectra-native-menu-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                using var runtime = new ExternalRuntime();
                var overlay = new OverlayWindow();
                var host = new NativeMenuHost(runtime, overlay, "1.13.1", OffsetBuildInfo.Current, new ClientConfigStore(configPath));
                foreach (var setting in Enum.GetValues<NativeBoolSetting>()) host.SetBool((int)setting, 1);
                var settings = runtime.Settings;
                Assert(settings.MasterEnabled && settings.PrivateMatchAuthorized && settings.EspEnabled && settings.TeamCheckEnabled && settings.TriggerEnabled && settings.AimAssistEnabled && settings.AimVisibilityCheckEnabled && settings.DrawAimFov, "Core native bool settings were not mapped.");
                Assert(settings.CornerBoxes && settings.DrawNames && settings.DrawHealth && settings.DrawDistance && settings.DrawWeapons && settings.DrawBombEsp && settings.DrawItemEsp && settings.DrawGrenadePrediction, "ESP native bool settings were not mapped.");
                Assert(settings.DrawSnaplines && settings.DrawOffscreenArrows && settings.DrawRadar && settings.DrawHeadMarker && settings.HideOverlayFromCapture && !settings.DrawSkeleton, "Overlay native bool settings or the skeleton safety lock were not mapped.");

                host.SetInt((int)NativeIntSetting.AimAssistFovPixels, 999);
                host.SetInt((int)NativeIntSetting.AimAssistStrengthPercent, 1);
                host.SetInt((int)NativeIntSetting.AimTargetPoint, (int)AimTargetPoint.Chest);
                host.SetInt((int)NativeIntSetting.AimPriority, (int)AimPriority.MostVisible);
                host.SetInt((int)NativeIntSetting.AimMovement, (int)AimMovement.Snap);
                host.SetInt((int)NativeIntSetting.AimActivation, (int)AimActivation.Always);
                host.SetInt((int)NativeIntSetting.AimActivationKey, 0x58);
                host.SetInt((int)NativeIntSetting.EspTheme, 99);
                host.SetDouble((int)NativeDoubleSetting.UiOpacity, .5);
                host.SetString((int)NativeStringSetting.GrenadePredictionMap, "de_mirage");
                Assert(settings.AimAssistFovPixels == 300 && settings.AimAssistStrengthPercent == 5 && settings.AimTargetPoint == AimTargetPoint.Chest && settings.AimPriority == AimPriority.MostVisible && settings.AimMovement == AimMovement.Snap && settings.AimActivation == AimActivation.Always && settings.AimActivationKey == 0x58, "Native aim setting mapping or validation failed.");
                Assert(settings.EspTheme == 2 && Math.Abs(settings.UiOpacity - .82) < .001 && settings.GrenadePredictionMap == "de_mirage", "Native visual/string setting validation failed.");

                var state = new NativeMenuState(); host.FillState(ref state);
                Assert(state.ApiVersion == NativeMenuHost.ApiVersion && state.Version == "1.13.1" && state.MapCount >= 1 && state.Maps.Length == 24 * 32, "Native menu state contract was not populated.");
                Assert(Encoding.UTF8.GetString(state.Maps, 0, 32).TrimEnd('\0') == "Auto", "Native map list did not begin with Auto.");

                var command = new NativeCommandResult(); host.ExecuteCommand((int)NativeMenuCommand.SaveConfiguration, ref command);
                Assert(command.Success == 1 && File.Exists(configPath), "Native Save command failed.");
                settings.PrivateMatchAuthorized = true; settings.AimAssistEnabled = false;
                command = new NativeCommandResult(); host.ExecuteCommand((int)NativeMenuCommand.LoadConfiguration, ref command);
                Assert(command.Success == 1 && !settings.PrivateMatchAuthorized && settings.AimAssistEnabled, "Native Load command failed or restored private-match authorization.");
                command = new NativeCommandResult(); host.ExecuteCommand(999, ref command);
                Assert(command.Success == 0, "Unsupported native command was accepted.");
                overlay.Close();
            }
            catch (Exception error) { failure = error; }
            finally { if (File.Exists(configPath)) File.Delete(configPath); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    private static void ValidatesFovWithoutSnapshot()
    {
        const int width = 400, height = 300;
        var surface = new OverlaySurface {
            HasGameBounds = true,
            Snapshot = null,
            Settings = new ClientSettings { AimAssistEnabled = true, DrawAimFov = true, EspEnabled = false },
            FovScale = 1
        };
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        Assert(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0), "FOV did not render without an ESP snapshot.");
    }

    private static void ValidatesVisualStyleMathAndRendering()
    {
        var defaults = new ClientSettings();
        Assert(defaults.EspEnabled && defaults.DrawNames && defaults.DrawHealth && defaults.DrawDistance && defaults.TeamCheckEnabled, "Minimal-core ESP defaults were not enabled.");
        Assert(!defaults.CornerBoxes && !defaults.DrawWeapons && !defaults.DrawBombEsp && !defaults.DrawItemEsp && !defaults.DrawGrenadePrediction && defaults.GrenadePredictionMap == "Auto" && !defaults.DrawSnaplines && !defaults.DrawOffscreenArrows && !defaults.DrawRadar && !defaults.DrawSkeleton && !defaults.DrawHeadMarker && !defaults.HideOverlayFromCapture, "Optional ESP details or Streamproof were enabled by default.");

        var start = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        AssertClose(.55f, (float)OverlaySurface.AppearanceOpacity(start, start));
        AssertClose(1, (float)OverlaySurface.AppearanceOpacity(start, start.AddMilliseconds(55)));
        AssertClose(0, (float)OverlaySurface.HealthProgress(start, start));
        AssertClose(.5f, (float)OverlaySurface.HealthProgress(start, start.AddMilliseconds(75)));
        AssertClose(1, (float)OverlaySurface.HealthProgress(start, start.AddMilliseconds(150)));

        var align = typeof(OverlaySurface).GetMethod("AlignCoordinate", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var scale in new[] { 1d, 1.25d, 1.5d }) {
            var aligned = (double)align.Invoke(null, new object[] { 37.31d, scale })!;
            var physical = aligned * scale;
            Assert(Math.Abs(physical - Math.Floor(physical) - .5) < .0001, $"Pixel alignment failed at {scale:P0} DPI scale.");
        }

        const int width = 640, height = 420;
        var matrix = new float[16]; matrix[0] = .01f; matrix[5] = .01f; matrix[15] = 1;
        var player = new PlayerSnapshot(2, 2, 2, 2, 62, true, false, "Spectator Target", 384, new WeaponSnapshot(true, 7, "AK-47", 19, 30), default, default, new Vec3(-16, -16, 0), new Vec3(16, 16, 72), true, false) { HasVisibilityData = true, IsVisible = true };
        var snapshot = new GameSnapshot(true, 1, default, 35, true, -1, RecoilSnapshot.Unavailable, matrix, new[] { player }, DateTimeOffset.UtcNow, 14171, TimeSpan.Zero, 120);
        var settings = new ClientSettings { TeamCheckEnabled = true, DrawWeapons = true, DrawRadar = true, DrawSkeleton = false, DrawHeadMarker = true, AimAssistEnabled = true, DrawAimFov = true };
        var surface = new OverlaySurface { HasGameBounds = true, Snapshot = snapshot, Settings = settings, AimReport = new(AimAssistState.Locked, "synthetic target", true, 2, AimTargetPoint.Head) };
        surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height));
        var radarFrame = Render(surface, width, height);
        var radarPixels = new byte[width * height * 4]; radarFrame.CopyPixels(radarPixels, width * 4, 0);
        var radarVisible = 0;
        for (var y = 10; y < 155; y++) for (var x = 10; x < 155; x++) if (radarPixels[(y * width + x) * 4 + 3] != 0) radarVisible++;
        Assert(radarVisible > 300, "Glass radar did not render in the expected viewport region.");
        Thread.Sleep(170);
        surface.InvalidateVisual(); surface.UpdateLayout();
        var bitmap = Render(surface, width, height);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var visiblePixels = pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0);
        Assert(visiblePixels > 500, "Synthetic premium ESP frame did not render its expected visual elements.");
        var centerVisible = 0;
        for (var y = 100; y < 330; y++) for (var x = 220; x < 420; x++) if (pixels[(y * width + x) * 4 + 3] != 0) centerVisible++;
        Assert(centerVisible > 150, "Synthetic player box, health bar, and labels were not rendered in the expected viewport region.");
        var previewPath = Path.Combine(Path.GetTempPath(), "vectra-external-aim-overlay-preview.png"); SavePreview(bitmap, previewPath);
        Console.WriteLine($"Synthetic aim/ESP preview: {previewPath}");

        var skeletonJoints = PlayerSnapshotBones.EmptyJoints; var skeletonValid = PlayerSnapshotBones.EmptyValidity;
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.Pelvis, new(0, 0, 18)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.SpineLower, new(0, 0, 32));
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.SpineUpper, new(0, 0, 47)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.Neck, new(0, 0, 58)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.Head, new(0, 0, 69));
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftUpperArm, new(-12, 0, 55)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftLowerArm, new(-24, 0, 47)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftHand, new(-32, 0, 42));
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightUpperArm, new(12, 0, 55)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightLowerArm, new(24, 0, 47)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightHand, new(32, 0, 42));
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftUpperLeg, new(-7, 0, 16)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftLowerLeg, new(-8, 0, -5)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.LeftAnkle, new(-9, 0, -24));
        SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightUpperLeg, new(7, 0, 16)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightLowerLeg, new(8, 0, -5)); SetJoint(skeletonJoints, skeletonValid, SkeletonJoint.RightAnkle, new(9, 0, -24));
        var skeletonPlayer = player with { SkeletonJoints = skeletonJoints, HasSkeletonJoint = skeletonValid };
        var skeletonMatrix = new float[16]; skeletonMatrix[0] = .01f; skeletonMatrix[6] = .01f; skeletonMatrix[15] = 1;
        var skeletonSettings = new ClientSettings { TeamCheckEnabled = false, DrawNames = false, DrawHealth = false, DrawDistance = false };
        var skeletonSurface = new OverlaySurface { HasGameBounds = true, Snapshot = snapshot with { ViewMatrix = skeletonMatrix, Players = new[] { skeletonPlayer }, CapturedAt = DateTimeOffset.UtcNow }, Settings = skeletonSettings };
        skeletonSurface.Measure(new Size(width, height)); skeletonSurface.Arrange(new Rect(0, 0, width, height)); _ = Render(skeletonSurface, width, height); Thread.Sleep(170);
        var withoutSkeleton = Render(skeletonSurface, width, height); var withoutPixels = new byte[width * height * 4]; withoutSkeleton.CopyPixels(withoutPixels, width * 4, 0);
        skeletonSettings.DrawSkeleton = true; skeletonSurface.InvalidateVisual(); skeletonSurface.UpdateLayout();
        var withSkeleton = Render(skeletonSurface, width, height); var withPixels = new byte[width * height * 4]; withSkeleton.CopyPixels(withPixels, width * 4, 0);
        Assert(withPixels.Zip(withoutPixels).Count(pair => pair.First != pair.Second) > 100, "Clean full-body skeleton did not add visible overlay lines.");

        surface.Snapshot = snapshot with { Players = Array.Empty<PlayerSnapshot>(), CapturedAt = DateTimeOffset.UtcNow };
        surface.InvalidateVisual(); surface.UpdateLayout(); _ = Render(surface, width, height); Thread.Sleep(170); surface.InvalidateVisual(); surface.UpdateLayout(); _ = Render(surface, width, height);
        var states = (System.Collections.IDictionary)typeof(OverlaySurface).GetField("_visualStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
        Assert(states.Count == 0, "Expired player visual state was not removed after its 150 ms fade-out.");
    }

    private static RenderTargetBitmap Render(OverlaySurface surface, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface); return bitmap;
    }

    private static void SavePreview(Visual visual, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        SavePreview(bitmap, path);
    }

    private static void SavePreview(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var preview = File.Create(path); encoder.Save(preview);
    }

    private static void ValidatesSkeletonGeometryAndLayout()
    {
        var names = new[] { "pelvis", "leg_upper_l", "leg_lower_l", "ankle_l", "leg_upper_r", "leg_lower_r", "ankle_r", "head", "neck_0", "spine_2", "spine_1", "arm_upper_l", "arm_lower_l", "hand_l", "arm_upper_r", "arm_lower_r", "hand_r" };
        var parents = Enumerable.Repeat((short)-1, names.Length).ToArray();
        parents[1] = 0; parents[2] = 1; parents[3] = 2; parents[4] = 0; parents[5] = 4; parents[6] = 5; parents[8] = 9; parents[9] = 10; parents[10] = 0;
        Assert(SkeletonLayoutResolver.TryResolve(names, parents, out var indices), "Dynamic skeleton layout was not resolved.");
        Assert(indices[(int)SkeletonJoint.Head] == 7 && indices[(int)SkeletonJoint.LeftAnkle] == 3, "Resolved skeleton indices were incorrect.");
        parents[3] = 4;
        Assert(SkeletonLayoutResolver.TryResolve(names, parents, out var partial), "Partial skeleton layout was discarded.");
        Assert(partial[(int)SkeletonJoint.LeftUpperLeg] >= 0 && partial[(int)SkeletonJoint.LeftLowerLeg] >= 0 && partial[(int)SkeletonJoint.LeftAnkle] < 0, "Invalid leg segment was not isolated from the valid partial chain.");
        var unknownNames = Enumerable.Repeat("unknown", 16).ToArray();
        var unknownParents = Enumerable.Repeat((short)-1, 16).ToArray();
        Assert(SkeletonLayoutResolver.TryCompose(unknownNames, unknownParents, 14170, out var fallback), "Build-14170 fallback was not applied.");
        Assert(fallback[(int)SkeletonJoint.Head] == 6 && fallback[(int)SkeletonJoint.RightHand] == 15, "Fallback upper-body slots were incorrect.");
        Assert(!SkeletonLayoutResolver.TryCompose(unknownNames, unknownParents, 14169, out _), "Fallback was applied to an unsupported build.");
        Assert(SkeletonGeometry.Connections.Count == 16, "Skeleton connection graph is incomplete.");

        var source2Names = new[] { "models/player/pelvis", "leg_upper_l", "knee_helper_l", "leg_lower_l", "ankle_helper_l", "foot_l", "spine_1", "spine_2", "neck_0", "arm_upper_l", "arm_twist_l", "forearm_l", "hand_l", "head_0" };
        var source2Parents = new short[] { -1, 0, 1, 2, 3, 4, 0, 6, 7, 8, 9, 10, 11, 8 };
        Assert(SkeletonLayoutResolver.TryResolve(source2Names, source2Parents, out var source2Layout), "Source 2 skeleton with helper bones was rejected.");
        Assert(source2Layout[(int)SkeletonJoint.LeftAnkle] == 5 && source2Layout[(int)SkeletonJoint.LeftHand] == 12, "Helper-bone ancestor traversal resolved the wrong joints.");
        Assert(!SkeletonLayoutResolver.HasValidParentTable(new short[] { 1, 0 }), "Cyclic parent data was accepted.");
        Assert(!SkeletonLayoutResolver.HasValidParentTable(new short[] { -1, 4 }), "Out-of-range parent data was accepted.");
        Assert(SkeletonMemoryLayout.IsValidVectorHeader((nint)0x10000, 128), "Valid Source 2 vector header was rejected.");
        Assert(!SkeletonMemoryLayout.IsValidVectorHeader((nint)0x10001, 128) && !SkeletonMemoryLayout.IsValidVectorHeader((nint)0x10000, 257), "Invalid Source 2 vector header was accepted.");
        Assert(SkeletonMemoryLayout.ModelResourcePointerOffsets.SequenceEqual(new[] { 0, 8, 16, 24 }), "Model resource candidates are no longer explicitly bounded.");

        var joints = PlayerSnapshotBones.EmptyJoints; var valid = PlayerSnapshotBones.EmptyValidity;
        joints[(int)SkeletonJoint.Head] = new Vec3(0, 0, 20); joints[(int)SkeletonJoint.Neck] = new Vec3(0, 0, 15); valid[(int)SkeletonJoint.Head] = valid[(int)SkeletonJoint.Neck] = true;
        var player = new PlayerSnapshot(1, 1, 1, 2, 100, true, false, "target", 100, WeaponSnapshot.Unavailable, default, default, new Vec3(-16, -16, 0), new Vec3(16, 16, 72), true, false) { SkeletonJoints = joints, HasSkeletonJoint = valid };
        var matrix = new float[16]; matrix[0] = .01f; matrix[5] = .01f; matrix[15] = 1;
        Assert(SkeletonGeometry.TryGetHeadMarker(player, default, matrix, 1920, 1080, 30, out var marker), "Valid head marker was rejected.");
        Assert(marker.Radius is >= 3 and <= 16, "Head marker radius was not bounded.");
        valid[(int)SkeletonJoint.Head] = false; valid[(int)SkeletonJoint.Neck] = false; player = player with { HasSkeletonJoint = valid };
        Assert(SkeletonGeometry.TryGetHeadMarker(player, default, matrix, 1920, 1080, 30, out _), "Bounds fallback head marker was rejected.");

        var pose = PlayerSnapshotBones.EmptyJoints; var poseValid = PlayerSnapshotBones.EmptyValidity;
        SetJoint(pose, poseValid, SkeletonJoint.Pelvis, new(0, 0, 18)); SetJoint(pose, poseValid, SkeletonJoint.SpineLower, new(0, 0, 32));
        SetJoint(pose, poseValid, SkeletonJoint.SpineUpper, new(0, 0, 48)); SetJoint(pose, poseValid, SkeletonJoint.Neck, new(0, 0, 60)); SetJoint(pose, poseValid, SkeletonJoint.Head, new(0, 0, 70));
        SetJoint(pose, poseValid, SkeletonJoint.LeftUpperArm, new(-16, 0, 56)); SetJoint(pose, poseValid, SkeletonJoint.LeftLowerArm, new(-36, 0, 50)); SetJoint(pose, poseValid, SkeletonJoint.LeftHand, new(-55, 0, 47));
        SetJoint(pose, poseValid, SkeletonJoint.RightUpperArm, new(16, 0, 56)); SetJoint(pose, poseValid, SkeletonJoint.RightLowerArm, new(36, 0, 50)); SetJoint(pose, poseValid, SkeletonJoint.RightHand, new(55, 0, 47));
        Assert(SkeletonPoseValidator.ValidatePose(pose, poseValid, default, new(-16, -16, 0), new(16, 16, 72), true), "Animated full-body envelope rejected extended arms.");
        pose[(int)SkeletonJoint.LeftHand] = new(500, 0, 47);
        Assert(SkeletonPoseValidator.ValidatePose(pose, poseValid, default, new(-16, -16, 0), new(16, 16, 72), true), "One invalid limb discarded the complete pose.");
        Assert(!poseValid[(int)SkeletonJoint.LeftHand] && poseValid[(int)SkeletonJoint.RightHand], "Partial-pose validation did not isolate the invalid limb.");
        Assert(!GameSnapshot.Empty(14171).Skeleton.Requested, "Empty snapshots unexpectedly expose skeleton capture data.");
    }

    private static void SetJoint(Vec3[] joints, bool[] valid, SkeletonJoint joint, Vec3 value)
    {
        joints[(int)joint] = value; valid[(int)joint] = true;
    }

    private static void ValidatesBotSkeletonFallback()
    {
        var cache = new Vec3[16];
        cache[0] = new(0, 0, 20); cache[2] = new(0, 0, 35); cache[4] = new(0, 0, 50); cache[5] = new(0, 0, 60); cache[6] = new(0, 0, 70);
        cache[8] = new(-10, 0, 55); cache[9] = new(-22, 0, 50); cache[10] = new(-31, 0, 47);
        cache[13] = new(10, 0, 55); cache[14] = new(22, 0, 50); cache[15] = new(31, 0, 47);
        var joints = PlayerSnapshotBones.EmptyJoints; var valid = PlayerSnapshotBones.EmptyValidity;
        Assert(SkeletonPoseValidator.TryApplyBuild14170UpperBody(cache, default, new Vec3(-32, -32, 0), new Vec3(32, 32, 100), true, joints, valid), "Bot skeleton fallback was rejected.");
        Assert(SkeletonPoseValidator.HasRenderableSkeleton(valid), "Bot skeleton fallback did not produce a drawable connection.");
        cache[10] = new(1000, 0, 47);
        var rejectedJoints = PlayerSnapshotBones.EmptyJoints; var rejectedValid = PlayerSnapshotBones.EmptyValidity;
        Assert(!SkeletonPoseValidator.TryApplyBuild14170UpperBody(cache, default, new Vec3(-32, -32, 0), new Vec3(32, 32, 100), true, rejectedJoints, rejectedValid), "Invalid bot fallback pose was accepted.");
        Assert(!rejectedValid.Any(value => value), "Rejected bot fallback left drawable joints behind.");
    }

    private static void ValidatesBombEspStateAndRendering()
    {
        Assert(BombEsp.ResolvePlantedState(true, true, false, false, true) == BombState.Planted, "Active planted C4 was rejected.");
        Assert(BombEsp.ResolvePlantedState(true, true, true, false, true) == BombState.Unavailable, "Exploded C4 remained active.");
        Assert(BombEsp.ResolvePlantedState(true, true, false, true, true) == BombState.Unavailable, "Defused C4 remained active.");
        Assert(BombEsp.ResolvePlantedState(true, true, false, false, false) == BombState.Unavailable, "Planted C4 with invalid origin was accepted.");
        Assert(BombEsp.ResolveWeaponState(false, 42, true) == BombState.Carried, "Owned C4 was not classified as carried.");
        Assert(BombEsp.ResolveWeaponState(false, 0xFFFFFFFF, true) == BombState.Dropped, "Ownerless C4 was not classified as dropped.");
        Assert(BombEsp.ResolveWeaponState(true, 42, true) == BombState.Unavailable, "Weapon C4 remained active after planting.");
        Assert(BombEsp.ResolveWeaponState(false, 42, false) == BombState.Unavailable, "Weapon C4 with invalid origin was accepted.");

        AssertClose(30, BombEsp.RemainingSeconds(130, 100, 40)!.Value);
        AssertClose(0, BombEsp.RemainingSeconds(99.9f, 100, 40)!.Value);
        Assert(BombEsp.RemainingSeconds(90, 100, 40) is null, "Implausible negative bomb time was accepted.");
        Assert(BombEsp.RemainingSeconds(float.NaN, 100, 40) is null, "Invalid bomb time was accepted.");
        Assert(BombEsp.SiteLabel(0) == "A" && BombEsp.SiteLabel(1) == "B" && BombEsp.SiteLabel(9) == "?", "Bombsite labels were incorrect.");

        const int width = 640, height = 420;
        var matrix = new float[16]; matrix[0] = .01f; matrix[5] = .01f; matrix[15] = 1;
        var planted = new BombSnapshot(BombState.Planted, default, 250, 0, 0, 140, 12.4f, true, false, 105, 3.2f);
        Assert(BombEsp.Label(planted) == "C4 · SITE A · 12.4s" && BombEsp.DefuseLabel(planted) == "DEFUSE · 3.2s", "Planted/defuse labels were incorrect.");
        var snapshot = new GameSnapshot(true, 1, default, 0, true, -1, RecoilSnapshot.Unavailable, matrix, Array.Empty<PlayerSnapshot>(), DateTimeOffset.UtcNow, 14171, TimeSpan.Zero, 120) { Bomb = planted };
        var surface = new OverlaySurface { HasGameBounds = true, Snapshot = snapshot, Settings = new ClientSettings { DrawBombEsp = true } };
        surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height));
        var enabledPixels = PixelCount(Render(surface, width, height), width, height, 0, width, 0, height);
        Assert(enabledPixels > 80, "On-screen planted Bomb ESP did not render.");

        var disabled = new OverlaySurface { HasGameBounds = true, Snapshot = snapshot, Settings = new ClientSettings { DrawBombEsp = false } };
        disabled.Measure(new Size(width, height)); disabled.Arrange(new Rect(0, 0, width, height));
        Assert(PixelCount(Render(disabled, width, height), width, height, 0, width, 0, height) == 0, "Disabled Bomb ESP still rendered.");

        var offscreenSnapshot = snapshot with { Bomb = planted with { State = BombState.Dropped, Origin = new Vec3(2000, 0, 0), BombSite = -1, ExplosionTime = null, ExplosionRemainingSeconds = null, BeingDefused = false, DefuseTime = null, DefuseRemainingSeconds = null } };
        var offscreen = new OverlaySurface { HasGameBounds = true, Snapshot = offscreenSnapshot, Settings = new ClientSettings { DrawBombEsp = true } };
        offscreen.Measure(new Size(width, height)); offscreen.Arrange(new Rect(0, 0, width, height));
        Assert(PixelCount(Render(offscreen, width, height), width, height, 460, 540, 170, 250) > 10, "Off-screen Bomb ESP indicator did not render near the viewport edge.");
    }

    private static void ValidatesIconAssetsAndWorldRendering()
    {
        Assert(SvgIconCatalog.Get("mp5sd.svg") is not null, "Embedded MP5-SD SVG could not be parsed.");
        Assert(SvgIconCatalog.Get("icon_defuse_default.svg") is not null, "Embedded defuse-kit SVG could not be parsed.");
        Assert(SvgIconCatalog.Get("../outside.svg") is null, "SVG catalog accepted a non-local fallback path.");
        Assert(!SvgIconCatalog.HealthshotGeometry().Bounds.IsEmpty, "Healthshot cross geometry was empty.");

        const int width = 640, height = 420; var matrix = new float[16]; matrix[0] = .01f; matrix[5] = .01f; matrix[15] = 1;
        var items = new WorldItemSnapshot[] {
            new(100, WorldItemKind.Weapon, 7, "AK-47", "weapon_ak47", new Vec3(-20, 0, 0), 120, false),
            new(101, WorldItemKind.DefuseKit, 0, "Defuse Kit", "item_defuser", new Vec3(20, 0, 0), 140, false),
            new(102, WorldItemKind.Healthshot, 57, "Healthshot", "weapon_healthshot", new Vec3(0, 20, 0), 150, false)
        };
        var snapshot = new GameSnapshot(true, 1, default, 0, true, -1, RecoilSnapshot.Unavailable, matrix, Array.Empty<PlayerSnapshot>(), DateTimeOffset.UtcNow, 14171, TimeSpan.Zero, 120) { WorldItems = items };
        var trajectory = new GrenadeTrajectory(new[] { new Vec3(-20, -20, 0), new Vec3(0, 0, 0), new Vec3(20, 20, 0) }, new[] { 1 }, GrenadeKind.HighExplosive, TrajectoryQuality.CollisionAware, DateTimeOffset.UtcNow);
        var surface = new OverlaySurface { HasGameBounds = true, Snapshot = snapshot, GrenadeTrajectory = trajectory, Settings = new ClientSettings { EspEnabled = true, DrawItemEsp = true, DrawGrenadePrediction = true } };
        surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height));
        Assert(PixelCount(Render(surface, width, height), width, height, 0, width, 0, height) > 100, "World-item icons or collision trajectory did not render.");
        trajectory = trajectory with { Quality = TrajectoryQuality.Approximate, BouncePointIndices = Array.Empty<int>() }; surface.GrenadeTrajectory = trajectory; surface.InvalidateVisual();
        Assert(PixelCount(Render(surface, width, height), width, height, 0, width, 0, height) > 80, "Approximate dashed trajectory did not render.");
    }

    private static int PixelCount(BitmapSource bitmap, int width, int height, int left, int right, int top, int bottom)
    {
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); var count = 0;
        for (var y = Math.Clamp(top, 0, height); y < Math.Clamp(bottom, 0, height); y++)
            for (var x = Math.Clamp(left, 0, width); x < Math.Clamp(right, 0, width); x++)
                if (pixels[(y * width + x) * 4 + 3] != 0) count++;
        return count;
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindVisual<T>(VisualTreeHelper.GetChild(root, i));
            if (result is not null) return result;
        }
        return null;
    }

    private static T? FindVisualByAutomationName<T>(DependencyObject root, string name) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetName(root) == name) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindVisualByAutomationName<T>(VisualTreeHelper.GetChild(root, i), name);
            if (result is not null) return result;
        }
        return null;
    }

    private static TextBlock? FindVisualByText(DependencyObject root, string text)
    {
        if (root is TextBlock block && block.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) return block;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindVisualByText(VisualTreeHelper.GetChild(root, i), text);
            if (result is not null) return result;
        }
        return null;
    }

    private static List<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        var matches = new List<T>();
        if (root is T match) matches.Add(match);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) matches.AddRange(FindVisuals<T>(VisualTreeHelper.GetChild(root, i)));
        return matches;
    }

    private static void AssertClose(float expected, float actual) => Assert(MathF.Abs(expected - actual) <= Tolerance, $"Expected {expected}, received {actual}.");
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
