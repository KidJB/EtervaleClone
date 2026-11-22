using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Admin;

namespace PristonTale.Persistence.Postgres;

// The admin panel's view of a PostgreSQL database: the public schema's tables and columns from the information schema, the
// primary keys from its key usage. Every written value is cast to its column's type, so a uuid, date or jsonb column takes
// the text the panel sends.
public sealed class PostgresAdminSchemaReader(IDbConnectionFactory connections) : IAdminSchemaReader
{
    private const string ColumnsSql = """
        SELECT c.table_name, c.column_name, c.data_type, c.is_nullable, c.column_default IS NOT NULL OR c.is_identity = 'YES',
               COALESCE(k.ordinal_position, 0)
        FROM information_schema.columns c
        JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
        LEFT JOIN information_schema.table_constraints p
            ON p.table_schema = c.table_schema AND p.table_name = c.table_name AND p.constraint_type = 'PRIMARY KEY'
        LEFT JOIN information_schema.key_column_usage k
            ON k.constraint_schema = p.constraint_schema AND k.constraint_name = p.constraint_name AND k.column_name = c.column_name
        WHERE c.table_schema = 'public'
        ORDER BY c.table_name, c.ordinal_position
        """;

    public async Task<IReadOnlyList<AdminTable>> ReadAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = ColumnsSql;
        var tables = new List<AdminTable>();
        string? current = null;
        var columns = new List<AdminColumn>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var table = reader.GetString(0);
            if (table != current)
            {
                if (current is not null)
                {
                    tables.Add(new AdminTable(current, columns, false));
                }

                current = table;
                columns = [];
            }

            var type = reader.GetString(2);
            columns.Add(new AdminColumn(reader.GetString(1), Kind(type), reader.GetString(3) == "NO", reader.GetInt32(5), reader.GetBoolean(4), type));
        }

        if (current is not null)
        {
            tables.Add(new AdminTable(current, columns, false));
        }

        return tables;
    }

    public string Parameter(AdminColumn column, string parameterName) =>
        column.SqlType.Length == 0 ? parameterName : $"CAST({parameterName} AS {column.SqlType})";

    public static AdminColumnKind Kind(string type) => type switch
    {
        "smallint" or "integer" or "bigint" => AdminColumnKind.Integer,
        "real" or "double precision" or "numeric" => AdminColumnKind.Real,
        "boolean" => AdminColumnKind.Boolean,
        "json" or "jsonb" => AdminColumnKind.Json,
        "bytea" or "ARRAY" or "USER-DEFINED" => AdminColumnKind.Opaque,
        _ => AdminColumnKind.Text
    };
}
