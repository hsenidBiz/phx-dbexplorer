using PhxDbExplorer.Models;

namespace PhxDbExplorer.Providers;

public interface ISchemaProvider
{
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default);

    Task<TableSchemaResult> GetTableSchemaAsync(
        string tableName,
        string? schemaName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcedureInfo>> ListStoredProceduresAsync(CancellationToken cancellationToken = default);

    Task<ProcedureDefinition?> GetProcedureDefinitionAsync(
        string procedureName,
        string? schemaName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FunctionInfo>> ListFunctionsAsync(CancellationToken cancellationToken = default);

    Task<FunctionDefinition?> GetFunctionDefinitionAsync(
        string functionName,
        string? schemaName = null,
        CancellationToken cancellationToken = default);

    Task<SearchResult> SearchSchemaAsync(string keyword, CancellationToken cancellationToken = default);
}
