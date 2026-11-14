using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Sessions;
using PristonTale.Engine3D.Rendering;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;

namespace PristonTale.Tests;

// "Remember me": the server keeps only a hash of each token, a token logs in only its own account until it expires, and
// the client keeps the token, never the password.
internal static class RememberLoginTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("remember me: a token logs its account in until it expires, never another account, and only its hash is stored", TokenAsync);
        yield return ("remember me: the login carries its mode and the result its token on the wire, and an unknown mode is refused", WireAsync);
        yield return ("remember me: a Remember login over the network is given a token and a new connection logs in with it, not with a forged one", NetworkAsync);
        yield return ("remember me: the client keeps one account's token, forgets it, and drops a malformed one", SettingsAsync);
    }

    private static async Task TokenAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-remember-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var accounts = provider.GetRequiredService<IAccountRepository>();
            var tokens = provider.GetRequiredService<IRememberTokenRepository>();
            var ann = await accounts.CreateAsync("ann", PasswordHasher.Hash("secret"));
            await accounts.CreateAsync("bob", PasswordHasher.Hash("other"));
            var clock = new BellatraGameTests.GameClock();
            var sessions = new SessionRegistry();
            var auth = new AuthAppService(sessions, accounts, tokens, clock);

            var token = await auth.RememberAsync(ann.Id);
            Assert.True(token.Length is > 40 and <= AccountWire.MaxRememberTokenBytes, $"token length {token.Length}");
            Assert.True(await tokens.FindAccountAsync(token, clock.Now.ToUnixTimeMilliseconds()) is null
                && (await tokens.FindAccountAsync(AuthAppService.HashToken(token), clock.Now.ToUnixTimeMilliseconds()))?.Id == ann.Id,
                "the token itself, not only its hash, was stored");

            var login = await auth.LoginWithTokenAsync("ANN", token);
            Assert.True(login.Ok && sessions.TryGet(login.SessionToken, out var session) && session.AccountId == ann.Id, "the token did not log its account in");
            Assert.True((await auth.LoginWithTokenAsync("bob", token)).Failure == LoginFailure.BadCredentials, "the token logged another account in");
            Assert.True((await auth.LoginWithTokenAsync("ann", token + "x")).Failure == LoginFailure.BadCredentials, "a wrong token logged in");

            clock.Now += AuthAppService.RememberLifetime + TimeSpan.FromMinutes(1);
            Assert.True((await auth.LoginWithTokenAsync("ann", token)).Failure == LoginFailure.BadCredentials, "an expired token logged in");

            var kept = new List<string>();
            for (var index = 0; index < AuthAppService.RememberedPerAccount + 2; index++)
            {
                clock.Now += TimeSpan.FromSeconds(1);
                kept.Add(await auth.RememberAsync(ann.Id));
            }

            Assert.True(!(await auth.LoginWithTokenAsync("ann", kept[0])).Ok && !(await auth.LoginWithTokenAsync("ann", kept[1])).Ok
                && (await auth.LoginWithTokenAsync("ann", kept[^1])).Ok, "the account's oldest tokens beyond the limit were kept");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task NetworkAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var account = await server.CreatePlayerAsync(new PlayerSetup("Kept", Contracts.Characters.PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        string token;
        await using (var first = new Client.Net.GameServerConnection(server.Address))
        {
            var login = await first.LoginAsync(account, MultiplayerServer.Password, "en", LoginMode.Remember);
            token = login.RememberToken;
            Assert.True(login.Ok && token.Length > 0, $"a Remember login got no token: {login.Failure}");
        }

        await using (var plain = new Client.Net.GameServerConnection(server.Address))
        {
            var login = await plain.LoginAsync(account, MultiplayerServer.Password, "en");
            Assert.True(login.Ok && login.RememberToken.Length == 0, "a plain login was given a token");
        }

        await using (var forged = new Client.Net.GameServerConnection(server.Address))
        {
            Assert.True((await forged.LoginAsync(account, "not-a-token", "en", LoginMode.Token)).Failure == LoginFailure.BadCredentials, "a forged token logged in");
        }

        await using var second = new Client.Net.GameServerConnection(server.Address);
        var again = await second.LoginAsync(account, token, "en", LoginMode.Token);
        Assert.True(again.Ok && (await second.ListCharactersAsync()).Any(static character => character.Name == "Kept"), $"the token did not log in: {again.Failure}");
    }

    private static Task WireAsync()
    {
        var buffer = new byte[LoginPacket.MaxSize];
        var sent = new LoginPacket(ProtocolVersion.Current, "ann", "token-value", "en", LoginMode.Token);
        var length = sent.Write(buffer);
        Assert.True(LoginPacket.Read(buffer.AsSpan(0, length)) == sent, "the login mode did not survive the wire");
        buffer[length - 1] = 9;
        Assert.Throws<InvalidDataException>(() => LoginPacket.Read(buffer.AsSpan(0, length)), "an unknown login mode was accepted");

        var result = new byte[LoginResultPacket.MaxSize];
        var issued = new LoginResultPacket(LoginFailure.None, new string('a', AccountWire.MaxRememberTokenBytes));
        Assert.True(LoginResultPacket.Read(result.AsSpan(0, issued.Write(result))) == issued, "the token did not survive the login result");
        var plain = new LoginResultPacket(LoginFailure.BadCredentials);
        Assert.True(LoginResultPacket.Read(result.AsSpan(0, plain.Write(result))) == plain, "a result without a token changed on the wire");
        return Task.CompletedTask;
    }

    private static Task SettingsAsync()
    {
        var settings = new ClientSettings().WithRemembered("Ann", "token");
        Assert.True(settings.Remembers("ann") && !settings.Remembers("bob"), "the token is not kept for its account only");
        Assert.True(!settings.WithoutRemembered().Remembers("ann") && settings.WithoutRemembered().RememberToken.Length == 0, "forgetting kept the token");
        var log = Engine3D.Diagnostics.ConsoleClientLog.Instance;
        Assert.True(settings.Validate(log) == settings, "a valid kept token was replaced");
        var malformed = settings with { RememberToken = new string('a', ClientSettings.RememberTokenLength + 1) };
        Assert.True(malformed.Validate(log) is { RememberToken: "", RememberedAccount: "" }, "a malformed token survived validation");
        return Task.CompletedTask;
    }
}
