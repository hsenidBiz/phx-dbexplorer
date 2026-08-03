using FluentAssertions;

namespace PhxDbExplorer.IntegrationTests;

[Collection("Postgres")]
public class PostgresSchemaProviderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    // ── list_tables ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTables_ReturnsTablesAndViews()
    {
        var tables = await fixture.Provider.ListTablesAsync();

        tables.Should().Contain(t => t.TableName == "employees" && t.TableType == "BASE TABLE");
        tables.Should().Contain(t => t.TableName == "departments" && t.TableType == "BASE TABLE");
        tables.Should().Contain(t => t.TableName == "vw_active_employees" && t.TableType == "VIEW");
    }

    [Fact]
    public async Task ListTables_AllResultsInConfiguredSchema()
    {
        var tables = await fixture.Provider.ListTablesAsync();

        tables.Should().AllSatisfy(t => t.SchemaName.Should().Be("public"));
    }

    // ── get_table_schema: columns ─────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_Employees_ColumnsCorrect()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.TableName.Should().Be("employees");
        schema.Columns.Should().Contain(c => c.ColumnName == "employee_id");
        schema.Columns.Should().Contain(c => c.ColumnName == "first_name");
        schema.Columns.Should().Contain(c => c.ColumnName == "email");
    }

    [Fact]
    public async Task GetTableSchema_PrimaryKeyFlagSet()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Columns.Single(c => c.ColumnName == "employee_id").IsPrimaryKey.Should().BeTrue();
        schema.Columns.Where(c => c.ColumnName != "employee_id")
            .Should().AllSatisfy(c => c.IsPrimaryKey.Should().BeFalse());
    }

    [Fact]
    public async Task GetTableSchema_IdentityColumnDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Columns.Single(c => c.ColumnName == "employee_id").IsIdentity.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableSchema_NullabilityCorrect()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Columns.Single(c => c.ColumnName == "email").IsNullable.Should().BeFalse();
        schema.Columns.Single(c => c.ColumnName == "department_id").IsNullable.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableSchema_FullDataTypeIncludesPrecision()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        var salary = schema.Columns.Single(c => c.ColumnName == "salary");
        salary.FullDataType.Should().Be("numeric(12,2)");
    }

    // ── get_table_schema: foreign keys ────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_ForeignKeyDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.ForeignKeys.Should().ContainSingle();
        var fk = schema.ForeignKeys[0];
        fk.ConstraintName.Should().Be("fk_employees_departments");
        fk.ColumnName.Should().Be("department_id");
        fk.ReferencedTable.Should().Be("departments");
        fk.ReferencedColumn.Should().Be("department_id");
        fk.DeleteAction.Should().Be("SET NULL");
    }

    [Fact]
    public async Task GetTableSchema_TableWithNoForeignKeys_EmptyForeignKeys()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("departments");

        schema.ForeignKeys.Should().BeEmpty();
    }

    // ── get_table_schema: indexes ─────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_IndexesDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Indexes.Should().Contain(i => i.IndexName == "ix_employees_last_name");
        schema.Indexes.Should().Contain(i => i.IndexName.StartsWith("employees_pkey") ||
                                             i.IndexName.Contains("pkey") ||
                                             i.IsPrimaryKey);
    }

    [Fact]
    public async Task GetTableSchema_UniqueIndexMarkedCorrectly()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Indexes.Should().Contain(i => i.IsUnique && i.IndexName.Contains("email"));
    }

    // ── get_table_schema: constraints ─────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_UniqueConstraintDetected()
    {
        var schema = await fixture.Provider.GetTableSchemaAsync("employees");

        schema.Constraints.Should().Contain(c =>
            c.ConstraintName == "uq_employees_email" && c.ConstraintType == "UNIQUE");
    }

    // ── stored procedures ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListStoredProcedures_ReturnsSeededProcedure()
    {
        var procs = await fixture.Provider.ListStoredProceduresAsync();

        procs.Should().Contain(p => p.ProcedureName == "usp_deactivate_employee");
    }

    [Fact]
    public async Task GetProcedureDefinition_DefinitionContainsSql()
    {
        var def = await fixture.Provider.GetProcedureDefinitionAsync("usp_deactivate_employee");

        def.Should().NotBeNull();
        def!.Definition.Should().Contain("UPDATE");
    }

    // ── functions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListFunctions_ReturnsSeededFunction()
    {
        var funcs = await fixture.Provider.ListFunctionsAsync();

        funcs.Should().Contain(f => f.FunctionName == "fn_get_full_name");
    }

    [Fact]
    public async Task GetFunctionDefinition_ParametersAndDefinitionReturned()
    {
        var def = await fixture.Provider.GetFunctionDefinitionAsync("fn_get_full_name");

        def.Should().NotBeNull();
        def!.Parameters.Should().HaveCount(2);
        def.Definition.Should().Contain("RETURNS");
    }

    // ── search_schema ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSchema_FindsTableByName()
    {
        var result = await fixture.Provider.SearchSchemaAsync("employ");

        result.Matches.Should().Contain(m => m.ObjectName == "employees" && m.ObjectType == "TABLE");
    }

    [Fact]
    public async Task SearchSchema_FindsColumnByName()
    {
        var result = await fixture.Provider.SearchSchemaAsync("department_id");

        result.Matches.Should().Contain(m => m.ObjectType == "COLUMN" && m.Detail == "department_id");
    }

    [Fact]
    public async Task SearchSchema_FindsFunction()
    {
        var result = await fixture.Provider.SearchSchemaAsync("fn_get");

        result.Matches.Should().Contain(m => m.ObjectName == "fn_get_full_name");
    }

    [Fact]
    public async Task SearchSchema_NoMatches_ReturnsEmptyList()
    {
        var result = await fixture.Provider.SearchSchemaAsync("xyz_no_match_ever_123");

        result.Matches.Should().BeEmpty();
    }
}
