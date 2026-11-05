using PristonTale.Engine3D.Formats;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Mounts: the table and its items, owning, picking and riding with the speed it adds, the ride command and its refusals in
// the world, the messages that carry them, and the later model layouts their models use.
internal static class MountTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("mounts have unique ids and items, a speed per rarity, and their items add them to the stable", TableAsync);
        yield return ("a character owns, picks and rides a mount: riding adds its speed and the stable is the account's", CharacterAsync);
        yield return ("the mount command toggles riding, picks an owned mount and gets off, and refuses a mount not owned", WorldAsync);
        yield return ("the ridden mount travels in a player's looks and the stable in the pet collection", WireAsync);
        yield return ("mount models of the later layouts read with their vertices and skeleton, their bind-pose vertices carried into bone space, and every class has its riding poses", ModelsAsync);
    }

    private static Task TableAsync()
    {
        var mounts = Mounts.All;
        Assert.True(mounts.Count is > 0 and <= Mounts.Max && mounts.Select(mount => mount.Id).SequenceEqual(Enumerable.Range(1, mounts.Count)),
            "mount ids are not 1 to the table's size");
        Assert.True(mounts.Select(mount => mount.ItemCode).Distinct().Count() == mounts.Count, "two mounts share an item");
        Assert.True(mounts.All(mount => mount.SaddleHeight > 0f && mount.Scale > 0f && mount.Model.EndsWith(".inx", StringComparison.OrdinalIgnoreCase)),
            "a mount's model, scale or saddle height is odd");
        Assert.True(Mounts.SpeedOf(MountRarity.Common) < Mounts.SpeedOf(MountRarity.Rare) && Mounts.SpeedOf(MountRarity.Rare) < Mounts.SpeedOf(MountRarity.Epic)
            && Mounts.SpeedOf(MountRarity.Epic) < Mounts.SpeedOf(MountRarity.Legendary), "a rarer mount is not faster");
        foreach (var mount in mounts)
        {
            Assert.True(UsableItemCatalog.TryGet(mount.ItemKey, out var rule) && rule.Action == UsableItemAction.Mount && rule.Argument == mount.Id
                && Mounts.IsItem(mount.ItemKey) && !PetSkins.IsItem(mount.ItemKey), $"{mount.Name}'s item does not add it");
        }

        var items = Mounts.ItemDefinitions().ToArray();
        Assert.True(items.Length == mounts.Count && items.All(item => item.Kind == ItemKind.Misc && ItemCodes.Parse(item.CatalogKey) == item.Code),
            "the mounts' items are not one-cell items under their own keys");
        Assert.True(Mounts.Get(0) is null && Mounts.Get(mounts.Count + 1) is null && Mounts.Bit(0) == 0 && Mounts.Bit(64) == 0, "out-of-range mounts exist");
        return Task.CompletedTask;
    }

    private static Task CharacterAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Rider");
        var walking = character.Derived.MoveSpeed;
        Assert.True(character.Mounts == 0 && !character.Ride(true) && !character.ChooseMount(1), "a character without mounts rode");
        Assert.True(character.UnlockMount(1) && !character.UnlockMount(1) && !character.UnlockMount(0) && character.MountChosen == 1,
            "a mount was added twice, an unknown one added, or a new one not picked");
        Assert.True(character.Ride(true) && character.MountRidden == 1 && character.Derived.MoveSpeed == walking + Mounts.Get(1)!.Speed,
            "riding did not add the mount's speed");
        Assert.True(character.UnlockMount(17) && character.Derived.MoveSpeed == walking + Mounts.Get(17)!.Speed, "a new mount picked while riding kept the old speed");
        var before = OwnerState.Capture(character);
        Assert.True(character.Ride(false) && character.MountRidden == 0 && character.Derived.MoveSpeed == walking, "getting off kept the speed");
        Assert.True(!ReferenceEquals(OwnerState.Capture(character, before), before), "getting off did not change the owner state");

        var sibling = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Sibling");
        sibling.RestoreMounts(0, 1);
        Assert.True(sibling.MountChosen == 0, "a mount the account does not own was restored as picked");
        sibling.JoinAccountMounts(character.Mounts);
        Assert.True(Mounts.Has(sibling.Mounts, 1) && Mounts.Has(sibling.Mounts, 17) && sibling.ChooseMount(17), "the account's mounts did not join");
        return Task.CompletedTask;
    }

    private static async Task WorldAsync()
    {
        Assert.True(MountCommand.TryParse("/mount", out var toggle) && toggle is null && MountCommand.TryParse("/mount off", out var off) && off == 0
            && MountCommand.TryParse("/mount 3", out var three) && three == 3 && !MountCommand.TryParse("/mount 99", out _) && !MountCommand.TryParse("/mounts", out _),
            "the mount command was misread");
        Assert.True(MountCommand.Of(null) == "/mount" && MountCommand.Of(0) == "/mount off" && MountCommand.Of(4) == "/mount 4", "the mount command was miswritten");

        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var character = ann.Session.Character!;
        Assert.True(!await fixture.RunAsync(fixture.World.RideAsync(ann.Id, null)), "a player without a mount rode");
        character.UnlockMount(2);
        Assert.True(await fixture.RunAsync(fixture.World.RideAsync(ann.Id, null)) && character.Riding, "the toggle did not ride");
        Assert.True(await fixture.RunAsync(fixture.World.RideAsync(ann.Id, null)) && !character.Riding, "the toggle did not get off");
        Assert.True(!await fixture.RunAsync(fixture.World.RideAsync(ann.Id, 5)) && !character.Riding, "a mount not owned was ridden");
        Assert.True(await fixture.RunAsync(fixture.World.RideAsync(ann.Id, 2)) && character.MountRidden == 2, "an owned mount was not ridden");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(ann)!.Value.Entities.Single(view => view.EntityId == ann.Id).Appearance.Mount == 2,
            "the ridden mount is not in the player's looks");
        Assert.True(await fixture.RunAsync(fixture.World.RideAsync(ann.Id, 0)) && !character.Riding, "off did not get off");
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var collection = new PetCollectionPacket(5, 2) { Name = "Rex", Mounts = 0b1011, MountChosen = 4, Riding = true };
        Assert.True(PetCollectionPacket.Read(buffer.AsSpan(0, collection.Write(buffer))) == collection, "the stable did not survive the wire");
        return Task.CompletedTask;
    }

    private static Task ModelsAsync()
    {
        if (Assets() is not { } root)
        {
            return Task.CompletedTask;
        }

        var reader = new PristonSmdReader();
        var horse = reader.ReadModel(Path.Combine(root, "game", "objects", "mounts", "Horse", "Horse.smd"));
        var skeleton = reader.ReadModel(Path.Combine(root, "game", "objects", "mounts", "Horse", "Horse.smb"));
        Assert.True(horse.Header.Kind == PristonSmdHeaderKind.Version66Model && horse.Objects.Sum(obj => obj.Vertices.Count) == 2113 && skeleton.Objects.Count > 0,
            "the 0.66 horse did not read");
        Assert.True(horse.Bounds.Max.Y is > 20f and < 120f, $"the horse is {horse.Bounds.Max.Y} units tall, not a mount's size");

        // The float layout stores skinned vertices in the bind pose: carried into their bones' space, each bone's bind
        // transform brings them back where the file put them.
        var local = PristonBindSpace.ToBoneSpace(horse, skeleton);
        var bones = skeleton.Objects.GroupBy(bone => bone.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(),
            StringComparer.OrdinalIgnoreCase);
        var body = horse.Objects.First(obj => obj.HasPhysique);
        var moved = local.Objects[horse.Objects.ToList().IndexOf(body)];
        Assert.True(!ReferenceEquals(moved, body) && Enumerable.Range(0, body.Vertices.Count).All(index =>
            Microsoft.Xna.Framework.Vector3.Distance(Microsoft.Xna.Framework.Vector3.Transform(moved.Vertices[index].Position,
                bones[body.PhysiqueBoneNames[index]].Transform), body.Vertices[index].Position) < 0.01f),
            "the horse's vertices were not carried into their bones' space");

        // Every class folder has an idle and a run riding pose per seat, on the character's bones; blended, a bone the
        // pose names takes the pose's world transform and a bone it does not follows its parent.
        var character = reader.ReadModel(Path.Combine(root, "char", "tmABCD", "m1.smb"));
        foreach (var folder in (string[])["fs", "as", "ps", "mgs", "prs", "asn", "sha"])
        {
            foreach (var seat in Enum.GetValues<MountSeat>())
            {
                foreach (var run in (string[])["", "_Run"])
                {
                    var clip = reader.ReadModel(Path.Combine(root, "game", "objects", "animations", folder, $"Mount_{seat}{run}.smb"));
                    var shared = clip.Objects.Count(bone => character.Objects.Any(own => own.Name == bone.Name));
                    Assert.True(clip.LastFrame > 0 && shared >= 30, $"{folder} {seat}{run} shares {shared} bones with the character or has no frames");
                }
            }
        }

        var riding = reader.ReadModel(Path.Combine(root, "game", "objects", "animations", "fs", "Mount_Horse.smb"));
        var clipWorld = new PristonTale.Engine3D.Rendering.PristonModelAnimator(riding).Evaluate(0f);
        var clipIndex = riding.ObjectIndexByName();
        var map = character.Objects.Select(bone => clipIndex.TryGetValue(bone.Name, out var index) ? index : -1).ToArray();
        var pose = new PristonTale.Engine3D.Rendering.PristonModelAnimator(character).Evaluate(0f, float.PositiveInfinity, clipWorld, map);
        var pelvis = character.ObjectIndexByName()["Bip01 Pelvis"];
        Assert.True(pose[pelvis] == clipWorld[clipIndex["Bip01 Pelvis"]], "the riding pose did not drive the pelvis");
        return Task.CompletedTask;
    }

    private static string? Assets()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "assets");
            if (File.Exists(Path.Combine(candidate, "game", "objects", "mounts", "Horse", "Horse.smd")))
            {
                return candidate;
            }
        }

        return null;
    }
}
