using System.Data.Common;
using System.Globalization;
using System.Text;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Admin;

namespace PristonTale.Persistence.Repositories;

// The admin panel's generic table access, shared by both providers. Rows are read and written through plain ADO commands
// (a row of an arbitrary table has no record type); names come only from the provider's schema (AdminPolicy applied) and
// are quoted, values are parameters. Each write runs in a transaction and must touch exactly the rows it names.
public sealed class AdminDataRepository(IDbConnectionFactory connections, IAdminSchemaReader schema) : IAdminDataRepository
{
    public async Task<IReadOnlyList<AdminTable>> TablesAsync(CancellationToken ct = default) => AdminPolicy.Apply(await schema.ReadAsync(ct));

    public async Task<AdminPage?> RowsAsync(string table, AdminQuery query, CancellationToken ct = default)
    {
        if (await FindAsync(table, ct) is not { } found)
        {
            return null;
        }

        var size = Math.Clamp(query.Size, 1, AdminQuery.MaxSize);
        var page = Math.Max(0, query.Page);
        var columns = string.Join(", ", found.Columns.Select(static column => Quote(column.Name)));
        var where = new StringBuilder();
        if (query.Search.Length > 0)
        {
            where.Append(" WHERE ");
            where.Append(string.Join(" OR ", found.Columns.Where(static column => column.Kind != AdminColumnKind.Opaque)
                .Select(static column => $"LOWER(CAST({Quote(column.Name)} AS TEXT)) LIKE @search ESCAPE '\\'")));
        }

        var sort = found.Find(query.Sort) ?? found.Keys.FirstOrDefault() ?? found.Columns[0];
        var order = $" ORDER BY {Quote(sort.Name)}{(query.Descending ? " DESC" : "")}";
        var search = "%" + query.Search.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var connection = await connections.OpenAsync(ct);
        long total;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM {Quote(found.Name)}{where}";
            AddSearch(count, query, search);
            total = Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        var rows = new List<string?[]>(size);
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT {columns} FROM {Quote(found.Name)}{where}{order} LIMIT {size} OFFSET {(long)page * size}";
            AddSearch(select, query, search);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new string?[found.Columns.Count];
                for (var index = 0; index < row.Length; index++)
                {
                    row[index] = reader.IsDBNull(index) ? null : Text(reader.GetValue(index));
                }

                rows.Add(row);
            }
        }

        return new AdminPage(rows, total, page, size);
    }

    public async Task<AdminWriteResult> UpdateAsync(string table, IReadOnlyDictionary<string, string?> key, IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        if (await WritableAsync(table, ct) is not { } found)
        {
            return AdminWriteResult.Refused($"{table} cannot be edited");
        }

        if (values.Count == 0)
        {
            return AdminWriteResult.Refused("nothing to change");
        }

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var where = KeyClause(found, key, command);
        if (where.Error is { } keyError)
        {
            return AdminWriteResult.Refused(keyError);
        }

        var previous = await ReadRowAsync(found, where.Clause, command, ct);
        if (previous is null)
        {
            return AdminWriteResult.Refused("the row no longer exists");
        }

        var sets = new List<string>(values.Count);
        foreach (var (name, text) in values)
        {
            if (found.Find(name) is not { } column)
            {
                return AdminWriteResult.Refused($"{table} has no column {name}");
            }

            var value = AdminValue.Parse(column, text, previous.GetValueOrDefault(column.Name));
            if (!value.Ok)
            {
                return AdminWriteResult.Refused(value.Error!);
            }

            var parameter = "v" + sets.Count.ToString(CultureInfo.InvariantCulture);
            Add(command, parameter, value.Value);
            sets.Add($"{Quote(column.Name)} = {schema.Parameter(column, "@" + parameter)}");
        }

        command.CommandText = $"UPDATE {Quote(found.Name)} SET {string.Join(", ", sets)} WHERE {where.Clause}";
        return await FinishAsync(command, transaction, ct);
    }

    public async Task<AdminWriteResult> InsertAsync(string table, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        if (await WritableAsync(table, ct) is not { } found)
        {
            return AdminWriteResult.Refused($"{table} cannot be edited");
        }

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var names = new List<string>();
        var parameters = new List<string>();
        foreach (var column in found.Columns)
        {
            if (!values.TryGetValue(column.Name, out var text))
            {
                if (column.NotNull && !column.HasDefault && !(column.IsKey && column.Kind == AdminColumnKind.Integer && found.Keys.Count == 1))
                {
                    return AdminWriteResult.Refused($"{column.Name} is required");
                }

                continue;
            }

            var value = AdminValue.Parse(column, text);
            if (!value.Ok)
            {
                return AdminWriteResult.Refused(value.Error!);
            }

            var parameter = "v" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            Add(command, parameter, value.Value);
            names.Add(Quote(column.Name));
            parameters.Add(schema.Parameter(column, "@" + parameter));
        }

        if (values.Keys.FirstOrDefault(name => found.Find(name) is null) is { } unknown)
        {
            return AdminWriteResult.Refused($"{table} has no column {unknown}");
        }

        if (names.Count == 0)
        {
            return AdminWriteResult.Refused("nothing to insert");
        }

        command.CommandText = $"INSERT INTO {Quote(found.Name)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)})";
        return await FinishAsync(command, transaction, ct);
    }

    public async Task<AdminWriteResult> DeleteAsync(string table, IReadOnlyDictionary<string, string?> key, CancellationToken ct = default)
    {
        if (await WritableAsync(table, ct) is not { } found)
        {
            return AdminWriteResult.Refused($"{table} cannot be edited");
        }

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var where = KeyClause(found, key, command);
        if (where.Error is { } error)
        {
            return AdminWriteResult.Refused(error);
        }

        command.CommandText = $"DELETE FROM {Quote(found.Name)} WHERE {where.Clause}";
        return await FinishAsync(command, transaction, ct);
    }

    private async Task<AdminTable?> FindAsync(string table, CancellationToken ct) =>
        (await TablesAsync(ct)).FirstOrDefault(candidate => string.Equals(candidate.Name, table, StringComparison.Ordinal));

    private async Task<AdminTable?> WritableAsync(string table, CancellationToken ct) => await FindAsync(table, ct) is { ReadOnly: false } found ? found : null;

    // The WHERE clause naming one row by its whole primary key; its key parameters are added to the command.
    private (string Clause, string? Error) KeyClause(AdminTable table, IReadOnlyDictionary<string, string?> key, DbCommand command)
    {
        var parts = new List<string>();
        foreach (var column in table.Keys)
        {
            if (!key.TryGetValue(column.Name, out var text) || text is null)
            {
                return ("", $"the key column {column.Name} is missing");
            }

            var value = AdminValue.Parse(column, text);
            if (!value.Ok)
            {
                return ("", value.Error);
            }

            var parameter = "k" + parts.Count.ToString(CultureInfo.InvariantCulture);
            Add(command, parameter, value.Value);

            parts.Add($"{Quote(column.Name)} = {schema.Parameter(column, "@" + parameter)}");
        }

        return key.Count == table.Keys.Count ? (string.Join(" AND ", parts), null) : ("", "the key names columns outside the primary key");
    }

    // The row the command's key parameters name, as text by column; null when it is gone.
    private static async Task<Dictionary<string, string?>?> ReadRowAsync(AdminTable table, string where, DbCommand command, CancellationToken ct)
    {
        command.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(static column => Quote(column.Name)))} FROM {Quote(table.Name)} WHERE {where}";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < table.Columns.Count; index++)
        {
            row[table.Columns[index].Name] = reader.IsDBNull(index) ? null : Text(reader.GetValue(index));
        }

        return row;
    }

    private static async Task<AdminWriteResult> FinishAsync(DbCommand command, DbTransaction transaction, CancellationToken ct)
    {
        int rows;
        try
        {
            rows = await command.ExecuteNonQueryAsync(ct);
        }
        catch (DbException exception)
        {
            await transaction.RollbackAsync(ct);
            return AdminWriteResult.Refused(exception.Message);
        }

        if (rows != 1)
        {
            await transaction.RollbackAsync(ct);
            return AdminWriteResult.Refused(rows == 0 ? "no row matched" : "more than one row matched; nothing changed");
        }

        await transaction.CommitAsync(ct);
        return new AdminWriteResult(true, rows);
    }

    private static void AddSearch(DbCommand command, AdminQuery query, string search)
    {
        if (query.Search.Length > 0)
        {
            Add(command, "search", search);
        }
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Text(object value) => value switch
    {
        byte[] bytes => $"<{bytes.Length} bytes>",
        bool flag => flag ? "true" : "false",
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
