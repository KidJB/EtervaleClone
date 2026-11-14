using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace PristonTale.Gameplay.UI.Rendering;

// A window the player drags by a strip along its top: a press in the strip that then moves more than a few units moves
// the window (its Offset from its own place), kept inside the screen; a press that does not move stays a click.
public sealed class HudWindowDrag
{
    private const int Threshold = 4;
    private bool _pressed;
    private bool _dragging;
    private Point _from;
    private Point _start;

    public Point Offset { get; private set; }

    public bool Dragging => _dragging;

    // home: where the window sits without an offset; strip: its draggable strip there.
    public void Update(HudLayout layout, Rectangle home, Rectangle strip, MouseState mouse, MouseState previous, Point pointer)
    {
        var down = mouse.LeftButton == ButtonState.Pressed;
        if (down && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Moved(strip).Contains(pointer);
            _dragging = false;
            (_from, _start) = (pointer, Offset);
            return;
        }

        if (!down)
        {
            _pressed = false;
            _dragging = false;
            return;
        }

        if (_pressed && (_dragging || Math.Abs(pointer.X - _from.X) + Math.Abs(pointer.Y - _from.Y) > Threshold))
        {
            _dragging = true;
            Offset = new Point(Math.Clamp(_start.X + pointer.X - _from.X, -home.X, Math.Max(-home.X, layout.Width - home.Right)),
                Math.Clamp(_start.Y + pointer.Y - _from.Y, -home.Y, Math.Max(-home.Y, layout.Height - home.Bottom)));
        }
    }

    public Rectangle Moved(Rectangle bounds) => new(bounds.X + Offset.X, bounds.Y + Offset.Y, bounds.Width, bounds.Height);
}
