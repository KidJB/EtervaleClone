using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Units;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// The running premium timers, in the order the items were used: the Personal rows of the Active Buffs panel (HudWorldBuffs).
// Hovering a row shows the name, the effect and the time left.
public sealed class HudPremiumTimers
{
    public const int Left = 8;
    public const int Top = 80;
    public const int Step = 36;
    public const int Icon = 32;
    private const int RowIcon = 20;
    private const int RowStep = 22;
    private const int HoverReach = 250;
    private const string Folder = "image/Sinimage/Premium/";
    private static readonly long[] Tiers = [3600, 10800, 86400, 604800, 2592000];
    private static readonly Color Effect = new(50, 250, 50);

    private readonly HudSprites _sprites;
    private readonly List<Entry> _entries = new();
    private int _left = Left;
    private int _width = Icon;
    private long _revision = -1;

    public HudPremiumTimers(HudSprites sprites)
    {
        _sprites = sprites;
        var paths = new List<string>();
        foreach (var kind in Enum.GetValues<PremiumEffectKind>().Where(static kind => !PremiumTimers.PausesOffline(kind)))
        {
            paths.Add(IconPath(kind));
            if (BackPath(kind) is { } back)
            {
                paths.Add(back);
            }
        }

        _sprites.Request(paths);
    }

    // Adopts the server's timer list; nowMs is the HUD clock the remaining seconds are counted from.
    public void Apply(PremiumTimerSnapshot snapshot, double nowMs)
    {
        if (snapshot is null || snapshot.Revision == _revision)
        {
            return;
        }

        _revision = snapshot.Revision;
        var next = new List<Entry>();
        foreach (var timer in snapshot.Timers)
        {
            // Boosters and the skill-delay item show in the running-effects bar instead.
            if (PremiumTimers.PausesOffline(timer.Kind))
            {
                continue;
            }

            var existing = _entries.FirstOrDefault(entry => entry.Kind == timer.Kind);
            var entry = existing ?? new Entry { Kind = timer.Kind, Icon = IconPath(timer.Kind), Back = BackPath(timer.Kind), Y = Top + Step * next.Count };
            entry.Value = timer.Value;
            entry.EndMs = nowMs + timer.RemainingSeconds * 1000d;
            if (timer.RemainingSeconds > 10)
            {
                entry.Blinking = false;
            }

            next.Add(entry);
        }

        _entries.Clear();
        _entries.AddRange(next);
    }

    public bool Contains(Point pointer) => At(pointer) is not null;

    public int Count => _entries.Count;

    // The Personal rows of the Active Buffs panel from (x, y): each timer's icon, name and time left. Answers the y below them.
    public int Draw(HudPainter painter, int x, int y, int width, double nowMs)
    {
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            if (_entries[index].EndMs <= nowMs)
            {
                _entries.RemoveAt(index);
            }
        }

        _left = x;
        _width = width;
        foreach (var entry in _entries)
        {
            entry.Y = y;
            painter.Stretch(entry.Icon, new Rectangle(x, y, RowIcon, RowIcon));
            var remaining = Remaining(entry, nowMs);
            if (entry.RowTime is null || entry.RowSeconds != remaining)
            {
                entry.RowSeconds = remaining;
                entry.RowTime = string.Create(CultureInfo.InvariantCulture, $"{remaining / 3600:00}:{remaining / 60 % 60:00}:{remaining % 60:00}");
            }

            painter.Text(painter.Fit(entry.Name ??= ItemStatuses.PremiumName(entry.Kind), width - RowIcon - 72), x + RowIcon + 4, y + 2);
            painter.Text(entry.RowTime, x + width - 64, y + 2, Effect);
            y += RowStep;
        }

        return y;
    }

    // The hover box and text, drawn late so they stay above the windows.
    public void DrawHover(HudPainter painter, Point pointer, double nowMs)
    {
        if (At(pointer) is not { } entry)
        {
            return;
        }

        var x = _left - HoverReach;
        var y = (int)entry.Y;
        // The lines are made again only when the time left reaches another second.
        var remaining = Remaining(entry, nowMs);
        if (entry.Hover is null || entry.HoverSeconds != remaining)
        {
            entry.Hover = [ItemStatuses.PremiumName(entry.Kind), Describe(entry.Kind, entry.Value), TimeLeft(remaining)];
            entry.HoverSeconds = remaining;
        }

        var lines = entry.Hover;
        var widest = 0;
        foreach (var line in lines)
        {
            widest = Math.Max(widest, painter.Measure(line));
        }

        // The original box is 160 x 96; it grows to the right when this font's lines are wider.
        var width = Math.Max(160, 20 + widest + 16);
        painter.InfoBox(HudPainter.InfoBoxBounds(x + 30, y, width, 96));
        painter.Text(lines[0], x + 50, y + 20, Color.White);
        painter.Text(lines[1], x + 50, y + 50, Effect);
        painter.Text(lines[2], x + 50, y + 66, Effect);
    }

    public static string TimeLeft(long seconds) =>
        ClientText.Format("Hud.TimeLeft", string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}"));

    // Rows of the grey cover: 32 minus the time left in 32nds of the smallest standard tier above it.
    public static int Cover(PremiumEffectKind kind, long remainingSeconds)
    {
        if (kind is PremiumEffectKind.PhoenixPet || PremiumTimers.IsHelperPet(kind))
        {
            return 0;
        }

        foreach (var tier in Tiers)
        {
            if (remainingSeconds > 0 && remainingSeconds < tier)
            {
                return (int)Math.Clamp(Icon - remainingSeconds / (tier / Icon), 0, Icon);
            }
        }

        return 0;
    }

    public static string Describe(PremiumEffectKind kind, int value) => kind switch
    {
        PremiumEffectKind.ThirdEyes => ClientText.Format("Premium.ThirdEyes", value),
        PremiumEffectKind.ExperienceUp => ClientText.Format("Premium.ExperienceUp", value),
        PremiumEffectKind.VampiricCuspid or PremiumEffectKind.VampiricCuspidEx => ClientText.Get("Premium.Vampiric"),
        PremiumEffectKind.ManaRecharging => ClientText.Get("Premium.ManaRecharging"),
        PremiumEffectKind.MightOfAwell => ClientText.Format("Premium.MightOfAwell", value),
        PremiumEffectKind.ManaReduce => ClientText.Format("Premium.ManaReduce", value),
        PremiumEffectKind.PhoenixPet => ClientText.Get("Premium.Phoenix"),
        PremiumEffectKind.HelpPetTerry => ClientText.Format("Premium.ManaReduce", 20),
        PremiumEffectKind.HelpPetMute => ClientText.Get("Premium.Mute"),
        PremiumEffectKind.HelpPetNepsys or PremiumEffectKind.HelpPetIo => ClientText.Get("Premium.FightsBeside"),
        PremiumEffectKind.StaminaReduce => ClientText.Format("Premium.StaminaReduce", value),
        _ => DataText.StatusEffect(ItemStatuses.PremiumKeepCode(kind)) ?? ""
    };

    public static string IconPath(PremiumEffectKind kind) => Folder + kind switch
    {
        PremiumEffectKind.ThirdEyes => "ThirdEyes.tga",
        PremiumEffectKind.ExperienceUp => "ExpUpPotion.tga",
        PremiumEffectKind.VampiricCuspid => "Vampiric.tga",
        PremiumEffectKind.ManaRecharging => "M_Rechar.tga",
        PremiumEffectKind.MightOfAwell => "M_Might.tga",
        PremiumEffectKind.ManaReduce => "M_Reduce.tga",
        PremiumEffectKind.PhoenixPet => "M_Phoenix.TGA",
        PremiumEffectKind.HelpPetTerry => "Pet_Terry-pose.tga",
        PremiumEffectKind.HelpPetNepsys => "Pet_nepsys-pose.tga",
        PremiumEffectKind.HelpPetIo => "Pet_Io-pose.tga",
        PremiumEffectKind.HelpPetMute => "Pet-mute-pose.tga",
        PremiumEffectKind.VampiricCuspidEx => "Vampiric_EX.tga",
        _ => "S_Reduce.tga"
    };

    public static string BackPath(PremiumEffectKind kind) => kind switch
    {
        PremiumEffectKind.ThirdEyes => Folder + "ThirdEyes_Back.bmp",
        PremiumEffectKind.ExperienceUp => Folder + "ExpUpPotion_Back.bmp",
        PremiumEffectKind.VampiricCuspid => Folder + "Vampiric_back.bmp",
        PremiumEffectKind.ManaRecharging => Folder + "M_Rechar_back.bmp",
        PremiumEffectKind.MightOfAwell => Folder + "M_Might_back.bmp",
        PremiumEffectKind.ManaReduce => Folder + "M_Reduce_back.bmp",
        PremiumEffectKind.VampiricCuspidEx => Folder + "Vampiric_back_EX.bmp",
        PremiumEffectKind.StaminaReduce => Folder + "S_Reduce_back.bmp",
        _ => null
    };

    private Entry At(Point pointer)
    {
        foreach (var entry in _entries)
        {
            if (pointer.X > _left && pointer.X < _left + _width && pointer.Y > entry.Y && pointer.Y < entry.Y + RowStep)
            {
                return entry;
            }
        }

        return null;
    }

    private static long Remaining(Entry entry, double nowMs) => (long)Math.Max(0d, Math.Ceiling((entry.EndMs - nowMs) / 1000d));

    private sealed class Entry
    {
        public PremiumEffectKind Kind { get; init; }
        public string Icon { get; init; }
        public string Back { get; init; }
        public string[] Hover { get; set; }
        public long HoverSeconds { get; set; }
        public int Value { get; set; }
        public double EndMs { get; set; }
        public double Y { get; set; }
        public string Name { get; set; }
        public string RowTime { get; set; }
        public long RowSeconds { get; set; }
        public double Alpha { get; set; }
        public bool Blinking { get; set; }
        public bool Rising { get; set; }
    }
}
