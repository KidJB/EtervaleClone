using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// Far depth of field (Shaders/Passes/PostDepthOfField.fx): the scene blurred behind the camera's focus, into its own target.
internal sealed class PristonDepthOfField : IDisposable
{
    public const string Asset = "Effects/PostDepthOfField";
    public const float DefaultStart = 700f;
    public const float DefaultEnd = 4200f;
    // The largest blur radius at a 1080-pixel-tall image; it scales with the image.
    public const float DefaultRadius = 7f;
    private const float ReferenceHeight = 1080f;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectParameter _source;
    private readonly EffectParameter _depth;
    private readonly EffectParameter _texel;
    private readonly EffectParameter _focus;
    private readonly EffectParameter _maxRadius;
    private readonly EffectParameter _blurStart;
    private readonly EffectParameter _blurEnd;
    private RenderTarget2D _target;

    public PristonDepthOfField(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _source = effect.Parameters["Source"];
        _depth = effect.Parameters["Depth"];
        _texel = effect.Parameters["SourceTexel"];
        _focus = effect.Parameters["FocusDistance"];
        _maxRadius = effect.Parameters["MaxRadius"];
        _blurStart = effect.Parameters["BlurStart"];
        _blurEnd = effect.Parameters["BlurEnd"];
    }

    // The blur radius in pixels of a surface at a view depth; the shader's RadiusAt.
    public static float Radius(float depth, float focus, int height, float start = DefaultStart, float end = DefaultEnd, float radius = DefaultRadius) =>
        MathHelper.Clamp((depth - focus - start) / Math.Max(1f, end - start), 0f, 1f) * MaxRadius(height, radius);

    private static float MaxRadius(int height, float radius) => radius * height / ReferenceHeight;

    public Texture2D Run(Texture2D scene, Texture2D depth, float focusDistance, float start = DefaultStart, float end = DefaultEnd, float radius = DefaultRadius)
    {
        if (_target is null || _target.Width != scene.Width || _target.Height != scene.Height)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(_device, scene.Width, scene.Height, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        }

        _device.SetRenderTarget(_target);
        _device.BlendState = BlendState.Opaque;
        _source.SetValue(scene);
        _depth.SetValue(depth);
        _texel.SetValue(new Vector2(1f / scene.Width, 1f / scene.Height));
        _focus.SetValue(focusDistance);
        _maxRadius.SetValue(MaxRadius(scene.Height, radius));
        _blurStart.SetValue(start);
        _blurEnd.SetValue(Math.Max(start + 1f, end));
        _triangle.Draw(_effect);
        return _target;
    }

    public void Dispose() => _target?.Dispose();
}
