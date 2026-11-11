using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Achievements;
using PristonTale.Simulation.Achievements;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.Achievements;

// A character's badges and the title it shows (AchievementRules.TitleOf).
public sealed record AchievementView(IReadOnlyList<EarnedAchievement> Earned, int Title);

// Stores the badges off the tick: a claimed top level badge goes to the first of its class and is announced to everyone.
public sealed class AchievementAppService
{
    private readonly IAchievementRepository _repository;
    private readonly ChatDirectory _directory;
    private readonly WorldServer _world;
    private readonly TimeProvider _clock;

    public AchievementAppService(IAchievementRepository repository, ChatDirectory directory, GameMasterCommands commands, TimeProvider? clock = null)
    {
        _repository = repository;
        _directory = directory;
        _world = commands.World;
        _clock = clock ?? TimeProvider.System;
        commands.GrantAchievement = GrantAsync;
    }

    public async Task<bool> ClaimAsync(TopLevelClaim claim, CancellationToken ct = default)
    {
        if (!await _repository.TryAwardAsync((int)claim.Id, (long)claim.Class, claim.CharacterId, Now(), ct))
        {
            return false;
        }

        _directory.Broadcast(new ChatLine(ChatKind.Notice, 0, ChatRules.ClipToCapacity($"Achievement> New Top {claim.Class}: {claim.Name}! Congratulations!")));
        await ApplyTitleAsync(claim.EntityId, claim.CharacterId, ct);
        return true;
    }

    public async Task<bool> GrantAsync(ChatMember member, int achievementId)
    {
        if (member.Session.Loaded is not { } loaded || !AchievementRules.IsGranted(achievementId)
            || !await _repository.TryAwardAsync(achievementId, loaded.Id, loaded.Id, Now()))
        {
            return false;
        }

        await ApplyTitleAsync(member.EntityId, loaded.Id);
        return true;
    }

    // Shows the character's title over its name: the badge it chose, or its best when it never chose.
    public async Task ApplyTitleAsync(ulong entityId, long characterId, CancellationToken ct = default)
    {
        var view = await ViewAsync(characterId, ct);
        await _world.SetAchievementTitleAsync(entityId, view.Title);
    }

    // A badge the character holds to wear, or AchievementRules.NoTitle to hide its title; anything else changes nothing.
    public async Task ChooseTitleAsync(ulong entityId, long characterId, int achievementId, CancellationToken ct = default)
    {
        var earned = await ListAsync(characterId, ct);
        if (achievementId != AchievementRules.NoTitle && !earned.Any(badge => badge.AchievementId == achievementId))
        {
            return;
        }

        await _repository.SaveTitleAsync(characterId, achievementId, ct);
        await ApplyTitleAsync(entityId, characterId, ct);
    }

    public async Task<AchievementView> ViewAsync(long characterId, CancellationToken ct = default)
    {
        var earned = await ListAsync(characterId, ct);
        var chosen = await _repository.LoadTitleAsync(characterId, ct);
        return new AchievementView(earned, AchievementRules.TitleOf(earned.Select(static badge => (badge.AchievementId, badge.HolderKey)), chosen));
    }

    public Task<IReadOnlyList<EarnedAchievement>> ListAsync(long characterId, CancellationToken ct = default) =>
        _repository.ListAsync(characterId, AchievementLimits.MaxShown, ct);

    private long Now() => _clock.GetUtcNow().ToUnixTimeSeconds();
}
