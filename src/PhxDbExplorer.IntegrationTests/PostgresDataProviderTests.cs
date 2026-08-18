using FluentAssertions;
using Npgsql;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.IntegrationTests;

[Collection("Postgres")]
public class PostgresDataProviderTests(PostgresFixture fixture)
{
    // ── sample_table_data ────────────────────────────────────────────────────

    [Fact]
    public async Task SampleTableData_ReturnsRowsAndColumns()
    {
        var result = await fixture.Provider.SampleTableDataAsync("employees");

        result.Rows.Should().HaveCount(5);
        result.Columns.Select(c => c.Name).Should().Contain(["employee_id", "first_name", "salary"]);
        result.Rows.Should().Contain(r => (string?)r["last_name"] == "Lovelace");
    }

    [Fact]
    public async Task SampleTableData_NullColumnIsNullInResult()
    {
        var result = await fixture.Provider.SampleTableDataAsync(
            "employees", whereClause: "last_name = 'Dijkstra'");

        result.Rows.Should().ContainSingle();
        result.Rows[0]["department_id"].Should().BeNull();
    }

    [Fact]
    public async Task SampleTableData_HonoursLimitAndReportsTruncation()
    {
        var result = await fixture.Provider.SampleTableDataAsync("employees", limit: 2);

        result.Rows.Should().HaveCount(2);
        result.RowLimit.Should().Be(2);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task SampleTableData_LimitEqualToRowCount_IsNotTruncated()
    {
        // Exactly-full must not read as cut short — the provider fetches one row past the cap to tell them apart
        var result = await fixture.Provider.SampleTableDataAsync("employees", limit: 5);

        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SampleTableData_LimitAboveRowCount_IsNotTruncated()
    {
        var result = await fixture.Provider.SampleTableDataAsync("employees", limit: 50);

        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SampleTableData_LimitCannotExceedMaxRows()
    {
        var provider = new PostgresSchemaProvider(ConfigWithMaxRows(3));

        var result = await provider.SampleTableDataAsync("employees", limit: 1000);

        result.RowLimit.Should().Be(3);
        result.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task SampleTableData_AppliesWhereAndOrderBy()
    {
        var result = await fixture.Provider.SampleTableDataAsync(
            "employees", whereClause: "is_active = TRUE", orderBy: "salary DESC");

        result.Rows.Should().HaveCount(4);
        result.Rows.Select(r => (string?)r["last_name"]).First().Should().Be("Hopper");
    }

    [Fact]
    public async Task SampleTableData_ReadsViews()
    {
        var result = await fixture.Provider.SampleTableDataAsync("vw_active_employees");

        result.Rows.Should().HaveCount(4);
    }

    [Fact]
    public async Task SampleTableData_UnknownTable_Throws()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("no_such_table");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task SampleTableData_SchemaOutsideFilter_Throws()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("employees", schemaName: "pg_catalog");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not exposed*");
    }

    [Fact]
    public async Task SampleTableData_InjectionInTableName_IsRejected()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("employees\"; DROP TABLE public.departments --");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
        (await TableExistsAsync("departments")).Should().BeTrue();
    }

    [Fact]
    public async Task SampleTableData_InjectionInWhereClause_IsRejected()
    {
        var act = () => fixture.Provider.SampleTableDataAsync(
            "employees", whereClause: "1=1; DROP TABLE public.departments");

        await act.Should().ThrowAsync<ArgumentException>();
        (await TableExistsAsync("departments")).Should().BeTrue();
    }

    // ── execute_query ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_RunsJoinAndAggregate()
    {
        var result = await fixture.Provider.ExecuteQueryAsync("""
            SELECT d.department_name, COUNT(*) AS headcount
            FROM public.employees e
            JOIN public.departments d ON e.department_id = d.department_id
            GROUP BY d.department_name
            ORDER BY d.department_name
            """);

        result.Rows.Should().HaveCount(2);
        result.Rows[0]["department_name"].Should().Be("Engineering");
        result.Rows[0]["headcount"].Should().Be(2L);
    }

    [Fact]
    public async Task ExecuteQuery_CapsRowsAtMaxRows()
    {
        var provider = new PostgresSchemaProvider(ConfigWithMaxRows(2));

        var result = await provider.ExecuteQueryAsync("SELECT * FROM public.employees");

        result.Rows.Should().HaveCount(2);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_DuplicateColumnNamesStayAddressable()
    {
        var result = await fixture.Provider.ExecuteQueryAsync(
            "SELECT first_name, first_name FROM public.employees LIMIT 1");

        result.Columns.Should().HaveCount(2);
        result.Columns.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        result.Rows[0].Should().HaveCount(2);
    }

    [Theory]
    [InlineData("DELETE FROM public.departments")]
    [InlineData("UPDATE public.employees SET salary = 0")]
    [InlineData("DROP TABLE public.departments")]
    [InlineData("SELECT 1; DROP TABLE public.departments")]
    [InlineData("CALL public.usp_deactivate_employee(1)")]
    [InlineData("WITH x AS (DELETE FROM public.departments RETURNING *) SELECT * FROM x")]
    public async Task ExecuteQuery_WriteAttempts_AreRejectedAndChangeNothing(string sql)
    {
        var act = () => fixture.Provider.ExecuteQueryAsync(sql);

        await act.Should().ThrowAsync<ArgumentException>();
        (await TableExistsAsync("departments")).Should().BeTrue();
        (await RowCountAsync("public.employees")).Should().Be(5);
    }

    [Fact]
    public async Task ExecuteQuery_InvalidSql_ThrowsDbException()
    {
        var act = () => fixture.Provider.ExecuteQueryAsync("SELECT * FROM public.no_such_table");

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ── get_table_row_count ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTableRowCount_ReturnsExactCount()
    {
        var result = await fixture.Provider.GetTableRowCountAsync("employees");

        result.SchemaName.Should().Be("public");
        result.TableName.Should().Be("employees");
        result.RowCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTableRowCount_UnknownTable_Throws()
    {
        var act = () => fixture.Provider.GetTableRowCountAsync("no_such_table");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private DatabaseConfig ConfigWithMaxRows(int maxRows) => new()
    {
        DbType = DatabaseType.PostgreSQL,
        ConnectionString = fixture.Config.ConnectionString,
        SchemaFilter = fixture.Config.SchemaFilter,
        MaxRows = maxRows
    };

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(fixture.Config.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @t", conn);
        cmd.Parameters.AddWithValue("@t", tableName);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private async Task<long> RowCountAsync(string qualifiedTable)
    {
        await using var conn = new NpgsqlConnection(fixture.Config.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {qualifiedTable}", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
