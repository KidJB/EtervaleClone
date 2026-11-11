using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World;

// A listed character as the Character Market draws it: its class, tier, face and hair and its worn armour, weapon and off-hand.
// Full is the whole body (the details page); otherwise head to waist (a card).
public sealed record MarketLook(PlayerClass Class, int Tier, int Face, int Hair, string Armor, string Weapon, string OffHand, bool Full);

// The Character Market's pictures: each card look asked for is prepared off the game thread, composed hidden, drawn into its own picture for a few frames
// (until its textures have streamed in) and then kept, its actor removed. One look is prepared and drawn at a time; the oldest pictures go past the cache.
// A whole-body look (the details page) is live instead: its actor stays, breathing in its stand pose and turning slowly, and is drawn every frame while asked for.
public sealed partial class GameScene
{
    private const int MarketCardWidth = 120;
    private const int MarketCardHeight = 128;
    private const int MarketFullWidth = 250;
    private const int MarketFullHeight = 440;
    private const int MarketDrawFrames = 8;
    private const int MarketCacheSize = 24;
    private const float MarketCardVisible = 0.42f;
    private const float MarketCardFocus = 0.72f;
    private const float MarketFullVisible = 1.08f;
    private const float MarketFullFocus = 0.5f;
    private const float MarketTurnPerFrame = 0.006f;
    private const int MarketLiveGrace = 30;

    private static readonly CharacterClassRegistry MarketClasses = new();

    private sealed class MarketPicture
    {
        public CharacterSelectPortrait Portrait;
        public Texture2D Texture;
        public long Used;
    }

    private readonly Dictionary<MarketLook, MarketPicture> _marketPictures = [];
    private readonly Queue<MarketLook> _marketWanted = new();
    private MarketLook _marketDrawing;
    private CharacterVisualSet _marketVisuals;
    private Task _marketPreparation;
    private PlayerActorHandles _marketHandles = new(ActorHandle.None, ActorHandle.None);
    private int _marketFrames;
    private long _marketClock;
    private MarketLook _liveLook;
    private Task _livePreparation;
    private CharacterVisualSet _liveVisuals;
    private PlayerActorHandles _liveHandles = new(ActorHandle.None, ActorHandle.None);
    private CharacterSelectPortrait _livePortrait;
    private BoundingBox? _liveFraming;
    private Texture2D _liveTexture;
    private float _liveTurn;
    private long _liveAsked;

    // The picture of that look, or null while it is being made; asking queues it.
    public Texture2D MarketPortrait(MarketLook look)
    {
        _marketClock++;
        if (look.Full)
        {
            return LivePortrait(look);
        }

        if (_marketPictures.TryGetValue(look, out var picture))
        {
            picture.Used = _marketClock;
            return picture.Texture;
        }

        if (!look.Equals(_marketDrawing) && !_marketWanted.Contains(look))
        {
            _marketWanted.Enqueue(look);
        }

        return null;
    }

    // Draws the look in progress into its picture; called before the world is drawn.
    public void RenderMarketPortraits()
    {
        if (_renderer is null || _playerComposer is null || _assetRoot is null)
        {
            return;
        }

        RenderLivePortrait();

        if (_marketDrawing is null)
        {
            StartMarketLook();
            return;
        }

        if (_marketPreparation is { IsCompleted: true } preparation)
        {
            _marketPreparation = null;
            if (!preparation.IsCompletedSuccessfully)
            {
                _log.Warn($"[Market] Portrait not made: {preparation.Exception?.GetBaseException().Message}");
                _marketDrawing = null;
                return;
            }

            _marketHandles = _playerComposer.ComposePortrait(_assetRoot, _marketVisuals, _marketVisuals.FaceIndex, MotionCode.Stand);
            _marketFrames = 0;
        }

        if (!_marketHandles.Body.IsValid)
        {
            return;
        }

        var look = _marketDrawing;
        if (!_marketPictures.TryGetValue(look, out var picture))
        {
            picture = new MarketPicture { Portrait = new CharacterSelectPortrait(_graphicsDevice), Used = _marketClock };
            _marketPictures[look] = picture;
            EvictMarketPictures();
        }

        var texture = look.Full
            ? picture.Portrait.Render(_renderer, _marketHandles, MarketFullWidth, MarketFullHeight, null, MarketFullVisible, MarketFullFocus)
            : picture.Portrait.Render(_renderer, _marketHandles, MarketCardWidth, MarketCardHeight, null, MarketCardVisible, MarketCardFocus);
        if (++_marketFrames >= MarketDrawFrames)
        {
            picture.Texture = texture;
            RemoveMarketActor();
        }
    }

    private Texture2D LivePortrait(MarketLook look)
    {
        _liveAsked = _marketClock;
        if (look.Equals(_liveLook))
        {
            return _liveTexture;
        }

        RemoveLiveActor();
        if (_playerComposer is null || _assetRoot is null || !Enum.IsDefined(look.Class) || VisualsOf(look) is not { } visuals)
        {
            return null;
        }

        _liveLook = look;
        _liveVisuals = visuals;
        _livePreparation = _playerComposer.PrepareAsync(_assetRoot, visuals, visuals.FaceIndex);
        return null;
    }

    private void RenderLivePortrait()
    {
        if (_liveLook is null)
        {
            return;
        }

        // The page closed: the actor goes once nothing asked for a moment.
        if (_marketClock - _liveAsked > MarketLiveGrace)
        {
            RemoveLiveActor();
            return;
        }

        if (_livePreparation is { IsCompleted: true } preparation)
        {
            _livePreparation = null;
            if (!preparation.IsCompletedSuccessfully)
            {
                _log.Warn($"[Market] Live portrait not made: {preparation.Exception?.GetBaseException().Message}");
                return;
            }

            _liveHandles = _playerComposer.ComposePortrait(_assetRoot, _liveVisuals, _liveVisuals.FaceIndex, MotionCode.Stand);
            _liveFraming = null;
        }

        if (!_liveHandles.Body.IsValid)
        {
            return;
        }

        // Framed once at rest, so the turning model and its breathing keep one camera.
        if (_liveFraming is null)
        {
            Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
            var count = _liveHandles.CopyTo(handles);
            if (count == 0 || !_renderer.TryGetActorBounds(handles[..count], out var bounds))
            {
                return;
            }

            var size = bounds.Max - bounds.Min;
            var reach = size.Y * 0.2f;
            var center = (bounds.Min + bounds.Max) * 0.5f;
            _liveFraming = new BoundingBox(new Vector3(center.X - reach, bounds.Min.Y, center.Z - reach), new Vector3(center.X + reach, bounds.Max.Y, center.Z + reach));
        }

        _liveTurn = (_liveTurn + MarketTurnPerFrame) % MathHelper.TwoPi;
        _liveHandles.SetWorld(_actorScene, Matrix.CreateRotationY(_liveTurn));
        _livePortrait ??= new CharacterSelectPortrait(_graphicsDevice);
        _liveTexture = _livePortrait.Render(_renderer, _liveHandles, MarketFullWidth, MarketFullHeight, _liveFraming, MarketFullVisible, MarketFullFocus);
    }

    private void RemoveLiveActor()
    {
        if (_actorScene is not null)
        {
            _liveHandles.Remove(_actorScene);
        }

        _liveHandles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        _liveLook = null;
        _livePreparation = null;
        _liveTexture = null;
        _liveTurn = 0;
    }

    private static CharacterVisualSet VisualsOf(MarketLook look)
    {
        var definition = MarketClasses.Get(look.Class).Definition;
        try
        {
            var visuals = CharacterHead.Apply(definition.DefaultVisuals, look.Class, look.Tier, look.Face, look.Hair);
            return EquipmentVisuals.Apply(visuals, look.Armor, look.Weapon, look.OffHand, village: true, definition.MotionJobMask);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private void StartMarketLook()
    {
        while (_marketWanted.TryDequeue(out var look))
        {
            if (_marketPictures.ContainsKey(look) || !Enum.IsDefined(look.Class))
            {
                continue;
            }

            if (VisualsOf(look) is not { } visuals)
            {
                _log.Warn($"[Market] Look not drawn: {look.Class} wearing '{look.Armor}'");
                continue;
            }

            _marketVisuals = visuals;

            _marketDrawing = look;
            _marketPreparation = _playerComposer.PrepareAsync(_assetRoot, _marketVisuals, _marketVisuals.FaceIndex);
            return;
        }
    }

    private void EvictMarketPictures()
    {
        while (_marketPictures.Count > MarketCacheSize)
        {
            var oldest = _marketPictures.MinBy(static entry => entry.Value.Used);
            oldest.Value.Portrait.Dispose();
            _marketPictures.Remove(oldest.Key);
        }
    }

    private void RemoveMarketActor()
    {
        if (_actorScene is not null)
        {
            _marketHandles.Remove(_actorScene);
        }

        _marketHandles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        _marketDrawing = null;
    }

    private void DisposeMarketPortraits()
    {
        RemoveMarketActor();
        RemoveLiveActor();
        _livePortrait?.Dispose();
        _livePortrait = null;
        _marketWanted.Clear();
        foreach (var picture in _marketPictures.Values)
        {
            picture.Portrait.Dispose();
        }

        _marketPictures.Clear();
    }
}
