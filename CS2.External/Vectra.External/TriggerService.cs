using System.Runtime.InteropServices;

namespace Vectra.External;

public sealed class TriggerService
{
    private const float PunchChangeThreshold = .015f;
    private const int RequiredQuietSnapshots = 3;
    private static readonly TimeSpan MinimumTapInterval = TimeSpan.FromMilliseconds(175);

    private DateTimeOffset _lastClick;
    private uint _stableTarget;
    private int _stableSnapshots;
    private bool _shotPending;
    private bool _shotObserved;
    private int _shotsAtClick;
    private int _punchTickAtClick = -1;
    private bool _hadPunchTickAtClick;
    private bool _hasPreviousRecoil;
    private RecoilSnapshot _previousRecoil = RecoilSnapshot.Unavailable;
    private float _punchDelta;
    private int _quietSnapshots;
    private readonly SnapshotGapTracker _snapshotGap = new();

    public TriggerState State { get; private set; } = TriggerState.Disabled;
    public TriggerDiagnostics Diagnostics { get; private set; } = TriggerDiagnostics.Empty;

    public void Update(GameSnapshot snapshot, GameProcessSession session, ClientSettings settings)
    {
        if (!settings.TriggerEnabled) { Stop(TriggerState.Disabled, "trigger disabled"); return; }
        if (!settings.MasterEnabled) { Stop(TriggerState.MasterDisabled, "master disabled"); return; }
        if (!settings.PrivateMatchAuthorized) { Stop(TriggerState.SessionUnauthorized, "private-match authorization required"); return; }
        if (!OwnsForegroundWindow(session)) { Stop(TriggerState.WindowUnfocused, "CS2 is not the foreground process"); return; }
        if (!snapshot.IsFresh) {
            if (!_snapshotGap.BeginOrContinue(DateTimeOffset.UtcNow)) Stop(TriggerState.SnapshotStale, "snapshot is stale");
            else {
                _stableSnapshots = 0;
                SetState(TriggerState.SnapshotStale, "snapshot is stale; preserving trigger context", true);
            }
            return;
        }
        _snapshotGap.Clear();
        if (!snapshot.Recoil.Available) { Stop(TriggerState.RecoilDataUnavailable, "recoil data unavailable"); return; }

        var recoilChanged = UpdateRecoilDelta(snapshot.Recoil);
        if (_shotPending) {
            ObservePendingShot(snapshot.Recoil, recoilChanged);
            return;
        }

        if (snapshot.Recoil.Reloading) { SetState(TriggerState.Reloading, "weapon is reloading", true); return; }
        if (_hasPreviousRecoil && recoilChanged) { SetState(TriggerState.WaitingForRecoil, "recoil is still changing", true); return; }
        TryFireAtStableTarget(snapshot, session, settings);
    }

    private void ObservePendingShot(RecoilSnapshot recoil, bool recoilChanged)
    {
        var tickObserved = recoil.HasPunchTick && (!_hadPunchTickAtClick || recoil.PunchTick != _punchTickAtClick);
        _shotObserved |= recoil.ShotsFired > _shotsAtClick || recoilChanged || tickObserved;
        if (!_shotObserved) { SetState(TriggerState.WaitingForShotObservation, "waiting for shot observation", true); return; }
        if (recoil.Reloading) { _quietSnapshots = 0; SetState(TriggerState.Reloading, "weapon is reloading", true); return; }
        if (recoilChanged) { _quietSnapshots = 0; SetState(TriggerState.WaitingForRecoil, "waiting for recoil to stop changing", true); return; }

        _quietSnapshots++;
        if (_quietSnapshots < RequiredQuietSnapshots || DateTimeOffset.UtcNow - _lastClick < MinimumTapInterval) {
            SetState(TriggerState.WaitingForRecoil, $"recoil settling ({_quietSnapshots}/{RequiredQuietSnapshots})", true);
            return;
        }

        _shotPending = false;
        _shotObserved = false;
        _stableSnapshots = 0;
        _quietSnapshots = 0;
        SetState(TriggerState.WaitingForRecoil, "recoil settled; reacquiring target", true);
    }

    private void TryFireAtStableTarget(GameSnapshot snapshot, GameProcessSession session, ClientSettings settings)
    {
        if (snapshot.CrosshairEntityIndex <= 0) { _stableSnapshots = 0; SetState(TriggerState.NoCrosshairTarget, "no crosshair target", true); return; }
        var target = snapshot.Players.FirstOrDefault(player => !player.IsLocal && player.PawnEntityIndex == (uint)snapshot.CrosshairEntityIndex);
        if (target is null) { _stableSnapshots = 0; SetState(TriggerState.NoCrosshairTarget, "crosshair target is not in snapshot", true); return; }
        if (settings.TeamCheckEnabled && target.Team == snapshot.LocalTeam) { _stableSnapshots = 0; SetState(TriggerState.FriendlyTarget, "crosshair target is a teammate", true); return; }
        if (!target.Alive || target.Dormant) { _stableSnapshots = 0; SetState(TriggerState.IneligibleTarget, "crosshair target is not eligible", true); return; }

        if (_stableTarget == target.PawnEntityIndex) _stableSnapshots++;
        else { _stableTarget = target.PawnEntityIndex; _stableSnapshots = 1; }
        if (_stableSnapshots < 2) { SetState(TriggerState.TargetStabilizing, "stabilizing crosshair target", true); return; }
        if (DateTimeOffset.UtcNow - _lastClick < MinimumTapInterval) { SetState(TriggerState.WaitingForRecoil, "minimum tap interval", true); return; }

        var inputs = new[] {
            new NativeMethods.Input { Type = 0, Mouse = new NativeMethods.MouseInput { Flags = 0x0002 } },
            new NativeMethods.Input { Type = 0, Mouse = new NativeMethods.MouseInput { Flags = 0x0004 } }
        };
        if (NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.Input>()) != 2) { Stop(TriggerState.InputFailed, "SendInput failed"); return; }
        _lastClick = DateTimeOffset.UtcNow;
        _shotPending = true;
        _shotObserved = false;
        _shotsAtClick = snapshot.Recoil.ShotsFired;
        _punchTickAtClick = snapshot.Recoil.PunchTick;
        _hadPunchTickAtClick = snapshot.Recoil.HasPunchTick;
        _quietSnapshots = 0;
        SetState(TriggerState.Fired, "single tap sent", true);
    }

    private bool UpdateRecoilDelta(RecoilSnapshot recoil)
    {
        var changed = _hasPreviousRecoil && (PunchDistance(recoil.ViewPunch, _previousRecoil.ViewPunch) > PunchChangeThreshold || recoil.ShotsFired != _previousRecoil.ShotsFired || (recoil.HasPunchTick && _previousRecoil.HasPunchTick && recoil.PunchTick != _previousRecoil.PunchTick));
        _punchDelta = _hasPreviousRecoil ? PunchDistance(recoil.ViewPunch, _previousRecoil.ViewPunch) : 0;
        _previousRecoil = recoil;
        _hasPreviousRecoil = true;
        return changed;
    }

    private static float PunchDistance(Angles left, Angles right) => MathF.Sqrt(MathF.Pow(left.Pitch - right.Pitch, 2) + MathF.Pow(left.Yaw - right.Yaw, 2));

    private static bool OwnsForegroundWindow(GameProcessSession session)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || session.ProcessId == 0) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        return foregroundProcessId == (uint)session.ProcessId;
    }

    private void Stop(TriggerState state, string gate)
    {
        _snapshotGap.Clear();
        _stableTarget = 0; _stableSnapshots = 0; _shotPending = false; _shotObserved = false; _shotsAtClick = 0; _punchTickAtClick = -1; _hadPunchTickAtClick = false; _hasPreviousRecoil = false; _previousRecoil = RecoilSnapshot.Unavailable; _punchDelta = 0; _quietSnapshots = 0;
        SetState(state, gate, false);
    }

    private void SetState(TriggerState state, string gate, bool focusOwned)
    {
        State = state;
        Diagnostics = new(state, gate, focusOwned, _shotPending, _shotObserved, _stableSnapshots, _quietSnapshots, _punchDelta);
    }
}
