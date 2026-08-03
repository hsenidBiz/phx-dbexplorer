using PhxDbExplorer.IntegrationTests;
using FluentAssertions;

namespace PhxDbExplorer.IntegrationTests;

[Collection("SqlServer")]
public class SqlServerSchemaProviderTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    // ── list_tables ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTables_ReturnsTablesAndViews()
    {
        var tables = await fixture.Provider.ListTablesAsync();

        tables.Should().Contain(t => t.TableName == "Employees" && t.TableType == "BASE TABLE");
        tables.Should().Contain(t => t.TableName == "Departments" && t.TableType == "BASE TABLE");
        tables.Should().Contain(t => t.TableName == "vw_ActiveEmployees" && t.TableType == "VIEW");
    }

    [Fact]
    public async Task ListTables_AllResultsInConfiguredSchema()
    {
        var tables = await fixture.Provider.ListTablesAsync();

        tables.Should().AllSatisfy(t => t.SchemaName.Should().Be("dbo"));
    }

    // ── get_table_schema: columns ─────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_Employees_ColumnsCorrect()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.TableName.Should().Be("Employees");
        schema.SchemaName.Should().Be("dbo");
        schema.Columns.Should().Contain(c => c.ColumnName == "EmployeeId");
        schema.Columns.Should().Contain(c => c.ColumnName == "FirstName");
        schema.Columns.Should().Contain(c => c.ColumnName == "Email");
    }

    [Fact]
    public async Task GetTableSchema_PrimaryKeyFlagSet()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Columns.Single(c => c.ColumnName == "EmployeeId").IsPrimaryKey.Should().BeTrue();
        schema.Columns.Where(c => c.ColumnName != "EmployeeId").Should().AllSatisfy(c => c.IsPrimaryKey.Should().BeFalse());
    }

    [Fact]
    public async Task GetTableSchema_IdentityColumnDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Columns.Single(c => c.ColumnName == "EmployeeId").IsIdentity.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableSchema_NullabilityCorrect()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Columns.Single(c => c.ColumnName == "Email").IsNullable.Should().BeFalse();
        schema.Columns.Single(c => c.ColumnName == "DepartmentId").IsNullable.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableSchema_FullDataTypeIncludesPrecision()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        var salary = schema.Columns.Single(c => c.ColumnName == "Salary");
        salary.FullDataType.Should().Be("decimal(12,2)");
    }

    // ── get_table_schema: foreign keys ────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_ForeignKeyDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.ForeignKeys.Should().ContainSingle();
        var fk = schema.ForeignKeys[0];
        fk.ConstraintName.Should().Be("FK_Employees_Departments");
        fk.ColumnName.Should().Be("DepartmentId");
        fk.ReferencedTable.Should().Be("Departments");
        fk.ReferencedColumn.Should().Be("DepartmentId");
        fk.DeleteAction.Should().Be("SET_NULL");
        fk.UpdateAction.Should().Be("CASCADE");
    }

    [Fact]
    public async Task GetTableSchema_TableWithNoForeignKeys_EmptyForeignKeys()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Departments");

        schema.ForeignKeys.Should().BeEmpty();
    }

    // ── get_table_schema: indexes ─────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_IndexesDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Indexes.Should().Contain(i => i.IndexName == "PK_Employees" && i.IsPrimaryKey);
        schema.Indexes.Should().Contain(i => i.IndexName == "IX_Employees_LastName");
        schema.Indexes.Should().Contain(i => i.IndexName.StartsWith("UQ_"));
    }

    [Fact]
    public async Task GetTableSchema_UniqueIndexMarkedCorrectly()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Indexes.Should().Contain(i => i.IsUnique && i.IndexName.Contains("Email"));
    }

    // ── get_table_schema: constraints ─────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_UniqueConstraintDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("Employees");

        schema.Constraints.Should().Contain(c =>
            c.ConstraintName == "UQ_Employees_Email" && c.ConstraintType == "UNIQUE");
    }

    // ── stored procedures ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListStoredProcedures_ReturnsSeededProcedure()
    {
        var procs = await fixture.Provider.ListStoredProceduresAsync();

        procs.Should().Contain(p => p.ProcedureName == "usp_GetEmployeesByDept");
    }

    [Fact]
    public async Task GetProcedureDefinition_ParametersReturned()
    {
        var def = await fixture.Provider.GetProcedureDefinitionAsync("usp_GetEmployeesByDept");

        def.Should().NotBeNull();
        def!.Parameters.Should().HaveCount(2);
        def.Parameters[0].ParameterName.Should().Be("@DepartmentId");
        def.Parameters[0].IsOutput.Should().BeFalse();
    }

    [Fact]
    public async Task GetProcedureDefinition_DefinitionContainsSql()
    {
        var def = await fixture.Provider.GetProcedureDefinitionAsync("usp_GetEmployeesByDept");

        def!.Definition.Should().Contain("SELECT");
    }

    // ── functions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListFunctions_ReturnsSeededFunction()
    {
        var funcs = await fixture.Provider.ListFunctionsAsync();

        funcs.Should().Contain(f => f.FunctionName == "fn_GetFullName");
    }

    [Fact]
    public async Task GetFunctionDefinition_ParametersAndDefinitionReturned()
    {
        var def = await fixture.Provider.GetFunctionDefinitionAsync("fn_GetFullName");

        def.Should().NotBeNull();
        def!.Parameters.Should().HaveCount(2);
        def.Definition.Should().Contain("RETURNS");
    }

    // ── search_schema ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSchema_FindsTableByName()
    {
        var result = await fixture.Provider.SearchSchemaAsync("Employ");

        result.Matches.Should().Contain(m => m.ObjectName == "Employees" && m.ObjectType == "TABLE");
    }

    [Fact]
    public async Task SearchSchema_FindsColumnByName()
    {
        var result = await fixture.Provider.SearchSchemaAsync("DepartmentId");

        result.Matches.Should().Contain(m => m.ObjectType == "COLUMN" && m.Detail == "DepartmentId");
    }

    [Fact]
    public async Task SearchSchema_FindsProcedure()
    {
        var result = await fixture.Provider.SearchSchemaAsync("usp_Get");

        result.Matches.Should().Contain(m => m.ObjectName == "usp_GetEmployeesByDept");
    }

    [Fact]
    public async Task SearchSchema_NoMatches_ReturnsEmptyList()
    {
        var result = await fixture.Provider.SearchSchemaAsync("xyz_no_match_ever_123");

        result.Matches.Should().BeEmpty();
    }
}
