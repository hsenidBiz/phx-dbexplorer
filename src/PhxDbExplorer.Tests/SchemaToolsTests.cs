using System.Text.Json;
using Moq;
using PhxDbExplorer.Models;
using PhxDbExplorer.Providers;
using PhxDbExplorer.Tools;
using FluentAssertions;

namespace PhxDbExplorer.Tests;

public class SchemaToolsTests
{
    private readonly Mock<ISchemaProvider> _mockProvider = new();
    private readonly SchemaTools _tools;

    public SchemaToolsTests()
    {
        _tools = new SchemaTools(_mockProvider.Object);
    }

    // ── list_tables ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTables_ReturnsJsonArray()
    {
        _mockProvider
            .Setup(p => p.ListTablesAsync(default))
            .ReturnsAsync([
                new TableInfo("dbo", "Employees", "BASE TABLE", "Stores employee records"),
                new TableInfo("dbo", "vw_ActiveEmployees", "VIEW", null)
            ]);

        var json = await _tools.ListTablesAsync();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("tableName").GetString().Should().Be("Employees");
        doc.RootElement[1].GetProperty("tableName").GetString().Should().Be("vw_ActiveEmployees");
    }

    [Fact]
    public async Task ListTables_EmptyDatabase_ReturnsEmptyArray()
    {
        _mockProvider
            .Setup(p => p.ListTablesAsync(default))
            .ReturnsAsync([]);

        var json = await _tools.ListTablesAsync();

        json.Should().Be("[]");
    }

    // ── get_table_schema ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_ReturnsFullSchemaJson()
    {
        var expected = new TableSchemaResult(
            "dbo", "Employees", "BASE TABLE",
            Columns: [
                new ColumnInfo("EmployeeId", "int", "int", false, null, true, true, "Primary key"),
                new ColumnInfo("Name", "nvarchar", "nvarchar(200)", false, null, false, false, null)
            ],
            ForeignKeys: [],
            Indexes: [new IndexInfo("PK_Employees", "CLUSTERED", true, true, ["EmployeeId"])],
            Constraints: []
        );

        _mockProvider
            .Setup(p => p.GetTableSchemaAsync("Employees", null, default))
            .ReturnsAsync(expected);

        var json = await _tools.GetTableSchemaAsync("Employees");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("tableName").GetString().Should().Be("Employees");
        doc.RootElement.GetProperty("columns").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("columns")[0].GetProperty("isPrimaryKey").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("columns")[0].GetProperty("isIdentity").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetTableSchema_WithSchemaName_PassesSchemaToProvider()
    {
        var result = new TableSchemaResult("hr", "Employees", "BASE TABLE", [], [], [], []);
        _mockProvider
            .Setup(p => p.GetTableSchemaAsync("Employees", "hr", default))
            .ReturnsAsync(result);

        var json = await _tools.GetTableSchemaAsync("Employees", "hr");

        _mockProvider.Verify(p => p.GetTableSchemaAsync("Employees", "hr", default), Times.Once);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("schemaName").GetString().Should().Be("hr");
    }

    // ── list_stored_procedures ───────────────────────────────────────────────

    [Fact]
    public async Task ListStoredProcedures_ReturnsJsonArray()
    {
        _mockProvider
            .Setup(p => p.ListStoredProceduresAsync(default))
            .ReturnsAsync([
                new ProcedureInfo("dbo", "usp_GetEmployees", DateTime.UtcNow, DateTime.UtcNow),
                new ProcedureInfo("dbo", "usp_UpdateSalary", DateTime.UtcNow, null)
            ]);

        var json = await _tools.ListStoredProceduresAsync();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("procedureName").GetString().Should().Be("usp_GetEmployees");
    }

    // ── get_procedure_definition ─────────────────────────────────────────────

    [Fact]
    public async Task GetProcedureDefinition_FoundProcedure_ReturnsDefinitionJson()
    {
        var definition = new ProcedureDefinition(
            "dbo", "usp_GetEmployees",
            "CREATE PROCEDURE dbo.usp_GetEmployees AS SELECT * FROM Employees",
            Parameters: [new ProcedureParameter("@DeptId", "int", false, false)]
        );

        _mockProvider
            .Setup(p => p.GetProcedureDefinitionAsync("usp_GetEmployees", null, default))
            .ReturnsAsync(definition);

        var json = await _tools.GetProcedureDefinitionAsync("usp_GetEmployees");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("procedureName").GetString().Should().Be("usp_GetEmployees");
        doc.RootElement.GetProperty("parameters").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("definition").GetString().Should().Contain("CREATE PROCEDURE");
    }

    [Fact]
    public async Task GetProcedureDefinition_NotFound_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.GetProcedureDefinitionAsync("usp_Missing", null, default))
            .ReturnsAsync((ProcedureDefinition?)null);

        var json = await _tools.GetProcedureDefinitionAsync("usp_Missing");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("usp_Missing");
    }

    // ── list_functions ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListFunctions_ReturnsJsonArray()
    {
        _mockProvider
            .Setup(p => p.ListFunctionsAsync(default))
            .ReturnsAsync([
                new FunctionInfo("dbo", "fn_GetFullName", "FUNCTION", "nvarchar", DateTime.UtcNow, null)
            ]);

        var json = await _tools.ListFunctionsAsync();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("functionName").GetString().Should().Be("fn_GetFullName");
    }

    // ── get_function_definition ───────────────────────────────────────────────

    [Fact]
    public async Task GetFunctionDefinition_NotFound_ReturnsErrorJson()
    {
        _mockProvider
            .Setup(p => p.GetFunctionDefinitionAsync("fn_Missing", null, default))
            .ReturnsAsync((FunctionDefinition?)null);

        var json = await _tools.GetFunctionDefinitionAsync("fn_Missing");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("fn_Missing");
    }

    [Fact]
    public async Task GetFunctionDefinition_FoundFunction_ReturnsDefinitionJson()
    {
        var def = new FunctionDefinition("dbo", "fn_GetFullName",
            "CREATE FUNCTION dbo.fn_GetFullName(@id int) RETURNS nvarchar(200) AS BEGIN RETURN '' END",
            Parameters: [new FunctionParameter("@id", "int", false)]);

        _mockProvider
            .Setup(p => p.GetFunctionDefinitionAsync("fn_GetFullName", null, default))
            .ReturnsAsync(def);

        var json = await _tools.GetFunctionDefinitionAsync("fn_GetFullName");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("functionName").GetString().Should().Be("fn_GetFullName");
        doc.RootElement.GetProperty("parameters").GetArrayLength().Should().Be(1);
    }

    // ── search_schema ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSchema_ReturnsMatchesJson()
    {
        _mockProvider
            .Setup(p => p.SearchSchemaAsync("employee", default))
            .ReturnsAsync(new SearchResult("employee", [
                new SearchMatch("TABLE", "dbo", "Employees", null),
                new SearchMatch("COLUMN", "dbo", "Salaries", "EmployeeId")
            ]));

        var json = await _tools.SearchSchemaAsync("employee");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("keyword").GetString().Should().Be("employee");
        doc.RootElement.GetProperty("matches").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("matches")[1].GetProperty("detail").GetString().Should().Be("EmployeeId");
    }

    [Fact]
    public async Task SearchSchema_NoMatches_ReturnsEmptyMatchesArray()
    {
        _mockProvider
            .Setup(p => p.SearchSchemaAsync("xyz_nothing", default))
            .ReturnsAsync(new SearchResult("xyz_nothing", []));

        var json = await _tools.SearchSchemaAsync("xyz_nothing");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("matches").GetArrayLength().Should().Be(0);
    }

    // ── null-safety ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_NullableFieldsOmittedInJson()
    {
        var result = new TableSchemaResult("dbo", "Orders", "BASE TABLE",
            [new ColumnInfo("Id", "int", "int", false, null, false, true, null)],
            [], [], []);

        _mockProvider.Setup(p => p.GetTableSchemaAsync("Orders", null, default)).ReturnsAsync(result);

        var json = await _tools.GetTableSchemaAsync("Orders");

        // Null description should be absent (WhenWritingNull policy)
        json.Should().NotContain("\"description\"");
    }
}
