using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudActionBarButton
{
    None,
    Page1,
    Page2,
    Page3,
    Lock,
    Turn,
    Close
}

// The action bar: two rows of nine short keys over the scene, three pages of them (HudActionBarSlots). Skills come from
// the skill window (released over a key with the button that pressed them), items from the bag (a carried potion or
// usable item set down on a key; the item stays in the bag). Unlocked, a key's shortcut is lifted with a click and set on
// another key or dropped off the bar, and the header moves the bar; locked, both are refused. A right click on a key
// acts as its key. It lies horizontal or, turned, vertical, and keeps its distance from the bottom edge.
public sealed class HudActionBar
{
    public const int Slot = 41;
    public const int Step = Slot + 1;
    private const int PaddingX = 6;
    private const int PaddingY = 8;
    private const int LongSide = 424;
    private const int ShortSide = 96;
    // In the upright bar the slots start under the buttons.
    private const int UprightSlotsY = 38;
    private const int IconInset = 4;
    private const string Folder = "Hud/ActionBar/";
    private const string BackgroundWide = Folder + "background_horizontal.png";
    private const string BackgroundTall = Folder + "background_vertical.png";
    private const string SlotArt = Folder + "slot.png";
    private const string SlotLit = Folder + "slot_.png";
    private static readonly string[] ButtonNames = ["", "tab1", "tab2", "tab3", "btnlock", "btnswap", "btnclose"];
    private static readonly string[] ButtonArt = ButtonNames.Select(static name => $"{Folder}{name}.png").ToArray();
    private static readonly string[] ButtonArtLit = ButtonNames.Select(static name => $"{Folder}{name}_.png").ToArray();
    public static readonly string[] Art = [BackgroundWide, BackgroundTall, SlotArt, SlotLit, .. ButtonArt.Skip(1), .. ButtonArtLit.Skip(1)];
    private const int ItemCell = 22;
    private const int MaxCount = 999;
    // Until moved, the bar sits centred with its bottom this far above the screen's bottom, over the bottom bar.
    private const int DefaultLift = 110;
    private static readonly Color Frame = new(150, 128, 88);
    private static readonly Color Lit = new(255, 220, 140);
    private static readonly string[] Counts = Enumerable.Range(0, MaxCount + 1).Select(count => count.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private readonly HudSkillBindings _bindings;
    private Point _anchor = new(-1, -1);
    private Point _grab;
    private bool _moving;
    private int _liftedFrom = -1;

    public HudActionBar(HudSkillBindings bindings) => _bindings = bindings;

    public HudActionBarSlots Slots => _bindings.Slots;
    // Hidden until B shows it; its keys work either way.
    public bool Open { get; set; }
    public bool Locked { get; set; }
    public bool Vertical { get; private set; }

    // A shortcut lifted off a key and following the pointer.
    public HudActionSlot Carried { get; private set; }

    // Window state changes (shown, locked, turned, moved), for saving with the slots.
    public int Revision { get; private set; }

    // A refusal to show as a notice; Refusals counts them.
    public string Refusal { get; private set; } = "";
    public int Refusals { get; private set; }

    // Where the bar's top-left lies: X from the left edge, Y up from the bottom edge (both logical pixels).
    public Point Anchor => _anchor;

    public void Restore(bool open, bool locked, bool vertical, Point anchor)
    {
        Open = open;
        Locked = locked;
        Vertical = vertical;
        _anchor = anchor;
    }

    public Point Size => Vertical ? new Point(ShortSide, LongSide) : new Point(LongSide, ShortSide);

    public Rectangle Bounds(HudLayout layout)
    {
        var size = Size;
        var anchor = _anchor.X < 0 ? new Point((layout.Width - size.X) / 2, DefaultLift + size.Y) : _anchor;
        var x = Math.Clamp(anchor.X, 0, Math.Max(0, layout.Width - size.X));
        var y = Math.Clamp(layout.Height - anchor.Y, 0, Math.Max(0, layout.Height - size.Y));
        return new Rectangle(x, y, size.X, size.Y);
    }

    public Rectangle SlotBounds(Rectangle bar, int index)
    {
        var column = index % HudActionBarSlots.Columns;
        var row = index / HudActionBarSlots.Columns;
        var (across, down) = Vertical ? (row, column) : (column, row);
        return new Rectangle(bar.X + PaddingX + across * Step, bar.Y + (Vertical ? UprightSlotsY + PaddingX : PaddingY) + down * Step, Slot, Slot);
    }

    public Rectangle ButtonBounds(Rectangle bar, HudActionBarButton button)
    {
        var (x, y, width, height) = button switch
        {
            HudActionBarButton.Lock => Vertical ? (53, 4, 12, 15) : (387, 10, 12, 15),
            HudActionBarButton.Turn => Vertical ? (72, 25, 12, 15) : (406, 31, 12, 15),
            HudActionBarButton.Close => Vertical ? (70, 5, 14, 14) : (404, 11, 14, 14),
            _ => Vertical ? (PaddingX, 4 + 14 * ((int)button - 1), 34, 12) : (384, 50 + 14 * ((int)button - 1), 34, 12)
        };
        return new Rectangle(bar.X + x, bar.Y + y, width, height);
    }

    public int SlotAt(HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return -1;
        }

        var bar = Bounds(layout);
        if (!bar.Contains(pointer))
        {
            return -1;
        }

        for (var index = 0; index < HudActionBarSlots.SlotCount; index++)
        {
            if (SlotBounds(bar, index).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    public HudActionBarButton ButtonAt(HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return HudActionBarButton.None;
        }

        var bar = Bounds(layout);
        for (var button = HudActionBarButton.Page1; button <= HudActionBarButton.Close; button++)
        {
            if (ButtonBounds(bar, button).Contains(pointer))
            {
                return button;
            }
        }

        return HudActionBarButton.None;
    }

    public bool Captures(HudLayout layout, Point pointer) => Open && Bounds(layout).Contains(pointer) || _moving || !Carried.IsEmpty;

    public void Toggle()
    {
        Open = !Open;
        Revision++;
        if (!Open)
        {
            DropCarried();
        }
    }

    // One frame of the pointer. carrying: the bag has an item on the cursor, which the bar takes through TakeItem.
    public void Update(HudLayout layout, Point pointer, MouseState mouse, MouseState previous, bool carrying, bool blocked)
    {
        var leftDown = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var leftUp = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;
        var rightDown = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        var rightUp = mouse.RightButton == ButtonState.Released && previous.RightButton == ButtonState.Pressed;
        if (_moving)
        {
            var bar = Bounds(layout);
            _anchor = new Point(Math.Clamp(pointer.X - _grab.X, 0, Math.Max(0, layout.Width - bar.Width)),
                layout.Height - Math.Clamp(pointer.Y - _grab.Y, 0, Math.Max(0, layout.Height - bar.Height)));
            if (mouse.LeftButton == ButtonState.Released)
            {
                _moving = false;
                Revision++;
            }

            return;
        }

        if (!Open || blocked)
        {
            DropCarried();
            return;
        }

        var index = SlotAt(layout, pointer);
        if (!Carried.IsEmpty)
        {
            // Set on a key, the shortcut there moves to the lifted one's key; dropped off the bar, it is gone.
            if (leftUp || leftDown)
            {
                if (index >= 0)
                {
                    var displaced = Slots[index];
                    Slots.Place(index, Carried);
                    if (index != _liftedFrom && !displaced.IsEmpty)
                    {
                        Slots.Place(_liftedFrom, displaced);
                    }
                }

                Carried = default;
                _liftedFrom = -1;
            }

            return;
        }

        if (leftUp && _bindings.PressedSide == SkillMouseUse.Left || rightUp && _bindings.PressedSide == SkillMouseUse.Right)
        {
            TakeSkill(index, _bindings.PressedSkill, _bindings.PressedSide);
        }

        if (leftDown && !carrying)
        {
            var button = ButtonAt(layout, pointer);
            if (button != HudActionBarButton.None)
            {
                Press(button);
            }
            else if (index >= 0 && !Slots[index].IsEmpty)
            {
                if (Locked)
                {
                    Refuse("The action bar is locked.");
                }
                else
                {
                    Carried = Slots.Lift(index);
                    _liftedFrom = index;
                }
            }
            else if (index < 0 && Bounds(layout).Contains(pointer) && !Locked)
            {
                var bar = Bounds(layout);
                _grab = new Point(pointer.X - bar.X, pointer.Y - bar.Y);
                _moving = true;
            }
        }

        if (rightDown && index >= 0)
        {
            _bindings.Activate(index);
        }
    }

    // A carried bag item set down on a key: true when the pointer is over a key (the item goes back to the bag).
    public bool TakeItem(HudLayout layout, Point pointer, OwnedItemView item)
    {
        var index = SlotAt(layout, pointer);
        if (index < 0 || item is null)
        {
            return false;
        }

        if (Locked)
        {
            Refuse("The action bar is locked.");
        }
        else if (!Shortcuts(item))
        {
            Refuse("Only potions and usable items go on the action bar.");
        }
        else
        {
            Slots.AssignItem(index, item.CatalogKey);
        }

        return true;
    }

    // Potions and the items a right click uses.
    public static bool Shortcuts(OwnedItemView item) =>
        !item.Equipped && (item.Kind == ItemKind.Potion || UsableItemCatalog.IsUsable(item.CatalogKey));

    public void Draw(HudPainter painter, HudLayout layout, OwnerState state, HudItemIcons icons, SkillCooldownBook cooldowns, double nowMs,
        HudSkillHud skillHud, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var bar = Bounds(layout);
        painter.Sprite(Vertical ? BackgroundTall : BackgroundWide, bar.X, bar.Y);
        var hoveredButton = ButtonAt(layout, pointer);
        for (var button = HudActionBarButton.Page1; button <= HudActionBarButton.Close; button++)
        {
            var bounds = ButtonBounds(bar, button);
            var on = button switch
            {
                HudActionBarButton.Lock => Locked,
                HudActionBarButton.Turn => Vertical,
                HudActionBarButton.Close => false,
                _ => Slots.Page == (int)button - 1
            };
            painter.Sprite(on || button == hoveredButton ? ButtonArtLit[(int)button] : ButtonArt[(int)button], bounds.X, bounds.Y);
        }

        for (var index = 0; index < HudActionBarSlots.SlotCount; index++)
        {
            var bounds = SlotBounds(bar, index);
            painter.Sprite(bounds.Contains(pointer) ? SlotLit : SlotArt, bounds.X, bounds.Y);
            var inner = new Rectangle(bounds.X + IconInset, bounds.Y + IconInset, bounds.Width - IconInset * 2, bounds.Height - IconInset * 2);
            DrawShortcut(painter, inner, Slots[index], state, icons, cooldowns, nowMs, skillHud);
            painter.Text(HudActionBarSlots.Labels[index], bounds.X + 4, bounds.Y + 1, Color.White);
        }
    }

    // The lifted shortcut under the pointer and the name of the hovered key's shortcut; drawn over the other windows.
    public void DrawOverlay(HudPainter painter, HudLayout layout, OwnerState state, HudItemIcons icons, HudSkillHud skillHud, Point pointer)
    {
        if (!Carried.IsEmpty)
        {
            var bounds = new Rectangle(pointer.X - HudSkillHud.SkillIcon / 2, pointer.Y - HudSkillHud.SkillIcon / 2, HudSkillHud.SkillIcon, HudSkillHud.SkillIcon);
            DrawShortcut(painter, bounds, Carried, state, icons, null, 0, skillHud);
            return;
        }

        var index = SlotAt(layout, pointer);
        if (index < 0 || _moving)
        {
            return;
        }

        var name = Name(Slots[index], state);
        if (name.Length == 0)
        {
            return;
        }

        var width = painter.Measure(name) + 8;
        var box = layout.PlaceOverlay(new Point(pointer.X + 12, pointer.Y - HudPainter.LineHeight - 6), width, HudPainter.LineHeight + 4);
        painter.ColorBox(box, 0, 0, 0, 200);
        Outline(painter, box, Frame);
        painter.Text(name, box.X + 4, box.Y, Color.White);
    }

    // How many of the item the owner carries in the bag and the belt.
    public static int CountOf(OwnerState state, string itemKey)
    {
        var count = 0;
        if (state is null)
        {
            return count;
        }

        foreach (var item in state.Items)
        {
            if ((item.InBag || item.BeltBox >= 0) && item.CatalogKey == itemKey)
            {
                count += Math.Max(1, item.Count);
            }
        }

        return count;
    }

    private string Name(HudActionSlot slot, OwnerState state)
    {
        if (slot.Kind == HudActionKind.Skill)
        {
            var side = slot.Side == SkillMouseUse.Left ? "left" : "right";
            return _bindings.Choice(slot.SkillId) is { } skill ? $"{skill.Name} ({side})" : "Skill not learned";
        }

        if (slot.Kind == HudActionKind.Item)
        {
            foreach (var item in state?.Items ?? [])
            {
                if (item.CatalogKey == slot.ItemKey)
                {
                    return item.Name;
                }
            }

            return "None carried";
        }

        return "";
    }

    private void DrawShortcut(HudPainter painter, Rectangle bounds, HudActionSlot slot, OwnerState state, HudItemIcons icons, SkillCooldownBook cooldowns,
        double nowMs, HudSkillHud skillHud)
    {
        if (slot.Kind == HudActionKind.Skill)
        {
            var learned = _bindings.Choice(slot.SkillId) is not null;
            if (!skillHud.DrawSkillIcon(painter, bounds, slot.SkillId, learned ? cooldowns : null, nowMs))
            {
                painter.TextCentered(slot.SkillId == HudSkillBindings.BasicAttackId ? "Atk" : "?", bounds.X, bounds.Right, bounds.Y + 12, Color.White);
            }

            if (!learned)
            {
                painter.ColorBox(bounds, 0, 0, 0, 150);
            }

            painter.Text(slot.Side == SkillMouseUse.Left ? "L" : "R", bounds.X + 2, bounds.Bottom - HudPainter.LineHeight, Lit);
            return;
        }

        if (slot.Kind != HudActionKind.Item)
        {
            return;
        }

        OwnedItemView shown = null;
        foreach (var item in state?.Items ?? [])
        {
            if (item.CatalogKey == slot.ItemKey)
            {
                shown = item;
                break;
            }
        }

        var width = (shown?.Width ?? 1) * ItemCell;
        var height = (shown?.Height ?? 1) * ItemCell;
        var fit = Math.Min(1f, Math.Min((float)bounds.Width / width, (float)bounds.Height / height));
        width = (int)(width * fit);
        height = (int)(height * fit);
        var count = CountOf(state, slot.ItemKey);
        var icon = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        icons.Draw(painter, slot.ItemKey, icon, count > 0 ? Color.White : new Color(90, 90, 90));
        var label = Counts[Math.Min(count, MaxCount)];
        painter.Text(label, bounds.Right - painter.Measure(label) - 1, bounds.Bottom - HudPainter.LineHeight, Color.White);
    }

    private void Press(HudActionBarButton button)
    {
        switch (button)
        {
            case HudActionBarButton.Page1:
            case HudActionBarButton.Page2:
            case HudActionBarButton.Page3:
                Slots.Page = (int)button - 1;
                break;
            case HudActionBarButton.Lock:
                Locked = !Locked;
                Revision++;
                break;
            case HudActionBarButton.Turn:
                Vertical = !Vertical;
                Revision++;
                break;
            case HudActionBarButton.Close:
                Toggle();
                break;
        }
    }

    private void TakeSkill(int index, uint skillId, SkillMouseUse side)
    {
        if (index < 0 || skillId == 0)
        {
            return;
        }

        if (Locked)
        {
            Refuse("The action bar is locked.");
        }
        else
        {
            _bindings.Assign(index, skillId, side);
        }
    }

    private void DropCarried()
    {
        if (!Carried.IsEmpty && _liftedFrom >= 0)
        {
            Slots.Place(_liftedFrom, Carried);
        }

        Carried = default;
        _liftedFrom = -1;
        _moving = false;
    }

    private void Refuse(string message)
    {
        Refusal = message;
        Refusals++;
    }

    private static void Outline(HudPainter painter, Rectangle bounds, Color color)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
    }
}
