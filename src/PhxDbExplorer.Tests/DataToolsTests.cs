using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Moq;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Models;
using PhxDbExplorer.Providers;
using PhxDbExplorer.Tools;

namespace PhxDbExplorer.Tests;

public class DataToolsTests
{
    private readonly Mock<IDataProvider> _mockProvider = new();
    private readonly DatabaseConfig _config = new()
    {
        DbType = DatabaseType.SqlServer,
        ConnectionString = "Server=.;Database=Test;",
        SchemaFilter = ["dbo", "hr"],
        MaxRows = 50,
        QueryTimeoutSeconds = 15
    };

    private readonly DataTools _tools;

    public DataToolsTests()
    {
        _tools = new DataTools(_mockProvider.Object, _config);
    }

    private static QueryResult SampleResult(bool truncated = false) => new(
        Columns: [new QueryColumn("EmployeeId", "int"), new QueryColumn("FirstName", "nvarchar")],
        Rows:
        [
            new Dictionary<string, object?> { ["EmployeeId"] = 1, ["FirstName"] = "Ada" },
            new Dictionary<string, object?> { ["EmployeeId"] = 2, ["FirstName"] = null }
        ],
        RowCount: 2,
        RowLimit: 50,
        Truncated: truncated);

    // ── sample_table_data ────────────────────────────────────────────────────

    [Fact]
    public async Task SampleTableData_ReturnsColumnsAndRows()
    {
        _mockProvider
            .Setup(p => p.SampleTableDataAsync("Employees", null, null, null, null, default))
            .ReturnsAsync(SampleResult());

        var json = await _tools.SampleTableDataAsync("Employees");

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("columns").GetArrayLength().Should().Be(2);
        root.GetProperty("rows").GetArrayLength().Should().Be(2);
        root.GetProperty("rows")[0].GetProperty("FirstName").GetString().Should().Be("Ada");
        root.GetProperty("rowCount").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SampleTableData_PassesEveryArgumentThrough()
    {
        _mockProvider
            .Setup(p => p.SampleTableDataAsync("Employees", "hr", 10, "IsActive = 1", "LastName DESC", default))
            .ReturnsAsync(SampleResult())
            .Verifiable();

        await _tools.SampleTableDataAsync("Employees", "hr", 10, "IsActive = 1", "LastName DESC");

        _mockProvider.Verify();
    }

    [Fact]
    public async Task SampleTableData_SurfacesTruncation()
    {
        _mockProvider
            .Setup(p => p.SampleTableDataAsync("Employees", null, null, null, null, default))
            .ReturnsAsync(SampleResult(truncated: true));

        var json = await _tools.SampleTableDataAsync("Employees");

        JsonDocument.Parse(json).RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SampleTableData_UnknownTable_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.SampleTableDataAsync("Nope", null, null, null, null, default))
            .ThrowsAsync(new ArgumentException("Table or view 'dbo.Nope' was not found."));

        var json = await _tools.SampleTableDataAsync("Nope");

        JsonDocument.Parse(json).RootElement.GetProperty("error").GetString()
            .Should().Contain("was not found");
    }

    [Fact]
    public async Task SampleTableData_RejectedWhereClause_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.SampleTableDataAsync("Employees", null, null, "1=1; DROP TABLE x", null, default))
            .ThrowsAsync(new ArgumentException("The where clause may not contain ';'."));

        var json = await _tools.SampleTableDataAsync("Employees", whereClause: "1=1; DROP TABLE x");

        JsonDocument.Parse(json).RootElement.GetProperty("error").GetString()
            .Should().Contain("may not contain");
    }

    // ── execute_query ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_ReturnsRows()
    {
        _mockProvider
            .Setup(p => p.ExecuteQueryAsync("SELECT * FROM Employees", null, default))
            .ReturnsAsync(SampleResult());

        var json = await _tools.ExecuteQueryAsync("SELECT * FROM Employees");

        JsonDocument.Parse(json).RootElement.GetProperty("rows").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteQuery_ValidationFailure_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.ExecuteQueryAsync("DELETE FROM Employees", null, default))
            .ThrowsAsync(new ArgumentException("Only read-only queries are allowed — the statement must start with SELECT or WITH."));

        var json = await _tools.ExecuteQueryAsync("DELETE FROM Employees");

        JsonDocument.Parse(json).RootElement.GetProperty("error").GetString()
            .Should().Contain("read-only");
    }

    [Fact]
    public async Task ExecuteQuery_DatabaseError_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.ExecuteQueryAsync("SELECT * FROM Nope", null, default))
            .ThrowsAsync(new FakeDbException("Invalid object name 'Nope'."));

        var json = await _tools.ExecuteQueryAsync("SELECT * FROM Nope");

        JsonDocument.Parse(json).RootElement.GetProperty("error").GetString()
            .Should().Contain("Invalid object name");
    }

    // ── get_table_row_count ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTableRowCount_ReturnsCount()
    {
        _mockProvider
            .Setup(p => p.GetTableRowCountAsync("Employees", null, default))
            .ReturnsAsync(new TableRowCount("dbo", "Employees", 4200));

        var json = await _tools.GetTableRowCountAsync("Employees");

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("schemaName").GetString().Should().Be("dbo");
        root.GetProperty("rowCount").GetInt64().Should().Be(4200);
    }

    // ── get_data_read_limits ─────────────────────────────────────────────────

    [Fact]
    public void GetDataReadLimits_ReportsConfiguredLimits()
    {
        var root = JsonDocument.Parse(_tools.GetDataReadLimits()).RootElement;

        root.GetProperty("maxRows").GetInt32().Should().Be(50);
        root.GetProperty("queryTimeoutSeconds").GetInt32().Should().Be(15);
        root.GetProperty("readableSchemas").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("dbo", "hr");
    }

    private sealed class FakeDbException(string message) : DbException(message);
}
