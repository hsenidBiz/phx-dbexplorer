namespace PhxDbExplorer.Configuration;

public sealed class DatabaseConfig
{
    public const int DefaultMaxRows = 100;
    public const int DefaultQueryTimeoutSeconds = 30;

    public DatabaseType DbType { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public IReadOnlyList<string> SchemaFilter { get; init; } = [];

    /// <summary>Hard ceiling on the number of rows any data-read tool may return.</summary>
    public int MaxRows { get; init; } = DefaultMaxRows;

    /// <summary>Command timeout applied to data-read queries.</summary>
    public int QueryTimeoutSeconds { get; init; } = DefaultQueryTimeoutSeconds;

    /// <summary>
    /// Resolves a caller-supplied row limit against <see cref="MaxRows"/>. A missing or
    /// non-positive request falls back to the configured maximum, which is never exceeded.
    /// </summary>
    public int ResolveRowLimit(int? requested) =>
        requested is null or <= 0 ? MaxRows : Math.Min(requested.Value, MaxRows);

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
            SchemaFilter = schemaFilter,
            MaxRows = ReadPositiveInt("MAX_ROWS", DefaultMaxRows),
            QueryTimeoutSeconds = ReadPositiveInt("QUERY_TIMEOUT_SECONDS", DefaultQueryTimeoutSeconds)
        };
    }

    private static int ReadPositiveInt(string variable, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(variable)?.Trim();
        if (string.IsNullOrEmpty(raw))
            return defaultValue;

        if (!int.TryParse(raw, out var value) || value <= 0)
            throw new InvalidOperationException(
                $"Environment variable '{variable}' must be a positive integer (got '{raw}').");

        return value;
    }
}

public enum DatabaseType
{
    SqlServer,
    PostgreSQL
}
