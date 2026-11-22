namespace PristonTale.Simulation.Items;

// The Rogue's grimoires are Etervale's own: each grimoire WX1nn mirrors the phantom WN1nn of the same number (stats,
// requirements, price, shops and drops), named with "Grimoire" for "Phantom". Only the numbers that have a model mirror.
public static class Grimoires
{
    public const string Family = "WX1";
    private const string PhantomFamily = "WN1";
    private const int LastModel = 27;

    public static bool TryMirror(string phantomKey, out string key)
    {
        key = "";
        if (phantomKey is null || !phantomKey.StartsWith(PhantomFamily, StringComparison.OrdinalIgnoreCase)
            || !ItemCodes.TryParse(phantomKey, out var code) || ItemCodes.Ordinal(code) is < 1 or > LastModel)
        {
            return false;
        }

        key = Family + phantomKey[PhantomFamily.Length..];
        return true;
    }

    public static string Name(string phantomName) => phantomName.Replace("Phantom", "Grimoire", StringComparison.OrdinalIgnoreCase);
}
