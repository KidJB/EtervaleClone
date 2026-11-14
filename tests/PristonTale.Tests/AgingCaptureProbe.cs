using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

// The held-weapon glow of every aging level from +4 to +20: a fighter per level in a grid, captured at moments of the
// pulse (rising, peak, falling) and later on to show the overlay's scroll. aging-NNNN.png is named by the glow clock.
internal sealed class AgingCaptureProbe : Game
{
    private const string Sword = "WA110";
    private const int Columns = 6;
    private const int WarmupFrames = 40;
    private const double FrameMs = 1000d / 60;
    private static readonly int[] CaptureMs = [1000, 1250, 1500, 1750, 3000];

    private readonly string _root;
    private readonly string _output;
    private PristonRenderer _renderer = null!;
    private PlayerActorComposer _composer = null!;
    private ActorLookPresenter _looks = null!;
    private PristonCamera _camera = null!;
    private Task _preparation = Task.CompletedTask;
    private readonly List<(CharacterVisualSet Visuals, Matrix World, HeldItemGlow Glow)> _avatars = [];
    private readonly List<PlayerActorHandles> _handles = [];
    private int _frames;
    private int _capture;

    public AgingCaptureProbe(string root, string content, string output)
    {
        _root = root;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    private double ClockMs => _frames * FrameMs;

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(12);
        var scene = new RendererActorScene(_renderer);
        _composer = new PlayerActorComposer(scene, new PristonActorLoader());
        _looks = new ActorLookPresenter(scene, new PristonActorLoader(), _root);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = 34 });
        var definition = new CharacterClassRegistry().Get(PlayerClass.Fighter).Definition;
        var sword = new ItemDefinition(ItemCodes.Parse(Sword), Sword, ItemKind.Axe) { CatalogKey = Sword, EquipSlot = EquipmentSlot.Weapon };
        var pending = new List<Task>();
        for (var level = HeldItemGlow.FirstGlowingAge; level <= HeldItemGlow.LastGlowingAge; level++)
        {
            var index = level - HeldItemGlow.FirstGlowingAge;
            var visuals = EquipmentVisuals.Apply(definition.DefaultVisuals, "DA108", Sword, "", false, definition.MotionJobMask);
            var world = Matrix.CreateTranslation(new Vector3((index % Columns - (Columns - 1) / 2f) * 100, 0, index / Columns * 150));
            _avatars.Add((visuals, world, HeldItemGlow.For(new ItemInstance(sword) { UpgradeLevel = level })));
            pending.Add(_composer.PrepareAsync(_root, visuals, 0));
        }

        _preparation = Task.WhenAll(pending);
    }

    protected override void Update(GameTime gameTime)
    {
        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Aging capture preparation failed.", _preparation.Exception);
        }

        if (_handles.Count < _avatars.Count)
        {
            foreach (var avatar in _avatars)
            {
                _handles.Add(_composer.Compose(_root, avatar.Visuals, 0, avatar.World, MotionCode.Stand));
            }

            return;
        }

        _frames++;
        for (var index = 0; index < _handles.Count; index++)
        {
            _looks.PaintGlow(_handles[index].Weapon, _avatars[index].Glow, (long)ClockMs);
        }

        _looks.EndGlowFrame();
        _renderer.Update(new GameTime(TimeSpan.FromMilliseconds(ClockMs), TimeSpan.FromMilliseconds(FrameMs)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frames < WarmupFrames || _capture >= CaptureMs.Length || ClockMs < CaptureMs[_capture])
        {
            return;
        }

        using var target = new RenderTarget2D(GraphicsDevice, 1600, 900, false, SurfaceFormat.Color, DepthFormat.Depth24);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(20, 22, 28));
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 330, 640), new Vector3(0, 30, 170));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var path = Path.Combine(_output, $"aging-{CaptureMs[_capture]:0000}.png");
        using (var stream = File.Create(path))
        {
            target.SaveAsPng(stream, 1600, 900);
        }

        Console.WriteLine(path);
        if (++_capture == CaptureMs.Length)
        {
            Exit();
        }
    }

    protected override void UnloadContent() => _renderer.Dispose();
}
