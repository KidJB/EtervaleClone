using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World;

// The armour preview: a hidden copy of the player wearing the body item the HUD asks for (PreviewArmor), facing the
// camera, rendered into its own picture (ArmorPreview) before the world each frame. Its parts are prepared off the game thread
// as the player's own look is; the copy appears once they are ready and is removed when the HUD stops asking.
public sealed partial class GameScene
{
    private const int PreviewWidth = 160;
    private const int PreviewHeight = 200;
    private const int FullWidth = 188;
    private const int FullHeight = 270;
    private const float FullVisible = 1.1f;
    private const float FullFocus = 0.5f;
    private const float FullReach = 0.2f;

    // The picture shows the top PreviewVisible of the model's height, centred at PreviewFocus of it: head to waist.
    private const float PreviewVisible = 0.38f;
    private const float PreviewReach = 0.12f;
    private const float PreviewFocus = 0.74f;

    private CharacterSelectPortrait _previewPortrait;
    private Simulation.Characters.SkinSet _previewLook;
    private bool _skinsLoading;
    private int _selfTier;
    private string _previewArmor = "";
    private CharacterVisualSet _previewVisuals;
    private Task _previewPreparation;
    private PlayerActorHandles _previewHandles = new(ActorHandle.None, ActorHandle.None);
    private BoundingBox? _previewFraming;

    // The picture of the player in the previewed armour, or null while none is ready.
    public Texture2D ArmorPreview { get; private set; }

    // The Cabinet: the whole character in these looks, drawn at full height; null stops.
    public void PreviewLook(Simulation.Characters.SkinSet look)
    {
        if (ReferenceEquals(look, _previewLook))
        {
            return;
        }

        _previewLook = look;
        _previewArmor = "";
        RemovePreviewActor();
        _previewVisuals = null;
        _previewPreparation = null;
        if (look is null || _equipmentPresentation is null || _playerComposer is null || _equipmentPresentation.Wearing(look) is not { } visuals)
        {
            return;
        }

        // A hair look shows on the preview before it is applied.
        if (Simulation.Characters.SkinRules.HairOf(look.Hair) is { } hair)
        {
            visuals = Simulation.Characters.CharacterHead.Apply(visuals, _player.Class.Class, _selfTier, visuals.FaceIndex, hair);
        }

        _previewVisuals = visuals;
        _previewPreparation = _playerComposer.PrepareAsync(_assetRoot, visuals, visuals.FaceIndex);
    }

    // The body item to preview ("" or null for none); a key without a body model shows nothing.
    public void PreviewArmor(string armorKey)
    {
        armorKey ??= "";
        if (string.Equals(armorKey, _previewArmor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _previewArmor = armorKey;
        RemovePreviewActor();
        _previewVisuals = null;
        _previewPreparation = null;
        if (armorKey.Length == 0 || _equipmentPresentation is null || _playerComposer is null
            || !EquipmentVisualTables.ArmorSlots.ContainsKey(armorKey) || _equipmentPresentation.Wearing(armorKey) is not { } visuals)
        {
            return;
        }

        _previewVisuals = visuals;
        _previewPreparation = _playerComposer.PrepareAsync(_assetRoot, visuals, visuals.FaceIndex);
    }

    private void UpdateArmorPreview()
    {
        if (!_skinsLoading && _equipmentPresentation is { } presentation && _playChannel is not null)
        {
            _skinsLoading = true;
            _ = LoadSkinsAsync(presentation);
        }

        if (_previewPreparation is { IsCompleted: true } preparation)
        {
            _previewPreparation = null;
            if (preparation.IsCompletedSuccessfully && _previewVisuals is not null)
            {
                _previewHandles = _playerComposer.ComposePortrait(_assetRoot, _previewVisuals, _previewVisuals.FaceIndex, MotionCode.Stand);
            }
            else if (preparation.Exception is { } failure)
            {
                _log.Warn($"[Equipment] Armour preview failed: {failure.GetBaseException().Message}");
            }
        }

    }

    // Draws the preview into its picture; called before the world is drawn.
    public void RenderArmorPreview()
    {
        if (!_previewHandles.Body.IsValid || _renderer is null)
        {
            ArmorPreview = null;
            return;
        }

        // The framing is measured once, so the breathing stand pose keeps one camera.
        if (_previewFraming is null)
        {
            Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
            var count = _previewHandles.CopyTo(handles);
            if (count == 0 || !_renderer.TryGetActorBounds(handles[..count], out var bounds))
            {
                ArmorPreview = null;
                return;
            }

            // Framed by height alone: a narrow box, so a held weapon or wide shoulders never push the camera back.
            var size = bounds.Max - bounds.Min;
            var reach = size.Y * (_previewLook is null ? PreviewReach : FullReach);
            var center = (bounds.Min + bounds.Max) * 0.5f;
            _previewFraming = new BoundingBox(new Vector3(center.X - reach, bounds.Min.Y, center.Z - reach), new Vector3(center.X + reach, bounds.Max.Y, center.Z + reach));
        }

        _previewPortrait ??= new CharacterSelectPortrait(_graphicsDevice);
        ArmorPreview = _previewLook is null
            ? _previewPortrait.Render(_renderer, _previewHandles, PreviewWidth, PreviewHeight, _previewFraming, PreviewVisible, PreviewFocus)
            : _previewPortrait.Render(_renderer, _previewHandles, FullWidth, FullHeight, _previewFraming, FullVisible, FullFocus);
    }

    private void RemovePreviewActor()
    {
        if (_actorScene is not null)
        {
            _previewHandles.Remove(_actorScene);
        }

        _previewHandles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        _previewFraming = null;
        ArmorPreview = null;
    }

    private void DisposeArmorPreview()
    {
        RemovePreviewActor();
        _previewPortrait?.Dispose();
        _previewPortrait = null;
    }
}
