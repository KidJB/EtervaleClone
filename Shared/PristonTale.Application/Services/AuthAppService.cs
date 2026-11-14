using System.Security.Cryptography;
using System.Text;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Application.Services;

public sealed class AuthAppService
{
    // A "remember me" token lasts this long and an account keeps this many (one per computer it was ticked on).
    public static readonly TimeSpan RememberLifetime = TimeSpan.FromDays(30);
    public const int RememberedPerAccount = 5;
    private const int RememberTokenBytes = 32;

    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;
    private readonly IRememberTokenRepository? _remembered;
    private readonly TimeProvider _clock;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts, IRememberTokenRepository? remembered = null, TimeProvider? clock = null)
    {
        _sessions = sessions;
        _accounts = accounts;
        _remembered = remembered;
        _clock = clock ?? TimeProvider.System;
    }

    // A login by a "remember me" token: the token must be unexpired and belong to that account. Only the token's SHA-256
    // is stored, so a copy of the database cannot be used to log in.
    public async Task<LoginOutcome> LoginWithTokenAsync(string account, string token, CancellationToken ct = default)
    {
        if (_remembered is null || string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(token))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        var record = await _remembered.FindAccountAsync(HashToken(token), _clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        if (record is null || !string.Equals(record.UserId, account, StringComparison.OrdinalIgnoreCase))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        return record.IsBlocked ? LoginOutcome.Denied(LoginFailure.LoginDenied) : await StartAsync(record, ct);
    }

    // A new token for the account a session logged into, or empty where tokens are not stored.
    public async Task<string> RememberAsync(long accountId, CancellationToken ct = default)
    {
        if (_remembered is null)
        {
            return string.Empty;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(RememberTokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = _clock.GetUtcNow();
        await _remembered.AddAsync(accountId, HashToken(token), now.ToUnixTimeMilliseconds(), (now + RememberLifetime).ToUnixTimeMilliseconds(), RememberedPerAccount, ct);
        return token;
    }

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        var record = await _accounts.FindByUserIdAsync(account, ct);

        if (record is null)
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        if (record.IsBlocked)
        {
            return LoginOutcome.Denied(LoginFailure.LoginDenied);
        }

        if (!PasswordHasher.Verify(password, record.PasswordHash))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        return await StartAsync(record, ct);
    }

    private async Task<LoginOutcome> StartAsync(AccountRecord record, CancellationToken ct)
    {
        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);
        session.GmLevel = Math.Clamp(record.GmLevel, 0, GameMasters.GameMasterLevel.Highest);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }
}
