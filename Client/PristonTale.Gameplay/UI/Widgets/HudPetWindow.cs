using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Widgets;

// The stable, drawn on the frame of Hud/Stable with two tabs. Pets: the pet out's name with a pencil that opens the rename
// box, then one row per pet skin (portrait frame, name, unlocked or locked) four at a time with a scroll bar, and "N of M
// acquired" at the foot; a row's paw sends that pet out, and on the pet out the remove mark puts it away. Mounts: the
// picked mount's name, then one row per mount (its portrait from Hud/Mount/mounts, its name in its rarity's colour, its
// speed or locked); a row's saddle picks that mount and rides it, and on the ridden one the remove mark gets off. It sends
// the server's "/pet", "/petname" and "/mount" commands through the chat (PetCommand, MountCommand), and the server's
// PetCollection answers. A pet button under the boss list's, left of the minimap, and J open and close it; Escape closes
// it. Without the stable art it draws on a plain dark box.
public sealed class HudPetWindow
{
    private const string Folder = "Hud/Stable/";
    private const string BaseArt = Folder + "Base.png";
    private const string FrameArt = Folder + "Frame.png";
    private const string PetsTabArt = Folder + "Pets.png";
    private const string PetsTabLitArt = Folder + "H_Pets.png";
    private const string MountTabArt = Folder + "Mount.png";
    private const string MountTabLitArt = Folder + "H_Mount.png";
    private const string SaddleArt = Folder + "Saddle.png";
    private const string SaddleHoverArt = Folder + "H_Saddle.png";
    private const string PortraitFolder = "Hud/Mount/mounts/";
    private const string EditArt = Folder + "EditIcon.png";
    private const string EditHoverArt = Folder + "EditIcon_.png";
    private const string SummonArt = Folder + "Summon.png";
    private const string SummonHoverArt = Folder + "H_Summon.png";
    private const string RemoveArt = Folder + "Remove.png";
    private const string RemoveHoverArt = Folder + "Remove_.png";
    private const string ScrollArt = Folder + "ScrollBar.png";
    private const string ScrollerArt = Folder + "ScrollIcon.png";
    private static readonly string[] Portraits = Mounts.All.Select(static mount => PortraitFolder + mount.Portrait).ToArray();
    public static readonly string[] Art =
    [
        BaseArt, FrameArt, PetsTabArt, PetsTabLitArt, MountTabArt, MountTabLitArt, SaddleArt, SaddleHoverArt, EditArt, EditHoverArt, SummonArt,
        SummonHoverArt, RemoveArt, RemoveHoverArt, ScrollArt, ScrollerArt, .. Portraits
    ];

    // The stable art is 254x439; every place below is in its pixels.
    private const int Width = 254;
    private const int Height = 439;
    private static readonly Rectangle PetsTab = new(43, 61, 73, 21);
    private static readonly Rectangle MountTab = new(118, 61, 73, 21);
    private static readonly Rectangle NameLine = new(40, 92, 170, 18);
    private static readonly Rectangle Edit = new(171, 96, 10, 10);
    private static readonly Rectangle Track = new(213, 92, 4, 270);
    private const int RowLeft = 43;
    private const int RowTop = 123;
    private const int RowStep = 65;
    private const int VisibleRows = 4;
    private const int FrameSize = 55;
    private const int TextLeft = 111;
    private const int MarkLeft = 186;
    private const int CountBottom = 388;
    private const int ButtonSize = HudBossList.ButtonSize;

    private static readonly Color TitleColor = new(255, 220, 140);
    private static readonly Color NameColor = new(240, 240, 240);
    private static readonly Color LockedColor = new(120, 120, 120);
    private static readonly Color StateColor = new(150, 160, 230);
    private static readonly Color OutColor = new(255, 200, 80);
    private static readonly string[] Names = PetSkins.All.Select(static skin => skin.Name).ToArray();
    private static readonly string[] MountNames = Mounts.All.Select(static mount => mount.Name).ToArray();
    private static readonly string[] MountSpeeds = Mounts.All.Select(static mount => $"+{mount.Speed} speed, {mount.Rarity}").ToArray();

    // The pet's new name while the player types it (PetNames: letters, digits and spaces the HUD font draws).
    private readonly TextInputField _name = new() { MaxLength = PetNames.MaxLength, Accepts = static ch => char.IsLetterOrDigit(ch) || ch == ' ' };
    private PetCollectionInfo _shownFor;
    private string _shownOwner;
    private string _nameLine = "";
    private string _countLine = "";
    private string _mountLine = "";
    private string _mountCountLine = "";
    private int _scroll;

    // The tab shown: the pets or the mounts.
    public bool MountsShown { get; private set; }

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

    // The typed characters while naming: Enter sends "/petname <name>" (an empty name clears it).
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

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta != 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(wheelDelta), 0, MaxScroll);
        }
    }

    private int RowCount => MountsShown ? Mounts.All.Count : PetSkins.All.Count;

    private int MaxScroll => Math.Max(0, RowCount - VisibleRows);

    // A mount's name colour: its rarity's.
    public static Color RarityColor(MountRarity rarity) => rarity switch
    {
        MountRarity.Common => new Color(0, 168, 255),
        MountRarity.Rare => new Color(40, 200, 40),
        MountRarity.Epic => new Color(190, 90, 230),
        _ => new Color(255, 233, 0)
    };

    // The button sits under the boss list's, left of the minimap's title.
    public static Rectangle Button(Rectangle minimapArea)
    {
        var boss = HudBossList.Button(minimapArea);
        return new Rectangle(boss.X, boss.Bottom + 2, ButtonSize, ButtonSize);
    }

    public static Rectangle Window(HudLayout layout) =>
        new(Math.Max(0, layout.Width - Width - 220), Math.Max(0, (layout.Height - Height) / 2 - 40), Width, Height);

    public static Rectangle Pencil(Rectangle window) => At(window, Edit);

    private static Rectangle At(Rectangle window, Rectangle place) => new(window.X + place.X, window.Y + place.Y, place.Width, place.Height);

    private static Rectangle Row(Rectangle window, int visibleIndex) =>
        new(window.X + RowLeft, window.Y + RowTop + visibleIndex * RowStep, MarkLeft + 20 - RowLeft, FrameSize);

    private static Rectangle Mark(Rectangle row) => new(row.X + MarkLeft - RowLeft, row.Y + 12, 17, 16);

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

        if (At(window, PetsTab).Contains(pointer) || At(window, MountTab).Contains(pointer))
        {
            MountsShown = At(window, MountTab).Contains(pointer);
            _scroll = 0;
            _name.Focused = false;
            return true;
        }

        if (MountsShown)
        {
            ClickMount(window, pointer, pets);
            return true;
        }

        if (At(window, Edit).Contains(pointer) && pets.Out != 0)
        {
            _name.Clear();
            _name.Focused = !_name.Focused;
            return true;
        }

        for (var row = 0; row < VisibleRows && _scroll + row < PetSkins.All.Count; row++)
        {
            var skin = PetSkins.All[_scroll + row];
            if (Mark(Row(window, row)).Contains(pointer) && PetSkins.Has(pets.Skins, skin.Id))
            {
                Request = PetCommand.Out(pets.Out == skin.Id ? 0 : skin.Id);
            }
        }

        return true;
    }

    private void ClickMount(Rectangle window, Point pointer, PetCollectionInfo pets)
    {
        for (var row = 0; row < VisibleRows && _scroll + row < Mounts.All.Count; row++)
        {
            var mount = Mounts.All[_scroll + row];
            if (Mark(Row(window, row)).Contains(pointer) && Mounts.Has(pets.Mounts, mount.Id))
            {
                Request = MountCommand.Of(pets.Riding && pets.MountChosen == mount.Id ? 0 : mount.Id);
            }
        }
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
        if (painter.Texture(BaseArt) is null)
        {
            painter.ColorBox(window, 20, 20, 20, 230);
        }

        painter.Sprite(BaseArt, window.X, window.Y);
        var petsTab = At(window, PetsTab);
        var mountTab = At(window, MountTab);
        painter.Sprite(!MountsShown || petsTab.Contains(pointer) ? PetsTabLitArt : PetsTabArt, petsTab.X, petsTab.Y);
        painter.Sprite(MountsShown || mountTab.Contains(pointer) ? MountTabLitArt : MountTabArt, mountTab.X, mountTab.Y);
        Describe(pets, ownerName);
        if (MountsShown)
        {
            DrawMounts(painter, window, pets, pointer);
            DrawScroll(painter, window);
            painter.Text(_mountCountLine, window.X + Width - 40 - painter.Measure(_mountCountLine), window.Y + CountBottom, NameColor);
            return;
        }

        DrawName(painter, window, pets, pointer);

        for (var row = 0; row < VisibleRows && _scroll + row < PetSkins.All.Count; row++)
        {
            var skin = PetSkins.All[_scroll + row];
            var bounds = Row(window, row);
            var unlocked = PetSkins.Has(pets.Skins, skin.Id);
            var isOut = pets.Out == skin.Id;
            painter.Sprite(FrameArt, bounds.X, bounds.Y);
            var icon = new Rectangle(bounds.X + 11, bounds.Y + 10, 36, 36);
            icons.Draw(painter, PetSkins.IconKey, icon, unlocked ? Color.White : new Color(70, 70, 70));
            var textX = window.X + TextLeft;
            painter.Text(painter.Fit(Names[_scroll + row], MarkLeft - TextLeft - 4), textX, bounds.Y + 12, isOut ? OutColor : unlocked ? NameColor : LockedColor);
            painter.Text(isOut ? "Out" : unlocked ? "Unlocked" : "Locked", textX, bounds.Y + 28, unlocked ? StateColor : LockedColor);
            if (unlocked)
            {
                var mark = Mark(bounds);
                var hovered = mark.Contains(pointer);
                painter.Sprite(isOut ? hovered ? RemoveHoverArt : RemoveArt : hovered ? SummonHoverArt : SummonArt, mark.X, mark.Y);
            }
        }

        DrawScroll(painter, window);
        painter.Text(_countLine, window.X + Width - 40 - painter.Measure(_countLine), window.Y + CountBottom, NameColor);
    }

    private void DrawScroll(HudPainter painter, Rectangle window)
    {
        if (MaxScroll > 0)
        {
            var track = At(window, Track);
            painter.Stretch(ScrollArt, track);
            painter.Sprite(ScrollerArt, track.X + track.Width / 2 - 8, track.Y + (track.Height - 17) * _scroll / MaxScroll);
        }
    }

    // The picked mount's name, then a row per mount: its portrait, its name in its rarity's colour, its speed (or riding,
    // or locked), and the saddle that rides it or the mark that gets off it.
    private void DrawMounts(HudPainter painter, Rectangle window, PetCollectionInfo pets, Point pointer)
    {
        var line = At(window, NameLine);
        painter.TextCentered(_mountLine, line.X, line.Right - 20, line.Y + 1, pets.MountChosen != 0 ? NameColor : LockedColor);
        for (var row = 0; row < VisibleRows && _scroll + row < Mounts.All.Count; row++)
        {
            var index = _scroll + row;
            var mount = Mounts.All[index];
            var bounds = Row(window, row);
            var owned = Mounts.Has(pets.Mounts, mount.Id);
            var ridden = pets.Riding && pets.MountChosen == mount.Id;
            painter.Sprite(FrameArt, bounds.X, bounds.Y);
            painter.Sprite(Portraits[index], bounds.X + 7, bounds.Y + 5, owned ? Color.White : new Color(70, 70, 70));
            var textX = window.X + TextLeft;
            painter.Text(painter.Fit(MountNames[index], MarkLeft - TextLeft - 4), textX, bounds.Y + 12, owned ? RarityColor(mount.Rarity) : LockedColor);
            painter.Text(ridden ? "Riding" : owned ? MountSpeeds[index] : "Locked", textX, bounds.Y + 28, ridden ? OutColor : owned ? StateColor : LockedColor);
            if (owned)
            {
                var mark = Mark(bounds);
                var hovered = mark.Contains(pointer);
                painter.Sprite(ridden ? hovered ? RemoveHoverArt : RemoveArt : hovered ? SaddleHoverArt : SaddleArt, mark.X, mark.Y);
            }
        }
    }

    // The pet out's name and the pencil; while renaming, the box takes the line.
    private void DrawName(HudPainter painter, Rectangle window, PetCollectionInfo pets, Point pointer)
    {
        var line = At(window, NameLine);
        if (Naming)
        {
            painter.InputBox(line);
            painter.Text(_name.CaretVisible ? _name.Text + "_" : _name.Text, line.X + 4, line.Y + 1, NameColor);
            return;
        }

        painter.TextCentered(_nameLine, line.X, line.Right - 20, line.Y + 1, pets.Out != 0 ? NameColor : LockedColor);
        if (pets.Out != 0)
        {
            var edit = At(window, Edit);
            painter.Sprite(edit.Contains(pointer) ? EditHoverArt : EditArt, edit.X, edit.Y);
        }
    }

    // The pet out's name and the acquired count, formatted again only when the collection changes.
    private void Describe(PetCollectionInfo pets, string ownerName)
    {
        if (ReferenceEquals(pets, _shownFor) && string.Equals(ownerName, _shownOwner, StringComparison.Ordinal))
        {
            return;
        }

        _shownFor = pets;
        _shownOwner = ownerName;
        _nameLine = PetSkins.Get(pets.Out) is not null ? PetNames.Shown(pets.Name, ownerName) : "No pet out";
        var owned = 0;
        foreach (var skin in PetSkins.All)
        {
            if (PetSkins.Has(pets.Skins, skin.Id))
            {
                owned++;
            }
        }

        _countLine = $"{owned} of {PetSkins.All.Count} acquired";
        var mounts = 0;
        foreach (var mount in Mounts.All)
        {
            if (Mounts.Has(pets.Mounts, mount.Id))
            {
                mounts++;
            }
        }

        _mountCountLine = $"{mounts} of {Mounts.All.Count} owned";
        _mountLine = Mounts.Get(pets.MountChosen) is { } chosen ? pets.Riding ? $"Riding {chosen.Name}" : chosen.Name : "No mount chosen";
    }

    private void SubmitName()
    {
        Request = $"{PetNames.Command} {_name.Text.Trim()}".TrimEnd();
        _name.Focused = false;
    }
}
