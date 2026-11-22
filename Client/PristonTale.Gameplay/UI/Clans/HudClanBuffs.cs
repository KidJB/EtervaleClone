using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Clans;

public enum HudClanBuffsActionKind
{
    None,
    Consumed,
    Activate,

    // The coin treasury pays for the buff (Argument: WorldBuffs.Argument).
    SpendCoins
}

// Item: the clan buff item a member uses; Argument: the buff the coin treasury pays for.
public readonly record struct HudClanBuffsAction(HudClanBuffsActionKind Kind, OwnedItemView Item = null, int Argument = 0);

// The Clan World Buffs tab of the Clan Warehouse window (HudClanWarehouseWindow), in the Active Buffs box's colours: each
// buff with the time the clan has left of it and its 1-hour and 24-hour clan items (CL1xx, CL2xx), lit when the player
// carries one and its rank may start clan buffs (ClanPermission.ActivateBuffs), and the same hour and day paid from the clan
// coin treasury, lit for a rank that may spend it (ClanPermission.SpendCoins) while it holds the price. Column titles head
// the rows. Drawn and clicked in the area it is given.
public sealed class HudClanBuffs
{
    public const int RowStep = 24;
    public const int TitleHeight = 22;
    private const int Tile = 21;
    private const int ButtonWidth = 34;
    private const int CoinWidth = 50;
    private const int GroupGap = 14;
    private static readonly Color IdleColor = new(140, 130, 115);

    private readonly double[] _endMs = new double[WorldBuffs.All.Count + 1];
    private readonly string[] _times = new string[WorldBuffs.All.Count + 1];
    private readonly string[] _names = new string[WorldBuffs.All.Count + 1];
    private IReadOnlyList<(int Kind, int SecondsLeft)> _from;
    private long _second = -1;

    private static readonly string HourPrice = ClanBuffs.HourCoinPrice.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string DayPrice = ClanBuffs.DayCoinPrice.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static Rectangle Row(Rectangle area, int index) => new(area.X, area.Y + TitleHeight + index * RowStep, area.Width, Tile);

    private static Rectangle HourButton(Rectangle row, bool day) => new(row.Right - (day ? ButtonWidth : ButtonWidth * 2 + 4), row.Y + 2, ButtonWidth, 17);

    private static Rectangle CoinButton(Rectangle row, bool day) =>
        new(row.Right - ButtonWidth * 2 - 4 - GroupGap - (day ? CoinWidth : CoinWidth * 2 + 4), row.Y + 2, CoinWidth, 17);

    public void Receive(IReadOnlyList<(int Kind, int SecondsLeft)> buffs, double nowMs)
    {
        if (buffs is null || ReferenceEquals(buffs, _from))
        {
            return;
        }

        _from = buffs;
        Array.Clear(_endMs);
        foreach (var (kind, secondsLeft) in buffs)
        {
            if (kind > 0 && kind < _endMs.Length)
            {
                _endMs[kind] = nowMs + secondsLeft * 1000d;
            }
        }

        _second = -1;
    }

    public HudClanBuffsAction Click(Rectangle area, Point pointer, ClanInfo clan, OwnerState state)
    {
        var bounds = area;
        if (!bounds.Contains(pointer))
        {
            return default;
        }

        for (var index = 0; index < WorldBuffs.All.Count; index++)
        {
            var buff = WorldBuffs.All[index];
            var row = Row(bounds, index);
            foreach (var day in (ReadOnlySpan<bool>)[false, true])
            {
                if (HourButton(row, day).Contains(pointer) && clan.May(ClanPermission.ActivateBuffs) && Held(state, ClanBuffs.ItemKey(buff, day)) is { } item)
                {
                    return new HudClanBuffsAction(HudClanBuffsActionKind.Activate, item);
                }

                var argument = WorldBuffs.Argument(buff.Kind, day);
                if (CoinButton(row, day).Contains(pointer) && ClanBuffs.CheckCoinSpend(clan.Rights, argument, clan.CoinTreasury) == ClanStatus.Ok)
                {
                    return new HudClanBuffsAction(HudClanBuffsActionKind.SpendCoins, Argument: argument);
                }
            }
        }

        return new HudClanBuffsAction(HudClanBuffsActionKind.Consumed);
    }

    public void Draw(HudPainter painter, Rectangle area, Point pointer, ClanInfo clan, OwnerState state, double nowMs)
    {
        Tick(nowMs);
        var bounds = area;
        var may = clan.May(ClanPermission.ActivateBuffs);
        var spend = clan.May(ClanPermission.SpendCoins);
        var first = Row(bounds, 0);
        var coins = CoinButton(first, false);
        var items = HourButton(first, false);
        painter.Text(ClientText.Get("ClanBuffs.CoinsColumn"), coins.X, bounds.Y, HudWorldBuffs.Heading);
        painter.Text(ClientText.Get("ClanBuffs.ItemsColumn"), items.X, bounds.Y, HudWorldBuffs.Heading);
        for (var index = 0; index < WorldBuffs.All.Count; index++)
        {
            var buff = WorldBuffs.All[index];
            var kind = (int)buff.Kind;
            var row = Row(bounds, index);
            painter.Stretch(HudWorldBuffs.Art[kind - 1], new Rectangle(row.X, row.Y, Tile, Tile));
            var running = _times[kind] is not null;
            painter.Text(_names[kind] ??= painter.Fit(ClanBuffs.Name(buff), 120), row.X + Tile + 6, row.Y + 3, running ? HudWorldBuffs.Name : IdleColor);
            if (running)
            {
                painter.Text(_times[kind], row.X + Tile + 128, row.Y + 3, HudWorldBuffs.Time);
            }

            HudWorldBuffs.Button(painter, CoinButton(row, false), pointer, HourPrice, spend && clan.CoinTreasury >= ClanBuffs.HourCoinPrice);
            HudWorldBuffs.Button(painter, CoinButton(row, true), pointer, DayPrice, spend && clan.CoinTreasury >= ClanBuffs.DayCoinPrice);
            HudWorldBuffs.Button(painter, HourButton(row, false), pointer, "1h", may && Held(state, ClanBuffs.ItemKey(buff, false)) is not null);
            HudWorldBuffs.Button(painter, HourButton(row, true), pointer, "24h", may && Held(state, ClanBuffs.ItemKey(buff, true)) is not null);
        }
    }

    // Each second: the time each running buff has left (empty once it ended).
    private void Tick(double nowMs)
    {
        var second = (long)(nowMs / 1000d);
        if (second == _second)
        {
            return;
        }

        _second = second;
        for (var kind = 1; kind < _endMs.Length; kind++)
        {
            var left = (int)Math.Ceiling((_endMs[kind] - nowMs) / 1000d);
            _times[kind] = left > 0 ? HudWorldBuffs.TimeLeft(left) : null;
        }
    }

    // The first carried (not worn) item of that key.
    private static OwnedItemView Held(OwnerState state, string key)
    {
        if (state is null)
        {
            return null;
        }

        foreach (var item in state.Items)
        {
            if (item.Slot < 0 && item.CatalogKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

}
