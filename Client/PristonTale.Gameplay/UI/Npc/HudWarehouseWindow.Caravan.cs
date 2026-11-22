using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The caravan's storage shows in the warehouse window with the caravan's own controls where the warehouse keeps its gold and
// weight: the caravan's title, its name (a field over an underline, at most PetNames.MaxLength characters), a "Follow me!"
// box and a Close button. Closing asks for what changed with Caravans' chat commands (CaravanChanges).
public sealed partial class HudWarehouseWindow
{
    private const string CaravanFolder = "game/images/caravan/";
    public const string CaravanTitle = CaravanFolder + "title.png";
    public const string CaravanUnderline = CaravanFolder + "underline.png";
    public const string CheckOff = "game/images/UI/checkbox.png";
    public const string CheckOn = "game/images/UI/checkbox_.png";
    public const string CloseButton = "game/images/UI/window/btnclose.png";
    public const string CloseButtonLit = "game/images/UI/window/btnclose_.png";
    public static readonly string[] CaravanArt = [CaravanTitle, CaravanUnderline, CheckOff, CheckOn, CloseButton, CloseButtonLit];

    private readonly TextInputField _caravanName = new() { MaxLength = PetNames.MaxLength, Accepts = static ch => char.IsLetterOrDigit(ch) || ch == ' ' };
    private string _caravanShownName = "";
    private bool _caravanFollows;
    private bool _caravanShownFollows;

    public bool CaravanMode { get; private set; }

    public bool NamingCaravan => CaravanMode && FullyOpen && _caravanName.Focused;

    public static Rectangle CaravanNameField(Point origin) => new(origin.X + 232, origin.Y + 152, 98, 18);
    public static Rectangle CaravanFollowBox(Point origin) => new(origin.X + 232, origin.Y + 196, 98, 15);
    public static Rectangle CaravanClose(Point origin) => new(origin.X + 234, origin.Y + 228, 93, 23);

    // name: the name given (empty for none); follows: whether the caravan follows its owner.
    public void ShowCaravan(WarehouseView view, string name, bool follows)
    {
        Show(view);
        CaravanMode = true;
        _caravanName.Set(name);
        _caravanName.Focused = false;
        _caravanShownName = _caravanName.Text;
        _caravanFollows = follows;
        _caravanShownFollows = follows;
    }

    public void TypeCaravanName(ReadOnlySpan<char> typed, double elapsedSeconds) => _caravanName.Update(typed, elapsedSeconds);

    public void StopNamingCaravan() => _caravanName.Focused = false;

    // The commands that apply what the window changed, in the order they are sent; empty when nothing changed.
    public List<string> CaravanChanges()
    {
        var commands = new List<string>(2);
        var name = _caravanName.Text.Trim();
        if (!string.Equals(name, _caravanShownName.Trim(), StringComparison.Ordinal))
        {
            commands.Add(Caravans.NameCommand(name));
        }

        if (_caravanFollows != _caravanShownFollows)
        {
            commands.Add(Caravans.FollowCommand(_caravanFollows));
        }

        _caravanShownName = name;
        _caravanShownFollows = _caravanFollows;
        return commands;
    }

    private HudWarehouseAction? CaravanClick(Point pointer)
    {
        var origin = Origin;
        _caravanName.Focused = CaravanNameField(origin).Contains(pointer);
        if (_caravanName.Focused)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        if (CaravanFollowBox(origin).Contains(pointer))
        {
            _caravanFollows = !_caravanFollows;
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        return CaravanClose(origin).Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Exit) : null;
    }

    private void DrawCaravan(HudPainter painter, Point origin, Point pointer)
    {
        painter.Sprite(CaravanTitle, origin.X + 243, origin.Y + 100);
        var field = CaravanNameField(origin);
        painter.Text(ClientText.Get("Caravan.Name"), field.X, field.Y - 16, HudTextColors.Hint);
        painter.Text(_caravanName.CaretVisible ? _caravanName.Text + "_" : _caravanName.Text, field.X + 2, field.Y + 1, Color.White);
        painter.Stretch(CaravanUnderline, new Rectangle(field.X, field.Bottom, field.Width, 1));
        painter.Text(ClientText.Format("Caravan.MaxName", PetNames.MaxLength), field.X, field.Bottom + 3, HudTextColors.Hint);
        var follow = CaravanFollowBox(origin);
        painter.Sprite(_caravanFollows ? CheckOn : CheckOff, follow.X, follow.Y + 1);
        painter.Text(ClientText.Get("Caravan.Follow"), follow.X + 18, follow.Y, follow.Contains(pointer) ? HudTextColors.Highlight : Color.White);
        var close = CaravanClose(origin);
        painter.Sprite(close.Contains(pointer) ? CloseButtonLit : CloseButton, close.X, close.Y);
    }

    // The hint over the Follow box, drawn over the other windows.
    private void DrawCaravanHint(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (CaravanFollowBox(Origin).Contains(pointer))
        {
            HudItemTooltip.Draw(painter, layout, pointer, ClientText.Get("Caravan.Follow"), ClientText.Get("Caravan.FollowHint"));
        }
    }
}
