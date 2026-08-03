using Npgsql;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.IntegrationTests;

/// <summary>
/// Shared PostgreSQL container fixture — started once per test class collection.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public ISchemaProvider Provider { get; private set; } = null!;
    public DatabaseConfig Config { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Config = new DatabaseConfig
        {
            DbType = DatabaseType.PostgreSQL,
            ConnectionString = _container.GetConnectionString(),
            SchemaFilter = ["public"]
        };

        await SeedDatabaseAsync(Config.ConnectionString);
        Provider = new PostgresSchemaProvider(Config);
    }

    private static async Task SeedDatabaseAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string ddl = """
            CREATE TABLE public.departments (
                department_id   SERIAL        PRIMARY KEY,
                department_name VARCHAR(100)  NOT NULL,
                created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            );

            CREATE TABLE public.employees (
                employee_id   SERIAL        PRIMARY KEY,
                first_name    VARCHAR(100)  NOT NULL,
                last_name     VARCHAR(100)  NOT NULL,
                email         VARCHAR(200)  NOT NULL,
                department_id INT           NULL,
                salary        NUMERIC(12,2) NOT NULL DEFAULT 0,
                is_active     BOOLEAN       NOT NULL DEFAULT TRUE,
                CONSTRAINT uq_employees_email UNIQUE (email),
                CONSTRAINT fk_employees_departments FOREIGN KEY (department_id)
                    REFERENCES public.departments (department_id) ON DELETE SET NULL ON UPDATE CASCADE
            );

            CREATE INDEX ix_employees_last_name ON public.employees (last_name);
            CREATE INDEX ix_employees_department ON public.employees (department_id) WHERE is_active = TRUE;

            CREATE VIEW public.vw_active_employees AS
                SELECT e.employee_id, e.first_name, e.last_name, e.email, d.department_name
                FROM public.employees e
                LEFT JOIN public.departments d ON e.department_id = d.department_id
                WHERE e.is_active = TRUE;

            CREATE OR REPLACE PROCEDURE public.usp_deactivate_employee(p_employee_id INT)
            LANGUAGE plpgsql AS $$
            BEGIN
                UPDATE public.employees SET is_active = FALSE WHERE employee_id = p_employee_id;
            END;
            $$;

            CREATE OR REPLACE FUNCTION public.fn_get_full_name(p_first_name VARCHAR, p_last_name VARCHAR)
            RETURNS VARCHAR LANGUAGE plpgsql AS $$
            BEGIN
                RETURN p_first_name || ' ' || p_last_name;
            END;
            $$;
            """;

        await using var cmd = new NpgsqlCommand(ddl, conn);
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
