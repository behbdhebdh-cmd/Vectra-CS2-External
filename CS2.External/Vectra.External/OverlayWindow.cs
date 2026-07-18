using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Vectra.External;

public sealed class OverlayWindow : Window
{
    private const double BoundsChangeTolerance = .25;
    private static readonly TimeSpan TopmostRecoveryInterval = TimeSpan.FromSeconds(1);
    private readonly OverlaySurface _surface = new();
    private ExternalRuntime? _runtime;
    private bool? _captureExclusionRequested;
    private bool _hasBounds;
    private int _lastLeft, _lastTop, _lastWidth, _lastHeight;
    private DateTimeOffset _lastValidBoundsAt;
    private DateTimeOffset _lastTopmostAt;
    private long _lastMaintenanceTick;
    private long _lastObservedSnapshotSequence;
    private DateTimeOffset _lastBoundsPollAt;
    private double _lastDpiScale;
    private GameSnapshot? _lastPresentedSnapshot;
    private AimAssistReport _lastPresentedAim = AimAssistReport.Disabled;
    private GrenadeTrajectory _lastPresentedTrajectory = GrenadeTrajectory.Unavailable;
    private int _lastSettingsStamp;
    public bool CaptureExclusionApplied { get; private set; }
    public bool CaptureAffinityAvailable { get; private set; }
    public bool StreamproofRequested => _captureExclusionRequested == true;
    public bool IsAttachedToGame { get; private set; }
    public OverlayReport Report { get; private set; } = OverlayReport.Waiting;
    public OverlayWindow()
    {
        Content = _surface; AllowsTransparency = true; Background = Brushes.Transparent; WindowStyle = WindowStyle.None;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false; Focusable = false;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }
    public void Attach(ExternalRuntime runtime) { _runtime = runtime; if (!IsVisible) Show(); EnsureTopmost(); CompositionTarget.Rendering -= OnRendering; CompositionTarget.Rendering += OnRendering; }
    private void OnRendering(object? sender, EventArgs args)
    {
        if (_runtime is null) return;
        var foreground = _runtime.Session.Ready && _runtime.GameForeground;
        var interval = OverlayCadence.Select(_runtime.Session.Ready, foreground);
        var now = Stopwatch.GetTimestamp();
        var sequence = _runtime.SnapshotSequence;
        var trajectory = _runtime.GrenadeTrajectory;
        var aim = _runtime.AimAssistReport;
        var settingsStamp = VisualSettingsStamp(_runtime.Settings);
        var visualChange = sequence != _lastObservedSnapshotSequence || !ReferenceEquals(_lastPresentedTrajectory, trajectory) || !Equals(_lastPresentedAim, aim) || _lastSettingsStamp != settingsStamp;
        if (!OverlayUpdatePolicy.ShouldUpdate(visualChange, _lastMaintenanceTick, now, interval)) return;
        _lastMaintenanceTick = now; _lastObservedSnapshotSequence = sequence;
        Update(_runtime.Latest, _runtime.Session, _runtime.Settings, foreground);
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlpExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlpExStyle, new nint(style | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate));
        EnsureTopmost();
    }
    public void Update(GameSnapshot snapshot, GameProcessSession session, ClientSettings settings)
        => Update(snapshot, session, settings, session.Ready && session.OwnsForegroundWindow());

    private void Update(GameSnapshot snapshot, GameProcessSession session, ClientSettings settings, bool foreground)
    {
        ApplyCaptureExclusion(settings.HideOverlayFromCapture);
        _surface.Settings = settings;
        if (!session.Ready) {
            var state = session.Report.State == ReaderState.BuildMismatch ? OverlayState.BuildMismatch : OverlayState.WaitingForProcess;
            ClearOverlayState(state, session.Report.Message);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var boundsInterval = foreground ? TimeSpan.FromMilliseconds(33) : TimeSpan.FromMilliseconds(250);
        var pollBounds = !_hasBounds || _lastBoundsPollAt == default || now - _lastBoundsPollAt >= boundsInterval;
        var hasCurrentBounds = false; var left = _lastLeft; var top = _lastTop; var width = _lastWidth; var height = _lastHeight;
        if (pollBounds) { _lastBoundsPollAt = now; hasCurrentBounds = NativeMethods.TryGetClientBounds(session.WindowHandle, out left, out top, out width, out height); }
        var boundsChanged = false;
        if (hasCurrentBounds) {
            boundsChanged = !_hasBounds || left != _lastLeft || top != _lastTop || width != _lastWidth || height != _lastHeight;
            _hasBounds = true;
            _lastValidBoundsAt = DateTimeOffset.UtcNow;
            _lastLeft = left; _lastTop = top; _lastWidth = width; _lastHeight = height;
        } else if (pollBounds && (!_hasBounds || !SnapshotTiming.IsWithinGrace(_lastValidBoundsAt, now, SnapshotTiming.OverlayGrace))) {
            ClearOverlayState(OverlayState.WaitingForBounds, "Waiting for valid CS2 client bounds");
            return;
        }

        ApplyBounds(_lastLeft, _lastTop, _lastWidth, _lastHeight, boundsChanged);
        _surface.HasGameBounds = true;
        _surface.FovScale = _lastWidth > 0 ? ActualWidth / _lastWidth : 1;
        var presentedSnapshot = snapshot.IsRenderable ? snapshot : null;
        _surface.Snapshot = presentedSnapshot;
        var aim = _runtime?.AimAssistReport ?? _surface.AimReport;
        _surface.AimReport = aim;
        var trajectory = _runtime?.GrenadeTrajectory ?? _surface.GrenadeTrajectory;
        _surface.GrenadeTrajectory = trajectory;

        if (boundsChanged || _lastTopmostAt == default || now - _lastTopmostAt >= TopmostRecoveryInterval)
            IsAttachedToGame = EnsureTopmost();

        var zOrderStatus = IsAttachedToGame ? string.Empty : "; topmost recovery pending";
        var nextReport = snapshot.IsRenderable
            ? new OverlayReport(OverlayState.Active, "Overlay active" + zOrderStatus)
            : new OverlayReport(OverlayState.WaitingForSnapshot, "FOV active; waiting for a fresh ESP snapshot" + zOrderStatus);
        var reportChanged = !Equals(Report, nextReport); Report = nextReport;
        var settingsStamp = VisualSettingsStamp(settings);
        if (boundsChanged || reportChanged || !ReferenceEquals(_lastPresentedSnapshot, presentedSnapshot) || !Equals(_lastPresentedAim, aim) || !ReferenceEquals(_lastPresentedTrajectory, trajectory) || _lastSettingsStamp != settingsStamp)
        {
            _lastPresentedSnapshot = presentedSnapshot; _lastPresentedAim = aim; _lastPresentedTrajectory = trajectory; _lastSettingsStamp = settingsStamp;
            _surface.InvalidateVisual();
        }
    }

    private void ApplyBounds(int left, int top, int width, int height, bool refreshDpi)
    {
        if (refreshDpi || _lastDpiScale <= 0) _lastDpiScale = Math.Max(NativeMethods.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96d, .1d);
        var scale = _lastDpiScale;
        var nextLeft = left / scale; var nextTop = top / scale; var nextWidth = width / scale; var nextHeight = height / scale;
        if (!double.IsFinite(Left) || Math.Abs(Left - nextLeft) > BoundsChangeTolerance) Left = nextLeft;
        if (!double.IsFinite(Top) || Math.Abs(Top - nextTop) > BoundsChangeTolerance) Top = nextTop;
        if (!double.IsFinite(Width) || Math.Abs(Width - nextWidth) > BoundsChangeTolerance) Width = nextWidth;
        if (!double.IsFinite(Height) || Math.Abs(Height - nextHeight) > BoundsChangeTolerance) Height = nextHeight;
    }

    private void ClearOverlayState(OverlayState state, string message)
    {
        var changed = _hasBounds || _surface.Snapshot is not null || Report.State != state || Report.Message != message;
        _hasBounds = false;
        IsAttachedToGame = false;
        _surface.HasGameBounds = false;
        _surface.Snapshot = null;
        var next = new OverlayReport(state, message); Report = next;
        if (changed) { _lastPresentedSnapshot = null; _surface.InvalidateVisual(); }
    }

    private bool EnsureTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return false;
        _lastTopmostAt = DateTimeOffset.UtcNow;
        var applied = NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
        return applied;
    }

    private void ApplyCaptureExclusion(bool requested)
    {
        if (_captureExclusionRequested == requested) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        CaptureAffinityAvailable = NativeMethods.SetWindowDisplayAffinity(handle, requested ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
        CaptureExclusionApplied = requested && CaptureAffinityAvailable;
        _captureExclusionRequested = requested;
    }

    private static int VisualSettingsStamp(ClientSettings settings)
    {
        var hash = new HashCode();
        hash.Add(settings.EspEnabled); hash.Add(settings.TeamCheckEnabled); hash.Add(settings.CornerBoxes); hash.Add(settings.DrawNames); hash.Add(settings.DrawHealth); hash.Add(settings.DrawDistance);
        hash.Add(settings.DrawWeapons); hash.Add(settings.DrawBombEsp); hash.Add(settings.DrawSnaplines); hash.Add(settings.DrawOffscreenArrows); hash.Add(settings.DrawRadar);
        hash.Add(settings.DrawItemEsp); hash.Add(settings.DrawGrenadePrediction);
        hash.Add(settings.HideOverlayFromCapture);
        hash.Add(settings.DrawSkeleton); hash.Add(settings.DrawHeadMarker); hash.Add(settings.EspTheme); hash.Add(settings.AimAssistEnabled); hash.Add(settings.DrawAimFov); hash.Add(settings.AimAssistFovPixels);
        return hash.ToHashCode();
    }
}

public sealed class OverlaySurface : FrameworkElement
{
    internal static readonly TimeSpan AppearanceTransition = TimeSpan.FromMilliseconds(55);
    internal static readonly TimeSpan HealthTransition = TimeSpan.FromMilliseconds(150);
    internal const double MinimumAppearanceOpacity = .55;
    private const double RadarMargin = 18;
    private const double RadarRadius = 62;
    private const float RadarRangeUnits = 1200;
    private static readonly Brush RadarFill = RadialBrush(Color.FromArgb(142, 34, 39, 50), Color.FromArgb(102, 16, 20, 28));
    private static readonly Brush RadarGrid = SolidBrush(Color.FromArgb(68, 207, 216, 226));
    private static readonly Brush RadarLocal = SolidBrush(Color.FromArgb(232, 238, 242, 246));
    private static readonly Brush TeamBrush = SolidBrush(Color.FromRgb(147, 169, 191));
    private static readonly Brush TextPrimary = SolidBrush(Color.FromRgb(238, 242, 246));
    private static readonly Brush TextShadow = SolidBrush(Color.FromArgb(92, 4, 7, 11));
    private static readonly Brush PillFill = SolidBrush(Color.FromArgb(126, 18, 22, 29));
    private static readonly Brush PillBorder = SolidBrush(Color.FromArgb(62, 225, 232, 240));
    private static readonly Brush SoftOutline = SolidBrush(Color.FromArgb(76, 3, 6, 10));
    private static readonly Brush HealthTrack = SolidBrush(Color.FromArgb(118, 12, 16, 21));
    private static readonly Brush HealthGradient = HealthGradientBrush();
    private static readonly Brush Lavender = SolidBrush(Color.FromRgb(174, 180, 214));
    private static readonly Brush Glacier = SolidBrush(Color.FromRgb(145, 187, 193));
    private static readonly Brush Rose = SolidBrush(Color.FromRgb(195, 160, 175));
    private static readonly Brush BombBrush = SolidBrush(Color.FromRgb(218, 181, 108));
    private static readonly Brush DefuseBrush = SolidBrush(Color.FromRgb(126, 190, 205));
    private static readonly Pen Outline24 = FrozenPen(SoftOutline, 2.4);
    private static readonly Pen Outline2 = FrozenPen(SoftOutline, 2);
    private static readonly Pen Outline07 = FrozenPen(SoftOutline, .7);
    private static readonly Pen Outline08 = FrozenPen(SoftOutline, .8);
    private static readonly Pen Outline075 = FrozenPen(SoftOutline, .75);
    private static readonly Pen RadarGridPen = FrozenPen(RadarGrid, .7);
    private static readonly Pen PillPen = FrozenPen(PillBorder, .65);
    private static readonly ThemeDrawingResources LavenderResources = new(Lavender);
    private static readonly ThemeDrawingResources GlacierResources = new(Glacier);
    private static readonly ThemeDrawingResources RoseResources = new(Rose);
    private static readonly ThemeDrawingResources TeamResources = new(TeamBrush);
    private static readonly ThemeDrawingResources BombResources = new(BombBrush);
    private readonly Dictionary<uint, TrackSample> _tracks = new();
    private readonly Dictionary<uint, Vec3> _frameOrigins = new();
    private readonly Dictionary<uint, PlayerVisualState> _visualStates = new();
    private readonly Dictionary<(int X, int Y), int> _itemOverlap = new();
    private readonly HashSet<uint> _presentVisuals = new();
    private readonly List<uint> _staleVisuals = new();
    public GameSnapshot? Snapshot { get; set; }
    public ClientSettings? Settings { get; set; }
    public AimAssistReport AimReport { get; set; } = AimAssistReport.Disabled;
    public GrenadeTrajectory GrenadeTrajectory { get; set; } = GrenadeTrajectory.Unavailable;
    public bool HasGameBounds { get; set; }
    public double FovScale { get; set; } = 1;

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var settings = Settings;
        if (settings is null || !HasGameBounds || ActualWidth <= 1 || ActualHeight <= 1) return;
        _frameOrigins.Clear();
        if (settings.AimAssistEnabled && settings.DrawAimFov) DrawAimFov(drawing, settings);
        var snapshot = Snapshot;
        if (snapshot is not null && settings.AimAssistEnabled && AimReport.ActivationActive && AimReport.HasTarget) DrawAimTarget(drawing, snapshot, settings);
        if (snapshot is not null && settings.DrawGrenadePrediction && GrenadeTrajectory.Available) DrawGrenadeTrajectory(drawing, snapshot, settings);
        if (snapshot is null || !settings.EspEnabled) { _visualStates.Clear(); return; }
        if (settings.DrawRadar && snapshot.HasLocalViewYaw) DrawRadar(drawing, snapshot, settings);
        var now = DateTimeOffset.UtcNow;
        _presentVisuals.Clear();
        foreach (var player in snapshot.Players) {
            if (player.IsLocal || !player.Alive || player.Dormant) continue;
            var teammate = player.Team == snapshot.LocalTeam;
            if (settings.TeamCheckEnabled && teammate) continue;
            _presentVisuals.Add(player.PawnEntityIndex);
            var state = GetVisualState(player, snapshot.CapturedAt, now);
            DrawPlayer(drawing, snapshot, settings, state, teammate, FadeIn(state, now));
        }
        _staleVisuals.Clear();
        foreach (var pair in _visualStates) if (!_presentVisuals.Contains(pair.Key)) _staleVisuals.Add(pair.Key);
        foreach (var key in _staleVisuals) {
            var state = _visualStates[key];
            if (state.DisappearedAt == default) state.BeginDisappear(now, FadeIn(state, now));
            var opacity = state.DisappearFromOpacity * (1 - TransitionProgress(state.DisappearedAt, now, AppearanceTransition));
            if (opacity <= 0) { _visualStates.Remove(key); continue; }
            DrawPlayer(drawing, snapshot, settings, state, state.Player.Team == snapshot.LocalTeam, opacity);
        }
        if (settings.DrawItemEsp) DrawWorldItems(drawing, snapshot, settings);
        if (settings.DrawBombEsp && snapshot.Bomb.Available) DrawBomb(drawing, snapshot);
    }

    private void DrawPlayer(DrawingContext drawing, GameSnapshot snapshot, ClientSettings settings, PlayerVisualState state, bool teammate, double opacity)
    {
        if (opacity <= 0) return;
        var player = state.Player;
        var origin = GetPredictedOrigin(player, state.CapturedAt);
        var predictionOffset = new Vec3(origin.X - player.Origin.X, origin.Y - player.Origin.Y, origin.Z - player.Origin.Z);
        var color = teammate ? TeamBrush : EnemyBrush(settings.EspTheme);
        var hasBox = TryGetBounds(player, origin, snapshot.ViewMatrix, ActualWidth, ActualHeight, out var box);
        drawing.PushOpacity(Math.Clamp(opacity, 0, 1));
        if (settings.DrawSkeleton) DrawSkeleton(drawing, player, predictionOffset, snapshot.ViewMatrix, color);
        if (settings.DrawHeadMarker) DrawHeadMarker(drawing, player, predictionOffset, snapshot.ViewMatrix, hasBox ? (float)box.Width : 24, color);
        if (!hasBox) {
            if (settings.DrawOffscreenArrows) DrawOffscreenArrow(drawing, origin, snapshot.ViewMatrix, color);
            drawing.Pop(); return;
        }
        if (settings.DrawSnaplines) drawing.DrawLine(ResourcesFor(color).SnaplinePen, Align(new Point(ActualWidth / 2, ActualHeight)), Align(new Point(box.Left + box.Width / 2, box.Bottom)));
        if (settings.CornerBoxes) DrawCornerBox(drawing, box, color); else DrawBox(drawing, box, color);
        if (settings.DrawHealth) DrawHealth(drawing, state.DisplayedHealth(DateTimeOffset.UtcNow), box);
        if (settings.DrawNames || settings.DrawDistance) DrawTopText(drawing, player, box, color, settings);
        if (settings.DrawWeapons && player.Weapon.Available) DrawWeapon(drawing, player.Weapon, box, color);
        drawing.Pop();
    }

    private void DrawAimFov(DrawingContext drawing, ClientSettings settings)
    {
        var radius = Math.Clamp(settings.AimAssistFovPixels * FovScale, 8, Math.Min(ActualWidth, ActualHeight) * .48);
        var center = Align(new Point(ActualWidth / 2, ActualHeight / 2));
        drawing.DrawEllipse(null, Outline2, center, radius, radius);
        drawing.DrawEllipse(null, EnemyResources(settings.EspTheme).AimPen, center, radius, radius);
    }

    private void DrawAimTarget(DrawingContext drawing, GameSnapshot snapshot, ClientSettings settings)
    {
        var player = snapshot.Players.FirstOrDefault(candidate => candidate.PawnEntityIndex == AimReport.TargetEntityIndex);
        if (player is null || !AimTargeting.IsEligible(player, snapshot, settings)) return;
        var target = AimAssistMath.TargetPoint(player, AimReport.TargetPoint);
        if (!WorldProjection.TryProject(target, snapshot.ViewMatrix, ActualWidth, ActualHeight, out var projected, true)) return;
        var center = Align(new Point(projected.X, projected.Y)); var resources = EnemyResources(settings.EspTheme);
        drawing.DrawEllipse(null, Outline2, center, 6, 6);
        drawing.DrawEllipse(resources.Fill82, resources.TextMarkerPen, center, 2, 2);
        drawing.DrawEllipse(null, resources.TargetPen, center, 6, 6);
    }

    private void DrawSkeleton(DrawingContext drawing, PlayerSnapshot player, Vec3 predictionOffset, float[] matrix, Brush color)
    {
        foreach (var connection in SkeletonGeometry.Connections)
        {
            var from = (int)connection.From; var to = (int)connection.To;
            if (player.HasSkeletonJoint.Length <= Math.Max(from, to) || !player.HasSkeletonJoint[from] || !player.HasSkeletonJoint[to]) continue;
            var first = Add(player.SkeletonJoints[from], predictionOffset); var second = Add(player.SkeletonJoints[to], predictionOffset);
            if (!WorldProjection.TryProject(first, matrix, ActualWidth, ActualHeight, out var projectedFirst) || !WorldProjection.TryProject(second, matrix, ActualWidth, ActualHeight, out var projectedSecond)) continue;
            var start = new Point(projectedFirst.X, projectedFirst.Y); var end = new Point(projectedSecond.X, projectedSecond.Y);
            start = Align(start); end = Align(end);
            drawing.DrawLine(Outline2, start, end); drawing.DrawLine(ResourcesFor(color).FinePen, start, end);
        }
    }

    private void DrawHeadMarker(DrawingContext drawing, PlayerSnapshot player, Vec3 predictionOffset, float[] matrix, float fallbackBoxWidth, Brush color)
    {
        if (!SkeletonGeometry.TryGetHeadMarker(player, predictionOffset, matrix, (float)ActualWidth, (float)ActualHeight, fallbackBoxWidth, out var marker)) return;
        var center = new Point(marker.Center.X, marker.Center.Y);
        var radius = Math.Min(marker.Radius, Math.Clamp(fallbackBoxWidth * .09f, 3, 8));
        drawing.DrawEllipse(null, Outline2, Align(center), radius, radius);
        drawing.DrawEllipse(null, ResourcesFor(color).FinePen, Align(center), radius, radius);
    }

    private void DrawWorldItems(DrawingContext drawing, GameSnapshot snapshot, ClientSettings settings)
    {
        _itemOverlap.Clear();
        foreach (var item in snapshot.WorldItems.OrderBy(item => item.EntityIndex))
        {
            if (item.Dormant || !Vec3.IsFinite(item.Origin) || (item.Kind == WorldItemKind.C4 && settings.DrawBombEsp && snapshot.Bomb.Available)) continue;
            if (!WorldProjection.TryProject(item.Origin, snapshot.ViewMatrix, ActualWidth, ActualHeight, out var projected, true)) continue;
            var key = ((int)(projected.X / 34), (int)(projected.Y / 22)); _itemOverlap.TryGetValue(key, out var level); _itemOverlap[key] = level + 1;
            var center = Align(new Point(projected.X, projected.Y - level * 19)); var descriptor = item.Kind == WorldItemKind.DefuseKit
                ? new ItemIconDescriptor("defuse", "Defuse Kit", null, "icon_defuse_default.svg")
                : ItemIconCatalog.Resolve(item.DefinitionIndex);
            if (!descriptor.HasIcon && !string.IsNullOrWhiteSpace(item.Name)) descriptor = descriptor with { Name = item.Name };
            DrawItemIcon(drawing, descriptor, center, 18, EnemyBrush(settings.EspTheme));
            var distance = $"{item.Distance:F0}u"; var text = Text(distance, TextPrimary, 8.5);
            DrawLabelPill(drawing, distance, 8.5, new Point(center.X - text.Width / 2, center.Y + 10), layout: text);
        }
    }

    private void DrawGrenadeTrajectory(DrawingContext drawing, GameSnapshot snapshot, ClientSettings settings)
    {
        var trajectory = GrenadeTrajectory; if (!trajectory.Available) return;
        var color = EnemyBrush(settings.EspTheme);
        var resources = ResourcesFor(color); var pen = trajectory.Quality == TrajectoryQuality.CollisionAware ? resources.TrajectoryCollisionPen : resources.TrajectoryApproximatePen;
        for (var i = 1; i < trajectory.Points.Count; i++)
        {
            if (!WorldProjection.TryProject(trajectory.Points[i - 1], snapshot.ViewMatrix, ActualWidth, ActualHeight, out var first, true) ||
                !WorldProjection.TryProject(trajectory.Points[i], snapshot.ViewMatrix, ActualWidth, ActualHeight, out var second, true)) continue;
            drawing.DrawLine(pen, Align(new Point(first.X, first.Y)), Align(new Point(second.X, second.Y)));
        }
        foreach (var index in trajectory.BouncePointIndices)
        {
            if (index < 0 || index >= trajectory.Points.Count || !WorldProjection.TryProject(trajectory.Points[index], snapshot.ViewMatrix, ActualWidth, ActualHeight, out var point, true)) continue;
            drawing.DrawEllipse(resources.Fill82, Outline07, Align(new Point(point.X, point.Y)), 3, 3);
        }
        var last = trajectory.Points[^1];
        if (WorldProjection.TryProject(last, snapshot.ViewMatrix, ActualWidth, ActualHeight, out var end, true))
            DrawItemIcon(drawing, ItemIconCatalog.Resolve(GrenadeDefinition(trajectory.Kind)), Align(new Point(end.X, end.Y)), 17, color);
    }

    private void DrawBomb(DrawingContext drawing, GameSnapshot snapshot)
    {
        var bomb = snapshot.Bomb;
        if (!Vec3.IsFinite(bomb.Origin)) return;
        if (!WorldProjection.TryProject(bomb.Origin, snapshot.ViewMatrix, ActualWidth, ActualHeight, out var projected, true))
        {
            DrawOffscreenArrow(drawing, bomb.Origin, snapshot.ViewMatrix, BombBrush);
            return;
        }

        var center = Align(new Point(projected.X, projected.Y));
        var radius = bomb.State == BombState.Planted ? 10d : 8d;
        DrawItemIcon(drawing, ItemIconCatalog.Resolve(49), center, radius * 2, BombBrush);

        var carrier = bomb.CarrierEntityIndex == 0 ? null : snapshot.Players.FirstOrDefault(player => player.PawnEntityIndex == bomb.CarrierEntityIndex);
        var label = BombEsp.Label(bomb, carrier?.Name);
        if (!string.IsNullOrWhiteSpace(label))
        {
            var text = Text(label, TextPrimary, 10);
            DrawLabelPill(drawing, label, 10, new Point(center.X - text.Width / 2, center.Y - radius - text.Height - 7), layout: text);
        }
        var defuse = BombEsp.DefuseLabel(bomb);
        if (!string.IsNullOrWhiteSpace(defuse))
        {
            var text = Text(defuse, DefuseBrush, 9.5);
            DrawLabelPill(drawing, defuse, 9.5, new Point(center.X - text.Width / 2, center.Y + radius + 5), DefuseBrush, text);
        }
    }

    private static ThemeDrawingResources EnemyResources(int theme) => theme switch { 1 => GlacierResources, 2 => RoseResources, _ => LavenderResources };
    private static Brush EnemyBrush(int theme) => EnemyResources(theme).Accent;
    private static ThemeDrawingResources ResourcesFor(Brush color)
        => ReferenceEquals(color, TeamBrush) ? TeamResources : ReferenceEquals(color, Glacier) ? GlacierResources : ReferenceEquals(color, Rose) ? RoseResources : ReferenceEquals(color, BombBrush) ? BombResources : LavenderResources;
    private void DrawBox(DrawingContext drawing, Rect box, Brush color)
    {
        box = Align(box); var radius = Math.Clamp(Math.Min(box.Width, box.Height) * .055, 4, 6);
        drawing.DrawRoundedRectangle(null, Outline24, box, radius, radius);
        drawing.DrawRoundedRectangle(null, ResourcesFor(color).BoxPen, box, radius, radius);
    }
    private void DrawCornerBox(DrawingContext drawing, Rect box, Brush color)
    {
        box = Align(box); var corner = Math.Clamp(Math.Min(box.Width, box.Height) * .25, 7, 22);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Segment(context, box.Left, box.Top, box.Left + corner, box.Top); Segment(context, box.Left, box.Top, box.Left, box.Top + corner);
            Segment(context, box.Right, box.Top, box.Right - corner, box.Top); Segment(context, box.Right, box.Top, box.Right, box.Top + corner);
            Segment(context, box.Left, box.Bottom, box.Left + corner, box.Bottom); Segment(context, box.Left, box.Bottom, box.Left, box.Bottom - corner);
            Segment(context, box.Right, box.Bottom, box.Right - corner, box.Bottom); Segment(context, box.Right, box.Bottom, box.Right, box.Bottom - corner);
        }
        geometry.Freeze(); drawing.DrawGeometry(null, Outline24, geometry); drawing.DrawGeometry(null, ResourcesFor(color).BoxPen, geometry);
    }
    private static void Segment(StreamGeometryContext context, double fromX, double fromY, double toX, double toY)
    {
        context.BeginFigure(new Point(fromX, fromY), false, false); context.LineTo(new Point(toX, toY), true, false);
    }
    private void DrawHealth(DrawingContext drawing, double health, Rect box)
    {
        var ratio = Math.Clamp(health / 100d, 0, 1);
        var track = Align(new Rect(box.Left + 3, box.Top + 5, 3, Math.Max(3, box.Height - 10)));
        drawing.DrawRoundedRectangle(HealthTrack, null, track, 1.5, 1.5);
        var fillHeight = Math.Max(1, track.Height * ratio); var fill = new Rect(track.Left, track.Bottom - fillHeight, track.Width, fillHeight);
        drawing.PushClip(new RectangleGeometry(fill, 1.5, 1.5)); drawing.DrawRoundedRectangle(HealthGradient, null, track, 1.5, 1.5); drawing.Pop();
    }
    private void DrawTopText(DrawingContext drawing, PlayerSnapshot player, Rect box, Brush color, ClientSettings settings)
    {
        var name = settings.DrawNames && !string.IsNullOrWhiteSpace(player.Name) ? player.Name.Length > 18 ? player.Name[..15] + "..." : player.Name : string.Empty;
        var distance = settings.DrawDistance ? $"{player.Distance:F0}u" : string.Empty;
        var value = name.Length > 0 && distance.Length > 0 ? name + "  ·  " + distance : name.Length > 0 ? name : distance;
        if (value.Length == 0) return;
        var text = Text(value, TextPrimary, 10.5); DrawLabelPill(drawing, value, 10.5, new Point(box.Left + (box.Width - text.Width) / 2, box.Top - text.Height - 5), layout: text);
    }
    private void DrawWeapon(DrawingContext drawing, WeaponSnapshot weapon, Rect box, Brush color)
    {
        var descriptor = ItemIconCatalog.Resolve(weapon.DefinitionIndex); var ammo = weapon.MaxClip > 0 ? $"  {weapon.Clip}/{weapon.MaxClip}" : string.Empty;
        var value = descriptor.Glyph is not null ? descriptor.Glyph + ammo : weapon.Name + ammo; var text = descriptor.Glyph is not null ? IconText(value, TextPrimary, 12) : Text(value, TextPrimary, 10);
        DrawLabelPill(drawing, value, descriptor.Glyph is not null ? 12 : 10, new Point(box.Left + (box.Width - text.Width) / 2, box.Bottom + 5), layout: text);
    }

    private void DrawRadar(DrawingContext drawing, GameSnapshot snapshot, ClientSettings settings)
    {
        var center = new Point(RadarMargin + RadarRadius, RadarMargin + RadarRadius);
        if (center.X + RadarRadius > ActualWidth || center.Y + RadarRadius > ActualHeight) return;

        center = Align(center); var ringPen = RadarGridPen;
        drawing.DrawEllipse(SoftOutline, null, center, RadarRadius + 3, RadarRadius + 3);
        drawing.DrawEllipse(RadarFill, EnemyResources(settings.EspTheme).RadarPen, center, RadarRadius, RadarRadius);
        drawing.DrawEllipse(null, ringPen, center, RadarRadius * .33, RadarRadius * .33);
        drawing.DrawEllipse(null, ringPen, center, RadarRadius * .66, RadarRadius * .66);
        drawing.DrawLine(ringPen, new Point(center.X - RadarRadius, center.Y), new Point(center.X + RadarRadius, center.Y));
        drawing.DrawLine(ringPen, new Point(center.X, center.Y - RadarRadius), new Point(center.X, center.Y + RadarRadius));

        var markerRadius = 2.4d;
        var plotRadius = (float)(RadarRadius - markerRadius - 5);
        drawing.PushClip(new EllipseGeometry(center, RadarRadius - 3, RadarRadius - 3));
        foreach (var player in snapshot.Players) {
            if (player.IsLocal || !player.Alive || player.Dormant || player.Distance < 1) continue;
            var teammate = player.Team == snapshot.LocalTeam;
            if (settings.TeamCheckEnabled && teammate) continue;
            var origin = GetPredictedOrigin(player, snapshot.CapturedAt);
            if (!RadarGeometry.TryProject(snapshot.LocalOrigin, origin, snapshot.LocalViewYaw, RadarRangeUnits, plotRadius, out var radarPoint)) continue;
            var color = teammate ? TeamBrush : EnemyBrush(settings.EspTheme);
            var marker = new Point(center.X + radarPoint.X, center.Y + radarPoint.Y);
            var resources = ResourcesFor(color); drawing.DrawEllipse(resources.Fill90, Outline07, Align(marker), markerRadius, markerRadius);
        }
        drawing.Pop();
        DrawRadarLocalMarker(drawing, center);
    }

    private static void DrawRadarLocalMarker(DrawingContext drawing, Point center)
    {
        var marker = new StreamGeometry();
        using (var context = marker.Open()) {
            context.BeginFigure(new Point(center.X, center.Y - 7), true, true);
            context.LineTo(new Point(center.X - 4.5, center.Y + 5), true, false);
            context.LineTo(new Point(center.X, center.Y + 2), true, false);
            context.LineTo(new Point(center.X + 4.5, center.Y + 5), true, false);
        }
        marker.Freeze();
        drawing.DrawGeometry(RadarLocal, Outline08, marker);
    }

    private void DrawLabelPill(DrawingContext drawing, string value, double size, Point origin, Brush? textBrush = null, FormattedText? layout = null)
    {
        var formatted = layout ?? Text(value, textBrush ?? TextPrimary, size); const double horizontal = 5, vertical = 2;
        var pill = Align(new Rect(origin.X - horizontal, origin.Y - vertical, formatted.Width + horizontal * 2, formatted.Height + vertical * 2));
        drawing.DrawRoundedRectangle(PillFill, PillPen, pill, 4, 4);
        var textOrigin = Align(origin);
        if (layout is null) drawing.DrawText(Text(value, TextShadow, size), new Point(textOrigin.X + .5, textOrigin.Y + .5));
        drawing.DrawText(formatted, textOrigin);
    }
    private FormattedText Text(string value, Brush color, double size) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private FormattedText IconText(string value, Brush color, double size) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(ItemIconCatalog.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawItemIcon(DrawingContext drawing, ItemIconDescriptor descriptor, Point center, double size, Brush brush)
    {
        if (!string.IsNullOrEmpty(descriptor.Glyph))
        {
            var glyph = IconText(descriptor.Glyph, brush, size);
            drawing.DrawText(glyph, Align(new Point(center.X - glyph.Width / 2, center.Y - glyph.Height / 2)));
            return;
        }

        Geometry? geometry = null; var viewBox = new Rect(0, 0, 100, 100);
        if (descriptor.Key == "healthshot") geometry = SvgIconCatalog.HealthshotGeometry();
        else if (!string.IsNullOrWhiteSpace(descriptor.SvgName) && SvgIconCatalog.Get(descriptor.SvgName) is { } svg) { geometry = svg.Geometry; viewBox = svg.ViewBox; }
        if (geometry is not null && viewBox.Width > 0 && viewBox.Height > 0)
        {
            var scale = size / Math.Max(viewBox.Width, viewBox.Height);
            var transform = new MatrixTransform(new Matrix(scale, 0, 0, scale,
                center.X - (viewBox.X + viewBox.Width / 2) * scale,
                center.Y - (viewBox.Y + viewBox.Height / 2) * scale));
            drawing.PushTransform(transform); drawing.DrawGeometry(brush, null, geometry); drawing.Pop(); return;
        }

        var fallback = Text(descriptor.Name.Length <= 5 ? descriptor.Name : descriptor.Name[..5], brush, Math.Max(7, size * .45));
        drawing.DrawText(fallback, Align(new Point(center.X - fallback.Width / 2, center.Y - fallback.Height / 2)));
    }

    private static ushort GrenadeDefinition(GrenadeKind kind) => kind switch
    {
        GrenadeKind.Flashbang => 43, GrenadeKind.HighExplosive => 44, GrenadeKind.Smoke => 45,
        GrenadeKind.Molotov => 46, GrenadeKind.Decoy => 47, GrenadeKind.Incendiary => 48, _ => 44
    };
    private Vec3 GetPredictedOrigin(PlayerSnapshot player, DateTimeOffset capturedAt)
    {
        if (_frameOrigins.TryGetValue(player.PawnEntityIndex, out var cached)) return cached;
        var now = DateTimeOffset.UtcNow;
        var seconds = Math.Clamp((now - capturedAt).TotalSeconds, 0d, .10d);
        var velocity = player.Velocity;
        if (VelocityMagnitude(velocity) < .1f && _tracks.TryGetValue(player.PawnEntityIndex, out var previous)) {
            var elapsed = Math.Clamp((now - previous.CapturedAt).TotalSeconds, .001d, .25d);
            var estimated = new Vec3((player.Origin.X - previous.Origin.X) / (float)elapsed, (player.Origin.Y - previous.Origin.Y) / (float)elapsed, (player.Origin.Z - previous.Origin.Z) / (float)elapsed);
            if (Vec3.IsFinite(estimated) && VelocityMagnitude(estimated) <= 4000) velocity = estimated;
        }
        var predicted = new Vec3(player.Origin.X + velocity.X * (float)seconds, player.Origin.Y + velocity.Y * (float)seconds, player.Origin.Z + velocity.Z * (float)seconds);
        _tracks[player.PawnEntityIndex] = new TrackSample(player.Origin, now);
        if (_tracks.Count > 128) foreach (var stale in _tracks.Where(pair => now - pair.Value.CapturedAt > TimeSpan.FromSeconds(1)).Select(pair => pair.Key).ToArray()) _tracks.Remove(stale);
        _frameOrigins[player.PawnEntityIndex] = predicted;
        return predicted;
    }

    private PlayerVisualState GetVisualState(PlayerSnapshot player, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        if (!_visualStates.TryGetValue(player.PawnEntityIndex, out var state)) {
            state = new PlayerVisualState(player, capturedAt, now);
            _visualStates[player.PawnEntityIndex] = state;
            return state;
        }
        state.Update(player, capturedAt, now);
        return state;
    }

    private static double FadeIn(PlayerVisualState state, DateTimeOffset now) => AppearanceOpacity(state.AppearedAt, now);
    internal static double AppearanceOpacity(DateTimeOffset started, DateTimeOffset now)
        => MinimumAppearanceOpacity + (1 - MinimumAppearanceOpacity) * TransitionProgress(started, now, AppearanceTransition);
    internal static double HealthProgress(DateTimeOffset started, DateTimeOffset now) => TransitionProgress(started, now, HealthTransition);
    private static double TransitionProgress(DateTimeOffset started, DateTimeOffset now, TimeSpan duration) => Math.Clamp((now - started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);

    private static float VelocityMagnitude(Vec3 velocity) => MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
    private static Vec3 Add(Vec3 first, Vec3 second) => new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
    private bool TryGetBounds(PlayerSnapshot player, Vec3 origin, float[] matrix, double width, double height, out Rect result)
    {
        result = Rect.Empty;
        if (!EspGeometry.TryGetBounds(player, origin, matrix, width, height, out var bounds)) return false;
        result = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return true;
    }
    private void DrawOffscreenArrow(DrawingContext drawing, Vec3 origin, float[] matrix, Brush color)
    {
        if (!WorldProjection.TryProject(origin, matrix, ActualWidth, ActualHeight, out var projected)) return;
        var point = new Point(projected.X, projected.Y); var center = new Point(ActualWidth / 2, ActualHeight / 2); var dx = point.X - center.X; var dy = point.Y - center.Y; var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1 || (point.X >= 0 && point.X <= ActualWidth && point.Y >= 0 && point.Y <= ActualHeight)) return;
        var ux = dx / length; var uy = dy / length; var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 34; var tip = Align(new Point(center.X + ux * radius, center.Y + uy * radius));
        var left = Align(new Point(tip.X - ux * 10 - uy * 5, tip.Y - uy * 10 + ux * 5)); var right = Align(new Point(tip.X - ux * 10 + uy * 5, tip.Y - uy * 10 - ux * 5));
        var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(tip, true, true); context.LineTo(left, true, false); context.LineTo(right, true, false); }
        drawing.DrawGeometry(ResourcesFor(color).Fill78, Outline075, geometry);
    }

    private Point Align(Point point)
    {
        var dpi = VisualTreeHelper.GetDpi(this); return new Point(AlignCoordinate(point.X, dpi.DpiScaleX), AlignCoordinate(point.Y, dpi.DpiScaleY));
    }
    private Rect Align(Rect rect)
    {
        var topLeft = Align(rect.TopLeft); var bottomRight = Align(rect.BottomRight);
        return new Rect(topLeft, bottomRight);
    }
    internal static double AlignCoordinate(double value, double scale)
    {
        if (!double.IsFinite(value) || !double.IsFinite(scale) || scale <= 0) return value;
        return (Math.Round(value * scale - .5, MidpointRounding.AwayFromZero) + .5) / scale;
    }
    private static SolidColorBrush SolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness, bool rounded = false)
    {
        var pen = new Pen(brush, thickness);
        if (rounded) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; }
        pen.Freeze(); return pen;
    }

    private static Brush Translucent(Brush source, double opacity)
    {
        if (source is not SolidColorBrush solid) return source;
        var alpha = (byte)Math.Clamp(Math.Round(solid.Color.A * opacity), 0, 255);
        return SolidBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
    }

    private sealed class ThemeDrawingResources
    {
        public Brush Accent { get; }
        public Brush Fill82 { get; }
        public Brush Fill90 { get; }
        public Brush Fill78 { get; }
        public Pen BoxPen { get; }
        public Pen SnaplinePen { get; }
        public Pen AimPen { get; }
        public Pen FinePen { get; }
        public Pen TargetPen { get; }
        public Pen TextMarkerPen { get; }
        public Pen RadarPen { get; }
        public Pen TrajectoryCollisionPen { get; }
        public Pen TrajectoryApproximatePen { get; }

        public ThemeDrawingResources(Brush accent)
        {
            Accent = accent; Fill82 = Translucent(accent, .82); Fill90 = Translucent(accent, .9); Fill78 = Translucent(accent, .78);
            BoxPen = FrozenPen(Translucent(accent, .92), 1, true);
            SnaplinePen = FrozenPen(Translucent(accent, .52), .75);
            AimPen = FrozenPen(Translucent(accent, .68), .8);
            FinePen = FrozenPen(Fill82, .9);
            TargetPen = FrozenPen(Translucent(accent, .72), .85);
            TextMarkerPen = FrozenPen(Translucent(TextPrimary, .74), .7);
            RadarPen = FrozenPen(Translucent(accent, .58), .9);
            TrajectoryCollisionPen = FrozenPen(Translucent(accent, .92), 1.25);
            var approximate = new Pen(Translucent(accent, .68), 1) { DashStyle = new DashStyle(new[] { 3d, 3d }, 0) }; approximate.Freeze(); TrajectoryApproximatePen = approximate;
        }
    }

    private static Brush RadialBrush(Color center, Color edge)
    {
        var brush = new RadialGradientBrush(center, edge) { RadiusX = .72, RadiusY = .72 };
        brush.Freeze(); return brush;
    }

    private static Brush HealthGradientBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(143, 181, 163), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(209, 183, 125), .52));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(201, 137, 142), 1));
        brush.Freeze(); return brush;
    }

    private readonly record struct TrackSample(Vec3 Origin, DateTimeOffset CapturedAt);

    private sealed class PlayerVisualState
    {
        private double _healthFrom;
        private double _healthTarget;
        private DateTimeOffset _healthChangedAt;
        public PlayerSnapshot Player { get; private set; }
        public DateTimeOffset CapturedAt { get; private set; }
        public DateTimeOffset AppearedAt { get; private set; }
        public DateTimeOffset DisappearedAt { get; private set; }
        public double DisappearFromOpacity { get; private set; } = 1;

        public PlayerVisualState(PlayerSnapshot player, DateTimeOffset capturedAt, DateTimeOffset now)
        {
            Player = player; CapturedAt = capturedAt; AppearedAt = now; _healthFrom = _healthTarget = player.Health; _healthChangedAt = now;
        }

        public void Update(PlayerSnapshot player, DateTimeOffset capturedAt, DateTimeOffset now)
        {
            if (player.Health != _healthTarget) { _healthFrom = DisplayedHealth(now); _healthTarget = player.Health; _healthChangedAt = now; }
            if (DisappearedAt != default) AppearedAt = now;
            Player = player; CapturedAt = capturedAt; DisappearedAt = default;
        }

        public void BeginDisappear(DateTimeOffset now, double opacity) { DisappearedAt = now; DisappearFromOpacity = Math.Clamp(opacity, 0, 1); }

        public double DisplayedHealth(DateTimeOffset now)
        {
            var progress = HealthProgress(_healthChangedAt, now); return _healthFrom + (_healthTarget - _healthFrom) * progress;
        }
    }
}
