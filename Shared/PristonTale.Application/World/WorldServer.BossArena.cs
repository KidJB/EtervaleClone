using System.Collections.Concurrent;
using System.Numerics;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.ItemBoxes;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A prize item for the Item Distributor, written off the tick (BossArenaPrizes).
public sealed record ArenaPrize(long AccountId, string Item);

// The boss arena on the tick (BossArena): announcements before each start, entry through the arena NPC while it is open,
// the boss and its minions in the middle of Cursed Temple 2F, minions that come back each minute, and the end: the boss's fall rewards
// every player still in the arena (experience and gold at once, a prize through the Item Distributor), then everyone in
// the arena returns to Ricarten.
public sealed partial class WorldServer
{
    private DateTimeOffset _arenaAnnouncedFor;
    private int _arenaAnnounced = int.MaxValue;
    private DateTimeOffset _arenaRanFor;
    private ArenaRun? _arena;

    // Prizes waiting for the Item Distributor; the boss arena service drains it.
    public ConcurrentQueue<ArenaPrize> ArenaPrizes { get; } = new();

    private sealed class ArenaRun(BossArenaEvent arena, DateTimeOffset startedAt, MonsterInstance boss)
    {
        public BossArenaEvent Event { get; } = arena;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public MonsterInstance Boss { get; } = boss;
        public MonsterInstance?[] Minions { get; } = new MonsterInstance?[BossArena.Minions];
        public DateTimeOffset MinionsAt { get; set; } = startedAt;
    }

    // The "/arena" command: the next event, or entry when the player stands at the arena NPC while it is open.
    public Task<bool> ArenaAsync(ulong entityId, bool enter) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection))
            {
                return false;
            }

            var now = _clock.GetLocalNow();
            var start = BossArena.NextStart(now);
            var arena = BossArena.EventAt(start.Hour);
            var open = BossArena.EntryOpen(now, start);
            if (!enter)
            {
                Reply(connection.Player, BossArenaMessages.Next(arena, start, open));
                return true;
            }

            var player = connection.Player;
            var refusal = !open ? BossArenaMessages.NotOpen
                : !AtArenaNpc(connection) ? BossArenaMessages.NotAtNpc
                : player.Level < arena.MinLevel ? BossArenaMessages.TooLow(arena)
                : player.Trade is not null || player.Shop is not null || player.IsDead ? BossArenaMessages.Busy
                : null;
            if (refusal is not null)
            {
                Reply(player, refusal);
                return false;
            }

            MoveTo(connection, BossArena.FieldCode, BossArena.Arrival);
            Reply(player, $"Boss Arena> You entered the arena. {arena.Name} appears at {start:HH:mm}.");
            return true;
        });

    // A game master's start (event 0 or 1) or end (-1) of the arena, whatever the schedule.
    public Task<string> StartArenaNowAsync(int eventId) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (eventId < 0)
            {
                if (_arena is not { } running)
                {
                    return "Boss Arena: none running";
                }

                EndArena(running, GetField(BossArena.FieldCode));
                return "Boss Arena: ended";
            }

            if (_arena is not null)
            {
                return "Boss Arena: already running (/@arena end first)";
            }

            StartArena(BossArena.Events[eventId], _clock.GetLocalNow());
            return _arena is null ? "Boss Arena: its boss is not in the world data" : $"Boss Arena: {BossArena.Events[eventId].Name} started in Cursed Temple 2F";
        }, waitForCapacity: true);

    // Tick only: announcements, the start, the minions and the end.
    private void UpdateBossArena(DateTimeOffset now)
    {
        var start = BossArena.NextStart(now);
        if (start != _arenaAnnouncedFor)
        {
            (_arenaAnnouncedFor, _arenaAnnounced) = (start, int.MaxValue);
        }

        var minutesLeft = (int)Math.Ceiling((start - now).TotalMinutes);
        foreach (var minutes in BossArena.AnnounceMinutes)
        {
            if (minutesLeft == minutes && minutes < _arenaAnnounced)
            {
                _arenaAnnounced = minutes;
                Announce(BossArenaMessages.Soon(BossArena.EventAt(start.Hour), minutes));
            }
        }

        // The last start (a multiple of six hours) began less than a minute ago and has not run yet.
        var passed = PreviousStart(now);
        if (_arena is null && passed != _arenaRanFor && now < passed.AddMinutes(1))
        {
            _arenaRanFor = passed;
            StartArena(BossArena.EventAt(passed.Hour), now);
        }

        if (_arena is { } run)
        {
            StepArena(run, now);
        }
    }

    private static DateTimeOffset PreviousStart(DateTimeOffset now)
    {
        var hour = now.Hour / 6 * 6;
        return new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, now.Offset);
    }

    private void StartArena(BossArenaEvent arena, DateTimeOffset now)
    {
        var field = GetField(BossArena.FieldCode);
        if (!field.TryFindMonsterDefinition(arena.Boss, out var boss))
        {
            return;
        }

        var monster = field.SpawnArenaMonster(boss, new Vector3(BossArena.Center.X, 0f, BossArena.Center.Z));
        _arena = new ArenaRun(arena, now, monster);
        SpawnMinions(_arena, field);
        Announce(BossArenaMessages.Started(arena));
    }

    private void StepArena(ArenaRun run, DateTimeOffset now)
    {
        var field = GetField(BossArena.FieldCode);
        var minutes = (now - run.StartedAt).TotalMinutes;
        if (!run.Boss.IsAlive)
        {
            Announce(BossArenaMessages.Defeated(run.Event, minutes));
            RewardArena(run, minutes);
            EndArena(run, field);
            return;
        }

        if (minutes >= BossArena.MaxMinutes || minutes > 1 && !_connections.Values.Any(static connection => InArena(connection)))
        {
            EndArena(run, field);
            return;
        }

        if ((now - run.MinionsAt).TotalSeconds >= BossArena.MinionRespawnSeconds)
        {
            run.MinionsAt = now;
            SpawnMinions(run, field);
        }
    }

    private void SpawnMinions(ArenaRun run, FieldInstance field)
    {
        if (!field.TryFindMonsterDefinition(run.Event.Minion, out var minion))
        {
            return;
        }

        for (var index = 0; index < run.Minions.Length; index++)
        {
            if (run.Minions[index] is { IsAlive: true })
            {
                continue;
            }

            var angle = MathF.Tau * index / run.Minions.Length;
            var position = new Vector3(BossArena.Center.X + MathF.Cos(angle) * BossArena.MinionRing, 0f, BossArena.Center.Z + MathF.Sin(angle) * BossArena.MinionRing);
            run.Minions[index] = field.SpawnArenaMonster(minion, position);
        }
    }

    // Every player in the arena: the experience by kill time and the gold now, a random prize for the Item Distributor.
    private void RewardArena(ArenaRun run, double minutes)
    {
        foreach (var connection in _connections.Values)
        {
            if (!InArena(connection) || connection.Session is not { } session)
            {
                continue;
            }

            var character = connection.Player.Character;
            var experience = BossArena.Experience(run.Event, character.Level, minutes);
            var gold = Math.Max(0, Math.Min(run.Event.Gold, MoneyLimits.CarryLimit(character.Level, character.Tier) - character.Money));
            character.AwardExperience(experience);
            character.TryChangeMoney(gold);
            var (key, count) = run.Event.Prizes[GetField(BossArena.FieldCode).Rng.NextExclusive(0, run.Event.Prizes.Count)];
            var prize = "a prize";
            if (_worldData.Current.Items.TryGet(ItemCodes.Parse(key), out var definition))
            {
                var item = definition.IsStackable ? new ItemInstance(definition, Math.Min(count, definition.MaxStack)) : ItemRoller.CreatePerfect(definition, 1, 0);
                ArenaPrizes.Enqueue(new ArenaPrize(session.AccountId, ItemBoxCodec.Capture(item)));
                prize = item.Count > 1 ? $"{item.Count} x {definition.Name}" : definition.Name;
            }

            Reply(connection.Player, BossArenaMessages.Rewarded(experience, gold, prize));
        }
    }

    // The boss and minions leave and every player in the arena returns to Ricarten.
    private void EndArena(ArenaRun run, FieldInstance field)
    {
        field.RemoveArenaMonster(run.Boss.EntityId);
        foreach (var minion in run.Minions)
        {
            if (minion is not null)
            {
                field.RemoveArenaMonster(minion.EntityId);
            }
        }

        foreach (var connection in _connections.Values.Where(static connection => InArena(connection)).ToArray())
        {
            MoveTo(connection, BossArena.ReturnFieldCode, BossArena.Return);
        }

        Announce(BossArenaMessages.Ended(run.Event));
        _arena = null;
    }

    private void MoveTo(Connection connection, int fieldCode, (float X, float Z) point)
    {
        if (!_catalog.TryGetFieldByCode(fieldCode, out var destination))
        {
            return;
        }

        var position = GetField(fieldCode).Terrain.PlaceOnFloor(new Vector3(point.X, 0f, point.Z));
        var inVillage = destination.State == FieldState.Village;
        Relocate(connection, fieldCode, position, inVillage);
        connection.Player.EnterField(position, inVillage);
    }

    private static bool InArena(Connection connection) =>
        connection.FieldId == BossArena.FieldCode && BossArena.Inside(connection.Player.Position.X, connection.Player.Position.Z);

    private bool AtArenaNpc(Connection connection)
    {
        foreach (var npc in _fields[connection.FieldId].Npcs)
        {
            if (npc.Definition.Find(NpcServiceKind.BossArena) is not null && NpcServiceTransactions.CanReach(connection.Player, npc))
            {
                return true;
            }
        }

        return false;
    }

    private void Announce(string text)
    {
        foreach (var id in _connections.Keys)
        {
            Notify(id, ChatKind.Notice, text);
        }
    }
}
