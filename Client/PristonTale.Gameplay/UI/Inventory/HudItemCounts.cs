using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

// The stack counts drawn at an item's bottom-right corner (the bag, the belt, the warehouse) and the "Name (count)" its
// tooltip shows, built once per revision of the items they label so drawing allocates nothing. A single item shows no
// count.
public sealed class HudItemCounts
{
    private readonly Dictionary<Guid, string> _labels = new();
    private readonly Dictionary<Guid, OwnedItemView> _named = new();
    private long _revision = long.MinValue;
    private const float WashAlpha = 0.18f;

    public void Refresh(long revision, IReadOnlyList<OwnedItemView> items)
    {
        if (_revision == revision)
        {
            return;
        }

        _revision = revision;
        _labels.Clear();
        _named.Clear();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Count > 1)
            {
                _labels[item.InstanceId] = item.Count.ToString();
                _named[item.InstanceId] = item with { Name = $"{item.Name} ({item.Count})" };
            }
        }
    }

    public string Label(OwnedItemView item) => _labels.GetValueOrDefault(item.InstanceId, "");

    // The item as its tooltip names it: with its count when it is a stack.
    public OwnedItemView Named(OwnedItemView item) => _named.GetValueOrDefault(item.InstanceId, item);

    // The item's icon in its cells, its count over the corner; gear of Uncommon grade and above sits on a faint wash of
    // its grade's colour inside a one-pixel border of it (HudRarityColors).
    public void Draw(HudPainter painter, HudItemIcons icons, OwnedItemView item, Rectangle bounds, Color tint)
    {
        var rarity = item.Details?.Rarity ?? Simulation.Items.ItemRarity.None;
        var graded = rarity >= Simulation.Items.ItemRarity.Uncommon;
        if (graded)
        {
            painter.Fill(bounds, HudRarityColors.Of(rarity) * WashAlpha);
        }

        icons.Draw(painter, item.CatalogKey, bounds, tint);
        if (graded)
        {
            var color = HudRarityColors.Of(rarity);
            painter.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
            painter.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
            painter.Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
            painter.Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
        }

        var label = Label(item);
        if (label.Length > 0)
        {
            painter.Text(label, bounds.Right - painter.Measure(label) - 1, bounds.Bottom - 13);
        }
    }
}
