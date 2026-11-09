using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D;

namespace PristonTale.Gameplay.UI.Widgets;

// NeighbourFieldId is another loaded field whose map is drawn under the active one where the two meet.
public readonly record struct HudWorldView(
    string FieldId,
    bool Underground,
    PristonIntRectangle MapRectangle,
    Vector3 PlayerPosition,
    Vector3 PlayerForward,
    float TimeOfDay,
    bool Running,
    bool MapHidden = false,
    string NeighbourFieldId = null,
    PristonIntRectangle NeighbourMapRectangle = default);

public enum HudMapMarkerKind : byte
{
    Npc,
    Player,
    Party,
    // A boss's spot: grey while the boss waits for its pass, red while it is alive.
    BossWaiting,
    BossAlive,
    // A monster spawn point of the field (its id is its index in the spawn list); it has no name.
    Spawn
}

// An entity marked on the minimap; its name is looked up only when the pointer is over it. Icon: the art drawn in place of
// the kind's own mark (an NPC's service or quest icon, a clan mate's), or null.
public readonly record struct HudMapMarker(ulong Id, Vector3 Position, HudMapMarkerKind Kind, string Icon = null);

// Original field minimap: 128x128 box at (656,426), right and bottom anchored, black 50% backdrop, the
// field\map\<id>.tga region around the player (the neighbouring field's map first), NPC dots, the heading arrow,
// MapBox.tga and the field title. North is up; the map does not rotate. The player can drag it elsewhere (Offset), and
// the NPC, player or party marker under the pointer shows its name. N switches to the large map: a 384-pixel box over the
// same corner that shows half again as far, in border_large.png.
public sealed class HudMinimap
{
    public const int SmallSize = 128;
    public const int LargeSize = 384;
    private const float LargeViewScale = 1.5f;
    private const string LargeBorder = Folder + "border_large.png";
    private const string Folder = "Hud/Minimap/";
    public const string SpawnIcon = Folder + "spawn.png";
    public const string PlayerIcon = Folder + "player.png";
    public const string FriendlyIcon = Folder + "friendly.png";
    public const string NpcIcon = Folder + "npc.png";
    public const string WarpIcon = Folder + "Warp.png";
    public const string WarehouseIcon = Folder + "Warehouse.png";
    public const string CraftIcon = Folder + "Aging-Mixing.png";
    public const string SkillMasterIcon = Folder + "Skill-Master.png";
    public const string SupplyIcon = Folder + "Supply.png";
    public const string SmithIcon = Folder + "Anvil.png";
    public const string MagicIcon = Folder + "Magicsmith.png";
    public const string QuestStartIcon = Folder + "Quest-Start.png";
    public const string QuestRepeatableIcon = Folder + "Quest-Start-Repeatable.png";
    public const string QuestProgressIcon = Folder + "Quest-Progress.png";
    public const string QuestFinishIcon = Folder + "Quest-Finish.png";
    public static readonly string[] Art =
    [
        BossAliveIcon, BossWaitingIcon, LargeBorder, SpawnIcon, PlayerIcon, FriendlyIcon, NpcIcon, WarpIcon, WarehouseIcon, CraftIcon, SkillMasterIcon,
        SupplyIcon, SmithIcon, MagicIcon, QuestStartIcon, QuestRepeatableIcon, QuestProgressIcon, QuestFinishIcon
    ];

    // The view radius starts at 8 cells and eases toward the field's size by 8 units per 70 Hz frame.
    private const float InitialViewSize = 8 * 64f;
    private const float ZoomSpeed = 8f * GameUnits.TicksPerSecond;
    // The original adds a specular 96 to the frame (a quarter of it to the title) under the cursor; adding the sprite's own
    // colour once (a quarter for the title) gives the same brightening on the grey frame art.
    private const int HoverHighlight = 255;

    private string _field;
    private string _neighbour;
    private string _fieldMap;
    private string _fieldTitle;
    private string _neighbourMap;
    // A sharper map of the same area (HudArt.FieldMapHd), drawn instead when the file exists.
    private string _fieldMapHd;
    private string _neighbourMapHd;
    private float _viewSize = InitialViewSize;
    private const int TitleHeight = 16;
    // A press on the map moves it once the pointer has gone this far, so a click stays a click.
    private const int DragThreshold = 3;
    private const int MarkerReach = 4;
    private static readonly Color PlayerColor = new(150, 200, 255);
    private static readonly Color NpcNameColor = new(255, 255, 180);
    private static readonly Color PartyNameColor = new(175, 230, 175);
    private static readonly Color BossColor = new(255, 180, 180);
    private static readonly Color BossWaitingColor = new(200, 200, 200);
    public const string BossAliveIcon = "Hud/Minimap/boss-active.png";
    public const string BossWaitingIcon = "Hud/Minimap/boss-inactive.png";
    private const int BossIconSize = 16;
    private Point _offset;
    private Point _pressedAt;
    private Point _pressedOffset;
    private bool _pressed;
    private bool _dragging;
    // A press on the title strip moves the box; a press on the map pans it.
    private bool _panning;
    private Vector2 _pan;
    private Vector2 _pressedPan;
    // How far a pan may take the view from the player, in world units.
    private const float MaxPan = 16000f;

    // Where the original places the box; Bounds is where it lies after the player moved it.
    public static Rectangle Box(HudLayout layout) => layout.BottomRight(new Rectangle(656, 426, SmallSize, SmallSize));

    // The large map grows up and left from the small box's bottom right corner. Switching size recentres the view.
    public bool Large
    {
        get => _large;
        set
        {
            _large = value;
            _pan = Vector2.Zero;
        }
    }

    private bool _large;

    // The world offset of the view's centre from the player (x east, y the engine's z), set by dragging the map.
    public Vector2 Pan => _pan;

    private int Size => Large ? LargeSize : SmallSize;

    private float ViewRadius => Large ? _viewSize * LargeViewScale : _viewSize;

    // How far the player moved the map from its original place (logical pixels); Revision counts moves, for saving.
    public Point Offset
    {
        get => _offset;
        set => _offset = value;
    }

    public int Revision { get; private set; }
    public bool Dragging => _dragging;

    // The box where it lies, kept on screen with its title strip.
    public Rectangle Bounds(HudLayout layout)
    {
        var box = Box(layout);
        var grow = Size - SmallSize;
        var x = Math.Clamp(box.X - grow + _offset.X, 0, Math.Max(0, layout.Width - Size));
        var y = Math.Clamp(box.Y - grow + _offset.Y, TitleHeight, Math.Max(TitleHeight, layout.Height - Size));
        return new Rectangle(x, y, Size, Size);
    }

    // The box and its title strip, which both take the pointer.
    public Rectangle Area(HudLayout layout)
    {
        var box = Bounds(layout);
        return new Rectangle(box.X, box.Y - TitleHeight, box.Width, box.Height + TitleHeight);
    }

    // A left press and a move past DragThreshold: on the title strip it carries the box with the pointer, on the map it
    // pans the view the other way, as if the map were dragged under the pointer. A press outside the box recentres it.
    public void Drag(HudLayout layout, Point pointer, MouseState mouse, MouseState previous, bool allowed)
    {
        if (mouse.LeftButton == ButtonState.Released)
        {
            if (_dragging && !_panning)
            {
                Revision++;
            }

            _pressed = false;
            _dragging = false;
            return;
        }

        if (previous.LeftButton == ButtonState.Released)
        {
            var area = Area(layout);
            _pressed = allowed && area.Contains(pointer);
            _panning = Bounds(layout).Contains(pointer);
            _pressedAt = pointer;
            _pressedOffset = _offset;
            _pressedPan = _pan;
            if (!area.Contains(pointer))
            {
                _pan = Vector2.Zero;
            }

            return;
        }

        if (!_pressed)
        {
            return;
        }

        var moved = new Point(pointer.X - _pressedAt.X, pointer.Y - _pressedAt.Y);
        _dragging |= Math.Abs(moved.X) > DragThreshold || Math.Abs(moved.Y) > DragThreshold;
        if (_dragging && _panning)
        {
            var unitsPerPixel = ViewRadius / (Size / 2);
            _pan = Vector2.Clamp(_pressedPan - new Vector2(moved.X, moved.Y) * unitsPerPixel, new Vector2(-MaxPan), new Vector2(MaxPan));
        }
        else if (_dragging)
        {
            _offset = new Point(_pressedOffset.X + moved.X, _pressedOffset.Y + moved.Y);
            var box = Box(layout);
            var placed = Bounds(layout);
            var grow = Size - SmallSize;
            _offset = new Point(placed.X + grow - box.X, placed.Y + grow - box.Y);
        }
    }

    // 16 (dungeon) or 24 (other fields) cells of 64 world units from the centre to the box edge.
    public static float ViewSize(bool underground) => (underground ? 16 : 24) * 64f;

    public float CurrentViewSize => _viewSize;

    // The original shows no minimap in the Bellatra arena, the quest arena, the boss field and the action fields.
    public static bool HiddenOn(int fieldCode, FieldState state) =>
        fieldCode is FieldCodes.Bellatra or FieldCodes.QuestArena or FieldCodes.Boss
        || state is FieldState.QuestArena or FieldState.Room or FieldState.Action;

    // Original stage coordinates are fixed point with Z mirrored relative to the engine (stage vertex Z = -z).
    public static Vector2 MapPoint(PristonIntRectangle rectangle, Vector3 position) =>
        new(position.X - rectangle.Left / GameUnits.FixedScale, rectangle.Bottom / GameUnits.FixedScale + position.Z);

    // Returns the texture source (normalized) and destination (relative to the 126x126 interior) after the original clipping.
    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, bool underground, out Vector4 source, out Vector4 destination) =>
        MapRegion(rectangle, position, ViewSize(underground), out source, out destination);

    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, float size, out Vector4 source, out Vector4 destination) =>
        MapRegion(rectangle, position, size, SmallSize, out source, out destination);

    // boxSize: the box in pixels (the interior is 2 pixels narrower and its half spans the view radius).
    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, float size, int boxSize, out Vector4 source, out Vector4 destination)
    {
        source = default;
        destination = default;
        var width = (rectangle.Right - rectangle.Left) / GameUnits.FixedScale;
        var height = (rectangle.Bottom - rectangle.Top) / GameUnits.FixedScale;
        if (width <= 0 || height <= 0 || size <= 0)
        {
            return false;
        }

        var point = MapPoint(rectangle, position);
        var fpx = size / width;
        var fpy = size / height;
        var fx = point.X / width - fpx;
        var fw = point.X / width + fpx;
        var fy = point.Y / height - fpy;
        var fh = point.Y / height + fpy;
        if (fx < 0 && fw < 0 || fx > 1 && fw > 1 || fy < 0 && fh < 0 || fy > 1 && fh > 1)
        {
            return false;
        }

        float half = boxSize / 2;
        float dx = 0;
        float dy = 0;
        float dw = boxSize - 2;
        float dh = boxSize - 2;
        if (fx < 0)
        {
            var clip = -fx * (half / fpx);
            dx += clip;
            dw -= clip;
            fx = 0;
        }

        if (fw > 1)
        {
            dw -= (fw - 1) * (half / fpx);
            fw = 1;
        }

        if (fy < 0)
        {
            var clip = -fy * (half / fpy);
            dy += clip;
            dh -= clip;
            fy = 0;
        }

        if (fh > 1)
        {
            dh -= (fh - 1) * (half / fpy);
            fh = 1;
        }

        source = new Vector4(fx, fy, fw - fx, fh - fy);
        destination = new Vector4(dx, dy, dw, dh);
        return dw > 0 && dh > 0;
    }

    // The minimap arrow bitmap points down and is turned half a circle before applying the heading.
    public static float ArrowRotation(Vector3 forward) =>
        forward.X == 0 && forward.Z == 0 ? MathF.PI : WorldSpace.TransportFacing(forward) + MathF.PI;

    // A world offset from the player to the box centre: x east, y south (engine z), 64 pixels per view radius.
    public static Vector2 MarkerOffset(Vector3 player, Vector3 marker, float size) => MarkerOffset(player, marker, size, SmallSize);

    public static Vector2 MarkerOffset(Vector3 player, Vector3 marker, float size, int boxSize) =>
        new((marker.X - player.X) * (boxSize / 2) / size, (marker.Z - player.Z) * (boxSize / 2) / size);

    public static float EaseViewSize(float current, float target, double elapsedSeconds)
    {
        var step = (float)(ZoomSpeed * Math.Max(0, elapsedSeconds));
        return current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);
    }

    public void Update(HudSprites sprites, HudWorldView world, double elapsedSeconds)
    {
        var neighbour = string.Equals(world.NeighbourFieldId, world.FieldId, StringComparison.OrdinalIgnoreCase) ? null : world.NeighbourFieldId;
        if (!string.Equals(world.FieldId, _field, StringComparison.OrdinalIgnoreCase) || !string.Equals(neighbour, _neighbour, StringComparison.OrdinalIgnoreCase))
        {
            // Field art is not reference counted: when the two fields swap, keep what stays in use.
            var fieldMap = world.FieldId is null ? null : HudArt.FieldMap(world.FieldId);
            var fieldTitle = world.FieldId is null ? null : HudArt.FieldTitle(world.FieldId);
            var neighbourMap = neighbour is null ? null : HudArt.FieldMap(neighbour);
            var fieldMapHd = world.FieldId is null ? null : HudArt.FieldMapHd(world.FieldId);
            var neighbourMapHd = neighbour is null ? null : HudArt.FieldMapHd(neighbour);
            ReleaseUnless(sprites, _fieldMap, fieldMap, neighbourMap);
            ReleaseUnless(sprites, _fieldTitle, fieldTitle, null);
            ReleaseUnless(sprites, _neighbourMap, fieldMap, neighbourMap);
            ReleaseUnless(sprites, _fieldMapHd, fieldMapHd, neighbourMapHd);
            ReleaseUnless(sprites, _neighbourMapHd, fieldMapHd, neighbourMapHd);
            _field = world.FieldId;
            _neighbour = neighbour;
            _fieldMap = fieldMap;
            _fieldTitle = fieldTitle;
            _neighbourMap = neighbourMap;
            _fieldMapHd = fieldMapHd;
            _neighbourMapHd = neighbourMapHd;
            _pan = Vector2.Zero;
            Request(sprites, _fieldMap);
            Request(sprites, _fieldTitle);
            Request(sprites, _neighbourMap);
            Request(sprites, _fieldMapHd);
            Request(sprites, _neighbourMapHd);
        }

        _viewSize = EaseViewSize(_viewSize, ViewSize(world.Underground), elapsedSeconds);
    }

    // The next boss line on a dark strip over the field title.
    public void DrawBossTime(HudPainter painter, HudLayout layout, string line)
    {
        var area = Area(layout);
        var strip = new Rectangle(area.X - 4, Math.Max(0, area.Y - TitleHeight - 2), area.Width + 8, TitleHeight);
        painter.ColorBox(strip, 0, 0, 0, 160);
        painter.TextCentered(line, strip.X, strip.Right, strip.Y - 1, BossColor);
    }

    // The marker the pointer is over, nearest first; -1 for none.
    public int MarkerAt(HudLayout layout, HudWorldView world, IReadOnlyList<HudMapMarker> markers, Point pointer)
    {
        var box = Bounds(layout);
        if (!box.Contains(pointer) || _dragging)
        {
            return -1;
        }

        var center = new Vector2(box.X + Size / 2, box.Y + Size / 2);
        var radius = ViewRadius;
        var view = ViewCenter(world.PlayerPosition);
        var found = -1;
        var best = float.MaxValue;
        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index].Position;
            if (markers[index].Kind == HudMapMarkerKind.Spawn
                || MathF.Abs(marker.X - view.X) >= radius || MathF.Abs(marker.Z - view.Z) >= radius)
            {
                continue;
            }

            var offset = MarkerOffset(view, marker, radius, Size);
            var distance = Vector2.DistanceSquared(center + offset, new Vector2(pointer.X, pointer.Y));
            if (distance <= MarkerReach * MarkerReach && distance < best)
            {
                best = distance;
                found = index;
            }
        }

        return found;
    }

    // The hovered marker's name beside the pointer, in its kind's colour.
    public static void DrawName(HudPainter painter, HudLayout layout, string name, HudMapMarkerKind kind, Point pointer)
    {
        var width = painter.Measure(name) + 8;
        var box = layout.PlaceOverlay(new Point(pointer.X - width - 6, pointer.Y - HudPainter.LineHeight - 4), width, HudPainter.LineHeight + 4);
        painter.ColorBox(box, 0, 0, 0, 200);
        var color = kind switch
        {
            HudMapMarkerKind.Npc => NpcNameColor,
            HudMapMarkerKind.Party => PartyNameColor,
            HudMapMarkerKind.BossAlive => BossColor,
            HudMapMarkerKind.BossWaiting => BossWaitingColor,
            _ => PlayerColor
        };
        painter.Text(name, box.X + 4, box.Y, color);
    }

    // NPCs as the original's dots, other players as small blue squares, party members as the party mark (red past
    // PartyLimits.NearRange, as the original marks them).
    public void Draw(HudPainter painter, HudLayout layout, HudWorldView world, IReadOnlyList<HudMapMarker> markers, bool hovered)
    {
        var box = Bounds(layout);
        var view = ViewCenter(world.PlayerPosition);
        painter.ColorBox(box, 0, 0, 0, 128);
        if (_neighbour is not null)
        {
            DrawMap(painter, box, _neighbourMapHd, _neighbourMap, world.NeighbourMapRectangle, view);
        }

        if (_field is not null)
        {
            DrawMap(painter, box, _fieldMapHd, _fieldMap, world.MapRectangle, view);
        }

        var center = new Vector2(box.X + Size / 2, box.Y + Size / 2);
        var radius = ViewRadius;
        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];
            if (MathF.Abs(marker.Position.X - view.X) >= radius || MathF.Abs(marker.Position.Z - view.Z) >= radius)
            {
                continue;
            }

            var dx = marker.Position.X - world.PlayerPosition.X;
            var dz = marker.Position.Z - world.PlayerPosition.Z;
            var offset = MarkerOffset(view, marker.Position, radius, Size);
            var x = (int)(center.X + offset.X);
            var y = (int)(center.Y + offset.Y);
            if (marker.Icon is { } art && painter.Texture(art) is { } texture)
            {
                painter.Sprite(art, x - texture.Width / 2, y - texture.Height / 2);
                continue;
            }

            switch (marker.Kind)
            {
                case HudMapMarkerKind.Spawn:
                    painter.Sprite(SpawnIcon, x - 2, y - 2);
                    break;
                case HudMapMarkerKind.Npc:
                    painter.Sprite(HudArt.MapNpc, x - 3, y - 3);
                    break;
                case HudMapMarkerKind.Player:
                    painter.Fill(new Rectangle(x - 1, y - 1, 3, 3), PlayerColor);
                    break;
                case HudMapMarkerKind.BossAlive:
                case HudMapMarkerKind.BossWaiting:
                    var icon = marker.Kind == HudMapMarkerKind.BossAlive ? BossAliveIcon : BossWaitingIcon;
                    painter.Stretch(icon, new Rectangle(x - BossIconSize / 2, y - BossIconSize / 2, BossIconSize, BossIconSize));
                    break;
                default:
                    var near = dx * dx + dz * dz < PartyLimits.NearRange * PartyLimits.NearRange;
                    painter.Stretch(Parties.HudPartyArt.MapMark, new Rectangle(x - 3, y - 3, 8, 8), null, near ? Color.White : Color.Red);
                    break;
            }
        }

        // The player's arrow stays where the player is on the panned map, and only while that is inside the box.
        var arrow = center + MarkerOffset(view, world.PlayerPosition, radius, Size);
        if (MathF.Abs(arrow.X - center.X) < Size / 2 && MathF.Abs(arrow.Y - center.Y) < Size / 2)
        {
            painter.Rotated(HudArt.MapArrow, arrow, ArrowRotation(world.PlayerForward));
        }
        if (Large)
        {
            painter.Sprite(LargeBorder, box.X - 9, box.Y - 24);
        }
        else
        {
            painter.Sprite(HudArt.MapBox, box.X, box.Y);
            if (hovered)
            {
                painter.Highlight(HudArt.MapBox, box, HoverHighlight);
            }
        }

        if (_field is not null)
        {
            var title = new Rectangle(box.X, box.Y - 16, SmallSize, 16);
            painter.Stretch(_fieldTitle, title);
            if (hovered)
            {
                painter.Highlight(_fieldTitle, title, HoverHighlight >> 2);
            }
        }
    }

    private Vector3 ViewCenter(Vector3 player) => new(player.X + _pan.X, player.Y, player.Z + _pan.Y);

    private void DrawMap(HudPainter painter, Rectangle box, string hdPath, string path, PristonIntRectangle rectangle, Vector3 player)
    {
        if (hdPath is not null && painter.Texture(hdPath) is not null)
        {
            path = hdPath;
        }

        var map = painter.Texture(path);
        if (map is null || !MapRegion(rectangle, player, ViewRadius, Size, out var source, out var destination))
        {
            return;
        }

        var from = new Rectangle((int)(source.X * map.Width), (int)(source.Y * map.Height),
            Math.Max(1, (int)(source.Z * map.Width)), Math.Max(1, (int)(source.W * map.Height)));
        var to = new Rectangle(box.X + 1 + (int)destination.X, box.Y + 1 + (int)destination.Y, (int)destination.Z, (int)destination.W);
        painter.Stretch(path, to, from);
    }

    private static void Request(HudSprites sprites, string path)
    {
        if (path is not null)
        {
            sprites.Request(path);
        }
    }

    private static void ReleaseUnless(HudSprites sprites, string path, string keep, string keepToo)
    {
        if (path is not null && !string.Equals(path, keep, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, keepToo, StringComparison.OrdinalIgnoreCase))
        {
            sprites.Release(path);
        }
    }
}
