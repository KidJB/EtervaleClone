using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Client.Net.Presence;

public sealed record PresenceUpdate(
    long ServerTick,
    IReadOnlyList<EntitySpawnInfo> Spawns,
    IReadOnlyList<EntityStateInfo> States,
    IReadOnlyList<ulong> Removed,
    IReadOnlyList<CombatEventInfo> Events,
    int? FieldId = null,
    OwnerState? Owner = null)
{
    public IReadOnlyList<PristonTale.Simulation.World.Loot.GroundDropView> GroundAdded { get; init; } = [];
    public IReadOnlyList<Guid> GroundRemoved { get; init; } = [];

    // FieldWarp for the local player: a forced correction after a rejected report, otherwise a field confirmation.
    public WarpInfo? Warp { get; init; }

    // Server simulation time of a freshly received group or event list (PresenceClock extension); null otherwise.
    public double? ServerTimeMs { get; init; }

    public IReadOnlyList<SkillEventInfo> SkillEvents { get; init; } = [];
    public IReadOnlyList<StatusEffectInfo> StatusEffects { get; init; } = [];
    public SkillCooldownSnapshot? Cooldowns { get; init; }
    public SkillCastResultInfo? CastResult { get; init; }

    public PremiumTimerSnapshot? PremiumTimers { get; init; }

    // What standing up again would cost the local player while it is dead.
    public PristonTale.Simulation.Progression.RestartOffer? RestartOffer { get; init; }

    // The weather of the local player's field, when it entered the world or the weather changed.
    public PristonTale.Contracts.World.WeatherState? Weather { get; init; }

    // The next boss pass of the local player's field, when it entered a field or the pass changed.
    public BossTimeInfo? BossTime { get; init; }

    // The bosses of the local player's field at their spot, when it entered a field or a boss came alive or went.
    public IReadOnlyList<BossSpotInfo>? BossSpots { get; init; }

    // Every scheduled boss of the world, soonest first, when the player entered the world or a pass in it came.
    public IReadOnlyList<BossListInfo>? BossList { get; init; }

    // The local player's pet collection, when it entered the world or it changed.
    public PetCollectionInfo? PetCollection { get; init; }
    public DailyQuestsInfo? DailyQuests { get; init; }
    public IReadOnlyList<ItemEffectInfo> ItemEffects { get; init; } = [];

    // The quest journal when it changed, quest notices and a quest dialog the world opened on its own.
    public QuestView? Quest { get; init; }
    public IReadOnlyList<QuestNotice> QuestNotices { get; init; } = [];
    public QuestPrompt? QuestPrompt { get; init; }

    // Chat lines from the server, and the server's request to save and quit (its shutdown).
    public IReadOnlyList<ChatLineInfo> Chat { get; init; } = [];
    public bool CloseRequested { get; init; }

    // The local player's party when it changed (no member once it has none), and a player's request to group.
    public PartyInfo? Party { get; init; }
    public PartyRequestInfo? PartyRequest { get; init; }

    // The local player's trade window when it changed (TradeInfo.None once it ended), and a player's request to trade.
    public TradeInfo? Trade { get; init; }
    public TradeRequestInfo? TradeRequest { get; init; }

    // One of the local player's contact lists, as the server answered a change or a refresh.
    public ContactListInfo? Contacts { get; init; }
}

// A contact list in the order added; Online tells a friend playing now.
public sealed record ContactListInfo(ContactList List, IReadOnlyList<(string Name, bool Online)> Entries);

// The trade window as the server holds it: the partner, the local player's own offer, the partner's items (anchored at
// their trade grid cells, with their values for the local player), the partner's gold and coins and both locks.
public sealed record TradeInfo(ulong PartnerId, string PartnerName, PristonTale.Simulation.Trades.TradeOffer Own, bool OwnLocked,
    IReadOnlyList<OwnedItemView> PartnerItems, long PartnerMoney, bool PartnerLocked, long PartnerCoins = 0)
{
    public static readonly TradeInfo None = new(0, "", PristonTale.Simulation.Trades.TradeOffer.Empty, false, [], 0, false);

    public bool Open => PartnerId != 0;
}

public sealed record TradeRequestInfo(ulong FromId, string FromName);

// The local player's party, the leader first; Position is the member's planar place (x, z) in world units.
// The next boss pass of the player's field as the server sent it: no name for a field without bosses; Exact false for a
// pass on a later day (only its hour is known).
public sealed record BossTimeInfo(string BossName, int SecondsLeft, bool Exact)
{
    public bool HasBoss => BossName.Length > 0;
}

// A boss of the player's field at its spot (wire x, z in world units) and whether it is alive now.
// The pet skins the account unlocked (PetSkins bits) and the one out (0 none).
// The local player's daily quests of the day: the accepted ones with their kills and the ones claimed.
public sealed record SkinCatalogInfo(long Balance, PristonTale.Simulation.Characters.SkinSet Skins, IReadOnlyList<PristonTale.Contracts.Characters.SkinArticle> Articles);

public sealed record DailyQuestsInfo(int Day, IReadOnlyDictionary<int, int> Accepted, IReadOnlySet<int> Claimed)
{
    public static readonly DailyQuestsInfo None = new(0, new Dictionary<int, int>(), new HashSet<int>());
}

public sealed record PetCollectionInfo(ulong Skins, int Out)
{
    // The name the player gave its pet (empty: the skin's name shows).
    public string Name { get; init; } = "";

    // The account's mounts (Mounts bits), the one picked (0 none) and whether the character rides it.
    public ulong Mounts { get; init; }
    public int MountChosen { get; init; }
    public bool Riding { get; init; }
}

public readonly record struct BossSpotInfo(string BossName, float X, float Z, bool Alive);

// A boss of the world, the field it spawns in, and the seconds to its next pass when the list arrived (Exact false: a
// pass on a later day, of which only the hour is known).
public readonly record struct BossListInfo(string BossName, string FieldName, int SecondsLeft, bool Exact);

public sealed record PartyInfo(IReadOnlyList<PartyMemberInfo> Members)
{
    public static readonly PartyInfo None = new([]);

    public bool Leads(ulong entityId) => Members.Count > 0 && Members[0].EntityId == entityId;

    public bool Contains(ulong entityId)
    {
        for (var index = 0; index < Members.Count; index++)
        {
            if (Members[index].EntityId == entityId)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record PartyMemberInfo(ulong EntityId, string Name, int Level, int Life, int MaxLife, System.Numerics.Vector2 Position);

// Recommended: the player a member asks its leader to take (empty for a request to join).
public sealed record PartyRequestInfo(ulong FromId, string FromName, string Recommended = "");

// One chat line: Kind is the server's colour and tab code, SpeakerId the entity that said it (0 for server lines).
// Direct lines (the plain chat message) go to the log as they are; the others show over a speaker the client sees.
// Line is a server line the client words in its language; Text is shown as it is when Line is none.
public sealed record ChatLineInfo(ChatKind Kind, ulong SpeakerId, string Text, GameText Line = default);

public sealed record WarpInfo(int FieldId, System.Numerics.Vector3 Position, bool Forced);
