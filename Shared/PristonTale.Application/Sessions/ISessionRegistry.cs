namespace PristonTale.Application.Sessions;

public interface ISessionRegistry
{
    PlayerSession Create(long accountId, string userId);
    bool TryGet(string sessionToken, out PlayerSession session);
    void Remove(string sessionToken);
    bool TryClaimCharacter(long characterId, string sessionToken);
    void ReleaseCharacter(long characterId, string sessionToken);

    // The session that holds the character (entering or playing), if any.
    bool TryGetCharacterSession(long characterId, out PlayerSession session)
    {
        session = null!;
        return false;
    }
}
