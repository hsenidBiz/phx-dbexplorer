using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.IntegrationTests;

/// <summary>
/// Shared SQL Server container fixture — started once per test class collection.
/// Creates a test schema with tables, FKs, indexes, a stored procedure, and a function.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public ISchemaProvider Provider { get; private set; } = null!;
    public DatabaseConfig Config { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Config = new DatabaseConfig
        {
            DbType = DatabaseType.SqlServer,
            ConnectionString = _container.GetConnectionString(),
            SchemaFilter = ["dbo"]
        };

        await SeedDatabaseAsync(Config.ConnectionString);
        Provider = new SqlServerSchemaProvider(Config);
    }

    private static async Task SeedDatabaseAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        // SQL Server requires CREATE VIEW, CREATE PROCEDURE, and CREATE FUNCTION
        // to be the only statement in their batch, so each must be executed separately.
        string[] batches =
        [
            """
            CREATE TABLE dbo.Departments (
                DepartmentId   INT           NOT NULL IDENTITY PRIMARY KEY,
                DepartmentName NVARCHAR(100) NOT NULL,
                CreatedAt      DATETIME2     NOT NULL DEFAULT GETUTCDATE()
            )
            """,
            """
            CREATE TABLE dbo.Employees (
                EmployeeId   INT           NOT NULL IDENTITY,
                FirstName    NVARCHAR(100) NOT NULL,
                LastName     NVARCHAR(100) NOT NULL,
                Email        NVARCHAR(200) NOT NULL,
                DepartmentId INT           NULL,
                Salary       DECIMAL(12,2) NOT NULL DEFAULT 0,
                IsActive     BIT           NOT NULL DEFAULT 1,
                CONSTRAINT PK_Employees PRIMARY KEY (EmployeeId),
                CONSTRAINT UQ_Employees_Email UNIQUE (Email),
                CONSTRAINT FK_Employees_Departments FOREIGN KEY (DepartmentId)
                    REFERENCES dbo.Departments (DepartmentId) ON DELETE SET NULL ON UPDATE CASCADE
            )
            """,
            "CREATE INDEX IX_Employees_LastName ON dbo.Employees (LastName)",
            "CREATE INDEX IX_Employees_Department ON dbo.Employees (DepartmentId) WHERE IsActive = 1",
            """
            CREATE VIEW dbo.vw_ActiveEmployees AS
                SELECT e.EmployeeId, e.FirstName, e.LastName, e.Email, d.DepartmentName
                FROM dbo.Employees e
                LEFT JOIN dbo.Departments d ON e.DepartmentId = d.DepartmentId
                WHERE e.IsActive = 1
            """,
            """
            CREATE PROCEDURE dbo.usp_GetEmployeesByDept
                @DepartmentId INT,
                @IncludeInactive BIT = 0
            AS
            BEGIN
                SELECT * FROM dbo.Employees
                WHERE DepartmentId = @DepartmentId
                AND (IsActive = 1 OR @IncludeInactive = 1)
            END
            """,
            """
            CREATE FUNCTION dbo.fn_GetFullName(@FirstName NVARCHAR(100), @LastName NVARCHAR(100))
            RETURNS NVARCHAR(201)
            AS
            BEGIN
                RETURN @FirstName + ' ' + @LastName
            END
            """
        ];

        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, conn);
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
