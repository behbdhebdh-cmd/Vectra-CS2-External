namespace Vectra.External;

public sealed class ExternalRuntime : IDisposable
{
    private const int CoreFailureReattachThreshold = 30;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly GameSnapshotReader _reader = new();
    private readonly TriggerService _trigger = new();
    private readonly AimAssistService _aimAssist = new();
    private readonly GrenadePredictionService _grenadePrediction = new();
    private readonly MapCollisionProvider _mapCollision = new();
    private Task? _worker;
    private int _started;
    private GameSnapshot _latest = GameSnapshot.Empty();
    private int _consecutiveCoreFailures;
    private long _snapshotSequence;
    private int _gameForeground;
    public ClientSettings Settings { get; } = new();
    public GameProcessSession Session { get; } = new();
    public GameSnapshot Latest => Volatile.Read(ref _latest);
    public long SnapshotSequence => Volatile.Read(ref _snapshotSequence);
    public bool GameForeground => Volatile.Read(ref _gameForeground) != 0;
    public TriggerState TriggerState => _trigger.State;
    public TriggerDiagnostics TriggerDiagnostics => _trigger.Diagnostics;
    public AimAssistReport AimAssistReport => _aimAssist.Report;
    public GrenadeTrajectory GrenadeTrajectory => _grenadePrediction.Latest;
    public GrenadePredictionReport GrenadePredictionReport { get; private set; } = GrenadePredictionReport.Unavailable;
    public IReadOnlyList<string> AvailableGrenadeMaps => _mapCollision.AvailableMaps;
    public WorldEntityDiscoveryReport WorldEntityDiscovery => _reader.WorldEntityDiscovery;

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public bool Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return false;
        _worker = Task.Run(WorkerLoop);
        return true;
    }

    private async Task WorkerLoop()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var nextCaptureMs = 0d;
        while (!_cancellation.IsCancellationRequested) {
            GameSnapshot current = GameSnapshot.Empty(Session.GameBuild);
            var attached = false;
            try { attached = Session.EnsureAttached(); }
            catch (Exception error) {
                Session.Report = new(ReaderState.MemoryUnavailable, $"Attach failed: {error.GetType().Name}", Session.DumpBuild, Session.GameBuild);
            }

            var foreground = attached && Session.OwnsForegroundWindow();
            Volatile.Write(ref _gameForeground, foreground ? 1 : 0);
            if (attached) {
                try {
                    current = _reader.Capture(Session, CaptureOptions.From(Settings));
                    if (current.Valid) {
                        _consecutiveCoreFailures = 0;
                    } else if (Session.Report.State == ReaderState.MemoryUnavailable) {
                        RegisterCoreFailure();
                    } else {
                        _consecutiveCoreFailures = 0;
                    }
                } catch (Exception error) {
                    Session.Report = new(ReaderState.MemoryUnavailable, $"Core capture failed: {error.GetType().Name}", Session.DumpBuild, Session.GameBuild);
                    RegisterCoreFailure();
                }
            } else {
                _consecutiveCoreFailures = 0;
            }

            try
            {
                var visibilityEnabled = Settings.AimAssistEnabled && Settings.AimVisibilityCheckEnabled;
                _mapCollision.Update(Settings.DrawGrenadePrediction || visibilityEnabled, Settings.GrenadePredictionMap, Session);
                var mapCollision = _mapCollision.CollisionWorld;
                var collisionWorld = mapCollision is null ? null : current.DynamicCollisions.Count == 0
                    ? mapCollision : new CompositeCollisionWorld(mapCollision, new DynamicBoundsCollisionWorld(current.DynamicCollisions));
                if (visibilityEnabled) current = VisibilityRaycaster.Apply(current, collisionWorld, Settings.AimTargetPoint);
                _grenadePrediction.SetCollisionWorld(collisionWorld);
                _grenadePrediction.Submit(Settings.DrawGrenadePrediction ? current.GrenadeThrow : GrenadeThrowState.Unavailable);
                GrenadePredictionReport = Settings.DrawGrenadePrediction || visibilityEnabled ? _mapCollision.Report : GrenadePredictionReport.Unavailable;
            }
            catch { }

            if (current.Valid)
            {
                Volatile.Write(ref _latest, current);
                Interlocked.Increment(ref _snapshotSequence);
            }
            try { _trigger.Update(current, Session, Settings); } catch { }
            try { _aimAssist.Update(current, Session, Settings); } catch { }

            nextCaptureMs += RuntimeCadence.Select(attached, foreground, Settings).TotalMilliseconds;
            var delayMs = nextCaptureMs - clock.Elapsed.TotalMilliseconds;
            if (delayMs <= 0) nextCaptureMs = clock.Elapsed.TotalMilliseconds;
            else {
                try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _cancellation.Token); } catch (OperationCanceledException) { }
            }
        }
    }

    private void RegisterCoreFailure()
    {
        _consecutiveCoreFailures++;
        if (_consecutiveCoreFailures < CoreFailureReattachThreshold) return;
        var reason = Session.Report.Message;
        Session.Detach();
        Session.Report = new(ReaderState.MemoryUnavailable, $"Reattaching after repeated core failures: {reason}", Session.DumpBuild, Session.GameBuild);
        _consecutiveCoreFailures = 0;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _grenadePrediction.Dispose(); _mapCollision.Dispose(); Session.Dispose(); _cancellation.Dispose();
    }
}
