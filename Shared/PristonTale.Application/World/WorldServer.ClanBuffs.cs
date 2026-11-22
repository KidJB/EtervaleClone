using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Clan world buffs on the tick (ClanBuffs): one end time per clan and buff. Each second every player carries the mask of
// its own clan's running buffs (WorldPlayer.Clan, which the clan service keeps), so joining, leaving and entering the world
// change them on the next pass. ClanBuffState answers them for saving; each clan's running buffs are one array for the
// encoder until they change.
public sealed partial class WorldServer
{
    private static readonly ClanBuffEnd[] NoClanBuffs = [];
    private readonly Dictionary<long, long[]> _clanBuffEnds = new();
    private readonly Dictionary<long, WorldBuffEnd[]> _clanBuffViews = new();
    private readonly Dictionary<long, int> _clanBuffMasks = new();

    // Every clan's running buffs, a new array only when they change.
    public ClanBuffEnd[] ClanBuffState { get; private set; } = NoClanBuffs;

    public Task RestoreClanBuffsAsync(IReadOnlyList<ClanBuffEnd> ends) =>
        EnqueueAsync(NoOwner, () =>
        {
            foreach (var end in ends)
            {
                // A buff started before the stored ones were read keeps the later end.
                if (end.ClanId > 0 && WorldBuffs.Get(end.Kind) is not null)
                {
                    var clanEnds = EndsOf(end.ClanId);
                    clanEnds[end.Kind] = Math.Max(clanEnds[end.Kind], end.EndSeconds);
                }
            }

            RefreshClanBuffs(_clock.GetUtcNow().ToUnixTimeSeconds());
            return true;
        }, waitForCapacity: true);

    // A dissolved clan's buffs end and its warehouse copy goes.
    public Task ForgetClanAsync(long clanId) =>
        EnqueueAsync(NoOwner, () =>
        {
            _clanWarehouses.Remove(clanId);
            if (_clanBuffEnds.Remove(clanId))
            {
                RefreshClanBuffs(_clock.GetUtcNow().ToUnixTimeSeconds());
            }

            return true;
        }, waitForCapacity: true);

    private WorldBuffEnd[] ClanBuffsOf(long clanId) => _clanBuffViews.GetValueOrDefault(clanId, NoWorldBuffs);

    private int ClanBuffMaskOf(long clanId) => _clanBuffMasks.GetValueOrDefault(clanId);

    private long[] EndsOf(long clanId)
    {
        if (!_clanBuffEnds.TryGetValue(clanId, out var ends))
        {
            ends = new long[WorldBuffs.All.Count + 1];
            _clanBuffEnds[clanId] = ends;
        }

        return ends;
    }

    // Tick only, from UpdateWorldBuffs once a second: uses of clan buff items and expiries.
    private void UpdateClanBuffs(long now)
    {
        var changed = false;
        foreach (var connection in _connections.Values)
        {
            var used = connection.Player.PendingClanBuffs;
            foreach (var (clanId, argument) in used)
            {
                changed |= StartClanBuff(clanId, connection.Player.Character.Name, argument, now);
            }

            used.Clear();
        }

        if (!changed)
        {
            foreach (var ends in _clanBuffEnds.Values)
            {
                foreach (var end in ends)
                {
                    if (end != 0 && end <= now)
                    {
                        changed = true;
                        break;
                    }
                }
            }
        }

        if (changed)
        {
            RefreshClanBuffs(now);
        }
    }

    // A clan buff paid from the clan coin treasury (ClanAppService.SpendCoinsAsync), started at once.
    public Task StartClanBuffAsync(long clanId, string player, int argument) =>
        EnqueueAsync(NoOwner, () =>
        {
            var now = _clock.GetUtcNow().ToUnixTimeSeconds();
            if (StartClanBuff(clanId, player, argument, now))
            {
                RefreshClanBuffs(now);
            }

            return true;
        }, waitForCapacity: true);

    // Tick only: the buff of the argument (WorldBuffs.Argument) added to what the clan has left of it, told to its members.
    private bool StartClanBuff(long clanId, string player, int argument, long now)
    {
        var (kind, seconds) = WorldBuffs.FromArgument(argument);
        if (WorldBuffs.Get(kind) is not { } buff)
        {
            return false;
        }

        var ends = EndsOf(clanId);
        ends[kind] = WorldBuffs.Extend(ends[kind], now, seconds);
        AnnounceClanBuff(clanId, player, buff);
        return true;
    }

    private void AnnounceClanBuff(long clanId, string player, WorldBuff buff)
    {
        foreach (var (id, connection) in _connections)
        {
            if (connection.Player.Clan?.ClanId == clanId)
            {
                Notify(id, ChatKind.Notice, GameText.Of(GameTextId.ClanBuffActivated, player, buff.Name));
            }
        }
    }

    private void RefreshClanBuffs(long now)
    {
        var state = new List<ClanBuffEnd>();
        _clanBuffViews.Clear();
        _clanBuffMasks.Clear();
        foreach (var (clanId, ends) in _clanBuffEnds.ToArray())
        {
            var running = new List<WorldBuffEnd>(WorldBuffs.All.Count);
            foreach (var buff in WorldBuffs.All)
            {
                var kind = (int)buff.Kind;
                if (ends[kind] > now)
                {
                    running.Add(new WorldBuffEnd(kind, ends[kind]));
                    state.Add(new ClanBuffEnd(clanId, kind, ends[kind]));
                }
                else
                {
                    ends[kind] = 0;
                }
            }

            if (running.Count == 0)
            {
                _clanBuffEnds.Remove(clanId);
                continue;
            }

            _clanBuffViews[clanId] = running.ToArray();
            _clanBuffMasks[clanId] = ClanBuffs.Mask(ends, now);
        }

        ClanBuffState = state.ToArray();
    }
}
