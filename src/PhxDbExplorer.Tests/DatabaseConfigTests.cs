using PhxDbExplorer.Configuration;
using FluentAssertions;

namespace PhxDbExplorer.Tests;

public class DatabaseConfigTests : IDisposable
{
    private readonly List<string> _envVarsSet = [];

    private void SetEnv(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _envVarsSet.Add(key);
    }

    public void Dispose()
    {
        foreach (var key in _envVarsSet)
            Environment.SetEnvironmentVariable(key, null);
    }

    [Fact]
    public void FromEnvironment_MissingDbType_Throws()
    {
        Environment.SetEnvironmentVariable("DB_TYPE", null);
        Environment.SetEnvironmentVariable("CONNECTION_STRING", null);

        var act = () => DatabaseConfig.FromEnvironment();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DB_TYPE*");
    }

    [Fact]
    public void FromEnvironment_MissingConnectionString_Throws()
    {
        SetEnv("DB_TYPE", "mssql");
        Environment.SetEnvironmentVariable("CONNECTION_STRING", null);

        var act = () => DatabaseConfig.FromEnvironment();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CONNECTION_STRING*");
    }

    [Theory]
    [InlineData("badvalue")]
    [InlineData("oracle")]
    [InlineData("mysql")]
    public void FromEnvironment_InvalidDbType_Throws(string dbType)
    {
        SetEnv("DB_TYPE", dbType);
        SetEnv("CONNECTION_STRING", "Server=test;");

        var act = () => DatabaseConfig.FromEnvironment();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{dbType}'*");
    }

    [Theory]
    [InlineData("mssql", DatabaseType.SqlServer)]
    [InlineData("MSSQL", DatabaseType.SqlServer)]
    [InlineData("sqlserver", DatabaseType.SqlServer)]
    [InlineData("postgres", DatabaseType.PostgreSQL)]
    [InlineData("POSTGRES", DatabaseType.PostgreSQL)]
    [InlineData("postgresql", DatabaseType.PostgreSQL)]
    public void FromEnvironment_ValidDbType_ParsedCorrectly(string rawType, DatabaseType expected)
    {
        SetEnv("DB_TYPE", rawType);
        SetEnv("CONNECTION_STRING", "Server=test;");

        var config = DatabaseConfig.FromEnvironment();

        config.DbType.Should().Be(expected);
    }

    [Fact]
    public void FromEnvironment_MssqlWithNoSchemaFilter_DefaultsToDbo()
    {
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", "Server=test;");
        Environment.SetEnvironmentVariable("SCHEMA_FILTER", null);

        var config = DatabaseConfig.FromEnvironment();

        config.SchemaFilter.Should().ContainSingle().Which.Should().Be("dbo");
    }

    [Fact]
    public void FromEnvironment_PostgresWithNoSchemaFilter_DefaultsToPublic()
    {
        SetEnv("DB_TYPE", "postgres");
        SetEnv("CONNECTION_STRING", "Host=test;");
        Environment.SetEnvironmentVariable("SCHEMA_FILTER", null);

        var config = DatabaseConfig.FromEnvironment();

        config.SchemaFilter.Should().ContainSingle().Which.Should().Be("public");
    }

    [Fact]
    public void FromEnvironment_MultipleSchemas_ParsedCorrectly()
    {
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", "Server=test;");
        SetEnv("SCHEMA_FILTER", "dbo, hr, finance");

        var config = DatabaseConfig.FromEnvironment();

        config.SchemaFilter.Should().BeEquivalentTo(["dbo", "hr", "finance"]);
    }

    [Fact]
    public void FromEnvironment_ConnectionStringPreserved()
    {
        var connStr = "Server=myserver;Database=mydb;Trusted_Connection=True;";
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", connStr);

        var config = DatabaseConfig.FromEnvironment();

        config.ConnectionString.Should().Be(connStr);
    }

    [Fact]
    public void FromEnvironment_MaxRowsNotSet_UsesDefault()
    {
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", "Server=test;");
        Environment.SetEnvironmentVariable("MAX_ROWS", null);
        Environment.SetEnvironmentVariable("QUERY_TIMEOUT_SECONDS", null);

        var config = DatabaseConfig.FromEnvironment();

        config.MaxRows.Should().Be(DatabaseConfig.DefaultMaxRows);
        config.QueryTimeoutSeconds.Should().Be(DatabaseConfig.DefaultQueryTimeoutSeconds);
    }

    [Fact]
    public void FromEnvironment_MaxRowsParsed()
    {
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", "Server=test;");
        SetEnv("MAX_ROWS", "500");
        SetEnv("QUERY_TIMEOUT_SECONDS", "90");

        var config = DatabaseConfig.FromEnvironment();

        config.MaxRows.Should().Be(500);
        config.QueryTimeoutSeconds.Should().Be(90);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void FromEnvironment_InvalidMaxRows_Throws(string value)
    {
        SetEnv("DB_TYPE", "mssql");
        SetEnv("CONNECTION_STRING", "Server=test;");
        SetEnv("MAX_ROWS", value);

        var act = () => DatabaseConfig.FromEnvironment();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MAX_ROWS*");
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(5000, 100)]
    public void ResolveRowLimit_NeverExceedsMaxRows(int? requested, int expected)
    {
        var config = new DatabaseConfig { MaxRows = 100 };

        config.ResolveRowLimit(requested).Should().Be(expected);
    }
}
