using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Achievements;

namespace PristonTale.Gameplay.UI.Widgets;

// The player's badges (key Y, or the button left of the minimap), one a page with arrows, in the RPT art
// (assets/Hud/Achievements). Opening asks the server for the list.
public sealed class HudAchievements
{
    private const string Folder = "Hud/Achievements/";
    private const string ButtonArt = Folder + "achievement_btn.png";
    private const string LeftArt = Folder + "leftarrow.png";
    private const string LeftLit = Folder + "leftarrow_.png";
    private const string RightArt = Folder + "rightarrow.png";
    private const string RightLit = Folder + "rightarrow_.png";
    private const string Smoke = Folder + "background_smoke.png";
    private const int Width = 320;
    private const int Height = 260;
    private const int ArrowSize = 32;
    private const int BadgeSize = 128;
    private const int ButtonSize = 32;
    private const int ButtonGap = 58;
    private const int TextMargin = 20;
    private const int TitleButtonWidth = 152;
    private const int TitleButtonHeight = 28;

    private static readonly Dictionary<AchievementId, string> Badges = new()
    {
        [AchievementId.GameMaster] = Folder + "game_master.png",
        [AchievementId.LegacyMember] = Folder + "legacy_member.png",
        [AchievementId.Streamer] = Folder + "streamer.png",
        [AchievementId.TopLevel115] = Folder + "top_level115.png",
        [AchievementId.TopLevel125] = Folder + "top_level125.png",
        [AchievementId.TopLevel130] = Folder + "top_level130.png",
        [AchievementId.TopLevel145] = Folder + "top_level145.png"
    };

    public static readonly string[] Art = [ButtonArt, LeftArt, LeftLit, RightArt, RightLit, Smoke, .. Badges.Values];

    private readonly List<AchievementWireEntry> _entries = [];
    private int _page;
    private uint _active;
    private int _shownPage = -1;
    private string _name = "";
    private string _description = "";
    private string _pageText = "";

    public Action Ask { get; set; }
    public Action<uint> Choose { get; set; }
    public bool Open { get; private set; }

    public static string Name(AchievementId id, long holderKey) => AchievementRules.IsTopLevel(id)
        ? ClientText.Format("Achievement.Top", DataText.ClassTitle((PlayerClass)holderKey, 0))
        : ClientText.Get("Achievement.Name." + id);

    public void Toggle()
    {
        Open = !Open;
        if (Open)
        {
            Ask?.Invoke();
        }
    }

    public void Hide() => Open = false;

    public void Observe(AchievementsPacket packet)
    {
        _active = packet.ActiveId;
        _entries.Clear();
        foreach (var entry in packet.Entries)
        {
            if (Badges.ContainsKey((AchievementId)entry.Id))
            {
                _entries.Add(entry);
            }
        }

        _page = Math.Clamp(_page, 0, Math.Max(0, _entries.Count - 1));
        _shownPage = -1;
    }

    public static Rectangle Button(Rectangle minimap) => new(minimap.X - ButtonGap, minimap.Y + 4, ButtonSize, ButtonSize);

    public static Rectangle Box(HudLayout layout) => new((layout.Width - Width) / 2, (layout.Height - Height) / 2, Width, Height);

    private static Rectangle Left(Rectangle box) => new(box.X + 6, box.Center.Y - ArrowSize, ArrowSize, ArrowSize);

    private static Rectangle Right(Rectangle box) => new(box.Right - ArrowSize - 6, box.Center.Y - ArrowSize, ArrowSize, ArrowSize);

    private static Rectangle TitleButton(Rectangle box) => new(box.Center.X - TitleButtonWidth / 2, box.Bottom - TitleButtonHeight - 8, TitleButtonWidth, TitleButtonHeight);

    private bool Wearing => _page < _entries.Count && _entries[_page].Id == _active;

    public bool Contains(HudLayout layout, Point pointer) => Open && Box(layout).Contains(pointer);

    public void Click(HudLayout layout, Point pointer)
    {
        var box = Box(layout);
        if (_entries.Count > 0 && TitleButton(box).Contains(pointer))
        {
            Choose?.Invoke(Wearing ? (uint)AchievementRules.NoTitle : _entries[_page].Id);
        }
        else if (_page > 0 && Left(box).Contains(pointer))
        {
            _page--;
        }
        else if (_page < _entries.Count - 1 && Right(box).Contains(pointer))
        {
            _page++;
        }
    }

    public void DrawButton(HudPainter painter, Rectangle minimap)
    {
        var button = Button(minimap);
        painter.Sprite(ButtonArt, button.X, button.Y);
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var box = Box(layout);
        painter.Flush();
        painter.InfoBox(box);
        painter.Sprite(Smoke, box.X + 60, box.Y);
        if (_entries.Count == 0)
        {
            painter.TextCentered(ClientText.Get("Achievement.None"), box.Left, box.Right, box.Center.Y - 8);
            return;
        }

        Refresh();
        var entry = _entries[_page];
        painter.Stretch(Badges[(AchievementId)entry.Id], new Rectangle(box.Center.X - BadgeSize / 2, box.Y + 12, BadgeSize, BadgeSize));
        painter.TextCentered(painter.Fit(_name, Width - TextMargin * 2), box.Left, box.Right, box.Y + 150, HudTextColors.Title);
        painter.TextCentered(painter.Fit(_description, Width - TextMargin * 2), box.Left, box.Right, box.Y + 176);
        painter.TextCentered(_pageText, box.Left, box.Right, box.Y + 198, HudTextColors.Hint);
        var titleButton = TitleButton(box);
        painter.Sprite(HudArt.HelpTitle, titleButton.X, titleButton.Y);
        painter.TextCentered(ClientText.Get(Wearing ? "Achievement.HideTitle" : "Achievement.ActivateTitle"), titleButton.Left, titleButton.Right, titleButton.Y + 7,
            titleButton.Contains(pointer) ? HudTextColors.Highlight : Color.White);
        if (_page > 0)
        {
            var left = Left(box);
            painter.Sprite(left.Contains(pointer) ? LeftLit : LeftArt, left.X, left.Y);
        }

        if (_page < _entries.Count - 1)
        {
            var right = Right(box);
            painter.Sprite(right.Contains(pointer) ? RightLit : RightArt, right.X, right.Y);
        }
    }

    private void Refresh()
    {
        if (_shownPage == _page)
        {
            return;
        }

        _shownPage = _page;
        var entry = _entries[_page];
        var id = (AchievementId)entry.Id;
        _name = Name(id, (long)entry.HolderKey);
        _description = AchievementRules.IsTopLevel(id)
            ? ClientText.Format("Achievement.TopText", DataText.ClassTitle((PlayerClass)entry.HolderKey, 0), AchievementRules.LevelOf(id))
            : ClientText.Get("Achievement.Text." + id);

        _pageText = $"{_page + 1} / {_entries.Count}";
    }
}
