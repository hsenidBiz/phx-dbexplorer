using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.Tools;

[McpServerToolType]
public sealed class SchemaTools(ISchemaProvider schemaProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    [McpServerTool(Name = "list_tables", ReadOnly = true)]
    [Description("Lists all tables and views in the configured schema(s). Returns schema name, table/view name, type (BASE TABLE or VIEW), and optional description.")]
    public async Task<string> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = await schemaProvider.ListTablesAsync(cancellationToken);
        return Serialize(tables);
    }

    [McpServerTool(Name = "get_table_schema", ReadOnly = true)]
    [Description("""
        Returns the full schema details for a specific table or view, including:
        - Columns (name, data type, nullability, default value, identity, primary key flag, description)
        - Foreign keys (constraint name, column, referenced table/column, delete/update actions)
        - Indexes (name, type, uniqueness, columns)
        - Check and Unique constraints
        """)]
    public async Task<string> GetTableSchemaAsync(
        [Description("The name of the table or view to inspect.")] string tableName,
        [Description("Optional schema name. If not provided, the first configured schema is used.")] string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await schemaProvider.GetTableSchemaAsync(tableName, schemaName, cancellationToken);
        return Serialize(result);
    }

    [McpServerTool(Name = "list_stored_procedures", ReadOnly = true)]
    [Description("Lists all stored procedures in the configured schema(s). Returns schema name, procedure name, created date, and last altered date.")]
    public async Task<string> ListStoredProceduresAsync(CancellationToken cancellationToken = default)
    {
        var procedures = await schemaProvider.ListStoredProceduresAsync(cancellationToken);
        return Serialize(procedures);
    }

    [McpServerTool(Name = "get_procedure_definition", ReadOnly = true)]
    [Description("Returns the full definition of a stored procedure including its parameters (name, data type, direction) and the SQL source code.")]
    public async Task<string> GetProcedureDefinitionAsync(
        [Description("The name of the stored procedure.")] string procedureName,
        [Description("Optional schema name. If not provided, the first configured schema is used.")] string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await schemaProvider.GetProcedureDefinitionAsync(procedureName, schemaName, cancellationToken);
        if (result is null)
            return $"{{\"error\": \"Procedure '{procedureName}' not found.\"}}";
        return Serialize(result);
    }

    [McpServerTool(Name = "list_functions", ReadOnly = true)]
    [Description("Lists all user-defined functions (UDFs) in the configured schema(s). Returns schema name, function name, function type, return type, and dates.")]
    public async Task<string> ListFunctionsAsync(CancellationToken cancellationToken = default)
    {
        var functions = await schemaProvider.ListFunctionsAsync(cancellationToken);
        return Serialize(functions);
    }

    [McpServerTool(Name = "get_function_definition", ReadOnly = true)]
    [Description("Returns the full definition of a user-defined function including its parameters and SQL source code.")]
    public async Task<string> GetFunctionDefinitionAsync(
        [Description("The name of the function.")] string functionName,
        [Description("Optional schema name. If not provided, the first configured schema is used.")] string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await schemaProvider.GetFunctionDefinitionAsync(functionName, schemaName, cancellationToken);
        if (result is null)
            return $"{{\"error\": \"Function '{functionName}' not found.\"}}";
        return Serialize(result);
    }

    [McpServerTool(Name = "search_schema", ReadOnly = true)]
    [Description("Searches for database objects (tables, views, columns, stored procedures, functions) by keyword using a case-insensitive partial match. Useful for discovering relevant objects when you know part of a name.")]
    public async Task<string> SearchSchemaAsync(
        [Description("The keyword to search for. Partial matches are supported (e.g., 'customer' will match 'CustomerOrder', 'GetCustomers', etc.).")] string keyword,
        CancellationToken cancellationToken = default)
    {
        var result = await schemaProvider.SearchSchemaAsync(keyword, cancellationToken);
        return Serialize(result);
    }
}
