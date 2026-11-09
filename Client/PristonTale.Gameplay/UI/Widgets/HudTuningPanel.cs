using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The graphics tuning panel (F12): sliders, switches and lists over the rendering settings, applied while they move. A slider
// sends its value at most every ApplySeconds while it is dragged, then its last value on release. Revert goes back to the
// values the panel was opened with. It is a developer's tool, in English.
public sealed class HudTuningPanel(PresentationSettings settings)
{
    private const int Width = 340;
    private const int RowHeight = 18;
    private const int Top = 30;
    private const int Padding = 8;
    private const int HeaderHeight = 22;
    private const int LabelWidth = 136;
    private const int ValueWidth = 66;
    private const double ApplySeconds = 0.15;
    private static readonly Color FillColor = new(90, 150, 220);
    private static readonly Color TrackColor = new(50, 46, 42);
    private static readonly Color OnColor = new(120, 220, 130);
    private static readonly Color OffColor = new(170, 160, 150);

    private enum RowKind
    {
        Slider,
        Switch,
        List
    }

    private sealed class Row(string name, RowKind kind, float minimum, float maximum, float step, Func<ClientSettings, float> get,
        Func<ClientSettings, float, ClientSettings> set, string[] names = null, string format = "0.##")
    {
        public string Name => name;
        public RowKind Kind => kind;
        public float Minimum => minimum;
        public float Maximum => maximum;
        public float Step => step;
        public Func<ClientSettings, float> Get => get;
        public Func<ClientSettings, float, ClientSettings> Set => set;
        public string[] Names => names;
        public string Format => format;
        public float ShownValue { get; set; } = float.NaN;
        public string ShownText { get; set; } = "";
    }

    private static Row Slider(string name, float minimum, float maximum, float step, Func<ClientSettings, float> get, Func<ClientSettings, float, ClientSettings> set, string format = "0.##") =>
        new(name, RowKind.Slider, minimum, maximum, step, get, set, null, format);

    private static Row Switch(string name, Func<ClientSettings, bool> get, Func<ClientSettings, bool, ClientSettings> set) =>
        new(name, RowKind.Switch, 0f, 1f, 1f, value => get(value) ? 1f : 0f, (value, next) => set(value, next > 0.5f), ["Off", "On"]);

    private static Row List<T>(string name, Func<ClientSettings, T> get, Func<ClientSettings, T, ClientSettings> set)
        where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return new Row(name, RowKind.List, 0f, values.Length - 1, 1f, value => Array.IndexOf(values, get(value)), (value, next) => set(value, values[(int)next]), Enum.GetNames<T>());
    }

    private readonly PresentationSettings _settings = settings;
    private readonly Row[] _rows =
    [
        Slider("Exposure (stops)", -3f, 3f, 0.1f, static s => s.ExposureCompensation, static (s, v) => s with { ExposureCompensation = v }),
        Slider("Sharpening", 0f, 1f, 0.05f, static s => s.Sharpening, static (s, v) => s with { Sharpening = v }),
        Slider("Render scale", 0.5f, 1f, 0.01f, static s => s.RenderScale, static (s, v) => s with { RenderScale = v }),
        Slider("Hold FPS (0 off)", 0f, 144f, 1f, static s => s.DynamicResolutionFps, static (s, v) => s with { DynamicResolutionFps = (int)v }, "0"),
        Switch("Depth of field", static s => s.DepthOfField, static (s, v) => s with { DepthOfField = v }),
        Slider("DOF start", 0f, 3000f, 50f, static s => s.DepthOfFieldStart, static (s, v) => s with { DepthOfFieldStart = v }, "0"),
        Slider("DOF end", 500f, 9000f, 100f, static s => s.DepthOfFieldEnd, static (s, v) => s with { DepthOfFieldEnd = v }, "0"),
        Slider("DOF radius (px)", 1f, 16f, 0.5f, static s => s.DepthOfFieldRadius, static (s, v) => s with { DepthOfFieldRadius = v }),
        List<AntiAliasingMode>("Anti-aliasing", static s => s.AntiAliasing, static (s, v) => s with { AntiAliasing = v }),
        List<BloomQuality>("Bloom", static s => s.Bloom, static (s, v) => s with { Bloom = v }),
        List<TonemapperMode>("Tonemapper", static s => s.Tonemapper, static (s, v) => s with { Tonemapper = v }),
        List<DistanceFogMode>("Distance fog", static s => s.DistanceFog, static (s, v) => s with { DistanceFog = v }),
        List<ShadowFilterMode>("Shadow filter", static s => s.ShadowFilter, static (s, v) => s with { ShadowFilter = v }),
        List<PristonDebugView>("Debug view", static s => s.DebugView, static (s, v) => s with { DebugView = v }),
        Switch("Auto exposure", static s => s.AutoExposure, static (s, v) => s with { AutoExposure = v }),
        Switch("Colour grading", static s => s.ColorGrading, static (s, v) => s with { ColorGrading = v }),
        Switch("Sun shadows", static s => s.ShadowsEnabled, static (s, v) => s with { ShadowsEnabled = v }),
        Switch("Ambient occlusion", static s => s.AmbientOcclusion, static (s, v) => s with { AmbientOcclusion = v }),
        Switch("Contact shadows", static s => s.ContactShadows, static (s, v) => s with { ContactShadows = v }),
        Switch("Image-based light", static s => s.ImageBasedLighting, static (s, v) => s with { ImageBasedLighting = v }),
        Switch("Reflections (SSR)", static s => s.ScreenSpaceReflections, static (s, v) => s with { ScreenSpaceReflections = v }),
        Switch("Volumetric light", static s => s.VolumetricLight, static (s, v) => s with { VolumetricLight = v }),
        Switch("Realistic sky", static s => s.RealisticSky, static (s, v) => s with { RealisticSky = v })
    ];

    private ClientSettings _opened;
    private int _dragging = -1;
    private float _pendingValue;
    private bool _pending;
    private double _appliedAt;

    public bool Open { get; private set; }
    public bool Dragging => _dragging >= 0;

    private int RowsTop => Top + HeaderHeight;

    private Rectangle Bounds(HudLayout layout) => new(layout.Width - Width - 10, Top, Width, HeaderHeight + _rows.Length * RowHeight + Padding);

    private Rectangle RowBounds(HudLayout layout, int index)
    {
        var bounds = Bounds(layout);
        return new Rectangle(bounds.X + Padding, RowsTop + index * RowHeight, bounds.Width - Padding * 2, RowHeight);
    }

    private static Rectangle Control(Rectangle row) => new(row.X + LabelWidth, row.Y + 4, row.Width - LabelWidth - ValueWidth, RowHeight - 8);

    private Rectangle Revert(HudLayout layout)
    {
        var bounds = Bounds(layout);
        return new Rectangle(bounds.Right - 62, bounds.Y + 3, 54, HeaderHeight - 6);
    }

    // A value from where the pointer lies along a slider's track, on its step.
    private static float ValueAt(Row row, Rectangle track, int x)
    {
        var fraction = MathHelper.Clamp((x - track.X) / (float)Math.Max(1, track.Width), 0f, 1f);
        var value = row.Minimum + fraction * (row.Maximum - row.Minimum);
        return MathHelper.Clamp(MathF.Round(value / row.Step) * row.Step, row.Minimum, row.Maximum);
    }

    public void Toggle()
    {
        Open = !Open;
        _dragging = -1;
        _opened = _settings.Requested;
    }

    public void Hide()
    {
        Open = false;
        _dragging = -1;
    }

    public bool Contains(HudLayout layout, Point pointer) => Open && (Dragging || Bounds(layout).Contains(pointer));

    public void Update(HudLayout layout, Point pointer, MouseState mouse, MouseState previous, double nowSeconds)
    {
        if (!Open)
        {
            return;
        }

        if (mouse.LeftButton == ButtonState.Released)
        {
            if (_dragging >= 0 && _pending)
            {
                Apply(_rows[_dragging], _pendingValue);
            }

            _dragging = -1;
            _pending = false;
            return;
        }

        if (previous.LeftButton == ButtonState.Released)
        {
            Press(layout, pointer, nowSeconds);
        }

        if (_dragging < 0)
        {
            return;
        }

        var row = _rows[_dragging];
        var value = ValueAt(row, Control(RowBounds(layout, _dragging)), pointer.X);
        if (value == row.Get(_settings.Requested))
        {
            _pending = false;
        }
        else if (nowSeconds - _appliedAt >= ApplySeconds)
        {
            Apply(row, value);
            _pending = false;
            _appliedAt = nowSeconds;
        }
        else
        {
            _pendingValue = value;
            _pending = true;
        }
    }

    private void Press(HudLayout layout, Point pointer, double nowSeconds)
    {
        if (Revert(layout).Contains(pointer))
        {
            _settings.Requested = _opened;
            return;
        }

        for (var index = 0; index < _rows.Length; index++)
        {
            var bounds = RowBounds(layout, index);
            if (!bounds.Contains(pointer))
            {
                continue;
            }

            var row = _rows[index];
            if (row.Kind == RowKind.Slider)
            {
                _dragging = Control(bounds).Contains(pointer) ? index : -1;
                _appliedAt = nowSeconds - ApplySeconds;
            }
            else
            {
                Apply(row, (row.Get(_settings.Requested) + 1f) % (row.Maximum + 1f));
            }

            return;
        }
    }

    private void Apply(Row row, float value) => _settings.Requested = row.Set(_settings.Requested, value) with { Quality = GraphicsQuality.Custom };

    public void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (!Open)
        {
            return;
        }

        var bounds = Bounds(layout);
        HudPteuWindow.Frame(painter, bounds);
        painter.Text("Graphics tuning (F12)", bounds.X + Padding, bounds.Y + 3, HudPteuWindow.SectionColor);
        var revert = Revert(layout);
        HudPteuWindow.Panel(painter, revert);
        painter.TextCentered("Revert", revert.X, revert.Right, revert.Y, revert.Contains(pointer) ? HudPteuWindow.SectionColor : HudPteuWindow.TextColor);
        var current = _settings.Requested;
        for (var index = 0; index < _rows.Length; index++)
        {
            DrawRow(painter, _rows[index], RowBounds(layout, index), current, pointer);
        }
    }

    private static void DrawRow(HudPainter painter, Row row, Rectangle bounds, ClientSettings current, Point pointer)
    {
        if (bounds.Contains(pointer))
        {
            painter.Fill(bounds, HudPteuWindow.HoverColor);
        }

        var value = row.Get(current);
        painter.Text(row.Name, bounds.X + 2, bounds.Y, HudPteuWindow.TextColor);
        var control = Control(bounds);
        if (row.Kind == RowKind.Slider)
        {
            painter.Fill(control, TrackColor);
            var fraction = (value - row.Minimum) / (row.Maximum - row.Minimum);
            painter.Fill(new Rectangle(control.X, control.Y, (int)(control.Width * MathHelper.Clamp(fraction, 0f, 1f)), control.Height), FillColor);
        }

        if (value != row.ShownValue)
        {
            row.ShownValue = value;
            row.ShownText = row.Names is { } names ? names[(int)value] : value.ToString(row.Format, CultureInfo.InvariantCulture);
        }

        var color = row.Kind == RowKind.Switch ? value > 0.5f ? OnColor : OffColor : HudPteuWindow.TextColor;
        if (row.Kind == RowKind.Slider)
        {
            painter.Text(row.ShownText, bounds.Right - ValueWidth + 6, bounds.Y, color);
        }
        else
        {
            painter.Text(painter.Fit(row.ShownText, bounds.Width - LabelWidth), bounds.X + LabelWidth, bounds.Y, color);
        }
    }
}
