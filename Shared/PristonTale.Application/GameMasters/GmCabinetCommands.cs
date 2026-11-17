using System.Globalization;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.GameMasters;

// The Cabinet's game-master commands: reload and edit data/cabinet.json (a look's price, rarity, sources, availability,
// description and token; drops), give, list and reset an online player's looks, and open, list and end bidding deals.
internal static class GmCabinetCommands
{
    // /@cabinet_reload
    public static Task Reload(GmCommandContext context)
    {
        context.Reply(context.Commands.Cabinet?.Reload() ?? "no Cabinet settings on this server");
        return Task.CompletedTask;
    }

    // /@cabinet_set <look key> <price|rarity|sources|enabled|description|token> <value...>
    public static Task Set(GmCommandContext context)
    {
        const string usage = "usage: /@cabinet_set <look> <price|rarity|sources|enabled|description|token> <value> (sources: Coins,MonsterDrop,BossDrop,Quest,Event,Brush,Deal)";
        if (context.Commands.Cabinet is not { } store)
        {
            context.Reply("no Cabinet settings on this server");
            return Task.CompletedTask;
        }

        var key = context.Word();
        var field = context.Word().ToLowerInvariant();
        var value = context.Phrase();
        if (CabinetLooks.Find(key, context.Commands.WorldData.Current.Items, store.Current) is not { } look)
        {
            context.Reply($"'{key}' is no look of The Cabinet");
            return Task.CompletedTask;
        }

        var rule = store.Current.Rule(look.Key) ?? new CosmeticRule(look.Key);
        try
        {
            rule = field switch
            {
                "price" => rule with { Price = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) },
                "rarity" => rule with { Rarity = Enum.Parse<ItemRarity>(value, true) },
                "sources" => rule with { Sources = CabinetRules.ParseSources(value) },
                "enabled" => rule with { Enabled = bool.Parse(value) },
                "description" => rule with { Description = value },
                "token" => rule with { Token = value.ToUpperInvariant() },
                _ => throw new FormatException(usage)
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            context.Reply(exception.Message == usage ? usage : $"{exception.Message} {usage}");
            return Task.CompletedTask;
        }

        context.Reply(store.Replace(rule) + (field == "token" ? " (the token item exists after a restart)" : ""));
        return Task.CompletedTask;
    }

    // /@cabinet_drop <token or brush key> <monster code|boss> <chance per 10000 (0 removes)>
    public static Task Drop(GmCommandContext context)
    {
        if (context.Commands.Cabinet is not { } store)
        {
            context.Reply("no Cabinet settings on this server");
            return Task.CompletedTask;
        }

        var item = context.Word().ToUpperInvariant();
        var from = context.Word();
        var bosses = from.Equals("boss", StringComparison.OrdinalIgnoreCase);
        var monster = 0;
        if (!bosses && !int.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out monster)
            || !int.TryParse(context.Word(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var chance))
        {
            context.Reply("usage: /@cabinet_drop <token or brush key> <monster code|boss> <chance per 10000, 0 removes>");
            return Task.CompletedTask;
        }

        context.Reply(store.SetDrop(new CosmeticDrop(item, monster, bosses, chance)));
        return Task.CompletedTask;
    }

    // /@cabinet_unlock <online name> <look key> [source]: gives the look to the player's account (by default as an event's).
    public static async Task Unlock(GmCommandContext context)
    {
        var target = context.Commands.Directory.FindByName(context.Word());
        var key = context.Word();
        var sourceText = context.Word();
        if (target is null || context.Commands.Coins is not { } coins)
        {
            context.Reply("usage: /@cabinet_unlock <online name> <look> [Quest|Event|...]");
            return;
        }

        if (CabinetLooks.Find(key, context.Commands.WorldData.Current.Items, context.Commands.World.Cabinet) is not { } look)
        {
            context.Reply($"'{key}' is no look of The Cabinet");
            return;
        }

        CosmeticSource source;
        try
        {
            source = sourceText.Length == 0 ? CosmeticSource.Event : CabinetRules.ParseSources(sourceText);
        }
        catch (FormatException exception)
        {
            context.Reply(exception.Message);
            return;
        }

        var unlocked = await coins.UnlockSkinAsync(new OwnedSkinRecord(target.Session.AccountId, look.Key, context.Commands.World.UtcNow.ToUnixTimeSeconds(),
            (short)source, target.Session.Loaded?.Id ?? 0));
        context.Reply(unlocked ? $"{look.Name} unlocked for {target.Name}'s account" : $"{target.Name}'s account owns {look.Name} already");
    }

    // /cabinet_owned <online name>
    public static async Task Owned(GmCommandContext context)
    {
        var target = context.Commands.Directory.FindByName(context.Word());
        if (target is null || context.Commands.Coins is not { } coins)
        {
            context.Reply("usage: /cabinet_owned <online name>");
            return;
        }

        var records = await coins.SkinRecordsAsync(target.Session.AccountId);
        var shown = records.Take(30).Select(static record => string.Create(CultureInfo.InvariantCulture,
            $"{record.ItemKey} ({CabinetRules.SourceText((CosmeticSource)record.Source)}, {DateTimeOffset.FromUnixTimeSeconds(record.UnlockedAt):yyyy-MM-dd})"));
        context.Reply(string.Create(CultureInfo.InvariantCulture, $"{target.Name}'s account owns {records.Count} looks: ") + string.Join(", ", shown)
            + (records.Count > 30 ? " ..." : ""));
    }

    // /@cabinet_reset <online name>: takes every look from the player's account and clears the character's chosen looks.
    public static async Task Reset(GmCommandContext context)
    {
        var target = context.Commands.Directory.FindByName(context.Word());
        if (target is null || context.Commands.Coins is not { } coins)
        {
            context.Reply("usage: /@cabinet_reset <online name>");
            return;
        }

        var taken = await coins.ResetSkinsAsync(target.Session.AccountId);
        await context.Commands.World.ApplySkinsAsync(target.Session, SkinSet.None);
        context.Reply(string.Create(CultureInfo.InvariantCulture, $"{taken} looks taken from {target.Name}'s account"));
    }

    // /@deal_new <look key> <start bid> <bid step> <hours> [min level]
    public static async Task NewDeal(GmCommandContext context)
    {
        if (context.Commands.Deals is not { } deals)
        {
            context.Reply("no bidding deals on this server");
            return;
        }

        var key = context.Word();
        if (!long.TryParse(context.Word(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            || !long.TryParse(context.Word(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)
            || !double.TryParse(context.Word(), NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
        {
            context.Reply("usage: /@deal_new <look> <start bid> <bid step> <hours> [min level]");
            return;
        }

        var levelText = context.Word();
        var minLevel = levelText.Length == 0 ? 0 : int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : -1;
        var (id, problem) = await deals.CreateAsync(key, start, step, hours, minLevel, context.Issuer.Name);
        context.Reply(problem ?? string.Create(CultureInfo.InvariantCulture, $"deal {id} opened for {key} at {start} coins for {hours} hours"));
    }

    // /@deal_end <id>: closes the deal now; its leader receives the look.
    public static async Task EndDeal(GmCommandContext context)
    {
        if (context.Commands.Deals is not { } deals || !long.TryParse(context.Word(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            context.Reply("usage: /@deal_end <id>");
            return;
        }

        context.Reply(await deals.EndAsync(id) is { } closed
            ? closed.Awarded ? $"deal {id} ended: {closed.Deal.TopName} wins {closed.Deal.ItemKey}"
            : closed.Refunded ? $"deal {id} ended: {closed.Deal.TopName} owned the look and got the coins back"
            : $"deal {id} ended without bids"
            : $"deal {id} is not open");
    }

    // /deals
    public static async Task ListDeals(GmCommandContext context)
    {
        if (context.Commands.Deals is not { } deals)
        {
            context.Reply("no bidding deals on this server");
            return;
        }

        var open = await deals.OpenAsync();
        context.Reply(open.Count == 0 ? "no open deals" : string.Join(" | ", open.Select(static deal => string.Create(CultureInfo.InvariantCulture,
            $"{deal.Id} {deal.ItemKey}: {(deal.TopAccountId == 0 ? "no bid" : $"{deal.TopBid} by {deal.TopName}")}, ends {DateTimeOffset.FromUnixTimeSeconds(deal.EndsAt):MM-dd HH:mm} UTC"))));
    }
}
