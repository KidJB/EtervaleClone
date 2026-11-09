using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// Upscales the display image of a smaller scene to the caller's size (Shaders/Passes/PostFsr.fx): edge-adaptive upsampling
// into an output-sized target, then sharpening into the caller's target.
internal sealed class PristonFsr : IDisposable
{
    public const string Asset = "Effects/PostFsr";

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _easu;
    private readonly EffectTechnique _rcas;
    private readonly EffectParameter _source;
    private readonly EffectParameter _sourceSize;
    private readonly EffectParameter _sharpness;
    private RenderTarget2D _target;

    public PristonFsr(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _easu = effect.Techniques["Easu"];
        _rcas = effect.Techniques["Rcas"];
        _source = effect.Parameters["Source"];
        _sourceSize = effect.Parameters["SourceSize"];
        _sharpness = effect.Parameters["Sharpness"];
    }

    public void Apply(Texture2D image, int width, int height, float sharpness, Action bindOutput)
    {
        if (_target is null || _target.Width != width || _target.Height != height)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        }

        _device.SetRenderTarget(_target);
        _device.BlendState = BlendState.Opaque;
        Draw(_easu, image, 0f);
        bindOutput();
        Draw(_rcas, _target, sharpness);
    }

    private void Draw(EffectTechnique technique, Texture2D source, float sharpness)
    {
        _effect.CurrentTechnique = technique;
        _source.SetValue(source);
        _sourceSize.SetValue(new Vector4(source.Width, source.Height, 1f / source.Width, 1f / source.Height));
        _sharpness.SetValue(sharpness);
        _triangle.Draw(_effect);
    }

    public void Dispose() => _target?.Dispose();
}

// Moves the render scale toward a frame rate: a step down when frames stay slower than the target, a step up when they stay
// well faster, never above the player's own scale. A step recreates the scene targets, so steps are at least HoldSeconds apart.
public sealed class PristonDynamicResolution
{
    public const float MinimumScale = 0.5f;
    private const float Step = 0.05f;
    private const float HoldSeconds = 1.5f;
    private const float SlowerThan = 1.05f;
    private const float FasterThan = 0.8f;

    private float _elapsed;
    private int _frames;

    public float Scale { get; private set; } = 1f;

    // The scale for the next frame; targetFps 0 keeps the player's scale.
    public float Update(double deltaSeconds, float playerScale, int targetFps)
    {
        if (targetFps <= 0)
        {
            Scale = playerScale;
            _elapsed = 0f;
            _frames = 0;
            return Scale;
        }

        Scale = Math.Min(Scale, playerScale);
        _elapsed += (float)deltaSeconds;
        _frames++;
        if (_elapsed < HoldSeconds)
        {
            return Scale;
        }

        var frameSeconds = _elapsed / _frames;
        var budget = 1f / targetFps;
        if (frameSeconds > budget * SlowerThan)
        {
            Scale = Math.Max(MinimumScale, Scale - Step);
        }
        else if (frameSeconds < budget * FasterThan)
        {
            Scale = Math.Min(playerScale, Scale + Step);
        }

        _elapsed = 0f;
        _frames = 0;
        return Scale;
    }
}
