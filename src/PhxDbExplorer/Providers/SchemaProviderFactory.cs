using PhxDbExplorer.Configuration;

namespace PhxDbExplorer.Providers;

public static class SchemaProviderFactory
{
    public static ISchemaProvider Create(DatabaseConfig config) => config.DbType switch
    {
        DatabaseType.SqlServer => new SqlServerSchemaProvider(config),
        DatabaseType.PostgreSQL => new PostgresSchemaProvider(config),
        _ => throw new NotSupportedException($"Database type '{config.DbType}' is not supported.")
    };
}
