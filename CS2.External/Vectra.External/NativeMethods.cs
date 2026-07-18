using System.Runtime.InteropServices;

namespace Vectra.External;

internal static class NativeMethods
{
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ProcessVmRead = 0x0010;
    internal const int GwlpExStyle = -20;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    internal const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern unsafe bool ReadProcessMemory(nint process, nint address, void* buffer, nuint size, out nuint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation buffer, nint length);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetProcessDpiAwarenessContext(nint value);

    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryBasicInformation { public nint BaseAddress; public nint AllocationBase; public uint AllocationProtect; public ushort PartitionId; public nint RegionSize; public uint State; public uint Protect; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    internal static bool TryGetClientBounds(nint window, out int left, out int top, out int width, out int height)
    {
        left = top = width = height = 0;
        if (window == 0 || !GetClientRect(window, out var rect)) return false;
        var point = new Point { X = rect.Left, Y = rect.Top };
        if (!ClientToScreen(window, ref point)) return false;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        left = point.X; top = point.Y;
        return width > 0 && height > 0;
    }
}
