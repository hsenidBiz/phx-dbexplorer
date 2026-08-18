using FluentAssertions;
using Microsoft.Data.SqlClient;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;

namespace PhxDbExplorer.IntegrationTests;

[Collection("SqlServer")]
public class SqlServerDataProviderTests(SqlServerFixture fixture)
{
    // ── sample_table_data ────────────────────────────────────────────────────

    [Fact]
    public async Task SampleTableData_ReturnsRowsAndColumns()
    {
        var result = await fixture.Provider.SampleTableDataAsync("Employees");

        result.Rows.Should().HaveCount(5);
        result.Columns.Select(c => c.Name).Should().Contain(["EmployeeId", "FirstName", "Salary"]);
        result.Rows.Should().Contain(r => (string?)r["LastName"] == "Lovelace");
    }

    [Fact]
    public async Task SampleTableData_NullColumnIsNullInResult()
    {
        var result = await fixture.Provider.SampleTableDataAsync(
            "Employees", whereClause: "LastName = 'Dijkstra'");

        result.Rows.Should().ContainSingle();
        result.Rows[0]["DepartmentId"].Should().BeNull();
    }

    [Fact]
    public async Task SampleTableData_HonoursLimitAndReportsTruncation()
    {
        var result = await fixture.Provider.SampleTableDataAsync("Employees", limit: 2);

        result.Rows.Should().HaveCount(2);
        result.RowLimit.Should().Be(2);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task SampleTableData_LimitEqualToRowCount_IsNotTruncated()
    {
        // Exactly-full must not read as cut short — the provider fetches one row past the cap to tell them apart
        var result = await fixture.Provider.SampleTableDataAsync("Employees", limit: 5);

        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SampleTableData_LimitAboveRowCount_IsNotTruncated()
    {
        var result = await fixture.Provider.SampleTableDataAsync("Employees", limit: 50);

        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SampleTableData_LimitCannotExceedMaxRows()
    {
        var config = ConfigWithMaxRows(3);
        var provider = new SqlServerSchemaProvider(config);

        var result = await provider.SampleTableDataAsync("Employees", limit: 1000);

        result.RowLimit.Should().Be(3);
        result.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task SampleTableData_AppliesWhereAndOrderBy()
    {
        var result = await fixture.Provider.SampleTableDataAsync(
            "Employees", whereClause: "IsActive = 1", orderBy: "Salary DESC");

        result.Rows.Should().HaveCount(4);
        result.Rows.Select(r => (string?)r["LastName"]).First().Should().Be("Hopper");
    }

    [Fact]
    public async Task SampleTableData_ReadsViews()
    {
        var result = await fixture.Provider.SampleTableDataAsync("vw_ActiveEmployees");

        result.Rows.Should().HaveCount(4);
    }

    [Fact]
    public async Task SampleTableData_UnknownTable_Throws()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("NoSuchTable");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task SampleTableData_SchemaOutsideFilter_Throws()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("Employees", schemaName: "sys");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not exposed*");
    }

    [Fact]
    public async Task SampleTableData_InjectionInTableName_IsRejected()
    {
        var act = () => fixture.Provider.SampleTableDataAsync("Employees]; DROP TABLE dbo.Departments --");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
        (await TableExistsAsync("Departments")).Should().BeTrue();
    }

    [Fact]
    public async Task SampleTableData_InjectionInWhereClause_IsRejected()
    {
        var act = () => fixture.Provider.SampleTableDataAsync(
            "Employees", whereClause: "1=1; DROP TABLE dbo.Departments");

        await act.Should().ThrowAsync<ArgumentException>();
        (await TableExistsAsync("Departments")).Should().BeTrue();
    }

    // ── execute_query ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_RunsJoinAndAggregate()
    {
        var result = await fixture.Provider.ExecuteQueryAsync("""
            SELECT d.DepartmentName, COUNT(*) AS Headcount
            FROM dbo.Employees e
            JOIN dbo.Departments d ON e.DepartmentId = d.DepartmentId
            GROUP BY d.DepartmentName
            ORDER BY d.DepartmentName
            """);

        result.Rows.Should().HaveCount(2);
        result.Rows[0]["DepartmentName"].Should().Be("Engineering");
        result.Rows[0]["Headcount"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteQuery_CapsRowsAtMaxRows()
    {
        var provider = new SqlServerSchemaProvider(ConfigWithMaxRows(2));

        var result = await provider.ExecuteQueryAsync("SELECT * FROM dbo.Employees");

        result.Rows.Should().HaveCount(2);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_DuplicateColumnNamesStayAddressable()
    {
        var result = await fixture.Provider.ExecuteQueryAsync(
            "SELECT TOP 1 FirstName, FirstName FROM dbo.Employees");

        result.Columns.Should().HaveCount(2);
        result.Columns.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        result.Rows[0].Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteQuery_UnnamedColumnGetsPlaceholderName()
    {
        var result = await fixture.Provider.ExecuteQueryAsync("SELECT COUNT(*) FROM dbo.Employees");

        result.Columns.Should().ContainSingle();
        result.Rows[0].Values.Single().Should().Be(5);
    }

    [Theory]
    [InlineData("DELETE FROM dbo.Departments")]
    [InlineData("UPDATE dbo.Employees SET Salary = 0")]
    [InlineData("DROP TABLE dbo.Departments")]
    [InlineData("SELECT 1; DROP TABLE dbo.Departments")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.Employees")]
    [InlineData("EXEC dbo.usp_GetEmployeesByDept 1")]
    public async Task ExecuteQuery_WriteAttempts_AreRejectedAndChangeNothing(string sql)
    {
        var act = () => fixture.Provider.ExecuteQueryAsync(sql);

        await act.Should().ThrowAsync<ArgumentException>();
        (await TableExistsAsync("Departments")).Should().BeTrue();
        (await RowCountAsync("dbo.Employees")).Should().Be(5);
    }

    [Fact]
    public async Task ExecuteQuery_InvalidSql_ThrowsDbException()
    {
        var act = () => fixture.Provider.ExecuteQueryAsync("SELECT * FROM dbo.NoSuchTable");

        await act.Should().ThrowAsync<SqlException>();
    }

    // ── get_table_row_count ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTableRowCount_ReturnsExactCount()
    {
        var result = await fixture.Provider.GetTableRowCountAsync("Employees");

        result.SchemaName.Should().Be("dbo");
        result.TableName.Should().Be("Employees");
        result.RowCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTableRowCount_UnknownTable_Throws()
    {
        var act = () => fixture.Provider.GetTableRowCountAsync("NoSuchTable");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private DatabaseConfig ConfigWithMaxRows(int maxRows) => new()
    {
        DbType = DatabaseType.SqlServer,
        ConnectionString = fixture.Config.ConnectionString,
        SchemaFilter = fixture.Config.SchemaFilter,
        MaxRows = maxRows
    };

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new SqlConnection(fixture.Config.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @t", conn);
        cmd.Parameters.AddWithValue("@t", tableName);
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private async Task<int> RowCountAsync(string qualifiedTable)
    {
        await using var conn = new SqlConnection(fixture.Config.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {qualifiedTable}", conn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
