namespace PristonTale.Application.GameMasters;

// The boss arena's game-master command.
internal static class GmArenaCommands
{
    // /@worldbuff [seconds] [kind 1-10]: starts one world buff or all of them (one hour by default); 0 seconds ends them.
    public static async Task WorldBuff(GmCommandContext context)
    {
        var seconds = int.TryParse(context.Word(), out var parsed) ? parsed : Simulation.World.WorldBuffs.DurationSeconds;
        var kind = int.TryParse(context.Word(), out var which) ? which : 0;
        context.Reply(await context.Commands.World.SetWorldBuffsAsync(kind, seconds));
    }

    // /@arena [king|hagios]: starts the boss arena now (King Fury by default), whatever the schedule; /@arena end ends it.
    public static async Task Arena(GmCommandContext context)
    {
        var word = context.Word().ToLowerInvariant();
        context.Reply(await context.Commands.World.StartArenaNowAsync(word == "end" ? -1 : word == "hagios" ? 1 : 0));
    }
}
