using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// World buffs on the tick (WorldBuffs): one end time per buff, every player carrying the mask of the running ones, so a
// player who enters later has them for the same time left. WorldBuffState answers them for saving and for the encoder.
public sealed partial class WorldServer
{
    private static readonly WorldBuffEnd[] NoWorldBuffs = [];
    private readonly long[] _worldBuffEnds = new long[WorldBuffs.All.Count + 1];
    private int _worldBuffMask;
    private long _worldBuffCheckedAt;

    // The running buffs, a new array only when they change.
    public WorldBuffEnd[] WorldBuffState { get; private set; } = NoWorldBuffs;

    public Task RestoreWorldBuffsAsync(IReadOnlyList<WorldBuffEnd> ends) =>
        EnqueueAsync(NoOwner, () =>
        {
            foreach (var end in ends)
            {
                if (WorldBuffs.Get(end.Kind) is not null)
                {
                    _worldBuffEnds[end.Kind] = end.EndSeconds;
                }
            }

            RefreshWorldBuffs(_clock.GetUtcNow().ToUnixTimeSeconds());
            return true;
        }, waitForCapacity: true);

    // A game master's start of one buff, or all of them (kind 0), for that many seconds; 0 ends them.
    public Task<string> SetWorldBuffsAsync(int kind, int seconds) =>
        EnqueueAsync(NoOwner, () =>
        {
            var now = _clock.GetUtcNow().ToUnixTimeSeconds();
            foreach (var buff in WorldBuffs.All)
            {
                if (kind == 0 || (int)buff.Kind == kind)
                {
                    _worldBuffEnds[(int)buff.Kind] = seconds > 0 ? now + seconds : 0;
                }
            }

            RefreshWorldBuffs(now);
            return seconds > 0 ? "World buffs started" : "World buffs ended";
        }, waitForCapacity: true);

    private void StartWorldBuff(string player, WorldBuff buff, int seconds, long now)
    {
        _worldBuffEnds[(int)buff.Kind] = Math.Max(_worldBuffEnds[(int)buff.Kind], now) + seconds;
        RefreshWorldBuffs(now);
        foreach (var id in _connections.Keys)
        {
            Notify(id, ChatKind.Notice, GameText.Plain($"{player} has activated {buff.Name}!"));
        }
    }

    // Tick only, once a second: uses of buff items, expiries, and the mask of players who just entered.
    private void UpdateWorldBuffs()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (now == _worldBuffCheckedAt)
        {
            return;
        }

        _worldBuffCheckedAt = now;
        foreach (var connection in _connections.Values)
        {
            var used = connection.Player.PendingWorldBuffs;
            foreach (var argument in used)
            {
                var (kind, seconds) = WorldBuffs.FromArgument(argument);
                if (WorldBuffs.Get(kind) is { } buff)
                {
                    StartWorldBuff(connection.Player.Character.Name, buff, seconds, now);
                }
            }

            used.Clear();
        }

        foreach (var buff in WorldBuffs.All)
        {
            var end = _worldBuffEnds[(int)buff.Kind];
            if (end != 0 && end <= now)
            {
                RefreshWorldBuffs(now);
                break;
            }
        }

        foreach (var connection in _connections.Values)
        {
            connection.Player.Character.Premium.WorldBuffMask = _worldBuffMask;
        }
    }

    private void RefreshWorldBuffs(long now)
    {
        var mask = 0;
        var running = new List<WorldBuffEnd>(WorldBuffs.All.Count);
        foreach (var buff in WorldBuffs.All)
        {
            var kind = (int)buff.Kind;
            if (_worldBuffEnds[kind] > now)
            {
                mask |= buff.Bit;
                running.Add(new WorldBuffEnd(kind, _worldBuffEnds[kind]));
            }
            else
            {
                _worldBuffEnds[kind] = 0;
            }
        }

        _worldBuffMask = mask;
        WorldBuffState = running.ToArray();
    }
}
