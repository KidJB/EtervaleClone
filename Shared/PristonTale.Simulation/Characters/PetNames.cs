namespace PristonTale.Simulation.Characters;

// A pet's name: letters, digits and single spaces, at most MaxLength characters; empty for no name (the pet then shows its
// skin's name). "/petname <name>" names the character's pet and "/petname" alone clears the name.
public static class PetNames
{
    public const int MaxLength = 16;
    // UTF-8 bytes the wire and the row keep: MaxLength letters of up to four bytes each.
    public const int MaxBytes = MaxLength * 4;
    public const string Command = "/petname";

    // A typed name made tidy (trimmed, runs of spaces made one); false for a name too long or with other characters.
    public static bool TryNormalize(string raw, out string name)
    {
        name = string.Join(' ', (raw ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > MaxLength)
        {
            return false;
        }

        foreach (var letter in name)
        {
            if (!char.IsLetterOrDigit(letter) && letter != ' ')
            {
                return false;
            }
        }

        return true;
    }

    // "/petname" followed by nothing or by the name; the name may be refused later by TryNormalize.
    public static bool TryParseCommand(string text, out string raw)
    {
        raw = "";
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed[Command.Length..];
        if (rest.Length > 0 && rest[0] != ' ')
        {
            return false;
        }

        raw = rest.Trim();
        return true;
    }

    // What shows over a pet: its name, or its skin's name without one.
    public static string Shown(string name, PetSkin skin) => string.IsNullOrEmpty(name) ? skin?.Name ?? "" : name;
}
