using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

// The mounts players ride: each rider's Mount (from its looks) drawn at the rider's place and facing, running while the
// rider moves and standing while it stops. The rider sits in its class's riding pose for the mount's seat
// (game/objects/animations/<class>/Mount_<Seat>.smb, and _Run while moving), which seats it on the mount; without the pose
// files its own model rises by the mount's saddle height instead.
public sealed class MountPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IClientLog log) : IDisposable
{
    // A rider that moved this far (units) in a frame is running.
    private const float MovingDistance = 0.2f;

    private readonly Dictionary<int, Task<PristonActor>> _models = new();
    private readonly Dictionary<(string Folder, MountSeat Seat, bool Running), Task<PristonModel>> _poses = new();
    private readonly Dictionary<ulong, Ridden> _ridden = new();
    // Riders that got off since the last update, whose models come back down and out of the riding pose.
    private readonly List<ulong> _dismounted = new();

    // The mount each player's looks show, from the spawns of players.
    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Player is { } player)
            {
                Set(spawn.EntityId, player.Mount, player.Class);
            }
        }

        foreach (var removed in update.Removed)
        {
            Set(removed, 0, PlayerClass.Fighter);
        }
    }

    public void Set(ulong riderId, int mountId, PlayerClass riderClass)
    {
        if (_ridden.TryGetValue(riderId, out var ridden))
        {
            if (ridden.Mount.Id == mountId && ridden.Folder == PoseFolder(riderClass))
            {
                return;
            }

            ridden.Remove(scene);
            _ridden.Remove(riderId);
            _dismounted.Add(riderId);
        }

        if (Mounts.Get(mountId) is { } mount)
        {
            var folder = PoseFolder(riderClass);
            _ridden[riderId] = new Ridden(mount, folder);
            _dismounted.Remove(riderId);
            Prepare(mount);
            PreparePose(folder, mount.Seat, false);
            PreparePose(folder, mount.Seat, true);
        }
    }

    // How far the rider's model rises: the saddle height while its mount is drawn and no riding pose seats it, else 0.
    public float LiftOf(ulong riderId) => _ridden.TryGetValue(riderId, out var ridden) && ridden.HasActor ? ridden.Lift : 0f;

    // pose: the rider's drawn ground position and facing (yaw), or null when it is not drawn. seat: puts the rider's model
    // in its riding pose (null: its own motions) and lifts it.
    public void Update(Func<ulong, (Vector3 Position, float Facing)?> pose, Action<ulong, PristonModel, float> seat)
    {
        foreach (var riderId in _dismounted)
        {
            seat(riderId, null, 0f);
        }

        _dismounted.Clear();
        foreach (var (riderId, ridden) in _ridden)
        {
            if (pose(riderId) is not { } at)
            {
                continue;
            }

            if (!ridden.HasActor)
            {
                if (_models.GetValueOrDefault(ridden.Mount.Id) is not { IsCompletedSuccessfully: true } model)
                {
                    continue;
                }

                ridden.Position = at.Position;
                ridden.Handle = scene.AddActor(new PristonActorInstance(model.Result, World(ridden.Mount, at.Position, at.Facing), MotionCode.Stand));
                ridden.HasActor = true;
            }

            var moved = Vector2.Distance(new Vector2(at.Position.X, at.Position.Z), new Vector2(ridden.Position.X, ridden.Position.Z));
            ridden.Position = at.Position;
            var running = moved > MovingDistance;
            if (running != ridden.Running)
            {
                ridden.Running = running;
                scene.SetActorMotion(ridden.Handle, running ? MotionCode.Run : MotionCode.Stand);
            }

            scene.SetActorWorld(ridden.Handle, World(ridden.Mount, at.Position, at.Facing));
            var clip = _poses.GetValueOrDefault((ridden.Folder, ridden.Mount.Seat, running)) is { IsCompletedSuccessfully: true } loaded ? loaded.Result : null;
            ridden.Lift = clip is null ? ridden.Mount.SaddleHeight : 0f;
            seat(riderId, clip, ridden.Lift);
        }
    }

    public void Dispose()
    {
        foreach (var ridden in _ridden.Values)
        {
            ridden.Remove(scene);
        }

        _ridden.Clear();
    }

    // The class's folder of riding poses; the classes the poses were not made for borrow a close class's.
    private static string PoseFolder(PlayerClass riderClass) => riderClass switch
    {
        PlayerClass.Archer or PlayerClass.Atalanta => "as",
        PlayerClass.Pikeman => "ps",
        PlayerClass.Magician => "mgs",
        PlayerClass.Priestess => "prs",
        PlayerClass.Assassin => "asn",
        PlayerClass.Shaman => "sha",
        _ => "fs"
    };

    private static Matrix World(Mount mount, Vector3 position, float facing) =>
        Matrix.CreateScale(mount.Scale) * Matrix.CreateRotationY(facing) * Matrix.CreateTranslation(position);

    private void Prepare(Mount mount)
    {
        if (_models.ContainsKey(mount.Id))
        {
            return;
        }

        var task = Task.Run(async () =>
        {
            var actor = loader.Load(assetRoot, AssetFiles.Combine(assetRoot, mount.Model));
            await scene.PrepareActorResourcesAsync(actor).ConfigureAwait(false);
            return actor;
        });
        _models[mount.Id] = task;
        _ = ObserveAsync(task, mount.Name + " mount's model");
    }

    private void PreparePose(string folder, MountSeat seat, bool running)
    {
        var key = (folder, seat, running);
        if (_poses.ContainsKey(key))
        {
            return;
        }

        var path = AssetFiles.Combine(assetRoot, Path.Combine("game", "objects", "animations", folder, $"Mount_{seat}{(running ? "_Run" : "")}.smb"));
        var task = Task.Run(() => new PristonSmdReader().ReadModel(path));
        _poses[key] = task;
        _ = ObserveAsync(task, $"{seat} riding pose ({folder})");
    }

    private async Task ObserveAsync(Task task, string what)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Warn($"[Mounts] The {what} could not be loaded: {exception.Message}");
        }
    }

    private sealed class Ridden(Mount mount, string folder)
    {
        public Mount Mount { get; } = mount;
        public string Folder { get; } = folder;
        public ActorHandle Handle { get; set; }
        public bool HasActor { get; set; }
        public bool Running { get; set; }
        public float Lift { get; set; }
        public Vector3 Position { get; set; }

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
