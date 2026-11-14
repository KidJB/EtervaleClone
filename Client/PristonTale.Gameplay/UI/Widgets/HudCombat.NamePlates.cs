using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.UI.Widgets;

// Name boxes over the scene: an NPC's name always within 768 units of the camera, a party member's always with its life
// bar, a clan mate's always and another player's while it is under the cursor or the clicked target, a player's clan in
// a "[Clan]" line over its name (for another clan only while hovered or clicked, as in the original), a monster's only
// while it is under the cursor or is the clicked target. A summon or crystal pet's box shows within
// the same range, with its owner's name in parentheses on a second line and, on the viewer's own, its life bar. Farther
// boxes draw first; the hovered and the clicked one draw last. Every box is HudPainter.NameBox,
// the speech bubble's fill and gold rim with rounded corners, highlighted under the cursor. Classic (classicNamePlates)
// is the original's layout: the HUD text size and the life bar while the cursor is on the monster. Otherwise the same box
// holds smaller text, and the bar shows once the monster was clicked or the local player swung at it, not for the
// cursor alone.
public sealed partial class HudCombat
{
    // Classic: the frame's top this far above the head point. Compact: the fill's bottom this far above it.
    private const int ClassicPlateLift = 36;
    private const int PlateLift = 12;
    private const int PlatePadY = 1;
    private const float PlateTextScale = 12f / HudPainter.BakePixels;
    // The original's 100x16 life bar drawn at 0.6 x 0.7.
    private const int PlateBarWidth = 60;
    private const int PlateBarHeight = 11;
    private readonly List<(ulong Id, HudOverheadPoint Point, bool Focus)> _plates = new();

    // The top edge of each name plate drawn this frame, in design pixels; what draws over a character keeps above it.
    public Dictionary<ulong, int> PlateTops { get; } = new();

    // The quest mark art over an NPC, by its name (HudQuestBook.FillMarks).
    public IReadOnlyDictionary<string, string> QuestMarks { get; set; } = new Dictionary<string, string>();

    // The mark stands this far above the head point, over the name box.
    private const int QuestMarkLift = 84;
    private const int QuestMarkWidth = 12;

    // Whether a player is in the local player's party.
    public Func<ulong, bool> InParty { get; set; } = static _ => false;

    // Whether a monster's name box carries its life bar.
    public static bool ShowsPlateBar(bool classic, bool hovered, bool traced, bool attacked) => classic ? hovered : traced || attacked;

    public void DrawNamePlates(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project, ulong hovered, ulong traced, bool classic)
    {
        DrawQuestTargets(painter, layout, project);
        _plates.Clear();
        PlateTops.Clear();
        foreach (var (id, entity) in _entities)
        {
            var focus = id == hovered || id == traced;
            var kind = entity.Spawn.Kind;
            var member = kind == EntityKind.Player && InParty(id);
            var mate = kind == EntityKind.Player && id != _self && OwnClan != 0 && ClanOf(id) == OwnClan;
            var summon = kind == EntityKind.Monster && entity.Spawn.OwnerId != 0;
            var titled = kind == EntityKind.Player && entity.Spawn.Player is { Title: not 0 };
            var shown = kind == EntityKind.Npc || member || mate || titled || kind == EntityKind.Player && id != _self && focus
                || (kind == EntityKind.Monster && entity.Life > 0 && (summon || focus));
            if (shown && project(id) is { } point && (focus || member || point.Depth < HudNameColors.OverheadDepth))
            {
                _plates.Add((id, point, focus));
            }
        }

        _plates.Sort(static (left, right) => left.Focus != right.Focus ? (left.Focus ? 1 : -1) : right.Point.Depth.CompareTo(left.Point.Depth));
        var scale = Math.Max(1, layout.Scale);
        foreach (var (id, point, _) in _plates)
        {
            var entity = _entities[id];
            var monster = entity.Spawn.Kind == EntityKind.Monster;
            var member = entity.Spawn.Kind == EntityKind.Player;
            var summon = monster && entity.Spawn.OwnerId != 0;
            var color = member || summon ? HudNameColors.Party : !monster ? HudNameColors.Npc : id == traced ? HudNameColors.HoveredMonster : HudNameColors.Monster;
            var owner = summon ? OwnerLabel(entity) : null;
            var clan = member || entity.Spawn.Kind == EntityKind.Player && (id == hovered || id == traced || OwnClan != 0 && ClanOf(id) == OwnClan)
                ? ClanLine(id)
                : null;
            var x = (int)(point.Screen.X / scale);
            var y = (int)(point.Screen.Y / scale);
            if (!monster && !member && QuestMarks.TryGetValue(Name(id), out var mark))
            {
                painter.Sprite(mark, x - QuestMarkWidth / 2, y - QuestMarkLift);
            }
            var ownSummon = summon && entity.Spawn.OwnerId == _self;
            var title = TitleLine(entity);
            if (classic)
            {
                DrawClassicPlate(painter, layout, Name(id), owner ?? clan, owner is null && clan is not null, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(true, id == hovered, id == traced, entity.Attacked) ? entity : null, title, out var classicTop);
                PlateTops[id] = classicTop;
            }
            else
            {
                DrawPlate(painter, layout, Name(id), owner ?? clan, owner is null && clan is not null, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(false, id == hovered, id == traced, entity.Attacked) ? entity : null, title, out var plateTop);
                PlateTops[id] = plateTop;
            }
        }
    }

    // "(owner)" under a summon's name, formatted once per summon; null while its owner is not in view.
    private string OwnerLabel(Entity summon)
    {
        if (summon.OwnerLabel is null && PlayerName(summon.Spawn.OwnerId) is { } owner)
        {
            summon.OwnerLabel = $"({owner})";
        }

        return summon.OwnerLabel;
    }

    // A clan's emblem stands left of a name box's text at its full size, centred on the lines; the text is centred beside it.
    private const int EmblemSize = Protocol.Packets.ClanWire.MarkSize;
    private const int EmblemGap = 2;

    // A box that would leave the screen is not drawn, as in the original. A second line (a summon's owner under the name,
    // a player's clan over it) and an achievement title, the top line, grow the box upwards.
    private static void DrawClassicPlate(HudPainter painter, HudLayout layout, string name, string owner, bool clanLine, int anchorX, int anchorY, Color color,
        bool hovered, Entity bar, string title, out int top)
    {
        var nameWidth = painter.Measure(name);
        var ownerWidth = owner is null ? 0 : painter.Measure(owner);
        var titleWidth = title is null ? 0 : painter.Measure(title);
        var lineHeight = Math.Max(16, painter.TextHeight);
        var emblem = clanLine ? EmblemSize : 0;
        var emblemSpan = clanLine ? emblem + EmblemGap : 0;
        var textWidth = Math.Max(Math.Max(nameWidth, ownerWidth), titleWidth);
        var width = textWidth + emblemSpan;
        var textCenter = anchorX + emblemSpan / 2;
        var titleLine = title is null ? 0 : lineHeight;
        var extraLine = (owner is null ? 0 : lineHeight) + titleLine;
        var frameHeight = 4 + lineHeight + extraLine + 6 + 2;
        var x = anchorX - width / 2 - 9;
        var y = anchorY - ClassicPlateLift - extraLine;
        top = y;
        if (x < 0 || y < 0 || x + width + 18 > layout.Width || y + frameHeight + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        painter.NameBox(new Rectangle(x, y, width + 18, frameHeight), hovered);
        painter.Flush();
        var firstTop = y + 5 + 2 + titleLine;
        var nameTop = clanLine ? firstTop + lineHeight : firstTop;
        painter.Text(name, textCenter - nameWidth / 2, nameTop, color, false);
        if (title is not null)
        {
            painter.Text(title, textCenter - titleWidth / 2, y + 5 + 2, HudNameColors.Title, false);
        }

        if (owner is not null && clanLine)
        {
            painter.ClanMark(new Rectangle(anchorX - width / 2, y + 5 + (extraLine + lineHeight - emblem) / 2, emblem, emblem));
            painter.Text(owner, textCenter - ownerWidth / 2, firstTop, HudNameColors.Clan, false);
        }
        else if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, firstTop + lineHeight, HudNameColors.Player, false);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + frameHeight - 2, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }

    // A lone name in the NPCs' name box over an anchor (the pets' names).
    public static void DrawNamePlate(HudPainter painter, HudLayout layout, string name, int anchorX, int anchorY, Color color) =>
        DrawPlate(painter, layout, name, null, false, anchorX, anchorY, color, false, null, null, out _);

    // The name box around the smaller text, the capitals centred inside with room for descenders.
    private static void DrawPlate(HudPainter painter, HudLayout layout, string name, string owner, bool clanLine, int anchorX, int anchorY, Color color, bool hovered,
        Entity bar, string title, out int boxTop)
    {
        var nameWidth = painter.MeasureAt(name, PlateTextScale);
        var ownerWidth = owner is null ? 0 : painter.MeasureAt(owner, PlateTextScale);
        var (line, textTop) = painter.NameBoxText(PlateTextScale, PlatePadY);
        var titleWidth = title is null ? 0 : painter.MeasureAt(title, PlateTextScale);
        var titleLine = title is null ? 0 : line;
        var lines = (owner is null ? 1 : 2) + (title is null ? 0 : 1);
        var emblem = clanLine ? EmblemSize : 0;
        var emblemPad = Math.Max(0, emblem - line * lines);
        var emblemSpan = clanLine ? emblem + EmblemGap : 0;
        var contentWidth = Math.Max(Math.Max(nameWidth, ownerWidth), titleWidth) + emblemSpan;
        var textCenter = anchorX + emblemSpan / 2;
        var (width, textHeight) = HudPainter.NameBoxExtent(contentWidth, line, lines);
        var height = textHeight + emblemPad;
        var x = anchorX - width / 2;
        var y = anchorY - PlateLift - height;
        boxTop = y;
        if (x < 0 || y < 0 || x + width > layout.Width || y + height + 1 + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.DrawNameBox(new Rectangle(x, y, width, height), hovered);
        var top = y + textTop + emblemPad / 2 + titleLine;
        painter.Text(name, textCenter - nameWidth / 2, clanLine ? top + line : top, color, PlateTextScale);
        if (title is not null)
        {
            painter.Text(title, textCenter - titleWidth / 2, y + textTop + emblemPad / 2, HudNameColors.Title, PlateTextScale);
        }

        if (owner is not null && clanLine)
        {
            painter.ClanMark(new Rectangle(anchorX - contentWidth / 2, y + HudPainter.NameBoxTop + (line * lines + emblemPad - emblem) / 2, emblem, emblem));
            painter.Text(owner, textCenter - ownerWidth / 2, top, HudNameColors.Clan, PlateTextScale);
        }
        else if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, top + line, HudNameColors.Player, PlateTextScale);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + height + 1, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }
}
