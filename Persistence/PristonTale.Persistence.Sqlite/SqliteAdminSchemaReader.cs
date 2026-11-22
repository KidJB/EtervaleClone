using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Admin;

namespace PristonTale.Persistence.Sqlite;

// The admin panel's view of a SQLite database: its tables from sqlite_master and each one's columns from table_info.
// SQLite stores what FluentMigrator declares as booleans, dates and JSON as INTEGER and TEXT, so those edit as such.
public sealed class SqliteAdminSchemaReader(IDbConnectionFactory connections) : IAdminSchemaReader
{
    public async Task<IReadOnlyList<AdminTable>> ReadAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);
        var names = new List<string>();
        await using (var tables = connection.CreateCommand())
        {
            tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await tables.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        var result = new List<AdminTable>(names.Count);
        foreach (var name in names)
        {
            await using var info = connection.CreateCommand();
            info.CommandText = "SELECT name, type, \"notnull\", pk, dflt_value FROM pragma_table_info(@table) ORDER BY cid";
            var parameter = info.CreateParameter();
            parameter.ParameterName = "table";
            parameter.Value = name;
            info.Parameters.Add(parameter);
            var columns = new List<AdminColumn>();
            await using var reader = await info.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                columns.Add(new AdminColumn(reader.GetString(0), Kind(type), reader.GetInt64(2) != 0, (int)reader.GetInt64(3), !reader.IsDBNull(4), type));
            }

            result.Add(new AdminTable(name, columns, false));
        }

        return result;
    }

    public string Parameter(AdminColumn column, string parameterName) => parameterName;

    // SQLite's type affinity rules: INT anywhere is an integer, REAL/FLOA/DOUB a real, BLOB (or no type) opaque.
    public static AdminColumnKind Kind(string declared)
    {
        var type = declared.ToUpperInvariant();
        if (type.Contains("INT", StringComparison.Ordinal) || type.Contains("BOOL", StringComparison.Ordinal))
        {
            return AdminColumnKind.Integer;
        }

        if (type.Contains("REAL", StringComparison.Ordinal) || type.Contains("FLOA", StringComparison.Ordinal) || type.Contains("DOUB", StringComparison.Ordinal)
            || type.Contains("NUMERIC", StringComparison.Ordinal) || type.Contains("DECIMAL", StringComparison.Ordinal))
        {
            return AdminColumnKind.Real;
        }

        return type.Length == 0 || type.Contains("BLOB", StringComparison.Ordinal) ? AdminColumnKind.Opaque : AdminColumnKind.Text;
    }
}
