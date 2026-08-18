using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using PhxDbExplorer.Models;
using PhxDbExplorer.Query;

namespace PhxDbExplorer.Providers;

public sealed partial class SqlServerSchemaProvider
{
    public async Task<QueryResult> SampleTableDataAsync(
        string tableName,
        string? schemaName = null,
        int? limit = null,
        string? whereClause = null,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        Validate(ReadOnlySqlValidator.ValidateFragment(whereClause, "where clause"));
        Validate(ReadOnlySqlValidator.ValidateFragment(orderBy, "order by clause"));

        var rowLimit = config.ResolveRowLimit(limit);

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var (schema, table) = await ResolveTableAsync(conn, schemaName, tableName, cancellationToken);

        // One row beyond the cap, so QueryResultReader can tell "exactly full" from "more to come"
        var sql = new StringBuilder()
            .Append("SELECT TOP (@__fetch) * FROM ")
            .Append(Quote(schema)).Append('.').Append(Quote(table));

        if (!string.IsNullOrWhiteSpace(whereClause))
            sql.Append(" WHERE ").Append(whereClause);
        if (!string.IsNullOrWhiteSpace(orderBy))
            sql.Append(" ORDER BY ").Append(orderBy);

        // Rolled back unconditionally: nothing a caller-supplied fragment does can persist
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var cmd = new SqlCommand(sql.ToString(), conn, tx) { CommandTimeout = config.QueryTimeoutSeconds };
        cmd.Parameters.AddWithValue("@__fetch", ProbeLimit(rowLimit));

        var result = await ReadAsync(cmd, rowLimit, cancellationToken);
        await tx.RollbackAsync(cancellationToken);
        return result;
    }

    public async Task<QueryResult> ExecuteQueryAsync(
        string sql,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Validate(ReadOnlySqlValidator.ValidateStatement(sql));

        var rowLimit = config.ResolveRowLimit(limit);

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // SQL Server has no read-only transaction mode, so the rollback is the hard guarantee
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = config.QueryTimeoutSeconds };

        var result = await ReadAsync(cmd, rowLimit, cancellationToken);
        await tx.RollbackAsync(cancellationToken);
        return result;
    }

    public async Task<TableRowCount> GetTableRowCountAsync(
        string tableName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var (schema, table) = await ResolveTableAsync(conn, schemaName, tableName, cancellationToken);

        var sql = $"SELECT COUNT_BIG(*) FROM {Quote(schema)}.{Quote(table)}";
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = config.QueryTimeoutSeconds };
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;

        return new TableRowCount(schema, table, count);
    }

    private static async Task<QueryResult> ReadAsync(SqlCommand cmd, int rowLimit, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await QueryResultReader.ReadAsync(reader, rowLimit, ct);
    }

    /// <summary>
    /// Confirms the table/view exists inside an allowed schema and returns the names exactly as the
    /// catalog spells them. Only these catalog-sourced names are ever interpolated into SQL, so a
    /// caller cannot smuggle syntax through <c>tableName</c> or <c>schemaName</c>.
    /// </summary>
    private async Task<(string Schema, string Table)> ResolveTableAsync(
        SqlConnection conn, string? schemaName, string tableName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("A table or view name is required.", nameof(tableName));

        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "dbo";

        if (!config.SchemaFilter.Contains(schema, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Schema '{schema}' is not exposed by this server. Allowed schemas: {string.Join(", ", config.SchemaFilter)}.",
                nameof(schemaName));

        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new ArgumentException($"Table or view '{schema}.{tableName}' was not found.", nameof(tableName));

        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>
    /// The number of rows to ask the server for: one past the cap, so a result set that exactly
    /// fills the cap can be distinguished from one that was cut short.
    /// </summary>
    private static int ProbeLimit(int rowLimit) => rowLimit == int.MaxValue ? rowLimit : rowLimit + 1;

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private static void Validate(SqlValidationResult result)
    {
        if (!result.IsValid)
            throw new ArgumentException(result.Error);
    }
}
