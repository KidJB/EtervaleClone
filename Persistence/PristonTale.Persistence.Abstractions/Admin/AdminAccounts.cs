using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Persistence.Abstractions.Admin;

// What an operator does to accounts and characters outside the game (the admin panel, ptdevaccounts): create an account, set
// its password or game-master level, create a character at a level (or maxed: the level cap, the last tier, every class
// skill at the top rank and the level's status points) and move a character to another account. Each answers the error a
// person reads, or null when done.
public sealed class AdminAccounts(IAccountRepository accounts, ICharacterRepository characters, IGameMasterRepository gameMasters, CharacterClassRegistry classes)
{
    public const int MinPasswordLength = 4;
    public const int MaxUserIdLength = 32;

    public async Task<(AccountRecord? Account, string? Error)> CreateAccountAsync(string user, string password, CancellationToken ct = default)
    {
        user = user?.Trim() ?? "";
        if (user.Length == 0 || user.Length > MaxUserIdLength || !user.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.'))
        {
            return (null, $"an account name is 1 to {MaxUserIdLength} letters, digits, '.', '-' or '_'");
        }

        if (PasswordError(password) is { } error)
        {
            return (null, error);
        }

        if (await accounts.FindByUserIdAsync(user, ct) is not null)
        {
            return (null, $"account '{user}' already exists");
        }

        return (await accounts.CreateAsync(user, PasswordHasher.Hash(password), ct), null);
    }

    public async Task<string?> SetPasswordAsync(string user, string password, CancellationToken ct = default)
    {
        if (PasswordError(password) is { } error)
        {
            return error;
        }

        return await gameMasters.SetPasswordHashAsync(user?.Trim() ?? "", PasswordHasher.Hash(password), ct) ? null : $"no account '{user}'";
    }

    public async Task<string?> SetGameMasterAsync(string user, int level, CancellationToken ct = default)
    {
        if (level is < 0 or > 4)
        {
            return "a game-master level is 0 (player) to 4 (administrator)";
        }

        return await gameMasters.SetGmLevelAsync(user?.Trim() ?? "", level, ct) ? null : $"no account '{user}'";
    }

    public async Task<(CharacterRecord? Character, string? Error)> CreateCharacterAsync(string user, string name, PlayerClass playerClass, int level, bool max,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(playerClass))
        {
            return (null, $"unknown class {playerClass}");
        }

        level = max ? ExperienceTable.MaximumLevel : Math.Clamp(level, 1, ExperienceTable.MaximumLevel);
        name = name?.Trim() ?? "";
        if (await accounts.FindByUserIdAsync(user?.Trim() ?? "", ct) is not { } account)
        {
            return (null, $"no account '{user}'");
        }

        if (!CharacterNames.IsValidLength(name))
        {
            return (null, $"a character name is {CharacterNames.MinLength} to {CharacterNames.MaxLength} characters");
        }

        if (await characters.NameExistsAsync(name, ct))
        {
            return (null, $"the name '{name}' is taken");
        }

        if (CharacterSlots.FirstFree(await characters.UsedSlotsAsync(account.Id, ct)) is not { } slot)
        {
            return (null, $"account '{account.UserId}' has no free character slot (at most {CharacterSlots.Maximum})");
        }

        var field = DeathPenalty.TownField(playerClass);
        var record = CharacterRecordFactory.NewStarter(classes, account.Id, name, slot, playerClass, field, level: level);
        if (max)
        {
            var character = classes.CreatePlayer(playerClass, name, level);
            character.ApplyQuestProgress(PlayerCharacter.LastTier, (level - 1) * PlayerCharacter.StatusPointsPerLevel, 0, 0);
            foreach (var skill in classes.Get(playerClass).Skills.Where(static skill => skill.Code != 0))
            {
                character.Skills.Restore(skill.Code, SkillLearning.MaxRank);
            }

            record = CharacterRecordFactory.FromLiveSession(record, character, field, new System.Numerics.Vector3(record.PosX, 0f, record.PosZ));
        }

        return (await characters.CreateAsync(record, ct), null);
    }

    public async Task<string?> MoveCharacterAsync(string name, string targetUser, CancellationToken ct = default) =>
        await gameMasters.MoveCharacterAsync(name?.Trim() ?? "", targetUser?.Trim() ?? "", CharacterSlots.Maximum, ct) switch
        {
            CharacterTransfer.Moved => null,
            CharacterTransfer.NoCharacter => $"no character '{name}'",
            CharacterTransfer.NoAccount => $"no account '{targetUser}'",
            _ => $"account '{targetUser}' has no free character slot"
        };

    private static string? PasswordError(string? password) =>
        password is null || password.Length < MinPasswordLength ? $"a password is at least {MinPasswordLength} characters" : null;
}
