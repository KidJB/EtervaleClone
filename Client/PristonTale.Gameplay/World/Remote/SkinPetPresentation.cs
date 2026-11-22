using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Gameplay.World.Remote;

// The skin pets that walk beside their owners: each player's PetSkin (from its looks) drawn as that monster's model at the
// skin's scale, on the ground behind and to the side of its owner. It closes a tenth of the distance to its place per 70 Hz
// frame, faces where it goes, walks while it moves and stands while it is near; a pet left far behind (a warp) jumps there.
// The same walkers draw caravans (Caravans): skins and looks pick another list and part of the looks, side the other flank.
public sealed class SkinPetPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IClientLog log,
    Func<int, PetSkin> skins = null, Func<PlayerAppearanceInfo, (int Skin, string Label, bool Stays)> looks = null, float side = 1f) : IDisposable
{
    private const float Behind = 26f;
    private const float Aside = 18f;
    private const float Follow = 0.1f;
    private const float WalkingDistance = 6f;
    private const float JumpDistance = 600f;
    // The name hangs this far above the pet's feet at full size (scaled with the skin, as its model is).
    private const float LabelHeight = 70f;
    private const float PickRadius = LabelHeight / 2f;

    private readonly Dictionary<int, Task<PristonActor>> _models = new();
    private readonly Dictionary<ulong, Pet> _pets = new();
    private readonly List<ulong> _gone = new();

    // The skin each player's looks show, from the spawns of players.
    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Player is { } player)
            {
                var (skin, label, stays) = looks?.Invoke(player) ?? (player.PetSkin, PetNames.Shown(player.PetName, player.Name), false);
                Set(spawn.EntityId, skin, label, stays);
            }
        }

        foreach (var removed in update.Removed)
        {
            Set(removed, 0, "");
        }
    }

    public bool Has(ulong ownerId) => _pets.ContainsKey(ownerId);

    // label: the name shown over the pet (PetNames.Shown); a new name alone keeps the pet where it is. stays: the walker
    // stands where it is instead of following its owner.
    public void Set(ulong ownerId, int skin, string label, bool stays = false)
    {
        if (_pets.TryGetValue(ownerId, out var pet))
        {
            if (pet.Skin.Id == skin)
            {
                pet.Label = label;
                pet.Stays = stays;
                return;
            }

            pet.Remove(scene);
            _pets.Remove(ownerId);
        }

        if ((skins ?? PetSkins.Get)(skin) is { } chosen)
        {
            _pets[ownerId] = new Pet(chosen) { Label = label, Stays = stays };
            Prepare(chosen);
        }
    }

    // pose: the owner's drawn position and facing (yaw), or null when it is not drawn.
    public void Update(float dt, Func<ulong, (Vector3 Position, float Facing)?> pose)
    {
        var ticks = dt * GameUnits.TicksPerSecond;
        _gone.Clear();
        foreach (var (ownerId, pet) in _pets)
        {
            if (pose(ownerId) is not { } at)
            {
                continue;
            }

            var place = at.Position + Vector3.Transform(new Vector3(Aside * side, 0f, -Behind), Matrix.CreateRotationY(at.Facing));
            if (!pet.HasActor)
            {
                if (_models.GetValueOrDefault(pet.Skin.Id) is not { IsCompletedSuccessfully: true } model)
                {
                    continue;
                }

                pet.Position = place;
                pet.Facing = at.Facing;
                pet.Handle = scene.AddActor(new PristonActorInstance(model.Result, World(pet), MotionCode.Stand));
                pet.HasActor = true;
            }

            if (pet.Stays)
            {
                if (pet.Walking)
                {
                    pet.Walking = false;
                    scene.SetActorMotion(pet.Handle, MotionCode.Stand);
                }

                continue;
            }

            var offset = place - pet.Position;
            offset.Y = 0f;
            var distance = offset.Length();
            if (distance > JumpDistance)
            {
                pet.Position = place;
            }
            else
            {
                var step = 1f - MathF.Pow(1f - Follow, ticks);
                pet.Position += offset * step;
                pet.Position = new Vector3(pet.Position.X, at.Position.Y, pet.Position.Z);
                if (distance > 0.5f)
                {
                    pet.Facing = MathF.Atan2(offset.X, offset.Z);
                }
            }

            var walking = distance > WalkingDistance;
            if (walking != pet.Walking)
            {
                pet.Walking = walking;
                scene.SetActorMotion(pet.Handle, walking ? MotionCode.Walk : MotionCode.Stand);
            }

            scene.SetActorWorld(pet.Handle, World(pet));
        }
    }

    // Fills the caller's list (cleared first) with each drawn pet's name and the point above its head, without allocating.
    public void CollectLabels(List<(Vector3 Anchor, string Text)> labels)
    {
        labels.Clear();
        foreach (var pet in _pets.Values)
        {
            if (pet.HasActor && pet.Label.Length > 0)
            {
                labels.Add((pet.Position + Vector3.Up * (LabelHeight * pet.Skin.Scale), pet.Label));
            }
        }
    }

    // The distance along the ray to an owner's walker, while it is drawn: a sphere around its body, as tall as its label is
    // high; null when the ray misses it.
    public float? Pick(Ray ray, ulong ownerId)
    {
        if (!_pets.TryGetValue(ownerId, out var pet) || !pet.HasActor)
        {
            return null;
        }

        return PickAt(ray, pet.Position, pet.Skin.Scale);
    }

    // The distance along the ray to a walker standing at position with the model's scale; null when it misses.
    public static float? PickAt(Ray ray, Vector3 position, float scale)
    {
        var radius = PickRadius * scale;
        return CursorPicking.IntersectSphere(ray, position + Vector3.Up * radius, radius);
    }

    // The hover outline around an owner's walker (strength 0 removes it).
    public void SetOutline(ulong ownerId, Vector3 color, float strength)
    {
        if (_pets.TryGetValue(ownerId, out var pet) && pet.HasActor)
        {
            scene.SetActorOutline(pet.Handle, color, strength);
        }
    }

    // The point above an owner's walker's head, while it is drawn.
    public bool TryGetAnchor(ulong ownerId, out Vector3 anchor)
    {
        if (_pets.TryGetValue(ownerId, out var pet) && pet.HasActor)
        {
            anchor = pet.Position + Vector3.Up * (LabelHeight * pet.Skin.Scale);
            return true;
        }

        anchor = default;
        return false;
    }

    public void Dispose()
    {
        foreach (var pet in _pets.Values)
        {
            pet.Remove(scene);
        }

        _pets.Clear();
    }

    private static Matrix World(Pet pet) =>
        Matrix.CreateScale(pet.Skin.Scale) * Matrix.CreateRotationY(pet.Facing) * Matrix.CreateTranslation(pet.Position);

    private void Prepare(PetSkin skin)
    {
        if (_models.ContainsKey(skin.Id))
        {
            return;
        }

        var task = Task.Run(async () =>
        {
            var actor = loader.Load(assetRoot, AssetFiles.Combine(assetRoot, skin.Model));
            await scene.PrepareActorResourcesAsync(actor).ConfigureAwait(false);
            return actor;
        });
        _models[skin.Id] = task;
        _ = ObserveAsync(task, skin);
    }

    private async Task ObserveAsync(Task task, PetSkin skin)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Warn($"[Pets] The {skin.Name} pet's model could not be loaded: {exception.Message}");
        }
    }

    private sealed class Pet(PetSkin skin)
    {
        public PetSkin Skin { get; } = skin;
        public ActorHandle Handle { get; set; }
        public bool HasActor { get; set; }
        public bool Walking { get; set; }
        public Vector3 Position { get; set; }
        public float Facing { get; set; }
        public string Label { get; set; } = "";
        public bool Stays { get; set; }

        public void Remove(IActorScene scene)
        {
            if (HasActor)
            {
                scene.RemoveActor(Handle);
                HasActor = false;
            }
        }
    }
}
