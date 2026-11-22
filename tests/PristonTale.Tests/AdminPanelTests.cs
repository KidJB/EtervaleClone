using Microsoft.Extensions.DependencyInjection;
using PristonTale.AdminPanel;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Admin;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;

namespace PristonTale.Tests;

internal static class AdminPanelTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("admin panel: values are parsed by column kind, JSON columns stay JSON, required columns refuse NULL", ValuesAsync);
        yield return ("admin panel: tables come from the schema, secrets are hidden and logs read-only", PolicyAsync);
        yield return ("admin panel: rows page, search and sort; a write names one row by its whole key and is refused whole", WritesAsync);
        yield return ("admin panel: only an administrator signs in, and a session ends", SessionsAsync);
        yield return ("admin panel: data files are replaced only with valid JSON, inside the data folder, with a backup", FilesAsync);
        yield return ("admin panel: accounts are created, their password and game-master level set, characters created, maxed and moved", AccountsAsync);
    }

    private static Task ValuesAsync()
    {
        var number = new AdminColumn("level", AdminColumnKind.Integer, true, 0, false);
        Assert.True(AdminValue.Parse(number, " 42 ") is { Ok: true, Value: 42L } && !AdminValue.Parse(number, "4.5").Ok && !AdminValue.Parse(number, null).Ok,
            "an integer column");
        var real = new AdminColumn("scale", AdminColumnKind.Real, false, 0, false);
        Assert.True(AdminValue.Parse(real, "1.25") is { Ok: true, Value: 1.25d } && !AdminValue.Parse(real, "NaN").Ok && AdminValue.Parse(real, null) is { Ok: true, Value: null },
            "a real column");
        var flag = new AdminColumn("blocked", AdminColumnKind.Boolean, true, 0, false);
        Assert.True(AdminValue.Parse(flag, "TRUE") is { Value: true } && AdminValue.Parse(flag, "0") is { Value: false } && !AdminValue.Parse(flag, "yes").Ok, "a boolean column");
        var json = new AdminColumn("items", AdminColumnKind.Json, true, 0, false);
        Assert.True(AdminValue.Parse(json, "[1,2]").Ok && !AdminValue.Parse(json, "[1,").Ok, "a JSON column");
        var text = new AdminColumn("state", AdminColumnKind.Text, true, 0, false);
        Assert.True(!AdminValue.Parse(text, "plain", "{\"a\":1}").Ok && AdminValue.Parse(text, "{\"a\":2}", "{\"a\":1}").Ok && AdminValue.Parse(text, "plain", "old").Ok,
            "a text column holding JSON did not stay JSON");
        Assert.True(!AdminValue.Parse(new AdminColumn("blob", AdminColumnKind.Opaque, false, 0, false), "x").Ok, "an opaque column was written");
        return Task.CompletedTask;
    }

    private static async Task PolicyAsync()
    {
        await using var store = await Store.OpenAsync();
        var tables = await store.Data.TablesAsync();
        Assert.True(tables.Any(static table => table.Name == "item_definitions" && !table.ReadOnly) && tables.All(static table => table.Name != "remember_tokens")
            && tables.All(static table => table.Name != "VersionInfo"), "the tables shown");
        var accounts = tables.Single(static table => table.Name == "accounts");
        Assert.True(accounts.Find("password_hash") is null && accounts.Keys.Single().Name == "id", "the account password hash is shown");
        Assert.True(tables.Single(static table => table.Name == "gm_audit").ReadOnly && tables.Single(static table => table.Name == "coin_log").ReadOnly, "a log is writable");
        var texts = tables.Single(static table => table.Name == "item_texts");
        Assert.True(texts.Keys.Select(static column => column.Name).SequenceEqual(["definition_id", "language"]), "a composite key's order");
    }

    private static async Task WritesAsync()
    {
        await using var store = await Store.OpenAsync();
        var data = store.Data;
        foreach (var code in new[] { "xa", "xb", "xc" })
        {
            Assert.True((await data.InsertAsync("languages", new Dictionary<string, string?> { ["code"] = code, ["name"] = "Lang " + code })).Ok, "an insert");
        }

        var page = await data.RowsAsync("languages", new AdminQuery(Search: "lang x", Sort: "code", Descending: true));
        Assert.True(page is { Total: 3 } && page.Rows[0][0] == "xc", "search and sort");
        Assert.True(await data.RowsAsync("languages", new AdminQuery(Page: 1, Size: 2, Search: "lang x", Sort: "code")) is { Rows.Count: 1 }, "paging");
        Assert.True(await data.RowsAsync("languages", new AdminQuery(Search: "100%_")) is { Total: 0 }, "search wildcards are not escaped");
        Assert.True(await data.RowsAsync("no_such_table", new AdminQuery()) is null, "an unknown table was read");

        var key = new Dictionary<string, string?> { ["code"] = "xb" };
        Assert.True((await data.UpdateAsync("languages", key, new Dictionary<string, string?> { ["name"] = "Renamed" })).Ok, "an update");
        Assert.True(!(await data.UpdateAsync("languages", key, new Dictionary<string, string?> { ["nope"] = "x" })).Ok, "an unknown column was written");
        Assert.True(!(await data.UpdateAsync("languages", new Dictionary<string, string?> { ["code"] = "zz" }, new Dictionary<string, string?> { ["name"] = "x" })).Ok,
            "a missing row was written");
        Assert.True(!(await data.UpdateAsync("languages", new Dictionary<string, string?>(), new Dictionary<string, string?> { ["name"] = "x" })).Ok, "a keyless update ran");
        Assert.True(!(await data.InsertAsync("languages", new Dictionary<string, string?> { ["code"] = "xa", ["name"] = "dup" })).Ok, "a duplicate key was inserted");
        Assert.True(!(await data.InsertAsync("languages", new Dictionary<string, string?> { ["code"] = "xd" })).Ok, "a row lacking a required column was inserted");
        Assert.True(!(await data.DeleteAsync("gm_audit", new Dictionary<string, string?> { ["id"] = "1" })).Ok, "a read-only table was written");
        Assert.True((await data.DeleteAsync("languages", key)).Ok && !(await data.DeleteAsync("languages", key)).Ok, "a delete");
        var rows = await data.RowsAsync("languages", new AdminQuery(Search: "lang x"));
        Assert.True(rows is { Total: 2 } && rows.Rows.All(static row => row[0] != "xb"), "the rows after the writes");

        var accountId = (await store.Services.GetRequiredService<IAccountRepository>().CreateAsync("ann", PasswordHasher.Hash("pw"))).Id;
        var accountKey = new Dictionary<string, string?> { ["id"] = accountId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        Assert.True(!(await data.UpdateAsync("accounts", accountKey, new Dictionary<string, string?> { ["gm_level"] = "four" })).Ok
            && !(await data.UpdateAsync("accounts", accountKey, new Dictionary<string, string?> { ["password_hash"] = "x" })).Ok, "an account field took a bad value");
        Assert.True((await data.UpdateAsync("accounts", accountKey, new Dictionary<string, string?> { ["gm_level"] = "3" })).Ok
            && (await store.Services.GetRequiredService<IAccountRepository>().FindByUserIdAsync("ann"))!.GmLevel == 3, "the server does not read the edit");
    }

    private static async Task SessionsAsync()
    {
        await using var store = await Store.OpenAsync();
        var accounts = store.Services.GetRequiredService<IAccountRepository>();
        await accounts.CreateAsync("player", PasswordHasher.Hash("pw"));
        await accounts.CreateAsync("boss", PasswordHasher.Hash("pw"));
        await store.Data.UpdateAsync("accounts", new Dictionary<string, string?> { ["id"] = (await accounts.FindByUserIdAsync("boss"))!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new Dictionary<string, string?> { ["gm_level"] = "4" });
        var clock = new BellatraGameTests.GameClock();
        var sessions = new AdminSessions(accounts, clock, TimeSpan.FromHours(1));
        Assert.True(await sessions.SignInAsync("player", "pw") is null && await sessions.SignInAsync("boss", "wrong") is null, "a player or a wrong password signed in");
        var token = await sessions.SignInAsync(" boss ", "pw");
        Assert.True(token is not null && sessions.UserOf(token) == "boss" && sessions.UserOf("forged") is null, "an administrator's session");
        clock.Now += TimeSpan.FromHours(2);
        Assert.True(sessions.UserOf(token) is null, "a session outlived its hours");
        var again = await sessions.SignInAsync("boss", "pw");
        sessions.SignOut(again);
        Assert.True(sessions.UserOf(again) is null, "a session survived signing out");
    }

    private static async Task AccountsAsync()
    {
        await using var store = await Store.OpenAsync();
        var accounts = store.Services.GetRequiredService<IAccountRepository>();
        var admin = new AdminAccounts(accounts, store.Services.GetRequiredService<Persistence.Abstractions.Characters.ICharacterRepository>(),
            store.Services.GetRequiredService<Persistence.Abstractions.GameMasters.IGameMasterRepository>(), new Simulation.Characters.CharacterClassRegistry());
        Assert.True((await admin.CreateAccountAsync("ann", "pass1")).Account is { UserId: "ann" }, "an account was not created");
        Assert.True((await admin.CreateAccountAsync("ann", "pass1")).Error is not null && (await admin.CreateAccountAsync("bad name", "pass1")).Error is not null
            && (await admin.CreateAccountAsync("bob", "x")).Error is not null, "a duplicate, a bad name or a short password was accepted");
        Assert.True(await admin.SetPasswordAsync("ann", "newpass") is null && PasswordHasher.Verify("newpass", (await accounts.FindByUserIdAsync("ann"))!.PasswordHash)
            && await admin.SetPasswordAsync("ghost", "newpass") is not null, "the password was not reset");
        Assert.True(await admin.SetGameMasterAsync("ann", 3) is null && (await accounts.FindByUserIdAsync("ann"))!.GmLevel == 3
            && await admin.SetGameMasterAsync("ann", 5) is not null, "the game-master level");
        var (archer, _) = await admin.CreateCharacterAsync("ann", "Arrow", Contracts.Characters.PlayerClass.Archer, 40, false);
        var (knight, _) = await admin.CreateCharacterAsync("ann", "Shield", Contracts.Characters.PlayerClass.Knight, 1, true);
        Assert.True(archer is { Level: 40, Slot: 0 } && knight is { Slot: 1 } && knight.Level == Simulation.Progression.ExperienceTable.MaximumLevel,
            "the characters were not created at their levels");
        Assert.True((await admin.CreateCharacterAsync("ann", "Arrow", Contracts.Characters.PlayerClass.Archer, 1, false)).Error is not null
            && (await admin.CreateCharacterAsync("ghost", "Nobody", Contracts.Characters.PlayerClass.Archer, 1, false)).Error is not null, "a taken name or a missing account");
        await admin.CreateAccountAsync("bob", "pass1");
        Assert.True(await admin.MoveCharacterAsync("Arrow", "bob") is null && await admin.MoveCharacterAsync("Arrow", "ghost") is not null
            && await admin.MoveCharacterAsync("Nobody", "bob") is not null, "the move");
        var bob = await accounts.FindByUserIdAsync("bob");
        Assert.True((await store.Services.GetRequiredService<Persistence.Abstractions.Characters.ICharacterRepository>().ListByAccountAsync(bob!.Id)).Single().Name == "Arrow",
            "the character is not on its new account");
    }

    private static Task FilesAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "coinshop.json"), "{ \"a\": 1 }");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "x");
            var files = new AdminDataFiles(directory);
            Assert.True(files.List() is [{ Name: "coinshop.json", Reload: "/@coinshop_reload in game" }], "the data files listed");
            Assert.True(files.Write("coinshop.json", "{ bad") is not null && files.Read("coinshop.json") == "{ \"a\": 1 }", "an invalid file was written");
            Assert.True(files.Write("coinshop.json", "// comment\n{ \"a\": 2, }") is null && files.Read("coinshop.json")!.Contains("\"a\": 2", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(directory, "coinshop.json.bak")) == "{ \"a\": 1 }", "a valid file was not written with its backup");
            Assert.True(files.Read("../coinshop.json") is null && files.Write("..\\x.json", "{}") is not null && files.Read("notes.txt") is null
                && files.Write("missing.json", "{}") is not null, "a path outside the data files was reached");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        return Task.CompletedTask;
    }

    // A disposable, migrated SQLite database with the panel's repository.
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _directory;

        private Store(string directory, ServiceProvider services)
        {
            _directory = directory;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public IAdminDataRepository Data => Services.GetRequiredService<IAdminDataRepository>();

        public static async Task<Store> OpenAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "etervale-admin-db-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPristonSqlite(Path.Combine(directory, "world.db"));
            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            return new Store(directory, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }
    }
}
