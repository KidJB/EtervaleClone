using System.Globalization;
using Microsoft.Xna.Framework.Input;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudActionKind : byte
{
    None,
    Skill,
    Item
}

// One shortcut: a skill for one mouse socket, or a carried item by its catalog key.
public readonly record struct HudActionSlot(HudActionKind Kind, uint SkillId, SkillMouseUse Side, string ItemKey)
{
    public static HudActionSlot ForSkill(uint skillId, SkillMouseUse side) => new(HudActionKind.Skill, skillId, side, null);
    public static HudActionSlot ForItem(string itemKey) => new(HudActionKind.Item, 0, SkillMouseUse.None, itemKey);
    public bool IsEmpty => Kind == HudActionKind.None;
}

// The action bar's shortcuts: three pages of two rows of nine keys. A skill or an item sits on at most one key of a
// page. Revision counts every change, for saving.
public sealed class HudActionBarSlots
{
    public const int Pages = 3;
    public const int Columns = 9;
    public const int Rows = 2;
    public const int SlotCount = Columns * Rows;

    // F9 is the diagnostics overlay (Game1), so the first row's last key is F10.
    public static readonly Keys[] HotKeys =
    [
        Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F10,
        Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.OemPlus
    ];

    public static readonly string[] Labels = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F10", "4", "5", "6", "7", "8", "9", "0", "-", "="];

    private readonly HudActionSlot[,] _slots = new HudActionSlot[Pages, SlotCount];
    private int _page;

    public int Page
    {
        get => _page;
        set
        {
            var page = Math.Clamp(value, 0, Pages - 1);
            if (page != _page)
            {
                _page = page;
                Revision++;
            }
        }
    }

    public int Revision { get; private set; }

    public HudActionSlot this[int index] => Valid(index) ? _slots[_page, index] : default;

    public HudActionSlot At(int page, int index) => page is >= 0 and < Pages && Valid(index) ? _slots[page, index] : default;

    public static bool Valid(int index) => index is >= 0 and < SlotCount;

    public bool AssignSkill(int index, uint skillId, SkillMouseUse side)
    {
        if (!Valid(index) || skillId == 0 || side is not (SkillMouseUse.Left or SkillMouseUse.Right))
        {
            return false;
        }

        ClearWhere(slot => slot.Kind == HudActionKind.Skill && slot.SkillId == skillId);
        Set(index, HudActionSlot.ForSkill(skillId, side));
        return true;
    }

    public bool AssignItem(int index, string itemKey)
    {
        if (!Valid(index) || string.IsNullOrEmpty(itemKey))
        {
            return false;
        }

        ClearWhere(slot => slot.Kind == HudActionKind.Item && slot.ItemKey == itemKey);
        Set(index, HudActionSlot.ForItem(itemKey));
        return true;
    }

    // Puts a shortcut back on a key, as it was lifted: a lifted shortcut is never on the page twice.
    public bool Place(int index, HudActionSlot slot) => slot.Kind switch
    {
        HudActionKind.Skill => AssignSkill(index, slot.SkillId, slot.Side),
        HudActionKind.Item => AssignItem(index, slot.ItemKey),
        _ => false
    };

    // Takes the shortcut off its key and returns it.
    public HudActionSlot Lift(int index)
    {
        var slot = this[index];
        if (!slot.IsEmpty)
        {
            Set(index, default);
        }

        return slot;
    }

    public void Clear(int index) => Lift(index);

    public static int IndexOf(Keys key) => Array.IndexOf(HotKeys, key);

    // Saved form: one string per slot, page after page ("" empty, "s:<code hex>:L|R", "i:<catalog key>").
    public string[] Save()
    {
        var saved = new string[Pages * SlotCount];
        for (var page = 0; page < Pages; page++)
        {
            for (var index = 0; index < SlotCount; index++)
            {
                var slot = _slots[page, index];
                saved[page * SlotCount + index] = slot.Kind switch
                {
                    HudActionKind.Skill => $"s:{slot.SkillId.ToString("X8", CultureInfo.InvariantCulture)}:{(slot.Side == SkillMouseUse.Left ? 'L' : 'R')}",
                    HudActionKind.Item => $"i:{slot.ItemKey}",
                    _ => ""
                };
            }
        }

        return saved;
    }

    public void Restore(IReadOnlyList<string> saved, int page)
    {
        Array.Clear(_slots);
        for (var position = 0; saved is not null && position < Math.Min(saved.Count, Pages * SlotCount); position++)
        {
            if (Parse(saved[position]) is { } slot)
            {
                _slots[position / SlotCount, position % SlotCount] = slot;
            }
        }

        _page = Math.Clamp(page, 0, Pages - 1);
        Revision++;
    }

    private static HudActionSlot? Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith("i:", StringComparison.Ordinal) && value.Length > 2)
        {
            return HudActionSlot.ForItem(value[2..]);
        }

        var parts = value.Split(':');
        if (parts.Length == 3 && parts[0] == "s" && uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && code != 0)
        {
            return parts[2] switch
            {
                "L" => HudActionSlot.ForSkill(code, SkillMouseUse.Left),
                "R" => HudActionSlot.ForSkill(code, SkillMouseUse.Right),
                _ => null
            };
        }

        return null;
    }

    private void Set(int index, HudActionSlot slot)
    {
        _slots[_page, index] = slot;
        Revision++;
    }

    private void ClearWhere(Func<HudActionSlot, bool> match)
    {
        for (var index = 0; index < SlotCount; index++)
        {
            if (match(_slots[_page, index]))
            {
                _slots[_page, index] = default;
            }
        }
    }
}
