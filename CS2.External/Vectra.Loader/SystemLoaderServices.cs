using System.Diagnostics;
using System.IO;

namespace Vectra.Loader;

public sealed class SystemLoaderDelay : ILoaderDelay
{
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
}

public sealed class SystemLoaderProcessService : ILoaderProcessService
{
    public bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public bool IsExecutableRunning(string executablePath)
    {
        var expected = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(expected);
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && process.MainModule?.FileName is string path && Path.GetFullPath(path).Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public bool LaunchUri(string uri) => Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });

    public bool LaunchExecutable(string executablePath, string workingDirectory) => Start(new ProcessStartInfo
    {
        FileName = executablePath,
        WorkingDirectory = workingDirectory,
        UseShellExecute = true
    });

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
