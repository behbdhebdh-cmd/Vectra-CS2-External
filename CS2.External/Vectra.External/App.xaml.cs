using System.Windows;
using System.Reflection;

namespace Vectra.External;

public partial class App : Application
{
    private ExternalRuntime? _runtime;
    private OverlayWindow? _overlay;
    private Thread? _menuThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _runtime = new ExternalRuntime();
        _overlay = new OverlayWindow();
        _overlay.Show();
        _overlay.Attach(_runtime);
        _runtime.Start();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
        var host = new NativeMenuHost(_runtime, _overlay, version, OffsetBuildInfo.Current);
        _menuThread = new Thread(() => RunMenu(host)) { IsBackground = true, Name = "Vectra native menu" };
        _menuThread.Start();
    }

    private void RunMenu(NativeMenuHost host)
    {
        try
        {
            var result = host.Run();
            if (result != 0) throw new InvalidOperationException($"Native menu returned error {result}.");
        }
        catch (Exception error)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show($"The native vectraNewUi menu could not be started.\n\n{error.GetType().Name}: {error.Message}", "Vectra External", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2);
            }));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlay?.Close();
        _runtime?.Dispose();
        base.OnExit(e);
    }
}
