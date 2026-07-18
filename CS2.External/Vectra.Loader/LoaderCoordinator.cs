using System.IO;

namespace Vectra.Loader;

public enum LoaderState
{
    Selection,
    LaunchingSteam,
    WaitingForCs2,
    Stabilizing,
    StartingExternal,
    Ready,
    Error
}

public sealed record LoaderStatus(LoaderState State, string Title, string Detail)
{
    public static LoaderStatus Selection { get; } = new(LoaderState.Selection, "SELECT A CLIENT", "Choose External to prepare your private training session.");
}

public sealed record LoaderPackage(string ReleaseDirectory, string ExternalExecutable, string ExternalSha256, string NativeMenuPath = "", string NativeMenuSha256 = "");

public interface IReleasePackageResolver
{
    Task<LoaderPackage> ResolveAsync(CancellationToken cancellationToken);
}

public interface ILoaderProcessService
{
    bool IsProcessRunning(string processName);
    bool IsExecutableRunning(string executablePath);
    bool LaunchUri(string uri);
    bool LaunchExecutable(string executablePath, string workingDirectory);
}

public interface ILoaderDelay
{
    Task Delay(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class LoaderCoordinator
{
    public const string Cs2ProcessName = "cs2";
    public const string Cs2SteamUri = "steam://rungameid/730";
    public const int ProcessPollMilliseconds = 500;
    public const int ProcessTimeoutSeconds = 90;
    public const int StabilizationSeconds = 3;

    private readonly IReleasePackageResolver _packages;
    private readonly ILoaderProcessService _processes;
    private readonly ILoaderDelay _delay;
    private int _running;

    public LoaderCoordinator(IReleasePackageResolver packages, ILoaderProcessService processes, ILoaderDelay delay)
    {
        _packages = packages;
        _processes = processes;
        _delay = delay;
    }

    public LoaderStatus Status { get; private set; } = LoaderStatus.Selection;
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public event Action<LoaderStatus>? StatusChanged;

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        try
        {
            Set(LoaderState.StartingExternal, "VERIFYING RELEASE", "Checking the packaged External client and SHA-256 signature.");
            var package = await _packages.ResolveAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (_processes.IsExecutableRunning(package.ExternalExecutable))
            {
                SetReady("Vectra External is already running.");
                return true;
            }

            if (!_processes.IsProcessRunning(Cs2ProcessName))
            {
                Set(LoaderState.LaunchingSteam, "OPENING COUNTER-STRIKE 2", "Sending the launch request to Steam.");
                if (!_processes.LaunchUri(Cs2SteamUri)) throw new InvalidOperationException("Steam could not be opened.");
                Set(LoaderState.WaitingForCs2, "WAITING FOR CS2", "Steam is starting Counter-Strike 2. This can take a moment.");
                var found = false;
                var attempts = ProcessTimeoutSeconds * 1000 / ProcessPollMilliseconds;
                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    await _delay.Delay(TimeSpan.FromMilliseconds(ProcessPollMilliseconds), cancellationToken).ConfigureAwait(false);
                    if (_processes.IsProcessRunning(Cs2ProcessName)) { found = true; break; }
                }
                if (!found) throw new TimeoutException("CS2 did not start within 90 seconds.");
            }

            Set(LoaderState.Stabilizing, "CS2 DETECTED", "Preparing the External client for a stable handoff.");
            await _delay.Delay(TimeSpan.FromSeconds(StabilizationSeconds), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Set(LoaderState.StartingExternal, "STARTING EXTERNAL", "Opening Vectra External beside Counter-Strike 2.");
            if (!_processes.IsExecutableRunning(package.ExternalExecutable) &&
                !_processes.LaunchExecutable(package.ExternalExecutable, package.ReleaseDirectory))
                throw new InvalidOperationException("Vectra External could not be started.");

            SetReady("You can go into a training match now");
            return true;
        }
        catch (OperationCanceledException)
        {
            Set(LoaderStatus.Selection);
            return false;
        }
        catch (Exception error)
        {
            Set(LoaderState.Error, "LAUNCH FAILED", FriendlyMessage(error));
            return false;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Reset() => Set(LoaderStatus.Selection);

    private void SetReady(string detail) => Set(LoaderState.Ready, "EXTERNAL IS READY", detail);

    private static string FriendlyMessage(Exception error) => error switch
    {
        TimeoutException => error.Message,
        InvalidDataException => error.Message,
        FileNotFoundException => error.Message,
        _ => string.IsNullOrWhiteSpace(error.Message) ? "The launch could not be completed." : error.Message
    };

    private void Set(LoaderState state, string title, string detail) => Set(new LoaderStatus(state, title, detail));

    private void Set(LoaderStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}
