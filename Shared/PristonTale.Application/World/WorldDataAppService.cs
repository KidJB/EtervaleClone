using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed class WorldDataAppService
{
    private readonly IWorldDataRepository _repository;
    private readonly CourierCatalogStore? _couriers;
    private readonly CabinetConfigStore? _cabinet;

    public WorldDataAppService(IWorldDataRepository repository, CourierCatalogStore? couriers = null, CabinetConfigStore? cabinet = null)
    {
        _repository = repository;
        _couriers = couriers;
        _cabinet = cabinet;
    }

    public Task<bool> HasTextsAsync(string language, CancellationToken ct = default) => _repository.HasCatalogTextsAsync(language, ct);

    public async Task<WorldData> BuildAsync(string language = CatalogLanguages.English, CancellationToken ct = default)
    {
        language = CatalogLanguages.Normalize(language);
        var itemRecords = await _repository.GetAllItemTemplatesAsync(language, ct);
        var itemSets = await _repository.GetAllItemSetsAsync(ct);
        var monsterRecords = await _repository.GetAllMonstersAsync(language, ct);
        var npcRecords = await _repository.GetAllNpcsAsync(language, ct);
        var fieldRecords = await _repository.GetAllFieldSpawnsAsync(ct);
        var motionModels = await _repository.GetAllMotionModelsAsync(ct);
        var motionSlots = await _repository.GetAllMotionSlotsAsync(ct);

        var itemDefs = WorldDataFactory.ToItemDefinitions(itemRecords);
        // The pet skins' and mounts' items are Etervale's own; an imported item of the same code keeps its place.
        var imported = itemDefs.Select(static item => item.Code).ToHashSet();
        itemDefs =
        [
            .. itemDefs,
            .. Simulation.Characters.PetSkins.ItemDefinitions().Where(item => !imported.Contains(item.Code)),
            .. Simulation.Characters.Mounts.ItemDefinitions().Where(item => !imported.Contains(item.Code)),
            .. Simulation.World.WorldBuffs.ItemDefinitions().Where(item => !imported.Contains(item.Code))
        ];
        // The Cabinet's brushes and look tokens (data/cabinet.json), named after the look a token unlocks.
        var named = itemDefs.GroupBy(static item => item.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Name, StringComparer.OrdinalIgnoreCase);
        itemDefs =
        [
            .. itemDefs,
            .. Simulation.Characters.CabinetRules.ItemDefinitions(_cabinet?.Current ?? Simulation.Characters.CabinetConfig.Empty, key => LookName(key, named))
                .Where(item => !imported.Contains(item.Code))
        ];
        var itemCodes = itemDefs.ToDictionary(item => item.CatalogKey, item => item.Code, StringComparer.OrdinalIgnoreCase);
        var monsterDefs = WorldDataFactory.ToMonsterDefinitions(monsterRecords, itemCodes);
        // The boss arena's bosses are Etervale's own, made from the imported Fury.
        monsterDefs = [.. monsterDefs, .. Simulation.World.BossArenaBosses.From(monsterDefs)];
        // The daily quest giver offers Etervale's daily quests beside what it was authored with.
        var npcDefs = WorldDataFactory.ToNpcDefinitions(npcRecords)
            .Select(static npc => npc.Label == Simulation.Quests.DailyQuests.GiverName || npc.Name == Simulation.Quests.DailyQuests.GiverName
                ? npc with { Services = [.. npc.Services, new Simulation.World.Npcs.NpcService(Simulation.World.Npcs.NpcServiceKind.DailyQuest)] }
                : npc.Label == Simulation.World.BossArena.NpcName || npc.Name == Simulation.World.BossArena.NpcName
                    ? npc with
                    {
                        Services =
                        [
                            .. npc.Services, new Simulation.World.Npcs.NpcService(Simulation.World.Npcs.NpcServiceKind.BossArena),
                            new Simulation.World.Npcs.NpcService(Simulation.World.Npcs.NpcServiceKind.Cabinet)
                        ]
                    }
                    : npc)
            .ToList();
        npcDefs = Simulation.Markets.MarketNpcs.AddTo(npcDefs);
        // Each Quest Courier stands on the floor of its field, at its post (data/couriers.json).
        var couriers = _couriers?.Current ?? Simulation.Quests.CourierCatalog.Empty;
        var withCouriers = Simulation.Quests.CourierNpcs.AddTo(npcDefs, couriers);
        var placedCouriers = withCouriers.Count > npcDefs.Count;
        npcDefs = withCouriers;
        // The boss arena stands where the imported Fury statue stood.
        var fieldSpawns = WorldDataFactory.ToFieldSpawnData(fieldRecords.Select(field => field with
        {
            Npcs = string.Equals(field.Stage, Simulation.World.BossArena.FieldStage, StringComparison.OrdinalIgnoreCase)
                ? [.. field.Npcs.Where(static npc => npc.NpcId != Simulation.World.BossArena.StatueNpcId)]
                : string.Equals(field.Stage, Simulation.Markets.MarketNpcs.Stage, StringComparison.OrdinalIgnoreCase) && npcDefs.Exists(static npc => npc.Code == Simulation.Markets.MarketNpcs.CharacterMarketCode)
                    ? [.. field.Npcs, .. Simulation.Markets.MarketNpcs.Placements.Select((place, index) => new FieldNpcRecord(field.Stage, ushort.MaxValue + index, place.Code, place.X, place.Y, place.Z, place.Angle))]
                    : field.Npcs
        }).Select(field => placedCouriers ? field with { Npcs = [.. field.Npcs, .. CourierPlacements(field, couriers)] } : field).ToList());

        var motions = WorldDataFactory.ToMotionTimings(motionModels, motionSlots);

        return new WorldData(
            new ItemCatalog(itemDefs, WorldDataFactory.ToQuestVariantDefinitions(itemRecords), WorldDataFactory.ToNamedUniqueDefinitions(itemRecords),
                WorldDataFactory.ToItemSets(itemSets)),
            new MonsterCatalog(monsterDefs),
            new NpcCatalog(npcDefs),
            fieldSpawns,
            motions);
    }

    private static string LookName(string key, IReadOnlyDictionary<string, string> items) =>
        Simulation.Characters.Costumes.Find(key)?.Name ?? Simulation.Characters.Costumes.FindGear(key)?.Name
        ?? (items.TryGetValue(key, out var name) ? Simulation.Characters.SkinRules.DisplayName(name) : key);

    private static IEnumerable<FieldNpcRecord> CourierPlacements(FieldSpawnRecord field, Simulation.Quests.CourierCatalog couriers) =>
        couriers.Couriers.Where(post => post.FieldCode == field.FieldCode).Select((post, index) => new FieldNpcRecord(field.Stage,
            ushort.MaxValue + 16 + index, post.NpcCode, Contracts.Units.GameUnits.ToFixed(post.X), Simulation.World.Fields.FixedNpc.OnFloor,
            Contracts.Units.GameUnits.ToFixed(post.Z), post.Angle));
}
