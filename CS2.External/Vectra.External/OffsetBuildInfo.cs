using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Vectra.External;

public sealed record OffsetBuildInfo(uint BuildNumber, DateTimeOffset? GeneratedAt)
{
    private static readonly Lazy<OffsetBuildInfo> CurrentValue = new(() => Load(AppContext.BaseDirectory));

    public static OffsetBuildInfo Current => CurrentValue.Value;
    public bool Available => BuildNumber != 0;
    public string DisplayBuild => Available ? BuildNumber.ToString(CultureInfo.InvariantCulture) : "UNAVAILABLE";

    public static OffsetBuildInfo Load(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, "Offsets", "info.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var build = root.GetProperty("build_number").GetUInt32();
            DateTimeOffset? generatedAt = null;
            if (root.TryGetProperty("timestamp", out var timestamp) &&
                DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                generatedAt = parsed;
            return new OffsetBuildInfo(build, generatedAt);
        }
        catch
        {
            return new OffsetBuildInfo(0, null);
        }
    }
}
