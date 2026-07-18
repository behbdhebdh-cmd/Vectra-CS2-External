namespace Vectra.External;

public static class WeaponCatalog
{
    private static readonly IReadOnlyDictionary<ushort, (string Name, int Clip)> Entries = new Dictionary<ushort, (string, int)> {
        [1] = ("Deagle", 7), [2] = ("Dual Berettas", 30), [3] = ("Five-SeveN", 20), [4] = ("Glock", 20), [7] = ("AK-47", 30), [8] = ("AUG", 30),
        [9] = ("AWP", 5), [10] = ("FAMAS", 25), [11] = ("G3SG1", 20), [13] = ("Galil", 35), [14] = ("M249", 100), [16] = ("M4A4", 30),
        [17] = ("MAC-10", 30), [19] = ("P90", 50), [23] = ("MP5-SD", 30), [24] = ("UMP-45", 25), [25] = ("XM1014", 7), [26] = ("PP-Bizon", 64),
        [27] = ("MAG-7", 5), [28] = ("Negev", 150), [29] = ("Sawed-Off", 7), [30] = ("Tec-9", 18), [31] = ("Zeus", 1), [32] = ("P2000", 13),
        [33] = ("MP7", 30), [34] = ("MP9", 30), [35] = ("Nova", 8), [36] = ("P250", 13), [38] = ("SCAR-20", 20), [39] = ("SG 553", 30),
        [40] = ("SSG 08", 10), [43] = ("Flashbang", 1), [44] = ("HE Grenade", 1), [45] = ("Smoke", 1), [46] = ("Molotov", 1), [47] = ("Decoy", 1),
        [48] = ("Incendiary", 1), [49] = ("C4", 1), [57] = ("Healthshot", 1), [59] = ("Knife", 0), [60] = ("M4A1-S", 25), [61] = ("USP-S", 12),
        [63] = ("CZ75-Auto", 12), [64] = ("R8 Revolver", 8)
    };

    public static WeaponSnapshot From(ushort definitionIndex, int clip)
    {
        if (Entries.TryGetValue(definitionIndex, out var weapon)) return new WeaponSnapshot(true, definitionIndex, weapon.Name, Math.Max(clip, 0), weapon.Clip);
        return new WeaponSnapshot(true, definitionIndex, ItemIconCatalog.Resolve(definitionIndex).Name, Math.Max(clip, 0), 0);
    }
}
