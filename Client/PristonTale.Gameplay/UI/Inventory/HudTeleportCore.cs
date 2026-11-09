using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Inventory;

public enum HudTeleportAction
{
    None,
    Close,
    Choose
}

// The kind of a Teleport Core destination, shown as a coloured tag: a town, a field where players fight, or a hunting field.
public enum HudTeleportTag : byte
{
    Town,
    Pvp,
    Exp
}

// The Teleport Core window (PTEU's teleport window art): the title plate and its TELEPORT header over a dark frame; on the
// left the destinations with their tag and entry level, scrolled by the wheel; beside them the chosen field's map, name,
// level and tag with the TELEPORT button, which asks for confirmation before the core is used; on the right the world's
// boss timers (HudBossList); the CLOSE button under the frame.
public sealed class HudTeleportCore(HudSprites sprites)
{
    public const int Width = 640;
    public const int Height = 400;
    public const int X = 80;
    public const int Y = 95;
    private const int RowHeight = 30;
    private const int ListWidth = 226;
    private const int BossRow = 30;
    private const int PlateWidth = 184;
    private const int HeaderWidth = 61;
    private static readonly Color TimeColor = new(235, 70, 60);
    private static readonly Color[] TagColors = [new(0, 150, 220), new(170, 22, 22), new(140, 40, 160)];
    private static readonly string[] TagKeys = ["TeleportCore.Tag.Town", "TeleportCore.Tag.Pvp", "TeleportCore.Tag.Exp"];

    // The field of each destination (by index), for its map and tag; built once.
    private static readonly FieldDef[] Fields = BuildFields();

    private readonly HudSprites _sprites = sprites;
    private readonly string[] _levels = TeleportCoreDestinations.All.Select(static entry => Math.Max(1, entry.RequiredLevel).ToString(CultureInfo.InvariantCulture)).ToArray();
    private int _listScroll;
    private int _bossScroll;
    private TeleportCoreDestination _chosen;
    private string _preview;
    private string _previewHd;
    private string _levelLine;

    public bool IsOpen { get; private set; }
    public Guid ItemId { get; private set; }
    public TeleportCoreDestination Selected { get; private set; }

    public static Rectangle Box => new(X, Y, Width, Height);
    public static Rectangle LeftPanel => new(X + 12, Y + 28, 404, Height - 72);
    public static Rectangle RightPanel => new(X + 428, Y + 28, Width - 440, Height - 72);
    public static Rectangle CloseButton => HudPteuWindow.CloseButton(X + Width / 2, Y + Height - 34);
    public static Rectangle TeleportButton => new(LeftPanel.X + ListWidth + 14, LeftPanel.Bottom - 40, 148, 30);
    private static Rectangle List => new(LeftPanel.X + 4, LeftPanel.Y + 4, ListWidth, LeftPanel.Height - 8);
    private static Rectangle Preview => new(LeftPanel.X + ListWidth + 14, LeftPanel.Y + 8, 148, 148);
    private static int VisibleRows => List.Height / RowHeight;

    // The list row shown at a visible position (0 the top one).
    public static Rectangle Row(int visibleRow) => new(List.X, List.Y + visibleRow * RowHeight, List.Width, RowHeight - 1);
    private static int VisibleBosses => (RightPanel.Height - 28) / BossRow;

    public static HudTeleportTag TagOf(FieldDef field) => field is null ? HudTeleportTag.Exp
        : field.State == FieldState.Village ? HudTeleportTag.Town
        : field.State == FieldState.Castle || field.FieldCode == FieldCodes.Castle ? HudTeleportTag.Pvp
        : HudTeleportTag.Exp;

    public void Open(Guid itemId)
    {
        IsOpen = true;
        ItemId = itemId;
        Selected = null;
        _listScroll = 0;
        _bossScroll = 0;
        Choose(TeleportCoreDestinations.All[0]);
    }

    public void Close()
    {
        IsOpen = false;
        Selected = null;
        ReleasePreview();
    }

    public bool Contains(Point pointer) => IsOpen && (Box.Contains(pointer) || HudPteuWindow.Plate(Box, PlateWidth).Contains(pointer));

    public TeleportCoreDestination At(Point pointer)
    {
        if (!IsOpen || !List.Contains(pointer))
        {
            return null;
        }

        var index = _listScroll + (pointer.Y - List.Y) / RowHeight;
        return index < TeleportCoreDestinations.All.Count && (pointer.Y - List.Y) / RowHeight < VisibleRows ? TeleportCoreDestinations.All[index] : null;
    }

    // The wheel scrolls the list or the boss timers under the pointer.
    public void Scroll(int wheelDelta, Point pointer, int bossCount)
    {
        if (!IsOpen || wheelDelta == 0)
        {
            return;
        }

        var step = -Math.Sign(wheelDelta);
        if (List.Contains(pointer))
        {
            _listScroll = Math.Clamp(_listScroll + step, 0, Math.Max(0, TeleportCoreDestinations.All.Count - VisibleRows));
        }
        else if (RightPanel.Contains(pointer))
        {
            _bossScroll = Math.Clamp(_bossScroll + step, 0, Math.Max(0, bossCount - VisibleBosses));
        }
    }

    public HudTeleportAction Click(Point pointer)
    {
        if (CloseButton.Contains(pointer))
        {
            Close();
            return HudTeleportAction.Close;
        }

        if (TeleportButton.Contains(pointer) && _chosen is not null)
        {
            Selected = _chosen;
            IsOpen = false;
            ReleasePreview();
            return HudTeleportAction.Choose;
        }

        if (At(pointer) is { } destination)
        {
            Choose(destination);
        }

        return HudTeleportAction.None;
    }

    public void Draw(HudPainter painter, Point pointer, HudBossList bosses, double nowMs)
    {
        if (!IsOpen)
        {
            return;
        }

        HudPteuWindow.Frame(painter, Box);
        var plate = HudPteuWindow.Plate(Box, PlateWidth);
        HudPteuWindow.DrawPlate(painter, plate);
        painter.Sprite(HudPteuWindow.TeleportHeader, plate.X + (plate.Width - HeaderWidth) / 2, plate.Y + 14);
        HudPteuWindow.Panel(painter, LeftPanel);
        HudPteuWindow.Panel(painter, RightPanel);
        DrawList(painter, pointer);
        HudPteuWindow.SeparatorAt(painter, List.Right + 4, LeftPanel.Y + 6, LeftPanel.Height - 12);
        DrawPreview(painter, pointer);
        DrawBosses(painter, bosses, nowMs);
        HudPteuWindow.DrawClose(painter, CloseButton, pointer);
    }

    public static string ConfirmText(TeleportCoreDestination destination) => destination?.Name ?? "";

    private void DrawList(HudPainter painter, Point pointer)
    {
        var list = List;
        var hovered = At(pointer);
        var end = Math.Min(TeleportCoreDestinations.All.Count, _listScroll + VisibleRows);
        for (var index = _listScroll; index < end; index++)
        {
            var entry = TeleportCoreDestinations.All[index];
            var row = Row(index - _listScroll);
            if (ReferenceEquals(entry, _chosen) || ReferenceEquals(entry, hovered))
            {
                painter.Fill(row, ReferenceEquals(entry, _chosen) ? HudPteuWindow.SelectedColor : HudPteuWindow.HoverColor);
            }

            DrawTag(painter, TagOf(Fields[index]), row.X + 2, row.Y + 8);
            painter.Text(painter.Fit(entry.Name, row.Width - 84), row.X + 46, row.Y + 7, HudPteuWindow.TextColor);
            var level = _levels[index];
            painter.Text(level, row.Right - 14 - painter.Measure(level), row.Y + 7, HudPteuWindow.TextColor);
            HudPteuWindow.SeparatorBelow(painter, row);
        }

        HudPteuWindow.Scroller(painter, list, _listScroll, TeleportCoreDestinations.All.Count, VisibleRows);
    }

    private void DrawPreview(HudPainter painter, Point pointer)
    {
        if (_chosen is null)
        {
            return;
        }

        var preview = Preview;
        var art = _previewHd is not null && painter.Texture(_previewHd) is not null ? _previewHd : _preview;
        if (art is not null && painter.Texture(art) is { } map)
        {
            var scale = MathF.Min((float)preview.Width / map.Width, (float)preview.Height / map.Height);
            var width = (int)(map.Width * scale);
            var height = (int)(map.Height * scale);
            painter.Stretch(art, new Rectangle(preview.X + (preview.Width - width) / 2, preview.Y + (preview.Height - height) / 2, width, height));
        }

        var y = preview.Bottom + 6;
        painter.TextCentered(painter.Fit(_chosen.Name, preview.Width), preview.X, preview.Right, y, Color.White);
        painter.Text(_levelLine, preview.X, y + 22, HudPteuWindow.TextColor);
        var mapType = ClientText.Get("TeleportCore.MapType");
        painter.Text(mapType, preview.X, y + 40, HudPteuWindow.TextColor);
        DrawTag(painter, TagOf(Fields[_chosen.Index]), preview.X + painter.Measure(mapType) + 6, y + 42);

        var button = TeleportButton;
        HudPteuWindow.DrawPlate(painter, new Rectangle(button.X, button.Y - 7, button.Width, HudPteuWindow.PlateHeight), ClientText.Get("TeleportCore.Teleport"),
            button.Contains(pointer) ? HudPteuWindow.SectionColor : HudPteuWindow.TextColor);
    }

    private void DrawBosses(HudPainter painter, HudBossList bosses, double nowMs)
    {
        var panel = RightPanel;
        painter.Text(ClientText.Get("TeleportCore.Bosses"), panel.X + 8, panel.Y + 6, HudPteuWindow.SectionColor);
        HudPteuWindow.SeparatorBelow(painter, new Rectangle(panel.X + 6, panel.Y, panel.Width - 12, 24));
        if (bosses.Count == 0)
        {
            painter.Text(ClientText.Get("TeleportCore.NoBosses"), panel.X + 8, panel.Y + 32, HudPteuWindow.TextColor);
            return;
        }

        _bossScroll = Math.Clamp(_bossScroll, 0, Math.Max(0, bosses.Count - VisibleBosses));
        var end = Math.Min(bosses.Count, _bossScroll + VisibleBosses);
        for (var index = _bossScroll; index < end; index++)
        {
            var y = panel.Y + 28 + (index - _bossScroll) * BossRow;
            var time = bosses.NextText(index, nowMs);
            var timeWidth = painter.Measure(time);
            painter.Text(painter.Fit(bosses[index].BossName, panel.Width - timeWidth - 28), panel.X + 10, y + 7, HudPteuWindow.TextColor);
            painter.Text(time, panel.Right - 12 - timeWidth, y + 7, TimeColor);
            HudPteuWindow.SeparatorBelow(painter, new Rectangle(panel.X + 6, y, panel.Width - 12, BossRow - 1));
        }

        HudPteuWindow.Scroller(painter, new Rectangle(panel.X, panel.Y + 28, panel.Width - 2, panel.Height - 32), _bossScroll, bosses.Count, VisibleBosses);
    }

    private static void DrawTag(HudPainter painter, HudTeleportTag tag, int x, int y)
    {
        var box = new Rectangle(x, y, 38, 14);
        painter.Fill(box, TagColors[(int)tag]);
        painter.TextCentered(ClientText.Get(TagKeys[(int)tag]), box.X, box.Right, box.Y - 2, Color.White);
    }

    // Picking a destination shows its map: the HD render when there is one, the original map otherwise.
    private void Choose(TeleportCoreDestination destination)
    {
        _chosen = destination;
        _levelLine = ClientText.Format("TeleportCore.MapLevel", _levels[destination.Index]);
        ReleasePreview();
        if (Fields[destination.Index] is { } field)
        {
            _preview = HudArt.FieldMap(field.Id);
            _previewHd = HudArt.FieldMapHd(field.Id);
            _sprites.Request(_preview);
            _sprites.Request(_previewHd);
        }
    }

    private void ReleasePreview()
    {
        if (_preview is not null)
        {
            _sprites.Release(_preview);
            _sprites.Release(_previewHd);
            _preview = null;
            _previewHd = null;
        }
    }

    private static FieldDef[] BuildFields()
    {
        var fields = new WorldCatalog().AllFields;
        var result = new FieldDef[TeleportCoreDestinations.All.Count];
        foreach (var destination in TeleportCoreDestinations.All)
        {
            result[destination.Index] = fields.FirstOrDefault(field => field.FieldCode == destination.FieldCode);
        }

        return result;
    }
}
