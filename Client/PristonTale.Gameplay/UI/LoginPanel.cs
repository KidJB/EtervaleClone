using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public enum LoginIntent
{
    None,
    SubmitLogin,
    OpenSettings,
    Exit
}

// The login screen's controls over its background: game settings, the recent accounts list (folded by its tab), the
// logo, the account and password fields, Log in and Exit. Hud/Login holds their art; without it they draw as framed
// boxes from the bootstrap atlas.
public sealed class LoginPanel
{
    private enum FocusTarget
    {
        Account,
        Password,
        Login
    }

    private readonly record struct ButtonArt(string Normal, string Hover, string Pressed)
    {
        public static ButtonArt Of(string name) => new(Folder + name + "_normal.png", Folder + name + "_hover.png", Folder + name + "_pressed.png");
    }

    private const string Folder = "Hud/Login/";
    private const string LogoArt = Folder + "logo.png";
    private const string PanelArt = Folder + "panel_recent_accounts.png";
    private const string RowHoverArt = Folder + "row_hover.png";
    private const string RowSelectedArt = Folder + "row_selected.png";
    private const string TabArt = Folder + "tab_normal.png";
    private const string TabHoverArt = Folder + "tab_hover.png";
    private const string FieldArt = Folder + "field_normal.png";
    private const string FieldFocusArt = Folder + "field_focus.png";
    private const string AccountEmptyArt = Folder + "field_account_placeholder.png";
    private const string PasswordEmptyArt = Folder + "field_password_placeholder.png";
    private const string LoginDisabledArt = Folder + "btn_login_disabled.png";
    private static readonly ButtonArt LoginArt = ButtonArt.Of("btn_login");
    private static readonly ButtonArt SettingsArt = ButtonArt.Of("btn_settings");
    private static readonly ButtonArt ExitArt = ButtonArt.Of("btn_exit");

    public static readonly string[] ArtPaths =
    [
        LogoArt, PanelArt, RowHoverArt, RowSelectedArt, TabArt, TabHoverArt, FieldArt, FieldFocusArt, AccountEmptyArt, PasswordEmptyArt, LoginDisabledArt,
        LoginArt.Normal, LoginArt.Hover, LoginArt.Pressed, SettingsArt.Normal, SettingsArt.Hover, SettingsArt.Pressed,
        ExitArt.Normal, ExitArt.Hover, ExitArt.Pressed
    ];

    private static readonly Color FieldText = new(244, 226, 179);
    private const string StoredPassword = "********";
    private static readonly Color RowDetail = new(230, 207, 192);

    private readonly HudCanvas _canvas;
    private readonly SpriteBatch _batch;
    private MouseState _previousMouse;
    private FocusTarget _focus;

    public LoginPanel(SpriteBatch batch, SpriteFont font, HudAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if (!atlas.IsReady)
        {
            throw new InvalidOperationException("Login requires interface resources initialized before the first screen.");
        }

        _batch = batch;
        _canvas = new HudCanvas(batch, font, atlas);
    }

    public TextInputField Account { get; } = new() { MaxLength = 20, Focused = true };
    public TextInputField Password { get; } = new() { MaxLength = 20, Masked = true };
    public string Status { get; set; } = string.Empty;
    public bool InputEnabled { get; set; } = true;

    // The Hud/Login images by path, or null while one is missing or loading.
    public Func<string, Texture2D> Art { get; set; }

    // The accounts that logged in on this computer, newest first; a click fills the account field.
    public IReadOnlyList<string> Recent { get; set; } = [];

    // Under each recent account, the character it last played ("Hero - Lv.42 Fighter"), or empty.
    public IReadOnlyList<string> RecentDetails { get; set; } = [];
    public bool RecentOpen { get; set; } = true;

    // "Remember me": the next login asks the server for a token. RememberedAccount is the account a token is kept for;
    // with its name typed and the password left empty the field shows as filled and the token logs in.
    public bool RememberMe { get; set; }
    public string RememberedAccount { get; set; } = string.Empty;
    public bool UsesToken => Password.Text.Length == 0 && RememberedAccount.Length > 0
        && string.Equals(RememberedAccount, Account.Text, StringComparison.OrdinalIgnoreCase);

    // typed: the characters the window typed since the last frame (TypedText), for the focused field.
    public LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, ReadOnlySpan<char> typed, double dtSeconds, Viewport viewport)
    {
        return Update(keyboard, previousKeyboard, Mouse.GetState(), typed, dtSeconds, viewport);
    }

    internal LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds,
        Viewport viewport)
    {
        var intent = LoginIntent.None;
        if (InputEnabled)
        {
            var layout = LoginLayout.Create(viewport.Width, viewport.Height);
            var cursor = layout.Pointer(mouse.Position);
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (Pressed(keyboard, previousKeyboard, Keys.Tab))
            {
                var direction = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? -1 : 1;
                SetFocus((FocusTarget)(((int)_focus + direction + 3) % 3));
            }

            if (clicked)
            {
                intent = Click(layout, cursor);
            }

            Account.Update(typed, dtSeconds);
            Password.Update(typed, dtSeconds);
            if (Pressed(keyboard, previousKeyboard, Keys.Enter)
                || _focus >= FocusTarget.Login && Pressed(keyboard, previousKeyboard, Keys.Space))
            {
                intent = LoginIntent.SubmitLogin;
            }
        }

        _previousMouse = mouse;
        return intent;
    }

    private LoginIntent Click(LoginLayout layout, Point cursor)
    {
        if (layout.AccountField.Contains(cursor))
        {
            SetFocus(FocusTarget.Account);
        }
        else if (layout.PasswordField.Contains(cursor))
        {
            SetFocus(FocusTarget.Password);
        }
        else if (layout.Login.Contains(cursor))
        {
            SetFocus(FocusTarget.Login);
            return LoginIntent.SubmitLogin;
        }
        else if (layout.Remember.Contains(cursor))
        {
            RememberMe = !RememberMe;
        }
        else if (layout.Settings.Contains(cursor))
        {
            return LoginIntent.OpenSettings;
        }
        else if (layout.Exit.Contains(cursor))
        {
            return LoginIntent.Exit;
        }
        else if (TabBox(layout).Contains(cursor))
        {
            RecentOpen = !RecentOpen;
        }
        else if (RecentOpen && RecentAt(layout, cursor) is var row and >= 0)
        {
            Account.Set(Recent[row]);
            Password.Clear();
            SetFocus(FocusTarget.Password);
        }

        return LoginIntent.None;
    }

    // The tab sits at the panel's edge, or at the screen's left while the panel is folded away.
    private Rectangle TabBox(LoginLayout layout)
    {
        var tab = layout.Tab;
        return RecentOpen ? tab : new Rectangle(layout.Panel.X, tab.Y, tab.Width, tab.Height);
    }

    private int RecentAt(LoginLayout layout, Point cursor)
    {
        for (var index = 0; index < Math.Min(Recent.Count, LoginLayout.RecentRows); index++)
        {
            if (layout.RecentRow(index).Contains(cursor))
            {
                return index;
            }
        }

        return -1;
    }

    private void SetFocus(FocusTarget focus)
    {
        _focus = focus;
        Account.Focused = focus == FocusTarget.Account;
        Password.Focused = focus == FocusTarget.Password;
    }

    public void Draw(Viewport viewport)
    {
        var layout = LoginLayout.Create(viewport.Width, viewport.Height);
        var cursor = layout.Pointer(_previousMouse.Position);
        var down = _previousMouse.LeftButton == ButtonState.Pressed;
        _canvas.Begin(layout.Scale);
        DrawArt(LogoArt, layout.Logo, null);
        DrawButton(SettingsArt, layout.Settings, cursor, down, ClientText.Get("Login.Settings"));
        DrawRecent(layout, cursor);
        DrawField(Account, layout.AccountField, layout.Account, AccountEmptyArt, ClientText.Get("Login.Account"));
        DrawField(Password, layout.PasswordField, layout.Password, PasswordEmptyArt, ClientText.Get("Login.Password"));
        if (InputEnabled)
        {
            DrawButton(LoginArt, layout.Login, cursor, down, ClientText.Get("Login.LogIn"), _focus == FocusTarget.Login);
        }
        else
        {
            DrawArt(LoginDisabledArt, layout.Login, "Button", new Color(155, 155, 155));
        }

        DrawButton(ExitArt, layout.Exit, cursor, down, ClientText.Get("Login.Exit"));
        DrawRemember(layout, cursor);
        _canvas.Text(Status, layout.Status, HudCanvas.Gold, 16f, centered: true);
        _canvas.End();
    }

    private void DrawRecent(LoginLayout layout, Point cursor)
    {
        var tab = TabBox(layout);
        DrawArt(tab.Contains(cursor) ? TabHoverArt : TabArt, tab, "Button");
        if (!RecentOpen)
        {
            return;
        }

        DrawArt(PanelArt, layout.Recent, "Panel");
        var account = Account.Text;
        for (var index = 0; index < Math.Min(Recent.Count, LoginLayout.RecentRows); index++)
        {
            var row = layout.RecentRow(index);
            var name = Recent[index];
            if (string.Equals(name, account, StringComparison.OrdinalIgnoreCase))
            {
                DrawArt(RowSelectedArt, row, null);
            }
            else if (row.Contains(cursor))
            {
                DrawArt(RowHoverArt, row, null);
            }

            var detail = index < RecentDetails.Count ? RecentDetails[index] : string.Empty;
            if (detail.Length == 0)
            {
                _canvas.Text(name, new Rectangle(row.X + 8, row.Y + 9, row.Width - 16, 22), FieldText, 16f);
                continue;
            }

            _canvas.Text(name, new Rectangle(row.X + 8, row.Y + 2, row.Width - 16, 20), FieldText, 15f);
            _canvas.Text(detail, new Rectangle(row.X + 8, row.Y + 21, row.Width - 16, 16), RowDetail, 12f);
        }
    }

    private void DrawRemember(LoginLayout layout, Point cursor)
    {
        var hovered = InputEnabled && layout.Remember.Contains(cursor);
        _canvas.Frame(layout.RememberBox, "Inset", 4, hovered ? HudCanvas.Gold : null);
        if (RememberMe)
        {
            var box = layout.RememberBox;
            _canvas.Frame(new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8), "Button", 2, HudCanvas.Gold);
        }

        _canvas.Text(ClientText.Get("Login.RememberMe"), layout.RememberLabel, hovered ? Color.White : FieldText, 15f);
    }

    private void DrawField(TextInputField field, Rectangle box, Rectangle text, string placeholder, string label)
    {
        var focused = InputEnabled && field.Focused;
        var stored = ReferenceEquals(field, Password) && UsesToken;
        var empty = field.Text.Length == 0 && !stored;
        var art = focused ? FieldFocusArt : empty ? placeholder : FieldArt;
        if (!DrawArt(art, box, "Inset", focused ? HudCanvas.Gold : null) && empty && !focused)
        {
            _canvas.Text(label, text, HudCanvas.Muted, 16f, centered: true);
        }

        var shown = stored ? StoredPassword : field.DisplayText;
        if (focused && field.CaretVisible)
        {
            shown += "_";
        }

        _canvas.Text(shown, text, InputEnabled ? FieldText : HudCanvas.Muted, 16f, centered: true);
    }

    private void DrawButton(ButtonArt art, Rectangle box, Point cursor, bool down, string label, bool focused = false)
    {
        var hovered = InputEnabled && box.Contains(cursor);
        var path = hovered && down ? art.Pressed : hovered || focused && InputEnabled ? art.Hover : art.Normal;
        if (!DrawArt(path, box, "Button", hovered ? new Color(255, 230, 180) : null))
        {
            _canvas.Text(label, box, hovered ? Color.White : HudCanvas.Gold, 18f, centered: true);
        }
    }

    // Draws an image of Hud/Login, or the atlas frame standing in for it (none when fallback is null); false when the
    // image was missing.
    private bool DrawArt(string path, Rectangle box, string fallback, Color? tint = null)
    {
        if (Art?.Invoke(path) is { } texture)
        {
            _batch.Draw(texture, box, Color.White);
            return true;
        }

        if (fallback is not null)
        {
            _canvas.Frame(box, fallback, 8, tint);
        }

        return false;
    }

    private static bool Pressed(in KeyboardState current, in KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }
}
