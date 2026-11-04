namespace PristonTale.Simulation.Characters;

// The chat command the pet collection window sends: "/pet <id>" sends that skin's pet out, "/pet off" (or "/pet 0") puts
// the pet away.
public static class PetCommand
{
    public const string Name = "/pet";

    public static string Out(int skin) => skin == 0 ? $"{Name} off" : $"{Name} {skin}";

    public static bool TryParse(string text, out int skin)
    {
        skin = 0;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out skin)
            && skin >= 0 && skin <= PetSkins.Max;
    }
}
