using System.Runtime.InteropServices;

namespace Vectra.External;

public sealed class ProcessMemoryReader(nint processHandle) : IDisposable
{
    public const int MaximumCachedRegions = 128;
    private nint _processHandle = processHandle;
    private readonly List<MemoryRegion> _regions = new(32);
    public bool IsOpen => _processHandle != 0;
    public void BeginBatch() { }

    public unsafe bool Read<T>(nint address, out T value) where T : unmanaged
    {
        value = default;
        if (!IsReadable(address, sizeof(T))) return false;
        T temporary = default;
        if (!NativeMethods.ReadProcessMemory(_processHandle, address, &temporary, (nuint)sizeof(T), out var read) || read != (nuint)sizeof(T)) { _regions.Clear(); return false; }
        value = temporary;
        return true;
    }

    public unsafe bool ReadArray<T>(nint address, int count, out T[] values) where T : unmanaged
    {
        values = Array.Empty<T>();
        if (count <= 0 || count > 256) return false;
        var size = checked(sizeof(T) * count);
        if (!IsReadable(address, size)) return false;
        values = new T[count];
        fixed (T* destination = values) {
            if (!NativeMethods.ReadProcessMemory(_processHandle, address, destination, (nuint)size, out var read) || read != (nuint)size) { _regions.Clear(); values = Array.Empty<T>(); return false; }
        }
        return true;
    }

    public bool ReadPointer(nint address, out nint value)
    {
        value = 0;
        return Read(address, out value) && value >= 0x10000 && IsReadable(value, IntPtr.Size);
    }

    public bool ReadUtf8String(nint address, int maximumLength, out string value)
    {
        value = string.Empty;
        if (!ReadPointer(address, out var text) || !ReadArray<byte>(text, maximumLength, out var bytes)) return false;
        var length = Array.IndexOf(bytes, (byte)0);
        if (length <= 0) return false;
        value = System.Text.Encoding.UTF8.GetString(bytes, 0, length);
        return true;
    }

    private bool IsReadable(nint address, int size)
    {
        if (_processHandle == 0 || address == 0 || size <= 0) return false;
        var end = checked((long)address + size);
        foreach (var region in _regions) if ((long)address >= (long)region.Begin && end <= (long)region.End) return region.Readable;
        if (NativeMethods.VirtualQueryEx(_processHandle, address, out var info, Marshal.SizeOf<NativeMethods.MemoryBasicInformation>()) == 0) return false;
        const uint MemCommit = 0x1000, PageNoAccess = 0x01, PageGuard = 0x100;
        var regionEnd = checked((long)info.BaseAddress + (long)info.RegionSize);
        var readable = info.State == MemCommit && (info.Protect & (PageNoAccess | PageGuard)) == 0;
        if (_regions.Count >= MaximumCachedRegions) _regions.Clear();
        _regions.Add(new MemoryRegion(info.BaseAddress, (nint)regionEnd, readable));
        return readable && end <= regionEnd;
    }

    private readonly record struct MemoryRegion(nint Begin, nint End, bool Readable);

    public void Dispose()
    {
        if (_processHandle == 0) return;
        NativeMethods.CloseHandle(_processHandle);
        _processHandle = 0;
    }
}
