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

    public bool Open { get; private set; }

    // A "/pet" line to send, set by a click and taken by the HUD.
    public string Request { get; private set; }

    public void Toggle() => Open = !Open;

    public void Close() => Open = false;

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

    public void Draw(HudPainter painter, HudLayout layout, PetCollectionInfo pets, HudItemIcons icons, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var window = Window(layout);
        painter.ColorBox(window, 8, 6, 6, 220);
        painter.Text("Pets", window.X + Padding, window.Y + 3, TitleColor);
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
        painter.Text("Click a pet to send it out.", putAway.Right + 8, putAway.Y, Locked);
    }

    private static void Frame(HudPainter painter, Rectangle bounds, Color color)
    {
        painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
    }
}
