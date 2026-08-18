using PhxDbExplorer.Models;

namespace PhxDbExplorer.Providers;

/// <summary>
/// Read-only row-data access. Every implementation must run caller-supplied SQL inside a
/// transaction that is always rolled back, and must never return more than the configured
/// row limit.
/// </summary>
public interface IDataProvider
{
    Task<QueryResult> SampleTableDataAsync(
        string tableName,
        string? schemaName = null,
        int? limit = null,
        string? whereClause = null,
        string? orderBy = null,
        CancellationToken cancellationToken = default);

    Task<QueryResult> ExecuteQueryAsync(
        string sql,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<TableRowCount> GetTableRowCountAsync(
        string tableName,
        string? schemaName = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Both halves of the provider surface, as implemented by each engine-specific provider.</summary>
public interface IDatabaseProvider : ISchemaProvider, IDataProvider;
