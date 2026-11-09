using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// The options reached from the system menu "Settings" entry: the RPT window (assets/Hud/Settings), its four tabs and a dropdown
// per option. A choice applies at once; Reset goes back to the values the window was opened with.
// The interface scale is not listed: the in-game HUD always uses the largest integer scale that fits.
internal sealed class HudSettingsPanel
{
    public const int BoxX = 120;
    public const int BoxY = 75;
    private const int BoxWidth = 568;
    private const int BoxHeight = 450;
    private const int RowHeight = 18;
    private const int RowsTop = 44;
    private const int RowsLeft = 148;
    private const int RowsWidth = 380;
    private const int ContentBottom = 435;
    private const int NoteTop = 412;
    private const int ControlWidth = 170;
    private const int TabLeft = 14;
    private const int TabTop = 50;
    private const int TabWidth = 121;
    private const int TabHeight = 29;
    private const int BarWidth = 339;
    private const int BarHeight = 23;
    private const int HeadingOffset = 96;
    private const int MaxOptions = 14;
    private const int DropArrowWidth = 26;
    private const int DropWidth = 110;
    private const int DropHeight = 26;
    private const int RowCount = 25;
    private const int Bar = -1;

    private const string Folder = "Hud/Settings/";
    private const string WindowArt = Folder + "window.png";
    private const string BarArt = Folder + "title.png";
    private const string ResetArt = Folder + "btnreset.png";
    private const string ResetLit = Folder + "btnreset_.png";
    private const string SaveArt = Folder + "btnsave.png";
    private const string SaveLit = Folder + "btnsave_.png";
    private const string CloseArt = Folder + "btnclose.png";
    private const string CloseLit = Folder + "btnclose_.png";
    private const string DropArt = Folder + "drop.png";
    private const string DropLit = Folder + "drop_.png";
    private const string DropOpen = Folder + "dropon.png";
    private const string ListArt = Folder + "dropbg.png";
    private const string ListEnd = Folder + "dropdown.png";
    private const string ScreenHeading = Folder + "screen.png";
    private const string GraphicsHeading = Folder + "graphics.png";
    private const string MusicHeading = Folder + "bgmusic.png";
    private const string SoundHeading = Folder + "bgsound.png";
    private const string GeneralHeading = Folder + "generalt.png";
    private const string TargetHeading = Folder + "target.png";
    private const string PersonalHeading = Folder + "personalization.png";
    private static readonly string[] TabArt = [Folder + "video.png", Folder + "audio.png", Folder + "general.png", Folder + "ui.png"];
    private static readonly string[] TabLit = [Folder + "video_.png", Folder + "audio_.png", Folder + "general_.png", Folder + "ui_.png"];
    private static readonly Color HoverColor = Color.FromNonPremultiplied(255, 200, 0, 64);

    public static readonly string[] Art =
    [
        WindowArt, BarArt, ResetArt, ResetLit, SaveArt, SaveLit, CloseArt, CloseLit, DropArt, DropLit, DropOpen, ListArt, ListEnd,
        ScreenHeading, GraphicsHeading, MusicHeading, SoundHeading, GeneralHeading, TargetHeading, PersonalHeading, .. TabArt, .. TabLit
    ];

    // A tab's lines from the top: an option (its row) or a heading bar.
    private readonly record struct Line(int Row, string Heading = null);

    private static Line Heading(string art) => new(Bar, art);

    private static readonly Line[][] Tabs =
    [
        [Heading(ScreenHeading), new(0), new(1), new(2), new(3), new(4), new(7), new(8),
            Heading(GraphicsHeading), new(11), new(9), new(10), new(12), new(13), new(16), new(17), new(18), new(22), new(23), new(24)],
        [Heading(MusicHeading), new(5), Heading(SoundHeading), new(6)],
        [Heading(GeneralHeading), new(20), new(19)],
        [Heading(TargetHeading), new(14), new(21), Heading(PersonalHeading), new(15)]
    ];

    private const int CloudQualities = (int)CloudQuality.High + 1;

    private static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    private static readonly int[] ShadowSizes = [1024, 2048, 4096, 8192];
    private static readonly int WindowModes = Enum.GetValues<WindowMode>().Length;
    private static readonly int AntiAliasingModes = Enum.GetValues<AntiAliasingMode>().Length;
    private static readonly GraphicsQuality[] Presets = [GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High, GraphicsQuality.Ultra];
    private static readonly int[] ShadowUpdateRates = [30, 60, 120, 0];
    private static readonly float[] RenderScales = [1f, 0.77f, 0.67f, 0.59f, 0.5f];
    private static readonly int[] DynamicResolutionTargets = [0, 30, 60, 90, 120, 144];
    private readonly PresentationSettings _settings;
    private readonly string[] _labels = new string[RowCount];
    private readonly string[] _names = new string[RowCount];
    private readonly string[] _values = new string[RowCount];
    private readonly List<ClientSettings> _options = [];
    private readonly List<string> _optionTexts = [];
    private ClientSettings _labelSettings;
    private ClientSettings _opened;
    private int _tab;
    private int _dropLine = -1;

    public HudSettingsPanel(PresentationSettings settings) => _settings = settings;

    public static Rectangle Box => new(BoxX, BoxY, BoxWidth, BoxHeight);
    public static Rectangle OkButton => new(BoxX + 28, BoxY + 400, 93, 23);
    private static Rectangle ResetButton => new(BoxX + 28, BoxY + 374, 93, 23);
    private static Rectangle CloseButton => new(BoxX + 536, BoxY + 4, 32, 47);
    private static Rectangle Tab(int index) => new(BoxX + TabLeft, BoxY + TabTop + TabHeight * index, TabWidth, TabHeight);

    public static Rectangle Row(int line) => new(BoxX + RowsLeft, BoxY + RowsTop + line * RowHeight, RowsWidth, RowHeight);
    private static Rectangle Control(Rectangle row) => new(row.Right - ControlWidth, row.Y, ControlWidth, RowHeight);

    public static bool Closes(Point pointer) => OkButton.Contains(pointer) || CloseButton.Contains(pointer);

    // Every option's row, for checking that each has a place in a tab.
    internal static IEnumerable<int> ListedRows() => Tabs.SelectMany(static tab => tab).Where(static line => line.Row != Bar).Select(static line => line.Row);

    internal static int OptionRows => RowCount;

    // The window opens on its first tab and remembers the values Reset returns to.
    public void Opened()
    {
        _opened = _settings.Requested;
        _tab = 0;
        _dropLine = -1;
    }

    // The list opens under its option, or over it where it would leave the window.
    internal Rectangle OptionBounds(int index)
    {
        var control = Control(Row(_dropLine));
        var height = _options.Count * RowHeight;
        var top = control.Bottom + height > BoxY + ContentBottom ? control.Y - height : control.Bottom;
        return new Rectangle(control.X, top + index * RowHeight, control.Width, RowHeight);
    }

    public void Click(Point pointer, bool backwards)
    {
        if (_settings.AwaitingConfirmation)
        {
            return;
        }

        if (_dropLine >= 0)
        {
            ClickList(pointer);
            return;
        }

        if (ResetButton.Contains(pointer))
        {
            _settings.Requested = _opened;
            return;
        }

        for (var tab = 0; tab < Tabs.Length; tab++)
        {
            if (Tab(tab).Contains(pointer))
            {
                _tab = tab;
                return;
            }
        }

        var lines = Tabs[_tab];
        for (var line = 0; line < lines.Length; line++)
        {
            if (lines[line].Row == Bar || !Row(line).Contains(pointer))
            {
                continue;
            }

            if (backwards)
            {
                _settings.Requested = Step(lines[line].Row, _settings.Requested, -1);
            }
            else
            {
                OpenList(line);
            }

            return;
        }
    }

    private void ClickList(Point pointer)
    {
        for (var index = 0; index < _options.Count; index++)
        {
            if (OptionBounds(index).Contains(pointer))
            {
                _settings.Requested = _options[index];
                break;
            }
        }

        _dropLine = -1;
    }

    // The choices are the option's own steps from the current value, until they come round again.
    private void OpenList(int line)
    {
        var row = Tabs[_tab][line].Row;
        var labels = new string[RowCount];
        _options.Clear();
        _optionTexts.Clear();
        var candidate = _settings.Requested;
        while (_options.Count < MaxOptions)
        {
            FillLabels(labels, candidate);
            var value = Split(labels[row]).Value;
            if (_optionTexts.Contains(value))
            {
                break;
            }

            _options.Add(candidate);
            _optionTexts.Add(value);
            candidate = Step(row, candidate, 1);
        }

        _dropLine = line;
    }

    private ClientSettings Step(int row, ClientSettings current, int direction) => row switch
    {
            0 => current with { WindowMode = (WindowMode)(((int)current.WindowMode + direction + WindowModes) % WindowModes) },
            1 => current with { Monitor = ((current.Monitor + 1 + direction + _settings.MonitorCount + 1) % (_settings.MonitorCount + 1)) - 1 },
            2 => NextResolution(current, direction),
            3 => current with { RefreshRate = NextFrequency(current, direction) },
            4 => current with { VerticalSync = !current.VerticalSync },
            5 => current with { MusicEnabled = !current.MusicEnabled },
            6 => current with { SoundEnabled = !current.SoundEnabled },
            7 => current with { MaxFps = Cycle(FrameLimits, current.MaxFps, direction) },
            8 => current with { BackgroundMaxFps = Cycle(FrameLimits, current.BackgroundMaxFps, direction) },
            9 => current with { Quality = GraphicsQuality.Custom, AntiAliasing = (AntiAliasingMode)(((int)current.AntiAliasing + direction + AntiAliasingModes) % AntiAliasingModes) },
            10 => current with { Quality = GraphicsQuality.Custom, ShadowMapSize = Cycle(ShadowSizes, current.ShadowMapSize, direction) },
            11 => current.WithQuality(NextPreset(current.Quality, direction)),
            12 => current with { Quality = GraphicsQuality.Custom, ShadowUpdateRate = Cycle(ShadowUpdateRates, current.ShadowUpdateRate, direction) },
            13 => current with { Quality = GraphicsQuality.Custom, ShadowsEnabled = !current.ShadowsEnabled },
            14 => current with { ClassicTargetHealthBar = !current.ClassicTargetHealthBar },
            15 => current with { ClassicNamePlates = !current.ClassicNamePlates },
            16 => current with { WeatherEnabled = !current.WeatherEnabled },
            17 => current with { RealisticSky = !current.RealisticSky },
            18 => current with { Quality = GraphicsQuality.Custom, CloudQuality = (CloudQuality)(((int)current.CloudQuality + direction + CloudQualities) % CloudQualities) },
            19 => current with { PetPicksUpPotions = !current.PetPicksUpPotions },
            20 => current with { Language = NextLanguage(current.Language, direction) },
            21 => current with { ClassicTargetFrame = !current.ClassicTargetFrame },
            22 => current with { DepthOfField = !current.DepthOfField },
            23 => current with { RenderScale = RenderScales[(Array.IndexOf(RenderScales, current.RenderScale) + direction + RenderScales.Length) % RenderScales.Length] },
            24 => current with { DynamicResolutionFps = Cycle(DynamicResolutionTargets, current.DynamicResolutionFps, direction) },
        _ => current
    };

    private static (string Name, string Value) Split(string label)
    {
        var colon = label.IndexOf(':');
        return colon < 0 ? (label, string.Empty) : (label[..colon], label[(colon + 1)..].Trim());
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        RefreshLabels();
        painter.Sprite(WindowArt, BoxX, BoxY);
        for (var tab = 0; tab < Tabs.Length; tab++)
        {
            var bounds = Tab(tab);
            painter.Sprite(tab == _tab || bounds.Contains(pointer) ? TabLit[tab] : TabArt[tab], bounds.X, bounds.Y);
        }

        Button(painter, ResetButton, ResetArt, ResetLit, pointer);
        Button(painter, OkButton, SaveArt, SaveLit, pointer);
        Button(painter, CloseButton, CloseArt, CloseLit, pointer);
        var lines = Tabs[_tab];
        for (var line = 0; line < lines.Length; line++)
        {
            DrawLine(painter, lines[line], Row(line), line, pointer);
        }

        var hasMessage = _settings.Message.Length > 0;
        var note = hasMessage ? ClientText.Get("Settings.NeedsAttention") : _settings.EffectiveDisplay;
        painter.Text(painter.Fit(note, RowsWidth), BoxX + RowsLeft, BoxY + NoteTop, hasMessage ? Color.Orange : HudTextColors.Hint);
        if (_dropLine >= 0)
        {
            painter.Flush();
            DrawList(painter, pointer);
        }
    }

    private static void Button(HudPainter painter, Rectangle bounds, string art, string lit, Point pointer) =>
        painter.Sprite(bounds.Contains(pointer) ? lit : art, bounds.X, bounds.Y);

    private void DrawLine(HudPainter painter, Line line, Rectangle bounds, int index, Point pointer)
    {
        if (line.Row == Bar)
        {
            var barX = bounds.X + (RowsWidth - BarWidth) / 2;
            painter.Stretch(BarArt, new Rectangle(barX, bounds.Y, BarWidth, RowHeight));
            painter.Sprite(line.Heading, barX + HeadingOffset, bounds.Y + 2);
            return;
        }

        var hovered = _dropLine < 0 && bounds.Contains(pointer);
        if (hovered)
        {
            painter.Fill(bounds, HoverColor);
        }

        painter.Text(painter.Fit(_names[line.Row], RowsWidth - ControlWidth - 8), bounds.X + 6, bounds.Y + 1);
        var control = Control(bounds);
        DrawDrop(painter, control, index == _dropLine ? DropOpen : hovered ? DropLit : DropArt);
        painter.Text(painter.Fit(_values[line.Row], ControlWidth - DropArrowWidth - 10), control.X + 6, control.Y + 1);
    }

    // The closed box: its arrow end at its width, the rest stretched.
    private static void DrawDrop(HudPainter painter, Rectangle bounds, string art)
    {
        var body = bounds.Width - DropArrowWidth;
        painter.Stretch(art, new Rectangle(bounds.X, bounds.Y, body, bounds.Height), new Rectangle(0, 0, DropWidth - DropArrowWidth, DropHeight));
        painter.Stretch(art, new Rectangle(bounds.X + body, bounds.Y, DropArrowWidth, bounds.Height), new Rectangle(DropWidth - DropArrowWidth, 0, DropArrowWidth, DropHeight));
    }

    private void DrawList(HudPainter painter, Point pointer)
    {
        for (var index = 0; index < _options.Count; index++)
        {
            var bounds = OptionBounds(index);
            painter.ColorBox(bounds, 0, 0, 0, 255);
            painter.Stretch(index == _options.Count - 1 ? ListEnd : ListArt, bounds);
            if (bounds.Contains(pointer))
            {
                painter.Fill(bounds, HoverColor);
            }

            painter.Text(painter.Fit(_optionTexts[index], ControlWidth - 12), bounds.X + 6, bounds.Y + 1);
        }
    }

    private ClientSettings NextResolution(ClientSettings current, int direction)
    {
        var sizes = Resolutions(current.WindowMode, _settings.DisplayModes, _settings.Desktop);
        if (sizes.Count == 0)
        {
            return current;
        }

        var index = sizes.IndexOf(new Point(current.WindowWidth, current.WindowHeight));
        var next = index < 0 && direction < 0 ? sizes[^1] : sizes[(index + direction + sizes.Count) % sizes.Count];
        return current with { WindowWidth = next.X, WindowHeight = next.Y };
    }

    // The sizes a mode offers, smallest first, all from the modes the monitor reports: fullscreen any of them; a window
    // those that fit on the desktop below its title bar; borderless none, since it always covers the desktop.
    internal static List<Point> Resolutions(WindowMode mode, IReadOnlyList<DisplayModeOption> modes, DisplayModeOption desktop)
    {
        var sizes = new List<Point>();
        if (mode == WindowMode.Borderless)
        {
            return sizes;
        }

        foreach (var option in modes)
        {
            var size = new Point(option.Width, option.Height);
            var fits = mode == WindowMode.Fullscreen || option.Width <= desktop.Width && option.Height <= desktop.Height - WindowPlacement.TitleBar;
            if (fits && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        sizes.Sort(static (left, right) => ((long)left.X * left.Y).CompareTo((long)right.X * right.Y));
        return sizes;
    }

    private int NextFrequency(ClientSettings current, int direction)
    {
        if (current.WindowMode != WindowMode.Fullscreen)
        {
            return 0;
        }

        var frequencies = new List<int> { 0 };
        foreach (var mode in _settings.DisplayModes)
        {
            if ((current.Monitor == -1 || mode.Monitor == current.Monitor)
                && mode.Width == current.WindowWidth && mode.Height == current.WindowHeight && !frequencies.Contains(mode.RefreshRate))
            {
                frequencies.Add(mode.RefreshRate);
            }
        }

        frequencies.Sort();
        return Cycle(frequencies, current.RefreshRate, direction);
    }

    private static GraphicsQuality NextPreset(GraphicsQuality quality, int direction)
    {
        var index = Array.IndexOf(Presets, quality);
        if (index < 0)
        {
            return direction > 0 ? Presets[0] : Presets[^1];
        }

        return Presets[(index + direction + Presets.Length) % Presets.Length];
    }

    private static int Cycle(IReadOnlyList<int> values, int value, int direction)
    {
        var index = 0;
        for (var candidate = 0; candidate < values.Count; candidate++)
        {
            if (values[candidate] == value)
            {
                index = candidate;
                break;
            }
        }

        return values[(index + direction + values.Count) % values.Count];
    }

    private void RefreshLabels()
    {
        var settings = _settings.Requested;
        if (_labelSettings == settings)
        {
            return;
        }

        _labelSettings = settings;
        FillLabels(_labels, settings);
        for (var row = 0; row < RowCount; row++)
        {
            (_names[row], _values[row]) = Split(_labels[row]);
        }
    }

    private static void FillLabels(string[] labels, ClientSettings settings)
    {
        labels[0] = ClientText.Format("Settings.Window", ClientText.Get("Settings.WindowMode." + settings.WindowMode));
        labels[1] = settings.Monitor < 0 ? ClientText.Get("Settings.MonitorCurrent") : ClientText.Format("Settings.Monitor", settings.Monitor);
        labels[2] = settings.WindowMode == WindowMode.Borderless
            ? ClientText.Get("Settings.ResolutionDesktop")
            : ClientText.Format("Settings.Resolution", settings.WindowWidth, settings.WindowHeight);
        labels[3] = settings.WindowMode != WindowMode.Fullscreen ? ClientText.Get("Settings.FrequencyDesktop") : ClientText.Format("Settings.Frequency", settings.RefreshRate);
        labels[4] = ClientText.Get(settings.VerticalSync ? "Settings.VSyncOn" : "Settings.VSyncOff");
        labels[5] = ClientText.Get(settings.MusicEnabled ? "Settings.MusicOn" : "Settings.MusicOff");
        labels[6] = ClientText.Get(settings.SoundEnabled ? "Settings.SoundOn" : "Settings.SoundOff");
        labels[7] = settings.MaxFps == 0 ? ClientText.Get("Settings.MaxFpsUnlimited") : ClientText.Format("Settings.MaxFps", settings.MaxFps);
        labels[8] = settings.BackgroundMaxFps == 0
            ? ClientText.Get("Settings.BackgroundFpsForeground")
            : ClientText.Format("Settings.BackgroundFps", settings.BackgroundMaxFps);
        labels[9] = settings.AntiAliasing == AntiAliasingMode.Msaa
            ? ClientText.Format("Settings.AntiAliasingMsaa", Math.Max(settings.MultiSampleCount, 2))
            : settings.AntiAliasing == AntiAliasingMode.Off
                ? ClientText.Get("Settings.AntiAliasingOff")
                : ClientText.Format("Settings.AntiAliasing", settings.AntiAliasing.ToString().ToUpperInvariant());
        labels[10] = ClientText.Format("Settings.SunShadow", settings.ShadowMapSize);
        labels[11] = ClientText.Format("Settings.Quality", ClientText.Get("Settings.Quality." + settings.Quality));
        labels[12] = settings.ShadowUpdateRate == 0
            ? ClientText.Get("Settings.ShadowAnimationEveryFrame")
            : ClientText.Format("Settings.ShadowAnimation", settings.ShadowUpdateRate);
        labels[13] = ClientText.Get(settings.ShadowsEnabled ? "Settings.ShadowsOn" : "Settings.ShadowsOff");
        labels[14] = ClientText.Get(settings.ClassicTargetHealthBar ? "Settings.TargetHealthClassic" : "Settings.TargetHealthAlways");
        labels[15] = ClientText.Get(settings.ClassicNamePlates ? "Settings.NamePlatesClassic" : "Settings.NamePlatesCompact");
        labels[16] = ClientText.Get(settings.WeatherEnabled ? "Settings.WeatherOn" : "Settings.WeatherOff");
        labels[17] = ClientText.Get(settings.RealisticSky ? "Settings.SkyRealistic" : "Settings.SkyClassic");
        labels[18] = ClientText.Format("Settings.Clouds", ClientText.Get("Settings.Clouds." + settings.CloudQuality));
        labels[19] = settings.PetPicksUpPotions ? "Pet pickup: gold and potions" : "Pet pickup: gold only";
        labels[20] = ClientText.Format("Settings.Language", ClientText.Get("Language." + settings.Language));
        labels[21] = ClientText.Get(settings.ClassicTargetFrame ? "Settings.TargetFrameClassic" : "Settings.TargetFrameNew");
        labels[22] = ClientText.Get(settings.DepthOfField ? "Settings.DepthOfFieldOn" : "Settings.DepthOfFieldOff");
        labels[23] = ClientText.Format("Settings.RenderScale", (int)MathF.Round(settings.RenderScale * 100f));
        labels[24] = settings.DynamicResolutionFps == 0
            ? ClientText.Get("Settings.DynamicResolutionOff")
            : ClientText.Format("Settings.DynamicResolution", settings.DynamicResolutionFps);
    }

    private static string NextLanguage(string language, int direction)
    {
        var languages = ClientSettings.SupportedLanguages;
        var index = 0;
        for (var candidate = 0; candidate < languages.Count; candidate++)
        {
            if (string.Equals(languages[candidate], language, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        return languages[(index + direction + languages.Count) % languages.Count];
    }
}
