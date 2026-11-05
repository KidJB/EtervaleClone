using System.Globalization;
using PristonTale.Application.Chat;

namespace PristonTale.Application.GameMasters;

// The coin shop's game-master commands: grant or take an online player's account coins, and re-read the shop's file.
internal static class GmCoinCommands
{
    // /@coins name amount: a negative amount takes coins (never below zero). Logged with the game master's name.
    public static async Task Grant(GmCommandContext context)
    {
        var name = context.Word();
        var target = name.Length == 0 ? null : context.Commands.Directory.FindByName(name);
        if (target is null || context.Commands.Coins is null
            || !long.TryParse(context.Word(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) || amount == 0)
        {
            context.Reply("Usage: /@coins <online name> <amount> (negative takes coins)");
            return;
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"game master {context.Issuer.Name} gave {amount} to {target.Name}");
        var balance = await context.Commands.Coins.AddAsync(target.Session.AccountId, amount, reason);
        context.Reply(balance is { } coins
            ? string.Create(CultureInfo.InvariantCulture, $"{target.Name} now has {coins} coins")
            : string.Create(CultureInfo.InvariantCulture, $"{target.Name} does not have {-amount} coins"));
    }

    public static Task Reload(GmCommandContext context)
    {
        context.Reply(context.Commands.CoinShop?.Reload() ?? "no coin shop on this server");
        return Task.CompletedTask;
    }
}
