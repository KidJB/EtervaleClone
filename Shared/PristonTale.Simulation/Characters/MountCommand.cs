namespace PristonTale.Simulation.Characters;

// The chat command the stable window and the ride key send: "/mount" gets on or off the chosen mount, "/mount <id>" picks
// an owned mount and rides it, "/mount off" (or "/mount 0") gets off.
public static class MountCommand
{
    public const string Name = "/mount";

    // Toggle: 0 gets off, a mount id picks and rides it, null toggles riding.
    public static string Of(int? mount) => mount is null ? Name : mount == 0 ? $"{Name} off" : $"{Name} {mount}";

    public static bool TryParse(string text, out int? mount)
    {
        mount = null;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || !parts[0].Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            mount = 0;
            return true;
        }

        if (int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
            && id >= 0 && id <= Mounts.Max)
        {
            mount = id;
            return true;
        }

        return false;
    }
}
