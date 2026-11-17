using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI.Npc;

// The boss arena's timer while the player stands inside the arena (King Fury or Hagios, BossArena): under the top of the
// screen, the event's name over the time left until it starts, and once it runs the time it has run and the boss's life
// bar. The server sends the arena's state to its field's players when it changes (once a second at most); its texts are
// made then.
public sealed class HudArenaTimer
{
    private const string BarArt = HudArenaEvent.Folder + "bar.png";
    public static readonly string[] Art = [BarArt];

    private const int TimeWidth = 208;
    private const int TimeHeight = 20;
    private const int TimeTop = 100;
    private const int LifeWidth = 332;
    private const int LifeHeight = 21;
    private static readonly Color LifeFill = new Color(170, 30, 25) * 0.9f;

    private ArenaStatusPacket _status = ArenaStatusPacket.Hidden;
    private string _name = "";
    private string _time = "";
    private string _life = "";

    public void Observe(ArenaStatusPacket status)
    {
        _status = status;
        if (!status.Shown)
        {
            return;
        }

        _name = status.EventId < BossArena.Events.Count ? BossArena.Events[status.EventId].Name : "";
        var span = TimeSpan.FromSeconds(status.Seconds);
        var clock = $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        _time = ClientText.Format(status.Running ? "Arena.Time" : "Arena.TimeLeft", clock);
        _life = status.Running ? $"{status.BossLife:N0} / {status.BossMaxLife:N0}" : "";
    }

    // Drawn while the server shows the arena and the player stands inside it.
    public void Draw(HudPainter painter, HudLayout layout, int fieldCode, Vector3 player)
    {
        if (!_status.Shown || fieldCode != BossArena.FieldCode || !BossArena.Inside(player.X, player.Z))
        {
            return;
        }

        var center = layout.Width / 2;
        painter.TextCentered(_name, center - TimeWidth, center + TimeWidth, TimeTop - 20, HudTextColors.BoxTitle);
        painter.Sprite(HudArenaEvent.CountdownArt, center - TimeWidth / 2, TimeTop);
        painter.TextCentered(_time, center - TimeWidth / 2, center + TimeWidth / 2, TimeTop + 3, new Color(220, 135, 135));
        if (!_status.Running || _status.BossMaxLife <= 0)
        {
            return;
        }

        var bar = new Rectangle(center - LifeWidth / 2, TimeTop + TimeHeight + 6, LifeWidth, LifeHeight);
        painter.Sprite(BarArt, bar.X, bar.Y);
        painter.Fill(new Rectangle(bar.X + 3, bar.Y + 3, (int)((long)(bar.Width - 6) * Math.Clamp(_status.BossLife, 0, _status.BossMaxLife) / _status.BossMaxLife),
            bar.Height - 6), LifeFill);
        painter.TextCentered(_life, bar.Left, bar.Right, bar.Y + 4, Color.White);
    }
}
