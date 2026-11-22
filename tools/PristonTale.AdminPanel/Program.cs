using Microsoft.AspNetCore.Http.HttpResults;
using PristonTale.AdminPanel;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Admin;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;

var settings = AdminSettings.Load(AppContext.BaseDirectory, args);
// The empty builder reads no environment variables or appsettings: the panel is configured by AdminSettings alone.
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
builder.WebHost.UseKestrelCore().UseUrls(settings.Url);
builder.Services.AddRoutingCore();
builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(static options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
if (settings.UsesPostgres)
{
    builder.Services.AddPristonPostgres(settings.Postgres);
}
else
{
    builder.Services.AddPristonSqlite(settings.SqlitePath);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider => new AdminSessions(provider.GetRequiredService<IAccountRepository>(), TimeProvider.System, TimeSpan.FromHours(settings.SessionHours)));
builder.Services.AddSingleton(new AdminDataFiles(settings.DataPathResolved));
builder.Services.AddSingleton<PristonTale.Simulation.Characters.CharacterClassRegistry>();
builder.Services.AddSingleton<AdminAccounts>();
var app = builder.Build();

// The panel edits a migrated schema; migrating first makes a fresh copy usable and changes nothing on a current one.
await app.Services.GetRequiredService<IDatabaseMigrator>().MigrateAsync();

var page = ReadPage();
app.MapGet("/", () => Results.Content(page, "text/html; charset=utf-8"));

app.MapPost("/api/login", async (LoginRequest login, AdminSessions sessions, HttpContext context) =>
{
    if (await sessions.SignInAsync(login.User, login.Password, context.RequestAborted) is not { } token)
    {
        return Results.Json(new { error = "Unknown account, wrong password, or not an administrator (game-master level 4)." }, statusCode: 401);
    }

    context.Response.Cookies.Append(AdminSessions.Cookie, token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true });
    return Results.Ok(new { user = login.User.Trim() });
});

var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    var sessions = context.HttpContext.RequestServices.GetRequiredService<AdminSessions>();
    var user = sessions.UserOf(context.HttpContext.Request.Cookies[AdminSessions.Cookie]);
    // Writes need the panel's own header too, which a page of another site cannot add.
    var write = !HttpMethods.IsGet(context.HttpContext.Request.Method);
    if (user is null || write && context.HttpContext.Request.Headers["X-Admin-Panel"] != "1")
    {
        return Results.Json(new { error = "Sign in first." }, statusCode: 401);
    }

    context.HttpContext.Items["user"] = user;
    return await next(context);
});

api.MapGet("/me", (HttpContext context) => Results.Ok(new { user = context.Items["user"], database = settings.UsesPostgres ? "PostgreSQL" : settings.SqlitePath }));
api.MapPost("/logout", (AdminSessions sessions, HttpContext context) =>
{
    sessions.SignOut(context.Request.Cookies[AdminSessions.Cookie]);
    context.Response.Cookies.Delete(AdminSessions.Cookie);
    return Results.Ok();
});

api.MapGet("/tables", async (IAdminDataRepository data, CancellationToken ct) => Results.Ok(await data.TablesAsync(ct)));
api.MapGet("/tables/{table}/rows", async Task<Results<Ok<AdminPage>, NotFound>> (string table, int? page, int? size, string? search, string? sort, bool? desc,
    IAdminDataRepository data, CancellationToken ct) =>
    await data.RowsAsync(table, new AdminQuery(page ?? 0, size ?? 50, search ?? "", sort ?? "", desc ?? false), ct) is { } rows ? TypedResults.Ok(rows) : TypedResults.NotFound());
api.MapPut("/tables/{table}/rows", async (string table, RowEdit edit, IAdminDataRepository data, CancellationToken ct) =>
    Written(await data.UpdateAsync(table, edit.Key ?? [], edit.Values ?? [], ct)));
api.MapPost("/tables/{table}/rows", async (string table, RowEdit edit, IAdminDataRepository data, CancellationToken ct) =>
    Written(await data.InsertAsync(table, edit.Values ?? [], ct)));
api.MapDelete("/tables/{table}/rows", async (string table, [Microsoft.AspNetCore.Mvc.FromBody] RowEdit edit, IAdminDataRepository data, CancellationToken ct) =>
    Written(await data.DeleteAsync(table, edit.Key ?? [], ct)));

api.MapGet("/overview", async (IAdminDataRepository data, CancellationToken ct) => Results.Ok(new
{
    database = settings.UsesPostgres ? "PostgreSQL" : settings.SqlitePath,
    counts = (await AdminOverview.CountsAsync(data, ct)).Select(static count => new { table = count.Table, label = count.Label, rows = count.Rows }),
    reloads = AdminOverview.Reloads.Select(static reload => new { what = reload.What, how = reload.How })
}));
api.MapGet("/classes", () => Results.Ok(Enum.GetValues<PlayerClass>().Select(static playerClass =>
    new { name = playerClass.ToString(), id = (int)playerClass, open = PristonTale.Simulation.Characters.ClassProfiles.Creatable(playerClass) })));
api.MapPost("/accounts", async (AccountEdit edit, AdminAccounts admin, CancellationToken ct) =>
    Done((await admin.CreateAccountAsync(edit.User ?? "", edit.Password ?? "", ct)).Error, $"Account '{edit.User?.Trim()}' created."));
api.MapPut("/accounts/password", async (AccountEdit edit, AdminAccounts admin, CancellationToken ct) =>
    Done(await admin.SetPasswordAsync(edit.User ?? "", edit.Password ?? "", ct), $"Password of '{edit.User?.Trim()}' changed."));
api.MapPut("/accounts/gm", async (AccountEdit edit, AdminAccounts admin, CancellationToken ct) =>
    Done(await admin.SetGameMasterAsync(edit.User ?? "", edit.Level ?? -1, ct), $"'{edit.User?.Trim()}' is now game-master level {edit.Level} (from its next login)."));
api.MapPost("/characters", async (CharacterEdit edit, AdminAccounts admin, CancellationToken ct) =>
{
    if (!Enum.TryParse<PlayerClass>(edit.Class, ignoreCase: true, out var playerClass))
    {
        return Done($"unknown class {edit.Class}", "");
    }

    var (created, error) = await admin.CreateCharacterAsync(edit.User ?? "", edit.Name ?? "", playerClass, edit.Level ?? 1, edit.Max, ct);
    return Done(error, created is null ? "" : $"Character '{created.Name}' (#{created.Id}, {playerClass} level {created.Level}) created in slot {created.Slot}.");
});
api.MapPut("/characters/move", async (CharacterEdit edit, AdminAccounts admin, CancellationToken ct) =>
    Done(await admin.MoveCharacterAsync(edit.Name ?? "", edit.User ?? "", ct), $"'{edit.Name?.Trim()}' moved to '{edit.User?.Trim()}'."));

api.MapGet("/files", (AdminDataFiles files) => Results.Ok(files.List().Select(static file => new { file.Name, file.Bytes, file.Reload })));
api.MapGet("/files/{name}", (string name, AdminDataFiles files) => files.Read(name) is { } text ? Results.Ok(new { name, text }) : Results.NotFound());
api.MapPut("/files/{name}", (string name, FileEdit edit, AdminDataFiles files) =>
    files.Write(name, edit.Text ?? "") is { } error ? Results.Json(new { error }, statusCode: 400) : Results.Ok(new { ok = true }));

if (!settings.Loopback)
{
    Console.WriteLine($"warning: the panel listens on {settings.Url}, which is not a loopback address; anyone who can reach it can try to sign in");
}

Console.WriteLine($"Etervale admin panel on {settings.Url} ({(settings.UsesPostgres ? "PostgreSQL" : settings.SqlitePath)}); sign in with an administrator account");
await app.RunAsync();

static IResult Written(AdminWriteResult result) => result.Ok ? Results.Ok(result) : Results.Json(result, statusCode: 400);

static IResult Done(string? error, string message) => error is null ? Results.Ok(new { message }) : Results.Json(new { error }, statusCode: 400);

static string ReadPage()
{
    using var stream = typeof(AdminSessions).Assembly.GetManifestResourceStream("PristonTale.AdminPanel.admin.html")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

internal sealed record LoginRequest(string User, string Password);

internal sealed record RowEdit(Dictionary<string, string?>? Key, Dictionary<string, string?>? Values);

internal sealed record FileEdit(string? Text);

internal sealed record AccountEdit(string? User, string? Password, int? Level);

internal sealed record CharacterEdit(string? User, string? Name, string? Class, int? Level, bool Max);
