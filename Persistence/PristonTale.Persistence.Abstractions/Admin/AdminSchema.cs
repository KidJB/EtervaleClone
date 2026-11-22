using System.Globalization;
using System.Text.Json;

namespace PristonTale.Persistence.Abstractions.Admin;

// How the admin panel reads and writes a column's value: the provider's type mapped to the kinds a form can edit.
public enum AdminColumnKind
{
    Integer,
    Real,
    Boolean,
    Text,
    Json,
    // Binary or a type the panel cannot edit: shown, never written.
    Opaque
}

// SqlType: the provider's own type name. KeyOrdinal: the column's place in the primary key, 0 when it is not part of it.
public sealed record AdminColumn(string Name, AdminColumnKind Kind, bool NotNull, int KeyOrdinal, bool HasDefault, string SqlType = "")
{
    public bool IsKey => KeyOrdinal > 0;
}

// A table as the panel sees it. ReadOnly: logs, import provenance and tables without a primary key; the rows show but none
// is written.
public sealed record AdminTable(string Name, IReadOnlyList<AdminColumn> Columns, bool ReadOnly)
{
    public IReadOnlyList<AdminColumn> Keys => Columns.Where(static column => column.IsKey).OrderBy(static column => column.KeyOrdinal).ToArray();

    public AdminColumn? Find(string name) => Columns.FirstOrDefault(column => string.Equals(column.Name, name, StringComparison.Ordinal));
}

// The provider's own view of its tables and columns (SQLite pragmas, PostgreSQL's information schema), filtered by
// AdminPolicy before the panel sees them.
public interface IAdminSchemaReader
{
    Task<IReadOnlyList<AdminTable>> ReadAsync(CancellationToken ct = default);

    // The SQL that reads a parameter into the column: the parameter itself, or a cast where the provider needs one.
    string Parameter(AdminColumn column, string parameterName);
}

// What the panel may show and write: secrets never leave the database, logs and import provenance only show.
public static class AdminPolicy
{
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "remember_tokens", "VersionInfo" };

    private static readonly HashSet<string> ReadOnlyTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "gm_audit", "coin_log", "item_transfers", "server_events", "character_market_audit", "clan_warehouse_log", "currency_sinks",
        "world_source_files", "world_import_unresolved_refs", "world_entity_variants"
    };

    private static readonly HashSet<(string Table, string Column)> HiddenColumns = [("accounts", "password_hash")];

    public static bool Shows(string table) => !Hidden.Contains(table);

    public static AdminTable Apply(AdminTable table)
    {
        var columns = table.Columns.Where(column => !HiddenColumns.Contains((table.Name.ToLowerInvariant(), column.Name.ToLowerInvariant()))).ToArray();
        var readOnly = table.ReadOnly || ReadOnlyTables.Contains(table.Name) || !columns.Any(static column => column.IsKey);
        return table with { Columns = columns, ReadOnly = readOnly };
    }

    public static IReadOnlyList<AdminTable> Apply(IEnumerable<AdminTable> tables) =>
        tables.Where(table => Shows(table.Name)).Select(Apply).OrderBy(static table => table.Name, StringComparer.Ordinal).ToArray();
}

// A form's text turned into the value written for a column; Error names what is wrong. Null text writes NULL.
public readonly record struct AdminValue(object? Value, string? Error)
{
    public bool Ok => Error is null;

    public static AdminValue Parse(AdminColumn column, string? text, string? previous = null)
    {
        if (text is null)
        {
            return column.NotNull ? new(null, $"{column.Name} may not be empty") : new(null, null);
        }

        switch (column.Kind)
        {
            case AdminColumnKind.Integer:
                return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    ? new(integer, null)
                    : new(null, $"{column.Name} must be a whole number");
            case AdminColumnKind.Real:
                return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && double.IsFinite(real)
                    ? new(real, null)
                    : new(null, $"{column.Name} must be a number");
            case AdminColumnKind.Boolean:
                return text.Trim().ToLowerInvariant() switch
                {
                    "true" or "1" => new(true, null),
                    "false" or "0" => new(false, null),
                    _ => new(null, $"{column.Name} must be true or false")
                };
            case AdminColumnKind.Json:
                return IsJson(text) ? new(text, null) : new(null, $"{column.Name} must be valid JSON");
            case AdminColumnKind.Text:
                // A text column that held JSON (a saved state) must keep holding JSON.
                return previous is not null && IsJsonDocument(previous) && !IsJson(text) ? new(null, $"{column.Name} must stay valid JSON") : new(text, null);
            default:
                return new(null, $"{column.Name} cannot be edited");
        }
    }

    private static bool IsJsonDocument(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[') && IsJson(text);
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// One page of a table: the row values as text (null for NULL) in the table's column order, and the matching total.
public sealed record AdminPage(IReadOnlyList<string?[]> Rows, long Total, int Page, int Size);

public sealed record AdminQuery(int Page = 0, int Size = 50, string Search = "", string Sort = "", bool Descending = false)
{
    public const int MaxSize = 500;
}

// What a write did: Ok with the rows it touched, or the reason it was refused (nothing changed).
public sealed record AdminWriteResult(bool Ok, int Rows, string Error = "")
{
    public static AdminWriteResult Refused(string error) => new(false, 0, error);
}

// The panel's data access: every table and column name is checked against the schema (AdminPolicy applied) before it
// reaches SQL, and every value is a parameter.
public interface IAdminDataRepository
{
    Task<IReadOnlyList<AdminTable>> TablesAsync(CancellationToken ct = default);

    Task<AdminPage?> RowsAsync(string table, AdminQuery query, CancellationToken ct = default);

    // key: the row's primary key columns as text; values: the columns to set as text (null for NULL).
    Task<AdminWriteResult> UpdateAsync(string table, IReadOnlyDictionary<string, string?> key, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);

    Task<AdminWriteResult> InsertAsync(string table, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);

    Task<AdminWriteResult> DeleteAsync(string table, IReadOnlyDictionary<string, string?> key, CancellationToken ct = default);
}
