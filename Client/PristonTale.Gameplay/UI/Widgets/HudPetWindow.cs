using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

// The pet collection: every pet skin in a grid of five, unlocked ones lit and the one out framed. A click on an unlocked
// skin sends its pet out (on the one out, puts it away); "Put away" puts any pet away. It sends the server's "/pet"
// command through the chat (PetCommand), and the server's PetCollection answers. A pet button under the boss list's,
// left of the minimap, and J open and close it; Escape closes it.
public sealed class HudPetWindow
{
    private const int Columns = 5;
    private const int CellWidth = 72;
    private const int CellHeight = 52;
    private const int Header = 22;
    private const int Padding = 8;
    private const int Footer = 26;
    private const int ButtonSize = HudBossList.ButtonSize;
    private static readonly Color TitleColor = new(255, 220, 140);
    private static readonly Color Unlocked = new(240, 240, 240);
    private static readonly Color Locked = new(120, 120, 120);
    private static readonly Color OutFrame = new(255, 200, 80);
    private static readonly string[] Names = PetSkins.All.Select(static skin => skin.Name).ToArray();
    private static readonly int Rows = (PetSkins.All.Count + Columns - 1) / Columns;

    // The pet's new name while the player types it (PetNames: letters, digits and spaces the HUD font draws).
    private readonly TextInputField _name = new() { MaxLength = PetNames.MaxLength, Accepts = static ch => char.IsLetterOrDigit(ch) || ch == ' ' };
    private PetCollectionInfo _shownFor;
    private string _shownOwner;
    private string _nameLine = "";

    public bool Open { get; private set; }

    // The rename box has the keyboard: neither the chat nor a game key sees what is typed.
    public bool Naming => Open && _name.Focused;

    // A "/pet" line to send, set by a click and taken by the HUD.
    public string Request { get; private set; }

    public void Toggle() => Open = !Open;

    public void Close()
    {
        Open = false;
        _name.Focused = false;
    }

    public void CancelNaming() => _name.Focused = false;

    // The typed characters while naming: Enter (or OK) sends "/petname <name>" (an empty name clears it).
    public void Type(ReadOnlySpan<char> typed, double elapsedSeconds, Func<Microsoft.Xna.Framework.Input.Keys, bool> pressed)
    {
        _name.Update(typed, elapsedSeconds);
        if (pressed(Microsoft.Xna.Framework.Input.Keys.Enter))
        {
            SubmitName();
        }
    }

    public string TakeRequest()
    {
        var request = Request;
        Request = null;
        return request;
    }

    // The button sits under the boss list's, left of the minimap's title.
    public static Rectangle Button(Rectangle minimapArea)
    {
        var boss = HudBossList.Button(minimapArea);
        return new Rectangle(boss.X, boss.Bottom + 2, ButtonSize, ButtonSize);
    }

    public static Rectangle Window(HudLayout layout)
    {
        var width = Padding * 2 + Columns * CellWidth;
        var height = Header + Rows * CellHeight + Footer + Padding;
        return new Rectangle((layout.Width - width) / 2, Math.Max(0, (layout.Height - height) / 2 - 60), width, height);
    }

    public static Rectangle Cell(Rectangle window, int index) =>
        new(window.X + Padding + index % Columns * CellWidth, window.Y + Header + index / Columns * CellHeight, CellWidth - 4, CellHeight - 4);

    public static Rectangle PutAway(Rectangle window) => new(window.X + Padding, window.Bottom - Footer, 80, 18);

    public static Rectangle CloseMark(Rectangle window) => new(window.Right - 16, window.Y + 4, 12, 12);

    public static Rectangle Rename(Rectangle window) => new(PutAway(window).Right + 8, window.Bottom - Footer, 64, 18);

    public static Rectangle NameBox(Rectangle window) => new(Rename(window).Right + 8, window.Bottom - Footer, window.Right - Padding - 44 - Rename(window).Right - 8, 18);

    public static Rectangle Accept(Rectangle window) => new(window.Right - Padding - 36, window.Bottom - Footer, 36, 18);

    public bool Captures(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown) =>
        minimapShown && Button(minimapArea).Contains(pointer) || Open && Window(layout).Contains(pointer);

    // A click on the button or the window; true when the window took it.
    public bool Click(HudLayout layout, Rectangle minimapArea, Point pointer, bool minimapShown, PetCollectionInfo pets)
    {
        if (minimapShown && Button(minimapArea).Contains(pointer))
        {
            Toggle();
            return true;
        }

        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        if (CloseMark(window).Contains(pointer))
        {
            Close();
        }
        else if (Rename(window).Contains(pointer) && pets.Out != 0)
        {
            _name.Clear();
            _name.Focused = !_name.Focused;
        }
        else if (Naming && Accept(window).Contains(pointer))
        {
            SubmitName();
        }
        else if (PutAway(window).Contains(pointer) && pets.Out != 0)
        {
            Request = PetCommand.Out(0);
        }
        else
        {
            for (var index = 0; index < PetSkins.All.Count; index++)
            {
                var skin = PetSkins.All[index];
                if (Cell(window, index).Contains(pointer) && PetSkins.Has(pets.Skins, skin.Id))
                {
                    Request = PetCommand.Out(pets.Out == skin.Id ? 0 : skin.Id);
                }
            }
        }

        return true;
    }

    public static void DrawButton(HudPainter painter, Rectangle minimapArea, Point pointer, bool open)
    {
        var button = Button(minimapArea);
        painter.ColorBox(button, 0, 0, 0, open || button.Contains(pointer) ? 200 : 140);
        painter.TextCentered("P", button.X, button.Right, button.Y - 1, TitleColor);
    }

    public void Draw(HudPainter painter, HudLayout layout, PetCollectionInfo pets, string ownerName, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        painter.ColorBox(window, 8, 6, 6, 220);
        painter.Text("Pets", window.X + Padding, window.Y + 3, TitleColor);
        painter.Text(NameLine(pets, ownerName), window.X + Padding + 44, window.Y + 3, Unlocked);
        painter.TextCentered("x", CloseMark(window).X, CloseMark(window).Right, window.Y + 3, TitleColor);
        for (var index = 0; index < PetSkins.All.Count; index++)
        {
            var skin = PetSkins.All[index];
            var cell = Cell(window, index);
            var unlocked = PetSkins.Has(pets.Skins, skin.Id);
            var hovered = unlocked && cell.Contains(pointer);
            painter.ColorBox(cell, 30, 26, 22, hovered ? 240 : 200);
            if (pets.Out == skin.Id)
            {
                Frame(painter, cell, OutFrame);
            }

            var icon = new Rectangle(cell.X + (cell.Width - 22) / 2, cell.Y + 4, 22, 22);
            icons.Draw(painter, PetSkins.IconKey, icon, unlocked ? Color.White : new Color(70, 70, 70));
            painter.TextCentered(painter.Fit(Names[index], cell.Width - 4), cell.X, cell.Right, cell.Y + 28, unlocked ? Unlocked : Locked);
        }

        var putAway = PutAway(window);
        painter.ColorBox(putAway, 40, 34, 26, pets.Out != 0 && putAway.Contains(pointer) ? 240 : 200);
        painter.TextCentered("Put away", putAway.X, putAway.Right, putAway.Y, pets.Out != 0 ? Unlocked : Locked);
        var rename = Rename(window);
        painter.ColorBox(rename, 40, 34, 26, pets.Out != 0 && rename.Contains(pointer) || Naming ? 240 : 200);
        painter.TextCentered("Rename", rename.X, rename.Right, rename.Y, pets.Out != 0 ? Unlocked : Locked);
        if (!Naming)
        {
            painter.Text("Click a pet to send it out.", rename.Right + 8, rename.Y, Locked);
            return;
        }

        var box = NameBox(window);
        painter.ColorBox(box, 0, 0, 0, 230);
        Frame(painter, box, OutFrame);
        painter.Text(_name.CaretVisible ? _name.Text + "_" : _name.Text, box.X + 4, box.Y, Unlocked);
        var accept = Accept(window);
        painter.ColorBox(accept, 40, 34, 26, accept.Contains(pointer) ? 240 : 200);
        painter.TextCentered("OK", accept.X, accept.Right, accept.Y, Unlocked);
    }

    // "Name: <name>" of the pet out, formatted again only when the collection changes.
    private string NameLine(PetCollectionInfo pets, string ownerName)
    {
        if (!ReferenceEquals(pets, _shownFor) || !string.Equals(ownerName, _shownOwner, StringComparison.Ordinal))
        {
            _shownFor = pets;
            _shownOwner = ownerName;
            _nameLine = PetSkins.Get(pets.Out) is not null ? $"Name: {PetNames.Shown(pets.Name, ownerName)}" : "No pet out";
        }

        return _nameLine;
    }

    private void SubmitName()
    {
        Request = $"{PetNames.Command} {_name.Text.Trim()}".TrimEnd();
        _name.Focused = false;
    }

    private static void Frame(HudPainter painter, Rectangle bounds, Color color)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
    }
}
