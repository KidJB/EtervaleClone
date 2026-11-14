using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.Localization;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }

    private readonly ScreenContext _context;
    private readonly LoginAudio _audio;
    private readonly LoginPanel _login;
    private readonly HudSprites _sprites;
    private readonly HudPainter _painter;
    private readonly HudSettingsPanel _settings;
    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private LoginMode _loginMode;
    private CancellationTokenSource _loginCts;
    private bool _settingsOpen;
    private MouseState _previousMouse;

    public OpeningLoginController(ScreenContext context, LoginAudio audio)
    {
        _context = context;
        _audio = audio;
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(LoginPanel.ArtPaths);
        _sprites.Request(HudSettingsPanel.Art);
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _settings = new HudSettingsPanel(context.Presentation);
        _login = new LoginPanel(context.SpriteBatch, context.Font, context.UiAtlas)
        {
            Art = _sprites.Get,
            Recent = context.Presentation.Requested.RecentAccounts ?? [],
            RecentDetails = (context.Presentation.Requested.RecentAccounts ?? []).Select(account => Detail(context.Presentation.Requested.LastCharacter(account))).ToArray(),
            RememberedAccount = context.Presentation.Requested.RememberToken.Length > 0 ? context.Presentation.Requested.RememberedAccount : string.Empty
        };
        _login.RememberMe = _login.RememberedAccount.Length > 0;
        if (_login.Recent.Count > 0)
        {
            _login.Account.Set(_login.Recent[0]);
        }
    }

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, float dt)
    {
        _sprites.Update();
        var typed = _context.TypedText.Drain();
        var mouse = Mouse.GetState();
        if (_settingsOpen)
        {
            UpdateSettings(mouse);
        }
        else if (_phase == Phase.Login)
        {
            switch (_login.Update(keyboard, lastKeyboard, typed, dt, _context.GraphicsDevice.Viewport))
            {
                case LoginIntent.SubmitLogin:
                    BeginLogin();
                    break;
                case LoginIntent.OpenSettings:
                    _settingsOpen = true;
                    _settings.Opened();
                    break;
                case LoginIntent.Exit:
                    _context.ExitRequested = true;
                    break;
            }

            // Unticking Remember me forgets the kept token at once.
            if (!_login.RememberMe && _login.RememberedAccount.Length > 0)
            {
                Forget();
            }
        }

        _previousMouse = mouse;
        return _phase == Phase.Authenticating ? PollLogin() : LoginResult.None;
    }

    // The game settings window, the same as in play, over the login screen.
    private void UpdateSettings(MouseState mouse)
    {
        if (mouse.LeftButton != ButtonState.Pressed || _previousMouse.LeftButton != ButtonState.Released)
        {
            return;
        }

        var pointer = HudLayout.Of(_context.GraphicsDevice.Viewport).ToLogical(mouse.Position);
        if (HudSettingsPanel.Closes(pointer))
        {
            _settingsOpen = false;
        }
        else if (HudSettingsPanel.Box.Contains(pointer))
        {
            var keyboard = Keyboard.GetState();
            _settings.Click(pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        }
    }

    // Escape closes the settings window instead of the game.
    public bool HandleEscape()
    {
        if (!_settingsOpen)
        {
            return false;
        }

        _settingsOpen = false;
        return true;
    }

    private void BeginLogin()
    {
        var token = _login.UsesToken;
        if (string.IsNullOrWhiteSpace(_login.Account.Text) || string.IsNullOrEmpty(_login.Password.Text) && !token)
        {
            _login.Status = "Enter your account and password.";
            return;
        }

        _audio.Play(LoginCue.Login);
        _login.Status = "Connecting…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        if (_context.Backend.RequiresNewConnection)
        {
            _context.RenewBackend();
        }

        _loginMode = token ? LoginMode.Token : _login.RememberMe ? LoginMode.Remember : LoginMode.Password;
        var secret = token ? _context.Presentation.Requested.RememberToken : _login.Password.Text;
        _loginTask = _context.Backend.LoginAsync(_login.Account.Text, secret, _context.Presentation.Requested.Language, _loginMode, _loginCts.Token);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
        {
            return LoginResult.None;
        }

        var succeeded = _loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok;
        if (!succeeded)
        {
            _login.Status = _loginTask.IsCompletedSuccessfully
                ? ClientText.Get("Login.Failure." + _loginTask.Result.Failure)
                : ClientText.Get("Login.Unreachable");
            if (_loginMode == LoginMode.Token && _loginTask.IsCompletedSuccessfully && _loginTask.Result.Failure == LoginFailure.BadCredentials)
            {
                Forget();
                _login.RememberMe = true;
                _login.Status = ClientText.Get("Login.TokenExpired");
            }
            _login.InputEnabled = true;
            _phase = Phase.Login;
        }
        else
        {
            var settings = _context.Presentation.Requested.WithRecentAccount(_login.Account.Text);
            var kept = _loginTask.Result.RememberToken;
            if (_loginMode == LoginMode.Remember && kept.Length > 0)
            {
                settings = settings.WithRemembered(_login.Account.Text, kept);
            }
            else if (_loginMode == LoginMode.Password && settings.Remembers(_login.Account.Text))
            {
                settings = settings.WithoutRemembered();
            }

            _context.Presentation.Requested = settings;
        }

        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return succeeded ? LoginResult.Succeeded : LoginResult.None;
    }

    // "Hero - Lv.42 Fighter" for the character an account last played, or empty.
    internal static string Detail(RecentCharacter character) =>
        character is null ? string.Empty : ClientText.Format("Login.RecentCharacter", character.Name, character.Level, character.ClassName);

    private void Forget()
    {
        _login.RememberedAccount = string.Empty;
        _context.Presentation.Requested = _context.Presentation.Requested.WithoutRemembered();
    }

    public void Draw(Viewport viewport)
    {
        _login.Draw(viewport);
        if (!_settingsOpen)
        {
            return;
        }

        var layout = HudLayout.Of(viewport);
        _painter.Begin(layout);
        _settings.Draw(_painter, layout.ToLogical(Mouse.GetState().Position));
        _painter.End();
    }

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = ObserveLoginAsync(_loginTask);
        }

        _painter.Dispose();
        _sprites.Dispose();
    }

    private async Task ObserveLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Error($"[Login] canceled request failed: {ex}");
        }
    }
}
