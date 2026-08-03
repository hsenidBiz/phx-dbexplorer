namespace PhxDbExplorer.Configuration;

public sealed class DatabaseConfig
{
    public DatabaseType DbType { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public IReadOnlyList<string> SchemaFilter { get; init; } = [];

    public static DatabaseConfig FromEnvironment()
    {
        var rawType = Environment.GetEnvironmentVariable("DB_TYPE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(rawType))
            throw new InvalidOperationException("Environment variable 'DB_TYPE' is required. Set it to 'mssql' or 'postgres'.");

        var dbType = rawType switch
        {
            "mssql" or "sqlserver" => DatabaseType.SqlServer,
            "postgres" or "postgresql" => DatabaseType.PostgreSQL,
            _ => throw new InvalidOperationException($"Unsupported DB_TYPE '{rawType}'. Use 'mssql' or 'postgres'.")
        };

        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")?.Trim();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Environment variable 'CONNECTION_STRING' is required.");

        var schemaFilterRaw = Environment.GetEnvironmentVariable("SCHEMA_FILTER")?.Trim();
        IReadOnlyList<string> schemaFilter;

        if (string.IsNullOrEmpty(schemaFilterRaw))
        {
            schemaFilter = dbType == DatabaseType.SqlServer ? ["dbo"] : ["public"];
        }
        else
        {
            schemaFilter = schemaFilterRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return new DatabaseConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            SchemaFilter = schemaFilter
        };
    }
}

public enum DatabaseType
{
    SqlServer,
    PostgreSQL
}
