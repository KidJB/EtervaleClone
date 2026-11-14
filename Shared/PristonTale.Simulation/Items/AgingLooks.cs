namespace PristonTale.Simulation.Items;

// A colour of an aging look at a moment of its loop: light added to the item (1 = a full white light; above 1 glows).
public readonly record struct AgingColorKey(int Ms, float R, float G, float B);

// How an item aged to one level looks while held: its aging texture laid over it and sliding (texture units a second in
// U and V), and the light it adds, eased between the keys of a loop that lasts the last key's time.
public sealed record AgingLook(string Texture, float ScrollU, float ScrollV, AgingColorKey[] Keys)
{
    public int PeriodMs => Keys[^1].Ms;

    public (float R, float G, float B) Light(long nowMs)
    {
        var at = (int)(((nowMs % PeriodMs) + PeriodMs) % PeriodMs);
        for (var index = 1; index < Keys.Length; index++)
        {
            var to = Keys[index];
            if (at > to.Ms)
            {
                continue;
            }

            var from = Keys[index - 1];
            var t = to.Ms == from.Ms ? 1f : (at - from.Ms) / (float)(to.Ms - from.Ms);
            var eased = t * t * (3f - 2f * t);
            return (from.R + (to.R - from.R) * eased, from.G + (to.G - from.G) * eased, from.B + (to.B - from.B) * eased);
        }

        var last = Keys[^1];
        return (last.R, last.G, last.B);
    }
}

// The look of each glowing aging level, +4 to +20: a pulse from no light to the level's colour and back, its own aging
// texture, and from +11 a diagonal slide that quickens at +15 and +20. The higher the level, the stronger and quicker the
// pulse: its peak grows from 1.5 times the colour at +4 to three times it at +20 (overbright), and its loop shortens from 1000 ms
// to 700 ms.
public static class AgingLooks
{
    private const string Folder = "image/sinImage/Items/DropItem/aging";
    private const int SlowestPulseMs = 1000;
    private const int QuickestPulseMs = 700;
    private const float WeakestPeak = 1.5f;
    private const float StrongestPeak = 3f;

    private static readonly AgingLook[] Levels =
    [
        Pulse(4, 0.2f, 0.2f, 0.2f, 0f),
        Pulse(5, 0.47f, 0.39f, 0.03f, 0f),
        Pulse(6, 0f, 0.43f, 0.11f, 0f),
        Pulse(7, 0f, 0.19f, 0.54f, 0f),
        Pulse(8, 0.39f, 0f, 0.35f, 0f),
        Pulse(9, 0.58f, 0.23f, 0f, 0f),
        Pulse(10, 0.58f, 0.03f, 0f, 0f),
        Pulse(11, 0.07f, 0.86f, 0.74f, 0.05f),
        Pulse(12, 0.04f, 0.86f, 0.11f, 0.05f),
        Pulse(13, 0.66f, 0.15f, 0.66f, 0.05f),
        Pulse(14, 0.98f, 0.11f, 0.62f, 0.05f),
        Pulse(15, 0.11f, 0.74f, 1f, 0.1f),
        Pulse(16, 0.98f, 0.5f, 0.11f, 0.1f),
        Pulse(17, 0.47f, 0.11f, 0.11f, 0.1f),
        Pulse(18, 0.5f, 0f, 1f, 0.1f),
        Pulse(19, 0.86f, 0.94f, 0.27f, 0.1f),
        Pulse(20, 0.5f, 0.5f, 0.5f, 0.2f)
    ];

    public const int FirstLevel = HeldItemGlow.FirstGlowingAge;
    public static int LastLevel => FirstLevel + Levels.Length - 1;

    public static AgingLook For(int level) => level >= FirstLevel && level <= LastLevel ? Levels[level - FirstLevel] : null;

    // 0 at the first glowing level, 1 at the last.
    private static float Rise(int level) => (level - HeldItemGlow.FirstGlowingAge) / (float)(HeldItemGlow.LastGlowingAge - HeldItemGlow.FirstGlowingAge);

    private static AgingLook Pulse(int level, float r, float g, float b, float scroll)
    {
        var rise = Rise(level);
        var strength = WeakestPeak + (StrongestPeak - WeakestPeak) * rise;
        var period = (int)MathF.Round(SlowestPulseMs + (QuickestPulseMs - SlowestPulseMs) * rise);
        return new(Folder + level + ".bmp", scroll, scroll,
            [new(0, 0f, 0f, 0f), new(period / 2, r * strength, g * strength, b * strength), new(period, 0f, 0f, 0f)]);
    }
}
