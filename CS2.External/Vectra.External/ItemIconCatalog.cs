using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Vectra.External;

public sealed record ItemIconDescriptor(string Key, string Name, string? Glyph, string? SvgName = null)
{
    public bool HasIcon => Glyph is not null || SvgName is not null || Key == "healthshot";
}

public static class ItemIconCatalog
{
    public const string C4Glyph = "\uE031";
    private static readonly Lazy<FontFamily> IconFont = new(() => new FontFamily(new Uri("pack://application:,,,/Vectra.External;component/"), "./Assets/#csgo_icons"));
    public static FontFamily FontFamily => IconFont.Value;

    private static readonly IReadOnlyDictionary<ushort, ItemIconDescriptor> Items = new Dictionary<ushort, ItemIconDescriptor>
    {
        [1] = D("deagle", "Deagle", 0xE001), [2] = D("elite", "Dual Berettas", 0xE002), [3] = D("fiveseven", "Five-SeveN", 0xE003), [4] = D("glock", "Glock", 0xE004),
        [7] = D("ak47", "AK-47", 0xE007), [8] = D("aug", "AUG", 0xE008), [9] = D("awp", "AWP", 0xE009), [10] = D("famas", "FAMAS", 0xE00A),
        [11] = D("g3sg1", "G3SG1", 0xE00B), [13] = D("galilar", "Galil AR", 0xE00D), [14] = D("m249", "M249", 0xE03C), [16] = D("m4a1", "M4A4", 0xE00E),
        [17] = D("mac10", "MAC-10", 0xE011), [19] = D("p90", "P90", 0xE024), [23] = S("mp5sd", "MP5-SD"), [24] = D("ump45", "UMP-45", 0xE018),
        [25] = D("xm1014", "XM1014", 0xE019), [26] = D("bizon", "PP-Bizon", 0xE01A), [27] = D("mag7", "MAG-7", 0xE01B), [28] = D("negev", "Negev", 0xE01C),
        [29] = D("sawedoff", "Sawed-Off", 0xE01D), [30] = D("tec9", "Tec-9", 0xE01E), [31] = D("taser", "Zeus", 0xE01F), [32] = D("hkp2000", "P2000", 0xE013),
        [33] = D("mp7", "MP7", 0xE021), [34] = D("mp9", "MP9", 0xE022), [35] = D("nova", "Nova", 0xE023), [36] = D("p250", "P250", 0xE020),
        [38] = D("scar20", "SCAR-20", 0xE026), [39] = D("sg556", "SG 553", 0xE027), [40] = D("ssg08", "SSG 08", 0xE028),
        [42] = D("knife", "Knife", 0xE02A), [43] = D("flashbang", "Flashbang", 0xE02B), [44] = D("hegrenade", "HE Grenade", 0xE02C),
        [45] = D("smokegrenade", "Smoke", 0xE02D), [46] = D("molotov", "Molotov", 0xE02E), [47] = D("decoy", "Decoy", 0xE02F),
        [48] = D("incgrenade", "Incendiary", 0xE030), [49] = D("c4", "C4", 0xE031), [57] = new("healthshot", "Healthshot", null),
        [59] = D("knife_t", "Knife", 0xE03B), [60] = D("m4a1_silencer", "M4A1-S", 0xE010), [61] = D("usp_silencer", "USP-S", 0xE03D),
        [63] = D("cz75a", "CZ75-Auto", 0xE03F), [64] = D("revolver", "R8 Revolver", 0xE040),
        [500] = D("knife_bayonet", "Bayonet", 0xE1F4), [503] = D("knife_css", "Classic Knife", 0xE02A), [505] = D("knife_flip", "Flip Knife", 0xE1F9),
        [506] = D("knife_gut", "Gut Knife", 0xE1FA), [507] = D("knife_karambit", "Karambit", 0xE1FB), [508] = D("knife_m9_bayonet", "M9 Bayonet", 0xE1FC),
        [509] = D("knife_tactical", "Huntsman Knife", 0xE1FD), [512] = D("knife_falchion", "Falchion Knife", 0xE200), [514] = D("knife_survival_bowie", "Bowie Knife", 0xE202),
        [515] = D("knife_butterfly", "Butterfly Knife", 0xE203), [516] = D("knife_push", "Shadow Daggers", 0xE204),
        [517] = S("knife_cord", "Paracord Knife"), [518] = S("knife_canis", "Survival Knife"), [519] = S("knife_ursus", "Ursus Knife"),
        [520] = S("knife_gypsy_jackknife", "Navaja Knife"), [521] = S("knife_outdoor", "Nomad Knife"), [522] = S("knife_stiletto", "Stiletto Knife"),
        [523] = S("knife_widowmaker", "Talon Knife"), [525] = S("knife_skeleton", "Skeleton Knife")
    };

    public static ItemIconDescriptor Resolve(ushort definitionIndex)
        => Items.TryGetValue(definitionIndex, out var value) ? value : new("unknown", definitionIndex == 0 ? "Item" : $"Item {definitionIndex}", null);

    public static bool IsPickupDefinition(ushort definitionIndex) => Items.ContainsKey(definitionIndex);

    public static bool IsGrenade(ushort definitionIndex) => definitionIndex is >= 43 and <= 48;
    public static GrenadeKind Grenade(ushort definitionIndex) => definitionIndex switch
    {
        43 => GrenadeKind.Flashbang, 44 => GrenadeKind.HighExplosive, 45 => GrenadeKind.Smoke,
        46 => GrenadeKind.Molotov, 47 => GrenadeKind.Decoy, 48 => GrenadeKind.Incendiary, _ => GrenadeKind.None
    };

    private static ItemIconDescriptor D(string key, string name, int glyph) => new(key, name, char.ConvertFromUtf32(glyph));
    private static ItemIconDescriptor S(string key, string name) => new(key, name, null, key + ".svg");
}

public static class SvgIconCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, SvgIcon>? _icons;

    public static SvgIcon? Get(string name)
    {
        EnsureLoaded();
        return _icons!.TryGetValue(name, out var icon) ? icon : null;
    }

    public static Geometry HealthshotGeometry()
    {
        var geometry = Geometry.Parse("M 38,8 L 62,8 L 62,38 L 92,38 L 92,62 L 62,62 L 62,92 L 38,92 L 38,62 L 8,62 L 8,38 L 38,38 Z");
        geometry.Freeze(); return geometry;
    }

    private static void EnsureLoaded()
    {
        if (_icons is not null) return;
        lock (Gate)
        {
            if (_icons is not null) return;
            var result = new Dictionary<string, SvgIcon>(StringComparer.OrdinalIgnoreCase);
            var resource = Application.GetResourceStream(new Uri("/Vectra.External;component/Assets/obs_icons_svg.zip", UriKind.Relative));
            if (resource is not null)
            {
                using var archive = new ZipArchive(resource.Stream, ZipArchiveMode.Read, false);
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && entry.Length <= 512 * 1024))
                {
                    try
                    {
                        using var stream = entry.Open();
                        var document = XDocument.Load(stream, LoadOptions.None);
                        var root = document.Root; if (root is null) continue;
                        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value); if (viewBox.Width <= 0 || viewBox.Height <= 0) continue;
                        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                        foreach (var path in root.Descendants().Where(element => element.Name.LocalName == "path").Take(16))
                        {
                            var data = path.Attribute("d")?.Value;
                            if (string.IsNullOrWhiteSpace(data) || data.Length > 128 * 1024) continue;
                            var geometry = Geometry.Parse(data); group.Children.Add(geometry);
                        }
                        if (group.Children.Count == 0) continue;
                        group.Freeze(); result[entry.Name] = new SvgIcon(group, viewBox);
                    }
                    catch { }
                }
            }
            _icons = result;
        }
    }

    private static Rect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Rect.Empty;
        var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts.Select(part => double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)).All(valid => valid)
            ? new Rect(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture))
            : Rect.Empty;
    }
}

public sealed record SvgIcon(Geometry Geometry, Rect ViewBox);
