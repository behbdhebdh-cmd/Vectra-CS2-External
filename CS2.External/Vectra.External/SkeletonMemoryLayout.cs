namespace Vectra.External;

public static class SkeletonMemoryLayout
{
    // Source 2 container/model-handle ABI. Game-field offsets remain sourced from the generated cs2-dumper schemas.
    public const int BoneCacheOffsetInModelState = 0x80;
    public const int UtlVectorDataOffset = 0x0;
    public const int UtlVectorSizeOffset = 0x10;
    public const int MaximumModelBones = 256;
    public static IReadOnlyList<int> ModelResourcePointerOffsets { get; } = new[] { 0x0, 0x8, 0x10, 0x18 };

    public static bool IsValidVectorHeader(nint data, int count) =>
        data >= 0x10000 && ((long)data & (IntPtr.Size - 1)) == 0 && count is > 0 and <= MaximumModelBones;
}
