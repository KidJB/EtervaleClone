using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

// Loads every Cabinet costume body the way the client does and lists those that fail (--verify-costumes <assets>).
internal static class CostumeProbe
{
    public static void Run(string root)
    {
        var loader = new PristonActorLoader();
        var failures = 0;
        foreach (var costume in Costumes.All)
        {
            foreach (var (playerClass, file) in costume.Bodies)
            {
                try
                {
                    loader.Load(root, Path.Combine(root, "char", "tmABCD", file));
                }
                catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
                {
                    failures++;
                    Console.WriteLine($"BROKEN\t{costume.Key}\t{playerClass}\t{costume.Name}\t{failure.Message}");
                }
            }
        }

        foreach (var gear in Costumes.Gear)
        {
            try
            {
                loader.LoadRigid(Path.Combine(root, "image", "Sinimage", "Items", "DropItem", "it" + gear.Key.ToLowerInvariant() + ".smd"));
            }
            catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
            {
                failures++;
                Console.WriteLine($"BROKEN\t{gear.Key}\tgear\t{gear.Name}\t{failure.Message}");
            }
        }

        foreach (var number in Costumes.Hair)
        {
            foreach (var playerClass in Enum.GetValues<PlayerClass>())
            {
                try
                {
                    loader.Load(root, Path.Combine(root, CharacterHead.CustomHead(playerClass, number)));
                }
                catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
                {
                    failures++;
                    Console.WriteLine($"BROKEN	KH{number}	{playerClass}	hair	{failure.Message}");
                }
            }
        }

        Assert.True(failures == 0, $"{failures} costume bodies do not load");
    }
}
