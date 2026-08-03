using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;
using FluentAssertions;

namespace PhxDbExplorer.Tests;

public class SchemaProviderFactoryTests
{
    private static DatabaseConfig MakeConfig(DatabaseType dbType) => new()
    {
        DbType = dbType,
        ConnectionString = "Server=test;",
        SchemaFilter = ["dbo"]
    };

    [Fact]
    public void Create_SqlServer_ReturnsSqlServerProvider()
    {
        var provider = SchemaProviderFactory.Create(MakeConfig(DatabaseType.SqlServer));
        provider.Should().BeOfType<SqlServerSchemaProvider>();
    }

    [Fact]
    public void Create_PostgreSQL_ReturnsPostgresProvider()
    {
        var provider = SchemaProviderFactory.Create(MakeConfig(DatabaseType.PostgreSQL));
        provider.Should().BeOfType<PostgresSchemaProvider>();
    }

    [Fact]
    public void Create_ReturnsISchemaProvider()
    {
        var provider = SchemaProviderFactory.Create(MakeConfig(DatabaseType.SqlServer));
        provider.Should().BeAssignableTo<ISchemaProvider>();
    }
}
