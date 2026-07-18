namespace Vectra.External;

public sealed class AimAssistService
{
    private const float MaxStepPixels = 8f;
    private readonly AimVisibilityHistory _visibility = new();
    private readonly AimMovementState _movement = new();
    private AimAssistReport _report = AimAssistReport.Disabled;
    private uint _lockedTarget;
    private float _remainderX, _remainderY;

    public AimAssistReport Report => Volatile.Read(ref _report);

    public void Update(GameSnapshot snapshot, GameProcessSession session, ClientSettings settings)
    {
        if (!settings.AimAssistEnabled) { Stop(AimAssistState.Disabled, "aim assist disabled", false, true); return; }
        if (!snapshot.IsFresh) { Stop(AimAssistState.SnapshotStale, "snapshot is stale", false, true); return; }

        var now = DateTimeOffset.UtcNow;
        _visibility.Update(snapshot, now);
        var hasBounds = NativeMethods.TryGetClientBounds(session.WindowHandle, out _, out _, out var width, out var height);
        var selection = hasBounds ? AimTargeting.Select(snapshot, width, height, settings, _visibility.VisibleSince, now, _lockedTarget) : new AimSelectionResult(null, AimCandidateDiagnostics.Empty);
        if (!settings.MasterEnabled) { Stop(AimAssistState.MasterDisabled, "master disabled", false, false, selection.Diagnostics); return; }
        if (!settings.PrivateMatchAuthorized) { Stop(AimAssistState.SessionUnauthorized, "private-match authorization required", false, false, selection.Diagnostics); return; }
        if (!OwnsForegroundWindow(session)) { Stop(AimAssistState.WindowUnfocused, "CS2 is not the foreground process", false, false, selection.Diagnostics); return; }
        if (!AimActivationGate.IsActive(settings, key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)) { Stop(AimAssistState.ActivationReleased, "hold key released", false, false, selection.Diagnostics); return; }
        if (!hasBounds) { Stop(AimAssistState.WaitingForBounds, "waiting for valid CS2 client bounds", true, false, selection.Diagnostics); return; }
        if (selection.Candidate is null) { Stop(AimAssistState.NoTarget, "no eligible target inside FOV", true, false, selection.Diagnostics); return; }

        var candidate = selection.Candidate.Value;
        if (candidate.EntityIndex != _lockedTarget) {
            _lockedTarget = candidate.EntityIndex; _remainderX = _remainderY = 0; _movement.Acquire(candidate.EntityIndex);
        }

        if (settings.AimMovement == AimMovement.Snap) {
            if (_movement.ShouldSnap(candidate.EntityIndex)) {
                if (!AimAssistMath.TryGetSnapCorrection(candidate.Point, width, height, settings.AimAssistFovPixels, out var correction)) {
                    _movement.MarkSnapped(candidate.EntityIndex); SetReport(AimAssistState.Locked, "target already centered", true, candidate.EntityIndex, settings.AimTargetPoint, selection.Diagnostics); return;
                }
                var moveX = (int)Math.Round(correction.X, MidpointRounding.AwayFromZero); var moveY = (int)Math.Round(correction.Y, MidpointRounding.AwayFromZero);
                if (!SendRelative(moveX, moveY)) { Stop(AimAssistState.InputFailed, "SendInput failed", true, false, selection.Diagnostics, moveX, moveY); return; }
                _movement.MarkSnapped(candidate.EntityIndex); SetReport(AimAssistState.Snapped, "snap applied for current lock", true, candidate.EntityIndex, settings.AimTargetPoint, selection.Diagnostics, moveX, moveY, moveX, moveY); return;
            }
            SetReport(AimAssistState.Locked, "snap complete; holding target lock", true, candidate.EntityIndex, settings.AimTargetPoint, selection.Diagnostics); return;
        }

        var strength = Math.Clamp(settings.AimAssistStrengthPercent / 100f, .05f, 1f);
        if (!AimAssistMath.TryGetCorrection(candidate.Point, width, height, settings.AimAssistFovPixels, strength, MaxStepPixels, out var smoothCorrection)) {
            SetReport(AimAssistState.Locked, "smooth target centered", true, candidate.EntityIndex, settings.AimTargetPoint, selection.Diagnostics); return;
        }
        _remainderX += smoothCorrection.X; _remainderY += smoothCorrection.Y;
        var smoothX = Math.Clamp((int)Math.Truncate(_remainderX), -8, 8); var smoothY = Math.Clamp((int)Math.Truncate(_remainderY), -8, 8);
        _remainderX -= smoothX; _remainderY -= smoothY;
        if ((smoothX != 0 || smoothY != 0) && !SendRelative(smoothX, smoothY)) { Stop(AimAssistState.InputFailed, "SendInput failed", true, false, selection.Diagnostics, smoothX, smoothY); return; }
        SetReport(AimAssistState.Locked, "smooth tracking active", true, candidate.EntityIndex, settings.AimTargetPoint, selection.Diagnostics, smoothX, smoothY, smoothX, smoothY);
    }

    private static bool SendRelative(int moveX, int moveY)
    {
        if (moveX == 0 && moveY == 0) return true;
        var input = new[] { new NativeMethods.Input { Type = 0, Mouse = new NativeMethods.MouseInput { Dx = moveX, Dy = moveY, Flags = 0x0001 } } };
        return NativeMethods.SendInput(1, input, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>()) == 1;
    }

    private void Stop(AimAssistState state, string gate, bool activationActive, bool clearVisibility = false, AimCandidateDiagnostics? candidates = null, int plannedX = 0, int plannedY = 0)
    {
        ClearLock(); if (clearVisibility) _visibility.Clear(); SetReport(state, gate, activationActive, 0, AimTargetPoint.Head, candidates ?? AimCandidateDiagnostics.Empty, plannedX, plannedY);
    }

    private void ClearLock() { _lockedTarget = 0; _remainderX = _remainderY = 0; _movement.Clear(); }
    private void SetReport(AimAssistState state, string gate, bool activation, uint target, AimTargetPoint point, AimCandidateDiagnostics candidates, int plannedX = 0, int plannedY = 0, int sentX = 0, int sentY = 0) =>
        Volatile.Write(ref _report, new(state, gate, activation, target, point, candidates, plannedX, plannedY, sentX, sentY));

    private static bool OwnsForegroundWindow(GameProcessSession session)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || session.ProcessId == 0) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        return foregroundProcessId == (uint)session.ProcessId;
    }
}

internal static class AimActivationGate
{
    public static bool IsActive(ClientSettings settings, Func<int, bool> isPressed) =>
        settings.AimActivation == AimActivation.Always || IsValidKey(settings.AimActivationKey) && isPressed(settings.AimActivationKey);

    public static bool IsValidKey(int key) => key is >= 1 and <= 255 and not 0x1B;
}

internal sealed class AimMovementState
{
    private uint _target;
    private bool _snapped;
    public void Acquire(uint target) { if (_target == target) return; _target = target; _snapped = false; }
    public bool ShouldSnap(uint target) => target != 0 && (_target != target || !_snapped);
    public void MarkSnapped(uint target) { _target = target; _snapped = true; }
    public void Clear() { _target = 0; _snapped = false; }
}
