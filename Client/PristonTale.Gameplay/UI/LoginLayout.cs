using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

// The login screen in its 1024x768 design space (the Hud/Login art's top-left corners), scaled to fit the window and
// centred; Panel is that design area in scaled units.
public readonly record struct LoginLayout(float Scale, Rectangle Panel)
{
    public const int DesignWidth = 1024;
    public const int DesignHeight = 768;
    public const int RecentRowHeight = 40;
    public const int RecentRows = 11;

    public Rectangle Settings => At(428, 28, 168, 50);
    public Rectangle Recent => At(6, 144, 232, 528);
    public Rectangle Tab => At(228, 370, 30, 70);
    public Rectangle Logo => At(272, 112, 480, 300);
    public Rectangle AccountField => At(414, 432, 196, 62);
    public Rectangle PasswordField => At(414, 492, 196, 62);
    public Rectangle Account => At(438, 451, 148, 24);
    public Rectangle Password => At(438, 511, 148, 24);
    public Rectangle Login => At(446, 552, 132, 58);
    public Rectangle Remember => At(446, 614, 132, 22);
    public Rectangle RememberBox => At(446, 617, 16, 16);
    public Rectangle RememberLabel => At(468, 614, 110, 22);
    public Rectangle Status => At(312, 640, 400, 24);
    public Rectangle Exit => At(812, 674, 84, 56);

    public Rectangle RecentRow(int index) => At(24, 196 + index * RecentRowHeight, 196, RecentRowHeight);

    public static LoginLayout Create(int width, int height)
    {
        var scale = Math.Max(0.1f, Math.Min((float)width / DesignWidth, (float)height / DesignHeight));
        var x = (int)((width / scale - DesignWidth) / 2);
        var y = (int)((height / scale - DesignHeight) / 2);
        return new LoginLayout(scale, new Rectangle(x, y, DesignWidth, DesignHeight));
    }

    public Point Pointer(Point drawable) => new((int)(drawable.X / Scale), (int)(drawable.Y / Scale));

    private Rectangle At(int x, int y, int width, int height) => new(Panel.X + x, Panel.Y + y, width, height);
}
