using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Screens.Opening;

public sealed class OpeningScreen : GameScreen
{
    private OpeningCinematic _cinematic;
    private LoginVideo _video;
    private OpeningLoginController _loginController;

    private LoginAudio _audio;
    private KeyboardState _lastKeyboard;
    private string _status;

    private bool _loginRevealed;
    private PristonTale.Engine3D.Rendering.ShaderPreload _shaders;

    public OpeningScreen(ScreenContext context) : base(context)
    {
        context.Discord?.Show(Discord.DiscordPresenceText.Menu(context.Discord.StartUnixSeconds, "Logging in"));
    }

    public override string StatusText => _status;

    // The login screen plays its background video under the form from the start; without the video the opening
    // cinematic plays and any key or its end reveals the form.
    public override void Enter()
    {
        _audio = LoginAudio.Opening(Context.Audio);
        _loginController = new OpeningLoginController(Context, _audio);
        _shaders = new PristonTale.Engine3D.Rendering.ShaderPreload(Context.Content, Context.Log);
        PristonTale.Engine3D.Rendering.Atmosphere.PristonCloudNoise.Prepare();
        _video = LoginVideo.Open(Context.GraphicsDevice, Context.AssetRoot, Context.Log);
        if (_video is not null)
        {
            _loginRevealed = true;
            _status = ClientText.Format("Status.Opening", LoginVideo.AssetPath);
            return;
        }

        _cinematic = new OpeningCinematic(
            Context.GraphicsDevice,
            Context.Content,
            Context.Log,
            AssetPath,
            Context.AssetRoot,
            Context.Sky,
            Context.Presentation,
            Context.Effects);
        _cinematic.Load();
        _status = ClientText.Format("Status.Opening", _cinematic.StatusSuffix);
    }

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _audio.Update(dt);
        _shaders.Step();

        _cinematic?.Update(gameTime);
        _video?.Update(gameTime.ElapsedGameTime.TotalSeconds);

        var keyboard = Keyboard.GetState();
        if (Context.Presentation.AwaitingConfirmation)
        {
            _lastKeyboard = keyboard;
            return;
        }

        if (!_loginRevealed)
        {
            if (AnyKeyPressed(keyboard, _lastKeyboard) || _cinematic?.IsOver != false)
                _loginRevealed = true;
        }
        else if (_loginController.Update(keyboard, _lastKeyboard, dt) == OpeningLoginController.LoginResult.Succeeded)
        {
            Context.RequestScreen(new CharacterSelectScreen(Context));
        }

        _lastKeyboard = keyboard;
    }

    private static bool AnyKeyPressed(in KeyboardState current, in KeyboardState previous)
    {
        foreach (var key in current.GetPressedKeys())
            if (!previous.IsKeyDown(key))
                return true;
        return false;
    }

    public override void Draw()
    {
        var viewport = Context.GraphicsDevice.Viewport;
        if (_video is not null)
        {
            Context.GraphicsDevice.Clear(Color.Black);
            _video.Draw(Context.SpriteBatch, viewport);
        }
        else
        {
            _cinematic.Draw();
        }

        if (!_loginRevealed)
        {
            Context.SpriteBatch.Begin();
            Context.SpriteBatch.End();
        }
        else
        {
            _loginController.Draw(viewport);
        }
    }

    public override bool HandleEscape() => _loginController?.HandleEscape() == true;

    public override void Dispose()
    {
        _video?.Dispose();
        _loginController?.Dispose();
        _audio?.Dispose();
        _cinematic?.Dispose();
    }
}
