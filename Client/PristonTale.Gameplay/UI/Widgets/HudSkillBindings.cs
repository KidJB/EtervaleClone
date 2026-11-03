using PristonTale.Simulation.Skills;
using Microsoft.Xna.Framework.Input;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed record HudSkillChoice(uint Id, string Name, int Rank, SkillMouseUse MouseUse);
public readonly record struct HudSkillShortcut(uint SkillId, SkillMouseUse Side);

public sealed class HudSkillBindings
{
    public const uint BasicAttackId = 0x11111111;
    public static readonly HudSkillChoice BasicAttack = new(BasicAttackId, "Basic attack", 1, SkillMouseUse.Both);
    private readonly Dictionary<uint, HudSkillChoice> _choices = new() { [BasicAttackId] = BasicAttack };
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private uint _pressedSkill;
    private SkillMouseUse _pressedSide;
    private bool _boundDuringPress;

    // The saved record restores both sockets from the selected skill, which is 0 (the normal attack) until a skill is chosen.
    public uint LeftSkillId { get; private set; } = BasicAttackId;
    public uint RightSkillId { get; private set; } = BasicAttackId;
    public HudSkillChoice Left => _choices.GetValueOrDefault(LeftSkillId);
    public HudSkillChoice Right => _choices.GetValueOrDefault(RightSkillId);
    public HudSkillChoice Choice(uint skillId) => _choices.GetValueOrDefault(skillId);

    // The short keys are the action bar's: its shown page's skill shortcuts.
    public HudActionBarSlots Slots { get; } = new();

    public HudSkillShortcut Shortcut(int index) => Slots[index] is { Kind: HudActionKind.Skill } slot ? new HudSkillShortcut(slot.SkillId, slot.Side) : default;

    // The skill pressed in the skill window and its button, until that button is released; the action bar takes it
    // when it is released over one of its keys.
    public uint PressedSkill => _pressedSkill;
    public SkillMouseUse PressedSide => _pressedSide;

    // An item shortcut's key was pressed: the catalog key of the item to use.
    public event Action<string> ItemRequested;

    // Set when a click asked for a socket the skill cannot use; Refusals counts them so the HUD shows each once.
    public string Refusal { get; private set; } = "";
    public int Refusals { get; private set; }

    // Offers the learned active skills to the sockets and short keys; an unlearned or lost skill falls back to the
    // basic attack.
    public void SyncLearned(IReadOnlyList<Skill> classSkills, IReadOnlyList<PristonTale.Simulation.Characters.OwnedSkillView> learned)
    {
        foreach (var code in _choices.Keys.Where(code => code != BasicAttackId).ToArray())
        {
            _choices.Remove(code);
        }

        foreach (var owned in learned)
        {
            foreach (var skill in classSkills)
            {
                if (skill.Code == owned.Code && owned.Rank > 0 && skill.MouseUse != SkillMouseUse.None)
                {
                    _choices[skill.Code] = new HudSkillChoice(skill.Code, skill.Name, owned.Rank, skill.MouseUse);
                }
            }
        }

        if (!_choices.ContainsKey(LeftSkillId))
        {
            LeftSkillId = BasicAttackId;
        }

        if (!_choices.ContainsKey(RightSkillId))
        {
            RightSkillId = BasicAttackId;
        }
    }

    // One key per skill on a page, and one shortcut per key.
    public bool Assign(int index, uint skillId, SkillMouseUse side) =>
        HudActionBarSlots.Valid(index) && CanSelect(skillId, side) && Slots.AssignSkill(index, skillId, side);

    // A shortcut's key or a right click on it: a skill goes to its socket, an item is asked for.
    public void Activate(int index)
    {
        var slot = Slots[index];
        if (slot.Kind == HudActionKind.Skill)
        {
            Select(slot.SkillId, slot.Side);
        }
        else if (slot.Kind == HudActionKind.Item)
        {
            ItemRequested?.Invoke(slot.ItemKey);
        }
    }

    public bool Select(uint skillId, SkillMouseUse side)
    {
        if (!CanSelect(skillId, side))
        {
            return false;
        }

        if (side == SkillMouseUse.Left)
        {
            LeftSkillId = skillId;
        }
        else
        {
            RightSkillId = skillId;
        }

        return true;
    }

    public void Update(KeyboardState keyboard, MouseState mouse, uint hoveredSkill)
    {
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Left);
        }
        else if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Right);
        }

        // Ctrl chords are the debug keys and Alt with 1 to 3 turns the bar's page.
        var chord = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)
            || keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        for (var index = 0; index < HudActionBarSlots.SlotCount && !chord; index++)
        {
            var key = HudActionBarSlots.HotKeys[index];
            if (!keyboard.IsKeyDown(key) || _previousKeys.IsKeyDown(key))
            {
                continue;
            }

            if (hoveredSkill != 0)
            {
                var held = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton : mouse.RightButton;
                if (_pressedSkill == hoveredSkill && held == ButtonState.Pressed && Assign(index, hoveredSkill, _pressedSide))
                {
                    _boundDuringPress = true;
                }
            }
            else
            {
                Activate(index);
            }
        }

        var released = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton == ButtonState.Released : mouse.RightButton == ButtonState.Released;
        if (_pressedSkill != 0 && released)
        {
            if (!_boundDuringPress && hoveredSkill == _pressedSkill && !Select(_pressedSkill, _pressedSide)
                && _choices.TryGetValue(_pressedSkill, out var refused))
            {
                var allowed = refused.MouseUse == SkillMouseUse.Left ? "left" : "right";
                Refusal = $"{refused.Name} can only be used with the {allowed} mouse button.";
                Refusals++;
            }

            _pressedSkill = 0;
            _pressedSide = SkillMouseUse.None;
        }

        _previousKeys = keyboard;
        _previousMouse = mouse;
    }

    private void BeginPress(uint skillId, SkillMouseUse side)
    {
        _pressedSkill = skillId;
        _pressedSide = side;
        _boundDuringPress = false;
    }

    private bool CanSelect(uint skillId, SkillMouseUse side)
    {
        return side is SkillMouseUse.Left or SkillMouseUse.Right
            && _choices.TryGetValue(skillId, out var skill) && skill.Rank > 0 && (skill.MouseUse & side) == side;
    }
}
