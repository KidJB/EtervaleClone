using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Physics;

namespace PristonTale.Gameplay.World;

// Item use from the HUD, the premium timers the server sends and the item effects played around players.
public sealed partial class GameScene
{
    private ItemUseEffects _itemEffects;
    // The anchors the item effects follow, as delegates made once instead of every frame.
    private Func<ulong, Vector3?> _itemAnchor;
    private Func<ulong, (Vector3 Position, float Facing)?> _petOwnerPose;
    private PremiumPetPresentation _pets;

    // The local player's pet collection as the server last sent it (none until it does).
    public PetCollectionInfo PetCollection { get; private set; } = new(0, 0);
    private SkinPetPresentation _skinPets;
    private MountPresentation _mounts;
    private Action<ulong, PristonModel, float> _seatRider;
    private float _appliedSelfLift;
    private ForceOrbPresentation _forceOrbs;

    // The local player's running premium timers as last sent by the server.
    public PremiumTimerSnapshot PremiumTimers { get; private set; }

    public Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command)
    {
        return _playChannel is not null ? _playChannel.UseItemAsync(command, _presenceCts.Token)
            : Task.FromResult(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Rejected));
    }

    // The return sparkle (ItemVfx.Return) over feet at a point: ether cores, crystal summons, warps and wing gates.
    private PristonTale.Effects.Vfx.VfxHandle PlayReturn(Vector3 feet) =>
        _skillFx?.SpawnAt(ItemVfx.Return, feet + Vector3.Up * GroundTraversal.VisualOffset, Matrix.Identity) ?? default;

    // Loads the item effects with the scene's effects, at the field load that creates the renderer.
    private void CreateItemEffects()
    {
        if (_skillFx is not null && _itemEffects is null)
        {
            ItemVfx.Register(_skillFx.Library);
            _itemEffects = new ItemUseEffects(_skillFx, _renderer?.TransientLights, _skillSounds);
        }

        var scripts = _skillFx is null ? null : new SystemScriptEffects(_skillFx.System, _skillFx.Library);
        if (_forceOrbs is null && _skillFx is not null)
        {
            _forceOrbs = new ForceOrbPresentation(_skillFx, scripts, _renderer?.TransientLights);
        }

        if (_pets is null && _renderer is not null)
        {
            _pets = new PremiumPetPresentation(_actorScene, _actorLoader, _assetRoot ?? "", scripts, _renderer.TransientLights, _skillSounds, _log);
        }

        _skinPets ??= _renderer is null ? null : new SkinPetPresentation(_actorScene, _actorLoader, _assetRoot ?? "", _log);
        _mounts ??= _renderer is null ? null : new MountPresentation(_actorScene, _actorLoader, _assetRoot ?? "", _log);
    }

    private void ObserveItems(PresenceUpdate update)
    {
        if (update.PremiumTimers is { } timers)
        {
            PremiumTimers = timers;
        }

        _pets?.Observe(update, _itemAnchor ??= ItemAnchor);
        _skinPets?.Observe(update);
        _mounts?.Observe(update);
        if (update.PetCollection is { } collection)
        {
            PetCollection = collection;
            // The local player's own pet and mount follow its collection, which the server sends at every change.
            _skinPets?.Set(SelfEntityId, collection.Out, PetNames.Shown(collection.Name, _player?.Name ?? ""));
            _mounts?.Set(SelfEntityId, collection.Riding ? collection.MountChosen : 0, _player?.Class.Class ?? PlayerClass.Fighter);
        }
        ObserveOwnHead(update);
        _forceOrbs?.Observe(update, _itemAnchor ??= ItemAnchor, _skillClockMs);

        foreach (var effect in update.ItemEffects)
        {
            if (ItemAnchor(effect.Entity) is not { } position)
            {
                continue;
            }

            if (effect.Effect == ItemEffectKind.Mastery)
            {
                _skillFx?.Spawn(ItemVfx.Mastery, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect is ItemEffectKind.Learn or ItemEffectKind.ForceOrb or ItemEffectKind.HeadChange)
            {
                _skillFx?.Spawn(ItemVfx.Learn, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect == ItemEffectKind.Return)
            {
                // The sparkle stands where the server placed it: the user, the called monster or the revived player.
                PlayReturn(effect.Position);
            }

            _itemEffects?.Play(effect.Effect, effect.Entity, effect.Variant, position, PlayerPosition, _skillClockMs);
        }
    }

    // A hair tint or a big-head potion changes the local player's head when the server describes the player again.
    private void ObserveOwnHead(PresenceUpdate update)
    {
        ObserveOwnGlow(update);
        if (_remote is null || _equipmentPresentation is null || _player is null)
        {
            return;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.EntityId != _remote.SelfEntityId || spawn.Player is not { } appearance)
            {
                continue;
            }

            var playerClass = _player.Class.Class;
            var head = CharacterHead.Apply(_player.Definition.DefaultVisuals, playerClass, appearance.Tier, appearance.FaceIndex, appearance.HairIndex);
            _equipmentPresentation.SetBaseVisuals(CharacterHead.ApplyBigHead(head, playerClass, appearance.BigHead));
        }
    }

    private void UpdateItemEffects(float dt)
    {
        _itemAnchor ??= ItemAnchor;
        _petOwnerPose ??= PetOwnerPose;
        _itemEffects?.Update(_skillClockMs, _itemAnchor, PlayerPosition);
        _pets?.Update(dt, _petOwnerPose, _itemAnchor, PlayerPosition);
        _skinPets?.Update(dt, _petOwnerPose);
        _seatRider ??= SeatRider;
        _mounts?.Update(_petOwnerPose, _seatRider);
        if (SelfLift != _appliedSelfLift && _playerBody is not null)
        {
            _appliedSelfLift = SelfLift;
            ApplyPlayerWorld();
        }
        _forceOrbs?.Update(_skillClockMs, _itemAnchor);
    }

    private (Vector3 Position, float Facing)? PetOwnerPose(ulong entityId) =>
        TryGetDrawnPosition(entityId, out var position) && TryGetDrawnFacing(entityId, out var facing) ? (position, facing) : null;

    // A rider's model takes its riding pose (null: its own motions) and rises onto its mount; the local player's rises
    // through PlayerWorld.
    private void SeatRider(ulong entityId, PristonModel pose, float lift)
    {
        if (IsSelf(entityId))
        {
            PlayerActors.SetClip(_actorScene, pose);
            return;
        }

        _remote?.SetRiderLift(entityId, lift);
        _remote?.SetRiderClip(entityId, pose);
    }

    // How far the local player's model rises: its mount's saddle height while it rides.
    private float SelfLift => _mounts is null || _remote is null ? 0f : _mounts.LiftOf(_remote.SelfEntityId);

    private Vector3? ItemAnchor(ulong entityId) => TryGetDrawnPosition(entityId, out var position) ? position : null;

    // Where an entity is drawn: the local player's body (its collision position) or a remote entity's drawn position.
    private bool TryGetDrawnPosition(ulong entityId, out Vector3 position)
    {
        if (IsSelf(entityId))
        {
            position = _playerBody.Position;
            return true;
        }

        position = default;
        return _remote is not null && _remote.TryGetDrawnPosition(entityId, out position);
    }

    private bool TryGetDrawnFacing(ulong entityId, out float facing)
    {
        if (IsSelf(entityId))
        {
            facing = PlayerYaw;
            return true;
        }

        facing = 0f;
        return _remote is not null && _remote.TryGetDrawnFacing(entityId, out facing);
    }

    private bool IsSelf(ulong entityId) => _remote is not null && entityId == _remote.SelfEntityId && _playerBody is not null;
}
