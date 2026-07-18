using System.Diagnostics;
using OffsetEngine = CS2Dumper.Offsets.Engine2Dll;

namespace Vectra.External;

public sealed class GameProcessSession : IDisposable
{
    private Process? _process;
    public ProcessMemoryReader? Memory { get; private set; }
    public nint ClientBase { get; private set; }
    public nint EngineBase { get; private set; }
    public nint WindowHandle { get; private set; }
    public int ProcessId { get; private set; }
    public string ExecutablePath { get; private set; } = string.Empty;
    public uint DumpBuild { get; private set; }
    public uint GameBuild { get; private set; }
    public ReaderReport Report { get; internal set; } = new(ReaderState.WaitingForProcess, ObfuscatedText.Get(ProtectedText.WaitingForProcess));
    public bool Ready => Memory is not null && ClientBase != 0 && EngineBase != 0 && WindowHandle != 0 && GameBuild == DumpBuild;

    public bool EnsureAttached()
    {
        if (_process is { HasExited: false } && Ready) return true;
        Detach();
        DumpBuild = OffsetBuildInfo.Current.BuildNumber;
        if (DumpBuild == 0) { Report = new(ReaderState.MemoryUnavailable, ObfuscatedText.Get(ProtectedText.OffsetUnavailable)); return false; }
        foreach (var process in Process.GetProcessesByName("cs2")) {
            try {
                process.Refresh();
                var client = process.Modules.Cast<ProcessModule>().FirstOrDefault(module => string.Equals(module.ModuleName, "client.dll", StringComparison.OrdinalIgnoreCase));
                var engine = process.Modules.Cast<ProcessModule>().FirstOrDefault(module => string.Equals(module.ModuleName, "engine2.dll", StringComparison.OrdinalIgnoreCase));
                if (client is null || engine is null || process.MainWindowHandle == 0) { process.Dispose(); continue; }
                var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead, false, process.Id);
                if (handle == 0) { process.Dispose(); continue; }
                var memory = new ProcessMemoryReader(handle);
                if (!memory.Read(Address((nint)engine.BaseAddress, OffsetEngine.dwBuildNumber), out uint build)) { memory.Dispose(); process.Dispose(); continue; }
                if (build != DumpBuild) {
                    memory.Dispose(); process.Dispose();
                    Report = new(ReaderState.BuildMismatch, $"Dump build {DumpBuild}, game build {build}", DumpBuild, build);
                    return false;
                }
                _process = process; Memory = memory; ClientBase = (nint)client.BaseAddress; EngineBase = (nint)engine.BaseAddress; WindowHandle = process.MainWindowHandle; ProcessId = process.Id; ExecutablePath = process.MainModule?.FileName ?? string.Empty; GameBuild = build;
                Report = new(ReaderState.WaitingForLocalPlayer, ObfuscatedText.Get(ProtectedText.Attached) + build, DumpBuild, build);
                return true;
            } catch { process.Dispose(); }
        }
        Report = new(ReaderState.WaitingForProcess, ObfuscatedText.Get(ProtectedText.WaitingForProcess), DumpBuild);
        return false;
    }

    public static nint Address(nint baseAddress, nint offset) => checked((nint)((long)baseAddress + (long)offset));

    public bool OwnsForegroundWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground != 0 && NativeMethods.GetWindowThreadProcessId(foreground, out var processId) != 0 && processId == (uint)ProcessId;
    }

    public void Detach()
    {
        Memory?.Dispose(); Memory = null;
        _process?.Dispose(); _process = null;
        ClientBase = EngineBase = WindowHandle = 0; ProcessId = 0; ExecutablePath = string.Empty; GameBuild = 0;
    }
    public void Dispose() => Detach();
}
