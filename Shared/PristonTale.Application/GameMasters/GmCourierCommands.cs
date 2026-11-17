using System.Globalization;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.GameMasters;

// The Quest Couriers' game-master commands: reload data/couriers.json, see and reset a player's hunts, add and edit quests
// (edits are written back to the file and apply at once).
internal static class GmCourierCommands
{
    // /@courier_reload
    public static Task Reload(GmCommandContext context)
    {
        context.Reply(context.Commands.Couriers?.Reload() ?? "no Courier catalog on this server");
        return Task.CompletedTask;
    }

    // /courier_progress <character>
    public static async Task Progress(GmCommandContext context)
    {
        var name = context.Word();
        context.Reply(await context.Commands.World.CourierProgressAsync(name) ?? $"{name} is not in the world");
    }

    // /@courier_reset <character> [all|daily|midnight|onetime|repeatable|weekly]: all clears accepted hunts and records; a
    // category clears only its turn-in records, so its quests may be taken again.
    public static async Task Reset(GmCommandContext context)
    {
        var name = context.Word();
        var which = context.Word();
        CourierCategory? category = which.Length == 0 || which.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : Enum.TryParse<CourierCategory>(which, true, out var parsed) ? parsed : (CourierCategory)255;
        if (category == (CourierCategory)255)
        {
            context.Reply("categories: all, daily, midnight, onetime, repeatable, weekly");
            return;
        }

        context.Reply(await context.Commands.World.ResetCourierQuestsAsync(name, category)
            ? $"{name}'s Courier hunts reset ({category?.ToString() ?? "all"})"
            : $"{name} is not in the world");
    }

    // /@courier_new <id> <courier npc code> <monster id> <count> <experience> <min level> <max level> <name...>: a Daily
    // Normal hunt of one monster at that Courier, in its field.
    public static Task New(GmCommandContext context)
    {
        if (context.Commands.Couriers is not { } store)
        {
            context.Reply("no Courier catalog on this server");
            return Task.CompletedTask;
        }

        var numbers = new int[7];
        for (var index = 0; index < numbers.Length; index++)
        {
            if (!int.TryParse(context.Word(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[index]))
            {
                context.Reply("usage: /@courier_new <id> <courier code> <monster id> <count> <experience> <min level> <max level> <name>");
                return Task.CompletedTask;
            }
        }

        var post = store.Current.CourierAt(numbers[1]);
        var monster = context.Commands.WorldData.Current.Monsters.TryGet(numbers[2], out var definition) ? definition.Name : null;
        if (post is null || monster is null)
        {
            context.Reply(post is null ? $"no Courier {numbers[1]}" : $"no monster {numbers[2]}");
            return Task.CompletedTask;
        }

        var name = context.Rest.Trim();
        var quest = new CourierQuest(numbers[0], name.Length > 0 ? name : $"Hunt {numbers[0]}", "", CourierCategory.Daily, CourierDifficulty.Normal,
            post.FieldCode, post.NpcCode, post.NpcCode, numbers[5], numbers[6], [new CourierObjective(numbers[2], monster, numbers[3])], numbers[4]);
        context.Reply(store.Replace(quest));
        return Task.CompletedTask;
    }

    // /@courier_set <id> <exp|gold|level|category|difficulty|enabled|count|cooldown|name> <value...>
    public static Task Set(GmCommandContext context)
    {
        if (context.Commands.Couriers is not { } store || !int.TryParse(context.Word(), out var id) || store.Current.Get(id) is not { } quest)
        {
            context.Reply("usage: /@courier_set <quest id> <exp|gold|level|category|difficulty|enabled|count|cooldown|name> <value>");
            return Task.CompletedTask;
        }

        var field = context.Word().ToLowerInvariant();
        var first = context.Word();
        var second = context.Word();
        var number = long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        CourierQuest? changed = field switch
        {
            "exp" when number >= 0 => quest with { Experience = number },
            "gold" when number >= 0 => quest with { Gold = number },
            "cooldown" when number >= 0 => quest with { CooldownMinutes = (int)Math.Min(number, int.MaxValue) },
            "level" when number > 0 && int.TryParse(second, out var max) => quest with { MinLevel = (int)number, MaxLevel = max },
            "category" when Enum.TryParse<CourierCategory>(first, true, out var category) => quest with { Category = category },
            "difficulty" when Enum.TryParse<CourierDifficulty>(first, true, out var difficulty) => quest with { Difficulty = difficulty },
            "enabled" => quest with { Enabled = first is "on" or "1" or "true" },
            "count" when number > 0 && number <= quest.Objectives.Count && int.TryParse(second, out var count) =>
                quest with { Objectives = [.. quest.Objectives.Select((objective, index) => index == number - 1 ? objective with { Count = count } : objective)] },
            "name" => quest with { Name = (first + " " + second + " " + context.Rest).Trim() },
            _ => null
        };
        context.Reply(changed is null ? $"cannot set {field} to '{first}'" : store.Replace(changed));
        return Task.CompletedTask;
    }
}
