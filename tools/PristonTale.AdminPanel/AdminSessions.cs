using System.Collections.Concurrent;
using System.Security.Cryptography;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;

namespace PristonTale.AdminPanel;

// Who may use the panel: a game account whose game-master level is Administrator (4) signs in with its game password and
// gets a random session token (an HttpOnly, SameSite=Strict cookie) for SessionHours. Sessions live in memory: a restart
// signs everyone out.
public sealed class AdminSessions(IAccountRepository accounts, TimeProvider clock, TimeSpan lifetime)
{
    public const int AdministratorLevel = 4;
    public const string Cookie = "pt_admin";

    private readonly ConcurrentDictionary<string, (string User, DateTimeOffset Ends)> _sessions = new(StringComparer.Ordinal);

    public async Task<string?> SignInAsync(string user, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var account = await accounts.FindByUserIdAsync(user.Trim(), ct);
        if (account is null || account.IsBlocked || account.GmLevel < AdministratorLevel || !PasswordHasher.Verify(password, account.PasswordHash))
        {
            return null;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = (account.UserId, clock.GetUtcNow() + lifetime);
        return token;
    }

    // The signed-in account of a token, or null when it is unknown or expired (an expired one is forgotten).
    public string? UserOf(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (session.Ends <= clock.GetUtcNow())
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return session.User;
    }

    public void SignOut(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _sessions.TryRemove(token, out _);
        }
    }
}
