using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Npc;

// The Boss Arena's event window, opened from the arena NPC: the countdown to the next start, the banner, the next boss with
// its entry level, and the teleport to the arena entrance (the server refuses it outside the entry time). The countdown
// reads the schedule on this machine's clock. Escape closes it.
public sealed class HudArenaEvent
{
    private const string Folder = "Hud/GrandFury/";
    private const string BannerArt = Folder + "logoheader.png";
    private const string CountdownArt = Folder + "bartime.png";
    private const string BossBackArt = Folder + "monbg.png";
    private const string LineArt = Folder + "line.png";
    private const string ArrowArt = Folder + "arrow.png";
    private const string FuryArt = Folder + "fury.png";
    private const string HagiosArt = Folder + "hagios.png";
    public static readonly string[] Art = [BannerArt, CountdownArt, BossBackArt, LineArt, ArrowArt, FuryArt, HagiosArt];

    private const int Width = 349;
    private const int Height = 318;
    private const int BannerX = 14;
    private const string TeleportText = "Teleport to Fury Arena Entrance";
    private static readonly Color FrameColor = new(10, 9, 8, 235);
    private static readonly Color BorderColor = new(150, 120, 70);
    private static readonly Color TextColor = new(235, 225, 205);
    private static readonly Color BossColor = new(240, 205, 120);
    private static readonly Color LitColor = new(255, 214, 90);

    private BossArenaEvent _arena;
    private string _bossLine = "";
    private string _countdown = "";
    private long _shownSecond = -1;

    public bool Open { get; private set; }

    // Sends the entry request.
    public Action Teleport { get; set; }

    public static Rectangle Window(HudLayout layout) => new((layout.Width - Width) / 2, Math.Max(0, (layout.Height - Height) / 2 - 60), Width, Height);

    private static Rectangle TeleportRow(Rectangle window) => new(window.X + 20, window.Y + 252, Width - 40, 20);

    private static Rectangle CloseButton(Rectangle window) => new(window.X + (Width - 90) / 2, window.Bottom - 34, 90, 24);

    public void Show() => Open = true;

    public void Hide() => Open = false;

    public bool Captures(HudLayout layout, Point pointer) => Open && Window(layout).Contains(pointer);

    public bool Click(HudLayout layout, Point pointer)
    {
        var window = Window(layout);
        if (!Open || !window.Contains(pointer))
        {
            return false;
        }

        if (TeleportRow(window).Contains(pointer))
        {
            Teleport?.Invoke();
            Hide();
        }
        else if (CloseButton(window).Contains(pointer))
        {
            Hide();
        }

        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        Refresh();
        var window = Window(layout);
        painter.Fill(window, FrameColor);
        painter.Fill(new Rectangle(window.X, window.Y, window.Width, 1), BorderColor);
        painter.Fill(new Rectangle(window.X, window.Bottom - 1, window.Width, 1), BorderColor);
        painter.Fill(new Rectangle(window.X, window.Y, 1, window.Height), BorderColor);
        painter.Fill(new Rectangle(window.Right - 1, window.Y, 1, window.Height), BorderColor);

        painter.Sprite(CountdownArt, window.X + (Width - 208) / 2, window.Y + 10);
        painter.TextCentered(_countdown, window.X, window.Right, window.Y + 13, TextColor);
        painter.Sprite(BannerArt, window.X + BannerX, window.Y + 36);
        painter.Text("Next Boss:", window.X + 20, window.Y + 162, TextColor);
        painter.Sprite(BossBackArt, window.X + 20, window.Y + 180);
        painter.Sprite(_arena.Id == 0 ? FuryArt : HagiosArt, window.X + 26, window.Y + 186);
        painter.Text(_arena.Name, window.X + 72, window.Y + 188, BossColor);
        painter.Text(_bossLine, window.X + 72, window.Y + 204, TextColor);
        painter.Sprite(LineArt, window.X + 12, window.Y + 240);

        var row = TeleportRow(window);
        painter.Sprite(ArrowArt, row.X + 8, row.Y + 7);
        painter.Text(TeleportText, row.X + 20, row.Y + 2, row.Contains(pointer) ? LitColor : TextColor);

        var close = CloseButton(window);
        painter.ColorBox(close, 90, 50, 20, close.Contains(pointer) ? 230 : 180);
        painter.TextCentered("Close", close.X, close.Right, close.Y + 4, TextColor);
    }

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var second = now.ToUnixTimeSeconds();
        if (second == _shownSecond)
        {
            return;
        }

        _shownSecond = second;
        var start = BossArena.NextStart(now);
        _arena = BossArena.EventAt(start.Hour);
        _bossLine = string.Create(CultureInfo.InvariantCulture, $"Level {_arena.MinLevel}+");
        _countdown = string.Create(CultureInfo.InvariantCulture, $"Countdown Time: {start - now:hh\\:mm\\:ss}");
    }
}
