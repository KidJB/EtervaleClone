using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.UI.Widgets;

// Drop names over the scene: the name box 36 pixels above the drop's point, the text 9 pixels in at the HUD text size,
// its capitals centred with room for descenders (HudPainter.NameBoxLineAt). Labels come nearest first; each keeps its
// spot unless it would cover a label already placed, and then rises just above it, so a pile of drops reads as a column
// of names. Labels are moved by whole pixels, not snapped to a grid, and come in an order kept from frame to frame, so
// the column follows the pile without reshuffling.
// A box that would leave the screen is not drawn. Labels draw before the monster and NPC names, which cover them; the
// text is lavender, brighter under the cursor; gear of Uncommon grade and above takes its grade's colour (HudRarityColors).
public sealed class HudGroundLabels
{
    private const int Lift = 36;
    private const int Gap = 2;
    private const int PadY = 1;
    private static readonly Color Named = new(150, 144, 206);
    private static readonly Color Hovered = new(196, 186, 255);
    private readonly List<Rectangle> _placed = [];

    public void Draw(HudPainter painter, HudLayout layout, IReadOnlyList<GroundLabel> labels, Action<Guid, Rectangle> placed)
    {
        var scale = Math.Max(1, layout.Scale);
        var (line, textTop) = painter.NameBoxText(HudPainter.TextScale, PadY);
        _placed.Clear();
        foreach (var label in labels)
        {
            var size = HudPainter.NameBoxExtent(painter.Measure(label.Text), line, 1);
            var natural = new Rectangle((int)(label.Anchor.X / scale) - size.X / 2, (int)(label.Anchor.Y / scale) - Lift, size.X, size.Y);
            var box = Stack(natural, CollectionsMarshal.AsSpan(_placed));
            if (box.X < 0 || box.Y < 0 || box.Right > layout.Width || box.Bottom > layout.Height)
            {
                continue;
            }

            _placed.Add(box);
            painter.DrawNameBox(box);
            painter.Text(label.Text, box.X + HudPainter.NameBoxSide, box.Y + textTop, label.Rarity >= Simulation.Items.ItemRarity.Uncommon ? HudRarityColors.Of(label.Rarity, label.Hovered) : label.Hovered ? Hovered : Named,
                HudPainter.TextScale);
            placed?.Invoke(label.Id, new Rectangle(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale));
        }
    }

    // The loot light of each graded drop, in its grade's colour: a glow pool on the ground (the grade's name-glow art) and,
    // for Rare and above, a pillar of light of a fixed size on screen per grade (PillarPixels), so zooming the camera moves
    // the light with its drop but never resizes it. The pillar is BeamLayers nested columns, the
    // outer wide and faint, the inner narrow and nearly white, each fading from the ground up; the base burns brightest.
    // A drop coming into view flashes: the pool swells and a ring spreads out over FlashMs. Every light breathes slowly.
    public static void DrawBeams(HudPainter painter, HudLayout layout, IReadOnlyList<GroundBeam> beams, double nowMs)
    {
        var scale = Math.Max(1, layout.Scale);
        var pulse = 0.85f + 0.15f * MathF.Sin((float)(nowMs * BeamPulsePerMs));
        for (var index = 0; index < beams.Count; index++)
        {
            var beam = beams[index];
            var color = HudRarityColors.Of(beam.Rarity);
            var hot = Color.Lerp(color, Color.White, 0.7f);
            var footY = (int)(beam.Foot.Y / scale);
            var x = (int)(beam.Foot.X / scale);
            var height = PillarPixels[(int)beam.Rarity];
            var flash = beam.AgeMs < FlashMs ? 1f - beam.AgeMs / (float)FlashMs : 0f;
            var size = PoolPixels;
            var glow = Inventory.HudItemFrame.GlowOf(beam.Rarity);

            var pool = (int)(size * (1f + flash));
            painter.Stretch(glow, new Rectangle(x - pool, footY - pool / 4, pool * 2, pool / 2), null, color * (pulse + flash));
            if (flash > 0f)
            {
                var ring = (int)(size * (1f + 3f * (1f - flash)));
                painter.Stretch(glow, new Rectangle(x - ring, footY - ring / 4, ring * 2, ring / 2), null, hot * (0.8f * flash));
            }

            if (height <= BeamSlices)
            {
                continue;
            }

            // The spire art tinted to the grade, a smaller and whiter copy inside it for the hot core.
            if (painter.Texture(BeamArt) is not null)
            {
                var spire = Math.Max(6, (int)(height * BeamArtAspect));
                var top = footY - height;
                painter.Stretch(BeamArt, new Rectangle(x - spire / 2, top, spire, height + spire / 10), null, color * ((0.9f + flash * 0.3f) * pulse));
                var inner = Math.Max(3, spire / 2);
                var innerTop = footY - height * 3 / 4;
                painter.Stretch(BeamArt, new Rectangle(x - inner / 2, innerTop, inner, footY - innerTop + inner / 10), null, hot * (0.7f * pulse));
                continue;
            }

            // Without the art: a slim pillar, a thin glow around a sharp core one or two pixels wide.
            var width = Math.Max(2, height / 45);
            for (var layer = 0; layer < BeamLayers; layer++)
            {
                // Outer layers are a little wider and faint, the innermost a solid, nearly white line.
                var t = layer / (float)(BeamLayers - 1);
                var half = layer == BeamLayers - 1 ? Math.Max(1, width / 3) : Math.Max(1, (int)(width * (1f - 0.6f * t)));
                var tint = Color.Lerp(color, hot, t) * ((layer == BeamLayers - 1 ? 1f : 0.10f + 0.25f * t) * pulse);
                for (var slice = 0; slice < BeamSlices; slice++)
                {
                    var y0 = footY - height * (slice + 1) / BeamSlices;
                    var y1 = footY - height * slice / BeamSlices;
                    var rise = (float)slice / BeamSlices;
                    painter.Fill(new Rectangle(x - half, y0, half * 2, y1 - y0), tint * (Math.Min(1f, (1f - rise) * 1.3f) + flash * 0.5f));
                }
            }
        }
    }

    // Pillar height on screen by grade (none for None, Common and Uncommon) and the ground glow's half width, in pixels.
    private static readonly int[] PillarPixels = [0, 0, 0, 110, 150, 200];
    private const int PoolPixels = 16;

    // The loot pillar's spire (white, tinted per grade); its width is this share of its height.
    public const string BeamArt = "Hud/LootBeam/Item_Drop_Light_Beam.png";
    private const float BeamArtAspect = 0.35f;

    private const int BeamSlices = 20;
    private const int BeamLayers = 4;
    private const long FlashMs = 600;

    // One breath every two seconds.
    private const double BeamPulsePerMs = Math.PI / 1000.0;

    // The box raised just above every placed box it would cover, Gap pixels apart; each rise moves it strictly up, so the
    // search ends.
    public static Rectangle Stack(Rectangle box, ReadOnlySpan<Rectangle> placed)
    {
        for (var index = 0; index < placed.Length; index++)
        {
            var guard = placed[index];
            guard.Inflate(0, Gap);
            if (box.Intersects(guard))
            {
                box.Y = guard.Top - box.Height;
                index = -1;
            }
        }

        return box;
    }
}
