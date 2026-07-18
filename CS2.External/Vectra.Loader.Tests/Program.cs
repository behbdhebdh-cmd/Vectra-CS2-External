using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vectra.Loader;

static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await ValidatesCoordinatorFlows();
            await ValidatesManifestSecurity();
            ValidatesLoaderUi();
            Console.WriteLine("Vectra Loader coordinator, package validation, and animated UI tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task ValidatesCoordinatorFlows()
    {
        var package = new LoaderPackage(@"C:\release", @"C:\release\Vectra.External.exe", new string('A', 64));

        var runningProcesses = new FakeProcesses { Cs2ChecksUntilRunning = 0 };
        var runningDelay = new FakeDelay();
        var running = new LoaderCoordinator(new FakeResolver(package), runningProcesses, runningDelay);
        Assert(await running.StartAsync(CancellationToken.None), "Already-running CS2 flow did not complete.");
        Assert(running.Status.State == LoaderState.Ready && runningProcesses.UriLaunches == 0 && runningProcesses.ExternalLaunches == 1, "Already-running CS2 used the wrong launch path.");
        Assert(runningDelay.Durations.SequenceEqual(new[] { TimeSpan.FromSeconds(3) }), "Already-running CS2 did not receive the exact stabilization delay.");

        var delayedProcesses = new FakeProcesses { Cs2ChecksUntilRunning = 3 };
        var delayedDelay = new FakeDelay();
        var delayed = new LoaderCoordinator(new FakeResolver(package), delayedProcesses, delayedDelay);
        Assert(await delayed.StartAsync(CancellationToken.None), "Delayed Steam flow did not complete.");
        Assert(delayedProcesses.UriLaunches == 1 && delayedProcesses.LastUri == LoaderCoordinator.Cs2SteamUri && delayedProcesses.ExternalLaunches == 1, "Steam or External launch count was incorrect.");
        Assert(delayedDelay.Durations.Count(duration => duration == TimeSpan.FromMilliseconds(500)) == 3 && delayedDelay.Durations.Last() == TimeSpan.FromSeconds(3), "CS2 polling or stabilization cadence was incorrect.");

        var existingExternal = new FakeProcesses { ExternalRunning = true, Cs2ChecksUntilRunning = int.MaxValue };
        var existingDelay = new FakeDelay();
        var existing = new LoaderCoordinator(new FakeResolver(package), existingExternal, existingDelay);
        Assert(await existing.StartAsync(CancellationToken.None) && existing.Status.State == LoaderState.Ready, "Existing External process was not accepted.");
        Assert(existingExternal.UriLaunches == 0 && existingExternal.ExternalLaunches == 0 && existingDelay.Durations.Count == 0, "Existing External process caused a duplicate launch.");

        var timeoutProcesses = new FakeProcesses { Cs2ChecksUntilRunning = int.MaxValue };
        var timeoutDelay = new FakeDelay();
        var timeout = new LoaderCoordinator(new FakeResolver(package), timeoutProcesses, timeoutDelay);
        Assert(!await timeout.StartAsync(CancellationToken.None) && timeout.Status.State == LoaderState.Error, "90-second timeout did not enter the error state.");
        Assert(timeoutDelay.Durations.Count == 180 && timeoutProcesses.ExternalLaunches == 0, "Timeout polling exceeded its fixed boundary or launched External.");

        var failedStartProcesses = new FakeProcesses { Cs2ChecksUntilRunning = 0, ExternalLaunchResult = false };
        var failedStart = new LoaderCoordinator(new FakeResolver(package), failedStartProcesses, new FakeDelay());
        Assert(!await failedStart.StartAsync(CancellationToken.None) && failedStart.Status.State == LoaderState.Error, "External start failure produced a false success.");

        var failedSteamProcesses = new FakeProcesses { Cs2ChecksUntilRunning = int.MaxValue, UriLaunchResult = false };
        var failedSteam = new LoaderCoordinator(new FakeResolver(package), failedSteamProcesses, new FakeDelay());
        Assert(!await failedSteam.StartAsync(CancellationToken.None) && failedSteam.Status.State == LoaderState.Error && failedSteamProcesses.ExternalLaunches == 0, "Steam failure did not stop the launch.");

        using var cancellation = new CancellationTokenSource();
        var blockingDelay = new BlockingDelay();
        var cancellable = new LoaderCoordinator(new FakeResolver(package), new FakeProcesses { Cs2ChecksUntilRunning = int.MaxValue }, blockingDelay);
        var firstRun = cancellable.StartAsync(cancellation.Token);
        Assert(SpinWait.SpinUntil(() => cancellable.Status.State == LoaderState.WaitingForCs2, 1000), "Coordinator did not enter its cancellable wait.");
        Assert(!await cancellable.StartAsync(CancellationToken.None), "Repeated start was accepted while a launch was active.");
        cancellation.Cancel();
        Assert(!await firstRun && cancellable.Status.State == LoaderState.Selection, "Cancellation did not restore the selection state.");
    }

    private static async Task ValidatesManifestSecurity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vectra-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "Vectra.External.exe");
            var nativeMenu = Path.Combine(root, "Vectra.Menu.Native.dll");
            await File.WriteAllBytesAsync(executable, new byte[] { 1, 4, 1, 7, 1 });
            await File.WriteAllBytesAsync(nativeMenu, new byte[] { 9, 3, 1, 3, 0 });
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable)));
            var nativeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(nativeMenu)));
            await WriteManifest(root, "Vectra.External.exe", hash, "Vectra.Menu.Native.dll", nativeHash);
            var package = await new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None);
            Assert(package.ExternalExecutable == executable && package.ExternalSha256 == hash && package.NativeMenuPath == nativeMenu && package.NativeMenuSha256 == nativeHash, "Valid release manifest did not resolve both binaries.");

            await WriteManifest(root, "Vectra.External.exe", new string('0', 64), "Vectra.Menu.Native.dll", nativeHash);
            await AssertFails<InvalidDataException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Mismatched executable hash was accepted.");

            await WriteManifest(root, "missing.exe", hash, "Vectra.Menu.Native.dll", nativeHash);
            await AssertFails<FileNotFoundException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Missing External executable was accepted.");

            await WriteManifest(root, "..\\outside.exe", hash, "Vectra.Menu.Native.dll", nativeHash);
            await AssertFails<InvalidDataException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Manifest path traversal was accepted.");

            await WriteManifest(root, "Vectra.External.exe", hash, "missing.dll", nativeHash);
            await AssertFails<FileNotFoundException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Missing native menu was accepted.");

            await WriteManifest(root, "Vectra.External.exe", hash, "Vectra.Menu.Native.dll", new string('0', 64));
            await AssertFails<InvalidDataException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Mismatched native menu hash was accepted.");

            await File.WriteAllTextAsync(Path.Combine(root, "release.json"), "{ not-json }");
            await AssertFails<InvalidDataException>(() => new ReleasePackageResolver(root).ResolveAsync(CancellationToken.None), "Invalid release JSON was accepted.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void ValidatesLoaderUi()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var package = new LoaderPackage(@"C:\release", @"C:\release\Vectra.External.exe", new string('A', 64));
                var coordinator = new LoaderCoordinator(new FakeResolver(package), new FakeProcesses { Cs2ChecksUntilRunning = 0 }, new FakeDelay());
                var window = new MainWindow(coordinator, autoClose: false);
                var content = (FrameworkElement)window.Content; content.Measure(new Size(700, 430)); content.Arrange(new Rect(0, 0, 700, 430));
                var root = (DependencyObject)content;
                var external = FindByAutomationName<Button>(root, "Select External");
                var internalSoon = FindByAutomationName<Button>(root, "Internal coming soon");
                var start = FindByAutomationName<Button>(root, "Start External");
                Assert(external is not null && internalSoon is not null && start is not null, "Loader selection controls were not created.");
                Assert(!start!.IsEnabled && !internalSoon!.IsEnabled, "Initial loader selection state is incorrect.");
                external!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(start.IsEnabled, "External selection did not enable Start.");
                var card = (Border)external.Content;
                Assert(card.BorderThickness.Left == 1.5 && card.RenderTransform.HasAnimatedProperties, "External selection did not apply its smooth card animation.");

                var bitmap = new RenderTargetBitmap(700, 430, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var previewPath = Path.Combine(Path.GetTempPath(), "vectra-loader-preview.png");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(previewPath)) encoder.Save(stream);
                Console.WriteLine($"Loader preview: {previewPath}");

                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var detail = (TextBlock)typeof(MainWindow).GetField("_statusDetail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert(coordinator.Status.State == LoaderState.Ready && detail.Text == "You can go into a training match now", "Loader UI did not reach the exact success message.");
                window.Close();

                var errorCoordinator = new LoaderCoordinator(new FakeResolver(new InvalidDataException("Broken release manifest.")), new FakeProcesses(), new FakeDelay());
                var errorWindow = new MainWindow(errorCoordinator, autoClose: false);
                var errorContent = (FrameworkElement)errorWindow.Content; errorContent.Measure(new Size(700, 430)); errorContent.Arrange(new Rect(0, 0, 700, 430));
                var errorRoot = (DependencyObject)errorContent;
                FindByAutomationName<Button>(errorRoot, "Select External")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationName<Button>(errorRoot, "Start External")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var retry = FindByAutomationName<Button>(errorRoot, "Retry launch");
                var close = FindByAutomationName<Button>(errorRoot, "Close loader");
                Assert(errorCoordinator.Status.State == LoaderState.Error && retry?.Visibility == Visibility.Visible && close?.Visibility == Visibility.Visible, "Loader error state did not expose Retry and Close.");
                errorWindow.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    private static async Task WriteManifest(string root, string executable, string hash, string nativeMenu, string nativeHash)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["executable"] = executable, ["executable_sha256"] = hash, ["native_menu"] = nativeMenu, ["native_menu_sha256"] = nativeHash });
        await File.WriteAllTextAsync(Path.Combine(root, "release.json"), json);
    }

    private static async Task AssertFails<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static T? FindByAutomationName<T>(DependencyObject root, string name) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetName(root) == name) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindByAutomationName<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeResolver : IReleasePackageResolver
    {
        private readonly LoaderPackage? _package;
        private readonly Exception? _error;
        public FakeResolver(LoaderPackage package) => _package = package;
        public FakeResolver(Exception error) => _error = error;
        public Task<LoaderPackage> ResolveAsync(CancellationToken cancellationToken) => _error is null ? Task.FromResult(_package!) : Task.FromException<LoaderPackage>(_error);
    }

    private sealed class FakeProcesses : ILoaderProcessService
    {
        private int _cs2Checks;
        public int Cs2ChecksUntilRunning { get; init; }
        public bool ExternalRunning { get; set; }
        public bool UriLaunchResult { get; init; } = true;
        public bool ExternalLaunchResult { get; init; } = true;
        public int UriLaunches { get; private set; }
        public int ExternalLaunches { get; private set; }
        public string LastUri { get; private set; } = string.Empty;
        public bool IsProcessRunning(string processName) => _cs2Checks++ >= Cs2ChecksUntilRunning;
        public bool IsExecutableRunning(string executablePath) => ExternalRunning;
        public bool LaunchUri(string uri) { UriLaunches++; LastUri = uri; return UriLaunchResult; }
        public bool LaunchExecutable(string executablePath, string workingDirectory) { ExternalLaunches++; if (ExternalLaunchResult) ExternalRunning = true; return ExternalLaunchResult; }
    }

    private sealed class FakeDelay : ILoaderDelay
    {
        public List<TimeSpan> Durations { get; } = new();
        public Task Delay(TimeSpan duration, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Durations.Add(duration); return Task.CompletedTask; }
    }

    private sealed class BlockingDelay : ILoaderDelay
    {
        public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
