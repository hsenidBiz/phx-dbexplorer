using System.ComponentModel;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.Tools;

[McpServerToolType]
public sealed class DataTools(IDataProvider dataProvider, DatabaseConfig config)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string Error(string message) => Serialize(new { error = message });

    /// <summary>
    /// Runs a data-read call, turning the failures a caller can actually cause — a bad table name,
    /// a rejected fragment, invalid SQL — into a readable error rather than a transport fault.
    /// </summary>
    private static async Task<string> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Serialize(await action());
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
        catch (DbException ex)
        {
            return Error($"The database rejected the query: {ex.Message}");
        }
    }

    [McpServerTool(Name = "sample_table_data", ReadOnly = true)]
    [Description("""
        Returns a sample of rows from a table or view. The table is resolved against the catalog and
        must live in one of the configured schemas. Prefer this over execute_query for simple reads.
        Results are always capped by the server's row limit (MAX_ROWS); 'truncated' in the response
        indicates more rows matched than were returned.
        """)]
    public Task<string> SampleTableDataAsync(
        [Description("The name of the table or view to read from.")] string tableName,
        [Description("Optional schema name. If not provided, the first configured schema is used.")] string? schemaName = null,
        [Description("Maximum number of rows to return. Capped by the server's MAX_ROWS setting.")] int? limit = null,
        [Description("Optional SQL WHERE predicate without the WHERE keyword, e.g. \"Status = 'Active' AND Salary > 50000\". Must be read-only.")] string? whereClause = null,
        [Description("Optional SQL ORDER BY list without the ORDER BY keyword, e.g. \"HireDate DESC, LastName\".")] string? orderBy = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => dataProvider.SampleTableDataAsync(
            tableName, schemaName, limit, whereClause, orderBy, cancellationToken));

    [McpServerTool(Name = "execute_query", ReadOnly = true)]
    [Description("""
        Executes a single read-only SQL query and returns the rows. Use for joins, aggregates, and
        anything sample_table_data cannot express.

        Restrictions, all enforced server-side:
        - The statement must be a single SELECT or WITH ... SELECT. Multiple statements are rejected.
        - Writes, DDL, EXEC/CALL, SELECT ... INTO, and session or transaction control are rejected.
        - The query runs inside a transaction that is always rolled back (a READ ONLY transaction on
          PostgreSQL), so it can never change data even if it slips past validation.
        - Results are capped by the server's row limit (MAX_ROWS); add your own TOP/LIMIT for clarity.
        """)]
    public Task<string> ExecuteQueryAsync(
        [Description("The SELECT statement to run. Single statement, no trailing extra statements.")] string sql,
        [Description("Maximum number of rows to return. Capped by the server's MAX_ROWS setting.")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => dataProvider.ExecuteQueryAsync(sql, limit, cancellationToken));

    [McpServerTool(Name = "get_table_row_count", ReadOnly = true)]
    [Description("Returns the exact row count for a table or view. Useful for sizing a table before sampling it.")]
    public Task<string> GetTableRowCountAsync(
        [Description("The name of the table or view to count.")] string tableName,
        [Description("Optional schema name. If not provided, the first configured schema is used.")] string? schemaName = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => dataProvider.GetTableRowCountAsync(tableName, schemaName, cancellationToken));

    [McpServerTool(Name = "get_data_read_limits", ReadOnly = true)]
    [Description("Returns the server-side limits that apply to the data-read tools: the maximum rows returned, the query timeout, and the schemas that are readable.")]
    public string GetDataReadLimits() => Serialize(new
    {
        maxRows = config.MaxRows,
        queryTimeoutSeconds = config.QueryTimeoutSeconds,
        readableSchemas = config.SchemaFilter
    });
}
