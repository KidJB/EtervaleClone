using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Clans;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Gameplay.UI.Widgets;

// The new target frame (the settings' "Target frame: new"): a 301x80 bar at the top centre of the screen. The level sits
// in the ring on the left, the race or kind icon beside it, the name over the life bar, the life as current/maximum on
// the bar, its percentage at the bar's right end and the distance to the target under it. A player in a clan gets a strip
// with its mark and clan name under the bar. A player's level shows its tens only ("12x").
// The life always shows here: the classic target HP setting applies to the classic frame only.
public sealed partial class HudCombat
{
    private const string TargetFolder = "Hud/Target/";
    private const string BarOverlay = TargetFolder + "targetoverlay.png";
    private const string BarRing = TargetFolder + "targetbackground.png";
    private const string BarLife = TargetFolder + "targethp.png";
    private const string BarClanStrip = TargetFolder + "targetinfo.png";
    private const string TypeNpc = TargetFolder + "Type/npc.png";
    private const string TypePlayer = TargetFolder + "Type/player.png";
    public static readonly string[] TargetBarArt =
    [
        BarOverlay, BarRing, BarLife, BarClanStrip, TypeNpc, TypePlayer, TargetFolder + "Type/normal.png", TargetFolder + "Type/mutant.png",
        TargetFolder + "Type/undead.png", TargetFolder + "Type/demon.png", TargetFolder + "Type/mechanic.png"
    ];
    private const int BarWidth = 301;
    private const int BarHeight = 80;
    private const int LifeWidth = 208;
    private const int LifeHeight = 12;
    // Distance units per metre on the range line.
    private const int UnitsPerMetre = 27;
    private const float LevelScale = 22f / HudPainter.BakePixels;
    private const float SmallScale = 11f / HudPainter.BakePixels;
    private static readonly Color ClanStripColor = new(180, 180, 125);
    private static readonly Color RingLevelColor = new(227, 191, 152);

    private int _barLevelValue = -1;
    private bool _barLevelPlayer;
    private string _barLevelText = "";
    private long _lifeValue = -1;
    private int _lifeMaximum = -1;
    private string _lifeText = "";
    private int _rangeValue = -1;
    private string _rangeText = "";

    // The frame's box at the top centre; the player buttons hang under it.
    public static Rectangle TargetBarBox(HudLayout layout) => new((layout.Width >> 1) - BarWidth / 2, 0, BarWidth, BarHeight);

    private void DrawTargetBar(HudPainter painter, HudLayout layout, ulong target, Entity entity)
    {
        var box = TargetBarBox(layout);
        var x = box.X;
        var y = box.Y;
        var player = entity.Spawn.Player;
        if (player is { ClanId: not 0, ClanName.Length: > 0 })
        {
            painter.Sprite(BarClanStrip, x + 41, y + 52);
            painter.ClanMark(x + 61, y + 55);
            painter.Text(painter.Fit(player.ClanName, 150), x + 81, y + 55, ClanStripColor, true);
        }

        painter.Sprite(BarRing, x + 7, y + 11);
        painter.Sprite(BarOverlay, x, y);
        var showsLife = ShowsHealthBar(target, classic: false);
        if (showsLife)
        {
            var width = (int)((long)Math.Clamp(entity.Life, 0, entity.Spawn.MaxLife) * LifeWidth / entity.Spawn.MaxLife);
            if (width > 0)
            {
                painter.Stretch(BarLife, new Rectangle(x + 82, y + 39, width, LifeHeight), new Rectangle(0, 0, width, LifeHeight));
            }
        }

        if (TypeIcon(entity) is { } icon)
        {
            painter.Stretch(icon, new Rectangle(x + 62, y + 34, 16, 16));
        }

        var color = entity.Spawn.Kind switch
        {
            EntityKind.Monster => MonsterColor,
            EntityKind.Npc => NpcColor,
            _ => Color.White
        };
        painter.TextCentered(painter.Fit(Name(target), 195), x + 88, x + 283, y + 21, color, true, NameShadow);
        if (entity.Spawn.Kind != EntityKind.Npc)
        {
            var level = BarLevelText(Level(entity), player is not null);
            var levelWidth = painter.MeasureAt(level, LevelScale);
            painter.Text(level, x + 36 - levelWidth / 2, y + 30, RingLevelColor, LevelScale);
        }

        if (showsLife)
        {
            var life = LifeText(entity.Life, entity.Spawn.MaxLife);
            painter.Text(life, x + 186 - painter.MeasureAt(life, SmallScale) / 2, y + 39, Color.White, SmallScale);
            var percent = PercentText(HealthPercent(entity.Life, entity.Spawn.MaxLife));
            painter.Text(percent, x + 288 - painter.MeasureAt(percent, SmallScale), y + 54, Color.White, SmallScale);
        }

        if (_self != 0 && _self != target && _entities.TryGetValue(_self, out var self))
        {
            var dx = entity.Position.X - self.Position.X;
            var dz = entity.Position.Z - self.Position.Z;
            var range = RangeText((int)MathF.Sqrt(dx * dx + dz * dz));
            painter.Text(range, x + 293 - painter.MeasureAt(range, SmallScale), y + 66, Color.White, SmallScale);
        }
    }

    private string TypeIcon(Entity entity) => entity.Spawn.Kind switch
    {
        EntityKind.Monster => TargetTypeIcon(_catalog.GetMonster(entity.Spawn.TemplateId)?.Brood ?? MonsterBrood.Normal),
        EntityKind.Npc => TypeNpc,
        _ => entity.Spawn.Player is not null ? TypePlayer : null
    };

    private static string TargetTypeIcon(MonsterBrood brood) => brood switch
    {
        MonsterBrood.Mutant => TargetFolder + "Type/mutant.png",
        MonsterBrood.Undead => TargetFolder + "Type/undead.png",
        MonsterBrood.Demon => TargetFolder + "Type/demon.png",
        MonsterBrood.Mechanic => TargetFolder + "Type/mechanic.png",
        _ => TargetFolder + "Type/normal.png"
    };

    private string BarLevelText(int level, bool player)
    {
        if (level != _barLevelValue || player != _barLevelPlayer)
        {
            _barLevelValue = level;
            _barLevelPlayer = player;
            _barLevelText = level <= 0 ? "" : player && level >= 10 ? string.Create(CultureInfo.InvariantCulture, $"{level / 10}x")
                : level.ToString(CultureInfo.InvariantCulture);
        }

        return _barLevelText;
    }

    private string LifeText(long life, int maximum)
    {
        if (life != _lifeValue || maximum != _lifeMaximum)
        {
            _lifeValue = life;
            _lifeMaximum = maximum;
            _lifeText = string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, life)}/{maximum}");
        }

        return _lifeText;
    }

    private string RangeText(int units)
    {
        if (units != _rangeValue)
        {
            _rangeValue = units;
            _rangeText = units > 0 ? string.Create(CultureInfo.InvariantCulture, $"{units} ({units / UnitsPerMetre}m)") : "";
        }

        return _rangeText;
    }
}
