namespace Vectra.External;

internal static class AimKeyNames
{
    public static string Display(int key) => key switch
    {
        0x01 => "MOUSE 1", 0x02 => "MOUSE 2", 0x04 => "MOUSE 3", 0x05 => "MOUSE 4", 0x06 => "MOUSE 5",
        0x10 => "SHIFT", 0x11 => "CTRL", 0x12 => "ALT", 0x20 => "SPACE",
        >= 0x30 and <= 0x39 => ((char)key).ToString(), >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}", _ => $"VK {key:X2}"
    };
}
