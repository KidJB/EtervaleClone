namespace PristonTale.Application.GameMasters;

// The boss arena's game-master command.
internal static class GmArenaCommands
{
    // /@arena [king|hagios]: starts the boss arena now (King Fury by default), whatever the schedule; /@arena end ends it.
    public static async Task Arena(GmCommandContext context)
    {
        var word = context.Word().ToLowerInvariant();
        context.Reply(await context.Commands.World.StartArenaNowAsync(word == "end" ? -1 : word == "hagios" ? 1 : 0));
    }
}
