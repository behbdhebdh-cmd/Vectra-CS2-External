using System.Runtime.InteropServices;
using System.Windows;

namespace Vectra.Loader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SetProcessDpiAwarenessContext(new nint(-4));
        base.OnStartup(e);
        var coordinator = new LoaderCoordinator(
            new ReleasePackageResolver(AppContext.BaseDirectory),
            new SystemLoaderProcessService(),
            new SystemLoaderDelay());
        var window = new MainWindow(coordinator);
        MainWindow = window;
        window.Show();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
