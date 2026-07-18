using System.Text;

namespace Vectra.External;

public enum ProtectedText
{
    ProductName,
    Session,
    EspVisuals,
    ControlledTrigger,
    LiveDiagnostics,
    MasterEnable,
    PrivateMatch,
    CaptureShield,
    EnableEsp,
    RotatingRadar,
    Reader,
    Snapshot,
    Recoil,
    Trigger,
    WaitingForProcess,
    OffsetUnavailable,
    Attached
}

public static class ObfuscatedText
{
    private static readonly string[] Payloads =
    [
        "VnhZIwbwjo6QcUdNMhj6", "U1hpBD3e4A==", "RU5qdyLY/Z6pSXE=", "Q1J0Aybe4oetQQJrDjDR9JW/",
        "TFRsElTV54qvS21sCDDV4A==", "TXxJIxHjjq6GZEBTOQ==", "UG9TIRXly+aFZFZcNFn3xqSFZVUtGx/v0bqc",
        "SHReMlT+2K6aaUNGfB/k3L3NeUQ2BBv1mLaTf1g8FOY=", "RXNbNRj0jo67VQ==", "U3VVIFTjwb+JcUtRO1nk0rSMeA==",
        "Ulh7EzHD", "U1N7ByfZ4Z8=", "Ulh5GD3d", "VE9zEDPU/A==", "V3xTIx3/yeuOalAfHyqk",
        "T3tcJBHl3euMZFZefBDlk6WDa1ElCBL62rmX", "QWlONhf5y6/IcU0fHyqkk7KYY0sgQQ=="
    ];

    private static readonly Lazy<string>[] Values = Payloads.Select(payload => new Lazy<string>(() => Decode(payload), LazyThreadSafetyMode.ExecutionAndPublication)).ToArray();

    public static string Get(ProtectedText value) => Values[(int)value].Value;

    private static string Decode(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        for (var index = 0; index < bytes.Length; index++) bytes[index] ^= (byte)((index * 29) & 0xFF);
        return Encoding.UTF8.GetString(bytes);
    }
}
