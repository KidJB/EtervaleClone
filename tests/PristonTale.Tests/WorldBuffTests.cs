using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class WorldBuffTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world buffs: ten items, their rules read the shared mask, and the state packet round-trips", RulesAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(WorldBuffs.All.Count == 10 && WorldBuffs.All.All(static buff => UsableItemCatalog.TryGet(buff.ItemKey, out var rule) && rule.Action == UsableItemAction.WorldBuff),
            "a world buff has no item rule");
        var timers = new PremiumTimers { WorldBuffMask = WorldBuffs.Get((int)WorldBuffKind.Experience).Bit | WorldBuffs.Get((int)WorldBuffKind.Drop).Bit };
        Assert.True(PremiumRules.ExperiencePercent(timers) == WorldBuffs.ExperiencePercent && PremiumRules.ExtraDropChance(timers) == WorldBuffs.DropPercent, "exp and drop buffs");
        Assert.True(PremiumRules.StaminaCost(100, new PremiumTimers { WorldBuffMask = WorldBuffs.Get((int)WorldBuffKind.SpDown).Bit }) == 90, "SP down");
        Assert.True(WorldBuffs.FromArgument(WorldBuffs.Argument(WorldBuffKind.Experience, true)) == ((int)WorldBuffKind.Experience, WorldBuffs.DayDurationSeconds), "the 24-hour item");
        var buffer = new byte[WorldBuffPacket.MaxSize];
        var packet = new WorldBuffPacket([(9, 1200)]);
        Assert.True(WorldBuffPacket.Read(buffer.AsSpan(0, packet.Write(buffer))).Buffs[0] == (9, 1200), "the packet");
        return Task.CompletedTask;
    }
}
